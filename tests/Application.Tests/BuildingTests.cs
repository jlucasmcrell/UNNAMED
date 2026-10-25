using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Building v1 over the game's own content (M7 design §4, re-ported by WP-1G): each piece placed with its exact spend, events, view and
/// ID; each placement rule refusing with its reason and leaving the digest; the ghost answering as the command does; walls and doorways
/// blocking and passing bodies and aim; blows, mending and spills; taking down; host cells; navigation's restamps; and guards G10 and
/// G20 (building's own spends). M7's tests, on M7's crossing area moved with the hollow: every coordinate below is M7's own, local to
/// <see cref="Origin"/>.
/// </summary>
public class BuildingTests
{
    internal const string Pad = "piece.pad.timber", Wall = "piece.wall.timber", Doorway = "piece.doorway.timber", Roof = "piece.roof.timber",
        Door = "piece.door.timber", Chest = "piece.storage.chest", Bench = "piece.station.anvil";
    internal const string Timber = "item.material.timber";
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Tag = "unnamed.piece/v1";
    private static readonly (string, double, double, string)[] None = Array.Empty<(string, double, double, string)>();

    /// <summary>Where M7's coordinates are measured from: the hollow's corner less a metre in x, the building lattice being absolute.</summary>
    internal const long Ox = 399_000, Oz = 600_000;
    internal static readonly (double X, double Z) Origin = (Ox / 1000.0, Oz / 1000.0);

    private static PlayerRecord Carrying(PlayerRecord r, params int[] stacks) =>
        r.WithInventory(r.Inventory.Concat(stacks.Select(n => Arena.Stack(Timber, n))));

    /// <summary>An arena whose metres are M7's.</summary>
    internal static Arena OpenAt(GameSession session, SimulationSetup rules, (double X, double Z) at, int facingDeg,
        IEnumerable<(string DefId, double X, double Z, string Role)> creatures, Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, rules, at, facingDeg, creatures, change, origin: Origin);

    /// <summary>A builder at a place with the workshop's 45 timber (20, 20 and 5), or the stacks given.</summary>
    internal static Arena Builder(GameSession session, (double X, double Z) at, int facingDeg = 0, SimulationSetup? rules = null, int[]? timber = null) =>
        OpenAt(session, rules ?? session.Setup, at, facingDeg, None, r => Carrying(r, timber ?? new[] { 20, 20, 5 }));

    internal static string? Place(Arena arena, string def, long x, long z, int r) => arena.Submit(new PlacePieceCommand(arena.Player, def, x + Ox, z + Oz, r));

    internal static PlacementPreview Preview(Arena arena, string def, long x, long z, int r, bool checkNavigability = true) =>
        arena.Simulation.PreviewPlacement(def, x + Ox, z + Oz, r, checkNavigability);

    /// <summary>A piece's view in M7's coordinates.</summary>
    internal static PieceView Local(PieceView p) => p with
    {
        XMm = p.XMm - Ox, ZMm = p.ZMm - Oz, MinXMm = p.MinXMm - Ox, MinZMm = p.MinZMm - Oz, MaxXMm = p.MaxXMm - Ox, MaxZMm = p.MaxZMm - Oz,
        Parts = p.Parts.Select(q => q with { MinXMm = q.MinXMm - Ox, MinZMm = q.MinZMm - Oz, MaxXMm = q.MaxXMm - Ox, MaxZMm = q.MaxZMm - Oz })
            .ToImmutableArray(),
    };

    internal static ImmutableArray<PieceView> Pieces(Arena arena) => arena.Simulation.Pieces.Select(Local).ToImmutableArray();

    /// <summary>A pad at (106.5, 100.5) and three walls round it, then the fourth that would close it: reached from (102, 102).</summary>
    private static readonly (string Def, long X, long Z, int R)[] Hut =
    {
        (Pad, 106_500, 100_500, 0), (Wall, 106_500, 99_000, 0), (Wall, 106_500, 102_000, 0), (Wall, 105_000, 100_500, 1), (Wall, 108_000, 100_500, 1),
    };

    internal static string? Dismantle(Arena arena, EntityId piece) => arena.Submit(new DismantlePieceCommand(arena.Player, piece));

