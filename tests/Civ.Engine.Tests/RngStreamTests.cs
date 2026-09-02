using Civ.Engine.Random;

namespace Civ.Engine.Tests;

public sealed class RngStreamTests
{
    [Fact]
    public void SameCoordinateProducesSameSequence()
    {
        Rng a = RngStreams.Create(seed: 1, streamId: RngStreams.Id("x"), year: 10, discriminator: 3);
        Rng b = RngStreams.Create(seed: 1, streamId: RngStreams.Id("x"), year: 10, discriminator: 3);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Theory]
    [InlineData(2UL, "x", 10, 3)]
    [InlineData(1UL, "y", 10, 3)]
    [InlineData(1UL, "x", 11, 3)]
    [InlineData(1UL, "x", 10, 4)]
    public void ChangingAnyCoordinateChangesTheSequence(ulong seed, string stream, int year, ulong disc)
    {
        Rng baseline = RngStreams.Create(1, RngStreams.Id("x"), 10, 3);
        Rng variant = RngStreams.Create(seed, RngStreams.Id(stream), year, disc);

        Assert.NotEqual(baseline.NextUInt64(), variant.NextUInt64());
    }

    /// <summary>
    /// The core independence property: how much one consumer draws cannot affect another.
    /// </summary>
    /// <remarks>
    /// With a single shared generator this fails immediately. It is the reason nothing in the engine
    /// carries an <see cref="Rng"/> between systems or across ticks.
    /// </remarks>
    [Fact]
    public void DrawsInOneStreamDoNotAffectAnother()
    {
        ulong streamA = RngStreams.Id("system.a");
        ulong streamB = RngStreams.Id("system.b");

        Rng undisturbed = RngStreams.Create(seed: 77, streamB, year: 5);
        ulong expected = undisturbed.NextUInt64();

        Rng greedy = RngStreams.Create(seed: 77, streamA, year: 5);
        for (int i = 0; i < 10_000; i++)
        {
            _ = greedy.NextUInt64();
        }

        Rng afterwards = RngStreams.Create(seed: 77, streamB, year: 5);
        Assert.Equal(expected, afterwards.NextUInt64());
    }

    [Fact]
    public void StreamIdsAreStableAcrossProcesses()
    {
        // Guards against anyone swapping this for string.GetHashCode, which is randomised per
        // process and would make every run irreproducible in a way that only shows up across runs.
        Assert.Equal(Hash64.OfString("population.growth"), RngStreams.Id("population.growth"));
        Assert.NotEqual(RngStreams.Id("population.growth"), RngStreams.Id("population.growths"));
    }

    [Fact]
    public void NextIntStaysInRange()
    {
        Rng rng = RngStreams.Create(3, RngStreams.Id("range"), 0);
        for (int i = 0; i < 50_000; i++)
        {
            int value = rng.NextInt(-5, 6);
            Assert.InRange(value, -5, 5);
        }
    }

    [Fact]
    public void NextIntCoversItsWholeRange()
    {
        Rng rng = RngStreams.Create(4, RngStreams.Id("coverage"), 0);
        var seen = new HashSet<int>();
        for (int i = 0; i < 20_000; i++)
        {
            seen.Add(rng.NextInt(0, 10));
        }

        Assert.Equal(10, seen.Count);
    }

    [Fact]
    public void ChanceIsApproximatelyCalibrated()
    {
        Rng rng = RngStreams.Create(5, RngStreams.Id("chance"), 0);
        int hits = 0;
        const int trials = 100_000;

        for (int i = 0; i < trials; i++)
        {
            if (rng.Chance(250))
            {
                hits++;
            }
        }

        // Deterministic input, so this is a fixed assertion rather than a flaky statistical one.
        Assert.InRange(hits, (int)(trials * 0.24), (int)(trials * 0.26));
    }

    [Fact]
    public void EmptyPickIsRejected()
    {
        Rng rng = RngStreams.Create(6, RngStreams.Id("pick"), 0);
        Assert.Throws<ArgumentException>(() => rng.Pick(Array.Empty<int>()));
    }
}
