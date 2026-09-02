using Civ.Engine.Effects;
using Civ.Engine.Random;
using Civ.Engine.State;
using Civ.Engine.Systems;
using Civ.Engine.Worldgen;

namespace Civ.Systems;

/// <summary>
/// Rulers age, die, and are replaced.
/// </summary>
/// <remarks>
/// <para><b>What this exists for.</b> Administrative capacity used to be a fixed constant, so the
/// largest state a polity could govern never changed and the world settled into a permanent balance
/// of equal powers. Tying capacity to a ruler who is replaced every few decades is what lets that
/// ceiling move: an exceptional administrator can sustain territory their successor cannot, and the
/// territory then becomes restive through the ordinary cohesion rule.</para>
///
/// <para><b>It does not cause anything to collapse.</b> This system emits exactly two kinds of
/// effect - a death and an accession. It never touches territory, stability, borders or polity
/// status. Every consequence of a bad successor reaches the map through
/// <see cref="CohesionSecessionSystem"/> reading a lower capacity, which is the difference between
/// an emergent imperial cycle and a scripted one.</para>
///
/// <para><b>Ordering within the year.</b> The system runs in <see cref="SimulationPhase.Rulership"/>,
/// ahead of the Polity phase, and emits the death before the accession for the same polity. Effects
/// apply in pipeline order, so the chronicle always reads death-then-accession even when both fall
/// in the same year, and cohesion sees the new ruler's capacity in that same year rather than a year
/// late.</para>
///
/// <para><b>Succession is not inheritance.</b> There are no heirs and no dynasties; the successor is
/// drawn from the same centred distribution as everyone else. That is a placeholder, and it is the
/// obvious first thing a dynasty layer would replace.</para>
/// </remarks>
public sealed class RulerSuccessionSystem(RulerRules? rules = null) : ISimulationSystem
{
    public const string NaturalDeathReason = "natural causes";
    public const string SuccessionReason = "succession";
    public const string InterregnumReason = "vacant throne";

    private readonly RulerRules _rules = rules ?? RulerRules.Default;

    public string Name => "polity.succession";

    public SimulationPhase Phase => SimulationPhase.Rulership;

    public void Execute(in SystemContext context)
    {
        WorldState world = context.World;

        foreach (Polity polity in WorldQueries.ActivePolities(world))
        {
            if (!world.Rulers.TryGet(polity.CurrentRuler, out Ruler? ruler) || !ruler.IsReigning)
            {
                // No living ruler. Should only be reachable if some other system ended a reign
                // without arranging a successor; filling the vacancy keeps the invariant true.
                context.Effects.Emit(new InstallRuler(
                    polity.Id, Successor(in context, polity), InterregnumReason));
                continue;
            }

            if (!Dies(in context, ruler, world.Year))
            {
                continue;
            }

            // Death first, then accession. The applier refuses to install over a living ruler, so
            // this order is load-bearing rather than cosmetic.
            context.Effects.Emit(new EndReign(ruler.Id, NaturalDeathReason, ReignEndReason.Death));
            context.Effects.Emit(new InstallRuler(
                polity.Id, Successor(in context, polity), SuccessionReason));
        }
    }

    private bool Dies(in SystemContext context, Ruler ruler, int year)
    {
        int age = ruler.AgeIn(year);
        if (age >= _rules.MaximumAge)
        {
            return true;
        }

        int permille = _rules.MortalityBasePermille
            + (Math.Max(0, age - _rules.MortalityOnsetAge) * _rules.MortalityRisePermillePerYear);

        if (permille <= 0)
        {
            return false;
        }

        // Keyed on the ruler's own slot, so mortality does not depend on how many polities were
        // evaluated first or on how many rulers have lived and died elsewhere in the world.
        Rng rng = context.Rng((ulong)ruler.Id.Index);
        return rng.Chance(Math.Min(1000, permille));
    }

    private RulerProfile Successor(in SystemContext context, Polity polity)
    {
        // A separate discriminator from the mortality draw, so the qualities of a successor are
        // independent of the timing of the death that produced the vacancy.
        Rng rng = context.Rng(unchecked((ulong)polity.Id.Index + 0x9E37_79B9_7F4A_7C15UL));

        int age = rng.NextInt(_rules.MinAccessionAge, _rules.MaxAccessionAge + 1);

        int total = 0;
        for (int i = 0; i < _rules.AbilityDraws; i++)
        {
            total += rng.NextInt(0, 101);
        }

        int administration = total / Math.Max(1, _rules.AbilityDraws);

        // Drawn from the same stream but a later position, so the two abilities are independent:
        // a great general is no more or less likely to be a great administrator.
        int militaryTotal = 0;
        for (int i = 0; i < _rules.AbilityDraws; i++)
        {
            militaryTotal += rng.NextInt(0, 101);
        }

        int military = militaryTotal / Math.Max(1, _rules.AbilityDraws);
        string name = NameGenerator.Person(ref rng);

        return new RulerProfile(name, context.Year - age, administration, military);
    }
}
