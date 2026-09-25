// UNNAMED Presentation - the ground scatter: grass, leaf litter and stones in MultiMesh chunks, by the scatter rules (Phase A, the visual audit's V3)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Art;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// What grows and lies on the ground, drawn from <c>res://Art/scatter_rules.json</c>: each kind a procedural mesh scattered over a jittered
/// grid at its greatest density, each point kept with the probability its rules give there - by the ground under it (blended across the cells'
/// borders as the terrain shader blends them), patchy, bare round the buildings and on the worn paths, thinner under the trees, and clear of
/// every building, wall, rock, container, station and person. Placed from the world seed, chunk by chunk, so a world always shows the same
/// ground; drawn as one MultiMesh a chunk and a mesh (never a node an instance), each chunk fading out with distance. Scenery only: nothing
/// here is read by the simulation, and nothing collides.
/// </summary>
public partial class ScatterView : Node3D
{
    private const float FieldStep = 0.5f;

    /// <summary>A kind and its meshes: variants (one is picked a placement) or, for a prepared model, its levels (every placement in each, by distance).</summary>
    private sealed record Kind(ScatterKind Rules, Mesh[] Meshes, bool Levels = false);

    private readonly List<Kind> _kinds = new();
    private GroundField _ground = null!;
    private ScatterRules _rules = ScatterRules.None;
    private int _columns, _rows;
    // Distance fields over the region, capped: to a building's walls (negative inside), any box, any round structure less its radius, a site
    // where someone stands or something is worked, and a tree's trunk.
    private float[] _building = Array.Empty<float>(), _box = Array.Empty<float>(), _circle = Array.Empty<float>(), _spot = Array.Empty<float>(),
        _tree = Array.Empty<float>();
    private ulong? _builtFor;
    private WindField? _wind;

    /// <summary>Instances placed by kind, and how long placing them took, for the reports.</summary>
    public IReadOnlyDictionary<string, int> Counts => _counts;

    public double BuildMs { get; private set; }

    private readonly SortedDictionary<string, int> _counts = new(StringComparer.Ordinal);

    /// <summary>The ground, the rules and the layout: the distance fields and the kinds' meshes are made once here; the placing waits for a world.</summary>
    public void Bind(ArtLibrary art, GroundField ground, ScatterRules rules, RegionLayout layout, IEnumerable<string> buildingPrefixes)
    {
        _ground = ground;
        _rules = rules;
        BuildFields(layout, buildingPrefixes);
        foreach (var kind in rules.Kinds)
        {
            if (kind.ModelId is { } model)
            {
                // Prepared plant models (Phase B, B0.5) draw only when the run asks for them (--visual plants=models).
                if (VisualOptions.Plants != "models")
                    continue;
                if (_wind is null)
                {
                    WindField.Register();
                    _wind = new WindField { Name = "Wind" };
                    AddChild(_wind);
                }
                var levels = ModelMeshes(art, kind, model);
                if (levels.Length == 0)
                {
                    art.Coverage.Fallback("scatter", kind.Name, art.Why(model) ?? "the model would not load", model);
                    continue;
                }
                art.Coverage.Resolved("scatter", kind.Name, $"{model} ({levels.Length} levels)");
                _kinds.Add(new Kind(kind, levels, true));
                continue;
            }
            var meshes = Meshes(art, kind);
            if (meshes.Length == 0)
            {
                GD.PushWarning($"UNNAMED scatter: {kind.Name} names a mesh the scatter does not make ({kind.Mesh}); left out");
                continue;
            }
            _kinds.Add(new Kind(kind, meshes));
        }
        foreach (string problem in rules.Problems)
            GD.PushWarning($"UNNAMED scatter: {problem}; left out");
    }

