using Civ.Engine.Effects;
using Civ.Engine.Random;
using Civ.Engine.State;
using Civ.Engine.Systems;

namespace Civ.Systems;

/// <summary>
/// Polities take adjacent land when they are locally much stronger than whoever holds it.
/// </summary>
/// <remarks>
/// <para><b>This is not a war model.</b> There are no armies, no fronts, no casualties, no peace
/// terms and no diplomacy. It exists to prove that the default pipeline can rewrite political
/// borders safely for thousands of years: transfers go through the effect layer, capitals stay
/// valid, landless states are retired, and every event comes from a change that really happened.
/// A real conflict model will replace it wholesale.</para>
///
/// <para><b>Pressure is state-derived; randomness only sets the timing.</b> Each polity scores its
/// frontier from population, stability, distance from the capital and how much it already holds. If
/// the best score is below <see cref="ExpansionRules.MinPressure"/>, nothing can happen no matter
/// how many centuries pass - there is no "1% chance of conquest per year" anywhere in here. Above
/// the threshold, the margin sets an annual probability, so a decisive advantage is acted on sooner
/// than a marginal one.</para>
///
/// <para><b>Feedback in both directions, and the balance between them is the whole rule.</b>
/// Overextension, reach and conquest strain all push back against a growing polity; mobilisation
/// and the compounding weakness of a shrinking one push the other way. Getting this wrong does not
/// produce a slightly-off simulation, it produces a degenerate one, and it took several rounds of
/// batch sweeps to find out which way:</para>
///
/// <list type="bullet">
/// <item><description>A flat conquest strain made every conqueror briefly weak enough for its
/// victim to take the region straight back, so borders oscillated forever. Strain is now
/// proportional to how marginal the conquest was.</description></item>
/// <item><description>Purely local defence meant losing territory never weakened a polity, so
/// nothing could ever be finished off. Defence now includes what the owner can project.</description></item>
/// <item><description>A strong reach penalty dominates everything, because a shrinking polity
/// concentrates around its capital while a growing one stretches away from its own. At the original
/// value the political map was frozen solid for two thousand years.</description></item>
/// </list>
///
/// <para>Even with all three fixed, the world still would not move until <c>WorldGenerator</c> gave
/// fertility spatial correlation. Independent per-region noise made every polity statistically
/// identical, and no conquest rule can amplify an asymmetry that is not there.</para>
///
/// <para><b>One attempt per polity per year</b>, against its single best target. That bounds churn
/// and makes contested borders genuinely contested - two polities can name the same region in the
/// same phase, which is resolved by the applier and recorded as a conflict rather than silently
/// resolved.</para>
/// </remarks>
public sealed class OpportunisticExpansionSystem(ExpansionRules? rules = null) : ISimulationSystem
{
    /// <summary>Reason string on control changes caused by this system. Used to count expansions.</summary>
    public const string ExpansionReason = "opportunistic expansion";

    /// <summary>Stand-in distance for a region a polity cannot reach. Large, but safe to multiply.</summary>
    private const int Unreachable = 1_000;

    private readonly ExpansionRules _rules = rules ?? ExpansionRules.Default;

    public string Name => "polity.expansion";

    public SimulationPhase Phase => SimulationPhase.Diplomacy;

    public void Execute(in SystemContext context)
    {
        WorldState world = context.World;

        // Hoisted: these are whole-world scans, and they are needed once per candidate region.
        // Both dictionaries are lookup-only - never iterated - so they cannot leak hash ordering
        // into the simulation.
        var power = new Dictionary<PolityId, long>();
        var size = new Dictionary<PolityId, int>();

        foreach (Region region in world.Regions.All())
        {
            if (region.Controller.IsNone)
            {
                continue;
            }

            power[region.Controller] = power.GetValueOrDefault(region.Controller) + region.Population;
            size[region.Controller] = size.GetValueOrDefault(region.Controller) + 1;
        }

        // Reach is computed for every polity, not just the attacker: a region's defence depends on
        // how far it sits from the capital of whoever holds it. That asymmetry is what makes a core
        // defensible and a periphery not, and it is the only thing in the rule that makes political
        // history path-dependent rather than a fixed function of the fertility map.
        var reach = new Dictionary<PolityId, Dictionary<RegionId, int>>();
        foreach (Polity polity in WorldQueries.ActivePolities(world))
        {
            reach[polity.Id] = DistancesFrom(world, polity.Capital);
        }

        foreach (Polity polity in WorldQueries.ActivePolities(world))
        {
            Evaluate(in context, world, polity, power, size, reach);
        }
    }

