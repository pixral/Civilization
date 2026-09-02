using Civ.Engine.Effects;
using Civ.Engine.Random;
using Civ.Engine.State;
using Civ.Engine.Systems;
using Civ.Engine.Worldgen;

namespace Civ.Systems;

/// <summary>
/// Territory a polity can no longer govern breaks away as a new state.
/// </summary>
/// <remarks>
/// <para><b>The counter-force to conquest.</b> <see cref="OpportunisticExpansionSystem"/> can only
/// ever reduce the number of polities; this is what puts them back. The two are coupled through the
/// same quantity - territory - so growth directly produces the strain that fragments it, which is
/// what allows polity count to rise as well as fall instead of decaying to a single world empire.</para>
///
/// <para><b>This is not a rebellion model.</b> There are no factions, grievances, leaders, ideology
/// or civil war. A region is either governable or it is not, and ungovernable territory leaves.
/// Everything that would make a secession a story rather than a bookkeeping event is missing on
/// purpose.</para>
///
/// <para><b>Strain against authority, both state-derived.</b> Each region strains its polity by its
/// distance from the capital <i>measured through the polity's own territory</i>, by whether it is
/// cut off from the capital entirely, by how much else the polity holds, and by how rich it is
/// relative to its polity. Authority is a flat administrative capacity adjusted by stability. Where
/// strain exceeds authority the region is restive. Below that line nothing happens however many
/// centuries pass; above it, the margin sets an annual probability.</para>
///
/// <para><b>Breakaways are coherent.</b> The candidate is a connected component of restive regions,
/// not a scattering of individual cells, so successor states come out as contiguous territory with a
/// real border. The largest eligible component secedes; ties break on the lowest region index.</para>
///
/// <para><b>The capital never secedes.</b> The parent therefore always keeps at least one region,
/// so no secession can dissolve a state outright by replacing it with its own successor. This is
/// not what keeps the world valid - <c>PolityLifecycleSystem</c> would reseat or retire the parent
/// either way, and removing the guard produces no invariant violation. It is a modelling choice:
/// a capital walking out of its own country is a different event, and it belongs to the civil-war
/// model that does not exist yet.</para>
/// </remarks>
public sealed class CohesionSecessionSystem(CohesionRules? rules = null) : ISimulationSystem
{
    /// <summary>Reason recorded on foundings and transfers caused by this system.</summary>
    public const string SecessionReason = "secession";

    private readonly CohesionRules _rules = Validated(rules ?? CohesionRules.Default);

    public string Name => "polity.cohesion";

    public SimulationPhase Phase => SimulationPhase.Polity;

    public void Execute(in SystemContext context)
    {
        WorldState world = context.World;

        foreach (Polity polity in WorldQueries.ActivePolities(world))
        {
            Evaluate(in context, world, polity);
        }
    }

    /// <summary>One region's contribution to its polity's cohesion problem.</summary>
    public readonly record struct RegionStrain(int Strain, int Distance, bool Connected);

    /// <summary>
    /// Strain on every region a polity holds, excluding its capital.
    /// </summary>
    /// <remarks>
    /// Public because the batch runner needs to ask what the cohesion rule <i>would</i> decide under
    /// a different ruler. Exposing the real computation rather than letting the analysis reimplement
    /// it is the only way a diagnostic can stay honest as the rule changes.
    /// </remarks>
    public static Dictionary<RegionId, RegionStrain> StrainMap(
        WorldState world, Polity polity, CohesionRules rules, int administration)
    {
        var strains = new Dictionary<RegionId, RegionStrain>();
        List<Region> held = [.. WorldQueries.RegionsOf(world, polity.Id)];

        if (held.Count == 0 || polity.Capital.IsNone)
        {
            return strains;
        }

        Dictionary<RegionId, int> internalDistance = DistancesWithinTerritory(world, polity);

        long total = 0;
        foreach (Region region in held)
        {
            total += region.Population;
        }

        long average = Math.Max(1, total / held.Count);

        foreach (Region region in held)
        {
            if (region.Id.Equals(polity.Capital))
            {
                continue;
            }

            bool connected = internalDistance.TryGetValue(region.Id, out int distance);
            strains[region.Id] = new RegionStrain(
                Strain(rules, region, held.Count, internalDistance, average, administration),
                connected ? distance : -1,
                connected);
        }

        return strains;
    }

    /// <summary>
    /// Rejects a rule set whose ruler conversions do not leave an average ruler unchanged.
    /// </summary>
    /// <remarks>
    /// Here rather than only in the batch runner because every path that builds a simulation goes
    /// through this constructor. A conversion that quietly shifts the neutral point is not a bad
    /// tuning value - it is a different experiment wearing the name of this one - and it should be
    /// impossible to run rather than merely discouraged.
    /// </remarks>
    private static CohesionRules Validated(CohesionRules rules)
    {
        rules.Validate();
        return rules;
    }

    /// <summary>The strain a polity can absorb, given who is ruling it.</summary>
    public static int Authority(Polity polity, CohesionRules rules, int administration) =>
        rules.EffectiveCapacity(administration)
        + ((polity.Stability - rules.NeutralStability) * rules.StabilityRelief);

    /// <summary>The ruling ability the cohesion rule will actually use for this polity.</summary>
    public static int AdministrationOf(WorldState world, Polity polity, CohesionRules rules) =>
        WorldQueries.AdministrationOf(world, polity, rules.DefaultAdministration);

