using Civ.Engine.Systems;

namespace Civ.Systems;

/// <summary>
/// The standard pipeline.
/// </summary>
/// <remarks>
/// Composition lives in the content assembly, not the engine. <c>Civ.Engine</c> has no built-in
/// systems and no default pipeline, so it cannot grow a dependency on any particular simulation
/// content - and a host that wants a different world (a test with one system, a batch sweep with a
/// variant ruleset) builds its own list rather than opting out of a hidden default.
/// </remarks>
public static class DefaultSystems
{
    public static IReadOnlyList<ISimulationSystem> Build(
        ExpansionRules? expansion = null,
        CohesionRules? cohesion = null,
        RulerRules? rulers = null) =>
    [
        new PopulationSystem(),

        // Succession is its own phase, ahead of cohesion, so a new ruler's capacity is already in
        // effect when the territory that depends on it is evaluated in the same year.
        new RulerSuccessionSystem(rulers),

        // Cohesion is in the Polity phase and expansion in Diplomacy, so a breakaway state exists
        // by the time conquest is evaluated in the same year and can immediately be fought over.
        // That ordering is what produces reconquest rather than two independent processes.
        new CohesionSecessionSystem(cohesion),
        new OpportunisticExpansionSystem(expansion),
        new PolityLifecycleSystem(),
    ];
}
