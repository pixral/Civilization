using Civ.Engine.Config;
using Civ.Engine.Core;
using Civ.Engine.Events;
using Civ.Engine.Random;
using Civ.Engine.State;

namespace Civ.Engine.Worldgen;

/// <summary>
/// Builds the initial world from a configuration and a seed.
/// </summary>
/// <remarks>
/// <para>Worldgen is engine code, not a system: it runs once, before year one, and bootstraps the
/// state the effect pipeline then guards. It writes to state directly for that reason, and records
/// its events straight to the chronicle.</para>
///
/// <para>Geography is an adjacency graph laid out on a rectangle. It is not a map and does not
/// pretend to be one - continents, coastlines and elevation are worth having only once something
/// reads them. Regions draw their properties from per-index random streams, so world size can
/// change without reshuffling the regions that were already there.</para>
/// </remarks>
public static class WorldGenerator
{
    private static readonly ulong TerrainStream = RngStreams.Id("worldgen.terrain");
    private static readonly ulong NameStream = RngStreams.Id("worldgen.names");
    private static readonly ulong PolityStream = RngStreams.Id("worldgen.polities");
    private static readonly ulong HeartlandStream = RngStreams.Id("worldgen.heartlands");
    private static readonly ulong RulerStream = RngStreams.Id("worldgen.rulers");

    public static WorldState Generate(SimulationConfig config, Chronicle chronicle)
    {
        config.Validate();

        var world = new WorldState(config.Seed, config.StartYear);

        GenerateRegions(world, config);
        LinkGrid(world, config);

        // Recorded before the foundings it summarises: the chronicle is read in append order, so
        // append order is narrative order.
        chronicle.Record(new WorldGeneratedEvent(
            world.Year,
            config.Seed,
            world.Regions.Count,
            config.InitialPolityCount,
            WorldQueries.WorldPopulation(world)));

        SeedPolities(world, config, chronicle);

        return world;
    }

    private static void GenerateRegions(WorldState world, SimulationConfig config)
    {
        long popSpan = config.InitialRegionPopulationMax - config.InitialRegionPopulationMin + 1;
        int[] richness = Heartlands(config);

        for (int index = 0; index < config.RegionCount; index++)
        {
            // Year 0 for worldgen: it happens outside the tick loop.
            Rng terrainRng = RngStreams.Create(config.Seed, TerrainStream, 0, (ulong)index);
            Rng nameRng = RngStreams.Create(config.Seed, NameStream, 0, (ulong)index);

            var terrain = (Terrain)terrainRng.NextInt(0, Enum.GetValues<Terrain>().Length);
            int fertility = Math.Clamp(FertilityFor(terrain, ref terrainRng) + richness[index], 2, 100);
            long population = config.InitialRegionPopulationMin
                + (long)terrainRng.NextUInt32((uint)popSpan) * fertility / 100;

            world.AddRegion(NameGenerator.Region(ref nameRng), terrain, fertility, population);
        }
    }

    /// <summary>
    /// A low-frequency richness field: a few fertile centres whose bonus falls off with distance.
    /// </summary>
    /// <remarks>
    /// <para>Fertility was originally an independent draw per region. That made every part of the
    /// world statistically identical, which in turn made every polity identical, and the first
    /// border system to run on it produced a permanent stalemate: no polity ever grew, shrank or
    /// died in two thousand years, whatever its rules were tuned to.</para>
    ///
    /// <para>The fix is spatial correlation, not a stronger rule. Real geography is clustered, so
    /// some states sit on rich heartlands and others on marginal ground, and that initial asymmetry
    /// is what a conquest rule has to amplify. Independent per-cell noise gives it nothing to
    /// work with.</para>
    /// </remarks>
    private static int[] Heartlands(SimulationConfig config)
    {
        var richness = new int[config.RegionCount];
        Rng rng = RngStreams.Create(config.Seed, HeartlandStream, 0);

        int count = Math.Max(2, config.RegionCount / 24);
        var centres = new (int X, int Y, int Amplitude, int Falloff)[count];

        for (int i = 0; i < count; i++)
        {
            centres[i] = (
                rng.NextInt(0, config.WorldWidth),
                rng.NextInt(0, config.WorldHeight),
                rng.NextInt(20, 56),
                rng.NextInt(4, 10));
        }

        for (int index = 0; index < config.RegionCount; index++)
        {
            int x = index % config.WorldWidth;
            int y = index / config.WorldWidth;
            int best = 0;

            foreach ((int cx, int cy, int amplitude, int falloff) in centres)
            {
                int distance = Math.Abs(x - cx) + Math.Abs(y - cy);
                best = Math.Max(best, amplitude - (falloff * distance));
            }

            // Centred on zero so the world mean stays put; only its variance becomes spatial.
            richness[index] = best - 14;
        }

        return richness;
    }

