// UNNAMED Presentation - the greybox hollow, built from the region layout the domain collides against
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Builds Ashen Hollow from <see cref="RegionLayout"/>: the terrain surface (the same triangles the domain samples),
/// every structure and door as a box or cylinder, and scenery the domain never sees - roofs, the stream's water, the
/// ravine, trees' canopies, the sky. Colliders exist only for the camera; the body collides in the domain.
/// </summary>
public partial class HollowView : Node3D
{
    /// <summary>The physics layer the camera's spring arm collides with.</summary>
    public const uint CameraCollisionLayer = 1;

    private readonly Dictionary<string, (Node3D Hinge, float OpenDegrees)> _doors = new(StringComparer.Ordinal);
    private Node3D? _debug;

    public static Vector3 ToGodot(long xMm, long yMm, long zMm) => new(xMm / 1000f, yMm / 1000f, zMm / 1000f);

    public void Build(RegionLayout layout)
    {
        var terrain = layout.Space.Terrain;
        AddChild(BuildTerrain(terrain));
        foreach (var blocker in layout.Space.Blockers)
            AddChild(BuildStructure(blocker, terrain));
        foreach (var roof in Roofs(layout))
            AddChild(roof);
        foreach (var door in layout.Doors)
            AddChild(BuildDoor(door, layout, terrain));
        AddChild(BuildWater(layout.Space));
        AddChild(BuildRavine(layout.Space, terrain));
        AddChild(BuildDebugMarkers(layout));
        AddChild(BuildLighting());
    }

    public void SetDoor(string key, bool open)
    {
        if (_doors.TryGetValue(key, out var door))
            door.Hinge.RotationDegrees = new Vector3(0, open ? door.OpenDegrees : 0, 0);
    }

    public bool DebugVisible
    {
        get => _debug?.Visible ?? false;
        set
        {
            if (_debug is not null)
                _debug.Visible = value;
        }
    }

    private static Node3D BuildTerrain(TerrainGrid grid)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (int j = 0; j < grid.Rows; j++)
        for (int i = 0; i < grid.Columns; i++)
        {
            long x = grid.OriginXMm + i * grid.SpacingMm, z = grid.OriginZMm + j * grid.SpacingMm;
            tool.SetUV(new Vector2(i, j));
            tool.AddVertex(ToGodot(x, grid.HeightAt(i, j), z));
        }
        // The domain's split: every quad along its (0,0)-(1,1) diagonal, clockwise seen from above (Godot's front face).
        for (int j = 0; j < grid.Rows - 1; j++)
        for (int i = 0; i < grid.Columns - 1; i++)
        {
            int p00 = j * grid.Columns + i, p10 = p00 + 1, p01 = p00 + grid.Columns, p11 = p01 + 1;
            foreach (int index in new[] { p00, p10, p11, p00, p11, p01 })
                tool.AddIndex(index);
        }
        tool.GenerateNormals();
        var mesh = tool.Commit();
        var node = new MeshInstance3D { Name = "Terrain", Mesh = mesh, MaterialOverride = Palette.Terrain };
        var body = new StaticBody3D { CollisionLayer = CameraCollisionLayer, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
        node.AddChild(body);
        return node;
    }

    private static Node3D BuildStructure(Blocker blocker, TerrainGrid terrain)
    {
        switch (blocker)
        {
            case BoxBlocker box:
            {
                float baseY = LowestUnder(terrain, box) - 0.2f;
                float height = box.HeightMm / 1000f + 0.2f;
                var size = new Vector3((box.MaxXMm - box.MinXMm) / 1000f, height, (box.MaxZMm - box.MinZMm) / 1000f);
                var node = Solid(box.Id, new BoxMesh { Size = size }, new BoxShape3D { Size = size }, MaterialFor(box.Id));
                node.Position = new Vector3(box.CenterXMm / 1000f, baseY + height / 2, box.CenterZMm / 1000f);
                return node;
            }
            case CircleBlocker circle:
            {
                float radius = circle.RadiusMm / 1000f;
                float baseY = terrain.HeightAtMm(circle.CenterXMm, circle.CenterZMm) / 1000f - 0.2f;
                float height = circle.HeightMm / 1000f + 0.2f;
                bool tree = circle.Id.StartsWith("tree_", StringComparison.Ordinal);
                var mesh = new CylinderMesh { TopRadius = tree ? radius * 0.7f : radius * 0.8f, BottomRadius = radius, Height = height };
                var node = Solid(circle.Id, mesh, new CylinderShape3D { Radius = radius, Height = height }, tree ? Palette.Trunk : Palette.Rock);
                node.Position = new Vector3(circle.CenterXMm / 1000f, baseY + height / 2, circle.CenterZMm / 1000f);
                if (tree)
                {
                    // Foliage: scenery only, no collider, so the camera passes through it.
                    node.AddChild(new MeshInstance3D
                    {
                        Mesh = new SphereMesh { Radius = 2.4f, Height = 3.6f },
                        MaterialOverride = Palette.Canopy,
                        Position = new Vector3(0, height / 2 + 0.8f, 0),
                    });
                }
                return node;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(blocker), blocker.GetType().Name, "Unknown blocker shape");
        }
    }

