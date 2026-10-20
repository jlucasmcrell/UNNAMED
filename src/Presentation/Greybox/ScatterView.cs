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
    private sealed record Kind(ScatterKind Rules, Mesh[] Meshes, bool Levels = false, Mesh[][]? Pool = null);

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
    private readonly Dictionary<string, Texture2D> _modelAlbedos = new(StringComparer.Ordinal);

    /// <summary>Instances placed by kind, and how long placing them took, for the reports.</summary>
    public IReadOnlyDictionary<string, int> Counts => _counts;

    /// <summary>Leave the card grass out entirely (the owner's ruling: it is thrown away).</summary>
    public bool CardsOff { get; private set; }

    /// <summary>Draw nothing but the grass blades - no stones, heather, nettles or litter in the way.</summary>
    public bool BladesOnly { get; private set; }

    public double BuildMs { get; private set; }

    private readonly SortedDictionary<string, int> _counts = new(StringComparer.Ordinal);

    /// <summary>The ground, the rules and the layout: the distance fields and the kinds' meshes are made once here; the placing waits for a world.</summary>
    public void Bind(ArtLibrary art, GroundField ground, ScatterRules rules, RegionLayout layout, IEnumerable<string> buildingPrefixes,
        bool cardsOff = false, bool bladesOnly = false)
    {
        _modelAlbedos.Clear();
        CardsOff = cardsOff;
        BladesOnly = bladesOnly;
        _ground = ground;
        _rules = rules;
        if (rules.Otherworld is { } strange)
            Otherworld.Set(strange);
        BuildFields(layout, buildingPrefixes);
        foreach (var kind in rules.Kinds)
        {
            // The card grass is GONE by owner ruling: "Whatever grass is in the game right now - throw it away and
            // never use it again." ScatterView.GrassClump is three quads with the plant's silhouette painted into a
            // gradient drawn in C#, and no amount of lit, shaded or textured quad is a plant. The kind is refused
            // here rather than left in the rules file looking like it works - the same reason the dead
            // tall_tussocks kind was removed rather than left in place.
            // --grass=cards puts it back for one comparison run only; it is not a supported configuration.
            if (kind.ModelId is null && kind.Name == "moor_grass" && CardsOff)
                continue;
            if (BladesOnly && kind.Name != "moor_grass")
                continue;
            // A kind drawn only under some presentation options (Phase B: the procedural leaf cards give way to the leaf-litter models);
            // "a|b" names several values.
            if (kind.OnlyWhen is { } when && when.Any(w => !w.Value.Split('|').Contains(VisualOptions.All.GetValueOrDefault(w.Key))))
                continue;
            if (kind.ModelId is { } model)
            {
                // Prepared plant models (Phase B, B0.5) draw only when the run asks for them (--visual plants=models).
                if (VisualOptions.Plants != "models")
                    continue;
                _wind ??= WindField.Ensure(this);
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
            // A POOL OF FORMS IS TESTED AFTER THE SINGLE MESH, AND THIS ORDER IS THE BUG - KEPT AS IT WAS, ON
            // PURPOSE, BECAUSE THE FIX WAS MEASURED AND REVERTED. `ModelId` is non-null for any kind whose `mesh`
            // starts with "model:" (ScatterRules.cs:43), and a pooled kind sets BOTH keys, so this order makes the
            // pool branch unreachable and draws the single `mesh` at every instance. Moving the pool branch ABOVE
            // was tried on 2026-09-29 and reverted the same day: it makes the variety live (the coverage report goes
            // from "veg_gen_grass_a (4 levels)" to "3 forms of 3 asked for") and hue IQR rises 6.9 -> 12.0 deg,
            // reaching the reference floor - but per-frame primitives rise 79.0 M -> 173.8 M (+120%) and draw calls
            // +18%, against a budget that required Cairnwold to spend LESS, and it loses the Vulkan device
            // (VkResult error -4). The measurement and the census that explains it are in
            // docs/phase_b/REGION_VEGETATION_ROUND.md section 3.
            if (kind.Pool is { Count: > 0 } pool2 && Pool(art, kind, pool2))
                continue;
            var meshes = Meshes(art, kind);
            if (meshes.Length == 0)
            {
                GD.PushWarning($"UNNAMED scatter: {kind.Name} names a mesh the scatter does not make ({kind.Mesh}); left out");
                continue;
            }
            _kinds.Add(new Kind(kind, meshes));
        }
        _residencySpawn = new Vector2(layout.Spawn.XMm / 1000f, layout.Spawn.ZMm / 1000f);
        foreach (string problem in rules.Problems)
            GD.PushWarning($"UNNAMED scatter: {problem}; left out");
    }

    /// <summary>
    /// A POOL OF FORMS for one kind: load every form's LOD chain and register the kind once, so the placement roll
    /// picks one form per instance. Returns true when the kind is consumed (either registered, or deliberately left
    /// out because this run does not draw prepared plants).
    /// </summary>
    private bool Pool(ArtLibrary art, ScatterKind kind, IReadOnlyList<string> pool)
    {
        // One clump stamped a quarter of a million times is a plantation, however good the clump is - the owner's
        // note was "if it could be slightly randomized a bit like grass is in real life". The build is unchanged:
        // what differs is which of several prepared forms draws at each instance, picked by the placement roll and
        // carried in the MultiMesh's per-instance custom data.
        if (VisualOptions.Plants != "models")
            return true;
        _wind ??= WindField.Ensure(this);
        var loaded = new List<Mesh[]>();
        foreach (string formId in pool)
        {
            var row = ModelMeshes(art, kind, formId);
            if (row.Length == 0)
            {
                art.Coverage.Fallback("scatter", kind.Name, art.Why(formId) ?? "the model would not load", formId);
                continue;
            }
            loaded.Add(row);
        }
        if (loaded.Count == 0)
            return true;
        art.Coverage.Resolved("scatter", kind.Name, $"{loaded.Count} forms of {pool.Count} asked for");
        _kinds.Add(new Kind(kind, loaded[0], true, loaded.ToArray()));
        return true;
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
        if (_residentMode)
        {
            BuildResidents(worldSeed);
            BuildMs = Math.Round((Time.GetTicksUsec() - started) / 1000.0, 1);
            GD.Print($"UNNAMED scatter residency: {_residents.Count} chunks, {_counts.Values.Sum()} resident placements in {BuildMs} ms; original density/ranges retained");
            return;
        }
        var region = _ground.Region;
        foreach (var kind in _kinds)
        {
            float chunk = kind.Rules.ChunkM ?? _rules.ChunkM;
            int total = 0;
            int cz = 0;
            for (float z0 = region.Position.Y; z0 < region.End.Y; z0 += chunk, cz++)
            {
                int cx = 0;
                for (float x0 = region.Position.X; x0 < region.End.X; x0 += chunk, cx++)
                {
                    total += BuildChunk(worldSeed, kind, cx, cz, x0, z0);
                }
            }
            _counts[kind.Rules.Name] = total;
        }
        BuildMs = Math.Round((Time.GetTicksUsec() - started) / 1000.0, 1);
        GD.Print($"UNNAMED scatter: {string.Join(", ", _counts.Select(c => $"{c.Value} {c.Key}"))} placed for world seed {worldSeed:x16} in {BuildMs} ms");
        if (System.Environment.GetEnvironmentVariable("UNNAMED_SCATTER_CENSUS") is { Length: > 0 })
            Census(total: 0);
    }


    // One seeded chunk, shared by the whole-region baseline and the optional camera-local experiment.
    private int BuildChunk(ulong worldSeed, Kind kind, int cx, int cz, float x0, float z0)
    {
        var region = _ground.Region;
        float chunk = kind.Rules.ChunkM ?? _rules.ChunkM;
        float step = 1f / MathF.Sqrt(kind.Rules.MaxPerM2);
        int total = 0;
        var rng = new Random(ChunkSeed(worldSeed, kind.Rules.Name, cx, cz));
        var per = kind.Meshes.Select(_ => new List<(Transform3D T, Color C, int Form)>()).ToArray();
        for (float z = z0; z < z0 + chunk; z += step)
        for (float x = x0; x < x0 + chunk; x += step)
        {
            float px = x + (float)rng.NextDouble() * step, pz = z + (float)rng.NextDouble() * step;
            double roll = rng.NextDouble();
            if (px >= region.End.X || pz >= region.End.Y || px >= x0 + chunk || pz >= z0 + chunk)
                continue;
            var w = _ground.CellWeights(px, pz);
            // The tier thins the plant models (B1); the procedural kinds keep Phase A's density.
            float share = kind.Levels ? VisualOptions.PlantDensity : 1f;
            if (roll >= share * Density(kind.Rules, px, pz, w) / kind.Rules.MaxPerM2)
                continue;
            var (basis, sink, tint) = Place(kind.Rules, rng, w);
            // The place's strangeness rides in the tint's alpha (the foliage shader's INSTANCE_CUSTOM.a), worked out once a plant here
            // rather than in every vertex of it. Model kinds only: the procedural meshes' instance colour alpha is their coverage.
            if (kind.Levels)
                tint.A = _rules.Otherworld?.At(px, pz) ?? 0f;
            int m = kind.Levels ? 0 : rng.Next(kind.Meshes.Length);
            // The form: a pooled kind picks one of its prepared models per instance, so a field of a
            // quarter of a million clumps is a mix of forms rather than one clump stamped repeatedly.
            var put = new Transform3D(basis, new Vector3(px, _ground.Height(px, pz) - sink, pz));
            if (kind.Pool is { Length: > 0 } forms)
                per[m].Add((put, tint, rng.Next(forms.Length)));
            else
                per[m].Add((put, tint, 0));
        }
        if (kind.Levels)
        {
            if (kind.Pool is { Length: > 0 } forms)
            {
                // One level band per form, each drawn over the same distance rule.
                for (int f = 0; f < forms.Length; f++)
                {
                    var mine = per[0].Where(x => x.Form == f).ToList();
                    if (mine.Count > 0)
                        AddLevels(kind, mine, cx, cz, forms[f]);
                }
            }
            else if (per[0].Count > 0)
            {
                AddLevels(kind, per[0], cx, cz);
            }
            return per[0].Count;
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
                GIMode = GeometryInstance3D.GIModeEnum.Disabled, VisibilityRangeEnd = kind.Rules.FadeM,
                // No fade mode and no end margin: a stipple on an alpha-scissor material toggles the
                // instance on and off rather than fading it, which is the blink the owner reported.
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled,
            });
            total += per[m].Count;
        }
        return total;
    }

    /// <summary>
    /// WHAT ACTUALLY DRAWS, node by node - the instrument the scatter never had. Placements are cheap to count and
    /// were counted all along; the cost is in the MultiMeshInstance3D nodes the placements become, and nothing ever
    /// printed those. Each node is one MultiMesh for one (chunk, kind, form, LOD band); its instance count is EVERY
    /// placement in that chunk for that form, in every band, culled at draw time by its visibility range. So the
    /// submission is `bands x placements` while the draw is about `placements`, and the gap between those two is
    /// where a geometry regression hides.
    ///
    /// Printed only when `UNNAMED_SCATTER_CENSUS` is set. Reads the scene it is standing in; writes nothing.
    /// </summary>
    public void Census(int total = 0)
    {
        var rows = new Dictionary<string, (int Nodes, long Slots, long Bands, long Tris, int MinBand, int MaxBand)>(StringComparer.Ordinal);
        long allSlots = 0;
        int allNodes = 0;
        foreach (var child in GetChildren())
        {
            if (child is not MultiMeshInstance3D node || node.Multimesh is not { } mm || mm.Mesh is not { } mesh)
                continue;
            string kind = node.Name.ToString().Split('_')[0];
            // Names are "{kind}_{cx}_{cz}_lod{n}"; recover the kind even when its own name contains underscores.
            foreach (string known in _kinds.Select(k => k.Rules.Name))
                if (node.Name.ToString().StartsWith(known + "_", StringComparison.Ordinal))
                {
                    kind = known;
                    break;
                }
            long tris = mesh.GetFaces().Length / 3;
            long slots = (long)mm.InstanceCount * tris;
            allSlots += slots;
            allNodes++;
            var cur = rows.TryGetValue(kind, out var r) ? r : (0, 0L, 0L, 0L, int.MaxValue, 0);
            int lo = (int)node.VisibilityRangeBegin, hi = (int)node.VisibilityRangeEnd;
            rows[kind] = (cur.Item1 + 1, cur.Item2 + slots, cur.Item3 + 1, tris,
                          Math.Min(cur.Item5, lo), Math.Max(cur.Item6, hi));
        }
        GD.Print($"UNNAMED scatter census: {allNodes} MultiMeshInstance3D nodes, {allSlots:N0} instance-x-triangle slots (the submission if every band drew)");
        foreach (var (kind, r) in rows.OrderByDescending(r => r.Value.Slots))
            GD.Print($"UNNAMED scatter census:   {kind,-14} nodes {r.Nodes,6}  bands {r.MinBand}-{r.MaxBand} m  mesh tris {r.Tris,6}  slots {r.Slots,14:N0}");
    }

    /// <summary>The player controller, kept for future probes. The hand-written ProveGrass/ProbeNow
    /// draw paths that used it are DELETED: they duplicated placement, never rendered, and cost a day.
    /// Grass goes through the ordinary scatter path like every other kind.</summary>
    public Player.PlayerController? Controller { get; set; }

    /// <summary>
    /// A chunk of a prepared model: the same placements in one MultiMesh a level, each level drawn over its band of distance (the rules'
    /// <c>lods_m</c>, the last to the kind's fade), the tint in the instances' custom data (the plant's vertex colours carry its wind).
    /// <paramref name="levelsOwner"/> names which prepared form to draw - the pool's pick for these instances.
    /// </summary>
    private void AddLevels(Kind kind, List<(Transform3D T, Color C, int Form)> placed, int cx, int cz, Mesh[]? levelsOwner = null)
    {
        var from = kind.Rules.LodsM ?? new[] { 0f, 8f, 20f, 40f };
        var chain = levelsOwner ?? kind.Meshes;
        int levels = Math.Min(chain.Length, from.Count);
        bool fade = OS.GetEnvironment("UNNAMED_LOD_STIPPLE") == "1";
        var bands = new List<MultiMeshInstance3D>(levels);
        for (int level = 0; level < levels; level++)
        {
            float begin = from[level], end = level + 1 < levels ? from[level + 1] : Reach(kind.Rules);
            if (end <= begin)
                continue;
            var multi = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = chain[level], InstanceCount = placed.Count,
            };
            for (int i = 0; i < placed.Count; i++)
            {
                multi.SetInstanceTransform(i, placed[i].Item1);
                multi.SetInstanceCustomData(i, placed[i].Item2);
            }
            var band = new MultiMeshInstance3D
            {
                Name = $"{kind.Rules.Name}_{cx}_{cz}_lod{level}", Multimesh = multi,
                // THE BAND. Without these two the band is computed above and then thrown away, and EVERY level is
                // drawn at EVERY distance - four copies of the same field stacked on each other. That is not a
                // subtle waste: it is the whole frame. Measured on the pinned world, a full --perf capture, average
                // fps by segment - grass at 16/m2 with all four levels drawn everywhere: 8.5 / 14.7 / 12.9 / 18.2;
                // with the grass layer removed altogether: 42.8 / 57.3 / 62.5 / 79.9. Setting the band is what makes
                // lods_m mean anything, and it is why changing fade_m from 34 to 12 moved nothing (17.6 vs 18.2):
                // a number nothing reads cannot have an effect.
                // Metres, not squared: the card path below sets VisibilityRangeEnd = FadeM the same way.
                VisibilityRangeBegin = begin, VisibilityRangeEnd = !fade && level + 1 < levels ? MathF.BitDecrement(end) : end,
                VisibilityRangeBeginMargin = fade ? begin * .1f : 0,
                VisibilityRangeEndMargin = fade ? end * .1f : 0,
                // Far foliage cards must not cast slabs, so a casting kind casts from its two near levels only - unless the
                // rules say `far_shadows` (a rock, a sapling stand: solid and still), whose shadow must not switch off at
                // the third level's edge while the thing itself stays drawn (the ash stand's shadow popping onto a wall).
                CastShadow = kind.Rules.Shadows && (level < 2 || kind.Rules.FarShadows) && !chain[level].HasMeta("otherreach_impostor")
                    ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                // Ground cover keeps its existing per-plant thinning by default.
                // The explicit fade comparison also exercises native chunk cross-fading.
                VisibilityRangeFadeMode = fade ? GeometryInstance3D.VisibilityRangeFadeModeEnum.Self
                    : GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled,
            };
            AddChild(band);
            bands.Add(band);
        }
        // ONE BOX FOR EVERY BAND. Godot measures a node's visibility range to the centre of that node's own bounds, and
        // the levels' meshes differ in size (the impostor is narrower than the clump), so the bands did not meet: at a
        // band's edge a whole chunk drew in two levels, or in none for one to three frames - grass and its shadow
        // blinking out while walking. Measured on 2026-10-03: 15 gaps and 8 doubled frames on one walk, none with this.
        if (bands.Count > 0)
        {
            var box = bands.Skip(1).Aggregate(bands[0].Multimesh.GetAabb(), (b, n) => b.Merge(n.Multimesh.GetAabb()));
            foreach (var band in bands)
                band.CustomAabb = box;
        }
    }

    /// <summary>Where a prepared kind's last level ends: its <c>fade_m</c> times the tier's reach (<see cref="RenderTiers.GroundReach"/>).</summary>
    private static float Reach(ScatterKind rules) => rules.FadeM * RenderTiers.GroundReach;

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
        if (kind.NearWaterAdd > 0 && kind.NearWaterToM > kind.NearWaterFromM)
            d += kind.NearWaterAdd * (1f - GroundField.SmoothStep(kind.NearWaterFromM, kind.NearWaterToM, WaterDistance(x, z)));
        float path = _ground.PathAt(x, z);
        if (kind.PathShoulders > 0)
            d += kind.PathShoulders * GroundField.SmoothStep(0.05f, 0.3f, path) * (1f - GroundField.SmoothStep(0.4f, 0.7f, path));
        if (kind.WhereStrange is { } strange)
            d *= _rules.Otherworld is { } field ? GroundField.SmoothStep(strange.From, strange.To, field.At(x, z)) : 0;
        d *= Clear(x, z, kind.ClearMargin, kind.OffPath, path);
        if (!kind.OffPath)
            d *= 1f - GroundField.SmoothStep(0.45f, 0.7f, path);
        return d;
    }

    /// <summary>Metres to the Charwood's drainage line (Terrain3DView.CharwoodStream).</summary>
    private static float WaterDistance(float x, float z)
    {
        var line = Terrain3DView.CharwoodStream;
        var p = new Vector2(x, z);
        float best = float.MaxValue;
        for (int i = 0; i + 1 < line.Length; i++)
        {
            var a = line[i];
            var ab = line[i + 1] - a;
            float t = Math.Clamp((p - a).Dot(ab) / MathF.Max(ab.LengthSquared(), 1e-4f), 0, 1);
            best = MathF.Min(best, (a + ab * t - p).Length());
        }
        return best;
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
            // As with the grass clumps: a per-plant lean along a warm/cool axis, not one brightness factor,
            // so a stand of plants is not one hue at many brightnesses. `tint_lean` in the kind's rules sets
            // how far; 0 (the default, and every kind that does not name it) keeps the historical own*own*0.45
            // EXACTLY, so a region that does not opt in renders as it always did. See ScatterKind.TintLean for
            // why the linear shape is the fix and why the default is not simply made linear.
            float own = (float)rng.NextDouble();
            tint = kind.TintLean > 0
                ? tint.Lerp(new Color(0.90f, 0.80f, 0.56f), own * kind.TintLean)
                : tint.Lerp(new Color(0.90f, 0.80f, 0.56f), own * own * 0.45f);
            float drift = 0.88f + (float)rng.NextDouble() * 0.24f;
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
                // Greener in the wood and the wet, straw-coloured on the worked dirt and the quarry.
                var tint = new Color(0.92f, 0.86f, 0.62f).Lerp(new Color(0.74f, 0.86f, 0.64f), Math.Clamp(Lush(), 0, 1));
                // A clump's OWN dryness on top of the ground's. The old drift multiplied R, G and B by one
                // factor, so it varied brightness only and a whole meadow came out one hue - the defect the
                // owner's own note named ("grass is all the exact same hue"). This leans each clump along a
                // warm/cool axis instead, so a field holds greener and drier clumps side by side.
                //
                // own*own was tried first and was too timid: its mean lean is only about 0.2, and the field
                // still read as one green. Linear own against a browner target gives a mean lean near 0.38
                // and a visible mix, which is what the references show.
                float own = (float)rng.NextDouble();
                tint = tint.Lerp(new Color(0.82f, 0.68f, 0.38f), own * 0.75f);
                float drift = 0.90f + (float)rng.NextDouble() * 0.20f;
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
    private Mesh[] ModelMeshes(ArtLibrary art, ScatterKind kind, string id)
    {
        var levels = art.ModelLevels(id);
        var meshes = new List<Mesh>();
        float height = 0;
        foreach (var level in levels)
        {
            bool impostor = level.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
                .Any(m => m.Mesh is not null && Enumerable.Range(0, m.Mesh.GetSurfaceCount()).Any(s =>
                    m.GetActiveMaterial(s)?.ResourceName.Contains("impostor", StringComparison.OrdinalIgnoreCase) == true));
            // The outer part of the reach thins plant by plant (FoliageShader's thin_from/thin_to) on the levels that cast no
            // shadow: the shadow pass sees the light's camera, so a thinned caster would lose its shadow at the wrong distance.
            bool thins = !kind.Shadows || meshes.Count >= 2 && !kind.FarShadows;
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
                    merged.SurfaceSetMaterial(merged.GetSurfaceCount() - 1, Foliage(instance.GetActiveMaterial(s) as BaseMaterial3D, kind, height, thins, _modelAlbedos));
                }
            }
            level.Free();
            if (merged.GetSurfaceCount() == 0)
                break;
            // Surface merging above copies only the close-up arrays, losing the importer's LODs.
            // Regenerate after transforming into model space so each instanced plant keeps its
            // original near mesh, material and placement, with projected-error indices further out.
            if (System.Environment.GetEnvironmentVariable("UNNAMED_RUNTIME_MESH_LODS") != "0")
            {
                using var importer = new ImporterMesh();
                for (int s = 0; s < merged.GetSurfaceCount(); s++)
                {
                    using var arrays = merged.SurfaceGetArrays(s);
                    importer.AddSurface(Mesh.PrimitiveType.Triangles, arrays, material: merged.SurfaceGetMaterial(s));
                }
                using var boneTransforms = new Godot.Collections.Array();
                importer.GenerateLods(60, 0, boneTransforms);
                if (OS.GetEnvironment("UNNAMED_LOAD_REPORT") == "1")
                    GD.Print($"UNNAMED scatter mesh LODs: {id} level {meshes.Count}, {Enumerable.Range(0, importer.GetSurfaceCount()).Sum(importer.GetSurfaceLodCount)} index levels");
                merged = importer.GetMesh();
            }
            // Shader displacement is invisible to CPU culling. Grow only wind-deformed model bounds, in model
            // space so each MultiMesh instance's existing rotation/scale also transforms this finite padding.
            if (kind.Wind)
                merged.CustomAabb = merged.GetAabb().Grow(WindCullMargin(height, kind.Stiffness));
            if (impostor) merged.SetMeta("otherreach_impostor", true);
            meshes.Add(merged);
        }
        return meshes.ToArray();
    }

    private static float WindCullMargin(float height, float stiffness)
    {
        // FoliageShader: strength/gust/height fraction <= 1, gust multiplier <= 1.65, sway factor <= 1.
        // Sum the maximum bend, two-axis flutter (default 0.35), and vertical shortening lengths.
        float amount = 1.65f / Math.Max(stiffness, 0.05f);
        return height * (0.5f * amount + MathF.Sqrt(2f) * 0.35f * 0.05f + 0.08f * amount * amount);
    }

    internal Material Foliage(BaseMaterial3D? source, ScatterKind kind, float height, bool thins, Dictionary<string, Texture2D> albedos)
    {
        bool mineral = kind.Otherflora is { Vein: not null } or { Lichen: not null };
        Texture2D? coverage = null;
        bool opaque = false;
        if (source?.AlbedoTexture is { } albedo && albedo.GetImage() is { } image)
        {
            if (image.IsCompressed())
                image.Decompress();
            image.ClearMipmaps();
            image.Convert(Image.Format.Rgba8);
            opaque = image.DetectAlpha() == Image.AlphaMode.None
                && source.GetMeta("otherreach_source_transparency", (int)source.Transparency).AsInt32() == (int)BaseMaterial3D.TransparencyEnum.Disabled
                && System.Environment.GetEnvironmentVariable("UNNAMED_OPAQUE_FOLIAGE") != "0";
            string key = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(image.GetData()));
            if (!albedos.TryGetValue(key, out coverage))
                albedos[key] = coverage = ImageTexture.CreateFromImage(ImageMips.Coverage(image, 0.4f));
        }
        // Fully opaque source maps pass every existing alpha test (including the coverage mips).
        // Keep RGB, normals, wind and thinning; let depth/shadow passes omit the needless fragment alpha fetch.
        bool otherworld = kind.Otherflora is not null && Otherworld.Strength > 0;
        // Keep effect-bearing materials on the original arithmetic path. In particular,
        // removing zero wind operations changes the active mineral pattern on this backend.
        bool compileWind = kind.Wind || otherworld, compileThinning = thins || otherworld;
        bool compileNormal = otherworld || source is { NormalEnabled: true, NormalTexture: not null };
        bool compileArm = otherworld || source?.RoughnessTexture is not null;
        var shaderKey = (mineral, opaque, otherworld, compileWind, compileThinning, compileNormal, compileArm);
        if (!_plantShaders.TryGetValue(shaderKey, out var shader))
        {
            string code = opaque ? "#define OPAQUE_FOLIAGE\n" + WindField.FoliageShader : WindField.FoliageShader;
            if (!compileWind) code = "#define STATIC_FOLIAGE\n" + code;
            if (!compileThinning) code = "#define NO_FOLIAGE_THINNING\n" + code;
            if (!compileNormal) code = "#define NO_FOLIAGE_NORMAL_MAP\n" + code;
            if (!compileArm) code = "#define NO_FOLIAGE_ARM_MAP\n" + code;
            _plantShaders[shaderKey] = shader = new Shader { Code = otherworld ? Otherworld.Variant(code, mineral: mineral) : code };
        }
        var material = new ShaderMaterial { Shader = shader };
        if (coverage is not null) material.SetShaderParameter("albedo_tex", coverage);
        if (opaque && OS.GetEnvironment("UNNAMED_LOAD_REPORT") == "1")
            GD.Print($"UNNAMED scatter opaque pass: {kind.Name}");
        material.SetShaderParameter("plant_height", height);
        material.SetShaderParameter("stiffness", kind.Stiffness);
        // THE GROUND COVER'S EDGE IS A DENSITY, NOT A RING. A chunk's band ends all at once, so a kind used to stop in a hard
        // circle at its fade_m. Over the outer 40 % of the reach each plant now drops out at its own distance (a hash of where
        // it stands), so the cover thins to nothing at the band's end and the far band costs a fraction of its full density.
        if (thins)
        {
            material.SetShaderParameter("thin_from", 0.6f * Reach(kind));
            material.SetShaderParameter("thin_to", Reach(kind));
        }
        // STONES DO NOT SWAY. Every model kind goes through this foliage shader, which bends its vertices by wind -
        // correct for grass, ferns and flowers, and absurd for a boulder, which is what "even the rocks" was. A
        // kind says `wind: false` in the rules and the bend and the flutter both go to zero.
        if (!kind.Wind)
        {
            // `bend` scales with `tall` = plant_height * scale, and the flutter term with wind.w, so zeroing
            // plant_height and flutter freezes the mesh. There is no wind_scale uniform; these two are the levers.
            material.SetShaderParameter("plant_height", 0f);
            material.SetShaderParameter("flutter", 0f);
            // Preserve the imported solid-surface intent even if the loader later marks plant materials as cutouts.
            if (source is not null && source.GetMeta("otherreach_source_transparency", (int)source.Transparency).AsInt32()
                == (int)BaseMaterial3D.TransparencyEnum.Disabled)
            {
                material.SetShaderParameter("backlight_colour", Vector3.Zero);
                material.SetShaderParameter("roughness_value", source.Roughness);
            }
        }
        // Grass blades carry normals bent to the sky and are seen from both sides, so the shader must not flip the
        // normal on the back face - a flipped sky-normal points at the ground and the blade shades black. This
        // matched only "veg_gen_grass" and the region's grass is "veg_gen_tussock_*", so half of every clump was
        // drawn with dark undersides. Any generated blade model needs this.
        material.SetShaderParameter("upright_normals",
            kind.ModelId?.StartsWith("veg_gen_grass", StringComparison.Ordinal) == true
            || kind.ModelId?.StartsWith("veg_gen_tussock", StringComparison.Ordinal) == true
            || kind.ModelId?.StartsWith("veg_gen_blade", StringComparison.Ordinal) == true);
        if (kind.Otherflora is { } wrong)
        {
            static Vector3 Rgb(float[] c) => new(c[0], c[1], c[2]);
            material.SetShaderParameter("ow_hue", wrong.Hue);
            material.SetShaderParameter("ow_pale", wrong.Pale);
            material.SetShaderParameter("ow_sheen", wrong.Sheen);
            material.SetShaderParameter("ow_late", kind.Wind ? wrong.Late : 0f);
            material.SetShaderParameter("ow_glow_green", wrong.GlowGreen);
            material.SetShaderParameter("ow_iridescence", wrong.Iridescence);
            if (wrong.Backlight is { } backlight)
            {
                material.SetShaderParameter("ow_backlight", Rgb(backlight));
                material.SetShaderParameter("ow_backlight_on", 1f);
            }
            if (wrong.Glow is { } glow)
                material.SetShaderParameter("ow_glow", Rgb(glow));
            if (wrong.Vein is { } vein)
            {
                material.SetShaderParameter("ow_vein", Rgb(vein));
                material.SetShaderParameter("ow_vein_on", 1f);
            }
            if (wrong.Lichen is { } lichen)
            {
                material.SetShaderParameter("ow_lichen", Rgb(lichen));
                material.SetShaderParameter("ow_lichen_on", 1f);
            }
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

    private readonly Dictionary<(bool Mineral, bool Opaque, bool Otherworld, bool Wind, bool Thins, bool Normal, bool Arm), Shader> _plantShaders = new();

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
        return ImageTexture.CreateFromImage(ImageMips.Coverage(ToImage(buffer, W, H), 0.4f));
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
        return ImageTexture.CreateFromImage(ImageMips.Coverage(ToImage(buffer, S, S), 0.5f));
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
}
