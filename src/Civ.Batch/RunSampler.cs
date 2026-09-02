using Civ.Engine;
using Civ.Engine.State;

namespace Civ.Batch;

/// <summary>What one sampled run produced.</summary>
internal sealed record SampledRun(
    Simulation Simulation,
    List<Dictionary<PolityId, RulerAnalysis.PolityYear>> EndOfYear,
    int[] PolitiesByYear,
    int[] LargestShareByYear,
    int PeakSharePercent,
    int StartYear);

/// <summary>
/// Advances a simulation and records the end-of-year state of every polity.
/// </summary>
/// <remarks>
/// <para><b>Index convention, which the whole analysis depends on.</b> <c>EndOfYear[k]</c> is the
/// state at the end of simulated year <c>startYear + k</c>. Index 0 is the world as generated, before
/// any tick has run, so it doubles as the start-of-year state for the first simulated year. It
/// follows that <b>the state at the start of year Y is <c>EndOfYear[Y - startYear - 1]</c></b>.</para>
///
/// <para>That distinction is not pedantic. Territory held "at accession" has to be measured before
/// the year in which the accession happened, because a weak successor can lose provinces later in
/// that same year - and measuring afterwards silently defines those losses out of existence.</para>
///
/// <para>Shared with the tests rather than duplicated, so the assertions about timing are made
/// against the same sampling the reports use.</para>
/// </remarks>
internal static class RunSampler
{
    public static SampledRun Sample(Simulation sim, int years)
    {
        // Taken from the simulation, not from the config. A world handed to Simulation.Resume can
        // sit at any year, and deriving the index origin from configuration instead silently shifts
        // every window by one.
        int startYear = sim.Year;

        var endOfYear = new List<Dictionary<PolityId, RulerAnalysis.PolityYear>>(years + 1);
        var politiesByYear = new int[years + 1];
        var largestShareByYear = new int[years + 1];
        int peakShare = 0;

        for (int i = 0; i <= years; i++)
        {
            if (i > 0)
            {
                sim.AdvanceYear();
            }

            var snapshot = new Dictionary<PolityId, RulerAnalysis.PolityYear>();
            foreach (Polity polity in WorldQueries.ActivePolities(sim.World))
            {
                int admin = sim.World.Rulers.TryGet(polity.CurrentRuler, out Ruler? ruler)
                    ? ruler.Administration
                    : 50;

                int military = sim.World.Rulers.TryGet(polity.CurrentRuler, out Ruler? commander)
                    ? commander.Military
                    : 50;

                snapshot[polity.Id] = new RulerAnalysis.PolityYear(
                    WorldQueries.RegionCountOf(sim.World, polity.Id), admin, military);
            }

            endOfYear.Add(snapshot);
            politiesByYear[i] = snapshot.Count;
            largestShareByYear[i] = LargestSharePercent(sim);
            peakShare = Math.Max(peakShare, largestShareByYear[i]);
        }

        return new SampledRun(
            sim, endOfYear, politiesByYear, largestShareByYear, peakShare, startYear);
    }

    internal static int LargestSharePercent(Simulation sim)
    {
        int total = sim.World.Regions.Count;
        if (total == 0)
        {
            return 0;
        }

        int largest = 0;
        foreach (Polity polity in WorldQueries.ActivePolities(sim.World))
        {
            largest = Math.Max(largest, WorldQueries.RegionCountOf(sim.World, polity.Id));
        }

        return largest * 100 / total;
    }
}
