using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>A small seated supper gesture using the same built-in arm IK as foot planting.
/// No hunger simulation: the authored evening schedule decides when a meal is shown.</summary>
public partial class SeatedMeal : Node3D
{
    private Skeleton3D _skeleton = null!;
    private TwoBoneIK3D _ik = null!;
    private Node3D _target = null!, _pole = null!;
    private Node3D _cup = null!, _bread = null!;
    private int _head;
    private double _phase;

    public static SeatedMeal? Attach(SkinnedFigure figure, ArtLibrary art, string identity)
    {
        var skeleton = figure.Skeleton;
        string[] bones = ["upper_arm.R", "forearm.R", "hand.R", "head"];
        if (bones.Any(b => BoneNames.Find(skeleton, b) < 0)) return null;
        uint seed = 0;
        foreach (char c in identity) seed = seed * 31 + c;
        var meal = new SeatedMeal { Name = "Supper", _skeleton = skeleton,
            _head = BoneNames.Find(skeleton, "head"), _phase = seed % 1400 / 100.0 };
        figure.AddChild(meal);
        meal._target = new Node3D(); meal._pole = new Node3D();
        skeleton.AddChild(meal._target); skeleton.AddChild(meal._pole);
        meal._ik = new TwoBoneIK3D { Name = "SupperArm", Active = false };
        skeleton.AddChild(meal._ik);
        meal._ik.SetSettingCount(1);
        meal._ik.SetRootBoneName(0, BoneNames.Resolve(skeleton, bones[0])!);
        meal._ik.SetMiddleBoneName(0, BoneNames.Resolve(skeleton, bones[1])!);
        meal._ik.SetEndBoneName(0, BoneNames.Resolve(skeleton, bones[2])!);
        meal._ik.SetTargetNode(0, meal._ik.GetPathTo(meal._target));
        meal._ik.SetPoleNode(0, meal._ik.GetPathTo(meal._pole));
        var hand = new BoneAttachment3D { BoneName = BoneNames.Resolve(skeleton, "hand.R")! };
        skeleton.AddChild(hand);
        meal._cup = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .045f,
            BottomRadius = .035f, Height = .11f }, MaterialOverride = art.WorldMaterial("cw_timber_beam") };
        meal._bread = new MeshInstance3D { Mesh = new SphereMesh { Radius = .035f, Height = .05f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.45f, .25f, .10f), Roughness = 1 } };
        hand.AddChild(meal._cup); hand.AddChild(meal._bread);
        meal._cup.Visible = meal._bread.Visible = false;
        return meal;
    }

    public void Draw(bool dining, Vector3 forward, double delta)
    {
        _ik.Active = dining;
        if (!dining) { _cup.Visible = _bread.Visible = false; return; }
        _phase += delta;
        double phase = _phase % 14;
        bool drinking = phase >= 7;
        float reach = Mathf.SmoothStep(0, 1, (float)(Math.Sin((phase % 7) / 7 * Math.PI) * .95));
        var head = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_head).Origin;
        var resting = head + forward * .34f + Vector3.Down * .52f;
        var mouth = head + forward * .10f + Vector3.Down * .16f;
        _target.GlobalPosition = resting.Lerp(mouth, reach);
        _pole.GlobalPosition = head + forward.Cross(Vector3.Up) * .45f + Vector3.Down * .45f;
        _cup.Visible = drinking;
        _bread.Visible = !drinking;
    }
}
