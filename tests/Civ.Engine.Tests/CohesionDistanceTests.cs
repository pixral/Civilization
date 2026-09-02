using Civ.Batch;
using Civ.Engine.Config;
using Civ.Engine.Effects;
using Civ.Engine.Events;
using Civ.Engine.Persistence;
using Civ.Engine.State;
using Civ.Engine.Systems;
using Civ.Systems;

namespace Civ.Engine.Tests;

/// <summary>
/// The one-sided administrative reach benefit: exceptional administrators hold remote provinces.
/// </summary>
/// <remarks>
/// <para>The previous, symmetric version of this modifier was measured and rejected. It worked
/// exactly as designed and still made the world worse, because raising strain under weak rulers
/// pushed five times more territory over the restive threshold than lowering it under strong rulers
/// held below it. The conversion tested here removes the raising half entirely.</para>
///
/// <para>Two properties therefore matter more than any tuning value, and most of these tests exist
/// to pin them. <b>Half the ruler population must be inert</b>: everyone at ability 50 or below has
/// to reproduce the current world exactly, or this is a global change to distance strain wearing a
/// ruler's name. And <b>only connected distance may move</b>: a modifier that quietly lowered every
/// strain term would be a secession suppressor with a misleading label, which is precisely what arm
/// C of the experiment is built to imitate.</para>
/// </remarks>
public sealed class CohesionDistanceTests
{
    private static RulerRules Immortal => RulerRules.Default with
    {
        MortalityBasePermille = 0,
        MortalityRisePermillePerYear = 0,
        MaximumAge = 100_000,
    };

    private static SimulationConfig Config => SimulationConfig.Default with
    {
        Seed = 313_131,
        InvariantMode = InvariantMode.EveryTick,
        ThrowOnInvariantViolation = true,
    };

    /// <summary>
    /// The candidate: 100% distance strain to ability 50, falling to 50% at ability 100.
    /// </summary>
    /// <remarks>
    /// Opt-in, exactly like the overextension band. The default world runs with the benefit disabled
    /// until an experiment says otherwise, so nothing can quietly come to depend on a rule that is
    /// not shipped.
    /// </remarks>
    private static CohesionRules Reach => CohesionRules.Default with
    {
        DistanceStrainAtStrongestPercent = 50,
    };

    /// <summary>The default: connected-distance strain unaffected by who is ruling.</summary>
    private static CohesionRules FlatDistance => CohesionRules.Default;

    // ------------------------------------------------------------------ the conversion

    [Fact]
    public void EveryAdministratorUpToFiftyIsExactlyNeutral()
    {
        CohesionRules rules = Reach;

        foreach (int ability in (int[])[0, 1, 25, 37, 49, 50])
        {
            Assert.Equal(100, rules.DistanceStrainPercent(ability));
        }

        // Clamping below zero must not slip past the flat segment either.
        Assert.Equal(100, rules.DistanceStrainPercent(-40));

        for (int ability = 0; ability <= CohesionRules.NeutralAdministration; ability++)
        {
            for (int distance = 0; distance <= 40; distance++)
            {
                Assert.Equal(
                    (long)rules.DistanceStrainPerStep * distance,
                    rules.DistanceStrainTerm(distance, ability));
            }
        }
    }

    [Fact]
    public void AbilityAboveFiftyEarnsAMonotonicBenefitReachingTheConfiguredFloor()
    {
        CohesionRules rules = Reach;

        Assert.Equal(rules.DistanceStrainAtStrongestPercent, rules.DistanceStrainPercent(100));
        Assert.Equal(rules.DistanceStrainPercent(100), rules.DistanceStrainPercent(180));

        for (int ability = 51; ability <= 100; ability++)
        {
            Assert.True(
                rules.DistanceStrainPercent(ability) <= rules.DistanceStrainPercent(ability - 1),
                $"ability {ability} was not at least as good as {ability - 1}");
        }

        // And it genuinely moves rather than merely failing to rise.
        Assert.True(rules.DistanceStrainPercent(75) < 100);
        Assert.True(rules.DistanceStrainPercent(100) < rules.DistanceStrainPercent(75));
    }

