namespace Civ.Systems;

/// <summary>
/// Tuning constants for <see cref="RulerSuccessionSystem"/>.
/// </summary>
/// <remarks>
/// Mortality is an age-rising annual hazard rather than a lifespan fixed at birth. Both are
/// deterministic; the hazard needs no extra stored state, and "deterministic" here means the run
/// reproduces exactly, not that the date is knowable in advance.
/// </remarks>
public sealed record RulerRules
{
    public int MinAccessionAge { get; init; } = 20;

    public int MaxAccessionAge { get; init; } = 45;

    /// <summary>Annual death chance for a ruler below <see cref="MortalityOnsetAge"/>.</summary>
    public int MortalityBasePermille { get; init; } = 6;

    public int MortalityOnsetAge { get; init; } = 45;

    /// <summary>Extra annual death chance per year of age past the onset.</summary>
    public int MortalityRisePermillePerYear { get; init; } = 5;

    /// <summary>Age at which death is certain. Bounds the tail so no reign runs for centuries.</summary>
    public int MaximumAge { get; init; } = 95;

    /// <summary>
    /// Uniform draws averaged to produce administrative ability.
    /// </summary>
    /// <remarks>
    /// Three draws put most rulers within about 17 points of 50 and make the extremes genuinely
    /// uncommon, which is what keeps ordinary successions from being crises.
    /// </remarks>
    public int AbilityDraws { get; init; } = 3;

    public static RulerRules Default { get; } = new();
}
