using Civ.Engine.Config;
using Civ.Engine.Effects;
using Civ.Engine.Events;
using Civ.Engine.State;
using Civ.Engine.Systems;
using Civ.Systems;

namespace Civ.Engine.Tests;

/// <summary>
/// The character layer: rulers, succession, and the capacity that varies between reigns.
/// </summary>
/// <remarks>
/// The strong-versus-weak fixtures are the important ones. They use identical worlds, identical
/// seeds and immortal rulers, so the only variable is administrative ability - which is the only way
/// to attribute a territorial outcome to ruler quality rather than to the ambient randomness of a
/// generated run.
/// </remarks>
public sealed class RulerTests
{
    /// <summary>No mortality, so a controlled fixture keeps the ruler it was given.</summary>
    private static RulerRules Immortal => RulerRules.Default with
    {
        MortalityBasePermille = 0,
        MortalityRisePermillePerYear = 0,
        MaximumAge = 100_000,
    };

    /// <summary>
    /// The default cohesion shape scaled up tenfold.
    /// </summary>
    /// <remarks>
    /// Scaling strain and capacity together leaves which regions are restive unchanged while making
    /// the margins large enough that an eligible breakaway resolves on a known year instead of an
    /// expected one.
    /// </remarks>
    private static CohesionRules Scaled => CohesionRules.Default with
    {
        AdministrativeCapacity = 1_500,
        DistanceStrainPerStep = 140,
        SizeStrainPerRegion = 30,
        DisconnectionStrain = 700,
        ProsperityStrain = 140,
        StrainPerPermille = 1,
        MaxAttemptPermille = 1000,
    };

    private static SimulationConfig Config => SimulationConfig.Default with
    {
        Seed = 8080,
        InvariantMode = InvariantMode.EveryTick,
        ThrowOnInvariantViolation = true,
    };

    private static Simulation Generated(ulong seed, int years, int width = 12, int height = 8, int polities = 6)
    {
        Simulation sim = Simulation.Create(
            SimulationConfig.Default with
            {
                Seed = seed,
                WorldWidth = width,
                WorldHeight = height,
                InitialPolityCount = polities,
                InvariantMode = InvariantMode.Periodic,
            },
            DefaultSystems.Build());

        sim.AdvanceYears(years);
        return sim;
    }

    /// <summary>An eleven-region realm: near provinces governable, far ones marginal.</summary>
    private static (WorldBuilder Builder, RegionId[] Line, PolityId Realm) LongRealm(int administration)
    {
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(Enumerable.Repeat(10_000L, 11).ToArray());
        PolityId realm = builder.Polity("Long Realm", 50, administration, line);
        return (builder, line, realm);
    }

    private static IEnumerable<PolityFoundedEvent> Secessions(Simulation sim) =>
        sim.Chronicle.Events
            .OfType<PolityFoundedEvent>()
            .Where(e => e.Reason == CohesionSecessionSystem.SecessionReason);

    // ---------------------------------------------------------------- presence

    [Fact]
    public void InitialPolitiesReceiveRulers()
    {
        Simulation sim = Generated(seed: 1, years: 0);

        Assert.NotEmpty(WorldQueries.ActivePolities(sim.World));

        foreach (Polity polity in WorldQueries.ActivePolities(sim.World))
        {
            Ruler ruler = sim.World.Rulers.Get(polity.CurrentRuler);
            Assert.True(ruler.IsAlive);
            Assert.Equal(polity.Id, ruler.Polity);
            Assert.Equal(sim.Config.StartYear, ruler.AccessionYear);
            Assert.InRange(ruler.Administration, 0, 100);
        }

        // Every founding was announced, and so was every accession.
        Assert.Equal(
            sim.Chronicle.Events.OfType<PolityFoundedEvent>().Count(),
            sim.Chronicle.Events.OfType<RulerAccessionEvent>().Count());
    }

