using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.M2Probe;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// Placed pieces in a save (schema 44, WP-1G), past what the fixture's expected state shows: a chest under any identity but its piece's is
/// refused at load; a piece whose definition was discarded is lost while its chest's stacks are put on the ground where it stood, under
/// their own identities; and an empty world encodes none.
/// </summary>
public class PieceSaveTests
{
    private static readonly EntityId Aelin = EntityId.Parse("chr_01HF7YAT00041061050R3GG28A");

    private static EntityId Piece(int ordinal) => EntityId.Derived(EntityKind.Piece, ordinal, "unnamed.piece/v1", Aelin.Value);

    private static string ChestKey => "container." + Piece(4).Value.ToLowerInvariant();

    private static LoadResult LoadV44(ContentIdentity? content = null)
    {
        using var profile = Fixtures.Copy(44);
        return new SaveStore(profile.Root).Load(SaveSlots.Quick, Fixtures.Context(new Registry(), content));
    }

    [Fact]
    public void APieceChest_UnderAnotherIdentity_IsRefused()
    {
        var world = LoadV44().World;
        var snapshot = world.TakeSnapshot();
        var chest = snapshot.Containers.Single(c => c.Key == ChestKey);
        var forged = snapshot with
        {
            Containers = snapshot.Containers.Replace(chest, chest with { InstanceId = EntityId.NewId(EntityKind.Container) }),
        };

        WorldDelta.FromSnapshot(M2Fixtures.Generator(), M2Fixtures.Seed, new Registry(), forged, out var rejected);

        Assert.Equal(("entities", ChestKey, "not its chest's identity"), Assert.Single(rejected).Parts());
    }

    [Fact]
    public void APieceChest_WhosePieceIsGone_IsRefused()
    {
        var snapshot = LoadV44().World.TakeSnapshot();
        var orphaned = snapshot with { Pieces = snapshot.Pieces.RemoveAll(p => p.InstanceId == Piece(4)) };

        WorldDelta.FromSnapshot(M2Fixtures.Generator(), M2Fixtures.Seed, new Registry(), orphaned, out var rejected);

        Assert.Equal(("entities", ChestKey, "its chest is gone"), Assert.Single(rejected).Parts());
    }

    [Fact]
    public void APieceWhoseDefinitionWasDiscarded_IsLost_AndItsChestSpillsWhereItStood()
    {
        var fixture = Fixtures.Content();
        var content = new ContentIdentity("0.3.0", "sha256:" + string.Concat(Enumerable.Repeat("4d", 32)),
            fixture.DefinitionIds.Where(id => id != "piece.fixture.chest"), fixture.Aliases, null, new[] { "piece.fixture.chest" });
        var kept = LoadV44().World.Container(ChestKey)!.Items.Single();

        var loaded = LoadV44(content);

        Assert.Empty(loaded.Report.Blockers);
        Assert.Empty(loaded.RejectedRecords);   // the piece is reported lost (so the load is not complete), and nothing else
        Assert.Equal(new[] { Piece(1) }, loaded.World.Pieces.Select(p => p.InstanceId));
        Assert.Null(loaded.World.Container(ChestKey));
        Assert.Contains(loaded.Report.Loss, l => l.Contains($"piece {Piece(4)}", StringComparison.Ordinal));
        Assert.Contains("1 stacks from the chest of piece " + Piece(4).Value + " were put on the ground", loaded.Report.Warnings);
        var spilled = loaded.World.CreatedIn(CellKey.Parse("r_0_0:c_00_04")).Single(c => c.InstanceId == kept.ItemId);
        Assert.Equal(("item.material.timber", 5, 4_950, 5_150), (spilled.DefId, spilled.Count, spilled.XCm, spilled.ZCm));
    }

    [Fact]
    public void AnEmptyWorld_EncodesNoPieces_AndSequenceZero()
    {
        var (pieces, sequence) = SectionCodec.DecodePieces(SectionCodec.EncodeEntities(DeltaSnapshot.Empty));
        Assert.Equal((0, 0L), (pieces.Length, sequence));
    }
}

internal static class RejectedRecordExtensions
{
    public static (string Section, string Key, string Reason) Parts(this RejectedRecord record) => (record.Section, record.Key, record.Reason);
}