    private void Evaluate(
        in SystemContext context,
        WorldState world,
        Polity polity,
        Dictionary<PolityId, long> power,
        Dictionary<PolityId, int> size,
        Dictionary<PolityId, Dictionary<RegionId, int>> reach)
    {
        int held = size.GetValueOrDefault(polity.Id);
        if (held == 0)
        {
            // Landless. PolityLifecycleSystem retires it in Bookkeeping; nothing to do here.
            return;
        }

        // One shared lookup, the same one cohesion uses, so the two systems can never disagree about
        // who is ruling or what an absent ruler counts as.
        int administration = WorldQueries.AdministrationOf(world, polity, _rules.NeutralAdministration);
        int military = WorldQueries.MilitaryOf(world, polity, _rules.NeutralMilitary);

        Dictionary<RegionId, int> distance = reach[polity.Id];
        Dictionary<RegionId, long> frontier = Frontier(world, polity.Id);

        RegionId best = RegionId.None;
        PolityId bestDefender = PolityId.None;
        long bestPressure = 0;

        // Sorted iteration. Dictionary order is not stable across runs, and choosing a target from
        // an unordered sequence would make the whole simulation irreproducible.
        foreach (RegionId targetId in frontier.Keys.Order())
        {
            Region target = world.Regions.Get(targetId);
            long pressure = Pressure(
                world, polity, target, frontier[targetId], held, administration,
                distance.GetValueOrDefault(targetId, Unreachable), power, reach);

            if (pressure > bestPressure)
            {
                bestPressure = pressure;
                best = targetId;
                bestDefender = target.Controller;
            }
        }

        if (best.IsNone || bestPressure < _rules.MinPressure)
        {
            Consolidate(in context, polity);
            return;
        }

        int basePermille = (int)Math.Clamp(
            (bestPressure - _rules.MinPressure) / _rules.PressurePerPermille,
            1,
            _rules.MaxAttemptPermille);

        // Applied only after the viability gate above. A commander changes how fast a possible
        // conquest happens, never whether it is possible, and never which target is chosen.
        int permille = _rules.CampaignPermille(basePermille, military);

        // Per-polity stream: a polity's roll does not depend on how many polities were evaluated
        // before it, so extinction elsewhere in the world cannot shift this one's history.
        Rng rng = context.Rng((ulong)polity.Id.Index);
        if (!rng.Chance(permille))
        {
            Consolidate(in context, polity);
            return;
        }

        context.Effects.Emit(new SetRegionController(best, polity.Id, ExpansionReason));

        // Strain scales inversely with how easy the conquest was. A flat cost made sustained
        // expansion impossible for anyone: every conqueror was immediately weakened enough for its
        // victim to take the region straight back, so borders sloshed for millennia and no polity
        // ever grew or died. Cheap absorption of a weak neighbour is the positive feedback that
        // lets an empire actually form - and marginal conquests still cost the full amount.
        int strain = (int)Math.Clamp(
            (long)_rules.ConquestStrain * _rules.MinPressure / Math.Max(1, bestPressure),
            1,
            _rules.ConquestStrain);

        context.Effects.Emit(new AdjustPolityStability(polity.Id, -strain, "strain of expansion"));

        if (bestDefender.IsSome)
        {
            context.Effects.Emit(new AdjustPolityStability(
                bestDefender, -_rules.DefenderShock, "loss of territory"));
        }
    }

