using SiMed.Clinic;
using SiMed.Clinic.Logger;
using SiMed.Laboratory;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using Laboratory.Gemotest.GemotestRequests;

namespace Laboratory.Gemotest.SourseClass
{

    [Serializable]
    public class GemotestSampleServiceDetail
    {
        public string ServiceId;
        public string ComplexId;
        public int UtilizationFlag;
        public int RefuseFlag;
        public int ServiceCount;
        public double SharePercent;

        public GemotestSampleServiceDetail()
        {
            ServiceId = string.Empty;
            ComplexId = string.Empty;
            ServiceCount = 1;
        }
    }

    [Serializable]
    public class GemotestSampleDetail
    {
        public string OrderSampleGuid;
        public string Barcode;

        public string SampleId;
        public string SampleIdentifier;

        public string SampleDescription;

        public string BiomId;
        public string BiomCode;
        public string BiomName;

        public string ContId;
        public string ContCode;
        public string ContName;

        public string LocalizationId;
        public string LocalizationName;

        public string TransportId;
        public string TransportName;

        public string LabCenterId;

        public bool IsAliquot;
        public bool IsUtilize;
        public bool HasUtilizationService;
        public bool HasRefusedService;

        public string PrimarySampleIdentifier;
        public string ParentSampleId;

        public string SampleRole;
        public string SampleAction;

        public string MicrobiologyBiomaterialId;
        public string ParentOrderSampleGuid;
        public double UsedPercent;

        public List<string> OrderProductGuidList;
        public List<GemotestSampleServiceDetail> Services;

        public GemotestSampleDetail()
        {
            OrderProductGuidList = new List<string>();
            PrimarySampleIdentifier = "";
            ParentSampleId = "";
            SampleRole = "";
            SampleAction = "";
            MicrobiologyBiomaterialId = "";
            ParentOrderSampleGuid = "";
            Services = new List<GemotestSampleServiceDetail>();
        }
    }

    public class GemotestDetail
    {
        public int ID { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }
        public string SoapCode { get; set; }
        public List<int> MandatoryProducts { get; set; }
        public List<int> OptionalProducts { get; set; }
        public int? dictionaryId { get; set; } = null;
        public string regex { get; set; } = null;
        public bool replaced { get; set; } = false;
        public bool isStdField { get; set; } = false;
        public List<GemotestResultDetail> Results { get; set; }
        public List<GemotestAttachmentDetail> Attachments { get; set; }

        public string DisplayValue { get; set; }
        public GemotestDetail()
        {
            MandatoryProducts = new List<int>();
            OptionalProducts = new List<int>();
            Results = new List<GemotestResultDetail>();
            Attachments = new List<GemotestAttachmentDetail>();
        }
        public bool IsValid(bool _EmptyAvailable, out string _ErrorText)
        {
            _ErrorText = "";
            if (!_EmptyAvailable && String.IsNullOrEmpty(Value))
            {
                _ErrorText = "Поле обязательно для заполнения";
                return false;
            }
            if (!String.IsNullOrEmpty(Value) && !String.IsNullOrEmpty(regex))
            {
                Regex r = new Regex(regex);
                if (!r.IsMatch(Value))
                {
                    _ErrorText = $"Поле не соответствует формату заполнения '{regex}'";
                    return false;
                }
            }
            return true;
        }
    }

