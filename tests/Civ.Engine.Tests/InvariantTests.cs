using Civ.Engine.Config;
using Civ.Engine.Events;
using Civ.Engine.Invariants;
using Civ.Engine.State;
using Civ.Engine.Systems;
using Civ.Engine.Worldgen;
using Civ.Systems;

namespace Civ.Engine.Tests;

/// <summary>
/// Automatic detection of invalid state.
/// </summary>
/// <remarks>
/// Each test builds a world broken in one specific way and asserts the matching invariant notices.
/// Constructing corrupt state directly is only possible because the test assembly has internals
/// access; nothing outside <c>Civ.Engine</c> can reach these setters, which is exactly why systems
/// cannot cause this kind of damage in the first place.
/// </remarks>
public sealed class InvariantTests
{
    private static WorldState BuildWorld() => WorldGenerator.Generate(
        SimulationConfig.Default with
        {
            Seed = 31,
            WorldWidth = 4,
            WorldHeight = 3,
            InitialPolityCount = 2,
        },
        new Chronicle());

    private static List<InvariantViolation> Check(WorldState world)
    {
        var violations = new List<InvariantViolation>();
        new InvariantChecker(CoreInvariants.All).Run(world, violations);
        return violations;
    }

    [Fact]
    public void FreshlyGeneratedWorldIsValid() => Assert.Empty(Check(BuildWorld()));

    [Fact]
    public void LongRunProducesNoViolations()
    {
        // The real regression test: every invariant, every tick, over a run long enough that slow
        // drift would surface.
        Simulation sim = Simulation.Create(
            SimulationConfig.Default with
            {
                Seed = 2024,
                WorldWidth = 8,
                WorldHeight = 6,
                InitialPolityCount = 6,
                InvariantMode = InvariantMode.EveryTick,
                ThrowOnInvariantViolation = true,
            },
            DefaultSystems.Build());

        sim.AdvanceYears(1000);

        Assert.Empty(sim.Violations);
    }

    [Fact]
    public void DetectsRegionHeldByDefunctPolity()
    {
        WorldState world = BuildWorld();
        Polity victim = world.Polities.All().First();
        victim.Status = PolityStatus.Defunct;
        victim.DissolvedYear = world.Year;
        victim.Capital = RegionId.None;

        Assert.Contains(Check(world), v => v.Invariant == nameof(CoreInvariants.RegionControllerIsLive));
    }

    [Fact]
    public void DetectsRegionHeldByNonExistentPolity()
    {
        WorldState world = BuildWorld();
        world.Regions.All().First().Controller = new PolityId(404, 3);

        Assert.Contains(Check(world), v => v.Invariant == nameof(CoreInvariants.RegionControllerIsLive));
    }

    [Fact]
    public void DetectsLandlessActivePolity()
    {
        WorldState world = BuildWorld();
        Polity orphan = world.Polities.All().First();

        foreach (Region region in WorldQueries.RegionsOf(world, orphan.Id).ToList())
        {
            region.Controller = PolityId.None;
        }

        Assert.Contains(Check(world), v => v.Invariant == nameof(CoreInvariants.ActivePolityHoldsTerritory));
    }

    [Fact]
    public void DetectsCapitalNotControlledByItsPolity()
    {
        WorldState world = BuildWorld();
        Polity polity = world.Polities.All().First();
        world.Regions.Get(polity.Capital).Controller = world.Polities.All().Last().Id;

        Assert.Contains(Check(world), v => v.Invariant == nameof(CoreInvariants.PolityCapitalIsOwned));
    }

    [Fact]
    public void DetectsNegativePopulation()
    {
        WorldState world = BuildWorld();
        world.Regions.All().First().Population = -1;

        Assert.Contains(Check(world), v => v.Invariant == nameof(CoreInvariants.PopulationIsNonNegative));
    }

    [Fact]
    public void DetectsAsymmetricAdjacency()
    {
        WorldState world = BuildWorld();
        world.Regions.All().First().NeighborList.Add(new RegionId(11, 1));

        Assert.Contains(Check(world), v => v.Invariant == nameof(CoreInvariants.AdjacencyIsSymmetric));
    }

    [Fact]
    public void DetectsDefunctPolityStillSeated()
    {
        WorldState world = BuildWorld();
        Polity polity = world.Polities.All().First();
        PolityId survivor = world.Polities.All().Last().Id;

        foreach (Region region in WorldQueries.RegionsOf(world, polity.Id).ToList())
        {
            region.Controller = survivor;
        }

        polity.Status = PolityStatus.Defunct;
        polity.DissolvedYear = world.Year;
        // Capital deliberately left set.

        Assert.Contains(Check(world), v => v.Invariant == nameof(CoreInvariants.DefunctPolityIsInert));
    }

    [Fact]
    public void ViolationsCanBeConfiguredToBeFatal()
    {
        Simulation sim = Simulation.Create(
            SimulationConfig.Default with
            {
                Seed = 5,
                InvariantMode = InvariantMode.EveryTick,
                ThrowOnInvariantViolation = true,
            },
            [new StateCorruptingSystem()]);

        Assert.Throws<InvariantViolationException>(() => sim.AdvanceYear());
    }

    [Fact]
    public void ViolationsAccumulateWhenNotFatal()
    {
        Simulation sim = Simulation.Create(
            SimulationConfig.Default with
            {
                Seed = 5,
                InvariantMode = InvariantMode.EveryTick,
                ThrowOnInvariantViolation = false,
            },
            [new StateCorruptingSystem()]);

        sim.AdvanceYears(3);

        Assert.NotEmpty(sim.Violations);
        Assert.All(sim.Violations, v =>
            Assert.Equal(nameof(CoreInvariants.PopulationIsNonNegative), v.Invariant));
    }

    [Fact]
    public void PeriodicModeChecksOnlyOnItsInterval()
    {
        var checker = new InvariantChecker(CoreInvariants.All);

        Assert.False(checker.ShouldRun(InvariantMode.Off, 10, 10));
        Assert.True(checker.ShouldRun(InvariantMode.EveryTick, 10, 7));
        Assert.True(checker.ShouldRun(InvariantMode.Periodic, 10, 20));
        Assert.False(checker.ShouldRun(InvariantMode.Periodic, 10, 21));
    }

    /// <summary>
    /// Writes directly to state, bypassing the effect layer.
    /// </summary>
    /// <remarks>
    /// This only compiles because the test assembly has internals access. A real system in
    /// <c>Civ.Systems</c> cannot do it - that is the point of the assembly split - so it stands in
    /// for corruption an engine-level bug could introduce.
    /// </remarks>
    private sealed class StateCorruptingSystem : ISimulationSystem
    {
        public string Name => "test.direct_corruption";

        public SimulationPhase Phase => SimulationPhase.Bookkeeping;

        public void Execute(in SystemContext context) =>
            context.World.Regions.All().First().Population = -100;
    }
}
