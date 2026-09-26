// UNNAMED Presentation - a rigged model from the asset library and the clips that play on it
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// A skinned model with its clips, each under the name of the state it shows (<c>idle</c>, <c>walk</c>, <c>attack</c>...): the clips
/// are retargeted onto this model's skeleton by bone name, so a shared rig's clip (the NPC set, the husk's walk) plays on every
/// body of that rig. The owner of the figure says which state is showing; timing comes from the simulation, never from a clip.
/// </summary>
public sealed partial class SkinnedModel : Node3D
{
    private readonly HashSet<string> _states = new(StringComparer.Ordinal);
    private AnimationPlayer _player = null!;

    public Skeleton3D Skeleton { get; private set; } = null!;

    /// <summary>The state showing now, or null before the first.</summary>
    public string? Current { get; private set; }

    /// <summary>
    /// The rigged model <paramref name="assetId"/> with <paramref name="clips"/> (state to clip ID); null without the model. With an
    /// <paramref name="owner"/> (what it draws: <c>creature.beast.wolf_grey</c>), each state's clip is recorded in the coverage report.
    /// </summary>
    public static SkinnedModel? Create(ArtLibrary art, string assetId, IReadOnlyDictionary<string, string> clips, string? owner = null)
    {
        if (art.Rigged(assetId) is not { } model)
            return null;
        if (ArtLibrary.Find<Skeleton3D>(model) is not { } skeleton)
        {
            art.Report(assetId, "its rigged file holds no skeleton", assetId);
            model.Free();
            return null;
        }
        var figure = new SkinnedModel { Name = assetId, Skeleton = skeleton };
        figure.AddChild(model);
        CharacterMaterials.Upgrade(model);
        CharacterMaterials.ApplyLods(model);
        // Imported players (none in these files today) would fight ours over the same bones.
        foreach (var imported in model.FindChildren("*", nameof(AnimationPlayer), true, false))
            imported.QueueFree();
        var player = new AnimationPlayer { Name = "Clips" };
        figure.AddChild(player);
        player.RootNode = new NodePath("..");
        string skeletonPath = figure.GetPathTo(skeleton);
        var library = new AnimationLibrary();
        foreach (var (state, clipId) in clips)
        {
            if (art.Clip(clipId) is not { } clip)
            {
                if (owner is not null)
                    art.Coverage.Fallback("clip", $"{owner}:{state}", $"{art.Why(clipId) ?? "no clip"}: the body holds its last pose for this state", clipId);
                continue;
            }
            bool loop = art.Info(clipId)?.Loop ?? state is "idle" or "walk" or "run" or "sprint" or "talk" or "work";
            library.AddAnimation(state, ArtLibrary.Retarget(clip, skeletonPath, skeleton, loop, art.ClipRest(clipId)));
            figure._states.Add(state);
            if (owner is not null)
                art.Coverage.Resolved("clip", $"{owner}:{state}", clipId);
        }
        player.AddAnimationLibrary("", library);
        figure._player = player;
        figure.FindOutfits(model);
        figure.SetOutfit("base");
        return figure;
    }

    // Outfit variants (charstd export_character.py): a surface whose material is named "<variants>::<material>" shows only under those
    // variants - a garment, or the body under a garment only some variants wear. Everything else always shows.
    private readonly List<(MeshInstance3D Mesh, int Surface, string[] Variants, bool Whole)> _outfitParts = new();
    private string? _outfit;
    private static ShaderMaterial? _unseen;

    private void FindOutfits(Node model)
    {
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            if (mesh.Mesh is null)
                continue;
            var tagged = new List<(int, string[])>();
            for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
            {
                string name = mesh.Mesh.SurfaceGetMaterial(i)?.ResourceName ?? string.Empty;
                int cut = name.IndexOf("::", StringComparison.Ordinal);
                if (cut > 0)
                    tagged.Add((i, name[..cut].Split(',')));
            }
            bool whole = tagged.Count == mesh.Mesh.GetSurfaceCount() && tagged.All(t => t.Item2.SequenceEqual(tagged[0].Item2));
            foreach (var (surface, variants) in tagged)
                _outfitParts.Add((mesh, surface, variants, whole));
        }
    }

    /// <summary>Show one outfit variant (unknown names show <c>base</c>); a model without variants ignores it.</summary>
    public void SetOutfit(string variant)
    {
        if (_outfitParts.Count == 0)
            return;
        if (!_outfitParts.Any(p => p.Variants.Contains(variant)))
            variant = "base";
        if (variant == _outfit)
            return;
        _outfit = variant;
        _unseen ??= new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial; render_mode unshaded, cull_disabled; void fragment() { discard; }" } };
        foreach (var (mesh, surface, variants, whole) in _outfitParts)
        {
            bool shown = variants.Contains(variant);
            if (whole)
                mesh.Visible = shown;
            else
                mesh.SetSurfaceOverrideMaterial(surface, shown ? null : _unseen);
        }
    }

    public bool Has(string state) => _states.Contains(state);

    /// <summary>Show a state, blending from the last over <paramref name="blend"/> seconds; the same state keeps playing unless restarted.</summary>
    public void Play(string state, float blend = 0.2f, float speed = 1f, bool restart = false)
    {
        if (!_states.Contains(state))
            return;
        _player.SpeedScale = speed;
        if (Current == state && !restart)
            return;
        Current = state;
        _player.Play(state, blend);
        if (restart)
            _player.Seek(0, true);
    }

    /// <summary>A state's length in seconds (0 when it has no clip).</summary>
    public double Length(string state) => _states.Contains(state) ? _player.GetAnimation(state).Length : 0;

    /// <summary>Hold a state at a point in it (0-1): an attack's pose follows the simulation's phase, not the clip's clock.</summary>
    public void Hold(string state, float fraction, float blend = 0.1f)
    {
        if (!_states.Contains(state))
            return;
        if (Current != state)
        {
            Current = state;
            _player.Play(state, blend);
        }
        // Held by seeking every frame, the player's clock left running: a crossfade advances by the scaled clock, so at speed 0
        // the fade from the last state froze at its start and the last state went on showing (the held blow never appeared).
        _player.SpeedScale = 1;
        _player.Seek(Mathf.Clamp(fraction, 0, 1) * _player.GetAnimation(state).Length, true);
    }

    /// <summary>Something carried on a bone (a weapon in the hand), placed relative to it.</summary>
    public BoneAttachment3D? Attach(string bone)
    {
        if (Skeleton.FindBone(bone) < 0)
            return null;
        var attachment = new BoneAttachment3D { BoneName = bone, Name = "On_" + bone };
        Skeleton.AddChild(attachment);
        return attachment;
    }

    /// <summary>Drawn, or only casting its shadow (the body in first person).</summary>
    public void SetShadowOnly(bool shadowOnly)
    {
        foreach (var mesh in FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            // What rides on a bone (a held weapon: its model's meshes sit below the model's own root) stays drawn.
            if (OnBone(mesh))
                continue;
            mesh.CastShadow = shadowOnly ? GeometryInstance3D.ShadowCastingSetting.ShadowsOnly : GeometryInstance3D.ShadowCastingSetting.On;
        }
    }

    private bool OnBone(Node node)
    {
        for (var at = node.GetParent(); at is not null && at != this; at = at.GetParent())
        {
            if (at is BoneAttachment3D)
                return true;
        }
        return false;
    }
}
