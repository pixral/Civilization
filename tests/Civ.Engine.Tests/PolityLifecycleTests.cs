using Civ.Engine.Config;
using Civ.Engine.Effects;
using Civ.Engine.Events;
using Civ.Engine.State;
using Civ.Engine.Systems;
using Civ.Systems;

namespace Civ.Engine.Tests;

/// <summary>
/// Proof that the architecture survives polities being created, destroyed, split and merged.
/// </summary>
/// <remarks>
/// <para>None of these transitions is driven by a real system yet - there is no war, no cohesion
/// model, no succession. They are driven here directly through the effect layer, which is the
/// point: the plumbing that will carry secession and collapse is testable before the politics that
/// will cause them exists.</para>
///
/// <para>This is the case that breaks simulations of this kind. Every one of these operations
/// invalidates references held elsewhere, and a single missed cascade leaves regions owned by
/// states that no longer exist. Every test here asserts invariants afterwards for that reason.</para>
/// </remarks>
public sealed class PolityLifecycleTests
{
    private static SimulationConfig Config => SimulationConfig.Default with
    {
        Seed = 808,
        WorldWidth = 6,
        WorldHeight = 4,
        InitialPolityCount = 3,
        InvariantMode = InvariantMode.EveryTick,
        ThrowOnInvariantViolation = true,
    };

    /// <summary>Emits its effects on one specific year, then stays quiet.</summary>
    private static ScriptedSystem Once(string name, SimulationPhase phase, int year, SystemAction action) =>
        new(name, phase, (in SystemContext ctx) =>
        {
            if (ctx.Year == year)
            {
                action(in ctx);
            }
        });

    [Fact]
    public void SecessionCreatesASuccessorStateAndLeavesTheWorldValid()
    {
        PolityId parent = default;

        var secession = Once("test.secession", SimulationPhase.Polity, 5, (in SystemContext ctx) =>
        {
            Polity target = WorldQueries.ActivePolities(ctx.World).First();
            parent = target.Id;

            // Half the parent's territory, chosen deterministically.
            List<RegionId> breakaway =
            [
                .. WorldQueries.RegionsOf(ctx.World, target.Id)
                    .OrderBy(r => r.Id.Index)
                    .Take(WorldQueries.RegionCountOf(ctx.World, target.Id) / 2)
                    .Select(r => r.Id),
            ];

            ctx.Effects.Emit(new FoundPolity(
                "Breakaway Republic", breakaway[0], target.Id, "secession", breakaway));
        });

        Simulation sim = Simulation.Create(Config, [secession, new PolityLifecycleSystem()]);
        int before = WorldQueries.ActivePolities(sim.World).Count();

        sim.AdvanceYears(10);

        Assert.Empty(sim.Violations);
        Assert.Equal(before + 1, WorldQueries.ActivePolities(sim.World).Count());

        Polity successor = sim.World.Polities.All().Single(p => p.Name == "Breakaway Republic");
        Assert.Equal(parent, successor.Parent);
        Assert.Equal(5, successor.FoundedYear);
        Assert.True(WorldQueries.RegionCountOf(sim.World, successor.Id) > 0);

        // The parent kept the rest rather than being silently replaced.
        Assert.True(sim.World.Polities.Get(parent).IsActive);

        // And the chronicle recorded it as a real event, from a real state change.
        PolityFoundedEvent founding = sim.Chronicle.Events.OfType<PolityFoundedEvent>()
            .Single(e => e.PolityName == "Breakaway Republic");
        Assert.Equal(parent, founding.Parent);
    }

