// UNNAMED Presentation - the asset pipeline's models, animation clips and world materials, loaded at run time by semantic ID
// Godot presentation only: no gameplay state lives here (D-11)

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>What an animation clip's record says about it: whether it loops, how long it runs, and its timed events.</summary>
/// <param name="Bones">Each bone's rest transform relative to its parent, by name.</param>
/// <param name="Height">How high the hips (or pelvis) stand at rest: the measure a clip's root and hip motion scales by.</param>
public sealed record SkeletonRest(IReadOnlyDictionary<string, Transform3D> Bones, float Height)
{
    public static SkeletonRest Of(Skeleton3D skeleton)
    {
        var bones = new Dictionary<string, Transform3D>(StringComparer.Ordinal);
        for (int i = 0; i < skeleton.GetBoneCount(); i++)
            bones[skeleton.GetBoneName(i)] = skeleton.GetBoneRest(i);
        int hips = new[] { "hips", "pelvis" }.Select(skeleton.FindBone).FirstOrDefault(b => b >= 0, -1);
        float height = hips >= 0 ? skeleton.GetBoneGlobalRest(hips).Origin.Y : 0;
        return new SkeletonRest(bones, height);
    }
}


public sealed record ClipInfo(string Id, bool Loop, double Seconds, IReadOnlyList<(string Id, double Time)> Events)
{
    /// <summary>The ground speed a gait clip is drawn for, or null when its record does not say.</summary>
    public double? AuthoredSpeed { get; init; }
}

/// <summary>A world material's maps as loaded (mipmapped), its tile size in metres and its normal strength, for a surface that lays them itself.</summary>
public sealed record WorldMaps(string Id, Texture2D Albedo, Texture2D? Normal, Texture2D? Orm, float TileSizeM, float NormalStrength);

/// <summary>
/// What loading the library has cost this run: the model files (their time includes the mipmap pass), the model textures given mipmaps and
/// that pass's own time (Phase A), the world materials' maps read, and the textures' memory as loaded and as drawn (uncompressed, mipmapped).
/// </summary>
public sealed record LoadCost(int Scenes, double SceneMs, int ModelTextures, double MipmapMs, int MaterialMaps, double MaterialMapMs,
    double VramMbAsLoaded, double VramMbWithMipmaps);

/// <summary>
/// The asset pipeline's generated library, read where it lies (the asset workspace, never this repository) and by its own stable
/// IDs: a model is <c>ready/&lt;id&gt;/&lt;id&gt;.glb</c> (or its <c>_rigged</c> skin under <c>rigged/</c>), a clip is
/// <c>animation/ready/&lt;family&gt;/anim.&lt;id&gt;.glb</c> with its record in <c>animation/clips/</c>, and a world material is the maps
/// under <c>materials/&lt;id&gt;/</c>. Each file is loaded once and instanced after. Nothing here repairs an asset: one that is missing or
/// will not load is recorded against its ID (<see cref="Problems"/>) and its caller draws the greybox.
/// </summary>
public sealed class ArtLibrary
{
    /// <summary>Immutable native scenes/textures shared within this world's MetaHuman instances.</summary>
    public Godot.Collections.Dictionary MetaHumanResources { get; } = new();

    private readonly Dictionary<string, PackedScene?> _scenes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Animation?> _clips = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SkeletonRest?> _clipRests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClipInfo?> _clipInfo = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Material?> _materials = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorldMaps?> _maps = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, string> _problems = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _uses = new(StringComparer.Ordinal);
    /// <summary>A model's own vertices, read once and kept: <see cref="TurnedFootprint"/> asks for them per placement.</summary>
    private readonly Dictionary<string, Vector3[]> _vertices = new(StringComparer.Ordinal);
    private const string AoFlaggedPath = "res://Art/ao_flagged.json";
    private static HashSet<string>? _aoFlagged;
    private static bool _aoNothing;
    public static int OcclusionDrawn => _aoDrawn;
    public static int OcclusionSuppressed => _aoSuppressed;
    private static int _aoDrawn;
    private static int _aoSuppressed;
    private int _sceneCount, _textureCount, _mapCount;
    private double _sceneMs, _textureMs, _mapMs;
    private long _vramAsLoaded, _vramWithMips;

    private IReadOnlyDictionary<string, string> _withheld = new Dictionary<string, string>();

    /// <summary>A model file parsed ahead on a worker (<see cref="Prefetch"/>): the document and state, or why it would not parse.</summary>
    private sealed record Parsed(GltfDocument Document, GltfState State, Error Error);

    private readonly Dictionary<string, TaskCompletionSource<Parsed>> _ahead = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _claimed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _loadOrder = new();
    private SemaphoreSlim? _window;
    private readonly CancellationTokenSource _prefetchEnded = new();

    private readonly string? _sceneCachePrefix;
    private readonly bool _buildSceneCache;
    private readonly string? _nativeSceneRoot;
    private const string SceneCacheFormat = "v7"; // Original geometry casts shadows; invalidate custom position-only proxies in v6.
    private readonly TextureCache? _interiorBuildCache;

    public ArtLibrary(string? root, bool useTextureCache = false, string? sceneCachePrefix = null, bool buildSceneCache = false)
    {
        Root = root;
        _sceneCachePrefix = sceneCachePrefix;
        _buildSceneCache = buildSceneCache;
        _nativeSceneRoot = sceneCachePrefix is not null && FallbackRoot is not null
            ? Path.Combine(FallbackRoot, "barrowworks_native", Engine.GetVersionInfo()["string"].AsString(), SceneCacheFormat) : null;
        Cache = new TextureCache(useTextureCache || VisualOptions.All.GetValueOrDefault("textures") == "cache" || BuildTextureCache ? root : null, BuildTextureCache,
            _nativeSceneRoot is not null ? Path.Combine(_nativeSceneRoot, "textures") : null, buildSceneCache);
        PackedTerrainCache = useTextureCache && FallbackRoot is not null
            ? new TextureCache(Path.Combine(FallbackRoot, "barrowworks_terrain"), false) : null;
        // Build-only cache in the owned depot, never its primary texture-cache junction. Normal play loads the
        // shared BC7 resources referenced by the native scenes; no extra texture dictionary retains the interior.
        _interiorBuildCache = buildSceneCache && _nativeSceneRoot is not null && FallbackRoot is not null
            ? new TextureCache(Path.Combine(FallbackRoot, "barrowworks_materials"), true,
                Path.Combine(_nativeSceneRoot, "textures"), true) : null;
    }

    /// <summary>Set by <c>--texture-cache</c> (editor binary): every texture this run loads is compressed into the cache (Phase B, B1).</summary>
    public static bool BuildTextureCache { get; set; }

    /// <summary>
    /// Set by <c>--asset-fallback</c>: a second workspace searched for any file the primary does not hold. A region's own root carries the
    /// region's art and is small; the shared depot holds every generated body the pipeline has made. Without this a region can only draw the
    /// files it copied, so an ID the depot has but the region does not falls back to greybox for no reason but the root it was looked up in.
    /// </summary>
    public static string? FallbackRoot { get; set; }

    /// <summary>Every workspace a relative path is looked up under, the primary first.</summary>
    private IEnumerable<string> Workspaces
    {
        get
        {
            if (Root is not null)
                yield return Root;
            if (FallbackRoot is not null && !string.Equals(FallbackRoot, Root, StringComparison.OrdinalIgnoreCase))
                yield return FallbackRoot;
        }
    }

