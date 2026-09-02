# civilization

English | [Español](README.es.md)

A headless, deterministic history simulation with a terminal front end.

This is an **architectural skeleton plus three deliberately crude systems**. Population grows,
rulers age and are succeeded, polities take adjacent land when they are locally much stronger,
territory they can no longer govern breaks away as new states, and states that lose everything
are retired. There is no economy, army, technology, religion, culture, dynasty or faction model
yet. The purpose of this stage is to make those things cheap and safe to add later.

## Layout

| Project | Role |
| --- | --- |
| `src/Civ.Engine` | State, ids, RNG, effects, events, pipeline, invariants, worldgen, persistence. No I/O. |
| `src/Civ.Systems` | Simulation content: population, succession, cohesion/secession, expansion, polity lifecycle. References the engine; **cannot mutate state**. |
| `src/Civ.Terminal` | Interactive observer. |
| `src/Civ.Batch` | Headless multi-seed runner. |
| `tests/Civ.Engine.Tests` | Determinism, invariants, handle safety, effect semantics, polity lifecycle, persistence. |

## Running

```bash
dotnet test
```

```bash
dotnet run --project src/Civ.Batch -c Release -- --seeds 24 --years 2000 --width 16 --height 12 --polities 14 --parallel --verify
```

The batch runner takes both rule sets' constants as flags — expansion (`--min-pressure`,
`--reach`, `--mobilisation`, `--overextension`, `--defence`, `--strain`, `--shock`,
`--pressure-scale`, `--max-permille`) and cohesion (`--capacity`, `--distance-strain`,
`--disconnection`, `--size-strain`, `--secession-permille`, `--min-breakaway`) — so a tuning
sweep never needs a rebuild.

```bash
dotnet run --project src/Civ.Terminal -c Release -- --seed 1815 --width 8 --height 5 --polities 5
```

## The rules this skeleton exists to enforce

**Systems cannot write to state.** Every mutator on `WorldState`, `Region`, `Polity` and
`EntityTable` is `internal` to `Civ.Engine`. Systems live in a separate assembly, so this is a
compile error rather than a code-review convention. A system reads a snapshot and emits effects;
`EffectApplier` is the only code in the repository that mutates anything.

**Effects are applied at phase barriers.** A year runs
`Environment → Population → Economy → Culture → Rulership → Polity → Diplomacy → Bookkeeping`. Every system in a
phase sees the same start-of-phase state and their effects land together at the end of it. Systems
in one phase therefore cannot see each other's work, which makes them reorderable now and
parallelisable later; ordering between groups is declared, not accidental.

**Prefer commutative deltas.** `AdjustRegionPopulation(delta)` composes across systems with no
conflict. Absolute writes (`SetRegionController`) are resolved by pipeline order and the collision
is recorded as an `EffectConflict` rather than silently discarded.

**Events come only from the applier.** A `SimEvent` is emitted when state actually changed, so the
chronicle is a report of the simulation and not a story told alongside it. `RecordEvent` is the one
sanctioned escape hatch for observations with no state delta. Events snapshot the names they need,
because an entry from year 400 has to still render correctly in year 1800.

**Salience is in the model, not in the UI.** Every event is ranked. Population moves in every region
every year; only milestone crossings are reported. Without this the feed becomes a wall in which
nothing is distinguishable from anything else.

**Random streams are derived from system names.** `hash(seed, streamId, year, entityIndex)`, built
per call and never carried forward. Adding, removing or reordering a system cannot shift any other
system's rolls — the property that keeps balance work from being invalidated by every new feature.
`DeterminismTests.AddingSystemsDoesNotChangeExistingSystemsOutcomes` asserts it.

**A run is `(engine version, config, seed)`.** No wall-clock, no `Guid.NewGuid`, no framework string
hashing, no dictionary-order iteration, no floating point in state. `WorldHasher` produces an exact
canonical hash; the batch runner re-runs every seed and compares.

**Polities and rulers are never deleted.** Dissolved states and dead rulers are marked and kept
as historical records.
`EntityTable` still supports removal with generation bumping, because characters and armies will
need it and stale-handle detection is far cheaper to prove now than to retrofit.

