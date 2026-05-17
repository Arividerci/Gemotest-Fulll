using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Xml;
using ContainerMarker.Common;
using Laboratory.Gemotest.GemotestRequests;
using Laboratory.Gemotest.Options;
using SiMed.Clinic;
using SiMed.Laboratory;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Laboratory.Gemotest.SourseClass;
using static Laboratory.Gemotest.SourseClass.GemotestOrderDetail;
using Laboratory.Gemotest.GUI;
namespace Laboratory.Gemotest
{

    public class LaboratoryGemotest : ILaboratory
    {
        List<DictionaryService> ProductsGemotest;

        public List<ProductGemotest> product;

        public GemotestService Gemotest;
        public ProductsCollection AllProducts { get; set; }
        public LocalOptions LocalOptions { get; set; }
        public SystemOptions Options { get; set; }
        public Dictionaries Dicts { get; } = new Dictionaries();

        private Exception last_exception = new Exception("неизвестная ошибка");

        private INumerator numerator;

        private LaboratoryGemotestGUI laboratoryGUI;
        public LaboratoryType GetLaboratoryType()
        {
            return (LaboratoryType)24;

        }

        private void EnsureProductsLoaded()
        {
            if (ProductsGemotest != null) return;

            if (Gemotest == null)
            {
                throw new InvalidOperationException("Gemotest не инициализирован. Вызовите SetOptions сначала.");
            }

            if (Dicts.Directory == null || Dicts.Directory.Count == 0)
            {
                bool unpackSuccess = Dicts.Unpack(Gemotest.filePath);
                if (!unpackSuccess)
                {
                    ProductsGemotest = new List<DictionaryService>();
                    return;
                }

            }

            string dirPath = Path.Combine(Gemotest.filePath, "Directory.xml");
            if (File.Exists(dirPath))
            {
                string dirContent = File.ReadAllText(dirPath);
                ProductsGemotest = DictionaryService.Parse(dirContent);
            }
            else
            {
                ProductsGemotest = new List<DictionaryService>();
            }

            product = ProductsGemotest
             .Where(service =>
                 !service.is_blocked &&
                 service.service_type != 3 &&
                 service.service_type != 4 &&
                 !string.IsNullOrEmpty(service.id) &&
                 !string.IsNullOrEmpty(service.code) &&
                 !string.IsNullOrEmpty(service.name))
             .Select(service => new ProductGemotest(service, "", Dicts))
             .ToList();
        }

        public ProductsCollection GetProducts()
        {
            EnsureProductsLoaded();

            ProductsCollection pC = new ProductsCollection();

            foreach (var p in product)
            {
                if (p.IsBlocked)
                    continue;

                if (p.ServiceType == 3 || p.ServiceType == 4)
                    continue;

                if (string.IsNullOrEmpty(p.ID) || string.IsNullOrEmpty(p.Code) || string.IsNullOrEmpty(p.Name))
                    continue;

                pC.Add(new Product
                {
                    ID = p.ID,
                    Code = p.Code,
                    Name = p.Name,
                    Duration = p.Duration,
                    DurationInfo = !string.IsNullOrWhiteSpace(p.DurationInfo)
                        ? p.DurationInfo
                        : (p.Duration > 0 ? $"{p.Duration} дн." : string.Empty)
                });
            }

            return pC;
        }


        public Product ChooseProduct(Product _SourceProduct = null)
        {

            EnsureProductsLoaded();

            GemotestChoiceOfProductForm form = new GemotestChoiceOfProductForm(_SourceProduct, product);
            form.ShowDialog();
            return form.SelectedProduct;
        }


        public BaseOrderDetail CreateOrderDetail() { return new GemotestOrderDetail(); }

        public void FillDefaultOrderDetail(BaseOrderDetail _OrderDetail, OrderItemsCollection _Items)
        {
            var details = (GemotestOrderDetail)_OrderDetail;

            details.Products.Clear();

            int index = 0;
            foreach (var item in _Items)
            {
                var prod = item.Product;

                details.Products.Add(new GemotestProductDetail
                {
                    OrderProductGuid = index.ToString(),
                    ProductId = prod.ID,
                    ProductCode = prod.Code,
                    ProductName = prod.Name
                });

                index++;
            }

            details.Dicts = Dicts;
            ApplyPriceListToDetails(details);
            details.AddBiomaterialsFromProducts();
        }

        private bool HasProductsRequiringGemotestBiomaterialCollection(
            List<GemotestProductDetail> products,
            out string warningText)
        {
            warningText = string.Empty;

            if (products == null || products.Count == 0)
                return false;

            List<string> blockedLines = new List<string>();

            foreach (GemotestProductDetail productDetail in products)
            {
                if (productDetail == null || string.IsNullOrWhiteSpace(productDetail.ProductId))
                    continue;

                List<DictionaryService> requiredCollectServices = GetBiomaterialCollectAutoServicesForProduct(productDetail.ProductId);

                if (requiredCollectServices == null || requiredCollectServices.Count == 0)
                    continue;

                blockedLines.Add("• " + FormatProductDetailCaption(productDetail));

                foreach (DictionaryService service in requiredCollectServices)
                {
                    if (service == null)
                        continue;

                    blockedLines.Add("  требуется: " + FormatDictionaryServiceCaption(service));
                }
            }

            if (blockedLines.Count == 0)
                return false;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Заказ не может быть оформлен с текущими системными настройками.");
            sb.AppendLine();
            sb.AppendLine("Для выбранных исследований Гемотест требует автодобавляемые услуги забора биоматериала лабораторией, но галочка \"Забор биоматериалов осуществляет лаборатория Гемотест\" выключена.");
            sb.AppendLine();
            sb.AppendLine("Исследования не добавлены:");

            foreach (string line in blockedLines)
                sb.AppendLine(line);

            sb.AppendLine();
            sb.AppendLine("Включите эту настройку или уберите из заказа исследования, которые требуют забора биоматериала лабораторией.");

            warningText = sb.ToString();
            return true;
        }

        private List<DictionaryService> GetBiomaterialCollectAutoServicesForProduct(string parentServiceId)
        {
            List<DictionaryService> result = new List<DictionaryService>();

            parentServiceId = (parentServiceId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(parentServiceId))
                return result;

            if (Dicts == null || Dicts.ServiceAutoInsert == null || Dicts.Directory == null)
                return result;

            List<DictionaryServiceAutoInsert> rows;
            if (!Dicts.ServiceAutoInsert.TryGetValue(parentServiceId, out rows) || rows == null)
                return result;

            HashSet<string> addedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DictionaryServiceAutoInsert row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.auto_service_id))
                    continue;

                if (row.archive != 0)
                    continue;

                string autoServiceId = (row.auto_service_id ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(autoServiceId) || addedIds.Contains(autoServiceId))
                    continue;

                DictionaryService service;
                if (!Dicts.Directory.TryGetValue(autoServiceId, out service) || service == null)
                    continue;

                if (service.is_blocked || service.service_type != 4)
                    continue;

