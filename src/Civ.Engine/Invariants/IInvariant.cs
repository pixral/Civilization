using Civ.Engine.State;

namespace Civ.Engine.Invariants;

/// <summary>
/// A rule that must hold over world state at the end of any tick.
/// </summary>
/// <remarks>
/// These are the automated answer to "how do I know a thousand-year run did not quietly corrupt
/// itself in year 300". They are cheap, they run over the whole world, and every one of them
/// encodes an assumption that some system will eventually violate by accident. Adding an invariant
/// alongside each new system is the habit that keeps a simulation of this size debuggable.
/// </remarks>
public interface IInvariant
{
    string Name { get; }

    void Check(WorldState world, ICollection<InvariantViolation> violations);
}
