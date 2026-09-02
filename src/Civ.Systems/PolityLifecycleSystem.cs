using Civ.Engine.Effects;
using Civ.Engine.State;
using Civ.Engine.Systems;

namespace Civ.Systems;

/// <summary>
/// Keeps polities structurally coherent as territory changes hands.
/// </summary>
/// <remarks>
/// <para>Not politics. This is the caretaker that makes the polity lifecycle survivable: a state
/// that has lost its last province is retired, and a state that has lost its capital is reseated.
/// Both are cases every future system that moves borders - war, secession, collapse, colonisation -
/// would otherwise have to remember to handle, and one of them eventually would not.</para>
///
/// <para>It runs in <see cref="SimulationPhase.Bookkeeping"/>, so it sees the consequences of every
/// earlier phase in the same year and repairs them before invariants are checked.</para>
///
/// <para>The rules are deliberately mechanical: no thresholds, no randomness, no judgement. Whether
/// a state <i>should</i> fall apart is a question for a cohesion model that does not exist yet.</para>
/// </remarks>
public sealed class PolityLifecycleSystem : ISimulationSystem
{
    public string Name => "polity.lifecycle";

    public SimulationPhase Phase => SimulationPhase.Bookkeeping;

    public void Execute(in SystemContext context)
    {
        WorldState world = context.World;

        foreach (Polity polity in world.Polities.All())
        {
            if (!polity.IsActive)
            {
                continue;
            }

            List<Region> held = [.. WorldQueries.RegionsOf(world, polity.Id)];

            if (held.Count == 0)
            {
                context.Effects.Emit(new DissolvePolity(polity.Id, "no remaining territory"));
                continue;
            }

            bool seatHeld = polity.Capital.IsSome
                && world.Regions.TryGet(polity.Capital, out Region? capital)
                && capital.Controller.Equals(polity.Id);

            if (!seatHeld)
            {
                // Lowest region index, not a random or "best" choice. Deterministic and boring is
                // the right default until something models why a seat would move where it does.
                Region seat = held.OrderBy(r => r.Id.Index).First();
                context.Effects.Emit(new SetPolityCapital(polity.Id, seat.Id, "seat of government lost"));
            }
        }
    }
}
