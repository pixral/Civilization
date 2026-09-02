using Civ.Engine.Core;
using Civ.Engine.Events;
using Civ.Engine.State;

namespace Civ.Engine.Effects;

/// <summary>
/// A requested change to the world. Systems emit these; only <see cref="EffectApplier"/> acts on them.
/// </summary>
/// <remarks>
/// <para><b>Prefer deltas to absolute sets.</b> Additive effects such as
/// <see cref="AdjustRegionPopulation"/> compose with no conflict, so three systems can each push
/// population around in the same phase without any of them needing to know the others exist. Where
/// an absolute write is unavoidable (<see cref="SetRegionController"/>) the applier detects the
/// collision, resolves it deterministically, and records an <see cref="EffectConflict"/> rather
/// than letting last-write-win happen silently.</para>
///
/// <para><b>Creation effects cannot return an id.</b> A system emitting <see cref="FoundPolity"/>
/// does not learn the new <c>PolityId</c> during that tick - the applier allocates it and puts it
/// in the emitted event. Anything needing to act on the new polity does so next tick, by finding it
/// in state. This is a real constraint of the barrier model, and the alternative (placeholder
/// handles patched at apply time) buys very little at this scale.</para>
///
/// <para><see cref="Source"/> is the emitting system's name. Diagnostics only; nothing branches
/// on it, or the pipeline would stop being reorderable.</para>
/// </remarks>
public abstract record Effect
{
    public string Source { get; internal set; } = "unknown";

    public abstract string Kind { get; }
}

/// <summary>Additive, commutative population change.</summary>
public sealed record AdjustRegionPopulation(RegionId Region, long Delta, string Reason) : Effect
{
    public override string Kind => "region.adjust_population";
}

/// <summary>
/// Absolute territorial transfer. Pass <see cref="EntityId{TKind}.None"/> to release a region.
/// Conflicting writes in one phase are reported.
/// </summary>
public sealed record SetRegionController(RegionId Region, PolityId Controller, string Reason) : Effect
{
    public override string Kind => "region.set_controller";
}

/// <summary>Creates a polity. <paramref name="Parent"/> records secession or succession lineage.</summary>
public sealed record FoundPolity(
    string Name,
    RegionId Capital,
    PolityId Parent,
    string Reason,
    IReadOnlyList<RegionId>? InitialRegions = null,
    RulerProfile? RulerProfile = null) : Effect
{
    public override string Kind => "polity.found";
}

/// <summary>
/// Marks a polity defunct. The applier releases its regions in the same operation - a cascade that
/// exists precisely so the world cannot be left in a state an invariant would reject.
/// </summary>
public sealed record DissolvePolity(PolityId Polity, string Reason) : Effect
{
    public override string Kind => "polity.dissolve";
}

/// <summary>Additive stability change, clamped by the applier.</summary>
public sealed record AdjustPolityStability(PolityId Polity, int Delta, string Reason) : Effect
{
    public override string Kind => "polity.adjust_stability";
}

/// <summary>
/// Moves a polity's seat of government.
/// </summary>
/// <remarks>
/// Exists because a capital is a reference into territory that can change hands. Without a way to
/// relocate a seat, the first time a polity loses its capital region the world is in a state no
/// system can repair.
/// </remarks>
public sealed record SetPolityCapital(PolityId Polity, RegionId Capital, string Reason) : Effect
{
    public override string Kind => "polity.set_capital";
}

public sealed record RenamePolity(PolityId Polity, string NewName, string Reason) : Effect
{
    public override string Kind => "polity.rename";
}

/// <summary>
/// Installs a ruler on a polity that currently has none.
/// </summary>
/// <remarks>
/// A null <paramref name="Profile"/> asks the applier to generate one from its own deterministic
/// stream. Systems that care who succeeds - and tests that need an exact administrator - supply it.
/// Ignored if the polity already has a living ruler, so a death must be applied first.
/// </remarks>
public sealed record InstallRuler(PolityId Polity, RulerProfile? Profile, string Reason) : Effect
{
    public override string Kind => "ruler.install";
}

/// <summary>
/// Ends a reign. The ruler is recorded dead only when <paramref name="EndReason"/> says so.
/// </summary>
public sealed record EndReign(
    RulerId Ruler,
    string Reason,
    ReignEndReason EndReason = ReignEndReason.Death) : Effect
{
    public override string Kind => "ruler.end_reign";
}

/// <summary>
/// The single sanctioned way to record an event with no state change behind it.
/// </summary>
/// <remarks>
/// Kept narrow deliberately. Every use is a small hole in the rule that the chronicle only reports
/// real state deltas, so it is for genuine observations (an omen, a census) and not a shortcut for
/// systems that want to narrate.
/// </remarks>
public sealed record RecordEvent(SimEvent Event) : Effect
{
    public override string Kind => "chronicle.record";
}
