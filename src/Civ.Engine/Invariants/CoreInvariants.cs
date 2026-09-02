using Civ.Engine.Core;
using Civ.Engine.State;

namespace Civ.Engine.Invariants;

/// <summary>
/// The rules that must hold no matter what systems are installed.
/// </summary>
/// <remarks>
/// Most of these guard the polity lifecycle, because that is where structural corruption will come
/// from: creation, dissolution, secession and merger all rewrite who owns what, and a single missed
/// case leaves a region owned by a state that no longer exists. Catching that in year 300 of a test
/// run is cheap; discovering it as an unexplainable chronicle in year 1400 is not.
/// </remarks>
public static class CoreInvariants
{
    public static IReadOnlyList<IInvariant> All =>
    [
        new RegionControllerIsLive(),
        new ActivePolityHoldsTerritory(),
        new PolityCapitalIsOwned(),
        new PopulationIsNonNegative(),
        new AdjacencyIsSymmetric(),
        new DefunctPolityIsInert(),
        new PolityLineageResolves(),
        new ActivePolityHasExactlyOneReigningRuler(),
        new RulerRecordIsCoherent(),
    ];

    /// <summary>No region may be owned by a dissolved or non-existent polity.</summary>
    public sealed class RegionControllerIsLive : IInvariant
    {
        public string Name => nameof(RegionControllerIsLive);

