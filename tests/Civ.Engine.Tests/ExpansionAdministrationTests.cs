using Civ.Batch;
using Civ.Engine.Config;
using Civ.Engine.Effects;
using Civ.Engine.Events;
using Civ.Engine.State;
using Civ.Engine.Systems;
using Civ.Systems;

namespace Civ.Engine.Tests;

/// <summary>
/// Administration reaching expansion, through the overextension term and nothing else.
/// </summary>
/// <remarks>
/// The cohesion band decides what a state can keep; this decides what it can afford to take. The
/// tests here are mostly about what the modifier does <i>not</i> touch, because a ruler bonus that
/// leaks into population, defence, reach or target selection would be a general military bonus by
/// another name.
/// </remarks>
public sealed class ExpansionAdministrationTests
{
    /// <summary>
    /// The administration-to-overextension band, which is <b>off in the default simulation</b>.
    /// </summary>
    /// <remarks>
    /// The experiment that enabled it was measured and failed: across bands from 125/75 to 200/20,
    /// over 50 seeds and 3000 years, it moved expansion counts but never the peak-share distribution.
    /// The implementation is retained and still tested, because it is correct and cheap to re-enable
    /// - but every test here has to opt into it explicitly, so nothing can quietly come to depend on
    /// a rule the default world does not use.
    /// </remarks>
    private static ExpansionRules Experimental => ExpansionRules.Default with
    {
        OverextensionAtWeakestPercent = 125,
        OverextensionAtStrongestPercent = 75,
    };

    /// <summary>The default: overextension unaffected by who is ruling.</summary>
    private static ExpansionRules Inert => ExpansionRules.Default;

    private static RulerRules Immortal => RulerRules.Default with
    {
        MortalityBasePermille = 0,
        MortalityRisePermillePerYear = 0,
        MaximumAge = 100_000,
    };

    /// <summary>Cohesion held completely inert, so any territorial difference is expansion's doing.</summary>
    private static CohesionRules NoFragmentation => CohesionRules.Default with
    {
        AdministrativeCapacity = 1_000_000,
        RulerCapacityFloorPercent = 100,
        RulerCapacityCeilingPercent = 100,
    };

    private static SimulationConfig Config => SimulationConfig.Default with
    {
        Seed = 61616,
        InvariantMode = InvariantMode.EveryTick,
        ThrowOnInvariantViolation = true,
    };

    // ------------------------------------------------------------------ arithmetic

    [Fact]
    public void AdministrationFiftyReproducesTheUnmodifiedOverextensionTerm()
    {
        ExpansionRules rules = Experimental;

        for (int held = 0; held <= 200; held++)
        {
            Assert.Equal(
                (long)rules.OverextensionPerRegion * held,
                rules.OverextensionTerm(held, rules.NeutralAdministration));
        }

        Assert.Equal(100, rules.OverextensionPercent(50));
        Assert.Equal(125, rules.OverextensionPercent(0));
        Assert.Equal(75, rules.OverextensionPercent(100));
    }

    [Fact]
    public void TheModifierIsMonotonicAndBounded()
    {
        ExpansionRules rules = Experimental;

        for (int ability = 1; ability <= 100; ability++)
        {
            Assert.True(rules.OverextensionPercent(ability) <= rules.OverextensionPercent(ability - 1));
        }

        // Out-of-range abilities clamp rather than extrapolating.
        Assert.Equal(rules.OverextensionPercent(0), rules.OverextensionPercent(-40));
        Assert.Equal(rules.OverextensionPercent(100), rules.OverextensionPercent(400));
    }

    /// <summary>
    /// The effect scales with how much a polity holds, because overextension does.
    /// </summary>
    /// <remarks>
    /// This is why the percentage is applied to the whole term rather than to the per-region
    /// constant. Scaling a constant of 4 would round to 5 and 3 and give the same coarse modifier to
    /// a three-province state and a fifty-province empire.
    /// </remarks>
    [Fact]
    public void TheAdministrativeEffectGrowsWithPolitySize()
    {
        ExpansionRules rules = Experimental;
        long previous = 0;

        foreach (int held in (int[])[5, 10, 20, 40, 80])
        {
            long gap = rules.OverextensionTerm(held, 0) - rules.OverextensionTerm(held, 100);
            Assert.True(gap > previous, $"gap at {held} regions was {gap}, not above {previous}");
            previous = gap;
        }
    }

