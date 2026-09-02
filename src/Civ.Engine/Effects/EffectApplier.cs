using Civ.Engine.Core;
using Civ.Engine.Events;
using Civ.Engine.State;

namespace Civ.Engine.Effects;

/// <summary>
/// The single place in the codebase that mutates <see cref="WorldState"/>, and the single place
/// that produces <see cref="SimEvent"/>s.
/// </summary>
/// <remarks>
/// <para>Concentrating both here is the central architectural bet. It means every state change can
/// be logged, every event is guaranteed to correspond to a change that really happened, and
/// invariant-preserving cascades (dissolving a polity must release its regions) live next to the
/// mutation they protect instead of being scattered across the systems that trigger them.</para>
///
/// <para>Effects arrive already ordered by pipeline position, so application is a straight
/// sequential pass with no sorting and no ambiguity.</para>
/// </remarks>
internal sealed class EffectApplier
{
    private readonly Chronicle _chronicle;
    private readonly List<EffectConflict> _conflicts;

    /// <summary>Regions whose controller was set this phase, and by whom. Reset per phase.</summary>
    private readonly Dictionary<RegionId, string> _controllerWrites = [];

    /// <summary>Population at the start of the phase, so milestone crossings are detected once.</summary>
    private readonly Dictionary<RegionId, long> _populationBefore = [];

    internal EffectApplier(Chronicle chronicle, List<EffectConflict> conflicts)
    {
        _chronicle = chronicle;
        _conflicts = conflicts;
    }

    /// <summary>
    /// Applies one phase's worth of effects. Called at the phase barrier, never mid-system.
    /// </summary>
    internal void ApplyPhase(WorldState world, string phaseName, IReadOnlyList<Effect> effects)
    {
        _controllerWrites.Clear();
        _populationBefore.Clear();

        foreach (Effect effect in effects)
        {
            Apply(world, phaseName, effect);
        }

        EmitPopulationMilestones(world);
    }

    private void Apply(WorldState world, string phase, Effect effect)
    {
        switch (effect)
        {
            case AdjustRegionPopulation e:
                ApplyPopulation(world, e);
                break;

            case SetRegionController e:
                ApplyController(world, phase, e);
                break;

            case FoundPolity e:
                ApplyFoundPolity(world, phase, e);
                break;

            case DissolvePolity e:
                ApplyDissolvePolity(world, e);
                break;

            case AdjustPolityStability e:
                ApplyStability(world, e);
                break;

            case SetPolityCapital e:
                ApplyCapital(world, e);
                break;

            case InstallRuler e:
                ApplyInstallRuler(world, e);
                break;

            case EndReign e:
                ApplyEndReign(world, e);
                break;

            case RenamePolity e:
                ApplyRename(world, e);
                break;

            case RecordEvent e:
                _chronicle.Record(e.Event);
                break;

            default:
                throw new NotSupportedException(
                    $"No applier branch for effect kind {effect.Kind} ({effect.GetType().Name}).");
        }
    }

    private void ApplyPopulation(WorldState world, AdjustRegionPopulation e)
    {
        if (!world.Regions.TryGet(e.Region, out Region? region))
        {
            // Stale handle. Dropped rather than thrown: a system reading a start-of-phase snapshot
            // can legitimately name something an earlier phase removed.
            return;
        }

        if (!_populationBefore.ContainsKey(e.Region))
        {
            _populationBefore[e.Region] = region.Population;
        }

        region.Population = Math.Max(0, region.Population + e.Delta);
    }

    private void ApplyController(WorldState world, string phase, SetRegionController e)
    {
        if (!world.Regions.TryGet(e.Region, out Region? region))
        {
            return;
        }

        if (_controllerWrites.TryGetValue(e.Region, out string? winner))
        {
            // Deterministic resolution: the earlier effect in pipeline order already won.
            _conflicts.Add(new EffectConflict(
                world.Year,
                phase,
                $"Region[{region.Name}].Controller",
                winner,
                e.Source,
                $"second write ({e.Reason}) ignored"));
            return;
        }

        _controllerWrites[e.Region] = e.Source;

        PolityId from = region.Controller;
        if (from.Equals(e.Controller))
        {
            return;
        }

        // The target must be a live polity, or nothing at all. Anything else breaks an invariant.
        if (e.Controller.IsSome && !world.Polities.TryGet(e.Controller, out _))
        {
            return;
        }

        string fromName = WorldQueries.NameOf(world, from);
        string toName = WorldQueries.NameOf(world, e.Controller);
        region.Controller = e.Controller;

        _chronicle.Record(new RegionControlChangedEvent(
            world.Year, region.Id, region.Name, from, fromName, e.Controller, toName, e.Reason));
    }

