namespace Civ.Systems;

/// <summary>
/// Tuning constants for <see cref="OpportunisticExpansionSystem"/>.
/// </summary>
/// <remarks>
/// <para>Separated from the system so the rule can be swept without editing code, and so tests can
/// force behaviour that is merely likely under the defaults. This is the shape all simulation
/// content should eventually take - numbers in data, mechanism in code - even though it is a plain
/// record for now rather than something loaded from disk.</para>
///
/// <para><b>Caveat.</b> These values change outcomes but are not part of <c>SimulationConfig</c>, so
/// they do not appear in the config hash. A run's identity is currently "(engine version, config,
/// seed)" with system tuning folded into "engine version". That is fine while tuning lives in
/// source; it needs revisiting the moment rules become loadable content.</para>
/// </remarks>
public sealed record ExpansionRules
{
    /// <summary>
    /// Minimum attack-to-defence ratio, as a percentage, before expansion is considered at all.
    /// </summary>
    /// <remarks>
    /// 100 is parity. This threshold is the reason expansion is state-derived rather than random:
    /// below it, no roll happens at all, however many years pass. Randomness decides <i>when</i> a
    /// viable move happens, never <i>whether</i> one is possible.
    /// </remarks>
    public int MinPressure { get; init; } = 80;

    /// <summary>Ceiling on the annual chance of acting, in parts per thousand.</summary>
    public int MaxAttemptPermille { get; init; } = 45;

    /// <summary>Pressure above <see cref="MinPressure"/> needed for each extra permille of chance.</summary>
    public int PressurePerPermille { get; init; } = 3;

    /// <summary>
    /// Attack and defence penalty per step of distance from the relevant capital. Administrative reach.
    /// </summary>
    /// <remarks>
    /// Kept low deliberately. Because a shrinking polity concentrates around its capital while an
    /// expanding one stretches away from its own, a strong reach penalty is a powerful stabiliser -
    /// at 7 it dominated every other term and froze the political map completely.
    /// </remarks>
    public int ReachPenaltyPerStep { get; init; } = 2;

    /// <summary>Attack penalty per region already held. The cost of holding what you have.</summary>
    public int OverextensionPerRegion { get; init; } = 4;

    /// <summary>
    /// Share of a polity's total population it can project at a border, on attack and defence alike.
    /// </summary>
    /// <remarks>
    /// The only term that scales with the size of a polity rather than with one region, so it is
    /// what allows a large state to be genuinely stronger than a small one. Divide it too heavily
    /// and conquest has no compounding reward, which produces a world of permanently equal states.
    /// </remarks>
    public int MobilisationDivisor { get; init; } = 5;

    /// <summary>
    /// Organised defence of a held region, as a percentage of its population. Unclaimed land
    /// defends at 100 - nobody is organising it.
    /// </summary>
    public int DefenceMultiplier { get; init; } = 160;

    /// <summary>Stability at which a polity is neither helped nor hindered. Also the drift target.</summary>
    public int NeutralStability { get; init; } = 50;

    /// <summary>
    /// Stability cost of a marginal conquest. Scaled down in proportion to how far pressure
    /// exceeded <see cref="MinPressure"/>, so absorbing a weak neighbour is close to free.
    /// </summary>
    public int ConquestStrain { get; init; } = 9;

    /// <summary>Stability cost of losing one.</summary>
    public int DefenderShock { get; init; } = 6;

    /// <summary>Annual stability recovery for a polity that did not expand this year.</summary>
    public int ConsolidationRecovery { get; init; } = 1;

    /// <summary>Administration at which the overextension penalty is exactly the unmodified value.</summary>
    public int NeutralAdministration { get; init; } = 50;

/// <summary>
    /// Overextension penalty under the weakest possible administrator, as a percentage.
    /// </summary>
    /// <remarks>
    /// <b>Disabled by default (100/100).</b> The experiment that connected administration to
    /// overextension was measured and failed: across bands from 125/75 to 200/20, over 50 seeds and
    /// 3000 years, it changed expansion counts but never the peak-share distribution. Overextension
    /// is simply not what limits empire size here. The implementation is kept because it is correct
    /// and cheap to re-enable, but it is not part of the default simulation - administration affects
    /// cohesion only.
    /// </remarks>
    public int OverextensionAtWeakestPercent { get; init; } = 100;

    /// <summary>Overextension penalty under the strongest possible administrator. Disabled by default.</summary>
    public int OverextensionAtStrongestPercent { get; init; } = 100;

    /// <summary>Military ability at which campaign tempo is exactly the unmodified rate.</summary>
    public int NeutralMilitary { get; init; } = 50;

    /// <summary>Campaign tempo under the least capable commander, as a percentage.</summary>
    public int MilitaryTempoAtWeakestPercent { get; init; } = 50;

