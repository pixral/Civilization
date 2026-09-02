using Civ.Engine.Core;

namespace Civ.Engine.State;

/// <summary>
/// The generated attributes of a ruler, before they exist as an entity.
/// </summary>
/// <remarks>
/// Carried on effects so the decision of <i>who</i> succeeds is separable from the decision of
/// <i>when</i>. A system that wants a specific successor supplies one; a caller that does not care
/// leaves it null and the applier generates a deterministic profile of its own.
/// </remarks>
public sealed record RulerProfile(string Name, int BirthYear, int Administration, int Military = 50);

/// <summary>Why a reign ended.</summary>
public enum ReignEndReason
{
    /// <summary>The ruler died in office. The only cause the succession system produces.</summary>
    Death = 0,

    /// <summary>The state ceased to exist. The ruler is archived, not killed.</summary>
    PolityExtinct = 1,

    /// <summary>Removed while alive and while the state survived. No system produces this yet.</summary>
    Displaced = 2,
}

/// <summary>
/// One person who ruled one polity.
/// </summary>
/// <remarks>
/// <para><b>Minimal on purpose.</b> Administration is the only mechanically active ability. There
/// are no heirs, dynasties, traits, relationships or legitimacy, because none of those is needed for
/// the one thing this layer exists to do: make administrative capacity vary between reigns.</para>
///
/// <para><b>Age is derived, never stored.</b> <see cref="BirthYear"/> is the fact; age is a question
/// you ask about a year. Storing both would be two places to be wrong.</para>
///
/// <para><b>Rulers are never deleted.</b> Like polities, a dead ruler is retained forever so events
/// from year 400 still resolve in year 1800. <see cref="Polity"/> records which state they ruled and
/// stays correct after that state has moved on to a successor - or ceased to exist entirely.</para>
/// </remarks>
public sealed class Ruler
{
    internal Ruler(
        RulerId id,
        string name,
        int birthYear,
        int administration,
        int military,
        int accessionYear,
        PolityId polity)
    {
        Id = id;
        Name = name;
        BirthYear = birthYear;
        Administration = administration;
        Military = military;
        AccessionYear = accessionYear;
        Polity = polity;
    }

    public RulerId Id { get; }

    public string Name { get; }

    public int BirthYear { get; }

    /// <summary>0-100, centred near 50. Governs cohesion, and therefore what a state can keep.</summary>
    public int Administration { get; }

    /// <summary>
    /// 0-100, centred near 50, drawn independently of <see cref="Administration"/>.
    /// </summary>
    /// <remarks>
    /// Governs campaign tempo, and therefore how quickly a state acts on opportunities it already
    /// has. Independence is the point: the four combinations of the two abilities are meant to
    /// produce empire builders, unstable conquerors, consolidators and decline without any of those
    /// categories existing anywhere in the code.
    /// </remarks>
    public int Military { get; }

    public int AccessionYear { get; }

    /// <summary>Null while alive. Only ever set by an actual death.</summary>
    public int? DeathYear { get; internal set; }

    /// <summary>Null while still reigning. Set when the reign ends, for any reason.</summary>
    /// <remarks>
    /// Separate from <see cref="DeathYear"/> because the two genuinely differ. A state can fall out
    /// from under a ruler who is still alive, and conflating the two turned every extinction into a
    /// fabricated death - which then showed up in the mortality statistics as if it were one.
    /// </remarks>
    public int? ReignEndYear { get; internal set; }

    public ReignEndReason? EndReason { get; internal set; }

    /// <summary>The polity this ruler held. Remains meaningful after both are gone.</summary>
    public PolityId Polity { get; }

    public bool IsAlive => DeathYear is null;

    /// <summary>
    /// Still in power. This, not <see cref="IsAlive"/>, is what the polity invariants care about.
    /// </summary>
    /// <remarks>
    /// A ruler whose state was destroyed is archived alive and never reigns again: nothing in the
    /// simulation gives a deposed or stateless ruler anything to do, so they are simply not aged
    /// further. Exile, restoration and claims would each need that to change.
    /// </remarks>
    public bool IsReigning => ReignEndYear is null;

    /// <summary>Age in a given year. Derived; there is no stored age to fall out of step.</summary>
    public int AgeIn(int year) => year - BirthYear;

    /// <summary>Years ruled, up to the given year or to the end of the reign, whichever came first.</summary>
    public int ReignLengthAt(int year) => Math.Max(0, (ReignEndYear ?? year) - AccessionYear);

    public override string ToString() => $"{Name} ({Id})";
}
