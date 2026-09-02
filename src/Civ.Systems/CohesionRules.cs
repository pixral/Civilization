namespace Civ.Systems;

/// <summary>
/// Tuning constants for <see cref="CohesionSecessionSystem"/>.
/// </summary>
/// <remarks>
/// The shape of the model is: every region a polity holds exerts a <b>strain</b> on it, and the
/// polity answers with a fixed <b>authority</b> budget. Where strain exceeds authority the region is
/// restive; connected groups of restive regions are what break away. Strain rises with distance,
/// disconnection and the sheer number of regions held, which is what makes cohesion the natural
/// counter-force to conquest: the reward for expanding is the thing that makes expanding harder.
///
/// <para>Same caveat as <see cref="ExpansionRules"/>: these change outcomes but are not part of the
/// config hash, so run identity currently folds system tuning into "engine version".</para>
/// </remarks>
public sealed record CohesionRules
{
    /// <summary>The ability an average ruler has, and the point every ruler conversion must pin.</summary>
    /// <remarks>
    /// Not a knob. A ruler-dependent conversion that returns anything but 100% here is not a ruler
    /// mechanic at all - it is a global change to the underlying rule with a ruler's name on it, and
    /// the whole world moves whether or not any ruler is exceptional. <see cref="Validate"/> refuses
    /// such a configuration, because one was accepted by mistake once and produced the best-looking
    /// numbers in the project.
    /// </remarks>
    public const int NeutralAdministration = 50;

    /// <summary>The multiplier that means "unchanged".</summary>
    public const int NeutralPercent = 100;

    /// <summary>
    /// Strain a polity can absorb before any of its territory becomes restive.
    /// </summary>
    /// <remarks>
    /// The "administrative capacity" placeholder. A single constant on purpose - it is the natural
    /// hook for the thing that should eventually drive it (writing, roads, bureaucracy, later
    /// telegraphy), and having it as one number now means that change is a substitution rather than
    /// a rewrite.
    ///
    /// <para>It is also the single most sensitive constant in the simulation, because it sets the
    /// equilibrium size of a state. Sweeps over 2500-year runs: at 62 the world shattered into ~56
    /// statelets of three regions each; at 380 nothing ever seceded and conquest ran unopposed. The
    /// balanced band is narrow - around 130-170 - and 150 sits in it.</para>
    /// </remarks>
    public int AdministrativeCapacity { get; init; } = 150;

    /// <summary>Strain per step of distance from the capital, measured through the polity's own land.</summary>
    public int DistanceStrainPerStep { get; init; } = 14;

    // ------------------------------------------------------------------ administrative reach

    /// <summary>
    /// Connected-distance strain under the strongest possible administrator, as a percentage.
    /// </summary>
    /// <remarks>
    /// <para><b>The only endpoint this conversion exposes, on purpose.</b> The previous, symmetric
    /// version took a weak endpoint as well, and a one-sided linear band written through that
    /// parameter silently moved the neutral point: <c>100/50</c> looked like a benefit for strong
    /// administrators and was in fact a 25% distance discount for the average ruler, applied to
    /// every state in the world. There is no weak endpoint here, so that configuration cannot be
    /// expressed.</para>
    ///
    /// <para><b>Benefit only.</b> Weak administrators already pay through
    /// <see cref="EffectiveCapacity"/>; charging them a second time through distance is what made the
    /// symmetric band fail, and <see cref="Validate"/> rejects any value above
    /// <see cref="NeutralPercent"/>. 100 disables the modifier entirely.</para>
    ///
    /// <para><b>Disabled by default, because it was measured and rejected.</b> The conversion does
    /// exactly what it was designed to do - over 50 seeds x 3000 years it retained 107,293
    /// region-years, exposed <i>zero</i>, and was sharply concentrated where it should be (37.7% of
    /// polity-years changed for 30+ region states against 0.00% under 10, almost entirely four or
    /// more steps from the capital, entirely under administrators above 50). The world it produced
    /// was indistinguishable from the world without it: peak largest share 19.56% against 19.34%,
    /// identical medians, and a matched global control at 19.40% - so what little moved is a general
    /// distance discount rather than a ruler effect.</para>
    ///
    /// <para>The reason is a matter of size, measured in phase: connected distance is 40% of all
    /// strain, the size term is 62%, and a 50% benefit at the top of the ability range removes
    /// <b>2.97% of total strain</b>. See <see cref="SizeStrainPerRegion"/>, which is what the empire
    /// ceiling actually responds to.</para>
    /// </remarks>
    public int DistanceStrainAtStrongestPercent { get; init; } = NeutralPercent;

