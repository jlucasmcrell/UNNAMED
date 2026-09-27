// UNNAMED Presentation - the pieces the player builds, drawn (M7 design §9.3)
// Godot presentation only: it draws the simulation's piece views and holds no gameplay state (D-11)

using Godot;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// The greybox kit (M7 design §9.3): each placed piece from its view's parts and bounds in domain millimetres - a pad draped over the
/// ground, walls and doorway jambs as boxes with a drawn lintel, a roof as a slab at wall top - with camera colliders; the same builder
/// draws the placement ghost. Events only mark it dirty; <see cref="Sync"/> rebuilds at most once a frame, keyed on the structure
/// revision (P-02, P-03). No drawing code names a content ID. The build-area outlines show in build mode and F2.
/// </summary>
public partial class StructuresView : Node3D
{
    private const float LintelBottomM = 2.4f, WallTopM = 3.0f;

    private readonly SortedDictionary<string, Node3D> _built = new(StringComparer.Ordinal);
    private readonly Node3D _outlines = new() { Name = "BuildAreaOutlines", Visible = false };
    private readonly Node3D _markers = new() { Name = "DebugMarkers", Visible = false };
    private TerrainGrid _terrain = null!;
    private BuildingCatalog _catalog = BuildingCatalog.Empty;
    private bool _buildOutlines, _debugOutlines;
    private double _markersSince = double.MaxValue;
    private Art.ArtCoverage? _coverage;
    private long _syncedRevision = -1;
    private string? _highlighted;

    /// <summary>Set by an event: the next frame's <see cref="SyncIfDirty"/> rebuilds.</summary>
    public bool Dirty { get; private set; } = true;

    /// <summary>How many pieces are drawn.</summary>
    public int Count => _built.Count;

    /// <summary>The pieces drawn, by ID: what a scripted run checks.</summary>
    public IReadOnlyCollection<string> Drawn => _built.Keys;

    public void Bind(RegionLayout layout, BuildingCatalog catalog, Art.ArtCoverage coverage)
    {
        _terrain = layout.Space.Terrain;
        _catalog = catalog;
        _coverage = coverage;
        AddChild(_outlines);
        AddChild(_markers);
        foreach (var area in layout.BuildAreas)
        {
            _outlines.AddChild(Outline(area));
            coverage.Resolved("overlay", area.Key, "m7_effect");
        }
    }

    public void MarkDirty() => Dirty = true;

    public void SyncIfDirty(Simulation simulation)
    {
        if (Dirty || simulation.StructureRevision != _syncedRevision)
            Sync(simulation);
    }

    /// <summary>Every placed piece drawn, and nothing else: new ones built, gone ones freed. Idempotent.</summary>
    public void Sync(Simulation simulation)
    {
        var pieces = simulation.Pieces.ToDictionary(p => p.Id.Value, p => p, StringComparer.Ordinal);
        foreach (string gone in _built.Keys.Where(id => !pieces.ContainsKey(id)).ToList())
        {
            _built[gone].QueueFree();
            _built.Remove(gone);
        }
        foreach (var (id, piece) in pieces.Where(p => !_built.ContainsKey(p.Key)))
        {
            var node = BuildPiece(piece, _terrain, null);
            node.Name = id;
            AddChild(node);
            _built[id] = node;
            _coverage?.Fallback("piece", piece.DefId, "M7 ships no piece art");
        }
        if (_highlighted is not null && !_built.ContainsKey(_highlighted))
            _highlighted = null;
        _syncedRevision = simulation.StructureRevision;
        Dirty = false;
    }

    /// <summary>A placed door's leaf swung open or shut on its hinge; any other key is ignored.</summary>
    public void SetDoor(string key, bool open)
    {
        if (_built.TryGetValue(key, out var node) && node.GetNodeOrNull<Node3D>(HingeName) is { } hinge)
            Swing(hinge, open);
    }

    /// <summary>Where a placed door's leaf is drawn now, its centre in world metres: what a scripted run checks the swing by.</summary>
    public Vector3? LeafCentre(string key) =>
        _built.TryGetValue(key, out var node) && node.GetNodeOrNull<Node3D>($"{HingeName}/leaf") is { } leaf ? leaf.GlobalPosition : null;

