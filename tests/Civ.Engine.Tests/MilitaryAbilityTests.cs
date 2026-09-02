using Civ.Engine.Config;
using Civ.Engine.Effects;
using Civ.Engine.Events;
using Civ.Engine.Persistence;
using Civ.Engine.State;
using Civ.Engine.Systems;
using Civ.Systems;

namespace Civ.Engine.Tests;

/// <summary>
/// The second ruler ability: campaign tempo, and nothing else.
/// </summary>
/// <remarks>
/// Military ability decides how quickly a polity acts on an opportunity the existing pressure
/// calculation has <i>already</i> judged viable. Most of these tests are about what it must not
/// touch - pressure, defence, reach, target selection, cohesion - because the administrative
/// experiment showed how easily a ruler modifier becomes a general bonus that explains nothing.
/// </remarks>
public sealed class MilitaryAbilityTests
{
    private static RulerRules Immortal => RulerRules.Default with
    {
        MortalityBasePermille = 0,
        MortalityRisePermillePerYear = 0,
        MaximumAge = 100_000,
    };

    private static CohesionRules NoFragmentation => CohesionRules.Default with
    {
        AdministrativeCapacity = 1_000_000,
        RulerCapacityFloorPercent = 100,
        RulerCapacityCeilingPercent = 100,
    };

    private static SimulationConfig Config => SimulationConfig.Default with
    {
        Seed = 777_777,
        InvariantMode = InvariantMode.EveryTick,
        ThrowOnInvariantViolation = true,
    };

    private static Simulation Generated(ulong seed, int years, int width = 12, int height = 8, int polities = 6)
    {
        Simulation sim = Simulation.Create(
            SimulationConfig.Default with
            {
                Seed = seed,
                WorldWidth = width,
                WorldHeight = height,
                InitialPolityCount = polities,
                InvariantMode = InvariantMode.Periodic,
            },
            DefaultSystems.Build());

        sim.AdvanceYears(years);
        return sim;
    }

    // ------------------------------------------------------------------ the ability itself

    [Fact]
    public void MilitaryAbilityIsGeneratedDeterministically()
    {
        Simulation first = Generated(seed: 4242, years: 600);
        Simulation second = Generated(seed: 4242, years: 600);

        Assert.Equal(first.World.Rulers.Count, second.World.Rulers.Count);

        foreach (Ruler ruler in first.World.Rulers.All())
        {
            Ruler other = second.World.Rulers.Get(ruler.Id);
            Assert.Equal(ruler.Military, other.Military);
            Assert.Equal(ruler.Administration, other.Administration);
        }

        Assert.Equal(first.StateHash(), second.StateHash());
    }

    [Fact]
    public void MilitaryAbilityIsCentredWithUncommonExtremes()
    {
        var abilities = Generated(seed: 5, years: 2000, width: 14, height: 10, polities: 10)
            .World.Rulers.All()
            .Select(r => r.Military)
            .ToList();

        Assert.True(abilities.Count > 500, $"expected a large sample, got {abilities.Count}");
        Assert.All(abilities, m => Assert.InRange(m, 0, 100));

        double mean = abilities.Average();
        Assert.InRange(mean, 47, 53);

        // Extremes are uncommon: the mean of three uniform draws puts very few rulers past 80.
        double extremes = 100.0 * abilities.Count(m => m is < 20 or > 80) / abilities.Count;
        Assert.InRange(extremes, 1, 15);

        // And the middle is where most of them are.
        double middle = 100.0 * abilities.Count(m => m is >= 40 and <= 59) / abilities.Count;
        Assert.True(middle > 35, $"only {middle:0.0}% of rulers fell in the middle band");
    }

