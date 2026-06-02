using System;
using System.Collections.Generic;
using System.Linq;

namespace Laboratory.Gemotest
{
    public sealed class SampleServiceRow
    {
        public string ServiceId { get; set; }
        public string ComplexId { get; set; }

        public int ExecutionSampleId { get; set; }
        public string ExecutionSampleName { get; set; }
        public string ExecutionTransportId { get; set; }
        public bool ExecutionUtilize { get; set; }

        public int? PrimarySampleId { get; set; }
        public string PrimarySampleName { get; set; }
        public string PrimaryTransportId { get; set; }
        public bool PrimaryUtilize { get; set; }

        public string BiomaterialId { get; set; }
        public string MicroBioBiomaterialId { get; set; }
        public string LocalizationId { get; set; }

        public int ServiceCount { get; set; }

        public SampleServiceRow()
        {
            ServiceId = "";
            ComplexId = "";

            ExecutionSampleName = "";
            ExecutionTransportId = "";

            PrimarySampleName = "";
            PrimaryTransportId = "";

            BiomaterialId = "";
            MicroBioBiomaterialId = "";
            LocalizationId = "";

            ServiceCount = 1;
        }
    }

    public sealed class TubeServicePlan
    {
        public string ServiceId { get; set; }
        public string ComplexId { get; set; }

        public int UtilizationFlag { get; set; }
        public int RefuseFlag { get; set; }

        public int ServiceCount { get; set; }
        public double SharePercent { get; set; }

        public TubeServicePlan()
        {
            ServiceId = "";
            ComplexId = "";
            ServiceCount = 1;
        }
    }

    public sealed class TubePlan
    {
        public int SampleId { get; set; }
        public string SampleName { get; set; }
        public string TransportId { get; set; }
        public bool Utilize { get; set; }

        public string BiomaterialId { get; set; }
        public string MicroBioBiomaterialId { get; set; }
        public string LocalizationId { get; set; }

        public string SampleIdentifier { get; set; }
        public string PrimarySampleIdentifier { get; set; }

        public string OrderSampleGuid { get; set; }
        public string ParentOrderSampleGuid { get; set; }

        public TubePlan Parent { get; set; }

        public double UsedPercent { get; set; }
        public List<TubeServicePlan> Services { get; set; }

        public TubePlan()
        {
            SampleName = "";
            TransportId = "";
            BiomaterialId = "";
            MicroBioBiomaterialId = "";
            LocalizationId = "";

            SampleIdentifier = "";
            PrimarySampleIdentifier = "";
            OrderSampleGuid = "";
            ParentOrderSampleGuid = "";

            Services = new List<TubeServicePlan>();
        }
    }

    public static class GemotestSamplePacker
    {
        private const double Capacity = 100.0;
        private const double Eps = 1e-9;

        private sealed class WorkItem
        {
            public SampleServiceRow Src;

            public int DrawSampleId;
            public string DrawSampleName;
            public string DrawTransportId;
            public bool DrawUtilize;

            public int UtilizationFlag;

            public double Share;
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private struct BioKey : IEquatable<BioKey>
        {
            public readonly string Kind;
            public readonly string Value;

            public BioKey(string kind, string value)
            {
                Kind = kind ?? "";
                Value = value ?? "";
            }

            public bool Equals(BioKey other)
            {
                return Kind == other.Kind && Value == other.Value;
            }

            public override bool Equals(object obj)
            {
                return obj is BioKey && Equals((BioKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Kind.GetHashCode() * 397) ^ Value.GetHashCode();
                }
            }

            public override string ToString()
            {
                return Kind + ":" + Value;
            }
        }

        private struct MergeKey : IEquatable<MergeKey>
        {
            public readonly BioKey Bio;
            public readonly string Loc;
            public readonly string Transport;

            public MergeKey(BioKey bio, string loc, string transport)
            {
                Bio = bio;
                Loc = loc ?? "";
                Transport = transport ?? "";
            }

            public bool Equals(MergeKey other)
            {
                return Bio.Equals(other.Bio) && Loc == other.Loc && Transport == other.Transport;
            }

