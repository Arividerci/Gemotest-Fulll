using System;
using System.Collections.Generic;
using System.Linq;

namespace Laboratory.Gemotest
{
    public sealed class SampleServiceRow
    {
        public string ServiceId { get; set; }
        public string ComplexId { get; set; } // если услуга входит в маркетинговый комплекс

        public int ExecutionSampleId { get; set; }
        public string ExecutionSampleName { get; set; }
        public string ExecutionTransportId { get; set; }
        public bool ExecutionUtilize { get; set; }

        public int? PrimarySampleId { get; set; } // если есть - значит алиquot
        public string PrimarySampleName { get; set; }
        public string PrimaryTransportId { get; set; }
        public bool PrimaryUtilize { get; set; }

        public string BiomaterialId { get; set; }
        public string MicroBioBiomaterialId { get; set; }
        public string LocalizationId { get; set; }

        public int ServiceCount { get; set; } // доля: 100 / service_count

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

        private struct AliquotPackKey : IEquatable<AliquotPackKey>
        {
            public readonly int ExecSampleId;
            public readonly BioKey Bio;
            public readonly string Loc;
            public readonly string Transport;

            public AliquotPackKey(int execSampleId, BioKey bio, string loc, string transport)
            {
                ExecSampleId = execSampleId;
                Bio = bio;
                Loc = loc ?? "";
                Transport = transport ?? "";
            }

            public bool Equals(AliquotPackKey other)
            {
                return ExecSampleId == other.ExecSampleId && Bio.Equals(other.Bio) && Loc == other.Loc && Transport == other.Transport;
            }