    private void ApplyFoundPolity(WorldState world, string phase, FoundPolity e)
    {
        RegionId capital = e.Capital;
        if (capital.IsSome && !world.Regions.TryGet(capital, out _))
        {
            capital = RegionId.None;
        }

        PolityId parent = world.Polities.TryGet(e.Parent, out _) ? e.Parent : PolityId.None;

        PolityId id = world.AddPolity(e.Name, capital, world.Year, parent);
        string capitalName = WorldQueries.NameOf(world, capital);

        // The parent's name is snapshotted here, not resolved later. A secession recorded in year
        // 400 must still say what it broke away from in year 1800, long after that state is gone.
        string parentName = parent.IsSome ? WorldQueries.NameOf(world, parent) : string.Empty;

        _chronicle.Record(new PolityFoundedEvent(
            world.Year, id, e.Name, capital, capitalName, parent, parentName,
            e.InitialRegions?.Count ?? 0, e.Reason));

        // A state comes into existence with someone at its head. Doing this here rather than
        // leaving it to a later system means the "every active polity has exactly one living ruler"
        // invariant holds at the end of the very tick the polity was created - including for a
        // breakaway founded in the Polity phase, which no earlier phase could have reached.
        SeatRuler(world, id, e.Name, e.RulerProfile, e.Reason);

        // Territory is assigned through the normal control path so it emits the usual events and
        // obeys the same conflict rules.
        if (e.InitialRegions is { Count: > 0 })
        {
            foreach (RegionId regionId in e.InitialRegions)
            {
                var transfer = new SetRegionController(regionId, id, e.Reason) { Source = e.Source };
                ApplyController(world, phase, transfer);
            }
        }
    }

    /// <summary>Creates a ruler and installs them. The one place a reign begins.</summary>
    private void SeatRuler(
        WorldState world, PolityId polityId, string polityName, RulerProfile? profile, string reason)
    {
        RulerProfile chosen = profile ?? RulerFactory.Generate(world);
        RulerId rulerId = world.AddRuler(chosen, world.Year, polityId);
        world.Polities.Get(polityId).CurrentRuler = rulerId;

        _chronicle.Record(new RulerAccessionEvent(
            world.Year,
            rulerId,
            chosen.Name,
            polityId,
            polityName,
            chosen.Administration,
            chosen.Military,
            world.Year - chosen.BirthYear,
            reason));
    }

    private void ApplyInstallRuler(WorldState world, InstallRuler e)
    {
        if (!world.Polities.TryGet(e.Polity, out Polity? polity) || !polity.IsActive)
        {
            return;
        }

        // Refuses to displace a living ruler. A succession must apply its death first, which is why
        // the succession system emits EndReign before InstallRuler and the applier honours that order.
        if (world.Rulers.TryGet(polity.CurrentRuler, out Ruler? sitting) && sitting.IsReigning)
        {
            return;
        }

        SeatRuler(world, polity.Id, polity.Name, e.Profile, e.Reason);
    }

    private void ApplyEndReign(WorldState world, EndReign e)
    {
        if (!world.Rulers.TryGet(e.Ruler, out Ruler? ruler) || !ruler.IsReigning)
        {
            return;
        }

        ruler.ReignEndYear = world.Year;
        ruler.EndReason = e.EndReason;

        // Only a death records a death. A state collapsing under its ruler ends the reign and
        // archives the person; it does not kill them, and it must not be counted as mortality.
        if (e.EndReason == ReignEndReason.Death)
        {
            ruler.DeathYear = world.Year;
        }

        string polityName = WorldQueries.NameOf(world, ruler.Polity);
        if (world.Polities.TryGet(ruler.Polity, out Polity? polity)
            && polity.CurrentRuler.Equals(ruler.Id))
        {
            polity.CurrentRuler = RulerId.None;
        }

        int age = ruler.AgeIn(world.Year);
        int length = world.Year - ruler.AccessionYear;

        if (e.EndReason == ReignEndReason.Death)
        {
            _chronicle.Record(new RulerDeathEvent(
                world.Year, ruler.Id, ruler.Name, ruler.Polity, polityName, age, length, e.Reason));
        }
        else
        {
            _chronicle.Record(new ReignEndedEvent(
                world.Year, ruler.Id, ruler.Name, ruler.Polity, polityName, age, length,
                e.EndReason, e.Reason));
        }
    }

