// UNNAMED Presentation - a body's build pose: its arms turned out from the trunk after the clips (WP17, body content arm_splay_deg)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// Every build plays the same clips on the same skeleton; a heavier build's own sides would take the swinging arms. After the clips have
/// posed the skeleton, each upper arm (and so everything below it) is turned out from the trunk by the body's arm splay, about the
/// body's forward axis at the shoulder - the arithmetic the clipping gate (tools/character_build/mh_gate.py --arm-splay-deg) checks the
/// baked garments with. A splay of 0 does nothing.
/// </summary>
public sealed partial class BuildPose : SkeletonModifier3D
{
    private int _hips = -1;
    private readonly int[] _upper = { -1, -1 };
    private readonly int[] _lower = { -1, -1 };
    private Vector3 _across;
    private Basis _hipsRest;

    /// <summary>How far, in degrees, each upper arm is turned out from the trunk.</summary>
    public float ArmSplayDegrees { get; set; }

    /// <summary>A build pose on <paramref name="skeleton"/>, or null when the rig lacks the hips or the arms.</summary>
    public static BuildPose? On(Skeleton3D skeleton)
    {
        var pose = new BuildPose();
        if (pose.Bind(skeleton))
        {
            skeleton.AddChild(pose);
            return pose;
        }
        pose.Free();
        return null;
    }

    /// <summary>Find the hips and the arms on this rig (either vocabulary, <see cref="BoneNames"/>); false, and no effect, when it lacks them.</summary>
    private bool Bind(Skeleton3D skeleton)
    {
        _hips = BoneNames.Find(skeleton, "hips");
        int thighL = BoneNames.Find(skeleton, "thigh.L"), thighR = BoneNames.Find(skeleton, "thigh.R");
        for (int s = 0; s < 2; s++)
        {
            string side = s == 0 ? "L" : "R";
            _upper[s] = BoneNames.Find(skeleton, $"upper_arm.{side}");
            _lower[s] = BoneNames.Find(skeleton, $"forearm.{side}");
        }
        if (_hips < 0 || thighL < 0 || thighR < 0 || _upper.Concat(_lower).Any(b => b < 0))
            return false;
        _across = (skeleton.GetBoneGlobalRest(thighL).Origin - skeleton.GetBoneGlobalRest(thighR).Origin).Normalized();
        _hipsRest = skeleton.GetBoneGlobalRest(_hips).Basis.Orthonormalized();
        return true;
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        if (GetSkeleton() is not { } skeleton || _hips < 0 || Mathf.Abs(ArmSplayDegrees) < 0.01f)
            return;
        var turned = skeleton.GetBoneGlobalPose(_hips).Basis.Orthonormalized() * _hipsRest.Inverse();
        var across = (turned * _across).Normalized();
        var forward = (turned * Vector3.Up).Cross(across).Normalized();
        for (int s = 0; s < 2; s++)
        {
            var upper = skeleton.GetBoneGlobalPose(_upper[s]);
            var arm = skeleton.GetBoneGlobalPose(_lower[s]).Origin - upper.Origin;
            var outward = s == 0 ? across : -across;
            float sign = forward.Cross(arm).Dot(outward) > 0 ? 1f : -1f;
            var turn = new Basis(forward, sign * Mathf.DegToRad(ArmSplayDegrees));
            skeleton.SetBoneGlobalPose(_upper[s], new Transform3D(turn * upper.Basis, upper.Origin));
        }
    }
}