    /// <summary>
    /// No step at the join. The segment boundary is the one place a piecewise rule can misbehave.
    /// </summary>
    [Fact]
    public void TheConversionIsContinuousAtFifty()
    {
        CohesionRules rules = Reach;

        Assert.Equal(100, rules.DistanceStrainPercent(50));
        Assert.Equal(99, rules.DistanceStrainPercent(51));

        // One ability point either side of the join is worth one percentage point, not a jump.
        Assert.Equal(
            rules.DistanceStrainPercent(50) - rules.DistanceStrainPercent(51),
            rules.DistanceStrainPercent(51) - rules.DistanceStrainPercent(52));
    }

    /// <summary>
    /// It is a benefit and never a penalty, at any ability, distance or configured endpoint.
    /// </summary>
    /// <remarks>
    /// The whole finding behind this design is that exposing territory costs far more than retaining
    /// it saves. A configuration able to raise distance strain would reintroduce the failure the
    /// conversion exists to avoid, so the rules refuse one and this checks the arithmetic agrees.
    /// </remarks>
    [Fact]
    public void TheModifierCanNeverIncreaseDistanceStrain()
    {
        foreach (int strongest in (int[])[0, 25, 50, 75, 90, 100])
        {
            CohesionRules rules = CohesionRules.Default with
            {
                DistanceStrainAtStrongestPercent = strongest,
            };

            for (int ability = 0; ability <= 100; ability++)
            {
                Assert.True(rules.DistanceStrainPercent(ability) <= 100);

                for (int distance = 0; distance <= 20; distance++)
                {
                    Assert.True(
                        rules.DistanceStrainTerm(distance, ability)
                        <= (long)rules.DistanceStrainPerStep * distance,
                        $"strongest {strongest}, ability {ability}, distance {distance} rose");
                }
            }
        }
    }

    [Fact]
    public void ACapitalIsUnaffectedAtEveryAbility()
    {
        CohesionRules rules = Reach;

        for (int ability = 0; ability <= 100; ability += 5)
        {
            Assert.Equal(0, rules.DistanceStrainTerm(distance: 0, ability));
        }
    }

    /// <summary>
    /// The benefit widens with distance, which is the entire purpose of the modifier.
    /// </summary>
    /// <remarks>
    /// It is also why the percentage multiplies the complete term rather than the per-step constant:
    /// scaling 14 first would round to 7 and give a coarse, distance-independent modifier.
    /// </remarks>
    [Fact]
    public void TheBenefitGrowsWithDistance()
    {
        CohesionRules rules = Reach;
        long previous = 0;

        foreach (int distance in (int[])[1, 2, 4, 8, 16])
        {
            long gap = rules.DistanceStrainTerm(distance, 50) - rules.DistanceStrainTerm(distance, 100);
            Assert.True(gap > previous, $"gap at distance {distance} was {gap}, not above {previous}");
            previous = gap;
        }
    }

    // ------------------------------------------------------------------ term isolation

    /// <summary>A realm stretched along a line, with one province cut off behind a rival.</summary>
    private static (WorldState World, PolityId Realm, RegionId[] Line) StretchedRealm(int administration)
    {
        var builder = new WorldBuilder();

        // Ten in a row, then a gap held by a neighbour, then an exclave beyond it.
        long[] populations = [.. Enumerable.Repeat(10_000L, 10), 10_000L, 40_000L];
        RegionId[] line = builder.Line(populations);

        PolityId realm = builder.Polity("Long Realm", 50, administration, 50, [.. line[..10], line[11]]);
        builder.Polity("Wedge", 50, 50, 50, line[10]);

        return (builder.World, realm, line);
    }

