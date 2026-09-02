using Civ.Engine.Core;

namespace Civ.Engine.State;

/// <summary>
/// Derived reads over <see cref="WorldState"/>. Nothing here is stored; everything is recomputed.
/// </summary>
/// <remarks>
/// These are O(regions) scans. At the target scale - low thousands of regions on a yearly tick -
/// that is irrelevant, and it buys immunity from an entire class of bug where a cached count and
/// the thing it counts disagree.
/// </remarks>
public static class WorldQueries
{
    public static IEnumerable<Region> RegionsOf(WorldState world, PolityId polity)
    {
        foreach (Region region in world.Regions.All())
        {
            if (region.Controller.Equals(polity))
            {
                yield return region;
            }
        }
    }

    public static int RegionCountOf(WorldState world, PolityId polity)
    {
        int count = 0;
        foreach (Region region in world.Regions.All())
        {
            if (region.Controller.Equals(polity))
            {
                count++;
            }
        }

        return count;
    }

    public static long PopulationOf(WorldState world, PolityId polity)
    {
        long total = 0;
        foreach (Region region in world.Regions.All())
        {
            if (region.Controller.Equals(polity))
            {
                total += region.Population;
            }
        }

        return total;
    }

    public static long WorldPopulation(WorldState world)
    {
        long total = 0;
        foreach (Region region in world.Regions.All())
        {
            total += region.Population;
        }

        return total;
    }

    public static IEnumerable<Polity> ActivePolities(WorldState world)
    {
        foreach (Polity polity in world.Polities.All())
        {
            if (polity.IsActive)
            {
                yield return polity;
            }
        }
    }

    /// <summary>
    /// The administrative ability the simulation should use for a polity.
    /// </summary>
    /// <remarks>
    /// One implementation, shared by cohesion and expansion. Two systems reading the same fact
    /// through two lookups is how they quietly stop agreeing - especially on the fallback, where a
    /// polity between reigns would otherwise be average to one system and hopeless to the other.
    /// </remarks>
    public static int AdministrationOf(WorldState world, Polity polity, int fallback) =>
        world.Rulers.TryGet(polity.CurrentRuler, out Ruler? ruler) ? ruler.Administration : fallback;

    /// <summary>The military ability the simulation should use for a polity. Shares the same fallback rule.</summary>
    public static int MilitaryOf(WorldState world, Polity polity, int fallback) =>
        world.Rulers.TryGet(polity.CurrentRuler, out Ruler? ruler) ? ruler.Military : fallback;

    /// <summary>Display name for a possibly-dead or absent polity. Used when rendering old events.</summary>
    public static string NameOf(WorldState world, PolityId polity) =>
        polity.IsNone ? "unclaimed"
        : world.Polities.TryGet(polity, out Polity? p) ? p.Name
        : "a forgotten power";

    public static string NameOf(WorldState world, RegionId region) =>
        world.Regions.TryGet(region, out Region? r) ? r.Name : "a lost land";
}
