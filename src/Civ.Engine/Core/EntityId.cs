namespace Civ.Engine.Core;

/// <summary>
/// A stable, typed handle to an entity.
/// </summary>
/// <remarks>
/// Nothing in the simulation holds an object reference to another entity; it holds
/// one of these. That is what allows polities to be created, dissolved, split and
/// merged without leaving dangling references scattered through unrelated systems.
///
/// <para><b>Generation</b> is the safety mechanism. Slots are reusable, so index
/// alone is ambiguous across time. Generation starts at 1 and is bumped whenever a
/// slot is freed, which makes a handle to a removed entity detectable rather than
/// silently pointing at whatever now occupies the slot.</para>
///
/// <para><see cref="None"/> is <c>default</c>: generation 0 never refers to a live
/// entity. This is deliberately used instead of <c>EntityId?</c> so state stays flat
/// and trivially serializable.</para>
/// </remarks>
public readonly record struct EntityId<TKind> : IComparable<EntityId<TKind>>
    where TKind : IEntityKind
{
    public int Index { get; }
    public int Generation { get; }

    public EntityId(int index, int generation)
    {
        Index = index;
        Generation = generation;
    }

    /// <summary>The absent handle. Equivalent to <c>default</c>.</summary>
    public static EntityId<TKind> None => default;

    /// <summary>False for <see cref="None"/>. Says nothing about whether the entity still exists.</summary>
    public bool IsSome => Generation != 0;

    public bool IsNone => Generation == 0;

    public int CompareTo(EntityId<TKind> other)
    {
        int byIndex = Index.CompareTo(other.Index);
        return byIndex != 0 ? byIndex : Generation.CompareTo(other.Generation);
    }

    public override string ToString() =>
        IsNone ? $"{TKind.KindName}#none" : $"{TKind.KindName}#{Index}v{Generation}";
}
