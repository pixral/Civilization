using System.Diagnostics.CodeAnalysis;

namespace Civ.Engine.Core;

/// <summary>
/// A slot arena for one kind of entity: stable indices, generation counters, free-list reuse.
/// </summary>
/// <remarks>
/// <para><b>Determinism.</b> Iteration is always by ascending slot index, never by hash order.
/// Nothing in the engine may iterate a <see cref="Dictionary{TKey,TValue}"/> and expect a
/// reproducible run.</para>
///
/// <para><b>Mutation.</b> Add/Remove are <c>internal</c>. Only <c>Civ.Engine</c> can change the
/// contents of the world; simulation systems live in another assembly and see a read-only API.</para>
///
/// <para><b>Policy note.</b> Regions and polities are never removed in practice - a dissolved
/// polity is marked defunct and kept as a historical record. Removal exists and is tested because
/// short-lived entities (armies, characters, wars) will need it, and because stale-handle
/// detection is much cheaper to get right now than to retrofit.</para>
/// </remarks>
public sealed class EntityTable<TKind, T>
    where TKind : IEntityKind
    where T : class
{
    private readonly List<T?> _slots = [];
    private readonly List<int> _generations = [];
    private readonly List<int> _freeSlots = [];

    /// <summary>Number of live entities.</summary>
    public int Count { get; private set; }

    /// <summary>Number of allocated slots, live or free. Upper bound for index iteration.</summary>
    public int Capacity => _slots.Count;

    internal EntityId<TKind> Add(Func<EntityId<TKind>, T> factory)
    {
        int index;
        int generation;

        if (_freeSlots.Count > 0)
        {
            // LIFO reuse. Deterministic, which is all that matters here.
            index = _freeSlots[^1];
            _freeSlots.RemoveAt(_freeSlots.Count - 1);
            generation = _generations[index];
        }
        else
        {
            index = _slots.Count;
            generation = 1;
            _slots.Add(null);
            _generations.Add(generation);
        }

        var id = new EntityId<TKind>(index, generation);
        _slots[index] = factory(id);
        Count++;
        return id;
    }

    internal bool Remove(EntityId<TKind> id)
    {
        if (!Contains(id))
        {
            return false;
        }

        _slots[id.Index] = null;
        // Bump so every outstanding handle to this slot becomes detectably stale.
        _generations[id.Index] = unchecked(_generations[id.Index] + 1);
        _freeSlots.Add(id.Index);
        Count--;
        return true;
    }

    /// <summary>True if the handle refers to a live entity. False for None and for stale handles.</summary>
    public bool Contains(EntityId<TKind> id) =>
        id.IsSome
        && (uint)id.Index < (uint)_slots.Count
        && _generations[id.Index] == id.Generation
        && _slots[id.Index] is not null;

    public bool TryGet(EntityId<TKind> id, [NotNullWhen(true)] out T? entity)
    {
        if (Contains(id))
        {
            entity = _slots[id.Index]!;
            return true;
        }

        entity = null;
        return false;
    }

    /// <summary>Throws on a stale or absent handle. Use where a missing entity is a bug, not a case.</summary>
    public T Get(EntityId<TKind> id) =>
        TryGet(id, out T? entity)
            ? entity
            : throw new StaleEntityIdException($"{id} does not refer to a live {TKind.KindName}.");

    /// <summary>Live entities in ascending slot order.</summary>
    public IEnumerable<T> All()
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i] is { } entity)
            {
                yield return entity;
            }
        }
    }

    /// <summary>Live handles in ascending slot order.</summary>
    public IEnumerable<EntityId<TKind>> AllIds()
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i] is not null)
            {
                yield return new EntityId<TKind>(i, _generations[i]);
            }
        }
    }

    internal int GenerationAt(int index) => _generations[index];

    /// <summary>Rebuilds a table from persisted slot data. Used only by save loading.</summary>
    internal void RestoreSlot(int index, int generation, T? entity)
    {
        while (_slots.Count <= index)
        {
            _slots.Add(null);
            _generations.Add(1);
        }

        _slots[index] = entity;
        _generations[index] = generation;

        if (entity is null)
        {
            _freeSlots.Add(index);
        }
        else
        {
            Count++;
        }
    }
}

public sealed class StaleEntityIdException(string message) : Exception(message);
