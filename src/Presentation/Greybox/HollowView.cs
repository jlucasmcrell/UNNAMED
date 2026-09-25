// UNNAMED Presentation - the greybox hollow, built from the region layout the domain collides against
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

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

    private readonly Dictionary<string, (Node3D Hinge, Node3D? Leaf, float OpenDegrees)> _doors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _artBuildings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Node3D> _setMarks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Node3D> _barriers = new(StringComparer.Ordinal);
    private Node3D? _debug;
    private Art.ArtLibrary _art = Art.ArtLibrary.Empty;
    private Art.ArtBindings _bindings = Art.ArtBindings.Empty;

    public static Vector3 ToGodot(long xMm, long yMm, long zMm) => new(xMm / 1000f, yMm / 1000f, zMm / 1000f);

    /// <summary>The asset library and its bindings (the Phase-1 asset integration); without them, or for anything they lack, greybox.</summary>
    public void Bind(Art.ArtLibrary art, Art.ArtBindings bindings)
    {
        _art = art;
        _bindings = bindings;
    }

    public void Build(RegionLayout layout)
    {
        var terrain = layout.Space.Terrain;
        AddChild(BuildTerrain(terrain));
        foreach (var building in ArtBuildings(layout, terrain))
            AddChild(building);
        foreach (var blocker in layout.Space.Blockers)
            AddChild(InArtBuilding(blocker.Id) ? Unseen(BuildStructure(blocker, terrain)) : ArtStructure(blocker, terrain) ?? BuildStructure(blocker, terrain));
        foreach (var roof in Roofs(layout))
            AddChild(InArtBuilding(roof.Name) ? Unseen(roof) : roof);
        foreach (var door in layout.Doors)
            AddChild(BuildDoor(door, layout, terrain));
        foreach (var site in layout.Switches)
            AddChild(BuildSetMark(site, terrain));
        foreach (var barrier in layout.Barriers)
            AddChild(BuildBarrier(barrier, terrain));
        AddChild(BuildWater(layout.Space));
        AddChild(BuildRavine(layout.Space, terrain));
        AddChild(BuildDebugMarkers(layout));
        AddChild(BuildLighting());
    }

    public void SetDoor(string key, bool open)
    {
        if (_doors.TryGetValue(key, out var door))
        {
            door.Hinge.RotationDegrees = new Vector3(0, open ? door.OpenDegrees : 0, 0);
            if (door.Leaf is not null)
                door.Leaf.RotationDegrees = door.Hinge.RotationDegrees;
        }
    }

    /// <summary>Show which switches are set and which barriers still stand (M6), as the simulation says.</summary>
    public void SetFlags(IEnumerable<SwitchView> switches, IEnumerable<BarrierView> barriers)
    {
        foreach (var view in switches)
        {
            if (_setMarks.TryGetValue(view.Site.Key, out var mark))
                mark.Visible = view.Set;
        }
        foreach (var view in barriers)
        {
            if (_barriers.TryGetValue(view.Site.Key, out var node))
                node.Visible = view.Standing;
        }
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

    /// <summary>
    /// The ground: the domain's triangles, one surface per cell so each cell can wear its own ground material (the bindings'
    /// <c>terrain</c>); a cell without one, or whose material is unavailable, keeps the greybox colour.
    /// </summary>
    private Node3D BuildTerrain(TerrainGrid grid)
    {
        var cells = new SortedDictionary<string, SurfaceTool>(StringComparer.Ordinal);
        Vector3 At(int i, int j) => ToGodot(grid.OriginXMm + i * grid.SpacingMm, grid.HeightAt(i, j), grid.OriginZMm + j * grid.SpacingMm);
        // The domain's split: every quad along its (0,0)-(1,1) diagonal, clockwise seen from above (Godot's front face).
        for (int j = 0; j < grid.Rows - 1; j++)
        for (int i = 0; i < grid.Columns - 1; i++)
        {
            var centre = (At(i, j) + At(i + 1, j + 1)) / 2;
            string cell = UNNAMED.World.CellKey.OfWorld(centre.X, centre.Z).ToString();
            if (!cells.TryGetValue(cell, out var tool))
            {
                tool = new SurfaceTool();
                tool.Begin(Mesh.PrimitiveType.Triangles);
                cells[cell] = tool;
            }
            foreach (var (di, dj) in new[] { (0, 0), (1, 0), (1, 1), (0, 0), (1, 1), (0, 1) })
            {
                tool.SetUV(new Vector2(i + di, j + dj));
                tool.AddVertex(At(i + di, j + dj));
            }
        }
        var mesh = new ArrayMesh();
        var materials = new List<Material>();
        foreach (var (cell, tool) in cells)
        {
            tool.GenerateNormals();
            tool.Commit(mesh);
            materials.Add(_bindings.Terrain.TryGetValue(cell, out string? id) && _art.WorldMaterial(id) is { } ground ? ground : Palette.Terrain);
        }
        var node = new MeshInstance3D { Name = "Terrain", Mesh = mesh };
        for (int s = 0; s < materials.Count; s++)
            node.SetSurfaceOverrideMaterial(s, materials[s]);
        var body = new StaticBody3D { CollisionLayer = CameraCollisionLayer, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
        node.AddChild(body);
        return node;
    }

    /// <summary>
    /// A structure drawn with the asset library's model for it (the bindings' <c>structures</c>), sized to the footprint the body
    /// collides with, and with the same camera collider as its greybox; or its greybox shape wearing a bound surface material. Null
    /// when the bindings have nothing usable for it, so the greybox is drawn.
    /// </summary>
    /// <summary>
    /// The buildings drawn whole from the asset library, each over the footprint its walls enclose. Their walls and roofs stay as the
    /// camera's colliders, unseen: what the body collides with is the truth.
    /// </summary>
    private IEnumerable<Node3D> ArtBuildings(RegionLayout layout, TerrainGrid terrain)
    {
        foreach (var (prefix, look) in _bindings.Buildings)
        {
            var walls = layout.Space.Blockers.OfType<BoxBlocker>().Where(b => b.Id.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            if (walls.Count == 0)
                continue;
            var footprint = new BoxBlocker(prefix.TrimEnd('_'), walls.Min(w => w.MinXMm), walls.Min(w => w.MinZMm), walls.Max(w => w.MaxXMm),
                walls.Max(w => w.MaxZMm), walls.Max(w => w.HeightMm));
            if (Art.Fitting.Building(_art, look, footprint, terrain) is not { } model)
                continue;
            _artBuildings.Add(prefix);
            yield return model;
        }
    }

    private bool InArtBuilding(string id) => _artBuildings.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>A structure kept only as the camera's collider, its drawing left to the art over it.</summary>
    private static Node3D Unseen(Node3D node)
    {
        if (node is MeshInstance3D mesh)
            mesh.Mesh = null;
        return node;
    }

    private Node3D? ArtStructure(Blocker blocker, TerrainGrid terrain)
    {
        if (_bindings.Structure(blocker.Id) is not { } look)
            return null;
        if (look.Surface is { } surface)
        {
            if (_art.WorldMaterial(surface) is not { } material)
                return null;
            var greybox = BuildStructure(blocker, terrain);
            if (greybox is MeshInstance3D mesh)
                mesh.MaterialOverride = material;
            return greybox;
        }
        if (Art.Fitting.Structure(_art, look, blocker, terrain) is not { } model)
            return null;
        // The camera's collider stays the structure's own shape: what the body collides with is the truth.
        var greyboxShape = BuildStructure(blocker, terrain);
        if (greyboxShape.GetChildren().OfType<StaticBody3D>().FirstOrDefault() is { } body)
        {
            greyboxShape.RemoveChild(body);
            body.Position = greyboxShape.Position - model.Position;
            model.AddChild(body);
        }
        greyboxShape.Free();
        return model;
    }

    private static Node3D BuildStructure(Blocker blocker, TerrainGrid terrain)
    {
        switch (blocker)
        {
            case BoxBlocker box:
            {
                // An overhang (a beam a crouched body passes under) is drawn from its clearance up, not from the ground.
                float baseY = box.ClearanceMm > 0 ? (terrain.HeightAtMm(box.CenterXMm, box.CenterZMm) + box.ClearanceMm) / 1000f : LowestUnder(terrain, box) - 0.2f;
                float height = box.ClearanceMm > 0 ? (box.HeightMm - box.ClearanceMm) / 1000f : box.HeightMm / 1000f + 0.2f;
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
        var root = new Node3D { Name = door.Key + "_door" };
        root.AddChild(hinge);

        string group = door.Key["door.".Length..].Split('_')[0];
        // The door leaf from the asset library, only where its building is drawn from the library too (the building's frame closes the
        // opening round the leaf): at its authored size, never stretched to the opening, centred in it and swung from a hinge of its own
        // at the frame's jamb. The collider stays the opening's panel.
        Node3D? leafHinge = null;
        if (_artBuildings.Contains(group + "_") && _bindings.Doors.TryGetValue(door.Key, out var look) && look.Model is { } leafId
            && _art.Model(leafId) is { } leaf)
        {
            var bounds = Art.ArtGallery.Bounds(leaf);
            float run = alongZ ? size.Z : size.X, jamb = (run - bounds.Size.X) / 2;
            if (jamb < 0 || bounds.Size.Y > height)
            {
                _art.Report($"{leafId} at {door.Key}", $"authored {bounds.Size.X:0.00} x {bounds.Size.Y:0.00} m; the opening is {run:0.00} x {height:0.00} m; "
                    + "not rescaled", leafId);
                leaf.Free();
            }
            else
            {
                // Its hinge edge is its lowest x, its thickness centred in the wall; turned so its width runs along the opening.
                leaf.Position = new Vector3(-bounds.Position.X, -bounds.Position.Y, -bounds.GetCenter().Z);
                var turned = new Node3D { Rotation = new Vector3(0, alongZ ? -Mathf.Pi / 2 : 0, 0) };
                turned.AddChild(leaf);
                leafHinge = new Node3D { Name = "leaf", Position = hinge.Position + (alongZ ? new Vector3(0, 0, jamb) : new Vector3(jamb, 0, 0)) };
                leafHinge.AddChild(turned);
                root.AddChild(leafHinge);
                panel.Mesh = null;
            }
        }

        var walls = layout.Space.Blockers.OfType<BoxBlocker>().Where(b => b.Id.StartsWith(group + "_", StringComparison.Ordinal)).ToList();
        double inside = walls.Count == 0 ? 0 : alongZ ? walls.Average(w => w.CenterXMm) - box.CenterXMm : walls.Average(w => w.CenterZMm) - box.CenterZMm;
        // Rotating +Z by +90 degrees about Y points it at +X; rotating +X by +90 points it at -Z.
        float open = alongZ ? (inside > 0 ? 90f : -90f) : (inside > 0 ? -90f : 90f);
        _doors[door.Key] = (hinge, leafHinge, open);
        return root;
    }

    /// <summary>A pale band round the top of a switch's structure, shown once it is set. Scenery: no collider.</summary>
    private Node3D BuildSetMark(SwitchSite site, TerrainGrid terrain)
    {
        var (x, z) = Footprints.Center(site.Body);
        float radius = site.Body is CircleBlocker circle ? circle.RadiusMm / 1000f + 0.05f : 0.6f;
        var mark = new MeshInstance3D
        {
            Name = site.Key + "_set",
            Mesh = new CylinderMesh { TopRadius = radius * 0.85f, BottomRadius = radius * 0.9f, Height = 0.25f },
            MaterialOverride = Palette.Aligned,
            Position = new Vector3(x / 1000f, terrain.HeightAtMm(x, z) / 1000f + site.Body.HeightMm / 1000f - 0.3f, z / 1000f),
            Visible = false,
        };
        _setMarks[site.Key] = mark;
        return mark;
    }

    /// <summary>A barrier while it stands: a faint haze over its footprint. Scenery: the camera passes through it.</summary>
    private Node3D BuildBarrier(BarrierSite barrier, TerrainGrid terrain)
    {
        var (x, z) = Footprints.Center(barrier.Footprint);
        float height = barrier.Footprint.HeightMm / 1000f;
        Mesh mesh = barrier.Footprint switch
        {
            CircleBlocker circle => new CylinderMesh { TopRadius = circle.RadiusMm / 1000f, BottomRadius = circle.RadiusMm / 1000f, Height = height },
            BoxBlocker box => new BoxMesh { Size = new Vector3((box.MaxXMm - box.MinXMm) / 1000f, height, (box.MaxZMm - box.MinZMm) / 1000f) },
            _ => throw new ArgumentOutOfRangeException(nameof(barrier), barrier.Footprint.GetType().Name, "Unknown blocker shape"),
        };
        var node = new MeshInstance3D
        {
            Name = barrier.Key,
            Mesh = mesh,
            MaterialOverride = Palette.Fold,
            Position = new Vector3(x / 1000f, terrain.HeightAtMm(x, z) / 1000f + height / 2, z / 1000f),
        };
        _barriers[barrier.Key] = node;
        return node;
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
        id.StartsWith("den_rock", StringComparison.Ordinal) || id.StartsWith("rock_", StringComparison.Ordinal) ? Palette.Rock : Palette.Wood;

    private static float LowestUnder(TerrainGrid terrain, BoxBlocker box) =>
        new[]
        {
            terrain.HeightAtMm(box.MinXMm, box.MinZMm), terrain.HeightAtMm(box.MaxXMm, box.MinZMm),
            terrain.HeightAtMm(box.MinXMm, box.MaxZMm), terrain.HeightAtMm(box.MaxXMm, box.MaxZMm),
            terrain.HeightAtMm(box.CenterXMm, box.CenterZMm),
        }.Min() / 1000f;
}
