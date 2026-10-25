using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>Reconnect the paid rig's lower-teeth jaw to its FACS mouth, without changing its rest or skin.</summary>
public partial class HumGenJaw : SkeletonModifier3D
{
    public Func<float> OpenWeight { get; set; } = () => 0;
    public float AppliedWeight { get; private set; }
    public float AppliedAngle { get; private set; }
    public Quaternion PoseBefore { get; private set; }
    public Quaternion PoseAfter { get; private set; }

    public override void _ProcessModificationWithDelta(double delta)
    {
        var skeleton = GetSkeleton();
        int jaw = skeleton.FindBone("Jaw");
        if (jaw < 0) return;
        float weight = Mathf.Clamp(OpenWeight(), 0, 1);
        // The add-on's jaw Damped Track follows lip_lower_vg_b. Sampled through
        // its own jaw_open_lt_rt_frwd control at FACS 0/.25/.5/1; see
        // tools/animation/inspect_humgen_face_controls.py. Native local X hinge,
        // approximately 14.33 degrees at full opening; no translation required.
        float angle = weight <= .25f ? Mathf.Lerp(0, .0614438f, weight * 4)
            : weight <= .5f ? Mathf.Lerp(.0614438f, .1239398f, (weight - .25f) * 4)
            : Mathf.Lerp(.1239398f, .2501659f, (weight - .5f) * 2);
        PoseBefore = skeleton.GetBonePoseRotation(jaw);
        PoseAfter = PoseBefore * new Quaternion(Vector3.Right, angle);
        skeleton.SetBonePoseRotation(jaw, PoseAfter);
        AppliedWeight = weight;
        AppliedAngle = angle;
    }
}