    [Fact]
    public void EveryActivePolityHasExactlyOneReigningRulerAcrossALongRun()
    {
        Simulation sim = Generated(seed: 2, years: 1500);

        var reigningByPolity = new Dictionary<PolityId, int>();
        foreach (Ruler ruler in sim.World.Rulers.All())
        {
            if (ruler.IsReigning)
            {
                reigningByPolity[ruler.Polity] = reigningByPolity.GetValueOrDefault(ruler.Polity) + 1;
            }
        }

        foreach (Polity polity in sim.World.Polities.All())
        {
            int expected = polity.IsActive ? 1 : 0;
            Assert.Equal(expected, reigningByPolity.GetValueOrDefault(polity.Id));

            if (polity.IsActive)
            {
                Assert.True(sim.World.Rulers.Get(polity.CurrentRuler).IsReigning);
            }
            else
            {
                Assert.True(polity.CurrentRuler.IsNone);
            }
        }
    }

    /// <summary>
    /// A state falling does not kill the person who ruled it.
    /// </summary>
    /// <remarks>
    /// The reign ends and is archived with a cause; the ruler stays alive and simply never reigns
    /// again. Conflating the two previously turned every extinction into a fabricated natural death.
    /// </remarks>
    [Fact]
    public void ExtinctionEndsTheReignWithoutKillingTheRuler()
    {
        Simulation sim = Generated(seed: 7, years: 2000, width: 14, height: 10, polities: 10);

        var ends = sim.Chronicle.Events
            .OfType<ReignEndedEvent>()
            .Where(e => e.EndReason == ReignEndReason.PolityExtinct)
            .ToList();

        Assert.NotEmpty(ends);

        foreach (ReignEndedEvent end in ends)
        {
            Ruler ruler = sim.World.Rulers.Get(end.Ruler);
            Assert.True(ruler.IsAlive, "an extinguished state must not kill its ruler");
            Assert.False(ruler.IsReigning);
            Assert.Equal(end.Year, ruler.ReignEndYear);
            Assert.Equal(ReignEndReason.PolityExtinct, ruler.EndReason);
            Assert.Null(ruler.DeathYear);

            // And no death was reported for them.
            Assert.DoesNotContain(
                sim.Chronicle.Events.OfType<RulerDeathEvent>(), d => d.Ruler == end.Ruler);
        }
    }

    [Fact]
    public void ANaturalDeathEndsTheReignAndIsRecordedAsADeath()
    {
        Simulation sim = Generated(seed: 3, years: 600);

        foreach (RulerDeathEvent death in sim.Chronicle.Events.OfType<RulerDeathEvent>())
        {
            Ruler ruler = sim.World.Rulers.Get(death.Ruler);
            Assert.False(ruler.IsAlive);
            Assert.False(ruler.IsReigning);
            Assert.Equal(death.Year, ruler.DeathYear);
            Assert.Equal(death.Year, ruler.ReignEndYear);
            Assert.Equal(ReignEndReason.Death, ruler.EndReason);
        }
    }

    [Fact]
    public void BreakawayPolitiesReceiveARulerInTheYearTheyAreFounded()
    {
        // A cut-off province with nowhere to appeal: it secedes on the first tick.
        var builder = new WorldBuilder();
        RegionId[] line = builder.Line(10_000, 10_000, 10_000);
        builder.Polity("Sundered Realm", 50, line[0], line[2]);
        builder.Polity("Wedge", 50, line[1]);

        Simulation sim = Simulation.Resume(
            Config,
            builder.World,
            [
                new CohesionSecessionSystem(CohesionRules.Default with
                {
                    DisconnectionStrain = 5_000,
                    StrainPerPermille = 1,
                    MaxAttemptPermille = 1000,
                }),
                new PolityLifecycleSystem(),
            ]);

        sim.AdvanceYear();

        PolityFoundedEvent founding = Assert.Single(Secessions(sim));
        Polity successor = sim.World.Polities.Get(founding.Polity);

        Ruler ruler = sim.World.Rulers.Get(successor.CurrentRuler);
        Assert.True(ruler.IsAlive);
        Assert.Equal(successor.Id, ruler.Polity);
        Assert.Equal(sim.Year, ruler.AccessionYear);

        Assert.Contains(
            sim.Chronicle.Events.OfType<RulerAccessionEvent>(),
            e => e.Polity == successor.Id && e.Year == sim.Year);

        Assert.Empty(sim.Violations);
    }