                result.Add(service);
                addedIds.Add(autoServiceId);
            }

            return result;
        }

        private bool IsBiomaterialCollectService(string serviceId)
        {
            serviceId = (serviceId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(serviceId) || Dicts == null || Dicts.Directory == null)
                return false;

            DictionaryService service;
            return Dicts.Directory.TryGetValue(serviceId, out service) && service != null && service.service_type == 4;
        }

        private void RemoveDisabledBiomaterialCollectServices(GemotestOrderDetail details, string stage)
        {
            if (details == null || details.Products == null)
                return;

            if (Options != null && Options.CollectBiomaterialByGemotest)
                return;

            int removed = 0;

            for (int i = details.Products.Count - 1; i >= 0; i--)
            {
                GemotestProductDetail product = details.Products[i];
                if (product == null)
                    continue;

                if (!IsBiomaterialCollectService(product.ProductId))
                    continue;

                string message = "Гемотест: удалена услуга забора биоматериала при выключенной настройке CollectBiomaterialByGemotest. Stage=" + (stage ?? string.Empty) +
                    "; id=" + (product.ProductId ?? string.Empty) +
                    "; code=" + (product.ProductCode ?? string.Empty) +
                    "; name=" + (product.ProductName ?? string.Empty);

                try { SiMed.Clinic.Logger.LogEvent.SaveErrorToLog(message, "Gemotest"); } catch { }
                try { Console.WriteLine(message); } catch { }

                details.Products.RemoveAt(i);
                removed++;
            }

            if (removed > 0)
            {
                for (int i = 0; i < details.Products.Count; i++)
                {
                    if (details.Products[i] != null)
                        details.Products[i].OrderProductGuid = i.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        private static string FormatProductDetailCaption(GemotestProductDetail productDetail)
        {
            if (productDetail == null)
                return string.Empty;

            string code = productDetail.ProductCode ?? string.Empty;
            string name = productDetail.ProductName ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(name))
                return code + " | " + name;

            if (!string.IsNullOrWhiteSpace(name))
                return name;

            if (!string.IsNullOrWhiteSpace(code))
                return code;

            return productDetail.ProductId ?? string.Empty;
        }

        private static string FormatDictionaryServiceCaption(DictionaryService service)
        {
            if (service == null)
                return string.Empty;

            string code = service.code ?? string.Empty;
            string name = service.name ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(name))
                return code + " | " + name;

            if (!string.IsNullOrWhiteSpace(name))
                return name;

            if (!string.IsNullOrWhiteSpace(code))
                return code;

            return service.id ?? string.Empty;
        }

        private void ApplyPriceListToDetails(GemotestOrderDetail details)
        {
            if (details == null)
                return;

            if (!string.IsNullOrWhiteSpace(details.PriceListCode))
            {
                if (Options != null && Options.PriceLists != null && Options.PriceLists.Count > 0)
                {
                    var existing = Options.PriceLists.FirstOrDefault(x =>
                        x != null &&
                        !string.IsNullOrWhiteSpace(x.ContractorCode) &&
                        string.Equals(x.ContractorCode.Trim(), details.PriceListCode.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        details.PriceList = existing.Name ?? details.PriceList ?? "";
                        details.PriceListName = existing.Name ?? details.PriceListName ?? "";
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(details.PriceListName))
                            details.PriceListName = details.PriceList ?? "";
                    }
                }

                return;
            }

            if (Options != null && Options.PriceLists != null && Options.PriceLists.Count == 1)
            {
                var pl = Options.PriceLists[0];
                details.PriceList = pl.Name ?? "";
                details.PriceListName = pl.Name ?? "";
                details.PriceListCode = pl.ContractorCode ?? "";
                return;
            }

            if (Options != null && Options.PriceLists != null && Options.PriceLists.Count > 1)
            {
                details.PriceList = details.PriceList ?? "";
                details.PriceListName = details.PriceListName ?? "";
                details.PriceListCode = details.PriceListCode ?? "";
                return;
            }

            var code = Options != null ? (Options.Contractor_Code ?? "") : "";
            var name = Options != null ? (Options.Contractor ?? "") : "";

            details.PriceList = name;
            details.PriceListName = name;
            details.PriceListCode = code;
        }

        public bool CreateOrder(Order _Order)
        {
            var status = _Order.State;

            if (_Order != null && status != OrderState.NotSended)
            {
                ResultsCollection showResults = new ResultsCollection();
                return ShowOrder(_Order, true, ref showResults);
            }
            if (laboratoryGUI == null)
            {
                if (!Init())
                {
                    MessageBox.Show("Ошибка инициализации модуля Гемотест", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }

            GemotestOrderDetail details = (GemotestOrderDetail)_Order.OrderDetail;
            if (details == null)
            {
                last_exception = new Exception("OrderDetail не задан (ожидался GemotestOrderDetail).");
                MessageBox.Show(last_exception.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            if (details.Products == null)
                details.Products = new List<GemotestOrderDetail.GemotestProductDetail>();

            if (details.Products.Count == 0)
            {
                FillDefaultOrderDetail(details, _Order.Items);

                RemoveDisabledBiomaterialCollectServices(details, "CreateOrder: after FillDefaultOrderDetail");
            }
            else
            {
                details.Dicts = Dicts;
                ApplyPriceListToDetails(details);
                RemoveDisabledBiomaterialCollectServices(details, "CreateOrder: before RebuildBiomaterialsKeepSelection");
                RebuildBiomaterialsKeepSelection(details);
            }

            details.DeleteObsoleteDetails();

            bool readOnly = _Order.State != OrderState.NotSended;

            ResultsCollection currentResults = new ResultsCollection();
            OrderModelForGUI model = new OrderModelForGUI();

            if (!laboratoryGUI.CreateOrderModelForGUI(readOnly, _Order, ref currentResults, ref model))
            {
                MessageBox.Show(GetLastException().Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            FormLaboratoryOrder form = new FormLaboratoryOrder(_Laboratory: this,
                                                              _LaboratoryGUI: laboratoryGUI,
                                                              _Order: _Order,
                                                              _FormCaption: "Гемотест: оформление заказа",
                                                              _ResultsCollection: ref currentResults,
                                                              _OrderModel: ref model,
                                                              _ReadOnly: readOnly);

            var res = form.ShowDialog();

            if (res == DialogResult.OK)
            {
                if (!readOnly)
                {
                    if (!laboratoryGUI.SaveOrderModelForGUIToDetails(_Order, model))
                        MessageBox.Show($"Ошибка сохранения деталей заказа: {GetLastException().Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            if (_Order.State != status || res == DialogResult.OK)
                return true;

            return false;
        }

        public bool ShowOrder(Order _Order, bool _bReadOnly, ref ResultsCollection _Results)
        {
            var status = _Order.State;

            if (laboratoryGUI == null)
            {
                if (!Init())
                {
                    MessageBox.Show("Ошибка инициализации модуля Гемотест", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }

            GemotestOrderDetail details = _Order.OrderDetail as GemotestOrderDetail;
            if (details == null)
            {
                last_exception = new Exception("OrderDetail не задан (ожидался GemotestOrderDetail).");
                MessageBox.Show(last_exception.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            details.Dicts = Dicts;
            ApplyPriceListToDetails(details);
            RebuildBiomaterialsKeepSelection(details);
            details.DeleteObsoleteDetails();

            bool readOnly = _bReadOnly || _Order.State != OrderState.NotSended;

            ResultsCollection currentResults = _Results ?? new ResultsCollection();
            OrderModelForGUI model = new OrderModelForGUI();

            if (!laboratoryGUI.CreateOrderModelForGUI(readOnly, _Order, ref currentResults, ref model))
            {
                MessageBox.Show(GetLastException().Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            FormLaboratoryOrder form = new FormLaboratoryOrder(
                _Laboratory: this,
                _LaboratoryGUI: laboratoryGUI,
                _Order: _Order,
                _FormCaption: "Гемотест: просмотр заказа",
                _ResultsCollection: ref currentResults,
                _OrderModel: ref model,
                _ReadOnly: readOnly);

            var res = form.ShowDialog();

            if (res == DialogResult.OK && !readOnly)
            {
                if (!laboratoryGUI.SaveOrderModelForGUIToDetails(_Order, model))
                {
                    MessageBox.Show($"Ошибка сохранения деталей заказа: {GetLastException().Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }

            _Results = currentResults;
            return _Order.State != status || res == DialogResult.OK;
        }

        public bool SendOrder(Order _Order)
        {
            last_exception = null;
            try
            {
                if (_Order == null)
                    throw new InvalidOperationException("Заказ не задан.");

                if (_Order.State != OrderState.Prepared)
                {
                    var msg = $"Попытка отправки заказа. Заказ должен быть в состоянии {OrderState.Prepared}, а сейчас {_Order.State}.";
                    last_exception = new Exception(msg);
                    SiMed.Clinic.Logger.LogEvent.SaveErrorToLog(msg, "Gemotest");
                    return false;
                }

                if (!IsGemotestOptionsValid(Options))
                    throw new InvalidOperationException("Опции Gemotest не заполнены (Url/Login/Password/Contractor_Code/Salt).");

                var details = _Order.OrderDetail as GemotestOrderDetail;
                if (details == null)
                    throw new InvalidOperationException("OrderDetail не является GemotestOrderDetail.");

                details.Dicts = Dicts;
                RemoveDisabledBiomaterialCollectServices(details, "SendOrder: before send");

                if (details.Products == null || details.Products.Count == 0)
                    throw new InvalidOperationException("В заказе нет ни одной услуги (details.Products пуст).");

                var contractorCode = !string.IsNullOrEmpty(details.PriceListCode) ? details.PriceListCode : Options.Contractor_Code;

                var sender = new GemotestOrderSender(Options.UrlAdress, contractorCode, Options.Salt, Options.Login, Options.Password);

                string errorMessage;
                if (!sender.CreateOrder(_Order, out errorMessage))
                {
                    if (!string.IsNullOrEmpty(errorMessage))
                        last_exception = new Exception($"Ошибка отправки заказа в Гемотест: {errorMessage}");
                    else
                        last_exception = new Exception("Ошибка отправки заказа в Гемотест (без текста ошибки)");

                    SiMed.Clinic.Logger.LogEvent.SaveErrorToLog(last_exception.Message, "Gemotest");
                    return false;
                }
                else
                {
                    if (LocalOptions.PrintBlankAtOnce)
                    {
                        ResultsCollection results = null;
                        laboratoryGUI.PrintLaboratoryDocument(_Order, ref results, new DocumentInfoForGUI() { DocType = LaboratoryPrintDocumentType.Blank }, false);
                    }

                    if (LocalOptions.PrintStikersAtOnce)
                    {
                        List<SampleInfoForGUI> samplesForGui = laboratoryGUI.BuildStickerSamplesForGui(_Order);
                        laboratoryGUI.PrintStikers(_Order, samplesForGui);
                    }
                }

                _Order.State = OrderState.Commited;
                return true;
            }
            catch (Exception ex)
            {
                last_exception = ex;
                SiMed.Clinic.Logger.LogEvent.SaveErrorToLog(ex.Message, "Gemotest");
                return false;
            }
        }

        public void PrintOrderForms(Order _Order)
        {
            ResultsCollection resultsCollection = new ResultsCollection();
            OrderModelForGUI orderModelForGUI = new OrderModelForGUI();
            laboratoryGUI.CreateOrderModelForGUI(true, _Order, ref resultsCollection, ref orderModelForGUI);
        }


        public bool ShowSystemOptions(ref string _SystemOptions)
        {
            GemotestSystemOptionsForm optionsSystem = new GemotestSystemOptionsForm(_SystemOptions);

            if (optionsSystem.ShowDialog() == DialogResult.OK)
            {
                Options = optionsSystem.Options;
                _SystemOptions = Options.Pack();

                if (laboratoryGUI != null)
                {
                    laboratoryGUI.SetOptions(this, GetProducts(), LocalOptions, Options, numerator);
                }

                return true;
            }

            return false;
        }

        public bool ShowLocalOptions(ref string _LocalOptions)
        {
            LocalOptionsForm Local_options = new LocalOptionsForm(_LocalOptions);
            if (Local_options.ShowDialog() == DialogResult.OK)
            {
                _LocalOptions = Local_options.Options.Pack();
                return true;
            }
            return false;
        }

        private void RebuildBiomaterialsKeepSelection(GemotestOrderDetail details)
        {
            if (details == null)
                return;

            if (details.Products == null)
                details.Products = new List<GemotestOrderDetail.GemotestProductDetail>();

            if (details.BioMaterials == null)
                details.BioMaterials = new List<GemotestBioMaterial>();

            var oldSelectedByProductIndex = new Dictionary<int, HashSet<string>>();

            for (int productIndex = 0; productIndex < details.Products.Count; productIndex++)
            {
                var selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var bio in details.BioMaterials)
                {
                    if (bio == null || string.IsNullOrWhiteSpace(bio.Id))
                        continue;

                    if (bio.Chosen.Contains(productIndex) || bio.Mandatory.Contains(productIndex))
                        selectedIds.Add(bio.Id);
                }

                if (selectedIds.Count > 0)
                    oldSelectedByProductIndex[productIndex] = selectedIds;
            }

            details.BioMaterials.Clear();
            details.AddBiomaterialsFromProducts();

            foreach (var pair in oldSelectedByProductIndex)
            {
                int productIndex = pair.Key;
                HashSet<string> oldSelectedIds = pair.Value;

                if (productIndex < 0 || productIndex >= details.Products.Count)
                    continue;

                var linkedForProduct = details.BioMaterials.Where(b => b != null && (b.Mandatory.Contains(productIndex) || b.Chosen.Contains(productIndex) || b.Another.Contains(productIndex))).ToList();

                if (linkedForProduct.Count == 0)
                    continue;

                var validSelectedIds = new HashSet<string>(linkedForProduct.Where(b => b != null && oldSelectedIds.Contains(b.Id)).Select(b => b.Id), StringComparer.OrdinalIgnoreCase);

                if (validSelectedIds.Count == 0)
                    continue;

                foreach (var bio in linkedForProduct)
                {
                    if (bio == null)
                        continue;

                    if (bio.Mandatory.Contains(productIndex))
                    {
                        bio.Chosen.Remove(productIndex);
                        bio.Another.Remove(productIndex);
                        continue;
                    }

                    bio.Chosen.Remove(productIndex);
                    bio.Another.Remove(productIndex);

                    if (validSelectedIds.Contains(bio.Id))
                    {
                        if (!bio.Chosen.Contains(productIndex))
                            bio.Chosen.Add(productIndex);
                    }
                    else
                    {
                        if (!bio.Another.Contains(productIndex))
                            bio.Another.Add(productIndex);
                    }
                }
            }
        }

        public void SetOptions(string _SystemOptions, string _LocalOptions)
        {
            if (!string.IsNullOrWhiteSpace(_SystemOptions))
            {
                Options = (SystemOptions)new SystemOptions().Unpack(_SystemOptions);
            }
            else
            {
                if (Options == null) Options = new SystemOptions();

            }

            if (!string.IsNullOrWhiteSpace(_LocalOptions))
            {
                LocalOptions = (LocalOptions)new LocalOptions().Unpack(_LocalOptions);
            }
            else
            {

                if (LocalOptions == null) LocalOptions = new LocalOptions();
            }

            if (IsGemotestOptionsValid(Options))
            {
                string initContractorName;
                string initContractorCode;
                ResolveContractorForServiceInit(Options, out initContractorName, out initContractorCode);

                Gemotest = new GemotestService(Options.UrlAdress, Options.Login, Options.Password, initContractorName, initContractorCode, Options.Salt);
            }
            else
            {
                Gemotest = null;
            }
        }
        public bool Init()
        {
            last_exception = null;
            try
            {
                if (Options == null)
                    return false;

                if (!IsGemotestOptionsValid(Options))
                    return false;

                bool inited = false;
                foreach (var pl in GetInitCandidates())
                {
                    if (TryInitWithPriceList(pl))
                    {
                        inited = true;
                        break;
                    }
                }

                if (!inited)
                    return false;

                ProductsGemotest = null;
                product = null;
                laboratoryGUI = new LaboratoryGemotestGUI();

                EnsureProductsLoaded();

                laboratoryGUI.SetOptions(this, GetProducts(), LocalOptions, Options, numerator);

                SiMed.Clinic.Logger.LogEvent.RemoveOldFilesFromLog("Gemotest", 30);
                return true;
            }
            catch (Exception exc)
            {
                last_exception = exc;
                return false;
            }
        }

        private static readonly string[] DictionaryFiles = new string[]
        {
            "Biomaterials.xml",
            "Transport.xml",
            "Localization.xml",
            "Service_group.xml",
            "Service_parameters.xml",
            "Directory.xml",
            "Tests.xml",
            "Samples_services.xml",
            "Samples.xml",
            "Processing_rules.xml",
            "Marketing_complex_composition.xml",
            "Services_group_analogs.xml",
            "Service_auto_insert.xml",
            "Services_supplementals.xml"
        };

        private bool RefreshDictionariesAtInit()
        {
            if (Gemotest == null)
                return false;

            string root = Gemotest.filePath;

            bool oldLoaded = false;
            try { oldLoaded = Dicts.Unpack(root); } catch { oldLoaded = false; }

            string backupDir = Path.Combine(root, "_backup");

            try
            {
                BackupDictionaryFiles(root, backupDir);
                ForceDictionaryFilesOutdated(root, 2);

                bool downloaded = Gemotest.get_all_dictionary();

                if (downloaded)
                {
                    bool unpackOk = Dicts.Unpack(root);
                    if (unpackOk)
                    {
                        DeleteDirectorySafe(backupDir);
                        return true;
                    }
                }

                RestoreDictionaryFiles(root, backupDir);

                bool restoredOk = Dicts.Unpack(root);
                if (restoredOk)
                    return true;

                return oldLoaded;
            }
            catch (Exception ex)
            {
                last_exception = ex;
                try
                {
                    RestoreDictionaryFiles(root, backupDir);
                    if (Dicts.Unpack(root))
                        return true;
                }
                catch { }

                return oldLoaded;
            }
        }

        private static void BackupDictionaryFiles(string root, string backupDir)
        {
            Directory.CreateDirectory(backupDir);

            foreach (string name in DictionaryFiles)
            {
                string src = Path.Combine(root, name);
                if (!File.Exists(src)) continue;

                string dst = Path.Combine(backupDir, name);
                File.Copy(src, dst, true);
            }
        }

        private static void RestoreDictionaryFiles(string root, string backupDir)
        {
            if (!Directory.Exists(backupDir))
                return;

            foreach (string name in DictionaryFiles)
            {
                string src = Path.Combine(backupDir, name);
                if (!File.Exists(src)) continue;

                string dst = Path.Combine(root, name);
                File.Copy(src, dst, true);
            }
        }

        private static void ForceDictionaryFilesOutdated(string root, int daysBack)
        {
            DateTime ts = DateTime.Now.AddDays(-Math.Abs(daysBack));

            foreach (string name in DictionaryFiles)
            {
                string f = Path.Combine(root, name);
                if (!File.Exists(f)) continue;

                try { File.SetLastWriteTime(f, ts); } catch { }
            }
        }

        private static void DeleteDirectorySafe(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch { }
        }

        public void SetNumerator(INumerator _Numerator)
        {
            numerator = _Numerator;
        }

        private static bool IsGemotestOptionsValid(SystemOptions o)
        {
            return o != null && !string.IsNullOrWhiteSpace(o.UrlAdress) && !string.IsNullOrWhiteSpace(o.Login) && !string.IsNullOrWhiteSpace(o.Password) &&
                   !string.IsNullOrWhiteSpace(o.Salt) && (!string.IsNullOrWhiteSpace(o.Contractor_Code) ||
                       (o.PriceLists != null && o.PriceLists.Any(x => x != null && !string.IsNullOrWhiteSpace(x.ContractorCode))));
        }

        public bool CheckResult(Order _Order, ref ResultsCollection _Results)
        {
            last_exception = null;

            try
            {
                if (_Results == null)
                    _Results = new ResultsCollection();

                if (_Order == null)
                    throw new InvalidOperationException("Заказ не задан.");

                _Order.SampleError = false;

                if (laboratoryGUI == null)
                {
                    if (!Init())
                    {
                        MessageBox.Show("Ошибка инициализации модуля Гемотест", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                }

                if (!IsGemotestOptionsValid(Options))
                    throw new InvalidOperationException("Опции Gemotest не заполнены (Url/Login/Password/Contractor_Code/Salt).");

                var details = _Order.OrderDetail as GemotestOrderDetail;
                if (details == null)
                    throw new InvalidOperationException("OrderDetail не является GemotestOrderDetail.");

                string contractorCode = !string.IsNullOrWhiteSpace(details.PriceListCode)
                    ? details.PriceListCode
                    : (Options.Contractor_Code ?? string.Empty);

                if (string.IsNullOrWhiteSpace(contractorCode))
                    throw new InvalidOperationException("Не определен contractorCode для запроса результатов.");

                string extNum = _Order.Number ?? string.Empty;
                if (string.IsNullOrWhiteSpace(extNum))
                    throw new InvalidOperationException("У заказа отсутствует внешний номер (_Order.Number). Запросить результаты невозможно.");

                OrderState statusBefore = _Order.State;

                string hash = BuildContractorHash(contractorCode, Options.Salt);
                string requestXml = BuildGetAnalysisResultEnvelope(contractorCode, hash, "", extNum);

                string responseXml = SendSoapRequest(Options.UrlAdress, Options.Login, Options.Password, "get_analysis_result", requestXml);

                details.ResultsRawXml = responseXml ?? string.Empty;

                string resultOrderNum = ExtractAnalysisResultOrderNum(responseXml);
                string resultExtNum = ExtractAnalysisResultExtNum(responseXml);

                if (!string.IsNullOrWhiteSpace(resultOrderNum))
                    details.ResultsOrderNum = resultOrderNum;
                else if (!string.IsNullOrWhiteSpace(details.OrderNum))
                    details.ResultsOrderNum = details.OrderNum;

                if (!string.IsNullOrWhiteSpace(resultExtNum))
                    details.ResultsExtNum = resultExtNum;
                else if (!string.IsNullOrWhiteSpace(details.ExtNum))
                    details.ResultsExtNum = details.ExtNum;

                if (string.IsNullOrWhiteSpace(details.ExtNum))
                    details.ExtNum = extNum;

                if (!string.IsNullOrWhiteSpace(details.ResultsOrderNum) && string.IsNullOrWhiteSpace(details.OrderNum))
                    details.OrderNum = details.ResultsOrderNum;

                var response = ParseGetAnalysisResultResponse(responseXml);

                string fileExtNum = !string.IsNullOrWhiteSpace(details.ExtNum) ? details.ExtNum : extNum;
                SaveTextToLog("Result_Order_" + MakeSafeFileNamePart(fileExtNum) + ".xml", responseXml);

                if (response.ErrorCode != 0)
                {
                    last_exception = new Exception(
                        "Гемотест вернул ошибку при запросе результатов. Код=" + response.ErrorCode + ". " +
                        (response.ErrorDescription ?? string.Empty));
                    return false;
                }

                if (response.Status == 2)
                {
                    _Order.SampleError = true;
                    last_exception = new Exception("По заказу получен отказ от выполнения исследования.");
                    return false;
                }

                int completedResultsCount = CountCompletedGemotestResults(response);
                bool hasCompletedResults = completedResultsCount > 0;
                bool saveAttachments = hasCompletedResults && HasGemotestResultAttachments(response);

                SaveResultsToOrderDetail(details, response, saveAttachments);

                if (!hasCompletedResults)
                {
                    if (_Order.State == OrderState.Sended ||
                        _Order.State == OrderState.PartialResultReceived ||
                        _Order.State == OrderState.FullResultReceived)
                    {
                        _Order.State = OrderState.Commited;
                    }

                    return _Order.State != statusBefore;
                }

                int addedResultsCount = AddGemotestResultItemsToCollection(
                    _Order,
                    details,
                    response,
                    _Results,
                    fileExtNum);

                if (response.Status == 1)
                {
                    _Order.State = OrderState.FullResultReceived;
                    return addedResultsCount > 0 || _Order.State != statusBefore;
                }

                _Order.State = OrderState.PartialResultReceived;
                return addedResultsCount > 0 || _Order.State != statusBefore;
            }
            catch (Exception ex)
            {
                last_exception = ex;
                SiMed.Clinic.Logger.LogEvent.SaveErrorToLog(ex.Message, "Gemotest");
                return false;
            }
        }

        private static int CountCompletedGemotestResults(GemotestAnalysisResultResponse response)
        {
            if (response == null || response.Results == null)
                return 0;

            int count = 0;

            foreach (GemotestResultResponseItem item in response.Results)
            {
                if (IsCompletedGemotestResult(item))
                    count++;
            }

            return count;
        }

        private static bool HasGemotestResultAttachments(GemotestAnalysisResultResponse response)
        {
            if (response == null || response.Attachments == null)
                return false;

            foreach (GemotestAttachmentResponseItem item in response.Attachments)
            {
                if (item != null && !string.IsNullOrWhiteSpace(item.FileUrl))
                    return true;
            }

            return false;
        }

        private static bool IsCompletedGemotestResult(GemotestResultResponseItem item)
        {
            if (item == null)
                return false;

            return IsCompletedGemotestResult(item.Value, item.ResultDate, item.Status);
        }

        private static bool IsCompletedGemotestResult(GemotestResultDetail item)
        {
            if (item == null)
                return false;

            return IsCompletedGemotestResult(item.Value, item.ResultDate, item.Status);
        }

        private static bool IsCompletedGemotestResult(string value, string resultDate, string status)
        {
            string st = (status ?? string.Empty).Trim();
            string val = (value ?? string.Empty).Trim();
            string date = (resultDate ?? string.Empty).Trim();

            if (IsGemotestInWorkText(st) || IsGemotestInWorkText(val))
                return false;

            int statusCode;
            if (int.TryParse(st, NumberStyles.Integer, CultureInfo.InvariantCulture, out statusCode))
            {
                if (statusCode == 1)
                    return true;

                if (statusCode == 2)
                    return false;

                if (statusCode == 0)
                    return HasRealGemotestResultValue(val) || !string.IsNullOrWhiteSpace(date);
            }

            if (IsGemotestDoneText(st))
                return true;

            if (!string.IsNullOrWhiteSpace(date))
                return true;

            return HasRealGemotestResultValue(val);
        }

        private static bool HasRealGemotestResultValue(string value)
        {
            string val = (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(val))
                return false;

            if (IsGemotestInWorkText(val))
                return false;

            return true;
        }

        private static bool IsGemotestInWorkText(string text)
        {
            string value = (text ?? string.Empty).Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.Contains("в работе") ||
                   value.Contains("выполняется") ||
                   value.Contains("выполня") ||
                   value.Contains("не готов") ||
                   value.Contains("ожид") ||
                   value.Contains("processing") ||
                   value.Contains("in work");
        }

        private static bool IsGemotestDoneText(string text)
        {
            string value = (text ?? string.Empty).Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (IsGemotestInWorkText(value))
                return false;

            return value.Contains("выполнен") ||
                   value.Contains("выполнено") ||
                   value.Contains("готов") ||
                   value.Contains("complete") ||
                   value.Contains("done");
        }

        private int AddGemotestResultItemsToCollection(Order order, GemotestOrderDetail details, GemotestAnalysisResultResponse response, ResultsCollection resultsCollection, string extNum)
        {
            if (details == null || response == null || resultsCollection == null)
                return 0;

            string safeExtNum = MakeSafeFileNamePart(extNum);
            string baseId = "Gemotest_Order_" + safeExtNum;

            List<ProductParameter> parameters = BuildGemotestProductParameters(order, details);

            int added = 0;

            if (response.Attachments != null && response.Attachments.Count > 0)
            {
                for (int i = 0; i < response.Attachments.Count; i++)
                {
                    var attachment = response.Attachments[i];
                    if (attachment == null || string.IsNullOrWhiteSpace(attachment.FileUrl))
                        continue;

                    byte[] pdfBytes = DownloadAttachmentBytes(attachment.FileUrl);

                    string itemId = response.Attachments.Count == 1 ? baseId : baseId + "_" + (i + 1).ToString(CultureInfo.InvariantCulture);

                    string fileName = itemId + ".pdf";

                    SaveBytesToLog(fileName, pdfBytes);

                    if (details.Attachments != null && i < details.Attachments.Count && details.Attachments[i] != null)
                    {
                        details.Attachments[i].FileName = fileName;
                        details.Attachments[i].Data = pdfBytes;
                    }

                    ResultItem resultItem = BuildGemotestResultItem(itemId, fileName, pdfBytes, parameters, "PDF результатов Гемотест", safeExtNum);

                    AddResultItemIfNotExists(resultsCollection, resultItem);
                    added++;
                }

                return added;
            }

            if (parameters.Count > 0)
            {
                ResultItem resultItem = BuildGemotestResultItem(baseId, baseId + ".pdf", new byte[0], parameters, "", safeExtNum);

                AddResultItemIfNotExists(resultsCollection, resultItem);
                added++;
            }

            return added;
        }

        private byte[] DownloadAttachmentBytes(string fileUrl)
        {
            if (string.IsNullOrWhiteSpace(fileUrl))
                return new byte[0];

            string url = fileUrl.Replace("&amp;", "&").Trim();

            using (WebClient client = new WebClient())
            {
                if (Options != null)
                    client.Credentials = new NetworkCredential(Options.Login ?? "", Options.Password ?? "");

                return client.DownloadData(url);
            }
        }

        private ResultItem BuildGemotestResultItem(
            string id, string fileName, byte[] data, List<ProductParameter> parameters, string comment, string num)
        {
            ResultItem item = new ResultItem(id ?? string.Empty, data ?? new byte[0]);

            item.ID = id ?? string.Empty;
            item.FileName = fileName ?? string.Empty;
            item.Name = "Гемотест: Заказ " + num;
            item.Comment = comment ?? string.Empty;

            if (parameters != null)
            {
                foreach (ProductParameter parameter in parameters)
                {
                    item.ProductParametersList.Add(parameter);

                    if (parameter.Product != null &&
                        !item.ProductList.Any(p => IsSameProduct(p, parameter.Product)))
                    {
                        item.ProductList.Add(parameter.Product);
                    }
                }
            }

            return item;
        }

        private List<ProductParameter> BuildGemotestProductParameters(Order order, GemotestOrderDetail details)
        {
            List<ProductParameter> result = new List<ProductParameter>();

            if (details == null || details.Results == null)
                return result;

            foreach (GemotestResultDetail r in details.Results)
            {
                if (r == null)
                    continue;

                if (!IsCompletedGemotestResult(r))
                    continue;

                Product product = ResolveProductForResult(order, details, r);

                string name = !string.IsNullOrWhiteSpace(r.TestRusName)
                    ? r.TestRusName
                    : r.Name;

                string bioName = !string.IsNullOrWhiteSpace(r.SectionName)
                    ? r.SectionName
                    : "Биоматериал не указан";

                ProductParameter parameter = new ProductParameter
                {
                    Code = r.Id ?? string.Empty,
                    Name = name ?? string.Empty,
                    Value = r.Value ?? string.Empty,
                    Measure = r.MeasurementUnit ?? string.Empty,
                    RefMin = r.RefMin ?? string.Empty,
                    RefMax = r.RefMax ?? string.Empty,
                    RefText = !string.IsNullOrWhiteSpace(r.RefText) ? r.RefText : (r.RefRange ?? string.Empty),
                    TestName = product != null ? product.Name : string.Empty,
                    SubBioMaterialName = bioName,
                    Product = product
                };

                result.Add(parameter);
            }

            return result;
        }

        private Product ResolveProductForResult(Order order, GemotestOrderDetail details, GemotestResultDetail result)
        {
            GemotestProductDetail productDetail = null;

            if (details != null && details.Products != null && result != null)
            {
                productDetail = details.Products.FirstOrDefault(p =>
                    p != null &&
                    !string.IsNullOrWhiteSpace(p.OrderProductGuid) &&
                    string.Equals(p.OrderProductGuid, result.OrderProductGuid, StringComparison.OrdinalIgnoreCase));

                if (productDetail == null)
                {
                    productDetail = details.Products.FirstOrDefault(p =>
                        p != null &&
                        !string.IsNullOrWhiteSpace(p.ProductId) &&
                        string.Equals(p.ProductId, result.ServiceId, StringComparison.OrdinalIgnoreCase));
                }

                if (productDetail == null)
                {
                    productDetail = details.Products.FirstOrDefault(p =>
                        p != null &&
                        !string.IsNullOrWhiteSpace(p.ProductCode) &&
                        string.Equals(p.ProductCode, result.ServiceId, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (order != null && order.Items != null && productDetail != null)
            {
                foreach (var orderItem in order.Items)
                {
                    if (orderItem == null || orderItem.Product == null)
                        continue;

                    Product p = orderItem.Product;

                    if (!string.IsNullOrWhiteSpace(productDetail.ProductId) &&
                        string.Equals(p.ID, productDetail.ProductId, StringComparison.OrdinalIgnoreCase))
                        return p;

                    if (!string.IsNullOrWhiteSpace(productDetail.ProductCode) &&
                        string.Equals(p.Code, productDetail.ProductCode, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
            }

            if (productDetail != null)
            {
                return new Product
                {
                    ID = productDetail.ProductId ?? string.Empty,
                    Code = productDetail.ProductCode ?? string.Empty,
                    Name = productDetail.ProductName ?? string.Empty
                };
            }

            return new Product
            {
                ID = result != null ? (result.ServiceId ?? string.Empty) : string.Empty,
                Code = result != null ? (result.ServiceId ?? string.Empty) : string.Empty,
                Name = result != null ? (result.Name ?? result.TestRusName ?? string.Empty) : string.Empty
            };
        }

        private bool IsSameProduct(Product a, Product b)
        {
            if (a == null || b == null)
                return false;

            if (!string.IsNullOrWhiteSpace(a.ID) && !string.IsNullOrWhiteSpace(b.ID))
                return string.Equals(a.ID, b.ID, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(a.Code) && !string.IsNullOrWhiteSpace(b.Code))
                return string.Equals(a.Code, b.Code, StringComparison.OrdinalIgnoreCase);

            return string.Equals(a.Name ?? string.Empty, b.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private void AddResultItemIfNotExists(ResultsCollection results, ResultItem item)
        {
            if (results == null || item == null)
                return;

            foreach (ResultItem existing in results)
            {
                if (existing != null &&
                    string.Equals(existing.ID, item.ID, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            results.Add(item);
        }

        private static void SaveTextToLog(string fileName, string text)
        {
            byte[] body = Encoding.UTF8.GetBytes(text ?? string.Empty);
            SiMed.Clinic.Logger.LogEvent.SaveFileToLog("Gemotest", fileName, body);
        }

        private static void SaveBytesToLog(string fileName, byte[] body)
        {
            SiMed.Clinic.Logger.LogEvent.SaveFileToLog("Gemotest", fileName, body ?? new byte[0]);
        }

        private static string MakeSafeFileNamePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "empty";

            string result = value.Trim();

            foreach (char c in Path.GetInvalidFileNameChars())
                result = result.Replace(c, '_');

            return result;
        }

        private static string ExtractAnalysisResultOrderNum(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return string.Empty;

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            XmlNode returnNode = doc.SelectSingleNode("//*[local-name()='get_analysis_resultResponse']/*[local-name()='return']");
            return GetNodeText(returnNode, "order_num");
        }

        private static string ExtractAnalysisResultExtNum(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return string.Empty;

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            XmlNode returnNode = doc.SelectSingleNode("//*[local-name()='get_analysis_resultResponse']/*[local-name()='return']");
            return GetNodeText(returnNode, "ext_num");
        }

        private void SaveResultsToOrderDetail( GemotestOrderDetail details, GemotestAnalysisResultResponse response, bool saveAttachments)
        { 
            if (details == null || response == null)
                return;

            details.Results = new List<GemotestResultDetail>();
            details.Attachments = new List<GemotestAttachmentDetail>();

            if (response.Results != null)
            {
                foreach (GemotestResultResponseItem item in response.Results)
                {
                    if (!IsCompletedGemotestResult(item))
                        continue;

                    string orderProductGuid = ResolveOrderProductGuidByServiceId(details, item.ServiceId);

                    details.Results.Add(new GemotestResultDetail
                    {
                        Id = item.Id ?? string.Empty,
                        Name = item.Name ?? string.Empty,
                        TestRusName = item.TestRusName ?? string.Empty,
                        SectionName = item.SectionName ?? string.Empty,
                        Value = item.Value ?? string.Empty,
                        MeasurementUnit = item.MeasurementUnit ?? string.Empty,
                        RefMin = item.RefMin ?? string.Empty,
                        RefMax = item.RefMax ?? string.Empty,
                        RefRange = item.RefRange ?? string.Empty,
                        RefText = item.RefText ?? string.Empty,
                        ResultDate = item.ResultDate ?? string.Empty,
                        ServiceId = item.ServiceId ?? string.Empty,
                        Status = item.Status ?? string.Empty,
                        OrderProductGuid = orderProductGuid ?? string.Empty
                    });
                }
            }

            if (saveAttachments && response.Attachments != null)
            {
                int idx = 1;

                foreach (GemotestAttachmentResponseItem item in response.Attachments)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.FileUrl))
                        continue;

                    details.Attachments.Add(new GemotestAttachmentDetail
                    {
                        SectionName = item.SectionName ?? string.Empty,
                        FileUrl = item.FileUrl ?? string.Empty,
                        DisplayName = BuildAttachmentDisplayName(item, idx++),
                        OrderProductGuid = string.Empty,
                        OrderSampleGuid = string.Empty
                    });
                }
            }
        }

        private string ResolveOrderProductGuidByServiceId(GemotestOrderDetail details, string serviceId)
        {
            if (details == null || details.Products == null || string.IsNullOrWhiteSpace(serviceId))
                return string.Empty;

            var direct = details.Products.FirstOrDefault(x =>
                x != null &&
                !string.IsNullOrWhiteSpace(x.ProductId) &&
                string.Equals(x.ProductId, serviceId, StringComparison.OrdinalIgnoreCase));

            if (direct != null)
                return direct.OrderProductGuid ?? string.Empty;

            var byCode = details.Products.FirstOrDefault(x =>
                x != null &&
                !string.IsNullOrWhiteSpace(x.ProductCode) &&
                string.Equals(x.ProductCode, serviceId, StringComparison.OrdinalIgnoreCase));

            if (byCode != null)
                return byCode.OrderProductGuid ?? string.Empty;

            return string.Empty;
        }

        private string BuildAttachmentDisplayName(GemotestAttachmentResponseItem item, int index)
        {
            string section = item != null ? (item.SectionName ?? "") : "";
            if (!string.IsNullOrWhiteSpace(section))
                return $"Файл результатов {section}.pdf";

            return $"Файл результатов #{index}.pdf";
        }
        public bool ExtractContainers(Order _Order, ref ContainersCollection _Containers)
        {
            try
            {
                _Containers = new ContainersCollection();

                if (_Order == null)
                    throw new ArgumentNullException(nameof(_Order));

                if (laboratoryGUI == null)
                {
                    if (!Init())
                    {
                        MessageBox.Show("Ошибка инициализации модуля Гемотест", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                }

                GemotestOrderDetail details = _Order.OrderDetail as GemotestOrderDetail;
                if (details == null)
                    throw new InvalidOperationException("OrderDetail не является GemotestOrderDetail.");

                details.Dicts = Dicts;

                ResultsCollection results = new ResultsCollection();
                OrderModelForGUI model = new OrderModelForGUI();

                if (!laboratoryGUI.CreateOrderModelForGUI(true, _Order, ref results, ref model))
                {
                    Exception guiEx = laboratoryGUI.GetLastException();
                    if (guiEx != null)
                        throw guiEx;

                    throw new Exception("Не удалось построить GUI-модель заказа для извлечения контейнеров.");
                }

                if (model.Samples == null || model.Samples.Count == 0)
                    return true;

                foreach (var sample in model.Samples)
                {
                    if (sample == null || sample.Biomaterial == null)
                        continue;

                    Container labContainer = new Container();
                    labContainer.BarCode = sample.Barcode ?? string.Empty;
                    labContainer.Code = sample.Biomaterial.ContainerCode ?? string.Empty;
                    labContainer.Name = sample.Biomaterial.ContainerName ?? string.Empty;
                    labContainer.BioMaterialName = sample.Biomaterial.BiomaterialName ?? string.Empty;
                    labContainer.Comment = string.Empty;

                    _Containers.Add(labContainer);
                }

                return true;
            }
            catch (Exception e)
            {
                last_exception = e;
                MessageBox.Show("Ошибка при извлечении сведений о контейнерах (лаборатория Гемотест): " + e.Message);
                return false;
            }
        }

        public bool ExtractResult(Order _Order, ref ResultsCollection _Results)
        {
            try
            {
                if (_Results == null)
                    _Results = new ResultsCollection();

                if (_Order == null)
                    return false;

                GemotestOrderDetail details = _Order.OrderDetail as GemotestOrderDetail;
                if (details == null)
                    return false;

                bool hasSavedCompletedResults =
                    details.Results != null &&
                    details.Results.Any(x => IsCompletedGemotestResult(x));

                bool hasSavedAttachments =
                    details.Attachments != null &&
                    details.Attachments.Count > 0;

                if (!hasSavedCompletedResults && hasSavedAttachments)
                {
                    details.Attachments.Clear();
                }

                if (hasSavedCompletedResults)
                {
                    string extNum = !string.IsNullOrWhiteSpace(details.ExtNum)
                        ? details.ExtNum
                        : (!string.IsNullOrWhiteSpace(details.ResultsExtNum)
                            ? details.ResultsExtNum
                            : (_Order.Number ?? string.Empty));

                    int added = AddGemotestResultItemsFromDetails(_Order, details, _Results, extNum);
                    return added > 0;
                }

                return CheckResult(_Order, ref _Results);
            }
            catch (Exception e)
            {
                last_exception = e;
                return false;
            }
        }

        private int AddGemotestResultItemsFromDetails(Order order, GemotestOrderDetail details, ResultsCollection resultsCollection, string extNum)
        {
            if (details == null || resultsCollection == null)
                return 0;

            string safeExtNum = MakeSafeFileNamePart(extNum);
            string baseId = "Gemotest_Order_" + safeExtNum;

            List<ProductParameter> parameters = BuildGemotestProductParameters(order, details);

            int added = 0;

            if (details.Attachments != null && details.Attachments.Count > 0)
            {
                for (int i = 0; i < details.Attachments.Count; i++)
                {
                    GemotestAttachmentDetail attachment = details.Attachments[i];
                    if (attachment == null)
                        continue;

                    string itemId = details.Attachments.Count == 1
                        ? baseId
                        : baseId + "_" + (i + 1).ToString(CultureInfo.InvariantCulture);

                    string fileName = !string.IsNullOrWhiteSpace(attachment.FileName)
                        ? attachment.FileName
                        : itemId + ".pdf";

                    byte[] data = attachment.Data;

                    if ((data == null || data.Length == 0) && !string.IsNullOrWhiteSpace(attachment.FileUrl))
                    {
                        data = DownloadAttachmentBytes(attachment.FileUrl);
                        attachment.Data = data;
                        attachment.FileName = fileName;
                    }

                    ResultItem item = BuildGemotestResultItem(itemId, fileName, data ?? new byte[0], parameters, "PDF результатов Гемотест", safeExtNum);

                    AddResultItemIfNotExists(resultsCollection, item);
                    added++;
                }

                return added;
            }

            if (parameters.Count > 0)
            {
                ResultItem item = BuildGemotestResultItem(baseId, baseId + ".pdf", new byte[0], parameters, "", safeExtNum);

                AddResultItemIfNotExists(resultsCollection, item);
                added++;
            }

            return added;
        }

        public void SetContainerMarkerList(List<IContainerMarker> _ContainerMarkerList) { }

        public Exception GetLastException() { return last_exception; }

        public void BeginTransaction(LaboratoryTransactionType _TransactionType) { }

        public void EndTransaction(LaboratoryTransactionType _TransactionType) { }

        public bool GetNumbersPoolIfNeed(out bool _NumbersPoolChanged, out string _SystemOptionsNew) { _NumbersPoolChanged = false; _SystemOptionsNew = ""; return true; }

        private static bool HasConfiguredPriceLists(SystemOptions o)
        {
            return o != null && o.PriceLists != null && o.PriceLists.Any(x => x != null && !string.IsNullOrWhiteSpace(x.ContractorCode));
        }

        private IEnumerable<GemotestPriceList> GetInitCandidates()
        {
            var result = new List<GemotestPriceList>();

            if (Options != null && !string.IsNullOrWhiteSpace(Options.Contractor_Code))
            {
                result.Add(new GemotestPriceList
                {
                    ContractorCode = Options.Contractor_Code ?? "",
                    Name = Options.Contractor ?? ""
                });
            }

            if (Options != null && Options.PriceLists != null)
            {
                foreach (var pl in Options.PriceLists)
                {
                    if (pl == null || string.IsNullOrWhiteSpace(pl.ContractorCode))
                        continue;

                    if (result.Any(x => string.Equals(x.ContractorCode, pl.ContractorCode, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    result.Add(new GemotestPriceList
                    {
                        ContractorCode = pl.ContractorCode ?? "",
                        Name = pl.Name ?? ""
                    });
                }
            }

            return result;
        }

        private bool TryInitWithPriceList(GemotestPriceList pl)
        {
            if (pl == null || string.IsNullOrWhiteSpace(pl.ContractorCode))
                return false;

            try
            {
                Gemotest = new GemotestService(
                    Options.UrlAdress,
                    Options.Login,
                    Options.Password,
                    pl.Name ?? "",
                    pl.ContractorCode ?? "",
                    Options.Salt
                );

                if (!RefreshDictionariesAtInit())
                    return false;

                Options.Contractor = pl.Name ?? "";
                Options.Contractor_Code = pl.ContractorCode ?? "";
                return true;
            }
            catch (Exception ex)
            {
                last_exception = ex;
                return false;
            }
        }

        private static void ResolveContractorForServiceInit(SystemOptions o, out string contractorName, out string contractorCode)
        {
            contractorName = o != null ? (o.Contractor ?? "") : "";
            contractorCode = o != null ? (o.Contractor_Code ?? "") : "";

            if (!string.IsNullOrWhiteSpace(contractorCode))
                return;

            if (o == null || o.PriceLists == null)
                return;

            var first = o.PriceLists.FirstOrDefault(x => x != null && !string.IsNullOrWhiteSpace(x.ContractorCode));
            if (first != null)
            {
                contractorName = first.Name ?? "";
                contractorCode = first.ContractorCode ?? "";
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
                    sb.Append(hash[i].ToString("x2"));

                return sb.ToString();
            }
        }

        private static string BuildGetAnalysisResultEnvelope(string contractor, string hash, string orderNum, string extNum)
        {
            var sb = new StringBuilder();

            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<soapenv:Envelope ");
            sb.Append("xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" ");
            sb.Append("xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" ");
            sb.Append("xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" ");
            sb.Append("xmlns:urn=\"urn:OdoctorControllerwsdl\" ");
            sb.Append("xmlns:soapenc=\"http://schemas.xmlsoap.org/soap/encoding/\">");
            sb.Append("<soapenv:Header/>");
            sb.Append("<soapenv:Body>");
            sb.Append("<urn:get_analysis_result soapenv:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">");
            sb.Append("<params xsi:type=\"urn:request_get_analysis_result\">");

            sb.Append("<contractor xsi:type=\"xsd:string\">").Append(SecurityElementEscape(contractor)).Append("</contractor>");
            sb.Append("<hash xsi:type=\"xsd:string\">").Append(SecurityElementEscape(hash)).Append("</hash>");
            sb.Append("<order_num xsi:type=\"xsd:string\">").Append(SecurityElementEscape(orderNum)).Append("</order_num>");
            sb.Append("<ext_num xsi:type=\"xsd:string\">").Append(SecurityElementEscape(extNum ?? "")).Append("</ext_num>");

            sb.Append("</params>");
            sb.Append("</urn:get_analysis_result>");
            sb.Append("</soapenv:Body>");
            sb.Append("</soapenv:Envelope>");

            return sb.ToString();
        }

        private static string SendSoapRequest(string url, string login, string password, string method, string xmlBody)
        {
            string soapAction = "\"urn:OdoctorControllerwsdl#" + method + "\"";

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "text/xml; charset=utf-8";
            request.Headers["SOAPAction"] = soapAction;

            string credentials = (login ?? "") + ":" + (password ?? "");
            string authHeader = Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials));
            request.Headers["Authorization"] = "Basic " + authHeader;
            request.PreAuthenticate = true;

            byte[] payload = Encoding.UTF8.GetBytes(xmlBody);
            request.ContentLength = payload.Length;

            using (var reqStream = request.GetRequestStream())
                reqStream.Write(payload, 0, payload.Length);

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private sealed class GemotestAnalysisResultResponse
        {
            public int ErrorCode;
            public string ErrorDescription;
            public int Status;

            public List<GemotestResultResponseItem> Results = new List<GemotestResultResponseItem>();
            public List<GemotestAttachmentResponseItem> Attachments = new List<GemotestAttachmentResponseItem>();

            public int ResultsCount => Results != null ? Results.Count : 0;
            public int AttachmentsCount => Attachments != null ? Attachments.Count : 0;
        }

        private sealed class GemotestResultResponseItem
        {
            public string Id;
            public string Name;
            public string TestRusName;
            public string SectionName;
            public string Value;
            public string MeasurementUnit;
            public string RefMin;
            public string RefMax;
            public string RefRange;
            public string RefText;
            public string ResultDate;
            public string ServiceId;
            public string Status;
        }

        private sealed class GemotestAttachmentResponseItem
        {
            public string SectionName;
            public string FileUrl;
        }
        private static GemotestAnalysisResultResponse ParseGetAnalysisResultResponse(string xml)
        {
            var result = new GemotestAnalysisResultResponse
            {
                ErrorCode = -1,
                ErrorDescription = "Пустой ответ"
            };

            if (string.IsNullOrWhiteSpace(xml))
                return result;

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            XmlNode returnNode = doc.SelectSingleNode("//*[local-name()='get_analysis_resultResponse']/*[local-name()='return']");
            if (returnNode == null)
            {
                result.ErrorDescription = "Не найден узел return в ответе get_analysis_result.";
                return result;
            }

            result.ErrorCode = ParseInt(GetNodeText(returnNode, "error_code"), -1);
            result.ErrorDescription = GetNodeText(returnNode, "error_description");
            result.Status = ParseInt(GetNodeText(returnNode, "status"), 0);

            XmlNodeList clItems = returnNode.SelectNodes("./*[local-name()='results_cl']/*[local-name()='item']");
            if (clItems != null)
            {
                foreach (XmlNode node in clItems)
                {
                    result.Results.Add(ParseResultNode(node, false));
                }
            }

            XmlNodeList mbItems = returnNode.SelectNodes("./*[local-name()='results_mb']/*[local-name()='item']");
            if (mbItems != null)
            {
                foreach (XmlNode node in mbItems)
                {
                    result.Results.Add(ParseResultNode(node, true));
                }
            }

            XmlNodeList attItems = returnNode.SelectNodes("./*[local-name()='attachments']/*[local-name()='item']");
            if (attItems != null)
            {
                foreach (XmlNode node in attItems)
                {
                    if (node == null)
                        continue;

                    result.Attachments.Add(new GemotestAttachmentResponseItem
                    {
                        SectionName = GetNodeText(node, "section_name"),
                        FileUrl = GetNodeText(node, "file")
                    });
                }
            }

            return result;
        }

        private static GemotestResultResponseItem ParseResultNode(XmlNode node, bool isMb)
        {
            if (node == null)
                return new GemotestResultResponseItem();

            return new GemotestResultResponseItem
            {
                Id = GetNodeText(node, "id"),
                Name = GetNodeText(node, "name"),
                TestRusName = GetNodeText(node, "test_rusname"),
                SectionName = GetNodeText(node, "section_name"),
                Value = GetNodeText(node, "value"),
                MeasurementUnit = GetNodeText(node, "measurement_unit"),
                RefMin = GetNodeText(node, "ref_min"),
                RefMax = GetNodeText(node, "ref_max"),
                RefRange = GetNodeText(node, "ref_range"),
                RefText = GetNodeText(node, "ref_text"),
                ResultDate = GetNodeText(node, "result_date"),
                ServiceId = GetNodeText(node, "service_id"),
                Status = isMb ? GetNodeText(node, "status_mb") : GetNodeText(node, "status_cl")
            };
        }

        private static string GetNodeText(XmlNode parent, string localName)
        {
            if (parent == null || string.IsNullOrWhiteSpace(localName))
                return string.Empty;

            XmlNode node = parent.SelectSingleNode(".//*[local-name()='" + localName + "']");
            return node != null ? (node.InnerText ?? string.Empty).Trim() : string.Empty;
        }

        private static int ParseInt(string s, int defValue)
        {
            int v;
            return int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : defValue;
        }

        private static string SecurityElementEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

    }
}
