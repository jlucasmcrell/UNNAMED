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
    private readonly List<(string Flag, AnimationPlayer Player, string Clip)> _flagAnimations = new();
    private HashSet<string> _activeFlags = new(StringComparer.Ordinal);
    private Node3D? _debug;
    private Art.ArtLibrary _art = Art.ArtLibrary.Empty;
    private Art.ArtBindings _bindings = Art.ArtBindings.Empty;
    private List<(WorldEnvironment Node, Godot.Environment Environment)>? _environments;

    public void SetSpaceActive(bool active)
    {
        Visible = active;
        _environments ??= Environments(this).Select(e => (e, e.Environment)).ToList();
        foreach (var (node, environment) in _environments)
            node.Environment = active ? environment : null;
    }

    private static IEnumerable<WorldEnvironment> Environments(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is WorldEnvironment environment)
                yield return environment;
            foreach (var nested in Environments(child))
                yield return nested;
        }
    }

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

    /// <summary>The run's particle recipes (B0.7), read once for the whole region: a building's chimney smoke and a lit prop's own flame
    /// come out of this one instance, so the JSON and the effect textures are read once per region and not once per placement.</summary>
    private Art.ParticleRecipes? _recipes;

    private Art.ArtCoverage Coverage => _art.Coverage;

    public void Build(RegionLayout layout)
    {
        var terrain = layout.Space.Terrain;
        // The particle recipes, read once here (B0.7): a structure's own emitters (a lantern's flame, in ArtStructure) and a
        // building's (the smithy's chimney smoke, in BuildLighting) both build from this one instance.
        _recipes = VisualOptions.Recipes ? Art.ParticleRecipes.Load(_art.Root) : null;
        var ground = BuildTerrain(terrain, layout.SpaceId);
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
        // Phase B VFX lane: the Foldscar's stones, heart and fold come back into register as they are set (FoldscarRegister).
        if (VisualOptions.Recipes)
        {
            _register = new FoldscarRegister { Name = "FoldscarRegister" };
            AddChild(_register);
        }
        // The ring's middle: the structure of the switch whose flag lifts a barrier (the heart).
        var release = layout.Switches.FirstOrDefault(s => layout.Barriers.Any(b => b.FlagId == s.FlagId));
        var ringCentre = Vector3.Zero;
        if (release is not null)
        {
            var (cx, cz) = Footprints.Center(release.Body);
            ringCentre = new Vector3(cx / 1000f, 0, cz / 1000f);
        }
        foreach (var site in layout.Switches)
            AddChild(BuildSetMark(site, terrain, layout.Barriers.Any(b => b.FlagId == site.FlagId), ringCentre));
        foreach (var barrier in layout.Barriers)
            AddChild(BuildBarrier(barrier, terrain));
        // AN INTERIOR IS ENCLOSED. There is no stream running through the ring, no fields beyond its walls, no
        // weather in its air and no sky over it, so none of that is built here. It used to be: BuildWater laid a
        // stream plane over the whole walk space at 0.12 m and relied on terrain relief to hide it, which on the
        // ring's flat floor flooded every room (review section 7).
        bool exterior = layout.IsOutdoors;
        if (exterior)
        {
            AddChild(BuildWater(layout.Space));
        // Phase B demo: the authored set dressing (Art/dressing.json) - scenery only, grounded on the domain's terrain.
        if (VisualOptions.Tier != "phase_a")
        {
            var (placed, skipped) = Dressing.Place(this, _art, (x, z) => terrain.HeightAtMm((long)(x * 1000), (long)(z * 1000)) / 1000f,
                _art.Root is { } artRoot ? Path.Combine(artRoot, "dressing.json") : null);
            GD.Print($"UNNAMED dressing: {placed} props placed, {skipped} skipped");
        }
        var ravine = BuildRavine(layout.Space, terrain);
        AddChild(ravine);
        if (VisualOptions.Terrain == "terrain3d")
            DrawWithTerrain3D(terrain, ground, ravine, TerrainDetail(layout));
        // The air (B11): ash, pollen, rain and mist by rule, with the particle recipes.
        if (VisualOptions.Recipes && _ground is { } field)
        {
            var atmosphere = new AtmosphereView { Name = "Atmosphere" };
            AddChild(atmosphere);
            atmosphere.Build(Art.ParticleRecipes.Load(_art.Root), field, (x, z) => Terrain3DView.SceneryHeight(field, x, z));
        }
        // The Foldscar's ground (B9): shards of the heart's kind round it, stones resting on nothing - with the proof's heart.
        if (VisualOptions.Foldscar == "proof" && _ground is { } foldGround
            && layout.Space.Blockers.OfType<CircleBlocker>().FirstOrDefault(b => b.Id == "rock_foldscar_heart") is { } heartBody
            && layout.Barriers.FirstOrDefault() is { Footprint: CircleBlocker foldBody })
        {
            var pebbles = new List<(Node3D Pebble, float Settled)>();
            FoldscarDressing.Dress(this, _art, foldGround, new Vector2(heartBody.CenterXMm / 1000f, heartBody.CenterZMm / 1000f),
                new Vector2(foldBody.CenterXMm / 1000f, foldBody.CenterZMm / 1000f), foldBody.RadiusMm / 1000f, pebbles);
            _register?.AddPebbles(pebbles);
        }
        // Ambient life (B10): presentation only, placed from a fixed seed so the hollow's birds are the same every run.
        if (VisualOptions.All.GetValueOrDefault("life") == "on" && _ground is { } lifeGround)
        {
            var life = new Node3D { Name = "AmbientLife" };
            AddChild(life);
            var indoors = _bindings.Buildings.Keys.Select(prefix => Footprint(layout, prefix)).OfType<BoxBlocker>()
                .Select(b => new Rect2(b.MinXMm / 1000f, b.MinZMm / 1000f, (b.MaxXMm - b.MinXMm) / 1000f, (b.MaxZMm - b.MinZMm) / 1000f)).ToList();
            AmbientLife.Build(life, lifeGround, 0xA3B1E7, indoors);
            PerchedRavens.Build(life, _art, lifeGround);
        }
        }

        AddChild(BuildDebugMarkers(layout));
        AddChild(BuildLighting(layout, terrain, exterior));
    }

    /// <summary>
    /// Phase B (B0.1): Terrain3D draws the ground and the scenery beyond the edge. The Phase-A ground mesh and ravine boxes stop drawing
    /// but stay as the camera's colliders; nothing else changes. Without the extension or its layers, the Phase-A ground stays, and says why.
    /// </summary>
    /// <summary>What the ground lies under and beside (Terrain3DView.Detail): the trees' crowns, the buildings' footprints, the drainage.</summary>
    private static Terrain3DView.Detail TerrainDetail(RegionLayout layout)
    {
        var crowns = layout.Space.Blockers.OfType<CircleBlocker>().Where(b => b.Id.StartsWith("tree_", StringComparison.Ordinal))
            .Select(b => new Vector3(b.CenterXMm / 1000f, b.CenterZMm / 1000f, 5.5f)).ToList();
        var buildings = new List<Rect2>();
        foreach (var group in layout.Space.Blockers.OfType<BoxBlocker>().GroupBy(b => b.Id.Split('_')[0]))
        {
            if (group.Count() < 3)
                continue;   // a building is a group of walls; a lone box is a prop
            float x0 = group.Min(b => b.MinXMm) / 1000f, z0 = group.Min(b => b.MinZMm) / 1000f;
            float x1 = group.Max(b => b.MaxXMm) / 1000f, z1 = group.Max(b => b.MaxZMm) / 1000f;
            buildings.Add(new Rect2(x0, z0, x1 - x0, z1 - z0));
        }
        return new Terrain3DView.Detail(crowns, buildings, new[] { Terrain3DView.CharwoodStream });
    }

    private void DrawWithTerrain3D(TerrainGrid terrain, Node3D ground, Node3D ravine, Terrain3DView.Detail detail)
    {
        if (_ground is null)
        {
            Coverage.Fallback("terrain", "terrain3d", "no ground field to take the layers from: the Phase-A ground stands");
            return;
        }
        var drawn = Terrain3DView.Build(this, terrain, _ground, _art, out string? why, detail);
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

    /// <summary>
    /// Show which switches are set and which barriers still stand (M6), as the simulation says. <paramref name="inPlay"/>: the change just
    /// happened in play, so the Foldscar's register plays it (a stone turning, the heart's release); otherwise it is put as it stands.
    /// </summary>
    public void SetFlags(IEnumerable<SwitchView> switches, IEnumerable<BarrierView> barriers, bool inPlay = false)
    {
        if (_flagAnimations.Count > 0)
            _activeFlags = switches.Where(s => s.Set).Select(s => s.Site.FlagId).ToHashSet(StringComparer.Ordinal);
        foreach (var view in switches)
        {
            if (_setMarks.TryGetValue(view.Site.Key, out var mark) && _register?.Owns(view.Site.Key) != true)
                mark.Visible = view.Set;
        }
        foreach (var view in barriers)
        {
            if (_barriers.TryGetValue(view.Site.Key, out var node) && _register?.OwnsFold(view.Site.Key) != true)
            {
                node.Visible = view.Standing;
                foreach (var collision in node.FindChildren("*", nameof(StaticBody3D), true, false).Cast<StaticBody3D>())
                    collision.CollisionLayer = view.Standing ? CameraCollisionLayer : 0;
            }
        }
        _register?.Apply(switches, barriers, inPlay);
    }

    /// <summary>Fixture animation samples the simulation clock; a saved mechanism has the same phase after a load.</summary>
    public void DrawWorld(long tick, double tickSeconds)
    {
        foreach (var (flag, player, clip) in _flagAnimations)
        {
            double length = player.GetAnimation(clip).Length;
            player.Seek(_activeFlags.Contains(flag) && length > 0 ? tick * tickSeconds % length : 0, update: true);
        }
    }

    private FoldscarRegister? _register;

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
    private Node3D BuildTerrain(TerrainGrid grid, SpaceId space)
    {
        var cells = new SortedDictionary<string, SurfaceTool>(StringComparer.Ordinal);
        Vector3 At(int i, int j) => ToGodot(grid.OriginXMm + i * grid.SpacingMm, grid.HeightAt(i, j), grid.OriginZMm + j * grid.SpacingMm);
        // The domain's split: every quad along its (0,0)-(1,1) diagonal, clockwise seen from above (Godot's front face).
        for (int j = 0; j < grid.Rows - 1; j++)
        for (int i = 0; i < grid.Columns - 1; i++)
        {
            var centre = (At(i, j) + At(i + 1, j + 1)) / 2;
            string cell = (space.IsExterior ? UNNAMED.World.CellKey.OfWorld(centre.X, centre.Z)
                : UNNAMED.World.CellKey.OfInterior(space, centre.X, centre.Z)).ToString();
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
        if (blocker.Id.StartsWith("rock_rim_", StringComparison.Ordinal))
        {
            model.RotationDegrees = new Vector3(0, (blocker.Id[^1] - '0') * 73f, 0);
            model.Position += Vector3.Down * 0.32f; // Set the irregular scan into the uneven quarry floor.
        }
        // A model bound with a surface wears it over its own relief (Phase B, B0.6: an artifact in a world material).
        if (look.Surface is { } worn && _art.WorldMaps(worn) is { } maps)
        {
            var tint = look.SurfaceTint is { } t ? new Color(t.X, t.Y, t.Z) : Colors.White;
            // DS Phase B (2026-09-27): per-instance variation. Every structure wearing the same world material came
            // out the same value as every other one, which is half of the "all wood looks the same brown, all stone
            // the same light grey" read (the other half is a single flat light). A deterministic shift from the
            // blocker's own id gives each placement its own value and a slight warm/cool lean, so a field of
            // boulders reads as rock rather than as copies of one prop. Deterministic on purpose: the same world
            // must look the same on every run and in every capture.
            //
            // THE FORMULA MOVED TO `Palette.Shifted` ON 2026-09-29 so the tree path could use the same one - a place
            // that varies a placement must not vary it differently from another. The numbers are unchanged.
            tint = Palette.Shifted(blocker.Id, tint);
            var glow = look.Glow is { } g ? new Color(g.X, g.Y, g.Z) : Colors.Black;
            Palette.Wear(model, maps, tint, glow, look.GlowEnergy);
            Coverage.Resolved("structure_surface", blocker.Id, worn);
        }
        // A model bound with a wind stiffness sways on the world wind (Phase B, B4: the trees).
        if (look.Wind > 0 && VisualOptions.Wind)
        {
            WindField.Ensure(this);
            // A PLACED TREE HAD NO PER-INSTANCE COLOUR AT ALL UNTIL 2026-09-29. `Windblown` took only a stiffness, so
            // the region's 145 placed birches and pines shared one canopy colour per model - every birch exactly the
            // same yellow - while the 240 boulders beside them varied, because the shift above lives in the SURFACE
            // path and a tree can never take a surface: the broadleaves are multi-material, and a surface silently
            // takes a structure's wind away (Palette.Windblown skips any surface whose material is no longer a
            // BaseMaterial3D, which is what Wear leaves behind). Same id, same formula, same determinism.
            Palette.Windblown(model, look.Wind, Palette.Shifted(blocker.Id, Colors.White));
            // A tree that answers the wind a breath late (`late_s`): Old Late.
            if (look.LateS > 0)
                Palette.Late(model, look.LateS);
        }
        // A prop's own flame and light, where its binding gives it any (Phase B: Cairnwold's lantern posts).
        PropEffects(model, look, blocker.Id);
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

    /// <summary>
    /// A PROP's own emitters and light, from its binding (<c>emitters</c>, <c>light</c>): a lit lantern post's flame and the light it
    /// throws. The building loops in <see cref="BuildLighting"/> cannot reach a prop - they iterate <c>_bindings.Buildings</c>, and the
    /// footprint grouping already states the rule, "a building is a group of walls; a lone box is a prop". A lantern post is one blocker,
    /// so its effects are placed here, where a drawn prop's own node is in hand (the same place <see cref="BuildSetMark"/> hangs a
    /// switch's glow shell).
    ///
    /// THE OFFSET GOES ON THE MODEL, NOT ON THE ROOT, and that is not a detail. <see cref="Art.Fitting.Structure"/> stands the model
    /// inside a holder and re-centres it on its own bounds, while <c>cw_lantern_post</c> is authored off-centre on purpose - its origin is
    /// the post's foot and the lantern hangs 0.58 m out along the cross-arm, so its bounds centre is 0.30 m from the post. An offset
    /// measured from the root would stand the flame a whole lantern-width beside the lantern; measured from the model it is the asset's
    /// own metre, which is exactly what <c>bindings.json</c>'s numbers are.
    /// </summary>
    private void PropEffects(Node3D placed, Art.Placement look, string id)
    {
        if (look.Light is null && look.Emitters is not { Count: > 0 })
            return;
        // Fitting's own frame: the root (the footprint on the ground), then the holder, then the model as the asset was authored.
        var authored = placed.GetChildren().OfType<Node3D>().FirstOrDefault()?.GetChildren().OfType<Node3D>().FirstOrDefault() ?? placed;
        if (_recipes is not null && look.Emitters is { Count: > 0 } emitters)
        {
            foreach (var (recipe, at) in emitters)
            {
                if (_recipes.Build(recipe) is not { } particles)
                    continue;
                particles.Position = at;
                authored.AddChild(particles);
                Coverage.Resolved("emitter", $"{id}:{recipe}", recipe);
            }
        }
        if (look.Light is { } spec)
        {
            var light = spec.Build($"Light_{id}");
            authored.AddChild(light);
            // A prop that carries a FLAME carries a light lit by that flame, and a flame's light is never
            // steady. Measured 2026-09-29: the torch_flame sprite above a Cairnwold lantern moved and the
            // OmniLight3D inside it did not, so the pool it threw on the ground was perfectly constant over
            // 60 frames (mean |dluma| 0.472 whole-frame, 0.060 % of pixels beyond 32 code values). The
            // lantern is the key light of the village at night, which made that the single most visible
            // static thing in the scene. Only emitters that are actually a flame get the waver - a prop lit
            // beside a chimney's smoke is not lit by a fire.
            //
            // AND THE SUN TAKES ITS SHARE. A light authored "daylight" also gets the node, because the node is
            // what owns a light's energy and one owner is the point: measured 2026-09-29 at 13.00 h with
            // daylight 1.00, thirteen lantern posts were burning at 1.78 to 2.35 from a scene built once at
            // boot. A prop with neither a flame nor a daylight term is left a plain constant light, untouched.
            if (Art.FlameFlicker.Lit(look.Emitters) || spec.Daylight > 0f)
                Art.FlameFlicker.Attach(light, Art.FlameFlicker.DefaultAmount, spec.Daylight);
        }
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
    /// <remarks>
    /// The groups are the bindings' (`roof_groups`), so a region can roof its own rooms: the Barrowworks is
    /// workings cut into a hill and every one of its thirteen rooms wants a lid. The bindings default to the two
    /// the greybox has always roofed, so a region that names none is unchanged.
    /// </remarks>
    private IEnumerable<Node3D> Roofs(RegionLayout layout)
    {
        foreach (string group in _bindings.RoofGroups)
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
        var binding = _bindings.Doors.GetValueOrDefault(door.Key);
        string? leafId = binding?.Model;
        // The door's own key is short ("door.smithy") and names its building by the bare word, while a building's prefix is the layout's
        // full one ("cw_smithy", no trailing separator). The two never compared equal, so every door in a library building was drawn as
        // a plain panel with its leaf art sitting unused in the library. Matched here on the last word of the prefix.
        //
        // AND A THIRD CLAUSE, BECAUSE THE FIRST TWO ONLY WORK FOR A ONE-WORD BUILDING NAME. `group` is the door key's text before its
        // FIRST underscore, so `door.smithy` gives "smithy" and matches `cw_smithy` - but a house is `cw_house_05`, whose door key has
        // to carry the number to be unique, and `Split('_')[0]` on "door.cw_house_05" gives "cw", which matches nothing. Measured:
        // hollowing `cw_house_05` drew the building (`building:cw_house_05 resolved`) and counted the door unexpected - "its building
        // is drawn in greybox, so its door is a panel" - with the building drawn and standing right there.
        //
        // So a door may also name its building EXACTLY: `door.cw_house_05` finds `cw_house_05`. Strictly additive, so the two doors
        // that already work - `door.smithy` and `door.longhouse` - keep matching through the clauses they always used.
        string named = door.Key["door.".Length..];
        bool framed = !layout.IsOutdoors || _artBuildings.Any(built =>
        {
            string bare = built.TrimEnd('_');
            return bare.Equals(group, StringComparison.Ordinal)
                || bare.EndsWith("_" + group, StringComparison.Ordinal)
                || bare.Equals(named, StringComparison.Ordinal);
        });
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
                // The binding's offset lifts a leaf onto a raised sill (the staddle granary's floor); the collider panel stays put.
                leafHinge = new Node3D
                {
                    Name = "leaf",
                    Position = hinge.Position + (alongZ ? new Vector3(0, 0, jamb) : new Vector3(jamb, 0, 0)) + binding!.Offset,
                };
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
    private Node3D BuildSetMark(SwitchSite site, TerrainGrid terrain, bool liftsABarrier, Vector3 ringCentre)
    {
        if (_bindings.Structure(site.Body.Id)?.HideSetMark == true)
            return new Node3D { Name = site.Key + "_read" }; // A read document does not become an illuminated magical stone.
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
            _register?.AddStone(site, model, shell, liftsABarrier, ringCentre);
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
        if (_bindings.Structure(barrier.Key) is { Model: not null } look
            && Art.Fitting.Structure(_art, look, barrier.Footprint, terrain) is { } model)
        {
            model.AddChild(Unseen(BuildStructure(barrier.Footprint, terrain)));
            _barriers[barrier.Key] = model;
            if (look.AnimationFlag is { } flag && model.FindChildren("*", nameof(AnimationPlayer), true, false).FirstOrDefault() is AnimationPlayer player
                && player.GetAnimationList().FirstOrDefault(name => name != "RESET") is { } clip)
            {
                player.Play(clip);
                player.Pause();
                player.Seek(0, update: true);
                _flagAnimations.Add((flag, player, clip));
            }
            Coverage.Resolved("barrier", barrier.Key, look.Model);
            return model;
        }
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
        _register?.AddFold(barrier, node);
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
    private Node3D BuildLighting(RegionLayout layout, TerrainGrid terrain, bool exterior = true)
    {
        var root = new Node3D { Name = "Lighting" };
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-48, 35, 0),
            ShadowEnabled = true,
            LightEnergy = 1.85f,
            LightColor = new Color(1.0f, 0.95f, 0.86f),
            // NO SUN DISC: a hard sun under a wider fixed blur. An angular size above zero turns on percentage-closer soft
            // shadows, whose sample pattern rotates every frame; TAA cannot settle that on swaying grass, and it read as shadow
            // sparkle while walking. The 2026-10-02 walk measured the strong sparkles at 0.00155 with 1.0, 0.00046 with 0,
            // and 0.00023 with 0 and a blur of 2 - the October fix this restores (it was lost with the Oct 3-14 work).
            LightAngularDistance = 0f,
            ShadowBlur = 2.0f,
            // SHADOW BIAS WAS 0.03 - BELOW GODOT'S OWN DEFAULT OF 0.1 - AND THAT IS THE BEST EXPLANATION FOUND
            // FOR THE OWNER'S FLICKER. A bias that low leaves the shadow map comparing a surface against its own
            // depth, so surfaces at a grazing angle to the sun self-shadow in a pattern that changes with every
            // sub-pixel camera move. It shows up exactly where it was measured: a full-frame-rate capture of
            // ordinary play (Movie Maker, no screenshot stall) put the pixels that go bright->black->bright on the
            // roof underside, the posts and the anvil platform of the smithy - grazing, shadowed surfaces - and
            // nowhere else in the frame. 0.15 sits above the default with margin, and the normal bias stays at 1.0
            // so the shadow does not detach from its caster.
            // SHADOWS. Bias was 0.15 with normal bias 1.0, which is the recipe for peter-panning - the shadow
            // detaching from the caster's base, so a fence post or a wall loses the contact shadow that grounds
            // it. The documented remedy is to RAISE the normal bias and LOWER the bias, not the reverse.
            ShadowBias = 0.02f,
            ShadowNormalBias = 2.0f,
            // 90 m left everything past 90 m in a 2 km world completely unshadowed and lit only by ambient sky
            // plus SDFGI - the far village, the hillside and the tree line were flat by construction.
            DirectionalShadowMaxDistance = 350f,
            DirectionalShadowBlendSplits = true,
        };
        var environment = new Godot.Environment
        {
            // Indoors there is no sky to see: a closed colour behind the walls, lit by its own ambient, so a hole in
            // the geometry reads as dark rather than as daylight through the hill.
            BackgroundMode = exterior ? Godot.Environment.BGMode.Sky : Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.03f, 0.03f, 0.035f),
            // Size512, not 256: at 256 the sky IBL is a blurry average, which is precisely the uniform ambient
            // fill that flattens a scene - every surface receives nearly the same sky term whatever way it faces.
            // At 512 the gradient reaches the shading and a north wall genuinely differs from a south one.
            Sky = exterior ? new Sky { SkyMaterial = new ShaderMaterial { Shader = new Shader { Code = SkyShader } }, RadianceSize = Sky.RadianceSizeEnum.Size512,
                ProcessMode = Sky.ProcessModeEnum.Automatic } : null,
            AmbientLightSource = exterior ? Godot.Environment.AmbientSource.Sky : Godot.Environment.AmbientSource.Color,
            AmbientLightColor = exterior ? Colors.Black : new Color(0.16f, 0.15f, 0.13f),
            // DS Phase B (2026-09-27): directionality. Full-strength sky ambient (energy 1, sky contribution 1) lights
            // every surface from every direction at once, which is why the 27-view survey read flat and
            // directionless with no contact shadows - the same failure Digital Foundry describes on Crimson Desert
            // with a lean denoiser, and the same one a reader there describes as "grass all the exact same hue...
            // all wood the same brown... all stone the same light grey". The features were already on (AgX, SSAO,
            // SSIL, SDFGI, volumetric fog); what was missing was the balance. Ambient is dropped to 0.55 with the
            // sky still contributing, the sun is raised to 1.85 so it dominates, and exposure comes down to hold
            // the overall level.
            AmbientLightSkyContribution = 0.9f,
            // DS overnight 2026-09-27: FILL DOWN, SHADOW DEPTH UP. The owner's standing brief is "hard directional
            // sun with crisp, dark cast shadows that give every object form and ground contact", and "softness is
            // not the goal; shadow depth and contrast are". A video review of a walk said the sun and its shadows
            // are present - so the geometry of the light is right - but the frames show the distant hillside as a
            // flat, uniform wash and the ground as one flat field, which is a FILL problem, not a sun problem.
            // SDFGI with read_sky_light is the dominant fill in this scene: the previous session measured that
            // halving sdfgi_energy moved mean luma 22 points while a 66% ambient cut moved it 4. So the fill comes
            // down here, the exposure comes back up to hold the overall level, and the separation is left to the
            // sun. Ambient 0.55 -> 0.30 and sdfgi 1.0 -> 0.55; exposure 0.82 -> 1.02 to keep the frame at the
            // luminance it had, so what changes is CONTRAST rather than brightness.
            AmbientLightEnergy = exterior ? 0.30f : 0.55f,
            ReflectedLightSource = exterior ? Godot.Environment.ReflectionSource.Sky : Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            // DS overnight 2026-09-27: EXPOSURE DOWN, CONTRAST UP. A video review of a walk reported the
            // character's white top "noticeably overexposed/blown out under the direct sunlight, appearing close
            // to pure clipped white" and the sky "bright with washed-out, high-exposure clouds". With the sun at
            // 1.85 and exposure at 1.05, lit whites were landing above the point where AgX can hold detail. AgX is
            // chosen precisely because it shoulders off instead of clipping, so the fix is to stop pushing the
            // signal into the shoulder and to let the contrast carry the separation instead.
            TonemapExposure = 1.02f,
            TonemapAgxContrast = 1.34f,
            SsaoEnabled = true, SsaoRadius = 1.4f, SsaoIntensity = 2.2f, SsaoPower = 1.6f, SsaoDetail = 0.6f, SsaoHorizon = 0.06f, SsaoSharpness = 0.98f,
            // BOTH TO 0.0, on the lighting research. These were 0.45 and 0.5, and both are documented as
            // unphysical. `ssao_light_affect` makes SSAO darken DIRECTLY LIT surfaces - impossible, and because
            // SSAO is screen-space it darkens relative to the CAMERA, not the light, so it slides along a wall
            // base as you pan. `ssao_ao_channel_affect` double-applies occlusion on materials that already carry
            // an AO map, which is now 306 of them, so leaving it at 0.5 would have driven the crevices of every
            // AO-mapped surface toward black.
            SsaoLightAffect = 0.0f, SsaoAOChannelAffect = 0.0f,
            SsilEnabled = true, SsilRadius = 4f, SsilIntensity = 0.9f, SsilSharpness = 0.98f, SsilNormalRejection = 1f,
            SdfgiEnabled = true, SdfgiCascades = 6, SdfgiMinCellSize = 0.2f, SdfgiUseOcclusion = false, SdfgiReadSkyLight = true, SdfgiBounceFeedback = 0.5f,
            // 50 percent, not 75: the docs say 50 "is usually the best choice for scenes that don't feature much
            // verticality", and a village on a moor is exactly that. Free quality.
            SdfgiEnergy = 0.55f, SdfgiNormalBias = 1.1f, SdfgiProbeBias = 1.1f, SdfgiYScale = Godot.Environment.SdfgiyScale.Scale50Percent,
            // GLOW WAS NEVER FIRING. The HDR threshold is relative to the tonemapper's white value, and under AgX
            // that is `agx_white = 16.29` - so the old 1.1 meant 17.9, which nothing in this scene can reach. The
            // bright sky, the cloud breaks and any wet-stone specular have been silently not blooming.
            GlowEnabled = true, GlowIntensity = 0.35f, GlowStrength = 1f, GlowBloom = 0.02f, GlowHdrThreshold = 1.0f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
            // FogSkyAffect was 0, so the sky was never fogged and the horizon read as a hard cut-out against the
            // hills. 0.70 puts the sky into the same air as everything else.
            FogEnabled = true, FogMode = Godot.Environment.FogModeEnum.Exponential, FogDensity = 0.0016f, FogAerialPerspective = 0.75f, FogSkyAffect = 0.70f,
            FogLightColor = new Color(0.66f, 0.68f, 0.70f), FogSunScatter = 0.05f,
            VolumetricFogEnabled = true, VolumetricFogDensity = 0.0025f, VolumetricFogAlbedo = new Color(0.86f, 0.87f, 0.9f), VolumetricFogAnisotropy = 0.35f,
            VolumetricFogLength = 96f, VolumetricFogDetailSpread = 2f, VolumetricFogGIInject = 0.6f, VolumetricFogAmbientInject = 0.35f,
            VolumetricFogSkyAffect = 0f, VolumetricFogTemporalReprojectionEnabled = true,
        };
        // Phase B (B0.3): Sky3D draws the sky and drives the sun and moon over this environment; or the HDRI; or the Phase-A sky and sun.
        string? skyWhy = null;
        RenderTiers.Apply(environment, sun);
        // The region's air last, so it wins over the built-in numbers and over the tier's own overrides.
        // No sky over an interior, so no atmosphere model, no Sky3D and no HDRI either.
        if (exterior && SkyView.ApplyAtmosphere(environment, _art.Root) is { } atmosphere)
            GD.Print($"UNNAMED {atmosphere}");
        if (exterior && VisualOptions.Sky == "sky3d" && SkyView.BuildSky3D(root, environment, sun, out skyWhy) is not null)
            Coverage.Resolved("sky", "sky3d", "Sky3D 2.1.0 over the game's environment, at a set hour");
        else
        {
            if (exterior && VisualOptions.Sky == "sky3d")
                Coverage.Fallback("sky", "sky3d", skyWhy ?? "Sky3D did not build: the Phase-A sky stands");
            if (exterior && VisualOptions.Sky == "hdri" && !SkyView.ApplyHdri(environment, _art.Root, out string? hdriWhy))
                Coverage.Fallback("sky", "hdri", hdriWhy ?? "the HDRI did not load: the Phase-A sky stands");
            if (exterior)
                root.AddChild(sun);
            else
                sun.Free();
            root.AddChild(new WorldEnvironment { Environment = environment });
        }
        // Each building's particle emitters (B11: the smithy's chimney smoke), relative to its footprint's centre on the ground.
        var recipes = _recipes;
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
                // The same single owner of a light's energy as a prop's light gets. An interior lamp is authored
                // "daylight": 0 (a hearth burns all day) and so is untouched, but a binding that asks for a
                // daylight term here must not be silently ignored: this is the path the seven cottages' lamps
                // and the lodge hearth are built on, and a term that only worked on the prop path would be a
                // second defect of exactly the shape this round is fixing.
                //
                // AND THE FLAME'S WAVER, WHICH THIS PATH DID NOT HAVE AT ALL UNTIL 2026-09-29. This block
                // attached the node only when a binding asked for a daylight term, and always with
                // `amount: 0f` - so a light standing over a burning fire was a CONSTANT. Measured out of this
                // very dump, which prints every light's live energy: the thirteen Cairnwold lanterns, which go
                // through the prop path and do carry a flame, read 0.101732 to 0.132700 - thirteen different
                // values spread either side of their 0.12 base, which IS the waver, frozen at its own phase by
                // the capture's time freeze. In the same dump `InteriorLight_cw_longhouse_0`, 1.7 m above a
                // 3 x 3 bed of torch_flame, reads exactly 2.600000. The lodge's hearth is that room's key
                // light; a fire throwing a perfectly steady pool is the defect the lantern had, one path over.
                // `Lit` is the prop path's own test, so this cannot spread: a building whose only emitter is a
                // chimney's smoke - every cottage, and the smithy - keeps its steady lamp, and the seven
                // cottage lamps stay at exactly 1.6 and 1.3.
                bool lit = Art.FlameFlicker.Lit(look.Emitters);
                if (lit || lights[i].Daylight > 0f)
                    Art.FlameFlicker.Attach(light, lit ? Art.FlameFlicker.DefaultAmount : 0f, lights[i].Daylight);
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
