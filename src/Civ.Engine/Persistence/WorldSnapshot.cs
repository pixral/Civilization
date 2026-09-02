using Civ.Engine.Config;
using Civ.Engine.Core;

namespace Civ.Engine.Persistence;

/// <summary>Serializable form of an <see cref="EntityId{TKind}"/>. Kind is implied by position.</summary>
public readonly record struct IdRef(int Index, int Generation);

public sealed record RegionSnapshot(
    IdRef Id,
    string Name,
    int Terrain,
    int Fertility,
    long Population,
    IdRef Controller,
    IdRef[] Neighbors);

public sealed record PolitySnapshot(
    IdRef Id,
    string Name,
    IdRef Capital,
    int FoundedYear,
    int? DissolvedYear,
    int Status,
    IdRef Parent,
    int Stability,
    IdRef CurrentRuler);

public sealed record RulerSnapshot(
    IdRef Id,
    string Name,
    int BirthYear,
    int Administration,
    int Military,
    int AccessionYear,
    int? DeathYear,
    int? ReignEndYear,
    int? EndReason,
    IdRef Polity);

/// <summary>Flat, id-referenced projection of the whole world.</summary>
public sealed record WorldSnapshot(
    ulong Seed,
    int Year,
    RegionSnapshot[] Regions,
    PolitySnapshot[] Polities,
    RulerSnapshot[] Rulers);

/// <summary>
/// Identity of a saved run.
/// </summary>
/// <remarks>
/// <see cref="StateHash"/> is checked on load. It is what turns a corrupt or version-skewed save
/// into an immediate, precise failure instead of a world that quietly simulates differently from
/// the one that was saved.
/// </remarks>
public sealed record SaveHeader(
    string EngineVersion,
    ulong ConfigHash,
    ulong Seed,
    int Year,
    ulong StateHash);

/// <summary>
/// A save file.
/// </summary>
/// <remarks>
/// <para>The chronicle is not stored. History is fully reproducible from
/// <c>(engine version, config, seed)</c> by replaying to the saved year, so persisting it would be
/// storing a derived value - and a large, growing one. The snapshot is kept anyway because it makes
/// saves inspectable and gives the hash something to verify against.</para>
///
/// <para>This is intentionally the whole persistence layer. It exists at this stage to constrain the
/// state design - flat, integral, id-referenced, no back-pointers - not to be a durable format.
/// Versioning, migration and compression are problems for a world worth keeping.</para>
/// </remarks>
public sealed record SaveGame(
    SaveHeader Header,
    SimulationConfig Config,
    WorldSnapshot World);
