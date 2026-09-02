using Civ.Engine;
using Civ.Engine.Config;
using Civ.Engine.Events;
using Civ.Engine.State;
using Civ.Systems;
using Civ.Terminal;

// The observer client. It creates a simulation, advances it, and prints what happened.
// It contains no simulation logic whatsoever - if anything here started deciding outcomes, the
// engine would stop being runnable headlessly, which is the property the whole layout exists for.

SimulationConfig config = ParseArgs(args);
Simulation sim = Simulation.Create(config, DefaultSystems.Build());

PolityId focus = sim.World.Polities.AllIds().FirstOrDefault();
int lastShownYear = sim.Year;

Console.WriteLine();
Console.WriteLine(Renderer.WorldSummary(sim));
Console.WriteLine(Renderer.Map(sim, config.WorldWidth));
Console.WriteLine(Renderer.Events(sim.Chronicle, config.StartYear, sim.Year, Salience.Notable));
PrintHelp();

while (true)
{
    Console.Write($"[{sim.Year}] > ");
    string? line = Console.ReadLine();

    if (line is null)
    {
        // EOF: the app is being driven from a pipe. Exiting cleanly matters for scripted demos.
        Console.WriteLine();
        break;
    }

    string command = line.Trim();

    if (command.Length == 0)
    {
        command = "1";
    }

    if (command is "q" or "quit" or "exit")
    {
        break;
    }

    if (command is "h" or "help" or "?")
    {
        PrintHelp();
        continue;
    }

    if (command is "l" or "list")
    {
        Console.WriteLine();
        Console.WriteLine(Renderer.PolityList(sim));
        continue;
    }

    if (command is "m" or "map")
    {
        Console.WriteLine();
        Console.WriteLine(Renderer.Map(sim, config.WorldWidth));
        continue;
    }

    if (command is "w" or "world")
    {
        Console.WriteLine();
        Console.WriteLine(Renderer.WorldSummary(sim));
        continue;
    }

    if (command.StartsWith("f ", StringComparison.Ordinal))
    {
        if (int.TryParse(command[2..].Trim(), out int index))
        {
            PolityId candidate = sim.World.Polities.AllIds().FirstOrDefault(id => id.Index == index);
            if (candidate.IsSome)
            {
                focus = candidate;
                Console.WriteLine();
                Console.WriteLine(Renderer.PolityPanel(sim, focus));
                continue;
            }
        }

        Console.WriteLine("  no such polity");
        continue;
    }

    if (int.TryParse(command, out int years) && years > 0)
    {
        int from = sim.Year + 1;
        sim.AdvanceYears(years);
        lastShownYear = sim.Year;

        Console.WriteLine();
        Console.WriteLine(Renderer.PolityPanel(sim, focus));
        Console.WriteLine(Renderer.Events(sim.Chronicle, from, sim.Year, Salience.Notable));

        if (sim.Violations.Count > 0)
        {
            Console.WriteLine($"  !! {sim.Violations.Count} invariant violation(s); latest: {sim.Violations[^1]}");
        }

        continue;
    }

    Console.WriteLine("  unrecognised command; 'h' for help");
}

Console.WriteLine($"Ended at year {lastShownYear}. State hash {sim.StateHash():x16}.");

static void PrintHelp()
{
    Console.WriteLine("COMMANDS");
    Console.WriteLine("  <enter>   advance one year        <n>   advance n years");
    Console.WriteLine("  l         list polities           f <n> focus polity n");
    Console.WriteLine("  m         territory map           w     world summary");
    Console.WriteLine("  q         quit");
    Console.WriteLine();
}

static SimulationConfig ParseArgs(string[] args)
{
    var config = SimulationConfig.Default with { ThrowOnInvariantViolation = false };

    for (int i = 0; i + 1 < args.Length; i += 2)
    {
        string value = args[i + 1];
        config = args[i] switch
        {
            "--seed" => config with { Seed = ulong.Parse(value) },
            "--width" => config with { WorldWidth = int.Parse(value) },
            "--height" => config with { WorldHeight = int.Parse(value) },
            "--polities" => config with { InitialPolityCount = int.Parse(value) },
            "--start-year" => config with { StartYear = int.Parse(value) },
            _ => config,
        };
    }

    config.Validate();
    return config;
}