    [Serializable]
    public class GemotestResultDetail
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
        public string OrderProductGuid;
    }

    [Serializable]
    public class GemotestAttachmentDetail
    {
        public string SectionName;
        public string FileUrl;
        public string OrderProductGuid;
        public string OrderSampleGuid;
        public string DisplayName;
        public string FileName;
        public byte[] Data;
    }

    public class GemotestProductBioMaterial
    {
        public int ProductIndex { get; set; }
        public string ServiceId { get; set; }
        public string Id { get; set; }
        public string Code { get; set; }
        public string BiomaterialName { get; set; }
        public string ContainerId { get; set; }
        public string ContainerName { get; set; }
        public string LocalizationId { get; set; }
        public string SourceServiceId { get; set; }
        public bool IsMicrobiology { get; set; }
        public int GroupNum { get; set; }
        public bool Chosen { get; set; }
        public List<string> SubServiceIdList { get; set; } = new List<string>();
    }

    [Serializable]
    public class GemotestSupplementalInstance
    {
        public string InstanceKey { get; set; }

        public string SupplementalId { get; set; }
        public string SupplementalName { get; set; }

        public string AutoServiceId { get; set; }
        public string AutoServiceGuid { get; set; }
        public string AutoServiceName { get; set; }

        public string OwnerProductId { get; set; }
        public string OwnerProductGuid { get; set; }
        public string OwnerProductName { get; set; }

        public bool Required { get; set; }
    }

    [Serializable]
    public class GemotestOrderDetail : BaseOrderDetail
    {

        public string ExtNum { get; set; }
        public string OrderNum { get; set; }
        public string ResultsRawXml { get; set; }
        public string ResultsOrderNum { get; set; }
        public string ResultsExtNum { get; set; }
        public List<GemotestDetail> Details { get; set; }
        public List<GemotestProductBioMaterial> BioMaterials { get; set; }
        public List<Product> DefectProductList { get; set; }
        public string PriceList { get; set; }
        public string PriceListCode { get; set; }
        public string PriceListName { get; set; }
        public string PriceListNum { get; set; }
        public List<GemotestSampleDetail> Samples { get; set; }

        [XmlIgnore]
        public Dictionaries Dicts { get; set; }
        public List<GemotestProductDetail> Products { get; set; }
        public string DefectsMessages { get; set; }
        public List<GemotestResultDetail> Results { get; set; }
        public List<GemotestAttachmentDetail> Attachments { get; set; }
        public List<GemotestSupplementalInstance> SupplementalInstances { get; set; }
        public GemotestOrderDetail() : base()
        {
            ResultsRawXml = string.Empty;
            ResultsOrderNum = string.Empty;
            ResultsExtNum = string.Empty;
            Samples = new List<GemotestSampleDetail>();
            Details = new List<GemotestDetail>();
            BioMaterials = new List<GemotestProductBioMaterial>();
            DefectProductList = new List<Product>();
            Products = new List<GemotestProductDetail>();
            Results = new List<GemotestResultDetail>();
            Attachments = new List<GemotestAttachmentDetail>();
            LaboratoryType = (LaboratoryType)24;
            SupplementalInstances = new List<GemotestSupplementalInstance>();

            ExtNum = string.Empty;
            OrderNum = string.Empty;
        }

        [Serializable]
        public class GemotestProductDetail
        {

            public string OrderProductGuid;

            public string ProductId;

            public string ProductCode;


            public string ProductName;
            public Product AsProduct()
            {
                Product p = new Product(ProductName, ProductId, ProductCode);
                return p;
            }
        }

        private static string NormalizeId(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static bool SameId(string left, string right)
        {
            return string.Equals(NormalizeId(left), NormalizeId(right), StringComparison.OrdinalIgnoreCase);
        }

        private static int ServiceTypeValue(DictionaryService service)
        {
            return service != null && service.service_type.HasValue ? service.service_type.Value : -1;
        }

        private static bool IsUnsupportedService(DictionaryService service)
        {
            int serviceType = ServiceTypeValue(service);
            return service == null || service.is_blocked || serviceType == 3 || serviceType == 4;
        }

        private string ResolveBiomaterialName(string biomaterialId)
        {
            biomaterialId = NormalizeId(biomaterialId);
            if (string.IsNullOrWhiteSpace(biomaterialId))
                return string.Empty;

            if (Dicts != null && Dicts.Biomaterials != null)
            {
                DictionaryBiomaterials biomaterial;
                if (Dicts.Biomaterials.TryGetValue(biomaterialId, out biomaterial) &&
                    biomaterial != null &&
                    !string.IsNullOrWhiteSpace(biomaterial.name))
                {
                    return biomaterial.name;
                }
            }

            return biomaterialId;
        }

        private string ResolveTransportName(string transportId)
        {
            transportId = NormalizeId(transportId);
            if (string.IsNullOrWhiteSpace(transportId))
                return string.Empty;

            if (Dicts != null && Dicts.Transport != null)
            {
                DictionaryTransport transport;
                if (Dicts.Transport.TryGetValue(transportId, out transport) &&
                    transport != null &&
                    !string.IsNullOrWhiteSpace(transport.name))
                {
                    return transport.name;
                }
            }

            return transportId;
        }

        private string ResolveSampleRowBiomaterialId(DictionarySamplesServices row)
        {
            if (row == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(row.biomaterial_id))
                return NormalizeId(row.biomaterial_id);

            if (!string.IsNullOrWhiteSpace(row.microbiology_biomaterial_id))
                return NormalizeId(row.microbiology_biomaterial_id);

            return string.Empty;
        }

        private string ResolveTransportIdFromSampleRow(DictionarySamplesServices row)
        {
            if (row == null || row.sample_id <= 0 || Dicts == null || Dicts.Samples == null)
                return string.Empty;

            DictionarySamples sample;
            if (!Dicts.Samples.TryGetValue(row.sample_id.ToString(), out sample) || sample == null)
                return string.Empty;

            return NormalizeId(sample.transport_id);
        }

        private string ResolveTransportIdForRequirement(
            string serviceId,
            string biomaterialId,
            DictionarySamplesServices sampleRow,
            DictionaryService_parameters serviceParameter,
            DictionaryMarketingComplex marketingRow)
        {
            string transportId = ResolveTransportIdFromSampleRow(sampleRow);
            if (!string.IsNullOrWhiteSpace(transportId))
                return transportId;

            if (serviceParameter != null && !string.IsNullOrWhiteSpace(serviceParameter.transport_id))
                return NormalizeId(serviceParameter.transport_id);

            if (marketingRow != null && !string.IsNullOrWhiteSpace(marketingRow.transport_id))
                return NormalizeId(marketingRow.transport_id);

            if (Dicts != null)
            {
                DictionaryTransport transport = Dicts.ResolveTransport(serviceId, biomaterialId);
                if (transport != null && !string.IsNullOrWhiteSpace(transport.id))
                    return NormalizeId(transport.id);
            }

            if (Dicts != null && Dicts.Directory != null)
            {
                DictionaryService service;
                if (Dicts.Directory.TryGetValue(serviceId, out service) &&
                    service != null &&
                    !string.IsNullOrWhiteSpace(service.transport_id))
                {
                    return NormalizeId(service.transport_id);
                }
            }

            return string.Empty;
        }

        private void AddRequiredBiomaterial(
            List<GemotestProductBioMaterial> result,
            int productIndex,
            int groupNum,
            string serviceId,
            string subServiceId,
            string biomaterialId,
            string transportId,
            string localizationId = "",
            bool isMicrobiology = false)
        {
            if (result == null)
                return;

            biomaterialId = NormalizeId(biomaterialId);
            if (string.IsNullOrWhiteSpace(biomaterialId))
                return;

            if (SameId(biomaterialId, "Drugoe"))
                return;

            serviceId = NormalizeId(serviceId);
            subServiceId = NormalizeId(subServiceId);
            transportId = NormalizeId(transportId);
            localizationId = NormalizeId(localizationId);

            GemotestProductBioMaterial existing = result.FirstOrDefault(x =>
                x != null &&
                x.ProductIndex == productIndex &&
                x.GroupNum == groupNum &&
                SameId(x.ServiceId, serviceId) &&
                SameId(x.Id, biomaterialId) &&
                SameId(x.ContainerId, transportId));

            if (existing == null)
            {
                existing = new GemotestProductBioMaterial
                {
                    ProductIndex = productIndex,
                    GroupNum = groupNum <= 0 ? 1 : groupNum,
                    ServiceId = serviceId,
                    Id = biomaterialId,
                    Code = biomaterialId,
                    BiomaterialName = ResolveBiomaterialName(biomaterialId),
                    ContainerId = transportId,
                    ContainerName = string.IsNullOrWhiteSpace(transportId) ? "-Не указан-" : ResolveTransportName(transportId),
                    LocalizationId = localizationId,
                    SourceServiceId = subServiceId,
                    IsMicrobiology = isMicrobiology,
                    Chosen = false,
                    SubServiceIdList = new List<string>()
                };

                result.Add(existing);
            }

            if (string.IsNullOrWhiteSpace(existing.LocalizationId) && !string.IsNullOrWhiteSpace(localizationId))
                existing.LocalizationId = localizationId;

            if (string.IsNullOrWhiteSpace(existing.SourceServiceId) && !string.IsNullOrWhiteSpace(subServiceId))
                existing.SourceServiceId = subServiceId;

            if (isMicrobiology)
                existing.IsMicrobiology = true;

            if (!string.IsNullOrWhiteSpace(subServiceId) &&
                !existing.SubServiceIdList.Any(x => SameId(x, subServiceId)))
            {
                existing.SubServiceIdList.Add(subServiceId);
            }
        }

        private List<DictionarySamplesServices> GetSampleRequirementRows(string serviceId)
        {
            var result = new List<DictionarySamplesServices>();

            serviceId = NormalizeId(serviceId);
            if (string.IsNullOrWhiteSpace(serviceId) || Dicts == null || Dicts.SamplesServices == null)
                return result;

            List<DictionarySamplesServices> rows;
            if (!Dicts.SamplesServices.TryGetValue(serviceId, out rows) || rows == null)
                return result;

            foreach (DictionarySamplesServices row in rows)
            {
                if (row == null || row.sample_id <= 0)
                    continue;

                if (row.primary_sample_id > 0)
                    continue;

                string biomaterialId = ResolveSampleRowBiomaterialId(row);
                if (string.IsNullOrWhiteSpace(biomaterialId) || SameId(biomaterialId, "Drugoe"))
                    continue;

                if (!result.Any(x =>
                    SameId(x.service_id, row.service_id) &&
                    SameId(ResolveSampleRowBiomaterialId(x), biomaterialId) &&
                    SameId(x.localization_id, row.localization_id) &&
                    x.sample_id == row.sample_id))
                {
                    result.Add(row);
                }
            }

            return result;
        }

        private void AddBiomaterialsFromSampleRequirements(
            List<GemotestProductBioMaterial> result,
            int productIndex,
            int groupNum,
            string ownerServiceId,
            string serviceId)
        {
            foreach (DictionarySamplesServices row in GetSampleRequirementRows(serviceId))
            {
                string biomaterialId = ResolveSampleRowBiomaterialId(row);
                string transportId = ResolveTransportIdForRequirement(serviceId, biomaterialId, row, null, null);

                AddRequiredBiomaterial(
                    result,
                    productIndex,
                    groupNum,
                    ownerServiceId,
                    serviceId,
                    biomaterialId,
                    transportId,
                    row.localization_id ?? string.Empty,
                    !string.IsNullOrWhiteSpace(row.microbiology_biomaterial_id));
            }
        }

        private void AddBiomaterialsFromServiceParameters(
            List<GemotestProductBioMaterial> result,
            int productIndex,
            int groupNum,
            string ownerServiceId,
            string serviceId)
        {
            if (Dicts == null || Dicts.ServiceParameters == null)
                return;

            List<DictionaryService_parameters> parameters;
            if (!Dicts.ServiceParameters.TryGetValue(serviceId, out parameters) || parameters == null)
                return;

            foreach (DictionaryService_parameters parameter in parameters)
            {
                if (parameter == null || parameter.archive != 0)
                    continue;

                string biomaterialId = NormalizeId(parameter.biomaterial_id);
                if (string.IsNullOrWhiteSpace(biomaterialId) || SameId(biomaterialId, "Drugoe"))
                    continue;

                string transportId = ResolveTransportIdForRequirement(serviceId, biomaterialId, null, parameter, null);

                AddRequiredBiomaterial(
                    result,
                    productIndex,
                    groupNum,
                    ownerServiceId,
                    serviceId,
                    biomaterialId,
                    transportId,
                    parameter.localization_id ?? string.Empty,
                    false);
            }
        }

        private void AddBiomaterialFromDirectoryService(
            List<GemotestProductBioMaterial> result,
            int productIndex,
            int groupNum,
            string ownerServiceId,
            DictionaryService service)
        {
            if (service == null)
                return;

            string biomaterialId = NormalizeId(service.biomaterial_id);
            if (string.IsNullOrWhiteSpace(biomaterialId) || SameId(biomaterialId, "Drugoe"))
                return;

            string transportId = ResolveTransportIdForRequirement(service.id, biomaterialId, null, null, null);

            AddRequiredBiomaterial(
                result,
                productIndex,
                groupNum,
                ownerServiceId,
                service.id,
                biomaterialId,
                transportId,
                service.localization_id ?? string.Empty,
                service.type == 2);
        }

        private void AddBiomaterialsForSingleService(
            List<GemotestProductBioMaterial> result,
            int productIndex,
            int groupNum,
            string ownerServiceId,
            string serviceId)
        {
            int before = result.Count;
            AddBiomaterialsFromSampleRequirements(result, productIndex, groupNum, ownerServiceId, serviceId);

            if (result.Count > before)
                return;

            before = result.Count;
            AddBiomaterialsFromServiceParameters(result, productIndex, groupNum, ownerServiceId, serviceId);

            if (result.Count > before)
                return;

            if (Dicts != null && Dicts.Directory != null)
            {
                DictionaryService service;
                if (Dicts.Directory.TryGetValue(serviceId, out service) && service != null)
                    AddBiomaterialFromDirectoryService(result, productIndex, groupNum, ownerServiceId, service);
            }
        }

        private bool AddFixedBiomaterialFromMarketingItem(
            List<GemotestProductBioMaterial> result,
            int productIndex,
            int groupNum,
            string complexId,
            string subServiceId,
            DictionaryMarketingComplex item)
        {
            if (result == null || item == null || string.IsNullOrWhiteSpace(subServiceId))
                return false;

            string biomaterialId = NormalizeId(item.biomaterial_id);
            if (string.IsNullOrWhiteSpace(biomaterialId) || SameId(biomaterialId, "Drugoe"))
                return false;

            string itemLocalizationId = NormalizeId(item.localization_id);
            int before = result.Count;
            bool subServiceIsMicrobiology = false;

            if (Dicts != null && Dicts.Directory != null)
            {
                DictionaryService subService;
                if (Dicts.Directory.TryGetValue(subServiceId, out subService) && subService != null)
                    subServiceIsMicrobiology = subService.type == 2;
            }

            foreach (DictionarySamplesServices row in GetSampleRequirementRows(subServiceId))
            {
                string rowBiomaterialId = ResolveSampleRowBiomaterialId(row);
                if (!SameId(rowBiomaterialId, biomaterialId))
                    continue;

                if (!string.IsNullOrWhiteSpace(itemLocalizationId) &&
                    !string.IsNullOrWhiteSpace(row.localization_id) &&
                    !SameId(row.localization_id, itemLocalizationId))
                {
                    continue;
                }

                string transportId = ResolveTransportIdForRequirement(subServiceId, biomaterialId, row, null, item);
                string localizationId = !string.IsNullOrWhiteSpace(row.localization_id) ? row.localization_id : itemLocalizationId;

                AddRequiredBiomaterial(
                    result,
                    productIndex,
                    groupNum,
                    complexId,
                    subServiceId,
                    biomaterialId,
                    transportId,
                    localizationId,
                    subServiceIsMicrobiology || !string.IsNullOrWhiteSpace(row.microbiology_biomaterial_id));
            }

            if (result.Count > before)
                return true;

            string fallbackTransportId = ResolveTransportIdForRequirement(subServiceId, biomaterialId, null, null, item);
            AddRequiredBiomaterial(
                result,
                productIndex,
                groupNum,
                complexId,
                subServiceId,
                biomaterialId,
                fallbackTransportId,
                itemLocalizationId,
                subServiceIsMicrobiology);

            return result.Count > before;
        }

        private static string BuildMarketingComplexBiomaterialGroupKey(string subServiceId, string biomaterialId, string localizationId)
        {
            biomaterialId = NormalizeId(biomaterialId);
            localizationId = NormalizeId(localizationId);

            if (!string.IsNullOrWhiteSpace(biomaterialId))
                return string.Join("|", new string[] { "BIO", biomaterialId, localizationId });

            return string.Join("|", new string[] { "SERVICE", NormalizeId(subServiceId), localizationId });
        }

        private void AddBiomaterialsForMarketingComplex(
            List<GemotestProductBioMaterial> result,
            int productIndex,
            GemotestProductDetail product,
            DictionaryService service)
        {
            List<DictionaryMarketingComplex> items = null;

            if (Dicts != null && Dicts.MarketingComplexByComplexId != null)
                Dicts.MarketingComplexByComplexId.TryGetValue(service.id, out items);

            if (items == null || items.Count == 0)
            {
                AddBiomaterialsForSingleService(result, productIndex, 1, service.id, service.id);
                return;
            }

            var orderedGroups = items
                .Where(x => x != null)
                .Select(x => new
                {
                    Item = x,
                    SubServiceId = !string.IsNullOrWhiteSpace(x.service_id) ? NormalizeId(x.service_id) : NormalizeId(x.main_service),
                    BiomaterialId = NormalizeId(x.biomaterial_id),
                    LocalizationId = NormalizeId(x.localization_id)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.SubServiceId))
                .GroupBy(x => BuildMarketingComplexBiomaterialGroupKey(x.SubServiceId, x.BiomaterialId, x.LocalizationId), StringComparer.OrdinalIgnoreCase)
                .ToList();

            int groupNum = 1;

            foreach (var group in orderedGroups)
            {
                int beforeGroup = result.Count;

                foreach (var row in group)
                {
                    int beforeRow = result.Count;
                    AddFixedBiomaterialFromMarketingItem(result, productIndex, groupNum, service.id, row.SubServiceId, row.Item);

                    if (result.Count == beforeRow && string.IsNullOrWhiteSpace(row.BiomaterialId))
                        AddBiomaterialsForSingleService(result, productIndex, groupNum, service.id, row.SubServiceId);
                }

                if (result.Count > beforeGroup)
                    groupNum++;
            }
        }

        private static string BuildBioSelectionKey(GemotestProductBioMaterial biomaterial)
        {
            if (biomaterial == null)
                return string.Empty;

            return string.Join("|", new string[]
            {
                NormalizeId(biomaterial.Id),
                NormalizeId(biomaterial.ContainerId)
            });
        }

        private void ApplyDefaultBiomaterialSelection(List<GemotestProductBioMaterial> biomaterials)
        {
            if (biomaterials == null)
                return;

            foreach (var group in biomaterials
                .Where(x => x != null)
                .GroupBy(x => new { x.ProductIndex, x.GroupNum }))
            {
                List<GemotestProductBioMaterial> items = group.ToList();
                if (items.Count == 0)
                    continue;

                int distinctBiomaterialsCount = items
                    .Select(x => NormalizeId(x.Id))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                List<string> selectedBiomaterialIds = items
                    .Where(x => x.Chosen)
                    .Select(x => NormalizeId(x.Id))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (selectedBiomaterialIds.Count > 0)
                {
                    if (distinctBiomaterialsCount <= 1)
                    {
                        foreach (GemotestProductBioMaterial item in items)
                            item.Chosen = true;
                    }
                    else
                    {
                        foreach (GemotestProductBioMaterial item in items)
                            item.Chosen = selectedBiomaterialIds.Any(x => SameId(x, item.Id));
                    }

                    continue;
                }

                string firstBiomaterialId = NormalizeId(items[0].Id);

                if (distinctBiomaterialsCount <= 1)
                {
                    foreach (GemotestProductBioMaterial item in items)
                        item.Chosen = true;
                }
                else
                {
                    foreach (GemotestProductBioMaterial item in items)
                        item.Chosen = SameId(item.Id, firstBiomaterialId);
                }
            }
        }

        public List<GemotestProductBioMaterial> GetRequiredBiomaterialsForProduct(int productIndex, GemotestProductDetail product)
        {
            var result = new List<GemotestProductBioMaterial>();

            if (product == null || string.IsNullOrWhiteSpace(product.ProductId))
                return result;

            if (Dicts == null || Dicts.Directory == null)
                return result;

            DictionaryService service;
            if (!Dicts.Directory.TryGetValue(product.ProductId, out service) || IsUnsupportedService(service))
                return result;

            int serviceType = ServiceTypeValue(service);

            if (serviceType == 2)
                AddBiomaterialsForMarketingComplex(result, productIndex, product, service);
            else
                AddBiomaterialsForSingleService(result, productIndex, 1, service.id, service.id);

            ApplyDefaultBiomaterialSelection(result);
            return result;
        }

        public void AddBiomaterialsFromProducts()
        {
            if (BioMaterials == null)
                BioMaterials = new List<GemotestProductBioMaterial>();
            else
                BioMaterials.Clear();

            if (Products == null || Products.Count == 0)
                return;

            if (Dicts == null || Dicts.Directory == null || Dicts.Biomaterials == null)
                return;

            for (int productIndex = 0; productIndex < Products.Count; productIndex++)
            {
                List<GemotestProductBioMaterial> productBiomaterials = GetRequiredBiomaterialsForProduct(productIndex, Products[productIndex]);
                BioMaterials.AddRange(productBiomaterials);
            }

            EnsureDefaultBiomaterialSelection();
        }

        public void EnsureDefaultBiomaterialSelection()
        {
            ApplyDefaultBiomaterialSelection(BioMaterials);
        }

        public bool HasBiomaterialsForProductIndex(int productIndex)
        {
            if (BioMaterials == null)
                return false;

            return BioMaterials.Any(x => x != null && x.ProductIndex == productIndex);
        }

        public void RefreshRequiredBiomaterialsKeepSelection()
        {
            Dictionary<int, HashSet<string>> selectedByProduct = new Dictionary<int, HashSet<string>>();

            if (BioMaterials != null)
            {
                foreach (GemotestProductBioMaterial biomaterial in BioMaterials)
                {
                    if (biomaterial == null || !biomaterial.Chosen)
                        continue;

                    string key = BuildBioSelectionKey(biomaterial);
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    HashSet<string> set;
                    if (!selectedByProduct.TryGetValue(biomaterial.ProductIndex, out set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        selectedByProduct[biomaterial.ProductIndex] = set;
                    }

                    set.Add(key);
                }
            }

            AddBiomaterialsFromProducts();

            if (BioMaterials == null || selectedByProduct.Count == 0)
            {
                ApplyDefaultBiomaterialSelection(BioMaterials);
                return;
            }

            foreach (GemotestProductBioMaterial biomaterial in BioMaterials)
            {
                if (biomaterial == null)
                    continue;

                HashSet<string> set;
                if (!selectedByProduct.TryGetValue(biomaterial.ProductIndex, out set))
                    continue;

                biomaterial.Chosen = set.Contains(BuildBioSelectionKey(biomaterial));
            }

            ApplyDefaultBiomaterialSelection(BioMaterials);
        }

        public void DeleteObsoleteDetails()
        {
            List<int> toDelete = new List<int>();
            for (int i = 0; i < Details.Count; i++)
                if (Details[i].MandatoryProducts.Count == 0 && Details[i].OptionalProducts.Count == 0)
                    toDelete.Add(i);
            foreach (int index in toDelete)
                Details.RemoveAt(index);
        }
        List<int> FindIndexesByCode(string code, OrderItemsCollection products)
        {
            List<int> indexes = new List<int>();
            for (int i = 0; i < products.Count; i++)
                if (products[i].Product.Code == code)
                    indexes.Add(i);
            return indexes;
        }

        public override string Pack()
        {
            using (MemoryStream memStream = new MemoryStream())
            {
                new XmlSerializer(typeof(GemotestOrderDetail)).Serialize(memStream, this);
                memStream.Position = 0;
                return Encoding.UTF8.GetString(memStream.ToArray());
            }
        }
        public override BaseOrderDetail Unpack(string _Source)
        {
            try
            {
                return (GemotestOrderDetail)new XmlSerializer(typeof(GemotestOrderDetail)).Deserialize(new MemoryStream(Encoding.UTF8.GetBytes(_Source)));
            }
            catch (Exception e)
            {
                LogEvent.SaveExceptionToLog(e, GetType().Name);
                return null;
            }
        }

        internal void DeleteProduct(int productIndex)
        {
            for (int i = BioMaterials.Count - 1; i >= 0; i--)
            {
                if (BioMaterials[i].ProductIndex == productIndex)
                    BioMaterials.RemoveAt(i);
            }

            for (int i = BioMaterials.Count - 1; i >= 0; i--)
            {
                if (BioMaterials[i].ProductIndex > productIndex)
                    BioMaterials[i].ProductIndex = BioMaterials[i].ProductIndex - 1;
            }

            DeleteProductFromDetails(productIndex);
        }

        public List<string> GetServiceIdsForCreateOrder()
        {
            var result = new List<string>();

            if (Dicts == null)
                return result;
            if (Products == null)
                return result;

            foreach (var p in Products)
            {
                if (string.IsNullOrEmpty(p.ProductId))
                    continue;

                if (!Dicts.Directory.TryGetValue(p.ProductId, out var service) || service == null)
                    continue;

                if (service.service_type == 2)
                {
                    if (!result.Contains(p.ProductId))
                        result.Add(p.ProductId);

                    if (Dicts.MarketingComplexByComplexId != null &&
                        Dicts.MarketingComplexByComplexId.TryGetValue(p.ProductId, out var complexItems) &&
                        complexItems != null && complexItems.Count > 0)
                    {
                        foreach (var item in complexItems)
                        {
                            if (!string.IsNullOrEmpty(item.service_id) &&
                                !result.Contains(item.service_id))
                            {
                                result.Add(item.service_id);
                            }
                        }
                    }
                }
                else
                {
                    if (!result.Contains(p.ProductId))
                        result.Add(p.ProductId);
                }
            }

            return result;
        }


        public void DeleteProductFromDetails(int productIndex)
        {
            List<GemotestDetail> toDelete = new List<GemotestDetail>();
            for (int i = 0; i < Details.Count; i++)
            {
                if (Details[i].MandatoryProducts.Contains(productIndex) &&
                    Details[i].MandatoryProducts.Count == 1 &&
                    Details[i].OptionalProducts.Count == 0 ||
                    Details[i].OptionalProducts.Contains(productIndex) &&
                    Details[i].MandatoryProducts.Count == 0 &&
                    Details[i].OptionalProducts.Count == 1)
                {
                    toDelete.Add(Details[i]);
                    continue;
                }
                for (int j = 0; j < Details[i].MandatoryProducts.Count; j++)
                    if (Details[i].MandatoryProducts[j] == productIndex)
                        Details[i].MandatoryProducts.RemoveAt(j);
                    else if (Details[i].MandatoryProducts[j] > productIndex)
                        Details[i].MandatoryProducts[j]--;
                for (int j = 0; j < Details[i].OptionalProducts.Count; j++)
                    if (Details[i].OptionalProducts[j] == productIndex)
                        Details[i].OptionalProducts.RemoveAt(j);
                    else if (Details[i].OptionalProducts[j] > productIndex)
                        Details[i].OptionalProducts[j]--;
            }
            foreach (GemotestDetail item in toDelete)
                Details.Remove(item);
        }
    }
}
