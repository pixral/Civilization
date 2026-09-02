using Civ.Engine;
using Civ.Engine.Events;
using Civ.Engine.State;
using Civ.Systems;

namespace Civ.Batch;

/// <summary>Running sums for a Pearson correlation, so results combine exactly across seeds.</summary>
internal readonly record struct Correlation(double SumX, double SumY, double SumXX, double SumYY, double SumXY, long N)
{
    public static Correlation operator +(Correlation a, Correlation b) => new(
        a.SumX + b.SumX, a.SumY + b.SumY, a.SumXX + b.SumXX,
        a.SumYY + b.SumYY, a.SumXY + b.SumXY, a.N + b.N);

    public Correlation Add(double x, double y) =>
        new(SumX + x, SumY + y, SumXX + (x * x), SumYY + (y * y), SumXY + (x * y), N + 1);

    public double R
    {
        get
        {
            if (N < 2)
            {
                return 0;
            }

            double numerator = (N * SumXY) - (SumX * SumY);
            double denominator = Math.Sqrt(((N * SumXX) - (SumX * SumX)) * ((N * SumYY) - (SumY * SumY)));
            return denominator == 0 ? 0 : numerator / denominator;
        }
    }
}

/// <summary>Counts of windows examined and of those that ended in a major territorial loss.</summary>
internal readonly record struct LossWindow(int Windows, int MajorLosses)
{
    public static LossWindow operator +(LossWindow a, LossWindow b) =>
        new(a.Windows + b.Windows, a.MajorLosses + b.MajorLosses);

    public LossWindow Observe(bool major) => new(Windows + 1, MajorLosses + (major ? 1 : 0));

    public double Rate => Windows == 0 ? 0 : 100.0 * MajorLosses / Windows;
}

/// <summary>Summed territory change, so band means aggregate exactly across seeds.</summary>
internal readonly record struct Delta(long Sum, int Count)
{
    public static Delta operator +(Delta a, Delta b) => new(a.Sum + b.Sum, a.Count + b.Count);

    public Delta Add(int change) => new(Sum + change, Count + 1);

    public double Mean => Count == 0 ? 0 : (double)Sum / Count;
}

/// <summary>
/// Territory lost over a window, split into the periphery reach can hold and the core it cannot.
/// </summary>
/// <remarks>
/// The reach benefit only ever touches regions four or more steps from the capital, so if it is
/// doing what it claims, the territory a realm sheds when a strong administrator is replaced should
/// be disproportionately remote. If losses after a succession look like losses in any other year -
/// same mix of core and periphery - then whatever is contracting the realm is not this modifier.
/// </remarks>
internal readonly record struct RemoteLoss(int Windows, long CoreLost, long RemoteLost)
{
    public static RemoteLoss operator +(RemoteLoss a, RemoteLoss b) =>
        new(a.Windows + b.Windows, a.CoreLost + b.CoreLost, a.RemoteLost + b.RemoteLost);

    public RemoteLoss Observe(int core, int remote) =>
        new(Windows + 1, CoreLost + core, RemoteLost + remote);

    public double MeanCore => Windows == 0 ? 0 : (double)CoreLost / Windows;

    public double MeanRemote => Windows == 0 ? 0 : (double)RemoteLost / Windows;

    /// <summary>Share of all territory lost that was remote. Zero when nothing was lost.</summary>
    public double RemoteSharePercent
    {
        get
        {
            long total = Math.Max(0, CoreLost) + Math.Max(0, RemoteLost);
            return total == 0 ? 0 : 100.0 * Math.Max(0, RemoteLost) / total;
        }
    }
}

/// <summary>
/// How often ruler ability actually reaches the cohesion decision.
/// </summary>
/// <remarks>
/// The direct mechanism check, gathered by <see cref="MechanismObserverSystem"/> from the state
/// cohesion sees rather than reconstructed afterwards. Every other ruler statistic is downstream of
/// this: if a polity's restive set is identical under its real ruler and under a notional average
/// one, ruler quality made no difference that year whatever happened to the map later.
/// </remarks>
internal readonly record struct MechanismCounts(
    long PolityYears,
    long Changed,
    long Exposed,
    long Held,
    long RegionsExposed,
    long RegionsHeld)
{
    public static MechanismCounts operator +(MechanismCounts a, MechanismCounts b) => new(
        a.PolityYears + b.PolityYears,
        a.Changed + b.Changed,
        a.Exposed + b.Exposed,
        a.Held + b.Held,
        a.RegionsExposed + b.RegionsExposed,
        a.RegionsHeld + b.RegionsHeld);

    public double ChangedRate => PolityYears == 0 ? 0 : 100.0 * Changed / PolityYears;
}

