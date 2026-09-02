using System.Diagnostics;
using System.Globalization;
using Civ.Engine;
using Civ.Engine.Config;
using Civ.Engine.Events;
using Civ.Engine.State;
using Civ.Engine.Systems;
using Civ.Batch;
using Civ.Systems;

// Headless sweep. No console interaction, no rendering of world state - just many runs and a table.
//
// This exists from the first commit because it is the tool the project will actually be developed
// with. A simulation with no map cannot be evaluated by looking at it; every question worth asking
// ("does anything survive 2000 years", "did that change make collapse more common") is a question
// about a distribution across seeds, not about one run.

BatchOptions options = BatchOptions.Parse(args);
Console.WriteLine(options.Describe());
Console.WriteLine();

// Three arms, identical seeds and settings, isolating one mechanism each. Without this the question
// "how much did rulers change the equilibrium" cannot be answered, because every figure would be
// compared against a run from a different build.
if (options.PairedAb)
{
    // A is the accepted baseline exactly as it ships. B adds the one-sided reach benefit and nothing
    // else. C swaps that benefit for a flat multiplier set to the average the benefit produces, so
    // the arms differ by *who* gets cheaper distance rather than by how much cheaper it gets on
    // average - which is the only way to tell an exceptional-ruler effect from a general discount.
    CohesionRules baseline = options.Cohesion with
    {
        DistanceStrainAtStrongestPercent = CohesionRules.NeutralPercent,
        ExperimentalConstantDistancePercent = CohesionRules.NeutralPercent,
    };

    CohesionRules benefit = baseline with
    {
        DistanceStrainAtStrongestPercent = options.Cohesion.DistanceStrainAtStrongestPercent,
    };

    // Derived from the ability distribution alone, before a single year is simulated, so the control
    // cannot be tuned by the outcomes it is meant to explain.
    double expectedPercent = AbilityDistribution.ExpectedDistancePercent(options.Rulers, benefit);
    int matched = AbilityDistribution.MatchedControlPercent(options.Rulers, benefit);

    CohesionRules control = baseline with { ExperimentalConstantDistancePercent = matched };

    (string Name, string Detail, CohesionRules Cohesion, ExpansionRules Expansion)[] arms =
    [
        ("A", "accepted model: capacity band, tempo band, distance 100%", baseline, options.Rules),
        ("B", $"+ administrative reach: distance {benefit.DistanceStrainAtStrongestPercent}% at "
            + "ability 100, 100% at or below 50", benefit, options.Rules),
        ("C", $"matched global control: flat {matched}% distance for everyone (expected mean of B: "
            + $"{expectedPercent:0.00}%)", control, options.Rules),
    ];

    Console.WriteLine(
        $"matched control: mean multiplier of arm B over the ability distribution is "
        + $"{expectedPercent:0.000}%, applied to arm C as a flat {matched}%.");
    Console.WriteLine();

    var armResults = new RunResult[arms.Length][];
    var outcomes = new List<ArmOutcome>();

    for (int arm = 0; arm < arms.Length; arm++)
    {
        var runs = new RunResult[options.SeedCount];
        var repeats = new RunResult[options.SeedCount];

        int index = arm;
        System.Threading.Tasks.Parallel.For(0, options.SeedCount, i =>
        {
            ulong seed = options.FirstSeed + (ulong)i;
            runs[i] = RunWith(options, seed, arms[index].Cohesion, arms[index].Expansion);

            // Each arm is reproduced against itself. Comparing arms to each other says nothing about
            // determinism - they are different rulesets and are expected to diverge.
            if (options.Verify)
            {
                repeats[i] = RunWith(options, seed, arms[index].Cohesion, arms[index].Expansion);
            }
        });

        int armMismatches = 0;
        if (options.Verify)
        {
            for (int i = 0; i < options.SeedCount; i++)
            {
                if (runs[i].StateHash != repeats[i].StateHash)
                {
                    armMismatches++;
                    Console.WriteLine(
                        $"  MISMATCH arm {arms[arm].Name} seed {runs[i].Seed}: "
                        + $"{runs[i].StateHash:x16} vs {repeats[i].StateHash:x16}");
                }
            }
        }

        armResults[arm] = runs;
        outcomes.Add(new ArmOutcome(
            arms[arm].Name, runs.Sum(r => r.Violations), armMismatches, options.Verify));
    }

    ReportArms(options, [.. arms.Select(a => (a.Name, a.Detail))], armResults, outcomes);
    return BatchOutcome.ExitCode(outcomes);
}

var results = new RunResult[options.SeedCount];

if (options.Parallel)
{
    // The one place parallelism clearly pays here. A single run is inherently sequential and
    // globally coupled, but runs are completely independent of each other. Results are written to
    // fixed slots so the report is byte-identical regardless of completion order.
    System.Threading.Tasks.Parallel.For(0, options.SeedCount, i =>
    {
        results[i] = Run(options, options.FirstSeed + (ulong)i);
    });
}
else
{
    for (int i = 0; i < options.SeedCount; i++)
    {
        results[i] = Run(options, options.FirstSeed + (ulong)i);
    }
}

Console.WriteLine(
    "seed        year  alive  mean   min  max  born  died  secede  reconq  expand  "
    + "largest%  avg size  confl  viol  state hash          ms");
Console.WriteLine(new string('-', 137));

