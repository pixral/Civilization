using Civ.Engine.Config;
using Civ.Engine.Events;
using Civ.Engine.State;
using Civ.Engine.Systems;
using Civ.Systems;

namespace Civ.Engine.Tests;

/// <summary>
/// Fragmentation: the counter-force that lets polity count rise as well as fall.
/// </summary>
/// <remarks>
/// <para>The scenario tests amplify one strain term at a time rather than raising a global "always
/// act" flag. A rule whose <i>shape</i> is state-derived has to be tested by changing the state that
/// drives it, so each rule set here makes one specific pressure overwhelming and leaves the rest
/// alone - which means the tests still assert that the right regions secede, not merely that
/// something did.</para>
///
/// <para><see cref="AStronglyGovernedPolityNeverFragments"/> is the deliberate mirror of
/// <see cref="TheUngovernablePeripherySecedesAsOneContiguousBlock"/>: identical world, identical
/// seed, only the administrative capacity differs, and the outcomes are opposite.</para>
/// </remarks>
public sealed class SecessionTests
{
    /// <summary>Disconnection alone made overwhelming; connected land keeps its ordinary strain.</summary>
    private static CohesionRules CertainDisconnection => CohesionRules.Default with
    {
        DisconnectionStrain = 2_000,
        StrainPerPermille = 1,
        MaxAttemptPermille = 1000,
    };

    /// <summary>
    /// Distance made overwhelming, with authority raised to match so that near provinces are still
    /// comfortably governable and only the far end of a long realm is not.
    /// </summary>
    private static CohesionRules CertainDistance => CohesionRules.Default with
    {
        DistanceStrainPerStep = 400,
        AdministrativeCapacity = 1_500,
        StrainPerPermille = 1,
        MaxAttemptPermille = 1000,
    };

    /// <summary>Enough administrative capacity to absorb any strain this world can generate.</summary>
    private static CohesionRules StrongCohesion => CohesionRules.Default with
    {
        AdministrativeCapacity = 1_000_000,
    };

    private static SimulationConfig Config => SimulationConfig.Default with
    {
        Seed = 4711,
        InvariantMode = InvariantMode.EveryTick,
        ThrowOnInvariantViolation = true,
    };

    /// <summary>Cohesion in isolation, with only the caretaker alongside it.</summary>
    private static Simulation Fragmenting(WorldState world, CohesionRules rules) =>
        Simulation.Resume(
            Config,
            world,
            [new CohesionSecessionSystem(rules), new PolityLifecycleSystem()]);

    private static IEnumerable<PolityFoundedEvent> Secessions(Simulation sim) =>
        sim.Chronicle.Events
            .OfType<PolityFoundedEvent>()
            .Where(e => e.Reason == CohesionSecessionSystem.SecessionReason);

    /// <summary>A conquest in which a parent state takes territory back from its own breakaway.</summary>
    private static IEnumerable<RegionControlChangedEvent> Reconquests(Simulation sim) =>
        sim.Chronicle.Events
            .OfType<RegionControlChangedEvent>()
            .Where(e => e.Reason == OpportunisticExpansionSystem.ExpansionReason)
            .Where(e => e.From.IsSome
                && sim.World.Polities.TryGet(e.From, out Polity? loser)
                && loser.Parent.Equals(e.To));

    [Fact]
    public void ADisconnectedExclaveBreaksAway()
    {
        // A holds both ends of the line; B holds the middle, so A's far province has no route home.
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(10_000, 10_000, 10_000);
        PolityId split = builder.Polity("Sundered Realm", 50, line[0], line[2]);
        builder.Polity("Wedge", 50, line[1]);

        Simulation sim = Fragmenting(builder.World, CertainDisconnection);
        sim.AdvanceYear();

        PolityFoundedEvent secession = Assert.Single(Secessions(sim));
        Assert.Equal(split, secession.Parent);
        Assert.Equal("Sundered Realm", secession.ParentName);
        Assert.Equal(1, secession.Regions);

        Polity successor = sim.World.Polities.Get(secession.Polity);
        Assert.Equal(line[2], successor.Capital);
        Assert.Equal(successor.Id, sim.World.Regions.Get(line[2]).Controller);

        // The parent keeps its core and its seat.
        Polity parent = sim.World.Polities.Get(split);
        Assert.True(parent.IsActive);
        Assert.Equal(line[0], parent.Capital);
        Assert.Equal(1, WorldQueries.RegionCountOf(sim.World, split));
        Assert.Empty(sim.Violations);
    }