            public override bool Equals(object obj)
            {
                return obj is MergeKey && Equals((MergeKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h = h * 31 + Bio.GetHashCode();
                    h = h * 31 + Loc.GetHashCode();
                    h = h * 31 + Transport.GetHashCode();
                    return h;
                }
            }
        }

        private struct PrimaryPackKey : IEquatable<PrimaryPackKey>
        {
            public readonly int SampleId;
            public readonly BioKey Bio;
            public readonly string Loc;
            public readonly string Transport;

            public PrimaryPackKey(int sampleId, BioKey bio, string loc, string transport)
            {
                SampleId = sampleId;
                Bio = bio;
                Loc = loc ?? "";
                Transport = transport ?? "";
            }

            public bool Equals(PrimaryPackKey other)
            {
                return SampleId == other.SampleId && Bio.Equals(other.Bio) && Loc == other.Loc && Transport == other.Transport;
            }

            public override bool Equals(object obj)
            {
                return obj is PrimaryPackKey && Equals((PrimaryPackKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h = h * 31 + SampleId.GetHashCode();
                    h = h * 31 + Bio.GetHashCode();
                    h = h * 31 + Loc.GetHashCode();
                    h = h * 31 + Transport.GetHashCode();
                    return h;
                }
            }
        }

        private struct SampleServiceRowKey : IEquatable<SampleServiceRowKey>
        {
            private readonly string _serviceId;
            private readonly string _complexId;
            private readonly int _executionSampleId;
            private readonly string _executionTransportId;
            private readonly bool _executionUtilize;
            private readonly int _primarySampleId;
            private readonly string _primaryTransportId;
            private readonly bool _primaryUtilize;
            private readonly string _biomaterialId;
            private readonly string _microBioBiomaterialId;
            private readonly string _localizationId;
            private readonly int _serviceCount;

            public SampleServiceRowKey(SampleServiceRow row)
            {
                _serviceId = Normalize(row == null ? null : row.ServiceId);
                _complexId = Normalize(row == null ? null : row.ComplexId);
                _executionSampleId = row == null ? 0 : row.ExecutionSampleId;
                _executionTransportId = Normalize(row == null ? null : row.ExecutionTransportId);
                _executionUtilize = row != null && row.ExecutionUtilize;
                _primarySampleId = row != null && row.PrimarySampleId.HasValue ? row.PrimarySampleId.Value : 0;
                _primaryTransportId = Normalize(row == null ? null : row.PrimaryTransportId);
                _primaryUtilize = row != null && row.PrimaryUtilize;
                _biomaterialId = Normalize(row == null ? null : row.BiomaterialId);
                _microBioBiomaterialId = Normalize(row == null ? null : row.MicroBioBiomaterialId);
                _localizationId = Normalize(row == null ? null : row.LocalizationId);
                _serviceCount = row == null || row.ServiceCount <= 0 ? 1 : row.ServiceCount;
            }