    /// <summary>
    /// A flat, non-ruler distance multiplier. <b>Experimental control only.</b>
    /// </summary>
    /// <remarks>
    /// <para>This exists so a one-sided ruler benefit can be compared against the thing it is most
    /// likely to be mistaken for: a world in which distance is simply cheaper. It applies the same
    /// multiplier to every polity in every year whoever is ruling, so it is deliberately <i>not</i>
    /// a ruler mechanic and is exempt from the neutral-point rule that governs the real
    /// conversions.</para>
    ///
    /// <para>It is named for what it is, kept at 100 by default, and refused in combination with
    /// <see cref="DistanceStrainAtStrongestPercent"/>, so it can neither become a default by
    /// accident nor be mistaken in a report for a ruler effect.</para>
    /// </remarks>
    public int ExperimentalConstantDistancePercent { get; init; } = NeutralPercent;

    /// <summary>
    /// The connected-distance multiplier actually applied, as a percentage.
    /// </summary>
    /// <remarks>
    /// Dispatches between the ruler conversion and the constant experimental control;
    /// <see cref="Validate"/> guarantees at most one of them is active.
    /// </remarks>
    public int DistanceStrainPercent(int administration) =>
        ExperimentalConstantDistancePercent != NeutralPercent
            ? ExperimentalConstantDistancePercent
            : ReachBenefitPercent(administration);

    /// <summary>
    /// The one-sided administrative reach benefit: flat to ability 50, then falling.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately asymmetric, and the asymmetry is the finding that produced it. Under a
    /// symmetric band the counterfactual measured 100,558 region-years pushed over the restive
    /// threshold by weak administrators against 20,332 held below it by strong ones - five to one,
    /// from a band that was even. The strain distribution has most of its mass just below the
    /// threshold, so raising strain crosses many regions while lowering it rescues few.</para>
    ///
    /// <para>Removing the raising half is therefore not a softening of the previous experiment; it
    /// is the specific change the previous experiment's diagnostic asked for. Ability 0 through 50
    /// return exactly 100, so half the ruler population reproduces the current world bit for bit,
    /// and only genuinely exceptional administrators are worth anything at all.</para>
    /// </remarks>
    public int ReachBenefitPercent(int administration)
    {
        int ability = Math.Clamp(administration, 0, 100);
        if (ability <= NeutralAdministration)
        {
            return NeutralPercent;
        }

        int benefit = NeutralPercent - DistanceStrainAtStrongestPercent;
        return NeutralPercent
            - ((ability - NeutralAdministration) * benefit / (100 - NeutralAdministration));
    }

    /// <summary>
    /// The complete connected-distance strain for a region, under a given administrator.
    /// </summary>
    /// <remarks>
    /// The percentage multiplies the whole term, not <see cref="DistanceStrainPerStep"/>. That
    /// constant is 14, so scaling it first would round a 50% benefit to 7 - a coarse modifier whose
    /// error would be largest exactly where it matters, and which would not grow smoothly with
    /// distance.
    ///
    /// <para>Distance zero is zero at every ability: a capital is never remote from itself.</para>
    /// </remarks>
    public long DistanceStrainTerm(int distance, int administration) =>
        (long)DistanceStrainPerStep * distance * DistanceStrainPercent(administration) / 100;

