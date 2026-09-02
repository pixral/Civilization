using Civ.Batch;
using Civ.Engine.Config;
using Civ.Engine.Effects;
using Civ.Engine.Events;
using Civ.Engine.State;
using Civ.Engine.Systems;
using Civ.Systems;

namespace Civ.Engine.Tests;

/// <summary>
/// When a measurement is taken, not just what it measures.
/// </summary>
/// <remarks>
/// Every statistic about rulers is a claim about a window of years, and the boundaries of those
/// windows decide the answer. Reading territory "at accession" after the accession year has been
/// simulated defines a weak successor's first-year losses out of existence; attributing the accession
/// year to the wrong ruler moves a whole year of change onto the wrong reign. Both were wrong, and
/// neither is visible in a run that simply completes.
/// </remarks>
public sealed class ReignTimingTests
{
    private static SimulationConfig Config => SimulationConfig.Default with
    {
        Seed = 20250,
        InvariantMode = InvariantMode.EveryTick,
        ThrowOnInvariantViolation = true,
    };

    /// <summary>
    /// Strain and capacity scaled so an eleven-province realm is comfortably held by a strong
    /// administrator and overwhelmingly untenable for a weak one.
    /// </summary>
    /// <remarks>
    /// The wide capacity band is what makes the outcome certain in a known year rather than merely
    /// likely, which is what a timing test needs.
    /// </remarks>
    private static CohesionRules Decisive => CohesionRules.Default with
    {
        AdministrativeCapacity = 1_700,
        DistanceStrainPerStep = 250,
        SizeStrainPerRegion = 30,
        ProsperityStrain = 0,
        // Exaggerated, but centred on 100% so an average ruler is still neutral - the property
        // CohesionRules.Validate enforces, after a band that quietly shifted it produced the most
        // convincing wrong result in the project.
        RulerCapacityFloorPercent = 20,
        RulerCapacityCeilingPercent = 180,
        StrainPerPermille = 1,
        MaxAttemptPermille = 1000,
        SecessionShock = 0,
    };

    private static (WorldBuilder Builder, RegionId[] Line, PolityId Realm) LongRealm(int administration)
    {
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(Enumerable.Repeat(10_000L, 11).ToArray());
        PolityId realm = builder.Polity("Long Realm", 50, administration, line);
        return (builder, line, realm);
    }

    /// <summary>Replaces the reigning ruler of a polity in one specific year.</summary>
    private static ScriptedSystem Succession(
        PolityId polity, int year, int administration, string name = "test.timed_succession") =>
        new(name, SimulationPhase.Rulership, (in SystemContext ctx) =>
        {
            if (ctx.Year != year)
            {
                return;
            }

            ctx.Effects.Emit(new EndReign(ctx.World.Polities.Get(polity).CurrentRuler, "test"));
            ctx.Effects.Emit(new InstallRuler(
                polity,
                new RulerProfile("The Unready", ctx.Year - 30, administration),
                RulerSuccessionSystem.SuccessionReason));
        });

    /// <summary>
    /// A weak successor loses a quarter of the realm later in the very year they acceded.
    /// </summary>
    /// <remarks>
    /// The case the old metric could never see. Its baseline was the end-of-year figure for the
    /// accession year, by which point the provinces were already gone, so <c>before == after</c> and
    /// the most dramatic possible succession scored as uneventful.
    /// </remarks>
    [Fact]
    public void ASameYearCollapseIsMeasuredAgainstTerritoryHeldBeforeTheAccession()
    {
        const int SuccessionYear = 6;

        (WorldBuilder builder, _, PolityId realm) = LongRealm(administration: 100);

        Simulation sim = Simulation.Resume(
            Config,
            builder.World,
            [
                Succession(realm, SuccessionYear, administration: 0),
                new CohesionSecessionSystem(Decisive),
                new PolityLifecycleSystem(),
            ]);

        SampledRun run = RunSampler.Sample(sim, years: 60);

        // The accession and the secession fall in the same year.
        // The realm's own succession. The breakaway state seats a ruler of its own in the same year,
        // and may itself fragment later, so both selectors are scoped to the realm.
        RulerAccessionEvent accession = sim.Chronicle.Events
            .OfType<RulerAccessionEvent>()
            .Single(e => e.Polity == realm && e.Reason == RulerSuccessionSystem.SuccessionReason);
        Assert.Equal(SuccessionYear, accession.Year);

        PolityFoundedEvent breakaway = sim.Chronicle.Events
            .OfType<PolityFoundedEvent>()
            .First(e => e.Parent == realm && e.Reason == CohesionSecessionSystem.SecessionReason);
        Assert.Equal(SuccessionYear, breakaway.Year);

        // Baseline is the state before the accession year ran: the realm still whole.
        int? before = RulerAnalysis.RegionsAtStartOfYear(
            run.EndOfYear, run.StartYear, realm, SuccessionYear);
        Assert.Equal(11, before);

        int after = run.EndOfYear[SuccessionYear - run.StartYear][realm].Regions;
        Assert.True(after * 4 <= 11 * 3, $"expected at least a quarter lost, held {after} of 11");

        Assert.True(RulerAnalysis.MajorLoss(run.EndOfYear, run.StartYear, realm, SuccessionYear));
        Assert.Empty(sim.Violations);
    }