    /// <summary>
    /// Only the connected-distance component moves. Every other term is byte-identical.
    /// </summary>
    /// <remarks>
    /// Checked region by region against the neutral case: the disconnected exclave must not shift by
    /// a single point, and each connected province must shift by exactly the difference the distance
    /// formula predicts - no more, which would mean size or prosperity had been touched too.
    /// </remarks>
    [Fact]
    public void OnlyTheConnectedDistanceComponentChanges()
    {
        CohesionRules rules = Reach;

        (WorldState world, PolityId realm, RegionId[] line) = StretchedRealm(100);
        Polity polity = world.Polities.Get(realm);

        var neutral = CohesionSecessionSystem.StrainMap(world, polity, rules, 50);
        var strong = CohesionSecessionSystem.StrainMap(world, polity, rules, 100);
        var weak = CohesionSecessionSystem.StrainMap(world, polity, rules, 0);

        Assert.NotEmpty(neutral);

        foreach ((RegionId id, CohesionSecessionSystem.RegionStrain baseline) in neutral)
        {
            long expectedStrong = baseline.Strain
                - rules.DistanceStrainTerm(Math.Max(0, baseline.Distance), 50)
                + rules.DistanceStrainTerm(Math.Max(0, baseline.Distance), 100);

            if (!baseline.Connected)
            {
                // The exclave's strain is disconnection, size and prosperity - none of them scaled.
                Assert.Equal(baseline.Strain, strong[id].Strain);
                Assert.Equal(baseline.Strain, weak[id].Strain);
                continue;
            }

            Assert.Equal(expectedStrong, strong[id].Strain);

            // A weak administrator is the neutral case exactly, everywhere.
            Assert.Equal(baseline.Strain, weak[id].Strain);
        }

        // And the fixture really does contain the two cases the assertions depend on.
        Assert.Contains(neutral, kv => !kv.Value.Connected);
        Assert.Contains(neutral, kv => kv.Value.Connected && kv.Value.Distance >= 5);
        Assert.True(world.Regions.Get(line[11]).Controller == realm);
    }

    [Fact]
    public void NearProvincesBarelyMoveWhileRemoteOnesGainTheMost()
    {
        CohesionRules rules = Reach;
        (WorldState world, PolityId realm, _) = StretchedRealm(50);
        Polity polity = world.Polities.Get(realm);

        var strong = CohesionSecessionSystem.StrainMap(world, polity, rules, 100);
        var neutral = CohesionSecessionSystem.StrainMap(world, polity, rules, 50);

        foreach ((RegionId id, CohesionSecessionSystem.RegionStrain near) in strong)
        {
            if (!near.Connected || near.Distance > 1)
            {
                continue;
            }

            // Distance 0 and 1 barely move: at one step the whole term is 14 points.
            Assert.True(neutral[id].Strain - near.Strain <= rules.DistanceStrainPerStep);
        }

        CohesionSecessionSystem.RegionStrain farStrong =
            strong.Values.Where(v => v.Connected).MaxBy(v => v.Distance);
        RegionId farId = strong.First(kv => kv.Value.Distance == farStrong.Distance).Key;

        Assert.True(
            neutral[farId].Strain - farStrong.Strain > rules.DistanceStrainPerStep,
            "the remote end should gain much more than a single step");
    }

    /// <summary>
    /// Over a whole generated world, no ordinary administrator sees any difference at all.
    /// </summary>
    /// <remarks>
    /// The region-by-region fixture above proves the arithmetic; this proves it holds across every
    /// polity shape worldgen actually produces - exclaves, one-province statelets, sprawling realms -
    /// rather than only in the line the fixture happens to build.
    /// </remarks>
    [Fact]
    public void AverageAndWeakAdministratorsSeeNoChangeAnywhereInAGeneratedWorld()
    {
        Simulation sim = Simulation.Create(
            SimulationConfig.Default with
            {
                Seed = 4_242,
                WorldWidth = 14,
                WorldHeight = 10,
                InitialPolityCount = 10,
            },
            DefaultSystems.Build());

        sim.AdvanceYears(400);

        int compared = 0;

        foreach (Polity polity in WorldQueries.ActivePolities(sim.World))
        {
            for (int ability = 0; ability <= CohesionRules.NeutralAdministration; ability += 10)
            {
                var flat = CohesionSecessionSystem.StrainMap(sim.World, polity, FlatDistance, ability);
                var reach = CohesionSecessionSystem.StrainMap(sim.World, polity, Reach, ability);

                Assert.Equal(flat.Count, reach.Count);

                foreach ((RegionId id, CohesionSecessionSystem.RegionStrain expected) in flat)
                {
                    Assert.Equal(expected, reach[id]);
                    compared++;
                }
            }
        }

        Assert.True(compared > 500, $"only {compared} regions were compared");
    }

    // ------------------------------------------------------------------ in the simulation

