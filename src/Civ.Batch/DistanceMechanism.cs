namespace Civ.Batch;

/// <summary>
/// Whether administrative reach changed the cohesion decision, and where.
/// </summary>
/// <remarks>
/// <para>The counterfactual holds <b>everything</b> else identical - same ruler-modified capacity,
/// same size, prosperity and disconnection terms, same stability relief - and swaps only the
/// connected-distance multiplier for the neutral 100%. Anything that differs is therefore the new
/// modifier and nothing else.</para>
///
/// <para>Broken down by polity size, by distance from the capital, and by the ruler's own
/// administrative band, because the whole claim of this experiment is that the effect is
/// concentrated in large, geographically stretched states under exceptional administrators. An
/// effect spread evenly across small polities and ordinary rulers would be a general secession
/// suppressor wearing a distance costume - which is exactly what the matched control in arm C
/// is built to look like.</para>
///
/// <para><see cref="RegionsExposed"/> must be <b>zero</b> for a benefit-only conversion. It is kept
/// as a measured quantity rather than assumed away because it is the property that distinguishes
/// this experiment from the symmetric one that failed, and a test asserts it stays zero over a full
/// multi-seed sweep.</para>
/// </remarks>
internal sealed record DistanceMechanism(
    long PolityYears,
    long Changed,
    long RegionsExposed,
    long RegionsRetained,
    long[] ChangedBySizeBand,
    long[] RegionsBySizeBand,
    long[] RegionsByDistanceBand,
    long[] PolityYearsBySizeBand,
    long[] RegionsByAdminBand,
    long[] PolityYearsByAdminBand,
    long[] ChangedByAdminBand,
    long ModifiedDistanceStrain,
    long NeutralDistanceStrain,
    long TotalStrain,
    long SizeStrain)
{
    /// <summary>Polity sizes: under 10 regions, 10-19, 20-29, 30 or more.</summary>
    internal static readonly string[] SizeBandNames = ["<10", "10-19", "20-29", "30+"];

    /// <summary>Distance from the capital, through the polity's own territory.</summary>
    internal static readonly string[] DistanceBandNames = ["0-1", "2-3", "4-5", "6+"];

    internal const int SizeBands = 4;

    internal const int DistanceBands = 4;

    /// <summary>Administrative ability bands, shared with the rest of the ruler analysis.</summary>
    internal const int AdminBands = RulerAnalysis.Bands;

    public static DistanceMechanism Empty => new(
        0, 0, 0, 0,
        new long[SizeBands], new long[SizeBands], new long[DistanceBands], new long[SizeBands],
        new long[AdminBands], new long[AdminBands], new long[AdminBands], 0, 0, 0, 0);

    internal static int SizeBandOf(int regions) => regions switch
    {
        < 10 => 0,
        < 20 => 1,
        < 30 => 2,
        _ => 3,
    };

    internal static int DistanceBandOf(int distance) => distance switch
    {
        <= 1 => 0,
        <= 3 => 1,
        <= 5 => 2,
        _ => 3,
    };

    public double ChangedRate => PolityYears == 0 ? 0 : 100.0 * Changed / PolityYears;

    public double ChangedRateInSizeBand(int band) =>
        PolityYearsBySizeBand[band] == 0
            ? 0
            : 100.0 * ChangedBySizeBand[band] / PolityYearsBySizeBand[band];

    public double ChangedRateInAdminBand(int band) =>
        PolityYearsByAdminBand[band] == 0
            ? 0
            : 100.0 * ChangedByAdminBand[band] / PolityYearsByAdminBand[band];

    /// <summary>
    /// The multiplier the world actually ran at, weighted by the strain it applied to.
    /// </summary>
    /// <remarks>
    /// The expected multiplier from <see cref="AbilityDistribution"/> weights every ruler equally.
    /// This weights every point of connected-distance strain, so it folds in reign length, polity
    /// size and how stretched the states under good administrators happened to be. Where the two
    /// disagree, the control was matched to the wrong number and the report should say so.
    /// </remarks>
    public double RealizedMultiplierPercent =>
        NeutralDistanceStrain == 0 ? 100 : 100.0 * ModifiedDistanceStrain / NeutralDistanceStrain;

    /// <summary>
    /// What share of all strain the connected-distance term is, and what the benefit removed.
    /// </summary>
    /// <remarks>
    /// The question a null result has to answer is not "did the modifier fire" - the counterfactual
    /// already says it did - but "was there enough of it to matter". Distance is one of four terms
    /// competing for the same authority budget, and a large cut to a small term is a small cut.
    /// Measured against the strain the rule actually computed, so no arithmetic is reconstructed.
    /// </remarks>
    public double DistanceSharePercent =>
        TotalStrain == 0 ? 0 : 100.0 * NeutralDistanceStrain / TotalStrain;

    public double SizeSharePercent => TotalStrain == 0 ? 0 : 100.0 * SizeStrain / TotalStrain;

    /// <summary>The benefit expressed as a share of total strain, not of the distance term alone.</summary>
    public double StrainRemovedPercent =>
        TotalStrain == 0 ? 0 : 100.0 * (NeutralDistanceStrain - ModifiedDistanceStrain) / TotalStrain;

    public static DistanceMechanism operator +(DistanceMechanism a, DistanceMechanism b) => new(
        a.PolityYears + b.PolityYears,
        a.Changed + b.Changed,
        a.RegionsExposed + b.RegionsExposed,
        a.RegionsRetained + b.RegionsRetained,
        [.. a.ChangedBySizeBand.Zip(b.ChangedBySizeBand, (x, y) => x + y)],
        [.. a.RegionsBySizeBand.Zip(b.RegionsBySizeBand, (x, y) => x + y)],
        [.. a.RegionsByDistanceBand.Zip(b.RegionsByDistanceBand, (x, y) => x + y)],
        [.. a.PolityYearsBySizeBand.Zip(b.PolityYearsBySizeBand, (x, y) => x + y)],
        [.. a.RegionsByAdminBand.Zip(b.RegionsByAdminBand, (x, y) => x + y)],
        [.. a.PolityYearsByAdminBand.Zip(b.PolityYearsByAdminBand, (x, y) => x + y)],
        [.. a.ChangedByAdminBand.Zip(b.ChangedByAdminBand, (x, y) => x + y)],
        a.ModifiedDistanceStrain + b.ModifiedDistanceStrain,
        a.NeutralDistanceStrain + b.NeutralDistanceStrain,
        a.TotalStrain + b.TotalStrain,
        a.SizeStrain + b.SizeStrain);

    /// <summary>Mutable accumulator, so the observer does not allocate a record per polity-year.</summary>
    internal sealed class Accumulator
    {
        public long PolityYears;
        public long Changed;
        public long RegionsExposed;
        public long RegionsRetained;
        public long ModifiedDistanceStrain;
        public long NeutralDistanceStrain;
        public long TotalStrain;
        public long SizeStrain;
        public readonly long[] ChangedBySizeBand = new long[SizeBands];
        public readonly long[] RegionsBySizeBand = new long[SizeBands];
        public readonly long[] RegionsByDistanceBand = new long[DistanceBands];
        public readonly long[] PolityYearsBySizeBand = new long[SizeBands];
        public readonly long[] RegionsByAdminBand = new long[AdminBands];
        public readonly long[] PolityYearsByAdminBand = new long[AdminBands];
        public readonly long[] ChangedByAdminBand = new long[AdminBands];

        public DistanceMechanism Snapshot() => new(
            PolityYears,
            Changed,
            RegionsExposed,
            RegionsRetained,
            [.. ChangedBySizeBand],
            [.. RegionsBySizeBand],
            [.. RegionsByDistanceBand],
            [.. PolityYearsBySizeBand],
            [.. RegionsByAdminBand],
            [.. PolityYearsByAdminBand],
            [.. ChangedByAdminBand],
            ModifiedDistanceStrain,
            NeutralDistanceStrain,
            TotalStrain,
            SizeStrain);
    }
}
