namespace Civ.Engine.Random;

/// <summary>
/// A splitmix64 generator. A value type with no shared state and no allocation.
/// </summary>
/// <remarks>
/// These are created on demand from a hashed coordinate - see <see cref="RngStreams"/> - and
/// discarded at the end of the call. Nothing carries a generator across ticks, which is why
/// one system drawing more numbers can never shift another system's sequence.
/// </remarks>
public struct Rng
{
    private ulong _state;

    public Rng(ulong seed) => _state = seed;

    public ulong NextUInt64()
    {
        _state = unchecked(_state + 0x9E3779B97F4A7C15UL);
        return Hash64.Mix(_state);
    }

    /// <summary>Uniform in [0, bound). Rejection-free Lemire multiply-shift; bias is below 2^-64.</summary>
    public uint NextUInt32(uint bound)
    {
        if (bound == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bound), "bound must be positive.");
        }

        ulong product = (ulong)(uint)(NextUInt64() >> 32) * bound;
        return (uint)(product >> 32);
    }

    /// <summary>Uniform in [minInclusive, maxExclusive).</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "range must be non-empty.");
        }

        uint span = (uint)(maxExclusive - minInclusive);
        return minInclusive + (int)NextUInt32(span);
    }

    /// <summary>Uniform in [0, 1). Present for convenience; simulation state itself stays integral.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>True with probability <paramref name="permille"/>/1000. Integer-only, so no float drift.</summary>
    public bool Chance(int permille) => NextInt(0, 1000) < permille;

    /// <summary>Uniform choice from a list. Throws on empty.</summary>
    public T Pick<T>(IReadOnlyList<T> items) =>
        items.Count > 0
            ? items[NextInt(0, items.Count)]
            : throw new ArgumentException("cannot pick from an empty list.", nameof(items));
}