    [Fact]
    public void WithNoOverextensionAtAllAdministrationChangesNothing()
    {
        ExpansionRules rules = Experimental with { OverextensionPerRegion = 0 };

        for (int ability = 0; ability <= 100; ability += 10)
        {
            Assert.Equal(0, rules.OverextensionTerm(held: 40, ability));
        }
    }

    // ------------------------------------------------------------------ simulation

    /// <summary>
    /// A world of average rulers must behave exactly as it did before the modifier existed.
    /// </summary>
    /// <remarks>
    /// Asserted at the level that matters - the whole world hash after centuries - rather than on the
    /// arithmetic alone. If ability 50 is truly neutral then the band cannot be observed at all.
    /// </remarks>
    [Fact]
    public void AWorldOfAverageRulersIsIdenticalWithAndWithoutTheBand()
    {
        Simulation Run(ExpansionRules expansion)
        {
            var builder = new WorldBuilder();
            RegionId[] line = builder.Line(Enumerable.Repeat(20_000L, 12).ToArray());

            // Four peers, every ruler exactly average and immortal, so ability never varies.
            for (int i = 0; i < 4; i++)
            {
                builder.Polity($"Peer {i}", 50, 50, line[(i * 3)..((i * 3) + 3)]);
            }

            Simulation sim = Simulation.Resume(
                Config,
                builder.World,
                DefaultSystems.Build(expansion, CohesionRules.Default, Immortal));

            sim.AdvanceYears(400);
            return sim;
        }

        Assert.Equal(Run(Inert).StateHash(), Run(Experimental).StateHash());
    }

    /// <summary>
    /// A large realm, a tempting neighbour, and only the ruler differs.
    /// </summary>
    /// <remarks>
    /// The overextension term dominates at this size, so the strong administrator clears the pressure
    /// threshold and the weak one does not. Cohesion is switched off entirely, so nothing here can be
    /// explained by retention.
    /// </remarks>
    private static Simulation LargeRealmBesideAWeakNeighbour(
        int administration, ExpansionRules expansion, long targetPopulation = 38_000)
    {
        var builder = new WorldBuilder();

        // A 24-province realm, then a marginal neighbour: rich enough that the overextension term
        // decides the outcome rather than raw strength.
        long[] populations = [.. Enumerable.Repeat(30_000L, 24), targetPopulation, targetPopulation];
        RegionId[] line = builder.Line(populations);

        builder.Polity("Hegemon", 50, administration, line[..24]);
        builder.Polity("Marches", 50, 50, line[24..]);

        // No population growth. The fixture is calibrated on a knife edge, and populations drifting
        // toward their carrying capacity would move both sides out from under it.
        return Simulation.Resume(
            Config,
            builder.World,
            [
                new RulerSuccessionSystem(Immortal),
                new CohesionSecessionSystem(NoFragmentation),
                new OpportunisticExpansionSystem(expansion),
                new PolityLifecycleSystem(),
            ]);
    }

    [Fact]
    public void AStrongAdministratorSustainsAnExpansionAWeakOneCannot()
    {
        Simulation strong = LargeRealmBesideAWeakNeighbour(100, Experimental);
        Simulation weak = LargeRealmBesideAWeakNeighbour(0, Experimental);

        strong.AdvanceYears(400);
        weak.AdvanceYears(400);

        int strongHeld = WorldQueries.RegionCountOf(strong.World, strong.World.Polities.AllIds().First());
        int weakHeld = WorldQueries.RegionCountOf(weak.World, weak.World.Polities.AllIds().First());

        Assert.True(
            strongHeld > weakHeld,
            $"strong administrator held {strongHeld}, weak held {weakHeld}");

        Assert.Empty(strong.Violations);
        Assert.Empty(weak.Violations);
    }

