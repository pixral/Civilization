using Civ.Engine.State;
using Civ.Engine.Systems;
using Civ.Systems;

namespace Civ.Batch;

/// <summary>Accumulator the observer writes into. Lives outside the simulation.</summary>
internal sealed class MechanismSink
{
    public MechanismCounts All { get; set; }

    public MechanismCounts Large { get; set; }

    /// <summary>The distance-scaling counterfactual, accumulated in place.</summary>
    public DistanceMechanism.Accumulator Distance { get; } = new();

    /// <summary>
    /// How much of each polity was remote, year by year, read in phase.
    /// </summary>
    /// <remarks>
    /// Distance from the capital is not in the end-of-year sample and cannot be reconstructed from
    /// it, because it depends on the shape of the territory at the time. Recording it here is what
    /// lets the analysis ask whether territory lost after a succession was the remote periphery the
    /// reach benefit acts on, or the core it does not touch.
    /// </remarks>
    public Dictionary<(int Year, PolityId Polity), TerritoryShape> Shape { get; } = [];
}

/// <summary>A polity's territory split into the part administrative reach can affect and the rest.</summary>
/// <remarks>
/// "Remote" is four or more steps from the capital through the polity's own land - the distance
/// bands where the previous experiment's counterfactual found essentially all of the effect. The
/// capital, its immediate hinterland and every disconnected exclave count as core, the exclaves
/// because no administrator reaches them at any ability.
/// </remarks>
internal readonly record struct TerritoryShape(int Regions, int Remote)
{
    internal const int RemoteDistance = 4;

    public int Core => Regions - Remote;
}

/// <summary>
/// Measures whether ruler ability changed the cohesion decision, from the state cohesion itself sees.
/// </summary>
/// <remarks>
/// <para><b>Why it has to be a system.</b> The diagnostic was previously computed from the
/// end-of-year map, after secession and expansion had already moved the borders it was trying to
/// explain. Running in <see cref="SimulationPhase.Polity"/> alongside
/// <see cref="CohesionSecessionSystem"/> means it reads exactly the start-of-phase snapshot that
/// cohesion reads, because systems within a phase cannot see each other's effects.</para>
///
/// <para><b>Why it cannot perturb the run.</b> It emits no effects and draws no randomness. Random
/// streams are derived from system names, so adding it cannot shift any other system's rolls -
/// that is the property <c>DeterminismTests.AddingSystemsDoesNotChangeExistingSystemsOutcomes</c>
/// already asserts, and <c>MechanismObserverDoesNotChangeTheSimulation</c> asserts it for this
/// system specifically.</para>
///
/// <para><b>It is deliberately stateful</b>, which every other system is forbidden from being. That
/// is safe only because the state it accumulates is write-only from the simulation's point of view:
/// nothing ever reads it back, so it cannot influence anything. A measurement instrument is the one
/// legitimate exception to that rule, and it lives in the batch runner rather than in
/// <c>Civ.Systems</c> so it can never be mistaken for content.</para>
/// </remarks>
internal sealed class MechanismObserverSystem(CohesionRules rules, MechanismSink sink) : ISimulationSystem
{
    public string Name => "batch.mechanism_observer";

    public SimulationPhase Phase => SimulationPhase.Polity;

    public void Execute(in SystemContext context)
    {
        MechanismCounts all = sink.All;
        MechanismCounts large = sink.Large;

        foreach (Polity polity in WorldQueries.ActivePolities(context.World))
        {
            int admin = CohesionSecessionSystem.AdministrationOf(context.World, polity, rules);
            int actual = CohesionSecessionSystem.Authority(polity, rules, admin);
            int baseline = CohesionSecessionSystem.Authority(
                polity, rules, rules.DefaultAdministration);

            // Capacity diagnostic: same strains, different authority.
            var strains = CohesionSecessionSystem.StrainMap(context.World, polity, rules, admin);

            int restiveActual = 0;
            int restiveBaseline = 0;

            foreach (CohesionSecessionSystem.RegionStrain region in strains.Values)
            {
                if (region.Strain > actual)
                {
                    restiveActual++;
                }

                if (region.Strain > baseline)
                {
                    restiveBaseline++;
                }
            }

            ObserveDistanceScaling(context.World, polity, admin, actual, strains);

            int exposed = Math.Max(0, restiveActual - restiveBaseline);
            int held = Math.Max(0, restiveBaseline - restiveActual);

            var counts = new MechanismCounts(
                1,
                restiveActual != restiveBaseline ? 1 : 0,
                exposed > 0 ? 1 : 0,
                held > 0 ? 1 : 0,
                exposed,
                held);

            all += counts;

            if (WorldQueries.RegionCountOf(context.World, polity.Id) >= RulerAnalysis.LargePolityRegions)
            {
                large += counts;
            }
        }

        sink.All = all;
        sink.Large = large;
    }