    private void ApplyDissolvePolity(WorldState world, DissolvePolity e)
    {
        if (!world.Polities.TryGet(e.Polity, out Polity? polity) || !polity.IsActive)
        {
            return;
        }

        // Cascade. A defunct polity must never be left controlling territory, so releasing its
        // regions is part of the same operation rather than something every caller must remember.
        List<Region> held = WorldQueries.RegionsOf(world, e.Polity).ToList();
        foreach (Region region in held)
        {
            region.Controller = PolityId.None;
            _chronicle.Record(new RegionControlChangedEvent(
                world.Year,
                region.Id,
                region.Name,
                e.Polity,
                polity.Name,
                PolityId.None,
                "unclaimed",
                $"dissolution of {polity.Name}"));
        }

        // Closing the reign belongs to the same cascade that releases the territory: a defunct
        // polity with a living ruler is exactly the kind of half-retired state an invariant rejects.
        if (world.Rulers.TryGet(polity.CurrentRuler, out Ruler? ruler) && ruler.IsReigning)
        {
            ApplyEndReign(world, new EndReign(
                ruler.Id, $"fall of {polity.Name}", ReignEndReason.PolityExtinct));
        }

        polity.Status = PolityStatus.Defunct;
        polity.DissolvedYear = world.Year;
        polity.Capital = RegionId.None;
        polity.CurrentRuler = RulerId.None;

        _chronicle.Record(new PolityDissolvedEvent(
            world.Year, e.Polity, polity.Name, held.Count, e.Reason));
    }

    private void ApplyStability(WorldState world, AdjustPolityStability e)
    {
        if (!world.Polities.TryGet(e.Polity, out Polity? polity) || !polity.IsActive)
        {
            return;
        }

        int before = polity.Stability;
        int after = Math.Clamp(before + e.Delta, 0, 100);
        if (after == before)
        {
            return;
        }

        polity.Stability = after;

        // Reported on decade crossings only. Stability drifts every year; narrating each step
        // would drown everything else in the feed.
        if (before / 10 != after / 10)
        {
            _chronicle.Record(new PolityStabilityShiftEvent(
                world.Year, polity.Id, polity.Name, before, after, e.Reason));
        }
    }

    private void ApplyCapital(WorldState world, SetPolityCapital e)
    {
        if (!world.Polities.TryGet(e.Polity, out Polity? polity) || !polity.IsActive)
        {
            return;
        }

        // A seat must be territory the polity actually holds, or the capital invariant breaks.
        if (e.Capital.IsSome
            && (!world.Regions.TryGet(e.Capital, out Region? target) || !target.Controller.Equals(e.Polity)))
        {
            return;
        }

        RegionId from = polity.Capital;
        if (from.Equals(e.Capital))
        {
            return;
        }

        string fromName = WorldQueries.NameOf(world, from);
        string toName = WorldQueries.NameOf(world, e.Capital);
        polity.Capital = e.Capital;

        _chronicle.Record(new PolityCapitalMovedEvent(
            world.Year, polity.Id, polity.Name, from, fromName, e.Capital, toName, e.Reason));
    }

    private void ApplyRename(WorldState world, RenamePolity e)
    {
        if (world.Polities.TryGet(e.Polity, out Polity? polity))
        {
            polity.Name = e.NewName;
        }
    }

    /// <summary>
    /// Turns a phase's accumulated population deltas into at most one event per region.
    /// </summary>
    /// <remarks>
    /// Every region's population moves every year. Emitting an event per change would produce
    /// hundreds of lines a year, all of them noise. Only threshold crossings are reported, which is
    /// the same salience filter that will later apply to prices, armies and congregations.
    /// </remarks>
    private void EmitPopulationMilestones(WorldState world)
    {
        // Sorted, so event order never depends on dictionary layout.
        foreach (RegionId regionId in _populationBefore.Keys.Order())
        {
            long before = _populationBefore[regionId];
            if (!world.Regions.TryGet(regionId, out Region? region))
            {
                continue;
            }

            long after = region.Population;
            long crossed = MilestoneCrossed(before, after);
            if (crossed > 0)
            {
                _chronicle.Record(new PopulationMilestoneEvent(
                    world.Year, region.Id, region.Name, after, crossed, after > before));
            }
        }
    }

    /// <summary>Returns the milestone crossed between two population values, or 0.</summary>
    private static long MilestoneCrossed(long before, long after)
    {
        foreach (long milestone in Milestones)
        {
            bool up = before < milestone && after >= milestone;
            bool down = before >= milestone && after < milestone;
            if (up || down)
            {
                return milestone;
            }
        }

        return 0;
    }

    private static readonly long[] Milestones =
    [
        1_000, 5_000, 10_000, 25_000, 50_000, 100_000,
        250_000, 500_000, 1_000_000, 5_000_000, 10_000_000,
    ];
}