    [Fact]
    public void TheUngovernablePeripherySecedesAsOneContiguousBlock()
    {
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(10_000, 10_000, 10_000, 10_000, 10_000, 10_000, 10_000, 10_000);
        PolityId realm = builder.Polity("Long Realm", 50, line);

        Simulation sim = Fragmenting(builder.World, CertainDistance);
        sim.AdvanceYear();

        PolityFoundedEvent secession = Assert.Single(Secessions(sim));
        PolityId successor = secession.Polity;

        // The far half leaves together; the governable near half does not.
        var seceded = WorldQueries.RegionsOf(sim.World, successor).Select(r => r.Id).ToHashSet();
        Assert.Equal([line[4], line[5], line[6], line[7]], seceded.Order().ToList());
        Assert.Equal(4, secession.Regions);

        var retained = WorldQueries.RegionsOf(sim.World, realm).Select(r => r.Id).ToHashSet();
        Assert.Equal([line[0], line[1], line[2], line[3]], retained.Order().ToList());

        // Contiguous: every seceded region touches another one, so this is a province and not a
        // scattering of cells that happened to score highly.
        foreach (RegionId id in seceded)
        {
            Assert.Contains(sim.World.Regions.Get(id).Neighbors, n => seceded.Contains(n));
        }

        Assert.Equal(line[0], sim.World.Polities.Get(realm).Capital);
        Assert.Empty(sim.Violations);
    }

    [Fact]
    public void AStronglyGovernedPolityNeverFragments()
    {
        // Same world and seed as the test above; only administrative capacity differs.
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(10_000, 10_000, 10_000, 10_000, 10_000, 10_000, 10_000, 10_000);
        PolityId realm = builder.Polity("Long Realm", 50, line);

        Simulation sim = Fragmenting(builder.World, StrongCohesion);
        sim.AdvanceYears(500);

        Assert.Empty(Secessions(sim));
        Assert.Single(WorldQueries.ActivePolities(sim.World));
        Assert.Equal(8, WorldQueries.RegionCountOf(sim.World, realm));
        Assert.Empty(sim.Violations);
    }

    [Fact]
    public void ASuccessorStateIsSeatedInItsOwnTerritoryAndRecordsItsLineage()
    {
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(10_000, 10_000, 10_000, 10_000, 10_000, 10_000, 10_000, 90_000);
        PolityId realm = builder.Polity("Long Realm", 50, line);

        Simulation sim = Fragmenting(builder.World, CertainDistance);
        sim.AdvanceYear();

        Polity successor = sim.World.Polities.All().Single(p => p.Parent.Equals(realm));

        // Seat goes to the most populous region of the breakaway, and it must be territory the new
        // state actually holds - the invariant that a capital is owned has no other guard here.
        Assert.Equal(line[7], successor.Capital);
        Assert.Equal(successor.Id, sim.World.Regions.Get(successor.Capital).Controller);

        Assert.Equal(realm, successor.Parent);
        Assert.Equal(1, successor.FoundedYear);
        Assert.True(sim.World.Polities.Get(successor.Parent).IsActive);
        Assert.Empty(sim.Violations);
    }

    [Fact]
    public void SecessionCostsTheParentStability()
    {
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(10_000, 10_000, 10_000, 10_000, 10_000, 10_000, 10_000, 10_000);
        PolityId realm = builder.Polity("Long Realm", 50, line);

        Simulation sim = Fragmenting(builder.World, CertainDistance);
        sim.AdvanceYear();

        Assert.True(sim.World.Polities.Get(realm).Stability < 50);
    }

