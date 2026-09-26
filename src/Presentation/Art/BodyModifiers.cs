// UNNAMED Presentation - Godot's built-in skeleton modifiers on our bodies: feet planted on the ground, a head that looks, a tail that trails (Phase B, B0.4)
// Godot presentation only (D-11): bones are posed after the clip plays; the body's position, its collision and every rule stay the simulation's

using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// The engine's own modifiers set up on a skinned body: <see cref="TwoBoneIK3D"/> for the legs (fed by <see cref="FootPlanting"/>, which
/// finds the ground under each animated foot and lowers the hips for the lower one), <see cref="LookAtModifier3D"/> for the head, and
/// <see cref="SpringBoneSimulator3D"/> for a trailing bone. Modifiers run in the skeleton's child order after the clip is applied, so the
/// planting is added before the IK that reads its targets. Bone names are the 20-bone humanoid and 18-bone quadruped rigs' own.
/// </summary>
public static class BodyModifiers
{
    /// <summary>Plant a humanoid's feet on <paramref name="ground"/> (height at x, z in the world); null when the rig lacks the leg bones.</summary>
    public static FootPlanting? PlantFeet(Skeleton3D skeleton, Node3D body, Func<float, float, float> ground)
    {
        string[] bones = { "hips", "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R" };
        if (bones.Any(b => skeleton.FindBone(b) < 0))
            return null;
        var planting = new FootPlanting { Name = "FootPlanting", Body = body, Ground = ground };
        skeleton.AddChild(planting);
        var ik = new TwoBoneIK3D { Name = "LegIK" };
        skeleton.AddChild(ik);
        ik.SetSettingCount(2);
        for (int side = 0; side < 2; side++)
        {
            string s = side == 0 ? "L" : "R";
            var target = new Node3D { Name = "FootTarget." + s };
            var pole = new Node3D { Name = "KneePole." + s };
            skeleton.AddChild(target);
            skeleton.AddChild(pole);
            ik.SetRootBoneName(side, "thigh." + s);
            ik.SetMiddleBoneName(side, "shin." + s);
            ik.SetEndBoneName(side, "foot." + s);
            ik.SetTargetNode(side, ik.GetPathTo(target));
            ik.SetPoleNode(side, ik.GetPathTo(pole));
            planting.Targets[side] = target;
            planting.Poles[side] = pole;
        }
        planting.Ik = ik;
        planting.Bind(skeleton);
        return planting;
    }

    /// <summary>Turn the head toward <paramref name="target"/> within a comfortable range (the face's axis found from the rest pose); null without a head bone.</summary>
    public static LookAtModifier3D? LookAt(Skeleton3D skeleton, Node3D body, Node3D target, string bone = "head")
    {
        int head = skeleton.FindBone(bone);
        if (head < 0)
            return null;
        var look = new LookAtModifier3D
        {
            Name = "HeadLook", BoneName = bone, ForwardAxis = FaceAxis(skeleton, body, head), UseAngleLimitation = true, SymmetryLimitation = true,
            PrimaryLimitAngle = Mathf.DegToRad(140), SecondaryLimitAngle = Mathf.DegToRad(70), Duration = 0.35f,
        };
        skeleton.AddChild(look);
        look.TargetNode = look.GetPathTo(target);
        return look;
    }

    /// <summary>
    /// The eyes (the production rig's <c>eye.L</c> / <c>eye.R</c>) turned toward <paramref name="target"/> within an eye's range, on top of the
    /// head's own look; empty on a rig without eye bones.
    /// </summary>
    public static IReadOnlyList<LookAtModifier3D> EyesLook(Skeleton3D skeleton, Node3D body, Node3D target)
    {
        var looks = new List<LookAtModifier3D>();
        foreach (string bone in new[] { "eye.L", "eye.R" })
        {
            int eye = skeleton.FindBone(bone);
            if (eye < 0)
                continue;
            var look = new LookAtModifier3D
            {
                Name = "EyeLook." + bone[^1], BoneName = bone, ForwardAxis = FaceAxis(skeleton, body, eye), UseAngleLimitation = true,
                SymmetryLimitation = true, PrimaryLimitAngle = Mathf.DegToRad(60), SecondaryLimitAngle = Mathf.DegToRad(40), Duration = 0.08f,
            };
            skeleton.AddChild(look);
            look.TargetNode = look.GetPathTo(target);
            looks.Add(look);
        }
        return looks;
    }

