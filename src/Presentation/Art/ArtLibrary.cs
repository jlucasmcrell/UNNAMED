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


public sealed record ClipInfo(string Id, bool Loop, double Seconds, IReadOnlyList<(string Id, double Time)> Events);

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
    private readonly Dictionary<string, PackedScene?> _scenes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Animation?> _clips = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SkeletonRest?> _clipRests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClipInfo?> _clipInfo = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Material?> _materials = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorldMaps?> _maps = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, string> _problems = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _uses = new(StringComparer.Ordinal);
    private int _sceneCount, _textureCount, _mapCount;
    private double _sceneMs, _textureMs, _mapMs;
    private long _vramAsLoaded, _vramWithMips;

    private IReadOnlyDictionary<string, string> _withheld = new Dictionary<string, string>();

    public ArtLibrary(string? root) => Root = root;

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

    private bool IsWithheld(string id)
    {
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
        if (Model(id) is not { } full)
            return null;
        var levels = new List<Node3D>();
        bool textured = Textured(full);
        for (int n = 1; n <= 3; n++)
        {
            string relative = Path.Combine("ready", id, $"{id}_lod{n}.glb");
            if (Root is null || !File.Exists(Path.Combine(Root, relative)))
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
        }
        if (levels.Count == 0)
            return full;
        var root = new Node3D { Name = id };
        var extent = ArtGallery.Bounds(full).Size;
        float size = Math.Max(0.1f, Math.Max(extent.X, Math.Max(extent.Y, extent.Z)));
        float[] from = { 0, Math.Max(8f, 4f * size), Math.Max(20f, 10f * size), Math.Max(45f, 22f * size) };
        var all = new List<Node3D> { full };
        all.AddRange(levels);
        for (int i = 0; i < all.Count; i++)
        {
            root.AddChild(all[i]);
            float begin = from[i], end = i + 1 < all.Count ? from[i + 1] : 0f;
            foreach (var mesh in all[i].FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
            {
                mesh.VisibilityRangeBegin = begin;
                mesh.VisibilityRangeBeginMargin = begin > 0 ? 0.1f * begin : 0;
                mesh.VisibilityRangeEnd = end;
                mesh.VisibilityRangeEndMargin = end > 0 ? 0.1f * end : 0;
                mesh.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            }
        }
        return root;
    }

    private static bool Textured(Node3D model)
    {
        var meshes = model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>().ToList();
        return meshes.Count > 0 && meshes.All(m => m.Mesh is not null && Enumerable.Range(0, m.Mesh.GetSurfaceCount())
            .All(i => m.GetActiveMaterial(i) is BaseMaterial3D { AlbedoTexture: not null }));
    }

    /// <summary>A skinned model (<c>rigged/&lt;id&gt;/&lt;id&gt;_rigged.glb</c>), or null.</summary>
    public Node3D? Rigged(string id) => Instance(id, Path.Combine("rigged", id, id + "_rigged.glb"));

    private Node3D? Instance(string id, string relative)
    {
        if (Root is null || IsWithheld(id))
            return null;
        if (!_scenes.TryGetValue(relative, out var scene))
        {
            scene = LoadScene(id, Path.Combine(Root, relative));
            _scenes[relative] = scene;
        }
        if (scene?.Instantiate() is not Node3D node)
            return null;
        Use(id);
        return node;
    }

    private PackedScene? LoadScene(string id, string path)
    {
        if (!File.Exists(path))
        {
            Problem(id, $"no file at {Path.GetRelativePath(Root!, path)}");
            return null;
        }
        ulong started = Time.GetTicksUsec();
        var document = new GltfDocument();
        var state = new GltfState();
        var error = document.AppendFromFile(path, state);
        if (error != Error.Ok || document.GenerateScene(state) is not Node3D root)
        {
            Problem(id, $"the glTF would not load ({error})");
            return null;
        }
        Filter(root);
        var packed = new PackedScene();
        error = packed.Pack(root);
        root.Free();
        _sceneCount++;
        _sceneMs += (Time.GetTicksUsec() - started) / 1000.0;
        if (error != Error.Ok)
        {
            Problem(id, $"the loaded scene would not pack ({error})");
            return null;
        }
        return packed;
    }

    /// <summary>
    /// Every texture of every material in a freshly loaded model, once (the scene is cached and instanced after): mipmaps where the runtime
    /// glTF path made none - without them a textured surface shimmers and crawls with distance - and the materials sampling them with
    /// anisotropic filtering (Phase A: the visual audit's V1). The textures stay uncompressed RGBA8 (VRAM compression is a Phase-B import step).
    /// A surface whose mesh carries vertex colours (glTF COLOR_0) has them multiply its colour, as glTF says.
    /// </summary>
    private void Filter(Node3D root)
    {
        ulong started = Time.GetTicksUsec();
        var seen = new HashSet<ulong>();
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
                    for (int p = 0; p < (int)BaseMaterial3D.TextureParam.Max; p++)
                    {
                        if (material.GetTexture((BaseMaterial3D.TextureParam)p) is not ImageTexture texture || !seen.Add(texture.GetInstanceId()))
                            continue;
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
        if (LoadScene(id, Path.Combine(Root, relative))?.Instantiate() is { } scene)
        {
            var player = Find<AnimationPlayer>(scene);
            if (player?.GetAnimationList() is { Length: > 0 } names)
                clip = (Animation)player.GetAnimation(names[0]).Duplicate();
            else
                Problem(id, "the clip file holds no animation");
            if (Find<Skeleton3D>(scene) is { } source)
                rest = SkeletonRest.Of(source);
            scene.Free();
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
                    info = new ClipInfo(id, record.Loop, record.Seconds, record.Events);
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
            string record = Path.Combine(folder, id + "_material.json");
            if (!File.Exists(record))
            {
                Problem(id, $"no material record at materials/{id}/");
            }
            else if (ArtRecords.Material(File.ReadAllText(record), out string? why) is not { } read)
            {
                Problem(id, $"its material record cannot be used: {why}");
            }
            else if (Texture(Path.Combine(folder, read.Basecolor)) is not { } albedo)
            {
                Problem(id, $"its colour map ({read.Basecolor}) would not load");
            }
            else
            {
                var normal = read.Normal is { } n ? Texture(Path.Combine(folder, n)) : null;
                var orm = read.Orm is { } o ? Texture(Path.Combine(folder, o)) : null;
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

    private Texture2D? Texture(string path)
    {
        if (_textures.TryGetValue(path, out var cached))
            return cached;
        Texture2D? texture = null;
        ulong started = Time.GetTicksUsec();
        if (File.Exists(path) && Image.LoadFromFile(path) is { } image && !image.IsEmpty())
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