    internal static int[] Stacks(Arena arena) =>
        arena.Simulation.Player.Inventory.Where(e => e.DefId == Timber).Select(e => e.Count).Order().ToArray();

    internal static EntityId PieceAt(Arena arena, string def, long x, long z) =>
        Pieces(arena).Single(p => p.DefId == def && p.XMm == x && p.ZMm == z).Id;

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
                Space = layout.Space with { Blockers = layout.Space.Blockers.Add(new CircleBlocker("post.test", 97_500 + Ox, 103_500 + Oz, 400, 2_000)) },
                Npcs = layout.Npcs.Select(n => n.NpcId == Renn ? n with { XMm = 97_500 + Ox, ZMm = 97_500 + Oz } : n).ToImmutableArray(),
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
        int authored = arena.Simulation.ActiveSpace.Blockers.Length;
        var id = Enumerable.Range(1, 8).Select(n => EntityId.Derived(EntityKind.Piece, n, Tag, arena.Player.Value)).ToArray();

        // A pad: one timber, from the smallest stack; no parts, no footprint, nothing in the collision space.
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Equal(new[] { 4, 20, 20 }, Stacks(arena));
        var pad = Assert.Single(Pieces(arena));
        Assert.Equal((id[0], Pad, PieceFamily.Pad, 100_500L, 100_500L, 0, arena.Player, 200, 200, false), (pad.Id, pad.DefId, pad.Family, pad.XMm, pad.ZMm, pad.Rotation,
            pad.Owner, pad.HealthCurrent, pad.HealthMax, pad.DoorOpen));
        Assert.Equal((99_000L, 99_000L, 102_000L, 102_000L), (pad.MinXMm, pad.MinZMm, pad.MaxXMm, pad.MaxZMm));
        Assert.Empty(pad.Parts);
        Assert.Empty(arena.Simulation.StructureFootprints);
        Assert.Equal(authored, arena.Simulation.ActiveSpace.Blockers.Length);

        // A wall on its south edge: two timber; one solid part.
        Assert.Null(Place(arena, Wall, 100_500, 99_000, 0));
        Assert.Equal(new[] { 2, 20, 20 }, Stacks(arena));
        var wall = Pieces(arena).Single(p => p.Id == id[1]);
        Assert.Equal(new[] { new PiecePartView(98_800, 98_800, 102_200, 99_200, 3_000, TraversalClass.Solid) }, wall.Parts);

        // A doorway on its west edge, turned: two timber; two jambs round a 1.6 m opening.
        Assert.Null(Place(arena, Doorway, 99_000, 100_500, 1));
        Assert.Equal(new[] { 20, 20 }, Stacks(arena));
        var doorway = Pieces(arena).Single(p => p.Id == id[2]);
        Assert.Equal(new[] { new PiecePartView(98_800, 101_300, 99_200, 102_200, 3_000, TraversalClass.Solid),
            new PiecePartView(98_800, 98_800, 99_200, 99_700, 3_000, TraversalClass.Solid) }, doorway.Parts);

        // A roof over the pad, on the wall: one timber; no parts.
        Assert.Null(Place(arena, Roof, 100_500, 100_500, 0));
        Assert.Equal(new[] { 19, 20 }, Stacks(arena));
        var roof = Pieces(arena).Single(p => p.Id == id[3]);
        Assert.Equal((PieceFamily.Roof, 100, 100), (roof.Family, roof.HealthCurrent, roof.HealthMax));
        Assert.Empty(roof.Parts);

        // A door in the doorway (E6): one timber; one leaf across the opening, hung shut - a closed door, not part of the space.
        Assert.Null(Place(arena, Door, 99_000, 100_500, 1));
        Assert.Equal(new[] { 18, 20 }, Stacks(arena));
        var door = Pieces(arena).Single(p => p.Id == id[4]);
        Assert.Equal((PieceFamily.Door, 120, 120, false), (door.Family, door.HealthCurrent, door.HealthMax, door.DoorOpen));
        Assert.Equal(new[] { new PiecePartView(98_800, 99_700, 99_200, 101_300, 2_400, TraversalClass.Door) }, door.Parts);
        Assert.Contains(arena.Simulation.DynamicBlockers, b => b is BoxBlocker { MinXMm: 98_800 + Ox, MinZMm: 99_700 + Oz, MaxXMm: 99_200 + Ox, MaxZMm: 101_300 + Oz });

