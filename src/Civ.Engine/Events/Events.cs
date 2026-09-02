using Civ.Engine.Core;
using Civ.Engine.State;

namespace Civ.Engine.Events;

/// <summary>The world was generated. Always the first entry in any chronicle.</summary>
public sealed record WorldGeneratedEvent(
    int Year,
    ulong Seed,
    int RegionCount,
    int PolityCount,
    long Population) : SimEvent(Year, Salience.Historic)
{
    public override string Kind => "world.generated";

    public override string Text =>
        $"The world takes shape: {RegionCount} regions, {PolityCount} polities, {Population:N0} souls.";
}

public sealed record PolityFoundedEvent(
    int Year,
    PolityId Polity,
    string PolityName,
    RegionId Capital,
    string CapitalName,
    PolityId Parent,
    string ParentName,
    int Regions,
    string Reason) : SimEvent(Year, Salience.Major)
{
    public override string Kind => "polity.founded";

    public override string Text =>
        Parent.IsSome
            ? $"{PolityName} broke away from {ParentName} with {Regions} region(s), seated at {CapitalName}."
            : $"{PolityName} was founded, seated at {CapitalName}.";
}

public sealed record PolityDissolvedEvent(
    int Year,
    PolityId Polity,
    string PolityName,
    int RegionsLost,
    string Reason) : SimEvent(Year, Salience.Historic)
{
    public override string Kind => "polity.dissolved";

    public override string Text =>
        $"{PolityName} ceased to exist ({Reason}).";
}

public sealed record RegionControlChangedEvent(
    int Year,
    RegionId Region,
    string RegionName,
    PolityId From,
    string FromName,
    PolityId To,
    string ToName,
    string Reason) : SimEvent(Year, Salience.Major)
{
    public override string Kind => "region.control_changed";

    public override string Text =>
        To.IsNone
            ? $"{RegionName} slipped out of {FromName}'s control."
            : From.IsNone
                ? $"{ToName} took control of {RegionName}."
                : $"{RegionName} passed from {FromName} to {ToName}.";
}

/// <summary>
/// Population is reported on threshold crossings, never per tick. Every region's population moves
/// every year; reporting each change would bury everything else.
/// </summary>
public sealed record PopulationMilestoneEvent(
    int Year,
    RegionId Region,
    string RegionName,
    long Population,
    long Milestone,
    bool Rising) : SimEvent(Year, Salience.Notable)
{
    public override string Kind => "region.population_milestone";

    public override string Text =>
        Rising
            ? $"{RegionName} grew past {Milestone:N0} inhabitants."
            : $"{RegionName} fell below {Milestone:N0} inhabitants.";
}

public sealed record PolityStabilityShiftEvent(
    int Year,
    PolityId Polity,
    string PolityName,
    int From,
    int To,
    string Reason) : SimEvent(Year, Salience.Notable)
{
    public override string Kind => "polity.stability_shift";

    public override string Text =>
        To > From
            ? $"Order improved in {PolityName} ({From} to {To})."
            : $"Order deteriorated in {PolityName} ({From} to {To}).";
}

public sealed record PolityCapitalMovedEvent(
    int Year,
    PolityId Polity,
    string PolityName,
    RegionId From,
    string FromName,
    RegionId To,
    string ToName,
    string Reason) : SimEvent(Year, Salience.Major)
{
    public override string Kind => "polity.capital_moved";

    public override string Text =>
        From.IsNone
            ? $"{PolityName} established its seat at {ToName}."
            : $"{PolityName} moved its seat from {FromName} to {ToName} ({Reason}).";
}

/// <summary>
/// A ruler took power.
/// </summary>
/// <remarks>
/// Carries the ruler's id for querying and their name, age and ability as they stood at accession.
/// An archived ruler is still renderable centuries later from these fields alone, which is what lets
/// the chronicle survive both the ruler's death and the extinction of the state they ruled.
/// </remarks>
public sealed record RulerAccessionEvent(
    int Year,
    RulerId Ruler,
    string RulerName,
    PolityId Polity,
    string PolityName,
    int Administration,
    int Military,
    int Age,
    string Reason) : SimEvent(Year, Salience.Notable)
{
    public override string Kind => "ruler.accession";

    public override string Text =>
        $"{RulerName} took power in {PolityName} at {Age} "
        + $"(administration {Administration}, military {Military}).";
}

public sealed record RulerDeathEvent(
    int Year,
    RulerId Ruler,
    string RulerName,
    PolityId Polity,
    string PolityName,
    int Age,
    int ReignLength,
    string Reason) : SimEvent(Year, Salience.Notable)
{
    public override string Kind => "ruler.death";

    public override string Text =>
        $"{RulerName} of {PolityName} died at {Age} after {ReignLength} years ({Reason}).";
}

/// <summary>
/// A reign ended without the ruler dying.
/// </summary>
/// <remarks>
/// Disjoint from <see cref="RulerDeathEvent"/>: a ruler who dies in office produces a death, and one
/// whose state collapsed under them produces this. Keeping them apart is what lets the mortality
/// figures mean what they say.
/// </remarks>
public sealed record ReignEndedEvent(
    int Year,
    RulerId Ruler,
    string RulerName,
    PolityId Polity,
    string PolityName,
    int Age,
    int ReignLength,
    ReignEndReason EndReason,
    string Reason) : SimEvent(Year, Salience.Notable)
{
    public override string Kind => "ruler.reign_ended";

    public override string Text =>
        EndReason == ReignEndReason.PolityExtinct
            ? $"{RulerName} outlived {PolityName}, whose {ReignLength}-year reign ended with the state."
            : $"{RulerName} was removed from power in {PolityName} after {ReignLength} years ({Reason}).";
}
