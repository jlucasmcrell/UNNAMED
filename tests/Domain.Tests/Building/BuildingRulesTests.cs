using System.Collections.Immutable;
using UNNAMED.Content;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Tests.Building;

/// <summary>
/// Building's pure rules (M7 design §4.2-4.8): the lattice and its slot keys, quarter turns, sockets after a turn, snapping, the relief
/// of the shipped squares, roof support at depth one, and integer overlap.
/// </summary>
public class BuildingRulesTests
{
    // The greybox catalogue of §4.21, local millimetres at turn 0.
    private static PieceDefinition Piece(string id, PieceFamily family, BoundsMm bounds, IEnumerable<PiecePart> parts, IEnumerable<PieceSocket> sockets,
        int cost, int health, bool supportsRoof = false) =>
        new(id, id, family, PieceDefinition.SlotOf(family), ImmutableArray.Create(0, 1, 2, 3), bounds, parts.ToImmutableArray(), sockets.ToImmutableArray(),
            ImmutableArray.Create(new PieceCost("item.material.timber", cost)), health, supportsRoof);

    private static readonly BoundsMm Square = new(-1500, -1500, 1500, 1500);
    private static readonly BoundsMm WallBox = new(-1700, -200, 1700, 200);

    private static readonly PieceDefinition Pad = Piece("piece.pad.timber", PieceFamily.Pad, Square, Array.Empty<PiecePart>(), new[]
    {
        new PieceSocket(SocketType.Edge, 0, 1500, SocketAxis.X), new PieceSocket(SocketType.Edge, 0, -1500, SocketAxis.X),
        new PieceSocket(SocketType.Edge, 1500, 0, SocketAxis.Z), new PieceSocket(SocketType.Edge, -1500, 0, SocketAxis.Z),
        new PieceSocket(SocketType.Square, 0, 0, SocketAxis.None),
    }, 1, 200);

    private static readonly PieceDefinition Wall = Piece("piece.wall.timber", PieceFamily.Wall, WallBox,
        new[] { new PiecePart(WallBox, 3000, TraversalClass.Solid) }, new[] { new PieceSocket(SocketType.EdgeMount, 0, 0, SocketAxis.X) }, 2, 200, true);

    private static readonly PieceDefinition Doorway = Piece("piece.doorway.timber", PieceFamily.Doorway, WallBox,
        new[] { new PiecePart(new BoundsMm(-1700, -200, -800, 200), 3000, TraversalClass.Solid), new PiecePart(new BoundsMm(800, -200, 1700, 200), 3000, TraversalClass.Solid) },
        new[] { new PieceSocket(SocketType.EdgeMount, 0, 0, SocketAxis.X), new PieceSocket(SocketType.Door, 0, 0, SocketAxis.X) }, 2, 200, true);

    private static readonly PieceDefinition Door = Piece("piece.door.timber", PieceFamily.Door, new BoundsMm(-800, -200, 800, 200),
        new[] { new PiecePart(new BoundsMm(-800, -200, 800, 200), 2400, TraversalClass.Door) }, new[] { new PieceSocket(SocketType.DoorMount, 0, 0, SocketAxis.X) }, 1, 120);

    private static readonly PieceDefinition Roof = Piece("piece.roof.timber", PieceFamily.Roof, Square, Array.Empty<PiecePart>(), Array.Empty<PieceSocket>(), 1, 100);

    private static readonly PieceDefinition Chest = Piece("piece.storage.chest", PieceFamily.Storage, new BoundsMm(-500, 600, 500, 1200),
        new[] { new PiecePart(new BoundsMm(-500, 600, 500, 1200), 700, TraversalClass.Solid) }, new[] { new PieceSocket(SocketType.SquareMount, 0, 0, SocketAxis.None) }, 2, 100)
        with { Container = new PieceContainer(12, 0, 900) };

    private static readonly PieceDefinition Bench = Piece("piece.station.anvil", PieceFamily.Station, new BoundsMm(-500, 400, 500, 1000),
        new[] { new PiecePart(new BoundsMm(-500, 400, 500, 1000), 900, TraversalClass.Solid) }, new[] { new PieceSocket(SocketType.SquareMount, 0, 0, SocketAxis.None) }, 4, 300)
        with { Station = new PieceStation("anvil", 0, -250, 0) };

