using Laboratory.Gemotest.Options;
using SiMed.Laboratory;
using Laboratory.Gemotest.SourseClass;
using Laboratory.Gemotest.Reports;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using StatisticsCollectionSystemClient;
using static Laboratory.Gemotest.SourseClass.GemotestOrderDetail;
using Laboratory.Gemotest.GemotestRequests;
using SiMed.Clinic;
using PrintCommon;
using System.Data;
using System.Drawing.Printing;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using ZXing;
using ZXing.Common;
using System.Globalization;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Laboratory.Gemotest
{
    public class LaboratoryGemotestGUI : ILaboratoryGUI
    {
        private LaboratoryGemotest laboratory;
        private LocalOptions localOptions;
        private SystemOptions globalOptions;
        private ProductsCollection AllProducts;
        private INumerator numerator;

        private const string GemotestOrderNumFieldId = "__gemotest_order_num_info";
        private const string InternalFieldGemotestOrderNum = "__GEMOTEST_ORDER_NUM";

        public bool SetOptions(LaboratoryGemotest lab, ProductsCollection products, LocalOptions local, SystemOptions global, INumerator _Numerator)
        {
            numerator = _Numerator;
            laboratory = lab;
            localOptions = local;
            globalOptions = global;
            AllProducts = products;
            return true;
        }

        private Exception lastException { get; set; }
        private Exception LastException
        {
            get => lastException;
            set
            {
                if (value != null)
                    SiMed.Clinic.Logger.LogEvent.SaveErrorToLog($"Гемотест. {value.Message}\r\n{value.StackTrace}", "Gemotest");
                lastException = value;
            }
        }

        public Exception GetLastException() => LastException;

        private static void DebugGemotestGui(string message)
        {
            try
            {
                string line = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                    " [Gemotest GUI DEBUG] " + (message ?? string.Empty);

                Console.WriteLine(line);
                System.Diagnostics.Debug.WriteLine(line);
            }
            catch
            {
            }
        }

        private static string JoinDebugValues(IEnumerable<string> values)
        {
            if (values == null)
                return "";

            return string.Join(",", values.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray());
        }

        private void DumpGuiOrderState(string stage, Order order, OrderModelForGUI model, GemotestOrderDetail details)
        {
            try
            {
                if (details == null && order != null)
                    details = order.OrderDetail as GemotestOrderDetail;

                DebugGemotestGui("========== " + stage + " ==========");

                if (order != null)
                {
                    DebugGemotestGui("ORDER: Number=" + (order.Number ?? "") + "; State=" + order.State);
                }

                if (details != null)
                {
                    DebugGemotestGui("DETAILS: Products=" + (details.Products != null ? details.Products.Count.ToString(CultureInfo.InvariantCulture) : "null") +
                        "; BioMaterials=" + (details.BioMaterials != null ? details.BioMaterials.Count.ToString(CultureInfo.InvariantCulture) : "null") +
                        "; Samples=" + (details.Samples != null ? details.Samples.Count.ToString(CultureInfo.InvariantCulture) : "null"));

                    if (details.Products != null)
                    {
                        for (int i = 0; i < details.Products.Count; i++)
                        {
                            var p = details.Products[i];
                            if (p == null)
                                continue;

                            DebugGemotestGui("DETAILS.PRODUCT[" + i.ToString(CultureInfo.InvariantCulture) + "]: guid=" + (p.OrderProductGuid ?? "") +
                                "; id=" + (p.ProductId ?? "") +
                                "; code=" + (p.ProductCode ?? "") +
                                "; name=" + (p.ProductName ?? ""));
                        }
                    }

                    if (details.BioMaterials != null)
                    {
                        for (int i = 0; i < details.BioMaterials.Count; i++)
                        {
                            var b = details.BioMaterials[i];
                            if (b == null)
                                continue;

                            DebugGemotestGui("DETAILS.BIOMATERIAL[" + i.ToString(CultureInfo.InvariantCulture) + "]: id=" + (b.Id ?? "") +
                                "; code=" + (b.Code ?? "") +
                                "; name=" + (b.Name ?? "") +
                                "; Mandatory=" + JoinDebugValues((b.Mandatory ?? new List<int>()).Select(x => x.ToString(CultureInfo.InvariantCulture))) +
                                "; Chosen=" + JoinDebugValues((b.Chosen ?? new List<int>()).Select(x => x.ToString(CultureInfo.InvariantCulture))) +
                                "; Another=" + JoinDebugValues((b.Another ?? new List<int>()).Select(x => x.ToString(CultureInfo.InvariantCulture))));
                        }
                    }
                }

                if (model != null)
                {
                    DebugGemotestGui("MODEL: ProductsInfo=" + (model.ProductsInfo != null ? model.ProductsInfo.Count.ToString(CultureInfo.InvariantCulture) : "null") +
                        "; ServicesInfo=" + (model.ServicesInfo != null ? model.ServicesInfo.Count.ToString(CultureInfo.InvariantCulture) : "null") +
                        "; Samples=" + (model.Samples != null ? model.Samples.Count.ToString(CultureInfo.InvariantCulture) : "null") +
                        "; Fields=" + (model.Fields != null ? model.Fields.Count.ToString(CultureInfo.InvariantCulture) : "null"));

                    if (model.ProductsInfo != null)
                    {
                        for (int i = 0; i < model.ProductsInfo.Count; i++)
                        {
                            var p = model.ProductsInfo[i];
                            if (p == null)
                                continue;

                            DebugGemotestGui("MODEL.PRODUCT[" + i.ToString(CultureInfo.InvariantCulture) + "]: guid=" + (p.OrderProductGuid ?? "") +
                                "; id=" + (p.Id ?? "") +
                                "; code=" + (p.Code ?? "") +
                                "; name=" + (p.Name ?? ""));

                            if (p.BiomaterialGroups == null)
                                continue;

                            for (int g = 0; g < p.BiomaterialGroups.Count; g++)
                            {
                                var group = p.BiomaterialGroups[g];
                                if (group == null)
                                    continue;

                                var allBio = (group.Biomaterials ?? new List<BiomaterialInfoForGUI>())
                                    .Where(x => x != null)
                                    .Select(x => (x.BiomaterialId ?? "") + "/" + (x.BiomaterialName ?? "") + "/" + (x.ContainerCode ?? ""));

                                var selectedBio = (group.BiomaterialsSelected ?? new List<BiomaterialInfoForGUI>())
                                    .Where(x => x != null)
                                    .Select(x => (x.BiomaterialId ?? "") + "/" + (x.BiomaterialName ?? "") + "/" + (x.ContainerCode ?? ""));

                                DebugGemotestGui("MODEL.PRODUCT[" + i.ToString(CultureInfo.InvariantCulture) + "].GROUP[" + g.ToString(CultureInfo.InvariantCulture) + "]: SelectOnlyOne=" + group.SelectOnlyOne +
                                    "; Optional=" + group.Optional +
                                    "; All=[" + JoinDebugValues(allBio) + "]" +
                                    "; Selected=[" + JoinDebugValues(selectedBio) + "]");
                            }
                        }
                    }

                    if (model.Samples != null)
                    {
                        for (int i = 0; i < model.Samples.Count; i++)
                        {
                            var s = model.Samples[i];
                            if (s == null)
                                continue;

                            string biomaterial = "";
                            if (s.Biomaterial != null)
                            {
                                biomaterial = (s.Biomaterial.BiomaterialId ?? "") + "/" +
                                    (s.Biomaterial.BiomaterialName ?? "") + "/" +
                                    (s.Biomaterial.ContainerCode ?? "");
                            }

                            DebugGemotestGui("MODEL.SAMPLE[" + i.ToString(CultureInfo.InvariantCulture) + "]: guid=" + (s.OrderSampleGuid ?? "") +
                                "; barcode=" + (s.Barcode ?? "") +
                                "; biomaterial=" + biomaterial +
                                "; productGuids=" + JoinDebugValues(s.OrderProductGuids ?? new List<string>()));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugGemotestGui("DumpGuiOrderState failed: " + ex.Message);
            }
        }

        public bool GetGuiOptions(out List<GuiOption> _Options)
        {
            _Options = new List<GuiOption>
            {
                new GuiOption() { OptionName = eGuiOptionName.CanAddProduct },
                new GuiOption() { OptionName = eGuiOptionName.CanRemoveProduct },
                new GuiOption() { OptionName = eGuiOptionName.CanCheckResultsIfCommited },
                new GuiOption() { OptionName = eGuiOptionName.CanRemoveService }
            };
            return true;
        }

        public bool GenerateSamples(Order _Order, OrderModelForGUI _Model)
        {
            LastException = null;
            try
            {
                var samples = new List<SampleInfoForGUI>();

                foreach (var productInfo in _Model.ProductsInfo)
                {
                    if (productInfo?.BiomaterialGroups == null)
                        continue;

                    var biomaterialsForSamples = BuildSelectedBiomaterialsForSamples(productInfo);

                    foreach (var biomInfo in biomaterialsForSamples)
                    {
                        if (biomInfo == null)
                            continue;

                        var sameCodeProducts = _Model.ProductsInfo
                            .Where(x => x.Code == productInfo.Code)
                            .Select(x => x.OrderProductGuid)
                            .ToList();

                        SampleInfoForGUI sampleFind = null;

                        foreach (var sample in samples)
                        {
                            if (sample.Biomaterial == null)
                                continue;

                            if (!string.Equals(sample.Biomaterial.BiomaterialCode, biomInfo.BiomaterialCode, StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (!string.Equals(sample.Biomaterial.ContainerCode, biomInfo.ContainerCode, StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (sample.OrderProductGuids.Any(x => sameCodeProducts.Contains(x)))
                                continue;

                            sampleFind = sample;
                            break;
                        }

                        if (sampleFind == null)
                        {
                            var sampleNew = new SampleInfoForGUI
                            {
                                OrderSampleGuid = Guid.NewGuid().ToString(),
                                Biomaterial = biomInfo
                            };
                            sampleNew.OrderProductGuids.Add(productInfo.OrderProductGuid);
                            samples.Add(sampleNew);
                        }
                        else
                        {
                            sampleFind.OrderProductGuids.Add(productInfo.OrderProductGuid);
                        }
                    }
                }

                foreach (var sampleNew in samples)
                {
                    foreach (var oldSample in _Model.Samples)
                    {
                        if (oldSample?.Biomaterial == null)
                            continue;

                        if (!string.Equals(oldSample.Biomaterial.BiomaterialCode, sampleNew.Biomaterial.BiomaterialCode, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!string.Equals(oldSample.Biomaterial.ContainerCode, sampleNew.Biomaterial.ContainerCode, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var oldSet = string.Join(",", oldSample.OrderProductGuids.OrderBy(x => x));
                        var newSet = string.Join(",", sampleNew.OrderProductGuids.OrderBy(x => x));

                        if (oldSet != newSet)
                            continue;

                        sampleNew.OrderSampleGuid = oldSample.OrderSampleGuid;
                        sampleNew.Barcode = oldSample.Barcode;
                        break;
                    }
                }
                var details = _Order?.OrderDetail as GemotestOrderDetail;
                if (details != null && details.Samples != null)
                {
                    foreach (var sampleNew in samples)
                    {
                        if (!string.IsNullOrWhiteSpace(sampleNew.Barcode))
                            continue;

                        foreach (var sampleFromDetails in details.Samples)
                        {
                            if (sampleFromDetails == null)
                                continue;

                            if (!string.Equals(sampleFromDetails.BiomCode, sampleNew.Biomaterial.BiomaterialCode, StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (!string.Equals(sampleFromDetails.ContCode, sampleNew.Biomaterial.ContainerCode, StringComparison.OrdinalIgnoreCase))
                                continue;

                            var oldSet = string.Join(",", sampleFromDetails.OrderProductGuidList.OrderBy(x => x));
                            var newSet = string.Join(",", sampleNew.OrderProductGuids.OrderBy(x => x));

                            if (oldSet != newSet)
                                continue;

                            sampleNew.OrderSampleGuid = sampleFromDetails.OrderSampleGuid;
                            sampleNew.Barcode = sampleFromDetails.Barcode;
                            break;
                        }
                    }
                }
                _Model.Samples = samples;
                DumpGuiOrderState("GenerateSamples: after building samples", _Order, _Model, details);
                return true;
            }
            catch (Exception exc)
            {
                LastException = exc;
                return false;
            }
        }

        private const string SupplementalInstanceSeparator = "__FOR__";

        private static string MakeSupplementalInstanceFieldId(string supplementalId, string ownerProductGuid)
        {
            supplementalId = (supplementalId ?? string.Empty).Trim();
            ownerProductGuid = (ownerProductGuid ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(supplementalId))
                return string.Empty;

            if (string.IsNullOrWhiteSpace(ownerProductGuid))
                return supplementalId;

            return supplementalId + SupplementalInstanceSeparator + ownerProductGuid;
        }

        private static string GetSupplementalBaseIdFromFieldId(string fieldId)
        {
            fieldId = (fieldId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(fieldId))
                return string.Empty;

            int pos = fieldId.IndexOf(SupplementalInstanceSeparator, StringComparison.Ordinal);
            if (pos < 0)
                return fieldId;

            return fieldId.Substring(0, pos);
        }

        private string BuildSupplementalInstanceDescription(string supplementalName, string ownerProductName)
        {
            supplementalName = (supplementalName ?? string.Empty).Trim();
            ownerProductName = (ownerProductName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(supplementalName))
                supplementalName = "Дополнительная информация";

            if (string.IsNullOrWhiteSpace(ownerProductName))
                return supplementalName;

            return supplementalName + " для " + ownerProductName;
        }

        private HashSet<string> BuildAutoServiceIdsForSelectedProducts(OrderModelForGUI model)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (model == null ||
                model.ProductsInfo == null ||
                laboratory == null ||
                laboratory.Dicts == null ||
                laboratory.Dicts.ServiceAutoInsert == null)
            {
                return result;
            }

            foreach (var productInfo in model.ProductsInfo)
            {
                if (productInfo == null || string.IsNullOrWhiteSpace(productInfo.Id))
                    continue;

                List<DictionaryServiceAutoInsert> rows;

                if (!laboratory.Dicts.ServiceAutoInsert.TryGetValue(NormalizeServiceId(productInfo.Id), out rows) || rows == null)
                    continue;

                foreach (var row in rows)
                {
                    if (row == null || row.archive != 0 || string.IsNullOrWhiteSpace(row.auto_service_id))
                        continue;

                    result.Add(NormalizeServiceId(row.auto_service_id));
                }
            }

            return result;
        }
        private static bool IsInternalGemotestField(string fieldId)
        {
            return string.Equals(fieldId, GemotestOrderNumFieldId, StringComparison.OrdinalIgnoreCase);
        }
        private static string GetGemotestOrderNumForDisplay(GemotestOrderDetail details)
        {
            if (details == null)
                return "";

            if (!string.IsNullOrWhiteSpace(details.OrderNum))
                return details.OrderNum;

            if (!string.IsNullOrWhiteSpace(details.ResultsOrderNum))
                return details.ResultsOrderNum;

            return "";
        }
        private static void AddOrderNumInfoField(List<FieldInfoForGUI> fields, GemotestOrderDetail details)
        {
            if (fields == null || details == null)
                return;

            if (string.IsNullOrWhiteSpace(details.OrderNum))
                return;

            bool alreadyExists = fields.Any(x =>
                x != null &&
                string.Equals(x.Id, GemotestOrderNumFieldId, StringComparison.OrdinalIgnoreCase));

            if (alreadyExists)
                return;

            fields.Add(new FieldInfoForGUI()
            {
                Id = GemotestOrderNumFieldId,
                Description = "Номер заказа в ЛИС Гемотест",
                Value = details.OrderNum,
                DisplayValue = details.OrderNum,
                FieldDataType = FieldDataType.Text,
                Mandatory = false,
                RefreshFieldsOnValueChanged = false,
                RefreshSamplesOnValueChanged = false
            });
        }

        private static void AddGemotestOrderNumDisplayField(List<FieldInfoForGUI> fields, GemotestOrderDetail details)
        {
            if (fields == null || details == null)
                return;

            string orderNum = GetGemotestOrderNumForDisplay(details);

            if (string.IsNullOrWhiteSpace(orderNum))
                return;

            fields.Add(new FieldInfoForGUI
            {
                Id = InternalFieldGemotestOrderNum,
                Description = "Номер заказа в ЛИС Гемотест",
                Value = orderNum,
                FieldDataType = FieldDataType.Text,
                Mandatory = false
            });
        }

        public bool GenerateFields(Order _Order, OrderModelForGUI _Model)
        {
            LastException = null;

            try
            {
                var details = _Order?.OrderDetail as GemotestOrderDetail;
                if (details == null || _Model == null)
                    return true;

                var fields = new List<FieldInfoForGUI>();

                var generatedSupplementalBaseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var products = _Model.ProductsInfo ?? new List<ProductInfoForGUI>();

                if (laboratory?.Dicts?.ServicesSupplementals != null)
                {
                    foreach (var sourceProduct in products)
                    {
                        if (sourceProduct == null || string.IsNullOrWhiteSpace(sourceProduct.Id))
                            continue;

                        string sourceServiceId = NormalizeServiceId(sourceProduct.Id);
                        if (string.IsNullOrWhiteSpace(sourceServiceId))
                            continue;

                        if (!laboratory.Dicts.ServicesSupplementals.TryGetValue(sourceServiceId, out var supplementals) ||
                            supplementals == null)
                        {
                            continue;
                        }

                        List<ProductInfoForGUI> ownerProducts = GetSupplementalOwnerProducts(sourceProduct);

                        foreach (var supp in supplementals)
                        {
                            if (supp == null)
                                continue;

                            string baseId = (supp.test_id ?? string.Empty).Trim();
                            if (string.IsNullOrWhiteSpace(baseId))
                                continue;

                            generatedSupplementalBaseIds.Add(baseId);

                            string baseDescription = string.IsNullOrWhiteSpace(supp.name)
                                ? baseId
                                : supp.name.Trim();

                            bool isDictionary = !string.IsNullOrWhiteSpace(supp.value);

                            foreach (var ownerProduct in ownerProducts)
                            {
                                if (ownerProduct == null)
                                    continue;

                                string ownerGuid = ownerProduct.OrderProductGuid;
                                if (string.IsNullOrWhiteSpace(ownerGuid))
                                    continue;

                                string ownerName = !string.IsNullOrWhiteSpace(ownerProduct.Name)
                                    ? ownerProduct.Name.Trim()
                                    : GetServiceCaption(_Model, ownerProduct.Id);

                                string fieldId = MakeSupplementalInstanceFieldId(baseId, ownerGuid);

                                string description = BuildSupplementalInstanceDescription(
                                    baseDescription,
                                    ownerName);

                                AddSupplementalFieldIfNotExists(
                                    fields,
                                    fieldId,
                                    description,
                                    ownerGuid,
                                    supp.required,
                                    isDictionary ? FieldDataType.Dictionary : FieldDataType.Text,
                                    supp.value);
                            }
                        }
                    }
                }

                if (details.Details != null)
                {
                    foreach (var detail in details.Details)
                    {
                        if (detail == null)
                            continue;

                        string fieldId = GetDetailKey(detail);
                        if (string.IsNullOrWhiteSpace(fieldId))
                            continue;

                        FieldInfoForGUI field = fields.FirstOrDefault(x =>
                            string.Equals(x.Id, fieldId, StringComparison.OrdinalIgnoreCase));

                        if (field == null)
                        {
                            string detailBaseId = GetSupplementalBaseIdFromFieldId(fieldId);

                            if (!string.IsNullOrWhiteSpace(detailBaseId) &&
                                generatedSupplementalBaseIds.Contains(detailBaseId))
                            {
                                continue;
                            }

                            field = new FieldInfoForGUI()
                            {
                                Id = fieldId,
                                Description = string.IsNullOrWhiteSpace(detail.Name) ? fieldId : detail.Name,
                                Mandatory = detail.MandatoryProducts != null && detail.MandatoryProducts.Count > 0,
                                Regex = detail.regex,
                                FieldDataType = FieldDataType.Text,
                            };

                            foreach (var idx in (detail.MandatoryProducts ?? new List<int>())
                                         .Concat(detail.OptionalProducts ?? new List<int>())
                                         .Distinct())
                            {
                                if (details.Products != null &&
                                    idx >= 0 &&
                                    idx < details.Products.Count &&
                                    details.Products[idx] != null &&
                                    !string.IsNullOrWhiteSpace(details.Products[idx].OrderProductGuid))
                                {
                                    field.OrderProductGuidList.Add(details.Products[idx].OrderProductGuid);
                                }
                            }

                            fields.Add(field);
                        }

                        field.Value = detail.Value ?? string.Empty;
                        field.DisplayValue = string.IsNullOrWhiteSpace(detail.DisplayValue)
                            ? (detail.Value ?? string.Empty)
                            : detail.DisplayValue;


                        if (!string.IsNullOrWhiteSpace(detail.regex))
                            field.Regex = detail.regex;
                    }
                }

                AddOrderNumInfoField(fields, details);

                _Model.Fields = fields.OrderBy(x => x.Description).ThenBy(x => x.Id).ToList();

                return true;

                List<ProductInfoForGUI> GetSupplementalOwnerProducts(ProductInfoForGUI sourceProduct)
                {
                    var result = new List<ProductInfoForGUI>();

                    if (sourceProduct == null)
                        return result;

                    string sourceServiceId = NormalizeServiceId(sourceProduct.Id);
                    if (string.IsNullOrWhiteSpace(sourceServiceId))
                        return result;

                    if (laboratory?.Dicts?.ServiceAutoInsert != null)
                    {
                        foreach (var possibleOwner in products)
                        {
                            if (possibleOwner == null)
                                continue;

                            string ownerServiceId = NormalizeServiceId(possibleOwner.Id);

                            if (string.IsNullOrWhiteSpace(ownerServiceId))
                                continue;

                            if (string.Equals(ownerServiceId, sourceServiceId, StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (!laboratory.Dicts.ServiceAutoInsert.TryGetValue(ownerServiceId, out var autoRows) ||
                                autoRows == null)
                            {
                                continue;
                            }

                            foreach (var row in autoRows)
                            {
                                if (row == null || string.IsNullOrWhiteSpace(row.auto_service_id))
                                    continue;

                                string autoServiceId = NormalizeServiceId(row.auto_service_id);

                                if (string.Equals(autoServiceId, sourceServiceId, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!result.Any(x =>
                                            string.Equals(x.OrderProductGuid,
                                                possibleOwner.OrderProductGuid,
                                                StringComparison.OrdinalIgnoreCase)))
                                    {
                                        result.Add(possibleOwner);
                                    }

                                    break;
                                }
                            }
                        }
                    }

                    if (result.Count == 0)
                        result.Add(sourceProduct);

                    return result;
                }
            }
            catch (Exception exc)
            {
                LastException = exc;
                return false;
            }
        }
        public bool ValidateGUIModel(OrderModelForGUI _Model)
        {
            LastException = null;
            try
            {
                _Model.Errors.Clear();



                if (_Model.Fields != null)
                {
                    foreach (var field in _Model.Fields)
                    {
                        if (field == null)
                            continue;

                        if (field.Mandatory && string.IsNullOrWhiteSpace(field.Value))
                        {
                            _Model.Errors.Add(new ErrorMessage()
                            {
                                Field = field,
                                ErrorText = $"Поле '{field.Description}' обязательно для заполнения"
                            });
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(field.Value) && !string.IsNullOrWhiteSpace(field.Regex))
                        {
                            var regex = new Regex(field.Regex);
                            if (!regex.IsMatch(field.Value))
                            {
                                _Model.Errors.Add(new ErrorMessage()
                                {
                                    Field = field,
                                    ErrorText = $"Поле '{field.Description}' не соответствует формату '{field.Regex}'"
                                });
                            }
                        }
                    }
                }

                return _Model.Errors.Count == 0;
            }
            catch (Exception exc)
            {
                LastException = exc;
                return false;
            }
        }

        public bool CreateOrderModelForGUI(bool _ReadOnly, Order _Order, ref ResultsCollection _Results, ref OrderModelForGUI _Model)
        {
            LastException = null;

            _Model.Documents.Clear();
            _Model.Errors.Clear();
            _Model.Fields.Clear();
            _Model.PriceLists.Clear();
            _Model.PriceListSelected = null;
            _Model.ProductsInfo.Clear();
            _Model.ServicesInfo.Clear();
            _Model.Samples.Clear();

            try
            {
                var details = _Order.OrderDetail as GemotestOrderDetail;
                if (details == null)
                    return true;

                details.Dicts = laboratory.Dicts;

                if (!_ReadOnly && _Order != null && _Order.State == OrderState.NotSended)
                    RemoveDisabledBiomaterialCollectServicesFromDetails(details, "CreateOrderModelForGUI: before build model");

                if (_ReadOnly)
                    return BuildReadOnlyModelFromDetails(_Order, details, _Model);

                FillPriceListsForModel(false, details, _Model);

                foreach (var product in details.Products)
                {
                    var p = new ProductInfoForGUI
                    {
                        OrderProductGuid = string.IsNullOrWhiteSpace(product.OrderProductGuid)
                            ? details.Products.IndexOf(product).ToString()
                            : product.OrderProductGuid,
                        Id = product.ProductId,
                        Code = product.ProductCode,
                        Name = product.ProductName,
                        ProductGroupGuid = null
                    };

                    if (IsBiomaterialCollectService(product.ProductId))
                    {
                        _Model.ServicesInfo.Add(p);
                    }
                    else
                    {
                        _Model.ProductsInfo.Add(p);
                    }
                }

                if (_Order.State == OrderState.NotSended)
                {
                    ApplyAutoInsertServices(details, _Model);
                    RemoveDisabledBiomaterialCollectServicesFromModel(_Model, "CreateOrderModelForGUI: after ApplyAutoInsertServices");
                    RemoveDisabledBiomaterialCollectServicesFromDetails(details, "CreateOrderModelForGUI: after ApplyAutoInsertServices");

                    for (int i = 0; i < _Model.ProductsInfo.Count; i++)
                        _Model.ProductsInfo[i].OrderProductGuid = i.ToString();

                    if (details.Products != null)
                    {
                        for (int i = 0; i < details.Products.Count; i++)
                            details.Products[i].OrderProductGuid = i.ToString();
                    }

                    if (details.BioMaterials == null)
                        details.BioMaterials = new List<GemotestBioMaterial>();

                    var savedBiomaterialSelectionState = CaptureBiomaterialSelectionState(details.BioMaterials);
                    details.BioMaterials.Clear();
                    details.AddBiomaterialsFromProducts();
                    RestoreBiomaterialSelectionState(details.BioMaterials, savedBiomaterialSelectionState);
                    EnsureRequiredSampleBiomaterialsInDetails(details);
                }

                RebuildBiomaterialGroups(details, _Model);
                DumpGuiOrderState("CreateOrderModelForGUI: after RebuildBiomaterialGroups", _Order, _Model, details);

                if (!GenerateSamples(_Order, _Model))
                    return false;

                if (!GenerateFields(_Order, _Model))
                    return false;

                AddDocumentsForModel(details, _Model);

                return true;
            }
            catch (Exception ex)
            {
                LastException = ex;
                return false;
            }
        }

        private bool BuildReadOnlyModelFromDetails(Order order, GemotestOrderDetail details, OrderModelForGUI model)
        {
            try
            {
                FillPriceListsForModel(true, details, model);

                BuildReadOnlyProductsFromDetails(details, model);
                BuildReadOnlySamplesFromDetails(details, model);
                BuildReadOnlyFieldsFromDetails(details, model);

                AddDocumentsForModel(details, model);

                return true;
            }
            catch (Exception ex)
            {
                LastException = ex;
                return false;
            }
        }

        private void BuildReadOnlyProductsFromDetails(GemotestOrderDetail details, OrderModelForGUI model)
        {
            if (details == null || details.Products == null || model == null)
                return;

            for (int productIndex = 0; productIndex < details.Products.Count; productIndex++)
            {
                var product = details.Products[productIndex];

                if (product == null)
                    continue;

                var productNew = new ProductInfoForGUI
                {
                    OrderProductGuid = string.IsNullOrWhiteSpace(product.OrderProductGuid)
                        ? productIndex.ToString()
                        : product.OrderProductGuid,

                    Id = product.ProductId ?? string.Empty,
                    Code = product.ProductCode ?? string.Empty,
                    Name = product.ProductName ?? string.Empty,
                    ProductGroupGuid = null,
                    BiomaterialGroups = new List<BiomaterialGroupForGUI>()
                };
                if (IsBiomaterialCollectService(product.ProductId))
                {
                    model.ServicesInfo.Add(productNew);
                    continue;
                }

                var group = BuildBiomaterialGroupForProduct(details, productIndex);

                if (group == null)
                {
                    group = new BiomaterialGroupForGUI
                    {
                        GroupNum = 0,
                        SelectOnlyOne = true,
                        Optional = true
                    };
                }

                group.GroupNum = 0;
                group.Optional = true;

                if (group.Biomaterials == null || group.Biomaterials.Count == 0)
                {
                    group = BuildReadOnlySampleBiomaterialGroupFromDetails(
                        details,
                        productNew.OrderProductGuid);
                }

                if (group.Biomaterials == null || group.Biomaterials.Count == 0)
                {
                    var fallbackBiom = ResolveFallbackBiomaterialForProduct(details, product);

                    if (fallbackBiom != null)
                    {
                        group.Biomaterials.Add(fallbackBiom);
                        group.BiomaterialsSelected.Add(fallbackBiom);
                    }
                }

                productNew.BiomaterialGroups.Add(group);
                model.ProductsInfo.Add(productNew);
            }
        }

        private BiomaterialGroupForGUI   BuildReadOnlySampleBiomaterialGroupFromDetails(GemotestOrderDetail details, string orderProductGuid)
        {
            var group = new BiomaterialGroupForGUI
            {
                GroupNum = 0,
                SelectOnlyOne = false,
                Optional = true
            };

            if (details == null || details.Samples == null)
                return group;

            foreach (var sampleInfo in details.Samples)
            {
                if (sampleInfo == null || sampleInfo.OrderProductGuidList == null)
                    continue;

                if (!sampleInfo.OrderProductGuidList.Contains(orderProductGuid))
                    continue;

                var biomInfo = new BiomaterialInfoForGUI
                {
                    BiomaterialId = sampleInfo.BiomId ?? string.Empty,
                    BiomaterialCode = sampleInfo.BiomCode ?? string.Empty,
                    BiomaterialName = sampleInfo.BiomName ?? string.Empty,

                    ContainerId = sampleInfo.ContId ?? string.Empty,
                    ContainerCode = sampleInfo.ContCode ?? string.Empty,
                    ContainerName = sampleInfo.ContName ?? string.Empty
                };

                bool alreadyExists = group.Biomaterials.Any(x =>
                    string.Equals(x.BiomaterialId ?? string.Empty, biomInfo.BiomaterialId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.ContainerCode ?? string.Empty, biomInfo.ContainerCode ?? string.Empty, StringComparison.OrdinalIgnoreCase));

                if (!alreadyExists)
                {
                    group.Biomaterials.Add(biomInfo);
                    group.BiomaterialsSelected.Add(biomInfo);
                }
            }

            return group;
        }
        private BiomaterialInfoForGUI ResolveFallbackBiomaterialForProduct(GemotestOrderDetail details, GemotestOrderDetail.GemotestProductDetail product)
        {
            if (details == null || product == null || details.Dicts == null)
                return null;

            var dicts = details.Dicts;

            if (dicts.Directory == null)
                return null;

            if (!dicts.Directory.TryGetValue(product.ProductId ?? string.Empty, out var service) || service == null)
                return null;

            string biomId = service.biomaterial_id ?? string.Empty;
            if (string.IsNullOrWhiteSpace(biomId))
                return null;

            if (dicts.Biomaterials == null)
                return null;

            if (!dicts.Biomaterials.TryGetValue(biomId, out var biom) || biom == null)
                return null;

            DictionaryTransport transport = null;

            if (!string.IsNullOrWhiteSpace(service.transport_id) &&
                dicts.Transport != null)
            {
                dicts.Transport.TryGetValue(service.transport_id, out transport);
            }

            if (transport == null &&
                dicts.ServiceParameters != null &&
                dicts.ServiceParameters.TryGetValue(service.id ?? string.Empty, out var parameters) &&
                parameters != null)
            {
                var param = parameters.FirstOrDefault(x =>
                    x != null &&
                    string.Equals(x.biomaterial_id ?? string.Empty, biomId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(x.transport_id));

                if (param == null)
                {
                    param = parameters.FirstOrDefault(x =>
                        x != null &&
                        !string.IsNullOrWhiteSpace(x.transport_id));
                }

                if (param != null && dicts.Transport != null)
                    dicts.Transport.TryGetValue(param.transport_id, out transport);
            }

            if (transport == null)
                TryResolveTransportFromSamplesServices(service.id, biomId, out transport);

            return new BiomaterialInfoForGUI
            {
                BiomaterialId = biom.id ?? string.Empty,
                BiomaterialCode = biom.id ?? string.Empty,
                BiomaterialName = biom.name ?? string.Empty,

                ContainerId = transport != null ? (transport.id ?? string.Empty) : string.Empty,
                ContainerCode = transport != null ? (transport.id ?? string.Empty) : string.Empty,
                ContainerName = transport != null ? (transport.name ?? string.Empty) : "-"
            };
        }
        private void BuildReadOnlySamplesFromDetails(GemotestOrderDetail details, OrderModelForGUI model)
        {
            if (details.Samples == null)
                return;

            foreach (var sampleInfo in details.Samples)
            {
                if (sampleInfo == null)
                    continue;

                var sampleNew = new SampleInfoForGUI
                {
                    OrderSampleGuid = sampleInfo.OrderSampleGuid ?? string.Empty,
                    Barcode = sampleInfo.Barcode ?? string.Empty,
                    Biomaterial = new BiomaterialInfoForGUI
                    {
                        BiomaterialId = sampleInfo.BiomId ?? string.Empty,
                        BiomaterialCode = sampleInfo.BiomCode ?? string.Empty,
                        BiomaterialName = sampleInfo.BiomName ?? string.Empty,
                        ContainerId = sampleInfo.ContId ?? string.Empty,
                        ContainerCode = sampleInfo.ContCode ?? string.Empty,
                        ContainerName = sampleInfo.ContName ?? string.Empty
                    },
                    OrderProductGuids = sampleInfo.OrderProductGuidList != null
                        ? new List<string>(sampleInfo.OrderProductGuidList)
                        : new List<string>()
                };

                model.Samples.Add(sampleNew);
            }
        }

        private void BuildReadOnlyFieldsFromDetails(GemotestOrderDetail details, OrderModelForGUI model)
        {
            if (details.Details == null)
                return;

            foreach (var detail in details.Details)
            {
                if (detail == null)
                    continue;

                var fieldNew = new FieldInfoForGUI
                {
                    Id = GetDetailKey(detail),
                    Description = string.IsNullOrWhiteSpace(detail.Name) ? GetDetailKey(detail) : detail.Name,
                    Value = detail.Value ?? string.Empty,
                    DisplayValue = string.IsNullOrWhiteSpace(detail.DisplayValue) ? (detail.Value ?? string.Empty) : detail.DisplayValue,
                    Regex = detail.regex,
                    Mandatory = detail.MandatoryProducts != null && detail.MandatoryProducts.Count > 0,
                    FieldDataType = detail.dictionaryId.HasValue ? FieldDataType.Dictionary : FieldDataType.Text
                };

                if (detail.MandatoryProducts != null)
                {
                    foreach (var idx in detail.MandatoryProducts)
                    {
                        if (idx >= 0 && idx < details.Products.Count)
                            fieldNew.OrderProductGuidList.Add(details.Products[idx].OrderProductGuid);
                    }
                }

                if (detail.OptionalProducts != null)
                {
                    foreach (var idx in detail.OptionalProducts)
                    {
                        if (idx >= 0 && idx < details.Products.Count)
                        {
                            string guid = details.Products[idx].OrderProductGuid;
                            if (!fieldNew.OrderProductGuidList.Contains(guid))
                                fieldNew.OrderProductGuidList.Add(guid);
                        }
                    }
                }

                model.Fields.Add(fieldNew);
            }
            AddOrderNumInfoField(model.Fields, details);

            model.Fields = model.Fields.OrderBy(x => x.Description).ThenBy(x => x.Id).ToList();
        }

        private void AddDocumentsForModel(GemotestOrderDetail details, OrderModelForGUI model)
        {
            model.Documents.Add(new DocumentInfoForGUI()
            {
                Name = "Сопроводительный бланк",
                DocType = LaboratoryPrintDocumentType.Blank,
                PreviewAvailable = true,
                PrintAvailable = true
            });

            bool hasRealBarcodes =
                details.Samples != null &&
                details.Samples.Any(x => x != null && !string.IsNullOrWhiteSpace(x.Barcode));

            if (hasRealBarcodes)
            {
                model.Documents.Add(new DocumentInfoForGUI()
                {
                    Name = "Наклейки",
                    DocType = LaboratoryPrintDocumentType.Stikers,
                    PrintAvailable = true
                });
            }

            bool hasResults = details.Results != null && details.Results.Any(x => x != null);
            bool hasAttachments = details.Attachments != null && details.Attachments.Any(x => x != null && !string.IsNullOrWhiteSpace(x.FileUrl));

            if (hasResults)
            {
                model.Documents.Add(new DocumentInfoForGUI()
                {
                    DocType = LaboratoryPrintDocumentType.ToolStripSeparator,
                    PreviewAvailable = true,
                    PrintAvailable = true
                });

                model.Documents.Add(new DocumentInfoForGUI()
                {
                    Name = "Сводные результаты",
                    DocType = LaboratoryPrintDocumentType.ConsolidationResults,
                    PreviewAvailable = true,
                    PrintAvailable = true
                });
            }

            if (hasAttachments)
            {
                model.Documents.Add(new DocumentInfoForGUI()
                {
                    DocType = LaboratoryPrintDocumentType.ToolStripSeparator,
                    PreviewAvailable = true,
                    PrintAvailable = true
                });

                int idx = 1;
                foreach (var attachment in details.Attachments.Where(x => x != null && !string.IsNullOrWhiteSpace(x.FileUrl)))
                {
                    model.Documents.Add(new DocumentInfoForGUI()
                    {
                        Name = !string.IsNullOrWhiteSpace(attachment.DisplayName) ? attachment.DisplayName : $"Файл результатов #{idx}.pdf",
                        DocType = LaboratoryPrintDocumentType.ResultsFile,
                        Num = idx - 1,
                        PreviewAvailable = true,
                        PrintAvailable = true
                    });

                    idx++;
                }
            }
        }

        public bool SaveOrderModelForGUIToDetails(Order _Order, OrderModelForGUI _Model)
        {
            LastException = null;
            try
            {
                var details = _Order.OrderDetail as GemotestOrderDetail;
                if (details == null)
                    return true;

                details.Dicts = laboratory.Dicts;

                SavePriceListToDetails(details, _Model);

                details.Products.Clear();

                foreach (var productInfo in _Model.ProductsInfo)
                {
                    details.Products.Add(new GemotestProductDetail()
                    {
                        OrderProductGuid = details.Products.Count.ToString(),
                        ProductId = productInfo.Id,
                        ProductCode = productInfo.Code,
                        ProductName = productInfo.Name
                    });
                }

                if (_Model.ServicesInfo != null)
                {
                    foreach (var serviceInfo in _Model.ServicesInfo)
                    {
                        if (serviceInfo == null)
                            continue;

                        if (IsBiomaterialCollectService(serviceInfo.Id) && !IsCollectBiomaterialByGemotestEnabled())
                        {
                            DebugGemotestGui("SaveOrderModelForGUIToDetails: skip disabled biomaterial collect service: id=" + (serviceInfo.Id ?? "") +
                                "; code=" + (serviceInfo.Code ?? "") + "; name=" + (serviceInfo.Name ?? ""));
                            continue;
                        }

                        details.Products.Add(new GemotestProductDetail()
                        {
                            OrderProductGuid = details.Products.Count.ToString(),
                            ProductId = serviceInfo.Id,
                            ProductCode = serviceInfo.Code,
                            ProductName = serviceInfo.Name
                        });
                    }
                }

                RemoveDisabledBiomaterialCollectServicesFromDetails(details, "SaveOrderModelForGUIToDetails: before rebuild biomaterials");

                details.BioMaterials.Clear();
                details.AddBiomaterialsFromProducts();
                EnsureRequiredSampleBiomaterialsInDetails(details);

                ApplyBiomaterialSelectionFromModel(details, _Model);
                DumpGuiOrderState("SaveOrderModelForGUIToDetails: after ApplyBiomaterialSelectionFromModel", _Order, _Model, details);

                RebuildBiomaterialGroups(details, _Model);

                var oldSamples = details.Samples != null ? new List<GemotestSampleDetail>(details.Samples) : new List<GemotestSampleDetail>();

                if (_Order.State == OrderState.NotSended || _Order.State == OrderState.Prepared)
                {
                    details.Samples.Clear();

                    if (_Model.Samples != null)
                    {
                        foreach (var sampleFromModel in _Model.Samples)
                        {
                            if (sampleFromModel == null || sampleFromModel.Biomaterial == null)
                                continue;

                            var oldSample = oldSamples.FirstOrDefault(x =>
                                x != null &&
                                x.OrderSampleGuid == sampleFromModel.OrderSampleGuid);

                            details.Samples.Add(new GemotestSampleDetail()
                            {
                                OrderSampleGuid = sampleFromModel.OrderSampleGuid,
                                Barcode = oldSample != null ? oldSample.Barcode : sampleFromModel.Barcode,

                                SampleId = oldSample != null ? oldSample.SampleId : string.Empty,
                                SampleIdentifier = oldSample != null ? oldSample.SampleIdentifier : string.Empty,
                                SampleDescription = oldSample != null ? oldSample.SampleDescription : string.Empty,

                                BiomId = sampleFromModel.Biomaterial.BiomaterialId,
                                BiomCode = sampleFromModel.Biomaterial.BiomaterialCode,
                                BiomName = sampleFromModel.Biomaterial.BiomaterialName,

                                ContId = sampleFromModel.Biomaterial.ContainerId,
                                ContCode = sampleFromModel.Biomaterial.ContainerCode,
                                ContName = sampleFromModel.Biomaterial.ContainerName,

                                LocalizationId = oldSample != null ? oldSample.LocalizationId : string.Empty,
                                LocalizationName = oldSample != null ? oldSample.LocalizationName : string.Empty,

                                TransportId = oldSample != null ? oldSample.TransportId : sampleFromModel.Biomaterial.ContainerId,
                                TransportName = oldSample != null ? oldSample.TransportName : sampleFromModel.Biomaterial.ContainerName,

                                LabCenterId = oldSample != null ? oldSample.LabCenterId : string.Empty,

                                OrderProductGuidList = sampleFromModel.OrderProductGuids != null ? new List<string>(sampleFromModel.OrderProductGuids) : new List<string>()


                            });
                        }
                    }
                }

                SaveFieldsToDetails(details, _Model);
                details.DeleteObsoleteDetails();
                details.PriceListCode = details.PriceListCode ?? string.Empty;
                details.PriceListName = details.PriceListName ?? string.Empty;
                details.PriceList = details.PriceList ?? string.Empty;
                DumpGuiOrderState("SaveOrderModelForGUIToDetails: final details", _Order, _Model, details);
                return true;
            }
            catch (Exception exc)
            {
                LastException = exc;
                return false;
            }
        }

        public bool ProcessOrderGUIAction(eOrderAction _Action, Order _Order, ref OrderModelForGUI _OrderModel, ProductInfoForGUI _Product)
        {
            LastException = null;

            try
            {
                if (_Order == null)
                    throw new InvalidOperationException("Заказ не задан.");

                var details = _Order.OrderDetail as GemotestOrderDetail;
                if (details == null)
                    throw new InvalidOperationException("OrderDetail не является GemotestOrderDetail.");

                details.Dicts = laboratory.Dicts;

                if ((_Action == eOrderAction.AddProduct || _Action == eOrderAction.RemoveProduct || _Action == eOrderAction.RemoveService) && _Order.State != OrderState.NotSended)
                {
                    MessageBox.Show(
                        "Из уже подготовленного или отправленного заказа нельзя добавлять или удалять услуги.",
                        "Гемотест",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    return false;
                }

                
                if (_Action == eOrderAction.RemoveProduct)
                {
                    if (_OrderModel == null || _OrderModel.ProductsInfo == null)
                        return false;

                    if (details == null)
                        return false;

                    int productIndex = FindProductIndexForRemove(_OrderModel, _Product);

                    if (productIndex < 0 || productIndex >= _OrderModel.ProductsInfo.Count)
                        throw new IndexOutOfRangeException("Не удалось определить удаляемый продукт в модели заказа.");

                    List<int> deleteIndexes = ResolveProductDeleteIndexesByAutoInsertRules(_OrderModel, productIndex);

                    List<string> collectServiceIdsToDelete = ResolveCollectServiceIdsForRemovedProduct(_OrderModel, productIndex);

                    if (collectServiceIdsToDelete.Count > 0)
                    {
                        string text =
                            "Для удаляемой услуги есть автодобавляемые услуги забора биоматериала:\r\n\r\n" +
                            BuildServiceListText(_OrderModel, collectServiceIdsToDelete) +
                            "\r\n\r\nУдалить их вместе с основной услугой?";

                        DialogResult answer = MessageBox.Show(
                            text,
                            "Удаление услуг забора биоматериала",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);

                        if (answer == DialogResult.Yes)
                            RemoveServicesInfoByServiceIds(_OrderModel, collectServiceIdsToDelete);
                    }

                    if (deleteIndexes == null || deleteIndexes.Count == 0)
                        return false;

                    DeleteProductsFromOrderModel(_OrderModel, details, deleteIndexes);

                    if (!SaveOrderModelForGUIToDetails(_Order, _OrderModel))
                        return false;

                    details.DeleteObsoleteDetails();
                    RebuildBiomaterialGroups(details, _OrderModel);

                    if (!GenerateSamples(_Order, _OrderModel))
                        return false;

                    if (!GenerateFields(_Order, _OrderModel))
                        return false;

                    return true;
                }
                if (_Action == eOrderAction.AddProduct)
                {
                    var products = new List<ProductInfoForGUI>();
                    var groups = new List<ProductGroupInfoForGUI>();

                    foreach (var prod in AllProducts)
                    {
                        if (laboratory.Dicts.Directory != null &&
                            laboratory.Dicts.Directory.TryGetValue(prod.ID, out var svc) &&
                            svc != null)
                        {
                            if (svc.service_type == 3 || svc.service_type == 4)
                                continue;
                        }

                        products.Add(new ProductInfoForGUI()
                        {
                            OrderProductGuid = Guid.NewGuid().ToString(),
                            Id = prod.ID,
                            Code = prod.Code,
                            Name = prod.Name,
                            ProductGroupGuid = null
                        });
                    }

                    PrepareBiomaterialsForChooseForm(products);

                    var form = new FormLaboratoryChooseOfProduct(null, products, groups);
                    if (form.ShowDialog() != DialogResult.OK)
                        return false;

                    var productNew = products.Find(x =>
                        x != null &&
                        string.Equals(x.OrderProductGuid, form.selectedProductGuid, StringComparison.OrdinalIgnoreCase));

                    if (productNew == null)
                        return false;

                    var selectedBioIds = GetSelectedBiomaterialIds(productNew);

                    if (details.Products != null &&
                        details.Products.Any(p => string.Equals(p.ProductId, productNew.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show("Эта услуга уже есть в заказе.", "Добавление услуги", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return false;
                    }

                    int newIndex = _OrderModel.ProductsInfo.Count;
                    productNew.OrderProductGuid = newIndex.ToString();
                    _OrderModel.ProductsInfo.Add(productNew);

                    details.Products.Add(new GemotestOrderDetail.GemotestProductDetail
                    {
                        OrderProductGuid = productNew.OrderProductGuid,
                        ProductId = productNew.Id,
                        ProductCode = productNew.Code,
                        ProductName = productNew.Name
                    });

                    ApplyAutoInsertServices(details, _OrderModel);

                    for (int i = 0; i < _OrderModel.ProductsInfo.Count; i++)
                        _OrderModel.ProductsInfo[i].OrderProductGuid = i.ToString();

                    if (details.Products != null)
                    {
                        for (int i = 0; i < details.Products.Count; i++)
                            details.Products[i].OrderProductGuid = i.ToString();
                    }

                    var savedBiomaterialSelectionState = CaptureBiomaterialSelectionState(details.BioMaterials);
                    details.BioMaterials.Clear();
                    details.AddBiomaterialsFromProducts();
                    RestoreBiomaterialSelectionState(details.BioMaterials, savedBiomaterialSelectionState);
                    EnsureRequiredSampleBiomaterialsInDetails(details);

                    RebuildBiomaterialGroups(details, _OrderModel);
                    ApplySelectedBiomaterialsToAddedProduct(_Order, _OrderModel, newIndex, selectedBioIds);

                    if (!GenerateSamples(_Order, _OrderModel))
                        return false;

                    if (!GenerateFields(_Order, _OrderModel))
                        return false;

                    if (!SaveOrderModelForGUIToDetails(_Order, _OrderModel))
                        return false;

                    return true;
                }

                if (_Action == eOrderAction.PrepareOrderForSend)
                {
                    if (_OrderModel != null &&
                        _OrderModel.PriceLists != null &&
                        _OrderModel.PriceLists.Count > 1 &&
                        (_OrderModel.PriceListSelected == null || string.IsNullOrWhiteSpace(_OrderModel.PriceListSelected.Id)))
                    {
                        MessageBox.Show(
                            "Сначала выберите прайс-лист.",
                            "Гемотест",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

                        return false;
                    }

                    if (!ValidateGUIModel(_OrderModel))
                        return false;

                    if (!SaveOrderModelForGUIToDetails(_Order, _OrderModel))
                        return false;

                    details.DeleteObsoleteDetails();

                    if (string.IsNullOrWhiteSpace(_Order.Number))
                    {
                        if (numerator == null)
                            throw new InvalidOperationException("Не задан numerator для Gemotest.");

                        var orderDetails = _Order.OrderDetail as GemotestOrderDetail;
                        string priceListCode = orderDetails?.PriceListCode ?? "";
                        string priceListNum = orderDetails?.PriceListNum ?? "";

                        int nextNumber = numerator.GetNextNumber("GemotestOrderNum", DateTime.Now, priceListCode, priceListNum, "");
                        long nextNum = nextNumber + long.Parse(priceListNum) - 1;

                        if (nextNum <= 0)
                            throw new Exception("Не удалось получить номер заказа Gemotest через numerator.");

                        _Order.Number = nextNum.ToString();
                    }

                    _Order.State = OrderState.Prepared;
                    return true;
                }

                if (_Action == eOrderAction.CheckOrderState)
                {
                    if (_Order.State == OrderState.Sended)
                    {
                        _Order.State = OrderState.Commited;
                        return true;
                    }
                    return true;
                }

                if (_Action == eOrderAction.CancelOrder)
                {
                    _Order.State = OrderState.Canceled;
                    return true;
                }

                if (_Action == eOrderAction.RemoveService)
                {
                    if (_OrderModel == null || _OrderModel.ServicesInfo == null || _Product == null)
                        return false;

                    int serviceIndex = FindServiceIndexForRemove(_OrderModel, _Product);

                    if (serviceIndex < 0 || serviceIndex >= _OrderModel.ServicesInfo.Count)
                        return false;

                    _OrderModel.ServicesInfo.RemoveAt(serviceIndex);

                    if (!SaveOrderModelForGUIToDetails(_Order, _OrderModel))
                        return false;

                    details.DeleteObsoleteDetails();

                    if (details.BioMaterials == null)
                        details.BioMaterials = new List<GemotestBioMaterial>();

                    var savedBiomaterialSelectionState = CaptureBiomaterialSelectionState(details.BioMaterials);
                    details.BioMaterials.Clear();
                    details.AddBiomaterialsFromProducts();
                    RestoreBiomaterialSelectionState(details.BioMaterials, savedBiomaterialSelectionState);
                    EnsureRequiredSampleBiomaterialsInDetails(details);

                    RebuildBiomaterialGroups(details, _OrderModel);

                    if (!GenerateSamples(_Order, _OrderModel))
                        return false;

                    if (!GenerateFields(_Order, _OrderModel))
                        return false;

                    return true;
                }

                return true;
            }
            catch (Exception exc)
            {
                LastException = exc;
                return false;
            }
        }

        private int FindServiceIndexForRemove(OrderModelForGUI model, ProductInfoForGUI service)
        {
            if (model == null || model.ServicesInfo == null || service == null)
                return -1;

            int serviceIndex = -1;

            if (!string.IsNullOrWhiteSpace(service.OrderProductGuid))
            {
                serviceIndex = model.ServicesInfo.FindIndex(x =>
                    x != null &&
                    string.Equals(x.OrderProductGuid, service.OrderProductGuid, StringComparison.OrdinalIgnoreCase));
            }

            if (serviceIndex < 0)
                serviceIndex = model.ServicesInfo.IndexOf(service);

            if (serviceIndex < 0 && !string.IsNullOrWhiteSpace(service.Id))
            {
                serviceIndex = model.ServicesInfo.FindIndex(x =>
                    x != null &&
                    string.Equals(NormalizeServiceId(x.Id), NormalizeServiceId(service.Id), StringComparison.OrdinalIgnoreCase));
            }

            return serviceIndex;
        }

        private List<string> ResolveCollectServiceIdsForRemovedProduct(OrderModelForGUI model, int productIndex)
        {
            List<string> result = new List<string>();

            if (model == null ||
                model.ProductsInfo == null ||
                model.ServicesInfo == null ||
                productIndex < 0 ||
                productIndex >= model.ProductsInfo.Count)
            {
                return result;
            }

            string parentServiceId = GetProductServiceId(model.ProductsInfo[productIndex]);

            if (string.IsNullOrWhiteSpace(parentServiceId))
                return result;

            List<DictionaryServiceAutoInsert> autoRows;

            if (laboratory == null ||
                laboratory.Dicts == null ||
                laboratory.Dicts.ServiceAutoInsert == null ||
                !laboratory.Dicts.ServiceAutoInsert.TryGetValue(parentServiceId, out autoRows) ||
                autoRows == null)
            {
                return result;
            }

            foreach (DictionaryServiceAutoInsert row in autoRows)
            {
                if (row == null || row.archive != 0)
                    continue;

                string autoServiceId = NormalizeServiceId(row.auto_service_id);

                if (string.IsNullOrWhiteSpace(autoServiceId))
                    continue;

                DictionaryService service;
                if (!TryGetDictionaryService(autoServiceId, out service))
                    continue;

                if (service.service_type != 4)
                    continue;

                if (!ServiceInfoExists(model, autoServiceId))
                    continue;

                if (IsAutoServiceRequiredByOtherProducts(model, autoServiceId, parentServiceId))
                    continue;

                if (!result.Contains(autoServiceId, StringComparer.OrdinalIgnoreCase))
                    result.Add(autoServiceId);
            }

            return result;
        }

        private bool ServiceInfoExists(OrderModelForGUI model, string serviceId)
        {
            serviceId = NormalizeServiceId(serviceId);

            if (string.IsNullOrWhiteSpace(serviceId) || model == null || model.ServicesInfo == null)
                return false;

            return model.ServicesInfo.Any(x =>
                x != null &&
                string.Equals(NormalizeServiceId(x.Id), serviceId, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsAutoServiceRequiredByOtherProducts(
            OrderModelForGUI model,
            string autoServiceId,
            string removedParentServiceId)
        {
            autoServiceId = NormalizeServiceId(autoServiceId);
            removedParentServiceId = NormalizeServiceId(removedParentServiceId);

            if (string.IsNullOrWhiteSpace(autoServiceId) ||
                model == null ||
                model.ProductsInfo == null)
            {
                return false;
            }

            foreach (ProductInfoForGUI product in model.ProductsInfo)
            {
                if (product == null)
                    continue;

                string parentServiceId = GetProductServiceId(product);

                if (string.IsNullOrWhiteSpace(parentServiceId))
                    continue;

                if (string.Equals(parentServiceId, removedParentServiceId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (HasAutoInsert(parentServiceId, autoServiceId))
                    return true;
            }

            return false;
        }

        private void RemoveServicesInfoByServiceIds(OrderModelForGUI model, IEnumerable<string> serviceIds)
        {
            if (model == null || model.ServicesInfo == null || serviceIds == null)
                return;

            HashSet<string> ids = new HashSet<string>(
                serviceIds
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeServiceId),
                StringComparer.OrdinalIgnoreCase);

            if (ids.Count == 0)
                return;

            model.ServicesInfo.RemoveAll(x =>
                x != null &&
                ids.Contains(NormalizeServiceId(x.Id)));
        }

        private int FindProductIndexForRemove(OrderModelForGUI model, ProductInfoForGUI product)
        {
            if (model == null || model.ProductsInfo == null || product == null)
                return -1;

            int productIndex = -1;

            if (!string.IsNullOrWhiteSpace(product.OrderProductGuid))
            {
                productIndex = model.ProductsInfo.FindIndex(x => x != null && string.Equals(x.OrderProductGuid, product.OrderProductGuid, StringComparison.OrdinalIgnoreCase));
            }

            if (productIndex < 0)
                productIndex = model.ProductsInfo.IndexOf(product);

            if (productIndex < 0 && !string.IsNullOrWhiteSpace(product.Id))
            {
                productIndex = model.ProductsInfo.FindIndex(x => x != null && string.Equals(NormalizeServiceId(x.Id), NormalizeServiceId(product.Id), StringComparison.OrdinalIgnoreCase));
            }

            return productIndex;
        }

        private List<int> ResolveProductDeleteIndexesByAutoInsertRules(OrderModelForGUI model, int selectedIndex)
        {
            List<int> resultIndexes;
            HashSet<string> deleteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (model == null || model.ProductsInfo == null || selectedIndex < 0 || selectedIndex >= model.ProductsInfo.Count)
                return new List<int>();

            string selectedServiceId = GetProductServiceId(model.ProductsInfo[selectedIndex]);

            if (!string.IsNullOrWhiteSpace(selectedServiceId))
                deleteIds.Add(selectedServiceId);

            List<string> parentIds = GetAutoInsertParentServiceIds(model, selectedServiceId, deleteIds);

            if (parentIds.Count > 0)
            {
                string text =
                    "Удаляемая услуга является автодобавляемой для основной услуги:\r\n\r\n" +
                    BuildServiceListText(model, parentIds) +
                    "\r\n\r\nУдалить основную услугу вместе с ней?";

                if (parentIds.Count > 1)
                {
                    text =
                        "Удаляемая услуга является автодобавляемой для нескольких основных услуг:\r\n\r\n" +
                        BuildServiceListText(model, parentIds) +
                        "\r\n\r\nУдалить эти основные услуги вместе с ней?";
                }

                DialogResult answer = MessageBox.Show(text, "Удаление автодобавляемой услуги", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (answer == DialogResult.Yes)
                {
                    foreach (string parentId in parentIds)
                    {
                        if (!string.IsNullOrWhiteSpace(parentId))
                            deleteIds.Add(parentId);
                    }
                }
            }

            List<string> orphanAutoServiceIds = CollectOrphanAutoServiceIdsAfterDelete(model, deleteIds);

            if (orphanAutoServiceIds.Count > 0)
            {
                string text =
                    "После удаления выбранной услуги в заказе останутся автодобавляемые услуги, " +
                    "которые больше не требуются другим услугам:\r\n\r\n" +
                    BuildServiceListText(model, orphanAutoServiceIds) +
                    "\r\n\r\nУдалить их вместе с выбранной услугой?";

                DialogResult answer = MessageBox.Show(text, "Удаление связанных автодобавляемых услуг", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (answer == DialogResult.Yes)
                {
                    foreach (string autoServiceId in orphanAutoServiceIds)
                    {
                        if (!string.IsNullOrWhiteSpace(autoServiceId))
                            deleteIds.Add(autoServiceId);
                    }
                }
            }

            resultIndexes = GetProductIndexesByServiceIds(model, deleteIds);

            if (resultIndexes.Count == 0)
                resultIndexes.Add(selectedIndex);

            return resultIndexes;
        }

        private List<string> GetAutoInsertParentServiceIds( OrderModelForGUI model, string autoServiceId, HashSet<string> alreadyDeletedIds)
        {
            List<string> result = new List<string>();

            if (model == null || model.ProductsInfo == null)
                return result;

            autoServiceId = NormalizeServiceId(autoServiceId);

            if (string.IsNullOrWhiteSpace(autoServiceId))
                return result;

            foreach (ProductInfoForGUI product in model.ProductsInfo)
            {
                if (product == null)
                    continue;

                string parentServiceId = GetProductServiceId(product);

                if (string.IsNullOrWhiteSpace(parentServiceId))
                    continue;

                if (alreadyDeletedIds != null && alreadyDeletedIds.Contains(parentServiceId))
                    continue;

                if (string.Equals(parentServiceId, autoServiceId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!HasAutoInsert(parentServiceId, autoServiceId))
                    continue;

                if (!result.Contains(parentServiceId, StringComparer.OrdinalIgnoreCase))
                    result.Add(parentServiceId);
            }

            return result;
        }

        private List<string> CollectOrphanAutoServiceIdsAfterDelete( OrderModelForGUI model, HashSet<string> initialDeleteIds)
        {
            List<string> result = new List<string>();
            if (model == null || model.ProductsInfo == null || laboratory == null || laboratory.Dicts == null || laboratory.Dicts.ServiceAutoInsert == null)
                return result;
            HashSet<string> simulatedDeleteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (initialDeleteIds != null)
            {
                foreach (string id in initialDeleteIds)
                {
                    string normalizedId = NormalizeServiceId(id);

                    if (!string.IsNullOrWhiteSpace(normalizedId))
                        simulatedDeleteIds.Add(normalizedId);
                }
            }

            bool changed;

            do
            {
                changed = false;

                List<string> deletedParentIds = simulatedDeleteIds.ToList();

                foreach (string parentServiceId in deletedParentIds)
                {
                    List<DictionaryServiceAutoInsert> autoRows;

                    if (!laboratory.Dicts.ServiceAutoInsert.TryGetValue(parentServiceId, out autoRows) || autoRows == null)
                        continue;

                    foreach (DictionaryServiceAutoInsert row in autoRows)
                    {
                        if (row == null)
                            continue;

                        string autoServiceId = NormalizeServiceId(row.auto_service_id);

                        if (string.IsNullOrWhiteSpace(autoServiceId))
                            continue;

                        if (simulatedDeleteIds.Contains(autoServiceId))
                            continue;

                        if (!ProductServiceExistsInOrder(model, autoServiceId))
                            continue;

                        if (IsAutoServiceRequiredByRemainingServices(model, autoServiceId, simulatedDeleteIds))
                            continue;

                        simulatedDeleteIds.Add(autoServiceId);

                        if (!result.Contains(autoServiceId, StringComparer.OrdinalIgnoreCase))
                            result.Add(autoServiceId);

                        changed = true;
                    }
                }
            }
            while (changed);

            return result;
        }

        private bool IsAutoServiceRequiredByRemainingServices( OrderModelForGUI model, string autoServiceId, HashSet<string> deleteIds)
        {
            if (model == null || model.ProductsInfo == null)
                return false;

            autoServiceId = NormalizeServiceId(autoServiceId);

            if (string.IsNullOrWhiteSpace(autoServiceId))
                return false;

            foreach (ProductInfoForGUI product in model.ProductsInfo)
            {
                if (product == null)
                    continue;

                string parentServiceId = GetProductServiceId(product);

                if (string.IsNullOrWhiteSpace(parentServiceId))
                    continue;

                if (deleteIds != null && deleteIds.Contains(parentServiceId))
                    continue;

                if (HasAutoInsert(parentServiceId, autoServiceId))
                    return true;
            }

            return false;
        }

        private bool HasAutoInsert(string parentServiceId, string autoServiceId)
        {
            parentServiceId = NormalizeServiceId(parentServiceId);
            autoServiceId = NormalizeServiceId(autoServiceId);

            if (string.IsNullOrWhiteSpace(parentServiceId) || string.IsNullOrWhiteSpace(autoServiceId))
                return false;
            if (laboratory == null || laboratory.Dicts == null || laboratory.Dicts.ServiceAutoInsert == null)
                return false;
            List<DictionaryServiceAutoInsert> autoRows;

            if (!laboratory.Dicts.ServiceAutoInsert.TryGetValue(parentServiceId, out autoRows) || autoRows == null)
                return false;

            foreach (DictionaryServiceAutoInsert row in autoRows)
            {
                if (row == null)
                    continue;

                string rowAutoServiceId = NormalizeServiceId(row.auto_service_id);

                if (string.Equals(rowAutoServiceId, autoServiceId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private bool ProductServiceExistsInOrder(OrderModelForGUI model, string serviceId)
        {
            if (model == null || model.ProductsInfo == null)
                return false;

            serviceId = NormalizeServiceId(serviceId);

            if (string.IsNullOrWhiteSpace(serviceId))
                return false;

            return model.ProductsInfo.Any(x =>
                x != null &&
                string.Equals(GetProductServiceId(x), serviceId, StringComparison.OrdinalIgnoreCase));
        }

        private List<int> GetProductIndexesByServiceIds(OrderModelForGUI model, HashSet<string> serviceIds)
        {
            List<int> indexes = new List<int>();

            if (model == null || model.ProductsInfo == null || serviceIds == null || serviceIds.Count == 0)
                return indexes;

            for (int i = 0; i < model.ProductsInfo.Count; i++)
            {
                ProductInfoForGUI product = model.ProductsInfo[i];

                if (product == null)
                    continue;

                string serviceId = GetProductServiceId(product);

                if (serviceIds.Contains(serviceId))
                    indexes.Add(i);
            }

            return indexes.Distinct().OrderBy(x => x).ToList();
        }

        private void DeleteProductsFromOrderModel( OrderModelForGUI model, GemotestOrderDetail details, List<int> deleteIndexes)
        {
            if (model == null || model.ProductsInfo == null || deleteIndexes == null || deleteIndexes.Count == 0)
                return;

            List<int> indexes = deleteIndexes
                .Where(x => x >= 0 && x < model.ProductsInfo.Count)
                .Distinct()
                .OrderByDescending(x => x)
                .ToList();

            HashSet<string> removedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (int index in indexes)
            {
                string removedGuid = model.ProductsInfo[index].OrderProductGuid ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(removedGuid))
                    removedGuids.Add(removedGuid);
            }

            if (details != null && details.Products != null)
            {
                foreach (int index in indexes)
                {
                    if (index >= 0 && index < details.Products.Count)
                        details.DeleteProduct(index);
                }
            }

            foreach (int index in indexes)
            {
                model.ProductsInfo.RemoveAt(index);
            }

            ReindexProductGuidsAndLinkedObjects(model, removedGuids);
        }

        private void ReindexProductGuidsAndLinkedObjects( OrderModelForGUI model, HashSet<string> removedGuids)
        {
            if (model == null || model.ProductsInfo == null)
                return;

            Dictionary<string, string> guidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < model.ProductsInfo.Count; i++)
            {
                ProductInfoForGUI product = model.ProductsInfo[i];

                if (product == null)
                    continue;

                string oldGuid = product.OrderProductGuid ?? string.Empty;
                string newGuid = i.ToString();

                if (!string.IsNullOrWhiteSpace(oldGuid) && !guidMap.ContainsKey(oldGuid))
                    guidMap.Add(oldGuid, newGuid);

                product.OrderProductGuid = newGuid;
            }

            if (model.Fields != null)
            {
                for (int i = model.Fields.Count - 1; i >= 0; i--)
                {
                    FieldInfoForGUI field = model.Fields[i];

                    if (field == null)
                    {
                        model.Fields.RemoveAt(i);
                        continue;
                    }

                    field.OrderProductGuidList = RebuildGuidList(field.OrderProductGuidList, guidMap, removedGuids);

                    if (field.OrderProductGuidList.Count == 0)
                        model.Fields.RemoveAt(i);
                }
            }

            if (model.Samples != null)
            {
                for (int i = model.Samples.Count - 1; i >= 0; i--)
                {
                    SampleInfoForGUI sample = model.Samples[i];

                    if (sample == null)
                    {
                        model.Samples.RemoveAt(i);
                        continue;
                    }

                    sample.OrderProductGuids = RebuildGuidList(sample.OrderProductGuids, guidMap, removedGuids);

                    if (sample.OrderProductGuids.Count == 0)
                        model.Samples.RemoveAt(i);
                }
            }
        }

        private List<string> RebuildGuidList( List<string> oldGuids, Dictionary<string, string> guidMap, HashSet<string> removedGuids)
        {
            List<string> result = new List<string>();

            if (oldGuids == null)
                return result;

            foreach (string oldGuidRaw in oldGuids)
            {
                string oldGuid = oldGuidRaw ?? string.Empty;

                if (string.IsNullOrWhiteSpace(oldGuid))
                    continue;

                if (removedGuids != null && removedGuids.Contains(oldGuid))
                    continue;

                string newGuid;

                if (guidMap != null && guidMap.TryGetValue(oldGuid, out newGuid))
                {
                    if (!result.Contains(newGuid))
                        result.Add(newGuid);
                }
                else
                {
                    if (!result.Contains(oldGuid))
                        result.Add(oldGuid);
                }
            }

            return result;
        }

        private string GetProductServiceId(ProductInfoForGUI product)
        {
            if (product == null)
                return string.Empty;

            return NormalizeServiceId(product.Id);
        }

        private string NormalizeServiceId(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private string BuildServiceListText(OrderModelForGUI model, IEnumerable<string> serviceIds)
        {
            if (serviceIds == null)
                return string.Empty;

            StringBuilder sb = new StringBuilder();

            foreach (string serviceIdRaw in serviceIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string serviceId = NormalizeServiceId(serviceIdRaw);

                if (string.IsNullOrWhiteSpace(serviceId))
                    continue;

                sb.AppendLine("• " + GetServiceCaption(model, serviceId));
            }

            return sb.ToString().TrimEnd();
        }

        private string GetServiceCaption(OrderModelForGUI model, string serviceId)
        {
            serviceId = NormalizeServiceId(serviceId);

            if (model != null && model.ProductsInfo != null)
            {
                ProductInfoForGUI product = model.ProductsInfo.FirstOrDefault(x =>
                    x != null &&
                    string.Equals(GetProductServiceId(x), serviceId, StringComparison.OrdinalIgnoreCase));

                if (product != null)
                {
                    if (!string.IsNullOrWhiteSpace(product.Code) && !string.IsNullOrWhiteSpace(product.Name))
                        return product.Code + " | " + product.Name;

                    if (!string.IsNullOrWhiteSpace(product.Name))
                        return product.Name;

                    if (!string.IsNullOrWhiteSpace(product.Code))
                        return product.Code;
                }
            }
            if (laboratory != null && laboratory.Dicts != null && laboratory.Dicts.Directory != null)
            {
                DictionaryService service;

                if (laboratory.Dicts.Directory.TryGetValue(serviceId, out service) && service != null)
                {
                    if (!string.IsNullOrWhiteSpace(service.code) && !string.IsNullOrWhiteSpace(service.name))
                        return service.code + " | " + service.name;

                    if (!string.IsNullOrWhiteSpace(service.name))
                        return service.name;

                    if (!string.IsNullOrWhiteSpace(service.code))
                        return service.code;
                }
            }

            return serviceId;
        }

        public bool PrintStikers(Order _Order, List<SampleInfoForGUI> _SelectedSamples)
        {
            LastException = null;
            try
            {
                Encoding encoding = Encoding.UTF8;
                if (localOptions.LabelEncoding == LabelEncoding.UTF8)
                    encoding = Encoding.UTF8;
                else if (localOptions.LabelEncoding == LabelEncoding.Code866)
                    encoding = Encoding.GetEncoding(866);
                else if (localOptions.LabelEncoding == LabelEncoding.Windows1251)
                    encoding = Encoding.GetEncoding("windows-1251");
                PrinterSettings settings;
                if (localOptions != null && localOptions.StickerPrinterSettings != null)
                    settings = localOptions.StickerPrinterSettings;
                else
                    settings = Print.GetDefaultPrinterSettingsAccorgingToFormat();

                string labelTemplate = "";
                if (localOptions.LabelType == LabelType.ZPL)
                    labelTemplate = LocalOptions.GetDefaultLabelTemplate(LabelType.ZPL);
                else if (localOptions.LabelType == LabelType.EPL)
                    labelTemplate = LocalOptions.GetDefaultLabelTemplate(LabelType.EPL);
                else if (localOptions.LabelType == LabelType.Custom)
                    labelTemplate = localOptions.CustomLabelTemplate;

                GemotestOrderDetail details = (GemotestOrderDetail)_Order.OrderDetail;
                var detailSamples = details != null ? details.Samples : new List<GemotestSampleDetail>();

                foreach (var sample in _SelectedSamples)
                {
                    if (sample == null || string.IsNullOrWhiteSpace(sample.Barcode))
                        continue;

                    var fullSample = detailSamples.FirstOrDefault(x =>
                        x != null &&
                        (
                            (!string.IsNullOrWhiteSpace(x.OrderSampleGuid) && x.OrderSampleGuid == sample.OrderSampleGuid) ||
                            (!string.IsNullOrWhiteSpace(x.Barcode) && x.Barcode == sample.Barcode)
                        ));

                    if (fullSample == null)
                        continue;

                    string contName = fullSample.ContName;
                    if (!string.IsNullOrEmpty(contName) && contName.Length > 10)
                        contName = contName.Substring(0, 10);
                    string str_to_print = string.Format(labelTemplate, fullSample.Barcode, _Order.Patient.Surname, $"{_Order.Patient.Name} {_Order.Patient.Patronimic}", _Order.Date.ToString("dd.MM.yyyy"), fullSample.BiomName, contName, $"{globalOptions.Contractor_Code}.{globalOptions.Contractor}");
                    Print.SendStringToPrinter(settings, str_to_print, encoding);
                }
                return true;
            }
            catch (Exception exc)
            {
                LastException = exc;
                return false;
            }
        }

        public void ShowOrderDetail(Order _Order)
        {
            try
            {
                var details = _Order != null ? _Order.OrderDetail as GemotestOrderDetail : null;
                if (details == null)
                    throw new InvalidOperationException("OrderDetail не является GemotestOrderDetail.");

                string detailsString = details.Pack();
                FormLaboratoryOrderDetails form = new FormLaboratoryOrderDetails(ref detailsString, "Подробности заказа Гемотест");
                form.ShowDialog();
            }
            catch (Exception exc)
            {
                LastException = exc;
            }
        }

        private byte[] DownloadAttachmentBytes(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("Ссылка на вложение пуста.");

            using (var wc = new System.Net.WebClient())
            {
                return wc.DownloadData(url);
            }
        }

        public bool PrintLaboratoryDocument(Order _Order, ref ResultsCollection _Results, DocumentInfoForGUI _Document, bool _Preview)
        {
            LastException = null;

            try
            {
                if (_Order == null)
                    throw new ArgumentNullException(nameof(_Order));

                if (_Document == null)
                    throw new ArgumentNullException(nameof(_Document));
                if (_Document.DocType == LaboratoryPrintDocumentType.ResultsFile)
                {
                    var details = _Order.OrderDetail as GemotestOrderDetail;
                    if (details == null)
                        throw new InvalidOperationException("OrderDetail не является GemotestOrderDetail.");

                    if (!_Document.Num.HasValue)
                        throw new InvalidOperationException("Не задан индекс файла результатов.");

                    int index = _Document.Num.Value;
                    if (details.Attachments == null || index < 0 || index >= details.Attachments.Count)
                        throw new InvalidOperationException("Файл результатов не найден в заказе.");

                    var attachment = details.Attachments[index];
                    if (attachment == null || string.IsNullOrWhiteSpace(attachment.FileUrl))
                        throw new InvalidOperationException("Пустая ссылка на файл результатов.");

                    byte[] fileBytes = DownloadAttachmentBytes(attachment.FileUrl);

                    if (_Preview)
                    {
                        Print.OpenPdf(fileBytes);
                    }
                    else
                    {
                        PrinterSettings settings;
                        if (localOptions != null && localOptions.PdfPrinterSettings != null)
                            settings = localOptions.PdfPrinterSettings;
                        else
                            settings = Print.GetDefaultPrinterSettingsAccorgingToFormat();

                        Print.PrintPdf(settings, fileBytes);
                    }

                    return true;
                }

                if (_Document.DocType == LaboratoryPrintDocumentType.Stikers)
                {
                    if (_Order.State == OrderState.NotSended)
                    {
                        MessageBox.Show("Сначала необходимо подготовить или отправить заказ, чтобы получить штрихкоды.", "Гемотест", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return false;
                    }

                    var samplesForGui = BuildStickerSamplesForGui(_Order);
                    if (samplesForGui == null || samplesForGui.Count == 0)
                    {
                        MessageBox.Show("Для заказа нет образцов для печати наклеек.", "Гемотест", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return false;
                    }

                    var form = new FormLaboratoryPrintStickers(this, _Order, samplesForGui);
                    form.ShowDialog();
                    return true;
                }
                if (_Document.DocType == LaboratoryPrintDocumentType.ConsolidationResults)
                {
                    if (_Order.State != OrderState.FullResultReceived &&
                        _Order.State != OrderState.PartialResultReceived)
                    {
                        MessageBox.Show("Сначала необходимо получить результаты");
                        return false;
                    }

                    var details = _Order.OrderDetail as GemotestOrderDetail;
                    if (details == null)
                        throw new InvalidOperationException("OrderDetail не является GemotestOrderDetail.");

                    if (details.Results == null || details.Results.Count == 0)
                    {
                        MessageBox.Show("В заказе нет сохранённых результатов для сводного отчёта.");
                        return false;
                    }

                    PrinterSettings settings;
                    if (localOptions != null && localOptions.PdfPrinterSettings != null)
                        settings = localOptions.PdfPrinterSettings;
                    else
                        settings = Print.GetDefaultPrinterSettingsAccorgingToFormat();

                    List<Print.RDLReportParameter> parameters = new List<Print.RDLReportParameter>();

                    DataSetForReportGemotest dataset = FillDataSetForConsolidatedReport(_Order);
                    List<Print.RDLReportDataSet> datasets = new List<Print.RDLReportDataSet>();

                    BindingSource bs = new BindingSource();
                    bs.DataSource = new DataView(dataset.ConsolidatedReportParameters);
                    datasets.Add(new Print.RDLReportDataSet("dtsParameters", bs));

                    bs = new BindingSource();
                    bs.DataSource = new DataView(dataset.ConsolidatedReportHeader);
                    datasets.Add(new Print.RDLReportDataSet("dtsHeader", bs));

                    Print.PrintRDLCReport(settings, "LaboratoryConsolidatedReport.rdlc", parameters, datasets, !_Preview);
                    return true;
                }
                if (_Document.DocType == LaboratoryPrintDocumentType.Blank)
                {
                    if (_Order.State == OrderState.NotSended)
                    {
                        MessageBox.Show("Сначала необходимо подготовить заказ к отправке в лабораторию");
                        return false;
                    }

                    PrinterSettings settings;
                    if (localOptions != null && localOptions.PdfPrinterSettings != null)
                        settings = localOptions.PdfPrinterSettings;
                    else
                        settings = Print.GetDefaultPrinterSettingsAccorgingToFormat();

                    var parameters = new List<Print.RDLReportParameter>();

                    GemotestBlankReportDataSetV2 dataset = FillDataSetForBlankReport(_Order);

                    var datasets = new List<Print.RDLReportDataSet>();

                    BindingSource bs = new BindingSource();
                    bs.DataSource = new DataView(dataset.BlankTable);
                    datasets.Add(new Print.RDLReportDataSet("Blank", bs));

                    bs = new BindingSource();
                    bs.DataSource = new DataView(dataset.Products);
                    datasets.Add(new Print.RDLReportDataSet("Products", bs));

                    bs = new BindingSource();
                    bs.DataSource = new DataView(dataset.PatientParameters);
                    datasets.Add(new Print.RDLReportDataSet("PatientParams", bs));

                    Print.PrintRDLCReport(settings, "LaboratoryGemotestBlankReport.rdlc", parameters, datasets, !_Preview);
                    return true;
                }
                return true;
            }
            catch (Exception exc)
            {
                LastException = exc;
                return false;
            }
        }

        private void FillPriceListsForModel(bool readOnly, GemotestOrderDetail details, OrderModelForGUI model)
        {
            model.PriceLists.Clear();
            model.PriceListSelected = null;

            if (details == null)
                return;

            if (readOnly)
            {
                string roName = !string.IsNullOrWhiteSpace(details.PriceListName)
                    ? details.PriceListName
                    : (details.PriceList ?? string.Empty);

                string roId = !string.IsNullOrWhiteSpace(details.PriceListCode)
                    ? details.PriceListCode
                    : roName;
                string roNum = !string.IsNullOrWhiteSpace(details.PriceListNum)
                    ? details.PriceListNum
                    : "1";

                if (!string.IsNullOrWhiteSpace(roName) || !string.IsNullOrWhiteSpace(roId))
                {
                    model.PriceLists.Add(new PriceListForGUI()
                    {
                        Id = roId ?? string.Empty,
                        Name = roName ?? string.Empty,
                    });
                    model.PriceListSelected = model.PriceLists[0];
                }

                return;
            }

            if (globalOptions != null && globalOptions.PriceLists != null && globalOptions.PriceLists.Count > 0)
            {
                for (int i = 0; i < globalOptions.PriceLists.Count; i++)
                {
                    var pl = globalOptions.PriceLists[i];
                    if (pl == null) continue;

                    model.PriceLists.Add(new PriceListForGUI()
                    {
                        Id = i.ToString(),
                        Name = pl.Name ?? string.Empty
                    });
                }

                if (!string.IsNullOrWhiteSpace(details.PriceListCode))
                {
                    for (int i = 0; i < globalOptions.PriceLists.Count; i++)
                    {
                        var pl = globalOptions.PriceLists[i];
                        if (pl == null) continue;

                        if (string.Equals((pl.ContractorCode ?? string.Empty).Trim(), details.PriceListCode.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            model.PriceListSelected = model.PriceLists.FirstOrDefault(x => x.Id == i.ToString());
                            break;
                        }
                    }
                }
                else if (globalOptions.PriceLists.Count == 1)
                {
                    model.PriceListSelected = model.PriceLists.FirstOrDefault(x => x.Id == "0");
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(globalOptions?.Contractor_Code))
            {
                model.PriceLists.Add(new PriceListForGUI()
                {
                    Id = globalOptions.Contractor_Code ?? string.Empty,
                    Name = globalOptions.Contractor ?? string.Empty
                });
                model.PriceListSelected = model.PriceLists[0];
            }
        }

        private void SavePriceListToDetails(GemotestOrderDetail details, OrderModelForGUI model)
        {
            if (details == null)
                return;

            details.PriceList = string.Empty;
            details.PriceListName = string.Empty;
            details.PriceListCode = string.Empty;
            details.PriceListNum = string.Empty;

            var selected = model?.PriceListSelected;
            if (selected == null)
                return;

            if (string.IsNullOrWhiteSpace(selected.Id))
                return;

            int idx;
            if (int.TryParse(selected.Id, out idx) &&
                globalOptions != null &&
                globalOptions.PriceLists != null &&
                idx >= 0 && idx < globalOptions.PriceLists.Count)
            {
                var pl = globalOptions.PriceLists[idx];
                if (pl != null)
                {
                    details.PriceList = pl.Name ?? string.Empty;
                    details.PriceListName = pl.Name ?? string.Empty;
                    details.PriceListCode = pl.ContractorCode ?? string.Empty;
                    details.PriceListNum = pl.Num ?? string.Empty;
                    return;
                }
            }

            var byName = globalOptions?.PriceLists?.FirstOrDefault(x =>
                x != null &&
                string.Equals((x.Name ?? string.Empty).Trim(), (selected.Name ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));

            if (byName != null)
            {
                details.PriceList = byName.Name ?? string.Empty;
                details.PriceListName = byName.Name ?? string.Empty;
                details.PriceListCode = byName.ContractorCode ?? string.Empty;
                details.PriceListNum = byName.Num ?? string.Empty;
                return;
            }

            details.PriceList = selected.Name ?? string.Empty;
            details.PriceListName = selected.Name ?? string.Empty;
            details.PriceListCode = selected.Id ?? string.Empty;
        }

        private string GetDetailKey(GemotestDetail d)
        {
            if (d == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(d.Code))
                return d.Code;

            return !string.IsNullOrWhiteSpace(d.Name) ? d.Name : string.Empty;
        }

        public List<SampleInfoForGUI> BuildStickerSamplesForGui(Order _Order)
        {
            var result = new List<SampleInfoForGUI>();

            var details = _Order?.OrderDetail as GemotestOrderDetail;
            if (details == null || details.Samples == null)
                return result;

            foreach (var sample in details.Samples)
            {
                if (sample == null)
                    continue;

                result.Add(new SampleInfoForGUI()
                {
                    OrderSampleGuid = sample.OrderSampleGuid,
                    Barcode = sample.Barcode ?? string.Empty,
                    Biomaterial = new BiomaterialInfoForGUI()
                    {
                        BiomaterialId = sample.BiomId ?? string.Empty,
                        BiomaterialCode = sample.BiomCode ?? string.Empty,
                        BiomaterialName = sample.BiomName ?? string.Empty,

                        ContainerId = sample.ContId ?? string.Empty,
                        ContainerCode = sample.ContCode ?? string.Empty,
                        ContainerName = sample.ContName ?? string.Empty
                    },
                    OrderProductGuids = sample.OrderProductGuidList != null
                        ? new List<string>(sample.OrderProductGuidList)
                        : new List<string>()
                });
            }

            return result;
        }

        private void SaveFieldsToDetails(GemotestOrderDetail details, OrderModelForGUI model)
        {
            details.Details.Clear();

            if (model?.Fields == null)
                return;

            foreach (var field in model.Fields)
            {
                if (field == null)
                    continue;

                var d = new GemotestDetail
                {
                    Code = field.Id,
                    Name = field.Description,
                    Value = field.Value,
                    DisplayValue = string.IsNullOrWhiteSpace(field.DisplayValue) ? field.Value : field.DisplayValue,
                    regex = field.Regex,
                    isStdField = false
                };

                if (field.OrderProductGuidList != null)
                {
                    foreach (var guid in field.OrderProductGuidList)
                    {
                        int idx = details.Products.FindIndex(p => p.OrderProductGuid == guid);
                        if (idx < 0)
                            continue;

                        if (IsInternalGemotestField(field.Id))
                            continue;

                        if (field.Mandatory)
                        {
                            if (!d.MandatoryProducts.Contains(idx))
                                d.MandatoryProducts.Add(idx);
                        }
                        else
                        {
                            if (!d.OptionalProducts.Contains(idx))
                                d.OptionalProducts.Add(idx);
                        }
                    }
                }

                details.Details.Add(d);
            }
        }

        private void ApplyBiomaterialSelectionFromModel(GemotestOrderDetail details, OrderModelForGUI model)
        {
            if (details == null || model == null || model.ProductsInfo == null || details.BioMaterials == null)
                return;

            for (int productIndex = 0; productIndex < model.ProductsInfo.Count; productIndex++)
            {
                var product = model.ProductsInfo[productIndex];

                var selectedIds = new HashSet<string>(
                    (product?.BiomaterialGroups ?? new List<BiomaterialGroupForGUI>())
                        .Where(g => g != null && g.BiomaterialsSelected != null)
                        .SelectMany(g => g.BiomaterialsSelected)
                        .Where(x => x != null && !string.IsNullOrWhiteSpace(x.BiomaterialId))
                        .Select(x => x.BiomaterialId),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var biom in details.BioMaterials.Where(b =>
                             b != null &&
                             (b.Mandatory.Contains(productIndex) ||
                              b.Chosen.Contains(productIndex) ||
                              b.Another.Contains(productIndex))))
                {
                    biom.Chosen.Remove(productIndex);
                    biom.Another.Remove(productIndex);

                    if (selectedIds.Contains(biom.Id))
                        biom.Chosen.Add(productIndex);
                    else
                        biom.Another.Add(productIndex);
                }
            }
        }

        private void AddSupplementalFieldIfNotExists(List<FieldInfoForGUI> fields, string id, string description, string orderProductGuid, bool mandatory,
            FieldDataType fieldType, string rawDictionaryValues)
        {
            var field = fields.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (field == null)
            {
                field = new FieldInfoForGUI()
                {
                    Id = id,
                    Description = description,
                    Mandatory = mandatory,
                    FieldDataType = fieldType
                };

                if (fieldType == FieldDataType.Dictionary)
                    field.DictionaryValues = BuildDictionaryValues(GetSupplementalBaseIdFromFieldId(id), rawDictionaryValues);

                fields.Add(field);
            }
            else
            {
                if (mandatory)
                    field.Mandatory = true;

                if (field.FieldDataType != FieldDataType.Dictionary && fieldType == FieldDataType.Dictionary)
                    field.FieldDataType = FieldDataType.Dictionary;

                if (field.FieldDataType == FieldDataType.Dictionary)
                    MergeDictionaryValues(field, rawDictionaryValues);
            }

            if (!string.IsNullOrWhiteSpace(orderProductGuid) && !field.OrderProductGuidList.Contains(orderProductGuid))
                field.OrderProductGuidList.Add(orderProductGuid);
        }

        private List<FieldDictionaryValue> BuildDictionaryValues(string fieldId, string rawDictionaryValues)
        {
            var result = new List<FieldDictionaryValue>();
            if (string.IsNullOrWhiteSpace(rawDictionaryValues))
                return result;

            foreach (var item in rawDictionaryValues
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string display = item;

                if (string.Equals(fieldId, "Contingent", StringComparison.OrdinalIgnoreCase) &&
                    laboratory?.Dicts?.Contingents != null &&
                    laboratory.Dicts.Contingents.TryGetValue(item, out var mapped) &&
                    !string.IsNullOrWhiteSpace(mapped))
                {
                    display = mapped;
                }

                result.Add(new FieldDictionaryValue()
                {
                    Value = item,
                    DisplayText = display
                });
            }

            return result;
        }

        private void MergeDictionaryValues(FieldInfoForGUI field, string rawDictionaryValues)
        {
            if (field == null)
                return;

            field.DictionaryValues = field.DictionaryValues ?? new List<FieldDictionaryValue>();

            foreach (var item in BuildDictionaryValues(GetSupplementalBaseIdFromFieldId(field.Id), rawDictionaryValues))
            {
                if (!field.DictionaryValues.Any(x => string.Equals(x.Value, item.Value, StringComparison.OrdinalIgnoreCase)))
                    field.DictionaryValues.Add(item);
            }
        }

        private static string BuildBiomaterialDisplayName(string biomaterialName, string containerName)
        {
            biomaterialName = (biomaterialName ?? string.Empty).Trim();
            containerName = (containerName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(containerName) || containerName == "не указан")
                return biomaterialName;

            if (biomaterialName.IndexOf(containerName, StringComparison.OrdinalIgnoreCase) >= 0)
                return biomaterialName;

            return $"{biomaterialName} ({containerName})";
        }

        private static string NormalizeContainerName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "-")
                return "не указан";

            return raw.Trim();
        }

        private List<DictionaryMarketingComplex> GetMarketingComplexItems(DictionaryService service)
        {
            var result = new List<DictionaryMarketingComplex>();

            if (service == null || laboratory?.Dicts == null)
                return result;

            if (laboratory.Dicts.MarketingComplexByComplexId != null &&
                laboratory.Dicts.MarketingComplexByComplexId.TryGetValue(service.id, out var byComplex) &&
                byComplex != null)
            {
                result.AddRange(byComplex.Where(x => x != null));
            }

            if (laboratory.Dicts.MarketingComplexByServiceId != null &&
                laboratory.Dicts.MarketingComplexByServiceId.TryGetValue(service.id, out var byService) &&
                byService != null)
            {
                foreach (var item in byService.Where(x => x != null))
                {
                    if (!result.Any(r =>
                        string.Equals(r.complex_id, item.complex_id, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.service_id, item.service_id, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.main_service, item.main_service, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(item);
                    }
                }
            }

            return result;
        }

        private void AddBiomaterialsFromBaseService(DictionaryService service, List<DictionaryBiomaterials> result)
        {
            if (service == null || laboratory?.Dicts == null || result == null)
                return;

            var dicts = laboratory.Dicts;

            if (dicts.ServiceParameters != null &&
                dicts.ServiceParameters.TryGetValue(service.id, out var parameters) &&
                parameters != null)
            {
                var ids = parameters.Select(p => p?.biomaterial_id).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (var id in ids)
                {
                    if (dicts.Biomaterials != null &&
                        dicts.Biomaterials.TryGetValue(id, out var biom) &&
                        biom != null &&
                        !result.Any(r => string.Equals(r.id, biom.id, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(biom);
                    }
                }
            }

            if (!string.IsNullOrEmpty(service.biomaterial_id) && dicts.Biomaterials != null && dicts.Biomaterials.TryGetValue(service.biomaterial_id, out var baseBiom)
                && baseBiom != null && !result.Any(r => string.Equals(r.id, baseBiom.id, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(baseBiom);
            }

            if (string.Equals(service.biomaterial_id, "Drugoe", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(service.other_biomaterial) &&
                !result.Any(r => string.Equals(r.id, "Drugoe", StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(new DictionaryBiomaterials
                {
                    id = "Drugoe",
                    name = service.other_biomaterial,
                    archive = 0
                });
            }
        }

        private bool TryGetDictionaryService(string serviceId, out DictionaryService service)
        {
            service = null;
            serviceId = NormalizeServiceId(serviceId);

            if (string.IsNullOrWhiteSpace(serviceId) ||
                laboratory == null ||
                laboratory.Dicts == null ||
                laboratory.Dicts.Directory == null)
            {
                return false;
            }

            return laboratory.Dicts.Directory.TryGetValue(serviceId, out service) && service != null;
        }

        private bool IsBiomaterialCollectService(string serviceId)
        {
            DictionaryService service;
            return TryGetDictionaryService(serviceId, out service) && service.service_type == 4;
        }

        private bool ProductOrServiceExistsInModel(OrderModelForGUI model, string serviceId)
        {
            serviceId = NormalizeServiceId(serviceId);

            if (string.IsNullOrWhiteSpace(serviceId) || model == null)
                return false;

            bool existsInProducts = model.ProductsInfo != null && model.ProductsInfo.Any(x =>
                x != null &&
                string.Equals(NormalizeServiceId(x.Id), serviceId, StringComparison.OrdinalIgnoreCase));

            if (existsInProducts)
                return true;

            return model.ServicesInfo != null && model.ServicesInfo.Any(x =>
                x != null &&
                string.Equals(NormalizeServiceId(x.Id), serviceId, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsCollectBiomaterialByGemotestEnabled()
        {
            return globalOptions != null && globalOptions.CollectBiomaterialByGemotest;
        }

        private void RenumberProductGuids(GemotestOrderDetail details)
        {
            if (details == null || details.Products == null)
                return;

            for (int i = 0; i < details.Products.Count; i++)
            {
                if (details.Products[i] != null)
                    details.Products[i].OrderProductGuid = i.ToString(CultureInfo.InvariantCulture);
            }
        }

        private void RemoveDisabledBiomaterialCollectServicesFromDetails(GemotestOrderDetail details, string stage)
        {
            if (details == null || details.Products == null)
                return;

            if (IsCollectBiomaterialByGemotestEnabled())
                return;

            int removed = 0;

            for (int i = details.Products.Count - 1; i >= 0; i--)
            {
                var product = details.Products[i];
                if (product == null)
                    continue;

                if (!IsBiomaterialCollectService(product.ProductId))
                    continue;

                DebugGemotestGui((stage ?? "") + ": remove disabled biomaterial collect service from details: index=" + i.ToString(CultureInfo.InvariantCulture) +
                    "; id=" + (product.ProductId ?? "") + "; code=" + (product.ProductCode ?? "") + "; name=" + (product.ProductName ?? ""));

                details.Products.RemoveAt(i);
                removed++;
            }

            if (removed > 0)
                RenumberProductGuids(details);
        }

        private void RemoveDisabledBiomaterialCollectServicesFromModel(OrderModelForGUI model, string stage)
        {
            if (model == null || model.ServicesInfo == null)
                return;

            if (IsCollectBiomaterialByGemotestEnabled())
                return;

            for (int i = model.ServicesInfo.Count - 1; i >= 0; i--)
            {
                var serviceInfo = model.ServicesInfo[i];
                if (serviceInfo == null)
                    continue;

                if (!IsBiomaterialCollectService(serviceInfo.Id))
                    continue;

                DebugGemotestGui((stage ?? "") + ": remove disabled biomaterial collect service from model.ServicesInfo: index=" + i.ToString(CultureInfo.InvariantCulture) +
                    "; id=" + (serviceInfo.Id ?? "") + "; code=" + (serviceInfo.Code ?? "") + "; name=" + (serviceInfo.Name ?? ""));

                model.ServicesInfo.RemoveAt(i);
            }
        }

        private List<DictionaryBiomaterials> ResolveBiomaterialsForService(DictionaryService service)
        {
            var result = new List<DictionaryBiomaterials>();

            if (service == null || laboratory?.Dicts == null)
                return result;

            var dicts = laboratory.Dicts;
            var complexItems = GetMarketingComplexItems(service);

            if (complexItems.Count > 0)
            {
                var mcBiomIds = complexItems.Select(m => m?.biomaterial_id).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                foreach (var id in mcBiomIds)
                {
                    if (dicts.Biomaterials != null && dicts.Biomaterials.TryGetValue(id, out var biom) && biom != null &&
                        !result.Any(r => string.Equals(r.id, biom.id, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(biom);
                    }
                }

                if (result.Count > 0)
                    return result;

                var mainServiceIds = complexItems.Select(m => m?.main_service).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                foreach (var mainServiceId in mainServiceIds)
                {
                    if (dicts.Directory != null && dicts.Directory.TryGetValue(mainServiceId, out var mainService) && mainService != null)
                    {
                        AddBiomaterialsFromBaseService(mainService, result);
                    }
                }

                if (result.Count > 0)
                    return result;
            }

            AddBiomaterialsFromBaseService(service, result);

            return result;
        }

        private List<BiomaterialInfoForGUI> BuildSelectedBiomaterialsForSamples(ProductInfoForGUI productInfo)
        {
            var selected = new List<BiomaterialInfoForGUI>();
            var all = new List<BiomaterialInfoForGUI>();

            if (productInfo == null || productInfo.BiomaterialGroups == null)
                return selected;

            foreach (var group in productInfo.BiomaterialGroups)
            {
                if (group == null)
                    continue;

                if (group.Biomaterials != null)
                {
                    foreach (var biomaterial in group.Biomaterials)
                        AddUniqueBiomaterialInfo(all, biomaterial);
                }

                if (group.BiomaterialsSelected != null)
                {
                    foreach (var biomaterial in group.BiomaterialsSelected)
                        AddUniqueBiomaterialInfo(selected, biomaterial);
                }
            }

            var expanded = ExpandSelectedBiomaterialsForRequiredSampleRows(productInfo.Id, selected, all);

            var result = new List<BiomaterialInfoForGUI>();
            foreach (var biomaterial in expanded)
                AddUniqueBiomaterialInfo(result, biomaterial);

            return result;
        }

        private List<BiomaterialInfoForGUI> ExpandSelectedBiomaterialsForRequiredSampleRows(string serviceId, IEnumerable<BiomaterialInfoForGUI> selectedBiomaterials, IEnumerable<BiomaterialInfoForGUI> allBiomaterials)
        {
            var result = new List<BiomaterialInfoForGUI>();

            if (selectedBiomaterials != null)
            {
                foreach (var selected in selectedBiomaterials)
                    AddUniqueBiomaterialInfo(result, selected);
            }

            if (string.IsNullOrWhiteSpace(serviceId) || laboratory?.Dicts?.SamplesServices == null)
                return result;

            List<DictionarySamplesServices> rows;
            if (!laboratory.Dicts.SamplesServices.TryGetValue(serviceId, out rows) || rows == null || rows.Count == 0)
                return result;

            var selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var selected in result)
            {
                if (selected == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(selected.BiomaterialId))
                    selectedIds.Add(selected.BiomaterialId.Trim());

                if (!string.IsNullOrWhiteSpace(selected.BiomaterialCode))
                    selectedIds.Add(selected.BiomaterialCode.Trim());
            }

            if (selectedIds.Count == 0)
                return result;

            bool hasSelectedDictionaryRow = rows.Any(r => r != null && selectedIds.Contains((r.biomaterial_id ?? string.Empty).Trim()));
            if (!hasSelectedDictionaryRow)
                return result;

            int before = result.Count;
            var allInfos = (allBiomaterials ?? new List<BiomaterialInfoForGUI>()).Where(x => x != null).ToList();

            foreach (var row in rows)
            {
                if (row == null || !IsLinkedSampleRequirementRowForGui(row, rows))
                    continue;

                string biomaterialId = (row.biomaterial_id ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(biomaterialId))
                    continue;

                if (selectedIds.Contains(biomaterialId))
                    continue;

                var info = FindBiomaterialInfoById(allInfos, biomaterialId);
                if (info == null)
                    continue;

                AddUniqueBiomaterialInfo(result, info);
                selectedIds.Add(biomaterialId);
            }

            bool resultHasLinkedRows = rows.Any(row => row != null && IsLinkedSampleRequirementRowForGui(row, rows) && selectedIds.Contains((row.biomaterial_id ?? string.Empty).Trim()));
            bool resultHasIndependentRows = rows.Any(row => row != null && IsIndependentOrdinarySampleRequirementRowForGui(row, rows) && selectedIds.Contains((row.biomaterial_id ?? string.Empty).Trim()));

            if (resultHasLinkedRows && !resultHasIndependentRows)
            {
                var firstIndependentRow = rows.FirstOrDefault(row => IsIndependentOrdinarySampleRequirementRowForGui(row, rows));
                if (firstIndependentRow != null)
                {
                    string biomaterialId = (firstIndependentRow.biomaterial_id ?? string.Empty).Trim();
                    var info = FindBiomaterialInfoById(allInfos, biomaterialId);
                    if (info != null)
                    {
                        AddUniqueBiomaterialInfo(result, info);
                        selectedIds.Add(biomaterialId);
                    }
                }
            }

            if (result.Count != before)
            {
                DebugGemotestGui("ExpandSelectedBiomaterialsForRequiredSampleRows: service=" + (serviceId ?? string.Empty) +
                    "; before=" + before.ToString(CultureInfo.InvariantCulture) +
                    "; after=" + result.Count.ToString(CultureInfo.InvariantCulture));
            }

            return result;
        }

        private static BiomaterialInfoForGUI FindBiomaterialInfoById(List<BiomaterialInfoForGUI> allInfos, string biomaterialId)
        {
            if (allInfos == null || string.IsNullOrWhiteSpace(biomaterialId))
                return null;

            return allInfos.FirstOrDefault(x => x != null &&
                (string.Equals(x.BiomaterialId ?? string.Empty, biomaterialId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(x.BiomaterialCode ?? string.Empty, biomaterialId, StringComparison.OrdinalIgnoreCase)));
        }

        private static void AddUniqueBiomaterialInfo(List<BiomaterialInfoForGUI> target, BiomaterialInfoForGUI item)
        {
            if (target == null || item == null)
                return;

            bool exists = target.Any(x => x != null &&
                string.Equals(x.BiomaterialId ?? string.Empty, item.BiomaterialId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ContainerCode ?? string.Empty, item.ContainerCode ?? string.Empty, StringComparison.OrdinalIgnoreCase));

            if (!exists)
                target.Add(item);
        }

        private static int ToInt(object value, int defaultValue)
        {
            if (value == null)
                return defaultValue;

            if (value is int)
                return (int)value;

            if (value is long)
                return unchecked((int)(long)value);

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
                return defaultValue;

            int result;
            return int.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out result) ? result : defaultValue;
        }

        private sealed class BiomaterialSelectionState
        {
            public readonly List<int> Chosen = new List<int>();
            public readonly List<int> Another = new List<int>();
        }

        private static Dictionary<string, BiomaterialSelectionState> CaptureBiomaterialSelectionState(IEnumerable<GemotestBioMaterial> biomaterials)
        {
            var result = new Dictionary<string, BiomaterialSelectionState>(StringComparer.OrdinalIgnoreCase);

            if (biomaterials == null)
                return result;

            foreach (var biomaterial in biomaterials)
            {
                var key = BuildBiomaterialSelectionStateKey(biomaterial);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                BiomaterialSelectionState state;
                if (!result.TryGetValue(key, out state))
                {
                    state = new BiomaterialSelectionState();
                    result[key] = state;
                }

                AddDistinctValues(state.Chosen, biomaterial.Chosen);
                AddDistinctValues(state.Another, biomaterial.Another);
            }

            return result;
        }

        private static void RestoreBiomaterialSelectionState(IEnumerable<GemotestBioMaterial> biomaterials, Dictionary<string, BiomaterialSelectionState> savedState)
        {
            if (biomaterials == null || savedState == null || savedState.Count == 0)
                return;

            foreach (var biomaterial in biomaterials)
            {
                var key = BuildBiomaterialSelectionStateKey(biomaterial);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                BiomaterialSelectionState state;
                if (!savedState.TryGetValue(key, out state))
                    continue;

                if (biomaterial.Chosen == null)
                    biomaterial.Chosen = new List<int>();

                if (biomaterial.Another == null)
                    biomaterial.Another = new List<int>();

                biomaterial.Chosen.Clear();
                biomaterial.Another.Clear();
                AddDistinctValues(biomaterial.Chosen, state.Chosen);
                AddDistinctValues(biomaterial.Another, state.Another);
            }
        }

        private static string BuildBiomaterialSelectionStateKey(GemotestBioMaterial biomaterial)
        {
            if (biomaterial == null)
                return string.Empty;

            var id = Convert.ToString(biomaterial.Id, CultureInfo.InvariantCulture);
            var code = Convert.ToString(biomaterial.Code, CultureInfo.InvariantCulture);

            if (!string.IsNullOrWhiteSpace(id))
                return id.Trim();

            if (!string.IsNullOrWhiteSpace(code))
                return code.Trim();

            return Convert.ToString(biomaterial.Name, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static void AddDistinctValues(List<int> target, IEnumerable<int> source)
        {
            if (target == null || source == null)
                return;

            foreach (var value in source)
            {
                if (!target.Contains(value))
                    target.Add(value);
            }
        }

        private static bool IsIndependentOrdinarySampleRequirementRowForGui(DictionarySamplesServices row, List<DictionarySamplesServices> allRows)
        {
            if (row == null || allRows == null || allRows.Count == 0)
                return false;

            int sampleId = ToInt(row.sample_id, 0);
            int primarySampleId = ToInt(row.primary_sample_id, 0);

            if (sampleId <= 0 || primarySampleId > 0)
                return false;

            bool isParentOfLinkedChild = allRows.Any(child => child != null && !object.ReferenceEquals(child, row) && ToInt(child.primary_sample_id, 0) == sampleId);
            return !isParentOfLinkedChild;
        }

        private static bool IsLinkedSampleRequirementRowForGui(DictionarySamplesServices row, List<DictionarySamplesServices> allRows)
        {
            if (row == null || allRows == null || allRows.Count == 0)
                return false;

            int sampleId = ToInt(row.sample_id, 0);
            int primarySampleId = ToInt(row.primary_sample_id, 0);

            if (sampleId <= 0)
                return false;

            if (primarySampleId > 0)
                return true;

            return allRows.Any(x => x != null && !object.ReferenceEquals(x, row) && ToInt(x.primary_sample_id, 0) == sampleId);
        }

        private bool TryResolveTransportFromSamplesServices(string serviceId, string biomaterialId, out DictionaryTransport transport)
        {
            transport = null;

            if (string.IsNullOrEmpty(serviceId) || laboratory?.Dicts == null)
                return false;

            if (laboratory.Dicts.SamplesServices == null || !laboratory.Dicts.SamplesServices.TryGetValue(serviceId, out var rows) || rows == null || rows.Count == 0)
                return false;

            var row = rows.FirstOrDefault(r => r != null && string.Equals(r.service_id, serviceId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.biomaterial_id ?? string.Empty, biomaterialId ?? string.Empty, StringComparison.OrdinalIgnoreCase) && r.sample_id > 0);

            if (row == null)
            {
                row = rows.FirstOrDefault(r => r != null && string.Equals(r.service_id, serviceId, StringComparison.OrdinalIgnoreCase) && r.sample_id > 0);
            }

            if (row == null || laboratory.Dicts.Samples == null)
                return false;

            if (!laboratory.Dicts.Samples.TryGetValue(row.sample_id.ToString(), out var sample) || sample == null)
                return false;

            if (string.IsNullOrEmpty(sample.transport_id) || laboratory.Dicts.Transport == null)
                return false;

            return laboratory.Dicts.Transport.TryGetValue(sample.transport_id, out transport) && transport != null;
        }

        private bool TryResolveBaseTransport(string serviceId, string biomaterialId, out DictionaryTransport transport)
        {
            transport = null;

            if (string.IsNullOrEmpty(serviceId) || laboratory?.Dicts == null)
                return false;

            if (laboratory.Dicts.ServiceParameters != null && laboratory.Dicts.ServiceParameters.TryGetValue(serviceId, out var paramsList) &&
                paramsList != null && paramsList.Count > 0)
            {
                var param = paramsList.FirstOrDefault(p => p != null && string.Equals(p.service_id, serviceId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.biomaterial_id ?? string.Empty, biomaterialId ?? string.Empty, StringComparison.OrdinalIgnoreCase));

                if (param != null && !string.IsNullOrEmpty(param.transport_id) && laboratory.Dicts.Transport != null &&
                    laboratory.Dicts.Transport.TryGetValue(param.transport_id, out transport) && transport != null)
                {
                    return true;
                }
            }

            if (laboratory.Dicts.Directory != null && laboratory.Dicts.Directory.TryGetValue(serviceId, out var svc) && svc != null && !string.IsNullOrEmpty(svc.transport_id) &&
                laboratory.Dicts.Transport != null && laboratory.Dicts.Transport.TryGetValue(svc.transport_id, out transport) && transport != null)
            {
                return true;
            }

            return false;
        }

        private DictionaryTransport ResolveTransport(string serviceId, string biomaterialId)
        {
            DictionaryTransport transport = null;

            if (string.IsNullOrEmpty(serviceId) || laboratory?.Dicts == null || laboratory.Dicts.Directory == null)
                return null;

            if (!laboratory.Dicts.Directory.TryGetValue(serviceId, out var svc) || svc == null)
                return null;

            var complexItems = GetMarketingComplexItems(svc);

            if (complexItems.Count > 0)
            {
                var mcItem = complexItems.FirstOrDefault(m => m != null && !string.IsNullOrEmpty(m.transport_id) &&
                    (string.Equals(m.biomaterial_id ?? string.Empty, biomaterialId ?? string.Empty, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(m.biomaterial_id)));

                if (mcItem != null && laboratory.Dicts.Transport != null && laboratory.Dicts.Transport.TryGetValue(mcItem.transport_id, out transport) && transport != null)
                {
                    return transport;
                }

                var mainServiceIds = complexItems.Select(m => m?.main_service).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                foreach (var mainServiceId in mainServiceIds)
                {
                    if (TryResolveTransportFromSamplesServices(mainServiceId, biomaterialId, out transport))
                        return transport;

                    if (TryResolveBaseTransport(mainServiceId, biomaterialId, out transport))
                        return transport;
                }
            }

            if (TryResolveTransportFromSamplesServices(serviceId, biomaterialId, out transport))
                return transport;

            if (TryResolveBaseTransport(serviceId, biomaterialId, out transport))
                return transport;

            return null;
        }

        private bool IsMarketingComplex(string serviceId)
        {
            if (string.IsNullOrEmpty(serviceId) || laboratory?.Dicts == null || laboratory.Dicts.Directory == null)
                return false;

            if (!laboratory.Dicts.Directory.TryGetValue(serviceId, out var svc) || svc == null)
                return false;

            return svc.service_type == 1 || svc.service_type == 2 ||
                   (laboratory.Dicts.MarketingComplexByComplexId != null && laboratory.Dicts.MarketingComplexByComplexId.ContainsKey(serviceId)) ||
                   (laboratory.Dicts.MarketingComplexByServiceId != null && laboratory.Dicts.MarketingComplexByServiceId.ContainsKey(serviceId));
        }

        private static string NormalizeTransportKeyForBiomaterial(BiomaterialInfoForGUI biomaterial)
        {
            if (biomaterial == null)
                return string.Empty;

            string container = biomaterial.ContainerCode;
            if (!string.IsNullOrEmpty(container))
                return container.Trim();

            return (biomaterial.BiomaterialId ?? string.Empty).Trim();
        }

        private static List<BiomaterialInfoForGUI> BuildDefaultBiomaterialSelection(List<BiomaterialInfoForGUI> biomaterials)
        {
            List<BiomaterialInfoForGUI> result = new List<BiomaterialInfoForGUI>();

            if (biomaterials == null || biomaterials.Count == 0)
                return result;

            HashSet<string> usedTransportKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (BiomaterialInfoForGUI biomaterial in biomaterials)
            {
                if (biomaterial == null)
                    continue;

                string key = NormalizeTransportKeyForBiomaterial(biomaterial);
                if (string.IsNullOrEmpty(key))
                    key = biomaterial.BiomaterialId ?? string.Empty;

                if (usedTransportKeys.Add(key))
                    result.Add(biomaterial);
            }

            if (result.Count == 0)
                result.Add(biomaterials[0]);

            return result;
        }

        private bool ShouldAllowMultipleBiomaterialSelection(List<BiomaterialInfoForGUI> biomaterials)
        {
            if (biomaterials == null || biomaterials.Count <= 1)
                return false;

            return BuildDefaultBiomaterialSelection(biomaterials).Count > 1;
        }

        private void SetDefaultBiomaterialSelection(BiomaterialGroupForGUI group, List<string> validSelectedIds)
        {
            if (group == null)
                return;

            group.BiomaterialsSelected.Clear();

            if (group.Biomaterials == null || group.Biomaterials.Count == 0)
                return;

            if (validSelectedIds != null && validSelectedIds.Count > 0)
            {
                foreach (BiomaterialInfoForGUI biomaterial in group.Biomaterials)
                {
                    if (biomaterial == null)
                        continue;

                    if (!validSelectedIds.Any(x => string.Equals(x ?? string.Empty, biomaterial.BiomaterialId ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (group.SelectOnlyOne && group.BiomaterialsSelected.Count > 0)
                        break;

                    group.BiomaterialsSelected.Add(biomaterial);
                }
            }

            if (group.BiomaterialsSelected.Count > 0)
                return;

            if (group.SelectOnlyOne)
            {
                group.BiomaterialsSelected.Add(group.Biomaterials[0]);
                return;
            }

            foreach (BiomaterialInfoForGUI biomaterial in BuildDefaultBiomaterialSelection(group.Biomaterials))
                group.BiomaterialsSelected.Add(biomaterial);
        }

        private static string SafeTrim(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static bool SameIdForGui(string left, string right)
        {
            return string.Equals(SafeTrim(left), SafeTrim(right), StringComparison.OrdinalIgnoreCase);
        }

        private List<BiomaterialGroupForGUI> BuildBiomaterialGroupsForService(string serviceId, List<string> validSelectedIds)
        {
            var groups = new List<BiomaterialGroupForGUI>();

            if (string.IsNullOrWhiteSpace(serviceId) || laboratory?.Dicts?.Directory == null)
            {
                groups.Add(CreateEmptyRequiredBiomaterialGroup());
                NormalizeBiomaterialGroups(groups);
                return groups;
            }

            DictionaryService svc;
            if (!laboratory.Dicts.Directory.TryGetValue(serviceId, out svc) || svc == null)
            {
                groups.Add(CreateEmptyRequiredBiomaterialGroup());
                NormalizeBiomaterialGroups(groups);
                return groups;
            }

            if (IsMarketingComplex(serviceId))
            {
                groups = BuildMarketingComplexBiomaterialGroups(serviceId, validSelectedIds);
                if (groups.Count > 0)
                {
                    NormalizeBiomaterialGroups(groups);
                    DebugGemotestGui("BuildBiomaterialGroupsForService: marketing complex " + (serviceId ?? string.Empty) +
                        "; groups=" + groups.Count.ToString(CultureInfo.InvariantCulture));
                    return groups;
                }
            }

            var requiredSampleGroups = BuildRequiredSampleBiomaterialGroupsForService(serviceId, validSelectedIds);
            if (requiredSampleGroups.Count > 0)
            {
                NormalizeBiomaterialGroups(requiredSampleGroups);
                DebugGemotestGui("BuildBiomaterialGroupsForService: required sample rows " + (serviceId ?? string.Empty) +
                    "; groups=" + requiredSampleGroups.Count.ToString(CultureInfo.InvariantCulture));
                return requiredSampleGroups;
            }

            var group = new BiomaterialGroupForGUI
            {
                SelectOnlyOne = true,
                Optional = false
            };
            group.BiomaterialsSelected = group.BiomaterialsSelected ?? new List<BiomaterialInfoForGUI>();
            group.Biomaterials = group.Biomaterials ?? new List<BiomaterialInfoForGUI>();

            var bioms = ResolveBiomaterialsForService(svc);
            foreach (var biom in bioms)
            {
                if (biom == null || string.IsNullOrWhiteSpace(biom.id))
                    continue;

                var info = BuildBiomaterialInfoForGui(serviceId, biom, null);
                AddBiomaterialInfoToGroup(group, info);
            }

            group.SelectOnlyOne = !ShouldAllowMultipleBiomaterialSelection(group.Biomaterials);
            SetDefaultBiomaterialSelection(group, validSelectedIds);
            groups.Add(group);
            NormalizeBiomaterialGroups(groups);
            return groups;
        }

        private List<BiomaterialGroupForGUI> BuildBiomaterialGroupsForProduct(GemotestOrderDetail details, int productIndex)
        {
            var groups = new List<BiomaterialGroupForGUI>();

            if (details == null || details.Products == null || productIndex < 0 || productIndex >= details.Products.Count)
            {
                groups.Add(CreateEmptyRequiredBiomaterialGroup());
                NormalizeBiomaterialGroups(groups);
                return groups;
            }

            var productDetail = details.Products[productIndex];
            if (productDetail == null || string.IsNullOrWhiteSpace(productDetail.ProductId))
            {
                groups.Add(CreateEmptyRequiredBiomaterialGroup());
                NormalizeBiomaterialGroups(groups);
                return groups;
            }

            var validSelectedIds = GetSelectedBiomaterialIdsFromDetails(details, productIndex);
            groups = BuildBiomaterialGroupsForService(productDetail.ProductId, validSelectedIds);

            bool hasAnyBiomaterial = groups.Any(g => g != null && g.Biomaterials != null && g.Biomaterials.Count > 0);
            if (!hasAnyBiomaterial)
            {
                groups.Clear();
                groups.Add(BuildLegacyBiomaterialGroupFromDetails(details, productIndex, validSelectedIds));
            }

            NormalizeBiomaterialGroups(groups);

            DebugGemotestGui("BuildBiomaterialGroupsForProduct: productIndex=" + productIndex.ToString(CultureInfo.InvariantCulture) +
                "; productId=" + (productDetail.ProductId ?? string.Empty) +
                "; groups=" + groups.Count.ToString(CultureInfo.InvariantCulture) +
                "; selectedIds=" + JoinDebugValues(validSelectedIds));

            return groups;
        }

        private void EnsureRequiredSampleBiomaterialsInDetails(GemotestOrderDetail details)
        {
            if (details == null || details.Products == null || laboratory?.Dicts?.SamplesServices == null)
                return;

            if (details.BioMaterials == null)
                details.BioMaterials = new List<GemotestBioMaterial>();

            for (int productIndex = 0; productIndex < details.Products.Count; productIndex++)
            {
                var product = details.Products[productIndex];
                if (product == null || string.IsNullOrWhiteSpace(product.ProductId))
                    continue;

                List<DictionarySamplesServices> rows;
                if (!laboratory.Dicts.SamplesServices.TryGetValue(product.ProductId, out rows) || rows == null || rows.Count == 0)
                    continue;

                foreach (var row in rows)
                {
                    if (row == null || ToInt(row.sample_id, 0) <= 0)
                        continue;

                    string biomaterialId = GetBiomaterialIdFromSampleRequirement(row);
                    if (string.IsNullOrWhiteSpace(biomaterialId))
                        continue;

                    var existing = details.BioMaterials.FirstOrDefault(b => b != null && SameIdForGui(b.Id, biomaterialId));
                    if (existing == null)
                    {
                        string biomaterialName = biomaterialId;
                        DictionaryBiomaterials dictionaryBiomaterial;
                        if (laboratory.Dicts.Biomaterials != null &&
                            laboratory.Dicts.Biomaterials.TryGetValue(biomaterialId, out dictionaryBiomaterial) &&
                            dictionaryBiomaterial != null &&
                            !string.IsNullOrWhiteSpace(dictionaryBiomaterial.name))
                        {
                            biomaterialName = dictionaryBiomaterial.name;
                        }

                        existing = new GemotestBioMaterial
                        {
                            Id = biomaterialId,
                            Code = biomaterialId,
                            Name = biomaterialName
                        };

                        details.BioMaterials.Add(existing);
                    }

                    existing.Chosen = existing.Chosen ?? new List<int>();
                    existing.Another = existing.Another ?? new List<int>();
                    existing.Mandatory = existing.Mandatory ?? new List<int>();

                    if (!existing.Chosen.Contains(productIndex) &&
                        !existing.Mandatory.Contains(productIndex) &&
                        !existing.Another.Contains(productIndex))
                    {
                        existing.Another.Add(productIndex);
                    }
                }
            }
        }

        private static List<string> GetSelectedBiomaterialIdsFromDetails(GemotestOrderDetail details, int productIndex)
        {
            var result = new List<string>();

            if (details == null || details.BioMaterials == null || productIndex < 0)
                return result;

            foreach (var biom in details.BioMaterials)
            {
                if (biom == null || string.IsNullOrWhiteSpace(biom.Id))
                    continue;

                bool selected =
                    (biom.Mandatory != null && biom.Mandatory.Contains(productIndex)) ||
                    (biom.Chosen != null && biom.Chosen.Contains(productIndex));

                if (!selected)
                    continue;

                if (!result.Any(x => SameIdForGui(x, biom.Id)))
                    result.Add(biom.Id);
            }

            return result;
        }

        private BiomaterialGroupForGUI BuildLegacyBiomaterialGroupFromDetails(GemotestOrderDetail details, int productIndex, List<string> validSelectedIds)
        {
            var group = new BiomaterialGroupForGUI
            {
                SelectOnlyOne = true,
                Optional = false
            };
            group.BiomaterialsSelected = group.BiomaterialsSelected ?? new List<BiomaterialInfoForGUI>();
            group.Biomaterials = group.Biomaterials ?? new List<BiomaterialInfoForGUI>();

            if (details == null || details.Products == null || details.BioMaterials == null || productIndex < 0 || productIndex >= details.Products.Count)
                return group;

            var productDetail = details.Products[productIndex];
            string serviceId = productDetail != null ? productDetail.ProductId : string.Empty;

            foreach (var biom in details.BioMaterials)
            {
                if (biom == null || string.IsNullOrWhiteSpace(biom.Id))
                    continue;

                bool linked =
                    (biom.Mandatory != null && biom.Mandatory.Contains(productIndex)) ||
                    (biom.Chosen != null && biom.Chosen.Contains(productIndex)) ||
                    (biom.Another != null && biom.Another.Contains(productIndex));

                if (!linked)
                    continue;

                var dictBiom = ResolveDictionaryBiomaterial(biom.Id, biom.Name);
                var info = BuildBiomaterialInfoForGui(serviceId, dictBiom, null);
                AddBiomaterialInfoToGroup(group, info);
            }

            SetDefaultBiomaterialSelection(group, validSelectedIds);
            return group;
        }

        private BiomaterialGroupForGUI CreateEmptyRequiredBiomaterialGroup()
        {
            return new BiomaterialGroupForGUI
            {
                SelectOnlyOne = true,
                Optional = false,
                Biomaterials = new List<BiomaterialInfoForGUI>(),
                BiomaterialsSelected = new List<BiomaterialInfoForGUI>()
            };
        }

        private void NormalizeBiomaterialGroups(List<BiomaterialGroupForGUI> groups)
        {
            if (groups == null)
                return;

            MergeDuplicateSingleBiomaterialGroups(groups);

            int groupNum = 1;
            foreach (var group in groups.Where(g => g != null))
            {
                group.GroupNum = groupNum++;
                group.Optional = false;
                group.RefreshFieldsOnSelectionSet = true;
                group.RefreshFieldsOnSelectionRemove = true;
                group.Biomaterials = group.Biomaterials ?? new List<BiomaterialInfoForGUI>();
                group.BiomaterialsSelected = group.BiomaterialsSelected ?? new List<BiomaterialInfoForGUI>();

                RemoveDuplicateBiomaterialInfosFromGroup(group);

                if (group.BiomaterialsSelected.Count == 0 && group.Biomaterials.Count > 0)
                {
                    if (group.SelectOnlyOne)
                    {
                        group.BiomaterialsSelected.Add(group.Biomaterials[0]);
                    }
                    else
                    {
                        foreach (var biomaterial in BuildDefaultBiomaterialSelection(group.Biomaterials))
                            group.BiomaterialsSelected.Add(biomaterial);
                    }
                }

                if (group.SelectOnlyOne && group.BiomaterialsSelected.Count > 1)
                {
                    BiomaterialInfoForGUI firstSelected = group.BiomaterialsSelected[0];
                    group.BiomaterialsSelected.Clear();
                    group.BiomaterialsSelected.Add(firstSelected);
                }
            }
        }

        private void MergeDuplicateSingleBiomaterialGroups(List<BiomaterialGroupForGUI> groups)
        {
            if (groups == null || groups.Count <= 1)
                return;

            var result = new List<BiomaterialGroupForGUI>();
            var groupByKey = new Dictionary<string, BiomaterialGroupForGUI>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                if (group == null)
                    continue;

                group.Biomaterials = group.Biomaterials ?? new List<BiomaterialInfoForGUI>();
                group.BiomaterialsSelected = group.BiomaterialsSelected ?? new List<BiomaterialInfoForGUI>();
                RemoveDuplicateBiomaterialInfosFromGroup(group);

                string key = BuildBiomaterialGroupDuplicateKey(group);
                if (string.IsNullOrWhiteSpace(key))
                {
                    result.Add(group);
                    continue;
                }

                BiomaterialGroupForGUI existingGroup;
                if (groupByKey.TryGetValue(key, out existingGroup) && existingGroup != null)
                {
                    MergeBiomaterialGroupSelection(existingGroup, group);

                    if (!group.SelectOnlyOne)
                        existingGroup.SelectOnlyOne = false;

                    continue;
                }

                groupByKey[key] = group;
                result.Add(group);
            }

            groups.Clear();
            groups.AddRange(result);
        }

        private static string BuildBiomaterialGroupDuplicateKey(BiomaterialGroupForGUI group)
        {
            if (group == null || group.Biomaterials == null || group.Biomaterials.Count == 0)
                return string.Empty;

            var keys = group.Biomaterials
                .Where(x => x != null)
                .Select(BuildBiomaterialInfoDuplicateKey)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (keys.Count == 0)
                return string.Empty;

            return string.Join(";", keys.ToArray());
        }

        private static void MergeBiomaterialGroupSelection(BiomaterialGroupForGUI target, BiomaterialGroupForGUI source)
        {
            if (target == null || source == null || source.BiomaterialsSelected == null)
                return;

            target.BiomaterialsSelected = target.BiomaterialsSelected ?? new List<BiomaterialInfoForGUI>();
            target.Biomaterials = target.Biomaterials ?? new List<BiomaterialInfoForGUI>();

            foreach (var selected in source.BiomaterialsSelected)
            {
                if (selected == null)
                    continue;

                string selectedKey = BuildBiomaterialInfoDuplicateKey(selected);
                if (string.IsNullOrWhiteSpace(selectedKey))
                    continue;

                var targetItem = target.Biomaterials.FirstOrDefault(x =>
                    string.Equals(BuildBiomaterialInfoDuplicateKey(x), selectedKey, StringComparison.OrdinalIgnoreCase));

                if (targetItem == null)
                    continue;

                bool alreadySelected = target.BiomaterialsSelected.Any(x =>
                    string.Equals(BuildBiomaterialInfoDuplicateKey(x), selectedKey, StringComparison.OrdinalIgnoreCase));

                if (!alreadySelected)
                    target.BiomaterialsSelected.Add(targetItem);
            }
        }

        private static void RemoveDuplicateBiomaterialInfosFromGroup(BiomaterialGroupForGUI group)
        {
            if (group == null)
                return;

            group.Biomaterials = group.Biomaterials ?? new List<BiomaterialInfoForGUI>();
            group.BiomaterialsSelected = group.BiomaterialsSelected ?? new List<BiomaterialInfoForGUI>();

            var selectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var selected in group.BiomaterialsSelected)
            {
                string selectedKey = BuildBiomaterialInfoDuplicateKey(selected);
                if (!string.IsNullOrWhiteSpace(selectedKey))
                    selectedKeys.Add(selectedKey);
            }

            var cleanBiomaterials = new List<BiomaterialInfoForGUI>();
            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var biomaterial in group.Biomaterials)
            {
                if (biomaterial == null)
                    continue;

                string key = BuildBiomaterialInfoDuplicateKey(biomaterial);
                if (string.IsNullOrWhiteSpace(key))
                    key = Guid.NewGuid().ToString();

                if (usedKeys.Add(key))
                    cleanBiomaterials.Add(biomaterial);
            }

            group.Biomaterials.Clear();
            group.Biomaterials.AddRange(cleanBiomaterials);

            group.BiomaterialsSelected.Clear();
            foreach (var biomaterial in group.Biomaterials)
            {
                string key = BuildBiomaterialInfoDuplicateKey(biomaterial);
                if (!string.IsNullOrWhiteSpace(key) && selectedKeys.Contains(key))
                    group.BiomaterialsSelected.Add(biomaterial);
            }
        }

        private static string BuildBiomaterialInfoDuplicateKey(BiomaterialInfoForGUI biomaterial)
        {
            if (biomaterial == null)
                return string.Empty;

            string biomaterialKey = SafeTrim(biomaterial.BiomaterialId);
            if (string.IsNullOrWhiteSpace(biomaterialKey))
                biomaterialKey = SafeTrim(biomaterial.BiomaterialCode);
            if (string.IsNullOrWhiteSpace(biomaterialKey))
                biomaterialKey = SafeTrim(biomaterial.BiomaterialName);

            string containerKey = SafeTrim(biomaterial.ContainerCode);
            if (string.IsNullOrWhiteSpace(containerKey))
                containerKey = SafeTrim(biomaterial.ContainerId);
            if (string.IsNullOrWhiteSpace(containerKey))
                containerKey = SafeTrim(biomaterial.ContainerName);

            return biomaterialKey + "|" + containerKey;
        }

        private List<BiomaterialGroupForGUI> BuildRequiredSampleBiomaterialGroupsForService(string serviceId, List<string> validSelectedIds)
        {
            var groupsByBiomaterial = new Dictionary<string, BiomaterialGroupForGUI>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(serviceId) || laboratory?.Dicts?.SamplesServices == null)
                return new List<BiomaterialGroupForGUI>();

            List<DictionarySamplesServices> rows;
            if (!laboratory.Dicts.SamplesServices.TryGetValue(serviceId, out rows) || rows == null || rows.Count == 0)
                return new List<BiomaterialGroupForGUI>();

            foreach (var row in rows)
            {
                if (row == null || ToInt(row.sample_id, 0) <= 0)
                    continue;

                string biomaterialId = GetBiomaterialIdFromSampleRequirement(row);
                if (string.IsNullOrWhiteSpace(biomaterialId))
                    continue;

                string key = biomaterialId.Trim();
                if (groupsByBiomaterial.ContainsKey(key))
                    continue;

                var group = new BiomaterialGroupForGUI
                {
                    SelectOnlyOne = true,
                    Optional = false,
                    Biomaterials = new List<BiomaterialInfoForGUI>(),
                    BiomaterialsSelected = new List<BiomaterialInfoForGUI>()
                };

                var biom = ResolveDictionaryBiomaterial(biomaterialId, biomaterialId);
                string forcedTransportId = ResolveTransportIdFromSampleRequirement(row);
                var info = BuildBiomaterialInfoForGui(serviceId, biom, forcedTransportId);
                AddBiomaterialInfoToGroupByBiomaterialOnly(group, info);

                if (group.Biomaterials.Count == 0)
                    continue;

                SetDefaultBiomaterialSelection(group, validSelectedIds);
                groupsByBiomaterial[key] = group;
            }

            return groupsByBiomaterial.Values
                .Where(g => g != null && g.Biomaterials != null && g.Biomaterials.Count > 0)
                .ToList();
        }

        private static string GetBiomaterialIdFromSampleRequirement(DictionarySamplesServices row)
        {
            if (row == null)
                return string.Empty;

            string biomaterialId = SafeTrim(row.biomaterial_id);
            if (!string.IsNullOrWhiteSpace(biomaterialId))
                return biomaterialId;

            return SafeTrim(row.microbiology_biomaterial_id);
        }

        private string ResolveTransportIdFromSampleRequirement(DictionarySamplesServices row)
        {
            if (row == null || laboratory?.Dicts?.Samples == null)
                return string.Empty;

            int sampleId = ToInt(row.sample_id, 0);
            if (sampleId <= 0)
                return string.Empty;

            DictionarySamples sample;
            if (laboratory.Dicts.Samples.TryGetValue(sampleId.ToString(CultureInfo.InvariantCulture), out sample) &&
                sample != null &&
                !string.IsNullOrWhiteSpace(sample.transport_id))
            {
                return sample.transport_id.Trim();
            }

            return string.Empty;
        }

        private List<BiomaterialGroupForGUI> BuildMarketingComplexBiomaterialGroups(string serviceId, List<string> validSelectedIds)
        {
            var resultByKey = new Dictionary<string, BiomaterialGroupForGUI>(StringComparer.OrdinalIgnoreCase);

            var items = GetMarketingComplexItemsForServiceId(serviceId);
            if (items == null || items.Count == 0)
                return new List<BiomaterialGroupForGUI>();

            foreach (var item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.biomaterial_id))
                    continue;

                string itemServiceId = GetMarketingComplexItemServiceId(item, serviceId);
                var sampleRows = GetSampleRowsForMarketingComplexItem(item, itemServiceId);

                if (sampleRows.Count == 0)
                {
                    AddMarketingComplexBiomaterialToGroup(resultByKey, serviceId, itemServiceId, item, null);
                    continue;
                }

                foreach (var sampleRow in sampleRows)
                    AddMarketingComplexBiomaterialToGroup(resultByKey, serviceId, itemServiceId, item, sampleRow);
            }

            var groups = resultByKey.Values
                .Where(g => g != null && g.Biomaterials != null && g.Biomaterials.Count > 0)
                .ToList();

            foreach (var group in groups)
                SetDefaultBiomaterialSelection(group, validSelectedIds);

            return groups;
        }

        private void AddMarketingComplexBiomaterialToGroup(
            Dictionary<string, BiomaterialGroupForGUI> resultByKey,
            string complexServiceId,
            string itemServiceId,
            DictionaryMarketingComplex item,
            DictionarySamplesServices sampleRow)
        {
            if (resultByKey == null || item == null || string.IsNullOrWhiteSpace(item.biomaterial_id))
                return;

            string key = BuildMarketingComplexRequirementKey(item, complexServiceId, itemServiceId, sampleRow);
            if (string.IsNullOrWhiteSpace(key))
                key = Guid.NewGuid().ToString();

            BiomaterialGroupForGUI group;
            if (!resultByKey.TryGetValue(key, out group) || group == null)
            {
                group = new BiomaterialGroupForGUI
                {
                    SelectOnlyOne = true,
                    Optional = false,
                    Biomaterials = new List<BiomaterialInfoForGUI>(),
                    BiomaterialsSelected = new List<BiomaterialInfoForGUI>()
                };
                resultByKey[key] = group;
            }

            var biom = ResolveDictionaryBiomaterial(item.biomaterial_id, item.biomaterial_id);
            var info = BuildBiomaterialInfoForGui(itemServiceId, biom, item.transport_id);
            AddBiomaterialInfoToGroupByBiomaterialOnly(group, info);
        }

        private string BuildMarketingComplexRequirementKey(DictionaryMarketingComplex item, string complexServiceId, string itemServiceId, DictionarySamplesServices sampleRow)
        {
            if (item == null)
                return string.Empty;

            string biomaterialKey = SafeTrim(item.biomaterial_id);
            if (string.IsNullOrWhiteSpace(biomaterialKey) && sampleRow != null)
                biomaterialKey = GetBiomaterialIdFromSampleRequirement(sampleRow);

            // Для GUI у маркетингового комплекса один и тот же биоматериал не должен
            // превращаться в несколько одинаковых групп только из-за разных строк состава
            // или разных внутренних услуг комплекса. При отправке выбранный BiomaterialId
            // всё равно разворачивается в нужные строки состава комплекса.
            return biomaterialKey;
        }

        private string GetMarketingComplexItemServiceId(DictionaryMarketingComplex item, string fallbackServiceId)
        {
            if (item == null)
                return SafeTrim(fallbackServiceId);

            if (!string.IsNullOrWhiteSpace(item.service_id))
                return item.service_id.Trim();

            if (!string.IsNullOrWhiteSpace(item.main_service))
                return item.main_service.Trim();

            return SafeTrim(fallbackServiceId);
        }

        private List<DictionaryMarketingComplex> GetMarketingComplexItemsForServiceId(string serviceId)
        {
            var result = new List<DictionaryMarketingComplex>();

            if (string.IsNullOrWhiteSpace(serviceId) || laboratory?.Dicts == null)
                return result;

            List<DictionaryMarketingComplex> byComplexId = null;
            if (laboratory.Dicts.MarketingComplexByComplexId != null &&
                laboratory.Dicts.MarketingComplexByComplexId.TryGetValue(serviceId, out byComplexId) &&
                byComplexId != null)
            {
                result.AddRange(byComplexId.Where(x => x != null));
            }

            List<DictionaryMarketingComplex> byServiceId = null;
            if (laboratory.Dicts.MarketingComplexByServiceId != null &&
                laboratory.Dicts.MarketingComplexByServiceId.TryGetValue(serviceId, out byServiceId) &&
                byServiceId != null)
            {
                foreach (var item in byServiceId.Where(x => x != null))
                {
                    if (!result.Any(x => SameMarketingComplexItem(x, item)))
                        result.Add(item);
                }
            }

            return result;
        }

        private static bool SameMarketingComplexItem(DictionaryMarketingComplex left, DictionaryMarketingComplex right)
        {
            if (left == null || right == null)
                return false;

            return SameIdForGui(left.complex_id, right.complex_id) &&
                   SameIdForGui(left.service_id, right.service_id) &&
                   SameIdForGui(left.main_service, right.main_service) &&
                   SameIdForGui(left.biomaterial_id, right.biomaterial_id) &&
                   SameIdForGui(left.localization_id, right.localization_id) &&
                   SameIdForGui(left.transport_id, right.transport_id);
        }

        private List<DictionarySamplesServices> GetSampleRowsForMarketingComplexItem(DictionaryMarketingComplex item, string itemServiceId)
        {
            var result = new List<DictionarySamplesServices>();

            if (item == null || string.IsNullOrWhiteSpace(itemServiceId) || laboratory?.Dicts?.SamplesServices == null)
                return result;

            List<DictionarySamplesServices> rows;
            if (!laboratory.Dicts.SamplesServices.TryGetValue(itemServiceId, out rows) || rows == null || rows.Count == 0)
                return result;

            foreach (var row in rows)
            {
                if (row == null)
                    continue;

                if (!MarketingSampleRowMatchesItem(row, item))
                    continue;

                result.Add(row);
            }

            if (result.Count > 0)
                return result;

            return new List<DictionarySamplesServices>();
        }

        private bool MarketingSampleRowMatchesItem(DictionarySamplesServices row, DictionaryMarketingComplex item)
        {
            if (row == null || item == null)
                return false;

            string itemBio = SafeTrim(item.biomaterial_id);
            string rowBio = SafeTrim(row.biomaterial_id);
            string rowMicroBio = SafeTrim(row.microbiology_biomaterial_id);

            bool biomaterialMatches = true;
            if (!string.IsNullOrWhiteSpace(itemBio))
            {
                if (!string.IsNullOrWhiteSpace(rowBio) || !string.IsNullOrWhiteSpace(rowMicroBio))
                    biomaterialMatches = SameIdForGui(rowBio, itemBio) || SameIdForGui(rowMicroBio, itemBio);
            }

            if (!biomaterialMatches)
                return false;

            string itemLocalization = SafeTrim(item.localization_id);
            string rowLocalization = SafeTrim(row.localization_id);

            if (!string.IsNullOrWhiteSpace(itemLocalization) &&
                !string.IsNullOrWhiteSpace(rowLocalization) &&
                !SameIdForGui(itemLocalization, rowLocalization))
            {
                return false;
            }

            return true;
        }

        private DictionaryBiomaterials ResolveDictionaryBiomaterial(string biomaterialId, string fallbackName)
        {
            biomaterialId = SafeTrim(biomaterialId);

            DictionaryBiomaterials biom;
            if (!string.IsNullOrWhiteSpace(biomaterialId) &&
                laboratory?.Dicts?.Biomaterials != null &&
                laboratory.Dicts.Biomaterials.TryGetValue(biomaterialId, out biom) &&
                biom != null)
            {
                return biom;
            }

            return new DictionaryBiomaterials
            {
                id = biomaterialId,
                name = string.IsNullOrWhiteSpace(fallbackName) ? biomaterialId : fallbackName
            };
        }

        private BiomaterialInfoForGUI BuildBiomaterialInfoForGui(string serviceId, DictionaryBiomaterials biom, string forcedTransportId)
        {
            if (biom == null)
                return null;

            DictionaryTransport transport = null;

            if (!string.IsNullOrWhiteSpace(forcedTransportId) && laboratory?.Dicts?.Transport != null)
                laboratory.Dicts.Transport.TryGetValue(forcedTransportId.Trim(), out transport);

            if (transport == null)
                transport = ResolveTransport(serviceId, biom.id);

            string containerName = transport != null ? NormalizeContainerName(transport.name) : "не указан";
            string transportId = transport != null ? transport.id : string.Empty;

            return new BiomaterialInfoForGUI
            {
                BiomaterialId = biom.id,
                BiomaterialCode = biom.id,
                BiomaterialName = BuildBiomaterialDisplayName(biom.name, containerName),
                ContainerId = transportId,
                ContainerCode = transportId,
                ContainerName = containerName
            };
        }

        private static void AddBiomaterialInfoToGroup(BiomaterialGroupForGUI group, BiomaterialInfoForGUI info)
        {
            if (group == null || info == null || string.IsNullOrWhiteSpace(info.BiomaterialId))
                return;

            group.Biomaterials = group.Biomaterials ?? new List<BiomaterialInfoForGUI>();

            bool exists = group.Biomaterials.Any(x => x != null &&
                SameIdForGui(x.BiomaterialId, info.BiomaterialId) &&
                SameIdForGui(x.ContainerCode, info.ContainerCode));

            if (!exists)
                group.Biomaterials.Add(info);
        }

        private static void AddBiomaterialInfoToGroupByBiomaterialOnly(BiomaterialGroupForGUI group, BiomaterialInfoForGUI info)
        {
            if (group == null || info == null || string.IsNullOrWhiteSpace(info.BiomaterialId))
                return;

            group.Biomaterials = group.Biomaterials ?? new List<BiomaterialInfoForGUI>();

            bool exists = group.Biomaterials.Any(x => x != null && SameIdForGui(x.BiomaterialId, info.BiomaterialId));
            if (!exists)
                group.Biomaterials.Add(info);
        }

        private BiomaterialGroupForGUI BuildBiomaterialGroupForService(string serviceId)
        {
            var groups = BuildBiomaterialGroupsForService(serviceId, null);
            return groups.FirstOrDefault() ?? CreateEmptyRequiredBiomaterialGroup();
        }

        private BiomaterialGroupForGUI BuildBiomaterialGroupForProduct(GemotestOrderDetail details, int productIndex)
        {
            var groups = BuildBiomaterialGroupsForProduct(details, productIndex);
            return groups.FirstOrDefault() ?? CreateEmptyRequiredBiomaterialGroup();
        }

        private void RebuildBiomaterialGroups(GemotestOrderDetail details, OrderModelForGUI model)
        {
            if (model == null || model.ProductsInfo == null)
                return;

            foreach (var p in model.ProductsInfo)
            {
                if (p != null)
                    p.BiomaterialGroups.Clear();
            }

            for (int i = 0; i < model.ProductsInfo.Count; i++)
            {
                var productInfo = model.ProductsInfo[i];
                if (productInfo == null)
                    continue;

                var groups = BuildBiomaterialGroupsForProduct(details, i);
                NormalizeBiomaterialGroups(groups);

                foreach (var group in groups)
                    productInfo.BiomaterialGroups.Add(group);
            }
        }

        private void PrepareBiomaterialsForChooseForm(List<ProductInfoForGUI> products)
        {
            if (products == null)
                return;

            foreach (var product in products)
            {
                if (product == null)
                    continue;

                product.BiomaterialGroups.Clear();
                var groups = BuildBiomaterialGroupsForService(product.Id, null);
                NormalizeBiomaterialGroups(groups);

                foreach (var group in groups)
                    product.BiomaterialGroups.Add(group);
            }
        }


        private List<string> GetSelectedBiomaterialIds(ProductInfoForGUI product)
        {
            if (product?.BiomaterialGroups == null)
                return new List<string>();

            return product.BiomaterialGroups.Where(g => g?.BiomaterialsSelected != null).SelectMany(g => g.BiomaterialsSelected).Where(x => x != null && !string.IsNullOrWhiteSpace(x.BiomaterialId))
                .Select(x => x.BiomaterialId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void ApplySelectedBiomaterialsToAddedProduct(Order order, OrderModelForGUI model, int productIndex, List<string> selectedBioIds)
        {
            if (model?.ProductsInfo == null || productIndex < 0 || productIndex >= model.ProductsInfo.Count)
                return;

            if (selectedBioIds == null || selectedBioIds.Count == 0)
                return;

            var selectedSet = new HashSet<string>(selectedBioIds.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
            var product = model.ProductsInfo[productIndex];

            if (product?.BiomaterialGroups == null)
                return;

            foreach (var group in product.BiomaterialGroups)
            {
                if (group == null || group.Biomaterials == null)
                    continue;

                group.BiomaterialsSelected.Clear();

                foreach (var biom in group.Biomaterials)
                {
                    if (biom == null || string.IsNullOrWhiteSpace(biom.BiomaterialId))
                        continue;

                    if (!selectedSet.Contains(biom.BiomaterialId))
                        continue;

                    group.BiomaterialsSelected.Add(biom);

                    if (group.SelectOnlyOne)
                        break;
                }

                if (group.BiomaterialsSelected.Count == 0)
                    SetDefaultBiomaterialSelection(group, selectedBioIds);
            }
        }

        private void ApplyAutoInsertServices(GemotestOrderDetail details, OrderModelForGUI model)
        {
            if (details == null || model == null || model.ProductsInfo == null)
                return;

            HashSet<string> autoServiceIds = BuildAutoServiceIdsForSelectedProducts(model);

            foreach (string autoServiceId in autoServiceIds)
            {
                DictionaryService service;
                if (!TryGetDictionaryService(autoServiceId, out service))
                    continue;

                bool isCollectService = service.service_type == 4;
                bool collectEnabled = IsCollectBiomaterialByGemotestEnabled();

                DebugGemotestGui("AUTO_INSERT candidate: id=" + (service.id ?? "") +
                    "; code=" + (service.code ?? "") + "; name=" + (service.name ?? "") +
                    "; service_type=" + service.service_type.ToString() +
                    "; isCollectService=" + isCollectService.ToString() +
                    "; CollectBiomaterialByGemotest=" + collectEnabled.ToString());

                if (isCollectService && !collectEnabled)
                {
                    DebugGemotestGui("AUTO_INSERT skip: biomaterial collect service is disabled by system option: id=" + (service.id ?? ""));
                    continue;
                }

                if (ProductOrServiceExistsInModel(model, service.id))
                {
                    DebugGemotestGui("AUTO_INSERT skip: service already exists in model: id=" + (service.id ?? ""));
                    continue;
                }

                var detail = new GemotestProductDetail
                {
                    OrderProductGuid = details.Products.Count.ToString(),
                    ProductId = service.id ?? string.Empty,
                    ProductCode = service.code ?? string.Empty,
                    ProductName = service.name ?? string.Empty
                };

                details.Products.Add(detail);

                var productForGui = new ProductInfoForGUI
                {
                    OrderProductGuid = detail.OrderProductGuid,
                    Id = detail.ProductId,
                    Code = detail.ProductCode,
                    Name = detail.ProductName,
                    ProductGroupGuid = null
                };

                if (isCollectService)
                    model.ServicesInfo.Add(productForGui);
                else
                    model.ProductsInfo.Add(productForGui);

                DebugGemotestGui("AUTO_INSERT add: id=" + (service.id ?? "") +
                    "; target=" + (isCollectService ? "ServicesInfo" : "ProductsInfo"));
            }
        }

        private GemotestBlankReportDataSetV2 FillDataSetForBlankReport(Order _Order)
        {
            var details = _Order.OrderDetail as GemotestOrderDetail;
            if (details == null)
                throw new InvalidOperationException("OrderDetail не является GemotestOrderDetail.");

            var dataset = new GemotestBlankReportDataSetV2();

            FillBlankHeader(_Order, details, dataset);
            FillPatientParameters(_Order, details, dataset);
            FillProductsForBlank(details, dataset);

            return dataset;
        }

        private DataSetForReportGemotest FillDataSetForConsolidatedReport(Order order)
        {
            var details = order.OrderDetail as GemotestOrderDetail;
            if (details == null)
                throw new InvalidOperationException("OrderDetail не является GemotestOrderDetail.");

            var dataset = new DataSetForReportGemotest();

            FillConsolidatedHeader(order, dataset);
            FillConsolidatedParameters(order, details, dataset);

            return dataset;
        }

        private void FillConsolidatedHeader(Order order, DataSetForReportGemotest dataset)
        {
            DataRow row = dataset.ConsolidatedReportHeader.NewRow();

            DateTime orderDate = order.Date;
            if (orderDate <= DateTime.MinValue.AddDays(1))
                orderDate = DateTime.Now;

            row["ORDER_DATE"] = orderDate;
            row["ORDER_NUMBER"] = order.Number ?? string.Empty;
            row["PER_FIO"] = BuildPlainPatientFio(order);

            if (order.Patient != null && order.Patient.Birthday > DateTime.MinValue.AddDays(1))
                row["PER_BORN_DATE"] = order.Patient.Birthday;
            else
                row["PER_BORN_DATE"] = DateTime.Now.Date;

            if (order.Patient != null && order.Patient.Sex == Sex.Male)
                row["PER_SEX"] = "Муж";
            else if (order.Patient != null && order.Patient.Sex == Sex.Female)
                row["PER_SEX"] = "Жен";
            else
                row["PER_SEX"] = string.Empty;

            dataset.ConsolidatedReportHeader.Rows.Add(row);
        }

        private void FillConsolidatedParameters(Order order, GemotestOrderDetail details, DataSetForReportGemotest dataset)
        {
            if (details.Results == null || details.Results.Count == 0)
                return;

            int num = 0;

            foreach (var result in details.Results)
            {
                if (result == null)
                    continue;

                DataRow row = dataset.ConsolidatedReportParameters.NewRow();

                GemotestOrderDetail.GemotestProductDetail productFind = null;
                if (details.Products != null)
                {
                    if (!string.IsNullOrWhiteSpace(result.OrderProductGuid))
                    {
                        productFind = details.Products.FirstOrDefault(x =>
                            x != null &&
                            string.Equals(x.OrderProductGuid ?? "", result.OrderProductGuid ?? "", StringComparison.OrdinalIgnoreCase));
                    }

                    if (productFind == null && !string.IsNullOrWhiteSpace(result.ServiceId))
                    {
                        productFind = details.Products.FirstOrDefault(x =>
                            x != null &&
                            (
                                string.Equals(x.ProductId ?? "", result.ServiceId ?? "", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(x.ProductCode ?? "", result.ServiceId ?? "", StringComparison.OrdinalIgnoreCase)
                            ));
                    }
                }

                if (productFind != null)
                {
                    row["PROD_CODE"] = productFind.ProductCode ?? string.Empty;
                    row["PROD_NAME"] = productFind.ProductName ?? string.Empty;
                }
                else
                {
                    row["PROD_CODE"] = result.ServiceId ?? string.Empty;
                    row["PROD_NAME"] = !string.IsNullOrWhiteSpace(result.TestRusName)
                        ? result.TestRusName
                        : (result.Name ?? string.Empty);
                }

                var sampleFind = ResolveSampleForResult(details, result);

                if (sampleFind != null && !string.IsNullOrWhiteSpace(sampleFind.BiomName))
                    row["TEST_NAME"] = sampleFind.BiomName;
                else if (!string.IsNullOrWhiteSpace(result.SectionName))
                    row["TEST_NAME"] = result.SectionName;
                else
                    row["TEST_NAME"] = string.Empty;

                row["PARAM_NUMBER"] = num;
                row["PARAM_NAME"] = !string.IsNullOrWhiteSpace(result.TestRusName)
                    ? result.TestRusName
                    : (!string.IsNullOrWhiteSpace(result.Name) ? result.Name : (result.ServiceId ?? string.Empty));

                row["PARAM_UNIT"] = result.MeasurementUnit ?? string.Empty;
                row["PARAM_VALUE_TEXT"] = result.Value ?? string.Empty;
                row["PARAM_REF_TEXT"] = BuildRefText(result);

                row["PARAM_VALUE"] = ParseDoubleOrZero(result.Value);
                row["PARAM_MIN"] = ParseDoubleOrMin(result.RefMin);
                row["PARAM_MAX"] = ParseDoubleOrMax(result.RefMax);

                if ((double)row["PARAM_MIN"] == double.MinValue &&
                    (double)row["PARAM_MAX"] == double.MaxValue)
                {
                    row["PARAM_VALUE"] = 0.0;
                }

                dataset.ConsolidatedReportParameters.Rows.Add(row);
                num++;
            }
        }

        private GemotestSampleDetail ResolveSampleForResult(GemotestOrderDetail details, GemotestResultDetail result)
        {
            if (details == null || result == null || details.Samples == null || details.Samples.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(result.OrderProductGuid))
            {
                var byGuid = details.Samples.FirstOrDefault(x =>
                    x != null &&
                    x.OrderProductGuidList != null &&
                    x.OrderProductGuidList.Contains(result.OrderProductGuid));
                if (byGuid != null)
                    return byGuid;
            }

            if (!string.IsNullOrWhiteSpace(result.ServiceId))
            {
                var product = details.Products != null
                    ? details.Products.FirstOrDefault(x =>
                        x != null &&
                        (
                            string.Equals(x.ProductId ?? "", result.ServiceId ?? "", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(x.ProductCode ?? "", result.ServiceId ?? "", StringComparison.OrdinalIgnoreCase)
                        ))
                    : null;

                if (product != null)
                {
                    var byService = details.Samples.FirstOrDefault(x =>
                        x != null &&
                        x.OrderProductGuidList != null &&
                        x.OrderProductGuidList.Contains(product.OrderProductGuid));
                    if (byService != null)
                        return byService;
                }
            }

            return details.Samples.FirstOrDefault(x => x != null);
        }

        private string BuildRefText(GemotestResultDetail result)
        {
            if (result == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(result.RefText))
                return result.RefText;

            if (!string.IsNullOrWhiteSpace(result.RefRange))
                return result.RefRange;

            if (!string.IsNullOrWhiteSpace(result.RefMin) || !string.IsNullOrWhiteSpace(result.RefMax))
                return (result.RefMin ?? "") + " - " + (result.RefMax ?? "");

            return string.Empty;
        }

        private string BuildPlainPatientFio(Order order)
        {
            if (order == null || order.Patient == null)
                return string.Empty;

            return string.Format(
                "{0} {1} {2}",
                order.Patient.Surname ?? string.Empty,
                order.Patient.Name ?? string.Empty,
                order.Patient.Patronimic ?? string.Empty).Trim();
        }

        private double ParseDoubleOrZero(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0.0;

            double parsed;
            if (double.TryParse(
                value.Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator)
                     .Replace(",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator),
                NumberStyles.Any,
                CultureInfo.CurrentCulture,
                out parsed))
                return parsed;

            return 0.0;
        }

        private double ParseDoubleOrMin(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return double.MinValue;

            double parsed;
            if (double.TryParse(
                value.Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator)
                     .Replace(",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator),
                NumberStyles.Any,
                CultureInfo.CurrentCulture,
                out parsed))
                return parsed;

            return double.MinValue;
        }

        private double ParseDoubleOrMax(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return double.MaxValue;

            double parsed;
            if (double.TryParse(
                value.Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator)
                     .Replace(",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator),
                NumberStyles.Any,
                CultureInfo.CurrentCulture,
                out parsed))
                return parsed;

            return double.MaxValue;
        }

        private void FillBlankHeader(Order order, GemotestOrderDetail details, GemotestBlankReportDataSetV2 dataset)
        {
            DataRow row = dataset.BlankTable.NewRow();

            row["DateofFormation"] = order.Date.ToString("dd.MM.yyyy");
            row["DateSampling"] = order.Date.ToString("dd.MM.yyyy");
            row["ClinicName"] = GetClinicNameForBlank(details);
            row["OrderCode"] = order.Number ?? string.Empty;
            row["Pacient_FIO"] = BuildPatientFio(order);
            row["LaboratoryName"] = "Гемотест";
            row["OrderCodeBarcode"] = BuildOrderBarcodeBytes(order.Number ?? string.Empty);

            dataset.BlankTable.Rows.Add(row);
        }

        private void FillPatientParameters(Order order, GemotestOrderDetail details, GemotestBlankReportDataSetV2 dataset)
        {
            if (order.Patient != null)
            {
                AddPatientParameter(dataset, "Дата рождения:", order.Patient.Birthday.ToString("dd.MM.yyyy"));

                if (!string.IsNullOrWhiteSpace(order.Patient.Phone))
                    AddPatientParameter(dataset, "Телефон:", order.Patient.Phone);

                if (!string.IsNullOrWhiteSpace(order.Patient.EMail))
                    AddPatientParameter(dataset, "Email:", order.Patient.EMail);

                if (!string.IsNullOrWhiteSpace(order.Patient.SNILS))
                    AddPatientParameter(dataset, "СНИЛС:", order.Patient.SNILS);
            }

            if (details.Details == null)
                return;

            foreach (var detail in details.Details)
            {
                if (detail == null)
                    continue;

                if (string.IsNullOrWhiteSpace(detail.Value) && string.IsNullOrWhiteSpace(detail.DisplayValue))
                    continue;

                string caption = NormalizeFieldCaption(detail.Name, detail.Code);
                if (string.IsNullOrWhiteSpace(caption))
                    continue;

                string value = !string.IsNullOrWhiteSpace(detail.DisplayValue)
                    ? detail.DisplayValue
                    : (detail.Value ?? string.Empty);

                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (IsDuplicatePatientParam(dataset, caption, value))
                    continue;

                AddPatientParameter(dataset, caption, value);
            }
        }

        private void FillProductsForBlank(GemotestOrderDetail details, GemotestBlankReportDataSetV2 dataset)
        {
            if (details.Products == null || details.Products.Count == 0)
                return;

            foreach (var product in details.Products)
            {
                if (product == null)
                    continue;

                var linkedSamples = new List<GemotestSampleDetail>();

                if (details.Samples != null)
                {
                    linkedSamples = details.Samples
                        .Where(s => s != null &&
                                    s.OrderProductGuidList != null &&
                                    s.OrderProductGuidList.Contains(product.OrderProductGuid))
                        .ToList();
                }

                if (linkedSamples.Count == 0)
                {
                    DataRow row = dataset.Products.NewRow();
                    row["ProductCode"] = product.ProductCode ?? string.Empty;
                    row["ProductName"] = product.ProductName ?? string.Empty;
                    row["SubOrderInfo"] = "Образец не определён";
                    dataset.Products.Rows.Add(row);
                    continue;
                }

                foreach (var sample in linkedSamples)
                {
                    DataRow row = dataset.Products.NewRow();
                    row["ProductCode"] = product.ProductCode ?? string.Empty;
                    row["ProductName"] = product.ProductName ?? string.Empty;
                    row["SubOrderInfo"] = BuildSubOrderInfo(sample);
                    dataset.Products.Rows.Add(row);
                }
            }
        }

        private string BuildSubOrderInfo(GemotestSampleDetail sample)
        {
            if (sample == null)
                return "Информация по образцу отсутствует";

            var lines = new List<string>();

            Action<string> addLine = text =>
            {
                if (string.IsNullOrWhiteSpace(text))
                    return;

                text = text.Trim();

                if (!lines.Any(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase)))
                    lines.Add(text);
            };

            var line1Parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(sample.Barcode))
                line1Parts.Add("ШК: " + sample.Barcode);

            if (!string.IsNullOrWhiteSpace(sample.SampleIdentifier) && !string.Equals(sample.SampleIdentifier, sample.Barcode, StringComparison.OrdinalIgnoreCase))
            {
                line1Parts.Add("Идентификатор: " + sample.SampleIdentifier);
            }

            if (line1Parts.Count > 0)
                addLine(string.Join(", ", line1Parts));

            if (!string.IsNullOrWhiteSpace(sample.SampleId))
                addLine("ID типа пробы в ЛИС: " + sample.SampleId);

            var materialParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(sample.BiomName))
                materialParts.Add("биоматериал: " + sample.BiomName);

            if (!string.IsNullOrWhiteSpace(sample.ContName))
                materialParts.Add("контейнер: " + sample.ContName);

            if (materialParts.Count > 0)
                addLine("Материал: " + string.Join(", ", materialParts));

            if (!string.IsNullOrWhiteSpace(sample.LocalizationName))
                addLine("Локализация: " + sample.LocalizationName);

            if (!string.IsNullOrWhiteSpace(sample.LabCenterId))
                addLine("Лаборатория-исполнитель: " + sample.LabCenterId);

            string role = sample.SampleRole;

            if (string.IsNullOrWhiteSpace(role))
                role = "обычная рабочая проба";

            addLine("Тип образца: " + role);

            if (!string.IsNullOrWhiteSpace(sample.SampleAction))
                addLine("Действие: " + sample.SampleAction);

            if (sample.IsAliquot && !string.IsNullOrWhiteSpace(sample.PrimarySampleIdentifier))
                addLine("Родительская проба: " + sample.PrimarySampleIdentifier);

            if (sample.IsUtilize || sample.HasUtilizationService)
                addLine("Особенность: есть признак утилизации");

            if (sample.HasRefusedService && !string.Equals(sample.SampleRole ?? "", "родительская проба для аликвоты", StringComparison.OrdinalIgnoreCase))
            {
                addLine("Особенность: часть услуги не выполняется на этой пробе напрямую");
            }

            if (!string.IsNullOrWhiteSpace(sample.SampleDescription) && !IsTechnicalSampleDescription(sample.SampleDescription))
            {
                addLine("Комментарий ЛИС: " + sample.SampleDescription);
            }

            if (lines.Count == 0)
                return "Информация по образцу отсутствует";

            return string.Join(Environment.NewLine, lines);
        }

        private static bool IsTechnicalSampleDescription(string value)
        {
            value = (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(value))
                return true;

            if (value.IndexOf("_", StringComparison.Ordinal) >= 0)
                return true;

            if (value.StartsWith("Конт", StringComparison.OrdinalIgnoreCase) &&
                value.Length <= 30 &&
                value.IndexOf(" ", StringComparison.Ordinal) < 0)
            {
                return true;
            }

            return false;
        }
        private void AddPatientParameter(GemotestBlankReportDataSetV2 dataset, string name, string value)
        {
            if (dataset == null || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
                return;

            DataRow row = dataset.PatientParameters.NewRow();
            row["PatientParamName"] = name;
            row["PatientParamValue"] = value;
            dataset.PatientParameters.Rows.Add(row);
        }

        private string BuildPatientFio(Order order)
        {
            if (order == null || order.Patient == null)
                return string.Empty;

            string fio = string.Format("{0} {1} {2}", order.Patient.Surname ?? string.Empty, order.Patient.Name ?? string.Empty, order.Patient.Patronimic ?? string.Empty).Trim();

            string sex = string.Empty;
            if (order.Patient.Sex == Sex.Male)
                sex = "Муж";
            else if (order.Patient.Sex == Sex.Female)
                sex = "Жен";

            if (!string.IsNullOrWhiteSpace(sex))
                return fio + " (" + sex + ")";

            return fio;
        }

        private string GetClinicNameForBlank(GemotestOrderDetail details)
        {
            if (!string.IsNullOrWhiteSpace(details.PriceListName))
                return details.PriceListName;

            if (!string.IsNullOrWhiteSpace(details.PriceList))
                return details.PriceList;

            if (!string.IsNullOrWhiteSpace(details.PriceListCode))
                return details.PriceListCode;

            return "Клиника не указана";
        }

        private byte[] BuildOrderBarcodeBytes(string orderNumber)
        {
            if (string.IsNullOrWhiteSpace(orderNumber))
                return new byte[0];

            var options = new EncodingOptions
            {
                Height = 50,
                Width = 260,
                Margin = 2
            };

            var writer = new BarcodeWriter
            {
                Format = BarcodeFormat.CODE_128,
                Options = options
            };

            using (Bitmap bitmap = writer.Write(orderNumber))
            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        private string NormalizeFieldCaption(string name, string code)
        {
            string caption = !string.IsNullOrWhiteSpace(name) ? name.Trim() : (code ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(caption))
                return string.Empty;

            caption = caption
                .Replace("Пациент: ", "")
                .Replace("Продукт: ", "")
                .Replace("Заявка: ", "")
                .Replace("Образец: ", "");

            caption = caption.Trim();

            if (caption.Length == 0)
                return string.Empty;

            return char.ToUpper(caption[0]) + caption.Substring(1) + ":";
        }

        private bool IsDuplicatePatientParam(GemotestBlankReportDataSetV2 dataset, string caption, string value)
        {
            foreach (DataRow row in dataset.PatientParameters.Rows)
            {
                string existingCaption = row["PatientParamName"] as string;
                string existingValue = row["PatientParamValue"] as string;

                if (string.Equals(existingCaption, caption, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existingValue, value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