    /// <summary>A long realm whose far provinces sit right on the edge of governability.</summary>
    private static Simulation LongRealm(int administration, CohesionRules cohesion)
    {
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line([.. Enumerable.Repeat(10_000L, 12)]);
        builder.Polity("Long Realm", 50, administration, 50, line);

        return Simulation.Resume(
            Config,
            builder.World,
            [
                new RulerSuccessionSystem(Immortal),
                new CohesionSecessionSystem(cohesion),
                new PolityLifecycleSystem(),
            ]);
    }

    /// <summary>Capacity band off, so any difference is the reach benefit alone.</summary>
    private static CohesionRules ReachOnly => Reach with
    {
        RulerCapacityFloorPercent = 100,
        RulerCapacityCeilingPercent = 100,
        StrainPerPermille = 1,
        MaxAttemptPermille = 1000,
    };

    [Fact]
    public void AStrongAdministratorRetainsARemotePeripheryAnAverageOneCannot()
    {
        Simulation strong = LongRealm(100, ReachOnly);
        Simulation average = LongRealm(50, ReachOnly);

        strong.AdvanceYears(300);
        average.AdvanceYears(300);

        int strongHeld = WorldQueries.RegionCountOf(strong.World, strong.World.Polities.AllIds().First());
        int averageHeld = WorldQueries.RegionCountOf(average.World, average.World.Polities.AllIds().First());

        Assert.Equal(12, strongHeld);
        Assert.True(averageHeld < 12, $"the average administrator held all {averageHeld} provinces");
        Assert.Empty(strong.Violations);
        Assert.Empty(average.Violations);
    }

    /// <summary>A weak administrator does no worse than an average one. That is the design.</summary>
    [Fact]
    public void AWeakAdministratorFaresExactlyAsAnAverageOneDoes()
    {
        Simulation weak = LongRealm(0, ReachOnly);
        Simulation average = LongRealm(50, ReachOnly);

        weak.AdvanceYears(300);
        average.AdvanceYears(300);

        // Same territory and the same number of breakaways - the two rulers are the same rule.
        Assert.Equal(
            WorldQueries.RegionCountOf(average.World, average.World.Polities.AllIds().First()),
            WorldQueries.RegionCountOf(weak.World, weak.World.Polities.AllIds().First()));

        Assert.Equal(
            average.Chronicle.Events.OfType<PolityFoundedEvent>().Count(),
            weak.Chronicle.Events.OfType<PolityFoundedEvent>().Count());
    }

    /// <summary>
    /// The benefit is gone in the accession year itself, not a year later.
    /// </summary>
    /// <remarks>
    /// Succession runs in the Rulership phase and cohesion in the Polity phase, so the new ruler's
    /// reach is the one cohesion reads that same year. A one-year lag here would put every
    /// consequence of a succession into the wrong reign.
    /// </remarks>
    [Fact]
    public void SuccessionRemovesTheBenefitInTheAccessionYearItself()
    {
        const int SuccessionYear = 40;

        var builder = new WorldBuilder();
        RegionId[] line = builder.Line([.. Enumerable.Repeat(10_000L, 12)]);
        PolityId realm = builder.Polity("Long Realm", 50, 100, 50, line);

        var observed = new List<(int Year, long FarStrain)>();
        CohesionRules rules = ReachOnly;

        Simulation sim = Simulation.Resume(
            Config,
            builder.World,
            [
                new ScriptedSystem("test.heir", SimulationPhase.Rulership, (in SystemContext ctx) =>
                {
                    if (ctx.Year != SuccessionYear)
                    {
                        return;
                    }

                    ctx.Effects.Emit(new EndReign(ctx.World.Polities.Get(realm).CurrentRuler, "test"));
                    ctx.Effects.Emit(new InstallRuler(
                        realm,
                        new RulerProfile("The Ordinary", ctx.Year - 30, 40, 50),
                        RulerSuccessionSystem.SuccessionReason));
                }),

                // Reads in the Polity phase, exactly where cohesion reads.
                new ScriptedSystem("test.watch", SimulationPhase.Polity, (in SystemContext ctx) =>
                {
                    if (!ctx.World.Polities.TryGet(realm, out Polity? state))
                    {
                        return;
                    }

                    int admin = CohesionSecessionSystem.AdministrationOf(ctx.World, state, rules);
                    var strains = CohesionSecessionSystem.StrainMap(ctx.World, state, rules, admin);
                    CohesionSecessionSystem.RegionStrain far =
                        strains.Values.Where(v => v.Connected).MaxBy(v => v.Distance);
                    observed.Add((ctx.Year, far.Strain));
                }),
                new PolityLifecycleSystem(),
            ]);

        sim.AdvanceYears(SuccessionYear);

        long underStrong = observed.Single(o => o.Year == SuccessionYear - 1).FarStrain;
        long inAccessionYear = observed.Single(o => o.Year == SuccessionYear).FarStrain;

        Assert.True(
            inAccessionYear > underStrong,
            $"strain was {inAccessionYear} in the accession year against {underStrong} before it");

        // And it lands on the neutral value, because ability 40 is inside the flat segment.
        Polity polity = sim.World.Polities.Get(realm);
        var neutral = CohesionSecessionSystem.StrainMap(sim.World, polity, rules, 50);
        Assert.Equal(
            neutral.Values.Where(v => v.Connected).MaxBy(v => v.Distance).Strain,
            inAccessionYear);
    }