    private static readonly BuildingCatalog Catalog = new(new[] { Pad, Wall, Doorway, Door, Roof, Chest, Bench });

    [Fact]
    public void Lattice_AnchorsAndSlotKeys_ForEverySlotKind()
    {
        // Squares, roofs and furniture sit on half-module points; the key counts squares from the origin.
        Assert.True(Lattice.Fits(PieceSlot.Square, 100_500, 100_500, 0));
        Assert.Equal("sq:33:33", Lattice.SlotKey(PieceSlot.Square, 100_500, 100_500, 0));
        Assert.Equal("sq:32:33", Lattice.SlotKey(PieceSlot.Square, 97_500, 100_500, 3));
        Assert.Equal("rf:33:33", Lattice.SlotKey(PieceSlot.Roof, 100_500, 100_500, 0));
        Assert.Equal("fu:33:34", Lattice.SlotKey(PieceSlot.Furniture, 100_500, 103_500, 3));
        Assert.False(Lattice.Fits(PieceSlot.Square, 100_000, 100_500, 0));
        Assert.False(Lattice.Fits(PieceSlot.Furniture, 100_500, 99_000, 0));
        // Negative coordinates floor.
        Assert.True(Lattice.Fits(PieceSlot.Square, -1_500, -1_500, 0));
        Assert.Equal("sq:-1:-1", Lattice.SlotKey(PieceSlot.Square, -1_500, -1_500, 0));

        // Edges: the turn's parity fixes the axis, and two turns of one parity share a slot.
        Assert.True(Lattice.Fits(PieceSlot.Edge, 100_500, 99_000, 0));
        Assert.True(Lattice.Fits(PieceSlot.Edge, 100_500, 99_000, 2));
        Assert.False(Lattice.Fits(PieceSlot.Edge, 100_500, 99_000, 1));
        Assert.True(Lattice.Fits(PieceSlot.Edge, 99_000, 100_500, 1));
        Assert.False(Lattice.Fits(PieceSlot.Edge, 99_000, 100_500, 0));
        Assert.Equal("ex:33:33", Lattice.SlotKey(PieceSlot.Edge, 100_500, 99_000, 0));
        Assert.Equal("ex:33:33", Lattice.SlotKey(PieceSlot.Edge, 100_500, 99_000, 2));
        Assert.Equal("ez:33:33", Lattice.SlotKey(PieceSlot.Edge, 99_000, 100_500, 1));
        Assert.Equal("ex:-1:-1", Lattice.SlotKey(PieceSlot.Edge, -1_500, -3_000, 0));
        // A door's slot is its doorway's edge.
        Assert.True(Lattice.Fits(PieceSlot.Door, 100_500, 99_000, 2));
        Assert.Equal("dr:ex:33:33", Lattice.SlotKey(PieceSlot.Door, 100_500, 99_000, 2));

        // The ground a slot occupies: a square, or a segment with no width.
        Assert.Equal(new BoundsMm(99_000, 99_000, 102_000, 102_000), Lattice.Footprint(PieceSlot.Square, 100_500, 100_500, 0));
        Assert.Equal(new BoundsMm(99_000, 99_000, 102_000, 99_000), Lattice.Footprint(PieceSlot.Edge, 100_500, 99_000, 0));
        Assert.Equal(new BoundsMm(99_000, 99_000, 99_000, 102_000), Lattice.Footprint(PieceSlot.Edge, 99_000, 100_500, 1));
        Assert.Equal(Lattice.Footprint(PieceSlot.Edge, 100_500, 99_000, 0), Lattice.Footprint(PieceSlot.Door, 100_500, 99_000, 0));

        // A square's four edges and neighbours; an edge's two squares.
        Assert.Equal(new[] { "ex:33:33", "ez:34:33", "ex:33:34", "ez:33:33" },
            Lattice.EdgesOfSquare(100_500, 100_500).Select(e => Lattice.EdgeKey(e.XMm, e.ZMm, e.Rotation)));
        Assert.Equal(new[] { (100_500L, 97_500L), (100_500L, 100_500L) }, Lattice.SquaresBesideEdge(100_500, 99_000, 0));
        Assert.Equal(new[] { (97_500L, 100_500L), (100_500L, 100_500L) }, Lattice.SquaresBesideEdge(99_000, 100_500, 1));
        Assert.Equal(new[] { (100_500L, 97_500L), (103_500L, 100_500L), (100_500L, 103_500L), (97_500L, 100_500L) },
            Lattice.NeighbourSquares(100_500, 100_500));
    }

