// UNNAMED Domain - building pieces on the 3 m lattice (M7 design §4.2-4.8, §4.21; D-08, D-14)
// No Godot references - pure C#, integer only

using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Building;

/// <summary>What a piece is: a ground pad, a wall, a doorway, the door hung in one, a roof, a chest or a station.</summary>
public enum PieceFamily { Pad, Wall, Doorway, Door, Roof, Storage, Station }

/// <summary>Where a piece goes on the lattice: a square, an edge between squares, a doorway's opening, a square's roof, or on a square.</summary>
public enum PieceSlot { Square, Edge, Door, Roof, Furniture }

/// <summary>A socket: a provider a piece offers (a pad's edges and square, a doorway's opening), or the mount a piece needs.</summary>
public enum SocketType { Edge, Square, Door, EdgeMount, SquareMount, DoorMount }

/// <summary>The line an edge or door socket runs along; a square socket has none.</summary>
public enum SocketAxis { None, X, Z }

/// <summary>An axis-aligned rectangle on the ground, in integer millimetres, edges included.</summary>
public readonly record struct BoundsMm(long MinXMm, long MinZMm, long MaxXMm, long MaxZMm)
{
    public BoundsMm Offset(long dx, long dz) => new(MinXMm + dx, MinZMm + dz, MaxXMm + dx, MaxZMm + dz);

    public BoundsMm Union(BoundsMm other) =>
        new(Math.Min(MinXMm, other.MinXMm), Math.Min(MinZMm, other.MinZMm), Math.Max(MaxXMm, other.MaxXMm), Math.Max(MaxZMm, other.MaxZMm));

    /// <summary>Whether <paramref name="inner"/> lies inside, closed intervals: sharing an edge is inside.</summary>
    public bool Contains(BoundsMm inner) =>
        inner.MinXMm >= MinXMm && inner.MaxXMm <= MaxXMm && inner.MinZMm >= MinZMm && inner.MaxZMm <= MaxZMm;
}

/// <summary>A blocking part of a piece, local to its anchor at quarter turn 0.</summary>
public sealed record PiecePart(BoundsMm Box, long HeightMm, TraversalClass Traversal);

/// <summary>A socket, local to its anchor at quarter turn 0.</summary>
public sealed record PieceSocket(SocketType Type, long XMm, long ZMm, SocketAxis Axis);

/// <summary>One line of a piece's cost: so many of an item definition.</summary>
public sealed record PieceCost(string ItemId, int Count);

/// <summary>A storage piece's container: how many stacks it holds, and where its site stands, local at quarter turn 0.</summary>
public sealed record PieceContainer(int StackSlots, long XMm, long ZMm);

/// <summary>A station piece's work: the recipe station kind, and where and which way its worker stands, local at quarter turn 0.</summary>
public sealed record PieceStation(string Kind, long AnchorXMm, long AnchorZMm, int FacingMdeg);

/// <summary>
/// A piece definition (content kind <c>piece</c>; M7 design §4.3). Its shape, sockets, cost and health are the definition's and never
/// saved; a placed piece stores only its pose, owner and health.
/// </summary>
public sealed record PieceDefinition(string Id, string Name, PieceFamily Family, PieceSlot Slot, ImmutableArray<int> Rotations, BoundsMm Bounds,
    ImmutableArray<PiecePart> Parts, ImmutableArray<PieceSocket> Sockets, ImmutableArray<PieceCost> Cost, int HealthMax, bool SupportsRoof)
{
    public PieceContainer? Container { get; init; }
    public PieceStation? Station { get; init; }

    /// <summary>A family's one legal slot.</summary>
    public static PieceSlot SlotOf(PieceFamily family) => family switch
    {
        PieceFamily.Pad => PieceSlot.Square,
        PieceFamily.Wall or PieceFamily.Doorway => PieceSlot.Edge,
        PieceFamily.Door => PieceSlot.Door,
        PieceFamily.Roof => PieceSlot.Roof,
        _ => PieceSlot.Furniture,
    };
}

