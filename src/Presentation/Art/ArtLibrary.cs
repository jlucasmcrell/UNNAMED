// UNNAMED Presentation - the asset pipeline's models, animation clips and world materials, loaded at run time by semantic ID
// Godot presentation only: no gameplay state lives here (D-11)

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>What an animation clip's record says about it: whether it loops, how long it runs, and its timed events.</summary>
public sealed record ClipInfo(string Id, bool Loop, double Seconds, IReadOnlyList<(string Id, double Time)> Events);

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
    private readonly Dictionary<string, ClipInfo?> _clipInfo = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Material?> _materials = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, string> _problems = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _uses = new(StringComparer.Ordinal);

    private IReadOnlyDictionary<string, string> _withheld = new Dictionary<string, string>();

    public ArtLibrary(string? root) => Root = root;

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
        var document = new GltfDocument();
        var state = new GltfState();
        var error = document.AppendFromFile(path, state);
        if (error != Error.Ok || document.GenerateScene(state) is not Node3D root)
        {
            Problem(id, $"the glTF would not load ({error})");
            return null;
        }
        var packed = new PackedScene();
        error = packed.Pack(root);
        root.Free();
        if (error != Error.Ok)
        {
            Problem(id, $"the loaded scene would not pack ({error})");
            return null;
        }
        return packed;
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
        if (LoadScene(id, Path.Combine(Root, relative))?.Instantiate() is { } scene)
        {
            var player = Find<AnimationPlayer>(scene);
            if (player?.GetAnimationList() is { Length: > 0 } names)
                clip = (Animation)player.GetAnimation(names[0]).Duplicate();
            else
                Problem(id, "the clip file holds no animation");
            scene.Free();
        }
        if (clip is not null)
            Use(id);
        _clips[id] = clip;
        return clip;
    }

    /// <summary>A clip's record (<c>animation/clips/anim.&lt;id&gt;.json</c>): loop, length and events. Null when absent.</summary>
    public ClipInfo? Info(string id)
    {
        if (Root is null)
            return null;
        if (_clipInfo.TryGetValue(id, out var cached))
            return cached;
        string path = Path.Combine(Root, "animation", "clips", $"anim.{id}.json");
        ClipInfo? info = null;
        if (File.Exists(path))
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                var root = json.RootElement;
                var events = new List<(string, double)>();
                if (root.TryGetProperty("events", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in list.EnumerateArray())
                        events.Add((e.GetProperty("id").GetString()!, e.GetProperty("time").GetDouble()));
                }
                info = new ClipInfo(id, root.TryGetProperty("loop", out var loop) && loop.GetBoolean(),
                    root.TryGetProperty("duration_s", out var seconds) ? seconds.GetDouble() : 0, events);
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                Problem(id, $"its clip record could not be read ({e.Message})");
            }
        }
        _clipInfo[id] = info;
        return info;
    }

    /// <summary>
    /// A copy of a clip whose tracks drive the bones of <paramref name="skeletonPath"/> (relative to the player's root), matched by
    /// bone name; a track for a bone the skeleton lacks, or for anything but a bone, is dropped.
    /// </summary>
    public static Animation Retarget(Animation clip, string skeletonPath, Skeleton3D skeleton, bool loop)
    {
        var copy = (Animation)clip.Duplicate();
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
        if (Root is null || IsWithheld(id))
            return null;
        if (_materials.TryGetValue(id, out var cached))
            return cached;
        Material? material = null;
        string folder = Path.Combine(Root, "materials", id);
        string record = Path.Combine(folder, id + "_material.json");
        if (!File.Exists(record))
        {
            Problem(id, $"no material record at materials/{id}/");
        }
        else
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(record));
                var root = json.RootElement;
                var maps = root.GetProperty("maps");
                float tile = root.TryGetProperty("tile_size_m", out var t) ? (float)t.GetDouble() : 4f;
                var pbr = root.TryGetProperty("pbr", out var p) ? p : default;
                var orm = new OrmMaterial3D
                {
                    AlbedoTexture = Texture(Path.Combine(folder, maps.GetProperty("basecolor").GetString()!)),
                    NormalEnabled = maps.TryGetProperty("normal", out var normal),
                    NormalTexture = maps.TryGetProperty("normal", out normal) ? Texture(Path.Combine(folder, normal.GetString()!)) : null,
                    NormalScale = pbr.ValueKind == JsonValueKind.Object && pbr.TryGetProperty("normal_strength", out var strength) ? (float)strength.GetDouble() : 1f,
                    OrmTexture = maps.TryGetProperty("orm", out var ormMap) ? Texture(Path.Combine(folder, ormMap.GetString()!)) : null,
                    Uv1Triplanar = true,
                    Uv1WorldTriplanar = true,
                    Uv1Scale = Vector3.One / tile,
                };
                if (orm.AlbedoTexture is null)
                    Problem(id, "its colour map would not load");
                else
                    material = orm;
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                Problem(id, $"its material record could not be read ({e.Message})");
            }
        }
        if (material is not null)
            Use(id);
        _materials[id] = material;
        return material;
    }

    private Texture2D? Texture(string path)
    {
        if (_textures.TryGetValue(path, out var cached))
            return cached;
        Texture2D? texture = null;
        if (File.Exists(path) && Image.LoadFromFile(path) is { } image && !image.IsEmpty())
        {
            image.GenerateMipmaps();
            texture = ImageTexture.CreateFromImage(image);
        }
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
