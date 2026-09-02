using Civ.Engine.Config;
using Civ.Engine.Events;
using Civ.Engine.State;
using Civ.Engine.Systems;
using Civ.Systems;

namespace Civ.Engine.Tests;

/// <summary>
/// The first system that rewrites political borders.
/// </summary>
/// <remarks>
/// These tests are about the <i>plumbing</i>, not the rule. What matters is that a control transfer
/// through the effect layer leaves the world coherent: capitals stay owned, landless states are
/// retired, dissolved states keep resolving, and every reported event corresponds to a change that
/// actually happened. The rule itself is a placeholder and will be replaced.
/// </remarks>
public sealed class ExpansionTests
{
    /// <summary>
    /// Acts immediately whenever pressure clears the threshold, so scenarios resolve on a known
    /// year instead of an expected one. The threshold itself is untouched - these tests still have
    /// to earn their conquests through state, they just do not wait for the dice.
    /// </summary>
    private static ExpansionRules Certain => ExpansionRules.Default with
    {
        MaxAttemptPermille = 1000,
        PressurePerPermille = 1,
    };

    private static SimulationConfig Config => SimulationConfig.Default with
    {
        Seed = 90210,
        InvariantMode = InvariantMode.EveryTick,
        ThrowOnInvariantViolation = true,
    };

    private static Simulation Resume(WorldState world, ExpansionRules rules) =>
        Simulation.Resume(Config, world, DefaultSystems.Build(rules));

    private static IEnumerable<RegionControlChangedEvent> Conquests(Simulation sim) =>
        sim.Chronicle.Events
            .OfType<RegionControlChangedEvent>()
            .Where(e => e.Reason == OpportunisticExpansionSystem.ExpansionReason);

    [Fact]
    public void AStrongerNeighbourTakesTheRegionAndItIsRecorded()
    {
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(120_000, 2_000, 2_000);
        PolityId strong = builder.Polity("Hegemon", 50, line[0]);
        PolityId weak = builder.Polity("Marches", 50, line[1], line[2]);

        Simulation sim = Resume(builder.World, Certain);
        sim.AdvanceYear();

        Assert.Equal(strong, sim.World.Regions.Get(line[1]).Controller);
        Assert.True(sim.World.Polities.Get(weak).IsActive);
        Assert.Empty(sim.Violations);

        RegionControlChangedEvent conquest = Assert.Single(Conquests(sim));
        Assert.Equal(line[1], conquest.Region);
        Assert.Equal(weak, conquest.From);
        Assert.Equal(strong, conquest.To);
        Assert.Equal(1, conquest.Year);
    }

    /// <summary>
    /// Expansion is gated on state, not on a die roll: with no local superiority, nothing happens
    /// however long the simulation runs.
    /// </summary>
    [Fact]
    public void EvenlyMatchedNeighboursNeverExpand()
    {
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(20_000, 20_000, 20_000, 20_000);
        for (int i = 0; i < line.Length; i++)
        {
            builder.Polity($"Peer {i}", 50, line[i]);
        }

        Simulation sim = Resume(builder.World, Certain);
        sim.AdvanceYears(300);

        Assert.Empty(Conquests(sim));
        Assert.Equal(4, WorldQueries.ActivePolities(sim.World).Count());
        Assert.Empty(sim.Violations);
    }

    [Fact]
    public void LosingTheCapitalReseatsTheVictimInTheSameYear()
    {
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(120_000, 2_000, 2_000);
        PolityId strong = builder.Polity("Hegemon", 50, line[0]);

        // Seated at the frontier region, which is exactly the one that will fall.
        PolityId victim = builder.Polity("Marches", 50, line[1], line[2]);

        Simulation sim = Resume(builder.World, Certain);
        Assert.Equal(line[1], sim.World.Polities.Get(victim).Capital);

        sim.AdvanceYear();

        Polity survivor = sim.World.Polities.Get(victim);
        Assert.True(survivor.IsActive);
        Assert.Equal(line[2], survivor.Capital);
        Assert.Equal(victim, sim.World.Regions.Get(survivor.Capital).Controller);
        Assert.Equal(strong, sim.World.Regions.Get(line[1]).Controller);
        Assert.Empty(sim.Violations);

        PolityCapitalMovedEvent moved = Assert.Single(
            sim.Chronicle.Events.OfType<PolityCapitalMovedEvent>());
        Assert.Equal(victim, moved.Polity);
        Assert.Equal(line[1], moved.From);
        Assert.Equal(line[2], moved.To);
    }

