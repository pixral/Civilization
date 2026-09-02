using Civ.Batch;
using Civ.Systems;

namespace Civ.Engine.Tests;

/// <summary>
/// Rule sets that would misreport a global change as a ruler effect are refused outright.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> During the previous experiment two candidate bands - written as
/// <c>100/50</c> and <c>100/25</c> through a weak endpoint that no longer exists - produced the best
/// numbers the project had ever seen: the first empires above 25% world share, and fewer secessions
/// than the control. They were invalid. A one-sided <i>linear</i> band moves its own neutral point,
/// so under <c>100/50</c> an average administrator paid 75% distance strain and every state in the
/// world got a discount. The run was not measuring rulers at all.</para>
///
/// <para>Nothing in the output said so. The arithmetic that catches it is one comparison, and these
/// tests are that comparison, applied where a bad configuration can still be expressed: through the
/// command line, and through a rule set built in code.</para>
/// </remarks>
public sealed class RuleValidationTests
{
    // ------------------------------------------------------------------ accepted

    [Fact]
    public void TheShippedDefaultsAreValid()
    {
        CohesionRules.Default.Validate();

        Assert.Equal(100, CohesionRules.Default.DistanceStrainPercent(50));
        Assert.Equal(100, CohesionRules.Default.CapacityPercent(50));
    }

    [Fact]
    public void TheReachBenefitIsValidAtEveryStrongestEndpoint()
    {
        for (int strongest = 0; strongest <= 100; strongest++)
        {
            CohesionRules rules = CohesionRules.Default with
            {
                DistanceStrainAtStrongestPercent = strongest,
            };

            rules.Validate();
            Assert.Equal(100, rules.DistanceStrainPercent(CohesionRules.NeutralAdministration));
        }
    }

    /// <summary>A capacity band may be as wide as it likes, so long as it is centred on 100%.</summary>
    [Fact]
    public void SymmetricCapacityBandsAreValid()
    {
        foreach ((int floor, int ceiling) in ((int, int)[])[(100, 100), (75, 125), (20, 180), (0, 200)])
        {
            CohesionRules rules = CohesionRules.Default with
            {
                RulerCapacityFloorPercent = floor,
                RulerCapacityCeilingPercent = ceiling,
            };

            rules.Validate();
            Assert.Equal(100, rules.CapacityPercent(CohesionRules.NeutralAdministration));
        }
    }

    [Fact]
    public void TheConstantControlIsValidOnItsOwn()
    {
        CohesionRules rules = CohesionRules.Default with { ExperimentalConstantDistancePercent = 87 };

        rules.Validate();

        // Deliberately not a ruler mechanic: everyone gets the same multiplier.
        Assert.Equal(87, rules.DistanceStrainPercent(0));
        Assert.Equal(87, rules.DistanceStrainPercent(50));
        Assert.Equal(87, rules.DistanceStrainPercent(100));
    }

    // ------------------------------------------------------------------ rejected

    /// <summary>The exact configuration that fooled the previous experiment, in capacity form.</summary>
    [Fact]
    public void ACapacityBandThatMovesTheNeutralPointIsRefused()
    {
        CohesionRules rules = CohesionRules.Default with
        {
            RulerCapacityFloorPercent = 100,
            RulerCapacityCeilingPercent = 50,
        };

        ArgumentException error = Assert.Throws<ArgumentException>(rules.Validate);
        Assert.Contains("average administrator", error.Message, StringComparison.Ordinal);
        Assert.Contains("75%", error.Message, StringComparison.Ordinal);

        // And a simulation cannot be built with it either.
        Assert.Throws<ArgumentException>(() => new CohesionSecessionSystem(rules));
    }