    /// <summary>Where <paramref name="relative"/> actually lies, or null when no workspace holds it.</summary>
    private string? At(string relative)
    {
        foreach (string workspace in Workspaces)
        {
            string path = Path.Combine(workspace, relative);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    /// <summary>The workspace <paramref name="path"/> lies under: what names it in a report or keys it in the texture cache.</summary>
    /// <remarks>
    /// The LONGEST matching workspace, not the first one that matches. The convention on disk nests the region inside the depot
    /// (<c>&lt;depot&gt;\&lt;region&gt;\assets</c> against <c>&lt;depot&gt;\assets</c> - see <c>Main.SiblingDepot</c>), so a depot path
    /// STARTS WITH the region's path and the first-match form answered "the region" for every depot file. It was latent until a report
    /// needed the answer: anything keyed on it - <see cref="TextureCache"/>'s keys for the compressed copies - had the two workspaces
    /// collapsed onto one name for every file the region happens not to hold. Cairnwold's own root is
    /// <c>G:\UNNAMED_DS_ASSETS\cairnwold\assets</c> and its depot is <c>G:\UNNAMED_DS_ASSETS\assets</c>.
    /// </remarks>
    private string WorkspaceOf(string path)
    {
        string best = Root ?? string.Empty;
        int longest = 0;
        foreach (string workspace in Workspaces)
        {
            if (workspace.Length > longest && path.StartsWith(workspace, StringComparison.OrdinalIgnoreCase))
            {
                best = workspace;
                longest = workspace.Length;
            }
        }
        return best;
    }

    /// <summary>The compressed, shared copies of the textures (Phase B, B1), used when the run asks for them (<c>--visual textures=cache</c>).</summary>
    public TextureCache Cache { get; }

    /// <summary>Offline-compressed packed terrain maps for the opt-in space budget; source dimensions stay unchanged.</summary>
    public TextureCache? PackedTerrainCache { get; }

    /// <summary>What the views drew from this library and what they drew in greybox, by semantic ID (Phase A's coverage report).</summary>
    public ArtCoverage Coverage { get; } = new();

    /// <summary>What loading the models has cost so far.</summary>
    public LoadCost Cost => new(_sceneCount, Math.Round(_sceneMs, 1), _textureCount, Math.Round(_textureMs, 1), _mapCount, Math.Round(_mapMs, 1),
        Math.Round(_vramAsLoaded / 1048576.0, 1), Math.Round(_vramWithMips / 1048576.0, 1));

    /// <summary>
    /// Why an asset was not drawn - where it was to stand (<paramref name="at"/>, a structure's ID) first, then the asset itself - or null
    /// when nothing was recorded against it.
    /// </summary>
    public string? Why(string asset, string? at = null) =>
        at is not null && _problems.TryGetValue($"{asset} at {at}", out string? there) ? there : _problems.GetValueOrDefault(asset);

    /// <summary>IDs that exist but are not fit to draw (the bindings' "withheld"): asked for, they are reported and the greybox stands in.</summary>
    public void Withhold(IReadOnlyDictionary<string, string> withheld) => _withheld = withheld;

    /// <summary>
    /// The models whose occlusion channel is near-black over whole islands, so Godot would draw black patches where
    /// Blender draws none. Read once from <c>res://Art/ao_flagged.json</c>, which
    /// <c>tools/asset_pipeline/cairnwold/ao_flag_report.py</c> writes from <c>tools/visual_review/ao_check.py</c>'s
    /// own per-model measurement - Cairnwold 0 of 70, the shared depot 479 of 572.
    /// </summary>
    private static bool FlaggedForOcclusion(string id)
    {
        // THE STRONGEST FORM OF THE FLAG, KEPT AS A ONE-FLAG EXPERIMENT (handoff section 9.0ac). Setting
        // UNNAMED_AO_ALL_OFF=1 suppresses baked occlusion on EVERY material, which is the most this axis can
        // ever do. Measured with it, Ashen Hollow's sixteen view frames against an identical-run control:
        // the largest whole-frame change is mean |dL| 1.31 over 3.57 % of pixels - so baked occlusion, on or
        // off for all 347 materials, is a marginal term here. The reason is already in the code beside it:
        // the renderer's own SSAO does the contact shading (`HollowView.BuildLighting`, radius 1.4 intensity
        // 2.2), the foliage shader forces `AO = 1.0` by design, and `Palette.Wear` folds the map in as
        // `AO = ao * orm.r`. Kept rather than deleted, on the same rule as the `skin` bisect arms: one build
        // photographs both ends, and no shipping run ever sets it. Never set it without a control run.
        if (System.Environment.GetEnvironmentVariable("UNNAMED_AO_ALL_OFF") == "1")
            return true;
        if (_aoFlagged is null)
        {
            _aoFlagged = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (Godot.FileAccess.FileExists(AoFlaggedPath))
                {
                    using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(AoFlaggedPath));
                    foreach (var group in document.RootElement.EnumerateObject())
                    {
                        if (group.Value.ValueKind != JsonValueKind.Array)
                            continue;
                        foreach (var entry in group.Value.EnumerateArray())
                        {
                            if (entry.GetString() is not { } name)
                                continue;
                            // THE LIST STORES PATHS; THE LOOKUP IS A BARE ASSET ID, SO IT NEVER MATCHED.
                            // `Filter` builds the id it tests with as
                            // `Path.GetFileName(Path.GetDirectoryName(path))` - "creature_frost_wolf" - while
                            // every one of the 479 entries is a full path ending in "ready\<id>\<id>.glb", and
                            // `HashSet.Contains` is an ordinal exact match, so the two could never meet.
                            // `OcclusionSuppressed` has therefore been 0 in every run while the mechanism read
                            // as present in the code, and the suppression this file was built to apply has
                            // never once fired. Measured 2026-09-29: 479 of 479 entries are paths; 37 of the
                            // 206 assets Ashen Hollow resolves are flagged by bare name; 0 matched exactly.
                            // Checked against the measurement that made the list rather than trusting it:
                            // `tools/visual_review/ao_check.py` still flags all six sampled entries
                            // (landmark_ashen_waystone 49.2 % of its sampled surface near-black, frost wolf
                            // 45.3 %, ash hound 41.7 %, barrel 34.4 %, husk 18.3 %, anvil stump 18.2 %) and 0
                            // of 3 Cairnwold controls, so the list is current and it is the lookup that was
                            // wrong. BOTH FORMS ARE ADDED, so the file may be written either way from now on.
                            _aoFlagged.Add(name);
                            if (name.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                            {
                                string? folder = Path.GetDirectoryName(name);
                                if (!string.IsNullOrEmpty(folder))
                                    _aoFlagged.Add(Path.GetFileName(folder));
                            }
                        }
                    }
                }
            }
            catch (JsonException e)
            {
                GD.PushWarning($"UNNAMED art: {AoFlaggedPath} could not be read ({e.Message}); occlusion stays off");
                _aoFlagged.Clear();
                _aoNothing = true;
            }
        }
        // A list that would not parse means "no occlusion anywhere", which is the behaviour this replaced - the safe
        // answer, and the one that cannot paint black patches on a model nobody has measured.
        return _aoNothing || _aoFlagged.Contains(id);
    }

    private bool IsWithheld(string id)    {
        if (!_withheld.TryGetValue(id, out string? why))
            return false;
        Problem(id, "withheld: " + why);
        return true;
    }

    public static ArtLibrary Empty { get; } = new(null);

    /// <summary>The asset workspace, or null when there is none (a fresh clone, CI): then every caller draws greybox.</summary>
    public string? Root { get; }

    /// <summary>Every semantic ID asked for that could not be used, and why - reported, never repaired here.</summary>
    public IReadOnlyDictionary<string, string> Problems => _problems;

    /// <summary>Every semantic ID that loaded and is in use.</summary>
    public IReadOnlyCollection<string> Used => _uses.Where(u => u.Value > 0).Select(u => u.Key).ToList();

    private void Use(string id) => _uses[id] = _uses.GetValueOrDefault(id) + 1;

    /// <summary>
    /// How wide and deep a model's own vertices reach when it is turned about the centre of its bounds - the footprint
    /// the MESH occupies at a yaw, which is what a turned structure's blocker has to match.
    /// </summary>
    /// <remarks>
    /// <see cref="ArtGallery.Bounds"/> cannot answer this. Merging mesh AABBs gives the model's box in ITS OWN frame,
    /// and turning a box gives the box of the turned box - the worst case of the corners. On these rocks that is not a
    /// small difference: cw_boulder_a measures 1.01 x 1.46 m and its swept-corner box is 1.78 m, for a 1.49 m rock. A
    /// blocker built from the swept box stops the player half a metre short of open air on 240 placements, which is
    /// the owner's "I walk through the boulder" bug with the sign changed. So the vertices themselves are turned.
    /// </remarks>
    public Vector2 TurnedFootprint(string modelId, Node3D model, float yawRadians)
    {
        if (!_vertices.TryGetValue(modelId, out var points))
        {
            var gathered = new List<Vector3>();
            foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
            {
                if (mesh.Mesh is not ArrayMesh array)
                    continue;
                var toRoot = Relative(model, mesh);
                for (int surface = 0; surface < array.GetSurfaceCount(); surface++)
                {
                    var arrays = array.SurfaceGetArrays(surface);
                    if (arrays.Count <= (int)Mesh.ArrayType.Vertex)
                        continue;
                    foreach (var v in arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                        gathered.Add(toRoot * v);
                }
            }
            _vertices[modelId] = points = gathered.ToArray();
        }
        var bounds = ArtGallery.Bounds(model);
        if (points.Length == 0)
            return new Vector2(Math.Max(0.01f, bounds.Size.X), Math.Max(0.01f, bounds.Size.Z));
        var centre = bounds.GetCenter();
        float cos = Mathf.Cos(yawRadians), sin = Mathf.Sin(yawRadians);
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var p in points)
        {
            float dx = p.X - centre.X, dz = p.Z - centre.Z;
            float x = dx * cos + dz * sin, z = -dx * sin + dz * cos;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
        }
        return new Vector2(maxX - minX, maxZ - minZ);
    }

    /// <summary>
    /// A static model at its authored size (<c>ready/&lt;id&gt;/&lt;id&gt;.glb</c>), or null. Always the full model: every <c>_lod</c> file in
    /// the library carries no material (reported to the asset pipeline), so a lighter variant would draw white.
    /// </summary>
    public Node3D? Model(string id) => Instance(id, Path.Combine("ready", id, id + ".glb"));

    /// <summary>
    /// A static model with its lighter levels (<c>&lt;id&gt;_lod1-3.glb</c>) swapped in by distance, fading across each change. A level is
    /// used only when every surface of it carries a material - an untextured level would draw white - and the chain stops at the first
    /// level that is missing or unfit (reported). The switch distances grow with the model's size, so a small prop keeps its full mesh
    /// only while it is near and a large one for longer. Without usable levels this is just the full model.
    /// </summary>
    public Node3D? ModelWithLods(string id)
    {
        if (IsTree(id))
            return Whole(Model(id));
        if (ModelLevels(id) is not { Count: > 0 } all)
            return null;
        if (all.Count == 1)
            return all[0];
        var full = all[0];
        var root = new Node3D { Name = id };
        var shared = ArtGallery.Bounds(full);
        var extent = shared.Size;
        float size = Math.Max(0.1f, Math.Max(extent.X, Math.Max(extent.Y, extent.Z)));
        // A tree's levels come sooner than its size alone would bring them (docs/phase_b/demo/trees/TREES.md): its needles and twigs thin
        // to sky on the lighter meshes past about 130 m, where the crossed-plane impostor holds a solid crown.
        bool tree = IsTreeFamily(id);
        float reach = Greybox.RenderTiers.LodReach;
        float[] from = tree
            ? new[] { 0f, 35f * reach, 80f * reach, 150f * reach }
            : new[] { 0, Math.Max(8f, 4f * size) * reach, Math.Max(20f, 10f * size) * reach, Math.Max(45f, 22f * size) * reach };
        // UNNAMED_LOD_REPORT=1 prints which ladder a model took and where its levels switch, so a claim about switch distances is
        // measured off a run rather than argued from the source. A name rule is only as good as the names it knows (IsFoliage makes
        // the same point), and this is what makes a name the rule gets WRONG visible: flora_slowtree took the generic ladder, 14 m of
        // tree holding LOD0 to 56 m where the same 14 m as flora_willow_tree dropped it at 35 m, and nothing in any report said so.
        if (OS.GetEnvironment("UNNAMED_LOD_REPORT") == "1")
            GD.Print($"UNNAMED lod: {id} size={size:0.00} m levels={all.Count} ladder={(tree ? "tree" : "generic")} "
                + $"switch=[{string.Join(", ", from.Take(all.Count).Select(f => f.ToString("0.#")))}] m");
        // Tree silhouettes cross-fade through the authored bands. Explicit 0 retains the
        // hard-switch comparison; 1 also enables the diagnostic on other model families.
        string fadeOption = OS.GetEnvironment("UNNAMED_LOD_STIPPLE");
        bool stipple = fadeOption == "1" || tree && fadeOption != "0";
        // ONE BOX FOR EVERY SURFACE OF EVERY LEVEL. Godot measures each mesh's visibility range to the centre of that mesh's own
        // bounds, and a tree's trunk, crown and impostor centre metres apart, so at a switch distance one surface had changed level
        // and another had not: a crown drawn twice or not at all, its shadow with it, for as long as the camera stood in that gap.
        // The scatter's bands share a box for the same reason (ScatterView.AddLevels).
        for (int i = 0; i < all.Count; i++)
        {
            root.AddChild(all[i]);
            float begin = from[i], end = i + 1 < all.Count ? from[i + 1] : 0f;
            foreach (var mesh in all[i].FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
            {
                // An eight-view impostor represents the colour silhouette, not solid branches.
                // Its crossed sheets must not enter shadow maps or static GI as opaque slabs.
                if (Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Any(s =>
                    mesh.GetActiveMaterial(s)?.ResourceName.Contains("impostor", StringComparison.OrdinalIgnoreCase) == true))
                {
                    mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                    mesh.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
                }
                mesh.CustomAabb = Relative(all[i], mesh).AffineInverse() * shared;
                mesh.VisibilityRangeBegin = begin;
                mesh.VisibilityRangeEnd = !stipple && end > 0 ? MathF.BitDecrement(end) : end;
                // Adjacent levels share the same native fade interval.
                mesh.VisibilityRangeBeginMargin = stipple && begin > 0 ? 0.1f * begin : 0;
                mesh.VisibilityRangeEndMargin = stipple && end > 0 ? 0.1f * end : 0;
                mesh.VisibilityRangeFadeMode = stipple
                    ? GeometryInstance3D.VisibilityRangeFadeModeEnum.Self
                    : GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
            }
        }
        return root;
    }

    /// <summary>
    /// A static model's full mesh and its usable lighter levels, in order (the same checks as <see cref="ModelWithLods"/>), each a fresh
    /// instance the caller owns; empty when the model will not load. For callers that draw the levels themselves (the scatter's MultiMeshes).
    /// </summary>
    public IReadOnlyList<Node3D> ModelLevels(string id)
    {
        if (Model(id) is not { } full)
            return Array.Empty<Node3D>();
        var levels = new List<Node3D>();
        // Where each level was found, so a PARTIAL override is visible. The chain has always fallen through per FILE (At searches every
        // workspace), which is right; what was missing is that it did so silently. See ReportProvenance.
        var from = new List<string> { WorkspaceOfPath(id) };
        bool textured = Textured(full);
        for (int n = 1; n <= 3; n++)
        {
            string relative = Path.Combine("ready", id, $"{id}_lod{n}.glb");
            if (Root is null || At(relative) is not { } found)
                break;
            if (Instance($"{id}_lod{n}", relative) is not { } level)
                break;
            // Godot gives a surface with no material a default one, so a level is judged by its textures: the full model's are required.
            if (textured && !Textured(level))
            {
                Report($"{id}_lod{n}", "a surface without the full model's textures: the chain stops at the level before it", $"{id}_lod{n}");
                level.Free();
                break;
            }
            levels.Add(level);
            from.Add(WorkspaceOf(found));
        }
        ReportProvenance(id, from);
        var all = new List<Node3D> { full };
        all.AddRange(levels);
        return all;
    }

    /// <summary>Where <paramref name="id"/>'s own model (<c>ready/&lt;id&gt;/&lt;id&gt;.glb</c>) lies, by workspace.</summary>
    private string WorkspaceOfPath(string id) =>
        At(Path.Combine("ready", id, id + ".glb")) is { } path ? WorkspaceOf(path) : Root ?? string.Empty;

    private readonly HashSet<string> _provenanceReported = new(StringComparer.Ordinal);

    /// <summary>
    /// Prints, once per asset per run, which workspace each level of a model's chain came from: the case is a region that overrides an
    /// asset with LOD0 alone, so its LOD0 is the region's mesh while every lighter level is the depot's.
    /// </summary>
    /// <remarks>
    /// THIS IS A PROVENANCE NOTE, NOT AN ALARM, AND THE FIRST VERSION OF IT SAID OTHERWISE. It claimed the fall-through levels were
    /// "decimations of THAT workspace's LOD0 rather than of the mesh being drawn", and quoted 60 mm / 204 mm of shape error on
    /// Cairnwold's two boulders. **Both claims were wrong, and they were wrong in the same way: the measurement did not align the two
    /// meshes.** A depot asset is RE-ORIGINED - <c>env_ph_boulder_01_meta.json</c> says "base centre: LOD0 bounding-box centre at X=0,
    /// Z=0, min Y=0" - and the region's override is not, so the boulders' two bounding-box centres differ by ~0.11 m. Measuring a
    /// distance across that offset reports the re-origin as shape error and overstates it **46x**: 47.94 mm before alignment,
    /// **1.15 mm after** (mean, onto the region's LOD0), which is **0.10 px** on a 1920x1080 frame at the 8 m band. The depot's own
    /// chain is decimated from the SAME source mesh the region re-origined (same <c>source_faces</c>, bounds agreeing to 4 decimals,
    /// 0.02 % of the largest side by a scale-invariant shape test), so the hand-over is geometrically seamless and there is no pop.
    /// See docs/phase_b/LOD_OVERRIDE_ROUND.md. **Align before measuring: a re-origin is not a defect.**
    /// </remarks>
    private void ReportProvenance(string id, List<string> from)
    {
        if (!_provenanceReported.Add(id))
            return;
        if (from.Count < 2)
            return;
        // The full path, not its leaf: the convention on disk puts the region at <depot>\<region>\assets, so BOTH roots are named
        // "assets" and a leaf comparison answers "not mixed" for every partial override there is.
        var names = from.Select(WorkspaceLabel).ToArray();
        bool mixed = from.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        if (!mixed)
            return;
        GD.Print($"UNNAMED art: {id}: partial override - LOD0 from {names[0]}, "
            + $"lod{string.Join("/lod", Enumerable.Range(1, names.Length - 1))} from {string.Join(", ", names.Skip(1).Distinct(StringComparer.Ordinal))}. "
            + "All levels draw; this is a note on where they come from, not a fault. Check the fall-through chain is decimated from the "
            + "same mesh as the LOD0 being drawn (align them first - a re-origin is not a defect) before treating it as a pop.");
    }

    /// <summary>A workspace named in a report: <c>depot</c> for the fallback, <c>region</c> for the primary, with the path for the log.</summary>
    private string WorkspaceLabel(string workspace) =>
        string.Equals(workspace, FallbackRoot, StringComparison.OrdinalIgnoreCase) ? $"the depot ({workspace})"
        : string.Equals(workspace, Root, StringComparison.OrdinalIgnoreCase) ? $"the region ({workspace})"
        : workspace;

    private static bool Textured(Node3D model)
    {
        var meshes = model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>().ToList();
        return meshes.Count > 0 && meshes.All(m => m.Mesh is not null && Enumerable.Range(0, m.Mesh.GetSurfaceCount())
            .All(i => m.GetActiveMaterial(i) is BaseMaterial3D { AlbedoTexture: not null }));
    }

    /// <summary>
    /// Whether an id names a tree, so its levels switch on the tree ladder rather than on its extent. The test is the id's HEAD NOUN:
    /// drop an optional variant suffix (<c>flora_oak_tree_b</c> is an oak tree), then ask whether what is left names a tree - its last
    /// word is <c>tree</c>, or it is one of the species that is only ever a tree.
    /// </summary>
    /// <remarks>
    /// IT WAS <c>StartsWith("flora_") &amp;&amp; Contains("_tree")</c>, AND BOTH HALVES WERE WRONG.
    ///
    /// The <c>_tree</c> substring has an underscore in it that the family does not: <c>flora_slowtree</c> is a 14 m tree with a
    /// four-level chain and it matched neither half of a rule every one of its siblings matches, so it took the generic ladder - LOD0
    /// held to 56 m where <c>flora_willow_tree</c>, the same 14 m with the same chains, drops it at 35 m.
    ///
    /// The <c>flora_</c> prefix is the larger half, and it is measured rather than argued. A region names its own art by its own
    /// convention: Cairnwold's trees are <c>cw_birch_a..f</c> and <c>cw_pine_a..f</c>, so the rule never fired for a single tree the
    /// region actually draws. Twelve LIVE models - 55 pine placements and 90 birch placements - took the generic ladder. A run's own
    /// numbers (<c>UNNAMED_LOD_REPORT=1</c>): <c>cw_pine_f</c> at 15.54 m held LOD0 to <b>62.2 m</b> where an identical 15.5 m tree drops
    /// it at 35 m, and <c>cw_birch_a</c> at 9.54 m held it to 38.2 m against 35 m. Their LOD3s are 16 triangles - the crossed-plane
    /// impostor <c>tools/asset_pipeline/_procgen_tree_lods.py</c> builds, which docs/phase_b/demo/trees/TREES.md judges at 25/50/80 m
    /// and says is worth reaching sooner because "the alpha-cut pine needles and dead twigs of LOD1/LOD2 break up into sky, where the
    /// impostor stays solid". So these twelve were built as tree-ladder assets and handed a ladder that keeps the expensive levels
    /// 1.4-1.8x too long. It costs performance and it holds twig-thin LOD1 meshes past the range the sheet says they read at.
    ///
    /// The species list is applied to the HEAD NOUN only, so a part of a tree or a different plant keeps the generic ladder: measured
    /// over every tree-ish id in both workspaces (docs/phase_b/lod_override/flora_classification.md), 62 ids evaluated, 10 matched the
    /// old rule, 22 match this one, and the 12 that move are the region's own pines and birches. <c>flora_ash_stumps</c> (a stump),
    /// <c>flora_fallen_log</c> (a log), <c>flora_young_ash_stand</c> (a stand of trees, not a tree), <c>flora_bramble_bush</c> (a
    /// shrub) and <c>env_ph_dead_tree_trunk</c> (a trunk) all keep the generic ladder, which is correct for every one of them.
    /// </remarks>
    /// <summary>
    /// Owner ruling for finding 40 (2026-10-23): "keep all of the trees at full detail at every distance. Never change the way the trees are
    /// drawn." Every tree - the tree family, Old Late, the sapling stands - is its full model and nothing else, wherever it stands.
    /// </summary>
    public static bool IsTree(string id) => IsTreeFamily(id) || id == "flora_old_late" || id.Contains("sapling", StringComparison.Ordinal);

    /// <summary>A tree as drawn at every distance: no visibility range, no fade, the renderer's automatic mesh detail held at full, and never
    /// culled by the buildings' occluders.</summary>
    public static T? Whole<T>(T? node) where T : Node3D
    {
        if (node is null)
            return null;
        foreach (var mesh in node.FindChildren("*", nameof(GeometryInstance3D), true, false).Cast<GeometryInstance3D>().Prepend(node as GeometryInstance3D).OfType<GeometryInstance3D>())
        {
            mesh.VisibilityRangeBegin = mesh.VisibilityRangeEnd = 0;
            mesh.VisibilityRangeBeginMargin = mesh.VisibilityRangeEndMargin = 0;
            mesh.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
            mesh.LodBias = FullDetailLodBias;
            // The occlusion test runs a frame behind the camera: a tree it wrongly hides blinks out while the player runs at it.
            mesh.IgnoreOcclusionCulling = true;
        }
        return node;
    }

    /// <summary>Far beyond any distance in the world: the renderer never picks a lighter index level for a tree.</summary>
    public const float FullDetailLodBias = 10000f;

    private static bool IsTreeFamily(string id)
    {
        ReadOnlySpan<char> head = id;
        if (head.Length > 2 && head[^2] == '_' && char.IsAsciiLetterLower(head[^1]))
            head = head[..^2];
        if (head.EndsWith("tree", StringComparison.Ordinal))
            return true;
        string name = head.ToString();
        return TreeSpecies.Any(species => name.EndsWith(species, StringComparison.Ordinal));
    }

    /// <summary>
    /// Species whose every asset is a tree, whatever a region prefixes it with: Cairnwold's <c>cw_pine_*</c> and <c>cw_birch_*</c> are
    /// whole trees, while <c>cw_bed_straw</c>, <c>env_ph_tree_stump_01</c> and every <c>*_bush</c>, <c>*_fern</c> and <c>*_log</c> end in
    /// something else and are left on the generic ladder. Add a name here when a region ships a tree under a new species word.
    /// </summary>
    private static readonly string[] TreeSpecies = { "birch", "pine" };

    /// <summary>A skinned model (<c>rigged/&lt;id&gt;/&lt;id&gt;_rigged.glb</c>), or null.</summary>
    public Node3D? Rigged(string id) => Instance(id, Path.Combine("rigged", id, id + "_rigged.glb"));

    /// <summary>A file the pipeline ships beside a rigged model (<c>rigged/&lt;id&gt;/&lt;relative&gt;</c>), or null when no workspace has it.</summary>
    public string? RiggedFile(string id, string relative) => At(Path.Combine("rigged", id, relative));

    private Node3D? Instance(string id, string relative)
    {
        if (Root is null || IsWithheld(id))
            return null;
        if (!_scenes.TryGetValue(relative, out var scene))
        {
            scene = At(relative) is { } path ? LoadScene(id, path) : Miss(id, relative);
            _scenes[relative] = scene;
        }
        if (scene?.Instantiate() is not Node3D node)
            return null;
        UseOriginalShadowGeometry(node); // Also covers native cached scenes.
        Use(id);
        return node;
    }

    private static void UseOriginalShadowGeometry(Node3D root)
    {
        // A generated position-only proxy on the forge shed casts a false region-wide
        // shadow although its CPU triangles match. Keep normal shadow casting, using
        // the visible static mesh. Godot may generate another proxy during GenerateScene.
        foreach (var node in root.FindChildren("*", nameof(MeshInstance3D), true, false)
                     .OfType<MeshInstance3D>().Prepend(root as MeshInstance3D).OfType<MeshInstance3D>())
            if (node.Skin is null && node.Mesh is ArrayMesh { ShadowMesh: not null } mesh)
                mesh.ShadowMesh = null;
    }

    /// <summary>Release a departed space's cached model resources after its scene instances have been freed.</summary>
    public int ReleaseModels(string prefix)
    {
        int count = 0;
        foreach (string path in _scenes.Keys.Where(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.Ordinal)).ToArray())
        {
            _scenes[path]?.Dispose();
            _scenes.Remove(path);
            count++;
        }
        foreach (string id in _vertices.Keys.Where(id => id.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            _vertices.Remove(id);
        return count;
    }

    /// <summary>Records that no workspace holds <paramref name="relative"/> (what the greybox stands in for), and returns nothing.</summary>
    private PackedScene? Miss(string id, string relative)
    {
        Problem(id, $"no file at {relative}");
        return null;
    }

    private PackedScene? LoadScene(string id, string path)
    {
        if (!File.Exists(path))
        {
            Problem(id, $"no file at {Path.GetRelativePath(WorkspaceOf(path), path)}");
            return null;
        }
        ulong started = Time.GetTicksUsec();
        string? stamp = null;
        if (_nativeSceneRoot is not null && _sceneCachePrefix is not null && id.StartsWith(_sceneCachePrefix, StringComparison.Ordinal))
        {
            // Keep the full-source proof without allocating every GLB on the large-object heap at portal entry.
            using var source = File.OpenRead(path);
            stamp = SceneCacheFormat + " " + Engine.GetVersionInfo()["string"].AsString() + " "
                + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source));
        }
        string native = _nativeSceneRoot is null ? "" : Path.Combine(_nativeSceneRoot, "scenes", Path.GetRelativePath(WorkspaceOf(path), path) + ".scn").Replace('\\', '/');
        if (stamp is not null && File.Exists(native) && File.Exists(native + ".sha256")
            && File.ReadAllText(native + ".sha256") == stamp
            && ResourceLoader.Load<PackedScene>(native, cacheMode: ResourceLoader.CacheMode.Ignore) is { } cached)
        {
            _sceneCount++;
            _sceneMs += (Time.GetTicksUsec() - started) / 1000.0;
            GD.Print($"UNNAMED native scene: {Path.GetFileName(path)} {(Time.GetTicksUsec() - started) / 1000.0:0} ms");
            return cached;
        }
        _loadOrder.Add(path);
        // Disposed on return: the state holds every decoded source image, which otherwise stays in memory until the .NET collector
        // finalizes it (Phase B: five 4096-atlas characters held ~640 MB that way). Parsed ahead on a worker when it was prefetched.
        var parsed = Take(path);
        using var document = parsed?.Document ?? new GltfDocument();
        using var state = parsed?.State ?? new GltfState();
        var error = parsed?.Error ?? document.AppendFromFile(path, state);
        if (error != Error.Ok)
        {
            Problem(id, $"the glTF would not load ({error})");
            return null;
        }
        GenerateStaticMeshLods(state, id);
        if (document.GenerateScene(state) is not Node3D root)
        {
            Problem(id, "the glTF did not produce a 3D scene");
            return null;
        }
        // Each texture by its image's index in the file, for the texture cache.
        var images = new Dictionary<ulong, int>();
        var loaded = state.GetImages();
        for (int i = 0; i < loaded.Count; i++)
        {
            if (loaded[i] is { } image)
            {
                images.TryAdd(image.GetInstanceId(), i);
                image.Dispose();
            }
        }
        ((Godot.Collections.Array)loaded).Dispose();
        Filter(root, path, images);
        UseOriginalShadowGeometry(root); // Before packing/saving any new native cache.
        AddBuildingOccluders(root, id);
        var packed = new PackedScene();
        error = packed.Pack(root);
        root.Free();
        _sceneCount++;
        double ms = (Time.GetTicksUsec() - started) / 1000.0;
        _sceneMs += ms;
        // UNNAMED_LOAD_REPORT=1: each model file's load time and when in the boot it came, for the startup measurement (WP1).
        if (OS.GetEnvironment("UNNAMED_LOAD_REPORT") == "1")
            GD.Print($"UNNAMED load file: {ms:0} ms at {Time.GetTicksMsec() / 1000.0:0.0} s {Path.GetFileName(path)} ({new FileInfo(path).Length / 1048576.0:0.0} MB)");
        if (error != Error.Ok)
        {
            Problem(id, $"the loaded scene would not pack ({error})");
            return null;
        }
        if (stamp is not null && _buildSceneCache)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(native)!);
            var saved = ResourceSaver.Save(packed, native, ResourceSaver.SaverFlags.Compress);
            if (saved != Error.Ok) throw new IOException($"Could not build native scene {native}: {saved}");
            File.WriteAllText(native + ".sha256", stamp);
            File.WriteAllText(native + ".source.json", JsonSerializer.Serialize(new { source = path, stamp }));
            GD.Print($"UNNAMED native scene built: {native}");
        }
        return packed;
    }

