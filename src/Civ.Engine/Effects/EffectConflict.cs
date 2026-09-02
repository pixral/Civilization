namespace Civ.Engine.Effects;

/// <summary>
/// Two systems tried to make incompatible absolute writes in the same phase.
/// </summary>
/// <remarks>
/// Not an error. It is a designed-for outcome resolved by pipeline order, and surfacing it is the
/// point: a conflict that recurs every year usually means two systems own the same decision, which
/// is a modelling problem worth seeing rather than a race worth hiding.
/// </remarks>
public sealed record EffectConflict(
    int Year,
    string Phase,
    string Field,
    string WinningSource,
    string LosingSource,
    string Detail)
{
    public override string ToString() =>
        $"[{Year}/{Phase}] {Field}: {WinningSource} won over {LosingSource} ({Detail})";
}