    // ---------------------------------------------------------------- lifecycle

    [Fact]
    public void AgeIsDerivedFromBirthYear()
    {
        var builder = new WorldBuilder(seed: 1, year: 400);
        RegionId[] line = builder.Line(10_000, 10_000);
        PolityId realm = builder.Polity("Realm", 50, 50, line);

        Ruler ruler = builder.World.Rulers.Get(builder.RulerOf(realm));

        Assert.Equal(370, ruler.BirthYear);
        Assert.Equal(30, ruler.AgeIn(400));
        Assert.Equal(75, ruler.AgeIn(445));
        Assert.Equal(0, ruler.ReignLengthAt(400));
        Assert.Equal(45, ruler.ReignLengthAt(445));
    }

    [Fact]
    public void RulersDieAndAreReplaced()
    {
        Simulation sim = Generated(seed: 3, years: 800);

        var deaths = sim.Chronicle.Events.OfType<RulerDeathEvent>().ToList();
        var accessions = sim.Chronicle.Events.OfType<RulerAccessionEvent>().ToList();

        Assert.True(deaths.Count > 20, $"expected many reigns to end, saw {deaths.Count}");

        // Every death is answered by an accession somewhere, except where the state itself fell.
        Assert.True(accessions.Count >= deaths.Count - sim.World.Polities.Count);

        foreach (RulerDeathEvent death in deaths)
        {
            Ruler ruler = sim.World.Rulers.Get(death.Ruler);
            Assert.False(ruler.IsAlive);
            Assert.Equal(death.Year, ruler.DeathYear);
        }
    }

    [Fact]
    public void DeathIsRecordedBeforeAccessionWhenBothFallInTheSameYear()
    {
        Simulation sim = Generated(seed: 4, years: 600);
        var events = sim.Chronicle.Events;

        int checkedPairs = 0;

        for (int i = 0; i < events.Count; i++)
        {
            if (events[i] is not RulerDeathEvent death)
            {
                continue;
            }

            // The accession that answers this death, in the same year and the same polity.
            int accession = -1;
            for (int j = i + 1; j < events.Count && events[j].Year == death.Year; j++)
            {
                if (events[j] is RulerAccessionEvent a && a.Polity == death.Polity)
                {
                    accession = j;
                    break;
                }
            }

            if (accession < 0)
            {
                continue;
            }

            Assert.True(accession > i, "accession must be recorded after the death it followed");
            checkedPairs++;

            // And no accession for that polity precedes the death in the same year.
            for (int j = i - 1; j >= 0 && events[j].Year == death.Year; j--)
            {
                if (events[j] is RulerAccessionEvent earlier && earlier.Polity == death.Polity)
                {
                    Assert.Fail("an accession was recorded before the death that caused it");
                }
            }
        }

        Assert.True(checkedPairs > 10, $"expected same-year successions to test, saw {checkedPairs}");
    }

    [Fact]
    public void RepeatedSuccessionsOverLongRunsPreserveEveryInvariant()
    {
        Simulation sim = Simulation.Create(
            SimulationConfig.Default with
            {
                Seed = 99991,
                WorldWidth = 14,
                WorldHeight = 10,
                InitialPolityCount = 10,
                InvariantMode = InvariantMode.EveryTick,
                ThrowOnInvariantViolation = true,
            },
            DefaultSystems.Build());

        sim.AdvanceYears(2000);

        Assert.Empty(sim.Violations);
        Assert.True(sim.Chronicle.Events.OfType<RulerDeathEvent>().Count() > 200);
        Assert.True(sim.World.Rulers.Count > 200);
    }

