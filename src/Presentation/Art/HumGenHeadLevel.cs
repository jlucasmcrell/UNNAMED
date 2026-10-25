using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// Lifts a downcast head part of the way back to level. The UAL clips are authored for a big-headed mannequin with the
/// head pitched 13-28 degrees down (idle, walk, talk, jog, sword idle); on a realistic body that reads as staring at
/// the ground. Only downward pitch is reduced, never pushed past level; yaw and roll stay the clip's.
/// </summary>
public partial class HumGenHeadLevel : SkeletonModifier3D
{
    public Node3D Body { get; set; } = null!;
    private const float Strength = .7f;
    private static readonly float MaxLift = Mathf.DegToRad(20);
    private int _head = -1;
    private Vector3 _axis;

    public override void _Ready()
    {
        var rig = GetSkeleton();
        _head = rig.FindBone("Head");
        if (_head >= 0)
            _axis = (rig.GetBoneGlobalRest(_head).Basis.Inverse() * (rig.GlobalBasis.Inverse() * Body.GlobalBasis.Z)).Normalized();
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        if (_head < 0) return;
        var rig = GetSkeleton();
        var pose = rig.GetBoneGlobalPose(_head);
        var facing = (pose.Basis * _axis).Normalized();
        var level = Level(facing, (rig.GlobalBasis.Inverse() * Vector3.Up).Normalized());
        if (level.IsEqualApprox(facing)) return;
        pose.Basis = new Basis(new Quaternion(facing, level)) * pose.Basis;
        rig.SetBoneGlobalPose(_head, pose);
    }

    /// <summary>A facing direction with its downward pitch reduced; the eye target uses the same lift as the head.</summary>
    public static Vector3 Level(Vector3 facing, Vector3 up)
    {
        float pitch = Mathf.Asin(Mathf.Clamp(facing.Dot(up), -1, 1));
        var side = facing.Cross(up);
        if (pitch >= 0 || side.LengthSquared() < .0001f) return facing;
        return facing.Rotated(side.Normalized(), Mathf.Min(-pitch * Strength, MaxLift));
    }
}