**Invariants run every tick in tests.** Structural corruption in the polity lifecycle is cumulative
and quiet; one orphaned region in year 200 is invisible until something much later reads it.

## Known constraints

- A system emitting `FoundPolity` does not learn the new id in that tick. The applier allocates it
  and puts it in the event; anything that needs to act on the new polity does so next tick.
- Systems must be stateless between ticks. Anything that persists belongs in `WorldState`, where it
  is hashed, saved and checked.
- Saves store world state only. History is reproducible by replay and is not persisted.
- `EntityTable.RestoreSlot` rebuilds the free list in ascending index order rather than the original
  LIFO order. Irrelevant while regions and polities are never removed; revisit before any entity
  kind that *is* removed becomes part of a save.

## The expansion system

`OpportunisticExpansionSystem` runs in the `Diplomacy` phase. Each polity scores every foreign or
unclaimed region on its border and, if its best score clears a threshold, makes at most one attempt
per year against that one target. It is **not** a war model — no armies, fronts, casualties or peace
terms — and it will be replaced. It exists to prove the default pipeline can rewrite political
borders safely across millennia.

Pressure is attack over defence, as a percentage:

- **attack** = population facing the target from adjacent own regions, plus a share of the polity's
  total population, scaled by stability, then penalised by distance from its capital and by how much
  it already holds.
- **defence** = the target's own population, and if it is held, organised defence scaled by the
  owner's stability plus what that owner can project at *its* distance from *its* capital.

Below the threshold nothing happens however many centuries pass — there is no flat "chance of
conquest per year" anywhere. Above it, the margin sets the annual probability, so randomness decides
*when*, never *whether*.

### What the sweeps actually showed

Four separate things had to be fixed before the world would move at all, and every one of them was
found by running batches rather than by reasoning about the rule:

1. **Flat conquest strain froze everything.** A fixed stability cost left every conqueror briefly
   weak enough for its victim to take the region straight back. Strain is now proportional to how
   marginal the conquest was, so absorbing a weak neighbour is nearly free.
2. **Purely local defence meant weakness never compounded.** With defence derived only from the
   target region, losing territory never made a polity easier to finish off, so nothing could die.
3. **A strong reach penalty is a powerful stabiliser.** A shrinking polity concentrates around its
   capital while a growing one stretches away from its own, so a high per-step penalty dominates
   every other term. At the original value the map was frozen solid for two thousand years.
4. **The world was the real problem.** Fertility was drawn independently per region, making every
   part of the map statistically identical and therefore every polity identical. No conquest rule
   can amplify an asymmetry that is not there. `WorldGenerator` now lays down a low-frequency
   richness field, so some states sit on heartlands and others on marginal ground.

Before those changes, a 2000-year run produced ~380 "conquests" that were three border cells
oscillating, with the political map otherwise pixel-identical from year 1 to year 2000.

### Behaviour in isolation

Before cohesion existed, 24 seeds × 2000 years gave 1752 expansions and 114 extinctions with zero
invariant violations — but the polity count only ever fell, because conquest is the only force
acting on the political graph and nothing created new states.

## The cohesion and secession system

`CohesionSecessionSystem` runs in the `Polity` phase, ahead of expansion, and is the counter-force.
Every region a polity holds exerts **strain**; the polity answers with an **authority** budget:

- **strain** = distance from the capital *measured through the polity's own territory*, or a large
  flat penalty if there is no such route at all, plus a term for every other region held, plus a
  bounded term for being richer than the polity's average province.
- **authority** = a flat administrative capacity, adjusted by stability.

Where strain exceeds authority a region is restive. Restive regions are grouped into **connected
components**, and the largest one secedes as a single contiguous successor state — not a scattering
of individual cells. The margin sets the annual probability, so as with conquest the state decides
whether a breakaway is possible and randomness only decides when.

Growth directly produces the strain that fragments it, which is what couples the two systems: the
size term rises with every conquest, and territory taken across a rival becomes an exclave that is
very hard to keep. Because cohesion runs before expansion, a breakaway state is live and vulnerable
in the same year it is created — reconquest by the former parent is common.