    [Fact]
    public void AnotherNeutralShiftingCapacityBandIsRefused()
    {
        CohesionRules rules = CohesionRules.Default with
        {
            RulerCapacityFloorPercent = 40,
            RulerCapacityCeilingPercent = 200,
        };

        // 40 + 50 * 160 / 100 = 120%: a 20% capacity gift to every polity in the world.
        ArgumentException error = Assert.Throws<ArgumentException>(rules.Validate);
        Assert.Contains("120%", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReachBenefitThatWouldRaiseDistanceStrainIsRefused()
    {
        foreach (int strongest in (int[])[101, 125, 200, -1])
        {
            CohesionRules rules = CohesionRules.Default with
            {
                DistanceStrainAtStrongestPercent = strongest,
            };

            Assert.Throws<ArgumentOutOfRangeException>(rules.Validate);
        }
    }

    [Fact]
    public void TheConstantControlCannotRunAlongsideTheRulerBenefit()
    {
        CohesionRules rules = CohesionRules.Default with
        {
            DistanceStrainAtStrongestPercent = 50,
            ExperimentalConstantDistancePercent = 87,
        };

        ArgumentException error = Assert.Throws<ArgumentException>(rules.Validate);
        Assert.Contains("matched global control", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConstantControlIsRejectedOutsideItsRange()
    {
        foreach (int percent in (int[])[0, -20, 101, 400])
        {
            CohesionRules rules = CohesionRules.Default with
            {
                ExperimentalConstantDistancePercent = percent,
            };

            Assert.Throws<ArgumentOutOfRangeException>(rules.Validate);
        }
    }

    /// <summary>
    /// The constant control cannot become a default by accident.
    /// </summary>
    /// <remarks>
    /// It is the one setting in the file that is exempt from the neutral-point rule, so the only
    /// thing standing between it and a misread report is that nothing turns it on unasked.
    /// </remarks>
    [Fact]
    public void TheConstantControlIsOffUnlessAskedForByName()
    {
        Assert.Equal(
            CohesionRules.NeutralPercent, CohesionRules.Default.ExperimentalConstantDistancePercent);
        Assert.Equal(
            CohesionRules.NeutralPercent, CohesionRules.Default.DistanceStrainAtStrongestPercent);

        Assert.Equal(100, BatchOptions.Parse([]).Cohesion.ExperimentalConstantDistancePercent);
        Assert.Equal(
            100,
            BatchOptions.Parse(["--distance-strongest", "50"])
                .Cohesion.ExperimentalConstantDistancePercent);
    }

    // ------------------------------------------------------------------ through the command line

    [Fact]
    public void TheCommandLineAcceptsAValidReachBenefit()
    {
        BatchOptions options = BatchOptions.Parse(["--distance-strongest", "50"]);

        Assert.Equal(50, options.Cohesion.DistanceStrainAtStrongestPercent);
        Assert.Equal(100, options.Cohesion.DistanceStrainPercent(50));
    }

    [Fact]
    public void TheCommandLineRefusesAnInvalidCombinationImmediately()
    {
        Assert.Throws<ArgumentException>(() => BatchOptions.Parse(
            ["--distance-strongest", "50", "--constant-distance", "87"]));

        Assert.Throws<ArgumentException>(() => BatchOptions.Parse(
            ["--ruler-floor", "100", "--ruler-ceiling", "50"]));

        Assert.Throws<ArgumentOutOfRangeException>(() => BatchOptions.Parse(
            ["--distance-strongest", "150"]));
    }

    // ------------------------------------------------------------------ the matched control

    /// <summary>
    /// The control multiplier is derived from the ability distribution, not chosen.
    /// </summary>
    /// <remarks>
    /// Ability is the mean of three uniform draws, so it is symmetric about 50 and the flat half of
    /// the conversion covers very nearly half the population. A 50% benefit at ability 100 therefore
    /// averages out near 88%, which is what arm C runs at - not the 50% a glance at the endpoint
    /// would suggest.
    /// </remarks>
    [Fact]
    public void TheMatchedControlMatchesTheMeanOfTheRulerBenefit()
    {
        double[] distribution = AbilityDistribution.Of(RulerRules.Default);

        Assert.Equal(1.0, distribution.Sum(), 9);
        Assert.All(distribution, p => Assert.True(p >= 0));

        CohesionRules benefit = CohesionRules.Default with { DistanceStrainAtStrongestPercent = 50 };

        double expected = AbilityDistribution.ExpectedDistancePercent(RulerRules.Default, benefit);
        double manual = 0;
        for (int ability = 0; ability <= 100; ability++)
        {
            manual += distribution[ability] * benefit.ReachBenefitPercent(ability);
        }

        Assert.Equal(manual, expected, 9);
        Assert.InRange(expected, 50, 100);
        Assert.Equal(
            (int)Math.Round(expected, MidpointRounding.AwayFromZero),
            AbilityDistribution.MatchedControlPercent(RulerRules.Default, benefit));
    }

    [Fact]
    public void ADisabledBenefitHasAMatchedControlOfExactlyOneHundred()
    {
        Assert.Equal(
            100, AbilityDistribution.MatchedControlPercent(RulerRules.Default, CohesionRules.Default));
    }
}
