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

    /// <summary>The rigged model <paramref name="assetId"/> with <paramref name="clips"/> (state to clip ID); null without the model.</summary>
    public static SkinnedModel? Create(ArtLibrary art, string assetId, IReadOnlyDictionary<string, string> clips)
    {
        if (art.Rigged(assetId) is not { } model)
            return null;
        if (ArtLibrary.Find<Skeleton3D>(model) is not { } skeleton)
        {
            model.Free();
            return null;
        }
        var figure = new SkinnedModel { Name = assetId, Skeleton = skeleton };
        figure.AddChild(model);
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
                continue;
            bool loop = art.Info(clipId)?.Loop ?? state is "idle" or "walk" or "run" or "sprint" or "talk" or "work";
            library.AddAnimation(state, ArtLibrary.Retarget(clip, skeletonPath, skeleton, loop));
            figure._states.Add(state);
        }
        player.AddAnimationLibrary("", library);
        figure._player = player;
        return figure;
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
        _player.SpeedScale = 0;
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
            if (mesh.GetParent() is BoneAttachment3D)
                continue;
            mesh.CastShadow = shadowOnly ? GeometryInstance3D.ShadowCastingSetting.ShadowsOnly : GeometryInstance3D.ShadowCastingSetting.On;
        }
    }
}
