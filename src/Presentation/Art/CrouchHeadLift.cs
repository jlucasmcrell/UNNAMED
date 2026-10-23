using Godot;

namespace UNNAMED.Presentation.Art;

// Additive animation pose only. Native rests, hierarchy, binds and mesh buffers stay intact.
public partial class CrouchHeadLift : SkeletonModifier3D
{
    public Node3D Facing { get; set; } = null!;
    public bool Crouched { get; set; }
    public override void _Process(double delta) => Influence=Mathf.MoveToward(Influence,Crouched?1f:0f,(float)delta*6);
    public override void _ProcessModificationWithDelta(double delta)
    {
        var skeleton=GetSkeleton();
        int neck=BoneNames.Find(skeleton,"neck");
        if(neck<0)return;
        var axis=(skeleton.GlobalBasis.Inverse()*Facing.GlobalBasis.X).Normalized();
        var pose=skeleton.GetBoneGlobalPose(neck);
        pose.Basis=new Basis(axis,Mathf.DegToRad(-35))*pose.Basis;
        skeleton.SetBoneGlobalPose(neck,pose);
    }
}