/// <summary>
/// Everything the ruler layer is judged on, computed after a run from the chronicle, the end-of-year
/// records, and the in-phase mechanism observations.
/// </summary>
/// <remarks>
/// Kept as summable components rather than finished percentages so that twenty seeds aggregate into
/// one exact figure instead of an average of averages.
/// </remarks>
internal sealed record RulerAnalysis(
    int NaturalDeaths,
    int ReignsEndedByExtinction,
    int ReignsEndedByDisplacement,
    int Successions,
    long ReignYears,
    int CompletedReigns,
    int MinReign,
    int MaxReign,
    int[] ZeroYearReignsByReason,
    int[] AbilityHistogram,
    long[] BandPolityYears,
    long[] BandRegionYears,
    Correlation Immediate,
    Correlation Lagged,
    LossWindow AfterSuccession,
    LossWindow Ordinary,
    LossWindow AfterStrongerSuccessor,
    LossWindow AfterWeakerSuccessor,
    LossWindow[] BandLosses,
    LossWindow AfterSuccessionLarge,
    LossWindow OrdinaryLarge,
    Delta[] BandReignDelta,
    Delta[] BandFirst25Delta,
    MechanismCounts Mechanism,
    MechanismCounts MechanismLarge,
    LossWindow AfterLargeAbilityDrop,
    long[] ExpansionsByBand,
    long ExpansionsAttributed,
    long ExpansionsByLargePolity,
    int[] MilitaryHistogram,
    long[] ExpansionsByMilitaryBand,
    Delta[] QuadrantReignDelta,
    Delta BothHighReignDelta,
    Delta BothLowReignDelta,
    EpisodeStats At25,
    EpisodeStats At20,
    DistanceMechanism Distance,
    IReadOnlyList<EpisodeRecord> EpisodeLog,
    RemoteLoss RemoteAfterSuccession,
    RemoteLoss RemoteOrdinary,
    RemoteLoss RemoteAfterStrongAdministrator)
{
    internal const int Bands = 5;

    internal const int EndReasons = 3;

    internal static readonly string[] BandNames = ["0-19", "20-39", "40-59", "60-79", "80-100"];

    internal static readonly string[] EndReasonNames = ["natural death", "polity extinct", "displaced"];

    /// <summary>Length of the window in which a loss is looked for.</summary>
    internal const int WindowYears = 25;

    /// <summary>Ability points a successor must fall short by to count as a large administrative drop.</summary>
    internal const int LargeAbilityDrop = 30;

    /// <summary>
    /// Regions above which a polity is treated as large enough for capacity to bind.
    /// </summary>
    /// <remarks>
    /// A small state sits far inside any capacity its ruler could have, so its fate says nothing
    /// about ruler quality. Including those polity-years dilutes every ruler statistic toward zero,
    /// which is why the same measurements are repeated over large states alone.
    /// </remarks>
    internal const int LargePolityRegions = 20;

    /// <summary>The four combinations of the two abilities, split at the neutral value.</summary>
    internal const int Quadrants = 4;

    internal static readonly string[] QuadrantNames =
        ["low adm / low mil", "low adm / high mil", "high adm / low mil", "high adm / high mil"];

    /// <summary>Ability at or above which a ruler counts as exceptional in both dimensions.</summary>
    internal const int Exceptional = 70;

    internal static int QuadrantOf(int administration, int military) =>
        (administration >= 50 ? 2 : 0) + (military >= 50 ? 1 : 0);

    internal static int BandOf(int ability) => Math.Clamp(ability / 20, 0, Bands - 1);

    public static RulerAnalysis Empty => new(
        0, 0, 0, 0, 0, 0, int.MaxValue, 0,
        new int[EndReasons],
        new int[Bands], new long[Bands], new long[Bands],
        default, default, default, default, default, default,
        [.. Enumerable.Repeat(default(LossWindow), Bands)],
        default, default,
        [.. Enumerable.Repeat(default(Delta), Bands)],
        [.. Enumerable.Repeat(default(Delta), Bands)],
        default, default,
        default, new long[Bands], 0, 0,
        new int[Bands], new long[Bands],
        [.. Enumerable.Repeat(default(Delta), Quadrants)], default, default,
        EpisodeStats.Empty, EpisodeStats.Empty, DistanceMechanism.Empty,
        [], default, default, default);

    public static RulerAnalysis operator +(RulerAnalysis a, RulerAnalysis b) => new(
        a.NaturalDeaths + b.NaturalDeaths,
        a.ReignsEndedByExtinction + b.ReignsEndedByExtinction,
        a.ReignsEndedByDisplacement + b.ReignsEndedByDisplacement,
        a.Successions + b.Successions,
        a.ReignYears + b.ReignYears,
        a.CompletedReigns + b.CompletedReigns,
        Math.Min(a.MinReign, b.MinReign),
        Math.Max(a.MaxReign, b.MaxReign),
        [.. a.ZeroYearReignsByReason.Zip(b.ZeroYearReignsByReason, (x, y) => x + y)],
        [.. a.AbilityHistogram.Zip(b.AbilityHistogram, (x, y) => x + y)],
        [.. a.BandPolityYears.Zip(b.BandPolityYears, (x, y) => x + y)],
        [.. a.BandRegionYears.Zip(b.BandRegionYears, (x, y) => x + y)],
        a.Immediate + b.Immediate,
        a.Lagged + b.Lagged,
        a.AfterSuccession + b.AfterSuccession,
        a.Ordinary + b.Ordinary,
        a.AfterStrongerSuccessor + b.AfterStrongerSuccessor,
        a.AfterWeakerSuccessor + b.AfterWeakerSuccessor,
        [.. a.BandLosses.Zip(b.BandLosses, (x, y) => x + y)],
        a.AfterSuccessionLarge + b.AfterSuccessionLarge,
        a.OrdinaryLarge + b.OrdinaryLarge,
        [.. a.BandReignDelta.Zip(b.BandReignDelta, (x, y) => x + y)],
        [.. a.BandFirst25Delta.Zip(b.BandFirst25Delta, (x, y) => x + y)],
        a.Mechanism + b.Mechanism,
        a.MechanismLarge + b.MechanismLarge,
        a.AfterLargeAbilityDrop + b.AfterLargeAbilityDrop,
        [.. a.ExpansionsByBand.Zip(b.ExpansionsByBand, (x, y) => x + y)],
        a.ExpansionsAttributed + b.ExpansionsAttributed,
        a.ExpansionsByLargePolity + b.ExpansionsByLargePolity,
        [.. a.MilitaryHistogram.Zip(b.MilitaryHistogram, (x, y) => x + y)],
        [.. a.ExpansionsByMilitaryBand.Zip(b.ExpansionsByMilitaryBand, (x, y) => x + y)],
        [.. a.QuadrantReignDelta.Zip(b.QuadrantReignDelta, (x, y) => x + y)],
        a.BothHighReignDelta + b.BothHighReignDelta,
        a.BothLowReignDelta + b.BothLowReignDelta,
        a.At25 + b.At25,
        a.At20 + b.At20,
        a.Distance + b.Distance,
        [.. a.EpisodeLog, .. b.EpisodeLog],
        a.RemoteAfterSuccession + b.RemoteAfterSuccession,
        a.RemoteOrdinary + b.RemoteOrdinary,
        a.RemoteAfterStrongAdministrator + b.RemoteAfterStrongAdministrator);

    public double MeanReign => CompletedReigns == 0 ? 0 : (double)ReignYears / CompletedReigns;

    public double MeanSizeInBand(int band) =>
        BandPolityYears[band] == 0 ? 0 : (double)BandRegionYears[band] / BandPolityYears[band];

    /// <summary>One year's record of a polity: how big it was and who was ruling at year end.</summary>
    internal readonly record struct PolityYear(int Regions, int Admin, int Mil = 50);

    // ------------------------------------------------------------------ timing primitives

    /// <summary>
    /// Territory held at the start of a year, which is the end of the year before.
    /// </summary>
    /// <remarks>
    /// The single most important index in this file. "Territory at accession" must be read before the
    /// accession year is simulated, because a weak successor can lose half a realm later in that same
    /// year, and taking the reading afterwards defines exactly the losses being studied out of the
    /// measurement.
    /// </remarks>
    internal static int? RegionsAtStartOfYear(
        IReadOnlyList<Dictionary<PolityId, PolityYear>> endOfYear,
        int startYear,
        PolityId polity,
        int year)
    {
        int index = year - startYear - 1;
        return index >= 0 && index < endOfYear.Count
            ? endOfYear[index].GetValueOrDefault(polity).Regions
            : null;
    }

    /// <summary>
    /// Whether the polity lost at least a quarter of its land at any point in the window.
    /// </summary>
    /// <remarks>
    /// <para>Baseline is the start of <paramref name="year"/>; the 25 outcomes are the end-of-year
    /// figures for that year through <c>year + 24</c>, so a loss caused later in the accession year
    /// itself is counted.</para>
    ///
    /// <para>Null when the baseline or the full window is unavailable, so incomplete windows are
    /// excluded rather than measured against a truncated horizon. Extinction reads as zero regions,
    /// which is total loss. Compared with <c>after * 4 &lt;= before * 3</c>, so losing exactly a
    /// quarter counts and no rounding decides the result.</para>
    /// </remarks>
    internal static bool? MajorLoss(
        IReadOnlyList<Dictionary<PolityId, PolityYear>> endOfYear,
        int startYear,
        PolityId polity,
        int year)
    {
        if (RegionsAtStartOfYear(endOfYear, startYear, polity, year) is not { } before || before == 0)
        {
            return null;
        }

        int first = year - startYear;
        if (first < 0 || first + WindowYears - 1 >= endOfYear.Count)
        {
            return null;
        }

        for (int k = 0; k < WindowYears; k++)
        {
            int after = endOfYear[first + k].GetValueOrDefault(polity).Regions;
            if (after * 4 <= before * 3)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Territory a ruler gained or lost across their whole reign.
    /// </summary>
    /// <remarks>
    /// Measured from the start of their accession year to the start of the year their reign ended.
    /// A ruler therefore owns every change in years <c>[accession, reignEnd - 1]</c>, and their
    /// successor owns the accession year itself. Consecutive reigns partition the timeline exactly:
    /// one ruler's end index is the next one's start index, so no year is counted twice or dropped.
    /// </remarks>
    internal static int? ReignDelta(
        IReadOnlyList<Dictionary<PolityId, PolityYear>> endOfYear,
        int startYear,
        Ruler ruler)
    {
        if (ruler.ReignEndYear is not { } end)
        {
            return null;
        }

        int? from = RegionsAtStartOfYear(endOfYear, startYear, ruler.Polity, ruler.AccessionYear);
        int? to = RegionsAtStartOfYear(endOfYear, startYear, ruler.Polity, end);

        return from is null || to is null ? null : to - from;
    }

    /// <summary>
    /// Territory change over the first 25 years of a reign, for reigns that lasted at least that long.
    /// </summary>
    /// <remarks>
    /// Requires the reign to span the whole window; otherwise the figure would include a successor's
    /// record and attribute it to the predecessor.
    /// </remarks>
    internal static int? First25Delta(
        IReadOnlyList<Dictionary<PolityId, PolityYear>> endOfYear,
        int startYear,
        Ruler ruler)
    {
        int end = ruler.ReignEndYear ?? int.MaxValue;
        if (end - ruler.AccessionYear < WindowYears)
        {
            return null;
        }

        int? from = RegionsAtStartOfYear(endOfYear, startYear, ruler.Polity, ruler.AccessionYear);
        int? to = RegionsAtStartOfYear(
            endOfYear, startYear, ruler.Polity, ruler.AccessionYear + WindowYears);

        return from is null || to is null ? null : to - from;
    }

    // ------------------------------------------------------------------ aggregation

    /// <summary>
    /// Finds every continuous stretch in which one polity held at least the threshold share.
    /// </summary>
    /// <remarks>
    /// An "empire episode". Tracked per polity rather than per world, so two powers cresting in
    /// different centuries are two episodes and not one long smear. The ruler at the peak year is
    /// recorded because the question is whether exceptional rulers build these, and the successor's
    /// ability because the question after that is whether weaker heirs lose them.
    /// </remarks>
    internal static (EpisodeStats Stats, List<EpisodeRecord> Log) Episodes(
        SampledRun run,
        int thresholdPercent,
        IReadOnlyDictionary<PolityId, List<Succession>> successions)
    {
        int total = run.Simulation.World.Regions.Count;
        var log = new List<EpisodeRecord>();
        if (total == 0)
        {
            return (EpisodeStats.Empty, log);
        }

        var open = new Dictionary<PolityId, OpenEpisode>();
        var stats = new EpisodeStats.Accumulator();

        for (int offset = 0; offset <= run.EndOfYear.Count; offset++)
        {
            var seen = new HashSet<PolityId>();

            if (offset < run.EndOfYear.Count)
            {
                foreach ((PolityId id, PolityYear year) in run.EndOfYear[offset])
                {
                    int share = year.Regions * 100 / total;
                    if (share < thresholdPercent)
                    {
                        continue;
                    }

                    seen.Add(id);

                    if (!open.TryGetValue(id, out OpenEpisode episode))
                    {
                        open[id] = new OpenEpisode(
                            offset, offset, share, year.Admin, year.Mil, year.Admin);
                    }
                    else if (share > episode.PeakShare)
                    {
                        open[id] = episode with
                        {
                            PeakOffset = offset,
                            PeakShare = share,
                            PeakAdmin = year.Admin,
                            PeakMilitary = year.Mil,
                        };
                    }
                }
            }

            // Anything open but not seen this year has just ended.
            foreach (PolityId id in open.Keys.Order().ToList())
            {
                if (seen.Contains(id))
                {
                    continue;
                }

                OpenEpisode episode = open[id];
                open.Remove(id);

                bool extinct = offset >= run.EndOfYear.Count
                    || !run.EndOfYear[offset].ContainsKey(id);

                int adminAfter = extinct || offset >= run.EndOfYear.Count
                    ? 0
                    : run.EndOfYear[offset][id].Admin;

                int duration = offset - episode.StartOffset;

                stats.Add(
                    duration,
                    episode.PeakShare,
                    episode.PeakAdmin,
                    episode.PeakMilitary,
                    extinct,
                    !extinct && adminAfter < episode.PeakAdmin);

                int endYear = run.StartYear + offset;
                int after = offset + WindowYears;

                log.Add(new EpisodeRecord(
                    run.Simulation.World.Seed,
                    run.Simulation.World.Polities.TryGet(id, out Polity? state)
                        ? state.Name
                        : $"polity {id.Index}",
                    run.StartYear + episode.StartOffset,
                    run.StartYear + episode.PeakOffset,
                    episode.PeakShare,
                    duration,
                    episode.StartAdmin,
                    episode.PeakAdmin,
                    episode.PeakMilitary,
                    extinct,
                    AdminChangeNear(successions, id, endYear),
                    RegionsAt(run.EndOfYear, offset, id),
                    after < run.EndOfYear.Count ? RegionsAt(run.EndOfYear, after, id) : 0));
            }
        }

        return (stats.Snapshot(), log);
    }

    /// <summary>An episode still in progress, carried across years while it is open.</summary>
    private readonly record struct OpenEpisode(
        int StartOffset,
        int PeakOffset,
        int PeakShare,
        int PeakAdmin,
        int PeakMilitary,
        int StartAdmin);

    /// <summary>One change of ruler, and how far administrative ability moved with it.</summary>
    internal readonly record struct Succession(int Year, int Administration, int Change)
    {
        /// <summary>The outgoing ruler. Successions at a founding are not recorded, so this exists.</summary>
        public int Predecessor => Administration - Change;
    }

    private static int RegionsAt(
        IReadOnlyList<Dictionary<PolityId, PolityYear>> endOfYear, int offset, PolityId polity) =>
        offset >= 0 && offset < endOfYear.Count
            ? endOfYear[offset].GetValueOrDefault(polity).Regions
            : 0;

    /// <summary>
    /// The administrative change at the last succession before an episode ended, if there was one.
    /// </summary>
    /// <remarks>
    /// Within the standard 25-year window, so "ended after a succession" spans the same stretch of
    /// time as every other loss measurement here. Null when no succession fell in that window, which
    /// is the honest answer for an episode that was simply conquered.
    /// </remarks>
    private static int? AdminChangeNear(
        IReadOnlyDictionary<PolityId, List<Succession>> successions, PolityId polity, int endYear)
    {
        if (!successions.TryGetValue(polity, out List<Succession>? list))
        {
            return null;
        }

        int? change = null;
        foreach (Succession succession in list)
        {
            if (succession.Year > endYear - WindowYears && succession.Year <= endYear)
            {
                change = succession.Change;
            }
        }

        return change;
    }

    /// <summary>
    /// Whether territory lost over a window was the remote periphery or the core.
    /// </summary>
    /// <remarks>
    /// <para>Read from the in-phase territory shapes, because distance from the capital cannot be
    /// recovered from end-of-year region counts - it depends on the shape of the realm in that year,
    /// which the observer is the only thing that saw.</para>
    ///
    /// <para>A polity absent at the far end of the window counts as having lost everything, so
    /// extinction is not quietly dropped from the comparison it would otherwise dominate.</para>
    /// </remarks>
    private static (RemoteLoss AfterSuccession, RemoteLoss Ordinary, RemoteLoss AfterStrongPredecessor)
        RemoteLosses(
            SampledRun run,
            MechanismSink mechanism,
            IReadOnlySet<(PolityId Polity, int Year)> successionYears,
            IReadOnlySet<(PolityId Polity, int Year)> afterStrongPredecessor)
    {
        RemoteLoss afterSuccession = default;
        RemoteLoss ordinary = default;
        RemoteLoss afterStrong = default;

        for (int offset = 0; offset + WindowYears < run.EndOfYear.Count; offset++)
        {
            int year = run.StartYear + offset;

            foreach (PolityId polity in run.EndOfYear[offset].Keys)
            {
                if (!mechanism.Shape.TryGetValue((year, polity), out TerritoryShape before)
                    || before.Regions == 0)
                {
                    continue;
                }

                // Absent at the far end means extinct, which is total loss rather than missing data.
                TerritoryShape after = run.EndOfYear[offset + WindowYears].ContainsKey(polity)
                    ? mechanism.Shape.GetValueOrDefault(
                        (year + WindowYears, polity), new TerritoryShape(0, 0))
                    : new TerritoryShape(0, 0);

                int core = before.Core - after.Core;
                int remote = before.Remote - after.Remote;

                if (successionYears.Contains((polity, year)))
                {
                    afterSuccession = afterSuccession.Observe(core, remote);

                    if (afterStrongPredecessor.Contains((polity, year)))
                    {
                        afterStrong = afterStrong.Observe(core, remote);
                    }
                }
                else
                {
                    ordinary = ordinary.Observe(core, remote);
                }
            }
        }

        return (afterSuccession, ordinary, afterStrong);
    }

    public static RulerAnalysis Compute(SampledRun run, MechanismSink mechanism)
    {
        int startYear = run.StartYear;
        Simulation sim = run.Simulation;
        IReadOnlyList<Dictionary<PolityId, PolityYear>> endOfYear = run.EndOfYear;

        var histogram = new int[Bands];
        var militaryHistogram = new int[Bands];
        var quadrantDelta = new Delta[Quadrants];
        Delta bothHigh = default;
        Delta bothLow = default;
        var zeroYear = new int[EndReasons];
        var bandReignDelta = new Delta[Bands];
        var bandFirst25 = new Delta[Bands];
        long reignYears = 0;
        int completed = 0;
        int minReign = int.MaxValue;
        int maxReign = 0;

        foreach (Ruler ruler in sim.World.Rulers.All())
        {
            int band = BandOf(ruler.Administration);
            histogram[band]++;
            militaryHistogram[BandOf(ruler.Military)]++;

            if (ReignDelta(endOfYear, startYear, ruler) is { } reignChange)
            {
                bandReignDelta[band] = bandReignDelta[band].Add(reignChange);

                int quadrant = QuadrantOf(ruler.Administration, ruler.Military);
                quadrantDelta[quadrant] = quadrantDelta[quadrant].Add(reignChange);

                if (ruler.Administration >= Exceptional && ruler.Military >= Exceptional)
                {
                    bothHigh = bothHigh.Add(reignChange);
                }
                else if (ruler.Administration <= 100 - Exceptional && ruler.Military <= 100 - Exceptional)
                {
                    bothLow = bothLow.Add(reignChange);
                }
            }

            if (First25Delta(endOfYear, startYear, ruler) is { } firstChange)
            {
                bandFirst25[band] = bandFirst25[band].Add(firstChange);
            }

            if (ruler.ReignEndYear is not { } end)
            {
                continue;
            }

            int length = end - ruler.AccessionYear;
            reignYears += length;
            completed++;
            minReign = Math.Min(minReign, length);
            maxReign = Math.Max(maxReign, length);

            if (length == 0 && ruler.EndReason is { } reason)
            {
                zeroYear[(int)reason]++;
            }
        }

        var bandPolityYears = new long[Bands];
        var bandRegionYears = new long[Bands];
        Correlation immediate = default;
        Correlation lagged = default;

        for (int offset = 0; offset < endOfYear.Count; offset++)
        {
            foreach ((PolityId polity, PolityYear year) in endOfYear[offset])
            {
                int band = BandOf(year.Admin);
                bandPolityYears[band]++;
                bandRegionYears[band] += year.Regions;
                immediate = immediate.Add(year.Admin, year.Regions);

                int later = offset + WindowYears;
                if (later < endOfYear.Count)
                {
                    lagged = lagged.Add(year.Admin, endOfYear[later].GetValueOrDefault(polity).Regions);
                }
            }
        }

        // Accessions in chronicle order, so each polity's previous ruler is known when the next one
        // arrives. Foundings are excluded: there is no predecessor to compare against.
        var previousAbility = new Dictionary<PolityId, int>();
        var successionYears = new HashSet<(PolityId Polity, int Year)>();

        // Every succession, keyed by polity, for the episode histories; and the subset in which an
        // exceptional administrator was replaced by a lesser one, which is the case this experiment
        // predicts should cost a realm its periphery.
        var successionLog = new Dictionary<PolityId, List<Succession>>();
        var afterStrongPredecessor = new HashSet<(PolityId Polity, int Year)>();
        LossWindow afterSuccession = default;
        LossWindow afterSuccessionLarge = default;
        LossWindow stronger = default;
        LossWindow weaker = default;
        LossWindow largeDrop = default;
        var bandLosses = new LossWindow[Bands];
        int successions = 0;

        foreach (RulerAccessionEvent accession in sim.Chronicle.Events.OfType<RulerAccessionEvent>())
        {
            if (accession.Reason == RulerSuccessionSystem.SuccessionReason)
            {
                successions++;
                successionYears.Add((accession.Polity, accession.Year));

                if (MajorLoss(endOfYear, startYear, accession.Polity, accession.Year) is { } outcome)
                {
                    int band = BandOf(accession.Administration);
                    afterSuccession = afterSuccession.Observe(outcome);
                    bandLosses[band] = bandLosses[band].Observe(outcome);

                    int held = RegionsAtStartOfYear(
                        endOfYear, startYear, accession.Polity, accession.Year) ?? 0;

                    if (held >= LargePolityRegions)
                    {
                        afterSuccessionLarge = afterSuccessionLarge.Observe(outcome);
                    }

                    if (previousAbility.TryGetValue(accession.Polity, out int before))
                    {
                        if (accession.Administration >= before)
                        {
                            stronger = stronger.Observe(outcome);
                        }
                        else
                        {
                            weaker = weaker.Observe(outcome);
                        }

                        // A sharp fall is the case the relative split cannot see: a successor 30
                        // points below their predecessor is a different event from one 3 points below.
                        if (before - accession.Administration >= LargeAbilityDrop)
                        {
                            largeDrop = largeDrop.Observe(outcome);
                        }
                    }
                }
            }

            if (previousAbility.TryGetValue(accession.Polity, out int predecessor)
                && accession.Reason == RulerSuccessionSystem.SuccessionReason)
            {
                if (!successionLog.TryGetValue(accession.Polity, out List<Succession>? list))
                {
                    list = [];
                    successionLog[accession.Polity] = list;
                }

                list.Add(new Succession(
                    accession.Year,
                    accession.Administration,
                    accession.Administration - predecessor));

                if (predecessor >= Exceptional && accession.Administration < predecessor)
                {
                    afterStrongPredecessor.Add((accession.Polity, accession.Year));
                }
            }

            previousAbility[accession.Polity] = accession.Administration;
        }

        (RemoteLoss remoteAfterSuccession, RemoteLoss remoteOrdinary, RemoteLoss remoteAfterStrong) =
            RemoteLosses(run, mechanism, successionYears, afterStrongPredecessor);

        EpisodeStats at25 = Episodes(run, 25, successionLog).Stats;

        // Histories are kept at the diagnostic threshold: every 25% episode lies inside a 20% one,
        // so the lower cut loses no empire and catches the ones that never quite got there.
        (EpisodeStats at20, List<EpisodeRecord> log20) = Episodes(run, 20, successionLog);

        // The control: every other polity-year, measured with the identical baseline convention.
        LossWindow ordinary = default;
        LossWindow ordinaryLarge = default;
        for (int offset = 0; offset < endOfYear.Count; offset++)
        {
            int year = startYear + offset;
            foreach (PolityId polity in endOfYear[offset].Keys)
            {
                if (successionYears.Contains((polity, year)))
                {
                    continue;
                }

                if (MajorLoss(endOfYear, startYear, polity, year) is not { } outcome)
                {
                    continue;
                }

                ordinary = ordinary.Observe(outcome);

                if ((RegionsAtStartOfYear(endOfYear, startYear, polity, year) ?? 0) >= LargePolityRegions)
                {
                    ordinaryLarge = ordinaryLarge.Observe(outcome);
                }
            }
        }

        // Which rulers and which polities the conquests belonged to. Attributed to the start of the
        // year, so the ability and the size are the ones that fed the overextension term that made
        // the expansion possible - not whatever they became after it succeeded.
        var expansionsByBand = new long[Bands];
        var expansionsByMilitaryBand = new long[Bands];
        long expansionsAttributed = 0;
        long expansionsByLarge = 0;

        foreach (RegionControlChangedEvent conquest in sim.Chronicle.Events
            .OfType<RegionControlChangedEvent>()
            .Where(e => e.Reason == OpportunisticExpansionSystem.ExpansionReason))
        {
            int index = conquest.Year - startYear - 1;
            if (index < 0 || index >= endOfYear.Count
                || !endOfYear[index].TryGetValue(conquest.To, out PolityYear before))
            {
                continue;
            }

            expansionsByBand[BandOf(before.Admin)]++;
            expansionsByMilitaryBand[BandOf(before.Mil)]++;
            expansionsAttributed++;

            if (before.Regions >= LargePolityRegions)
            {
                expansionsByLarge++;
            }
        }

        return new RulerAnalysis(
            sim.Chronicle.Events.OfType<RulerDeathEvent>().Count(),
            sim.Chronicle.Events.OfType<ReignEndedEvent>()
                .Count(e => e.EndReason == ReignEndReason.PolityExtinct),
            sim.Chronicle.Events.OfType<ReignEndedEvent>()
                .Count(e => e.EndReason == ReignEndReason.Displaced),
            successions,
            reignYears,
            completed,
            completed == 0 ? 0 : minReign,
            maxReign,
            zeroYear,
            histogram,
            bandPolityYears,
            bandRegionYears,
            immediate,
            lagged,
            afterSuccession,
            ordinary,
            stronger,
            weaker,
            bandLosses,
            afterSuccessionLarge,
            ordinaryLarge,
            bandReignDelta,
            bandFirst25,
            mechanism.All,
            mechanism.Large,
            largeDrop,
            expansionsByBand,
            expansionsAttributed,
            expansionsByLarge,
            militaryHistogram,
            expansionsByMilitaryBand,
            quadrantDelta,
            bothHigh,
            bothLow,
            at25,
            at20,
            mechanism.Distance.Snapshot(),
            log20,
            remoteAfterSuccession,
            remoteOrdinary,
            remoteAfterStrong);
    }
}
