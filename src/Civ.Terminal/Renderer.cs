using System.Text;
using Civ.Engine;
using Civ.Engine.Events;
using Civ.Engine.State;

namespace Civ.Terminal;

/// <summary>
/// Turns simulation state into text.
/// </summary>
/// <remarks>
/// <para>Strictly one-directional: the renderer reads the world and the chronicle and returns
/// strings. It holds no state, decides nothing, and the engine has never heard of it. Swapping this
/// for a full-screen TUI later touches nothing else.</para>
///
/// <para>Formatting lives here rather than on the events for the same reason. <c>SimEvent.Text</c>
/// is a fallback; the structured fields are the real payload, and a presentation layer that wants to
/// group by polity or phrase things differently is free to ignore it.</para>
/// </remarks>
public static class Renderer
{
    public static string PolityPanel(Simulation sim, PolityId polityId)
    {
        WorldState world = sim.World;
        var sb = new StringBuilder();

        if (!world.Polities.TryGet(polityId, out Polity? polity))
        {
            return $"YEAR {world.Year} — no polity in focus\n";
        }

        long population = WorldQueries.PopulationOf(world, polityId);
        int regions = WorldQueries.RegionCountOf(world, polityId);
        string capital = WorldQueries.NameOf(world, polity.Capital);

        sb.AppendLine($"YEAR {world.Year} — {polity.Name}");
        sb.AppendLine($"  Population : {population:N0}");
        sb.AppendLine($"  Regions    : {regions}");
        sb.AppendLine($"  Seat       : {capital}");
        sb.AppendLine($"  Stability  : {polity.Stability}%");
        sb.AppendLine($"  Founded    : {polity.FoundedYear}"
            + (polity.DissolvedYear is { } d ? $"   Dissolved: {d}" : string.Empty));

        return sb.ToString();
    }

    public static string WorldSummary(Simulation sim)
    {
        WorldState world = sim.World;
        var sb = new StringBuilder();

        sb.AppendLine($"WORLD  year {world.Year}   seed {world.Seed}   hash {sim.StateHash():x16}");
        sb.AppendLine(
            $"  regions {world.Regions.Count}"
            + $"   polities {WorldQueries.ActivePolities(world).Count()} active"
            + $" / {world.Polities.Count} recorded"
            + $"   population {WorldQueries.WorldPopulation(world):N0}");

        return sb.ToString();
    }

    public static string PolityList(Simulation sim)
    {
        var sb = new StringBuilder();
        sb.AppendLine("POLITIES");

        foreach (Polity polity in sim.World.Polities.All())
        {
            long population = WorldQueries.PopulationOf(sim.World, polity.Id);
            int regions = WorldQueries.RegionCountOf(sim.World, polity.Id);
            string status = polity.IsActive ? "active " : "defunct";

            sb.AppendLine(
                $"  [{polity.Id.Index,2}] {status}  {polity.Name,-28} "
                + $"regions {regions,3}   pop {population,12:N0}   stability {polity.Stability,3}");
        }

        return sb.ToString();
    }

    public static string Events(Chronicle chronicle, int fromYear, int toYear, Salience minimum)
    {
        var sb = new StringBuilder();
        var events = chronicle.Between(fromYear, toYear).Where(e => e.Salience >= minimum).ToList();

        sb.AppendLine(events.Count == 0 ? "WORLD EVENTS  (none)" : "WORLD EVENTS");
        foreach (SimEvent e in events)
        {
            sb.AppendLine($"  • [{e.Year,5}] {e.Text}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// A crude territorial map.
    /// </summary>
    /// <remarks>
    /// Included this early because the terminal has no other way to convey space, and a history
    /// simulation whose borders the reader cannot picture reduces every war, migration and collapse
    /// to numbers changing. One character per region, keyed by controlling polity index.
    /// </remarks>
    public static string Map(Simulation sim, int width)
    {
        var sb = new StringBuilder();
        var regions = sim.World.Regions.All().ToList();

        sb.AppendLine("TERRITORY");
        for (int i = 0; i < regions.Count; i++)
        {
            if (i % width == 0)
            {
                sb.Append("  ");
            }

            Region region = regions[i];
            char glyph = region.Controller.IsNone
                ? '.'
                : (char)('A' + (region.Controller.Index % 26));

            sb.Append(glyph).Append(' ');

            if ((i + 1) % width == 0)
            {
                sb.AppendLine();
            }
        }

        if (regions.Count % width != 0)
        {
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