    private void Evaluate(in SystemContext context, WorldState world, Polity polity)
    {
        List<Region> held = [.. WorldQueries.RegionsOf(world, polity.Id)];
        if (held.Count <= _rules.MinBreakawaySize || polity.Capital.IsNone)
        {
            return;
        }

        // Capacity is a property of who is ruling, not a world constant. This single lookup is the
        // entire mechanical effect of the character layer - everything downstream is the same
        // cohesion rule reading a different number.
        int administration = AdministrationOf(world, polity, _rules);
        int authority = Authority(polity, _rules, administration);

        // Restive set, in ascending index order so component discovery is reproducible.
        var restive = new Dictionary<RegionId, int>();
        foreach ((RegionId id, RegionStrain region) in StrainMap(world, polity, _rules, administration))
        {
            if (region.Strain > authority)
            {
                restive[id] = region.Strain - authority;
            }
        }

        if (restive.Count == 0)
        {
            return;
        }

        List<RegionId>? breakaway = LargestComponent(world, polity.Id, restive);
        if (breakaway is null || breakaway.Count < _rules.MinBreakawaySize)
        {
            return;
        }

        // The most detached region in the group sets the pace - a province that is barely governable
        // waits, one that is completely cut off does not.
        int margin = 0;
        foreach (RegionId id in breakaway)
        {
            margin = Math.Max(margin, restive[id]);
        }

        int permille = Math.Clamp(margin / _rules.StrainPerPermille, 1, _rules.MaxAttemptPermille);

        Rng rng = context.Rng((ulong)polity.Id.Index);
        if (!rng.Chance(permille))
        {
            return;
        }

        RegionId capital = ChooseCapital(world, breakaway);
        string name = NameGenerator.Polity(ref rng);

        context.Effects.Emit(new FoundPolity(name, capital, polity.Id, SecessionReason, breakaway));
        context.Effects.Emit(new AdjustPolityStability(
            polity.Id, -_rules.SecessionShock, "loss of a province to secession"));
    }

    private static int Strain(
        CohesionRules rules,
        Region region,
        int held,
        Dictionary<RegionId, int> internalDistance,
        long averagePopulation,
        int administration)
    {
        bool connected = internalDistance.TryGetValue(region.Id, out int distance);

        int strain = rules.SizeStrainPerRegion * (held - 1);

        // Administration can only *reduce* the connected-distance term, and touches nothing else. A
        // province cut off from its capital is cut off whoever is on the throne - no administrator
        // reaches across a rival's territory - so the disconnection penalty is left alone.
        strain += connected
            ? (int)rules.DistanceStrainTerm(distance, administration)
            : rules.DisconnectionStrain;

        // Wealth relative to the rest of the polity. Bounded so one very rich province cannot
        // single-handedly outweigh geography.
        long excess = (region.Population - averagePopulation) * rules.ProsperityStrain / averagePopulation;
        strain += (int)Math.Clamp(excess, -rules.ProsperityStrain, 2L * rules.ProsperityStrain);

        return strain;
    }

    /// <summary>
    /// Hop count from the capital <i>through the polity's own territory</i>.
    /// </summary>
    /// <remarks>
    /// Deliberately not geographic distance. A province two steps away across a rival's land is not
    /// two steps away for the purposes of governing it - it is unreachable, and regions missing from
    /// this map are exactly the exclaves that should be hardest to hold.
    /// </remarks>
    private static Dictionary<RegionId, int> DistancesWithinTerritory(WorldState world, Polity polity)
    {
        var distance = new Dictionary<RegionId, int> { [polity.Capital] = 0 };
        var queue = new Queue<RegionId>();
        queue.Enqueue(polity.Capital);

        while (queue.Count > 0)
        {
            RegionId current = queue.Dequeue();
            int next = distance[current] + 1;

            foreach (RegionId neighbor in world.Regions.Get(current).Neighbors)
            {
                if (!world.Regions.Get(neighbor).Controller.Equals(polity.Id))
                {
                    continue;
                }

                if (distance.TryAdd(neighbor, next))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return distance;
    }

    /// <summary>
    /// The largest contiguous group of restive regions. Ties break on the lowest region index.
    /// </summary>
    /// <remarks>
    /// Components rather than individual cells is what makes successor states look like countries.
    /// Seceding the highest-strain region on its own would scatter one-province statelets across a
    /// large empire instead of splitting off its ungovernable periphery in one piece.
    /// </remarks>
    private static List<RegionId>? LargestComponent(
        WorldState world,
        PolityId polity,
        Dictionary<RegionId, int> restive)
    {
        var visited = new HashSet<RegionId>();
        List<RegionId>? best = null;

        // Ascending order: both the flood-fill seeds and the tie-break depend on it.
        foreach (RegionId seed in restive.Keys.Order())
        {
            if (!visited.Add(seed))
            {
                continue;
            }

            var component = new List<RegionId> { seed };
            var queue = new Queue<RegionId>();
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                RegionId current = queue.Dequeue();
                foreach (RegionId neighbor in world.Regions.Get(current).Neighbors)
                {
                    if (!restive.ContainsKey(neighbor)
                        || !world.Regions.Get(neighbor).Controller.Equals(polity)
                        || !visited.Add(neighbor))
                    {
                        continue;
                    }

                    component.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }

            component.Sort();

            if (best is null || component.Count > best.Count)
            {
                best = component;
            }
        }

        return best;
    }

    /// <summary>Most populous region in the breakaway; ties break on the lowest index.</summary>
    private static RegionId ChooseCapital(WorldState world, List<RegionId> breakaway)
    {
        RegionId best = breakaway[0];
        long bestPopulation = world.Regions.Get(best).Population;

        foreach (RegionId id in breakaway)
        {
            long population = world.Regions.Get(id).Population;
            if (population > bestPopulation)
            {
                bestPopulation = population;
                best = id;
            }
        }

        return best;
    }
}
