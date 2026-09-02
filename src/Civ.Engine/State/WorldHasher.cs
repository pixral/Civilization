using Civ.Engine.Random;

namespace Civ.Engine.State;

/// <summary>
/// Canonical hash of the whole world. Two runs are identical if and only if their hashes match at
/// every year.
/// </summary>
/// <remarks>
/// This is the workhorse of the determinism tests and of the batch runner's cross-run comparison.
/// It is exact because no simulation state is floating point - see <see cref="Region.Population"/>.
/// The moment a float lands in <see cref="WorldState"/>, this hash stops being a portable identity
/// and starts being a same-machine one.
/// </remarks>
public static class WorldHasher
{
    public static ulong Hash(WorldState world)
    {
        ulong acc = Hash64.FnvOffsetBasis;
        acc = Hash64.Step(acc, world.Seed);
        acc = Hash64.Step(acc, unchecked((ulong)(long)world.Year));
        acc = Hash64.Step(acc, (ulong)world.Regions.Count);
        acc = Hash64.Step(acc, (ulong)world.Polities.Count);
        acc = Hash64.Step(acc, (ulong)world.Rulers.Count);

        // Ascending slot order. Never hash-map order.
        foreach (Region region in world.Regions.All())
        {
            acc = Hash64.Step(acc, (ulong)region.Id.Index);
            acc = Hash64.Step(acc, (ulong)region.Id.Generation);
            acc = Hash64.Step(acc, Hash64.OfString(region.Name));
            acc = Hash64.Step(acc, (ulong)region.Terrain);
            acc = Hash64.Step(acc, (ulong)region.Fertility);
            acc = Hash64.Step(acc, unchecked((ulong)region.Population));
            acc = Hash64.Step(acc, (ulong)region.Controller.Index);
            acc = Hash64.Step(acc, (ulong)region.Controller.Generation);

            foreach (RegionId neighbor in region.Neighbors)
            {
                acc = Hash64.Step(acc, (ulong)neighbor.Index);
            }
        }

        foreach (Polity polity in world.Polities.All())
        {
            acc = Hash64.Step(acc, (ulong)polity.Id.Index);
            acc = Hash64.Step(acc, (ulong)polity.Id.Generation);
            acc = Hash64.Step(acc, Hash64.OfString(polity.Name));
            acc = Hash64.Step(acc, (ulong)polity.Capital.Index);
            acc = Hash64.Step(acc, unchecked((ulong)(long)polity.FoundedYear));
            acc = Hash64.Step(acc, unchecked((ulong)(long)(polity.DissolvedYear ?? -1)));
            acc = Hash64.Step(acc, (ulong)polity.Status);
            acc = Hash64.Step(acc, (ulong)polity.Parent.Index);
            acc = Hash64.Step(acc, unchecked((ulong)(long)polity.Stability));
            acc = Hash64.Step(acc, (ulong)polity.CurrentRuler.Index);
            acc = Hash64.Step(acc, (ulong)polity.CurrentRuler.Generation);
        }

        // Rulers are part of the world's identity, so the determinism tests cover the character
        // layer without needing anything of their own.
        foreach (Ruler ruler in world.Rulers.All())
        {
            acc = Hash64.Step(acc, (ulong)ruler.Id.Index);
            acc = Hash64.Step(acc, (ulong)ruler.Id.Generation);
            acc = Hash64.Step(acc, Hash64.OfString(ruler.Name));
            acc = Hash64.Step(acc, unchecked((ulong)(long)ruler.BirthYear));
            acc = Hash64.Step(acc, unchecked((ulong)(long)ruler.Administration));
            acc = Hash64.Step(acc, unchecked((ulong)(long)ruler.Military));
            acc = Hash64.Step(acc, unchecked((ulong)(long)ruler.AccessionYear));
            acc = Hash64.Step(acc, unchecked((ulong)(long)(ruler.DeathYear ?? -1)));
            acc = Hash64.Step(acc, unchecked((ulong)(long)(ruler.ReignEndYear ?? -1)));
            acc = Hash64.Step(acc, unchecked((ulong)(long)((int?)ruler.EndReason ?? -1)));
            acc = Hash64.Step(acc, (ulong)ruler.Polity.Index);
        }

        return acc;
    }
}
