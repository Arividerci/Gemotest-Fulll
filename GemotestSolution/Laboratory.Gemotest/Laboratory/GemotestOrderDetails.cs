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
        public int GroupNum { get; set; }
        public bool Chosen { get; set; }
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

                private List<DictionaryBiomaterials> ResolveBiomaterialsForService(DictionaryService service)
        {
            var result = new List<DictionaryBiomaterials>();
            if (service == null)
                return result;

            if (Dicts == null)
                return result;

            if (!string.IsNullOrEmpty(service.biomaterial_id) &&
                !string.Equals(service.biomaterial_id, "Drugoe", StringComparison.OrdinalIgnoreCase))
            {
                if (Dicts.Biomaterials.TryGetValue(service.biomaterial_id, out var biom) && biom != null)
                    result.Add(biom);
            }


            if (service.service_type == 0 && Dicts.ServiceParameters != null)
            {
                if (Dicts.ServiceParameters.TryGetValue(service.id, out var paramsList) &&
                    paramsList != null && paramsList.Count > 0)
                {
                    var ids = paramsList
                        .Select(p => p.biomaterial_id)
                        .Where(id => !string.IsNullOrEmpty(id))
                        .Distinct(StringComparer.OrdinalIgnoreCase);

                    foreach (var id in ids)
                    {
                        if (Dicts.Biomaterials.TryGetValue(id, out var biom) && biom != null &&
                            !result.Any(r => string.Equals(r.id, biom.id, StringComparison.OrdinalIgnoreCase)))
                        {
                            result.Add(biom);
                        }
                    }
                }
            }


            if (service.service_type == 1 || service.service_type == 2)
            {
                List<DictionaryMarketingComplex> complexItems = null;

                if (service.service_type == 2)
                {
                    if (Dicts.MarketingComplexByComplexId != null)
                        Dicts.MarketingComplexByComplexId.TryGetValue(service.id, out complexItems);

                    if (complexItems != null && complexItems.Count > 0)
                    {
                        var service_ids = complexItems
                        .Select(m => m.service_id)
                        .Where(id => !string.IsNullOrEmpty(id))
                        .Distinct(StringComparer.OrdinalIgnoreCase);

                        List<string> biomaterial_ids = new List<string>();
                        foreach (var service_id in service_ids)
                        {
                            if (!Dicts.SamplesServices.ContainsKey(service_id))
                                continue;

                            foreach (var sampleRow in Dicts.SamplesServices[service_id])
                            {
                                if (sampleRow == null)
                                    continue;

                                string find_id = !string.IsNullOrWhiteSpace(sampleRow.biomaterial_id)
                                    ? sampleRow.biomaterial_id
                                    : sampleRow.microbiology_biomaterial_id;

                                if (string.IsNullOrWhiteSpace(find_id))
                                    continue;

                                if (!biomaterial_ids.Any(x => string.Equals(x, find_id, StringComparison.OrdinalIgnoreCase)))
                                    biomaterial_ids.Add(find_id);
                            }
                        }

                        foreach (var id in biomaterial_ids)
                        {
                            if (Dicts.Biomaterials.TryGetValue(id, out var biom) && biom != null &&
                                !result.Any(r => string.Equals(r.id, biom.id, StringComparison.OrdinalIgnoreCase)))
                            {
                                result.Add(biom);
                            }
                        }
                    }
                }
                else
                {
                    if (Dicts.MarketingComplexByServiceId != null)
                        Dicts.MarketingComplexByServiceId.TryGetValue(service.id, out complexItems);

                    if (complexItems != null && complexItems.Count > 0)
                    {
                        var ids = complexItems
                        .Select(m => m.biomaterial_id)
                        .Where(id => !string.IsNullOrEmpty(id))
                        .Distinct(StringComparer.OrdinalIgnoreCase);

                        foreach (var id in ids)
                        {
                            if (Dicts.Biomaterials.TryGetValue(id, out var biom) && biom != null &&
                                !result.Any(r => string.Equals(r.id, biom.id, StringComparison.OrdinalIgnoreCase)))
                            {
                                result.Add(biom);
                            }
                        }
                    }
                }
            }

            if (string.Equals(service.biomaterial_id, "Drugoe", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(service.other_biomaterial))
            {
                if (!result.Any(b => string.Equals(b.id, "Drugoe", StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(new DictionaryBiomaterials
                    {
                        id = "Drugoe",
                        name = service.other_biomaterial,
                        archive = 0
                    });
                }
            }

            return result;
        }

        public void AddBiomaterialsFromProducts()
        {
            if (Products == null || Products.Count == 0)
                return;

            if (Dicts == null)
                return;

            if (Dicts.Directory == null || Dicts.Biomaterials == null)
                return;

            if (BioMaterials == null)
                BioMaterials = new List<GemotestProductBioMaterial>();
            else
                BioMaterials.Clear();

            for (int productIndex = 0; productIndex < Products.Count; productIndex++)
            {
                var product = Products[productIndex];

                if (!Dicts.Directory.TryGetValue(product.ProductId, out var service) || service == null)
                    continue;

                var biomaterialsForService = ResolveBiomaterialsForService(service);
                if (!biomaterialsForService.Any())
                    continue;

                int bioGroupNum = 1;
                foreach (var biom in biomaterialsForService)
                {
                    if (biom == null || string.IsNullOrEmpty(biom.id))
                        continue;

                    var transport = Dicts.ResolveTransport(service.id, biom.id);
                    string containerId = null;
                    string containerName = "";
                    if (transport != null && !string.IsNullOrEmpty(transport.name))
                    {
                        containerId = transport.id;
                        containerName = transport.name;
                    }
                    else
                    {
                        containerId = null;
                        containerName = "-Не указан-";
                    }

                    var existing = BioMaterials.FirstOrDefault(b => b.Id == biom.id && b.ProductIndex == productIndex);
                    if (existing == null)
                    {
                        existing = new GemotestProductBioMaterial
                        {
                            Id = biom.id,
                            Code = biom.id,
                            BiomaterialName = biom.name,
                            ContainerId = containerId,
                            ContainerName = containerName,
                            GroupNum = bioGroupNum,
                            ProductIndex = productIndex,
                            ServiceId = service.id
                        };
                        BioMaterials.Add(existing);
                    }

                    if (service.service_type == 2)
                        bioGroupNum++;
                }
            }

            for (int i = 0; i < Products.Count; i++)
            {
                List<GemotestProductBioMaterial> productBioMaterials = BioMaterials.Where(x => x.ProductIndex == i).ToList();
                //Если в группе биоматериалов только один биоматериал, то его выбираем. Иначе - не выбираем
                foreach (GemotestProductBioMaterial b in productBioMaterials)
                {
                    List<GemotestProductBioMaterial> findList = productBioMaterials.Where(x => x.GroupNum == b.GroupNum).ToList();
                    if (findList.Count == 1)
                        findList[0].Chosen = true;
                }
            }
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