        // A chest on the pad (E8): two timber; one solid part against the square's north side; its container keyed by the piece, its site
        // inside its box, and its record made empty as it is placed (WP-1G: an empty loot table is no baseline).
        Assert.Null(Place(arena, Chest, 100_500, 100_500, 0));
        Assert.Equal(new[] { 16, 20 }, Stacks(arena));
        var chest = Pieces(arena).Single(p => p.Id == id[5]);
        Assert.Equal(new[] { new PiecePartView(100_000, 101_100, 101_000, 101_700, 700, TraversalClass.Solid) }, chest.Parts);
        Assert.Equal("container." + id[5].Value.ToLowerInvariant(), chest.ContainerKey);
        var site = arena.Simulation.Containers.Single(c => c.Site.Key == chest.ContainerKey).Site;
        Assert.Equal((100_500L, 101_400L, 12, ""), (site.XMm - Ox, site.ZMm - Oz, site.StackSlots, site.LootTableId));
        Assert.Empty(arena.Simulation.World.Container(chest.ContainerKey!)!.Items);

        // A bench on a second pad (E8): four timber; one solid part against the north side; its station keyed by the piece.
        Assert.Null(Place(arena, Pad, 103_500, 100_500, 0));
        Assert.Null(Place(arena, Bench, 103_500, 100_500, 0));
        Assert.Equal(new[] { 11, 20 }, Stacks(arena));
        var bench = Pieces(arena).Single(p => p.Id == id[7]);
        Assert.Equal(new[] { new PiecePartView(103_000, 100_900, 104_000, 101_500, 900, TraversalClass.Solid) }, bench.Parts);
        Assert.Equal((PieceFamily.Station, 300, 300, (string?)null), (bench.Family, bench.HealthCurrent, bench.HealthMax, bench.ContainerKey));
        Assert.Equal("station." + id[7].Value.ToLowerInvariant(), bench.StationKey);

