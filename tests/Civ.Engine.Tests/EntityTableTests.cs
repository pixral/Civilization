using Civ.Engine.Core;

namespace Civ.Engine.Tests;

/// <summary>
/// Handle safety. This is the machinery that has to survive polities being created, dissolved,
/// split and merged for two thousand years without leaving a single reference pointing at the
/// wrong thing.
/// </summary>
public sealed class EntityTableTests
{
    private sealed record Thing(string Label);

    private static (EntityTable<RegionKind, Thing> Table, Func<string, EntityId<RegionKind>> Add) Fresh()
    {
        var table = new EntityTable<RegionKind, Thing>();
        return (table, label => table.Add(_ => new Thing(label)));
    }

    [Fact]
    public void DefaultIdIsNoneAndNeverResolves()
    {
        (EntityTable<RegionKind, Thing> table, _) = Fresh();

        Assert.True(default(EntityId<RegionKind>).IsNone);
        Assert.False(EntityId<RegionKind>.None.IsSome);
        Assert.False(table.Contains(EntityId<RegionKind>.None));
    }

    [Fact]
    public void IdsOfDifferentKindsAreDifferentTypes()
    {
        // Compile-time property, asserted here so its removal is a visible failure rather than a
        // silent loosening: a RegionId can never be passed where a PolityId is expected.
        Assert.NotEqual(typeof(RegionId), typeof(PolityId));
    }

    [Fact]
    public void AddAssignsAscendingIndicesAndGenerationOne()
    {
        (EntityTable<RegionKind, Thing> table, Func<string, EntityId<RegionKind>> add) = Fresh();

        EntityId<RegionKind> a = add("a");
        EntityId<RegionKind> b = add("b");

        Assert.Equal(0, a.Index);
        Assert.Equal(1, b.Index);
        Assert.Equal(1, a.Generation);
        Assert.Equal(2, table.Count);
    }

    [Fact]
    public void RemovedHandleIsRejected()
    {
        (EntityTable<RegionKind, Thing> table, Func<string, EntityId<RegionKind>> add) = Fresh();
        EntityId<RegionKind> id = add("gone");

        Assert.True(table.Remove(id));
        Assert.False(table.Contains(id));
        Assert.False(table.TryGet(id, out _));
        Assert.Throws<StaleEntityIdException>(() => table.Get(id));
        Assert.False(table.Remove(id));
    }

    /// <summary>
    /// The case generations exist for: a slot is reused, and the old handle must not silently
    /// resolve to the new occupant.
    /// </summary>
    [Fact]
    public void ReusedSlotDoesNotResurrectAnOldHandle()
    {
        (EntityTable<RegionKind, Thing> table, Func<string, EntityId<RegionKind>> add) = Fresh();

        EntityId<RegionKind> original = add("first");
        table.Remove(original);
        EntityId<RegionKind> replacement = add("second");

        Assert.Equal(original.Index, replacement.Index);
        Assert.NotEqual(original.Generation, replacement.Generation);

        Assert.False(table.Contains(original));
        Assert.True(table.Contains(replacement));
        Assert.Equal("second", table.Get(replacement).Label);
    }

    [Fact]
    public void IterationIsAscendingBySlotAndSkipsHoles()
    {
        (EntityTable<RegionKind, Thing> table, Func<string, EntityId<RegionKind>> add) = Fresh();

        add("a");
        EntityId<RegionKind> b = add("b");
        add("c");
        table.Remove(b);

        Assert.Equal(["a", "c"], table.All().Select(t => t.Label));
        Assert.Equal([0, 2], table.AllIds().Select(i => i.Index));
    }

    [Fact]
    public void CapacityRetainsFreedSlotsButCountDoesNot()
    {
        (EntityTable<RegionKind, Thing> table, Func<string, EntityId<RegionKind>> add) = Fresh();

        EntityId<RegionKind> a = add("a");
        add("b");
        table.Remove(a);

        Assert.Equal(1, table.Count);
        Assert.Equal(2, table.Capacity);
    }

    [Fact]
    public void IdsAreOrderableForStableIteration()
    {
        var ids = new List<RegionId>
        {
            new(5, 1),
            new(1, 2),
            new(1, 1),
        };

        Assert.Equal(
            [new RegionId(1, 1), new RegionId(1, 2), new RegionId(5, 1)],
            ids.Order().ToList());
    }
}
