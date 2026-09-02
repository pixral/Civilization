using Civ.Engine.Config;
using Civ.Engine.Persistence;
using Civ.Engine.State;
using Civ.Systems;

namespace Civ.Engine.Tests;

/// <summary>
/// Round-trip tests for the snapshot format.
/// </summary>
/// <remarks>
/// The persistence layer is deliberately minimal, so these tests are really about the shape of
/// <see cref="WorldState"/>: flat, integral, id-referenced, no back-pointers. If a save ever stops
/// round-tripping, the usual cause is not the serializer but a new field that reintroduced an
/// object graph.
/// </remarks>
public sealed class PersistenceTests
{
    private static Simulation Run(int years)
    {
        Simulation sim = Simulation.Create(
            SimulationConfig.Default with
            {
                Seed = 4242,
                WorldWidth = 5,
                WorldHeight = 4,
                InitialPolityCount = 3,
            },
            DefaultSystems.Build());

        sim.AdvanceYears(years);
        return sim;
    }

    [Fact]
    public void RoundTripPreservesTheStateHash()
    {
        Simulation sim = Run(120);
        ulong before = sim.StateHash();

        string json = SaveIO.Serialize(SaveIO.Capture(sim.World, sim.Config));
        WorldState restored = SaveIO.Load(json, out SaveGame save);

        Assert.Equal(before, WorldHasher.Hash(restored));
        Assert.Equal(before, save.Header.StateHash);
        Assert.Equal(sim.Year, restored.Year);
        Assert.Equal(sim.Config.Hash(), save.Header.ConfigHash);
    }

    [Fact]
    public void RoundTripPreservesEntityIdentity()
    {
        Simulation sim = Run(60);
        WorldState restored = SaveIO.Restore(SaveIO.Snapshot(sim.World));

        foreach (Region original in sim.World.Regions.All())
        {
            // Same handle, same entity - so every id already recorded in an event still resolves.
            Region copy = restored.Regions.Get(original.Id);
            Assert.Equal(original.Name, copy.Name);
            Assert.Equal(original.Population, copy.Population);
            Assert.Equal(original.Controller, copy.Controller);
            Assert.Equal(original.Neighbors, copy.Neighbors);
        }

        foreach (Polity original in sim.World.Polities.All())
        {
            Polity copy = restored.Polities.Get(original.Id);
            Assert.Equal(original.Name, copy.Name);
            Assert.Equal(original.Status, copy.Status);
            Assert.Equal(original.Capital, copy.Capital);
            Assert.Equal(original.FoundedYear, copy.FoundedYear);
            Assert.Equal(original.DissolvedYear, copy.DissolvedYear);
        }
    }

    [Fact]
    public void TamperedSavesAreRejected()
    {
        Simulation sim = Run(30);
        SaveGame save = SaveIO.Capture(sim.World, sim.Config);

        SaveGame tampered = save with
        {
            World = save.World with { Year = save.World.Year + 1 },
        };

        Assert.Throws<SaveIntegrityException>(
            () => SaveIO.Load(SaveIO.Serialize(tampered), out _));
    }

    [Fact]
    public void SavesFromOtherEngineVersionsAreRejected()
    {
        Simulation sim = Run(10);
        SaveGame save = SaveIO.Capture(sim.World, sim.Config);
        SaveGame stale = save with { Header = save.Header with { EngineVersion = "0.0.1-ancient" } };

        Assert.Throws<SaveIntegrityException>(() => SaveIO.Load(SaveIO.Serialize(stale), out _));
    }

    /// <summary>
    /// A loaded world continues identically to one that was never saved.
    /// </summary>
    /// <remarks>
    /// The real test of the state design: if anything a system reads lives outside
    /// <see cref="WorldState"/>, the two runs diverge here.
    /// </remarks>
    [Fact]
    public void ALoadedWorldContinuesIdenticallyToOneThatWasNeverSaved()
    {
        Simulation continuous = Run(100);
        Simulation saved = Run(100);

        WorldState restored = SaveIO.Restore(SaveIO.Snapshot(saved.World));

        continuous.AdvanceYears(50);

        // Replay the same 50 years against the restored state using an identical pipeline.
        Simulation resumed = Simulation.Resume(saved.Config, restored, DefaultSystems.Build());
        resumed.AdvanceYears(50);

        Assert.Equal(continuous.StateHash(), resumed.StateHash());
    }
}
