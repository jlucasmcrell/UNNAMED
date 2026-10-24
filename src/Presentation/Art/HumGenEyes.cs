using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>Small native-eye aim changes without Euler-axis flips or edits to the exported rest.</summary>
public partial class HumGenEyes : SkeletonModifier3D
{
    public Node3D Body { get; set; } = null!;
    public Func<Vector3> WorldTarget { get; set; } = () => Vector3.Zero;
    private readonly List<(int Bone, Vector3 OpticalAxis)> _eyes = [];
    public int BoundCount => _eyes.Count;

    public override void _Ready()
    {
        var rig = GetSkeleton();
        var forward = rig.GlobalBasis.Inverse() * Body.GlobalBasis.Z;
        foreach (string name in new[] { "LeftEye", "RightEye" })
        {
            int bone = rig.FindBone(name);
            if (bone >= 0) _eyes.Add((bone, (rig.GetBoneGlobalRest(bone).Basis.Inverse() * forward).Normalized()));
        }
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        var rig = GetSkeleton();
        var target = rig.GlobalTransform.AffineInverse() * WorldTarget();
        foreach (var (bone, opticalAxis) in _eyes)
        {
            var pose = rig.GetBoneGlobalPose(bone);
            var toward = target - pose.Origin;
            if (toward.LengthSquared() < .0001f) continue;
            var from = (pose.Basis * opticalAxis).Normalized();
            var to = toward.Normalized();
            float angle = from.AngleTo(to);
            var turn = new Quaternion(from, to);
            if (angle > Mathf.DegToRad(25)) turn = Quaternion.Identity.Slerp(turn, Mathf.DegToRad(25) / angle);
            pose.Basis = new Basis(turn) * pose.Basis;
            rig.SetBoneGlobalPose(bone, pose);
        }
    }
}