            public bool Equals(SampleServiceRowKey other)
            {
                return string.Equals(_serviceId, other._serviceId, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(_complexId, other._complexId, StringComparison.OrdinalIgnoreCase) &&
                       _executionSampleId == other._executionSampleId &&
                       string.Equals(_executionTransportId, other._executionTransportId, StringComparison.OrdinalIgnoreCase) &&
                       _executionUtilize == other._executionUtilize &&
                       _primarySampleId == other._primarySampleId &&
                       string.Equals(_primaryTransportId, other._primaryTransportId, StringComparison.OrdinalIgnoreCase) &&
                       _primaryUtilize == other._primaryUtilize &&
                       string.Equals(_biomaterialId, other._biomaterialId, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(_microBioBiomaterialId, other._microBioBiomaterialId, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(_localizationId, other._localizationId, StringComparison.OrdinalIgnoreCase) &&
                       _serviceCount == other._serviceCount;
            }

            public override bool Equals(object obj)
            {
                return obj is SampleServiceRowKey && Equals((SampleServiceRowKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(_serviceId ?? string.Empty);
                    hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(_complexId ?? string.Empty);
                    hash = (hash * 397) ^ _executionSampleId;
                    hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(_executionTransportId ?? string.Empty);
                    hash = (hash * 397) ^ _executionUtilize.GetHashCode();
                    hash = (hash * 397) ^ _primarySampleId;
                    hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(_primaryTransportId ?? string.Empty);
                    hash = (hash * 397) ^ _primaryUtilize.GetHashCode();
                    hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(_biomaterialId ?? string.Empty);
                    hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(_microBioBiomaterialId ?? string.Empty);
                    hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(_localizationId ?? string.Empty);
                    hash = (hash * 397) ^ _serviceCount;
                    return hash;
                }
            }
        }

private sealed class Bin
        {
            public double Remaining;
            public double Used;
            public List<WorkItem> Items;

            public Bin()
            {
                Remaining = Capacity;
                Used = 0.0;
                Items = new List<WorkItem>();
            }
        }


        public static List<TubePlan> Pack(List<SampleServiceRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var cleanRows = new List<SampleServiceRow>();
            var seenRows = new HashSet<SampleServiceRowKey>();

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];

                if (r == null)
                    continue;

                if (string.IsNullOrWhiteSpace(r.ServiceId))
                    continue;

                if (r.ExecutionSampleId <= 0)
                    continue;

                if (!seenRows.Add(new SampleServiceRowKey(r)))
                    continue;

                cleanRows.Add(r);
            }

            var result = new List<TubePlan>();


            var ordinaryRows = new List<SampleServiceRow>();

            for (int i = 0; i < cleanRows.Count; i++)
            {
                var r = cleanRows[i];

                if (HasPrimary(r))
                    continue;

                ordinaryRows.Add(r);
            }

            result.AddRange(PackOrdinaryRows(ordinaryRows));

            return result;
        }

        private static List<TubePlan> PackOrdinaryRows(List<SampleServiceRow> rows)
        {
            var items = new List<WorkItem>();

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];

                int sc = NormalizeServiceCount(r.ServiceCount);
                double share = Capacity / sc;

                items.Add(new WorkItem
                {
                    Src = r,
                    DrawSampleId = r.ExecutionSampleId,
                    DrawSampleName = r.ExecutionSampleName ?? "",
                    DrawTransportId = r.ExecutionTransportId ?? "",
                    DrawUtilize = r.ExecutionUtilize,
                    UtilizationFlag = r.ExecutionUtilize ? 1 : 0,
                    Share = share
                });
            }

            MergeUtilizeIntoNonUtilize(items);

            var plans = new List<TubePlan>();

            var groups = items
                .GroupBy(x => new PrimaryPackKey(
                    x.DrawSampleId,
                    GetBioKey(x.Src),
                    x.Src.LocalizationId ?? "",
                    x.DrawTransportId ?? ""))
                .ToList();

            foreach (var g in groups)
            {
                var bins = BestFitDecreasing(g.ToList());

                foreach (var b in bins)
                {
                    var p = new TubePlan
                    {
                        Parent = null,
                        SampleId = g.Key.SampleId,
                        SampleName = b.Items.Count > 0 ? (b.Items[0].DrawSampleName ?? "") : "",
                        TransportId = g.Key.Transport ?? "",
                        Utilize = b.Items.Count > 0 && b.Items[0].DrawUtilize,
                        BiomaterialId = ResolveBiomaterialId(g.Key.Bio),
                        MicroBioBiomaterialId = ResolveMicroBioId(g.Key.Bio),
                        LocalizationId = g.Key.Loc ?? "",
                        UsedPercent = b.Used
                    };

                    foreach (var it in b.Items)
                    {
                        AddTubeServiceIfMissing(
                            p.Services,
                            MakeServicePlan(
                                it.Src,
                                ResolvePrimaryUtilizationFlag(it),
                                0));
                    }

                    plans.Add(p);
                }
            }

