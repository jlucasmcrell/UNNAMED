using System.Diagnostics;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The full-area layout (M7 design §14.12.3), T2's setup: the build area [87, 114]² filled to its 256 pieces through commands - a 3 × 3 of
/// 9 m rooms, 81 pads, 14 doorways (one in the middle edge of each shared wall, one out to the west, one to the east), 58 walls, 14 doors,
/// 7 chests, the bench over both seams and 81 roofs - by a character who walks room to room through the doorways.
/// </summary>
internal sealed class FullAreaLayout
{
    public const string Pad = "piece.pad.timber", Doorway = "piece.doorway.timber", Wall = "piece.wall.timber", Door = "piece.door.timber",
        Chest = "piece.storage.chest", Bench = "piece.station.anvil", Roof = "piece.roof.timber";

    private const string Tavar = "npc.ashen_hollow.tavar_orr";
    private static readonly long[] Lines = { 87_000, 96_000, 105_000, 114_000 };

    /// <summary>The rooms in the order the character walks them, a serpentine whose every step crosses a shared wall's doorway.</summary>
    private static readonly (int I, int K)[] Serpentine = { (0, 0), (1, 0), (2, 0), (2, 1), (1, 1), (0, 1), (0, 2), (1, 2), (2, 2) };

    private readonly Arena _arena;
    private (int I, int K) _room = (1, 0);

    private FullAreaLayout(Arena arena) => _arena = arena;

    /// <summary>
    /// Each placement's <c>Submit</c> with its drain, in milliseconds, in order (the T3 fold), with the garbage collections of each
    /// generation that ran inside it.
    /// </summary>
    public List<(string Def, long X, long Z, double Ms, int Gen0, int Gen1, int Gen2, double PauseMs)> Drains { get; } = new();

    /// <summary>
    /// The crafted start: at (100.5, 94.5) carrying 17 stacks of 20 timber (weight is checked only when items enter the pack), with Tavar
    /// recruited and waiting at (91.5, 107.5) in the north-west room.
    /// </summary>
    public static PlayerRecord Start(GameSession session, PlayerRecord r) =>
        r.WithInventory(r.Inventory.Concat(Enumerable.Range(0, 17).Select(_ => Arena.Stack("item.material.timber", 20))))
            .WithCompanions(new[]
            {
                new CompanionRecord(Tavar, CompanionOrder.Wait, CompanionCondition.Up, 91_500, 107_500, 0, session.Setup.Social.Npcs[Tavar].Companion!.MaxHealth),
            });

    public static long Centre(int n) => 91_500 + 9_000L * n;

    public static IEnumerable<(long X, long Z)> Squares() =>
        from i in Enumerable.Range(29, 9) from k in Enumerable.Range(29, 9) select (3_000L * i + 1_500, 3_000L * k + 1_500);

    public static readonly (long X, long Z, int R)[] Doorways = Enumerable.Range(0, 3).SelectMany(n => new[]
        {
            (96_000L, Centre(n), 1), (105_000L, Centre(n), 1), (Centre(n), 96_000L, 0), (Centre(n), 105_000L, 0),
        })
        .Concat(new[] { (87_000L, 100_500L, 1), (114_000L, 100_500L, 1) }).ToArray();

    public static readonly (long X, long Z, int R)[] Walls = Lines.SelectMany(line => Enumerable.Range(0, 9).SelectMany(e => new[]
        {
            (line, 88_500L + 3_000L * e, 1), (88_500L + 3_000L * e, line, 0),
        }))
        .Where(w => !Doorways.Any(d => (d.X, d.Z) == (w.Item1, w.Item2))).ToArray();

    /// <summary>The room centres with a chest: every room but the centre (the bench's) and the south-east, [105, 114] × [87, 96] m.</summary>
    public static readonly (long X, long Z)[] Chests = Serpentine.Select(r => (Centre(r.I), Centre(r.K)))
        .Where(c => c != (100_500, 100_500) && c != (109_500, 91_500)).ToArray();