The capital never secedes, so no state can be replaced outright by its own successor. That guard is
a modelling choice rather than a safety net: removing it produces no invariant violation, because
`PolityLifecycleSystem` would reseat or retire the parent anyway.

### Administrative capacity is the sensitive constant

It sets the equilibrium size of a state, and the usable band is narrow. Sweeps over 2500-year runs
on a 192-region world starting from 12 polities:

| capacity | outcome |
| --- | --- |
| 62 | shattered into ~56 statelets averaging 3.3 regions; largest share 6% |
| 130 | 113 states created, 105 extinguished; count oscillates around 13 |
| **150** | **94 created, 101 extinguished; count oscillates around 11; largest share 15%** |
| 280 | 4 secessions in six runs; conquest effectively unopposed |
| 380 | zero secessions; monotonic decline |

### Current behaviour

20 seeds × 3000 years, 192 regions, 12 starting polities: **305 states created, 320 extinguished**,
491 reconquests, 2369 expansions, 10 effect conflicts, **0 invariant violations**, all 20 seeds
reproduced exactly.

```
year     0   12.0 polities  largest 15.0%
year   600   12.2 polities  largest 14.8%
year  1200   11.7 polities  largest 15.3%
year  1800   11.9 polities  largest 15.0%
year  2400   11.5 polities  largest 14.8%
year  3000   11.3 polities  largest 15.0%
```

Creation and destruction are near-balanced, and **15 of 20 runs rose above their starting polity
count** at some point — the count genuinely oscillates rather than only declining. Per seed it
moves between 8 and 18. Average polity size is 17.4 regions.

A chronicle excerpt from one run reads:

```
[  430] Dominion of Daneigard ceased to exist (no remaining territory).
[  620] Kingdom of Zoroath broke away from Dominion of Tavoukal with 7 region(s), seated at Eluth.
```

### What this does not yet do

The equilibrium is *tight*. Largest polity share sits at 15% and peaks at 20% across every seed, so
no run ever produces a dominant empire — the world is a permanent balance of middling powers.
Neither failure mode is present, which is what this stage set out to show, but "empires rise and
fall" is not yet true in any dramatic sense. That needs the strain and authority terms to vary over
time rather than being fixed constants, which is what a technology or administration model would
supply.

## The ruler layer

`RulerSuccessionSystem` runs in its own `Rulership` phase, ahead of `Polity`, so a new ruler's
capacity is in effect before the cohesion rule that reads it runs in the same year. Every active
polity has exactly one living ruler; the applier seats one whenever a polity is founded, by any
path, so the invariant cannot be broken by a caller who did not think about it.

A ruler has a stable id, a generated name, a birth year, an administrative ability (0–100, the mean
of three uniform draws, so most rulers sit near 50), an accession year, and a death year. **Age is
derived from birth year, never stored.** Dead rulers are retained forever, so an accession event
from year 90 still renders in year 3000 after both the ruler and their state are gone.

`CohesionRules.EffectiveCapacity(ability)` maps ability onto the baseline: 50 → exactly the
configured `AdministrativeCapacity`, 0 → 75% of it, 100 → 125%. The conversion lives in the rules,
not on the ruler, because it is a statement about how this simulation values administration rather
than a property of the person.

The system emits exactly two kinds of effect — a death and an accession. It never touches territory,
stability or borders. Everything downstream is the existing cohesion rule reading a different
number.

A reign ending and a ruler dying are separate facts. `DeathYear` is only ever set by an actual
death; `ReignEndYear` plus `EndReason` record the end of the reign, which can also come from the
state itself ceasing to exist. A ruler whose polity falls is archived alive and never reigns again.

### Measurement timing

Three windows decide every ruler statistic, and all three were originally wrong:

- **Territory "at accession"** is the state at the *start* of the accession year, i.e. the end of the
  year before. Reading it at year end defined a weak successor's first-year losses out of existence.
- **A reign owns years `[accession, reignEnd - 1]`.** The accession year belongs to the successor.
  Consecutive reigns therefore tile the timeline exactly: one ruler's end index is the next one's
  start index, so no year is counted twice or dropped.
