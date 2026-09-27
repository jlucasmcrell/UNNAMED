namespace UNNAMED.Domain.Tests;

/// <summary>Derived identities (M7 design §4.18): the same derivation gives the same ID, and IDs sort by their ordinal.</summary>
public class EntityIdDerivedTests
{
    private const string Owner = "chr_01J8ZC4K9P4M2Q7X8B3NDTVW6R";

    [Fact]
    public void EntityIdDerived_IsStable_AndOrdersLikeItsOrdinal()
    {
        var first = EntityId.Derived(EntityKind.Piece, 1, "unnamed.piece/v1", Owner);
        Assert.Equal(first, EntityId.Derived(EntityKind.Piece, 1, "unnamed.piece/v1", Owner));
        Assert.Equal(EntityKind.Piece, first.Kind);
        Assert.StartsWith("pce_", first.Value);
        Assert.Equal(1, first.Timestamp);
        Assert.Equal(first, EntityId.Parse(first.Value));

        // Another tag, salt or ordinal is another identity.
        Assert.NotEqual(first, EntityId.Derived(EntityKind.Piece, 1, "unnamed.piece/v2", Owner));
        Assert.NotEqual(first, EntityId.Derived(EntityKind.Piece, 1, "unnamed.piece/v1", "chr_01J8ZC4K9P4M2Q7X8B3NDTVW6S"));
        var ids = Enumerable.Range(1, 300).Select(n => EntityId.Derived(EntityKind.Piece, n, "unnamed.piece/v1", Owner)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(ids, ids.OrderBy(id => id.Value, StringComparer.Ordinal));
        Assert.Equal(Enumerable.Range(1, 300).Select(n => (long)n), ids.Select(id => id.Timestamp));

        // A piece chest's identity comes from its piece's.
        var chest = EntityId.Derived(EntityKind.Container, first.Timestamp, "unnamed.piece-container/v1", first.Value);
        Assert.StartsWith("cnt_", chest.Value);
        Assert.Equal(1, chest.Timestamp);
    }
}