    private static int FertilityFor(Terrain terrain, ref Rng rng) => terrain switch
    {
        Terrain.Plains => rng.NextInt(55, 96),
        Terrain.Forest => rng.NextInt(40, 76),
        Terrain.Hills => rng.NextInt(30, 61),
        Terrain.Mountains => rng.NextInt(5, 26),
        Terrain.Desert => rng.NextInt(2, 21),
        Terrain.Coast => rng.NextInt(45, 86),
        _ => 30,
    };

    /// <summary>Four-way adjacency on a rectangle. Symmetric by construction.</summary>
    private static void LinkGrid(WorldState world, SimulationConfig config)
    {
        var ids = world.Regions.AllIds().ToList();

        for (int y = 0; y < config.WorldHeight; y++)
        {
            for (int x = 0; x < config.WorldWidth; x++)
            {
                int index = (y * config.WorldWidth) + x;

                if (x + 1 < config.WorldWidth)
                {
                    world.LinkRegions(ids[index], ids[index + 1]);
                }

                if (y + 1 < config.WorldHeight)
                {
                    world.LinkRegions(ids[index], ids[index + config.WorldWidth]);
                }
            }
        }
    }

    /// <summary>
    /// Places polity seats and assigns every region to its nearest seat.
    /// </summary>
    /// <remarks>
    /// A Manhattan-distance Voronoi partition. Territories come out contiguous, ties break on the
    /// lower polity index so the result is order-independent, and no region starts unclaimed.
    /// Nothing about this is meant to model settlement - it exists so the world starts with a
    /// political map for later systems to disturb.
    /// </remarks>
    private static void SeedPolities(WorldState world, SimulationConfig config, Chronicle chronicle)
    {
        if (config.InitialPolityCount == 0)
        {
            return;
        }

        var regionIds = world.Regions.AllIds().ToList();
        Rng rng = RngStreams.Create(config.Seed, PolityStream, 0);

        // Distinct seat indices, drawn without replacement.
        var available = Enumerable.Range(0, config.RegionCount).ToList();
        var seats = new List<int>();
        for (int i = 0; i < config.InitialPolityCount; i++)
        {
            int pick = rng.NextInt(0, available.Count);
            seats.Add(available[pick]);
            available.RemoveAt(pick);
        }

        var polityIds = new List<PolityId>();
        foreach (int seatIndex in seats)
        {
            Rng nameRng = RngStreams.Create(config.Seed, PolityStream, 0, (ulong)(seatIndex + 1));
            string name = NameGenerator.Polity(ref nameRng);
            PolityId id = world.AddPolity(name, regionIds[seatIndex], config.StartYear, PolityId.None);
            polityIds.Add(id);
        }

        for (int index = 0; index < config.RegionCount; index++)
        {
            int best = 0;
            int bestDistance = int.MaxValue;

            for (int p = 0; p < seats.Count; p++)
            {
                int distance = GridDistance(index, seats[p], config.WorldWidth);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = p;
                }
            }

            world.Regions.Get(regionIds[index]).Controller = polityIds[best];
        }

        // Founding rulers. Seated after territory is assigned so the accession events follow the
        // foundings they belong to, and drawn per polity index so world size does not reshuffle them.
        for (int p = 0; p < polityIds.Count; p++)
        {
            Rng rulerRng = RngStreams.Create(config.Seed, RulerStream, 0, (ulong)p);
            int age = rulerRng.NextInt(20, 46);
            int administration =
                (rulerRng.NextInt(0, 101) + rulerRng.NextInt(0, 101) + rulerRng.NextInt(0, 101)) / 3;
            int military =
                (rulerRng.NextInt(0, 101) + rulerRng.NextInt(0, 101) + rulerRng.NextInt(0, 101)) / 3;
            string rulerName = NameGenerator.Person(ref rulerRng);

            var profile = new RulerProfile(
                rulerName, config.StartYear - age, administration, military);
            RulerId rulerId = world.AddRuler(profile, config.StartYear, polityIds[p]);
            world.Polities.Get(polityIds[p]).CurrentRuler = rulerId;
        }

        for (int p = 0; p < polityIds.Count; p++)
        {
            Polity polity = world.Polities.Get(polityIds[p]);
            chronicle.Record(new PolityFoundedEvent(
                config.StartYear,
                polity.Id,
                polity.Name,
                polity.Capital,
                world.Regions.Get(regionIds[seats[p]]).Name,
                PolityId.None,
                string.Empty,
                WorldQueries.RegionCountOf(world, polity.Id),
                "world generation"));

            Ruler ruler = world.Rulers.Get(polity.CurrentRuler);
            chronicle.Record(new RulerAccessionEvent(
                config.StartYear,
                ruler.Id,
                ruler.Name,
                polity.Id,
                polity.Name,
                ruler.Administration,
                ruler.Military,
                ruler.AgeIn(config.StartYear),
                "world generation"));
        }
    }

    private static int GridDistance(int a, int b, int width)
    {
        int ax = a % width;
        int ay = a / width;
        int bx = b % width;
        int by = b / width;
        return Math.Abs(ax - bx) + Math.Abs(ay - by);
    }
}