- **The mechanism diagnostic is captured in-phase**, by `MechanismObserverSystem` running in the
  Polity phase alongside cohesion, so it reads the same pre-effect state. Recomputing it from the
  end-of-year map measured the consequences of the decision rather than the decision.

The index origin comes from the simulation's own year, not from configuration — a world handed to
`Simulation.Resume` can sit at any year, and taking it from config shifts every window by one.

### Measured result, 20 seeds × 3000 years

28,086 natural deaths, 322 reigns ended by the fall of the state, 0 displacements. Zero-year reigns
are **15, all polity extinctions** — a breakaway founded and annexed inside one year — and none are
deaths. Mean reign 26.7 years. **0 invariant violations**, all 20 seeds reproduced exactly.

```
mechanism (observed in-phase, from the state cohesion reads)
  all polity-years   :  95,823 / 764,105 = 12.54% differ from an average administrator
    weak rulers exposed 203,178 region-years; strong rulers held 57,060 region-years
  20+ region polities:  86,980 / 256,602 = 33.90%

territory change per reign, by ability band (regions)
  0-19   -0.22 | 20-39 -0.04 | 40-59 +0.03 | 60-79 +0.06 | 80-100 +0.09

major loss (>=25% of land at any point in the following complete 25 years)
  after a succession 2.8%   ordinary years 2.9%   (control)
  by successor band: 0-19 3.6% | 20-39 3.1% | 40-59 2.6% | 60-79 2.7% | 80-100 2.2%
```

### The paired A/B, which is what actually answers the question

Identical seeds and settings; the ruler capacity band is the only difference. **A** maps every
ability to 100% of baseline, so rulers exist but are mechanically inert. **B** is the current
75–125%.

```
                                    A          B      B - A
largest share, final mean %     14.95      13.75      -1.20
largest share, peak mean %      17.35      17.30      -0.05
average polity size             17.44      14.93      -2.51
final polity count              11.25      13.15      +1.90
states created (total)            305        345        +40
states extinguished (total)       320        322         +2
secessions (total)                305        345        +40
reconquests (total)               491        559        +68
expansions (total)              2,369      2,719       +350
invariant violations                0          0          0
mechanism: polity-years changed  0.00%     12.54%    +12.54

territory change per reign, by ability band
  0-19    -0.002   -0.224   -0.222
  20-39   +0.012   -0.039   -0.051
  40-59   +0.019   +0.031   +0.012
  60-79   +0.013   +0.057   +0.044
  80-100  -0.013   +0.087   +0.099
```

Arm A is the control that makes the rest readable. Its mechanism rate is exactly 0.00%, confirming
the diagnostic measures what it claims, and its territory-by-band column is flat noise with no
gradient — so the monotone gradient in B is the treatment effect and not an artifact of the
measurement.

**Rulers do change the equilibrium, and the direction is fragmentation.** Average polity size falls
14%, states created rise 13%, and the final count rises by nearly two. What does *not* move is the
ceiling: peak largest share is 17.35% against 17.30%, and the maximum across all seeds is 20% in
both arms. Rulers loosen the floor and leave the roof exactly where it was.

That is the same structural fact from a better vantage point: **administration only removes a brake.**
It governs what a state can keep, never what it can take, so a strong administrator holds their
inheritance and no more while a weak one loses provinces that are rarely recovered.

### Administration and expansion: measured, negative, disabled

`ExpansionRules.OverextensionTerm(held, administration)` scales the overextension term by ability.
It is **off by default (100/100)**. Across bands from 125/75 to 200/20, over 50 seeds and 3000
years, it moved expansion counts but never the peak-share distribution: no run above 25% at any
setting, and the extra expansion never concentrated in large polities. The implementation is kept
and still tested - tests opt into it explicitly - but administration affects cohesion only.

The diagnostic that explained it: tripling the *base* expansion rate moved the ceiling in every arm,
including the one with inert rulers. Overextension is not what limits empire size. A modifier on a
rate already near zero cannot produce empires.

