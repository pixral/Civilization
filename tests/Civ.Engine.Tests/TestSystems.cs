using Civ.Engine.Random;
using Civ.Engine.Systems;

namespace Civ.Engine.Tests;

public delegate void SystemAction(in SystemContext context);

/// <summary>
/// A system whose behaviour is supplied by the test.
/// </summary>
/// <remarks>
/// Only possible because <see cref="ISimulationSystem"/> is a narrow interface with no engine
/// callbacks - a test can stand in for real content without the engine noticing. It also proves the
/// property the whole layout is for: the engine has no built-in systems, so a simulation composed
/// entirely of test doubles is a completely ordinary simulation.
/// </remarks>
public sealed class ScriptedSystem(string name, SimulationPhase phase, SystemAction action)
    : ISimulationSystem
{
    public string Name { get; } = name;

    public SimulationPhase Phase { get; } = phase;

    public void Execute(in SystemContext context) => action(in context);
}

/// <summary>A system that does nothing. Used to prove that adding one changes no other outcome.</summary>
public sealed class NoOpSystem(string name, SimulationPhase phase) : ISimulationSystem
{
    public string Name { get; } = name;

    public SimulationPhase Phase { get; } = phase;

    public void Execute(in SystemContext context)
    {
        // Deliberately draws randomness. If streams were shared or positional rather than derived
        // from the system name, this would perturb every other system's sequence.
        Rng noise = context.Rng(12345);
        _ = noise.NextUInt64();
    }
}