    /// <summary>
    /// The two abilities are drawn independently.
    /// </summary>
    /// <remarks>
    /// If they were correlated, every "high military" ruler would also be a good administrator and
    /// the four ruler types the design is aiming for would collapse into two.
    /// </remarks>
    [Fact]
    public void MilitaryAbilityIsIndependentOfAdministration()
    {
        var rulers = Generated(seed: 6, years: 2000, width: 14, height: 10, polities: 10)
            .World.Rulers.All()
            .ToList();

        double meanA = rulers.Average(r => r.Administration);
        double meanM = rulers.Average(r => r.Military);
        double covariance = rulers.Average(r => (r.Administration - meanA) * (r.Military - meanM));
        double sdA = Math.Sqrt(rulers.Average(r => Math.Pow(r.Administration - meanA, 2)));
        double sdM = Math.Sqrt(rulers.Average(r => Math.Pow(r.Military - meanM, 2)));

        double correlation = covariance / (sdA * sdM);
        Assert.InRange(correlation, -0.12, 0.12);

        // All four combinations actually occur.
        Assert.Contains(rulers, r => r.Administration >= 65 && r.Military >= 65);
        Assert.Contains(rulers, r => r.Administration >= 65 && r.Military <= 35);
        Assert.Contains(rulers, r => r.Administration <= 35 && r.Military >= 65);
        Assert.Contains(rulers, r => r.Administration <= 35 && r.Military <= 35);
    }

    [Fact]
    public void MilitaryAbilityIsHashedAndPersisted()
    {
        WorldState Build(int military)
        {
            var builder = new WorldBuilder();
            RegionId[] line = builder.Line(10_000, 10_000);
            builder.Polity("Realm", 50, 50, military, line);
            return builder.World;
        }

        Assert.Equal(WorldHasher.Hash(Build(50)), WorldHasher.Hash(Build(50)));
        Assert.NotEqual(WorldHasher.Hash(Build(20)), WorldHasher.Hash(Build(80)));

        Simulation sim = Generated(seed: 8, years: 300);
        WorldState restored = SaveIO.Restore(SaveIO.Snapshot(sim.World));

        foreach (Ruler original in sim.World.Rulers.All())
        {
            Assert.Equal(original.Military, restored.Rulers.Get(original.Id).Military);
        }

        Assert.Equal(sim.StateHash(), WorldHasher.Hash(restored));
    }