## Military ability

Every ruler has a second ability, drawn independently: `Military`, 0-100, the mean of three uniform
draws. It governs **campaign tempo only** - how quickly a polity acts on an opportunity the existing
pressure calculation has already judged viable.

```
basePermille = clamp((pressure - MinPressure) / PressurePerPermille, 1, MaxAttemptPermille)
permille     = clamp(basePermille * CampaignTempoPercent(military) / 100, 1, MaxCampaignPermille)
```

The viability gate is entirely upstream, so no commander can make an impossible target possible, and
target selection is untouched. `CampaignTempoPercent` is two straight segments meeting at ability 50
- 50% at ability 0, **exactly 100% at 50**, 800% at 100 - because a single line across an asymmetric
band would put the neutral value at 175% and silently change every existing result.

`MaxCampaignPermille` is 1000, i.e. certainty. Its job is to keep a probability a probability, not to
impose a second ceiling: the base is already clamped by `MaxAttemptPermille`, and re-applying that
after multiplying would erase the entire bonus.

### Tempo band sweep

Cohesion, reach, mobilisation and every other expansion rule held fixed. 10 seeds x 3000 years, arm C:

| band | peak mean % | peak max % | runs >=20% | runs >=25% | avg size | years above 20% |
| --- | --- | --- | --- | --- | --- | --- |
| control (100/100) | 18.10 | 20 | 2 | 0 | 14.40 | 7.5 |
| 50 / 300 | 18.70 | 22 | 3 | 0 | 15.32 | 26.7 |
| 50 / 500 | 17.80 | 20 | 2 | 0 | 15.67 | 7.5 |
| **50 / 800** | **19.40** | **22** | **5** | 0 | **16.07** | **55.5** |
| 25 / 1200 | 18.70 | 22 | 4 | 0 | 15.06 | 46.5 |

500 was indistinguishable from the control and 1200 was no better than 800, so the response is not
monotone at ten seeds - but 800 was the strongest candidate on every measure and was adopted.

### Three-arm experiment

**A** inert rulers (capacity 100%, tempo 100%). **B** administration only (capacity band, tempo
100%). **C** administration and military (both bands). Overextension flat in all three.

50 seeds x 3000 years, zero invariant violations everywhere; determinism verified separately at 20
seeds for all three arms.

```
metric                                   A          B          C
largest share, final mean %          15.22      13.56      14.00
largest share, peak mean %           18.50      18.62      19.34
largest share, peak max %            26.00      26.00      26.00
runs peaking >= 20%                     17         17         22
runs peaking >= 25%                      1          1          1
runs peaking >= 30% / 40%                0          0          0
mean years above 20% share           20.72      23.08      46.22
average polity size                  16.69      14.72      15.61
final polity count                   11.84      13.40      12.60
states created / extinguished     938/946    877/807  1126/1096
secessions / reconquests         938/1559   877/1443  1126/2187
expansions                            7,205      7,697     11,344
effect conflicts                         30         24         62
invariant violations                      0          0          0

peak share distribution (runs per bucket)
  0-17%   18  18  10
  18-19%  15  15  18
  20-21%  12  11  14
  22-24%   4   5   7
  25-29%   1   1   1
  30%+     0   0   0
```

**The mechanism is decisively concentrated.** Expansions per reign, by the acting commander's
military band - A and B are the controls and are flat:

```
band       A       B       C
0-19    0.105   0.109   0.072
20-39   0.103   0.104   0.084
40-59   0.106   0.105   0.122
60-79   0.102   0.102   0.271
80-100  0.107   0.097   0.397
```

An 80-100 commander conquers 5.5x more often than a 0-19 one. The arms without the band show no
gradient at all, so this is the treatment and not an artefact.

**The four ruler types emerge**, territory change per reign:

```
combination              A          B          C
low adm / low mil    +0.017     -0.029     -0.106
low adm / high mil   +0.015     -0.032     +0.023
high adm / low mil   +0.015     +0.062     +0.015
high adm / high mil  +0.020     +0.051     +0.143
both >= 70           -0.009     +0.067     +0.300
both <= 30           -0.003     -0.083     -0.388
```

