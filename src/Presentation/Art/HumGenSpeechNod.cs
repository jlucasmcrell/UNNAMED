using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>A small speech accent, after the authored body pose and head levelling. Never changes rest.</summary>
public partial class HumGenSpeechNod : SkeletonModifier3D
{
    public Node3D Body { get; set; } = null!;
    public Func<float> Pitch { get; set; } = () => 0;
    private int _head = -1;

    public override void _Ready() => _head = GetSkeleton().FindBone("Head");

    public override void _ProcessModificationWithDelta(double delta)
    {
        if (_head < 0) return;
        var rig = GetSkeleton();
        var pose = rig.GetBoneGlobalPose(_head);
        var right = (rig.GlobalBasis.Inverse() * Body.GlobalBasis.X).Normalized();
        pose.Basis = new Basis(new Quaternion(right, Mathf.Clamp(Pitch(), -.025f, .025f))) * pose.Basis;
        rig.SetBoneGlobalPose(_head, pose);
    }
}
