using Civ.Engine.Core;

namespace Civ.Engine.State;

/// <summary>
/// The atomic unit of territory. Population lives here; polities are defined by which regions
/// they control, never by a territory list of their own.
/// </summary>
/// <remarks>
/// <para>Ownership is stored on the region, in exactly one place. The alternative - a region list
/// on the polity - has to be kept in sync with something, and de-synchronised ownership is the
/// single most common way a simulation of this kind starts telling two different stories.</para>
///
/// <para>Every setter is <c>internal</c>. Only <see cref="Effects.EffectApplier"/> calls them.</para>
/// </remarks>
public sealed class Region
{
    internal Region(RegionId id, string name, Terrain terrain, int fertility, long population)
    {
        Id = id;
        Name = name;
        Terrain = terrain;
        Fertility = fertility;
        Population = population;
        Controller = PolityId.None;
    }

    public RegionId Id { get; }

    public string Name { get; internal set; }

    public Terrain Terrain { get; }

    /// <summary>0-100. A static property of the land; climate and improvement come later.</summary>
    public int Fertility { get; }

    /// <summary>Aggregate, never a collection of individuals. Integral so state hashing stays exact.</summary>
    public long Population { get; internal set; }

    /// <summary>Controlling polity, or <see cref="EntityId{TKind}.None"/> for unclaimed land.</summary>
    public PolityId Controller { get; internal set; }

    internal readonly List<RegionId> NeighborList = [];

    /// <summary>Adjacent regions. Symmetric; an invariant enforces it.</summary>
    public IReadOnlyList<RegionId> Neighbors => NeighborList;

    public override string ToString() => $"{Name} ({Id})";
}