    /// <summary>
    /// Campaign tempo under the most capable commander, as a percentage.
    /// </summary>
    /// <remarks>
    /// Deliberately asymmetric. The administrative experiment established that a modest multiplier on
    /// a rate already close to zero cannot produce reign-scale expansion: typical pressure sits just
    /// above <see cref="MinPressure"/>, so an eighteen percent bonus turned 0.08 expected conquests
    /// per reign into 0.22. A commander has roughly one lifetime to act, so the upper end has to be
    /// large enough to matter within it.
    /// </remarks>
    /// <remarks>
    /// <para>Selected by sweep at 10 and 20 seeds over 3000 years, cohesion and every other
    /// expansion rule held fixed: 300 gave a real but small shift, 500 was indistinguishable from
    /// the control, 800 roughly doubled 300's effect, and 1200 with a lower floor was no better than
    /// 800. At 800 an 80-100 commander makes 0.415 conquests per reign against 0.070 for a 0-19 one,
    /// while the arms without the band sit flat at 0.09 across every military band - so the extra
    /// expansion is concentrated in capable commanders rather than being general churn.</para>
    /// </remarks>
    public int MilitaryTempoAtStrongestPercent { get; init; } = 800;

    /// <summary>
    /// Absolute ceiling on the annual chance of acting, after the tempo multiplier.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="MaxAttemptPermille"/> on purpose. That one caps the <i>base</i>
    /// probability; applying it again afterwards would erase the entire bonus, since the base is
    /// already at or below it. This is the safety cap on the finished figure.
    ///
    /// <para>1000 - certainty - because the job of this cap is to keep a probability a probability,
    /// not to impose a second, lower ceiling. Anything below 1000 would silently reduce rule sets
    /// that deliberately raise <see cref="MaxAttemptPermille"/>, which is exactly the kind of quiet
    /// interaction that makes a test fixture stop meaning what it says.</para>
    /// </remarks>
    public int MaxCampaignPermille { get; init; } = 1000;

    /// <summary>
    /// Campaign tempo multiplier for a commander, as a percentage.
    /// </summary>
    /// <remarks>
    /// Two straight segments meeting at <see cref="NeutralMilitary"/>, because the band is not
    /// symmetric about it: a single line from 50% to 300% would put ability 50 at 175% and quietly
    /// change every existing result. Ability 50 must return exactly 100.
    /// </remarks>
    public int CampaignTempoPercent(int military)
    {
        int ability = Math.Clamp(military, 0, 100);
        int neutral = Math.Clamp(NeutralMilitary, 1, 99);

        return ability <= neutral
            ? MilitaryTempoAtWeakestPercent
                + (ability * (100 - MilitaryTempoAtWeakestPercent) / neutral)
            : 100
                + ((ability - neutral) * (MilitaryTempoAtStrongestPercent - 100) / (100 - neutral));
    }

    /// <summary>
    /// The annual chance of acting on an opportunity that has already been judged viable.
    /// </summary>
    /// <remarks>
    /// The viability gate is entirely upstream: this is never called unless pressure already cleared
    /// <see cref="MinPressure"/>, so no commander can make an impossible target possible. All this
    /// decides is how quickly a possible one is taken.
    /// </remarks>
    public int CampaignPermille(int basePermille, int military) =>
        Math.Clamp(basePermille * CampaignTempoPercent(military) / 100, 1, MaxCampaignPermille);

    /// <summary>
    /// The overextension multiplier for a given administrative ability, as a percentage.
    /// </summary>
    /// <remarks>
    /// Inverted relative to the cohesion band on purpose: a better administrator suffers <i>less</i>
    /// penalty. Ability 50 returns exactly 100, so a world of average rulers reproduces the previous
    /// expansion calculation bit for bit.
    /// </remarks>
    public int OverextensionPercent(int administration)
    {
        int span = OverextensionAtStrongestPercent - OverextensionAtWeakestPercent;
        return OverextensionAtWeakestPercent + (Math.Clamp(administration, 0, 100) * span / 100);
    }

    /// <summary>
    /// The complete overextension term for a polity of a given size under a given administrator.
    /// </summary>
    /// <remarks>
    /// The percentage is applied to the whole term rather than to
    /// <see cref="OverextensionPerRegion"/>. That constant is 4, so scaling it first would round a
    /// 125%/75% band down to 5 and 3 - a coarse three-value modifier instead of a smooth one - and
    /// would make the effect independent of how large the polity actually is.
    ///
    /// <para>This is the only channel by which a ruler touches expansion. Population, defence, reach,
    /// mobilisation, stability and target selection are all untouched.</para>
    /// </remarks>
    public long OverextensionTerm(int held, int administration) =>
        (long)OverextensionPerRegion * held * OverextensionPercent(administration) / 100;

    public static ExpansionRules Default { get; } = new();
}