    /// <summary>
    /// Attack-to-defence ratio as a percentage. 100 is parity.
    /// </summary>
    /// <remarks>
    /// Integer throughout, in the same order every time, because this feeds a comparison that
    /// decides history. The divisions are lossy and that is fine - what matters is that they are
    /// lossy identically on every machine and every run.
    /// </remarks>
    private long Pressure(
        WorldState world,
        Polity polity,
        Region target,
        long adjacentOwnPopulation,
        int held,
        int administration,
        int distanceFromCapital,
        Dictionary<PolityId, long> power,
        Dictionary<PolityId, Dictionary<RegionId, int>> reach)
    {
        long attack = adjacentOwnPopulation
            + (power.GetValueOrDefault(polity.Id) / _rules.MobilisationDivisor);

        attack = attack * polity.Stability / _rules.NeutralStability;
        attack = attack * 100 / (100 + (_rules.ReachPenaltyPerStep * (long)distanceFromCapital));
        // The ruler's single point of contact with expansion: a capable administrator carries the
        // same territory at a lower cost, so a large state can keep growing sustainably, and an
        // incapable one is throttled at exactly the moment cohesion is also failing them.
        attack = attack * 100 / (100 + _rules.OverextensionTerm(held, administration));

        // Unclaimed land defends with nothing but its own populace.
        long defence = target.Population;

        if (target.Controller.IsSome && world.Polities.TryGet(target.Controller, out Polity? defender))
        {
            int defenderDistance = reach.TryGetValue(defender.Id, out Dictionary<RegionId, int>? map)
                ? map.GetValueOrDefault(target.Id, Unreachable)
                : Unreachable;

            // Organised local defence, plus whatever the defender can project this far from its own
            // capital. The projected part is what makes losing compound: a shrinking polity has less
            // to project, so its remaining periphery becomes progressively cheaper to take.
            defence = (defence
                    * _rules.DefenceMultiplier / 100
                    * (_rules.NeutralStability + defender.Stability) / (2 * _rules.NeutralStability))
                + (power.GetValueOrDefault(defender.Id) / _rules.MobilisationDivisor
                    * 100 / (100 + (_rules.ReachPenaltyPerStep * (long)defenderDistance)));
        }

        return attack * 100 / Math.Max(1, defence);
    }

    private void Consolidate(in SystemContext context, Polity polity)
    {
        // Mean reversion toward neutral, not growth without limit. Recovery stops at the baseline,
        // so peace cannot compound into an ever-stronger state.
        if (polity.Stability < _rules.NeutralStability)
        {
            context.Effects.Emit(new AdjustPolityStability(
                polity.Id, _rules.ConsolidationRecovery, "consolidation"));
        }
    }

    /// <summary>Foreign or unclaimed regions on the polity's border, mapped to the population facing them.</summary>
    private static Dictionary<RegionId, long> Frontier(WorldState world, PolityId polity)
    {
        var frontier = new Dictionary<RegionId, long>();

        foreach (Region own in WorldQueries.RegionsOf(world, polity))
        {
            foreach (RegionId neighborId in own.Neighbors)
            {
                Region neighbor = world.Regions.Get(neighborId);
                if (neighbor.Controller.Equals(polity))
                {
                    continue;
                }

                // Summed across every own region touching the target, so a salient pressed from
                // three sides is genuinely stronger than one pressed from one.
                frontier[neighborId] = frontier.GetValueOrDefault(neighborId) + own.Population;
            }
        }

        return frontier;
    }

    /// <summary>
    /// Hop count from the capital, over the whole adjacency graph.
    /// </summary>
    /// <remarks>
    /// Deliberately ignores who owns the intervening land: this is geographic distance, not a
    /// supply route. A real model would path through controlled territory and weight by terrain.
    /// Deterministic because the start is fixed and neighbour lists are stored in insertion order.
    /// </remarks>
    private static Dictionary<RegionId, int> DistancesFrom(WorldState world, RegionId origin)
    {
        var distance = new Dictionary<RegionId, int>();
        if (origin.IsNone || !world.Regions.Contains(origin))
        {
            return distance;
        }

        var queue = new Queue<RegionId>();
        distance[origin] = 0;
        queue.Enqueue(origin);

        while (queue.Count > 0)
        {
            RegionId current = queue.Dequeue();
            int next = distance[current] + 1;

            foreach (RegionId neighbor in world.Regions.Get(current).Neighbors)
            {
                if (distance.TryAdd(neighbor, next))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return distance;
    }
}
