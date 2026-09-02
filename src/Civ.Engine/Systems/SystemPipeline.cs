using Civ.Engine.Effects;
using Civ.Engine.Random;

namespace Civ.Engine.Systems;

/// <summary>
/// The ordered set of systems that make up one year, grouped into phases.
/// </summary>
/// <remarks>
/// <para>Ordering is <c>(phase, registration order)</c> and is fixed at construction. Registration
/// order matters only for effect application and conflict resolution - never for randomness, which
/// is derived from system <i>names</i> - so reordering two systems inside a phase changes which one
/// wins a contested write and nothing else.</para>
///
/// <para>Duplicate system names are rejected. Sharing a name would mean sharing a random stream,
/// which produces correlated draws between unrelated systems: a subtle, near-undebuggable class
/// of bug that is trivial to make impossible here.</para>
/// </remarks>
public sealed class SystemPipeline
{
    private readonly List<ISimulationSystem> _systems;
    private readonly Dictionary<string, ulong> _streamIds;

    public SystemPipeline(IEnumerable<ISimulationSystem> systems)
    {
        ArgumentNullException.ThrowIfNull(systems);

        // Stable ordering: phase first, then registration order. OrderBy is a stable sort.
        _systems = [.. systems];
        var byName = new HashSet<string>(StringComparer.Ordinal);
        foreach (ISimulationSystem system in _systems)
        {
            if (!byName.Add(system.Name))
            {
                throw new ArgumentException(
                    $"Duplicate system name '{system.Name}'. Names identify random streams and must be unique.",
                    nameof(systems));
            }
        }

        _systems = [.. _systems.OrderBy(s => (int)s.Phase)];
        _streamIds = _systems.ToDictionary(s => s.Name, s => RngStreams.Id(s.Name), StringComparer.Ordinal);
    }

    public IReadOnlyList<ISimulationSystem> Systems => _systems;

    /// <summary>Systems in the given phase, in execution order.</summary>
    public IEnumerable<ISimulationSystem> InPhase(SimulationPhase phase)
    {
        foreach (ISimulationSystem system in _systems)
        {
            if (system.Phase == phase)
            {
                yield return system;
            }
        }
    }

    internal ulong StreamIdOf(ISimulationSystem system) => _streamIds[system.Name];

    /// <summary>One reusable buffer per system, so buffers can be concatenated in pipeline order.</summary>
    internal Dictionary<string, EffectBuffer> CreateBuffers() =>
        _systems.ToDictionary(s => s.Name, s => new EffectBuffer(s.Name), StringComparer.Ordinal);
}