foreach (RunResult r in results)
{
    Console.WriteLine(
        $"{r.Seed,-10}  {r.Year,4}  {r.AlivePolities,5}  {r.MeanPolities,4:0.0}  "
        + $"{r.MinPolities,4}  {r.MaxPolities,3}  {r.Born,4}  {r.Extinct,4}  {r.Secessions,6}  "
        + $"{r.Reconquests,6}  {r.Expansions,6}  {r.LargestSharePercent,7}%  {r.MeanPolitySize,8:0.0}  "
        + $"{r.Conflicts,5}  {r.Violations,4}  {r.StateHash,16:x16}  {r.Milliseconds,4}");
}

Console.WriteLine();
Console.WriteLine(
    $"{results.Length} runs | states created {results.Sum(r => r.Born)} "
    + $"| states extinguished {results.Sum(r => r.Extinct)} "
    + $"| secessions {results.Sum(r => r.Secessions)} "
    + $"| reconquests {results.Sum(r => r.Reconquests)} "
    + $"| expansions {results.Sum(r => (long)r.Expansions)}");
Console.WriteLine(
    $"effect conflicts {results.Sum(r => r.Conflicts)} "
    + $"| invariant violations {results.Sum(r => r.Violations)}");
Console.WriteLine(
    $"largest polity share: mean {results.Average(r => r.LargestSharePercent):0.0}%, "
    + $"max {results.Max(r => r.LargestSharePercent)}% "
    + $"| average polity size: {results.Average(r => r.MeanPolitySize):0.0} regions");
Console.WriteLine(
    $"peak largest share: mean {results.Average(r => r.PeakSharePercent):0.0}%, "
    + $"max {results.Max(r => r.PeakSharePercent)}% "
    + $"- the gap against the final figure is empires that rose and then fell back.");
Console.WriteLine(
    $"polity count: final mean {results.Average(r => r.AlivePolities):0.0}, "
    + $"low-water mean {results.Average(r => r.MinPolities):0.0}, "
    + $"high-water mean {results.Average(r => r.MaxPolities):0.0} "
    + $"(started at {results[0].PolitiesByYear[0]})");

// Whether the count genuinely oscillates or merely drifts. A run whose high-water mark is its
// starting value is monotonic decline wearing a distribution.
int oscillating = results.Count(r => r.MaxPolities > r.PolitiesByYear[0]);
Console.WriteLine(
    $"{oscillating}/{results.Length} runs rose above their starting polity count at some point.");

// Average polity count over time, across every seed. The single most useful curve in the report:
// a conquest-only world has no counter-force to fragmentation, so this line only ever falls, and
// how fast it falls is the whole question.
Console.WriteLine();
Console.WriteLine("ACTIVE POLITIES OVER TIME (mean across seeds)");

const int Checkpoints = 10;
for (int c = 0; c <= Checkpoints; c++)
{
    int year = options.Years * c / Checkpoints;
    double mean = results.Average(r => (double)r.PolitiesByYear[year]);
    double largest = results.Average(r => (double)r.LargestShareByYear[year]);
    int bar = (int)Math.Round(mean * 40 / Math.Max(1, results.Max(x => x.PolitiesByYear[0])));

    Console.WriteLine(
        $"  year {year,5}  {mean,5:0.0} polities  largest {largest,4:0.0}%  {new string('#', bar)}");
}

RulerAnalysis rulers = results.Aggregate(RulerAnalysis.Empty, (acc, r) => acc + r.Rulers);

Console.WriteLine();
Console.WriteLine("RULERS");
Console.WriteLine(
    $"  reigns ended: {rulers.NaturalDeaths} natural deaths, "
    + $"{rulers.ReignsEndedByExtinction} by the fall of the state, "
    + $"{rulers.ReignsEndedByDisplacement} by displacement");
Console.WriteLine(
    $"  successions {rulers.Successions} "
    + $"| reign length: mean {rulers.MeanReign:0.0}, min {rulers.MinReign}, max {rulers.MaxReign} years");
Console.WriteLine(
    "  zero-year reigns by recorded cause: "
    + string.Join(", ", Enumerable.Range(0, RulerAnalysis.EndReasons)
        .Select(i => $"{rulers.ZeroYearReignsByReason[i]} {RulerAnalysis.EndReasonNames[i]}")));

Console.WriteLine();
Console.WriteLine("  ability band   rulers   share   mean polity size");
int totalRulers = rulers.AbilityHistogram.Sum();
for (int b = 0; b < RulerAnalysis.Bands; b++)
{
    double share = totalRulers == 0 ? 0 : 100.0 * rulers.AbilityHistogram[b] / totalRulers;
    Console.WriteLine(
        $"  {RulerAnalysis.BandNames[b],-12}  {rulers.AbilityHistogram[b],7}  {share,5:0.0}%  "
        + $"{rulers.MeanSizeInBand(b),16:0.00}");
}

Console.WriteLine();
Console.WriteLine(
    $"  ability vs polity size: r = {rulers.Immediate.R:0.000} (same year, n={rulers.Immediate.N:N0}), "
    + $"r = {rulers.Lagged.R:0.000} (25 years later, n={rulers.Lagged.N:N0})");

Console.WriteLine();
Console.WriteLine("  territory change by ability band (regions)");
Console.WriteLine("  band          over the whole reign      over first 25 years of reign");
for (int b = 0; b < RulerAnalysis.Bands; b++)
{
    Delta reign = rulers.BandReignDelta[b];
    Delta first = rulers.BandFirst25Delta[b];
    Console.WriteLine(
        $"  {RulerAnalysis.BandNames[b],-12}  {reign.Mean,+8:0.00} (n={reign.Count,6})      "
        + $"{first.Mean,+8:0.00} (n={first.Count,6})");
}