    [Fact]
    public void AccessionEventsCarryMilitaryAbilityAndStillRender()
    {
        Simulation sim = Generated(seed: 9, years: 1200);

        RulerAccessionEvent early = sim.Chronicle.Events
            .OfType<RulerAccessionEvent>()
            .First(e => e.Year < 200);

        Ruler ruler = sim.World.Rulers.Get(early.Ruler);
        Assert.Equal(ruler.Military, early.Military);
        Assert.False(ruler.IsReigning);

        // Still renderable from the archived record alone, a millennium later.
        Assert.Contains(early.RulerName, early.Text, StringComparison.Ordinal);
        Assert.Contains($"military {early.Military}", early.Text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the tempo conversion

    [Fact]
    public void MilitaryFiftyReproducesTheBaseAttemptProbabilityExactly()
    {
        ExpansionRules rules = ExpansionRules.Default;

        Assert.Equal(100, rules.CampaignTempoPercent(rules.NeutralMilitary));

        for (int basePermille = 1; basePermille <= rules.MaxAttemptPermille; basePermille++)
        {
            Assert.Equal(basePermille, rules.CampaignPermille(basePermille, rules.NeutralMilitary));
        }
    }

    [Fact]
    public void TheTempoBandHitsItsEndpointsAndIsMonotonic()
    {
        ExpansionRules rules = ExpansionRules.Default;

        Assert.Equal(rules.MilitaryTempoAtWeakestPercent, rules.CampaignTempoPercent(0));
        Assert.Equal(100, rules.CampaignTempoPercent(50));
        Assert.Equal(rules.MilitaryTempoAtStrongestPercent, rules.CampaignTempoPercent(100));

        for (int ability = 1; ability <= 100; ability++)
        {
            Assert.True(rules.CampaignTempoPercent(ability) >= rules.CampaignTempoPercent(ability - 1));
        }

        Assert.Equal(rules.CampaignTempoPercent(0), rules.CampaignTempoPercent(-30));
        Assert.Equal(rules.CampaignTempoPercent(100), rules.CampaignTempoPercent(250));
    }

    /// <summary>
    /// The existing base ceiling must not swallow the bonus.
    /// </summary>
    /// <remarks>
    /// The base probability is already clamped to <c>MaxAttemptPermille</c>; re-applying that clamp
    /// after multiplying would leave a brilliant commander exactly as fast as an average one at every
    /// opportunity good enough to reach the ceiling - which is precisely where tempo should matter.
    /// </remarks>
    [Fact]
    public void TheBaseCeilingDoesNotEraseTheTempoBonus()
    {
        ExpansionRules rules = ExpansionRules.Default;
        int atCeiling = rules.MaxAttemptPermille;

        Assert.True(rules.CampaignPermille(atCeiling, 100) > atCeiling);
        Assert.Equal(
            atCeiling * rules.MilitaryTempoAtStrongestPercent / 100,
            rules.CampaignPermille(atCeiling, 100));
        Assert.True(rules.CampaignPermille(atCeiling, 0) < atCeiling);

        // The final cap keeps it a probability and nothing more.
        Assert.Equal(
            rules.MaxCampaignPermille,
            rules.CampaignPermille(rules.MaxCampaignPermille, 100));
    }

    // ------------------------------------------------------------------ in the simulation

    /// <summary>
    /// A realm facing a neighbour it can just about beat, with only the commander varying.
    /// </summary>
    /// <remarks>
    /// The target population is calibrated so pressure sits a little above <c>MinPressure</c>. Too
    /// weak a neighbour and every commander takes everything, which measures nothing; too strong and
    /// none of them can act at all.
    /// </remarks>
    private static Simulation Frontier(int military, ulong seed = 1, long targetPopulation = 32_000)
    {
        var builder = new WorldBuilder(seed);
        long[] populations = [.. Enumerable.Repeat(60_000L, 6), .. Enumerable.Repeat(targetPopulation, 6)];
        RegionId[] line = builder.Line(populations);

        builder.Polity("Hegemon", 50, 50, military, line[..6]);
        builder.Polity("Marches", 50, 50, 50, line[6..]);

        // No population growth and no fragmentation: the only variable is campaign tempo.
        return Simulation.Resume(
            Config,
            builder.World,
            [
                new RulerSuccessionSystem(Immortal),
                new CohesionSecessionSystem(NoFragmentation),
                new OpportunisticExpansionSystem(ExpansionRules.Default),
                new PolityLifecycleSystem(),
            ]);
    }

    private static int Conquests(Simulation sim) =>
        sim.Chronicle.Events
            .OfType<RegionControlChangedEvent>()
            .Count(e => e.Reason == OpportunisticExpansionSystem.ExpansionReason);

    /// <summary>Conquests made <i>by</i> one polity. Counting both directions measures the wrong thing.</summary>
    private static int ConquestsBy(Simulation sim, PolityId polity) =>
        sim.Chronicle.Events
            .OfType<RegionControlChangedEvent>()
            .Count(e => e.Reason == OpportunisticExpansionSystem.ExpansionReason && e.To == polity);

    private static PolityId Hegemon(Simulation sim) => sim.World.Polities.AllIds().First();

    /// <summary>
    /// A core facing a long march of small, independently weak neighbours.
    /// </summary>
    /// <remarks>
    /// Deliberately many targets. With only a handful, every commander either takes all of them or
    /// none, and the measurement collapses into a coin flip - the rate, which is the thing under
    /// test, becomes invisible behind saturation.
    /// </remarks>
    private static Simulation MarchOfStatelets(int military, ulong seed)
    {
        var builder = new WorldBuilder(seed);
        long[] populations = [.. Enumerable.Repeat(60_000L, 6), .. Enumerable.Repeat(30_000L, 20)];
        RegionId[] line = builder.Line(populations);

        builder.Polity("Hegemon", 50, 50, military, line[..6]);
        for (int i = 6; i < line.Length; i++)
        {
            builder.Polity($"Statelet {i}", 50, 50, 50, line[i]);
        }

        return Simulation.Resume(
            Config,
            builder.World,
            [
                new RulerSuccessionSystem(Immortal),
                new CohesionSecessionSystem(NoFragmentation),
                new OpportunisticExpansionSystem(ExpansionRules.Default),
                new PolityLifecycleSystem(),
            ]);
    }

    [Fact]
    public void AStrongCommanderActsMoreOftenThanAWeakOneInIdenticalSituations()
    {
        int Total(int military)
        {
            int total = 0;
            for (ulong seed = 1; seed <= 4; seed++)
            {
                Simulation sim = MarchOfStatelets(military, seed);
                sim.AdvanceYears(400);
                total += ConquestsBy(sim, Hegemon(sim));
                Assert.Empty(sim.Violations);
            }

            return total;
        }

        int strong = Total(100);
        int average = Total(50);
        int weak = Total(0);

        Assert.True(
            strong > average && average > weak,
            $"expected tempo ordering, saw strong {strong}, average {average}, weak {weak}");
    }

    /// <summary>
    /// Below the viability threshold, the finest commander in the world can do nothing.
    /// </summary>
    /// <remarks>
    /// The gate is upstream of tempo by construction, but this is the property most worth pinning:
    /// the modifier must never turn an impossible target into a possible one.
    /// </remarks>
    [Fact]
    public void MilitaryAbilityCannotTakeATargetThatFailsThePressureGate()
    {
        // Evenly matched neighbours: pressure never reaches MinPressure in either direction.
        Simulation strong = Frontier(military: 100, targetPopulation: 60_000);
        Simulation weak = Frontier(military: 0, targetPopulation: 60_000);

        strong.AdvanceYears(1000);
        weak.AdvanceYears(1000);

        // A thousand years of the finest commander available, and not one province changes hands.
        Assert.Equal(0, Conquests(strong));
        Assert.Equal(0, Conquests(weak));
        Assert.Equal(6, WorldQueries.RegionCountOf(strong.World, Hegemon(strong)));
        Assert.Empty(strong.Violations);
    }

    /// <summary>
    /// Tempo changes the timing, never the choice.
    /// </summary>
    /// <remarks>
    /// Every conquest a weak commander eventually makes is one a strong commander also makes, and in
    /// the same order - only sooner. A modifier that reordered the target list would be changing the
    /// pressure calculation through the back door.
    /// </remarks>
    [Fact]
    public void MilitaryAbilityDoesNotChangeTargetSelection()
    {
        Simulation strong = Frontier(military: 100);
        Simulation weak = Frontier(military: 0);

        strong.AdvanceYears(1200);
        weak.AdvanceYears(1200);

        static List<RegionId> Order(Simulation sim) =>
        [
            .. sim.Chronicle.Events
                .OfType<RegionControlChangedEvent>()
                .Where(e => e.Reason == OpportunisticExpansionSystem.ExpansionReason)
                .Select(e => e.Region),
        ];

        List<RegionId> strongOrder = Order(strong);
        List<RegionId> weakOrder = Order(weak);

        Assert.NotEmpty(weakOrder);
        Assert.Equal(weakOrder, strongOrder[..weakOrder.Count]);
    }

    [Fact]
    public void ChangingMilitaryAbilityEmitsNoImmediateTerritorialEffect()
    {
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(Enumerable.Repeat(10_000L, 8).ToArray());
        PolityId realm = builder.Polity("Realm", 50, 50, 100, line);

        // Succession only: no expansion, no cohesion. Commanders come and go and nothing moves.
        Simulation sim = Simulation.Resume(
            Config,
            builder.World,
            [new RulerSuccessionSystem(), new PolityLifecycleSystem()]);

        sim.AdvanceYears(1500);

        Assert.True(sim.Chronicle.Events.OfType<RulerDeathEvent>().Count() > 20);
        Assert.Contains(sim.World.Rulers.All(), r => r.Military >= 70);
        Assert.Empty(sim.Chronicle.Events.OfType<RegionControlChangedEvent>());
        Assert.Equal(8, WorldQueries.RegionCountOf(sim.World, realm));
        Assert.Empty(sim.Violations);
    }

    /// <summary>
    /// Administration still governs cohesion, and no longer touches expansion at all.
    /// </summary>
    [Fact]
    public void AdministrationAffectsCohesionButNotExpansion()
    {
        // Expansion: with the overextension band off by default, administration is inert.
        Simulation Expansion(int administration)
        {
            var builder = new WorldBuilder();
            long[] populations = [.. Enumerable.Repeat(60_000L, 6), .. Enumerable.Repeat(20_000L, 6)];
            RegionId[] line = builder.Line(populations);

            builder.Polity("Hegemon", 50, administration, 50, line[..6]);
            builder.Polity("Marches", 50, 50, 50, line[6..]);

            Simulation sim = Simulation.Resume(
                Config,
                builder.World,
                [
                    new RulerSuccessionSystem(Immortal),
                    new CohesionSecessionSystem(NoFragmentation),
                    new OpportunisticExpansionSystem(ExpansionRules.Default),
                    new PolityLifecycleSystem(),
                ]);

            sim.AdvanceYears(400);
            return sim;
        }

        Simulation lowAdmin = Expansion(0);
        Simulation highAdmin = Expansion(100);
        Assert.Equal(
            ConquestsBy(lowAdmin, Hegemon(lowAdmin)),
            ConquestsBy(highAdmin, Hegemon(highAdmin)));

        // Cohesion: administration still decides what can be held.
        CohesionRules cohesion = CohesionRules.Default with
        {
            AdministrativeCapacity = 1_700,
            DistanceStrainPerStep = 250,
            SizeStrainPerRegion = 30,
            ProsperityStrain = 0,
            // Deliberately exaggerated so a single succession is visible, and deliberately
            // centred on 100%: a band like 40/200 would hand every average ruler a 20% capacity
            // gift, which CohesionRules.Validate now refuses outright.
            RulerCapacityFloorPercent = 20,
            RulerCapacityCeilingPercent = 180,
            StrainPerPermille = 1,
            MaxAttemptPermille = 1000,
        };

        Simulation Cohesion(int administration)
        {
            var builder = new WorldBuilder();
            RegionId[] line = builder.Line(Enumerable.Repeat(10_000L, 11).ToArray());
            builder.Polity("Long Realm", 50, administration, 50, line);

            Simulation sim = Simulation.Resume(
                Config,
                builder.World,
                [new CohesionSecessionSystem(cohesion), new PolityLifecycleSystem()]);

            sim.AdvanceYears(120);
            return sim;
        }

        Assert.Single(WorldQueries.ActivePolities(Cohesion(100).World));
        Assert.True(WorldQueries.ActivePolities(Cohesion(0).World).Count() > 1);
    }

    [Fact]
    public void RepeatedSuccessionsPreserveInvariantsAndDeterminism()
    {
        Simulation Run() => Simulation.Create(
            SimulationConfig.Default with
            {
                Seed = 606_060,
                WorldWidth = 14,
                WorldHeight = 10,
                InitialPolityCount = 10,
                InvariantMode = InvariantMode.EveryTick,
                ThrowOnInvariantViolation = true,
            },
            DefaultSystems.Build());

        Simulation first = Run();
        first.AdvanceYears(2000);

        Simulation second = Run();
        second.AdvanceYears(2000);

        Assert.Empty(first.Violations);
        Assert.Equal(first.StateHash(), second.StateHash());
        Assert.True(first.Chronicle.Events.OfType<RulerDeathEvent>().Count() > 200);

        for (int i = 0; i < first.Chronicle.Count; i++)
        {
            Assert.Equal(first.Chronicle.Events[i], second.Chronicle.Events[i]);
        }
    }
}
