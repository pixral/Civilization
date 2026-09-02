namespace Civ.Engine.Core;

/// <summary>
/// Compile-time tag distinguishing one family of entities from another.
/// Exists purely so <see cref="EntityId{TKind}"/> of different kinds are
/// different types and cannot be assigned to one another.
/// </summary>
public interface IEntityKind
{
    static abstract string KindName { get; }
}

public readonly struct RegionKind : IEntityKind
{
    public static string KindName => "Region";
}

public readonly struct PolityKind : IEntityKind
{
    public static string KindName => "Polity";
}

public readonly struct RulerKind : IEntityKind
{
    public static string KindName => "Ruler";
}
