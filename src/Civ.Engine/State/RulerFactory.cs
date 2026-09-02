using Civ.Engine.Random;
using Civ.Engine.Worldgen;

namespace Civ.Engine.State;

/// <summary>
/// Generates a ruler profile when no caller supplied one.
/// </summary>
/// <remarks>
/// <para>Engine-side fallback, on its own derived stream. It exists so that <i>any</i> path that
/// creates a polity - worldgen, secession, a test emitting a raw <c>FoundPolity</c> - produces a
/// state with a ruler, and the "every active polity has exactly one" invariant can never be broken
/// by a caller who simply did not think about it. The dissolution cascade in the applier follows the
/// same principle.</para>
///
/// <para>The discriminator is the id of the slot the ruler is about to occupy, not the polity, so
/// several rulers created in the same year - a death, a succession and a founding - cannot collide
/// into identical people.</para>
///
/// <para>Ability is the mean of three uniform draws, which puts most rulers near 50 and makes the
/// extremes genuinely uncommon rather than merely less likely.</para>
/// </remarks>
internal static class RulerFactory
{
    private static readonly ulong Stream = RngStreams.Id("engine.rulers");

    internal static RulerProfile Generate(WorldState world, int accessionAgeMin, int accessionAgeMax)
    {
        Rng rng = RngStreams.Create(world.Seed, Stream, world.Year, (ulong)world.Rulers.Capacity);

        int age = rng.NextInt(accessionAgeMin, accessionAgeMax + 1);
        int administration = (rng.NextInt(0, 101) + rng.NextInt(0, 101) + rng.NextInt(0, 101)) / 3;
        int military = (rng.NextInt(0, 101) + rng.NextInt(0, 101) + rng.NextInt(0, 101)) / 3;
        string name = NameGenerator.Person(ref rng);

        return new RulerProfile(name, world.Year - age, administration, military);
    }

    internal static RulerProfile Generate(WorldState world) => Generate(world, 20, 45);
}