    /// <summary>
    /// Extra strain on a region with no path to the capital through the polity's own territory.
    /// </summary>
    /// <remarks>
    /// Large by design. An exclave cut off by a rival's conquest is the clearest case of territory a
    /// pre-modern state cannot actually govern, and it should be the first thing to go.
    /// </remarks>
    public int DisconnectionStrain { get; init; } = 70;

    /// <summary>
    /// Strain added to every region for each other region the polity holds.
    /// </summary>
    /// <remarks>
    /// <para><b>The binding constraint on empire size, and it took three rejected ruler experiments
    /// to find it.</b> At 3 points per region a 30-region state carries 87 strain on every province
    /// it holds, against an authority budget of 150 - so before geography, wealth or who is on the
    /// throne is considered, size alone has spent 58% of the budget. Measured across 50 seeds it is
    /// 62% of all strain in the world.</para>
    ///
    /// <para>Unlike distance, the ceiling moves when this does. Ten seeds x 3000 years on a
    /// 192-region world, changing nothing else: at 3 the peak largest share averaged 19.4% (max
    /// 22%), at 2 it was 20.6% (max 25%), at 1 it was 24.0% (max 31%), with average polity size
    /// rising 16.1 -> 19.8 -> 24.3 regions.</para>
    ///
    /// <para>It is left at 3. That figure is not defended by this measurement - it is what the
    /// balanced world was tuned around - but any future attempt to raise the empire ceiling should
    /// start here rather than with another ruler-side modifier to a term worth 3% of the total.</para>
    /// </remarks>
    public int SizeStrainPerRegion { get; init; } = 3;

    /// <summary>
    /// Strain from a region being richer than its polity's average, scaled by the excess.
    /// </summary>
    /// <remarks>
    /// A wealthy province has the means to stand alone; a poor one does not. Bounded so a single
    /// outlier cannot dominate the score.
    /// </remarks>
    public int ProsperityStrain { get; init; } = 14;

    /// <summary>Stability at which authority is neither raised nor lowered.</summary>
    public int NeutralStability { get; init; } = 50;

    /// <summary>Authority gained or lost per point of stability away from neutral.</summary>
    public int StabilityRelief { get; init; } = 1;

    /// <summary>Strain above authority needed for each extra permille of annual chance.</summary>
    public int StrainPerPermille { get; init; } = 4;

    /// <summary>Ceiling on the annual chance that an eligible breakaway actually happens.</summary>
    public int MaxAttemptPermille { get; init; } = 40;

    /// <summary>Minimum number of contiguous restive regions before a breakaway is viable.</summary>
    /// <remarks>
    /// 1 by default: a single cut-off province declaring itself independent is exactly the case this
    /// system exists to model. Raise it to force larger, rarer fragmentations.
    /// </remarks>
    public int MinBreakawaySize { get; init; } = 1;

    /// <summary>Stability the parent loses when part of it breaks away.</summary>
    public int SecessionShock { get; init; } = 12;

    /// <summary>Effective capacity of a polity ruled by the weakest possible administrator, as a percentage.</summary>
    public int RulerCapacityFloorPercent { get; init; } = 75;

    /// <summary>Effective capacity under the strongest possible administrator, as a percentage.</summary>
    public int RulerCapacityCeilingPercent { get; init; } = 125;

    /// <summary>Ability of a polity with no living ruler. Should never be needed; kept as a safe default.</summary>
    public int DefaultAdministration { get; init; } = NeutralAdministration;

    /// <summary>The capacity multiplier for an administrator, as a percentage.</summary>
    public int CapacityPercent(int administration)
    {
        int span = RulerCapacityCeilingPercent - RulerCapacityFloorPercent;
        return RulerCapacityFloorPercent + (Math.Clamp(administration, 0, 100) * span / 100);
    }