            public override bool Equals(object obj)
            {
                return obj is AliquotPackKey && Equals((AliquotPackKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h = h * 31 + ExecSampleId.GetHashCode();
                    h = h * 31 + Bio.GetHashCode();
                    h = h * 31 + Loc.GetHashCode();
                    h = h * 31 + Transport.GetHashCode();
                    return h;
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

        /*public static List<TubePlan> Pack(List<SampleServiceRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            // 0) подготовка work items
            var items = new List<WorkItem>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                int sc = r.ServiceCount <= 0 ? 1 : r.ServiceCount;
                double share = Capacity / sc;

                int drawId = r.PrimarySampleId.HasValue ? r.PrimarySampleId.Value : r.ExecutionSampleId;
                string drawName = r.PrimarySampleId.HasValue ? (r.PrimarySampleName ?? "") : (r.ExecutionSampleName ?? "");
                string drawTransport = r.PrimarySampleId.HasValue ? (r.PrimaryTransportId ?? "") : (r.ExecutionTransportId ?? "");
                bool drawUtilize = r.PrimarySampleId.HasValue ? r.PrimaryUtilize : r.ExecutionUtilize;

                items.Add(new WorkItem
                {
                    Src = r,
                    DrawSampleId = drawId,
                    DrawSampleName = drawName,
                    DrawTransportId = drawTransport,
                    DrawUtilize = drawUtilize,
                    UtilizationFlag = r.ExecutionUtilize ? 1 : 0,
                    Share = share
                });
            }

            // 1) merge utilize -> non-utilize (если совпали bio+loc+transport)
            var groupsForMerge = items.GroupBy(x => new MergeKey(GetBioKey(x.Src), x.Src.LocalizationId ?? "", x.DrawTransportId ?? "")).ToList();
            foreach (var g in groupsForMerge)
            {
                var nonUtil = g.FirstOrDefault(x => x.DrawUtilize == false);
                if (nonUtil == null) continue;

                foreach (var it in g)
                {
                    if (it.DrawUtilize == true)
                    {
                        it.DrawSampleId = nonUtil.DrawSampleId;
                        it.DrawSampleName = nonUtil.DrawSampleName;
                        it.DrawTransportId = nonUtil.DrawTransportId;
                        it.DrawUtilize = nonUtil.DrawUtilize;
                        it.UtilizationFlag = 1;
                    }
                }
            }

            // 2) бин-пэкинг первичных пробирок
            var primaryPlans = new List<TubePlan>();
            var itemsByPrimaryKey = items.GroupBy(x => new PrimaryPackKey(x.DrawSampleId, GetBioKey(x.Src), x.Src.LocalizationId ?? "", x.DrawTransportId ?? "")).ToList();

            foreach (var g in itemsByPrimaryKey)
            {
                var binList = BestFitDecreasing(g.ToList());

                foreach (var b in binList)
                {
                    // первичная пробирка
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

                    // услуги в первичной (aliquot-услуги тут refuse=1)
                    foreach (var it in b.Items)
                    {
                        var r = it.Src;
                        int sc = r.ServiceCount <= 0 ? 1 : r.ServiceCount;
                        double share = Capacity / sc;

                        p.Services.Add(new TubeServicePlan
                        {
                            ServiceId = r.ServiceId ?? "",
                            ComplexId = r.ComplexId ?? "",
                            UtilizationFlag = ResolvePrimaryUtilizationFlag(it),
                            RefuseFlag = 0,
                            ServiceCount = sc,
                            SharePercent = share
                        });
                    }

                    primaryPlans.Add(p);

                   
                }
            }
            return primaryPlans;
        }
*/
        public static List<TubePlan> Pack(List<SampleServiceRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var cleanRows = new List<SampleServiceRow>();

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];

                if (r == null)
                    continue;

                if (string.IsNullOrWhiteSpace(r.ServiceId))
                    continue;

                if (r.ExecutionSampleId <= 0)
                    continue;

                cleanRows.Add(r);
            }

            var result = new List<TubePlan>();

            // 1) Обычные строки без primary_sample_id.
            // Но если это parent-строка для aliquot-услуги, отдельно как обычную пробу ее не отправляем.
            var ordinaryRows = new List<SampleServiceRow>();

            for (int i = 0; i < cleanRows.Count; i++)
            {
                var r = cleanRows[i];

                if (HasPrimary(r))
                    continue;

                if (IsDuplicateAliquotParentRow(r, cleanRows))
                    continue;

                ordinaryRows.Add(r);
            }

            result.AddRange(PackOrdinaryRows(ordinaryRows));

            // 2) Aliquot-строки: создаем parent-пробу и дочернюю execution-пробу.
            var aliquotRows = new List<SampleServiceRow>();

            for (int i = 0; i < cleanRows.Count; i++)
            {
                if (HasPrimary(cleanRows[i]))
                    aliquotRows.Add(cleanRows[i]);
            }

            result.AddRange(PackAliquotRows(aliquotRows, cleanRows));

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

        private static List<TubePlan> PackAliquotRows(List<SampleServiceRow> aliquotRows, List<SampleServiceRow> allRows)
        {
            var result = new List<TubePlan>();

            if (aliquotRows == null || aliquotRows.Count == 0)
                return result;

            var parentItems = new List<WorkItem>();

            for (int i = 0; i < aliquotRows.Count; i++)
            {
                var r = aliquotRows[i];

                if (!HasPrimary(r))
                    continue;

                var parentRow = FindMatchingParentRow(r, allRows);

                int sc = NormalizeServiceCount(r.ServiceCount);
                double share = Capacity / sc;

                string parentName = "";
                string parentTransport = "";
                bool parentUtilize = r.PrimaryUtilize;

                if (parentRow != null)
                {
                    parentName = parentRow.ExecutionSampleName ?? "";
                    parentTransport = parentRow.ExecutionTransportId ?? "";
                    parentUtilize = parentRow.ExecutionUtilize;
                }

                if (string.IsNullOrWhiteSpace(parentName))
                    parentName = r.PrimarySampleName ?? "";

                if (string.IsNullOrWhiteSpace(parentTransport))
                    parentTransport = r.PrimaryTransportId ?? "";

                // Последний fallback, чтобы transport_id не стал пустым.
                if (string.IsNullOrWhiteSpace(parentTransport))
                    parentTransport = r.ExecutionTransportId ?? "";

                parentItems.Add(new WorkItem
                {
                    Src = r,
                    DrawSampleId = r.PrimarySampleId.Value,
                    DrawSampleName = parentName,
                    DrawTransportId = parentTransport,
                    DrawUtilize = parentUtilize,
                    UtilizationFlag = parentUtilize ? 1 : 0,
                    Share = share
                });
            }

            var parentGroups = parentItems
                .GroupBy(x => new PrimaryPackKey(
                    x.DrawSampleId,
                    GetBioKey(x.Src),
                    x.Src.LocalizationId ?? "",
                    x.DrawTransportId ?? ""))
                .ToList();

            foreach (var pg in parentGroups)
            {
                var parentBins = BestFitDecreasing(pg.ToList());

                foreach (var parentBin in parentBins)
                {
                    var parentTube = new TubePlan
                    {
                        Parent = null,
                        SampleId = pg.Key.SampleId,
                        SampleName = parentBin.Items.Count > 0 ? (parentBin.Items[0].DrawSampleName ?? "") : "",
                        TransportId = pg.Key.Transport ?? "",
                        Utilize = parentBin.Items.Count > 0 && parentBin.Items[0].DrawUtilize,
                        BiomaterialId = ResolveBiomaterialId(pg.Key.Bio),
                        MicroBioBiomaterialId = ResolveMicroBioId(pg.Key.Bio),
                        LocalizationId = pg.Key.Loc ?? "",
                        UsedPercent = parentBin.Used
                    };

                    // На parent-пробе услуга должна быть отмечена как отказанная/refuse,
                    // потому что реально выполняется на дочерней aliquot-пробе.
                    foreach (var it in parentBin.Items)
                    {
                        AddTubeServiceIfMissing(
                            parentTube.Services,
                            MakeServicePlan(
                                it.Src,
                                ResolvePrimaryUtilizationFlag(it),
                                1));
                    }

                    result.Add(parentTube);

                    // Теперь создаем дочерние aliquot-пробы.
                    var childGroups = parentBin.Items
                        .GroupBy(x => new AliquotPackKey(
                            x.Src.ExecutionSampleId,
                            GetBioKey(x.Src),
                            x.Src.LocalizationId ?? "",
                            x.Src.ExecutionTransportId ?? ""))
                        .ToList();

                    foreach (var cg in childGroups)
                    {
                        var childBins = BestFitDecreasing(cg.ToList());

                        foreach (var childBin in childBins)
                        {
                            SampleServiceRow first = childBin.Items.Count > 0 ? childBin.Items[0].Src : null;

                            var childTube = new TubePlan
                            {
                                Parent = parentTube,
                                SampleId = cg.Key.ExecSampleId,
                                SampleName = first != null ? (first.ExecutionSampleName ?? "") : "",
                                TransportId = cg.Key.Transport ?? "",
                                Utilize = childBin.Items.Any(x => x.Src != null && x.Src.ExecutionUtilize),
                                BiomaterialId = ResolveBiomaterialId(cg.Key.Bio),
                                MicroBioBiomaterialId = ResolveMicroBioId(cg.Key.Bio),
                                LocalizationId = cg.Key.Loc ?? "",
                                UsedPercent = childBin.Used
                            };

                            foreach (var it in childBin.Items)
                            {
                                AddTubeServiceIfMissing(
                                    childTube.Services,
                                    MakeServicePlan(
                                        it.Src,
                                        ResolveAliquotUtilizationFlag(it),
                                        0));
                            }

                            result.Add(childTube);
                        }
                    }
                }
            }

            return result;
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

        private static bool IsDuplicateAliquotParentRow(SampleServiceRow candidate, List<SampleServiceRow> allRows)
        {
            if (candidate == null || allRows == null)
                return false;

            for (int i = 0; i < allRows.Count; i++)
            {
                var a = allRows[i];

                if (!HasPrimary(a))
                    continue;

                if (IsMatchingAliquotParentRow(candidate, a))
                    return true;
            }

            return false;
        }

        private static SampleServiceRow FindMatchingParentRow(SampleServiceRow aliquotRow, List<SampleServiceRow> allRows)
        {
            if (aliquotRow == null || allRows == null)
                return null;

            for (int i = 0; i < allRows.Count; i++)
            {
                var candidate = allRows[i];

                if (candidate == null)
                    continue;

                if (HasPrimary(candidate))
                    continue;

                if (IsMatchingAliquotParentRow(candidate, aliquotRow))
                    return candidate;
            }

            return null;
        }

        private static bool IsMatchingAliquotParentRow(SampleServiceRow candidateParent, SampleServiceRow aliquotRow)
        {
            if (candidateParent == null || aliquotRow == null)
                return false;

            if (!HasPrimary(aliquotRow))
                return false;

            if (candidateParent.ExecutionSampleId != aliquotRow.PrimarySampleId.Value)
                return false;

            if (!SameText(candidateParent.ServiceId, aliquotRow.ServiceId))
                return false;

            if (!SameText(candidateParent.ComplexId, aliquotRow.ComplexId))
                return false;

            if (!GetBioKey(candidateParent).Equals(GetBioKey(aliquotRow)))
                return false;

            if (!SameText(candidateParent.LocalizationId, aliquotRow.LocalizationId))
                return false;

            // Если primary transport известен, проверяем строго.
            // Если он пустой, не отсекаем parent-строку.
            if (!string.IsNullOrWhiteSpace(aliquotRow.PrimaryTransportId))
            {
                if (!SameText(candidateParent.ExecutionTransportId, aliquotRow.PrimaryTransportId))
                    return false;
            }

            return true;
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

            // 1) Если утильная проба была объединена с рабочей,
            // этот флаг уже выставлен на этапе merge.
            if (it.UtilizationFlag == 1)
                return 1;

            // 2) Если проба сама по себе утильная и не была объединена,
            // всё равно нужно отправлять utilization_flag = 1.
            if (it.DrawUtilize)
                return 1;

            return 0;
        }

        private static int ResolveAliquotUtilizationFlag(WorkItem it)
        {
            if (it == null || it.Src == null) return 0;

            // Для дочерней aliquot-пробы флаг должен отражать именно execution-пробу,
            // а не parent/draw-пробу.
            return it.Src.ExecutionUtilize ? 1 : 0;
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
            return ""; // для микробиологии biomaterial_id может быть пустым
        }

        private static string ResolveMicroBioId(BioKey key)
        {
            if (key.Kind == "MB") return key.Value;
            return "";
        }
    }
}
