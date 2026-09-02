using Civ.Engine.Config;
using Civ.Engine.State;

namespace Civ.Engine.Invariants;

/// <summary>Runs the registered invariants according to the configured cadence.</summary>
public sealed class InvariantChecker(IEnumerable<IInvariant> invariants)
{
    private readonly List<IInvariant> _invariants = [.. invariants];

    public IReadOnlyList<IInvariant> Invariants => _invariants;

    public bool ShouldRun(InvariantMode mode, int interval, int year) => mode switch
    {
        InvariantMode.Off => false,
        InvariantMode.EveryTick => true,
        InvariantMode.Periodic => interval > 0 && year % interval == 0,
        _ => false,
    };

    public void Run(WorldState world, ICollection<InvariantViolation> violations)
    {
        foreach (IInvariant invariant in _invariants)
        {
            invariant.Check(world, violations);
        }
    }
}