    [Fact]
    public void QuarterTurn_MapsBoxesAndFacingsExactly()
    {
        Assert.Equal((0L, 1L), QuarterTurn.Apply(0, 1, 0));
        Assert.Equal((1L, 0L), QuarterTurn.Apply(0, 1, 1));   // local +Z along world facing 90: towards +X
        Assert.Equal((0L, -1L), QuarterTurn.Apply(0, 1, 2));
        Assert.Equal((-1L, 0L), QuarterTurn.Apply(0, 1, 3));
        Assert.Equal(new BoundsMm(-200, -1700, 200, 1700), QuarterTurn.Apply(WallBox, 1));

        foreach (var piece in Catalog.Pieces.Values)
        {
            foreach (int r in piece.Rotations)
            {
                var turned = QuarterTurn.Apply(piece.Bounds, r);
                // An exact box of the same size, square for even turns and transposed for odd ones.
                long w = piece.Bounds.MaxXMm - piece.Bounds.MinXMm, d = piece.Bounds.MaxZMm - piece.Bounds.MinZMm;
                Assert.Equal(r % 2 == 0 ? (w, d) : (d, w), (turned.MaxXMm - turned.MinXMm, turned.MaxZMm - turned.MinZMm));
                // Four turns come back.
                var box = piece.Bounds;
                for (int n = 0; n < 4; n++)
                    box = QuarterTurn.Apply(box, r);
                Assert.Equal(piece.Bounds, box);
                foreach (var part in piece.Parts)
                    Assert.True(turned.Contains(QuarterTurn.Apply(part.Box, r)), $"{piece.Id} r{r}: a part outside its bounds");
            }
        }

        // The workshop's poses (§4.22): the doorway's opening, the bench and both chests.
        var doorway = BuildingMath.WorldParts(Doorway, 100_500, 99_000, 0, "d");
        Assert.Equal((99_700L, 101_300L), (doorway[0].MaxXMm, doorway[1].MinXMm));
        var bench = Assert.Single(BuildingMath.WorldParts(Bench, 100_500, 103_500, 3, "b"));
        Assert.Equal((99_500L, 100_100L, 103_000L, 104_000L), (bench.MinXMm, bench.MaxXMm, bench.MinZMm, bench.MaxZMm));
        Assert.Equal((100_750L, 103_500L), BuildingMath.World(Bench.Station!.AnchorXMm, Bench.Station.AnchorZMm, 100_500, 103_500, 3));
        Assert.Equal(270_000, QuarterTurn.Facing(Bench.Station.FacingMdeg, 3));
        var chest = Assert.Single(BuildingMath.WorldParts(Chest, 103_500, 103_500, 0, "c"));
        Assert.Equal((103_000L, 104_000L, 104_100L, 104_700L), (chest.MinXMm, chest.MaxXMm, chest.MinZMm, chest.MaxZMm));
        Assert.Equal((103_500L, 104_400L), BuildingMath.World(Chest.Container!.XMm, Chest.Container.ZMm, 103_500, 103_500, 0));
        var chest2 = Assert.Single(BuildingMath.WorldParts(Chest, 103_500, 100_500, 1, "c"));
        Assert.Equal((104_100L, 104_700L, 100_000L, 101_000L), (chest2.MinXMm, chest2.MaxXMm, chest2.MinZMm, chest2.MaxZMm));
        Assert.Equal((104_400L, 100_500L), BuildingMath.World(Chest.Container.XMm, Chest.Container.ZMm, 103_500, 100_500, 1));
        Assert.Equal("c#0", chest2.Id);
        Assert.Equal(0, chest2.ClearanceMm);

        // Facings wrap into [0, 360 000).
        Assert.Equal(new[] { 0, 90_000, 180_000, 270_000 }, Enumerable.Range(0, 4).Select(r => QuarterTurn.Facing(0, r)));
        Assert.Equal(30_000, QuarterTurn.Facing(300_000, 1));
    }

