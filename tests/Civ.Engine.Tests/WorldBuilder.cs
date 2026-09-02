using Civ.Engine.State;

namespace Civ.Engine.Tests;

/// <summary>
/// Builds a small, exactly-specified world for tests.
/// </summary>
/// <remarks>
/// Generated worlds are the wrong tool for testing a rule: you cannot state "an overwhelming
/// neighbour faces a weak one" without knowing the populations. This constructs the scenario
/// directly. It only compiles because the test assembly has internals access - the same access that
/// lets the invariant tests corrupt state, and that <c>Civ.Systems</c> deliberately does not have.
///
/// <para>Worlds built here are handed to <see cref="Simulation.Resume"/>, which checks invariants
/// immediately, so a malformed scenario fails at construction rather than midway through a test.</para>
/// </remarks>
internal sealed class WorldBuilder(ulong seed = 1, int year = 0)
{
    private readonly WorldState _world = new(seed, year);

    public WorldState World => _world;

    public RegionId Region(string name, long population, int fertility = 50) =>
        _world.AddRegion(name, Terrain.Plains, fertility, population);

    /// <summary>Creates regions in a line, each adjacent to the next.</summary>
    public RegionId[] Line(params long[] populations)
    {
        var ids = new RegionId[populations.Length];
        for (int i = 0; i < populations.Length; i++)
        {
            ids[i] = Region($"R{i}", populations[i]);
            if (i > 0)
            {
                _world.LinkRegions(ids[i - 1], ids[i]);
            }
        }

        return ids;
    }

    public void Link(RegionId a, RegionId b) => _world.LinkRegions(a, b);

    /// <summary>
    /// Creates a polity seated at the first region and controlling all of them, under a ruler of
    /// exactly the given administrative ability.
    /// </summary>
    /// <remarks>
    /// Ability is explicit rather than generated because the fixtures that matter most - strong
    /// administrator versus weak one, same world, same seed - are only meaningful if ruler quality
    /// is the single thing that differs.
    /// </remarks>
    public PolityId Polity(
        string name, int stability, int administration, int military, params RegionId[] regions)
    {
        PolityId id = _world.AddPolity(name, regions[0], _world.Year, PolityId.None);
        _world.Polities.Get(id).Stability = stability;

        foreach (RegionId region in regions)
        {
            _world.Regions.Get(region).Controller = id;
        }

        var profile = new RulerProfile($"{name} I", _world.Year - 30, administration, military);
        RulerId ruler = _world.AddRuler(profile, _world.Year, id);
        _world.Polities.Get(id).CurrentRuler = ruler;

        return id;
    }

    /// <summary>Creates a polity whose ruler has the given administration and average military ability.</summary>
    public PolityId Polity(string name, int stability, int administration, params RegionId[] regions) =>
        Polity(name, stability, administration, 50, regions);

    /// <summary>Creates a polity under an average (ability 50) ruler.</summary>
    public PolityId Polity(string name, int stability, params RegionId[] regions) =>
        Polity(name, stability, 50, 50, regions);

    public RulerId RulerOf(PolityId polity) => _world.Polities.Get(polity).CurrentRuler;
}
