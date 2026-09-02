using Civ.Engine.Effects;
using Civ.Engine.Random;
using Civ.Engine.State;
using Civ.Engine.Systems;

namespace Civ.Systems;

/// <summary>
/// Bounded population change, one region at a time.
/// </summary>
/// <remarks>
/// <para>Trivial on purpose. Its job at this stage is to make the tick loop do something observable
/// so the effect path, the milestone filter and the state hash have real movement to work with.</para>
///
/// <para>The one thing it does take seriously is <b>boundedness</b>. Growth is a logistic pull
/// toward a fertility-derived capacity, so population cannot run away or collapse to zero however
/// long the run is. Every unbounded quantity in a simulation like this is a year-1500 bug, and it is
/// far cheaper to establish the habit on the first system than to retrofit it onto twenty.</para>
///
/// <para>All integer arithmetic, so the world hash stays exact and portable.</para>
/// </remarks>
public sealed class PopulationSystem : ISimulationSystem
{
    public string Name => "population.growth";

    public SimulationPhase Phase => SimulationPhase.Population;

    /// <summary>Supportable population per point of fertility. A stand-in for food, land and technology.</summary>
    private const long CapacityPerFertility = 2_000;

    private const int MaxGrowthPermille = 22;
    private const int JitterPermille = 5;

    public void Execute(in SystemContext context)
    {
        foreach (Region region in context.World.Regions.All())
        {
            long capacity = Math.Max(1, region.Fertility * CapacityPerFertility);

            // Per-entity stream: a region's outcome does not depend on how many regions were
            // processed before it, so iteration order is not a hidden input.
            Rng rng = context.Rng((ulong)region.Id.Index);

            long headroom = capacity - region.Population;
            long pull = MaxGrowthPermille * headroom / capacity;
            int permille = (int)Math.Clamp(pull, -40, MaxGrowthPermille)
                + rng.NextInt(-JitterPermille, JitterPermille + 1);

            long delta = region.Population * permille / 1000;

            // Small populations would otherwise be frozen by integer truncation.
            if (delta == 0 && permille != 0)
            {
                delta = permille > 0 ? 1 : -1;
            }

            if (delta != 0)
            {
                context.Effects.Emit(new AdjustRegionPopulation(region.Id, delta, "natural change"));
            }
        }
    }
}
