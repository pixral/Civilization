namespace Civ.Engine.Systems;

/// <summary>
/// One unit of simulation behaviour.
/// </summary>
/// <remarks>
/// <para>A system reads state and emits effects. It cannot write state - the mutating members of
/// <c>WorldState</c> are internal to <c>Civ.Engine</c>, and systems live in another assembly.</para>
///
/// <para>Systems must be stateless between ticks. Everything that persists belongs in
/// <c>WorldState</c>, where it is hashed, saved, and checked by invariants. A field on a system is
/// invisible to all three, and is the fastest way to break reproducibility.</para>
///
/// <para><see cref="Name"/> is the system's identity for random streams. Two systems must not share
/// a name, and renaming one changes its own rolls and nothing else.</para>
/// </remarks>
public interface ISimulationSystem
{
    string Name { get; }

    SimulationPhase Phase { get; }

    void Execute(in SystemContext context);
}
