using Civ.Engine.Config;
using Civ.Engine.Effects;
using Civ.Engine.Events;
using Civ.Engine.Invariants;
using Civ.Engine.State;
using Civ.Engine.Systems;
using Civ.Engine.Worldgen;

namespace Civ.Engine;

/// <summary>
/// A running world. The whole public surface of the engine.
/// </summary>
/// <remarks>
/// <para>Headless by construction: no console, no rendering, no input. The terminal application and
/// the batch runner are two peers that observe the same object, which is what keeps "the simulation
/// runs without a UI" true by default rather than by intention.</para>
///
/// <para>The tick is deliberately boring:
/// <c>year++</c>, then for each phase run its systems against a frozen snapshot, apply their effects
/// at the barrier, then check invariants. All the interesting behaviour lives in systems; the loop
/// itself should not need to change again.</para>
/// </remarks>
public sealed class Simulation
{
    private readonly SystemPipeline _pipeline;
    private readonly EffectApplier _applier;
    private readonly InvariantChecker _invariants;
    private readonly List<EffectConflict> _conflicts = [];
    private readonly List<InvariantViolation> _violations = [];
    private readonly Dictionary<string, EffectBuffer> _buffers;
    private readonly SimulationPhase[] _phases = Enum.GetValues<SimulationPhase>();

    private Simulation(
        SimulationConfig config,
        WorldState world,
        Chronicle chronicle,
        SystemPipeline pipeline,
        InvariantChecker invariants)
    {
        Config = config;
        World = world;
        Chronicle = chronicle;
        _pipeline = pipeline;
        _invariants = invariants;
        _applier = new EffectApplier(chronicle, _conflicts);
        _buffers = pipeline.CreateBuffers();
    }

    public SimulationConfig Config { get; }

    public WorldState World { get; }

    public Chronicle Chronicle { get; }

    public IReadOnlyList<ISimulationSystem> Systems => _pipeline.Systems;

    /// <summary>Contested absolute writes, in occurrence order. Diagnostic, not an error log.</summary>
    public IReadOnlyList<EffectConflict> Conflicts => _conflicts;

    public IReadOnlyList<InvariantViolation> Violations => _violations;

    public int Year => World.Year;

    /// <summary>
    /// Creates and generates a world.
    /// </summary>
    /// <param name="systems">
    /// The simulation content. The engine has no built-in systems and no default pipeline; every
    /// host composes its own, which is what stops the engine from silently depending on content.
    /// </param>
    public static Simulation Create(
        SimulationConfig config,
        IEnumerable<ISimulationSystem> systems,
        IEnumerable<IInvariant>? invariants = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(systems);

        var chronicle = new Chronicle();
        WorldState world = WorldGenerator.Generate(config, chronicle);
        var pipeline = new SystemPipeline(systems);
        var checker = new InvariantChecker(invariants ?? CoreInvariants.All);

        var simulation = new Simulation(config, world, chronicle, pipeline, checker);
        simulation.CheckInvariants(force: true);
        return simulation;
    }

    /// <summary>
    /// Continues an existing world instead of generating one. Used to resume a loaded save.
    /// </summary>
    /// <remarks>
    /// The chronicle starts empty: history is not part of a save, because it is reproducible from
    /// <c>(engine version, config, seed)</c> and would otherwise be a large, ever-growing derived
    /// value on disk. A caller that wants the narrative back replays from year one.
    /// </remarks>
    public static Simulation Resume(
        SimulationConfig config,
        WorldState world,
        IEnumerable<ISimulationSystem> systems,
        IEnumerable<IInvariant>? invariants = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(systems);

        var simulation = new Simulation(
            config,
            world,
            new Chronicle(),
            new SystemPipeline(systems),
            new InvariantChecker(invariants ?? CoreInvariants.All));

        simulation.CheckInvariants(force: true);
        return simulation;
    }

    public void AdvanceYear()
    {
        World.Year++;

        foreach (SimulationPhase phase in _phases)
        {
            RunPhase(phase);
        }

        CheckInvariants(force: false);
    }

    public void AdvanceYears(int years)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(years);

        for (int i = 0; i < years; i++)
        {
            AdvanceYear();
        }
    }

    /// <summary>Canonical state hash. Equal hashes at equal years mean identical worlds.</summary>
    public ulong StateHash() => WorldHasher.Hash(World);

    private void RunPhase(SimulationPhase phase)
    {
        var active = _pipeline.InPhase(phase).ToList();
        if (active.Count == 0)
        {
            return;
        }

        // Read half. Every system here sees the same start-of-phase state and writes nothing.
        foreach (ISimulationSystem system in active)
        {
            EffectBuffer buffer = _buffers[system.Name];
            buffer.Clear();

            var context = new SystemContext(
                World, World.Year, buffer, Config.Seed, _pipeline.StreamIdOf(system));

            system.Execute(in context);
        }

        // Barrier. Buffers concatenate in pipeline order, so application order is a property of the
        // pipeline definition rather than of execution timing.
        var ordered = new List<Effect>();
        foreach (ISimulationSystem system in active)
        {
            ordered.AddRange(_buffers[system.Name].Effects);
        }

        if (ordered.Count > 0)
        {
            _applier.ApplyPhase(World, phase.ToString(), ordered);
        }
    }

    private void CheckInvariants(bool force)
    {
        if (!force && !_invariants.ShouldRun(Config.InvariantMode, Config.InvariantInterval, World.Year))
        {
            return;
        }

        int before = _violations.Count;
        _invariants.Run(World, _violations);

        if (Config.ThrowOnInvariantViolation && _violations.Count > before)
        {
            throw new InvariantViolationException(_violations[before]);
        }
    }
}