    // ---------------------------------------------------------------- determinism

    [Fact]
    public void SuccessionIsDeterministic()
    {
        Simulation first = Generated(seed: 5150, years: 900);
        Simulation second = Generated(seed: 5150, years: 900);

        Assert.Equal(first.StateHash(), second.StateHash());
        Assert.Equal(first.World.Rulers.Count, second.World.Rulers.Count);

        for (int i = 0; i < first.Chronicle.Count; i++)
        {
            Assert.Equal(first.Chronicle.Events[i], second.Chronicle.Events[i]);
        }

        foreach (Ruler ruler in first.World.Rulers.All())
        {
            Ruler other = second.World.Rulers.Get(ruler.Id);
            Assert.Equal(ruler.Name, other.Name);
            Assert.Equal(ruler.Administration, other.Administration);
            Assert.Equal(ruler.BirthYear, other.BirthYear);
            Assert.Equal(ruler.DeathYear, other.DeathYear);
        }
    }

    [Fact]
    public void RulerDataParticipatesInTheStateHash()
    {
        WorldState Build(int administration)
        {
            var builder = new WorldBuilder();
            RegionId[] line = builder.Line(10_000, 10_000);
            builder.Polity("Realm", 50, administration, line);
            return builder.World;
        }

        // Control: identical rulers, identical hash.
        Assert.Equal(WorldHasher.Hash(Build(50)), WorldHasher.Hash(Build(50)));

        // Ability is the only difference, and the hash notices.
        Assert.NotEqual(WorldHasher.Hash(Build(30)), WorldHasher.Hash(Build(70)));
    }

    // ---------------------------------------------------------------- archival

    [Fact]
    public void RulerEventsRemainRenderableAfterTheRulerDies()
    {
        Simulation sim = Generated(seed: 6, years: 1200);

        RulerAccessionEvent early = sim.Chronicle.Events
            .OfType<RulerAccessionEvent>()
            .First(e => e.Year < 200);

        Ruler ruler = sim.World.Rulers.Get(early.Ruler);
        Assert.False(ruler.IsReigning);

        // The event still resolves and still reads correctly a millennium later.
        Assert.Contains(early.RulerName, early.Text, StringComparison.Ordinal);
        Assert.Contains(early.PolityName, early.Text, StringComparison.Ordinal);
        Assert.Equal(ruler.Name, early.RulerName);
        Assert.Equal(ruler.Administration, early.Administration);
    }