Console.WriteLine();
Console.WriteLine("MECHANISM: did ruler ability change which regions were restive?");
Console.WriteLine("  (observed in the Polity phase, from the state cohesion itself reads)");
Console.WriteLine(
    $"  all polity-years   : {rulers.Mechanism.Changed,8:N0} / {rulers.Mechanism.PolityYears,9:N0} "
    + $"= {rulers.Mechanism.ChangedRate,5:0.00}% differ from an average administrator");
Console.WriteLine(
    $"    exposed extra regions in {rulers.Mechanism.Exposed:N0} polity-years "
    + $"({rulers.Mechanism.RegionsExposed:N0} region-years), "
    + $"held regions in {rulers.Mechanism.Held:N0} ({rulers.Mechanism.RegionsHeld:N0} region-years)");
Console.WriteLine(
    $"  {RulerAnalysis.LargePolityRegions}+ region polities: {rulers.MechanismLarge.Changed,8:N0} / "
    + $"{rulers.MechanismLarge.PolityYears,9:N0} = {rulers.MechanismLarge.ChangedRate,5:0.00}%");

Console.WriteLine();
Console.WriteLine("ADMINISTRATIVE REACH (connected distance only, benefit-only conversion)");
Console.WriteLine(
    $"  expected mean multiplier {AbilityDistribution.ExpectedDistancePercent(options.Rulers, options.Cohesion):0.000}% "
    + $"| realized (strain-weighted) {rulers.Distance.RealizedMultiplierPercent:0.000}%");
Console.WriteLine(
    $"  polity-years changed {rulers.Distance.ChangedRate:0.00}% "
    + $"| region-years retained {rulers.Distance.RegionsRetained:N0} "
    + $"| region-years exposed {rulers.Distance.RegionsExposed:N0} (must be 0)");
Console.WriteLine(
    $"  connected distance is {rulers.Distance.DistanceSharePercent:0.0}% of all strain "
    + $"(size term {rulers.Distance.SizeSharePercent:0.0}%); "
    + $"the benefit removed {rulers.Distance.StrainRemovedPercent:0.00}% of all strain");

Console.WriteLine();
Console.WriteLine(
    $"MAJOR TERRITORIAL LOSS (>=25% of land lost within {RulerAnalysis.WindowYears} years)");
Console.WriteLine(
    $"  after a succession : {rulers.AfterSuccession.MajorLosses,6} / {rulers.AfterSuccession.Windows,7} "
    + $"= {rulers.AfterSuccession.Rate,5:0.0}%");
Console.WriteLine(
    $"  ordinary years     : {rulers.Ordinary.MajorLosses,6} / {rulers.Ordinary.Windows,7} "
    + $"= {rulers.Ordinary.Rate,5:0.0}%   (control)");
Console.WriteLine(
    $"  successor stronger : {rulers.AfterStrongerSuccessor.MajorLosses,6} / "
    + $"{rulers.AfterStrongerSuccessor.Windows,7} = {rulers.AfterStrongerSuccessor.Rate,5:0.0}%");
Console.WriteLine(
    $"  successor weaker   : {rulers.AfterWeakerSuccessor.MajorLosses,6} / "
    + $"{rulers.AfterWeakerSuccessor.Windows,7} = {rulers.AfterWeakerSuccessor.Rate,5:0.0}%");

Console.WriteLine();
Console.WriteLine(
    $"  restricted to polities holding {RulerAnalysis.LargePolityRegions}+ regions, where capacity binds");
Console.WriteLine(
    $"  after a succession : {rulers.AfterSuccessionLarge.MajorLosses,6} / "
    + $"{rulers.AfterSuccessionLarge.Windows,7} = {rulers.AfterSuccessionLarge.Rate,5:0.0}%");
Console.WriteLine(
    $"  ordinary years     : {rulers.OrdinaryLarge.MajorLosses,6} / {rulers.OrdinaryLarge.Windows,7} "
    + $"= {rulers.OrdinaryLarge.Rate,5:0.0}%   (control)");

Console.WriteLine();
Console.WriteLine("  by successor ability band");
for (int b = 0; b < RulerAnalysis.Bands; b++)
{
    LossWindow w = rulers.BandLosses[b];
    Console.WriteLine(
        $"  {RulerAnalysis.BandNames[b],-12}  {w.MajorLosses,6} / {w.Windows,7} = {w.Rate,5:0.0}%");
}

int mismatches = 0;

if (options.Verify)
{
    Console.WriteLine();
    Console.WriteLine("Determinism check: re-running every seed and comparing state hashes.");

    for (int i = 0; i < options.SeedCount; i++)
    {
        RunResult repeat = Run(options, results[i].Seed);
        if (repeat.StateHash != results[i].StateHash)
        {
            mismatches++;
            Console.WriteLine(
                $"  MISMATCH seed {results[i].Seed}: {results[i].StateHash:x16} vs {repeat.StateHash:x16}");
        }
    }

    Console.WriteLine(mismatches == 0
        ? $"  all {options.SeedCount} seeds reproduced exactly."
        : $"  {mismatches} seed(s) failed to reproduce.");
}

return BatchOutcome.ExitCode(
    [new ArmOutcome("default", results.Sum(r => r.Violations), mismatches, options.Verify)]);