    [Fact]
    public void DissolutionReleasesTerritoryAndRetiresThePolity()
    {
        PolityId doomed = default;

        var collapse = Once("test.collapse", SimulationPhase.Polity, 3, (in SystemContext ctx) =>
        {
            doomed = WorldQueries.ActivePolities(ctx.World).First().Id;
            ctx.Effects.Emit(new DissolvePolity(doomed, "test collapse"));
        });

        Simulation sim = Simulation.Create(Config, [collapse, new PolityLifecycleSystem()]);
        sim.AdvanceYears(6);

        Assert.Empty(sim.Violations);

        Polity dead = sim.World.Polities.Get(doomed);
        Assert.False(dead.IsActive);
        Assert.Equal(3, dead.DissolvedYear);
        Assert.True(dead.Capital.IsNone);
        Assert.Equal(0, WorldQueries.RegionCountOf(sim.World, doomed));

        // Its handle still resolves: dissolved polities are historical records, not deletions.
        Assert.True(sim.World.Polities.Contains(doomed));
        Assert.Contains(sim.Chronicle.Events.OfType<PolityDissolvedEvent>(), e => e.Polity == doomed);
    }

    [Fact]
    public void LosingEveryRegionDissolvesAPolityAutomatically()
    {
        PolityId victim = default;

        var conquest = Once("test.conquest", SimulationPhase.Diplomacy, 4, (in SystemContext ctx) =>
        {
            List<Polity> active = [.. WorldQueries.ActivePolities(ctx.World)];
            victim = active[0].Id;
            PolityId conqueror = active[1].Id;

            foreach (Region region in WorldQueries.RegionsOf(ctx.World, victim))
            {
                ctx.Effects.Emit(new SetRegionController(region.Id, conqueror, "annexation"));
            }
        });

        Simulation sim = Simulation.Create(Config, [conquest, new PolityLifecycleSystem()]);
        sim.AdvanceYears(6);

        Assert.Empty(sim.Violations);
        Assert.False(sim.World.Polities.Get(victim).IsActive);

        // Dissolved in the same year: the lifecycle system runs in Bookkeeping, after Diplomacy.
        Assert.Equal(4, sim.World.Polities.Get(victim).DissolvedYear);
    }

    [Fact]
    public void LosingTheCapitalReseatsThePolityRatherThanBreakingIt()
    {
        PolityId victim = default;
        RegionId oldSeat = default;

        var raid = Once("test.raid", SimulationPhase.Diplomacy, 4, (in SystemContext ctx) =>
        {
            List<Polity> active = [.. WorldQueries.ActivePolities(ctx.World)];
            victim = active[0].Id;
            oldSeat = active[0].Capital;

            ctx.Effects.Emit(new SetRegionController(oldSeat, active[1].Id, "capital sacked"));
        });

        Simulation sim = Simulation.Create(Config, [raid, new PolityLifecycleSystem()]);
        sim.AdvanceYears(8);

        Assert.Empty(sim.Violations);

        Polity survivor = sim.World.Polities.Get(victim);
        Assert.True(survivor.IsActive);
        Assert.NotEqual(oldSeat, survivor.Capital);
        Assert.Equal(victim, sim.World.Regions.Get(survivor.Capital).Controller);

        Assert.Contains(sim.Chronicle.Events.OfType<PolityCapitalMovedEvent>(), e => e.Polity == victim);
    }

    [Fact]
    public void MergingTwoPolitiesLeavesOneCoherentState()
    {
        PolityId absorbed = default;
        PolityId survivor = default;

        var union = Once("test.union", SimulationPhase.Diplomacy, 6, (in SystemContext ctx) =>
        {
            List<Polity> active = [.. WorldQueries.ActivePolities(ctx.World)];
            absorbed = active[0].Id;
            survivor = active[1].Id;

            foreach (Region region in WorldQueries.RegionsOf(ctx.World, absorbed))
            {
                ctx.Effects.Emit(new SetRegionController(region.Id, survivor, "personal union"));
            }
        });

        Simulation sim = Simulation.Create(Config, [union, new PolityLifecycleSystem()]);

        int regionsBefore = sim.World.Regions.Count;
        sim.AdvanceYears(10);

        Assert.Empty(sim.Violations);
        Assert.False(sim.World.Polities.Get(absorbed).IsActive);
        Assert.True(sim.World.Polities.Get(survivor).IsActive);

        // No territory was lost or duplicated in the process.
        Assert.Equal(regionsBefore, sim.World.Regions.Count);
    }