A conqueror without an administrator gains almost nothing (+0.023); an administrator without a
commander holds but does not grow (+0.015); the two together are an order of magnitude better
(+0.143, and +0.300 for rulers above 70 in both). Nothing in the code names these categories.

### What it still does not do

The peak-share ceiling did not move. Peak max is 26% in all three arms, exactly one run in each
reaches 25-29%, and none reaches 30%. What C changes is the *middle* of the distribution: ten fewer
runs stay below 17%, 22 of 50 now peak above 20% against 17, and time spent above 20% doubles from
23 to 46 years per run.

So arm C produces **larger and longer-lived middling powers, not empires**. The 25-35% temporary
empire the experiment was aiming for did not appear at any tempo tested. Consistent with the
administrative result, the binding constraint is on the retention side - cohesion and reach - not on
how fast a state can conquer.

## Administrative reach: two experiments, both rejected

`CohesionRules.DistanceStrainTerm(distance, administration)` scales the complete connected-distance
term by the ruler's administrative ability. It touches only connected distance: disconnection, size,
prosperity, stability relief and administrative capacity are untouched, and a province cut off from
its capital stays cut off whoever is on the throne.

**It is off by default.** Two shapes of the conversion were built and measured. Both worked exactly
as designed. Neither changed the world.

### First attempt: a symmetric band, and why it failed

125% at ability 0, 100% at 50, 75% at 100. Mechanically flawless and sharply concentrated - 54.9% of
polity-years changed for 30+ region states against 0.01% under 10 - and the world came out *more*
fragmented.

```
polity-years changed                      8.97%
region-years exposed by weak rulers     100,558
region-years retained by strong rulers   20,332
```

A symmetric band exposed five times more territory than it retained, because most remote provinces
sit *below* the restive threshold: raising strain pushes many across it, lowering strain only helps
the few already above. Widening the band made it monotonically worse (average polity size 14.40,
13.58, 12.33 at 125/75, 150/50, 175/25 against 16.07 without it).

### Second attempt: a one-sided benefit

The diagnostic asked for exactly one change - delete the half that raises strain - so the conversion
became flat through ability 50 and falling above it, with only the strong endpoint exposed:

```
administration 0-50   -> 100% connected-distance strain
administration 51-100 -> falling linearly to DistanceStrainAtStrongestPercent
```

Half the ruler population is now inert by construction, and `region-years exposed` is **zero** rather
than merely small. A test asserts it over a full multi-seed sweep.

### The counterfactual: the mechanism is perfect

Same ruler-modified capacity, neutral distance multiplier, everything else identical, read in-phase
from the state cohesion itself sees. 50 seeds x 3000 years, 192 regions, arm B at 50%:

```
polity-years changed                      2.89%
region-years retained                   107,293
region-years exposed                          0

changed polity-years %, by size    <10  0.00 | 10-19  0.26 | 20-29  5.33 | 30+ 37.69
changed region-years, by distance  0-1    12 |  2-3  5,115 |  4-5 45,179 |  6+ 56,987
retained region-years, by admin   0-39     0 | 40-59 34,583 | 60-79 64,858 | 80-100 7,852
```

Nothing below ability 40 changes, nothing under 10 regions changes, nothing within one step of a
capital changes. That is the intended target hit dead centre.

### The matched control, and why it is required

A one-sided benefit lowers average distance strain across the whole world, so "bigger empires" has
an innocent explanation: distance got cheaper. Arm C therefore runs a flat multiplier for everyone,
set to the mean of arm B's conversion over the ruler ability distribution - computed by enumerating
`(d1+d2+d3)/3` exactly, before a year is simulated, so the control cannot be tuned by the outcomes it
exists to explain.

```
expected mean multiplier (ability distribution)   93.33%
realized, strain-weighted, arm B                  92.62%
realized, strain-weighted, arm C                  92.86%
```

Within a quarter of a point. The two arms really do apply the same average discount to different
people.

### Three arms, 50 seeds x 3000 years, every arm verified