    [Fact]
    public void Sockets_HaveTheirWorldPositionsAndAxes()
    {
        var pad = BuildingMath.WorldSockets(Pad, 100_500, 100_500, 0);
        Assert.Equal(new[]
        {
            new PieceSocket(SocketType.Edge, 100_500, 102_000, SocketAxis.X), new PieceSocket(SocketType.Edge, 100_500, 99_000, SocketAxis.X),
            new PieceSocket(SocketType.Edge, 102_000, 100_500, SocketAxis.Z), new PieceSocket(SocketType.Edge, 99_000, 100_500, SocketAxis.Z),
            new PieceSocket(SocketType.Square, 100_500, 100_500, SocketAxis.None),
        }, pad);
        // A turned pad offers the same edges.
        Assert.Equal(pad.ToHashSet(), BuildingMath.WorldSockets(Pad, 100_500, 100_500, 1).ToHashSet());

        // A wall's mount mates with a pad's edge exactly, on either axis.
        Assert.Contains(Assert.Single(BuildingMath.WorldSockets(Wall, 100_500, 99_000, 0)) with { Type = SocketType.Edge }, pad);
        Assert.Contains(Assert.Single(BuildingMath.WorldSockets(Wall, 99_000, 100_500, 1)) with { Type = SocketType.Edge }, pad);
        Assert.Equal(new PieceSocket(SocketType.EdgeMount, 99_000, 100_500, SocketAxis.Z), BuildingMath.WorldSockets(Wall, 99_000, 100_500, 3)[0]);
        Assert.DoesNotContain(BuildingMath.WorldSockets(Wall, 100_500, 99_000, 1)[0] with { Type = SocketType.Edge }, pad);

        // A doorway offers a door along its own axis; the door mounts on it; furniture mounts on a square.
        Assert.Equal(new PieceSocket(SocketType.Door, 99_000, 100_500, SocketAxis.Z), BuildingMath.WorldSockets(Doorway, 99_000, 100_500, 1)[1]);
        Assert.Equal(new PieceSocket(SocketType.DoorMount, 99_000, 100_500, SocketAxis.Z), BuildingMath.WorldSockets(Door, 99_000, 100_500, 3)[0]);
        Assert.Equal(new PieceSocket(SocketType.SquareMount, 100_500, 103_500, SocketAxis.None), BuildingMath.WorldSockets(Bench, 100_500, 103_500, 3)[0]);
        Assert.Equal(SocketType.Edge, BuildingMath.ProviderFor(SocketType.EdgeMount));
        Assert.Equal(SocketType.Door, BuildingMath.ProviderFor(SocketType.DoorMount));
    }