    /// <summary>
    /// The stress case: repeated splitting and collapse over centuries, with every invariant checked
    /// every tick.
    /// </summary>
    /// <remarks>
    /// Structural corruption in this area is cumulative and quiet - one orphaned region in year 200
    /// is invisible until something much later reads it. Churning the political graph hundreds of
    /// times is the cheapest way to find that class of bug now rather than in a thousand-year run.
    /// </remarks>
    [Fact]
    public void RepeatedSplittingAndCollapseNeverCorruptsTheWorld()
    {
        var churn = new ScriptedSystem("test.churn", SimulationPhase.Polity, (in SystemContext ctx) =>
        {
            List<Polity> active = [.. WorldQueries.ActivePolities(ctx.World)];
            if (active.Count == 0)
            {
                return;
            }

            Polity subject = active[ctx.Year % active.Count];
            List<Region> held = [.. WorldQueries.RegionsOf(ctx.World, subject.Id).OrderBy(r => r.Id.Index)];

            if (ctx.Year % 7 == 0 && held.Count >= 2)
            {
                List<RegionId> breakaway = [.. held.Take(held.Count / 2).Select(r => r.Id)];
                ctx.Effects.Emit(new FoundPolity(
                    $"Successor of {subject.Name} ({ctx.Year})",
                    breakaway[0],
                    subject.Id,
                    "fragmentation",
                    breakaway));
            }
            else if (ctx.Year % 11 == 0)
            {
                ctx.Effects.Emit(new DissolvePolity(subject.Id, "collapse"));
            }
            else if (ctx.Year % 5 == 0 && active.Count >= 2 && held.Count > 0)
            {
                PolityId neighbour = active[(ctx.Year + 1) % active.Count].Id;
                ctx.Effects.Emit(new SetRegionController(held[0].Id, neighbour, "annexation"));
            }
        });

        Simulation sim = Simulation.Create(Config, [churn, new PolityLifecycleSystem()]);
        sim.AdvanceYears(400);

        Assert.Empty(sim.Violations);

        // The churn really did rewrite the political graph, rather than quietly doing nothing.
        Assert.True(sim.World.Polities.Count > 3);
        Assert.Contains(sim.Chronicle.Events, e => e is PolityDissolvedEvent);
        Assert.Contains(sim.Chronicle.Events, e => e is PolityFoundedEvent { Reason: "fragmentation" });

        // And every surviving region points at something real.
        foreach (Region region in sim.World.Regions.All())
        {
            if (region.Controller.IsSome)
            {
                Assert.True(sim.World.Polities.Get(region.Controller).IsActive);
            }
        }
    }

    [Fact]
    public void EventsFromDissolvedPolitiesStillRenderCenturiesLater()
    {
        var collapse = Once("test.collapse", SimulationPhase.Polity, 2, (in SystemContext ctx) =>
            ctx.Effects.Emit(new DissolvePolity(
                WorldQueries.ActivePolities(ctx.World).First().Id, "test collapse")));

        Simulation sim = Simulation.Create(Config, [collapse, new PolityLifecycleSystem()]);
        sim.AdvanceYears(500);

        PolityDissolvedEvent dissolution = sim.Chronicle.Events.OfType<PolityDissolvedEvent>().First();

        // The name was snapshotted at the time. Nothing needs to resolve it against current state,
        // which is what stops a five-hundred-year-old entry from turning into "a forgotten power".
        Assert.False(string.IsNullOrWhiteSpace(dissolution.PolityName));
        Assert.Contains(dissolution.PolityName, dissolution.Text, StringComparison.Ordinal);
    }
}
