namespace Civ.Engine.Invariants;

/// <summary>A rule about world state that did not hold.</summary>
public sealed record InvariantViolation(string Invariant, int Year, string Message)
{
    public override string ToString() => $"[{Year}] {Invariant}: {Message}";
}

/// <summary>Thrown when the configuration asks for violations to be fatal. Tests use this.</summary>
public sealed class InvariantViolationException(InvariantViolation violation)
    : Exception(violation.ToString())
{
    public InvariantViolation Violation { get; } = violation;
}