    /// <summary>Scenery: a roof over each building, the group of structures sharing an id prefix that forms walls.</summary>
    private static IEnumerable<Node3D> Roofs(RegionLayout layout)
    {
        foreach (string group in new[] { "longhouse", "forge" })
        {
            var walls = layout.Space.Blockers.OfType<BoxBlocker>().Where(b => b.Id.StartsWith(group + "_", StringComparison.Ordinal)).ToList();
            if (walls.Count == 0)
                continue;
            long minX = walls.Min(w => w.MinXMm), maxX = walls.Max(w => w.MaxXMm), minZ = walls.Min(w => w.MinZMm), maxZ = walls.Max(w => w.MaxZMm);
            float top = walls.Max(w => LowestUnder(layout.Space.Terrain, w) - 0.2f + w.HeightMm / 1000f + 0.2f);
            var size = new Vector3((maxX - minX) / 1000f + 0.6f, 0.3f, (maxZ - minZ) / 1000f + 0.6f);
            var roof = Solid(group + "_roof", new BoxMesh { Size = size }, new BoxShape3D { Size = size }, Palette.Roof);
            roof.Position = new Vector3((minX + maxX) / 2000f, top + 0.15f, (minZ + maxZ) / 2000f);
            yield return roof;
        }
    }

    private Node3D BuildDoor(DoorSite door, RegionLayout layout, TerrainGrid terrain)
    {
        var box = door.ClosedFootprint;
        float baseY = LowestUnder(terrain, box);
        float height = box.HeightMm / 1000f;
        bool alongZ = box.MaxZMm - box.MinZMm >= box.MaxXMm - box.MinXMm;
        var size = new Vector3((box.MaxXMm - box.MinXMm) / 1000f, height, (box.MaxZMm - box.MinZMm) / 1000f);
        // Hinged at one end; it swings towards the building's inside, the side where its roof group's walls are.
        var hinge = new Node3D
        {
            Name = door.Key,
            Position = alongZ
                ? new Vector3(box.CenterXMm / 1000f, baseY, box.MinZMm / 1000f)
                : new Vector3(box.MinXMm / 1000f, baseY, box.CenterZMm / 1000f),
        };
        var panel = Solid(door.Key + "_panel", new BoxMesh { Size = size }, new BoxShape3D { Size = size }, Palette.Door);
        panel.Position = alongZ ? new Vector3(0, height / 2, size.Z / 2) : new Vector3(size.X / 2, height / 2, 0);
        hinge.AddChild(panel);

        string group = door.Key["door.".Length..].Split('_')[0];
        var walls = layout.Space.Blockers.OfType<BoxBlocker>().Where(b => b.Id.StartsWith(group + "_", StringComparison.Ordinal)).ToList();
        double inside = walls.Count == 0 ? 0 : alongZ ? walls.Average(w => w.CenterXMm) - box.CenterXMm : walls.Average(w => w.CenterZMm) - box.CenterZMm;
        // Rotating +Z by +90 degrees about Y points it at +X; rotating +X by +90 points it at -Z.
        float open = alongZ ? (inside > 0 ? 90f : -90f) : (inside > 0 ? -90f : 90f);
        _doors[door.Key] = (hinge, open);
        return hinge;
    }

    /// <summary>A still water surface just above the stream bed: the terrain hides it everywhere else.</summary>
    private static Node3D BuildWater(WalkSpace space) => new MeshInstance3D
    {
        Name = "Water",
        Mesh = new PlaneMesh { Size = new Vector2((space.MaxXMm - space.MinXMm) / 1000f, (space.MaxZMm - space.MinZMm) / 1000f) },
        MaterialOverride = Palette.Water,
        Position = new Vector3((space.MinXMm + space.MaxXMm) / 2000f, 0.12f, (space.MinZMm + space.MaxZMm) / 2000f),
    };

