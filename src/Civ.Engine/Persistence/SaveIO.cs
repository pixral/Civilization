using System.Text.Json;
using System.Text.Json.Serialization;
using Civ.Engine.Config;
using Civ.Engine.Core;
using Civ.Engine.State;

namespace Civ.Engine.Persistence;

public sealed class SaveIntegrityException(string message) : Exception(message);

/// <summary>Converts between live world state and its serializable snapshot.</summary>
public static class SaveIO
{
    public const string EngineVersion = "0.1.0";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static SaveGame Capture(WorldState world, SimulationConfig config) => new(
        new SaveHeader(EngineVersion, config.Hash(), world.Seed, world.Year, WorldHasher.Hash(world)),
        config,
        Snapshot(world));

    public static WorldSnapshot Snapshot(WorldState world)
    {
        var regions = new List<RegionSnapshot>(world.Regions.Count);
        foreach (Region region in world.Regions.All())
        {
            regions.Add(new RegionSnapshot(
                Ref(region.Id),
                region.Name,
                (int)region.Terrain,
                region.Fertility,
                region.Population,
                Ref(region.Controller),
                [.. region.Neighbors.Select(Ref)]));
        }

        var polities = new List<PolitySnapshot>(world.Polities.Count);
        foreach (Polity polity in world.Polities.All())
        {
            polities.Add(new PolitySnapshot(
                Ref(polity.Id),
                polity.Name,
                Ref(polity.Capital),
                polity.FoundedYear,
                polity.DissolvedYear,
                (int)polity.Status,
                Ref(polity.Parent),
                polity.Stability,
                Ref(polity.CurrentRuler)));
        }

        var rulers = new List<RulerSnapshot>(world.Rulers.Count);
        foreach (Ruler ruler in world.Rulers.All())
        {
            rulers.Add(new RulerSnapshot(
                Ref(ruler.Id),
                ruler.Name,
                ruler.BirthYear,
                ruler.Administration,
                ruler.Military,
                ruler.AccessionYear,
                ruler.DeathYear,
                ruler.ReignEndYear,
                (int?)ruler.EndReason,
                Ref(ruler.Polity)));
        }

        return new WorldSnapshot(
            world.Seed, world.Year, [.. regions], [.. polities], [.. rulers]);
    }

    /// <summary>
    /// Rebuilds live state from a snapshot.
    /// </summary>
    /// <remarks>
    /// Slots are restored at their original indices and generations, so every id in the save - and
    /// in any event that referenced one - still resolves to the same entity after a round trip.
    /// </remarks>
    public static WorldState Restore(WorldSnapshot snapshot)
    {
        var world = new WorldState(snapshot.Seed, snapshot.Year);

        foreach (RegionSnapshot r in snapshot.Regions)
        {
            var id = new RegionId(r.Id.Index, r.Id.Generation);
            var region = new Region(id, r.Name, (Terrain)r.Terrain, r.Fertility, r.Population)
            {
                Controller = new PolityId(r.Controller.Index, r.Controller.Generation),
            };
            region.NeighborList.AddRange(r.Neighbors.Select(n => new RegionId(n.Index, n.Generation)));
            world.Regions.RestoreSlot(r.Id.Index, r.Id.Generation, region);
        }

        foreach (PolitySnapshot p in snapshot.Polities)
        {
            var id = new PolityId(p.Id.Index, p.Id.Generation);
            var polity = new Polity(
                id,
                p.Name,
                new RegionId(p.Capital.Index, p.Capital.Generation),
                p.FoundedYear,
                new PolityId(p.Parent.Index, p.Parent.Generation))
            {
                DissolvedYear = p.DissolvedYear,
                Status = (PolityStatus)p.Status,
                Stability = p.Stability,
                CurrentRuler = new RulerId(p.CurrentRuler.Index, p.CurrentRuler.Generation),
            };
            world.Polities.RestoreSlot(p.Id.Index, p.Id.Generation, polity);
        }

        foreach (RulerSnapshot r in snapshot.Rulers)
        {
            var id = new RulerId(r.Id.Index, r.Id.Generation);
            var ruler = new Ruler(
                id,
                r.Name,
                r.BirthYear,
                r.Administration,
                r.Military,
                r.AccessionYear,
                new PolityId(r.Polity.Index, r.Polity.Generation))
            {
                DeathYear = r.DeathYear,
                ReignEndYear = r.ReignEndYear,
                EndReason = (ReignEndReason?)r.EndReason,
            };
            world.Rulers.RestoreSlot(r.Id.Index, r.Id.Generation, ruler);
        }

        return world;
    }

    public static string Serialize(SaveGame save) => JsonSerializer.Serialize(save, Options);

    public static SaveGame Deserialize(string json) =>
        JsonSerializer.Deserialize<SaveGame>(json, Options)
        ?? throw new SaveIntegrityException("Save file deserialized to null.");

    /// <summary>Deserializes and verifies that the restored world hashes to the recorded value.</summary>
    public static WorldState Load(string json, out SaveGame save)
    {
        save = Deserialize(json);

        if (save.Header.EngineVersion != EngineVersion)
        {
            throw new SaveIntegrityException(
                $"Save was written by engine {save.Header.EngineVersion}; this is {EngineVersion}.");
        }

        WorldState world = Restore(save.World);
        ulong hash = WorldHasher.Hash(world);

        if (hash != save.Header.StateHash)
        {
            throw new SaveIntegrityException(
                $"State hash mismatch: expected {save.Header.StateHash:x16}, restored {hash:x16}.");
        }

        return world;
    }

    private static IdRef Ref<TKind>(EntityId<TKind> id)
        where TKind : IEntityKind => new(id.Index, id.Generation);
}
