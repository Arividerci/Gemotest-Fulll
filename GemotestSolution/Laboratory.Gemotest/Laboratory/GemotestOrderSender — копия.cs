using Laboratory.Gemotest.SourseClass;
using SiMed.Laboratory;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System;
using System.Security;

namespace Laboratory.Gemotest.GemotestRequests
{
    internal sealed class GemotestOrderSender
    {
        private const string SenderVersion = "GemotestOrderSender v2026-04-23_micro_fix_v2";
        private readonly string _url;
        private readonly string _contractor;
        private readonly string _salt;
        private readonly string _login;
        private readonly string _password;

        private Dictionaries _dictionaries;

        public GemotestOrderSender(string url, string contractor, string salt, string login, string password)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _contractor = contractor ?? throw new ArgumentNullException(nameof(contractor));
            _salt = salt ?? throw new ArgumentNullException(nameof(salt));
            _login = login ?? throw new ArgumentNullException(nameof(login));
            _password = password ?? throw new ArgumentNullException(nameof(password));
        }

        private sealed class SoapTopServiceItem
        {
            public string Id;
            public string BiomaterialId;
            public string LocalizationId;
            public string TransportId;
            public string SampleId;
            public string MicrobiologyBiomaterialId;
        }

        private sealed class SoapSupplementalItem
        {
            public string Id;
            public string Name;
            public string Value;
        }

        public bool CreateOrder(Order order, out string errorMessage)
        {
            errorMessage = null;

            try
            {
                if (order == null)
                    throw new ArgumentNullException(nameof(order));

                var details = order.OrderDetail as GemotestOrderDetail;
                if (details == null)
                    throw new InvalidOperationException("OrderDetail должен быть GemotestOrderDetail.");

                _dictionaries = details.Dicts;
                if (_dictionaries == null)
                    throw new InvalidOperationException("Dictionaries не назначены: перед отправкой заказа нужно установить details.Dicts.");

                if (details.Products == null || details.Products.Count == 0)
                    throw new InvalidOperationException("В заказе нет ни одной услуги.");

                var patient = order.Patient ?? new Patient();

                string extNum = string.IsNullOrWhiteSpace(order.Number)
                    ? "SiMed_" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
                    : order.Number;

                string orderNum = "";

                DateTime birthDate = patient.Birthday == default(DateTime) ? DateTime.Today : patient.Birthday;

                string createHash = BuildCreateOrderHash(
                    extNum,
                    orderNum,
                    _contractor,
                    patient.Surname ?? "",
                    birthDate,
                    _salt);

                DumpUserSelection(details);

                var rows = BuildSampleServiceRows(details);

                if (rows == null || rows.Count == 0)
                    throw new InvalidOperationException("Не удалось определить пробы для выбранных услуг (rows=0).");

                DumpRows(rows);

                var tubes = GemotestSamplePacker.Pack(rows);
                NormalizeTubeServices(tubes);
                if (tubes == null || tubes.Count == 0)
                    throw new InvalidOperationException("Упаковка не дала ни одной пробирки (tubes=0).");

                DumpPacking(tubes);

                long rangeStart;
                long rangeEnd;
                GetSampleIdentifiersRange(tubes.Count, out rangeStart, out rangeEnd);

                long available = (rangeEnd - rangeStart) + 1;
                if (available < tubes.Count)
                    throw new InvalidOperationException("get_sample_identifiers вернул недостаточно идентификаторов.");

                AssignIdentifiers(tubes, rangeStart);
                FillDetailsSamplesFromTubes(details, tubes);

                var topServices = BuildTopLevelServices(details, tubes);
                var supplementals = BuildServiceSupplementals(details);

                DumpSupplementals(supplementals);
                string doctor = "";

                if (order != null && order.Worker != null)
                {
                    doctor = ((order.Worker.Surname ?? "") + " " + (order.Worker.Name ?? "")).Trim();
                }
                if (order != null && order.Author != null)
                {
                    doctor = ((order.Author.Surname ?? "") + " " + (order.Author.Name ?? "")).Trim();
                }

                string xml = BuildCreateOrderEnvelopeVariantA(
                    extNum,
                    orderNum,
                    _contractor,
                    createHash,
                    doctor,
                    "",
                    patient,
                    details,
                    topServices,
                    tubes,
                    supplementals);

                string responseXml = SendSoapRequest("create_order", xml);

                var doc = new XmlDocument();
                doc.LoadXml(responseXml);

                string status = GetXmlNodeValue(doc, "status");
                if (!string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase))
                {
                    string errorText = GetErrorDescription(doc);
                    if (string.IsNullOrWhiteSpace(errorText))
                        errorText = "Неизвестная ошибка create_order.";

                    throw new Exception(errorText);
                }
                WriteReturnedBarcodesToDetails(details, doc);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private void NormalizeTubeServices(IList<TubePlan> tubes)
        {
            if (tubes == null) return;

            for (int i = 0; i < tubes.Count; i++)
            {
                var t = tubes[i];
                if (t == null || t.Services == null) continue;

                if (t.Parent != null)
                {
                    t.Services.Clear();
                    continue;
                }

                var groups = new Dictionary<string, List<TubeServicePlan>>();

                for (int j = 0; j < t.Services.Count; j++)
                {
                    var s = t.Services[j];
                    if (s == null) continue;

                    string key = (s.ServiceId ?? "") + "|" + (s.ComplexId ?? "");

                    List<TubeServicePlan> list;
                    if (!groups.TryGetValue(key, out list))
                    {
                        list = new List<TubeServicePlan>();
                        groups[key] = list;
                    }

                    list.Add(s);
                }

                var normalized = new List<TubeServicePlan>();

                foreach (var pair in groups)
                {
                    var group = pair.Value;
                    if (group == null || group.Count == 0) continue;

                    TubeServicePlan selected = group[0];

                    for (int j = 0; j < group.Count; j++)
                    {
                        if (group[j].RefuseFlag == 0)
                        {
                            selected = group[j];
                            break;
                        }
                    }

                    int utilizationFlag = 0;
                    for (int j = 0; j < group.Count; j++)
                    {
                        if (group[j].UtilizationFlag == 1)
                        {
                            utilizationFlag = 1;
                            break;
                        }
                    }

                    int sc = selected.ServiceCount <= 0 ? 1 : selected.ServiceCount;

                    normalized.Add(new TubeServicePlan
                    {
                        ServiceId = selected.ServiceId ?? "",
                        ComplexId = selected.ComplexId ?? "",
                        UtilizationFlag = utilizationFlag,
                        RefuseFlag = selected.RefuseFlag,
                        ServiceCount = sc,
                        SharePercent = 100.0 / sc
                    });
                }

                t.Services.Clear();
                t.Services.AddRange(normalized);
            }
        }

        private const string SupplementalInstanceSeparator = "__FOR__";

        private static string GetSupplementalBaseIdFromDetailCode(string code)
        {
            code = (code ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;

            int pos = code.IndexOf(SupplementalInstanceSeparator, StringComparison.Ordinal);

            if (pos < 0)
                return code;

            return code.Substring(0, pos).Trim();
        }

        private static string GetSupplementalSoapId(GemotestDetail detail)
        {
            if (detail == null)
                return string.Empty;

            string code = !string.IsNullOrWhiteSpace(detail.SoapCode)
                ? detail.SoapCode
                : detail.Code;

            return GetSupplementalBaseIdFromDetailCode(code);
        }

        private void WriteReturnedBarcodesToDetails(GemotestOrderDetail details, XmlDocument doc)
        {
            if (details == null || details.Samples == null || doc == null)
                return;

            var barcodeNodes = doc.SelectNodes("//*[local-name()='barcodes']/*[local-name()='item']");
            if (barcodeNodes == null || barcodeNodes.Count == 0)
                return;

            int index = 0;

            foreach (XmlNode node in barcodeNodes)
            {
                if (node == null)
                    continue;

                string barcode = GetNodeValue(node, "barcode");
                string sampleIdentifier = GetNodeValue(node, "sample_identifier");
                string sampleId = GetNodeValue(node, "sample_id");

                string sampleDescription = GetNodeValue(node, "sample_description");

                string biomaterialId = GetNodeValue(node, "biomaterial_id");
                string biomaterialName = GetNodeValue(node, "biomaterial_name");

                string localizationId = GetNodeValue(node, "localization_id");
                string localizationName = GetNodeValue(node, "localization_name");

                string transportId = GetNodeValue(node, "transport_id");
                string transportName = GetNodeValue(node, "transport_name");

                string labCenterId = GetNodeValue(node, "id_lab_center");

                GemotestSampleDetail target = null;

                if (!string.IsNullOrWhiteSpace(sampleIdentifier))
                {
                    target = details.Samples.FirstOrDefault(x =>
                        x != null &&
                        string.Equals(x.SampleIdentifier ?? "", sampleIdentifier, StringComparison.OrdinalIgnoreCase));
                }

                if (target == null && !string.IsNullOrWhiteSpace(sampleId))
                {
                    target = details.Samples.FirstOrDefault(x =>
                        x != null &&
                        string.Equals(x.SampleId ?? "", sampleId, StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrWhiteSpace(x.Barcode));
                }

                if (target == null && index < details.Samples.Count)
                {
                    target = details.Samples[index];
                    index++;
                }

                if (target == null)
                    continue;

                target.Barcode = barcode ?? "";

                if (!string.IsNullOrWhiteSpace(sampleIdentifier))
                    target.SampleIdentifier = sampleIdentifier;

                if (!string.IsNullOrWhiteSpace(sampleId))
                    target.SampleId = sampleId;

                if (!string.IsNullOrWhiteSpace(sampleDescription))
                    target.SampleDescription = sampleDescription;

                if (!string.IsNullOrWhiteSpace(biomaterialId))
                {
                    target.BiomId = biomaterialId;
                    target.BiomCode = biomaterialId;
                }

                if (!string.IsNullOrWhiteSpace(biomaterialName))
                    target.BiomName = biomaterialName;

                if (!string.IsNullOrWhiteSpace(localizationId))
                    target.LocalizationId = localizationId;

                if (!string.IsNullOrWhiteSpace(localizationName))
                    target.LocalizationName = localizationName;

                if (!string.IsNullOrWhiteSpace(transportId))
                {
                    target.TransportId = transportId;
                    target.ContId = transportId;
                    target.ContCode = transportId;
                }

                if (!string.IsNullOrWhiteSpace(transportName))
                {
                    target.TransportName = transportName;
                    target.ContName = transportName;
                }

                if (!string.IsNullOrWhiteSpace(labCenterId))
                    target.LabCenterId = labCenterId;
            }
        }

        private static string GetNodeValue(XmlNode node, string localName)
        {
            if (node == null || string.IsNullOrWhiteSpace(localName))
                return "";

            var child = node.SelectSingleNode("./*[local-name()='" + localName + "']");
            return child != null ? (child.InnerText ?? "").Trim() : "";
        }
        private static int ToInt(object value, int defaultValue)
        {
            if (value == null) return defaultValue;

            if (value is int) return (int)value;
            if (value is long) return (int)(long)value;
            if (value is short) return (short)value;
            if (value is byte) return (byte)value;

            var s = value as string;
            if (s != null)
            {
                int r;
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out r))
                    return r;
            }

            return defaultValue;
        }

        private static int ToInt(object value)
        {
            return ToInt(value, 0);
        }

        private static int? ToNullableInt(object value)
        {
            if (value == null) return null;

            if (value is int) return (int)value;
            if (value is long) return (int)(long)value;

            var s = value as string;
            if (s != null)
            {
                int r;
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out r))
                    return r;
            }