    private static void AddBuildingOccluders(Node3D root, string id)
    {
        if (!id.StartsWith("bld_", StringComparison.Ordinal)
            || id.EndsWith("_lod1", StringComparison.Ordinal) || id.EndsWith("_lod2", StringComparison.Ordinal) || id.EndsWith("_lod3", StringComparison.Ordinal)
            || System.Environment.GetEnvironmentVariable("UNNAMED_BUILDING_OCCLUSION") == "0"
            || root.FindChildren("*", nameof(AnimationPlayer), true, false).Count > 0) return;
        foreach (var node in root.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            if (!node.Visible || node.Skin is not null || node.Mesh is not ArrayMesh mesh) continue;
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                // Use the actual opaque triangles, preserving window/door holes. Alpha-tested,
                // transparent and custom-shader surfaces cannot be trusted as solid occluders.
                if (mesh.SurfaceGetPrimitiveType(s) != Mesh.PrimitiveType.Triangles
                    || node.GetActiveMaterial(s) is not BaseMaterial3D { Transparency: BaseMaterial3D.TransparencyEnum.Disabled } material
                    || material.NoDepthTest || material.DepthDrawMode == BaseMaterial3D.DepthDrawModeEnum.Disabled) continue;
                using var arrays = mesh.SurfaceGetArrays(s);
                var points = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                int offset = vertices.Count;
                vertices.AddRange(points);
                if (arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil)
                    for (int i = 0; i < points.Length; i++) indices.Add(offset + i);
                else
                    foreach (int i in arrays[(int)Mesh.ArrayType.Index].AsInt32Array()) indices.Add(offset + i);
            }
            if (indices.Count == 0) continue;
            var shape = new ArrayOccluder3D();
            shape.SetArrays(vertices.ToArray(), indices.ToArray());
            var occluder = new OccluderInstance3D { Name = "OpaqueBuildingOccluder", Occluder = shape };
            node.AddChild(occluder);
            occluder.Owner = root; // It must survive PackedScene.Pack/Instantiate, like the imported mesh.
        }
    }

    // Runtime GLTF loading bypasses the editor's mesh-LOD generation. Keep the authored
    // close-up mesh and materials; let Godot select index LODs by projected error in each pass.
    private static void GenerateStaticMeshLods(GltfState state, string id)
    {
        if (System.Environment.GetEnvironmentVariable("UNNAMED_RUNTIME_MESH_LODS") == "0"
            || id.StartsWith("anim.", StringComparison.Ordinal)) return;
        var skins = state.GetSkins();
        bool skinned = skins.Count > 0;
        ((Godot.Collections.Array)skins).Dispose();
        if (skinned) return;
        ulong started = Time.GetTicksUsec();
        int generated = 0;
        var meshes = state.GetMeshes();
        foreach (var entry in meshes)
        {
            var mesh = entry.Mesh;
            if (mesh is null || mesh.GetBlendShapeCount() > 0) continue;
            long indices = 0;
            bool existing = false;
            for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            {
                existing |= mesh.GetSurfaceLodCount(surface) > 0;
                using var arrays = mesh.GetSurfaceArrays(surface);
                indices += arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil
                    ? arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length
                    : arrays[(int)Mesh.ArrayType.Index].AsInt32Array().Length;
            }
            if (!existing && indices >= 30_000)
            {
                using var boneTransforms = new Godot.Collections.Array();
                mesh.GenerateLods(60, 0, boneTransforms);
                generated++;
            }
            // Use Godot's original mesh for depth and shadows. The custom welded
            // proxy produced a view-dependent shadow over the region from the
            // forge shed despite passing CPU topology checks. Keep casting and
            // authored materials intact; never substitute that proxy on import.
        }
        ((Godot.Collections.Array)meshes).Dispose();
        if (generated > 0 && OS.GetEnvironment("UNNAMED_LOAD_REPORT") == "1")
            GD.Print($"UNNAMED runtime mesh LODs: {id}, {generated} static meshes in {(Time.GetTicksUsec() - started) / 1000.0:0.0} ms");
    }

    /// <summary>
    /// The model files a boot will ask for, parsed and their images decoded on worker threads ahead of the scene build, which then only
    /// builds each scene on the main thread (<see cref="LoadScene"/>). <paramref name="paths"/> is the last boot's order
    /// (<see cref="LoadOrder"/>), so the workers run ahead in the order the build asks; at most <paramref name="ahead"/> files are parsed
    /// and waiting at once, which bounds the decoded images held in memory. A file the build never asks for is let go at
    /// <see cref="EndPrefetch"/>. Measured on BEAST (WP1): the parse and decode were most of the boot's model time.
    /// </summary>
    public void Prefetch(IEnumerable<string> paths, int ahead)
    {
        var window = _window = new SemaphoreSlim(ahead);
        var ended = _prefetchEnded.Token;
        var queue = paths.Where(p => File.Exists(p) && !_ahead.ContainsKey(p)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => (Path: p, Done: _ahead[p] = new TaskCompletionSource<Parsed>(TaskCreationOptions.RunContinuationsAsynchronously))).ToList();
        Task.Run(async () =>
        {
            foreach (var (path, done) in queue)
            {
                await window.WaitAsync();
                if (ended.IsCancellationRequested)
                {
                    done.TrySetCanceled();
                    continue;
                }
                if (!_claimed.TryAdd(path, 0))
                {
                    window.Release();      // the build asked for it first and parsed it in place
                    continue;
                }
                _ = Task.Run(() =>
                {
                    var document = new GltfDocument();
                    var state = new GltfState();
                    try
                    {
                        done.TrySetResult(new Parsed(document, state, document.AppendFromFile(path, state)));
                    }
                    catch (Exception e)
                    {
                        document.Dispose();
                        state.Dispose();
                        done.TrySetException(e);
                    }
                });
            }
        });
    }

    /// <summary>A prefetched file's parse, waited for if a worker is still on it; null when it was not prefetched or its parse failed.</summary>
    private Parsed? Take(string path)
    {
        if (!_ahead.Remove(path, out var pending))
            return null;
        // A file the workers have not reached is parsed in place: waiting for it while the window is full of files this boot never
        // asks for (the last boot's order differs) would block the main thread for ever.
        if (_claimed.TryAdd(path, 0))
            return null;
        try
        {
            return pending.Task.Result;
        }
        catch (AggregateException e)
        {
            GD.PushWarning($"UNNAMED art: {Path.GetFileName(path)} could not be parsed ahead ({e.InnerException?.Message}); parsed again in place");
            return null;
        }
        finally
        {
            _window?.Release();
        }
    }

    /// <summary>The scene is built: whatever was prefetched and not asked for is let go as its worker finishes.</summary>
    public void EndPrefetch()
    {
        _prefetchEnded.Cancel();
        foreach (var pending in _ahead.Values)
        {
            pending.Task.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    t.Result.Document.Dispose();
                    t.Result.State.Dispose();
                }
            });
            _window?.Release();
        }
        _ahead.Clear();
    }

    /// <summary>Every model file loaded so far, in the order the build asked for them: the next boot's prefetch order.</summary>
    public IReadOnlyList<string> LoadOrder => _loadOrder;

    /// <summary>
    /// Every texture of every material in a freshly loaded model, once (the scene is cached and instanced after): mipmaps where the runtime
    /// glTF path made none - without them a textured surface shimmers and crawls with distance - and the materials sampling them with
    /// anisotropic filtering (Phase A: the visual audit's V1). The textures stay uncompressed RGBA8 (VRAM compression is a Phase-B import step).
    /// A surface whose mesh carries vertex colours (glTF COLOR_0) has them multiply its colour, as glTF says.
    /// </summary>
    /// <summary>
    /// Whether an asset is a plant, and so whose materials are leaves rather than surfaces. Named rather
    /// than guessed from the textures, and a stump is wood: it keeps ordinary shading.
    /// </summary>
    /// <summary>
    /// Whether an asset is a plant, and so gets leaf shading rather than wood shading. This is keyed on the
    /// asset's NAME because the pipeline carries no other signal, and a name rule is only as good as the names
    /// it knows: a region whose trees are not called <c>flora_*</c> silently gets no foliage treatment at all -
    /// no alpha scissor, no backlight, no two-sided cards, no roughness floor. Cairnwold's birches and pines
    /// were drawn entirely the wrong way round for exactly that reason, which is why their crowns read as
    /// stacks of flat paper. Any future region must add its own plant names here.
    /// </summary>
    private static readonly string[] PlantWords =
        { "birch", "pine", "heather", "grass", "fern", "nettle", "leaf", "needle", "clump", "tuft", "tussock", "moss" };

    private static bool IsFoliage(string assetId)
    {
        bool planted = assetId.StartsWith("flora_", StringComparison.Ordinal)
                       || assetId.StartsWith("veg_", StringComparison.Ordinal)
                       || assetId.StartsWith("env_ph_tree", StringComparison.Ordinal)
                       || (assetId.StartsWith("cw_", StringComparison.Ordinal)
                           && PlantWords.Any(plant => assetId.Contains(plant, StringComparison.Ordinal)));
        bool woody = assetId.Contains("stump", StringComparison.Ordinal)
                     || assetId.Contains("bark", StringComparison.Ordinal)
                     || assetId.Contains("log", StringComparison.Ordinal)
                     || assetId.Contains("dead", StringComparison.Ordinal);
        return planted && !woody;
    }

    private static bool IsExplicitOpaqueWood(BaseMaterial3D material)
    {
        if (material.Transparency != BaseMaterial3D.TransparencyEnum.Disabled)
            return false;
        string name = material.ResourceName;
        return name.Contains("bark", StringComparison.OrdinalIgnoreCase)
            || name.Contains("wood", StringComparison.OrdinalIgnoreCase)
            || name.Contains("trunk", StringComparison.OrdinalIgnoreCase)
            || name.Contains("branch", StringComparison.OrdinalIgnoreCase);
    }

    private void Filter(Node3D root, string path, IReadOnlyDictionary<ulong, int> images)
    {
        ulong started = Time.GetTicksUsec();
        // The asset's own id (its folder under the workspace), which is what says whether this is a plant.
        string assetId = Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty;
        var seen = new HashSet<ulong>();
        var swapped = new Dictionary<ulong, Texture2D>();
        string relative = Path.GetRelativePath(WorkspaceOf(path), path);
        foreach (var mesh in root.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            if (mesh.Mesh is null)
                continue;
            for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                // glTF's COLOR_0 multiplies the base colour; the runtime import leaves the material ignoring it (the den's rock masses carry it).
                bool coloured = mesh.Mesh is ArrayMesh array && (array.SurfaceGetFormat(s) & Mesh.ArrayFormat.FormatColor) != 0;
                foreach (var candidate in new[] { mesh.Mesh.SurfaceGetMaterial(s), mesh.GetSurfaceOverrideMaterial(s) })
                {
                    if (coloured && candidate is BaseMaterial3D tinted)
                        tinted.VertexColorUseAsAlbedo = true;
                    if (candidate is not BaseMaterial3D material || !seen.Add(material.GetInstanceId()))
                        continue;
                    material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
                    // Keep imported opacity before whole-plant foliage treatment changes it.
                    if (!material.HasMeta("otherreach_source_transparency"))
                        material.SetMeta("otherreach_source_transparency", (int)material.Transparency);
                    // A file's baked occlusion is drawn UNLESS THIS MODEL IS ON THE FLAGGED LIST. The reason it used
                    // to be off for everything: "the generated and scanned models' occlusion channels are black over
                    // whole islands (the rim boulder over 57 % of its surface, a third of the models likewise:
                    // tools/visual_review/ao_check.py), which Godot applies as black patches where Blender shows
                    // none". That measurement is real and the list is it, made once by
                    // tools/asset_pipeline/cairnwold/ao_flag_report.py and shipped beside this file.
                    //
                    // THE BLANKET REMEDY COST MORE THAN THE FAULT. Run over Cairnwold's 70 ready models, ao_check
                    // flags ZERO; over the shared depot it flags 479 of 572. So switching AO off everywhere threw
                    // away the occlusion on every model that does not have the defect - including all of Cairnwold's,
                    // whose materials carry authored occlusion (`cw_wet_stone_wall` R = 0.445, `cw_thatch` 0.479,
                    // `cw_slate_roof` 0.822, `cw_timber_beam` 0.781, per check_orm.py) that the game has never drawn.
                    //
                    // And three separate blind passes asked for exactly this: "flat lighting lacking ambient
                    // occlusion" (round 146), "lack of subtle micro-geometry and natural shading" (148), "without
                    // ambient occlusion ... integrate contact shadows around building foundations" (155).
                    bool aoDrawn = material.AOTexture is not null && !FlaggedForOcclusion(assetId);
                    material.AOEnabled = aoDrawn;
                    if (aoDrawn) _aoDrawn++; else if (material.AOTexture is not null) _aoSuppressed++;
                    // ROUGHNESS WAS BEING READ FROM THE WRONG CHANNEL - on every imported asset, in both regions.
                    //
                    // Godot's BaseMaterial3D description: "OrmTexture ... a more efficient replacement of
                    // AOTexture, RoughnessTexture and MetallicTexture ... Ambient occlusion is stored in the red
                    // channel. Roughness map is stored in the green channel. Metallic map is stored in the blue
                    // channel." The glTF importer does NOT build an ORM material, it binds the maps separately -
                    // and `roughness_texture_channel` DEFAULTS TO RED, while glTF packs roughness in GREEN.
                    //
                    // That default is only harmless when the two slots hold different images. They do not:
                    // tools/asset_pipeline/cairnwold/asset root audit measured 1,916 of 1,980 shipped materials as
                    // TRUE ORM, meaning occlusionTexture and metallicRoughnessTexture resolve to the SAME image
                    // index. So AOTexture read R correctly and MetallicTexture read B correctly, while
                    // RoughnessTexture read R - ambient occlusion - as the roughness value. Authored roughness
                    // detail in G has never been drawn, and because AO sits around 0.5-1.0 the whole world has
                    // been rendering uniformly too rough and too matte. "Flat and plasticky" is this line.
                    //
                    // Green is correct for both cases: a glTF metallicRoughness map and a packed ORM map both put
                    // roughness in G.
                    if (material.RoughnessTexture is not null)
                        material.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Green;
                    // Foliage: a leaf transmits light. Without that, an alpha-cutout canopy is lit from one
                    // side only and goes dead on the shadow side, which is what makes a tree read as
                    // construction paper rather than as leaves - the owner's own words for it. Applied here,
                    // to every plant model, because it is a property of leaves rather than of one tree, and
                    // it is what turns the same geometry from card into canopy.
                    if (IsFoliage(assetId) && candidate is BaseMaterial3D leaf && !IsExplicitOpaqueWood(leaf))
                    {
                        leaf.CullMode = BaseMaterial3D.CullModeEnum.Disabled;   // a leaf plane is seen from both sides
                        leaf.BacklightEnabled = true;
                        leaf.Backlight = new Color(0.26f, 0.34f, 0.11f);        // sunlight coming through, not off, the leaf
                        leaf.MetallicSpecular = 0.12f;                          // leaves are not metal and barely gloss
                        if (leaf.Roughness < 0.72f)
                            leaf.Roughness = 0.72f;
                        if (leaf.Transparency is BaseMaterial3D.TransparencyEnum.Disabled or BaseMaterial3D.TransparencyEnum.Alpha or BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass)
                        {
                            // ALPHA BLEND IS THE WRONG MODE FOR FOLIAGE CARDS and it is what the tree models ship
                            // with (alphaMode BLEND in the glTF). Blend writes no depth, so a crown of fifty
                            // overlapping cards composites in whatever order it is drawn and reads as a stack of
                            // flat translucent quads - "torn paper", reported from ordinary gameplay and visible
                            // as plane edges through the crown. Scissor writes depth, so the cards occlude each
                            // other and the crown reads as a mass with holes in it, which is what a canopy is.
                            leaf.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                            leaf.AlphaScissorThreshold = 0.42f;   // low enough not to eat thin leaf edges
                            // ALPHA-TO-COVERAGE, WHICH THE SCATTER'S GRASS AND LEAVES ALREADY SET AND THIS PATH DID NOT.
                            // A hard scissor cutoff and a sub-pixel leaf are a flicker generator: as the camera moves,
                            // a thin leaf's coverage crosses 0.42 between one frame and the next, so the leaf is drawn,
                            // then not, then drawn again - the owner's "tree leaves and limbs ... go black and come back,
                            // draw and undraw, especially while you are walking around". Alpha-to-coverage hands the
                            // cutoff to the MSAA sample mask instead of a per-pixel yes/no, so a leaf that covers part
                            // of a pixel keeps that part and the edge resolves smoothly instead of toggling. It is the
                            // same setting ScatterView.cs gives grass (threshold 0.4) and its leaf atlas (0.5); the
                            // asymmetry is why the scatter stopped flickering and the trees did not.
                            leaf.AlphaAntialiasingMode = BaseMaterial3D.AlphaAntiAliasing.AlphaToCoverage;
                        }
                    }
                    for (int p = 0; p < (int)BaseMaterial3D.TextureParam.Max; p++)
                    {
                        var param = (BaseMaterial3D.TextureParam)p;
                        if (material.GetTexture(param) is not ImageTexture texture)
                            continue;
                        if (swapped.TryGetValue(texture.GetInstanceId(), out var shared))
                        {
                            material.SetTexture(param, shared);
                            texture.Dispose();
                            continue;
                        }
                        if (!seen.Add(texture.GetInstanceId()))
                            continue;
                        // The cache's compressed, shared copy (Phase B, B1), when the run uses the cache and has (or is building) one.
                        if (images.TryGetValue(texture.GetInstanceId(), out int index)
                            && (Cache.For(TextureCache.Key(relative, index), path, texture.GetImage, Role(material, param))
                                ?? (assetId.StartsWith("bw_codex_", StringComparison.Ordinal)
                                    ? _interiorBuildCache?.For(TextureCache.Key(relative, index), path, texture.GetImage, Role(material, param)) : null)) is { } cached)
                        {
                            swapped[texture.GetInstanceId()] = cached;
                            material.SetTexture(param, cached);
                            texture.Dispose();  // the uncompressed upload, now unreferenced
                            continue;
                        }
                        var image = texture.GetImage();
                        if (image is null || image.IsEmpty() || image.IsCompressed())
                            continue;
                        long pixels = (long)image.GetWidth() * image.GetHeight();
                        // Uploaded as four bytes a pixel (a three-channel image too, on Vulkan); a full mip chain adds a third.
                        _vramAsLoaded += pixels * 4 * (image.HasMipmaps() ? 4 : 3) / 3;
                        _vramWithMips += pixels * 4 * 4 / 3;
                        if (!image.HasMipmaps())
                        {
                            image.GenerateMipmaps();
                            texture.SetImage(image);
                        }
                        _textureCount++;
                    }
                }
            }
        }
        _textureMs += (Time.GetTicksUsec() - started) / 1000.0;
    }

    /// <summary>How the texture cache treats a map: colour (and whether alpha-tested), a normal map, or data (roughness, occlusion...).</summary>
    private static string Role(BaseMaterial3D material, BaseMaterial3D.TextureParam param) => param switch
    {
        BaseMaterial3D.TextureParam.Albedo => material.Transparency is BaseMaterial3D.TransparencyEnum.AlphaScissor or BaseMaterial3D.TransparencyEnum.AlphaHash
            ? "cutout" : "color",
        BaseMaterial3D.TextureParam.Emission => "color",
        BaseMaterial3D.TextureParam.Normal => "normal",
        _ => "data",
    };

    /// <summary>
    /// An animation clip by its ID (<c>creature.ash_ember_hound.walk</c>, <c>humanoid.locomotion.walk_forward</c>, <c>npc.talk</c>), as its
    /// file has it; <see cref="Retarget"/> points it at the skeleton that plays it. Null when absent.
    /// </summary>
    public Animation? Clip(string id)
    {
        if (Root is null)
            return null;
        if (_clips.TryGetValue(id, out var cached))
            return cached;
        string family = id.Split('.')[0] switch { "creature" => "creatures", "humanoid" => "humanoid", "npc" => "npc", var other => other };
        string relative = Path.Combine("animation", "ready", family, $"anim.{id}.glb");
        Animation? clip = null;
        SkeletonRest? rest = null;
        if (At(relative) is { } clipPath)
        {
            // Clips may carry a whole textured body. Keep only the copied animation/rest, not a temporary packed model.
            using var packed = LoadScene(id, clipPath);
            if (packed?.Instantiate() is { } scene)
            {
                try
                {
                    var player = Find<AnimationPlayer>(scene);
                    if (player?.GetAnimationList() is { Length: > 0 } names)
                        clip = (Animation)player.GetAnimation(names[0]).Duplicate();
                    else
                        Problem(id, "the clip file holds no animation");
                    if (Find<Skeleton3D>(scene) is { } source)
                        rest = SkeletonRest.Of(source);
                }
                finally { scene.Free(); }
            }
        }
        if (clip is not null)
            Use(id);
        _clips[id] = clip;
        _clipRests[id] = rest;
        return clip;
    }

    /// <summary>The rest pose of the skeleton a clip was authored on (from its file), or null when the file carries none.</summary>
    public SkeletonRest? ClipRest(string id)
    {
        Clip(id);
        return _clipRests.GetValueOrDefault(id);
    }

    /// <summary>A clip's record (<c>animation/clips/anim.&lt;id&gt;.json</c>): loop, length and events. Null when absent.</summary>
    public ClipInfo? Info(string id)
    {
        if (Root is null)
            return null;
        if (_clipInfo.TryGetValue(id, out var cached))
            return cached;
        ClipInfo? info = null;
        try
        {
            string path = Path.Combine(Root, "animation", "clips", $"anim.{id}.json");
            if (File.Exists(path))
            {
                // Each field is checked for its kind (the Phase-1 technical audit, H-02): a bad one refuses the record, never throws.
                if (ArtRecords.Clip(File.ReadAllText(path), out string? why) is { } record)
                    info = new ClipInfo(id, record.Loop, record.Seconds, record.Events) { AuthoredSpeed = record.AuthoredSpeed };
                else
                    Problem(id, $"its clip record cannot be used: {why}");
            }
        }
        catch (Exception e) when (IsReadFailure(e))
        {
            Problem(id, $"its clip record could not be read ({e.GetType().Name}: {e.Message})");
        }
        _clipInfo[id] = info;
        return info;
    }

    /// <summary>What reading a generated record can throw that is the record's fault, never the game's: caught and reported, the greybox drawn.</summary>
    private static bool IsReadFailure(Exception e) =>
        e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException
            or FormatException or NotSupportedException;

    /// <summary>
    /// A copy of a clip whose tracks drive the bones of <paramref name="skeletonPath"/> (relative to the player's root), matched by
    /// bone name; a track for a bone the skeleton lacks, or for anything but a bone, is dropped. Given the rest pose the clip was
    /// authored on, each key is carried over relative to rest: the target bone's own rest plus the clip's motion away from the
    /// source's, so a skeleton of other proportions keeps its own bone lengths and orientations (a shared clip set on a fitted rig).
    /// Root and hip travel scale with the hips' height. The same skeleton gets its keys back unchanged.
    /// </summary>
    public static Animation Retarget(Animation clip, string skeletonPath, Skeleton3D skeleton, bool loop, SkeletonRest? source = null)
    {
        var copy = (Animation)clip.Duplicate();
        var target = source is null ? null : SkeletonRest.Of(skeleton);
        float scale = source is { Height: > 0.01f } && target is { Height: > 0.01f } ? target.Height / source.Height : 1f;
        for (int i = copy.GetTrackCount() - 1; i >= 0; i--)
        {
            var path = copy.TrackGetPath(i);
            string bone = path.GetSubNameCount() > 0 ? path.GetSubName(0) : string.Empty;
            if (bone.Length == 0 || skeleton.FindBone(bone) < 0)
            {
                copy.RemoveTrack(i);
                continue;
            }
            copy.TrackSetPath(i, new NodePath($"{skeletonPath}:{bone}"));
            if (source is null || target is null || !source.Bones.TryGetValue(bone, out var from) || !target.Bones.TryGetValue(bone, out var to))
                continue;
            bool travels = skeleton.GetBoneParent(skeleton.FindBone(bone)) < 0 || bone is "hips" or "pelvis";
            switch (copy.TrackGetType(i))
            {
                case Animation.TrackType.Position3D:
                    for (int k = 0; k < copy.TrackGetKeyCount(i); k++)
                    {
                        var moved = (Vector3)copy.TrackGetKeyValue(i, k) - from.Origin;
                        copy.TrackSetKeyValue(i, k, to.Origin + (travels ? moved * scale : moved));
                    }
                    break;
                case Animation.TrackType.Rotation3D:
                    var toward = to.Basis.GetRotationQuaternion() * from.Basis.GetRotationQuaternion().Inverse();
                    for (int k = 0; k < copy.TrackGetKeyCount(i); k++)
                        copy.TrackSetKeyValue(i, k, (toward * (Quaternion)copy.TrackGetKeyValue(i, k)).Normalized());
                    break;
            }
        }
        // Godot's glTF import leaves out a track that holds a bone at its rest (only tracks that move a bone off its rest are kept), so
        // such a clip left that bone wherever the last clip had put it: a ready stance's bent back, a curled hand or a bent toe carried
        // on into a walk. Every bone the clip does not move is held at its rest, as the clip's file has it.
        var rotated = new HashSet<string>(StringComparer.Ordinal);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < copy.GetTrackCount(); i++)
        {
            string bone = copy.TrackGetPath(i).GetSubName(0);
            if (copy.TrackGetType(i) == Animation.TrackType.Rotation3D)
                rotated.Add(bone);
            else if (copy.TrackGetType(i) == Animation.TrackType.Position3D)
                placed.Add(bone);
        }
        for (int b = 0; b < skeleton.GetBoneCount(); b++)
        {
            string bone = skeleton.GetBoneName(b);
            var rest = skeleton.GetBoneRest(b);
            if (!rotated.Contains(bone))
            {
                int track = copy.AddTrack(Animation.TrackType.Rotation3D);
                copy.TrackSetPath(track, new NodePath($"{skeletonPath}:{bone}"));
                copy.RotationTrackInsertKey(track, 0, rest.Basis.GetRotationQuaternion());
            }
            if (!placed.Contains(bone))
            {
                int track = copy.AddTrack(Animation.TrackType.Position3D);
                copy.TrackSetPath(track, new NodePath($"{skeletonPath}:{bone}"));
                copy.PositionTrackInsertKey(track, 0, rest.Origin);
            }
        }
        copy.LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
        return copy;
    }

    /// <summary>
    /// A world material by its ID (<c>material_packed_dirt_ground</c>): its colour, normal and packed occlusion-roughness-metal maps,
    /// laid in world space at the tile size its record gives, so it tiles in metres on any surface. Null when absent.
    /// </summary>
    public Material? WorldMaterial(string id)
    {
        if (_materials.TryGetValue(id, out var cached))
            return cached;
        Material? material = null;
        if (WorldMaps(id) is { } maps)
        {
            material = new OrmMaterial3D
            {
                AlbedoTexture = maps.Albedo,
                NormalEnabled = maps.Normal is not null,
                NormalTexture = maps.Normal,
                NormalScale = maps.NormalStrength,
                OrmTexture = maps.Orm,
                Uv1Triplanar = true,
                Uv1WorldTriplanar = true,
                Uv1Scale = Vector3.One / maps.TileSizeM,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            };
        }
        _materials[id] = material;
        return material;
    }

    /// <summary>
    /// A world material's maps (<c>materials/&lt;id&gt;/</c>, by its record), mipmapped, for a surface that lays them itself (the terrain's
    /// splat, a triplanar cliff): null, with the problem recorded, when it is withheld, absent, or its record or colour map cannot be used.
    /// Never throws: a record with a field of the wrong kind is that one material's greybox (the Phase-1 technical audit, H-02).
    /// </summary>
    public WorldMaps? WorldMaps(string id)
    {
        if (Root is null || IsWithheld(id))
            return null;
        if (_maps.TryGetValue(id, out var cached))
            return cached;
        WorldMaps? maps = null;
        try
        {
            string folder = Path.Combine(Root, "materials", id);
            if (!Directory.Exists(folder) && FallbackRoot is not null && Directory.Exists(Path.Combine(FallbackRoot, "materials", id)))
                folder = Path.Combine(FallbackRoot, "materials", id);
            string record = Path.Combine(folder, id + "_material.json");
            if (!File.Exists(record))
            {
                Problem(id, $"no material record at materials/{id}/");
            }
            else if (ArtRecords.Material(File.ReadAllText(record), out string? why) is not { } read)
            {
                Problem(id, $"its material record cannot be used: {why}");
            }
            else if (Texture(Path.Combine(folder, read.Basecolor), "color") is not { } albedo)
            {
                Problem(id, $"its colour map ({read.Basecolor}) would not load");
            }
            else
            {
                var normal = read.Normal is { } n ? Texture(Path.Combine(folder, n), "normal") : null;
                var orm = read.Orm is { } o ? Texture(Path.Combine(folder, o), "data") : null;
                if (read.Normal is not null && normal is null || read.Orm is not null && orm is null)
                    Problem(id, $"its {(normal is null && read.Normal is not null ? "normal" : "ORM")} map would not load");
                else
                    maps = new WorldMaps(id, albedo, normal, orm, read.TileSizeM, read.NormalStrength);
            }
        }
        catch (Exception e) when (IsReadFailure(e))
        {
            Problem(id, $"its material record could not be read ({e.GetType().Name}: {e.Message})");
        }
        if (maps is not null)
            Use(id);
        _maps[id] = maps;
        return maps;
    }

    /// <summary>A map file as a texture, through the texture cache (<paramref name="role"/> as TextureCache takes it); null when it will not load.</summary>
    public Texture2D? Texture(string path, string role)
    {
        if (_textures.TryGetValue(path, out var cached))
            return cached;
        Texture2D? texture = null;
        ulong started = Time.GetTicksUsec();
        if (Cache.For(TextureCache.Key(Path.GetRelativePath(WorkspaceOf(path), path)), path, () => Image.LoadFromFile(path), role) is { } compressed)
        {
            texture = compressed;
            _mapCount++;
        }
        else if (File.Exists(path) && Image.LoadFromFile(path) is { } image && !image.IsEmpty())
        {
            if (image.IsCompressed())
                image.Decompress();
            long pixels = (long)image.GetWidth() * image.GetHeight();
            _vramAsLoaded += pixels * 4 * 4 / 3;
            _vramWithMips += pixels * 4 * 4 / 3;
            image.GenerateMipmaps();
            texture = ImageTexture.CreateFromImage(image);
            _mapCount++;
        }
        _mapMs += (Time.GetTicksUsec() - started) / 1000.0;
        _textures[path] = texture;
        return texture;
    }

    /// <summary>
    /// Something about an asset the presentation could not use, found by its caller (a missing socket, a size the world disagrees with):
    /// reported against <paramref name="key"/> - the ID, or the ID and where it was to stand - and the greybox stands in. The instance of
    /// <paramref name="released"/> the caller loaded and will not draw no longer counts as in use.
    /// </summary>
    public void Report(string key, string why, string released)
    {
        if (_uses.GetValueOrDefault(released) > 0)
            _uses[released]--;
        Problem(key, why, decided: true);
    }

    private void Problem(string id, string why, bool decided = false)
    {
        if (!_problems.TryAdd(id, why))
            return;
        // A withheld asset or a mismatch its caller found is a decision already made (and reported); a file that will not load is news.
        if (decided || why.StartsWith("withheld", StringComparison.Ordinal))
            GD.Print($"UNNAMED art: {id}: {why} - drawing greybox");
        else
            GD.PushWarning($"UNNAMED art: {id}: {why} - drawing greybox");
    }

    /// <summary>A node's transform relative to one of its ancestors.</summary>
    public static Transform3D Relative(Node3D root, Node3D node)
    {
        var transform = Transform3D.Identity;
        for (Node? at = node; at is not null && at != root; at = at.GetParent())
        {
            if (at is Node3D spatial)
                transform = spatial.Transform * transform;
        }
        return transform;
    }

    /// <summary>The first node of a type under a root, depth first.</summary>
    public static T? Find<T>(Node root) where T : Node
    {
        if (root is T found)
            return found;
        foreach (var child in root.GetChildren())
        {
            if (Find<T>(child) is { } inner)
                return inner;
        }
        return null;
    }
}
