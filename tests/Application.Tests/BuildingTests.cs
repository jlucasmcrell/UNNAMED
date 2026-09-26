using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Building v1 over the game's own content (M7 design §4): each piece placed with its exact spend, events, view and ID; each placement
/// rule refusing with its reason and leaving the digest; the ghost answering as the command does; walls and doorways blocking and
/// passing bodies, prediction, creatures and aim; taking down; ownership; host cells; the load audit; and guards G9, G10 and G20.
/// </summary>
public class BuildingTests
{
    private const string Pad = "piece.pad.timber", Wall = "piece.wall.timber", Doorway = "piece.doorway.timber", Roof = "piece.roof.timber",
        Door = "piece.door.timber", Chest = "piece.storage.chest", Bench = "piece.station.anvil";
    private const string Timber = "item.material.timber";
    private const string Renn = "npc.ashen_hollow.renn_vale", Kera = "npc.ashen_hollow.kera_voss";
    private const string Tag = "unnamed.piece/v1";
    private static readonly (string, double, double, string)[] None = Array.Empty<(string, double, double, string)>();

    private static PlayerRecord Carrying(PlayerRecord r, params int[] stacks) =>
        r.WithInventory(r.Inventory.Concat(stacks.Select(n => Arena.Stack(Timber, n))));

    /// <summary>A builder at a place with the workshop's 45 timber (20, 20 and 5), or the stacks given.</summary>
    internal static Arena Builder(GameSession session, (double X, double Z) at, int facingDeg = 0, SimulationSetup? rules = null, int[]? timber = null) =>
        Arena.OpenCreatures(session, rules ?? session.Setup, at, facingDeg, None, r => Carrying(r, timber ?? new[] { 20, 20, 5 }));

    internal static string? Place(Arena arena, string def, long x, long z, int r) => arena.Submit(new PlacePieceCommand(arena.Player, def, x, z, r));

    /// <summary>A pad at (106.5, 100.5) and three walls round it, then the fourth that would close it: reached from (102, 102).</summary>
    private static readonly (string Def, long X, long Z, int R)[] Hut =
    {
        (Pad, 106_500, 100_500, 0), (Wall, 106_500, 99_000, 0), (Wall, 106_500, 102_000, 0), (Wall, 105_000, 100_500, 1), (Wall, 108_000, 100_500, 1),
    };

    private static string? Dismantle(Arena arena, EntityId piece) => arena.Submit(new DismantlePieceCommand(arena.Player, piece));

    private static int[] Stacks(Arena arena) =>
        arena.Simulation.Player.Inventory.Where(e => e.DefId == Timber).Select(e => e.Count).Order().ToArray();

    private static EntityId PieceAt(Arena arena, string def, long x, long z) =>
        arena.Simulation.Pieces.Single(p => p.DefId == def && p.XMm == x && p.ZMm == z).Id;

    private static LoadResult SaveAndLoad(TempProfile profile, GameSession session, Arena arena, string slot)
    {
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual(slot), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        return store.Load(SaveSlots.Manual(slot), new LoadContext(session.Generator, session.Content, new Registry()));
    }

    /// <summary>
    /// The game's rules edited after load, for the rules the shipped area never meets (BLD007 keeps it clear): a pad limit of 0.10 m of
    /// relief, an authored post at (97.5, 103.5), Renn's place moved to (97.5, 97.5), and room for six pieces.
    /// </summary>
    private static SimulationSetup Edited(SimulationSetup setup)
    {
        var layout = setup.Layout;
        return setup with
        {
            Layout = layout with
            {
                Space = layout.Space with { Blockers = layout.Space.Blockers.Add(new CircleBlocker("post.test", 97_500, 103_500, 400, 2_000)) },
                Npcs = layout.Npcs.Select(n => n.NpcId == Renn ? n with { XMm = 97_500, ZMm = 97_500 } : n).ToImmutableArray(),
                BuildAreas = layout.BuildAreas.Select(a => a with { MaxPieces = 6 }).ToImmutableArray(),
            },
            Building = setup.Building with { Constants = setup.Building.Constants with { PadMaxReliefMm = 100 } },
        };
    }