    /// <summary>
    /// Swapping the ruler alone makes remote territory restive, through the existing cohesion rule.
    /// </summary>
    [Fact]
    public void AWeakSuccessorAloneCanMakeRemoteTerritoryRestive()
    {
        const int SuccessionYear = 150;

        var builder = new WorldBuilder();
        RegionId[] line = builder.Line([.. Enumerable.Repeat(10_000L, 12)]);
        PolityId realm = builder.Polity("Long Realm", 50, 100, 50, line);

        Simulation sim = Simulation.Resume(
            Config,
            builder.World,
            [
                new ScriptedSystem("test.weak_heir", SimulationPhase.Rulership, (in SystemContext ctx) =>
                {
                    if (ctx.Year != SuccessionYear)
                    {
                        return;
                    }

                    ctx.Effects.Emit(new EndReign(ctx.World.Polities.Get(realm).CurrentRuler, "test"));
                    ctx.Effects.Emit(new InstallRuler(
                        realm,
                        new RulerProfile("The Unready", ctx.Year - 30, 0, 50),
                        RulerSuccessionSystem.SuccessionReason));
                }),
                new CohesionSecessionSystem(ReachOnly),
                new PolityLifecycleSystem(),
            ]);

        sim.AdvanceYears(SuccessionYear - 1);
        Assert.Equal(12, WorldQueries.RegionCountOf(sim.World, realm));
        Assert.DoesNotContain(
            sim.Chronicle.Events.OfType<PolityFoundedEvent>(),
            e => e.Reason == CohesionSecessionSystem.SecessionReason);

        sim.AdvanceYears(300);

        var secessions = sim.Chronicle.Events
            .OfType<PolityFoundedEvent>()
            .Where(e => e.Reason == CohesionSecessionSystem.SecessionReason)
            .ToList();

        // Every province that left did so through the cohesion system, not through succession.
        Assert.NotEmpty(secessions);
        Assert.All(secessions, e => Assert.True(e.Year >= SuccessionYear));
        Assert.True(WorldQueries.RegionCountOf(sim.World, realm) < 12);
        Assert.Empty(sim.Violations);

        Assert.All(
            sim.Chronicle.Events.OfType<RegionControlChangedEvent>(),
            e => Assert.Equal(CohesionSecessionSystem.SecessionReason, e.Reason));
    }

    [Fact]
    public void NoRulerSystemEmitsATerritorialOrStabilityEffect()
    {
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line([.. Enumerable.Repeat(10_000L, 12)]);
        PolityId realm = builder.Polity("Realm", 50, 50, 50, line);

        // Succession alone. Centuries of rulers of every ability, and nothing at all happens.
        Simulation sim = Simulation.Resume(
            Config,
            builder.World,
            [new RulerSuccessionSystem(), new PolityLifecycleSystem()]);

        sim.AdvanceYears(1500);

        Assert.True(sim.Chronicle.Events.OfType<RulerDeathEvent>().Count() > 20);
        Assert.Empty(sim.Chronicle.Events.OfType<RegionControlChangedEvent>());
        Assert.DoesNotContain(sim.Chronicle.Events.OfType<PolityFoundedEvent>(), e => e.Parent.IsSome);
        Assert.Empty(sim.Chronicle.Events.OfType<PolityDissolvedEvent>());
        Assert.Empty(sim.Chronicle.Events.OfType<PolityStabilityShiftEvent>());
        Assert.Equal(12, WorldQueries.RegionCountOf(sim.World, realm));
    }

