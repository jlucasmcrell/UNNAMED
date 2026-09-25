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
    public void Bind(Art.ArtLibrary art, Art.ArtBindings bindings, GroundField? ground = null)
    {
        _art = art;
        _bindings = bindings;
        _ground = ground;
    }

    private GroundField? _ground;
    private readonly Dictionary<string, Node3D> _models = new(StringComparer.Ordinal);

    private Art.ArtCoverage Coverage => _art.Coverage;

    public void Build(RegionLayout layout)
    {
        var terrain = layout.Space.Terrain;
        var ground = BuildTerrain(terrain);
        AddChild(ground);
        foreach (var building in ArtBuildings(layout, terrain))
            AddChild(building);
        foreach (var blocker in layout.Space.Blockers)
        {
            if (InArtBuilding(blocker.Id))
            {
                AddChild(Unseen(BuildStructure(blocker, terrain)));
                continue;
            }
            if (ArtStructure(blocker, terrain) is { } drawn)
            {
                // Drawn by the library: what shows a switch set on it (the glow) follows the model, not the footprint.
                if (drawn is not MeshInstance3D)
                    _models[blocker.Id] = drawn;
                AddChild(drawn);
                continue;
            }
            AddChild(BuildStructure(blocker, terrain));
        }
        foreach (var roof in Roofs(layout))
        {
            if (InArtBuilding(roof.Name))
            {
                AddChild(Unseen(roof));
                continue;
            }
            Coverage.Fallback("roof", roof.Name, "its building is drawn in greybox: a flat slab over the walls");
            AddChild(roof);
        }
        foreach (var door in layout.Doors)
            AddChild(BuildDoor(door, layout, terrain));
        foreach (var site in layout.Switches)
            AddChild(BuildSetMark(site, terrain));
        foreach (var barrier in layout.Barriers)
            AddChild(BuildBarrier(barrier, terrain));
        AddChild(BuildWater(layout.Space));
        var ravine = BuildRavine(layout.Space, terrain);
        AddChild(ravine);
        if (VisualOptions.Terrain == "terrain3d")
            DrawWithTerrain3D(terrain, ground, ravine);
        // The air (B11): ash, pollen, rain and mist by rule, with the particle recipes.
        if (VisualOptions.Recipes && _ground is { } field)
        {
            var atmosphere = new AtmosphereView { Name = "Atmosphere" };
            AddChild(atmosphere);
            atmosphere.Build(Art.ParticleRecipes.Load(_art.Root), field, (x, z) => Terrain3DView.SceneryHeight(field, x, z));
        }
        // Ambient life (B10): presentation only, placed from a fixed seed so the hollow's birds are the same every run.
        if (VisualOptions.All.GetValueOrDefault("life") == "on" && _ground is { } lifeGround)
        {
            var life = new Node3D { Name = "AmbientLife" };
            AddChild(life);
            AmbientLife.Build(life, lifeGround, 0xA3B1E7);
        }
        AddChild(BuildDebugMarkers(layout));
        AddChild(BuildLighting(layout, terrain));
    }

    /// <summary>
    /// Phase B (B0.1): Terrain3D draws the ground and the scenery beyond the edge. The Phase-A ground mesh and ravine boxes stop drawing
    /// but stay as the camera's colliders; nothing else changes. Without the extension or its layers, the Phase-A ground stays, and says why.
    /// </summary>
    private void DrawWithTerrain3D(TerrainGrid terrain, Node3D ground, Node3D ravine)
    {
        if (_ground is null)
        {
            Coverage.Fallback("terrain", "terrain3d", "no ground field to take the layers from: the Phase-A ground stands");
            return;
        }
        var drawn = Terrain3DView.Build(this, terrain, _ground, _art, out string? why);
        if (drawn is null)
        {
            Coverage.Fallback("terrain", "terrain3d", why ?? "Terrain3D did not build: the Phase-A ground stands");
            return;
        }
        Coverage.Resolved("terrain", "terrain3d", "Terrain3D 1.0.2, fed one way from the domain grid; its collision off");
        foreach (var mesh in ground.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>().Append(ground as MeshInstance3D))
            if (mesh is not null)
                mesh.Visible = false;
        foreach (var mesh in ravine.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
            mesh.Visible = false;
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
        // The four grounds in one splat shader (their borders blended, the tiling broken up, the paths worn in: Phase A, the visual audit's
        // V2); without all four, each cell its own world material, or the greybox colour.
        var splat = _ground?.TerrainMaterial(_art);
        foreach (var (cell, tool) in cells)
        {
            tool.GenerateNormals();
            tool.Commit(mesh);
            string? id = _bindings.Terrain.GetValueOrDefault(cell);
            if (splat is not null && _ground!.Cells.Contains(cell))
            {
                materials.Add(splat);
                Coverage.Resolved("terrain", cell, id!);
            }
            else if (id is not null && _art.WorldMaterial(id) is { } ground)
            {
                materials.Add(ground);
                Coverage.Resolved("terrain", cell, id);
            }
            else
            {
                materials.Add(Palette.Terrain);
                Coverage.Fallback("terrain", cell, id is null ? "no ground bound for this cell: a flat colour" : _art.Why(id) ?? "its material would not load", id);
            }
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
    /// The buildings drawn whole from the asset library, each over the footprint its walls enclose. Their walls and roofs stay as the
    /// camera's colliders, unseen: what the body collides with is the truth.
    /// </summary>
    private IEnumerable<Node3D> ArtBuildings(RegionLayout layout, TerrainGrid terrain)
    {
        foreach (var (prefix, look) in _bindings.Buildings)
        {
            if (look.Model is not { } id)
                continue;
            var footprint = Footprint(layout, prefix);
            if (footprint is null)
                continue;
            if (Art.Fitting.Building(_art, look, footprint, terrain) is not { } model)
            {
                Coverage.Fallback("building", footprint.Id, $"{_art.Why(id, footprint.Id) ?? "not drawn"}: its walls and roof are drawn in greybox", id);
                continue;
            }
            Coverage.Resolved("building", footprint.Id, id);
            _artBuildings.Add(prefix);
            yield return model;
        }
    }

    /// <summary>The footprint a building's walls enclose (the walls sharing its prefix), or null when the layout has none.</summary>
    private static BoxBlocker? Footprint(RegionLayout layout, string prefix)
    {
        var walls = layout.Space.Blockers.OfType<BoxBlocker>().Where(b => b.Id.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        return walls.Count == 0 ? null : new BoxBlocker(prefix.TrimEnd('_'), walls.Min(w => w.MinXMm), walls.Min(w => w.MinZMm), walls.Max(w => w.MaxXMm),
            walls.Max(w => w.MaxZMm), walls.Max(w => w.HeightMm));
    }

    private bool InArtBuilding(string id) => _artBuildings.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>A structure kept only as the camera's collider, its drawing left to the art over it.</summary>
    private static Node3D Unseen(Node3D node)
    {
        if (node is MeshInstance3D mesh)
            mesh.Mesh = null;
        return node;
    }

    /// <summary>
    /// A structure drawn with the asset library's model for it (the bindings' <c>structures</c>), sized to the footprint the body
    /// collides with, and with the same camera collider as its greybox; or its greybox shape wearing a bound surface material (triplanar in
    /// world space). Null when the bindings have nothing usable for it, so the greybox is drawn - and the coverage report says why.
    /// </summary>
    private Node3D? ArtStructure(Blocker blocker, TerrainGrid terrain)
    {
        string shape = blocker is CircleBlocker ? "a cylinder" : "a box";
        if (_bindings.Structure(blocker.Id) is not { } look)
        {
            Coverage.Fallback("structure", blocker.Id, $"no binding: {shape}");
            return null;
        }
        if (look.Surface is { } surface && look.Model is null)
        {
            if (_art.WorldMaterial(surface) is not { } material)
            {
                Coverage.Fallback("structure", blocker.Id, $"{_art.Why(surface) ?? "its surface would not load"}: {shape}", surface);
                return null;
            }
            var greybox = BuildStructure(blocker, terrain);
            if (greybox is MeshInstance3D mesh)
                mesh.MaterialOverride = material;
            Coverage.Resolved("structure", blocker.Id, surface);
            return greybox;
        }
        if (Art.Fitting.Structure(_art, look, blocker, terrain) is not { } model)
        {
            Coverage.Fallback("structure", blocker.Id, $"{(look.Model is { } id ? _art.Why(id, blocker.Id) ?? "not drawn" : "no model bound")}: {shape}", look.Model);
            return null;
        }
        Coverage.Resolved("structure", blocker.Id, look.Model!);
        // A model bound with a surface wears it over its own relief (Phase B, B0.6: an artifact in a world material).
        if (look.Surface is { } worn && _art.WorldMaps(worn) is { } maps)
        {
            var tint = look.SurfaceTint is { } t ? new Color(t.X, t.Y, t.Z) : Colors.White;
            var glow = look.Glow is { } g ? new Color(g.X, g.Y, g.Z) : Colors.Black;
            Palette.Wear(model, maps, tint, glow, look.GlowEnergy);
            Coverage.Resolved("structure_surface", blocker.Id, worn);
        }
        // A model bound with a wind stiffness sways on the world wind (Phase B, B4: the trees).
        if (look.Wind > 0 && VisualOptions.Wind)
        {
            WindField.Ensure(this);
            Palette.Windblown(model, look.Wind);
        }
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
        string? leafId = _bindings.Doors.GetValueOrDefault(door.Key)?.Model;
        bool framed = _artBuildings.Contains(group + "_");
        var leaf = framed && leafId is not null ? _art.Model(leafId) : null;
        if (!framed)
            Coverage.Fallback("door", door.Key, "its building is drawn in greybox, so its door is a panel", leafId);
        else if (leafId is null)
            Coverage.Fallback("door", door.Key, "no binding: a panel");
        else if (leaf is null)
            Coverage.Fallback("door", door.Key, $"{_art.Why(leafId) ?? "not drawn"}: a panel", leafId);
        if (leaf is not null && leafId is not null)
        {
            var bounds = Art.ArtGallery.Bounds(leaf);
            float run = alongZ ? size.Z : size.X, jamb = (run - bounds.Size.X) / 2;
            if (jamb < 0 || bounds.Size.Y > height)
            {
                _art.Report($"{leafId} at {door.Key}", $"authored {bounds.Size.X:0.00} x {bounds.Size.Y:0.00} m; the opening is {run:0.00} x {height:0.00} m; "
                    + "not rescaled", leafId);
                Coverage.Fallback("door", door.Key, $"{_art.Why(leafId, door.Key)}: a panel", leafId);
                leaf.Free();
            }
            else
            {
                Coverage.Resolved("door", door.Key, leafId);
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

    /// <summary>
    /// What shows a switch set, once it is: on a structure the library draws, a pale glow over the model itself (a shell of its own
    /// meshes, grown a little along their normals) - a band floating at the footprint's height would miss a model shorter or leaner than
    /// its footprint; on a greybox structure, a pale band round its top. Scenery: no collider.
    /// </summary>
    private Node3D BuildSetMark(SwitchSite site, TerrainGrid terrain)
    {
        if (_models.TryGetValue(site.Body.Id, out var model))
        {
            var shell = new Node3D { Name = site.Key + "_set", Visible = false };
            // The full model's meshes only (its lighter levels draw at distance, where the glow need not follow them).
            foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>()
                         .Where(m => m.Mesh is not null && m.VisibilityRangeBegin <= 0))
            {
                shell.AddChild(new MeshInstance3D
                {
                    Mesh = mesh.Mesh, Transform = Art.ArtLibrary.Relative(model, mesh), MaterialOverride = Palette.AlignedGlow,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                });
            }
            model.AddChild(shell);
            _setMarks[site.Key] = shell;
            Coverage.Resolved("switch_mark", site.Key, $"a glow over {model.Name}'s model");
            return new Node3D { Name = site.Key + "_set_on_model" };
        }
        Coverage.Fallback("switch_mark", site.Key, "a pale band drawn in code round the greybox stone's top, shown once it is set; no asset");
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
        Coverage.Resolved("barrier", barrier.Key, "a refractive violet shimmer shader with drifting motes (Palette.FoldscarBarrier); an effect, not a model");
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
            MaterialOverride = VisualOptions.Foldscar == "proof" ? Palette.FoldscarFold : Palette.FoldscarBarrier,
            Position = new Vector3(x / 1000f, terrain.HeightAtMm(x, z) / 1000f + height / 2, z / 1000f),
        };
        _barriers[barrier.Key] = node;
        return node;
    }

    /// <summary>A still water surface just above the stream bed: the terrain hides it everywhere else.</summary>
    private Node3D BuildWater(WalkSpace space)
    {
        Coverage.Resolved("water", "stream", "a ripple/depth-tint/fresnel water shader (Palette.StreamWater); an effect, not a model");
        return new MeshInstance3D
        {
            Name = "Water",
            Mesh = new PlaneMesh { Size = new Vector2((space.MaxXMm - space.MinXMm) / 1000f, (space.MaxZMm - space.MinZMm) / 1000f) },
            MaterialOverride = Palette.StreamWater,
            Position = new Vector3((space.MinXMm + space.MaxXMm) / 2000f, 0.12f, (space.MinZMm + space.MaxZMm) / 2000f),
        };
    }

    /// <summary>
    /// PROTOTYPE.md §3: a sheer ravine on three sides (west, north, east) and a cliff to the south. Scenery, in the bindings' cliff material
    /// laid triplanar (Phase A, the visual audit's V2), or greybox.
    /// </summary>
    private Node3D BuildRavine(WalkSpace space, TerrainGrid terrain)
    {
        var root = new Node3D { Name = "Ravine" };
        float minX = space.MinXMm / 1000f, maxX = space.MaxXMm / 1000f, minZ = space.MinZMm / 1000f, maxZ = space.MaxZMm / 1000f;
        float width = maxX - minX, depth = maxZ - minZ;
        var rock = GroundField.Cliff(_art, _bindings.Cliffs);
        if (rock is not null)
            Coverage.Resolved("cliffs", "ravine", _bindings.Cliffs!);
        else
            Coverage.Fallback("cliffs", "ravine", _bindings.Cliffs is { } id ? _art.Why(id) ?? "its material would not load" : "no cliff material bound", _bindings.Cliffs);
        void Add(string name, Vector3 size, Vector3 centre)
        {
            var node = Solid(name, new BoxMesh { Size = size }, new BoxShape3D { Size = size }, rock ?? Palette.Cliff);
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
        Coverage.Fallback("debug", "discovery_rings", "the F3 overlay's discovery rings, flat translucent discs");
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

    /// <summary>
    /// The light over the hollow (Phase A: the visual audit's V1, measured on the development machine and chosen for RAZER's 4070 Ti at
    /// 1080p): an overcast sun with soft, blended shadow cascades to 90 m; an overcast sky (a cloud deck and a haze at the horizon) that also
    /// lights and reflects; AgX with a little exposure; SSAO and SSIL for contact shadow and bounce; SDFGI for the light under roofs and
    /// canopies; exponential depth fog with aerial perspective and a thin volumetric fog; a soft glow. The viewport's side of it (MSAA 4x with
    /// TAA, 16x anisotropy, soft shadow filtering, the 4096 shadow atlas) is in <c>project.godot</c>. Then each building's interior lights,
    /// which are the bindings' data (<c>building_walls.*.interior_light</c>), never coordinates here.
    /// </summary>
    private Node3D BuildLighting(RegionLayout layout, TerrainGrid terrain)
    {
        var root = new Node3D { Name = "Lighting" };
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-48, 35, 0),
            ShadowEnabled = true,
            LightEnergy = 1.35f,
            LightColor = new Color(1.0f, 0.95f, 0.86f),
            LightAngularDistance = 1.0f,
            ShadowBlur = 1.0f,
            ShadowBias = 0.03f,
            ShadowNormalBias = 1.0f,
            DirectionalShadowMaxDistance = 90f,
            DirectionalShadowBlendSplits = true,
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ShaderMaterial { Shader = new Shader { Code = SkyShader } }, RadianceSize = Sky.RadianceSizeEnum.Size256,
                ProcessMode = Sky.ProcessModeEnum.Automatic },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 1f,
            AmbientLightEnergy = 1f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 1.15f,
            TonemapAgxContrast = 1.15f,
            SsaoEnabled = true, SsaoRadius = 1.4f, SsaoIntensity = 2.2f, SsaoPower = 1.6f, SsaoDetail = 0.6f, SsaoHorizon = 0.06f, SsaoSharpness = 0.98f,
            SsaoLightAffect = 0.15f, SsaoAOChannelAffect = 0.5f,
            SsilEnabled = true, SsilRadius = 4f, SsilIntensity = 0.9f, SsilSharpness = 0.98f, SsilNormalRejection = 1f,
            SdfgiEnabled = true, SdfgiCascades = 6, SdfgiMinCellSize = 0.2f, SdfgiUseOcclusion = true, SdfgiReadSkyLight = true, SdfgiBounceFeedback = 0.5f,
            SdfgiEnergy = 1.0f, SdfgiNormalBias = 1.1f, SdfgiProbeBias = 1.1f, SdfgiYScale = Godot.Environment.SdfgiyScale.Scale75Percent,
            GlowEnabled = true, GlowIntensity = 0.35f, GlowStrength = 1f, GlowBloom = 0.02f, GlowHdrThreshold = 1.1f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
            FogEnabled = true, FogMode = Godot.Environment.FogModeEnum.Exponential, FogDensity = 0.0016f, FogAerialPerspective = 0.75f, FogSkyAffect = 0f,
            FogLightColor = new Color(0.66f, 0.68f, 0.70f), FogSunScatter = 0.05f,
            VolumetricFogEnabled = true, VolumetricFogDensity = 0.0025f, VolumetricFogAlbedo = new Color(0.86f, 0.87f, 0.9f), VolumetricFogAnisotropy = 0.35f,
            VolumetricFogLength = 96f, VolumetricFogDetailSpread = 2f, VolumetricFogGIInject = 0.6f, VolumetricFogAmbientInject = 0.35f,
            VolumetricFogSkyAffect = 0f, VolumetricFogTemporalReprojectionEnabled = true,
        };
        // Phase B (B0.3): Sky3D draws the sky and drives the sun and moon over this environment; or the HDRI; or the Phase-A sky and sun.
        string? skyWhy = null;
        RenderTiers.Apply(environment, sun);
        if (VisualOptions.Sky == "sky3d" && SkyView.BuildSky3D(root, environment, sun, out skyWhy) is not null)
            Coverage.Resolved("sky", "sky3d", "Sky3D 2.1.0 over the game's environment, at a set hour");
        else
        {
            if (VisualOptions.Sky == "sky3d")
                Coverage.Fallback("sky", "sky3d", skyWhy ?? "Sky3D did not build: the Phase-A sky stands");
            if (VisualOptions.Sky == "hdri" && !SkyView.ApplyHdri(environment, _art.Root, out string? hdriWhy))
                Coverage.Fallback("sky", "hdri", hdriWhy ?? "the HDRI did not load: the Phase-A sky stands");
            root.AddChild(sun);
            root.AddChild(new WorldEnvironment { Environment = environment });
        }
        // Each building's particle emitters (B11: the smithy's chimney smoke), relative to its footprint's centre on the ground.
        var recipes = VisualOptions.Recipes ? Art.ParticleRecipes.Load(_art.Root) : null;
        foreach (var (prefix, look) in _bindings.Buildings)
        {
            if (recipes is null || look.Emitters is not { Count: > 0 } emitters || Footprint(layout, prefix) is not { } footprint)
                continue;
            var origin = new Vector3(footprint.CenterXMm / 1000f, LowestUnder(terrain, footprint), footprint.CenterZMm / 1000f);
            foreach (var (recipe, at) in emitters)
            {
                if (recipes.Build(recipe) is not { } particles)
                    continue;
                particles.Position = origin + at;
                root.AddChild(particles);
                Coverage.Resolved("emitter", $"{footprint.Id}:{recipe}", recipe);
            }
        }
        // Inside each building, its lights: relative to its footprint's centre on the ground (where its model stands).
        foreach (var (prefix, look) in _bindings.Buildings)
        {
            if (look.Lights is not { Count: > 0 } lights || Footprint(layout, prefix) is not { } footprint)
                continue;
            var origin = new Vector3(footprint.CenterXMm / 1000f, LowestUnder(terrain, footprint), footprint.CenterZMm / 1000f);
            for (int i = 0; i < lights.Count; i++)
            {
                var light = lights[i].Build($"InteriorLight_{footprint.Id}_{i}");
                light.Position += origin;
                root.AddChild(light);
            }
        }
        return root;
    }

    /// <summary>An overcast sky: a zenith-to-horizon gradient, an FBM cloud deck on a plane (86 % cover, lit and dark cloud, brighter towards the sun), a haze band at the horizon.</summary>
    private const string SkyShader = @"
shader_type sky;
uniform vec3 zenith : source_color = vec3(0.46, 0.49, 0.53);
uniform vec3 horizon : source_color = vec3(0.66, 0.68, 0.70);
uniform vec3 ground : source_color = vec3(0.33, 0.33, 0.32);
uniform vec3 cloud_lit : source_color = vec3(0.74, 0.74, 0.75);
uniform vec3 cloud_dark : source_color = vec3(0.34, 0.36, 0.40);
uniform float coverage = 0.86;
uniform float cloud_scale = 0.9;
uniform float energy = 1.0;

float hash(vec2 p) { vec3 q = fract(vec3(p.xyx) * 0.1031); q += dot(q, q.yzx + 33.33); return fract((q.x + q.y) * q.z); }
float noise(vec2 p) {
    vec2 i = floor(p); vec2 f = fract(p); vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
float fbm(vec2 p) {
    float v = 0.0; float a = 0.5; mat2 r = mat2(vec2(0.8, -0.6), vec2(0.6, 0.8));
    for (int i = 0; i < 6; i++) { v += a * noise(p); p = r * p * 2.03 + vec2(1.7, 9.2); a *= 0.5; }
    return v;
}
void sky() {
    vec3 d = EYEDIR;
    float h = d.y;
    vec3 col = mix(horizon, zenith, pow(clamp(h, 0.0, 1.0), 0.45));
    if (h > 0.0) {
        vec2 uv = d.xz / (h + 0.12) * cloud_scale;
        float n = fbm(uv + vec2(3.1, 7.7));
        float cov = smoothstep(1.0 - coverage - 0.15, 1.0 - coverage + 0.35, n);
        float thick = smoothstep(0.35, 0.85, fbm(uv * 1.7 + vec2(11.0, 2.0)));
        vec3 cc = mix(cloud_lit, cloud_dark, thick * 0.85);
        if (LIGHT0_ENABLED) {
            float sun = pow(max(dot(d, LIGHT0_DIRECTION), 0.0), 6.0);
            cc += LIGHT0_COLOR * sun * 0.2 * (1.0 - thick);
        }
        col = mix(col, cc, cov * smoothstep(0.0, 0.25, h));
    } else {
        col = mix(horizon, ground, smoothstep(0.0, 0.2, -h));
    }
    col = mix(col, horizon, exp(-abs(h) * 14.0) * 0.6);
    COLOR = col * energy;
}
";

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