    /// <summary>
    /// The accession year belongs to the successor, and to them alone.
    /// </summary>
    /// <remarks>
    /// Asserted from both sides and then as a partition: the two deltas must sum to the total change
    /// across the combined span, which is only true if the boundary year is counted exactly once.
    /// </remarks>
    [Fact]
    public void TheAccessionYearIsAttributedToTheSuccessorAndNotThePredecessor()
    {
        const int FirstSuccession = 4;
        const int SecondSuccession = 12;
        const int ThirdSuccession = 20;

        (WorldBuilder builder, _, PolityId realm) = LongRealm(administration: 100);

        // Two successions, so the ruler under test acceded after sampling began and therefore has a
        // measurable window on both sides. The founding ruler cannot: there is no record of the year
        // before the run started, and a delta invented for them would be a delta for nothing.
        Simulation sim = Simulation.Resume(
            Config,
            builder.World,
            [
                Succession(realm, FirstSuccession, administration: 100, "test.succession_one"),
                Succession(realm, SecondSuccession, administration: 0, "test.succession_two"),

                // Closes the window on the reign under test. A reign still in progress has no end
                // boundary, so no delta can be attributed to it.
                Succession(realm, ThirdSuccession, administration: 100, "test.succession_three"),
                new CohesionSecessionSystem(Decisive),
                new PolityLifecycleSystem(),
            ]);

        SampledRun run = RunSampler.Sample(sim, years: 60);

        Ruler predecessor = sim.World.Rulers.All()
            .Single(r => r.Polity.Equals(realm) && r.AccessionYear == FirstSuccession);
        Ruler successor = sim.World.Rulers.All()
            .Single(r => r.Polity.Equals(realm) && r.AccessionYear == SecondSuccession);

        Assert.Equal(SecondSuccession, predecessor.ReignEndYear);
        Assert.Equal(ThirdSuccession, successor.ReignEndYear);
        Assert.False(predecessor.IsReigning);

        int? predecessorDelta = RulerAnalysis.ReignDelta(run.EndOfYear, run.StartYear, predecessor);
        int? successorDelta = RulerAnalysis.ReignDelta(run.EndOfYear, run.StartYear, successor);

        // The strong predecessor held everything through the year before the handover. The collapse
        // happened in the accession year, so it belongs to the successor.
        Assert.Equal(0, predecessorDelta);
        Assert.NotNull(successorDelta);
        Assert.True(
            successorDelta < 0,
            $"the successor should own the territory lost in their first year, saw {successorDelta}");

        // The predecessor's window closes on exactly the index the successor's opens on - the shared
        // boundary is read once, so no year is double-counted and none is dropped.
        int boundary = RulerAnalysis.RegionsAtStartOfYear(
            run.EndOfYear, run.StartYear, realm, SecondSuccession)!.Value;
        Assert.Equal(11, boundary);

        int spanStart = RulerAnalysis.RegionsAtStartOfYear(
            run.EndOfYear, run.StartYear, realm, predecessor.AccessionYear)!.Value;

        int spanEnd = RulerAnalysis.RegionsAtStartOfYear(
            run.EndOfYear, run.StartYear, realm, ThirdSuccession)!.Value;
        Assert.Equal(spanEnd - spanStart, predecessorDelta + successorDelta);

        Assert.Empty(sim.Violations);
    }

    /// <summary>
    /// Consecutive reigns tile the whole timeline with no gap and no overlap.
    /// </summary>
    /// <remarks>
    /// The generated-world version of the boundary check: over a long run with many successions, the
    /// per-reign deltas of one polity must sum to its total territorial change across the same span.
    /// </remarks>
    [Fact]
    public void ReignDeltasSumToTheTotalChangeOverTheSameSpan()
    {
        Simulation sim = Simulation.Create(
            SimulationConfig.Default with
            {
                Seed = 4242,
                WorldWidth = 12,
                WorldHeight = 8,
                InitialPolityCount = 6,
                InvariantMode = InvariantMode.Periodic,
            },
            DefaultSystems.Build());

        SampledRun run = RunSampler.Sample(sim, years: 600);
        int startYear = run.StartYear;

        int checkedPolities = 0;

        foreach (Polity polity in sim.World.Polities.All())
        {
            var reigns = sim.World.Rulers.All()
                .Where(r => r.Polity.Equals(polity.Id) && r.ReignEndYear is not null)
                .OrderBy(r => r.AccessionYear)
                .ToList();

            if (reigns.Count < 3)
            {
                continue;
            }

            // Only reigns whose whole window is inside the sampled span.
            var usable = reigns
                .Where(r => RulerAnalysis.ReignDelta(run.EndOfYear, startYear, r) is not null)
                .ToList();

            if (usable.Count < 3)
            {
                continue;
            }

            // Consecutive: each reign begins the year the previous one ended.
            for (int i = 1; i < usable.Count; i++)
            {
                if (usable[i].AccessionYear != usable[i - 1].ReignEndYear)
                {
                    return;
                }
            }

            int sum = usable.Sum(r => RulerAnalysis.ReignDelta(run.EndOfYear, startYear, r)!.Value);

            int from = RulerAnalysis.RegionsAtStartOfYear(
                run.EndOfYear, startYear, polity.Id, usable[0].AccessionYear)!.Value;
            int to = RulerAnalysis.RegionsAtStartOfYear(
                run.EndOfYear, startYear, polity.Id, usable[^1].ReignEndYear!.Value)!.Value;

            Assert.Equal(to - from, sum);
            checkedPolities++;
        }

        Assert.True(checkedPolities > 0, "expected at least one polity with a chain of complete reigns");
    }