    [Fact]
    public void Snapper_SnapsFixedHalfModuleAndDoorAims()
    {
        var none = Array.Empty<PiecePose>();
        (long, long, int)? Snap(string def, long x, long z, int r, IReadOnlyList<PiecePose>? pieces = null) =>
            Snapper.Snap(Catalog, pieces ?? none, def, x, z, r);

        // Squares: the square the aim is in; an aim on a line takes the square it begins.
        Assert.Equal((100_500L, 100_500L, 0), Snap(Pad.Id, 100_000, 100_000, 0));
        Assert.Equal((100_500L, 103_500L, 2), Snap(Roof.Id, 99_999, 102_000, 2));
        Assert.Equal((100_500L, 100_500L, 1), Snap(Chest.Id, 99_000, 99_000, 1));
        Assert.Equal((-1_500L, -1_500L, 0), Snap(Pad.Id, -1, -1, 0));

        // Edges: the nearest line of the turn's axis; half way resolves by the floor.
        Assert.Equal((100_500L, 99_000L, 0), Snap(Wall.Id, 100_000, 99_400, 0));
        Assert.Equal((100_500L, 99_000L, 2), Snap(Wall.Id, 100_000, 100_499, 2));
        Assert.Equal((100_500L, 102_000L, 0), Snap(Wall.Id, 100_000, 100_500, 0));
        Assert.Equal((99_000L, 100_500L, 1), Snap(Doorway.Id, 99_400, 100_000, 1));
        Assert.Equal((102_000L, 100_500L, 3), Snap(Wall.Id, 100_500, 100_000, 3));

        // Doors: the nearest doorway without a door within 2 m; ties to the lower x, then the lower z; the turn keeps the doorway's axis.
        var id = EntityId.NewId(EntityKind.Piece);
        var south = new PiecePose(id, Doorway.Id, 100_500, 99_000, 0);
        var north = new PiecePose(id, Doorway.Id, 100_500, 102_000, 2);
        var west = new PiecePose(id, Doorway.Id, 99_000, 100_500, 1);
        var doorways = new[] { north, south };
        Assert.Equal((100_500L, 99_000L, 2), Snap(Door.Id, 100_500, 100_400, 1, doorways));
        Assert.Equal((100_500L, 102_000L, 0), Snap(Door.Id, 100_500, 100_600, 0, doorways));
        Assert.Equal((100_500L, 99_000L, 2), Snap(Door.Id, 100_500, 100_500, 2, doorways));    // equidistant: the lower z
        Assert.Equal((99_000L, 100_500L, 1), Snap(Door.Id, 100_500, 100_500, 0, new[] { south, west }));   // equidistant: the lower x
        Assert.Equal((100_500L, 102_000L, 0), Snap(Door.Id, 100_500, 100_400, 3,
            new[] { south, north, new PiecePose(id, Door.Id, 100_500, 99_000, 0) }));   // the south one already has its door
        Assert.Null(Snap(Door.Id, 100_500, 96_900, 0, doorways));   // 2.1 m from the nearest
        Assert.Null(Snap(Door.Id, 100_500, 100_000, 0));
        Assert.Null(Snap("piece.nothing", 100_500, 100_500, 0));
    }