    [Fact]
    public void TheDifferenceVanishesWhenOverextensionIsRemoved()
    {
        ExpansionRules noOverextension = Experimental with { OverextensionPerRegion = 0 };

        Simulation strong = LargeRealmBesideAWeakNeighbour(100, noOverextension);
        Simulation weak = LargeRealmBesideAWeakNeighbour(0, noOverextension);

        strong.AdvanceYears(400);
        weak.AdvanceYears(400);

        // Compared on territory, not on the world hash: the hash includes the rulers themselves, so
        // two worlds with differently-abled rulers can never hash alike however identical their maps.
        static List<(int Year, RegionId Region, PolityId To)> Transfers(Simulation sim) =>
            [.. sim.Chronicle.Events.OfType<RegionControlChangedEvent>().Select(e => (e.Year, e.Region, e.To))];

        Assert.Equal(Transfers(strong), Transfers(weak));

        foreach (PolityId id in strong.World.Polities.AllIds())
        {
            Assert.Equal(
                WorldQueries.RegionCountOf(strong.World, id),
                WorldQueries.RegionCountOf(weak.World, id));
        }
    }

    /// <summary>
    /// A weak successor can halt an advance, with no rule that says a succession halts anything.
    /// </summary>
    /// <remarks>
    /// Compared against the same world where the strong ruler simply carries on, so the claim is
    /// "the succession stopped it" rather than "expansion happened to stall". Nothing in the ruler
    /// layer touches territory: the successor's only effect is a larger overextension term.
    /// </remarks>
    [Fact]
    public void ASuccessionFromStrongToWeakCanStopFurtherExpansion()
    {
        const int SuccessionYear = 150;

        Simulation Run(bool weakenTheHeir)
        {
            var builder = new WorldBuilder();
            long[] populations = [.. Enumerable.Repeat(30_000L, 24), 38_000L, 38_000L];
            RegionId[] line = builder.Line(populations);

            PolityId hegemon = builder.Polity("Hegemon", 50, 100, line[..24]);
            builder.Polity("Marches", 50, 50, line[24..]);

            var systems = new List<ISimulationSystem>
            {
                new RulerSuccessionSystem(Immortal),
                new CohesionSecessionSystem(NoFragmentation),
                new OpportunisticExpansionSystem(Experimental),
                new PolityLifecycleSystem(),
            };

            if (weakenTheHeir)
            {
                systems.Add(new ScriptedSystem(
                    "test.weak_heir", SimulationPhase.Rulership, (in SystemContext ctx) =>
                    {
                        if (ctx.Year != SuccessionYear)
                        {
                            return;
                        }

                        ctx.Effects.Emit(new EndReign(ctx.World.Polities.Get(hegemon).CurrentRuler, "test"));
                        ctx.Effects.Emit(new InstallRuler(
                            hegemon,
                            new RulerProfile("The Unready", ctx.Year - 30, 0),
                            RulerSuccessionSystem.SuccessionReason));
                    }));
            }

            return Simulation.Resume(Config, builder.World, systems);
        }

        Simulation succeeded = Run(weakenTheHeir: true);
        Simulation continuous = Run(weakenTheHeir: false);

        succeeded.AdvanceYears(SuccessionYear);
        PolityId hegemonId = succeeded.World.Polities.AllIds().First();
        int atSuccession = WorldQueries.RegionCountOf(succeeded.World, hegemonId);

        succeeded.AdvanceYears(600);
        continuous.AdvanceYears(SuccessionYear + 600);

        int afterWeakHeir = WorldQueries.RegionCountOf(succeeded.World, hegemonId);
        int underStrongRuler = WorldQueries.RegionCountOf(
            continuous.World, continuous.World.Polities.AllIds().First());

        Assert.Equal(0, succeeded.World.Rulers.Get(
            succeeded.World.Polities.Get(hegemonId).CurrentRuler).Administration);

        // The advance stopped where the strong ruler left it, and the counterfactual kept going.
        Assert.Equal(atSuccession, afterWeakHeir);
        Assert.True(
            underStrongRuler > afterWeakHeir,
            $"strong ruler reached {underStrongRuler}, weak heir stalled at {afterWeakHeir}");

        Assert.Empty(succeeded.Violations);
        Assert.Empty(continuous.Violations);
    }