static void ReportArms(
    BatchOptions options,
    (string Name, string Detail)[] arms,
    RunResult[][] results,
    IReadOnlyList<ArmOutcome> outcomes)
{
    Console.WriteLine("THREE-ARM EXPERIMENT - identical seeds and settings");
    foreach ((string name, string detail) in arms)
    {
        Console.WriteLine($"  {name}: {detail}");
    }

    Console.WriteLine();
    Console.WriteLine($"metric                              {arms[0].Name,10}{arms[1].Name,11}{arms[2].Name,11}");
    Console.WriteLine(new string('-', 65));

    Row("largest share, final mean %", r => r.Average(x => (double)x.LargestSharePercent));
    Row("largest share, peak mean %", r => r.Average(x => (double)x.PeakSharePercent));
    Row("largest share, peak median %", r => Median([.. r.Select(x => (double)x.PeakSharePercent)]));
    Row("largest share, peak max %", r => r.Max(x => (double)x.PeakSharePercent));
    Row("runs peaking >= 20%", r => r.Count(x => x.PeakSharePercent >= 20));
    Row("runs peaking >= 25%", r => r.Count(x => x.PeakSharePercent >= 25));
    Row("runs peaking >= 30%", r => r.Count(x => x.PeakSharePercent >= 30));
    Row("runs peaking >= 40%", r => r.Count(x => x.PeakSharePercent >= 40));
    Row("mean years above 20% share", r => r.Average(x => (double)x.YearsAbove20));
    Row("mean years above 25% share", r => r.Average(x => (double)x.YearsAbove25));
    Row("average polity size", r => r.Average(x => x.MeanPolitySize));
    Row("final polity count", r => r.Average(x => (double)x.AlivePolities));
    Row("states created (total)", r => r.Sum(x => x.Born));
    Row("states extinguished (total)", r => r.Sum(x => x.Extinct));
    Row("secessions (total)", r => r.Sum(x => x.Secessions));
    Row("reconquests (total)", r => r.Sum(x => x.Reconquests));
    Row("expansions (total)", r => r.Sum(x => (double)x.Expansions));
    Row("effect conflicts (total)", r => r.Sum(x => x.Conflicts));
    Row("invariant violations (total)", r => r.Sum(x => x.Violations));
    Row("major loss after succession %", r => Rulers(r).AfterSuccession.Rate);
    Row("major loss, ordinary years %", r => Rulers(r).Ordinary.Rate);
    Row("major loss after -30 ability %", r => Rulers(r).AfterLargeAbilityDrop.Rate);
    Row("  (windows behind that figure)", r => Rulers(r).AfterLargeAbilityDrop.Windows);
    Row("mechanism: polity-years changed %", r => Rulers(r).Mechanism.ChangedRate);

    Console.WriteLine();
    Console.WriteLine("peak largest share, distribution of per-run peaks");
    Console.WriteLine($"  bucket        {arms[0].Name,8}{arms[1].Name,9}{arms[2].Name,9}");
    int[] edges = [0, 18, 20, 22, 25, 30, 40, 101];
    for (int e = 0; e < edges.Length - 1; e++)
    {
        int low = edges[e];
        int high = edges[e + 1];
        Console.Write($"  {low,3}-{high - 1,3}%      ");
        for (int arm = 0; arm < arms.Length; arm++)
        {
            Console.Write($"{results[arm].Count(x => x.PeakSharePercent >= low && x.PeakSharePercent < high),8}");
            Console.Write(' ');
        }

        Console.WriteLine();
    }

    Console.WriteLine();
    Console.WriteLine("territory change per reign, by ability band (regions)");
    Console.WriteLine($"  band          {arms[0].Name,10}{arms[1].Name,11}{arms[2].Name,11}");
    for (int band = 0; band < RulerAnalysis.Bands; band++)
    {
        Console.Write($"  {RulerAnalysis.BandNames[band],-12}");
        for (int arm = 0; arm < arms.Length; arm++)
        {
            Console.Write($"{Rulers(results[arm]).BandReignDelta[band].Mean,11:+0.000;-0.000; 0.000}");
        }

        Console.WriteLine();
    }

    Console.WriteLine();
    Console.WriteLine("territory change per reign, by ability combination (regions)");
    Console.WriteLine($"  combination           {arms[0].Name,10}{arms[1].Name,11}{arms[2].Name,11}");
    for (int quadrant = 0; quadrant < RulerAnalysis.Quadrants; quadrant++)
    {
        Console.Write($"  {RulerAnalysis.QuadrantNames[quadrant],-20}");
        for (int arm = 0; arm < arms.Length; arm++)
        {
            Console.Write($"{Rulers(results[arm]).QuadrantReignDelta[quadrant].Mean,11:+0.000;-0.000; 0.000}");
        }

        Console.WriteLine();
    }

    Console.Write($"  {"both >= 70",-20}");
    for (int arm = 0; arm < arms.Length; arm++)
    {
        Console.Write($"{Rulers(results[arm]).BothHighReignDelta.Mean,11:+0.000;-0.000; 0.000}");
    }

    Console.WriteLine();
    Console.Write($"  {"both <= 30",-20}");
    for (int arm = 0; arm < arms.Length; arm++)
    {
        Console.Write($"{Rulers(results[arm]).BothLowReignDelta.Mean,11:+0.000;-0.000; 0.000}");
    }

    Console.WriteLine();
    Console.WriteLine();
    Console.WriteLine("expansions per reign, by the acting ruler's MILITARY ability");
    Console.WriteLine($"  band          {arms[0].Name,10}{arms[1].Name,11}{arms[2].Name,11}");
    for (int band = 0; band < RulerAnalysis.Bands; band++)
    {
        Console.Write($"  {RulerAnalysis.BandNames[band],-12}");
        for (int arm = 0; arm < arms.Length; arm++)
        {
            RulerAnalysis stats = Rulers(results[arm]);
            double perReign = stats.MilitaryHistogram[band] == 0
                ? 0
                : (double)stats.ExpansionsByMilitaryBand[band] / stats.MilitaryHistogram[band];
            Console.Write($"{perReign,11:0.000}");
        }

        Console.WriteLine();
    }

    Console.WriteLine();
    Console.WriteLine("expansions by the acting ruler's ability, share of all attributed expansions");
    Console.WriteLine($"  band          {arms[0].Name,10}{arms[1].Name,11}{arms[2].Name,11}");
    for (int band = 0; band < RulerAnalysis.Bands; band++)
    {
        Console.Write($"  {RulerAnalysis.BandNames[band],-12}");
        for (int arm = 0; arm < arms.Length; arm++)
        {
            RulerAnalysis stats = Rulers(results[arm]);
            double share = stats.ExpansionsAttributed == 0
                ? 0
                : 100.0 * stats.ExpansionsByBand[band] / stats.ExpansionsAttributed;
            Console.Write($"{share,10:0.0}%");
        }

        Console.WriteLine();
    }

    Console.Write($"  {"by 20+ region polities",-12}");
    for (int arm = 0; arm < arms.Length; arm++)
    {
        RulerAnalysis stats = Rulers(results[arm]);
        double share = stats.ExpansionsAttributed == 0
            ? 0
            : 100.0 * stats.ExpansionsByLargePolity / stats.ExpansionsAttributed;
        Console.Write($"{share,10:0.0}%");
    }

    Console.WriteLine();
    Console.WriteLine();
    Console.WriteLine("EMPIRE EPISODES - continuous stretches by one polity above a world-share threshold");
    foreach (int threshold in (int[])[25, 20])
    {
        Console.WriteLine(
            $"  at {threshold}% share      {arms[0].Name,10}{arms[1].Name,11}{arms[2].Name,11}"
            + (threshold == 20 ? "   (diagnostic threshold)" : string.Empty));

        EpisodeRow("episodes", threshold, e => e.Count);
        EpisodeRow("mean duration (years)", threshold, e => e.MeanDuration);
        EpisodeRow("max duration (years)", threshold, e => e.MaxDuration);
        EpisodeRow("mean peak share %", threshold, e => e.MeanPeakShare);
        EpisodeRow("mean admin at peak", threshold, e => e.MeanPeakAdmin);
        EpisodeRow("mean military at peak", threshold, e => e.MeanPeakMilitary);
        EpisodeRow("peaked with both >= 70", threshold, e => e.PeakedWithBothAbilitiesHigh);
        EpisodeRow("ended under weaker admin", threshold, e => e.EndedUnderWeakerAdministrator);
        EpisodeRow("ended by extinction", threshold, e => e.EndedByExtinction);
        Console.WriteLine();
    }

    Console.WriteLine("DISTANCE-SCALING COUNTERFACTUAL (same capacity, neutral distance multiplier)");
    Console.WriteLine($"  metric                     {arms[0].Name,10}{arms[1].Name,11}{arms[2].Name,11}");
    DistanceRow("polity-years changed %", d => d.ChangedRate);
    DistanceRow("region-years exposed (weak)", d => d.RegionsExposed);
    DistanceRow("region-years retained (strong)", d => d.RegionsRetained);

    Console.WriteLine();
    Console.WriteLine("  changed polity-years %, by polity size");
    for (int band = 0; band < DistanceMechanism.SizeBands; band++)
    {
        int captured = band;
        DistanceRow($"  {DistanceMechanism.SizeBandNames[band]} regions", d => d.ChangedRateInSizeBand(captured));
    }

    Console.WriteLine();
    Console.WriteLine("  changed region-years, by distance from capital");
    for (int band = 0; band < DistanceMechanism.DistanceBands; band++)
    {
        int captured = band;
        DistanceRow($"  {DistanceMechanism.DistanceBandNames[band]} steps", d => d.RegionsByDistanceBand[captured]);
    }

    Console.WriteLine();
    Console.WriteLine("  retained region-years, by the ruling administrator");
    for (int band = 0; band < DistanceMechanism.AdminBands; band++)
    {
        int captured = band;
        DistanceRow($"  admin {RulerAnalysis.BandNames[band]}", d => d.RegionsByAdminBand[captured]);
    }

    Console.WriteLine();
    Console.WriteLine("  changed polity-years %, by the ruling administrator");
    for (int band = 0; band < DistanceMechanism.AdminBands; band++)
    {
        int captured = band;
        DistanceRow($"  admin {RulerAnalysis.BandNames[band]}", d => d.ChangedRateInAdminBand(captured));
    }

    Console.WriteLine();
    Console.WriteLine("  realized mean distance multiplier %  (strain-weighted, measured in phase)");
    DistanceRow("  multiplier", d => d.RealizedMultiplierPercent);

    Console.WriteLine();
    Console.WriteLine("  what the benefit is worth against all strain, not just the distance term");
    DistanceRow("  connected distance, share of strain", d => d.DistanceSharePercent);
    DistanceRow("  size term, share of strain", d => d.SizeSharePercent);
    DistanceRow("  strain removed, share of all strain", d => d.StrainRemovedPercent);

    Console.WriteLine();
    Console.WriteLine(
        $"TERRITORY LOST OVER {RulerAnalysis.WindowYears} YEARS - core against remote periphery");
    Console.WriteLine(
        $"  (remote = {TerritoryShape.RemoteDistance}+ steps from the capital, the only regions "
        + "administrative reach can touch)");
    Console.WriteLine($"  window                     {arms[0].Name,10}{arms[1].Name,11}{arms[2].Name,11}");
    RemoteRow("after a succession, core", r => r.AfterSuccession.MeanCore);
    RemoteRow("after a succession, remote", r => r.AfterSuccession.MeanRemote);
    RemoteRow("  remote share of loss %", r => r.AfterSuccession.RemoteSharePercent);
    RemoteRow("ordinary years, core", r => r.Ordinary.MeanCore);
    RemoteRow("ordinary years, remote", r => r.Ordinary.MeanRemote);
    RemoteRow("  remote share of loss %", r => r.Ordinary.RemoteSharePercent);
    RemoteRow("after a strong predecessor, core", r => r.AfterStrong.MeanCore);
    RemoteRow("after a strong predecessor, remote", r => r.AfterStrong.MeanRemote);
    RemoteRow("  remote share of loss %", r => r.AfterStrong.RemoteSharePercent);
    RemoteRow("  windows behind that figure", r => r.AfterStrong.Windows);

    Console.WriteLine();
    Console.WriteLine("EMPIRE HISTORIES - every episode above 20%, longest first");
    for (int arm = 0; arm < arms.Length; arm++)
    {
        RulerAnalysis stats = Rulers(results[arm]);
        List<EpisodeRecord> episodes = [.. stats.EpisodeLog
            .OrderByDescending(e => e.Duration)
            .ThenBy(e => e.Seed)
            .ThenBy(e => e.StartYear)
            .Take(12)];

        Console.WriteLine();
        Console.WriteLine($"  arm {arms[arm].Name} - {stats.EpisodeLog.Count} episode(s)");

        if (episodes.Count == 0)
        {
            Console.WriteLine("    none");
            continue;
        }

        Console.WriteLine(
            "    seed  polity              start   peak  share  years  adm@start  adm@peak  "
            + "mil@peak  succession  loss/25y");

        foreach (EpisodeRecord e in episodes)
        {
            string succession = e.AdminChangeAtSuccession is { } change
                ? $"{change,+10:+0;-0;0}"
                : "         -";

            Console.WriteLine(
                $"    {e.Seed,4}  {Truncate(e.Polity, 18),-18}  {e.StartYear,5}  {e.PeakYear,5}  "
                + $"{e.PeakShare,4}%  {e.Duration,5}  {e.StartAdmin,9}  {e.PeakAdmin,8}  "
                + $"{e.PeakMilitary,8}  {succession}  {e.LossAfterEnd,8}");
        }

        int begunStrong = stats.EpisodeLog.Count(e => e.BeganUnderStrongAdministrator);
        int endedAfterSuccession = stats.EpisodeLog.Count(e => e.AdminChangeAtSuccession is not null);
        int endedAfterDrop = stats.EpisodeLog.Count(
            e => e.AdminChangeAtSuccession is { } c && c <= -RulerAnalysis.LargeAbilityDrop);

        Console.WriteLine(
            $"    began under an administrator >= {RulerAnalysis.Exceptional}: {begunStrong}"
            + $" | ended within {RulerAnalysis.WindowYears}y of a succession: {endedAfterSuccession}"
            + $" | of those, a drop of {RulerAnalysis.LargeAbilityDrop}+: {endedAfterDrop}");
    }

    Console.WriteLine();
    Console.WriteLine("DETERMINISM");
    foreach (ArmOutcome outcome in outcomes)
    {
        Console.WriteLine($"  {outcome.Describe()}");
    }

    Console.WriteLine(
        outcomes.All(o => o.ReproducedExactly)
            ? "  every arm reproduced exactly."
            : "  NOT all arms verified - see above.");

    void EpisodeRow(string label, int threshold, Func<EpisodeStats, double> metric)
    {
        Console.Write($"  {label,-28}");
        for (int arm = 0; arm < arms.Length; arm++)
        {
            RulerAnalysis stats = Rulers(results[arm]);
            Console.Write($"{metric(threshold == 25 ? stats.At25 : stats.At20),11:0.00}");
        }

        Console.WriteLine();
    }

    static string Truncate(string value, int width) =>
        value.Length <= width ? value : value[..width];

    void RemoteRow(
        string label,
        Func<(RemoteLoss AfterSuccession, RemoteLoss Ordinary, RemoteLoss AfterStrong), double> metric)
    {
        Console.Write($"  {label,-34}");
        for (int arm = 0; arm < arms.Length; arm++)
        {
            RulerAnalysis stats = Rulers(results[arm]);
            Console.Write($"{metric((stats.RemoteAfterSuccession, stats.RemoteOrdinary, stats.RemoteAfterStrongAdministrator)),11:0.000}");
        }

        Console.WriteLine();
    }

    void DistanceRow(string label, Func<DistanceMechanism, double> metric)
    {
        Console.Write($"  {label,-30}");
        for (int arm = 0; arm < arms.Length; arm++)
        {
            Console.Write($"{metric(Rulers(results[arm]).Distance),9:0.00}");
            Console.Write("  ");
        }

        Console.WriteLine();
    }

    void Row(string label, Func<RunResult[], double> metric)
    {
        Console.Write($"  {label,-34}");
        for (int arm = 0; arm < arms.Length; arm++)
        {
            Console.Write($"{metric(results[arm]),11:0.00}");
        }

        Console.WriteLine();
    }

    static RulerAnalysis Rulers(RunResult[] runs) =>
        runs.Aggregate(RulerAnalysis.Empty, (acc, r) => acc + r.Rulers);

    static double Median(double[] values)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        Array.Sort(values);
        int mid = values.Length / 2;
        return values.Length % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }
}