    /// <summary>
    /// The mechanism observer is an instrument, not a participant.
    /// </summary>
    /// <remarks>
    /// It sits in the same phase as cohesion so it reads the same pre-effect state. That is only
    /// legitimate if adding it changes nothing, which is exactly what name-derived random streams and
    /// the phase barrier are supposed to guarantee.
    /// </remarks>
    [Fact]
    public void MechanismObserverDoesNotChangeTheSimulation()
    {
        Simulation Run(bool observe)
        {
            var config = SimulationConfig.Default with
            {
                Seed = 31337,
                WorldWidth = 12,
                WorldHeight = 8,
                InitialPolityCount = 8,
                InvariantMode = InvariantMode.Periodic,
            };

            var systems = new List<ISimulationSystem>(DefaultSystems.Build());
            if (observe)
            {
                systems.Add(new MechanismObserverSystem(CohesionRules.Default, new MechanismSink()));
            }

            Simulation sim = Simulation.Create(config, systems);
            sim.AdvanceYears(500);
            return sim;
        }

        Simulation plain = Run(observe: false);
        Simulation observed = Run(observe: true);

        Assert.Equal(plain.StateHash(), observed.StateHash());
        Assert.Equal(plain.Chronicle.Count, observed.Chronicle.Count);

        for (int i = 0; i < plain.Chronicle.Count; i++)
        {
            Assert.Equal(plain.Chronicle.Events[i], observed.Chronicle.Events[i]);
        }
    }

    /// <summary>
    /// The observer sees the world before secession moves it, not after.
    /// </summary>
    /// <remarks>
    /// The whole point of running in-phase. Recomputing the diagnostic from the end-of-year map would
    /// measure the consequences of the decision instead of the decision.
    /// </remarks>
    [Fact]
    public void TheObserverReadsPreEffectStateInTheAccessionYear()
    {
        const int SuccessionYear = 6;

        (WorldBuilder builder, _, PolityId realm) = LongRealm(administration: 100);
        var sink = new MechanismSink();

        Simulation sim = Simulation.Resume(
            Config,
            builder.World,
            [
                Succession(realm, SuccessionYear, administration: 0),
                new CohesionSecessionSystem(Decisive),
                new MechanismObserverSystem(Decisive, sink),
                new PolityLifecycleSystem(),
            ]);

        sim.AdvanceYears(SuccessionYear - 1);
        long exposedBefore = sink.All.RegionsExposed;

        sim.AdvanceYear();
        long exposedDuringAccessionYear = sink.All.RegionsExposed - exposedBefore;

        // Recomputing the same quantity from the end-of-year map, after the secession has already
        // carried the restive provinces away, gives a materially smaller answer. That gap is the
        // entire reason the observer has to run inside the phase.
        Polity realmNow = sim.World.Polities.Get(realm);
        int adminNow = CohesionSecessionSystem.AdministrationOf(sim.World, realmNow, Decisive);
        int actualNow = CohesionSecessionSystem.Authority(realmNow, Decisive, adminNow);
        int baselineNow = CohesionSecessionSystem.Authority(
            realmNow, Decisive, Decisive.DefaultAdministration);

        var strainsNow = CohesionSecessionSystem.StrainMap(sim.World, realmNow, Decisive, adminNow)
            .Values
            .Select(v => v.Strain)
            .ToList();

        int postEffectExposed = strainsNow.Count(x => x > actualNow) - strainsNow.Count(x => x > baselineNow);

        Assert.True(
            exposedDuringAccessionYear > 0,
            "the observer should have seen the whole restive periphery before it seceded");
        Assert.True(
            postEffectExposed < exposedDuringAccessionYear,
            $"post-effect view showed {postEffectExposed} exposed regions against "
            + $"{exposedDuringAccessionYear} seen in phase - the timing correction must matter");

        Assert.True(WorldQueries.RegionCountOf(sim.World, realm) < 11);
        Assert.Empty(sim.Violations);
    }
}
