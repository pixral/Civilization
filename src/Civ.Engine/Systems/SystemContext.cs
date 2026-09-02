using Civ.Engine.Effects;
using Civ.Engine.Random;
using Civ.Engine.State;

namespace Civ.Engine.Systems;

/// <summary>
/// Everything a system is allowed to touch during one execution.
/// </summary>
/// <remarks>
/// A read-only world, a random source scoped to this system and year, and one outbound channel.
/// There is deliberately no service locator, no event bus and no reference to the pipeline: a
/// system that cannot reach other systems cannot develop hidden ordering requirements with them.
/// </remarks>
public readonly struct SystemContext
{
    private readonly ulong _seed;
    private readonly ulong _streamId;

    internal SystemContext(WorldState world, int year, IEffectSink effects, ulong seed, ulong streamId)
    {
        World = world;
        Year = year;
        Effects = effects;
        _seed = seed;
        _streamId = streamId;
    }

    /// <summary>The world as it stood when this phase began. Read-only by construction.</summary>
    public WorldState World { get; }

    public int Year { get; }

    /// <summary>The only way to change anything.</summary>
    public IEffectSink Effects { get; }

    /// <summary>
    /// A generator for this system, this year, and this discriminator.
    /// </summary>
    /// <remarks>
    /// Pass an entity index as the discriminator when drawing per entity. Independent streams per
    /// entity mean a region's outcome does not depend on how many regions were processed before it,
    /// so iteration order and entity count stop being hidden inputs to the result.
    /// </remarks>
    public Rng Rng(ulong discriminator = 0) =>
        RngStreams.Create(_seed, _streamId, Year, discriminator);
}