static RunResult Run(BatchOptions options, ulong seed) =>
    RunWith(options, seed, options.Cohesion, options.Rules);

// Local functions in a top-level program cannot be overloaded, hence the second name.
static RunResult RunWith(
    BatchOptions options, ulong seed, CohesionRules cohesion, ExpansionRules expansion)
{
    var config = SimulationConfig.Default with
    {
        Seed = seed,
        WorldWidth = options.Width,
        WorldHeight = options.Height,
        InitialPolityCount = options.Polities,
        InvariantMode = options.InvariantMode,
        InvariantInterval = options.InvariantInterval,
        ThrowOnInvariantViolation = false,
    };

    var stopwatch = Stopwatch.StartNew();

    // The observer joins the pipeline in the Polity phase, where it sees exactly the state cohesion
    // sees. It emits nothing and draws no randomness, so it cannot alter the run.
    var mechanism = new MechanismSink();
    var systems = new List<ISimulationSystem>(
        DefaultSystems.Build(expansion, cohesion, options.Rulers))
    {
        new MechanismObserverSystem(cohesion, mechanism),
    };

    Simulation sim = Simulation.Create(config, systems);
    SampledRun sampled = RunSampler.Sample(sim, options.Years);
    stopwatch.Stop();

    int expansions = sim.Chronicle.Events
        .OfType<RegionControlChangedEvent>()
        .Count(e => e.Reason == OpportunisticExpansionSystem.ExpansionReason);

    int secessions = sim.Chronicle.Events
        .OfType<PolityFoundedEvent>()
        .Count(e => e.Reason == CohesionSecessionSystem.SecessionReason);

    // A parent taking territory back from a state that broke away from it. The clearest evidence
    // that fragmentation and conquest are coupled rather than running past each other.
    int reconquests = sim.Chronicle.Events
        .OfType<RegionControlChangedEvent>()
        .Where(e => e.Reason == OpportunisticExpansionSystem.ExpansionReason)
        .Count(e => e.From.IsSome
            && sim.World.Polities.TryGet(e.From, out Polity? loser)
            && loser.Parent.Equals(e.To));

    int alive = sampled.PolitiesByYear[^1];
    int born = sim.Chronicle.Events.OfType<PolityFoundedEvent>().Count(e => e.Parent.IsSome);
    int died = sim.Chronicle.Events.OfType<PolityDissolvedEvent>().Count();

    return new RunResult(
        seed,
        sim.Year,
        alive,
        died,
        born,
        secessions,
        reconquests,
        expansions,
        sampled.LargestShareByYear[^1],
        sampled.PeakSharePercent,
        sampled.PolitiesByYear.Average(),
        sampled.PolitiesByYear.Min(),
        sampled.PolitiesByYear.Max(),
        alive > 0 ? (double)sim.World.Regions.Count / alive : 0,
        WorldQueries.WorldPopulation(sim.World),
        sim.Chronicle.Count,
        sim.Conflicts.Count,
        sim.Violations.Count,
        sim.StateHash(),
        stopwatch.ElapsedMilliseconds,
        sampled.PolitiesByYear,
        sampled.LargestShareByYear,
        sampled.LargestShareByYear.Count(v => v >= 20),
        sampled.LargestShareByYear.Count(v => v >= 25),
        RulerAnalysis.Compute(sampled, mechanism));
}

