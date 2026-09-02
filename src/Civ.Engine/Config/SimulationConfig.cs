using Civ.Engine.Random;

namespace Civ.Engine.Config;

public enum InvariantMode
{
    Off = 0,

    /// <summary>Default for tests and development. Cheap at this scale.</summary>
    EveryTick = 1,

    /// <summary>Every <c>InvariantInterval</c> years. For long batch sweeps.</summary>
    Periodic = 2,
}

/// <summary>
/// Everything that, together with the seed, determines a run.
/// </summary>
/// <remarks>
/// <para>A run's identity is <c>(engine version, config, seed)</c>. That triple is what save files
/// record and what the batch runner reports, because it is the only thing needed to reproduce a
/// world exactly - which in turn is why a bug found at year 1400 of seed 9 is a fixable bug rather
/// than an anecdote.</para>
///
/// <para><see cref="Hash"/> covers every field that affects the simulation. A new field that
/// changes outcomes must be added to it, or two different configs will claim to be the same run.</para>
/// </remarks>
public sealed record SimulationConfig
{
    public ulong Seed { get; init; } = 1;

    public int StartYear { get; init; } = 1;

    /// <summary>Regions are laid out on a rectangular adjacency grid. Not a map; just a graph.</summary>
    public int WorldWidth { get; init; } = 6;

    public int WorldHeight { get; init; } = 4;

    public int InitialPolityCount { get; init; } = 4;

    public long InitialRegionPopulationMin { get; init; } = 800;

    public long InitialRegionPopulationMax { get; init; } = 4_000;

    public InvariantMode InvariantMode { get; init; } = InvariantMode.EveryTick;

    public int InvariantInterval { get; init; } = 25;

    /// <summary>Fail fast instead of accumulating violations. On in tests, off in the terminal app.</summary>
    public bool ThrowOnInvariantViolation { get; init; }

    public int RegionCount => WorldWidth * WorldHeight;

    public static SimulationConfig Default => new();

    /// <summary>
    /// Identity of the configuration. Two runs with the same engine version, config hash and seed
    /// must produce byte-identical histories.
    /// </summary>
    public ulong Hash() => Hash64.Combine(
        Seed,
        unchecked((ulong)(long)StartYear),
        unchecked((ulong)(long)WorldWidth),
        unchecked((ulong)(long)WorldHeight),
        unchecked((ulong)(long)InitialPolityCount),
        unchecked((ulong)InitialRegionPopulationMin),
        unchecked((ulong)InitialRegionPopulationMax));

    public void Validate()
    {
        if (WorldWidth < 1 || WorldHeight < 1)
        {
            throw new ArgumentException("World dimensions must be positive.");
        }

        if (InitialPolityCount < 0 || InitialPolityCount > RegionCount)
        {
            throw new ArgumentException("Initial polity count must fit inside the world.");
        }

        if (InitialRegionPopulationMin < 0 || InitialRegionPopulationMax < InitialRegionPopulationMin)
        {
            throw new ArgumentException("Initial population range is inverted or negative.");
        }
    }
}