    [Fact]
    public void LosingTheLastRegionDissolvesTheVictimInTheSameYear()
    {
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(120_000, 2_000);
        PolityId strong = builder.Polity("Hegemon", 50, line[0]);
        PolityId doomed = builder.Polity("Remnant", 50, line[1]);

        Simulation sim = Resume(builder.World, Certain);
        sim.AdvanceYear();

        Polity dead = sim.World.Polities.Get(doomed);
        Assert.False(dead.IsActive);
        Assert.Equal(1, dead.DissolvedYear);
        Assert.True(dead.Capital.IsNone);
        Assert.Equal(strong, sim.World.Regions.Get(line[1]).Controller);
        Assert.Empty(sim.Violations);

        // The handle still resolves: dissolution is retirement, not deletion.
        Assert.True(sim.World.Polities.Contains(doomed));
        Assert.Contains(sim.Chronicle.Events.OfType<PolityDissolvedEvent>(), e => e.Polity == doomed);
    }

    /// <summary>
    /// Two polities naming the same region in the same phase.
    /// </summary>
    /// <remarks>
    /// The case an absolute write cannot compose its way out of. The applier resolves it by pipeline
    /// order and records an <c>EffectConflict</c>; what must never happen is a region ending up with
    /// two owners, or a conflict disappearing without trace.
    /// </remarks>
    [Fact]
    public void TwoPolitiesClaimingOneRegionResolveToASingleController()
    {
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(120_000, 1_000, 120_000);
        PolityId west = builder.Polity("West", 50, line[0]);
        PolityId east = builder.Polity("East", 50, line[2]);
        PolityId buffer = builder.Polity("Buffer", 50, line[1]);

        Simulation sim = Resume(builder.World, Certain);
        sim.AdvanceYear();

        PolityId owner = sim.World.Regions.Get(line[1]).Controller;
        Assert.True(owner == west || owner == east);
        Assert.False(sim.World.Polities.Get(buffer).IsActive);
        Assert.Empty(sim.Violations);

        // Both claims were emitted; exactly one was applied and the other was reported.
        Civ.Engine.Effects.EffectConflict conflict = Assert.Single(sim.Conflicts);
        Assert.Equal(1, conflict.Year);
        Assert.Equal(nameof(SimulationPhase.Diplomacy), conflict.Phase);
        Assert.Contains("Controller", conflict.Field, StringComparison.Ordinal);

        // And the chronicle reports one transfer, not two.
        Assert.Single(Conquests(sim), e => e.Region == line[1]);
    }

    [Fact]
    public void ConquestCostsStabilityOnBothSides()
    {
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(120_000, 2_000, 2_000);
        PolityId strong = builder.Polity("Hegemon", 50, line[0]);
        PolityId victim = builder.Polity("Marches", 50, line[1], line[2]);

        Simulation sim = Resume(builder.World, Certain);
        sim.AdvanceYear();

        Assert.True(sim.World.Polities.Get(strong).Stability < 50);
        Assert.True(sim.World.Polities.Get(victim).Stability < 50);
    }

    [Fact]
    public void APolityThatDoesNotExpandRecoversTowardNeutralButNoFurther()
    {
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(20_000, 20_000);
        PolityId a = builder.Polity("A", 10, line[0]);
        PolityId b = builder.Polity("B", 50, line[1]);

        Simulation sim = Resume(builder.World, Certain);
        sim.AdvanceYears(200);

        Assert.Equal(50, sim.World.Polities.Get(a).Stability);
        Assert.Equal(50, sim.World.Polities.Get(b).Stability);
    }

    [Fact]
    public void ExpansionIsDeterministic()
    {
        Simulation Run()
        {
            Simulation sim = Simulation.Create(
                SimulationConfig.Default with
                {
                    Seed = 616,
                    WorldWidth = 10,
                    WorldHeight = 8,
                    InitialPolityCount = 8,
                    InvariantMode = InvariantMode.Periodic,
                },
                DefaultSystems.Build());

            sim.AdvanceYears(600);
            return sim;
        }

        Simulation first = Run();
        Simulation second = Run();

        Assert.Equal(first.StateHash(), second.StateHash());
        Assert.Equal(first.Chronicle.Count, second.Chronicle.Count);
        Assert.True(Conquests(first).Any(), "the default rule should actually move borders");
    }

    /// <summary>
    /// The long-horizon guarantee: borders churn for a thousand years and the world stays coherent.
    /// </summary>
    [Fact]
    public void ACenturiesLongRunWithBorderChurnNeverCorruptsTheWorld()
    {
        Simulation sim = Simulation.Create(
            SimulationConfig.Default with
            {
                Seed = 31337,
                WorldWidth = 12,
                WorldHeight = 8,
                InitialPolityCount = 10,
                InvariantMode = InvariantMode.EveryTick,
                ThrowOnInvariantViolation = true,
            },
            DefaultSystems.Build());

        sim.AdvanceYears(1000);

        Assert.Empty(sim.Violations);
        Assert.True(Conquests(sim).Count() > 10, "expected sustained border movement");
        Assert.Contains(sim.Chronicle.Events, e => e is PolityDissolvedEvent);

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
        }
    }
}