    [Fact]
    public void EachPiece_Places_WithItsExactSpendEventsViewAndId()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (102.0, 102.0));
        var placed = arena.Record<PiecePlaced>();
        var changed = arena.Record<StructuresChanged>();
        int authored = arena.Simulation.Space.Blockers.Length;
        var id = Enumerable.Range(1, 8).Select(n => EntityId.Derived(EntityKind.Piece, n, Tag, arena.Player.Value)).ToArray();

        // A pad: one timber, from the smallest stack; no parts, no footprint, nothing in the collision space.
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Equal(new[] { 4, 20, 20 }, Stacks(arena));
        var pad = Assert.Single(arena.Simulation.Pieces);
        Assert.Equal((id[0], Pad, PieceFamily.Pad, 100_500L, 100_500L, 0, arena.Player, 200, 200, false), (pad.Id, pad.DefId, pad.Family, pad.XMm, pad.ZMm, pad.Rotation,
            pad.Owner, pad.HealthCurrent, pad.HealthMax, pad.DoorOpen));
        Assert.Equal((99_000L, 99_000L, 102_000L, 102_000L), (pad.MinXMm, pad.MinZMm, pad.MaxXMm, pad.MaxZMm));
        Assert.Empty(pad.Parts);
        Assert.Empty(arena.Simulation.StructureFootprints);
        Assert.Equal(authored, arena.Simulation.Space.Blockers.Length);

        // A wall on its south edge: two timber; one solid part.
        Assert.Null(Place(arena, Wall, 100_500, 99_000, 0));
        Assert.Equal(new[] { 2, 20, 20 }, Stacks(arena));
        var wall = arena.Simulation.Pieces.Single(p => p.Id == id[1]);
        Assert.Equal(new[] { new PiecePartView(98_800, 98_800, 102_200, 99_200, 3_000, TraversalClass.Solid) }, wall.Parts);

        // A doorway on its west edge, turned: two timber; two jambs round a 1.6 m opening.
        Assert.Null(Place(arena, Doorway, 99_000, 100_500, 1));
        Assert.Equal(new[] { 20, 20 }, Stacks(arena));
        var doorway = arena.Simulation.Pieces.Single(p => p.Id == id[2]);
        Assert.Equal(new[] { new PiecePartView(98_800, 101_300, 99_200, 102_200, 3_000, TraversalClass.Solid),
            new PiecePartView(98_800, 98_800, 99_200, 99_700, 3_000, TraversalClass.Solid) }, doorway.Parts);

        // A roof over the pad, on the wall: one timber; no parts.
        Assert.Null(Place(arena, Roof, 100_500, 100_500, 0));
        Assert.Equal(new[] { 19, 20 }, Stacks(arena));
        var roof = arena.Simulation.Pieces.Single(p => p.Id == id[3]);
        Assert.Equal((PieceFamily.Roof, 100, 100), (roof.Family, roof.HealthCurrent, roof.HealthMax));
        Assert.Empty(roof.Parts);

        // A door in the doorway (E6): one timber; one leaf across the opening, hung shut - a closed door, not part of the space.
        Assert.Null(Place(arena, Door, 99_000, 100_500, 1));
        Assert.Equal(new[] { 18, 20 }, Stacks(arena));
        var door = arena.Simulation.Pieces.Single(p => p.Id == id[4]);
        Assert.Equal((PieceFamily.Door, 120, 120, false), (door.Family, door.HealthCurrent, door.HealthMax, door.DoorOpen));
        Assert.Equal(new[] { new PiecePartView(98_800, 99_700, 99_200, 101_300, 2_400, TraversalClass.Door) }, door.Parts);
        Assert.Contains(arena.Simulation.DynamicBlockers, b => b is BoxBlocker { MinXMm: 98_800, MinZMm: 99_700, MaxXMm: 99_200, MaxZMm: 101_300 });

        // A chest on the pad (E8): two timber; one solid part against the square's north side; its container keyed by the piece, its site
        // inside its box, no record until something goes in.
        Assert.Null(Place(arena, Chest, 100_500, 100_500, 0));
        Assert.Equal(new[] { 16, 20 }, Stacks(arena));
        var chest = arena.Simulation.Pieces.Single(p => p.Id == id[5]);
        Assert.Equal(new[] { new PiecePartView(100_000, 101_100, 101_000, 101_700, 700, TraversalClass.Solid) }, chest.Parts);
        Assert.Equal("container." + id[5].Value.ToLowerInvariant(), chest.ContainerKey);
        var site = arena.Simulation.Containers.Single(c => c.Site.Key == chest.ContainerKey).Site;
        Assert.Equal((100_500L, 101_400L, 12, "", arena.Player), (site.XMm, site.ZMm, site.StackSlots, site.LootTableId, site.Owner!));
        Assert.Null(arena.Simulation.World.Container(chest.ContainerKey!));

        // A bench on a second pad (E8): four timber; one solid part against the north side; its station keyed by the piece, an anvil at
        // the part's centre, after the authored ones.
        Assert.Null(Place(arena, Pad, 103_500, 100_500, 0));
        Assert.Null(Place(arena, Bench, 103_500, 100_500, 0));
        Assert.Equal(new[] { 11, 20 }, Stacks(arena));
        var bench = arena.Simulation.Pieces.Single(p => p.Id == id[7]);
        Assert.Equal(new[] { new PiecePartView(103_000, 100_900, 104_000, 101_500, 900, TraversalClass.Solid) }, bench.Parts);
        Assert.Equal((PieceFamily.Station, 300, 300, (string?)null), (bench.Family, bench.HealthCurrent, bench.HealthMax, bench.ContainerKey));
        Assert.Equal(new StationSite("station." + id[7].Value.ToLowerInvariant(), "anvil", 103_500, 101_200), arena.Simulation.Stations[^1]);
        Assert.Equal(arena.Simulation.Setup.Layout.Stations, arena.Simulation.Stations.SkipLast(1));
        Assert.Equal(arena.Simulation.Stations[^1].Key, bench.StationKey);

        // Derived IDs by sequence; one event of each per piece; the rows hosted by their anchors.
        Assert.Equal(id, placed.Select(p => p.PieceId));
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 8 }, placed.Select(p => p.Revision));
        Assert.Equal(new[] { Pad, Wall, Doorway, Roof, Door, Chest, Pad, Bench }, placed.Select(p => p.DefId));
        Assert.All(placed, p => Assert.Equal(arena.Player, p.Owner));
        Assert.Equal(new[] { (99_000L, 99_000L, 102_000L, 102_000L), (98_800L, 98_800L, 102_200L, 99_200L), (98_800L, 98_800L, 99_200L, 102_200L),
            (99_000L, 99_000L, 102_000L, 102_000L), (98_800L, 99_700L, 99_200L, 101_300L), (100_000L, 101_100L, 101_000L, 101_700L),
            (102_000L, 99_000L, 105_000L, 102_000L), (103_000L, 100_900L, 104_000L, 101_500L) },
            changed.Select(c => (c.MinXMm, c.MinZMm, c.MaxXMm, c.MaxZMm)));
        Assert.All(changed, c => Assert.Equal(StructureChangeKind.Placed, c.Kind));
        Assert.Equal(8, arena.Simulation.StructureRevision);
        Assert.Equal(new[] { "r_0_0:c_01_01", "r_0_0:c_01_00", "r_0_0:c_00_01", "r_0_0:c_01_01", "r_0_0:c_00_01", "r_0_0:c_01_01", "r_0_0:c_01_01",
                "r_0_0:c_01_01" },
            id.Select(i => arena.Simulation.World.Piece(i)!.HostCell));

        // Space appends exactly the solid parts, in structure order, after the authored blockers; the footprints are those parts and the
        // door's leaf.
        var footprints = arena.Simulation.StructureFootprints;
        Assert.Equal(6, footprints.Length);
        Assert.Equal(5, footprints.Count(f => f.Class == TraversalClass.Solid));
        Assert.Equal(id[4], Assert.Single(footprints, f => f.Class == TraversalClass.Door).PieceId);
        Assert.Equal(footprints.Where(f => f.Class == TraversalClass.Solid).Select(f => (f.MinXMm, f.MinZMm, f.MaxXMm, f.MaxZMm, f.HeightMm)),
            arena.Simulation.Space.Blockers.Skip(authored).Cast<BoxBlocker>().Select(b => (b.MinXMm, b.MinZMm, b.MaxXMm, b.MaxZMm, b.HeightMm)));
        Assert.Equal(authored + 5, arena.Simulation.Space.Blockers.Length);
    }

    [Fact]
    public void EachPlacementRule_RefusesWithItsReason_AndLeavesTheDigest()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (102.0, 102.0), rules: Edited(session.Setup), timber: new[] { 3 });
        void Refused(string expected, PlacePieceCommand command)
        {
            string before = arena.Simulation.StateDigest();
            Assert.Equal(expected, arena.Submit(command));
            Assert.Equal(before, arena.Simulation.StateDigest());
        }
        PlacePieceCommand P(string def, long x, long z, int r) => new(arena.Player, def, x, z, r);

        Refused($"unknown actor {EntityId.Parse("chr_01JZZZZZZZZZZZZZZZZZZZZZZZ")}", new PlacePieceCommand(EntityId.Parse("chr_01JZZZZZZZZZZZZZZZZZZZZZZZ"), Pad, 100_500, 100_500, 0));
        Refused("piece.nothing is not a piece this build knows", P("piece.nothing", 100_500, 100_500, 0));
        Refused("Timber Pad cannot be turned that way", P(Pad, 100_500, 100_500, 4));
        Refused("that is not on the building grid", P(Pad, 100_000, 100_500, 0));
        Refused("you may only build inside a build area", P(Pad, 85_500, 100_500, 0));
        Refused("that is 12.73 m away; building reach is 6.00 m", P(Pad, 112_500, 112_500, 0));
        Refused("a wall needs a floor pad on one side", P(Wall, 100_500, 99_000, 0));
        Refused("a roof needs a wall under one of its edges, or a roofed neighbour that has one", P(Roof, 100_500, 100_500, 0));
        Refused("the ground here is too uneven for a pad: 0.14 m of rise, 0.10 m allowed", P(Pad, 103_500, 100_500, 0));   // edited limit
        Refused("that would build over something already standing there", P(Pad, 97_500, 103_500, 0));                // edited: a post
        Refused("that ground is kept clear (Renn Vale's place)", P(Pad, 97_500, 97_500, 0));                          // edited: Renn moved
        Assert.Null(arena.Submit(P(Pad, 100_500, 100_500, 0)));
        Refused("a Timber Pad already stands there", P(Pad, 100_500, 100_500, 0));
        Refused("someone is standing there", P(Wall, 102_000, 100_500, 1));   // through the character, at (102, 102)
        Assert.Null(arena.Submit(P(Pad, 100_500, 103_500, 0)));
        Refused("needs 2 item.material.timber", P(Wall, 100_500, 99_000, 0));   // one timber left

        // Room for six (edited): four more pads fill the area, and the seventh piece is refused.
        var room = Builder(session, (102.0, 102.0), rules: Edited(session.Setup));
        foreach (var (x, z) in new[] { (100_500L, 100_500L), (100_500L, 103_500L), (97_500L, 100_500L), (100_500L, 97_500L), (103_500L, 97_500L), (106_500L, 97_500L) })
            Assert.Null(Place(room, Pad, x, z, 0));
        string digest = room.Simulation.StateDigest();
        Assert.Equal("this build area already holds 6 pieces", Place(room, Pad, 97_500, 106_500, 0));
        Assert.Equal(digest, room.Simulation.StateDigest());

        // The door's cases (E6): rule 4, off every doorway; rule 7, on a doorway that has its door.
        var doors = Builder(session, (102.0, 102.0));
        Assert.Null(Place(doors, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(doors, Doorway, 100_500, 99_000, 0));
        digest = doors.Simulation.StateDigest();
        Assert.Equal("that is not on the building grid", Place(doors, Door, 100_500, 102_000, 0));
        Assert.Equal(digest, doors.Simulation.StateDigest());
        Assert.Null(Place(doors, Door, 100_500, 99_000, 0));
        digest = doors.Simulation.StateDigest();
        Assert.Equal("that doorway already has a door", Place(doors, Door, 100_500, 99_000, 0));
        Assert.Equal(digest, doors.Simulation.StateDigest());

        // Rule 15 (E7): the wall that would close a hut with no way in.
        foreach (var (def, x, z, r) in Hut.SkipLast(1))
            Assert.Null(Place(doors, def, x, z, r));
        digest = doors.Simulation.StateDigest();
        var (lastDef, lastX, lastZ, lastR) = Hut[^1];
        Assert.Equal(NavEditCheck.SealedReason, Place(doors, lastDef, lastX, lastZ, lastR));
        Assert.Equal(digest, doors.Simulation.StateDigest());
        // Rule 1's other half, "dead", is never met at a command boundary: a death and the return to the Waystone are one tick.
    }

    [Fact]
    public void Preview_EqualsTheCommand_OverFortyPoses()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (102.0, 102.0), rules: Edited(session.Setup), timber: new[] { 7 });
        var poses = new List<(string Def, long X, long Z, int R)>
        {
            ("piece.nothing", 100_500, 100_500, 0), (Pad, 100_500, 100_500, 5), (Pad, 100_000, 100_500, 0), (Wall, 100_500, 100_500, 0),
            (Pad, 85_500, 100_500, 0), (Pad, 112_500, 112_500, 0), (Pad, 103_500, 100_500, 0), (Pad, 97_500, 103_500, 0), (Pad, 97_500, 97_500, 0),
            (Wall, 100_500, 99_000, 0), (Roof, 100_500, 100_500, 0), (Pad, 100_500, 100_500, 0), (Pad, 100_500, 100_500, 0), (Pad, 100_500, 103_500, 0),
            (Wall, 100_500, 99_000, 0), (Wall, 100_500, 99_000, 2), (Roof, 100_500, 100_500, 0), (Roof, 100_500, 103_500, 0), (Roof, 100_500, 106_500, 0),
            (Wall, 102_000, 100_500, 1), (Wall, 99_000, 100_500, 1), (Pad, 97_500, 100_500, 0), (Pad, 100_500, 97_500, 0),
        };
        // And a sweep round the character of every piece at every turn the pose admits.
        foreach (long x in new long[] { 97_500, 100_500, 103_500 })
        {
            poses.Add((Pad, x, 106_500, 0));
            poses.Add((Doorway, x, 105_000, 2));
            poses.Add((Wall, x - 1_500, 106_500, 3));
            poses.Add((Roof, x, 97_500, 1));
        }
        poses.AddRange(new[] { (Doorway, 102_000L, 103_500L, 1), (Wall, 103_500L, 102_000L, 0), (Pad, 94_500L, 100_500L, 2),
            (Roof, 103_500L, 103_500L, 3), (Wall, 96_000L, 100_500L, 1) });
        Assert.Equal(40, poses.Count);

        var failed = new HashSet<PlacementRule>();
        int allowed = 0;
        foreach (var (def, x, z, r) in poses)
        {
            var preview = arena.Simulation.PreviewPlacement(def, x, z, r, checkNavigability: true);
            string? refused = Place(arena, def, x, z, r);
            Assert.True(preview.Allowed == (refused is null), $"{def} at ({x}, {z}) r{r}: the ghost says {preview.Allowed}, the command {refused ?? "yes"}");
            Assert.Equal(refused, preview.Reason);
            Assert.Equal(preview.Allowed, preview.Failed is null);
            if (preview.Failed is { } rule)
                failed.Add(rule);
            else
                allowed++;
        }
        Assert.True(allowed >= 5, $"only {allowed} poses were allowed");

        // Rule 15 (E7) needs room and timber the edited rules do not give: the hut, and its fourth wall, on the game's own rules - the
        // ghost asked with navigability, as it asks when the pose changes.
        var hut = Builder(session, (102.0, 102.0));
        foreach (var (def, x, z, r) in Hut)
        {
            var preview = hut.Simulation.PreviewPlacement(def, x, z, r, checkNavigability: true);
            string? refused = Place(hut, def, x, z, r);
            Assert.True(preview.Allowed == (refused is null), $"{def} at ({x}, {z}) r{r}: the ghost says {preview.Allowed}, the command {refused ?? "yes"}");
            Assert.Equal(refused, preview.Reason);
            Assert.Equal(preview.Allowed ? (def == Pad ? NavVerdict.NotApplicable : NavVerdict.Proven) : NavVerdict.Refused, preview.Navigability);
            if (preview.Failed is { } rule)
                failed.Add(rule);
        }
        Assert.Equal(Enum.GetValues<PlacementRule>().Where(r => r != PlacementRule.Actor), failed.Order());
    }

    /// <summary>A chest at (100.5, 100.5) on its pad, reached from (100.5, 102.6), and its container's key.</summary>
    private static (Arena Arena, string Key) WithAChest(GameSession session)
    {
        var arena = Builder(session, (100.5, 102.6));
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Chest, 100_500, 100_500, 0));
        return (arena, arena.Simulation.Pieces.Single(p => p.DefId == Chest).ContainerKey!);
    }

    private static string? Store(Arena arena, string key, int count)
    {
        var stack = arena.Simulation.Player.Inventory.Where(e => e.DefId == Timber).OrderBy(e => e.Count).First(e => e.Count >= count);
        return arena.Submit(new MoveItemCommand(arena.Player, stack.ItemId.Value, ItemPlace.Carried, ItemPlace.In(key), count));
    }

    /// <summary>
    /// The C1 boundary (M7 design §4.13): a placed chest's first store gives its record the identity its piece derives; emptied, the
    /// record stays - the corpse clause never matches a piece chest's key - so the next store finds the same identity, with no exception.
    /// </summary>
    [Fact]
    public void APieceChest_EmptiedAndRefilled_KeepsOneIdentity()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, key) = WithAChest(session);
        var chest = arena.Simulation.Pieces.Single(p => p.DefId == Chest).Id;
        var derived = EntityId.Derived(EntityKind.Container, chest.Timestamp, "unnamed.piece-container/v1", chest.Value);

        Assert.Null(Store(arena, key, 2));
        Assert.Equal(derived, arena.Simulation.World.Container(key)!.InstanceId);
        Assert.Null(arena.Submit(new TakeAllCommand(arena.Player, key)));
        var emptied = arena.Simulation.World.Container(key);
        Assert.NotNull(emptied);
        Assert.Empty(emptied!.Items);
        Assert.Equal(derived, emptied.InstanceId);
        Assert.Null(Store(arena, key, 2));
        Assert.Equal(derived, arena.Simulation.World.Container(key)!.InstanceId);
        Assert.Equal(2, arena.Simulation.World.Container(key)!.Items.Sum(i => i.Count));
    }

    /// <summary>The same across a save: the emptied record is kept, loads as the chest's, and takes the next store.</summary>
    [Fact]
    public void APieceChest_EmptiedAndRefilled_AcrossASave_KeepsOneIdentity()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, key) = WithAChest(session);
        Assert.Null(Store(arena, key, 2));
        var identity = arena.Simulation.World.Container(key)!.InstanceId;
        Assert.Null(arena.Submit(new TakeAllCommand(arena.Player, key)));
        var loaded = Arena.Resume(session.Setup, SaveAndLoad(profile, session, arena, "emptied"));
        Assert.Equal((identity, 0), (loaded.Simulation.World.Container(key)!.InstanceId, loaded.Simulation.World.Container(key)!.Items.Length));
        Assert.Null(Store(loaded, key, 2));
        Assert.Equal(identity, loaded.Simulation.World.Container(key)!.InstanceId);
    }

    /// <summary>A chest holding anything cannot be taken down; emptied, it can, and its record goes with it.</summary>
    [Fact]
    public void AChestWithItems_CannotBeTakenDown()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, key) = WithAChest(session);
        var chest = arena.Simulation.Pieces.Single(p => p.DefId == Chest).Id;
        Assert.Null(Store(arena, key, 2));
        string digest = arena.Simulation.StateDigest();
        Assert.Equal("empty the chest first", Dismantle(arena, chest));
        Assert.Equal(digest, arena.Simulation.StateDigest());
        Assert.Null(arena.Submit(new TakeAllCommand(arena.Player, key)));
        Assert.Null(Dismantle(arena, chest));
        Assert.Null(arena.Simulation.World.Container(key));
        Assert.DoesNotContain(arena.Simulation.Containers, c => c.Site.Key == key);
    }

    /// <summary>The station piece (M7 design §4.14): a bench is an anvil for the March Spear while it stands, and not once it is taken down.</summary>
    [Fact]
    public void APieceAnvil_Crafts_UntilTakenDown()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const string Ingot = "item.material.iron_ingot", Haft = "item.material.ash_haft", Spear = "recipe.smithing.march_spear";
        var arena = Arena.OpenCreatures(session, session.Setup, (100.5, 102.6), 0, None, r =>
            Carrying(r.WithInventory(r.Inventory.Concat(new[] { Arena.Stack(Ingot, 2), Arena.Stack(Haft, 2) })), 20).WithProgression(r.Progression with { Known = r.Progression.Known.SetItem(Spear, new KnownTechnique(LearningSource.Teacher, Kera, 0)) }));
        int Carried(string def) => arena.Simulation.Player.Inventory.Where(e => e.DefId == def).Sum(e => e.Count);

        Assert.Equal("no anvil in reach", arena.Submit(new CraftCommand(arena.Player, Spear)));
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Bench, 100_500, 100_500, 0));
        var bench = arena.Simulation.Pieces.Single(p => p.DefId == Bench);
        var site = arena.Simulation.Stations.Single(s => s.Key == bench.StationKey);
        Assert.Equal(("anvil", 100_500L, 101_200L), (site.Kind, site.XMm, site.ZMm));
        Assert.Null(arena.Submit(new CraftCommand(arena.Player, Spear)));
        arena.Tick(200);
        Assert.Equal((1, 1, 1), (Carried(Ingot), Carried(Haft), Carried("item.weapon.march_spear")));

        Assert.Null(Dismantle(arena, bench.Id));
        Assert.DoesNotContain(arena.Simulation.Stations, s => s.Key == bench.StationKey);
        Assert.Equal("no anvil in reach", arena.Submit(new CraftCommand(arena.Player, Spear)));
        Assert.Equal((1, 1), (Carried(Ingot), Carried(Haft)));
    }

    // ── blows and mending (E8) ──────────────────────────────────────────────────

    /// <summary>Swing along the facing <paramref name="count"/> times, each once the last is over (waiting out any refusal).</summary>
    internal static void Blows(Arena arena, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int waited = 0;
            while (arena.Submit(new AttackCommand(arena.Player)) is { } refused)
            {
                Assert.True(++waited < 400, $"blow {i + 1} refused: {refused}");
                arena.Tick();
            }
            arena.Tick(arena.Simulation.Combat.Weapon.TotalTicks + 1);
        }
    }

    private static int Health(Arena arena, EntityId piece) => arena.Simulation.Pieces.Single(p => p.Id == piece).HealthCurrent;

    /// <summary>
    /// The one damage rule (M7 design §4.12): the sword's blow that meets no creature takes 10 from the piece it lands on, in place of a
    /// miss; swung at open air it still misses. An arrow and the impulse bolt stop at the same wall and take nothing.
    /// </summary>
    [Fact]
    public void DamageRules_OnlyAMeleeBlow_DamagesAPiece()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const string Bow = "item.weapon.hunting_bow", Arrow = "item.ammo.arrow_rough", Impulse = "spell.force.impulse_bolt";
        var arena = Arena.OpenCreatures(session, session.Setup, (100.5, 100.0), 180, None, r =>
            Carrying(r.WithInventory(r.Inventory.Concat(new[] { Arena.Stack(Bow, 1), Arena.Stack(Arrow, 5) })), 20).WithProgression(r.Progression with
            {
                Known = r.Progression.Known.SetItem(Impulse, new KnownTechnique(LearningSource.Book, "item.tome.resonance_primer", 0)),
                Skills = r.Progression.Skills.SetItem("skill.force", new SkillState(5, 0)),
            }));
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Wall, 100_500, 99_000, 0));
        var wall = PieceAt(arena, Wall, 100_500, 99_000);
        var damaged = arena.Record<PieceDamaged>();
        var missed = arena.Record<AttackMissed>();

        Blows(arena, 1);
        var blow = Assert.Single(damaged);
        Assert.Equal((wall, Wall, 10, 190, "melee"), (blow.PieceId, blow.DefId, blow.Amount, blow.HealthNow, blow.Source));
        Assert.Empty(missed);
        Assert.Equal(190, Health(arena, wall));

        arena.TurnTo(0);
        arena.Tick();
        Blows(arena, 1);
        Assert.Single(missed);
        Assert.Single(damaged);

        // The bow: the arrow stops at the wall's face and takes nothing.
        arena.TurnTo(180);
        arena.Tick();
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, arena.Simulation.Player.Inventory.Single(e => e.DefId == Bow).ItemId)));
        var shots = arena.Record<ShotLoosed>();
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(40);
        Assert.InRange(Assert.Single(shots).ToZMm, 99_200, 99_210);
        // The impulse bolt likewise.
        Assert.Null(arena.Submit(new CastCommand(arena.Player, Impulse)));
        arena.Tick(80);
        Assert.Single(damaged);
        Assert.Equal(190, Health(arena, wall));

        // A boar's charge that ends on a piece stuns the boar and takes nothing: shot from 9.8 m, it charges; the character steps aside
        // and it runs on into the wall on the pad's north edge, 1 m behind where they stood.
        const string Boar = "creature.beast.bristleback_boar";
        var field = Arena.OpenCreatures(session, session.Setup, (100.5, 100.8), 180, new[] { (Boar, 100.5, 91.0, "sentinel") }, r =>
        {
            var bow = Arena.Stack(Bow, 1);
            var armed = Carrying(r.WithInventory(r.Inventory.Concat(new[] { bow, Arena.Stack(Arrow, 5) })), 20);
            return new PlayerRecord(armed.Id, armed.Name, armed.XMm, armed.YMm, armed.ZMm, armed.AppearanceSeed, armed.Inventory, armed.Progression,
                armed.FacingMdeg, armed.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, bow.ItemId) }, armed.Currency, armed.Effects);
        });
        Assert.Null(Place(field, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(field, Wall, 100_500, 102_000, 0));
        var north = PieceAt(field, Wall, 100_500, 102_000);
        var stunned = field.Record<CreatureStunned>();
        var struck = field.Record<PieceDamaged>();
        Assert.Null(field.Submit(new AttackCommand(field.Player)));
        field.Tick(field.Simulation.Combat.Weapon.TotalTicks + 1);
        bool dodged = false;
        for (int i = 0; i < 200 && stunned.Count == 0; i++)
        {
            if (!dodged && field.Creature().Phase == CombatPhase.Active && field.Simulation.Combat.Phase == CombatPhase.Idle)
            {
                Assert.Null(field.Submit(new DodgeCommand(field.Player, 1000, 0)));
                dodged = true;
            }
            field.Tick();
        }
        Assert.True(dodged);
        Assert.Single(stunned);
        Assert.True(field.Creature().Body.ZMm > 100_800, $"the boar stopped at {field.Creature().Body}, short of the wall");
        Assert.Empty(struck);
        Assert.Equal(200, Health(field, north));
    }

    /// <summary>A swing that strikes a creature strikes no piece (M7 design §4.12): the piece's jamb on the facing line is left whole.</summary>
    [Fact]
    public void ABlowThatHitsACreature_DamagesNoPiece()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The facing line meets the doorway's west jamb 0.8 m off. Built first, then loaded with a wolf in the opening - inside the
        // swing's arc and reach - since a spawner's ground is kept clear of building.
        var built = Arena.OpenCreatures(session, session.Setup, (99.6, 100.0), 180, None, r => Carrying(r, 20));
        Assert.Null(Place(built, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(built, Doorway, 100_500, 99_000, 0));
        var doorway = PieceAt(built, Doorway, 100_500, 99_000);
        var wolf = new SpawnSite("spawn.test.wolf_0", 100_300, 99_000, 0, ImmutableArray.Create(new SpawnMember(Arena.Wolf, "sentinel")));
        var withWolf = session.Setup with { Combat = session.Setup.Combat with { Spawns = ImmutableArray.Create(wolf) } };
        var arena = Arena.Resume(withWolf, SaveAndLoad(profile, session, built, "wolf"));
        Assert.Equal((100_300L, 99_000L), (arena.Creature().Body.XMm, arena.Creature().Body.ZMm));
        var damaged = arena.Record<PieceDamaged>();
        var hits = arena.Record<HitResolved>();
        Blows(arena, 1);
        Assert.Contains(hits, h => h.Attacker == arena.Player && h.Target == arena.Creature().Id);
        Assert.Empty(damaged);
        Assert.Equal(200, Health(arena, doorway));

        // With the wolf gone, the same swing lands on the jamb.
        var alone = Arena.OpenCreatures(session, session.Setup, (99.6, 100.0), 180, None, r => Carrying(r, 20));
        Assert.Null(Place(alone, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(alone, Doorway, 100_500, 99_000, 0));
        Blows(alone, 1);
        Assert.Equal(190, Health(alone, PieceAt(alone, Doorway, 100_500, 99_000)));
    }

    /// <summary>
    /// An authored wall shields a piece it touches (M7 design §4.12): a blow whose line meets an authored blocker damages no piece; the
    /// same blow without it lands on the piece.
    /// </summary>
    [Fact]
    public void AnAuthoredWall_ShieldsATouchingPiece()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var layout = session.Setup.Layout;
        // A low authored box against the west face of a wall piece on a pad's west edge, 0.8 m before the body, off the pad's square.
        var shielded = session.Setup with
        {
            Layout = layout with
            {
                Space = layout.Space with { Blockers = layout.Space.Blockers.Add(new BoxBlocker("wall.test", 98_400, 100_000, 98_800, 101_000, 1_000)) },
            },
        };
        foreach (var (rules, expected) in new[] { (shielded, 200), (session.Setup, 190) })
        {
            var arena = Builder(session, (97.6, 100.5), 90, rules, new[] { 20 });
            Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
            Assert.Null(Place(arena, Wall, 99_000, 100_500, 1));
            var wall = PieceAt(arena, Wall, 99_000, 100_500);
            var missed = arena.Record<AttackMissed>();
            Blows(arena, 1);
            Assert.Equal(expected, Health(arena, wall));
            Assert.Equal(expected == 200 ? 1 : 0, missed.Count);
        }
    }

    /// <summary>
    /// A piece at 0 is destroyed (M7 design §4.13), with nothing back; a destroyed doorway takes its door first - two removals, two
    /// rebuilds, the sequence twice - and the pad it stood on is left.
    /// </summary>
    [Fact]
    public void APieceAtZero_IsDestroyed_AndADoorwayTakesItsDoor()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The facing line meets the doorway's west jamb, beside the door's leaf.
        var arena = Builder(session, (99.25, 100.0), 180);
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Doorway, 100_500, 99_000, 0));
        Assert.Null(Place(arena, Door, 100_500, 99_000, 0));
        var doorway = PieceAt(arena, Doorway, 100_500, 99_000);
        var door = PieceAt(arena, Door, 100_500, 99_000);
        var stacks = Stacks(arena);
        long revision = arena.Simulation.StructureRevision;
        var damaged = arena.Record<PieceDamaged>();
        var destroyed = arena.Record<PieceDestroyed>();
        var removed = arena.Record<PieceRemoved>();
        var changed = arena.Record<StructuresChanged>();
        var rebuilt = arena.Record<NavigationRebuilt>();

        Blows(arena, 19);
        Assert.Equal(Enumerable.Range(1, 19).Select(n => 200 - 10 * n), damaged.Select(d => d.HealthNow));
        Assert.All(damaged, d => Assert.Equal((doorway, "melee"), (d.PieceId, d.Source)));
        Assert.Equal((revision, 0, 120), (arena.Simulation.StructureRevision, rebuilt.Count, Health(arena, door)));
        Blows(arena, 1);

        Assert.Equal(new[] { (door, Door, "melee", revision + 1), (doorway, Doorway, "melee", revision + 2) },
            destroyed.Select(d => (d.PieceId, d.DefId, d.Source, d.Revision)));
        Assert.Empty(removed);
        Assert.Equal(new[] { (StructureChangeKind.Destroyed, revision + 1), (StructureChangeKind.Destroyed, revision + 2) },
            changed.Select(c => (c.Kind, c.Revision)));
        Assert.Equal(new[] { "destroyed", "destroyed" }, rebuilt.Select(r => r.Reason));
        Assert.Equal(revision + 2, arena.Simulation.StructureRevision);
        Assert.Equal(Pad, Assert.Single(arena.Simulation.Pieces).DefId);
        Assert.Equal(stacks, Stacks(arena));   // nothing back
        Assert.Null(arena.Simulation.World.Piece(door));
        Assert.Null(arena.Simulation.World.Piece(doorway));
        Assert.DoesNotContain(arena.Simulation.Space.Blockers, b => b.Id.StartsWith(doorway.Value, StringComparison.Ordinal));
    }

    /// <summary>
    /// Mending (M7 design §4.12): refused in order - the actor, the piece, reach, whole already, the materials - and otherwise one cost,
    /// the cost line scaled by the health missing and rounded up, for the whole of it.
    /// </summary>
    [Fact]
    public void Repair_CostsTheScaledTimber_AndRefusesInOrder()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (100.5, 100.0), 180, timber: new[] { 20 });
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Wall, 100_500, 99_000, 0));
        var wall = PieceAt(arena, Wall, 100_500, 99_000);
        var repaired = arena.Record<PieceRepaired>();
        var stranger = EntityId.Parse("chr_01JZZZZZZZZZZZZZZZZZZZZZZZ");

        Assert.Equal($"unknown actor {stranger}", arena.Submit(new RepairPieceCommand(stranger, wall)));
        Assert.Equal("there is no such piece", arena.Submit(new RepairPieceCommand(arena.Player, EntityId.NewId(EntityKind.Piece))));
        Assert.Equal("it needs no repair", arena.Submit(new RepairPieceCommand(arena.Player, wall)));
        Blows(arena, 3);
        Assert.Equal(170, Health(arena, wall));
        Assert.True(arena.WalkTo(110.5, 102.0));
        string far = arena.Submit(new RepairPieceCommand(arena.Player, wall))!;
        Assert.StartsWith("that is ", far);
        Assert.EndsWith(" m away; building reach is 6.00 m", far);
        Assert.True(arena.WalkTo(100.5, 100.3));
        Assert.Empty(repaired);

        // 170 of 200: ⌈2 × 30 × 100 / 20 000⌉ = 1 timber, for the whole of it.
        string digest = arena.Simulation.StateDigest();
        Assert.Null(arena.Submit(new RepairPieceCommand(arena.Player, wall)));
        Assert.Equal((wall, 170, 200), (Assert.Single(repaired).PieceId, repaired[0].From, repaired[0].To));
        Assert.Equal(200, Health(arena, wall));
        Assert.Equal(new[] { 16 }, Stacks(arena));
        Assert.NotEqual(digest, arena.Simulation.StateDigest());

        // Without the timber: refused, and nothing changes.
        var poor = Builder(session, (100.5, 100.0), 180, timber: new[] { 3 });
        Assert.Null(Place(poor, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(poor, Wall, 100_500, 99_000, 0));
        Blows(poor, 3);
        string before = poor.Simulation.StateDigest();
        Assert.Equal($"needs 1 {Timber}", poor.Submit(new RepairPieceCommand(poor.Player, PieceAt(poor, Wall, 100_500, 99_000))));
        Assert.Equal(before, poor.Simulation.StateDigest());
    }

    /// <summary>
    /// A destroyed chest spills (M7 design §4.13): each stack it held lies on the ground at its site with its item ID kept, and only the
    /// chest's own identity goes; nothing is lost and nothing minted.
    /// </summary>
    [Fact]
    public void ADestroyedChest_SpillsItsStacks_KeepingTheirIds()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const string Ingot = "item.material.iron_ingot";
        var arena = Arena.OpenCreatures(session, session.Setup, (100.5, 102.6), 180, None,
            r => Carrying(r.WithInventory(r.Inventory.Add(Arena.Stack(Ingot, 1))), 20));
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Chest, 100_500, 100_500, 0));
        var chest = PieceAt(arena, Chest, 100_500, 100_500);
        string key = arena.Simulation.Pieces.Single(p => p.Id == chest).ContainerKey!;
        Assert.Null(Store(arena, key, 2));
        var ingot = arena.Simulation.Player.Inventory.Single(e => e.DefId == Ingot);
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, ingot.ItemId.Value, ItemPlace.Carried, ItemPlace.In(key), 1)));
        var held = arena.Simulation.World.Container(key)!.Items;
        Assert.Equal(2, held.Length);
        var destroyed = arena.Record<PieceDestroyed>();

        Blows(arena, 10);
        Assert.Equal((chest, Chest, "melee"), (Assert.Single(destroyed).PieceId, destroyed[0].DefId, destroyed[0].Source));
        Assert.Null(arena.Simulation.World.Container(key));
        Assert.DoesNotContain(arena.Simulation.Containers, c => c.Site.Key == key);
        var spilled = arena.Simulation.WorldItems.Where(i => held.Any(h => h.ItemId == i.Id)).ToList();
        Assert.Equal(held.Select(h => (h.ItemId, h.DefId, h.Count, 100_500L, 101_400L)).Order(),
            spilled.Select(s => (s.Id, s.DefId, s.Count, s.XMm, s.ZMm)).Order());

        // They are ordinary ground stacks: picked up, the timber joins the carried stack and the ingot keeps its identity.
        foreach (var item in held)
            Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, item.ItemId.Value, ItemPlace.Ground, ItemPlace.Carried, item.Count)));
        Assert.Equal(new[] { 17 }, Stacks(arena));
        Assert.Equal(held.Single(h => h.DefId == Ingot).ItemId, arena.Simulation.Player.Inventory.Single(e => e.DefId == Ingot).ItemId);
        Assert.DoesNotContain(arena.Simulation.WorldItems, i => held.Any(h => h.ItemId == i.Id));
    }

    // F-E10
    /// <summary>
    /// Two fresh new games that take timber from the stack, place a pad and a chest, and store 2 timber give equal replayable dumps: the
    /// chest's key, which embeds its piece's ID, is named as its piece is.
    /// </summary>
    [Fact]
    public void TwoFreshRuns_WithAPieceChest_ProduceTheSameReplayableDump()
    {
        string Run()
        {
            using var profile = new TempProfile();
            var session = Harness.Boot(profile);
            var simulation = session.NewGame("Wanderer", seed: 42);
            var player = simulation.PlayerId;
            void Do(GameCommand command)
            {
                session.Submit(command);
                session.Frame(session.TickSeconds);
            }
            Assert.True(Harness.WalkPath(session, (44, 138), (54.5, 134), (62, 130), (80, 118)), $"the walk out stopped at {simulation.Player.Body}");
            Assert.True(Harness.WalkTo(session, 84_000, 116_600, Gait.Walk, toleranceMm: 50));
            Do(new MoveItemCommand(player, "container.timber_stack#00", ItemPlace.In("container.timber_stack"), ItemPlace.Carried, 5));
            Assert.True(Harness.WalkTo(session, 88_000, 112_500, Gait.Walk, toleranceMm: 50));
            Do(new PlacePieceCommand(player, Pad, 88_500, 112_500, 0));
            Do(new PlacePieceCommand(player, Chest, 88_500, 112_500, 0));
            string key = simulation.Pieces.Single(p => p.DefId == Chest).ContainerKey!;
            Do(new MoveItemCommand(player, simulation.Player.Inventory.Single(e => e.DefId == Timber).ItemId.Value, ItemPlace.Carried, ItemPlace.In(key), 2));
            Assert.Equal(2, simulation.World.Container(key)!.Items.Sum(i => i.Count));
            Assert.DoesNotContain(simulation.Player.Inventory, e => e.DefId == Timber);
            // The C1 boundary: IsCorpse matches only a creature's corpse key, and every one of the world's is corpse.*, never a chest's.
            Assert.NotEmpty(simulation.Creatures);
            Assert.All(simulation.Creatures, c => Assert.StartsWith("corpse.", c.CorpseKey));
            return StateDump.Render(simulation, replayable: true);
        }
        string first = Run(), second = Run();
        Assert.Empty(StateDump.Compare(first, second, out _));
        Assert.Contains("container.pce#", first);
        Assert.DoesNotContain("container.pce_", first);
    }

    // L6
    /// <summary>
    /// A hut of one square with no way in is refused as its last wall goes up (M7 design §3.13, V-N1), with no protected point in it:
    /// rooms need a doorway. With a doorway in that side instead, it stands.
    /// </summary>
    [Fact]
    public void ADoorlessOneSquareHut_IsRefused()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (102.0, 102.0));
        foreach (var (def, x, z, r) in Hut.SkipLast(1))
            Assert.Null(Place(arena, def, x, z, r));
        string digest = arena.Simulation.StateDigest();
        var (_, lastX, lastZ, lastR) = Hut[^1];
        var preview = arena.Simulation.PreviewPlacement(Wall, lastX, lastZ, lastR, checkNavigability: true);
        Assert.Equal((false, (PlacementRule?)PlacementRule.Navigability, NavVerdict.Refused), (preview.Allowed, preview.Failed, preview.Navigability));
        Assert.Equal("that would close off a space with no way in; rooms need a doorway", Place(arena, Wall, lastX, lastZ, lastR));
        Assert.Equal(digest, arena.Simulation.StateDigest());
        Assert.Equal(1, arena.Simulation.Navigation.Counters.EditRefusalsByRule.GetValueOrDefault("V-N1"));
        Assert.Null(Place(arena, Doorway, lastX, lastZ, lastR));
    }

    [Fact]
    public void WallsDoorwaysAndDoors_BlockAndPass()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (102.0, 97.0));
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Pad, 103_500, 100_500, 0));
        Assert.Null(Place(arena, Doorway, 100_500, 99_000, 0));
        Assert.Null(Place(arena, Wall, 103_500, 99_000, 0));

        // Walking north into the wall, the body stops a body's radius short of it, and stays there.
        Assert.True(arena.WalkTo(103.5, 97.0));
        var north = new MoveIntent(0, MoveIntent.FullDeflection, Gait.Walk, 0);
        var zs = new List<long>();
        for (int i = 0; i < 40; i++)
        {
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, north));
            arena.Tick();
            zs.Add(arena.Simulation.Player.Body.ZMm);
        }
        Assert.All(zs, z => Assert.True(z <= 98_450, $"the body went to z {z}, into the wall"));
        Assert.InRange(zs[^1], 98_400, 98_450);

        // Through the doorway it walks on.
        Assert.True(arena.WalkTo(100.5, 97.0));
        for (int i = 0; i < 40; i++)
        {
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, north));
            arena.Tick();
        }
        Assert.True(arena.Simulation.Player.Body.ZMm > 99_200, $"the doorway held the body at z {arena.Simulation.Player.Body.ZMm}");

        // A door hung in it (E6), shut: the body stops short of the leaf as of a wall; opened with E, it walks through.
        Assert.True(arena.WalkTo(100.5, 97.0));
        Assert.Null(Place(arena, Door, 100_500, 99_000, 0));
        var door = PieceAt(arena, Door, 100_500, 99_000);
        zs.Clear();
        for (int i = 0; i < 40; i++)
        {
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, north));
            arena.Tick();
            zs.Add(arena.Simulation.Player.Body.ZMm);
        }
        Assert.All(zs, z => Assert.True(z <= 98_450, $"the body went to z {z}, into the shut door"));
        Assert.InRange(zs[^1], 98_400, 98_450);
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, door.Value)));
        Assert.True(arena.Simulation.Pieces.Single(p => p.Id == door).DoorOpen);
        for (int i = 0; i < 40; i++)
        {
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, north));
            arena.Tick();
        }
        Assert.True(arena.Simulation.Player.Body.ZMm > 99_200, $"the open door held the body at z {arena.Simulation.Player.Body.ZMm}");
    }

    [Fact]
    public void Prediction_EqualsAuthority_AcrossANewWall()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (100.5, 97.0));
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Wall, 100_500, 99_000, 0));
        SaveAndLoad(profile, session, arena, "built");
        Assert.True(session.Load(SaveSlots.Manual("built")).IsComplete);

        var motion = new PlayerMotion(session);
        motion.Resync();
        session.Subscribe<BodyMoved>(motion.OnBodyMoved);
        var north = new MoveIntent(0, MoveIntent.FullDeflection, Gait.Walk, 0);
        Assert.True(motion.Send(north));
        session.Submit(new MoveCommand(session.Simulation!.PlayerId, north));
        session.Frame(session.TickSeconds);
        int blocked = 0;
        for (int frame = 0; frame < 50; frame++)
        {
            // Drawn a whole tick ahead, the body is where the authority then puts it - at the wall too.
            var ahead = motion.Predict(1.0);
            session.Frame(session.TickSeconds);
            var now = session.Simulation!.Player.Body;
            Assert.Equal((ahead.XMm, ahead.ZMm), (now.XMm, now.ZMm));
            if (now.ZMm >= 98_400)
                blocked++;
        }
        Assert.True(blocked > 10, "the character never reached the wall");
        Assert.True(session.Simulation!.Player.Body.ZMm <= 98_450);
        Assert.Equal(0, session.SubscriberFailures);
    }

    [Fact]
    public void Creatures_AreBlockedByPieces()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (102.0, 101.0));
        foreach (long x in new long[] { 100_500, 103_500 })
        {
            Assert.Null(Place(arena, Pad, x, 100_500, 0));
            Assert.Null(Place(arena, Wall, x, 99_000, 0));
        }
        var loaded = SaveAndLoad(profile, session, arena, "walls");

        // A roaming wolf whose route runs north through the wall line, injected after the walls went up.
        var patrol = new SpawnSite("spawn.test.patrol", 101_500, 95_000, 0, ImmutableArray.Create(new SpawnMember(Arena.Wolf, "roamer")))
        {
            Route = ImmutableArray.Create((101_500L, 95_000L), (101_500L, 104_000L)),
        };
        var rules = session.Setup with { Combat = session.Setup.Combat with { Spawns = ImmutableArray.Create(patrol) } };
        var world = Arena.Resume(rules, loaded);
        long radius = session.Setup.Combat.Creatures[Arena.Wolf].RadiusMm;
        var parts = world.Simulation.Pieces.SelectMany(p => p.Parts).Select(p => new BoxBlocker("part", p.MinXMm, p.MinZMm, p.MaxXMm, p.MaxZMm, p.HeightMm)).ToList();
        long furthest = long.MinValue;
        for (int i = 0; i < 1_000; i++)
        {
            world.Tick();
            var wolf = world.Creature().Body;
            Assert.All(parts, part => Assert.Null(part.Separation(wolf.XMm, wolf.ZMm, radius)));
            furthest = Math.Max(furthest, wolf.ZMm);
        }
        // It walked up to the wall and no further.
        Assert.InRange(furthest, 98_800 - radius - 60, 98_800 - radius);
    }

    [Fact]
    public void Aim_StopsAtAPieceWall()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (100.5, 101.0), 270);
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Wall, 99_000, 100_500, 1));

        var (x, _, onCreature) = arena.Simulation.Aim(270_000, 20_000);
        Assert.InRange(x, 99_200, 99_210);
        Assert.False(onCreature);
    }

    [Fact]
    public void Dismantle_RefusesInOrder_AndRefundsHalf()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (102.0, 103.0));
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Pad, 103_500, 100_500, 0));
        Assert.Null(Place(arena, Wall, 100_500, 99_000, 0));   // the south edge: no pad beyond it
        Assert.Null(Place(arena, Wall, 102_000, 100_500, 1));  // between the two pads
        var south = PieceAt(arena, Wall, 100_500, 99_000);
        var between = PieceAt(arena, Wall, 102_000, 100_500);
        var pad = PieceAt(arena, Pad, 100_500, 100_500);
        Assert.Equal(39, Stacks(arena).Sum());
        var removed = arena.Record<PieceRemoved>();
        var changed = arena.Record<StructuresChanged>();

        var stranger = EntityId.Parse("chr_01JZZZZZZZZZZZZZZZZZZZZZZZ");
        Assert.Equal($"unknown actor {stranger}", arena.Submit(new DismantlePieceCommand(stranger, south)));
        Assert.Equal("there is no such piece", Dismantle(arena, EntityId.NewId(EntityKind.Piece)));
        Assert.True(arena.WalkTo(110.5, 102.0));
        string far = Dismantle(arena, south)!;
        Assert.StartsWith("that is ", far);
        Assert.EndsWith(" m away; building reach is 6.00 m", far);
        Assert.True(arena.WalkTo(102.0, 103.0));
        Assert.Equal("take down what stands on it first", Dismantle(arena, pad));
        Assert.Empty(removed);

        // The wall comes down for half its two timber, merged into the fullest partial stack; its identity retires.
        long before = arena.Simulation.StructureRevision;
        Assert.Null(Dismantle(arena, south));
        var gone = Assert.Single(removed);
        Assert.Equal((south, Wall, arena.Player, before + 1), (gone.PieceId, gone.DefId, gone.Actor, gone.Revision));
        Assert.Equal(new[] { new CostView(Timber, 1, 0) }, gone.Refund);
        Assert.Equal(StructureChangeKind.Dismantled, Assert.Single(changed).Kind);
        Assert.Null(arena.Simulation.World.Piece(south));
        Assert.Equal(40, Stacks(arena).Sum());


        // A pad whose only wall has a pad beyond it comes down; half of one timber is nothing.
        Assert.Null(Dismantle(arena, pad));
        Assert.Empty(removed[^1].Refund);
        Assert.Equal(40, Stacks(arena).Sum());
        Assert.Null(Dismantle(arena, between));
        Assert.Equal(41, Stacks(arena).Sum());
        Assert.Equal(before + 3, arena.Simulation.StructureRevision);

        // A chest holding anything comes down last of all (E8): "empty the chest first".
        Assert.Null(Place(arena, Chest, 103_500, 100_500, 0));
        var chest = PieceAt(arena, Chest, 103_500, 100_500);
        string key = arena.Simulation.Pieces.Single(p => p.Id == chest).ContainerKey!;
        Assert.True(arena.WalkTo(103.5, 102.6));
        var one = arena.Simulation.Player.Inventory.First(e => e.DefId == Timber);
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, one.ItemId.Value, ItemPlace.Carried, ItemPlace.In(key), 1)));
        Assert.Equal("empty the chest first", Dismantle(arena, chest));
    }

    [Fact]
    public void ForeignPieces_AreNotYoursToTouch()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (100.5, 97.8));
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Doorway, 100_500, 99_000, 0));
        Assert.Null(Place(arena, Door, 100_500, 99_000, 0));
        Assert.Null(Place(arena, Chest, 100_500, 100_500, 0));
        // And a bench (E9), with Kera sent to work at it; the other character then stands beside her in the forge shed.
        Assert.Null(Place(arena, Pad, 100_500, 103_500, 0));
        Assert.Null(Place(arena, Bench, 100_500, 103_500, 3));
        ErrandTests.ToKera(arena, (97.0, 97.0));
        Assert.Null(ErrandTests.Assign(arena, Kera, arena.Simulation.Pieces.Single(p => p.DefId == Bench).Id));
        var loaded = SaveAndLoad(profile, session, arena, "theirs");

        // The same world, played by another character: the pad is not theirs.
        var r = loaded.Player;
        var other = loaded with
        {
            Player = new PlayerRecord(EntityId.NewId(EntityKind.Character), r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory, r.Progression,
                r.FacingMdeg, r.Discoveries, r.Equipment, r.Currency, r.Effects, r.Relationships, r.Conversations),
        };
        var world = Arena.Resume(session.Setup, other);
        var pad = world.Simulation.Pieces.Single(p => p.DefId == Pad && p.ZMm == 100_500);
        Assert.NotEqual(world.Player, pad.Owner);
        Assert.Equal("that is not yours to take down", Dismantle(world, pad.Id));
        Assert.Equal("that is not yours to mend", world.Submit(new RepairPieceCommand(world.Player, pad.Id)));   // E8
        // Nor is their door (E6): it will not open for this character, standing beside it, nor come down.
        var door = world.Simulation.Pieces.Single(p => p.DefId == Door);
        Assert.Equal("that door is not yours", world.Submit(new InteractCommand(world.Player, door.Id.Value)));
        Assert.False(world.Simulation.Pieces.Single(p => p.Id == door.Id).DoorOpen);
        Assert.Equal("that is not yours to take down", Dismantle(world, door.Id));
        // Nor their chest (E8): nothing goes into it.
        var chest = world.Simulation.Pieces.Single(p => p.DefId == Chest);
        var carried = world.Simulation.Player.Inventory.First();
        Assert.Equal("that chest is not yours", world.Submit(new MoveItemCommand(world.Player, carried.ItemId.Value, ItemPlace.Carried, ItemPlace.In(chest.ContainerKey!), 1)));
        // Nor their bench (E9): no one is asked to work at it, and the one who works there does not work for this character.
        var bench = world.Simulation.Pieces.Single(p => p.DefId == Bench);
        Assert.Equal("that Anvil Bench is not yours", world.Submit(new AssignWorkerCommand(world.Player, Kera, bench.Id)));
        Assert.Equal("Kera Voss does not work for you", world.Submit(new ReleaseWorkerCommand(world.Player, Kera)));
        Assert.Equal(6, world.Simulation.Pieces.Length);
    }

    [Fact]
    public void TheFourCellPad_IsHostedByItsAnchor_AndDigestedOnce()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (102.0, 102.0));
        var cells = new[] { "r_0_0:c_00_00", "r_0_0:c_00_01", "r_0_0:c_01_00", "r_0_0:c_01_01" }.Select(CellKey.Parse).ToArray();
        var before = cells.Select(arena.Simulation.World.EffectiveCellDigest).ToArray();

        // The square (99-102)² meets all four cells; the row lives in one, its anchor's.
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        var pad = Assert.Single(arena.Simulation.Pieces);
        Assert.Equal((99_000L, 99_000L, 102_000L, 102_000L), (pad.MinXMm, pad.MinZMm, pad.MaxXMm, pad.MaxZMm));
        Assert.Equal(new[] { 0, 0, 0, 1 }, cells.Select(c => arena.Simulation.World.PiecesIn(c).Count));
        var after = cells.Select(arena.Simulation.World.EffectiveCellDigest).ToArray();
        Assert.Equal(before[..3], after[..3]);
        Assert.NotEqual(before[3], after[3]);
    }

    [Fact]
    public void ALayoutEditUnderASavedPiece_IsAuditedAndKept()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (88.5, 116.0), 180, timber: new[] { 3 });
        Assert.Null(Place(arena, Pad, 88_500, 112_500, 0));
        Assert.Null(Place(arena, Wall, 87_000, 112_500, 1));
        var rows = arena.Simulation.Pieces.Select(p => (p.Id, p.DefId, p.XMm, p.ZMm, p.Rotation)).ToList();
        var wall = PieceAt(arena, Wall, 87_000, 112_500);
        var loaded = SaveAndLoad(profile, session, arena, "edited");

        // Later content: an authored post under the wall, and walls retuned to 150 health.
        var setup = session.Setup;
        var edited = setup with
        {
            Layout = setup.Layout with
            {
                Space = setup.Layout.Space with { Blockers = setup.Layout.Space.Blockers.Add(new CircleBlocker("post.later", 87_000, 112_500, 400, 2_000)) },
            },
            Building = setup.Building with
            {
                Catalog = new BuildingCatalog(setup.Building.Catalog.Pieces.Values.Select(p => p.Id == Wall ? p with { HealthMax = 150 } : p)),
            },
        };
        var bus = new EventBus();
        var published = new List<object>();
        bus.Subscribe<PiecePlaced>(published.Add);
        bus.Subscribe<PieceRemoved>(published.Add);
        bus.Subscribe<StructuresChanged>(published.Add);
        bus.Subscribe<NavigationRebuilt>(published.Add);
        var simulation = Simulation.Start(edited, loaded.Player, loaded.World, loaded.Manifest.WorldTick, bus);

        Assert.Equal(rows, simulation.Pieces.Select(p => (p.Id, p.DefId, p.XMm, p.ZMm, p.Rotation)));
        Assert.Equal(150, simulation.Pieces.Single(p => p.Id == wall).HealthCurrent);
        Assert.Contains(new StructureConflict(wall.Value, "it stands over post.later"), simulation.StructureAudit);
        Assert.Contains(new StructureConflict(wall.Value, "health 200 is above Timber Wall's 150: clamped"), simulation.StructureAudit);
        Assert.Empty(published);
        // Navigation derives from the changed inputs: the edited layout and the pieces it kept.
        var inputs = NavigationLayout.AuthoredInputs(edited.Layout, edited.Navigation)
            .AddRange(simulation.StructureFootprints.Select(f => new NavInput(NavInputKind.Solid, f.Box(), null)));
        var fresh = NavGrid.Build(edited.Navigation, NavigationLayout.Bounds(edited.Layout), NavigationLayout.TileKeys(edited.Layout), inputs);
        Assert.Equal(fresh.Digest(), simulation.Navigation.Grid.Digest());
    }

    [Fact]
    public void Kera_BehindAPlacedWall_CannotBeTalkedTo_TradedWith_OrAssigned()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // Kera's place moved onto the build area, and NPC places not kept clear, so a wall can stand between her and the character.
        var setup = session.Setup;
        var rules = setup with
        {
            Layout = setup.Layout with { Npcs = setup.Layout.Npcs.Select(n => n.NpcId == Kera ? n with { XMm = 100_500, ZMm = 101_200 } : n).ToImmutableArray() },
            Building = setup.Building with
            {
                Constants = setup.Building.Constants with { Protection = setup.Building.Constants.Protection with { NpcSiteMm = 0 } },
            },
        };
        var arena = Arena.OpenCreatures(session, rules, (100.5, 102.6), 180, None, r => Carrying(r, 20));
        var ware = arena.Simulation.Wares(Kera)!.Wares.First();

        // Within a hand's reach and in clear view: she answers.
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
        Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
        Assert.False(arena.Simulation.Walled(100_500, 102_600, 100_500, 101_200));

        // A wall placed between them, and she is out of reach, as through the smithy's own wall.
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Wall, 100_500, 102_000, 0));
        Assert.True(arena.Simulation.Walled(100_500, 102_600, 100_500, 101_200));
        Assert.Equal("Kera Voss is out of reach", arena.Submit(new TalkCommand(arena.Player, Kera)));
        Assert.Equal("Kera Voss is out of reach", arena.Submit(new BuyCommand(arena.Player, Kera, ware.Ref, 1)));
        // Nor is she asked to work (E9): asking is in person, through the same reach.
        Assert.Null(Place(arena, Pad, 103_500, 103_500, 0));
        Assert.Null(Place(arena, Bench, 103_500, 103_500, 0));
        var bench = arena.Simulation.Pieces.Single(p => p.DefId == Bench).Id;
        Assert.Equal("Kera Voss is out of reach", arena.Submit(new AssignWorkerCommand(arena.Player, Kera, bench)));
    }

    // G9
    [Fact]
    public void CreatureHomes_DoNotDependOnPlacedPieces()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var built = Builder(session, (102.0, 101.0));
        Assert.Null(Place(built, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(built, Wall, 100_500, 99_000, 0));
        var withWall = SaveAndLoad(profile, session, built, "with_wall");
        var bare = SaveAndLoad(profile, session, Builder(session, (102.0, 101.0)), "bare");

        // A spawner injected on the wall after it stood, bypassing check 11: its first home sample lies inside the wall.
        var site = new SpawnSite("spawn.test.homes", 100_500, 99_000, 100, ImmutableArray.Create(new SpawnMember(Arena.Wolf, "sleeper")));
        var rules = session.Setup with { Combat = session.Setup.Combat with { Spawns = ImmutableArray.Create(site) } };
        var a = Arena.Resume(rules, withWall).Simulation.Creatures.Select(c => (c.Key, c.Body)).ToList();
        var b = Arena.Resume(rules, bare).Simulation.Creatures.Select(c => (c.Key, c.Body)).ToList();
        Assert.Single(a);
        Assert.Equal(b, a);

        // And the code says so: Populate reads the authored space, never the built one.
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "World", "Runtime", "Creatures.cs")).Replace("\r\n", "\n");
        int start = source.IndexOf("public void Populate()", StringComparison.Ordinal);
        string body = source[start..source.IndexOf("\n    }\n", start, StringComparison.Ordinal)];
        Assert.Contains("_context.Setup.Layout.Space", body);
        Assert.DoesNotContain("_context.Space", body);
    }

    // G10
    [Fact]
    public void SpaceOrder_IsAFunctionOfThePieceSet()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var set = new (string Def, long X, long Z, int R)[]
        {
            (Pad, 100_500, 100_500, 0), (Pad, 103_500, 100_500, 0), (Wall, 100_500, 99_000, 0), (Wall, 103_500, 99_000, 2),
            (Doorway, 102_000, 100_500, 1), (Wall, 99_000, 100_500, 3), (Roof, 100_500, 100_500, 0), (Door, 102_000, 100_500, 1),
        };
        Arena Build(IEnumerable<(string Def, long X, long Z, int R)> order)
        {
            var arena = Builder(session, (102.0, 102.6));
            foreach (var (def, x, z, r) in order)
                Assert.Null(Place(arena, def, x, z, r));
            return arena;
        }
        var first = Build(set);
        var second = Build(set.Take(2).Reverse().Concat(new[] { set[4], set[7], set[6] }).Concat(set.Skip(2).Take(2).Reverse()).Append(set[5]));

        static (string, long, long, long, long, long, long) Projection(Blocker b) => b switch
        {
            BoxBlocker x => ("box", x.MinXMm, x.MinZMm, x.MaxXMm, x.MaxZMm, x.HeightMm, x.ClearanceMm),
            CircleBlocker c => ("circle", c.CenterXMm, c.CenterZMm, c.RadiusMm, 0, c.HeightMm, c.ClearanceMm),
            _ => throw new ArgumentOutOfRangeException(nameof(b)),
        };
        Assert.Equal(first.Simulation.Space.Blockers.Select(Projection), second.Simulation.Space.Blockers.Select(Projection));
        Assert.Equal(first.Simulation.DynamicBlockers.Select(Projection), second.Simulation.DynamicBlockers.Select(Projection));

        // Each placed part names a piece of the same definition and pose in both worlds.
        (string, long, long, int) Owner(Arena arena, Blocker part)
        {
            var row = arena.Simulation.World.Piece(EntityId.Parse(part.Id.Split('#')[0]))!;
            return (row.DefId, row.XMm, row.ZMm, row.Rotation);
        }
        var placedA = first.Simulation.Space.Blockers.Where(b => b.Id.Contains('#')).ToList();
        var placedB = second.Simulation.Space.Blockers.Where(b => b.Id.Contains('#')).ToList();
        Assert.Equal(5, placedA.Count);
        Assert.Equal(placedA.Select(p => Owner(first, p)), placedB.Select(p => Owner(second, p)));
        // And the door's closed leaf, among the closed doors (E6).
        var leafA = Assert.Single(first.Simulation.DynamicBlockers, b => b.Id.Contains('#'));
        var leafB = Assert.Single(second.Simulation.DynamicBlockers, b => b.Id.Contains('#'));
        Assert.Equal(Owner(first, leafA), Owner(second, leafB));
    }

    // G20 (E5: a placement's spend and a take-down's refund)
    [Fact]
    public void StackCounts_DoNotDependOnItemIdOrder()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var ids = Enumerable.Range(0, 3).Select(_ => EntityId.NewId(EntityKind.Item)).OrderBy(i => i.Value, StringComparer.Ordinal).ToArray();
        Arena With(params int[] counts) => Arena.OpenCreatures(session, session.Setup, (102.0, 102.0), 0, None,
            r => r.WithInventory(r.Inventory.Concat(ids.Zip(counts, (id, n) => new InventoryEntry(id, Timber, n)))));
        var worlds = new[] { With(3, 3, 7), With(7, 3, 3) };

        foreach (var world in worlds)
        {
            Assert.Null(Place(world, Pad, 100_500, 100_500, 0));
            Assert.Null(Place(world, Wall, 100_500, 99_000, 0));
        }
        Assert.Equal(new[] { 3, 7 }, Stacks(worlds[0]));
        Assert.Equal(Stacks(worlds[0]), Stacks(worlds[1]));
        foreach (var world in worlds)
            Assert.Null(Dismantle(world, PieceAt(world, Wall, 100_500, 99_000)));
        Assert.Equal(new[] { 3, 8 }, Stacks(worlds[0]));   // the refund goes to the fullest partial stack, whatever its ID
        Assert.Equal(Stacks(worlds[0]), Stacks(worlds[1]));

        // A chest's stores (E8) merge the same way, whichever stack each came from.
        foreach (var world in worlds)
        {
            Assert.True(world.WalkTo(100.5, 102.6));
            Assert.Null(Place(world, Chest, 100_500, 100_500, 0));
            string key = world.Simulation.Pieces.Single(p => p.DefId == Chest).ContainerKey!;
            Assert.Null(Store(world, key, 1));
            Assert.Null(Store(world, key, 3));
        }
        int[] Stored(Arena world) => world.Simulation.World.Container(world.Simulation.Pieces.Single(p => p.DefId == Chest).ContainerKey!)!.Items
            .Select(i => i.Count).Order().ToArray();
        Assert.Equal(new[] { 4 }, Stored(worlds[0]));
        Assert.Equal(new[] { 5 }, Stacks(worlds[0]));
        Assert.Equal(Stored(worlds[0]), Stored(worlds[1]));

        // And a repair's spend (E8): the wall again, three blows, and the one timber it costs to mend.
        foreach (var world in worlds)
        {
            Assert.Null(Place(world, Wall, 100_500, 99_000, 0));
            Assert.True(world.WalkTo(102.0, 102.6) && world.WalkTo(102.0, 100.3));
            world.TurnTo(180);
            world.Tick();
            Blows(world, 3);
            Assert.Null(world.Submit(new RepairPieceCommand(world.Player, PieceAt(world, Wall, 100_500, 99_000))));
        }
        Assert.Equal(new[] { 2 }, Stacks(worlds[0]));
        Assert.Equal(Stacks(worlds[0]), Stacks(worlds[1]));
        Assert.Equal(Stacks(worlds[0]), Stacks(worlds[1]));
    }


    /// <summary>
    /// The Crossing Workshop's step 1 as E5 builds it (M7 design §4.22, R01-R05, from (102, 102)): four pads over the four-cell corner,
    /// the south doorway across x = 100, seven walls, and four roofs - 16 pieces and 24 timber.
    /// </summary>
    internal static void WorkshopStepOne(Arena arena)
    {
        foreach (var (x, z) in new[] { (100_500L, 100_500L), (103_500L, 100_500L), (100_500L, 103_500L), (103_500L, 103_500L) })
            Assert.Null(Place(arena, Pad, x, z, 0));
        Assert.Null(Place(arena, Doorway, 100_500, 99_000, 0));
        foreach (var (x, z, r) in new[] { (103_500L, 99_000L, 0), (100_500L, 105_000L, 0), (103_500L, 105_000L, 0), (99_000L, 100_500L, 1),
                     (99_000L, 103_500L, 1), (105_000L, 100_500L, 1), (105_000L, 103_500L, 1) })
            Assert.Null(Place(arena, Wall, x, z, r));
        foreach (var (x, z) in new[] { (100_500L, 100_500L), (103_500L, 100_500L), (100_500L, 103_500L), (103_500L, 103_500L) })
            Assert.Null(Place(arena, Roof, x, z, 0));
    }

    [Fact]
    public void EachFootprintChange_SendsExactlyOneRebuild_AndPadsAndRoofsSendNone()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (102.0, 102.6));
        var rebuilt = arena.Record<NavigationRebuilt>();
        string empty = arena.Simulation.Navigation.Grid.Digest();
        long revision = 0;
        void Change(Func<string?> change, string? rebuilds)
        {
            int before = rebuilt.Count;
            Assert.Null(change());
            Assert.Equal(++revision, arena.Simulation.StructureRevision);
            Assert.Equal(before + (rebuilds is null ? 0 : 1), rebuilt.Count);
            if (rebuilds is not null)
                Assert.Equal(rebuilds, rebuilt[^1].Reason);
        }

        Change(() => Place(arena, Pad, 100_500, 100_500, 0), null);
        Change(() => Place(arena, Wall, 100_500, 99_000, 0), "placed");
        // A wall's parts, 3.4 x 0.4 m, and the 600 mm round them: 18 x 6 nodes, in the two tiles south of z = 100.
        Assert.Equal(108, rebuilt[^1].NodesRestamped);
        Assert.Equal(new[] { new NavTileKey(0, 0), new NavTileKey(1, 0) }, rebuilt[^1].Tiles);
        Change(() => Place(arena, Doorway, 102_000, 100_500, 1), "placed");
        Assert.Equal(108, rebuilt[^1].NodesRestamped);   // the union of its jambs
        Change(() => Place(arena, Roof, 100_500, 100_500, 0), null);
        // A door hung (E6) is a change with a leaf; opening and shutting it is none: no rebuild, no sequence, the same grid.
        Change(() => Place(arena, Door, 102_000, 100_500, 1), "placed");
        string hung = arena.Simulation.Navigation.Grid.Digest();
        var door = PieceAt(arena, Door, 102_000, 100_500);
        foreach (bool open in new[] { true, false })
        {
            int before = rebuilt.Count;
            Assert.Null(arena.Submit(new InteractCommand(arena.Player, door.Value)));
            Assert.Equal(open, arena.Simulation.Pieces.Single(p => p.Id == door).DoorOpen);
            Assert.Equal((revision, before, hung), (arena.Simulation.StructureRevision, rebuilt.Count, arena.Simulation.Navigation.Grid.Digest()));
        }
        Change(() => Dismantle(arena, PieceAt(arena, Roof, 100_500, 100_500)), null);
        Change(() => Dismantle(arena, PieceAt(arena, Wall, 100_500, 99_000)), "dismantled");
        Change(() => Dismantle(arena, door), "dismantled");
        Change(() => Dismantle(arena, PieceAt(arena, Doorway, 102_000, 100_500)), "dismantled");
        Change(() => Dismantle(arena, PieceAt(arena, Pad, 100_500, 100_500)), null);
        Assert.All(rebuilt, r => Assert.Equal(arena.Simulation.WorldTick, r.Tick));

        // Blows and mending (E8): damage that leaves a piece standing, and a repair, are no change - no rebuild, no sequence; a piece
        // destroyed is one change, and a doorway destroyed with its door two.
        Change(() => Place(arena, Pad, 100_500, 100_500, 0), null);
        Change(() => Place(arena, Wall, 100_500, 99_000, 0), "placed");
        Change(() => Place(arena, Doorway, 102_000, 100_500, 1), "placed");
        Change(() => Place(arena, Door, 102_000, 100_500, 1), "placed");
        Assert.True(arena.WalkTo(101.2, 102.6) && arena.WalkTo(100.5, 100.0));
        arena.TurnTo(180);
        arena.Tick();
        int rebuilds = rebuilt.Count;
        Blows(arena, 3);
        Assert.Null(arena.Submit(new RepairPieceCommand(arena.Player, PieceAt(arena, Wall, 100_500, 99_000))));
        Assert.Equal((revision, rebuilds), (arena.Simulation.StructureRevision, rebuilt.Count));
        Blows(arena, 20);
        Assert.Equal((++revision, rebuilds + 1, "destroyed"), (arena.Simulation.StructureRevision, rebuilt.Count, rebuilt[^1].Reason));
        Assert.True(arena.WalkTo(101.0, 102.0));
        arena.TurnTo(90);
        arena.Tick();
        Blows(arena, 20);
        revision += 2;
        Assert.Equal((revision, rebuilds + 3), (arena.Simulation.StructureRevision, rebuilt.Count));
        Assert.All(rebuilt.TakeLast(2), r => Assert.Equal("destroyed", r.Reason));
        Change(() => Dismantle(arena, PieceAt(arena, Pad, 100_500, 100_500)), null);

        // Everything gone, the grid is the grid it started as.
        Assert.Equal(empty, arena.Simulation.Navigation.Grid.Digest());
    }

    // F-E7
    [Fact]
    public void Building_RoundTripsThroughSave_AndNavigationDerivesIdentically()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (102.0, 102.0));
        WorkshopStepOne(arena);
        var loaded = Arena.Resume(session.Setup, SaveAndLoad(profile, session, arena, "workshop"));

        static IEnumerable<object> Rows(Arena a) => a.Simulation.World.Pieces.Select(p =>
            (object)(p.InstanceId, p.DefId, p.HostCell, p.XMm, p.ZMm, p.Rotation, p.Owner, p.HealthCurrent, p.DoorOpen));
        Assert.Equal(Rows(arena), Rows(loaded));
        Assert.Equal(16, loaded.Simulation.StructureRevision);
        Assert.Equal(arena.Simulation.Navigation.Grid.Digest(), loaded.Simulation.Navigation.Grid.Digest());
        Assert.Equal(arena.Simulation.Space.Blockers.ToList(), loaded.Simulation.Space.Blockers.ToList());
        Assert.Equal(arena.Simulation.StructureFootprints.ToList(), loaded.Simulation.StructureFootprints.ToList());
        Assert.Equal(arena.Simulation.StateDigest(), loaded.Simulation.StateDigest());
        Assert.Empty(loaded.Simulation.StructureAudit);
    }

    // RK-06
    [Fact]
    public void TwoHundredPieces_RoundTripAndStayNavigable()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (100.5, 94.5), timber: Enumerable.Repeat(20, 12).ToArray());
        var store = new SaveStore(profile.Root);
        void Save(string slot) => store.Save(SaveSlots.Manual(slot), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(),
            session.Content, arena.Simulation.WorldTick, 0));
        Save("before");

        // Walk as a person would: by a plan on the running world's own grid, corner to corner.
        void Go(long x, long z)
        {
            var body = arena.Simulation.Player.Body;
            var nav = arena.Simulation.Navigation;
            var open = nav.Gates.ToDictionary(g => g.Key, g => g.Open, StringComparer.Ordinal);
            var plan = NavSearch.Plan(new NavQuery(nav.Grid, g => g.GateKey is { } key && open.GetValueOrDefault(key), session.Setup.Navigation,
                new NavScratch(), null), new NavAgent(0, true), new NavPoint(body.XMm, body.ZMm), new NavPoint(x, z));
            Assert.True(plan.Outcome == NavOutcome.Found, $"no way from ({body.XMm}, {body.ZMm}) to ({x}, {z}): {plan.Outcome}");
            foreach (var corner in plan.Corners)
                Assert.True(arena.WalkTo(corner.XMm / 1000.0, corner.ZMm / 1000.0), $"never reached ({corner.XMm}, {corner.ZMm})");
        }
        void Put(string def, long x, long z, int r, long standX, long standZ)
        {
            var preview = arena.Simulation.PreviewPlacement(def, x, z, r, checkNavigability: false);
            if (preview.Failed is PlacementRule.Reach or PlacementRule.Bodies)
                Go(standX, standZ);
            string? refused = Place(arena, def, x, z, r);
            Assert.True(refused is null, $"{def} at ({x}, {z}) r{r}: {refused}");
        }
        var centres = Enumerable.Range(0, 9).Select(i => 88_500L + 3_000 * i).ToArray();

        // 81 pads, standing on each square.
        foreach (long z in centres)
        {
            foreach (long x in centres)
                Put(Pad, x, z, 0, x, z);
        }
        // 37 walls and a doorway: east-west lines on z = 93, 102 and 111, the doorway in the middle one; a line on x = 99; two walls on
        // x = 105 either side of z = 102. Each strip stays open at an edge of the area. Stand a square's width to one side.
        foreach (long line in new long[] { 93_000, 102_000, 111_000 })
        {
            foreach (long x in centres)
                Put(line == 102_000 && x == 100_500 ? Doorway : Wall, x, line, 0, x, line - 1_500);
        }
        foreach (long z in centres)
            Put(Wall, 99_000, z, 1, 97_500, z);
        Put(Wall, 105_000, 100_500, 1, 103_500, 100_500);
        Put(Wall, 105_000, 103_500, 1, 103_500, 103_500);
        // 81 roofs: the rows with a wall beneath them first, then the rows beside them.
        var roofs = centres.SelectMany(z => centres.Select(x => (X: x, Z: z)))
            .OrderBy(s => s.Z is 91_500 or 94_500 or 100_500 or 103_500 or 109_500 or 112_500 || s.X is 97_500 or 100_500 ? 0 : 1).ToList();
        foreach (var (x, z) in roofs)
            Put(Roof, x, z, 0, x, z);
        Assert.Equal(200, arena.Simulation.Pieces.Length);
        Assert.Equal(200, arena.Simulation.StructureRevision);
        Save("after");

        // Loaded under content whose hash differs, so the definition pass and its copies run.
        using var copy = new TempProfile();
        string content = Path.Combine(copy.Root, "content");
        CopyDirectory(Path.Combine(RepoRoot(), "content"), content);
        string config = Path.Combine(content, "config", "building.yaml");
        File.WriteAllText(config, File.ReadAllText(config).Replace("notes: Building v1 (M7).", "notes: Building v1 (M7), retold."));
        using var other = new TempProfile();
        var edited = GameSession.Boot(new GameOptions(content, other.Root));
        Assert.NotEqual(session.Content.Hash, edited.Content.Hash);
        var reloaded = Arena.Resume(edited.Setup, store.Load(SaveSlots.Manual("after"), new LoadContext(edited.Generator, edited.Content, new Registry())));

        static IEnumerable<object> Rows(Arena a) => a.Simulation.World.Pieces.Select(p =>
            (object)(p.InstanceId, p.DefId, p.HostCell, p.XMm, p.ZMm, p.Rotation, p.Owner, p.HealthCurrent, p.DoorOpen));
        Assert.Equal(Rows(arena), Rows(reloaded));
        Assert.Equal(200, reloaded.Simulation.StructureRevision);
        Assert.Equal(arena.Simulation.Navigation.Grid.Digest(), reloaded.Simulation.Navigation.Grid.Digest());
        long Size(string slot) => new FileInfo(Directory.EnumerateFiles(profile.Root, "entities.msgpack", SearchOption.AllDirectories)
            .Single(f => f.Contains(Path.DirectorySeparatorChar + SaveSlots.Manual(slot) + Path.DirectorySeparatorChar, StringComparison.Ordinal))).Length;
        long grown = Size("after") - Size("before");
        Assert.True(grown <= 200 * 250, $"200 pieces took {grown} bytes");

        // Through the middle line's doorway, before and after: found, and the same corners.
        NavPlan Through(Arena a)
        {
            var nav = a.Simulation.Navigation;
            return NavSearch.Plan(new NavQuery(nav.Grid, _ => true, session.Setup.Navigation, new NavScratch(), null), new NavAgent(0, true),
                new NavPoint(100_500, 100_500), new NavPoint(100_500, 103_500));
        }
        var before = Through(arena);
        var after = Through(reloaded);
        Assert.Equal(NavOutcome.Found, before.Outcome);
        Assert.Equal(before.Corners.ToArray(), after.Corners.ToArray());
        Assert.All(before.Corners, c => Assert.InRange(c.XMm, 100_050, 100_950));
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "UNNAMED.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found");
    }
}
