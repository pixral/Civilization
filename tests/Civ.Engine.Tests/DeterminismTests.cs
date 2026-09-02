using Civ.Engine.Config;
using Civ.Engine.Events;
using Civ.Engine.Systems;
using Civ.Systems;

namespace Civ.Engine.Tests;

/// <summary>
/// The reproducibility guarantee.
/// </summary>
/// <remarks>
/// These are the most important tests in the project. Without them a bug found at year 1400 of
/// seed 9 is an anecdote rather than something that can be reproduced, bisected and fixed, and
/// every balance observation is unrepeatable. They are cheap now and become impossible to add
/// later once something has quietly introduced wall-clock time, hash ordering or shared RNG.
/// </remarks>
public sealed class DeterminismTests
{
    private static SimulationConfig Config(ulong seed) => SimulationConfig.Default with
    {
        Seed = seed,
        WorldWidth = 8,
        WorldHeight = 6,
        InitialPolityCount = 5,
        InvariantMode = InvariantMode.Periodic,
        InvariantInterval = 50,
    };

    private static Simulation Run(ulong seed, int years, IEnumerable<ISimulationSystem>? systems = null)
    {
        Simulation sim = Simulation.Create(Config(seed), systems ?? DefaultSystems.Build());
        sim.AdvanceYears(years);
        return sim;
    }

    [Fact]
    public void SameSeedProducesIdenticalWorldAfterLongRun()
    {
        Simulation a = Run(seed: 42, years: 750);
        Simulation b = Run(seed: 42, years: 750);

        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void SameSeedProducesIdenticalChronicle()
    {
        Simulation a = Run(seed: 7, years: 400);
        Simulation b = Run(seed: 7, years: 400);

        Assert.Equal(a.Chronicle.Count, b.Chronicle.Count);

        for (int i = 0; i < a.Chronicle.Count; i++)
        {
            SimEvent left = a.Chronicle.Events[i];
            SimEvent right = b.Chronicle.Events[i];

            // Records compare structurally, so this checks every field of every event.
            Assert.Equal(left, right);
        }
    }

    [Fact]
    public void HashDivergesYearByYearOnlyWhenSeedsDiffer()
    {
        Simulation a = Simulation.Create(Config(1), DefaultSystems.Build());
        Simulation b = Simulation.Create(Config(1), DefaultSystems.Build());
        Simulation c = Simulation.Create(Config(2), DefaultSystems.Build());

        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.NotEqual(a.StateHash(), c.StateHash());

        for (int year = 0; year < 200; year++)
        {
            a.AdvanceYear();
            b.AdvanceYear();
            c.AdvanceYear();
            Assert.Equal(a.StateHash(), b.StateHash());
        }

        Assert.NotEqual(a.StateHash(), c.StateHash());
    }

    [Fact]
    public void ResumingFromAnIntermediateStateMatchesAContinuousRun()
    {
        // Advancing in two steps must equal advancing in one. This is what makes "step a year" in
        // the terminal and "run 500 years" in the batch runner the same simulation.
        Simulation continuous = Run(seed: 99, years: 300);

        Simulation stepped = Simulation.Create(Config(99), DefaultSystems.Build());
        stepped.AdvanceYears(120);
        stepped.AdvanceYears(180);

        Assert.Equal(continuous.StateHash(), stepped.StateHash());
    }

    /// <summary>
    /// The property that makes the system pipeline extensible: a new system cannot perturb the
    /// results of the ones already there.
    /// </summary>
    /// <remarks>
    /// This is the single test that justifies deriving random streams by name rather than sharing a
    /// generator. Without it, adding any system invalidates every previous balance observation, and
    /// tuning becomes archaeology.
    /// </remarks>
    [Fact]
    public void AddingSystemsDoesNotChangeExistingSystemsOutcomes()
    {
        Simulation baseline = Run(seed: 5, years: 250);

        // Built from the real pipeline rather than a hand-listed copy of it, so this keeps testing
        // the property instead of quietly becoming a comparison of two stale lists.
        var withExtras = new List<ISimulationSystem>
        {
            new NoOpSystem("test.noop.environment", SimulationPhase.Environment),
            new NoOpSystem("test.noop.economy", SimulationPhase.Economy),
            new NoOpSystem("test.noop.diplomacy", SimulationPhase.Diplomacy),
            new NoOpSystem("test.noop.bookkeeping", SimulationPhase.Bookkeeping),
        };
        withExtras.AddRange(DefaultSystems.Build());

        Simulation extended = Run(seed: 5, years: 250, withExtras);

        Assert.Equal(baseline.StateHash(), extended.StateHash());
    }

    /// <summary>
    /// Reordering systems within a phase changes nothing, because they read the same snapshot and
    /// their randomness comes from their names rather than their position.
    /// </summary>
    [Fact]
    public void ReorderingSystemsWithinAPhaseDoesNotChangeOutcomes()
    {
        // The orderings must move PopulationSystem to a genuinely different pipeline index,
        // otherwise this passes even if stream identity were positional rather than name-derived.
        var forward = new List<ISimulationSystem>
        {
            new PopulationSystem(),
            new NoOpSystem("test.a", SimulationPhase.Population),
            new NoOpSystem("test.b", SimulationPhase.Population),
            new OpportunisticExpansionSystem(),
            new PolityLifecycleSystem(),
        };

        var reversed = new List<ISimulationSystem>
        {
            new PolityLifecycleSystem(),
            new OpportunisticExpansionSystem(),
            new NoOpSystem("test.b", SimulationPhase.Population),
            new NoOpSystem("test.a", SimulationPhase.Population),
            new PopulationSystem(),
        };

        Assert.Equal(
            Run(seed: 11, years: 200, forward).StateHash(),
            Run(seed: 11, years: 200, reversed).StateHash());
    }

    [Fact]
    public void WorldGenerationAloneIsDeterministic()
    {
        Simulation a = Simulation.Create(Config(123), []);
        Simulation b = Simulation.Create(Config(123), []);

        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(
            a.World.Regions.All().Select(r => r.Name),
            b.World.Regions.All().Select(r => r.Name));
    }

    [Fact]
    public void DifferentSeedsProduceDifferentWorlds()
    {
        var hashes = new HashSet<ulong>();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            hashes.Add(Run(seed, years: 100).StateHash());
        }

        // Not a strict requirement of the design, but 20 identical worlds would mean the seed is
        // not reaching worldgen at all.
        Assert.Equal(20, hashes.Count);
    }
}