    /// <summary>Place the scatter for a world (its seed); the same seed again keeps what is placed.</summary>
    public void Build(ulong worldSeed)
    {
        if (_builtFor == worldSeed || _kinds.Count == 0)
            return;
        _builtFor = worldSeed;
        foreach (var child in GetChildren())
        {
            if (child == _wind)
                continue;
            RemoveChild(child);
            child.QueueFree();
        }
        _counts.Clear();
        ulong started = Time.GetTicksUsec();
        var region = _ground.Region;
        foreach (var kind in _kinds)
        {
            float chunk = kind.Rules.ChunkM ?? _rules.ChunkM;
            int total = 0;
            float step = 1f / MathF.Sqrt(kind.Rules.MaxPerM2);
            int cz = 0;
            for (float z0 = region.Position.Y; z0 < region.End.Y; z0 += chunk, cz++)
            {
                int cx = 0;
                for (float x0 = region.Position.X; x0 < region.End.X; x0 += chunk, cx++)
                {
                    var rng = new Random(ChunkSeed(worldSeed, kind.Rules.Name, cx, cz));
                    var per = kind.Meshes.Select(_ => new List<(Transform3D, Color)>()).ToArray();
                    for (float z = z0; z < z0 + chunk; z += step)
                    for (float x = x0; x < x0 + chunk; x += step)
                    {
                        float px = x + (float)rng.NextDouble() * step, pz = z + (float)rng.NextDouble() * step;
                        double roll = rng.NextDouble();
                        if (px >= region.End.X || pz >= region.End.Y || px >= x0 + chunk || pz >= z0 + chunk)
                            continue;
                        var w = _ground.CellWeights(px, pz);
                        if (roll >= Density(kind.Rules, px, pz, w) / kind.Rules.MaxPerM2)
                            continue;
                        var (basis, sink, tint) = Place(kind.Rules, rng, w);
                        int m = kind.Levels ? 0 : rng.Next(kind.Meshes.Length);
                        per[m].Add((new Transform3D(basis, new Vector3(px, _ground.Height(px, pz) - sink, pz)), tint));
                    }
                    if (kind.Levels)
                    {
                        if (per[0].Count > 0)
                            AddLevels(kind, per[0], cx, cz);
                        total += per[0].Count;
                        continue;
                    }
                    for (int m = 0; m < kind.Meshes.Length; m++)
                    {
                        if (per[m].Count == 0)
                            continue;
                        var multi = new MultiMesh
                        {
                            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = kind.Meshes[m], InstanceCount = per[m].Count,
                        };
                        for (int i = 0; i < per[m].Count; i++)
                        {
                            multi.SetInstanceTransform(i, per[m][i].Item1);
                            multi.SetInstanceColor(i, per[m][i].Item2);
                        }
                        AddChild(new MultiMeshInstance3D
                        {
                            Name = $"{kind.Rules.Name}_{cx}_{cz}_{m}", Multimesh = multi,
                            CastShadow = kind.Rules.Shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                            GIMode = GeometryInstance3D.GIModeEnum.Disabled, VisibilityRangeEnd = kind.Rules.FadeM, VisibilityRangeEndMargin = 10f,
                            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
                        });
                        total += per[m].Count;
                    }
                }
            }
            _counts[kind.Rules.Name] = total;
        }
        BuildMs = Math.Round((Time.GetTicksUsec() - started) / 1000.0, 1);
        GD.Print($"UNNAMED scatter: {string.Join(", ", _counts.Select(c => $"{c.Value} {c.Key}"))} placed for world seed {worldSeed:x16} in {BuildMs} ms");
    }

