using Civ.Engine.Config;
using Civ.Engine.Effects;
using Civ.Engine.State;
using Civ.Engine.Systems;
using Civ.Systems;

namespace Civ.Engine.Tests;

/// <summary>
/// The contract between systems and state: read a frozen snapshot, emit effects, and let the
/// applier decide what actually happens.
/// </summary>
public sealed class EffectPipelineTests
{
    private static SimulationConfig Config => SimulationConfig.Default with
    {
        Seed = 1234,
        WorldWidth = 4,
        WorldHeight = 3,
        InitialPolityCount = 2,
        ThrowOnInvariantViolation = true,
    };

    /// <summary>
    /// Two systems in the same phase see identical state, whatever order they run in.
    /// </summary>
    /// <remarks>
    /// This is what makes systems within a phase independently reorderable and, eventually, safely
    /// parallelisable. If an observer here saw the other system's write, every system would
    /// implicitly depend on pipeline position.
    /// </remarks>
    [Fact]
    public void SystemsInTheSamePhaseCannotSeeEachOthersEffects()
    {
        long observed = -1;
        RegionId target = default;

        var writer = new ScriptedSystem("test.writer", SimulationPhase.Population, (in SystemContext ctx) =>
        {
            target = ctx.World.Regions.AllIds().First();
            ctx.Effects.Emit(new AdjustRegionPopulation(target, 1_000_000, "test"));
        });

        var reader = new ScriptedSystem("test.reader", SimulationPhase.Population, (in SystemContext ctx) =>
        {
            observed = ctx.World.Regions.All().First().Population;
        });

        Simulation sim = Simulation.Create(Config, [writer, reader]);
        long before = sim.World.Regions.All().First().Population;

        sim.AdvanceYear();

        Assert.Equal(before, observed);
        Assert.Equal(before + 1_000_000, sim.World.Regions.Get(target).Population);
    }

    [Fact]
    public void LaterPhasesSeeEarlierPhaseEffects()
    {
        long observed = -1;

        var writer = new ScriptedSystem("test.writer", SimulationPhase.Population, (in SystemContext ctx) =>
            ctx.Effects.Emit(new AdjustRegionPopulation(
                ctx.World.Regions.AllIds().First(), 500_000, "test")));

        var reader = new ScriptedSystem("test.reader", SimulationPhase.Bookkeeping, (in SystemContext ctx) =>
            observed = ctx.World.Regions.All().First().Population);

        Simulation sim = Simulation.Create(Config, [writer, reader]);
        long before = sim.World.Regions.All().First().Population;

        sim.AdvanceYear();

        Assert.Equal(before + 500_000, observed);
    }

    [Fact]
    public void AdditiveEffectsFromDifferentSystemsCompose()
    {
        RegionId target = default;

        ScriptedSystem Contributor(string name, long delta) =>
            new(name, SimulationPhase.Population, (in SystemContext ctx) =>
            {
                target = ctx.World.Regions.AllIds().First();
                ctx.Effects.Emit(new AdjustRegionPopulation(target, delta, name));
            });

        Simulation sim = Simulation.Create(
            Config,
            [Contributor("test.a", 100), Contributor("test.b", 250), Contributor("test.c", -50)]);

        long before = sim.World.Regions.All().First().Population;
        sim.AdvanceYear();

        Assert.Equal(before + 300, sim.World.Regions.Get(target).Population);
        Assert.Empty(sim.Conflicts);
    }

    /// <summary>
    /// Absolute writes cannot compose, so the applier resolves them by pipeline order and records
    /// the collision rather than letting the loser vanish silently.
    /// </summary>
    [Fact]
    public void ConflictingAbsoluteWritesAreResolvedInOrderAndRecorded()
    {
        ScriptedSystem Claimant(string name, int polityOrdinal) =>
            new(name, SimulationPhase.Diplomacy, (in SystemContext ctx) =>
            {
                RegionId region = ctx.World.Regions.AllIds().First();
                PolityId claim = ctx.World.Polities.AllIds().Skip(polityOrdinal).First();
                ctx.Effects.Emit(new SetRegionController(region, claim, $"claim by {name}"));
            });

        Simulation contested = Simulation.Create(
            Config with { ThrowOnInvariantViolation = false },
            [Claimant("test.claim.first", 0), Claimant("test.claim.second", 1), new PolityLifecycleSystem()]);

        RegionId contestedRegion = contested.World.Regions.AllIds().First();
        PolityId winner = contested.World.Polities.AllIds().First();

        contested.AdvanceYear();

        Assert.Equal(winner, contested.World.Regions.Get(contestedRegion).Controller);

        EffectConflict conflict = Assert.Single(contested.Conflicts);
        Assert.Equal("test.claim.first", conflict.WinningSource);
        Assert.Equal("test.claim.second", conflict.LosingSource);
    }

    [Fact]
    public void EffectsCarryTheirEmittingSystemName()
    {
        var buffer = new EffectBuffer("test.source");
        buffer.Emit(new AdjustRegionPopulation(default, 1, "x"));

        Assert.Equal("test.source", buffer.Effects[0].Source);
    }

    [Fact]
    public void DuplicateSystemNamesAreRejected()
    {
        // Two systems sharing a name would share a random stream and draw correlated numbers.
        ArgumentException ex = Assert.Throws<ArgumentException>(() => Simulation.Create(
            Config,
            [
                new NoOpSystem("test.duplicate", SimulationPhase.Population),
                new NoOpSystem("test.duplicate", SimulationPhase.Economy),
            ]));

        Assert.Contains("Duplicate system name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EffectsNamingStaleEntitiesAreDroppedNotFatal()
    {
        // A system reading a start-of-phase snapshot can legitimately name something a later phase
        // removed. That must be survivable, because it is a race the architecture permits by design.
        var ghost = new ScriptedSystem("test.ghost", SimulationPhase.Economy, (in SystemContext ctx) =>
        {
            ctx.Effects.Emit(new AdjustRegionPopulation(new RegionId(999, 7), 100, "ghost"));
            ctx.Effects.Emit(new SetRegionController(new RegionId(999, 7), default, "ghost"));
            ctx.Effects.Emit(new DissolvePolity(new PolityId(999, 7), "ghost"));
            ctx.Effects.Emit(new SetPolityCapital(new PolityId(999, 7), new RegionId(999, 7), "ghost"));
        });

        Simulation sim = Simulation.Create(Config, [ghost]);
        sim.AdvanceYear();

        Assert.Empty(sim.Violations);
    }

    /// <summary>An effect type with no applier branch must fail loudly, not be ignored.</summary>
    [Fact]
    public void UnhandledEffectTypesThrow()
    {
        var rogue = new ScriptedSystem("test.rogue", SimulationPhase.Economy,
            (in SystemContext ctx) => ctx.Effects.Emit(new UnknownEffect()));

        Simulation sim = Simulation.Create(Config, [rogue]);

        Assert.Throws<NotSupportedException>(() => sim.AdvanceYear());
    }

    private sealed record UnknownEffect : Effect
    {
        public override string Kind => "test.unknown";
    }
}
