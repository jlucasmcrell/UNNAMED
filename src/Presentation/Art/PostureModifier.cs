// UNNAMED Presentation - a crouch and a jump's tuck bent into whatever clip is playing (a placeholder: the clip set has neither)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// Bends a humanoid skeleton after its clip has posed it: the pelvis drops and the thighs come forward and the shins back as the
/// body crouches, and the knees tuck while it is in the air (the simulation's posture, the owner's M6 playtest). The asset set has
/// no crouch or jump clips; this stands in until it does, so a crouched walk is the walk clip, bent.
/// </summary>
public sealed partial class PostureModifier : SkeletonModifier3D
{
    private int _pelvis = -1;
    private int[] _thighs = Array.Empty<int>();
    private int[] _shins = Array.Empty<int>();

    /// <summary>0 standing to 1 crouched, eased by the owner.</summary>
    public float Crouch { get; set; }

    /// <summary>0 to 1 in the air.</summary>
    public float Tuck { get; set; }

    /// <summary>Find the rig's bones by name; false (and no effect) when the rig lacks them.</summary>
    public bool Bind(Skeleton3D skeleton, string pelvis, string[] thighs, string[] shins)
    {
        _pelvis = skeleton.FindBone(pelvis);
        _thighs = thighs.Select(skeleton.FindBone).Where(b => b >= 0).ToArray();
        _shins = shins.Select(skeleton.FindBone).Where(b => b >= 0).ToArray();
        return _pelvis >= 0 && _thighs.Length == 2 && _shins.Length == 2;
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        if (GetSkeleton() is not { } skeleton || _pelvis < 0 || (Crouch < 0.001f && Tuck < 0.001f))
            return;
        // The body faces +Z: a thigh swung forward turns about -X, a shin folding back turns about +X.
        var pelvis = skeleton.GetBoneGlobalPose(_pelvis);
        pelvis.Origin -= new Vector3(0, 0.34f * Crouch, 0);
        pelvis.Basis = new Basis(Vector3.Right, 0.22f * Crouch) * pelvis.Basis;
        skeleton.SetBoneGlobalPose(_pelvis, pelvis);
        float thigh = -(1.05f * Crouch + 0.95f * Tuck);
        float shin = 1.75f * Crouch + 1.6f * Tuck;
        foreach (int bone in _thighs)
            Turn(skeleton, bone, thigh);
        foreach (int bone in _shins)
            Turn(skeleton, bone, shin);
    }

    /// <summary>Turn a bone about the skeleton's side axis, at its own origin (its children follow).</summary>
    private static void Turn(Skeleton3D skeleton, int bone, float radians)
    {
        var pose = skeleton.GetBoneGlobalPose(bone);
        pose.Basis = new Basis(Vector3.Right, radians) * pose.Basis;
        skeleton.SetBoneGlobalPose(bone, pose);
    }
}
