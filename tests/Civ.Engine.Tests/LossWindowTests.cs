using Civ.Batch;

namespace Civ.Engine.Tests;

/// <summary>
/// The major-loss rule, including where its window starts and ends.
/// </summary>
/// <remarks>
/// <para>This metric has been wrong in five separate ways, each biasing the result toward "nothing
/// happened": it compared only the endpoint of the window, it required a strictly greater loss than
/// a quarter, it silently truncated windows running past the end of the run, and - worst - it took
/// its baseline from the end of the year being examined, so a collapse inside that year had already
/// happened before the "before" reading was taken.</para>
///
/// <para>The convention now: <c>timeline[k]</c> is the end of year <c>startYear + k</c>, the baseline
/// for year Y is the end of year Y-1, and the outcomes are the end-of-year figures for Y through
/// Y+24.</para>
/// </remarks>
public sealed class LossWindowTests
{
    private static readonly PolityId Realm = new(1, 1);

    /// <summary>Builds end-of-year records from region counts. Zero means the polity is gone.</summary>
    private static List<Dictionary<PolityId, RulerAnalysis.PolityYear>> Timeline(params int[] regions)
    {
        var years = new List<Dictionary<PolityId, RulerAnalysis.PolityYear>>(regions.Length);
        foreach (int count in regions)
        {
            var year = new Dictionary<PolityId, RulerAnalysis.PolityYear>();
            if (count > 0)
            {
                year[Realm] = new RulerAnalysis.PolityYear(count, 50);
            }

            years.Add(year);
        }

        return years;
    }

    private static List<Dictionary<PolityId, RulerAnalysis.PolityYear>> Flat(int regions, int length) =>
        Timeline([.. Enumerable.Repeat(regions, length)]);

    private static void SetYear(
        List<Dictionary<PolityId, RulerAnalysis.PolityYear>> timeline, int index, int regions) =>
        timeline[index] = regions > 0
            ? new Dictionary<PolityId, RulerAnalysis.PolityYear> { [Realm] = new(regions, 50) }
            : [];

    /// <summary>
    /// The baseline is the year before, not the year being examined.
    /// </summary>
    /// <remarks>
    /// The bug that made same-year collapses invisible: a realm that ends the year at a fraction of
    /// its former size compared equal to itself, because both readings came from after the fall.
    /// </remarks>
    [Fact]
    public void TheBaselineIsTheStateAtTheStartOfTheYear()
    {
        var timeline = Flat(20, 60);
        for (int i = 1; i < timeline.Count; i++)
        {
            SetYear(timeline, i, 4);
        }

        // Year 1 opens with 20 regions and ends with 4. Measured from the start of the year, that is
        // a catastrophe; measured from the end of it, nothing happened at all.
        Assert.Equal(20, RulerAnalysis.RegionsAtStartOfYear(timeline, startYear: 0, Realm, year: 1));
        Assert.True(RulerAnalysis.MajorLoss(timeline, startYear: 0, Realm, year: 1));
    }

    [Fact]
    public void LosingExactlyAQuarterCounts()
    {
        var timeline = Flat(20, 60);
        for (int i = 10; i < timeline.Count; i++)
        {
            SetYear(timeline, i, 15);
        }

        Assert.True(RulerAnalysis.MajorLoss(timeline, startYear: 0, Realm, year: 1));
    }

    [Fact]
    public void LosingSlightlyLessThanAQuarterDoesNot()
    {
        var timeline = Flat(20, 60);
        for (int i = 10; i < timeline.Count; i++)
        {
            SetYear(timeline, i, 16);
        }

        Assert.False(RulerAnalysis.MajorLoss(timeline, startYear: 0, Realm, year: 1));
    }

    /// <summary>
    /// A collapse reversed inside the window still happened.
    /// </summary>
    /// <remarks>
    /// The case an endpoint-only comparison missed entirely: a state that lost half its land in year
    /// 5 and had reconquered it by year 25 was scored as having had a quiet quarter century.
    /// </remarks>
    [Fact]
    public void ALossThatIsRecoveredWithinTheWindowStillCounts()
    {
        var timeline = Flat(20, 60);
        SetYear(timeline, 5, 10);

        Assert.True(RulerAnalysis.MajorLoss(timeline, startYear: 0, Realm, year: 1));
    }

    [Fact]
    public void TheWindowIsExactlyTwentyFiveYearsInclusive()
    {
        // Year 1's window covers end-of-year figures for years 1 through 25.
        var onTheEdge = Flat(20, 80);
        SetYear(onTheEdge, 25, 4);
        Assert.True(RulerAnalysis.MajorLoss(onTheEdge, startYear: 0, Realm, year: 1));

        var justOutside = Flat(20, 80);
        SetYear(justOutside, 26, 4);
        Assert.False(RulerAnalysis.MajorLoss(justOutside, startYear: 0, Realm, year: 1));
    }

    [Fact]
    public void ALossInTheExaminedYearItselfCounts()
    {
        var timeline = Flat(20, 60);
        SetYear(timeline, 1, 5);
        for (int i = 2; i < timeline.Count; i++)
        {
            SetYear(timeline, i, 20);
        }

        Assert.True(RulerAnalysis.MajorLoss(timeline, startYear: 0, Realm, year: 1));
    }

    [Fact]
    public void ExtinctionIsTotalLoss()
    {
        var timeline = Flat(20, 60);
        for (int i = 12; i < timeline.Count; i++)
        {
            SetYear(timeline, i, 0);
        }

        Assert.True(RulerAnalysis.MajorLoss(timeline, startYear: 0, Realm, year: 1));
    }

    /// <summary>
    /// Windows without a full 25 years of data are excluded rather than measured short.
    /// </summary>
    /// <remarks>
    /// Truncating them made the tail of every run look uneventful, which dragged the control rate
    /// down and therefore made successions look comparatively safer than they were.
    /// </remarks>
    [Fact]
    public void IncompleteWindowsAreExcluded()
    {
        // Indices 0..29. Year Y needs a baseline at Y-1 and outcomes through Y+24.
        var timeline = Flat(20, 30);

        Assert.NotNull(RulerAnalysis.MajorLoss(timeline, startYear: 0, Realm, year: 1));
        Assert.NotNull(RulerAnalysis.MajorLoss(timeline, startYear: 0, Realm, year: 5));

        Assert.Null(RulerAnalysis.MajorLoss(timeline, startYear: 0, Realm, year: 6));
        Assert.Null(RulerAnalysis.MajorLoss(timeline, startYear: 0, Realm, year: 29));
    }

    [Fact]
    public void AYearWithNoBaselineIsNotAWindow()
    {
        var timeline = Flat(20, 60);

        // Year 0 has no preceding year to measure against.
        Assert.Null(RulerAnalysis.MajorLoss(timeline, startYear: 0, Realm, year: 0));

        // Nor does a polity that did not exist at the start of the year.
        SetYear(timeline, 0, 0);
        Assert.Null(RulerAnalysis.MajorLoss(timeline, startYear: 0, Realm, year: 1));
    }

    [Fact]
    public void TheStartYearOffsetIsHonoured()
    {
        var timeline = Flat(20, 60);
        SetYear(timeline, 10, 1);

        // With the run starting at year 500, index 10 is the end of year 510.
        Assert.True(RulerAnalysis.MajorLoss(timeline, startYear: 500, Realm, year: 501));
        Assert.False(RulerAnalysis.MajorLoss(timeline, startYear: 500, Realm, year: 512));
    }
}