    // ------------------------------------------------------------------ what it must not touch

    /// <summary>
    /// Expansion is untouched by the reach benefit, including under a brilliant administrator.
    /// </summary>
    /// <remarks>
    /// Expansion has its own reach penalty which this experiment was explicitly not to go near. Run
    /// with no cohesion system at all, the two rule sets have to produce the identical world.
    /// </remarks>
    [Fact]
    public void ExpansionIsUnchangedByTheReachBenefit()
    {
        Simulation Run(CohesionRules cohesion)
        {
            var builder = new WorldBuilder();
            long[] populations = [.. Enumerable.Repeat(40_000L, 8), .. Enumerable.Repeat(24_000L, 8)];
            RegionId[] line = builder.Line(populations);

            builder.Polity("Hegemon", 50, 100, 60, line[..8]);
            builder.Polity("Marches", 50, 20, 40, line[8..]);

            // Cohesion deliberately absent: only expansion can move a border here.
            Simulation sim = Simulation.Resume(
                Config,
                builder.World,
                [
                    new RulerSuccessionSystem(Immortal),
                    new OpportunisticExpansionSystem(ExpansionRules.Default),
                    new PolityLifecycleSystem(),
                ]);

            _ = cohesion;
            sim.AdvanceYears(500);
            return sim;
        }

        Assert.Equal(Run(FlatDistance).StateHash(), Run(Reach).StateHash());
    }

    [Fact]
    public void AdministrationStillHasNoExpansionEffect()
    {
        int Conquests(int administration)
        {
            var builder = new WorldBuilder();
            long[] populations = [.. Enumerable.Repeat(60_000L, 6), .. Enumerable.Repeat(20_000L, 6)];
            RegionId[] line = builder.Line(populations);

            PolityId hegemon = builder.Polity("Hegemon", 50, administration, 50, line[..6]);
            builder.Polity("Marches", 50, 50, 50, line[6..]);

            Simulation sim = Simulation.Resume(
                Config,
                builder.World,
                [
                    new RulerSuccessionSystem(Immortal),
                    new OpportunisticExpansionSystem(ExpansionRules.Default),
                    new PolityLifecycleSystem(),
                ]);

            sim.AdvanceYears(400);

            return sim.Chronicle.Events
                .OfType<RegionControlChangedEvent>()
                .Count(e => e.Reason == OpportunisticExpansionSystem.ExpansionReason && e.To == hegemon);
        }

        Assert.Equal(Conquests(0), Conquests(100));
    }

    /// <summary>
    /// Military ability still drives campaign tempo, and the reach benefit does not touch it.
    /// </summary>
    [Fact]
    public void MilitaryAbilityIsUnchangedByTheReachBenefit()
    {
        int Conquests(int military, CohesionRules cohesion)
        {
            var builder = new WorldBuilder();
            long[] populations = [.. Enumerable.Repeat(60_000L, 4), .. Enumerable.Repeat(20_000L, 8)];
            RegionId[] line = builder.Line(populations);

            PolityId hegemon = builder.Polity("Hegemon", 50, 100, military, line[..4]);
            builder.Polity("Marches", 50, 50, 50, line[4..]);

            Simulation sim = Simulation.Resume(
                Config,
                builder.World,
                [
                    new RulerSuccessionSystem(Immortal),
                    new OpportunisticExpansionSystem(ExpansionRules.Default),
                    new PolityLifecycleSystem(),
                ]);

            _ = cohesion;
            sim.AdvanceYears(300);

            return sim.Chronicle.Events
                .OfType<RegionControlChangedEvent>()
                .Count(e => e.Reason == OpportunisticExpansionSystem.ExpansionReason && e.To == hegemon);
        }

        Assert.Equal(Conquests(10, FlatDistance), Conquests(10, Reach));
        Assert.Equal(Conquests(95, FlatDistance), Conquests(95, Reach));
        Assert.True(Conquests(95, Reach) > Conquests(10, Reach), "military tempo stopped working");
    }

