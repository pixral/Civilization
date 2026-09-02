namespace Civ.Engine.Random;

/// <summary>
/// Integer hashing primitives. Fixed algorithms, no framework hashing.
/// </summary>
/// <remarks>
/// <c>string.GetHashCode</c> and <c>HashCode.Combine</c> are randomised per process in .NET.
/// Using either anywhere that touches simulation state or stream identity would make runs
/// irreproducible across processes. Everything here is a fixed, documented algorithm.
/// </remarks>
public static class Hash64
{
    public const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    /// <summary>splitmix64 finalizer. Good avalanche, no state.</summary>
    public static ulong Mix(ulong x)
    {
        x ^= x >> 33;
        x = unchecked(x * 0xFF51AFD7ED558CCDUL);
        x ^= x >> 33;
        x = unchecked(x * 0xC4CEB9FE1A85EC53UL);
        x ^= x >> 33;
        return x;
    }

    /// <summary>Order-dependent combination of several values into one.</summary>
    public static ulong Combine(params ulong[] values)
    {
        ulong acc = FnvOffsetBasis;
        foreach (ulong v in values)
        {
            acc = Mix(acc ^ Mix(v));
        }

        return acc;
    }

    /// <summary>Folds one value into a running hash. Used by the canonical state hasher.</summary>
    public static ulong Step(ulong acc, ulong value) => Mix(acc ^ Mix(value));

    /// <summary>FNV-1a over UTF-16 code units. Stable across processes and platforms.</summary>
    public static ulong OfString(string s)
    {
        ulong acc = FnvOffsetBasis;
        foreach (char c in s)
        {
            acc ^= c;
            acc = unchecked(acc * FnvPrime);
        }

        return acc;
    }
}