    /// <summary>One piece drawn over with a translucent overlay - the target, or the one armed to come down - and every other plain.</summary>
    public void Highlight(string? key, Material? overlay)
    {
        if (_highlighted is not null && _built.TryGetValue(_highlighted, out var before))
            Overlay(before, null);
        _highlighted = key is not null && overlay is not null && _built.ContainsKey(key) ? key : null;
        if (_highlighted is not null)
            Overlay(_built[_highlighted], overlay);
    }

    /// <summary>The build-area outlines, shown while building - and in F2 (<see cref="ShowDebug"/>).</summary>
    public void ShowOutlines(bool show)
    {
        _buildOutlines = show;
        _outlines.Visible = _buildOutlines || _debugOutlines;
    }

    /// <summary>F2's structure markers on or off; the outlines show with them.</summary>
    public void ShowDebug(bool show)
    {
        _debugOutlines = show;
        _outlines.Visible = _buildOutlines || _debugOutlines;
        _markers.Visible = show;
        _markersSince = double.MaxValue;
        if (!show)
            Clear(_markers);
    }

    /// <summary>How many sockets and ID tails F2 drew last, for a scripted run.</summary>
    public int Markers => _markers.GetChildCount();

    /// <summary>
    /// F2's structure markers (M7 design §8.14): the providers of the pieces within 12 m as 0.15 m cubes (edges green, squares blue,
    /// doorways orange), each such piece's ID over it, and the last change's rectangle - redrawn at most twice a second.
    /// </summary>
    public void DrawDebug(Simulation simulation, Vector3 viewer, StructuresChanged? last, double delta)
    {
        if (!_markers.Visible)
            return;
        _markersSince += delta;
        if (_markersSince < 0.5)
            return;
        _markersSince = 0;
        Clear(_markers);
        long x0 = (long)Math.Round(viewer.X * 1000), z0 = (long)Math.Round(viewer.Z * 1000);
        foreach (var piece in simulation.Pieces.Where(p => BuildingMath.DistanceSquared(new BoundsMm(p.MinXMm, p.MinZMm, p.MaxXMm, p.MaxZMm), x0, z0) <= 144_000_000))
        {
            if (_catalog.Find(piece.DefId) is { } definition)
            {
                foreach (var socket in BuildingMath.WorldSockets(definition, piece.XMm, piece.ZMm, piece.Rotation).Where(s => !BuildingMath.IsMount(s.Type)))
                {
                    var colour = socket.Type switch
                    {
                        SocketType.Edge => new Color(0.3f, 0.9f, 0.3f),
                        SocketType.Square => new Color(0.3f, 0.5f, 1f),
                        _ => new Color(1f, 0.6f, 0.2f),
                    };
                    _markers.AddChild(new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(0.15f, 0.15f, 0.15f) },
                        MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
                        Position = HollowView.ToGodot(socket.XMm, _terrain.HeightAtMm(socket.XMm, socket.ZMm) + 150, socket.ZMm),
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    });
                }
            }
            _markers.AddChild(new Label3D
            {
                Text = piece.Id.Value[^6..],
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 48,
                Position = HollowView.ToGodot((piece.MinXMm + piece.MaxXMm) / 2, _terrain.HeightAtMm(piece.XMm, piece.ZMm) + 3_400, (piece.MinZMm + piece.MaxZMm) / 2),
            });
        }
        if (last is { } change)
        {
            foreach (var (ax, az, bx, bz) in new[] { (change.MinXMm, change.MinZMm, change.MaxXMm, change.MinZMm), (change.MaxXMm, change.MinZMm, change.MaxXMm, change.MaxZMm),
                         (change.MaxXMm, change.MaxZMm, change.MinXMm, change.MaxZMm), (change.MinXMm, change.MaxZMm, change.MinXMm, change.MinZMm) })
            {
                var from = HollowView.ToGodot(ax, _terrain.HeightAtMm(ax, az) + 60, az);
                var to = HollowView.ToGodot(bx, _terrain.HeightAtMm(bx, bz) + 60, bz);
                if (from.DistanceTo(to) < 0.01f)
                    continue;
                var edge = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.06f, from.DistanceTo(to)) },
                    MaterialOverride = Palette.GhostUnchecked,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                _markers.AddChild(edge);
                edge.LookAtFromPosition((from + to) / 2, to, Vector3.Up);
            }
        }
    }

    private static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    public bool OutlinesShown => _outlines.Visible;

    private static void Overlay(Node node, Material? overlay)
    {
        if (node is GeometryInstance3D geometry)
            geometry.MaterialOverlay = overlay;
        foreach (var child in node.GetChildren())
            Overlay(child, overlay);
    }

    // ── the builder ─────────────────────────────────────────────────────────

    /// <summary>
    /// One piece, or the ghost of one (<paramref name="ghost"/> its material): a pad's drape; a roof's slab at wall top; otherwise a box
    /// per part, and a doorway's lintel over its opening. The ghost casts no shadow and has no colliders.
    /// </summary>
    public static Node3D BuildPiece(PieceView piece, TerrainGrid terrain, Material? ghost)
    {
        var root = new Node3D();
        var bounds = new BoxBlocker("bounds", piece.MinXMm, piece.MinZMm, piece.MaxXMm, piece.MaxZMm, 0);
        switch (piece.Family)
        {
            case PieceFamily.Pad:
                root.AddChild(Drape(bounds, terrain, ghost ?? Palette.Shaft));
                break;
            case PieceFamily.Roof:
            {
                var size = new Vector3((bounds.MaxXMm - bounds.MinXMm) / 1000f, 0.2f, (bounds.MaxZMm - bounds.MinZMm) / 1000f);
                var slab = Part("roof", size, ghost ?? Palette.Roof, ghost is null);
                slab.Position = new Vector3(bounds.CenterXMm / 1000f, LowestUnder(terrain, bounds) + WallTopM + 0.1f, bounds.CenterZMm / 1000f);
                root.AddChild(slab);
                break;
            }
            case PieceFamily.Door:
                root.AddChild(Hinge(piece, terrain, ghost ?? Palette.Door, ghost is null));
                break;
            default:
            {
                var material = ghost ?? piece.Family switch
                {
                    PieceFamily.Door => Palette.Door,
                    PieceFamily.Storage => Palette.Leather,
                    _ => Palette.Wood,
                };
                for (int i = 0; i < piece.Parts.Length; i++)
                {
                    var part = piece.Parts[i];
                    var box = new BoxBlocker($"part{i}", part.MinXMm, part.MinZMm, part.MaxXMm, part.MaxZMm, part.HeightMm);
                    float baseY = LowestUnder(terrain, box) - 0.2f;
                    float height = part.HeightMm / 1000f + 0.2f;
                    var node = Part($"part{i}", new Vector3((part.MaxXMm - part.MinXMm) / 1000f, height, (part.MaxZMm - part.MinZMm) / 1000f), material, ghost is null);
                    node.Position = new Vector3(box.CenterXMm / 1000f, baseY + height / 2, box.CenterZMm / 1000f);
                    root.AddChild(node);
                }
                if (piece.Family == PieceFamily.Doorway)
                    root.AddChild(Lintel(piece, terrain, material, ghost is null));
                if (piece.Family == PieceFamily.Station)
                    root.AddChild(AnvilBlock(piece, terrain, ghost ?? Palette.Iron));
                break;
            }
        }
        if (ghost is not null)
            NoShadows(root);
        return root;
    }

    /// <summary>
    /// The bench's anvil (M7 design §9.3.1): a 0.62 x 0.24 x 0.26 m iron block on the bench's top, its long side along the bench's, drawn
    /// only - the box is the bench's collider.
    /// </summary>
    private static Node3D AnvilBlock(PieceView bench, TerrainGrid terrain, Material material)
    {
        var part = bench.Parts[0];
        var box = new BoxBlocker("bench", part.MinXMm, part.MinZMm, part.MaxXMm, part.MaxZMm, part.HeightMm);
        bool alongX = part.MaxXMm - part.MinXMm >= part.MaxZMm - part.MinZMm;
        var block = Part("anvil", alongX ? new Vector3(0.62f, 0.26f, 0.24f) : new Vector3(0.24f, 0.26f, 0.62f), material, false);
        block.Position = new Vector3(box.CenterXMm / 1000f, LowestUnder(terrain, box) + part.HeightMm / 1000f + 0.13f, box.CenterZMm / 1000f);
        return block;
    }

    private const string HingeName = "hinge";

    /// <summary>
    /// A door's leaf on its hinge (M7 design §9.3.3): the hinge at R_r(-800, 0) from the anchor, the leaf reaching from it along the closed
    /// direction R_r(+1, 0). It swings through the angle that turns the closed direction onto the open one, R_r(0, +1) - never a number kept
    /// per turn - and stands as the piece's row says.
    /// </summary>
    private static Node3D Hinge(PieceView door, TerrainGrid terrain, Material material, bool collider)
    {
        var part = door.Parts[0];
        var leaf = new BoxBlocker("leaf", part.MinXMm, part.MinZMm, part.MaxXMm, part.MaxZMm, part.HeightMm);
        var (hx, hz) = QuarterTurn.Apply(-800, 0, door.Rotation);
        var (cx, cz) = QuarterTurn.Apply(1, 0, door.Rotation);
        var (ox, oz) = QuarterTurn.Apply(0, 1, door.Rotation);
        // HollowView's convention: +90 degrees about Y turns +Z to +X, so +90 takes (x, z) to (z, -x).
        float openDegrees = (cz, -cx) == (ox, oz) ? 90 : -90;
        float ground = LowestUnder(terrain, leaf);
        var hinge = new Node3D { Name = HingeName, Position = new Vector3((door.XMm + hx) / 1000f, 0, (door.ZMm + hz) / 1000f) };
        hinge.SetMeta("open_degrees", openDegrees);
        float height = part.HeightMm / 1000f;
        var node = Part("leaf", new Vector3((part.MaxXMm - part.MinXMm) / 1000f, height, (part.MaxZMm - part.MinZMm) / 1000f), material, collider);
        node.Position = new Vector3(leaf.CenterXMm / 1000f - hinge.Position.X, ground + height / 2, leaf.CenterZMm / 1000f - hinge.Position.Z);
        hinge.AddChild(node);
        Swing(hinge, door.DoorOpen);
        return hinge;
    }

    private static void Swing(Node3D hinge, bool open) =>
        hinge.RotationDegrees = new Vector3(0, open ? (float)hinge.GetMeta("open_degrees") : 0, 0);

    /// <summary>A doorway's lintel over its opening, drawn only (it blocks nothing): the gap between its jambs, 2.4 to 3.0 m up.</summary>
    private static Node3D Lintel(PieceView doorway, TerrainGrid terrain, Material material, bool collider)
    {
        // The opening is what lies between the jambs, across the doorway's thin axis: its bounds less its parts.
        var jambs = doorway.Parts.OrderBy(p => p.MinXMm).ThenBy(p => p.MinZMm).ToList();
        bool alongX = doorway.MaxXMm - doorway.MinXMm > doorway.MaxZMm - doorway.MinZMm;
        var gap = alongX
            ? new BoxBlocker("lintel", jambs[0].MaxXMm, doorway.MinZMm, jambs[^1].MinXMm, doorway.MaxZMm, 0)
            : new BoxBlocker("lintel", doorway.MinXMm, jambs.OrderBy(p => p.MinZMm).First().MaxZMm, doorway.MaxXMm, jambs.OrderBy(p => p.MinZMm).Last().MinZMm, 0);
        float ground = LowestUnder(terrain, gap);
        var size = new Vector3((gap.MaxXMm - gap.MinXMm) / 1000f, WallTopM - LintelBottomM, (gap.MaxZMm - gap.MinZMm) / 1000f);
        var node = Part("lintel", size, material, collider);
        node.Position = new Vector3(gap.CenterXMm / 1000f, ground + (LintelBottomM + WallTopM) / 2, gap.CenterZMm / 1000f);
        return node;
    }

    /// <summary>
    /// A pad's floor (M7 design §9.3.4): a 13 x 13 vertex grid at 250 mm, each vertex 20 mm over the domain terrain, with a 150 mm skirt
    /// hanging from its edge - so feet neither float nor sink, since the terrain is what the body stands on.
    /// </summary>
    private static MeshInstance3D Drape(BoxBlocker square, TerrainGrid terrain, Material material)
    {
        const int n = 12;
        long step = (square.MaxXMm - square.MinXMm) / n;
        Vector3 At(int i, int j) => HollowView.ToGodot(square.MinXMm + i * step, terrain.HeightAtMm(square.MinXMm + i * step, square.MinZMm + j * step) + 20,
            square.MinZMm + j * step);
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            // The terrain's own split and winding: clockwise seen from above.
            foreach (var (di, dj) in new[] { (0, 0), (1, 0), (1, 1), (0, 0), (1, 1), (0, 1) })
                tool.AddVertex(At(i + di, j + dj));
        }
        // The skirt, both faces: it is seen from outside the pad and, through a doorway, from within.
        var edge = new List<(int I, int J)>();
        for (int k = 0; k < n; k++)
            edge.Add((k, 0));
        for (int k = 0; k < n; k++)
            edge.Add((n, k));
        for (int k = n; k > 0; k--)
            edge.Add((k, n));
        for (int k = n; k > 0; k--)
            edge.Add((0, k));
        var skirt = new Vector3(0, -0.15f, 0);
        for (int e = 0; e < edge.Count; e++)
        {
            var a = At(edge[e].I, edge[e].J);
            var b = At(edge[(e + 1) % edge.Count].I, edge[(e + 1) % edge.Count].J);
            foreach (var v in new[] { a, b, b + skirt, a, b + skirt, a + skirt, a, b + skirt, b, a, a + skirt, b + skirt })
                tool.AddVertex(v);
        }
        tool.GenerateNormals();
        return new MeshInstance3D { Name = "drape", Mesh = tool.Commit(), MaterialOverride = material };
    }

    /// <summary>A build area's outline: four 0.1 m strips along its edges, sampled every metre at 30 mm over the ground.</summary>
    private Node3D Outline(BuildAreaSite area)
    {
        var node = new Node3D { Name = area.Key };
        var corners = new[] { (area.MinXMm, area.MinZMm), (area.MaxXMm, area.MinZMm), (area.MaxXMm, area.MaxZMm), (area.MinXMm, area.MaxZMm) };
        for (int side = 0; side < 4; side++)
        {
            var (x0, z0) = corners[side];
            var (x1, z1) = corners[(side + 1) % 4];
            long length = Math.Max(Math.Abs(x1 - x0), Math.Abs(z1 - z0));
            for (long along = 0; along < length; along += 1_000)
            {
                long x = x0 + (x1 - x0) * along / length, z = z0 + (z1 - z0) * along / length;
                long nx = x0 + (x1 - x0) * Math.Min(length, along + 1_000) / length, nz = z0 + (z1 - z0) * Math.Min(length, along + 1_000) / length;
                var from = HollowView.ToGodot(x, _terrain.HeightAtMm(x, z) + 30, z);
                var to = HollowView.ToGodot(nx, _terrain.HeightAtMm(nx, nz) + 30, nz);
                var strip = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.1f, 0.02f, from.DistanceTo(to)) },
                    MaterialOverride = Palette.AreaOutline,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                node.AddChild(strip);
                strip.LookAtFromPosition((from + to) / 2, to, Vector3.Up);
            }
        }
        return node;
    }

    private static MeshInstance3D Part(string name, Vector3 size, Material material, bool collider)
    {
        var node = new MeshInstance3D { Name = name, Mesh = new BoxMesh { Size = size }, MaterialOverride = material };
        if (collider)
        {
            var body = new StaticBody3D { CollisionLayer = HollowView.CameraCollisionLayer, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            node.AddChild(body);
        }
        return node;
    }

    private static void NoShadows(Node node)
    {
        if (node is GeometryInstance3D geometry)
            geometry.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        foreach (var child in node.GetChildren())
            NoShadows(child);
    }

    /// <summary>The lowest ground under a footprint's corners and centre (copied from HollowView, which stays as it is: M7 design §9.3.2).</summary>
    private static float LowestUnder(TerrainGrid terrain, BoxBlocker box) =>
        new[]
        {
            terrain.HeightAtMm(box.MinXMm, box.MinZMm), terrain.HeightAtMm(box.MaxXMm, box.MinZMm),
            terrain.HeightAtMm(box.MinXMm, box.MaxZMm), terrain.HeightAtMm(box.MaxXMm, box.MaxZMm),
            terrain.HeightAtMm(box.CenterXMm, box.CenterZMm),
        }.Min() / 1000f;
}
