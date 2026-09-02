using Civ.Engine.Core;

namespace Civ.Engine.State;

public enum PolityStatus
{
    Active = 0,

    /// <summary>
    /// Gone, but retained. Dissolved polities are historical records: events from year 400 still
    /// refer to them in year 1800, so their slot is never freed and their handle never goes stale.
    /// </summary>
    Defunct = 1,
}

/// <summary>
/// A political entity. Holds no territory list - see <see cref="Region.Controller"/>.
/// </summary>
public sealed class Polity
{
    internal Polity(PolityId id, string name, RegionId capital, int foundedYear, PolityId parent)
    {
        Id = id;
        Name = name;
        Capital = capital;
        FoundedYear = foundedYear;
        Parent = parent;
        Status = PolityStatus.Active;
        Stability = 50;
    }

    public PolityId Id { get; }

    public string Name { get; internal set; }

    /// <summary>Seat of government. May be <see cref="EntityId{TKind}.None"/> after territorial loss.</summary>
    public RegionId Capital { get; internal set; }

    public int FoundedYear { get; }

    public int? DissolvedYear { get; internal set; }

    public PolityStatus Status { get; internal set; }

    /// <summary>
    /// The polity this one broke away from or succeeded, if any. Cheap now, and it is what will
    /// let a later chronicle say "third successor state of the Aster hegemony" without archaeology.
    /// </summary>
    public PolityId Parent { get; }

    /// <summary>0-100. A single placeholder scalar so the effect and event paths have something to move.</summary>
    public int Stability { get; internal set; }

    /// <summary>
    /// The ruler currently holding this polity. <see cref="EntityId{TKind}.None"/> only while defunct.
    /// </summary>
    /// <remarks>
    /// Authoritative for "who rules this state now". <see cref="Ruler.Polity"/> answers the different
    /// question of "what did this person rule", and stays correct for the dead - the two agree only
    /// for the living ruler, which is an invariant rather than a redundancy.
    /// </remarks>
    public RulerId CurrentRuler { get; internal set; }

    public bool IsActive => Status == PolityStatus.Active;

    public override string ToString() => $"{Name} ({Id})";
}