    // ------------------------------------------------------------------ still a valid simulation

    [Fact]
    public void LongRunsRemainDeterministicAndInvariant()
    {
        Simulation Run(CohesionRules cohesion) => Simulation.Create(
            SimulationConfig.Default with
            {
                // Seed chosen because the benefit changes this world's history. On many seeds it
                // does not: it retains provinces whose breakaway roll never came up anyway, so the
                // run ends on the same hash. That is a finding, not a defect - the assertion below
                // needs a seed where the modifier reaches the map, and it should be honest about
                // being one.
                Seed = 1,
                WorldWidth = 14,
                WorldHeight = 10,
                InitialPolityCount = 10,
                InvariantMode = InvariantMode.EveryTick,
                ThrowOnInvariantViolation = true,
            },
            DefaultSystems.Build(ExpansionRules.Default, cohesion, RulerRules.Default));

        Simulation first = Run(Reach);
        first.AdvanceYears(2000);

        Simulation second = Run(Reach);
        second.AdvanceYears(2000);

        Assert.Empty(first.Violations);
        Assert.Equal(first.StateHash(), second.StateHash());
        Assert.True(first.Chronicle.Events.OfType<RulerDeathEvent>().Count() > 200);

        for (int i = 0; i < first.Chronicle.Count; i++)
        {
            Assert.Equal(first.Chronicle.Events[i], second.Chronicle.Events[i]);
        }

        // And it is genuinely a different world from the shipped default, so the run above is not
        // quietly reproducing a disabled modifier. See the seed comment above: this is not true of
        // every seed, and the counterfactual test is what proves the mechanism fires in general.
        Simulation flat = Run(FlatDistance);
        flat.AdvanceYears(2000);
        Assert.NotEqual(first.StateHash(), flat.StateHash());
    }

    /// <summary>
    /// The counterfactual finds regions retained and never a single one exposed.
    /// </summary>
    /// <remarks>
    /// <para>This is the property the whole design turns on, and it is measured rather than
    /// assumed. The symmetric band it replaces exposed 100,558 region-years across a twenty-seed
    /// sweep; a benefit-only conversion must expose zero, because no ability makes the multiplier
    /// exceed 100%.</para>
    ///
    /// <para>Read through the same in-phase observer the batch runner uses, so what is asserted here
    /// is the same number the report prints.</para>
    /// </remarks>
    [Fact]
    public void TheCounterfactualExposesNoRegionAtAll()
    {
        var sink = new MechanismSink();

        var systems = new List<ISimulationSystem>(
            DefaultSystems.Build(ExpansionRules.Default, Reach, RulerRules.Default))
        {
            new MechanismObserverSystem(Reach, sink),
        };

        Simulation sim = Simulation.Create(
            SimulationConfig.Default with
            {
                Seed = 909_090,
                WorldWidth = 14,
                WorldHeight = 10,
                InitialPolityCount = 10,
                InvariantMode = InvariantMode.EveryTick,
                ThrowOnInvariantViolation = true,
            },
            systems);

        sim.AdvanceYears(600);

        Assert.Equal(0, sink.Distance.RegionsExposed);
        Assert.True(sink.Distance.RegionsRetained > 0, "the modifier never retained anything");
        Assert.True(sink.Distance.PolityYears > 1_000);

        // A benefit can only lower the strain-weighted multiplier, never raise it.
        DistanceMechanism measured = sink.Distance.Snapshot();
        Assert.InRange(measured.RealizedMultiplierPercent, 50, 100);
        Assert.Empty(sim.Violations);
    }

    [Fact]
    public void SaveAndReloadSurvivesTheReachBenefit()
    {
        Simulation sim = LongRealm(100, ReachOnly);
        sim.AdvanceYears(120);

        WorldState restoredWorld = SaveIO.Restore(SaveIO.Snapshot(sim.World));
        Simulation restored = Simulation.Resume(
            sim.Config,
            restoredWorld,
            [
                new RulerSuccessionSystem(Immortal),
                new CohesionSecessionSystem(ReachOnly),
                new PolityLifecycleSystem(),
            ]);

        Assert.Equal(sim.StateHash(), restored.StateHash());

        sim.AdvanceYears(120);
        restored.AdvanceYears(120);

        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Empty(restored.Violations);
    }
}