internal sealed record RunResult(
    ulong Seed,
    int Year,
    int AlivePolities,
    int Extinct,
    int Born,
    int Secessions,
    int Reconquests,
    int Expansions,
    int LargestSharePercent,
    int PeakSharePercent,
    double MeanPolities,
    int MinPolities,
    int MaxPolities,
    double MeanPolitySize,
    long Population,
    int Events,
    int Conflicts,
    int Violations,
    ulong StateHash,
    long Milliseconds,
    int[] PolitiesByYear,
    int[] LargestShareByYear,
    int YearsAbove20,
    int YearsAbove25,
    RulerAnalysis Rulers);

internal sealed record BatchOptions(
    ulong FirstSeed,
    int SeedCount,
    int Years,
    int Width,
    int Height,
    int Polities,
    InvariantMode InvariantMode,
    int InvariantInterval,
    bool Verify,
    bool Parallel,
    bool PairedAb,
    ExpansionRules Rules,
    CohesionRules Cohesion,
    RulerRules Rulers)
{
    public static BatchOptions Parse(string[] args)
    {
        ulong firstSeed = 1;
        int seeds = 8;
        int years = 500;
        int width = 6;
        int height = 4;
        int polities = 4;
        var mode = InvariantMode.Periodic;
        int interval = 25;
        bool verify = false;
        bool parallel = false;
        bool pairedAb = false;

        // Rule knobs on the command line. The batch runner is the tuning tool, so sweeping a
        // constant must not require a rebuild.
        ExpansionRules rules = ExpansionRules.Default;
        CohesionRules cohesion = CohesionRules.Default;
        RulerRules rulers = RulerRules.Default;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--verify":
                    verify = true;
                    break;
                case "--parallel":
                    parallel = true;
                    break;
                case "--ab":
                    pairedAb = true;
                    break;
                case "--strict":
                    mode = InvariantMode.EveryTick;
                    break;
                case "--first-seed" when i + 1 < args.Length:
                    firstSeed = ulong.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--seeds" when i + 1 < args.Length:
                    seeds = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--years" when i + 1 < args.Length:
                    years = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--width" when i + 1 < args.Length:
                    width = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--height" when i + 1 < args.Length:
                    height = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--polities" when i + 1 < args.Length:
                    polities = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--invariant-interval" when i + 1 < args.Length:
                    interval = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--min-pressure" when i + 1 < args.Length:
                    rules = rules with { MinPressure = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--max-permille" when i + 1 < args.Length:
                    rules = rules with { MaxAttemptPermille = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--reach" when i + 1 < args.Length:
                    rules = rules with { ReachPenaltyPerStep = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--overextension" when i + 1 < args.Length:
                    rules = rules with { OverextensionPerRegion = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--defence" when i + 1 < args.Length:
                    rules = rules with { DefenceMultiplier = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--mobilisation" when i + 1 < args.Length:
                    rules = rules with { MobilisationDivisor = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--constant-distance" when i + 1 < args.Length:
                    cohesion = cohesion with { ExperimentalConstantDistancePercent = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--distance-strongest" when i + 1 < args.Length:
                    cohesion = cohesion with { DistanceStrainAtStrongestPercent = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--tempo-weakest" when i + 1 < args.Length:
                    rules = rules with { MilitaryTempoAtWeakestPercent = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--tempo-strongest" when i + 1 < args.Length:
                    rules = rules with { MilitaryTempoAtStrongestPercent = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--overext-weakest" when i + 1 < args.Length:
                    rules = rules with { OverextensionAtWeakestPercent = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--overext-strongest" when i + 1 < args.Length:
                    rules = rules with { OverextensionAtStrongestPercent = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--pressure-scale" when i + 1 < args.Length:
                    rules = rules with { PressurePerPermille = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--shock" when i + 1 < args.Length:
                    rules = rules with { DefenderShock = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--ruler-floor" when i + 1 < args.Length:
                    cohesion = cohesion with { RulerCapacityFloorPercent = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--ruler-ceiling" when i + 1 < args.Length:
                    cohesion = cohesion with { RulerCapacityCeilingPercent = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--mortality-base" when i + 1 < args.Length:
                    rulers = rulers with { MortalityBasePermille = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--mortality-rise" when i + 1 < args.Length:
                    rulers = rulers with { MortalityRisePermillePerYear = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--ability-draws" when i + 1 < args.Length:
                    rulers = rulers with { AbilityDraws = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--capacity" when i + 1 < args.Length:
                    cohesion = cohesion with { AdministrativeCapacity = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--distance-strain" when i + 1 < args.Length:
                    cohesion = cohesion with { DistanceStrainPerStep = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--disconnection" when i + 1 < args.Length:
                    cohesion = cohesion with { DisconnectionStrain = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--size-strain" when i + 1 < args.Length:
                    cohesion = cohesion with { SizeStrainPerRegion = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--secession-permille" when i + 1 < args.Length:
                    cohesion = cohesion with { MaxAttemptPermille = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--min-breakaway" when i + 1 < args.Length:
                    cohesion = cohesion with { MinBreakawaySize = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--strain" when i + 1 < args.Length:
                    rules = rules with { ConquestStrain = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--recovery" when i + 1 < args.Length:
                    rules = rules with { ConsolidationRecovery = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                default:
                    break;
            }
        }

        // Fails here rather than after twenty seeds have produced numbers nobody can interpret. The
        // hole this closes is real: a one-sided band written through the old weak endpoint scored
        // better than every other candidate in the project by quietly giving the *average* ruler a
        // discount, and nothing in the run said so.
        cohesion.Validate();

        return new BatchOptions(
            firstSeed, seeds, years, width, height, polities, mode, interval, verify, parallel,
            pairedAb, rules, cohesion, rulers);
    }

    public string Describe() =>
        $"civ-batch: seeds {FirstSeed}..{FirstSeed + (ulong)SeedCount - 1}, {Years} years, "
        + $"world {Width}x{Height}, {Polities} starting polities, invariants {InvariantMode}"
        + (InvariantMode == InvariantMode.Periodic ? $"/{InvariantInterval}y" : string.Empty)
        + (Parallel ? ", parallel" : string.Empty)
        + (Verify ? ", verifying determinism" : string.Empty)
        + (PairedAb ? ", paired A/B" : string.Empty)
        + Environment.NewLine
        + $"  rules: minPressure {Rules.MinPressure}, maxPermille {Rules.MaxAttemptPermille}, "
        + $"reach {Rules.ReachPenaltyPerStep}, overextension {Rules.OverextensionPerRegion}, "
        + $"defence {Rules.DefenceMultiplier}, mobilisation 1/{Rules.MobilisationDivisor}, "
        + $"strain {Rules.ConquestStrain}, recovery {Rules.ConsolidationRecovery}, "
        + $"overextension band {Rules.OverextensionAtWeakestPercent}-{Rules.OverextensionAtStrongestPercent}%"
        + Environment.NewLine
        + $"  cohesion: capacity {Cohesion.AdministrativeCapacity}, "
        + $"distance {Cohesion.DistanceStrainPerStep}, disconnection {Cohesion.DisconnectionStrain}, "
        + $"size {Cohesion.SizeStrainPerRegion}, maxPermille {Cohesion.MaxAttemptPermille}, "
        + $"minBreakaway {Cohesion.MinBreakawaySize}, "
        + $"reach benefit {Cohesion.DistanceStrainAtStrongestPercent}% at ability 100"
        + (Cohesion.ExperimentalConstantDistancePercent == CohesionRules.NeutralPercent
            ? string.Empty
            : $", EXPERIMENTAL flat distance {Cohesion.ExperimentalConstantDistancePercent}% (control)")
        + Environment.NewLine
        + $"  rulers: capacity {Cohesion.RulerCapacityFloorPercent}-{Cohesion.RulerCapacityCeilingPercent}% "
        + $"of baseline, mortality {Rulers.MortalityBasePermille}permille +{Rulers.MortalityRisePermillePerYear}/yr "
        + $"from age {Rulers.MortalityOnsetAge}, ability = mean of {Rulers.AbilityDraws} draws";
}