    /// <summary>
    /// Place the whole layout in the design's order - pads, doorways, walls, doors, chests and the bench, roofs (the 72 squares with a wall
    /// or doorway edge, then the 9 room centres) - every placement accepted; then stand at (84.0, 100.5), outside the west door. Asserts
    /// 256 intact pieces, sequence 256, one rebuild for each of the 94 pieces with parts, no structure audit, and every room centre
    /// reachable for a person who opens doors from where the character stands.
    /// </summary>
    public static FullAreaLayout Build(Arena arena)
    {
        var layout = new FullAreaLayout(arena);
        var rebuilt = arena.Record<NavigationRebuilt>();
        layout.Run();
        var simulation = arena.Simulation;
        Assert.Equal(256, simulation.Pieces.Length);
        Assert.All(simulation.Pieces, p => Assert.Equal(p.HealthMax, p.HealthCurrent));
        Assert.Equal(256, simulation.World.StructureSequence);
        Assert.Equal(94, rebuilt.Count);
        Assert.Empty(simulation.StructureAudit);
        var from = new NavPoint(84_000, 100_500);
        foreach (var r in Serpentine)
        {
            var plan = NavSearch.Plan(new NavQuery(simulation.Navigation.Grid, _ => true, simulation.Setup.Navigation, new NavScratch(), null),
                new NavAgent(0, true), from, new NavPoint(Centre(r.I), Centre(r.K)));
            Assert.True(plan.Outcome == NavOutcome.Found, $"the room at ({Centre(r.I)}, {Centre(r.K)}) is {plan.Outcome} from the west");
        }
        return layout;
    }

    private void Run()
    {
        var onPath = Serpentine.Zip(Serpentine.Skip(1)).Select(p => DoorwayBetween(p.First, p.Second)).ToHashSet();

        // Pads, room by room, from each centre (the start is in the south-middle room).
        foreach (var room in Serpentine)
        {
            GoTo(room, spot: false);
            foreach (var (x, z) in Squares().Where(s => RoomOf(s.X) == room.I && RoomOf(s.Z) == room.K))
                Place(Pad, x, z, 0);
        }
        // Doorways, walking back: each from the centre of a room it borders.
        foreach (var room in Serpentine.Reverse())
        {
            GoTo(room, spot: false);
            foreach (var d in Doorways.Where(d => Borders(room, d.X, d.Z) && !Placed(Doorway, d.X, d.Z)))
                Place(Doorway, d.X, d.Z, d.R);
        }
        // Walls, walking forward.
        foreach (var room in Serpentine)
        {
            GoTo(room, spot: false);
            foreach (var w in Walls.Where(w => Borders(room, w.X, w.Z) && !Placed(Wall, w.X, w.Z)))
                Place(Wall, w.X, w.Z, w.R);
        }
        // Doors, walking back: the ones on the way hung and opened as they are crossed, the rest from the room's centre.
        foreach (var room in Serpentine.Reverse())
        {
            GoTo(room, spot: false, hangDoors: true);
            foreach (var d in Doorways.Where(d => Borders(room, d.X, d.Z) && !onPath.Contains((d.X, d.Z)) && !Placed(Door, d.X, d.Z)))
                Place(Door, d.X, d.Z, d.R);
        }
        // Chests and the bench, each at its room's centre square, from the room's south-east spot.
        foreach (var room in Serpentine)
        {
            GoTo(room, spot: true);
            var centre = (Centre(room.I), Centre(room.K));
            if (centre == (100_500L, 100_500L))
                Place(Bench, centre.Item1, centre.Item2, 0);
            else if (Chests.Contains(centre))
                Place(Chest, centre.Item1, centre.Item2, 0);
        }
        // Roofs: the squares with an edge first, walking back; then the room centres, walking forward.
        foreach (var room in Serpentine.Reverse())
        {
            GoTo(room, spot: true);
            foreach (var (x, z) in Squares().Where(s => RoomOf(s.X) == room.I && RoomOf(s.Z) == room.K && (s.X, s.Z) != (Centre(room.I), Centre(room.K))))
                Place(Roof, x, z, 0);
        }
        foreach (var room in Serpentine)
        {
            GoTo(room, spot: true);
            Place(Roof, Centre(room.I), Centre(room.K), 0);
        }
        // Back to the west room and out by its door.
        foreach (var room in new[] { (1, 2), (0, 2), (0, 1) })
            GoTo(room, spot: true);
        Walk((88.2, 100.5));
        Open(87_000, 100_500);
        Walk((85.8, 100.5), (84.0, 100.5));
    }

    private static int RoomOf(long mm) => (int)((mm - 87_000) / 9_000);

    /// <summary>Whether a piece on a line borders a room: its anchor lies on the room's edge.</summary>
    private static bool Borders((int I, int K) room, long x, long z)
    {
        long minX = 87_000 + 9_000L * room.I, minZ = 87_000 + 9_000L * room.K;
        bool onX = (x == minX || x == minX + 9_000) && z > minZ && z < minZ + 9_000;
        bool onZ = (z == minZ || z == minZ + 9_000) && x > minX && x < minX + 9_000;
        return onX || onZ;
    }