        // Derived IDs by sequence; one event of each per piece; the rows hosted by their anchors.
        Assert.Equal(id, placed.Select(p => p.PieceId));
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 8 }, placed.Select(p => p.Revision));
        Assert.Equal(new[] { Pad, Wall, Doorway, Roof, Door, Chest, Pad, Bench }, placed.Select(p => p.DefId));
        Assert.All(placed, p => Assert.Equal(arena.Player, p.Owner));
        Assert.Equal(new[] { (99_000L, 99_000L, 102_000L, 102_000L), (98_800L, 98_800L, 102_200L, 99_200L), (98_800L, 98_800L, 99_200L, 102_200L),
            (99_000L, 99_000L, 102_000L, 102_000L), (98_800L, 99_700L, 99_200L, 101_300L), (100_000L, 101_100L, 101_000L, 101_700L),
            (102_000L, 99_000L, 105_000L, 102_000L), (103_000L, 100_900L, 104_000L, 101_500L) },
            changed.Select(c => (c.MinXMm - Ox, c.MinZMm - Oz, c.MaxXMm - Ox, c.MaxZMm - Oz)));
        Assert.All(changed, c => Assert.Equal(StructureChangeKind.Placed, c.Kind));
        Assert.Equal(8, arena.Simulation.StructureRevision);
        Assert.Equal(new[] { "r_0_0:c_04_07", "r_0_0:c_04_06", "r_0_0:c_04_07", "r_0_0:c_04_07", "r_0_0:c_04_07", "r_0_0:c_04_07", "r_0_0:c_05_07",
                "r_0_0:c_05_07" },
            id.Select(i => arena.Simulation.World.Piece(i)!.HostCell));

        // Space appends exactly the solid parts, in structure order, after the authored blockers; the footprints are those parts and the
        // door's leaf.
        var footprints = arena.Simulation.StructureFootprints;
        Assert.Equal(6, footprints.Length);
        Assert.Equal(5, footprints.Count(f => f.Class == TraversalClass.Solid));
        Assert.Equal(id[4], Assert.Single(footprints, f => f.Class == TraversalClass.Door).PieceId);
        Assert.Equal(footprints.Where(f => f.Class == TraversalClass.Solid).Select(f => (f.MinXMm, f.MinZMm, f.MaxXMm, f.MaxZMm, f.HeightMm)),
            arena.Simulation.ActiveSpace.Blockers.Skip(authored).Cast<BoxBlocker>().Select(b => (b.MinXMm, b.MinZMm, b.MaxXMm, b.MaxZMm, b.HeightMm)));
        Assert.Equal(authored + 5, arena.Simulation.ActiveSpace.Blockers.Length);
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
        PlacePieceCommand P(string def, long x, long z, int r) => new(arena.Player, def, x + Ox, z + Oz, r);

        Refused($"unknown actor {EntityId.Parse("chr_01JZZZZZZZZZZZZZZZZZZZZZZZ")}", new PlacePieceCommand(EntityId.Parse("chr_01JZZZZZZZZZZZZZZZZZZZZZZZ"), Pad, 100_500 + Ox, 100_500 + Oz, 0));
        Refused("piece.nothing is not a piece this build knows", P("piece.nothing", 100_500, 100_500, 0));
        Refused("Timber Pad cannot be turned that way", P(Pad, 100_500, 100_500, 4));
        Refused("that is not on the building grid", P(Pad, 100_000, 100_500, 0));
        Refused("you may only build inside a build area", P(Pad, 85_500, 100_500, 0));
        Refused("that is 12.73 m away; building reach is 6.00 m", P(Pad, 112_500, 112_500, 0));
        Refused("a wall needs a floor pad on one side", P(Wall, 100_500, 99_000, 0));
        Refused("a roof needs a wall under one of its edges, or a roofed neighbour that has one", P(Roof, 100_500, 100_500, 0));
        Refused("the ground here is too uneven for a pad: 0.12 m of rise, 0.10 m allowed", P(Pad, 103_500, 100_500, 0));   // edited limit
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
            var preview = Preview(arena, def, x, z, r);
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
            var preview = Preview(hut, def, x, z, r);
            string? refused = Place(hut, def, x, z, r);
            Assert.True(preview.Allowed == (refused is null), $"{def} at ({x}, {z}) r{r}: the ghost says {preview.Allowed}, the command {refused ?? "yes"}");
            Assert.Equal(refused, preview.Reason);
            Assert.Equal(preview.Allowed ? (def == Pad ? NavVerdict.NotApplicable : NavVerdict.Proven) : NavVerdict.Refused, preview.Navigability);
            if (preview.Failed is { } rule)
                failed.Add(rule);
        }
        Assert.Equal(Enum.GetValues<PlacementRule>().Where(r => r != PlacementRule.Actor), failed.Order());
    }

    /// <summary>
    /// The C1 boundary (M7 design §4.13): a placed chest's record is made, empty, under the identity its piece derives; emptied, the
    /// record stays - the corpse clause never matches a piece chest's key - so the next store finds the same identity, with no exception.
    /// </summary>
    [Fact]
    public void APieceChest_EmptiedAndRefilled_KeepsOneIdentity()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, key) = WithAChest(session);
        var chest = Pieces(arena).Single(p => p.DefId == Chest).Id;
        var derived = EntityId.Derived(EntityKind.Container, chest.Timestamp, "unnamed.piece-container/v1", chest.Value);
        Assert.Equal(derived, arena.Simulation.World.Container(key)!.InstanceId);   // made, empty, as the chest was placed

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

    /// <summary>A chest holding anything cannot be taken down; emptied, it can, and its record goes with it.</summary>
    [Fact]
    public void AChestWithItems_CannotBeTakenDown()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, key) = WithAChest(session);
        var chest = Pieces(arena).Single(p => p.DefId == Chest).Id;
        Assert.Null(Store(arena, key, 2));
        string digest = arena.Simulation.StateDigest();
        Assert.Equal("empty the chest first", Dismantle(arena, chest));
        Assert.Equal(digest, arena.Simulation.StateDigest());
        Assert.Null(arena.Submit(new TakeAllCommand(arena.Player, key)));
        Assert.Null(Dismantle(arena, chest));
        Assert.Null(arena.Simulation.World.Container(key));
        Assert.DoesNotContain(arena.Simulation.Containers, c => c.Site.Key == key);
    }

    /// <summary>The station piece (M7 design §4.14): a bench is an anvil for the fitting wedge while it stands, and not once it is taken down.</summary>
    [Fact]
    public void APieceAnvil_Crafts_UntilTakenDown()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const string Haft = "item.material.ash_haft", Wedge = "recipe.smithing.fitting_wedge";
        var arena = OpenAt(session, session.Setup, (100.5, 102.6), 0, None, r =>
            Carrying(r.WithInventory(r.Inventory.Concat(new[] { Arena.Stack(Haft, 2) })), 20).WithProgression(r.Progression with { Known = r.Progression.Known.SetItem(Wedge, new KnownTechnique(LearningSource.Teacher, "npc.ashen_hollow.kera_voss", 0)) }));
        int Carried(string def) => arena.Simulation.Player.Inventory.Where(e => e.DefId == def).Sum(e => e.Count);

        Assert.Equal("no anvil in reach", arena.Submit(new CraftCommand(arena.Player, Wedge)));
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Bench, 100_500, 100_500, 0));
        var bench = Pieces(arena).Single(p => p.DefId == Bench);
        Assert.Null(arena.Submit(new CraftCommand(arena.Player, Wedge)));
        arena.Tick(200);
        Assert.Equal((1, 1), (Carried(Haft), Carried("item.tool.fitting_wedge")));

        Assert.Null(Dismantle(arena, bench.Id));
        Assert.Equal("no anvil in reach", arena.Submit(new CraftCommand(arena.Player, Wedge)));
        Assert.Equal(1, Carried(Haft));
    }

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
        var arena = OpenAt(session, session.Setup, (100.5, 100.0), 180, None, r =>
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
        Assert.InRange(Assert.Single(shots).ToZMm - Oz, 99_200, 99_210);
        // The impulse bolt likewise.
        Assert.Null(arena.Submit(new CastCommand(arena.Player, Impulse)));
        arena.Tick(80);
        Assert.Single(damaged);
        Assert.Equal(190, Health(arena, wall));
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
                Space = layout.Space with { Blockers = layout.Space.Blockers.Add(new BoxBlocker("wall.test", 98_400 + Ox, 100_000 + Oz, 98_800 + Ox, 101_000 + Oz, 1_000)) },
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
        Assert.Equal(Pad, Assert.Single(Pieces(arena)).DefId);
        Assert.Equal(stacks, Stacks(arena));   // nothing back
        Assert.Null(arena.Simulation.World.Piece(door));
        Assert.Null(arena.Simulation.World.Piece(doorway));
        Assert.DoesNotContain(arena.Simulation.ActiveSpace.Blockers, b => b.Id.StartsWith(doorway.Value, StringComparison.Ordinal));
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
        var arena = OpenAt(session, session.Setup, (100.5, 102.6), 180, None,
            r => Carrying(r.WithInventory(r.Inventory.Add(Arena.Stack(Ingot, 1))), 20));
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Chest, 100_500, 100_500, 0));
        var chest = PieceAt(arena, Chest, 100_500, 100_500);
        string key = Pieces(arena).Single(p => p.Id == chest).ContainerKey!;
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
            spilled.Select(s => (s.Id, s.DefId, s.Count, s.XMm - Ox, s.ZMm - Oz)).Order());

        // They are ordinary ground stacks: picked up, the timber joins the carried stack and the ingot keeps its identity.
        foreach (var item in held)
            Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, item.ItemId.Value, ItemPlace.Ground, ItemPlace.Carried, item.Count)));
        Assert.Equal(new[] { 17 }, Stacks(arena));
        Assert.Equal(held.Single(h => h.DefId == Ingot).ItemId, arena.Simulation.Player.Inventory.Single(e => e.DefId == Ingot).ItemId);
        Assert.DoesNotContain(arena.Simulation.WorldItems, i => held.Any(h => h.ItemId == i.Id));
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
        var preview = Preview(arena, Wall, lastX, lastZ, lastR);
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
            zs.Add((arena.Simulation.Player.Body.ZMm - Oz));
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
        Assert.True((arena.Simulation.Player.Body.ZMm - Oz) > 99_200, $"the doorway held the body at z {(arena.Simulation.Player.Body.ZMm - Oz)}");

        // A door hung in it (E6), shut: the body stops short of the leaf as of a wall; opened with E, it walks through.
        Assert.True(arena.WalkTo(100.5, 97.0));
        Assert.Null(Place(arena, Door, 100_500, 99_000, 0));
        var door = PieceAt(arena, Door, 100_500, 99_000);
        zs.Clear();
        for (int i = 0; i < 40; i++)
        {
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, north));
            arena.Tick();
            zs.Add((arena.Simulation.Player.Body.ZMm - Oz));
        }
        Assert.All(zs, z => Assert.True(z <= 98_450, $"the body went to z {z}, into the shut door"));
        Assert.InRange(zs[^1], 98_400, 98_450);
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, door.Value)));
        Assert.True(Pieces(arena).Single(p => p.Id == door).DoorOpen);
        for (int i = 0; i < 40; i++)
        {
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, north));
            arena.Tick();
        }
        Assert.True((arena.Simulation.Player.Body.ZMm - Oz) > 99_200, $"the open door held the body at z {(arena.Simulation.Player.Body.ZMm - Oz)}");
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
        Assert.InRange(x - Ox, 99_200, 99_210);
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
        string key = Pieces(arena).Single(p => p.Id == chest).ContainerKey!;
        Assert.True(arena.WalkTo(103.5, 102.6));
        var one = arena.Simulation.Player.Inventory.First(e => e.DefId == Timber);
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, one.ItemId.Value, ItemPlace.Carried, ItemPlace.In(key), 1)));
        Assert.Equal("empty the chest first", Dismantle(arena, chest));
    }

    [Fact]
    public void TheFourCellPad_IsHostedByItsAnchor_AndDigestedOnce()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (102.0, 102.0));
        var cells = new[] { "r_0_0:c_04_06", "r_0_0:c_05_06", "r_0_0:c_05_07", "r_0_0:c_04_07" }.Select(CellKey.Parse).ToArray();
        var before = cells.Select(arena.Simulation.World.EffectiveCellDigest).ToArray();

        // The square (99-102)² - (498-501, 699-702) in the region - meets all four cells; the row lives in one, its anchor's.
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        var pad = Assert.Single(Pieces(arena));
        Assert.Equal((99_000L, 99_000L, 102_000L, 102_000L), (pad.MinXMm, pad.MinZMm, pad.MaxXMm, pad.MaxZMm));
        Assert.Equal(new[] { 0, 0, 0, 1 }, cells.Select(c => arena.Simulation.World.PiecesIn(c).Count));
        var after = cells.Select(arena.Simulation.World.EffectiveCellDigest).ToArray();
        Assert.Equal(before[..3], after[..3]);
        Assert.NotEqual(before[3], after[3]);
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
            PolygonBlocker h => ("hull", h.CenterXMm, h.CenterZMm, h.Corners.Length, 0, h.HeightMm, h.ClearanceMm),
            _ => throw new ArgumentOutOfRangeException(nameof(b)),
        };
        Assert.Equal(first.Simulation.ActiveSpace.Blockers.Select(Projection), second.Simulation.ActiveSpace.Blockers.Select(Projection));
        Assert.Equal(first.Simulation.DynamicBlockers.Select(Projection), second.Simulation.DynamicBlockers.Select(Projection));

        // Each placed part names a piece of the same definition and pose in both worlds.
        (string, long, long, int) Owner(Arena arena, Blocker part)
        {
            var row = arena.Simulation.World.Piece(EntityId.Parse(part.Id.Split('#')[0]))!;
            return (row.DefId, row.XMm, row.ZMm, row.Rotation);
        }
        var placedA = first.Simulation.ActiveSpace.Blockers.Where(b => b.Id.Contains('#')).ToList();
        var placedB = second.Simulation.ActiveSpace.Blockers.Where(b => b.Id.Contains('#')).ToList();
        Assert.Equal(5, placedA.Count);
        Assert.Equal(placedA.Select(p => Owner(first, p)), placedB.Select(p => Owner(second, p)));
        // And the door's closed leaf, among the closed doors (E6).
        var leafA = Assert.Single(first.Simulation.DynamicBlockers, b => b.Id.Contains('#'));
        var leafB = Assert.Single(second.Simulation.DynamicBlockers, b => b.Id.Contains('#'));
        Assert.Equal(Owner(first, leafA), Owner(second, leafB));
    }

    // G20 for building's own spends (E5, E8): a placement's and a repair's. A take-down's refund and a chest's store join carried stacks by
    // the inventory's own rule (GrantItem, MoveItemCommand), which is not building's: there only the totals are compared.
    [Fact]
    public void StackCounts_DoNotDependOnItemIdOrder()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var ids = Enumerable.Range(0, 3).Select(_ => EntityId.NewId(EntityKind.Item)).OrderBy(i => i.Value, StringComparer.Ordinal).ToArray();
        Arena With(params int[] counts) => OpenAt(session, session.Setup, (102.0, 102.0), 0, None,
            r => r.WithInventory(r.Inventory.Concat(ids.Zip(counts, (id, n) => new InventoryEntry(id, Timber, n)))));
        var worlds = new[] { With(3, 3, 7), With(7, 3, 3) };

        // The spend comes from the smallest stacks first, whatever their IDs.
        foreach (var world in worlds)
        {
            Assert.Null(Place(world, Pad, 100_500, 100_500, 0));
            Assert.Null(Place(world, Wall, 100_500, 99_000, 0));
        }
        Assert.Equal(new[] { 3, 7 }, Stacks(worlds[0]));
        Assert.Equal(Stacks(worlds[0]), Stacks(worlds[1]));

        // And a repair's: three blows, and the one timber it costs to mend.
        foreach (var world in worlds)
        {
            Assert.True(world.WalkTo(102.0, 100.3));
            world.TurnTo(180);
            world.Tick();
            Blows(world, 3);
            Assert.Null(world.Submit(new RepairPieceCommand(world.Player, PieceAt(world, Wall, 100_500, 99_000))));
        }
        Assert.Equal(new[] { 2, 7 }, Stacks(worlds[0]));
        Assert.Equal(Stacks(worlds[0]), Stacks(worlds[1]));

        // The refund: one timber back, into a stack the inventory chooses.
        foreach (var world in worlds)
            Assert.Null(Dismantle(world, PieceAt(world, Wall, 100_500, 99_000)));
        Assert.All(worlds, w => Assert.Equal(10, Stacks(w).Sum()));
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
        // A wall's parts, 3.4 x 0.4 m, and the 600 mm round them: 18 x 6 nodes, in the two tiles south of the region's z = 700.
        Assert.Equal(108, rebuilt[^1].NodesRestamped);
        Assert.Equal(new[] { "r_0_0:c_04_06", "r_0_0:c_05_06" }.Select(c => NavigationLayout.TileOf(CellKey.Parse(c))), rebuilt[^1].Tiles);
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
            Assert.Equal(open, Pieces(arena).Single(p => p.Id == door).DoorOpen);
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

    /// <summary>A chest at (100.5, 100.5) on its pad, reached from (100.5, 102.6), and its container's key.</summary>
    private static (Arena Arena, string Key) WithAChest(GameSession session)
    {
        var arena = Builder(session, (100.5, 102.6));
        Assert.Null(Place(arena, Pad, 100_500, 100_500, 0));
        Assert.Null(Place(arena, Chest, 100_500, 100_500, 0));
        return (arena, Pieces(arena).Single(p => p.DefId == Chest).ContainerKey!);
    }

    private static string? Store(Arena arena, string key, int count)
    {
        var stack = arena.Simulation.Player.Inventory.Where(e => e.DefId == Timber).OrderBy(e => e.Count).First(e => e.Count >= count);
        return arena.Submit(new MoveItemCommand(arena.Player, stack.ItemId.Value, ItemPlace.Carried, ItemPlace.In(key), count));
    }

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

    private static int Health(Arena arena, EntityId piece) => Pieces(arena).Single(p => p.Id == piece).HealthCurrent;

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
}