    [Fact]
    public void RulerEventsRemainRenderableAfterThePolityBecomesExtinct()
    {
        Simulation sim = Generated(seed: 7, years: 2000, width: 14, height: 10, polities: 10);

        PolityDissolvedEvent fall = sim.Chronicle.Events.OfType<PolityDissolvedEvent>().First();
        Polity dead = sim.World.Polities.Get(fall.Polity);
        Assert.False(dead.IsActive);
        Assert.True(dead.CurrentRuler.IsNone);

        var reigns = sim.Chronicle.Events
            .OfType<RulerAccessionEvent>()
            .Where(e => e.Polity == fall.Polity)
            .ToList();

        Assert.NotEmpty(reigns);

        foreach (RulerAccessionEvent accession in reigns)
        {
            // The id still resolves against the archive, and the text needs nothing live to render.
            // The last of them may still be alive - the state fell, not the person.
            Ruler ruler = sim.World.Rulers.Get(accession.Ruler);
            Assert.False(ruler.IsReigning);
            Assert.Equal(fall.Polity, ruler.Polity);
            Assert.Contains(accession.RulerName, accession.Text, StringComparison.Ordinal);
            Assert.Contains(fall.PolityName, accession.Text, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------- capacity

    [Fact]
    public void AnAverageRulerPreservesTheConfiguredBaselineCapacity()
    {
        CohesionRules rules = CohesionRules.Default;

        Assert.Equal(rules.AdministrativeCapacity, rules.EffectiveCapacity(50));

        // And the extremes land on the configured floor and ceiling.
        Assert.Equal(rules.AdministrativeCapacity * 75 / 100, rules.EffectiveCapacity(0));
        Assert.Equal(rules.AdministrativeCapacity * 125 / 100, rules.EffectiveCapacity(100));

        // Monotonic in between, so a better administrator is never worse.
        for (int ability = 1; ability <= 100; ability++)
        {
            Assert.True(rules.EffectiveCapacity(ability) >= rules.EffectiveCapacity(ability - 1));
        }
    }

    [Fact]
    public void AStrongAdministratorGovernsTerritoryAWeakOneCannot()
    {
        Simulation Run(int administration)
        {
            (WorldBuilder builder, _, _) = LongRealm(administration);
            Simulation sim = Simulation.Resume(
                Config,
                builder.World,
                [
                    new RulerSuccessionSystem(Immortal),
                    new CohesionSecessionSystem(Scaled),
                    new PolityLifecycleSystem(),
                ]);

            sim.AdvanceYears(150);
            return sim;
        }

        Simulation strong = Run(administration: 100);
        Simulation weak = Run(administration: 0);

        // Same world, same seed, same systems. Only the ruler differs.
        Assert.Empty(Secessions(strong));
        Assert.Single(WorldQueries.ActivePolities(strong.World));
        Assert.Equal(11, WorldQueries.RegionCountOf(strong.World, strong.World.Polities.AllIds().First()));

        Assert.NotEmpty(Secessions(weak));
        Assert.True(WorldQueries.ActivePolities(weak.World).Count() > 1);

        Assert.Empty(strong.Violations);
        Assert.Empty(weak.Violations);
    }

    /// <summary>
    /// The causal chain the whole stage exists for, with nothing scripted in it.
    /// </summary>
    /// <remarks>
    /// A strong administrator holds an eleven-province realm together for over a century. The moment
    /// a weak successor takes the throne, the periphery becomes restive and leaves - through the
    /// ordinary cohesion rule, reading nothing but a lower capacity. No system emits a territorial
    /// effect because a ruler died.
    /// </remarks>
    [Fact]
    public void AWeakSuccessorExposesTerritoryWithNoCollapseRuleInvolved()
    {
        const int SuccessionYear = 120;

        (WorldBuilder builder, _, PolityId realm) = LongRealm(administration: 100);

        var coup = new ScriptedSystem("test.succession", SimulationPhase.Rulership, (in SystemContext ctx) =>
        {
            if (ctx.Year != SuccessionYear)
            {
                return;
            }

            Polity polity = ctx.World.Polities.Get(realm);
            ctx.Effects.Emit(new EndReign(polity.CurrentRuler, "test"));
            ctx.Effects.Emit(new InstallRuler(
                realm,
                new RulerProfile("The Unready", ctx.Year - 30, 0),
                "test"));
        });

        Simulation sim = Simulation.Resume(
            Config,
            builder.World,
            [coup, new CohesionSecessionSystem(Scaled), new PolityLifecycleSystem()]);

        sim.AdvanceYears(SuccessionYear - 1);

        // A century of strong government, no fragmentation.
        Assert.Empty(Secessions(sim));
        Assert.Equal(11, WorldQueries.RegionCountOf(sim.World, realm));

        sim.AdvanceYears(150);

        var afterwards = Secessions(sim).ToList();
        Assert.NotEmpty(afterwards);
        Assert.All(afterwards, e => Assert.True(e.Year >= SuccessionYear));
        Assert.True(WorldQueries.RegionCountOf(sim.World, realm) < 11);

        // The realm lost territory but was never dissolved by anything: cohesion took provinces,
        // it did not stage a collapse.
        Assert.True(sim.World.Polities.Get(realm).IsActive);
        Assert.Empty(sim.Chronicle.Events.OfType<PolityDissolvedEvent>());
        Assert.Empty(sim.Violations);
    }
}