    /// <summary>A hand's fingers curled closed (around a held weapon) or relaxed; null on a rig without finger bones for that hand.</summary>
    public static HandGrip? Grip(Skeleton3D skeleton, string handBone)
    {
        var grip = new HandGrip { Name = "Grip." + handBone };
        if (!grip.Bind(skeleton, handBone))
        {
            grip.Free();
            return null;
        }
        skeleton.AddChild(grip);
        return grip;
    }

    /// <summary>A single trailing bone (a tail) made springy: it lags and swings as the body turns and moves; null without the bone.</summary>
    public static SpringBoneSimulator3D? Spring(Skeleton3D skeleton, string bone, float length, float stiffness = 0.6f, float drag = 0.35f, float gravity = 0.4f)
    {
        if (skeleton.FindBone(bone) < 0)
            return null;
        var spring = new SpringBoneSimulator3D { Name = "Spring." + bone };
        skeleton.AddChild(spring);
        spring.SetSettingCount(1);
        spring.SetRootBoneName(0, bone);
        spring.SetEndBoneName(0, bone);
        spring.SetExtendEndBone(0, true);
        spring.SetEndBoneDirection(0, SkeletonModifier3D.BoneDirection.PlusY);
        spring.SetEndBoneLength(0, length);
        spring.SetStiffness(0, stiffness);
        spring.SetDrag(0, drag);
        spring.SetGravity(0, gravity);
        return spring;
    }

    /// <summary>The head bone's axis that points where the body faces (its +Z), from the rest pose.</summary>
    private static SkeletonModifier3D.BoneAxis FaceAxis(Skeleton3D skeleton, Node3D body, int head)
    {
        var forward = (skeleton.GlobalTransform.Basis.Inverse() * body.GlobalTransform.Basis.Z).Normalized();
        var local = (skeleton.GetBoneGlobalRest(head).Basis.Inverse() * forward).Normalized();
        var abs = local.Abs();
        if (abs.X >= abs.Y && abs.X >= abs.Z)
            return local.X > 0 ? SkeletonModifier3D.BoneAxis.PlusX : SkeletonModifier3D.BoneAxis.MinusX;
        if (abs.Y >= abs.Z)
            return local.Y > 0 ? SkeletonModifier3D.BoneAxis.PlusY : SkeletonModifier3D.BoneAxis.MinusY;
        return local.Z > 0 ? SkeletonModifier3D.BoneAxis.PlusZ : SkeletonModifier3D.BoneAxis.MinusZ;
    }
}

/// <summary>
/// Before the leg IK: where each animated foot is, the ground under it, and a target the same height above that ground as the clip holds
/// the foot above the body's own ground point - so a planted foot stands on a slope and a lifted one clears it by as much as on the flat.
/// The hips come down (smoothly) by as much as the lower foot must reach, so a leg is never asked to stretch; the knees are pointed ahead.
/// </summary>
public partial class FootPlanting : SkeletonModifier3D
{
    public Node3D Body { get; set; } = null!;
    public Func<float, float, float> Ground { get; set; } = (_, _) => 0;
    public Node3D[] Targets { get; } = new Node3D[2];
    public Node3D[] Poles { get; } = new Node3D[2];

    /// <summary>The most a foot is raised above its clip's height (a step up), and dropped with the hips (a step down).</summary>
    public float MaxStep { get; set; } = 0.45f;

    /// <summary>The hips' drop this frame, in metres (negative is down), for the reports.</summary>
    public float Drop { get; private set; }

    /// <summary>The leg IK this feeds: its influence follows the planting's weight (none while the body is off the ground).</summary>
    public TwoBoneIK3D? Ik { get; set; }

    private float _weight = 1f;

    private int _hips, _hipsParent;
    private readonly int[] _feet = new int[2], _shins = new int[2];