    [Fact]
    public void SecessionIsDeterministic()
    {
        Simulation Run()
        {
            Simulation sim = Simulation.Create(
                SimulationConfig.Default with
                {
                    Seed = 5150,
                    WorldWidth = 12,
                    WorldHeight = 8,
                    InitialPolityCount = 6,
                    InvariantMode = InvariantMode.Periodic,
                },
                DefaultSystems.Build());

            sim.AdvanceYears(800);
            return sim;
        }

        Simulation first = Run();
        Simulation second = Run();

        Assert.Equal(first.StateHash(), second.StateHash());
        Assert.Equal(first.Chronicle.Count, second.Chronicle.Count);

        for (int i = 0; i < first.Chronicle.Count; i++)
        {
            Assert.Equal(first.Chronicle.Events[i], second.Chronicle.Events[i]);
        }

        Assert.NotEmpty(Secessions(first));
    }

    /// <summary>
    /// Secession and conquest acting on the same territory.
    /// </summary>
    /// <remarks>
    /// Cohesion runs in the Polity phase and expansion in Diplomacy, so a breakaway state is live and
    /// vulnerable in the same year it is created. A parent taking its lost province back is the
    /// clearest evidence that the two systems are genuinely coupled rather than running past each
    /// other, and it is the cycle that keeps polity count oscillating instead of drifting one way.
    /// </remarks>
    [Fact]
    public void BreakawayStatesAreFoughtOverByTheirFormerParents()
    {
        int reconquests = 0;
        int seceded = 0;

        for (ulong seed = 1; seed <= 6; seed++)
        {
            Simulation sim = Simulation.Create(
                SimulationConfig.Default with
                {
                    Seed = seed,
                    WorldWidth = 12,
                    WorldHeight = 8,
                    InitialPolityCount = 6,
                    InvariantMode = InvariantMode.Periodic,
                },
                DefaultSystems.Build());

            sim.AdvanceYears(1500);

            seceded += Secessions(sim).Count();
            reconquests += Reconquests(sim).Count();
            Assert.Empty(sim.Violations);
        }

        Assert.True(seceded > 0, "expected breakaway states across six 1500-year runs");
        Assert.True(reconquests > 0, "expected at least one parent to retake territory from its own breakaway");
    }

    [Fact]
    public void RepeatedSplitAndConquerCyclesNeverCorruptTheWorld()
    {
        Simulation sim = Simulation.Create(
            SimulationConfig.Default with
            {
                Seed = 271828,
                WorldWidth = 14,
                WorldHeight = 10,
                InitialPolityCount = 10,
                InvariantMode = InvariantMode.EveryTick,
                ThrowOnInvariantViolation = true,
            },
            DefaultSystems.Build());

        sim.AdvanceYears(2000);

        Assert.Empty(sim.Violations);

        // Both directions actually happened: the political graph grew and shrank.
        Assert.NotEmpty(Secessions(sim));
        Assert.Contains(sim.Chronicle.Events, e => e is PolityDissolvedEvent);
        Assert.True(sim.World.Polities.Count > 10, "expected new states beyond the ten founded at worldgen");

        // Every structural reference still resolves after two thousand years of churn.
        foreach (Region region in sim.World.Regions.All())
        {
            if (region.Controller.IsSome)
            {
                Assert.True(sim.World.Polities.Get(region.Controller).IsActive);
            }
        }

        foreach (Polity polity in WorldQueries.ActivePolities(sim.World))
        {
            Assert.Equal(polity.Id, sim.World.Regions.Get(polity.Capital).Controller);

            if (polity.Parent.IsSome)
            {
                Assert.True(sim.World.Polities.Contains(polity.Parent));
            }
        }
    }

    [Fact]
    public void ASinglePolityWorldCannotSecedeItsCapital()
    {
        // The guarantee the whole design leans on: the seat never leaves, so a secession can never
        // strand a parent without territory or without a capital it controls.
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(10_000, 10_000);
        PolityId realm = builder.Polity("Dyad", 50, line);

        Simulation sim = Fragmenting(builder.World, CertainDistance with { AdministrativeCapacity = 0 });
        sim.AdvanceYears(50);

        Polity parent = sim.World.Polities.Get(realm);
        Assert.True(parent.IsActive);
        Assert.Equal(line[0], parent.Capital);
        Assert.Equal(realm, sim.World.Regions.Get(line[0]).Controller);
        Assert.Empty(sim.Violations);
    }
}