    /// <summary>
    /// A chunk of a prepared model: the same placements in one MultiMesh a level, each level drawn over its band of distance (the rules'
    /// <c>lods_m</c>, the last to the kind's fade), the tint in the instances' custom data (the plant's vertex colours carry its wind).
    /// </summary>
    private void AddLevels(Kind kind, List<(Transform3D, Color)> placed, int cx, int cz)
    {
        var from = kind.Rules.LodsM ?? new[] { 0f, 8f, 20f, 40f };
        int levels = Math.Min(kind.Meshes.Length, from.Count);
        for (int level = 0; level < levels; level++)
        {
            float begin = from[level], end = level + 1 < levels ? from[level + 1] : kind.Rules.FadeM;
            if (end <= begin)
                continue;
            var multi = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = kind.Meshes[level], InstanceCount = placed.Count,
            };
            for (int i = 0; i < placed.Count; i++)
            {
                multi.SetInstanceTransform(i, placed[i].Item1);
                multi.SetInstanceCustomData(i, placed[i].Item2);
            }
            AddChild(new MultiMeshInstance3D
            {
                Name = $"{kind.Rules.Name}_{cx}_{cz}_lod{level}", Multimesh = multi,
                // Only the near, full levels throw shadows; the far cards would cast flat slabs.
                CastShadow = kind.Rules.Shadows && level < 2 ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                VisibilityRangeBegin = begin, VisibilityRangeBeginMargin = begin > 0 ? 1.5f : 0,
                VisibilityRangeEnd = end, VisibilityRangeEndMargin = level + 1 < levels ? 1.5f : 10f,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
            });
        }
    }

    /// <summary>A chunk's own random stream: the world seed, the kind and the chunk, so every chunk is the same in every run of that world.</summary>
    private static int ChunkSeed(ulong worldSeed, string kind, int cx, int cz)
    {
        ulong hash = 14695981039346656037UL;
        void Mix(ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                hash ^= (value >> (i * 8)) & 0xff;
                hash *= 1099511628211UL;
            }
        }
        Mix(worldSeed);
        foreach (char c in kind)
            Mix(c);
        Mix((ulong)(uint)cx);
        Mix((ulong)(uint)cz);
        return (int)(hash ^ (hash >> 32)) & int.MaxValue;
    }

    // ── where: the rules' density at a point ─────────────────────────────────

    private float Density(ScatterKind kind, float x, float z, Vector4 w)
    {
        float d = 0, yard = 0, lush = 0;
        for (int i = 0; i < 4; i++)
        {
            if (_ground.Grounds[i] is not { } ground || w[i] <= 0)
                continue;
            d += w[i] * kind.PerM2.GetValueOrDefault(ground);
            if (kind.BareYardGrounds.Contains(ground))
                yard += w[i];
            lush += w[i] * kind.Lush.GetValueOrDefault(ground);
        }
        if (kind.Patches.Count > 0)
        {
            var clump = kind.Patches[0];
            float c = GroundField.SmoothStep(clump.From, clump.To, _ground.MacroAt(x / clump.ScaleM, z / clump.ScaleM, 1));
            float f = 1;
            if (kind.Patches.Count > 1)
            {
                var fine = kind.Patches[1];
                f = GroundField.SmoothStep(fine.From, fine.To, _ground.MacroAt(x / fine.ScaleM, z / fine.ScaleM, 2));
            }
            d *= 0.3f + 0.7f * c * (0.5f + 0.5f * f);
        }
        if (yard > 0 && kind.BareYardToM > kind.BareYardFromM)
            d *= Mathf.Lerp(1f, GroundField.SmoothStep(kind.BareYardFromM, kind.BareYardToM, Field(_building, x, z)), yard);
        float tree = Field(_tree, x, z);
        if (kind.UnderTreesKeep < 1 && kind.UnderTreesToM > kind.UnderTreesFromM)
            d *= kind.UnderTreesKeep + (1 - kind.UnderTreesKeep) * GroundField.SmoothStep(kind.UnderTreesFromM, kind.UnderTreesToM, tree);
        if (kind.NearTreesAdd > 0 && kind.NearTreesToM > kind.NearTreesFromM)
            d += kind.NearTreesAdd * (1f - GroundField.SmoothStep(kind.NearTreesFromM, kind.NearTreesToM, tree));
        float path = _ground.PathAt(x, z);
        if (kind.PathShoulders > 0)
            d += kind.PathShoulders * GroundField.SmoothStep(0.05f, 0.3f, path) * (1f - GroundField.SmoothStep(0.4f, 0.7f, path));
        d *= Clear(x, z, kind.ClearMargin, kind.OffPath, path);
        if (!kind.OffPath)
            d *= 1f - GroundField.SmoothStep(0.45f, 0.7f, path);
        return d;
    }

    /// <summary>1 in the open, 0 inside a footprint, thinning over a metre or two outside one; 0 on a path (for most kinds), under water, on a steep slope.</summary>
    private float Clear(float x, float z, float margin, bool offPath, float path)
    {
        if (_ground.Height(x, z) < _rules.NeverBelowM || _ground.Slope(x, z) > _rules.NeverSlope)
            return 0;
        float k = GroundField.SmoothStep(0.2f, 0.2f + 2.2f * margin, Field(_building, x, z));
        k = Math.Min(k, GroundField.SmoothStep(0.05f, 0.05f + 0.9f * margin, Field(_box, x, z)));
        k = Math.Min(k, GroundField.SmoothStep(0.02f, 0.02f + 0.6f * margin, Field(_circle, x, z)));
        k = Math.Min(k, GroundField.SmoothStep(0.6f, 1.2f, Field(_spot, x, z)));
        if (offPath)
            k *= 1f - GroundField.SmoothStep(0.1f, 0.55f, path);
        return k;
    }

    // ── how: each kind's look at a point ────────────────────────────────────

    private (Basis Basis, float Sink, Color Tint) Place(ScatterKind kind, Random rng, Vector4 w)
    {
        float Lush() => Enumerable.Range(0, 4).Sum(i => _ground.Grounds[i] is { } g ? w[i] * kind.Lush.GetValueOrDefault(g) : 0);
        float Tall() => Enumerable.Range(0, 4).Sum(i => _ground.Grounds[i] is { } g ? w[i] * kind.Tall.GetValueOrDefault(g) : 0);
        if (kind.ModelId is not null)
        {
            // A prepared plant: turned any way, leaning a little, uniformly sized within the kind's range (larger where the ground grows
            // it tall - the wind shader needs the scale uniform), drier and paler where the ground is poor.
            float yaw = (float)rng.NextDouble() * Mathf.Tau;
            float size = Mathf.Lerp(kind.SizeMin, kind.SizeMax, (float)rng.NextDouble()) * (1f + Tall());
            var basis = Basis.FromEuler(new Vector3(((float)rng.NextDouble() - 0.5f) * 0.14f, yaw, ((float)rng.NextDouble() - 0.5f) * 0.14f)) * Basis.FromScale(Vector3.One * size);
            var tint = new Color(0.98f, 0.90f, 0.70f).Lerp(Colors.White, Math.Clamp(Lush(), 0, 1));
            float drift = 0.86f + (float)rng.NextDouble() * 0.24f;
            return (basis, 0.02f * size, new Color(tint.R * drift, tint.G * drift, tint.B * drift));
        }
        switch (kind.Mesh)
        {
            case "grass_clumps":
            {
                float yaw = (float)rng.NextDouble() * Mathf.Tau;
                float lean = ((float)rng.NextDouble() - 0.5f) * 0.25f;
                float size = 0.75f + (float)rng.NextDouble() * 0.55f;
                float tall = size * (0.8f + (float)rng.NextDouble() * 0.5f) * (0.75f + Tall());
                var basis = Basis.FromEuler(new Vector3(lean, yaw, ((float)rng.NextDouble() - 0.5f) * 0.25f)) * Basis.FromScale(new Vector3(size, tall, size));
                // Greener in the wood and the wet, straw-coloured on the worked dirt and the quarry, with a little drift a clump.
                var tint = new Color(0.92f, 0.86f, 0.62f).Lerp(new Color(0.78f, 0.88f, 0.70f), Math.Clamp(Lush(), 0, 1));
                float drift = 0.88f + (float)rng.NextDouble() * 0.24f;
                return (basis, 0.03f, new Color(tint.R * drift, tint.G * drift, tint.B * drift));
            }
            case "leaf_litter":
            {
                float size = 0.6f + (float)rng.NextDouble() * 0.5f;
                var basis = Basis.FromEuler(new Vector3(0, (float)rng.NextDouble() * Mathf.Tau, 0)) * Basis.FromScale(new Vector3(size, 1, size));
                float v = 0.85f + (float)rng.NextDouble() * 0.25f;
                return (basis, -0.012f - (float)rng.NextDouble() * 0.01f, new Color(v, v, v));
            }
            default:
            {
                // Stones: mostly pebbles, a few fist- and head-sized, sunk into the ground by a third.
                float r = (float)rng.NextDouble();
                float size = 0.05f + r * r * r * 0.32f;
                var basis = Basis.FromEuler(new Vector3(((float)rng.NextDouble() - 0.5f) * 0.6f, (float)rng.NextDouble() * Mathf.Tau, ((float)rng.NextDouble() - 0.5f) * 0.6f))
                    * Basis.FromScale(new Vector3(size * (0.8f + (float)rng.NextDouble() * 0.5f), size * (0.45f + (float)rng.NextDouble() * 0.3f), size));
                float v = 0.8f + (float)rng.NextDouble() * 0.35f;
                return (basis, size * 0.3f, new Color(v, v * 0.98f, v * 0.95f));
            }
        }
    }

    // ── the distance fields ─────────────────────────────────────────────────

    private void BuildFields(RegionLayout layout, IEnumerable<string> buildingPrefixes)
    {
        var region = _ground.Region;
        _columns = (int)MathF.Ceiling(region.Size.X / FieldStep) + 1;
        _rows = (int)MathF.Ceiling(region.Size.Y / FieldStep) + 1;
        float[] Blank(float cap)
        {
            var field = new float[_columns * _rows];
            Array.Fill(field, cap);
            return field;
        }
        _building = Blank(10f);
        _box = Blank(3f);
        _circle = Blank(3f);
        _spot = Blank(3f);
        _tree = Blank(8f);
        var boxes = layout.Space.Blockers.OfType<BoxBlocker>().ToList();
        var circles = layout.Space.Blockers.OfType<CircleBlocker>().ToList();
        Rect2 Rect(long minX, long minZ, long maxX, long maxZ) => new(minX / 1000f, minZ / 1000f, (maxX - minX) / 1000f, (maxZ - minZ) / 1000f);
        // Each building as a whole (its walls' extent), not wall by wall: nothing grows inside.
        foreach (string prefix in buildingPrefixes)
        {
            var walls = boxes.Where(b => b.Id.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            if (walls.Count > 0)
                Stamp(_building, 10f, Rect(walls.Min(w => w.MinXMm), walls.Min(w => w.MinZMm), walls.Max(w => w.MaxXMm), walls.Max(w => w.MaxZMm)));
        }
        foreach (var box in boxes)
            Stamp(_box, 3f, Rect(box.MinXMm, box.MinZMm, box.MaxXMm, box.MaxZMm));
        foreach (var door in layout.Doors)
        {
            var b = door.ClosedFootprint;
            Stamp(_box, 3f, Rect(b.MinXMm, b.MinZMm, b.MaxXMm, b.MaxZMm));
        }
        foreach (var circle in circles)
        {
            var centre = new Vector2(circle.CenterXMm / 1000f, circle.CenterZMm / 1000f);
            Stamp(_circle, 3f, centre, circle.RadiusMm / 1000f);
            if (circle.Id.StartsWith("tree_", StringComparison.Ordinal))
                Stamp(_tree, 8f, centre, 0);
        }
        var spots = layout.Containers.Select(c => new Vector2(c.XMm / 1000f, c.ZMm / 1000f))
            .Concat(layout.Stations.Select(s => new Vector2(s.XMm / 1000f, s.ZMm / 1000f)))
            .Concat(layout.Npcs.Select(n => new Vector2(n.XMm / 1000f, n.ZMm / 1000f)))
            .Concat(layout.Nodes.Select(n => new Vector2(n.XMm / 1000f, n.ZMm / 1000f)));
        foreach (var spot in spots)
            Stamp(_spot, 3f, spot, 0);
    }

    /// <summary>The distance to a rectangle (negative inside), written wherever it is nearer than what the field holds, within the cap.</summary>
    private void Stamp(float[] field, float cap, Rect2 rect)
    {
        var region = _ground.Region;
        int x0 = Math.Max(0, (int)((rect.Position.X - cap - region.Position.X) / FieldStep)), x1 = Math.Min(_columns - 1, (int)((rect.End.X + cap - region.Position.X) / FieldStep) + 1);
        int z0 = Math.Max(0, (int)((rect.Position.Y - cap - region.Position.Y) / FieldStep)), z1 = Math.Min(_rows - 1, (int)((rect.End.Y + cap - region.Position.Y) / FieldStep) + 1);
        for (int j = z0; j <= z1; j++)
        for (int i = x0; i <= x1; i++)
        {
            var p = new Vector2(region.Position.X + i * FieldStep, region.Position.Y + j * FieldStep);
            float dx = Math.Max(rect.Position.X - p.X, p.X - rect.End.X), dz = Math.Max(rect.Position.Y - p.Y, p.Y - rect.End.Y);
            float d = dx <= 0 && dz <= 0 ? Math.Max(dx, dz) : MathF.Sqrt(MathF.Max(dx, 0) * MathF.Max(dx, 0) + MathF.Max(dz, 0) * MathF.Max(dz, 0));
            int k = j * _columns + i;
            field[k] = Math.Min(field[k], d);
        }
    }

    /// <summary>The distance to a circle's edge (or a point), written the same way.</summary>
    private void Stamp(float[] field, float cap, Vector2 centre, float radius)
    {
        var region = _ground.Region;
        float reach = radius + cap;
        int x0 = Math.Max(0, (int)((centre.X - reach - region.Position.X) / FieldStep)), x1 = Math.Min(_columns - 1, (int)((centre.X + reach - region.Position.X) / FieldStep) + 1);
        int z0 = Math.Max(0, (int)((centre.Y - reach - region.Position.Y) / FieldStep)), z1 = Math.Min(_rows - 1, (int)((centre.Y + reach - region.Position.Y) / FieldStep) + 1);
        for (int j = z0; j <= z1; j++)
        for (int i = x0; i <= x1; i++)
        {
            float d = new Vector2(region.Position.X + i * FieldStep, region.Position.Y + j * FieldStep).DistanceTo(centre) - radius;
            int k = j * _columns + i;
            field[k] = Math.Min(field[k], d);
        }
    }

    /// <summary>A field at a point, bilinear.</summary>
    private float Field(float[] field, float x, float z)
    {
        var region = _ground.Region;
        float fx = Math.Clamp((x - region.Position.X) / FieldStep, 0, _columns - 1.001f), fz = Math.Clamp((z - region.Position.Y) / FieldStep, 0, _rows - 1.001f);
        int i = (int)fx, j = (int)fz;
        float tx = fx - i, tz = fz - j;
        float a = field[j * _columns + i], b = field[j * _columns + i + 1], c = field[(j + 1) * _columns + i], d = field[(j + 1) * _columns + i + 1];
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
    }

    // ── the meshes ──────────────────────────────────────────────────────────

    /// <summary>
    /// A prepared plant's levels as meshes for the MultiMeshes: each level's surfaces merged into one mesh in the model's space, drawn with
    /// the wind foliage shader from the level's own maps. The albedo is re-mipped keeping its alpha-tested coverage (plain mipmaps thin
    /// cut-out leaves to nothing at a distance), once per distinct image; the normal and roughness maps are the library's own.
    /// </summary>
    private static Mesh[] ModelMeshes(ArtLibrary art, ScatterKind kind, string id)
    {
        var levels = art.ModelLevels(id);
        var meshes = new List<Mesh>();
        var albedos = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        float height = 0;
        foreach (var level in levels)
        {
            var merged = new ArrayMesh();
            foreach (var instance in level.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
            {
                if (instance.Mesh is not { } mesh)
                    continue;
                var toModel = Transform3D.Identity;
                for (Node? n = instance; n is not null && n != level; n = n.GetParent())
                {
                    if (n is Node3D spatial)
                        toModel = spatial.Transform * toModel;
                }
                for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                {
                    var arrays = mesh.SurfaceGetArrays(s);
                    if (toModel != Transform3D.Identity)
                    {
                        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        for (int v = 0; v < vertices.Length; v++)
                            vertices[v] = toModel * vertices[v];
                        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
                        if (arrays[(int)Mesh.ArrayType.Normal].VariantType != Variant.Type.Nil)
                        {
                            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                            for (int v = 0; v < normals.Length; v++)
                                normals[v] = (toModel.Basis * normals[v]).Normalized();
                            arrays[(int)Mesh.ArrayType.Normal] = normals;
                        }
                    }
                    merged.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                    if (height <= 0)
                        height = Math.Max(0.01f, merged.GetAabb().End.Y);
                    merged.SurfaceSetMaterial(merged.GetSurfaceCount() - 1, Foliage(instance.GetActiveMaterial(s) as BaseMaterial3D, kind, height, albedos));
                }
            }
            level.Free();
            if (merged.GetSurfaceCount() == 0)
                break;
            meshes.Add(merged);
        }
        return meshes.ToArray();
    }

    private static Material Foliage(BaseMaterial3D? source, ScatterKind kind, float height, Dictionary<string, Texture2D> albedos)
    {
        var material = new ShaderMaterial { Shader = _foliageShader ??= new Shader { Code = WindField.FoliageShader } };
        material.SetShaderParameter("plant_height", height);
        material.SetShaderParameter("stiffness", kind.Stiffness);
        if (source?.AlbedoTexture is { } albedo && albedo.GetImage() is { } image)
        {
            if (image.IsCompressed())
                image.Decompress();
            image.ClearMipmaps();
            image.Convert(Image.Format.Rgba8);
            string key = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(image.GetData()));
            if (!albedos.TryGetValue(key, out var coverage))
                albedos[key] = coverage = ImageTexture.CreateFromImage(CoverageMips(image, 0.4f));
            material.SetShaderParameter("albedo_tex", coverage);
        }
        if (source is { NormalEnabled: true, NormalTexture: { } normal })
        {
            material.SetShaderParameter("normal_tex", normal);
            material.SetShaderParameter("has_normal", true);
        }
        // glTF's metallic-roughness map is the ARM map (occlusion red, roughness green); Godot's importer sets it as the roughness texture.
        if (source?.RoughnessTexture is { } arm)
        {
            material.SetShaderParameter("arm_tex", arm);
            material.SetShaderParameter("has_arm", true);
        }
        return material;
    }

    private static Shader? _foliageShader;

    private static Mesh[] Meshes(ArtLibrary art, ScatterKind kind)
    {
        switch (kind.Mesh)
        {
            case "grass_clumps":
            {
                var material = new StandardMaterial3D
                {
                    AlbedoTexture = GrassAtlas(), VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, AlphaScissorThreshold = 0.4f,
                    AlphaAntialiasingMode = BaseMaterial3D.AlphaAntiAliasing.AlphaToCoverage, CullMode = BaseMaterial3D.CullModeEnum.Back, Roughness = 0.85f,
                    MetallicSpecular = 0.25f, BacklightEnabled = true, Backlight = new Color(0.12f, 0.14f, 0.05f),
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                };
                return new[] { GrassClump(material, 0), GrassClump(material, 1) };
            }
            case "leaf_litter":
            {
                var material = new StandardMaterial3D
                {
                    AlbedoTexture = LeafAtlas(), VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, AlphaScissorThreshold = 0.5f,
                    AlphaAntialiasingMode = BaseMaterial3D.AlphaAntiAliasing.AlphaToCoverage, CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 0.9f,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                };
                return new[] { FlatCard(material) };
            }
            case "stones":
            {
                Material material;
                if (kind.Material is { } id && art.WorldMaps(id) is { } maps)
                {
                    material = new OrmMaterial3D
                    {
                        AlbedoTexture = maps.Albedo, NormalEnabled = maps.Normal is not null, NormalTexture = maps.Normal, OrmTexture = maps.Orm, Uv1Triplanar = true,
                        Uv1Scale = Vector3.One * 0.6f, AlbedoColor = new Color(0.72f, 0.70f, 0.66f),
                        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                    };
                    art.Coverage.Resolved("scatter", kind.Name, id);
                }
                else
                {
                    material = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.49f, 0.46f), Roughness = 0.9f };
                    art.Coverage.Fallback("scatter", kind.Name, kind.Material is { } m ? art.Why(m) ?? "its material would not load" : "no material named", kind.Material);
                }
                return Enumerable.Range(0, 4).Select(i => Stone(material, 100 + i)).ToArray();
            }
            default:
                return Array.Empty<Mesh>();
        }
    }

    /// <summary>Three cards crossed at 60 degrees, 0.6 m wide and 0.5 m tall, from half <paramref name="half"/> of the grass atlas.</summary>
    private static Mesh GrassClump(Material material, int half)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colours = new List<Color>();
        var indices = new List<int>();
        const float w = 0.4f, h = 0.5f;
        for (int c = 0; c < 3; c++)
        {
            float a = c * Mathf.Pi / 3;
            var along = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var face = new Vector3(-along.Z, 0, along.X);
            int b = vertices.Count;
            // Two rows of vertices so the top can splay outwards a little.
            for (int row = 0; row < 2; row++)
            for (int col = 0; col < 2; col++)
            {
                float sx = col == 0 ? -1 : 1;
                vertices.Add(along * w * sx * (row == 0 ? 0.75f : 1.1f) + Vector3.Up * h * row);
                normals.Add((Vector3.Up * 0.8f + along * sx * 0.25f + face * 0.15f).Normalized());
                uvs.Add(new Vector2(half * 0.5f + (col == 0 ? 0.002f : 0.498f), row == 0 ? 0.998f : 0.002f));
                float shade = row == 0 ? 0.62f : 1f;
                colours.Add(new Color(shade, shade, shade));
            }
            // Both windings over the same vertices: each side keeps the bent-up normal (a two-sided material would flip it on the back).
            indices.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3, b, b + 1, b + 2, b + 1, b + 3, b + 2 });
        }
        return Build(vertices, normals, uvs, colours, indices, material);
    }

    private static Mesh FlatCard(Material material)
    {
        var vertices = new List<Vector3> { new(-0.5f, 0, -0.5f), new(0.5f, 0, -0.5f), new(-0.5f, 0, 0.5f), new(0.5f, 0, 0.5f) };
        var normals = Enumerable.Repeat(Vector3.Up, 4).ToList();
        var uvs = new List<Vector2> { new(0, 0), new(1, 0), new(0, 1), new(1, 1) };
        var colours = Enumerable.Repeat(Colors.White, 4).ToList();
        return Build(vertices, normals, uvs, colours, new List<int> { 0, 1, 2, 1, 3, 2 }, material);
    }

    private static Mesh Build(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<Color> colours, List<int> indices, Material material)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    /// <summary>A stone: an icosphere twice subdivided, pushed about by noise and flattened underneath.</summary>
    private static Mesh Stone(Material material, int seed)
    {
        float t = (1 + MathF.Sqrt(5)) / 2;
        var v = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        var f = new List<(int, int, int)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
        };
        for (int i = 0; i < v.Count; i++)
            v[i] = v[i].Normalized();
        for (int level = 0; level < 2; level++)
        {
            var mid = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (!mid.TryGetValue(key, out int m))
                {
                    m = v.Count;
                    v.Add(((v[a] + v[b]) / 2).Normalized());
                    mid[key] = m;
                }
                return m;
            }
            var next = new List<(int, int, int)>();
            foreach (var (a, b, c) in f)
            {
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.AddRange(new[] { (a, ab, ca), (b, bc, ab), (c, ca, bc), (ab, bc, ca) });
            }
            f = next;
        }
        var noise = new FastNoiseLite { Seed = seed, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.9f, FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 3 };
        for (int i = 0; i < v.Count; i++)
        {
            var p = v[i] * (1f + noise.GetNoise3Dv(v[i] * 1.3f) * 0.35f);
            if (p.Y < -0.35f)
                p.Y = -0.35f + (p.Y + 0.35f) * 0.3f;
            v[i] = p;
        }
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var p in v)
            tool.AddVertex(p);
        // The icosphere's winding is counter-clockwise from outside; Godot's front face is clockwise.
        foreach (var (a, b, c) in f)
        {
            tool.AddIndex(a);
            tool.AddIndex(c);
            tool.AddIndex(b);
        }
        tool.GenerateNormals();
        tool.SetMaterial(material);
        return tool.Commit();
    }

    /// <summary>Two grass clumps side by side (512 x 512 each): tapered, curved blades, dark at the root and paler at the tip, some dry.</summary>
    private static ImageTexture GrassAtlas()
    {
        const int W = 1024, H = 512;
        var buffer = new float[W * H * 4];
        // Behind every blade, the grass's own mean colour at zero alpha, so filtering never bleeds a fringe.
        for (int i = 0; i < W * H; i++)
        {
            buffer[i * 4] = 0.28f;
            buffer[i * 4 + 1] = 0.32f;
            buffer[i * 4 + 2] = 0.12f;
        }
        var rng = new Random(7);
        for (int half = 0; half < 2; half++)
        {
            int blades = half == 0 ? 72 : 60;
            for (int n = 0; n < blades; n++)
            {
                float cx = half * 512 + 256 + (float)(Gauss(rng) * 95);
                float height = 240 + (float)rng.NextDouble() * 260;
                float lean = (float)(Gauss(rng) * 0.35) + (cx - (half * 512 + 256)) / 400f;
                float bend = lean * (0.6f + (float)rng.NextDouble() * 0.8f);
                float width = 7 + (float)rng.NextDouble() * 8;
                bool dry = rng.NextDouble() < (half == 0 ? 0.12 : 0.3);
                var root = dry ? new Color(0.24f, 0.22f, 0.10f) : new Color(0.12f, 0.16f, 0.06f);
                var tip = dry ? new Color(0.52f, 0.46f, 0.27f) : new Color(0.34f, 0.39f, 0.15f);
                float jitter = 0.85f + (float)rng.NextDouble() * 0.3f;
                for (float s = 0; s <= 1f; s += 0.5f / height)
                {
                    float y = H - 1 - s * height;
                    float x = cx + lean * s * height * 0.35f + bend * s * s * height * 0.4f;
                    float hw = width * 0.5f * MathF.Pow(1 - s, 0.9f) + 0.4f;
                    var colour = root.Lerp(tip, MathF.Pow(s, 0.7f));
                    for (int px = (int)(x - hw - 1); px <= (int)(x + hw + 1); px++)
                    {
                        if (px < half * 512 || px >= half * 512 + 512 || y < 0)
                            continue;
                        float d = MathF.Abs(px + 0.5f - x);
                        float a = Math.Clamp(hw - d + 0.5f, 0, 1);
                        if (a <= 0)
                            continue;
                        // The midrib a little lighter.
                        float rib = 1f + 0.12f * (1f - Math.Clamp(d / hw, 0, 1));
                        int i = ((int)y * W + px) * 4;
                        buffer[i] = Mathf.Lerp(buffer[i], colour.R * jitter * rib, a);
                        buffer[i + 1] = Mathf.Lerp(buffer[i + 1], colour.G * jitter * rib, a);
                        buffer[i + 2] = Mathf.Lerp(buffer[i + 2], colour.B * jitter * rib, a);
                        buffer[i + 3] = Math.Max(buffer[i + 3], a);
                    }
                }
            }
        }
        return ImageTexture.CreateFromImage(CoverageMips(ToImage(buffer, W, H), 0.4f));
    }

    /// <summary>A patch of fallen leaves (512 px): lobed leaves in browns, ochres and a dull red, each with a darker rim and a vein.</summary>
    private static ImageTexture LeafAtlas()
    {
        const int S = 512;
        var buffer = new float[S * S * 4];
        for (int i = 0; i < S * S; i++)
        {
            buffer[i * 4] = 0.33f;
            buffer[i * 4 + 1] = 0.24f;
            buffer[i * 4 + 2] = 0.12f;
        }
        var palette = new[]
        {
            new Color(0.36f, 0.25f, 0.12f), new Color(0.46f, 0.33f, 0.14f), new Color(0.30f, 0.20f, 0.11f), new Color(0.50f, 0.40f, 0.20f),
            new Color(0.38f, 0.19f, 0.10f), new Color(0.26f, 0.22f, 0.12f), new Color(0.42f, 0.36f, 0.20f),
        };
        var rng = new Random(11);
        for (int n = 0; n < 24; n++)
        {
            float cx = 60 + (float)rng.NextDouble() * (S - 120), cy = 60 + (float)rng.NextDouble() * (S - 120);
            float length = 38 + (float)rng.NextDouble() * 34, width = length * (0.32f + (float)rng.NextDouble() * 0.12f);
            float angle = (float)rng.NextDouble() * Mathf.Tau;
            var colour = palette[rng.Next(palette.Length)];
            float lobes = 2 + rng.Next(3);
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var across = new Vector2(-dir.Y, dir.X);
            int r = (int)length + 2;
            for (int py = (int)cy - r; py <= (int)cy + r; py++)
            for (int px = (int)cx - r; px <= (int)cx + r; px++)
            {
                var q = new Vector2(px + 0.5f - cx, py + 0.5f - cy);
                float u = q.Dot(dir) / length, v = q.Dot(across);
                if (u < -1 || u > 1)
                {
                    // The stalk.
                    if (u > 1 && u < 1.25f && MathF.Abs(v) < 1.2f)
                        Paint(buffer, S, px, py, colour * 0.6f, 1);
                    continue;
                }
                float edge = width * MathF.Pow(1 - u * u, 0.65f) * (1f + 0.16f * MathF.Sin((u + 1) * Mathf.Pi * lobes));
                float a = Math.Clamp(edge - MathF.Abs(v) + 0.5f, 0, 1);
                if (a <= 0)
                    continue;
                float rim = Math.Clamp(MathF.Abs(v) / Math.Max(edge, 0.01f), 0, 1);
                float shade = 1.05f - 0.3f * rim * rim - (MathF.Abs(v) < 0.9f ? 0.22f : 0f);
                Paint(buffer, S, px, py, colour * shade, a);
            }
        }
        return ImageTexture.CreateFromImage(CoverageMips(ToImage(buffer, S, S), 0.5f));
    }

    private static void Paint(float[] buffer, int width, int x, int y, Color colour, float a)
    {
        if (x < 0 || y < 0 || x >= width || y >= buffer.Length / 4 / width)
            return;
        int i = (y * width + x) * 4;
        buffer[i] = Mathf.Lerp(buffer[i], colour.R, a);
        buffer[i + 1] = Mathf.Lerp(buffer[i + 1], colour.G, a);
        buffer[i + 2] = Mathf.Lerp(buffer[i + 2], colour.B, a);
        buffer[i + 3] = Math.Max(buffer[i + 3], a);
    }

    private static double Gauss(Random rng) => Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());

    private static Image ToImage(float[] buffer, int w, int h)
    {
        var bytes = new byte[w * h * 4];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)Math.Clamp(buffer[i] * 255f + 0.5f, 0, 255);
        return Image.CreateFromData(w, h, false, Image.Format.Rgba8, bytes);
    }

    /// <summary>
    /// A mip chain whose alpha keeps the base level's alpha-tested coverage: plain averaging thins cut-out foliage to nothing with
    /// distance; each level's alpha is scaled until as much of it passes <paramref name="cutoff"/> as of the base.
    /// </summary>
    private static Image CoverageMips(Image source, float cutoff)
    {
        int w = source.GetWidth(), h = source.GetHeight();
        float target = Coverage(source.GetData(), cutoff, 1f);
        var data = new List<byte>(source.GetData());
        int lw = w, lh = h;
        while (lw > 1 || lh > 1)
        {
            lw = Math.Max(1, lw / 2);
            lh = Math.Max(1, lh / 2);
            var level = (Image)source.Duplicate();
            level.Resize(lw, lh, Image.Interpolation.Lanczos);
            var bytes = level.GetData();
            float lo = 0.5f, hi = 4f;
            for (int i = 0; i < 16; i++)
            {
                float mid = (lo + hi) / 2;
                if (Coverage(bytes, cutoff, mid) < target)
                    lo = mid;
                else
                    hi = mid;
            }
            float scale = (lo + hi) / 2;
            for (int i = 3; i < bytes.Length; i += 4)
                bytes[i] = (byte)Math.Clamp(bytes[i] * scale, 0, 255);
            data.AddRange(bytes);
        }
        return Image.CreateFromData(w, h, true, Image.Format.Rgba8, data.ToArray());
    }

    private static float Coverage(byte[] rgba, float cutoff, float scale)
    {
        int n = 0, pass = 0;
        for (int i = 3; i < rgba.Length; i += 4, n++)
        {
            if (rgba[i] / 255f * scale >= cutoff)
                pass++;
        }
        return n == 0 ? 0 : (float)pass / n;
    }
}