            return plans;
        }

private static void MergeUtilizeIntoNonUtilize(List<WorkItem> items)
        {
            var groupsForMerge = items
                .GroupBy(x => new MergeKey(
                    GetBioKey(x.Src),
                    x.Src.LocalizationId ?? "",
                    x.DrawTransportId ?? ""))
                .ToList();

            foreach (var g in groupsForMerge)
            {
                var nonUtil = g.FirstOrDefault(x => x.DrawUtilize == false);

                if (nonUtil == null)
                    continue;

                foreach (var it in g)
                {
                    if (it.DrawUtilize)
                    {
                        it.DrawSampleId = nonUtil.DrawSampleId;
                        it.DrawSampleName = nonUtil.DrawSampleName;
                        it.DrawTransportId = nonUtil.DrawTransportId;
                        it.DrawUtilize = nonUtil.DrawUtilize;
                        it.UtilizationFlag = 1;
                    }
                }
            }
        }

        private static TubeServicePlan MakeServicePlan(SampleServiceRow r, int utilizationFlag, int refuseFlag)
        {
            int sc = NormalizeServiceCount(r.ServiceCount);

            return new TubeServicePlan
            {
                ServiceId = r.ServiceId ?? "",
                ComplexId = r.ComplexId ?? "",
                UtilizationFlag = utilizationFlag,
                RefuseFlag = refuseFlag,
                ServiceCount = sc,
                SharePercent = Capacity / sc
            };
        }

        private static void AddTubeServiceIfMissing(List<TubeServicePlan> list, TubeServicePlan item)
        {
            if (list == null || item == null)
                return;

            for (int i = 0; i < list.Count; i++)
            {
                var x = list[i];

                if (x == null)
                    continue;

                if (SameText(x.ServiceId, item.ServiceId) &&
                    SameText(x.ComplexId, item.ComplexId) &&
                    x.RefuseFlag == item.RefuseFlag)
                {
                    if (item.UtilizationFlag == 1)
                        x.UtilizationFlag = 1;

                    return;
                }
            }

            list.Add(item);
        }

        private static bool HasPrimary(SampleServiceRow r)
        {
            return r != null && r.PrimarySampleId.HasValue && r.PrimarySampleId.Value > 0;
        }

private static int NormalizeServiceCount(int serviceCount)
        {
            return serviceCount <= 0 ? 1 : serviceCount;
        }

        private static bool SameText(string a, string b)
        {
            return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
        }

        private static string Norm(string s)
        {
            return (s ?? "").Trim();
        }

        private static List<Bin> BestFitDecreasing(List<WorkItem> workItems)
        {
            workItems.Sort((a, b) => b.Share.CompareTo(a.Share));

            var bins = new List<Bin>();

            for (int i = 0; i < workItems.Count; i++)
            {
                var it = workItems[i];

                Bin best = null;
                double bestRemain = double.MaxValue;

                for (int j = 0; j < bins.Count; j++)
                {
                    var b = bins[j];
                    if (b.Remaining + Eps >= it.Share)
                    {
                        double rem = b.Remaining - it.Share;
                        if (rem < bestRemain)
                        {
                            bestRemain = rem;
                            best = b;
                        }
                    }
                }

                if (best == null)
                {
                    best = new Bin();
                    bins.Add(best);
                }

                best.Items.Add(it);
                best.Remaining -= it.Share;
                best.Used += it.Share;
            }

            return bins;
        }

        private static int ResolvePrimaryUtilizationFlag(WorkItem it)
        {
            if (it == null) return 0;


            if (it.UtilizationFlag == 1)
                return 1;


            if (it.DrawUtilize)
                return 1;

            return 0;
        }

private static BioKey GetBioKey(SampleServiceRow r)
        {
            if (!string.IsNullOrWhiteSpace(r.MicroBioBiomaterialId))
                return new BioKey("MB", r.MicroBioBiomaterialId.Trim());

            return new BioKey("BM", (r.BiomaterialId ?? "").Trim());
        }

        private static string ResolveBiomaterialId(BioKey key)
        {
            if (key.Kind == "BM") return key.Value;
            return "";
        }

        private static string ResolveMicroBioId(BioKey key)
        {
            if (key.Kind == "MB") return key.Value;
            return "";
        }
    }
}