```
metric                                 A          B          C
largest share, peak mean %         19.34      19.56      19.40
largest share, peak median %       19.00      19.00      19.00
largest share, peak max %          26.00      26.00      26.00
runs peaking >= 20% / 25% / 30%   22/1/0     22/2/0     23/1/0
mean years above 20% share         46.22      57.50      46.14
average polity size                15.61      15.40      16.20
final polity count                 12.60      12.76      12.10
secessions / reconquests       1126/2187  1084/2100   953/1769
expansions                        11,344     11,305      9,511
effect conflicts                      62         50         47
invariant violations                   0          0          0

empire episodes at 20%                44         48         48
mean duration (years)              53.45      60.96      50.29
mean admin at peak                 54.34      51.71      51.60
```

Identical medians, identical maxima, 22 against 22 runs above 20%. Arm B's empires peak under
administrators averaging **51.7 against the baseline's 54.3** - lower, not higher. At 20 seeds arm B
looked like it had produced the project's first 25% empire; at 50 seeds arms A and C both reach 26%
too, and B's 25% episode turns out to have peaked under an administrator of 44, inside the flat
segment where the modifier does nothing at all.

### Why nothing moved, measured rather than argued

The observer now sums the strain the rule actually computed, by term:

```
connected distance, share of all strain    40.2%
size term, share of all strain             61.9%
strain removed by the benefit               2.97%
```

(The two shares exceed 100% because the prosperity term is negative for provinces poorer than their
realm's average.)

A 50% cut at the very top of the ability range, applied to the largest single geographic term,
removes **three percent of total strain**. The capacity band by comparison moves authority by 25%.
There was never enough of it to matter.

### The next binding constraint: `SizeStrainPerRegion`

Size is 62% of all strain. At 3 points per region a 30-region state carries 87 strain on *every*
province before geography is considered, against an authority budget of 150 - 58% of the budget spent
on being large. Unlike distance, the ceiling moves when this does. Ten seeds x 3000 years, 192
regions, nothing else changed:

| size strain per region | peak share, mean | peak, max | average polity size | final polities |
| --- | --- | --- | --- | --- |
| 3 (shipped) | 19.4% | 22% | 16.1 | 12.1 |
| 2 | 20.6% | 25% | 19.8 | 9.8 |
| 1 | 24.0% | 31% | 24.3 | 8.2 |

Three ruler-to-territory channels have now been rejected - overextension, symmetric distance,
one-sided distance - and all three were adjustments to terms worth a few percent of the strain
budget. The empire ceiling is set by the term nobody has varied.

### Configuration validation

Accepted ruler-dependent conversions must map administration 50 to exactly 100%. `CohesionRules.Validate`
enforces it, from `CohesionSecessionSystem`'s constructor and from `BatchOptions.Parse`, so an invalid
rule set fails before a year is simulated rather than after twenty seeds have produced numbers nobody
can interpret.

This closes a real hole. During the symmetric experiment the bands `100/50` and `100/25` scored better
than everything else - peak mean 20.70, seven of ten runs above 20%, and the first 25% episodes the
project had produced. They were invalid: a one-sided *linear* band moves its own neutral point, so
under `100/50` an average administrator paid 75% distance strain and every state in the world got a
discount. Nothing in the output said so.

```
band 125/ 75: ability 0 -> 125%, ability 50 -> 100%, ability 100 ->  75%   OK
band 100/ 50: ability 0 -> 100%, ability 50 ->  75%, ability 100 ->  50%   REFUSED
band 100/ 25: ability 0 -> 100%, ability 50 ->  62%, ability 100 ->  25%   REFUSED
```

The current conversion cannot express those at all - there is no weak endpoint to write them through -
and the validator catches the same mistake made through the capacity band, which is where two test
fixtures were quietly running at `40/200` and handing every average ruler a 20% capacity gift.

A deliberately constant global multiplier is still needed as a control, so it lives in a separately
named setting, `ExperimentalConstantDistancePercent`, which is exempt from the neutral-point rule,
refused in combination with the ruler conversion, and defaults to off. A test asserts it cannot be
switched on by any command line that does not name it.
