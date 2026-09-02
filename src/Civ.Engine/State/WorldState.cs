using Civ.Engine.Core;

namespace Civ.Engine.State;

/// <summary>
/// The entire mutable state of the simulation. Plain data: no behaviour, no back-references,
/// no derived caches.
/// </summary>
/// <remarks>
/// <para><b>Read-only to systems.</b> Every mutating member is <c>internal</c> and simulation
/// systems live in a separate assembly, so "a system must not write directly to state" is enforced
/// by the compiler rather than by discipline.</para>
///
/// <para><b>No denormalised indexes.</b> "How many regions does this polity hold" is a scan, not a
/// stored counter - see <see cref="WorldQueries"/>. Caches get added when a profile demands them,
/// and will be rebuilt inside the applier so they cannot drift.</para>
///
/// <para><b>Serializable by construction.</b> Flat, integral, id-referenced. That is the entire
/// reason save/load needs no object graph walking.</para>
/// </remarks>
public sealed class WorldState
{
    internal WorldState(ulong seed, int year)
    {
        Seed = seed;
        Year = year;
    }

    /// <summary>The world seed. Part of the run's identity, alongside the config.</summary>
    public ulong Seed { get; }

    public int Year { get; internal set; }

    public EntityTable<RegionKind, Region> Regions { get; } = new();

    public EntityTable<PolityKind, Polity> Polities { get; } = new();

    /// <summary>Every ruler who ever held a polity, living or dead. Never pruned.</summary>
    public EntityTable<RulerKind, Ruler> Rulers { get; } = new();

    internal RegionId AddRegion(string name, Terrain terrain, int fertility, long population) =>
        Regions.Add(id => new Region(id, name, terrain, fertility, population));

    internal PolityId AddPolity(string name, RegionId capital, int foundedYear, PolityId parent) =>
        Polities.Add(id => new Polity(id, name, capital, foundedYear, parent));

    internal RulerId AddRuler(RulerProfile profile, int accessionYear, PolityId polity) =>
        Rulers.Add(id => new Ruler(
            id, profile.Name, profile.BirthYear, profile.Administration, profile.Military,
            accessionYear, polity));

    internal void LinkRegions(RegionId a, RegionId b)
    {
        if (a.Equals(b))
        {
            return;
        }

        Region ra = Regions.Get(a);
        Region rb = Regions.Get(b);

        if (!ra.NeighborList.Contains(b))
        {
            ra.NeighborList.Add(b);
        }

        if (!rb.NeighborList.Contains(a))
        {
            rb.NeighborList.Add(a);
        }
    }
}