/// <summary>The content keys of the building enums, one spelling each.</summary>
public static class BuildingKeys
{
    public static readonly ImmutableSortedDictionary<string, PieceFamily> Families = new Dictionary<string, PieceFamily>
    {
        ["pad"] = PieceFamily.Pad, ["wall"] = PieceFamily.Wall, ["doorway"] = PieceFamily.Doorway, ["door"] = PieceFamily.Door,
        ["roof"] = PieceFamily.Roof, ["storage"] = PieceFamily.Storage, ["station"] = PieceFamily.Station,
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    public static readonly ImmutableSortedDictionary<string, PieceSlot> Slots = new Dictionary<string, PieceSlot>
    {
        ["square"] = PieceSlot.Square, ["edge"] = PieceSlot.Edge, ["door"] = PieceSlot.Door, ["roof"] = PieceSlot.Roof,
        ["furniture"] = PieceSlot.Furniture,
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    public static readonly ImmutableSortedDictionary<string, SocketType> SocketTypes = new Dictionary<string, SocketType>
    {
        ["edge"] = SocketType.Edge, ["square"] = SocketType.Square, ["door"] = SocketType.Door, ["edge_mount"] = SocketType.EdgeMount,
        ["square_mount"] = SocketType.SquareMount, ["door_mount"] = SocketType.DoorMount,
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    public static readonly ImmutableSortedDictionary<string, SocketAxis> Axes = new Dictionary<string, SocketAxis>
    {
        ["x"] = SocketAxis.X, ["z"] = SocketAxis.Z,
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    public static readonly ImmutableSortedDictionary<string, TraversalClass> Traversals = new Dictionary<string, TraversalClass>
    {
        ["solid"] = TraversalClass.Solid, ["door"] = TraversalClass.Door,
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    public static string Key(PieceFamily family) => Families.First(f => f.Value == family).Key;
}

/// <summary>Every piece the build knows, by ID.</summary>
public sealed class BuildingCatalog
{
    public static BuildingCatalog Empty { get; } = new(Array.Empty<PieceDefinition>());

    public BuildingCatalog(IEnumerable<PieceDefinition> pieces) =>
        Pieces = pieces.ToImmutableSortedDictionary(p => p.Id, p => p, StringComparer.Ordinal);

    public ImmutableSortedDictionary<string, PieceDefinition> Pieces { get; }

    public PieceDefinition? Find(string id) => Pieces.TryGetValue(id, out var piece) ? piece : null;
}

/// <summary>How far building keeps clear of what the region authored (M7 design §4.16), in millimetres.</summary>
public sealed record BuildingProtection(long SpawnPointMm, long NpcSiteMm, long SiteMm, long StructureMarginMm, long SpawnerMarginMm);

/// <summary>
/// Building's numbers (<c>config.building</c>; M7 design §4.10): the save-locked module, quarter turns, the reach a piece is placed
/// within, the relief a pad allows, refund and repair percentages, the protected margins, and the one damage rule.
/// </summary>
public sealed record BuildingConstants(long ModuleMm, int RotationStepDeg, long PlaceReachMm, long PadMaxReliefMm, int RefundPercent,
    int RepairCostPercent, BuildingProtection Protection, ImmutableSortedDictionary<string, int> Damage)
{
    /// <summary>BLD009: the most pieces the build areas meeting one cell, or one region, may hold between them.</summary>
    public const int PiecesPerCellCeiling = 256;

    public const int PiecesPerRegionCeiling = 256;

    /// <summary>The shipped values.</summary>
    public static BuildingConstants Default { get; } = new(Lattice.ModuleMm, 90, 6_000, 250, 50, 100,
        new BuildingProtection(3_000, 1_500, 1_500, 1_500, 1_000), ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,
            new[] { KeyValuePair.Create("melee", 10) }));

    /// <summary>What is wrong with these numbers, or null.</summary>
    public string? Problem()
    {
        if (ModuleMm != Lattice.ModuleMm)
            return $"module_m must be {Lattice.ModuleMm / 1000.0:0.0}: anchors are absolute, so the module is save-locked";
        if (RotationStepDeg != 90)
            return "rotation_step_deg must be 90: quarter turns only";
        if (PlaceReachMm < 1_000 || PlaceReachMm > 12_000)
            return "place_reach_m must be between 1 and 12";
        if (PadMaxReliefMm < 0 || PadMaxReliefMm > 1_000)
            return "pad_max_relief_m must be between 0 and 1";
        if (RefundPercent < 0 || RefundPercent > 100)
            return "refund_percent must be between 0 and 100";
        if (RepairCostPercent < 0 || RepairCostPercent > 1_000)
            return "repair_cost_percent must be between 0 and 1000";
        var p = Protection;
        if (p.SpawnPointMm < 0 || p.NpcSiteMm < 0 || p.SiteMm < 0 || p.StructureMarginMm < 0 || p.SpawnerMarginMm < 0)
            return "every protection must be at least 0";
        foreach (var (key, amount) in Damage)
        {
            if (key != "melee")
                return $"damage '{key}' is not a damage source M7 has (melee only)";
            if (amount < 1)
                return $"damage '{key}' must be at least 1";
        }
        return null;
    }
}

/// <summary>
/// The world-anchored 3 m lattice (M7 design §4.2): squares centred on half-module points, edges on module lines, slot keys, and the
/// geometry a slot occupies. All division floors, so negative coordinates and points on a line resolve one way.
/// </summary>
public static class Lattice
{
    public const long ModuleMm = 3_000;
    public const long HalfMm = ModuleMm / 2;

    private static long Mod(long a) => a - ModuleMm * NavGeometry.FloorDiv(a, ModuleMm);

    private static long Index(long a) => NavGeometry.FloorDiv(a, ModuleMm);

    /// <summary>Whether (x, z, r) satisfies a slot's anchor rule. A door's other half - an intact doorway there - is the world's to check.</summary>
    public static bool Fits(PieceSlot slot, long xMm, long zMm, int rotation) => slot switch
    {
        PieceSlot.Square or PieceSlot.Roof or PieceSlot.Furniture => Mod(xMm) == HalfMm && Mod(zMm) == HalfMm,
        _ => rotation % 2 == 0 ? Mod(xMm) == HalfMm && Mod(zMm) == 0 : Mod(xMm) == 0 && Mod(zMm) == HalfMm,
    };

    /// <summary>The key of the slot a piece at (x, z, r) holds: <c>sq</c>, <c>ex</c>/<c>ez</c>, <c>dr:</c> + its doorway's edge, <c>rf</c> or <c>fu</c>.</summary>
    public static string SlotKey(PieceSlot slot, long xMm, long zMm, int rotation) => slot switch
    {
        PieceSlot.Square => $"sq:{Index(xMm - HalfMm)}:{Index(zMm - HalfMm)}",
        PieceSlot.Roof => $"rf:{Index(xMm - HalfMm)}:{Index(zMm - HalfMm)}",
        PieceSlot.Furniture => $"fu:{Index(xMm - HalfMm)}:{Index(zMm - HalfMm)}",
        PieceSlot.Edge => EdgeKey(xMm, zMm, rotation),
        _ => "dr:" + EdgeKey(xMm, zMm, rotation),
    };

    /// <summary>An edge's key: <c>ex:i:k</c> along x (even turns) or <c>ez:i:k</c> along z (odd).</summary>
    public static string EdgeKey(long xMm, long zMm, int rotation) => $"{(rotation % 2 == 0 ? "ex" : "ez")}:{Index(xMm)}:{Index(zMm)}";

    /// <summary>
    /// The ground a slot occupies, for area containment: a square's square, or an edge's (or a door's) segment - a rectangle with no
    /// width across the line.
    /// </summary>
    public static BoundsMm Footprint(PieceSlot slot, long xMm, long zMm, int rotation) => slot switch
    {
        PieceSlot.Square or PieceSlot.Roof or PieceSlot.Furniture => new BoundsMm(xMm - HalfMm, zMm - HalfMm, xMm + HalfMm, zMm + HalfMm),
        _ => rotation % 2 == 0 ? new BoundsMm(xMm - HalfMm, zMm, xMm + HalfMm, zMm) : new BoundsMm(xMm, zMm - HalfMm, xMm, zMm + HalfMm),
    };

    /// <summary>The four edges of the square centred at (x, z) - south, east, north, west - each as an edge anchor and turn.</summary>
    public static ImmutableArray<(long XMm, long ZMm, int Rotation)> EdgesOfSquare(long xMm, long zMm) =>
        ImmutableArray.Create((xMm, zMm - HalfMm, 0), (xMm + HalfMm, zMm, 1), (xMm, zMm + HalfMm, 0), (xMm - HalfMm, zMm, 1));

    /// <summary>The two squares an edge lies between.</summary>
    public static ImmutableArray<(long XMm, long ZMm)> SquaresBesideEdge(long xMm, long zMm, int rotation) => rotation % 2 == 0
        ? ImmutableArray.Create((xMm, zMm - HalfMm), (xMm, zMm + HalfMm))
        : ImmutableArray.Create((xMm - HalfMm, zMm), (xMm + HalfMm, zMm));

    /// <summary>The four squares that share an edge with the square centred at (x, z): south, east, north, west.</summary>
    public static ImmutableArray<(long XMm, long ZMm)> NeighbourSquares(long xMm, long zMm) =>
        ImmutableArray.Create((xMm, zMm - ModuleMm), (xMm + ModuleMm, zMm), (xMm, zMm + ModuleMm), (xMm - ModuleMm, zMm));

    /// <summary>
    /// Roof support (M7 design §4.5, check 8), depth one and evaluated now only: a piece that supports a roof on one of the square's four
    /// edges, or an edge-adjacent roof that itself has one. <paramref name="edgeSupports"/> answers for an edge key,
    /// <paramref name="roofAt"/> for a roof slot key. Nothing is simulated afterwards: taking a wall away leaves its roofs standing.
    /// </summary>
    public static bool RoofSupported(long xMm, long zMm, Func<string, bool> edgeSupports, Func<string, bool> roofAt)
    {
        bool Direct(long x, long z) => EdgesOfSquare(x, z).Any(e => edgeSupports(EdgeKey(e.XMm, e.ZMm, e.Rotation)));
        return Direct(xMm, zMm)
               || NeighbourSquares(xMm, zMm).Any(n => roofAt(SlotKey(PieceSlot.Roof, n.XMm, n.ZMm, 0)) && Direct(n.XMm, n.ZMm));
    }
}

/// <summary>Quarter turns (M7 design §4.2): exact integer maps of points, boxes, facings and socket axes.</summary>
public static class QuarterTurn
{
    /// <summary>A local point turned by r quarter turns: local +Z points along world facing r x 90 degrees, clockwise towards +X.</summary>
    public static (long XMm, long ZMm) Apply(long lxMm, long lzMm, int rotation) => (rotation & 3) switch
    {
        0 => (lxMm, lzMm),
        1 => (lzMm, -lxMm),
        2 => (-lxMm, -lzMm),
        _ => (-lzMm, lxMm),
    };

    /// <summary>A local box turned: the box bounding its turned corners, which is exact.</summary>
    public static BoundsMm Apply(BoundsMm local, int rotation)
    {
        var (ax, az) = Apply(local.MinXMm, local.MinZMm, rotation);
        var (bx, bz) = Apply(local.MaxXMm, local.MaxZMm, rotation);
        return new BoundsMm(Math.Min(ax, bx), Math.Min(az, bz), Math.Max(ax, bx), Math.Max(az, bz));
    }

    /// <summary>A local facing turned, in millidegrees on [0, 360 000).</summary>
    public static int Facing(int localMdeg, int rotation) => (int)(((localMdeg + (rotation & 3) * 90_000L) % 360_000 + 360_000) % 360_000);

    /// <summary>A socket's axis turned: an odd turn swaps x and z.</summary>
    public static SocketAxis Apply(SocketAxis axis, int rotation) =>
        rotation % 2 == 0 || axis == SocketAxis.None ? axis : axis == SocketAxis.X ? SocketAxis.Z : SocketAxis.X;
}

/// <summary>A piece's world geometry from its definition and pose, and the integer tests placement is made of (M7 design §4.5).</summary>
public static class BuildingMath
{
    /// <summary>A local point of a piece at (x, z, r), in world millimetres.</summary>
    public static (long XMm, long ZMm) World(long lxMm, long lzMm, long xMm, long zMm, int rotation)
    {
        var (dx, dz) = QuarterTurn.Apply(lxMm, lzMm, rotation);
        return (xMm + dx, zMm + dz);
    }

    public static BoundsMm WorldBounds(PieceDefinition piece, long xMm, long zMm, int rotation) =>
        QuarterTurn.Apply(piece.Bounds, rotation).Offset(xMm, zMm);

    /// <summary>The piece's blocking parts in the world, named <c>{idPrefix}#{i}</c>, standing on the ground.</summary>
    public static ImmutableArray<BoxBlocker> WorldParts(PieceDefinition piece, long xMm, long zMm, int rotation, string idPrefix) =>
        piece.Parts.Select((part, i) =>
        {
            var box = QuarterTurn.Apply(part.Box, rotation).Offset(xMm, zMm);
            return new BoxBlocker($"{idPrefix}#{i}", box.MinXMm, box.MinZMm, box.MaxXMm, box.MaxZMm, part.HeightMm);
        }).ToImmutableArray();

    /// <summary>The piece's sockets in the world: positions turned and moved, axes turned.</summary>
    public static ImmutableArray<PieceSocket> WorldSockets(PieceDefinition piece, long xMm, long zMm, int rotation) =>
        piece.Sockets.Select(s =>
        {
            var (x, z) = World(s.XMm, s.ZMm, xMm, zMm, rotation);
            return new PieceSocket(s.Type, x, z, QuarterTurn.Apply(s.Axis, rotation));
        }).ToImmutableArray();

    /// <summary>The provider a mount mates with.</summary>
    public static SocketType ProviderFor(SocketType mount) => mount switch
    {
        SocketType.EdgeMount => SocketType.Edge,
        SocketType.SquareMount => SocketType.Square,
        SocketType.DoorMount => SocketType.Door,
        _ => throw new ArgumentOutOfRangeException(nameof(mount), mount, "Not a mount"),
    };

    public static bool IsMount(SocketType type) => type is SocketType.EdgeMount or SocketType.SquareMount or SocketType.DoorMount;

    /// <summary>Strict overlap of two boxes: touching is not overlap.</summary>
    public static bool Overlaps(BoundsMm a, BoundsMm b) =>
        a.MinXMm < b.MaxXMm && b.MinXMm < a.MaxXMm && a.MinZMm < b.MaxZMm && b.MinZMm < a.MaxZMm;

    /// <summary>Strict overlap of a box and a blocker: a box by <see cref="Overlaps(BoundsMm, BoundsMm)"/>, a circle by its clamp distance.</summary>
    public static bool Overlaps(BoundsMm box, Blocker blocker) => blocker switch
    {
        BoxBlocker b => Overlaps(box, new BoundsMm(b.MinXMm, b.MinZMm, b.MaxXMm, b.MaxZMm)),
        CircleBlocker c => DistanceSquared(box, c.CenterXMm, c.CenterZMm) < c.RadiusMm * c.RadiusMm,
        _ => throw new ArgumentOutOfRangeException(nameof(blocker), blocker.GetType().Name, "Unknown blocker shape"),
    };

    /// <summary>The squared distance from a point to the nearest point of a box, 0 inside it.</summary>
    public static long DistanceSquared(BoundsMm box, long xMm, long zMm)
    {
        long dx = xMm < box.MinXMm ? box.MinXMm - xMm : xMm > box.MaxXMm ? xMm - box.MaxXMm : 0;
        long dz = zMm < box.MinZMm ? box.MinZMm - zMm : zMm > box.MaxZMm ? zMm - box.MaxZMm : 0;
        return dx * dx + dz * dz;
    }

    /// <summary>
    /// The ground's rise under the square centred at (x, z): highest minus lowest of the terrain over a 13 x 13 lattice at 250 mm across
    /// the square, its edges included (M7 design §4.5, check 9).
    /// </summary>
    public static long Relief(TerrainGrid terrain, long xMm, long zMm)
    {
        long low = long.MaxValue, high = long.MinValue;
        for (long j = 0; j <= 12; j++)
        {
            for (long i = 0; i <= 12; i++)
            {
                long h = terrain.HeightAtMm(xMm - Lattice.HalfMm + i * 250, zMm - Lattice.HalfMm + j * 250);
                low = Math.Min(low, h);
                high = Math.Max(high, h);
            }
        }
        return high - low;
    }

    /// <summary>What taking a piece down gives back of one cost line: the percentage, rounded down.</summary>
    public static int Refund(int count, int refundPercent) => (int)((long)count * refundPercent / 100);
}

/// <summary>A placed piece as the snapper sees it: which it is, and where.</summary>
public sealed record PiecePose(EntityId Id, string DefId, long XMm, long ZMm, int Rotation);

/// <summary>
/// Aim to pose (M7 design §4.7), pure and called by presentation: the aim point is where the camera's centre ray meets the domain
/// terrain. Squares, roofs and furniture take the square the aim is in; an edge the nearest line of its axis; a door the nearest intact
/// doorway without one within 2 m. Every division floors, so an aim on a half-way line resolves one way, never by a tie.
/// </summary>
public static class Snapper
{
    public const long DoorSnapMm = 2_000;

    public static (long XMm, long ZMm, int Rotation)? Snap(BuildingCatalog catalog, IReadOnlyList<PiecePose> pieces, string pieceDefId,
        long aimXMm, long aimZMm, int rotation)
    {
        if (catalog.Find(pieceDefId) is not { } piece)
            return null;
        const long m = Lattice.ModuleMm, h = Lattice.HalfMm;
        int r = rotation & 3;
        switch (piece.Slot)
        {
            case PieceSlot.Square or PieceSlot.Roof or PieceSlot.Furniture:
                return (m * NavGeometry.FloorDiv(aimXMm, m) + h, m * NavGeometry.FloorDiv(aimZMm, m) + h, r);
            case PieceSlot.Edge:
                return r % 2 == 0
                    ? (m * NavGeometry.FloorDiv(aimXMm, m) + h, m * NavGeometry.FloorDiv(aimZMm + h, m), r)
                    : (m * NavGeometry.FloorDiv(aimXMm + h, m), m * NavGeometry.FloorDiv(aimZMm, m) + h, r);
        }

        // A door: the nearest doorway without one, by distance, then the lower x, then the lower z.
        bool IsFamily(PiecePose p, PieceFamily family) => catalog.Find(p.DefId)?.Family == family;
        var hung = pieces.Where(p => IsFamily(p, PieceFamily.Door)).Select(p => (p.XMm, p.ZMm)).ToHashSet();
        var best = pieces.Where(p => IsFamily(p, PieceFamily.Doorway) && !hung.Contains((p.XMm, p.ZMm)))
            .Select(p => (D2: (p.XMm - aimXMm) * (p.XMm - aimXMm) + (p.ZMm - aimZMm) * (p.ZMm - aimZMm), Pose: p))
            .Where(c => c.D2 <= DoorSnapMm * DoorSnapMm)
            .OrderBy(c => c.D2).ThenBy(c => c.Pose.XMm).ThenBy(c => c.Pose.ZMm)
            .Select(c => c.Pose).FirstOrDefault();
        if (best is null)
            return null;
        // The two legal turns only choose which side the leaf swings to.
        return (best.XMm, best.ZMm, r % 2 == best.Rotation % 2 ? r : (r + 1) & 3);
    }
}