        public void Check(WorldState world, ICollection<InvariantViolation> violations)
        {
            foreach (Region region in world.Regions.All())
            {
                if (region.Controller.IsNone)
                {
                    continue;
                }

                if (!world.Polities.TryGet(region.Controller, out Polity? polity))
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year,
                        $"{region.Name} is controlled by {region.Controller}, which does not exist."));
                }
                else if (!polity.IsActive)
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year,
                        $"{region.Name} is controlled by {polity.Name}, dissolved in {polity.DissolvedYear}."));
                }
            }
        }
    }

    /// <summary>
    /// An active polity holds at least one region. A landless state is a bookkeeping ghost; it
    /// should be dissolved by a system, not left drifting.
    /// </summary>
    public sealed class ActivePolityHoldsTerritory : IInvariant
    {
        public string Name => nameof(ActivePolityHoldsTerritory);

        public void Check(WorldState world, ICollection<InvariantViolation> violations)
        {
            foreach (Polity polity in world.Polities.All())
            {
                if (polity.IsActive && WorldQueries.RegionCountOf(world, polity.Id) == 0)
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year, $"{polity.Name} is active but holds no territory."));
                }
            }
        }
    }

    /// <summary>An active polity's capital must be a region it actually controls.</summary>
    public sealed class PolityCapitalIsOwned : IInvariant
    {
        public string Name => nameof(PolityCapitalIsOwned);

        public void Check(WorldState world, ICollection<InvariantViolation> violations)
        {
            foreach (Polity polity in world.Polities.All())
            {
                if (!polity.IsActive || polity.Capital.IsNone)
                {
                    continue;
                }

                if (!world.Regions.TryGet(polity.Capital, out Region? capital))
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year, $"{polity.Name} has a capital that does not exist."));
                }
                else if (!capital.Controller.Equals(polity.Id))
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year,
                        $"{polity.Name} is seated at {capital.Name}, which it does not control."));
                }
            }
        }
    }

    public sealed class PopulationIsNonNegative : IInvariant
    {
        public string Name => nameof(PopulationIsNonNegative);

        public void Check(WorldState world, ICollection<InvariantViolation> violations)
        {
            foreach (Region region in world.Regions.All())
            {
                if (region.Population < 0)
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year, $"{region.Name} has population {region.Population}."));
                }
            }
        }
    }

    /// <summary>Adjacency must be mutual. Asymmetric edges make spatial reasoning silently wrong.</summary>
    public sealed class AdjacencyIsSymmetric : IInvariant
    {
        public string Name => nameof(AdjacencyIsSymmetric);

        public void Check(WorldState world, ICollection<InvariantViolation> violations)
        {
            foreach (Region region in world.Regions.All())
            {
                foreach (RegionId neighborId in region.Neighbors)
                {
                    if (!world.Regions.TryGet(neighborId, out Region? neighbor))
                    {
                        violations.Add(new InvariantViolation(
                            Name, world.Year, $"{region.Name} borders {neighborId}, which does not exist."));
                        continue;
                    }

                    if (!neighbor.Neighbors.Contains(region.Id))
                    {
                        violations.Add(new InvariantViolation(
                            Name, world.Year,
                            $"{region.Name} borders {neighbor.Name} but not the reverse."));
                    }
                }
            }
        }
    }

    /// <summary>
    /// A recorded parent must refer to a polity that exists, and nothing may be its own ancestor.
    /// </summary>
    /// <remarks>
    /// Successor lineage is the one cross-reference between polities that outlives both of them, so
    /// it is the one most likely to rot silently as states are created and retired. A cycle would
    /// hang any future chronicle that walks the ancestry.
    /// </remarks>
    public sealed class PolityLineageResolves : IInvariant
    {
        public string Name => nameof(PolityLineageResolves);

        public void Check(WorldState world, ICollection<InvariantViolation> violations)
        {
            foreach (Polity polity in world.Polities.All())
            {
                if (polity.Parent.IsNone)
                {
                    continue;
                }

                if (polity.Parent.Equals(polity.Id))
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year, $"{polity.Name} is recorded as its own predecessor."));
                    continue;
                }

                if (!world.Polities.TryGet(polity.Parent, out _))
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year,
                        $"{polity.Name} descends from {polity.Parent}, which does not exist."));
                }
            }
        }
    }

    /// <summary>
    /// Every active polity is ruled by exactly one reigning ruler, who agrees it rules there.
    /// </summary>
    /// <remarks>
    /// <para>"Exactly one" is checked from both directions: the polity points at a reigning ruler,
    /// and no second reigning ruler claims the same polity. A succession that emitted an accession
    /// without ending the previous reign would pass the first check and fail the second.</para>
    ///
    /// <para>Reigning, not living. A ruler who outlived their state is alive and holds nothing, which
    /// is a legitimate archival state rather than a broken one.</para>
    /// </remarks>
    public sealed class ActivePolityHasExactlyOneReigningRuler : IInvariant
    {
        public string Name => nameof(ActivePolityHasExactlyOneReigningRuler);

        public void Check(WorldState world, ICollection<InvariantViolation> violations)
        {
            var reigning = new Dictionary<PolityId, int>();
            foreach (Ruler ruler in world.Rulers.All())
            {
                if (ruler.IsReigning)
                {
                    reigning[ruler.Polity] = reigning.GetValueOrDefault(ruler.Polity) + 1;
                }
            }

            foreach (Polity polity in world.Polities.All())
            {
                int count = reigning.GetValueOrDefault(polity.Id);

                if (!polity.IsActive)
                {
                    if (count > 0)
                    {
                        violations.Add(new InvariantViolation(
                            Name, world.Year, $"{polity.Name} is defunct but still has a reigning ruler."));
                    }

                    continue;
                }

                if (count != 1)
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year, $"{polity.Name} has {count} reigning rulers."));
                }

                if (!world.Rulers.TryGet(polity.CurrentRuler, out Ruler? current))
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year, $"{polity.Name} has no resolvable current ruler."));
                    continue;
                }

                if (!current.IsReigning)
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year,
                        $"{polity.Name} is ruled by {current.Name}, whose reign already ended."));
                }

                if (!current.Polity.Equals(polity.Id))
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year,
                        $"{polity.Name} claims {current.Name}, who is recorded as ruling elsewhere."));
                }
            }
        }
    }

    /// <summary>A ruler's dates must make sense and their ability must be in range.</summary>
    public sealed class RulerRecordIsCoherent : IInvariant
    {
        public string Name => nameof(RulerRecordIsCoherent);

        public void Check(WorldState world, ICollection<InvariantViolation> violations)
        {
            foreach (Ruler ruler in world.Rulers.All())
            {
                if (ruler.AccessionYear < ruler.BirthYear)
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year, $"{ruler.Name} acceded before being born."));
                }

                if (ruler.DeathYear is { } death && death < ruler.AccessionYear)
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year, $"{ruler.Name} died before acceding."));
                }

                if (ruler.ReignEndYear is { } end)
                {
                    if (end < ruler.AccessionYear)
                    {
                        violations.Add(new InvariantViolation(
                            Name, world.Year, $"{ruler.Name} stopped reigning before acceding."));
                    }

                    if (ruler.EndReason is null)
                    {
                        violations.Add(new InvariantViolation(
                            Name, world.Year, $"{ruler.Name} has an ended reign with no recorded cause."));
                    }
                }
                else if (ruler.EndReason is not null)
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year, $"{ruler.Name} has a reign-end cause but is still reigning."));
                }

                // A death always ends a reign; the reverse does not hold.
                if (ruler.DeathYear is not null && ruler.ReignEndYear is null)
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year, $"{ruler.Name} is dead but recorded as still reigning."));
                }

                if (ruler.Administration is < 0 or > 100)
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year,
                        $"{ruler.Name} has administration {ruler.Administration}, outside 0-100."));
                }

                if (ruler.Military is < 0 or > 100)
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year,
                        $"{ruler.Name} has military ability {ruler.Military}, outside 0-100."));
                }

                if (!world.Polities.TryGet(ruler.Polity, out _))
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year, $"{ruler.Name} ruled {ruler.Polity}, which does not exist."));
                }
            }
        }
    }

    /// <summary>A dissolved polity must be fully retired: dated, seatless, landless.</summary>
    public sealed class DefunctPolityIsInert : IInvariant
    {
        public string Name => nameof(DefunctPolityIsInert);

        public void Check(WorldState world, ICollection<InvariantViolation> violations)
        {
            foreach (Polity polity in world.Polities.All())
            {
                if (polity.IsActive)
                {
                    continue;
                }

                if (polity.DissolvedYear is null)
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year, $"{polity.Name} is defunct but has no dissolution year."));
                }

                if (polity.Capital.IsSome)
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year, $"{polity.Name} is defunct but still seated somewhere."));
                }

                if (polity.CurrentRuler.IsSome)
                {
                    violations.Add(new InvariantViolation(
                        Name, world.Year, $"{polity.Name} is defunct but still has a reigning ruler."));
                }
            }
        }
    }
}