    private static (long X, long Z) DoorwayBetween((int I, int K) a, (int I, int K) b) =>
        ((Centre(a.I) + Centre(b.I)) / 2, (Centre(a.K) + Centre(b.K)) / 2);

    private bool Placed(string def, long x, long z) => _arena.Simulation.Pieces.Any(p => p.DefId == def && p.XMm == x && p.ZMm == z);

    private void Place(string def, long x, long z, int r)
    {
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        var paused = GC.GetTotalPauseDuration();
        var clock = Stopwatch.StartNew();
        string? refused = _arena.Submit(new PlacePieceCommand(_arena.Player, def, x, z, r));
        Drains.Add((def, x, z, clock.Elapsed.TotalMilliseconds, GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2,
            (GC.GetTotalPauseDuration() - paused).TotalMilliseconds));
        if (refused is not null)
        {
            var rule = _arena.Simulation.PreviewPlacement(def, x, z, r, checkNavigability: true).Failed;
            Assert.Fail($"{def} at ({x}, {z}) r{r}, from {_arena.Simulation.Player.Body}: refused, rule {rule}: \"{refused}\"");
        }
    }

    /// <summary>Open a door the character owns, standing within reach of it.</summary>
    private void Open(long x, long z)
    {
        var door = _arena.Simulation.Pieces.Single(p => p.DefId == Door && p.XMm == x && p.ZMm == z);
        if (!door.DoorOpen)
            Assert.Null(_arena.Submit(new InteractCommand(_arena.Player, door.Id.Value)));
    }

    private void Walk(params (double X, double Z)[] legs)
    {
        foreach (var (x, z) in legs)
            Assert.True(_arena.WalkTo(x, z), $"the walk to ({x}, {z}) stopped at {_arena.Simulation.Player.Body}");
    }

    /// <summary>
    /// To an adjacent room by the doorway between them, from an approach 1.2 m before its line to 1.2 m past it, round the north side of a
    /// room's centre square (where a chest or the bench stands) and round Tavar in the north-west room; then to the room's centre, or to its
    /// south-east spot once the centre squares are furnished. With <paramref name="hangDoors"/> the doorway's door is hung and opened at
    /// the approach; otherwise a door there is opened if it is shut.
    /// </summary>
    private void GoTo((int I, int K) room, bool spot, bool hangDoors = false)
    {
        if (room != _room)
        {
            var (fx, fz) = ((double)Centre(_room.I) / 1000, (double)Centre(_room.K) / 1000);
            var (tx, tz) = ((double)Centre(room.I) / 1000, (double)Centre(room.K) / 1000);
            Assert.Equal(9.0, Math.Abs(tx - fx) + Math.Abs(tz - fz));
            var (dx, dz) = (Math.Sign(tx - fx), Math.Sign(tz - fz));
            var (mx, mz) = ((fx + tx) / 2, (fz + tz) / 2);
            var legs = new List<(double X, double Z)>();
            if (dz > 0)
                legs.Add((fx + 2, fz + 2.5));   // round the north side of this room's centre square
            if (_room == (0, 2) && dz < 0)
                legs.Add((93.0, 107.5));        // round Tavar
            legs.Add((mx - 1.2 * dx, mz - 1.2 * dz));
            Walk(legs.ToArray());
            var doorway = ((long)Math.Round(mx * 1000), (long)Math.Round(mz * 1000));
            if (hangDoors && !Placed(Door, doorway.Item1, doorway.Item2))
                Place(Door, doorway.Item1, doorway.Item2, dx != 0 ? 1 : 0);
            if (Placed(Door, doorway.Item1, doorway.Item2))
                Open(doorway.Item1, doorway.Item2);
            legs.Clear();
            legs.Add((mx + 1.2 * dx, mz + 1.2 * dz));
            if (dz < 0)
                legs.Add((tx + 2, tz + 2.5));   // round the north side of the new room's centre square
            if (room == (0, 2) && dz > 0)
                legs.Add((93.0, 107.5));        // round Tavar
            Walk(legs.ToArray());
            _room = room;
        }
        var (cx, cz) = ((double)Centre(room.I) / 1000, (double)Centre(room.K) / 1000);
        Walk(spot ? (cx + 2, cz - 2) : (cx, cz));
    }
}