    /// <summary>
    /// Administrative capacity as modified by the ruler's ability.
    /// </summary>
    /// <remarks>
    /// <para>The configured <see cref="AdministrativeCapacity"/> stays the baseline: ability 50 maps
    /// to exactly 100% of it, so a world of average rulers behaves as it did before rulers existed.
    /// Ability 0 and 100 map to the floor and ceiling percentages.</para>
    ///
    /// <para>The conversion lives here rather than on the ruler because it is a property of how this
    /// simulation values administration, not of the person. A different ruleset should be able to
    /// make the same ruler matter more or less without touching the character layer.</para>
    /// </remarks>
    public int EffectiveCapacity(int administration) =>
        AdministrativeCapacity * CapacityPercent(administration) / 100;

    /// <summary>
    /// Refuses rule sets that would misreport a global change as a ruler effect.
    /// </summary>
    /// <remarks>
    /// <para>Called from <see cref="CohesionSecessionSystem"/>'s constructor and from the batch
    /// runner's argument parsing, so an invalid combination fails before a single year is simulated
    /// rather than after twenty seeds have produced numbers nobody can interpret.</para>
    ///
    /// <para>This exists because of a specific mistake. A one-sided band expressed through a weak
    /// endpoint scored better than every other candidate in the project - and it was giving the
    /// average ruler a 25% discount, so the "empires" it produced were a world where distance had
    /// simply become cheap. The arithmetic that catches it is one line; the run that did not have it
    /// cost a day.</para>
    /// </remarks>
    public void Validate()
    {
        if (DistanceStrainAtStrongestPercent is < 0 or > NeutralPercent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DistanceStrainAtStrongestPercent),
                DistanceStrainAtStrongestPercent,
                "administrative reach is a benefit only: DistanceStrainAtStrongestPercent must be "
                + $"between 0 and {NeutralPercent}. A value above {NeutralPercent} would charge weak "
                + "administrators a second time, on top of the capacity band.");
        }

        if (ExperimentalConstantDistancePercent is < 1 or > NeutralPercent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ExperimentalConstantDistancePercent),
                ExperimentalConstantDistancePercent,
                "the constant-distance control must be between 1 and "
                + $"{NeutralPercent} ({NeutralPercent} disables it).");
        }

        if (ExperimentalConstantDistancePercent != NeutralPercent
            && DistanceStrainAtStrongestPercent != NeutralPercent)
        {
            throw new ArgumentException(
                "ExperimentalConstantDistancePercent "
                + $"({ExperimentalConstantDistancePercent}%) is the matched global control for the "
                + "administrative reach benefit and cannot run alongside it "
                + $"(DistanceStrainAtStrongestPercent = {DistanceStrainAtStrongestPercent}%). "
                + "Enable exactly one, or the arm measures both at once.",
                nameof(ExperimentalConstantDistancePercent));
        }

        RequireNeutralAtFifty(
            ReachBenefitPercent(NeutralAdministration),
            nameof(DistanceStrainAtStrongestPercent),
            $"{DistanceStrainAtStrongestPercent}% at ability 100");

        RequireNeutralAtFifty(
            CapacityPercent(NeutralAdministration),
            $"{nameof(RulerCapacityFloorPercent)}/{nameof(RulerCapacityCeilingPercent)}",
            $"{RulerCapacityFloorPercent}%/{RulerCapacityCeilingPercent}%");
    }

    private static void RequireNeutralAtFifty(int atFifty, string parameter, string configured)
    {
        if (atFifty == NeutralPercent)
        {
            return;
        }

        throw new ArgumentException(
            $"a ruler-dependent conversion must leave an average administrator unchanged: {parameter} "
            + $"= {configured} gives ability {NeutralAdministration} a multiplier of {atFifty}%, not "
            + $"{NeutralPercent}%. That is a global change to every polity in the world, not a ruler "
            + "effect, and it would be reported as one. For a deliberate global comparator use "
            + $"{nameof(ExperimentalConstantDistancePercent)}, which is labelled as a control.",
            parameter);
    }

    public static CohesionRules Default { get; } = new();
}