    /// <summary>
    /// Ruler ability never moves territory by itself.
    /// </summary>
    /// <remarks>
    /// Succession running alone, with no expansion and no cohesion installed, must produce no
    /// territorial event at all however many rulers come and go.
    /// </remarks>
    [Fact]
    public void ChangingRulersEmitsNoTerritorialEffects()
    {
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(Enumerable.Repeat(10_000L, 8).ToArray());
        PolityId realm = builder.Polity("Realm", 50, 50, line);

        Simulation sim = Simulation.Resume(
            Config,
            builder.World,
            [new RulerSuccessionSystem(), new PolityLifecycleSystem()]);

        sim.AdvanceYears(1500);

        Assert.True(sim.Chronicle.Events.OfType<RulerDeathEvent>().Count() > 20);
        Assert.Empty(sim.Chronicle.Events.OfType<RegionControlChangedEvent>());
        Assert.Empty(sim.Chronicle.Events.OfType<PolityDissolvedEvent>());
        Assert.Empty(sim.Chronicle.Events.OfType<PolityStabilityShiftEvent>());
        Assert.Equal(8, WorldQueries.RegionCountOf(sim.World, realm));
        Assert.Empty(sim.Violations);
    }

    [Fact]
    public void ExpansionRemainsDeterministicWithTheBandInPlace()
    {
        Simulation Run()
        {
            Simulation sim = Simulation.Create(
                SimulationConfig.Default with
                {
                    Seed = 909090,
                    WorldWidth = 12,
                    WorldHeight = 8,
                    InitialPolityCount = 8,
                    InvariantMode = InvariantMode.Periodic,
                },
                DefaultSystems.Build());

            sim.AdvanceYears(800);
            return sim;
        }

        Simulation first = Run();
        Simulation second = Run();

        Assert.Equal(first.StateHash(), second.StateHash());

        for (int i = 0; i < first.Chronicle.Count; i++)
        {
            Assert.Equal(first.Chronicle.Events[i], second.Chronicle.Events[i]);
        }
    }

    [Fact]
    public void LongRepeatedSuccessionsPreserveEveryInvariant()
    {
        Simulation sim = Simulation.Create(
            SimulationConfig.Default with
            {
                Seed = 123123,
                WorldWidth = 14,
                WorldHeight = 10,
                InitialPolityCount = 10,
                InvariantMode = InvariantMode.EveryTick,
                ThrowOnInvariantViolation = true,
            },
            DefaultSystems.Build());

        sim.AdvanceYears(2000);

        Assert.Empty(sim.Violations);
        Assert.True(sim.Chronicle.Events.OfType<RulerDeathEvent>().Count() > 200);
        Assert.NotEmpty(sim.Chronicle.Events.OfType<RegionControlChangedEvent>());
    }
}

/// <summary>
/// The batch runner's pass/fail evaluation.
/// </summary>
/// <remarks>
/// The paired path used to return success unconditionally, so a sweep with invariant violations
/// still exited 0. A regression gate that always opens is worse than no gate, and the only way to
/// keep it honest is to test the decision itself rather than trust the reporting around it.
/// </remarks>
public sealed class BatchOutcomeTests
{
    [Fact]
    public void CleanArmsSucceed()
    {
        ArmOutcome[] arms =
        [
            new("A", 0, 0, DeterminismChecked: true),
            new("B", 0, 0, DeterminismChecked: true),
        ];

        Assert.Equal(BatchOutcome.Success, BatchOutcome.ExitCode(arms));
        Assert.All(arms, arm => Assert.True(arm.ReproducedExactly));
    }

    [Fact]
    public void AnyInvariantViolationInAnyArmFails()
    {
        Assert.Equal(
            BatchOutcome.Failure,
            BatchOutcome.ExitCode([
                new ArmOutcome("A", 0, 0, true),
                new ArmOutcome("B", 3, 0, true),
            ]));
    }

    [Fact]
    public void AnyDeterminismMismatchInAnyArmFails()
    {
        Assert.Equal(
            BatchOutcome.Failure,
            BatchOutcome.ExitCode([
                new ArmOutcome("A", 0, 1, true),
                new ArmOutcome("B", 0, 0, true),
            ]));
    }

    /// <summary>
    /// An unverified arm passes the gate but must not be described as reproduced.
    /// </summary>
    /// <remarks>
    /// Running without <c>--verify</c> is a legitimate choice; claiming determinism afterwards is
    /// not. The two are separate questions and the outcome record keeps them separate.
    /// </remarks>
    [Fact]
    public void AnUncheckedArmIsNotReportedAsReproduced()
    {
        var arm = new ArmOutcome("A", 0, 0, DeterminismChecked: false);

        Assert.Equal(BatchOutcome.Success, BatchOutcome.ExitCode([arm]));
        Assert.False(arm.ReproducedExactly);
        Assert.Contains("NOT CHECKED", arm.Describe(), StringComparison.Ordinal);
    }
}
