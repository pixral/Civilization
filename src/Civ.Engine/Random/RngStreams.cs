namespace Civ.Engine.Random;

/// <summary>
/// Derives independent random streams from a world seed.
/// </summary>
/// <remarks>
/// A stream is identified by a hash of the consumer's <i>name</i>, not by its position in the
/// pipeline. Combined with the fact that generators are built per call from
/// <c>(seed, stream, year, discriminator)</c> rather than carried forward, this gives the property
/// the architecture depends on:
///
/// <para><b>Adding, removing, or reordering a system cannot change any other system's rolls.</b>
/// Without it, every balance change silently invalidates every previous observation, and tuning
/// becomes guesswork. <c>RngStreamTests</c> asserts it.</para>
///
/// <para>Renaming a system <i>does</i> change its own stream. That is intentional and cheap:
/// a rename is a new consumer.</para>
/// </remarks>
public static class RngStreams
{
    /// <summary>Stable stream identifier for a named consumer.</summary>
    public static ulong Id(string name) => Hash64.OfString(name);

    /// <summary>
    /// Builds a generator for one coordinate in the run.
    /// </summary>
    /// <param name="seed">The world seed.</param>
    /// <param name="streamId">Consumer identity, from <see cref="Id"/>.</param>
    /// <param name="year">Simulation year, so the same consumer differs across ticks.</param>
    /// <param name="discriminator">
    /// Usually an entity index, so per-entity draws are independent of iteration order and of how
    /// many entities were processed first.
    /// </param>
    public static Rng Create(ulong seed, ulong streamId, long year, ulong discriminator = 0) =>
        new(Hash64.Combine(seed, streamId, unchecked((ulong)year), discriminator));
}