    public void Bind(Skeleton3D skeleton)
    {
        _hips = skeleton.FindBone("hips");
        _hipsParent = skeleton.GetBoneParent(_hips);
        _feet[0] = skeleton.FindBone("foot.L");
        _feet[1] = skeleton.FindBone("foot.R");
        _shins[0] = skeleton.FindBone("shin.L");
        _shins[1] = skeleton.FindBone("shin.R");
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        if (GetSkeleton() is not { } skeleton || Body is null)
            return;
        var toWorld = skeleton.GlobalTransform;
        float baseY = Ground(Body.GlobalPosition.X, Body.GlobalPosition.Z);
        // Off the ground (a jump, a fall) or lying down, the clip keeps its legs: the planting fades out, and back in on landing.
        bool grounded = Body.GlobalPosition.Y - baseY < 0.12f && Mathf.Abs(Body.Rotation.X) < 0.3f;
        _weight = delta <= 0 ? (grounded ? 1f : 0f) : Mathf.MoveToward(_weight, grounded ? 1f : 0f, (float)delta * 6f);
        if (Ik is not null)
            Ik.Influence = _weight;
        if (_weight <= 0.001f)
            return;
        Span<Vector3> feet = stackalloc Vector3[2];
        Span<float> rise = stackalloc float[2];
        for (int i = 0; i < 2; i++)
        {
            feet[i] = toWorld * skeleton.GetBoneGlobalPose(_feet[i]).Origin;
            rise[i] = Math.Clamp(Ground(feet[i].X, feet[i].Z) - baseY, -MaxStep, MaxStep);
        }
        float drop = Math.Min(0, Math.Min(rise[0], rise[1])) * _weight;
        Drop = delta <= 0 ? drop : Mathf.Lerp(Drop, drop, 1f - MathF.Exp(-14f * (float)delta));
        // The hips down by the drop (a world-space offset carried into the hips' parent's space).
        var down = toWorld.Basis.Inverse() * new Vector3(0, Drop, 0);
        if (_hipsParent >= 0)
            down = skeleton.GetBoneGlobalPose(_hipsParent).Basis.Inverse() * down;
        skeleton.SetBonePosePosition(_hips, skeleton.GetBonePosePosition(_hips) + down);
        var ahead = Body.GlobalTransform.Basis.Z.Normalized();
        for (int i = 0; i < 2; i++)
        {
            Targets[i].GlobalPosition = feet[i] + new Vector3(0, rise[i], 0);
            Poles[i].GlobalPosition = toWorld * skeleton.GetBoneGlobalPose(_shins[i]).Origin + ahead;
        }
    }
}

/// <summary>
/// A hand's finger bones curled about their bend axis on top of the clip: <see cref="Closed"/> 1 is a grip around a haft, 0 the clip's
/// own fingers. The production rig (tools/asset_pipeline/charprod/rig_production.py) rolls every finger bone so its local X is the bend
/// axis and a grip is a negative turn about it.
/// </summary>
public partial class HandGrip : SkeletonModifier3D
{
    /// <summary>How closed the hand is asked to be (0..1); the fingers ease there.</summary>
    public float Closed { get; set; }

    private float _now;
    private readonly List<(int Bone, float Angle)> _bones = new();

    // A grip's curl per segment, in degrees: the fingers' knuckle, middle and end joints; the thumb curls less.
    private static readonly float[] Finger = { 62f, 78f, 48f };
    private static readonly float[] Thumb = { 12f, 32f, 28f };

    public bool Bind(Skeleton3D skeleton, string handBone)
    {
        string side = handBone.Length > 2 && handBone[^2] == '.' ? handBone[^1..] : handBone.EndsWith("_r", StringComparison.Ordinal) ? "R" : "L";
        foreach (string name in new[] { "thumb", "index", "middle", "ring", "pinky" })
        {
            for (int k = 0; k < 3; k++)
            {
                int bone = skeleton.FindBone($"{name}_0{k + 1}.{side}");
                if (bone >= 0)
                    _bones.Add((bone, Mathf.DegToRad(name == "thumb" ? Thumb[k] : Finger[k])));
            }
        }
        return _bones.Count > 0;
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        if (GetSkeleton() is not { } skeleton)
            return;
        _now = delta <= 0 ? Closed : Mathf.MoveToward(_now, Closed, (float)delta * 6f);
        if (_now <= 0.001f)
            return;
        foreach (var (bone, angle) in _bones)
            skeleton.SetBonePoseRotation(bone, skeleton.GetBonePoseRotation(bone) * new Quaternion(Vector3.Right, -angle * _now));
    }
}
