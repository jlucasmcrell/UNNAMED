// Presentation only: a Kal's folding dorsal wings (RACES.md 2). No gameplay state lives here.
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>What the wings are doing; the movement code decides, the wings only draw it.</summary>
public enum WingState { Folded, Glide, Flare, Leap }

/// <summary>
/// The wing rig (its own skeleton and the clip <c>deploy</c>: folded at its start, full span at its end) mounted on the body's
/// <c>UpperChest</c>, so it rides the torso through every clip. The rig's <c>wing_root</c> joint, between the two spar roots,
/// is placed on the spine just behind the chest and scaled from the stature the rig was built for.
/// </summary>
public partial class KalWings : BoneAttachment3D
{
    private const float BuiltForStature = 1.72f, DeploySeconds = .9f, FlarePitch = -.35f;

    private AnimationPlayer _player = null!;
    private Node3D _rig = null!;
    private Transform3D _placed;
    private Vector3 _pivot;
    private float _openness, _target, _pitch, _targetPitch;

    /// <summary>0 folded tight in the spine channels, 1 full span. Setting it snaps; <see cref="SetState"/> eases.</summary>
    public float Openness
    {
        get => _openness;
        set { _openness = _target = Mathf.Clamp(value, 0f, 1f); Apply(); }
    }

    public void SetState(WingState state)
    {
        _target = state switch { WingState.Folded => 0f, WingState.Leap => .6f, _ => 1f };
        _targetPitch = state == WingState.Flare ? FlarePitch : 0f;
    }

    public static KalWings? Attach(Skeleton3D skeleton, string glbPath, float backOffset = .1f)
    {
        int chest = skeleton.FindBone("UpperChest"), head = skeleton.FindBone("Head");
        if (chest < 0 || head < 0 || !File.Exists(glbPath))
            return null;
        var document = new GltfDocument();
        var state = new GltfState();
        if (document.AppendFromFile(glbPath, state) != Error.Ok || document.GenerateScene(state) is not Node3D rig)
            return null;
        var player = rig.FindChildren("*", nameof(AnimationPlayer), true, false).OfType<AnimationPlayer>().FirstOrDefault();
        var bones = rig.FindChildren("*", nameof(Skeleton3D), true, false).OfType<Skeleton3D>().FirstOrDefault();
        if (player is null || bones is null || !player.HasAnimation("deploy"))
            return null;
        var rootAt = (ArtLibrary.Relative(rig, bones) * bones.GetBoneGlobalRest(bones.FindBone("wing_root"))).Origin;
        // On these bodies the head joint sits at about 0.9 of stature.
        float scale = skeleton.GetBoneGlobalRest(head).Origin.Y / .9f / BuiltForStature;
        var chestRest = skeleton.GetBoneGlobalRest(chest);
        var wings = new KalWings { Name = "KalWings", BoneName = "UpperChest", _player = player, _rig = rig };
        wings._placed = chestRest.AffineInverse()
                        * new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), chestRest.Origin + Vector3.Forward * backOffset - rootAt * scale);
        wings._pivot = wings._placed * rootAt;
        skeleton.AddChild(wings);
        wings.AddChild(rig);
        foreach (var mesh in rig.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        player.Play("deploy");
        player.Pause();
        wings.Apply();
        return wings;
    }

    public override void _Process(double delta)
    {
        if (Mathf.IsEqualApprox(_openness, _target) && Mathf.IsEqualApprox(_pitch, _targetPitch))
            return;
        _openness = Mathf.MoveToward(_openness, _target, (float)delta / DeploySeconds);
        _pitch = Mathf.MoveToward(_pitch, _targetPitch, (float)delta * 1.5f);
        Apply();
    }

    private void Apply()
    {
        _player.Seek(_openness * _player.GetAnimation("deploy").Length, true);
        // Flare pitches the spread spars forward about their root on the spine.
        _rig.Transform = new Transform3D(Basis.Identity, _pivot) * new Transform3D(new Basis(Vector3.Right, _pitch), Vector3.Zero)
                         * new Transform3D(Basis.Identity, -_pivot) * _placed;
    }
}