            return null;
        }

        private static string ToStr(object value)
        {
            return value == null ? "" : value.ToString();
        }

        private static int MapGender(object sexEnum)
        {
            string s = sexEnum == null ? "" : sexEnum.ToString();
            s = (s ?? "").ToLowerInvariant();

            if (s.Contains("female") || s.Contains("жен")) return 2;
            if (s.Contains("male") || s.Contains("муж")) return 1;
            return 0;
        }

        private void AssignIdentifiers(List<TubePlan> tubes, long rangeStart)
        {
            long cur = rangeStart;

            for (int i = 0; i < tubes.Count; i++)
            {
                tubes[i].SampleIdentifier = cur.ToString(CultureInfo.InvariantCulture);
                cur++;
            }

            for (int i = 0; i < tubes.Count; i++)
            {
                if (tubes[i].Parent != null)
                    tubes[i].PrimarySampleIdentifier = tubes[i].Parent.SampleIdentifier ?? "";
                else
                    tubes[i].PrimarySampleIdentifier = "";
            }
        }

        private void FillDetailsSamplesFromTubes(GemotestOrderDetail details, List<TubePlan> tubes)
        {
            if (details == null)
                return;

            if (details.Samples == null)
                details.Samples = new List<GemotestSampleDetail>();
            else
                details.Samples.Clear();

            if (tubes == null)
                return;

            for (int i = 0; i < tubes.Count; i++)
            {
                var t = tubes[i];
                if (t == null)
                    continue;

                var sample = new GemotestSampleDetail
                {
                    OrderSampleGuid = Guid.NewGuid().ToString(),
                    Barcode = "",
                    SampleIdentifier = t.SampleIdentifier ?? "",
                    SampleId = t.SampleId.ToString(CultureInfo.InvariantCulture),

                    BiomId = t.BiomaterialId ?? "",
                    BiomCode = t.BiomaterialId ?? "",
                    BiomName = "",

                    ContId = t.TransportId ?? "",
                    ContCode = t.TransportId ?? "",
                    ContName = "",

                    OrderProductGuidList = new List<string>()
                };

                if (t.Services != null)
                {
                    for (int k = 0; k < t.Services.Count; k++)
                    {
                        var s = t.Services[k];
                        if (s == null || string.IsNullOrWhiteSpace(s.ServiceId))
                            continue;

                        var prod = details.Products.FirstOrDefault(p =>
                            p != null &&
                            string.Equals(p.ProductId ?? "", s.ServiceId ?? "", StringComparison.OrdinalIgnoreCase));

                        if (prod != null && !string.IsNullOrWhiteSpace(prod.OrderProductGuid))
                            sample.OrderProductGuidList.Add(prod.OrderProductGuid);
                    }
                }

                details.Samples.Add(sample);
            }
        }

        private List<SoapTopServiceItem> BuildTopLevelServices(GemotestOrderDetail details, List<TubePlan> tubes)
        {
            var res = new List<SoapTopServiceItem>();

            if (details == null || details.Products == null)
                return res;

            Dictionary<int, string> chosenBioByProductIndex = BuildChosenBiomaterialByProductIndex(details);

            for (int i = 0; i < details.Products.Count; i++)
            {
                var prod = details.Products[i];
                if (prod == null || string.IsNullOrEmpty(prod.ProductId))
                    continue;

                DictionaryService svc;
                if (!_dictionaries.Directory.TryGetValue(prod.ProductId, out svc) || svc == null)
                    continue;

                int? serviceType = svc.service_type;
                if (serviceType == 3 || serviceType == 4)
                    continue;

                string biomaterialId = "";
                string localizationId = "";
                string transportId = "";
                string sampleId = "";
                string microbiologyBiomaterialId = "";


                if (svc.type == 2)
                {
                    TubePlan microTube = null;

                    string chosenBioId = chosenBioByProductIndex.ContainsKey(i)
                        ? (chosenBioByProductIndex[i] ?? "")
                        : "";

                    if (tubes != null)
                    {
                        // 1. Сначала ищем пробу именно по выбранному пользователем биоматериалу.
                        // Для твоего случая это Mocha, а не Drugoe.
                        if (!string.IsNullOrWhiteSpace(chosenBioId))
                        {
                            microTube = tubes.FirstOrDefault(t =>
                                t != null &&
                                t.Services != null &&
                                t.Services.Any(s =>
                                    s != null &&
                                    (
                                        string.Equals(s.ServiceId ?? "", prod.ProductId ?? "", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(s.ComplexId ?? "", prod.ProductId ?? "", StringComparison.OrdinalIgnoreCase)
                                    )) &&
                                string.Equals(t.MicroBioBiomaterialId ?? "", chosenBioId, StringComparison.OrdinalIgnoreCase));
                        }

                        // 2. Если по выбранному биоматериалу не нашли, берем любую НЕ Drugoe.
                        if (microTube == null)
                        {
                            microTube = tubes.FirstOrDefault(t =>
                                t != null &&
                                t.Services != null &&
                                t.Services.Any(s =>
                                    s != null &&
                                    (
                                        string.Equals(s.ServiceId ?? "", prod.ProductId ?? "", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(s.ComplexId ?? "", prod.ProductId ?? "", StringComparison.OrdinalIgnoreCase)
                                    )) &&
                                !string.IsNullOrWhiteSpace(t.MicroBioBiomaterialId) &&
                                !string.Equals(t.MicroBioBiomaterialId ?? "", "Drugoe", StringComparison.OrdinalIgnoreCase));
                        }

                        // 3. Последний fallback — любая микробиологическая проба.
                        if (microTube == null)
                        {
                            microTube = tubes.FirstOrDefault(t =>
                                t != null &&
                                t.Services != null &&
                                t.Services.Any(s =>
                                    s != null &&
                                    (
                                        string.Equals(s.ServiceId ?? "", prod.ProductId ?? "", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(s.ComplexId ?? "", prod.ProductId ?? "", StringComparison.OrdinalIgnoreCase)
                                    )) &&
                                !string.IsNullOrWhiteSpace(t.MicroBioBiomaterialId));
                        }
                    }

                    if (microTube != null)
                    {
                        biomaterialId = microTube.MicroBioBiomaterialId ?? "";
                        localizationId = microTube.LocalizationId ?? "";
                        transportId = microTube.TransportId ?? "";
                        sampleId = microTube.SampleId > 0
                            ? microTube.SampleId.ToString(CultureInfo.InvariantCulture)
                            : "";
                        microbiologyBiomaterialId = microTube.MicroBioBiomaterialId ?? "";
                    }
                }

                res.Add(new SoapTopServiceItem
                {
                    Id = prod.ProductId,
                    BiomaterialId = biomaterialId,
                    LocalizationId = localizationId,
                    TransportId = transportId,
                    SampleId = sampleId,
                    MicrobiologyBiomaterialId = microbiologyBiomaterialId
                });
            }
            return res;
        }

        private Dictionary<int, string> BuildChosenBiomaterialByProductIndex(GemotestOrderDetail details)
        {
            var map = new Dictionary<int, string>();

            for (int b = 0; b < details.BioMaterials.Count; b++)
            {
                var bio = details.BioMaterials[b];
                if (bio == null) continue;

                for (int i = 0; i < bio.Mandatory.Count; i++)
                {
                    int idx = ToInt(bio.Mandatory[i], -1);
                    if (idx < 0) continue;
                    map[idx] = bio.Id ?? "";
                }
            }

            for (int b = 0; b < details.BioMaterials.Count; b++)
            {
                var bio = details.BioMaterials[b];
                if (bio == null) continue;

                for (int i = 0; i < bio.Chosen.Count; i++)
                {
                    int idx = ToInt(bio.Chosen[i], -1);
                    if (idx < 0) continue;

                    if (!map.ContainsKey(idx))
                        map[idx] = bio.Id ?? "";
                }
            }

            return map;
        }

        private List<SampleServiceRow> BuildSampleServiceRows(GemotestOrderDetail details)
        {
            if (_dictionaries == null)
                throw new InvalidOperationException("Dictionaries не инициализированы в GemotestOrderSender.");

            var rows = new List<SampleServiceRow>();
            var chosenBio = BuildChosenBiomaterialByProductIndex(details);

            for (int i = 0; i < details.Products.Count; i++)
            {
                var prod = details.Products[i];
                if (prod == null || string.IsNullOrEmpty(prod.ProductId)) continue;

                DictionaryService svc;
                if (!_dictionaries.Directory.TryGetValue(prod.ProductId, out svc) || svc == null)
                    continue;

                int? serviceType = svc.service_type;
                if (serviceType == 3 || serviceType == 4)
                    continue;

                string biomaterialId = chosenBio.ContainsKey(i) ? chosenBio[i] : "";

                if (svc.type == 2)
                {
                    Console.WriteLine("### MICRO SERVICE BEFORE ADDROWS ### productId=" + prod.ProductId + " biomaterialId=" + biomaterialId);

                    AddRowsForMicrobiologyComplex(prod.ProductId, biomaterialId, rows);

                    Console.WriteLine("### MICRO SERVICE AFTER ADDROWS ### rows.Count=" + rows.Count);

                    for (int r = 0; r < rows.Count; r++)
                    {
                        var row = rows[r];
                        if (row == null)
                            continue;

                        if (string.Equals(row.ServiceId ?? "", prod.ProductId ?? "", StringComparison.OrdinalIgnoreCase) &&
                            string.IsNullOrWhiteSpace(row.ComplexId))
                        {
                            row.ComplexId = prod.ProductId ?? "";
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(biomaterialId))
                    {
                        for (int r = rows.Count - 1; r >= 0; r--)
                        {
                            var row = rows[r];
                            if (row == null)
                            {
                                rows.RemoveAt(r);
                                continue;
                            }

                            if (string.Equals(row.ServiceId ?? "", prod.ProductId ?? "", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(row.MicroBioBiomaterialId ?? "", biomaterialId, StringComparison.OrdinalIgnoreCase))
                            {
                                Console.WriteLine("### REMOVE WRONG MICRO ROW ### svc=" + row.ServiceId +
                                                  " MB=" + row.MicroBioBiomaterialId +
                                                  " loc=" + row.LocalizationId);

                                rows.RemoveAt(r);
                            }
                        }

                        for (int r = rows.Count - 1; r >= 0; r--)
                        {
                            var row = rows[r];
                            if (row == null)
                                continue;

                            if (string.Equals(row.ServiceId ?? "", prod.ProductId ?? "", StringComparison.OrdinalIgnoreCase) &&
                                row.ExecutionSampleId == 53)
                            {
                                Console.WriteLine("### REMOVE EXTRA MICRO SAMPLE 53 ### svc=" + row.ServiceId +
                                                  " MB=" + row.MicroBioBiomaterialId +
                                                  " loc=" + row.LocalizationId);

                                rows.RemoveAt(r);
                            }
                        }

                    }

                    string selectedLocalizationId = "";

                    var firstRowForService = rows.FirstOrDefault(x =>
                        x != null &&
                        string.Equals(x.ServiceId ?? "", prod.ProductId ?? "", StringComparison.OrdinalIgnoreCase));

                    if (firstRowForService != null)
                        selectedLocalizationId = firstRowForService.LocalizationId ?? "";

                    if (!string.IsNullOrWhiteSpace(selectedLocalizationId))
                    {
                        for (int r = rows.Count - 1; r >= 0; r--)
                        {
                            var row = rows[r];
                            if (row == null)
                                continue;

                            if (string.Equals(row.ServiceId ?? "", prod.ProductId ?? "", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(row.LocalizationId ?? "", selectedLocalizationId, StringComparison.OrdinalIgnoreCase))
                            {
                                Console.WriteLine("### REMOVE EXTRA MICRO LOCALIZATION ### svc=" + row.ServiceId +
                                                  " keepLoc=" + selectedLocalizationId +
                                                  " removeLoc=" + row.LocalizationId +
                                                  " sample=" + row.ExecutionSampleId);

                                rows.RemoveAt(r);
                            }
                        }
                    }

                    Console.WriteLine("### MICRO SERVICE AFTER FILTER ### rows.Count=" + rows.Count);

                    continue;
                }

                if (serviceType == 2)
                {
                    int before = rows.Count;

                    AddRowsForMarketingComplex(prod.ProductId, biomaterialId, rows);

                    if (!string.IsNullOrWhiteSpace(biomaterialId))
                    {
                        for (int r = rows.Count - 1; r >= before; r--)
                        {
                            var row = rows[r];
                            if (row == null)
                            {
                                rows.RemoveAt(r);
                                continue;
                            }

                            if (string.Equals(row.ComplexId ?? "", prod.ProductId ?? "", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(row.MicroBioBiomaterialId ?? "", biomaterialId, StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(row.BiomaterialId ?? "", biomaterialId, StringComparison.OrdinalIgnoreCase))
                            {
                                Console.WriteLine("### REMOVE WRONG MK ROW ### complex=" + row.ComplexId +
                                                  " svc=" + row.ServiceId +
                                                  " bio=" + row.BiomaterialId +
                                                  " MB=" + row.MicroBioBiomaterialId +
                                                  " chosen=" + biomaterialId +
                                                  " loc=" + row.LocalizationId);

                                rows.RemoveAt(r);
                            }
                        }
                    }

                    continue;
                }

                AddRowsForSimpleService(prod.ProductId, biomaterialId, rows, "", "");
            }

            return rows;
        }

        private void AddRowsForMicrobiologyComplex(string complexId, string chosenBioId, List<SampleServiceRow> rows)
        {
            Console.WriteLine("### ENTER AddRowsForMicrobiologyComplex ### complexId=" + complexId + " chosenBioId=" + chosenBioId);
            if (string.IsNullOrEmpty(complexId))
                return;

            if (rows == null)
                return;

            chosenBioId = (chosenBioId ?? "").Trim();

            List<DictionaryMarketingComplex> comp;
            if (_dictionaries.MarketingComplexByComplexId == null ||
                !_dictionaries.MarketingComplexByComplexId.TryGetValue(complexId, out comp) ||
                comp == null || comp.Count == 0)
            {
                AddRowsForSimpleService(complexId, chosenBioId, rows, "", "");
                return;
            }

            int startIndex = rows.Count;

            List<DictionaryMarketingComplex> selectedRows = FilterMicrobiologyComplexRows(comp, chosenBioId);

            if (selectedRows.Count == 0 && string.IsNullOrWhiteSpace(chosenBioId))
                selectedRows = new List<DictionaryMarketingComplex>(comp);

            if (selectedRows.Count == 0)
                return;

            for (int i = 0; i < selectedRows.Count; i++)
            {
                DictionaryMarketingComplex c = selectedRows[i];
                if (c == null)
                    continue;

                string actualServiceId = c.service_id ?? "";
                if (string.IsNullOrEmpty(actualServiceId))
                    actualServiceId = complexId;

                List<DictionarySamplesServices> matchedRows = GetSampleRowsForMicrobiologyService(actualServiceId, c);
                if (matchedRows.Count == 0)
                    continue;

                AppendMicrobiologySampleRows(complexId, chosenBioId, c, matchedRows, rows);
            }

            // Жёсткая защита:
            // если пользователь выбрал Mocha, то строки Drugoe должны быть удалены
            // даже если они попали через Samples_services.
            if (!string.IsNullOrWhiteSpace(chosenBioId))
            {
                for (int i = rows.Count - 1; i >= startIndex; i--)
                {
                    var r = rows[i];
                    if (r == null)
                    {
                        rows.RemoveAt(i);
                        continue;
                    }

                    if (!string.Equals(r.MicroBioBiomaterialId ?? "", chosenBioId, StringComparison.OrdinalIgnoreCase))
                    {
                        rows.RemoveAt(i);
                    }
                }
            }

            Console.WriteLine(
                "[AddRowsForMicrobiologyComplex] complex=" + complexId +
                " chosenBioId=" + chosenBioId +
                " addedRows=" + (rows.Count - startIndex));
        }

        private DictionaryMarketingComplex ResolveMicrobiologyMainRow(string complexId, string chosenBioId)
        {
            if (string.IsNullOrEmpty(complexId) || _dictionaries.MarketingComplexByComplexId == null)
                return null;

            List<DictionaryMarketingComplex> comp;
            if (!_dictionaries.MarketingComplexByComplexId.TryGetValue(complexId, out comp) || comp == null || comp.Count == 0)
                return null;

            List<DictionaryMarketingComplex> filtered = FilterMicrobiologyComplexRows(comp, chosenBioId);
            List<DictionaryMarketingComplex> pool = filtered.Count > 0 ? filtered : comp;

            for (int i = 0; i < pool.Count; i++)
            {
                DictionaryMarketingComplex row = pool[i];
                if (row == null) continue;

                if (string.IsNullOrEmpty(row.main_service) || string.Equals(row.service_id ?? "", complexId, StringComparison.OrdinalIgnoreCase))
                    return row;
            }

            return pool[0];
        }

        private List<DictionaryMarketingComplex> FilterMicrobiologyComplexRows(List<DictionaryMarketingComplex> comp, string chosenBioId)
        {
            var result = new List<DictionaryMarketingComplex>();
            if (comp == null || comp.Count == 0)
                return result;

            bool useBio = !string.IsNullOrEmpty(chosenBioId);

            for (int i = 0; i < comp.Count; i++)
            {
                DictionaryMarketingComplex row = comp[i];
                if (row == null)
                    continue;

                if (useBio)
                {
                    string rowBio = row.biomaterial_id ?? "";

                    if (!string.Equals(rowBio, chosenBioId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (string.Equals(rowBio, "Drugoe", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(chosenBioId, "Drugoe", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                bool duplicate = false;
                for (int j = 0; j < result.Count; j++)
                {
                    DictionaryMarketingComplex existing = result[j];
                    if (existing == null) continue;

                    if (string.Equals(existing.service_id ?? "", row.service_id ?? "", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.localization_id ?? "", row.localization_id ?? "", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.biomaterial_id ?? "", row.biomaterial_id ?? "", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.transport_id ?? "", row.transport_id ?? "", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.main_service ?? "", row.main_service ?? "", StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                    result.Add(row);
            }

            return result;
        }

        private List<DictionarySamplesServices> GetSampleRowsForMicrobiologyService(string serviceId, DictionaryMarketingComplex row)
        {
            var result = new List<DictionarySamplesServices>();
            if (string.IsNullOrEmpty(serviceId))
                return result;

            List<DictionarySamplesServices> baseList;
            if (_dictionaries.SamplesServices == null ||
                !_dictionaries.SamplesServices.TryGetValue(serviceId, out baseList) ||
                baseList == null || baseList.Count == 0)
            {
                return result;
            }

            for (int stage = 0; stage < 4; stage++)
            {
                result.Clear();

                for (int i = 0; i < baseList.Count; i++)
                {
                    DictionarySamplesServices p = baseList[i];
                    if (p == null || p.sample_id <= 0)
                        continue;

                    bool needBio = stage <= 2 && !string.IsNullOrEmpty(row.biomaterial_id);
                    bool needLoc = stage <= 1 && !string.IsNullOrEmpty(row.localization_id);
                    bool needTransport = stage == 0 && !string.IsNullOrEmpty(row.transport_id);

                    if (needBio)
                    {
                        bool bioMatch = string.Equals(p.biomaterial_id ?? "", row.biomaterial_id ?? "", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(p.microbiology_biomaterial_id ?? "", row.biomaterial_id ?? "", StringComparison.OrdinalIgnoreCase);
                        if (!bioMatch)
                            continue;
                    }

                    if (needLoc && !string.Equals(p.localization_id ?? "", row.localization_id ?? "", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (needTransport)
                    {
                        DictionarySamples sample;
                        _dictionaries.Samples.TryGetValue(p.sample_id.ToString(CultureInfo.InvariantCulture), out sample);
                        string transportId = sample != null ? (sample.transport_id ?? "") : "";
                        if (!string.Equals(transportId, row.transport_id ?? "", StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    result.Add(p);
                }

                if (result.Count > 0)
                    return new List<DictionarySamplesServices>(result);
            }

            return result;
        }

        private void AppendMicrobiologySampleRows(string complexId, string chosenBioId, DictionaryMarketingComplex row, List<DictionarySamplesServices> list, List<SampleServiceRow> rows)
        {
            string microBioId = !string.IsNullOrEmpty(chosenBioId) ? chosenBioId : (row.biomaterial_id ?? "");

            for (int i = 0; i < list.Count; i++)
            {
                DictionarySamplesServices p = list[i];
                if (p == null || p.sample_id <= 0)
                    continue;

                if (IsStandalonePrimaryRow(p, list))
                    continue;

                int execSampleId = ToInt(p.sample_id, 0);
                if (execSampleId <= 0)
                    continue;

                int serviceCount = ToInt(p.service_count, 1);
                int primaryRaw = ToInt(p.primary_sample_id, 0);
                int? primarySampleId = primaryRaw > 0 ? (int?)primaryRaw : null;

                DictionarySamples execSample;
                _dictionaries.Samples.TryGetValue(execSampleId.ToString(CultureInfo.InvariantCulture), out execSample);

                string execName = execSample != null ? (execSample.name ?? "") : "";
                string execTransport = !string.IsNullOrEmpty(row.transport_id) ? (row.transport_id ?? "") : (execSample != null ? (execSample.transport_id ?? "") : "");
                bool execUtilize = execSample != null && execSample.utilize;

                string primName = "";
                string primTransport = "";
                bool primUtilize = false;

                if (primarySampleId.HasValue)
                {
                    DictionarySamples primSample;
                    _dictionaries.Samples.TryGetValue(primarySampleId.Value.ToString(CultureInfo.InvariantCulture), out primSample);
                    primName = primSample != null ? (primSample.name ?? "") : "";
                    primTransport = primSample != null ? (primSample.transport_id ?? "") : "";
                    primUtilize = primSample != null && primSample.utilize;
                }

                rows.Add(new SampleServiceRow
                {
                    ServiceId = p.service_id ?? "",
                    ComplexId = !string.IsNullOrWhiteSpace(row.complex_id) ? row.complex_id : (complexId ?? ""),

                    ExecutionSampleId = execSampleId,
                    ExecutionSampleName = execName,
                    ExecutionTransportId = execTransport,
                    ExecutionUtilize = execUtilize,
                    PrimarySampleId = primarySampleId,
                    PrimarySampleName = primName,
                    PrimaryTransportId = primTransport,
                    PrimaryUtilize = primUtilize,
                    BiomaterialId = microBioId,
                    MicroBioBiomaterialId = microBioId,
                    LocalizationId = !string.IsNullOrEmpty(row.localization_id) ? (row.localization_id ?? "") : (p.localization_id ?? ""),
                    ServiceCount = serviceCount <= 0 ? 1 : serviceCount
                });
            }
        }

        private void AddRowsForMarketingComplex(string complexId, string chosenBioId, List<SampleServiceRow> rows)
        {
            if (string.IsNullOrEmpty(complexId))
                return;

            List<DictionaryMarketingComplex> comp;
            if (_dictionaries.MarketingComplexByComplexId == null ||
                !_dictionaries.MarketingComplexByComplexId.TryGetValue(complexId, out comp) ||
                comp == null || comp.Count == 0)
            {
                return;
            }

            chosenBioId = (chosenBioId ?? "").Trim();

            for (int i = 0; i < comp.Count; i++)
            {
                var c = comp[i];
                if (c == null)
                    continue;

                if (string.IsNullOrEmpty(c.service_id))
                    continue;

                if (!string.IsNullOrWhiteSpace(chosenBioId) &&
                    !string.Equals(c.biomaterial_id ?? "", chosenBioId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string bio = c.biomaterial_id ?? "";
                string loc = c.localization_id ?? "";

                AddRowsForSimpleService(c.service_id, bio, rows, complexId, loc);
            }
        }
        private void AddRowsForSimpleService(
    string serviceId,
    string biomaterialId,
    List<SampleServiceRow> rows,
    string complexId,
    string forcedLocalizationId)
        {
            if (string.IsNullOrEmpty(serviceId))
                return;

            List<DictionarySamplesServices> baseList;
            if (_dictionaries.SamplesServices == null ||
                !_dictionaries.SamplesServices.TryGetValue(serviceId, out baseList) ||
                baseList == null)
            {
                baseList = new List<DictionarySamplesServices>();
            }

            var list = baseList.ToList();

            if (!string.IsNullOrEmpty(forcedLocalizationId))
            {
                list = list.Where(p =>
                    p != null &&
                    string.Equals(p.localization_id ?? "", forcedLocalizationId, StringComparison.OrdinalIgnoreCase)
                ).ToList();

                if (list.Count == 0)
                    return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == null)
                    continue;

                int execSampleId = ToInt(p.sample_id, 0);
                if (execSampleId <= 0)
                    continue;

                int serviceCount = ToInt(p.service_count, 1);

                int primaryRaw = ToInt(p.primary_sample_id, 0);
                int? primarySampleId = primaryRaw > 0 ? (int?)primaryRaw : null;

                DictionarySamples execSample;
                _dictionaries.Samples.TryGetValue(execSampleId.ToString(CultureInfo.InvariantCulture), out execSample);

                string execName = execSample != null ? (execSample.name ?? "") : "";
                string execTransport = execSample != null ? (execSample.transport_id ?? "") : "";
                bool execUtilize = execSample != null && execSample.utilize;

                string primName = "";
                string primTransport = "";
                bool primUtilize = false;

                if (primarySampleId.HasValue)
                {
                    DictionarySamples primSample;
                    _dictionaries.Samples.TryGetValue(primarySampleId.Value.ToString(CultureInfo.InvariantCulture), out primSample);

                    primName = primSample != null ? (primSample.name ?? "") : "";
                    primTransport = primSample != null ? (primSample.transport_id ?? "") : "";
                    primUtilize = primSample != null && primSample.utilize;
                }

                rows.Add(new SampleServiceRow
                {
                    ServiceId = serviceId ?? "",
                    ComplexId = complexId ?? "",

                    ExecutionSampleId = execSampleId,
                    ExecutionSampleName = execName,
                    ExecutionTransportId = execTransport,
                    ExecutionUtilize = execUtilize,

                    PrimarySampleId = primarySampleId,
                    PrimarySampleName = primName,
                    PrimaryTransportId = primTransport,
                    PrimaryUtilize = primUtilize,

                    BiomaterialId = p.biomaterial_id ?? "",
                    MicroBioBiomaterialId = p.microbiology_biomaterial_id ?? "",
                    LocalizationId = p.localization_id ?? "",

                    ServiceCount = serviceCount <= 0 ? 1 : serviceCount
                });
            }
        }

        private void GetSampleIdentifiersRange(int count, out long rangeStart, out long rangeEnd)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));

            string hash = BuildContractorHash(_contractor, _salt);

            string xml = BuildGetSampleIdentifiersEnvelope(count, _contractor, hash);
            string resp = SendSoapRequest("get_sample_identifiers", xml);

            bool accepted;
            string errorText;
            ParseGetSampleIdentifiersResponse(resp, out accepted, out rangeStart, out rangeEnd, out errorText);

            if (!accepted)
                throw new Exception("get_sample_identifiers отклонён: " + (errorText ?? ""));
        }

        private string BuildGetSampleIdentifiersEnvelope(int count, string contractor, string hash)
        {
            var sb = new StringBuilder();

            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<soapenv:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" ");
            sb.Append("xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" ");
            sb.Append("xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" ");
            sb.Append("xmlns:urn=\"urn:OdoctorControllerwsdl\" ");
            sb.Append("xmlns:soapenc=\"http://schemas.xmlsoap.org/soap/encoding/\">");

            sb.Append("<soapenv:Header/>");
            sb.Append("<soapenv:Body>");

            sb.Append("<urn:get_sample_identifiers soapenv:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">");
            sb.Append("<params xsi:type=\"urn:request_get_sample_identifiers\">");

            sb.Append("<contractor xsi:type=\"xsd:string\">")
              .Append(SecurityElement.Escape(contractor ?? ""))
              .Append("</contractor>");

            sb.Append("<hash xsi:type=\"xsd:string\">")
              .Append(SecurityElement.Escape(hash ?? ""))
              .Append("</hash>");

            sb.Append("<identifiers_count xsi:type=\"xsd:int\">")
              .Append(count.ToString(CultureInfo.InvariantCulture))
              .Append("</identifiers_count>");

            sb.Append("</params>");
            sb.Append("</urn:get_sample_identifiers>");
            sb.Append("</soapenv:Body>");
            sb.Append("</soapenv:Envelope>");

            return sb.ToString();
        }

        private void ParseGetSampleIdentifiersResponse(
            string responseXml,
            out bool accepted,
            out long rangeStart,
            out long rangeEnd,
            out string errorDesc)
        {
            accepted = false;
            rangeStart = 0;
            rangeEnd = 0;
            errorDesc = "";

            var doc = new XmlDocument();
            doc.LoadXml(responseXml);

            string status = GetXmlNodeValue(doc, "status");
            accepted = string.Equals((status ?? "").Trim(), "accepted", StringComparison.OrdinalIgnoreCase);

            errorDesc = GetErrorDescription(doc);

            var rsNode = doc.GetElementsByTagName("range_start");
            var reNode = doc.GetElementsByTagName("range_end");

            if (rsNode.Count > 0) long.TryParse(rsNode[0].InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out rangeStart);
            if (reNode.Count > 0) long.TryParse(reNode[0].InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out rangeEnd);
        }

        private string BuildCreateOrderEnvelopeVariantA(string extNum,  string orderNum, string contractor,  string hash,  string doctor, string comment,
            Patient patient, GemotestOrderDetail details, IList<SoapTopServiceItem> services,  IList<TubePlan> tubes, IList<SoapSupplementalItem> supplementals)
        {
            int svcCount = services != null ? services.Count : 0;
            int tubesCount = tubes != null ? tubes.Count : 0;
            int suppCount = supplementals != null ? supplementals.Count : 0;

            string surname = patient != null ? (patient.Surname ?? "") : "";
            string firstname = patient != null ? (patient.Name ?? "") : "";
            string middlename = patient != null ? (patient.Patronimic ?? "") : "";

            DateTime birthDate = (patient != null && patient.Birthday != default(DateTime)) ? patient.Birthday : DateTime.Today;
            int gender = MapGender(patient != null ? (object)patient.Sex : null);

            string email = FirstNotEmpty(
                GetDetailValue(details, "email", "Email", "Patient_Email"),
                patient != null ? patient.EMail : "");

            string mobilePhone = GetDetailValue(details, "mobile_phone", "MobilePhone", "Patient_Phone", "phone", "Phone");
            string homePhone = GetDetailValue(details, "home_phone", "HomePhone");
            string flagSms = GetDetailValue(details, "flag_sms_notifications", "FlagSmsNotifications");

            string address = GetDetailValue(details, "address", "Address");
            string actualAddress = GetDetailValue(details, "actual_address", "ActualAddress");
            string passport = GetDetailValue(details, "passport", "Passport");
            string passportIssued = GetDetailValue(details, "passport_issued", "PassportIssued");
            string passportIssuedBy = GetDetailValue(details, "passport_issued_by", "PassportIssuedBy");
            string snils = FirstNotEmpty(
                GetDetailValue(details, "snils", "SNILS", "Patient_SNILS"),
                patient != null ? patient.SNILS : "");
            string oms = GetDetailValue(details, "oms", "OMS");
            string dms = GetDetailValue(details, "dms", "DMS");
            string birthCertificate = GetDetailValue(details, "birth_certificate", "BirthCertificate");
            string birthCertificateIssueDate = GetDetailValue(details, "birth_certificate_issue_date", "BirthCertificateIssueDate");
            string birthCertificateIssueBy = GetDetailValue(details, "birth_certificate_issue_by", "BirthCertificateIssueBy");
            string countryCode = GetDetailValue(details, "country_code", "CountryCode");

            var sb = new StringBuilder();

            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<soapenv:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" ");
            sb.Append("xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" ");
            sb.Append("xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" ");
            sb.Append("xmlns:urn=\"urn:OdoctorControllerwsdl\" ");
            sb.Append("xmlns:soapenc=\"http://schemas.xmlsoap.org/soap/encoding/\">");

            sb.Append("<soapenv:Header/>");
            sb.Append("<soapenv:Body>");
            sb.Append("<urn:create_order soapenv:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">");
            sb.Append("<params xsi:type=\"urn:order\">");

            AppendSimpleElement(sb, "ext_num", extNum, "xsd:string");
            AppendSimpleElement(sb, "order_num", orderNum, "xsd:string");
            AppendSimpleElement(sb, "contractor", contractor, "xsd:string");
            AppendSimpleElement(sb, "hash", hash, "xsd:string");
            AppendSimpleElement(sb, "doctor", doctor, "xsd:string");

            sb.Append("<order_status xsi:type=\"xsd:integer\">0</order_status>");
            sb.Append("<registered xsi:type=\"xsd:integer\">1</registered>");

            AppendSimpleElement(sb, "comment", comment, "xsd:string");

            sb.Append("<patient xsi:type=\"urn:patient\">");
            AppendSimpleElement(sb, "surname", surname, "xsd:string");
            AppendSimpleElement(sb, "firstname", firstname, "xsd:string");
            AppendSimpleElement(sb, "middlename", middlename, "xsd:string");
            AppendSimpleElement(sb, "birthdate", birthDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "xsd:date");
            sb.Append("<gender xsi:type=\"xsd:int\">")
              .Append(gender.ToString(CultureInfo.InvariantCulture))
              .Append("</gender>");
            sb.Append("</patient>");

            bool hasAdditionalInformation =
                !string.IsNullOrWhiteSpace(address) ||
                !string.IsNullOrWhiteSpace(actualAddress) ||
                !string.IsNullOrWhiteSpace(passport) ||
                !string.IsNullOrWhiteSpace(passportIssued) ||
                !string.IsNullOrWhiteSpace(passportIssuedBy) ||
                !string.IsNullOrWhiteSpace(snils) ||
                !string.IsNullOrWhiteSpace(oms) ||
                !string.IsNullOrWhiteSpace(dms) ||
                !string.IsNullOrWhiteSpace(birthCertificate) ||
                !string.IsNullOrWhiteSpace(birthCertificateIssueDate) ||
                !string.IsNullOrWhiteSpace(birthCertificateIssueBy) ||
                !string.IsNullOrWhiteSpace(countryCode);

            if (!hasAdditionalInformation)
            {
                sb.Append("<additional_information xsi:type=\"urn:additional_information\"/>");
            }
            else
            {
                sb.Append("<additional_information xsi:type=\"urn:additional_information\">");
                AppendSimpleElement(sb, "address", address, "xsd:string");
                AppendSimpleElement(sb, "actual_address", actualAddress, "xsd:string");
                AppendSimpleElement(sb, "passport", passport, "xsd:string");
                AppendSimpleElement(sb, "passport_issued", passportIssued, "xsd:string");
                AppendSimpleElement(sb, "passport_issued_by", passportIssuedBy, "xsd:string");
                AppendSimpleElement(sb, "snils", snils, "xsd:string");
                AppendSimpleElement(sb, "oms", oms, "xsd:string");
                AppendSimpleElement(sb, "dms", dms, "xsd:string");
                AppendSimpleElement(sb, "birth_certificate", birthCertificate, "xsd:string");
                AppendSimpleElement(sb, "birth_certificate_issue_date", birthCertificateIssueDate, "xsd:string");
                AppendSimpleElement(sb, "birth_certificate_issue_by", birthCertificateIssueBy, "xsd:string");
                AppendSimpleElement(sb, "country_code", countryCode, "xsd:string");
                sb.Append("</additional_information>");
            }

            sb.Append("<informing xsi:type=\"urn:informing\">");
            AppendSimpleElement(sb, "email", email, "xsd:string");
            AppendSimpleElement(sb, "mobile_phone", mobilePhone, "xsd:string");
            AppendSimpleElement(sb, "home_phone", homePhone, "xsd:string");
            sb.Append("<flag_sms_notifications xsi:type=\"xsd:boolean\">")
              .Append(ToSoapBoolean(flagSms))
              .Append("</flag_sms_notifications>");
            sb.Append("</informing>");

            sb.Append("<services xsi:type=\"urn:servicesArray\" soapenc:arrayType=\"urn:services[")
              .Append(svcCount.ToString(CultureInfo.InvariantCulture))
              .Append("]\">");

            if (services != null)
            {
                for (int i = 0; i < services.Count; i++)
                {
                    var s = services[i];
                    if (s == null || string.IsNullOrEmpty(s.Id)) continue;

                    sb.Append("<item>");
                    AppendSimpleElement(sb, "id", s.Id, "xsd:string");

                    if (string.IsNullOrWhiteSpace(s.BiomaterialId))
                        sb.Append("<biomaterial_id xsi:nil=\"true\"/>");
                    else
                        AppendSimpleElement(sb, "biomaterial_id", s.BiomaterialId, "xsd:string");

                    if (string.IsNullOrWhiteSpace(s.LocalizationId))
                        sb.Append("<localization_id xsi:nil=\"true\"/>");
                    else
                        AppendSimpleElement(sb, "localization_id", s.LocalizationId, "xsd:string");

                    if (string.IsNullOrWhiteSpace(s.TransportId))
                        sb.Append("<transport_id xsi:nil=\"true\"/>");
                    else
                        AppendSimpleElement(sb, "transport_id", s.TransportId, "xsd:string");

                    if (string.IsNullOrWhiteSpace(s.SampleId))
                        sb.Append("<sample_id xsi:nil=\"true\"/>");
                    else
                        AppendSimpleElement(sb, "sample_id", s.SampleId, "xsd:int");

                    if (string.IsNullOrWhiteSpace(s.MicrobiologyBiomaterialId))
                        sb.Append("<microbiology_biomaterial_id xsi:nil=\"true\"/>");
                    else
                        AppendSimpleElement(sb, "microbiology_biomaterial_id", s.MicrobiologyBiomaterialId, "xsd:string");

                    sb.Append("</item>");
                }
            }

            sb.Append("</services>");

            sb.Append("<services_supplementals xsi:type=\"urn:services_supplementalsArray\" soapenc:arrayType=\"urn:services_supplementals[")
              .Append(suppCount.ToString(CultureInfo.InvariantCulture))
              .Append("]\">");

            if (supplementals != null)
            {
                for (int i = 0; i < supplementals.Count; i++)
                {
                    var s = supplementals[i];
                    if (s == null) continue;

                    sb.Append("<item>");
                    AppendSimpleElement(sb, "id", s.Id, "xsd:string");
                    AppendSimpleElement(sb, "name", s.Name, "xsd:string");
                    AppendSimpleElement(sb, "value", s.Value, "xsd:string");
                    sb.Append("</item>");
                }
            }

            sb.Append("</services_supplementals>");

            sb.Append("<order_samples xsi:type=\"urn:order_sampleArray\" soapenc:arrayType=\"urn:order_sample[")
              .Append(tubesCount.ToString(CultureInfo.InvariantCulture))
              .Append("]\">");

            if (tubes != null)
            {
                for (int i = 0; i < tubes.Count; i++)
                {
                    var t = tubes[i];
                    if (t == null) continue;

                    sb.Append("<item>");

                    sb.Append("<sample_id xsi:type=\"xsd:int\">")
                      .Append(t.SampleId.ToString(CultureInfo.InvariantCulture))
                      .Append("</sample_id>");

                    AppendSimpleElement(sb, "sample_identifier", t.SampleIdentifier, "xsd:string");

                    if (string.IsNullOrEmpty(t.PrimarySampleIdentifier))
                        sb.Append("<primary_sample_identifier/>");
                    else
                        AppendSimpleElement(sb, "primary_sample_identifier", t.PrimarySampleIdentifier, "xsd:string");

                    AppendSimpleElement(sb, "microbiology_biomaterial_id", t.MicroBioBiomaterialId, null);
                    AppendSimpleElement(sb, "localization_id", t.LocalizationId, null);
                    AppendSimpleElement(sb, "biomaterial_id", t.BiomaterialId, null);
                    AppendSimpleElement(sb, "transport_id", t.TransportId, null);

                    List<TubeServicePlan> sampleServices = new List<TubeServicePlan>();

                    if (t.Services != null)
                    {
                        for (int k = 0; k < t.Services.Count; k++)
                        {
                            TubeServicePlan ss = t.Services[k];

                            if (ss == null)
                                continue;

                            if (string.IsNullOrWhiteSpace(ss.ServiceId))
                                continue;

                            sampleServices.Add(ss);
                        }
                    }

                    if (t.Parent != null && sampleServices.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "Ошибка формирования заказа Gemotest: aliquot-проба sample_id=" +
                            t.SampleId.ToString(CultureInfo.InvariantCulture) +
                            " имеет parentSample=" +
                            t.Parent.SampleId.ToString(CultureInfo.InvariantCulture) +
                            ", но не содержит ни одной услуги в order_sample/services.");
                    }

                    int osCount = sampleServices.Count;

                    sb.Append("<services xsi:type=\"urn:order_sample_serviceArray\" soapenc:arrayType=\"urn:order_sample_service[")
                      .Append(osCount.ToString(CultureInfo.InvariantCulture))
                      .Append("]\">");

                    for (int k = 0; k < sampleServices.Count; k++)
                    {
                        TubeServicePlan ss = sampleServices[k];

                        sb.Append("<item>");
                        AppendSimpleElement(sb, "service_id", ss.ServiceId, "xsd:string");
                        AppendSimpleElement(sb, "complex_id", ss.ComplexId, "xsd:string");

                        sb.Append("<utilization_flag xsi:type=\"xsd:int\">")
                          .Append(ss.UtilizationFlag.ToString(CultureInfo.InvariantCulture))
                          .Append("</utilization_flag>");

                        sb.Append("<refuse_flag xsi:type=\"xsd:int\">")
                          .Append(ss.RefuseFlag.ToString(CultureInfo.InvariantCulture))
                          .Append("</refuse_flag>");

                        sb.Append("</item>");
                    }

                    sb.Append("</services>");
                    sb.Append("</item>");
                }
            }

            sb.Append("</order_samples>");
            sb.Append("</params>");
            sb.Append("</urn:create_order>");
            sb.Append("</soapenv:Body>");
            sb.Append("</soapenv:Envelope>");

            return sb.ToString();
        }

        private static string BuildCreateOrderHash(
            string extNum,
            string orderNum,
            string contractor,
            string surname,
            DateTime birthday,
            string salt)
        {
            string birthStr = birthday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            string plain =
                (extNum ?? "") +
                (orderNum ?? "") +
                (contractor ?? "") +
                (surname ?? "") +
                birthStr +
                (salt ?? "");

            using (var sha1 = SHA1.Create())
            {
                byte[] data = Encoding.UTF8.GetBytes(plain);
                byte[] hash = sha1.ComputeHash(data);

                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));

                return sb.ToString();
            }
        }

        private static string BuildContractorHash(string contractor, string salt)
        {
            string plain = (contractor ?? "") + (salt ?? "");

            using (var sha1 = SHA1.Create())
            {
                byte[] data = Encoding.UTF8.GetBytes(plain);
                byte[] hash = sha1.ComputeHash(data);

                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));

                return sb.ToString();
            }
        }

        private string SendSoapRequest(string method, string xmlBody)
        {
            string soapAction = "\"urn:OdoctorControllerwsdl#" + method + "\"";

            Console.WriteLine("========== Gemotest SOAP REQUEST (" + method + ") | " + SenderVersion + " ==========");
            Console.WriteLine(xmlBody);
            Console.WriteLine("============================================================");

            var request = (HttpWebRequest)WebRequest.Create(_url);
            request.Method = "POST";
            request.ContentType = "text/xml; charset=utf-8";
            request.Headers["SOAPAction"] = soapAction;
            request.Timeout = 120000;
            request.ReadWriteTimeout = 120000;

            string credentials = _login + ":" + _password;
            string authHeader = Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials));
            request.Headers["Authorization"] = "Basic " + authHeader;
            request.PreAuthenticate = true;

            byte[] buffer = Encoding.UTF8.GetBytes(xmlBody);
            using (var stream = request.GetRequestStream())
            {
                stream.Write(buffer, 0, buffer.Length);
            }

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var respStream = response.GetResponseStream())
                using (var reader = new StreamReader(respStream, Encoding.UTF8))
                {
                    string responseText = reader.ReadToEnd();

                    Console.WriteLine("========== Gemotest SOAP RESPONSE (" + method + ") | " + SenderVersion + " ==========");
                    Console.WriteLine(responseText);
                    Console.WriteLine("=============================================================");

                    return responseText;
                }
            }
            catch (WebException ex)
            {
                string responseText = "";
                if (ex.Response != null)
                {
                    using (var respStream = ex.Response.GetResponseStream())
                    using (var reader = new StreamReader(respStream, Encoding.UTF8))
                    {
                        responseText = reader.ReadToEnd();
                    }
                }

                string shortError = ExtractShortSoapError(responseText);

                if (string.IsNullOrWhiteSpace(shortError))
                    shortError = ex.Message;

                throw new Exception(shortError, ex);
            }
        }

        private static string ExtractShortSoapError(string responseXml)
        {
            if (string.IsNullOrWhiteSpace(responseXml))
                return "";

            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(responseXml);

                string code = "";
                string text = "";

                var codeNode = doc.SelectSingleNode("//*[local-name()='error_code']");
                if (codeNode != null)
                    code = (codeNode.InnerText ?? "").Trim();

                var descNodes = doc.SelectNodes("//*[local-name()='error_description']");
                if (descNodes != null)
                {
                    for (int i = 0; i < descNodes.Count; i++)
                    {
                        var node = descNodes[i];
                        if (node == null)
                            continue;

                        string value = (node.InnerText ?? "").Trim();

                        if (!string.IsNullOrWhiteSpace(value) && !value.All(char.IsDigit))
                        {
                            text = value;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(text))
                    return code + ", " + text;

                if (!string.IsNullOrWhiteSpace(text))
                    return text;

                if (!string.IsNullOrWhiteSpace(code))
                    return code;

                return "";
            }
            catch
            {
                return "";
            }
        }

        private List<SoapSupplementalItem> BuildServiceSupplementals(GemotestOrderDetail details)
        {
            var result = new List<SoapSupplementalItem>();

            if (details == null || details.Details == null)
                return result;

            var sentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < details.Details.Count; i++)
            {
                var d = details.Details[i];

                if (d == null)
                    continue;

                string soapId = GetSupplementalSoapId(d);

                if (string.IsNullOrWhiteSpace(soapId))
                    continue;

                if (IsStdInfoField(soapId))
                    continue;

                string sendValue = NormalizeSupplementalValue(d);

                if (string.IsNullOrWhiteSpace(sendValue))
                    continue;

                string guiCode = !string.IsNullOrWhiteSpace(d.Code)
                    ? d.Code.Trim()
                    : soapId;

                string uniqueKey = guiCode + "|" + sendValue;

                if (sentKeys.Contains(uniqueKey))
                    continue;

                sentKeys.Add(uniqueKey);

                result.Add(new SoapSupplementalItem
                {
                    Id = soapId,
                    Name = string.IsNullOrWhiteSpace(d.Name) ? soapId : d.Name,
                    Value = sendValue
                });
            }

            return result;
        }
        private static bool IsStdInfoField(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return false;

            code = code.Trim().ToLowerInvariant();

            switch (code)
            {
                case "email":
                case "patient_email":
                case "mobile_phone":
                case "patient_phone":
                case "phone":
                case "home_phone":
                case "flag_sms_notifications":
                case "address":
                case "actual_address":
                case "passport":
                case "passport_issued":
                case "passport_issued_by":
                case "snils":
                case "patient_snils":
                case "oms":
                case "dms":
                case "birth_certificate":
                case "birth_certificate_issue_date":
                case "birth_certificate_issue_by":
                case "country_code":
                    return true;

                default:
                    return false;
            }
        }

        private string NormalizeSupplementalValue(GemotestDetail detail)
        {
            if (detail == null)
                return "";

            string value = (detail.Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                value = (detail.DisplayValue ?? "").Trim();

            if (string.IsNullOrWhiteSpace(value))
                return "";

            if (string.Equals(detail.Code ?? "", "Contingent", StringComparison.OrdinalIgnoreCase))
            {
                string contingentCode = TryExtractContingentCode(value);
                if (!string.IsNullOrWhiteSpace(contingentCode))
                    return contingentCode;
            }

            return value;
        }

        private static string TryExtractContingentCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            value = value.Trim();

            int dashIndex = value.IndexOf('-');
            if (dashIndex > 0)
            {
                string left = value.Substring(0, dashIndex).Trim();
                if (left.All(char.IsDigit))
                    return left;
            }

            int spaceIndex = value.IndexOf(' ');
            if (spaceIndex > 0)
            {
                string left = value.Substring(0, spaceIndex).Trim();
                if (left.All(char.IsDigit))
                    return left;
            }

            if (value.All(char.IsDigit))
                return value;

            return value;
        }

        private string GetDetailValue(GemotestOrderDetail details, params string[] codes)
        {
            if (details == null || details.Details == null || codes == null)
                return "";

            for (int i = 0; i < codes.Length; i++)
            {
                string code = codes[i];
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                var d = details.Details.FirstOrDefault(x =>
                    x != null &&
                    !string.IsNullOrWhiteSpace(x.Code) &&
                    string.Equals(x.Code.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase));

                if (d == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(d.Value))
                    return d.Value;

                if (!string.IsNullOrWhiteSpace(d.DisplayValue))
                    return d.DisplayValue;
            }

            return "";
        }

        private static string FirstNotEmpty(params string[] values)
        {
            if (values == null)
                return "";

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return values[i];
            }

            return "";
        }

        private static string ToSoapBoolean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "0";

            value = value.Trim();

            if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "y", StringComparison.OrdinalIgnoreCase))
                return "1";

            return "0";
        }

        private static void AppendSimpleElement(StringBuilder sb, string name, string value, string xsiType)
        {
            if (sb == null || string.IsNullOrWhiteSpace(name))
                return;

            sb.Append("<").Append(name);
            if (!string.IsNullOrWhiteSpace(xsiType))
                sb.Append(" xsi:type=\"").Append(xsiType).Append("\"");
            sb.Append(">");
            sb.Append(SecurityElement.Escape(value ?? ""));
            sb.Append("</").Append(name).Append(">");
        }

        private static string GetXmlNodeValue(XmlDocument doc, string tagName)
        {
            if (doc == null || string.IsNullOrWhiteSpace(tagName))
                return "";

            var nodes = doc.GetElementsByTagName(tagName);
            return nodes.Count > 0 ? (nodes[0].InnerText ?? "") : "";
        }

        private static string GetErrorDescription(XmlDocument doc)
        {
            if (doc == null)
                return "";

            var errNodes = doc.GetElementsByTagName("error_description");
            if (errNodes.Count == 0)
                return "";

            XmlNode n = errNodes[0];
            if (n == null)
                return "";

            var text = n.InnerText ?? "";
            return text.Trim();
        }

        private void DumpUserSelection(GemotestOrderDetail details)
        {
            Console.WriteLine();
            Console.WriteLine("=== Выбор пользователя (услуги) ===");
            for (int i = 0; i < details.Products.Count; i++)
            {
                var p = details.Products[i];

                DictionaryService svc;
                _dictionaries.Directory.TryGetValue(p.ProductId ?? "", out svc);
                if (svc != null)
                    Console.WriteLine("    -> type=" + svc.type + ", service_type=" + (svc.service_type.HasValue ? svc.service_type.Value.ToString(CultureInfo.InvariantCulture) : "null"));

                Console.WriteLine("[" + i.ToString(CultureInfo.InvariantCulture) + "] " + (p.ProductId ?? "") + " | " + (p.ProductName ?? ""));
            }

            Console.WriteLine();
            Console.WriteLine("=== Выбор пользователя (биоматериал по услугам) ===");
            var chosen = BuildChosenBiomaterialByProductIndex(details);

            for (int i = 0; i < details.Products.Count; i++)
            {
                string bio = chosen.ContainsKey(i) ? chosen[i] : "";
                string bioName = "";
                if (!string.IsNullOrEmpty(bio))
                {
                    var b = details.BioMaterials.FirstOrDefault(x => x.Id == bio);
                    bioName = b != null ? (b.Name ?? "") : "";
                }

                Console.WriteLine("[" + i.ToString(CultureInfo.InvariantCulture) + "] bio=" + bio + " " + bioName);
            }
        }

        private void DumpRows(List<SampleServiceRow> rows)
        {
            Console.WriteLine();
            Console.WriteLine("=== Требования проб (samples_services / marketing composition) ===");

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];

                string bioKey = !string.IsNullOrWhiteSpace(r.MicroBioBiomaterialId)
                    ? ("MB:" + r.MicroBioBiomaterialId)
                    : ("BM:" + r.BiomaterialId);

                Console.WriteLine(
                    "[" + i.ToString(CultureInfo.InvariantCulture) + "] svc=" + r.ServiceId +
                    (string.IsNullOrEmpty(r.ComplexId) ? "" : (" complex=" + r.ComplexId)) +
                    " sample=" + r.ExecutionSampleId.ToString(CultureInfo.InvariantCulture) +
                    (r.PrimarySampleId.HasValue ? (" primary=" + r.PrimarySampleId.Value.ToString(CultureInfo.InvariantCulture)) : "") +
                    " loc=" + (r.LocalizationId ?? "") +
                    " " + bioKey +
                    " sc=" + r.ServiceCount.ToString(CultureInfo.InvariantCulture) +
                    " execTr=" + (r.ExecutionTransportId ?? "")
                );
            }
        }

        private void DumpPacking(List<TubePlan> tubes)
        {
            Console.WriteLine();
            Console.WriteLine("=== Итоговая упаковка (пробирки) ===");

            for (int i = 0; i < tubes.Count; i++)
            {
                var t = tubes[i];
                string parent = t.Parent != null ? (" parentSample=" + t.Parent.SampleId.ToString(CultureInfo.InvariantCulture)) : "";
                Console.WriteLine(
                    "[" + i.ToString(CultureInfo.InvariantCulture) + "] sample=" + t.SampleId.ToString(CultureInfo.InvariantCulture) +
                    parent +
                    " tr=" + (t.TransportId ?? "") +
                    " loc=" + (t.LocalizationId ?? "") +
                    " bio=" + (string.IsNullOrEmpty(t.MicroBioBiomaterialId) ? (t.BiomaterialId ?? "") : ("MB:" + t.MicroBioBiomaterialId)) +
                    " used=" + t.UsedPercent.ToString("0.##", CultureInfo.InvariantCulture) + "%"
                );

                if (t.Services != null)
                {
                    for (int k = 0; k < t.Services.Count; k++)
                    {
                        var s = t.Services[k];
                        Console.WriteLine("    - " + (s.ServiceId ?? "") +
                                          (string.IsNullOrEmpty(s.ComplexId) ? "" : (" (complex " + s.ComplexId + ")")) +
                                          " share=" + s.SharePercent.ToString("0.##", CultureInfo.InvariantCulture) +
                                          "% util=" + s.UtilizationFlag.ToString(CultureInfo.InvariantCulture) +
                                          " refuse=" + s.RefuseFlag.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("Всего контейнеров для отправки: " + tubes.Count.ToString(CultureInfo.InvariantCulture));
        }

        private static bool IsStandalonePrimaryRow(DictionarySamplesServices row, List<DictionarySamplesServices> rows)
        {
            if (row == null || rows == null || rows.Count == 0)
                return false;

            string sampleId = (row.sample_id.ToString() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(sampleId))
                return false;

            if (!string.IsNullOrWhiteSpace((row.primary_sample_id.ToString() ?? "").Trim()))
                return false;

            return rows.Any(other =>
                other != null &&
                !object.ReferenceEquals(other, row) &&
                string.Equals((other.primary_sample_id.ToString() ?? "").Trim(), sampleId, StringComparison.OrdinalIgnoreCase));
        }

        private static string TryGetStringMember(object source, string fallback, params string[] memberNames)
        {
            if (source == null || memberNames == null)
                return fallback;

            Type type = source.GetType();

            for (int i = 0; i < memberNames.Length; i++)
            {
                string memberName = memberNames[i];
                if (string.IsNullOrWhiteSpace(memberName))
                    continue;

                object value = null;

                var prop = type.GetProperty(
                    memberName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.IgnoreCase);

                if (prop != null)
                    value = prop.GetValue(source, null);
                else
                {
                    var field = type.GetField(
                        memberName,
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.IgnoreCase);

                    if (field != null)
                        value = field.GetValue(source);
                }

                if (value == null)
                    continue;

                string str = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(str))
                    return str;
            }

            return fallback;
        }

        private static int TryGetIntMember(object source, int fallback, params string[] memberNames)
        {
            string raw = TryGetStringMember(source, null, memberNames);
            int parsed;
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return parsed;

            return fallback;
        }
            
        private void DumpSupplementals(List<SoapSupplementalItem> supplementals)
        {
            Console.WriteLine();
            Console.WriteLine("=== Дополнительные поля (services_supplementals) ===");

            if (supplementals == null || supplementals.Count == 0)
            {
                Console.WriteLine("(пусто)");
                return;
            }

            for (int i = 0; i < supplementals.Count; i++)
            {
                var s = supplementals[i];
                if (s == null) continue;

                Console.WriteLine("[" + i.ToString(CultureInfo.InvariantCulture) + "] " +
                                  (s.Id ?? "") +
                                  " | " + (s.Name ?? "") +
                                  " | " + (s.Value ?? ""));
            }
        }
    }
}