    [Fact]
    public void Relief_OfTheKnownSquares()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoRoot(), "content"));
        Assert.False(loader.HasErrors);
        var layout = WorldContent.BuildLayout(loader, "region.ashen_hollow");
        var terrain = layout.Space.Terrain;

        Assert.Equal(228, BuildingMath.Relief(terrain, 112_500, 106_500));   // x 111-114, z 105-108: the worst square in the area
        Assert.Equal(96, BuildingMath.Relief(terrain, 100_500, 100_500));    // (99-102)², the four-cell square

        // Every one of the area's 81 squares takes a pad (<= 250 mm); the square above is the worst.
        var reliefs = new List<long>();
        for (long x = 88_500; x <= 112_500; x += 3_000)
        {
            for (long z = 88_500; z <= 112_500; z += 3_000)
                reliefs.Add(BuildingMath.Relief(terrain, x, z));
        }
        Assert.Equal(81, reliefs.Count);
        Assert.Equal(228, reliefs.Max());
    }

    [Fact]
    public void RoofSupport_DepthOneOnly()
    {
        // A wall on the south edge of square A (100.5, 100.5); roofs over A, B east of it, and C east of B.
        var walls = new HashSet<string> { Lattice.EdgeKey(100_500, 99_000, 0) };
        var roofs = new HashSet<string>();
        bool Supported(long x, long z) => Lattice.RoofSupported(x, z, walls.Contains, roofs.Contains);
        string RoofKey(long x, long z) => Lattice.SlotKey(PieceSlot.Roof, x, z, 0);

        Assert.True(Supported(100_500, 100_500));    // on its own wall
        Assert.False(Supported(103_500, 100_500));   // B: no wall, and no roofed neighbour yet
        roofs.Add(RoofKey(100_500, 100_500));
        Assert.True(Supported(103_500, 100_500));    // B: beside A, which has a wall
        roofs.Add(RoofKey(103_500, 100_500));
        Assert.False(Supported(106_500, 100_500));   // C: beside B only, and B's support is borrowed - depth one
        Assert.False(Supported(100_500, 106_500));   // not edge-adjacent to A

        // Evaluated now only: without the wall, nothing here is supported - and nothing already placed is taken down by this function.
        walls.Clear();
        Assert.False(Supported(100_500, 100_500));
        Assert.False(Supported(103_500, 100_500));
        Assert.Equal(2, roofs.Count);
    }

    [Fact]
    public void IntegerOverlap_TouchingIsNotOverlap()
    {
        var a = new BoundsMm(0, 0, 10_000, 10_000);
        Assert.False(BuildingMath.Overlaps(a, new BoundsMm(10_000, 0, 20_000, 10_000)));   // sharing an edge
        Assert.False(BuildingMath.Overlaps(a, new BoundsMm(10_000, 10_000, 20_000, 20_000)));   // sharing a corner
        Assert.True(BuildingMath.Overlaps(a, new BoundsMm(9_999, 0, 20_000, 10_000)));
        Assert.True(BuildingMath.Overlaps(a, new BoundsMm(2_000, 2_000, 3_000, 3_000)));   // inside
        Assert.False(BuildingMath.Overlaps(a, new BoundsMm(5_000, 10_000, 5_000, 12_000)));   // a segment touching the top

        // A circle by its squared clamp distance: touching is clear.
        Assert.False(BuildingMath.Overlaps(a, new CircleBlocker("c", 15_000, 5_000, 5_000, 0)));
        Assert.True(BuildingMath.Overlaps(a, new CircleBlocker("c", 15_000, 5_000, 5_001, 0)));
        Assert.False(BuildingMath.Overlaps(a, new CircleBlocker("c", 13_000, 14_000, 5_000, 0)));   // 3-4-5 from the corner
        Assert.True(BuildingMath.Overlaps(a, new CircleBlocker("c", 13_000, 14_000, 5_001, 0)));
        Assert.True(BuildingMath.Overlaps(a, new BoxBlocker("b", 9_000, 9_000, 30_000, 30_000, 0)));
        Assert.False(BuildingMath.Overlaps(a, new BoxBlocker("b", 10_000, -5_000, 30_000, 30_000, 0)));

        // Large coordinates stay exact in long.
        var far = new BoundsMm(3_000_000_000, 3_000_000_000, 3_000_003_000, 3_000_003_000);
        Assert.Equal(25_000_000L, BuildingMath.DistanceSquared(far, 3_000_006_000, 3_000_007_000));
        Assert.False(BuildingMath.Overlaps(far, new CircleBlocker("c", 3_000_006_000, 3_000_007_000, 5_000, 0)));
        Assert.Equal(0, BuildingMath.DistanceSquared(a, 5_000, 5_000));
    }

    [Fact]
    public void RepairCost_RoundsUp_AndRefund_RoundsDown()
    {
        // Mending: the percentage of each cost line, scaled by the health missing, rounded up - a wall at 170/200 is ⌈2 × 30 × 100 /
        // 20 000⌉ = 1 timber, at 10/200 ⌈2 × 190 × 100 / 20 000⌉ = 2, and one point short still a whole timber; whole, nothing.
        Assert.Equal(1, BuildingMath.RepairCost(2, 30, 200, 100));
        Assert.Equal(2, BuildingMath.RepairCost(2, 190, 200, 100));
        Assert.Equal(1, BuildingMath.RepairCost(2, 1, 200, 100));
        Assert.Equal(0, BuildingMath.RepairCost(2, 0, 200, 100));
        Assert.Equal(0, BuildingMath.RepairCost(2, 30, 200, 0));
        Assert.Equal(6, BuildingMath.RepairCost(4, 150, 300, 300));    // exact: 4 × 150 × 300 / 30 000
        Assert.Equal(20, BuildingMath.RepairCost(2, 200, 200, 1_000)); // the largest percentage, from nothing
        Assert.Equal(int.MaxValue, BuildingMath.RepairCost(int.MaxValue, 1_000, 1_000, 100));   // no overflow on the way

        // Taking down: the percentage, rounded down - half of 1 is nothing, of 2 is 1, of 5 is 2.
        Assert.Equal(0, BuildingMath.Refund(1, 50));
        Assert.Equal(1, BuildingMath.Refund(2, 50));
        Assert.Equal(2, BuildingMath.Refund(5, 50));
        Assert.Equal(4, BuildingMath.Refund(4, 100));
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "content")) && Directory.Exists(Path.Combine(dir.FullName, "src")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root");
    }
}