    /// <summary>PROTOTYPE.md §3: a sheer ravine on three sides (west, north, east) and a cliff to the south. Scenery.</summary>
    private static Node3D BuildRavine(WalkSpace space, TerrainGrid terrain)
    {
        var root = new Node3D { Name = "Ravine" };
        float minX = space.MinXMm / 1000f, maxX = space.MaxXMm / 1000f, minZ = space.MinZMm / 1000f, maxZ = space.MaxZMm / 1000f;
        float width = maxX - minX, depth = maxZ - minZ;
        void Add(string name, Vector3 size, Vector3 centre)
        {
            var node = Solid(name, new BoxMesh { Size = size }, new BoxShape3D { Size = size }, Palette.Cliff);
            node.Position = centre;
            root.AddChild(node);
        }
        // The near faces drop from just under the edge of the hollow; the far faces rise across a 30 m gulf.
        Add("ravine_west_near", new Vector3(2, 42, depth + 64), new Vector3(minX - 1, -20.5f, minZ + depth / 2));
        Add("ravine_west_far", new Vector3(20, 55, depth + 124), new Vector3(minX - 42, -13, minZ + depth / 2));
        Add("ravine_east_near", new Vector3(2, 42, depth + 64), new Vector3(maxX + 1, -20.5f, minZ + depth / 2));
        Add("ravine_east_far", new Vector3(20, 55, depth + 124), new Vector3(maxX + 42, -13, minZ + depth / 2));
        Add("ravine_north_near", new Vector3(width + 4, 42, 2), new Vector3(minX + width / 2, -20.5f, maxZ + 1));
        Add("ravine_north_far", new Vector3(width + 104, 55, 20), new Vector3(minX + width / 2, -13, maxZ + 42));
        Add("ravine_floor", new Vector3(width + 104, 2, depth + 104), new Vector3(minX + width / 2, -42, minZ + depth / 2));
        Add("cliff_south", new Vector3(width + 104, 16, 12), new Vector3(minX + width / 2, terrain.HeightAtMm(space.MinXMm, space.MinZMm) / 1000f + 6, minZ - 6));
        return root;
    }

    /// <summary>F3 overlay: each named place's discovery radius. Never shown in normal play: no markers (charter §3).</summary>
    private Node3D BuildDebugMarkers(RegionLayout layout)
    {
        _debug = new Node3D { Name = "DebugMarkers", Visible = false };
        var ring = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.85f, 0.2f, 0.35f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        foreach (var place in layout.Locations)
        {
            float radius = place.DiscoveryRadiusMm / 1000f;
            _debug.AddChild(new MeshInstance3D
            {
                Name = place.Id,
                Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.05f },
                MaterialOverride = ring,
                Position = new Vector3(place.XMm / 1000f, layout.Space.Terrain.HeightAtMm(place.XMm, place.ZMm) / 1000f + 0.1f, place.ZMm / 1000f),
            });
        }
        return _debug;
    }

    private static Node3D BuildLighting()
    {
        var root = new Node3D { Name = "Lighting" };
        root.AddChild(new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-48, 35, 0),
            ShadowEnabled = true,
            LightEnergy = 1.1f,
        });
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogDensity = 0.002f,
            FogLightColor = new Color(0.62f, 0.64f, 0.66f),
        };
        root.AddChild(new WorldEnvironment { Environment = environment });
        return root;
    }

    private static MeshInstance3D Solid(string name, Mesh mesh, Shape3D shape, Material material)
    {
        var node = new MeshInstance3D { Name = name, Mesh = mesh, MaterialOverride = material };
        var body = new StaticBody3D { CollisionLayer = CameraCollisionLayer, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = shape });
        node.AddChild(body);
        return node;
    }

    private static StandardMaterial3D MaterialFor(string id) =>
        id.StartsWith("den_rock", StringComparison.Ordinal) ? Palette.Rock : Palette.Wood;

    private static float LowestUnder(TerrainGrid terrain, BoxBlocker box) =>
        new[]
        {
            terrain.HeightAtMm(box.MinXMm, box.MinZMm), terrain.HeightAtMm(box.MaxXMm, box.MinZMm),
            terrain.HeightAtMm(box.MinXMm, box.MaxZMm), terrain.HeightAtMm(box.MaxXMm, box.MaxZMm),
            terrain.HeightAtMm(box.CenterXMm, box.CenterZMm),
        }.Min() / 1000f;
}
