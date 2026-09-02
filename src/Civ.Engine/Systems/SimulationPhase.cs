namespace Civ.Engine.Systems;

/// <summary>
/// Ordered execution stages within a single year.
/// </summary>
/// <remarks>
/// <para>Effects are applied at the end of every phase, not at the end of the tick. A single
/// end-of-tick barrier makes ordering inexpressible - population could never react to a famine in
/// the same year - while applying per system reintroduces exactly the order-dependence the effect
/// queue exists to remove. Phases give coarse, declared ordering between groups and complete
/// order-independence inside a group.</para>
///
/// <para>Every system in one phase reads the same snapshot: the state as it stood when the phase
/// began. Two systems in the same phase therefore cannot see each other's work, which is what makes
/// them safely reorderable and, later, safely parallelisable.</para>
///
/// <para>The full ladder is declared now even though most of it is empty. The dependency direction
/// is the part worth committing to early; filling it in is incremental.</para>
/// </remarks>
public enum SimulationPhase
{
    /// <summary>Climate, disasters, disease. Acts on the world, reads almost nothing.</summary>
    Environment = 0,

    /// <summary>Births, deaths, migration.</summary>
    Population = 1,

    /// <summary>Production, trade, treasuries.</summary>
    Economy = 2,

    /// <summary>Knowledge, culture, religion.</summary>
    Culture = 3,

    /// <summary>
    /// Who holds power: death, succession, accession.
    /// </summary>
    /// <remarks>
    /// Its own phase, ahead of <see cref="Polity"/>, because rulers are an input to the politics
    /// that reads them. A succession must be fully applied before cohesion computes the capacity it
    /// depends on, and systems inside one phase cannot see each other's effects - so putting the two
    /// together would silently delay every consequence of a succession by a year.
    /// </remarks>
    Rulership = 4,

    /// <summary>Internal politics: legitimacy, factions, cohesion, succession.</summary>
    Polity = 5,

    /// <summary>Relations between polities: diplomacy, war, borders.</summary>
    Diplomacy = 6,

    /// <summary>Cleanup and derived state. Runs last, sees everything.</summary>
    Bookkeeping = 7,
}
