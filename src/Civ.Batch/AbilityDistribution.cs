using Civ.Systems;

namespace Civ.Batch;

/// <summary>
/// The exact distribution of ruler ability, and what a conversion does to it on average.
/// </summary>
/// <remarks>
/// <para><b>Why this is computed rather than measured.</b> A one-sided benefit lowers the average
/// distance strain in the world, so any improvement it produces has an innocent explanation:
/// distance simply got cheaper. Ruling that out needs a control set to the <i>same</i> average
/// reduction - and the average has to come from the ability distribution alone, before any polity
/// has risen or fallen, or the control would be tuned by the very outcomes it is supposed to
/// explain.</para>
///
/// <para>Ability is <c>(d1 + d2 + d3) / 3</c> with each draw uniform on 0..100 and integer division
/// at the end, in both <c>RulerFactory</c> and <c>RulerSuccessionSystem</c>. That is a finite
/// distribution over 101 values, so it is enumerated exactly rather than sampled: 101^k outcomes
/// counted by convolution, no randomness and no seed.</para>
///
/// <para>It weights every ruler equally, which is the point but also its main limitation - rulers of
/// large states get no more weight than rulers of one-province statelets, and reigns are not
/// weighted by length. The realized, strain-weighted multiplier is measured separately in
/// <see cref="DistanceMechanism"/>; the two together say whether the control was matched to the
/// right number.</para>
/// </remarks>
internal static class AbilityDistribution
{
    /// <summary>Probability of each ability value 0..100, given the draw count.</summary>
    internal static double[] Of(RulerRules rules)
    {
        int draws = Math.Max(1, rules.AbilityDraws);

        // Counts of each attainable sum, built up one uniform draw at a time.
        double[] sums = new double[1];
        sums[0] = 1;

        for (int d = 0; d < draws; d++)
        {
            var next = new double[sums.Length + 100];
            for (int s = 0; s < sums.Length; s++)
            {
                if (sums[s] == 0)
                {
                    continue;
                }

                double weight = sums[s] / 101.0;
                for (int face = 0; face <= 100; face++)
                {
                    next[s + face] += weight;
                }
            }

            sums = next;
        }

        var ability = new double[101];
        for (int s = 0; s < sums.Length; s++)
        {
            ability[Math.Clamp(s / draws, 0, 100)] += sums[s];
        }

        return ability;
    }

    /// <summary>
    /// The mean connected-distance multiplier a ruler-dependent conversion produces, as a percentage.
    /// </summary>
    /// <remarks>
    /// Returned as a double so the report can show what was rounded away. The control itself takes
    /// the nearest whole percent, because the rule works in integers and a fractional multiplier
    /// would not be reproducible in the simulation.
    /// </remarks>
    internal static double ExpectedDistancePercent(RulerRules rulers, CohesionRules cohesion)
    {
        double[] probability = Of(rulers);
        double expected = 0;

        for (int ability = 0; ability <= 100; ability++)
        {
            expected += probability[ability] * cohesion.ReachBenefitPercent(ability);
        }

        return expected;
    }

    /// <summary>The matched global control for a given reach benefit, as a whole percent.</summary>
    internal static int MatchedControlPercent(RulerRules rulers, CohesionRules cohesion) =>
        Math.Clamp(
            (int)Math.Round(ExpectedDistancePercent(rulers, cohesion), MidpointRounding.AwayFromZero),
            1,
            CohesionRules.NeutralPercent);
}