    /// <summary>
    /// The distance-scaling counterfactual: identical authority, neutral distance multiplier.
    /// </summary>
    /// <remarks>
    /// <para>Every other term is untouched, so a region that changes classification here changed
    /// because of connected distance alone. Read from the same in-phase state cohesion is reading,
    /// before any secession has moved the borders being explained.</para>
    ///
    /// <para>The neutral comparison uses <see cref="CohesionRules.DefaultAdministration"/>, which the
    /// rules pin to the ability at which every conversion returns 100%. Under a benefit-only
    /// conversion no region can therefore be pushed <i>into</i> restiveness, and
    /// <c>RegionsExposed</c> is expected to stay at zero - it is counted anyway, because an
    /// assumption that is measured is worth more than one that is asserted.</para>
    /// </remarks>
    private void ObserveDistanceScaling(
        WorldState world,
        Polity polity,
        int administration,
        int authority,
        Dictionary<RegionId, CohesionSecessionSystem.RegionStrain> actualStrains)
    {
        var neutralStrains = CohesionSecessionSystem.StrainMap(
            world, polity, rules, rules.DefaultAdministration);

        int regions = WorldQueries.RegionCountOf(world, polity.Id);
        int sizeBand = DistanceMechanism.SizeBandOf(regions);
        int adminBand = RulerAnalysis.BandOf(administration);

        DistanceMechanism.Accumulator counts = sink.Distance;
        counts.PolityYears++;
        counts.PolityYearsBySizeBand[sizeBand]++;
        counts.PolityYearsByAdminBand[adminBand]++;

        bool changed = false;
        int remote = 0;

        foreach ((RegionId id, CohesionSecessionSystem.RegionStrain actual) in actualStrains)
        {
            // Composition of the strain the rule actually computed. Distance competes with size,
            // prosperity and disconnection for the same authority budget, and a benefit-only cut to
            // one term is only worth what that term is worth.
            counts.TotalStrain += actual.Strain;
            counts.SizeStrain += (long)rules.SizeStrainPerRegion * (regions - 1);

            if (actual.Connected)
            {
                // Realized multiplier, weighted by the strain it applies to rather than by ruler.
                counts.ModifiedDistanceStrain += rules.DistanceStrainTerm(actual.Distance, administration);
                counts.NeutralDistanceStrain +=
                    (long)rules.DistanceStrainPerStep * actual.Distance;

                if (actual.Distance >= TerritoryShape.RemoteDistance)
                {
                    remote++;
                }
            }

            if (!neutralStrains.TryGetValue(id, out CohesionSecessionSystem.RegionStrain neutral))
            {
                continue;
            }

            bool restiveActual = actual.Strain > authority;
            bool restiveNeutral = neutral.Strain > authority;

            if (restiveActual == restiveNeutral)
            {
                continue;
            }

            changed = true;

            if (restiveActual)
            {
                counts.RegionsExposed++;
            }
            else
            {
                counts.RegionsRetained++;
            }

            counts.RegionsBySizeBand[sizeBand]++;
            counts.RegionsByAdminBand[adminBand]++;
            counts.RegionsByDistanceBand[
                DistanceMechanism.DistanceBandOf(actual.Connected ? actual.Distance : 0)]++;
        }

        sink.Shape[(world.Year, polity.Id)] = new TerritoryShape(regions, remote);

        if (changed)
        {
            counts.Changed++;
            counts.ChangedBySizeBand[sizeBand]++;
            counts.ChangedByAdminBand[adminBand]++;
        }
    }
}
