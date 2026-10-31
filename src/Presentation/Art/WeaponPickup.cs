using Godot;
namespace UNNAMED.Presentation.Art;

/// <summary>A collected native prop stays on the floor until the recorded reach meets its real grip socket.</summary>
internal sealed partial class WeaponPickup : Node3D
{
    private Node3D? _prop;
    private Node3D _target = null!, _pole = null!;
    private Skeleton3D _rig = null!;
    private BoneAttachment3D _hand = null!;
    private TwoBoneIK3D _ik = null!;
    private GripRotation _rotation = null!;
    private HandGrip? _fingers;
    private Transform3D _grip, _socket, _handWorld;
    private Action? _finished;
    private float _scale, _contact, _through, _weight;
    private int _upper, _forearm, _wrist;
    private bool _ending, _graspQueued;
    public bool Grasped { get; private set; }
    public float ContactError { get; private set; } = float.PositiveInfinity;
    public float Facing { get; private set; }

    public bool Start(SkinnedFigure body, Skeleton3D rig, BoneAttachment3D hand, Transform3D grip,
        Node3D prop, Transform3D socket, HandGrip? fingers, float scale,
        (float Fraction, Vector3 Hand) contact, Action finished)
    {
        _upper = BoneNames.Find(rig, "upper_arm.R");
        _forearm = BoneNames.Find(rig, "forearm.R");
        _wrist = BoneNames.Find(rig, "hand.R");
        if (_upper < 0 || _forearm < 0 || _wrist < 0) return false;
        _rig=rig; _hand=hand; _grip=grip; _socket=socket; _fingers=fingers;
        _scale=scale; _contact=contact.Fraction; _finished=finished;
        var resting=prop.HasMeta("pickup_rest")?prop.GetMeta("pickup_rest").AsTransform3D():prop.GlobalTransform;
        _handWorld=resting*socket
            *new Transform3D(Basis.FromScale(Vector3.One/scale),Vector3.Zero)*grip.AffineInverse();
        var toward=_handWorld.Origin-body.GlobalPosition;
        Facing=toward.LengthSquared()>.0025f
            ? Mathf.Atan2(toward.X,toward.Z)-Mathf.Atan2(contact.Hand.X,contact.Hand.Z) : body.Rotation.Y;
        _prop=prop; prop.Reparent(this,keepGlobalTransform:true);
        if(prop.HasMeta("pickup_rest"))
        {
            prop.RemoveMeta("pickup_rest");
            var airborne=prop.GlobalTransform;
            float seconds=Mathf.Clamp(Mathf.Sqrt(2*MathF.Max(.01f,airborne.Origin.Y-resting.Origin.Y)/9.8f),.1f,.4f);
            prop.CreateTween().TweenMethod(Callable.From<float>(t=>prop.GlobalTransform=airborne.InterpolateWith(resting,t)),0f,1f,seconds);
        }
        _target=new Node3D {Name="GripTarget"};AddChild(_target);
        _pole=new Node3D {Name="ElbowPole"};AddChild(_pole);
        _ik=new TwoBoneIK3D {Name="PickupArm",Influence=0};rig.AddChild(_ik);
        _ik.SetSettingCount(1);
        _ik.SetRootBoneName(0,rig.GetBoneName(_upper));
        _ik.SetMiddleBoneName(0,rig.GetBoneName(_forearm));
        _ik.SetEndBoneName(0,rig.GetBoneName(_wrist));
        _ik.SetTargetNode(0,_ik.GetPathTo(_target));_ik.SetPoleNode(0,_ik.GetPathTo(_pole));
        _rotation=new GripRotation {Name="PickupWrist",Pickup=this};rig.AddChild(_rotation);
        return true;
    }

    public void Step(float through, bool active)
    {
        if(_ending)return;
        _through=through;
        if(through>=1 || (!active && through>.03f)){Finish();return;}
        _weight=through<_contact ? Mathf.SmoothStep(0,1,Mathf.Clamp((through-(_contact-.18f))/.18f,0,1))
            : 1-Mathf.SmoothStep(0,1,Mathf.Clamp((through-_contact)/.20f,0,1));
        _ik.Influence=_weight;
        _target.GlobalTransform=_handWorld;
        Vector3 At(int bone)=>_rig.GlobalTransform*_rig.GetBoneGlobalPose(bone).Origin;
        var upper=At(_upper);var elbow=At(_forearm);var wrist=At(_wrist);
        var line=wrist-upper;
        var bend=elbow-upper-line*Mathf.Clamp((elbow-upper).Dot(line)/Mathf.Max(line.LengthSquared(),.0001f),0,1);
        if(bend.LengthSquared()<.0001f)bend=-GlobalBasis.X;
        _pole.GlobalPosition=elbow+bend.Normalized()*.3f;
        if(_fingers is not null)_fingers.Closed=Grasped?1:Mathf.Lerp(.32f,1,_weight);
    }

    private void OrientAndGrasp()
    {
        if(_ending || _weight<=0)return;
        int parent=_rig.GetBoneParent(_wrist);
        var parentBasis=parent<0?Basis.Identity:_rig.GetBoneGlobalPose(parent).Basis;
        var local=parentBasis.Inverse()*_rig.GlobalBasis.Inverse()*_handWorld.Basis;
        _rig.SetBonePoseRotation(_wrist,_rig.GetBonePoseRotation(_wrist).Slerp(local.Orthonormalized().GetRotationQuaternion(),_weight));
        if(!Grasped)ContactError=(_rig.GlobalTransform*_rig.GetBoneGlobalPose(_wrist).Origin).DistanceTo(_handWorld.Origin);
        if(!Grasped && !_graspQueued && _through>=_contact && _weight>.95f && ContactError<.03f)
        {
            _graspQueued=true;
            Callable.From(Grasp).CallDeferred();
        }
    }

    private void Grasp()
    {
        if(_ending || _prop is null || !IsInstanceValid(_prop))return;
        if((_prop.GlobalTransform*_socket).Origin.DistanceTo((_hand.GlobalTransform*_grip).Origin)>.035f)
        {
            _graspQueued=false;
            return;
        }
        _prop.Reparent(_hand,keepGlobalTransform:true);
        _prop.Transform=_grip*new Transform3D(Basis.FromScale(Vector3.One*_scale),Vector3.Zero)*_socket.AffineInverse();
        foreach(var mesh in _prop.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>().Prepend(_prop as MeshInstance3D).OfType<MeshInstance3D>())
            mesh.Layers|=MHCharacters.MetaHumanFigure.FillLayer;
        Grasped=true;
    }

    public void Finish()
    {
        if(_ending)return;
        _ending=true;
        if(IsInstanceValid(_ik))_ik.Active=false;
        if(IsInstanceValid(_rotation))_rotation.Active=false;
        _finished?.Invoke();_finished=null;
        QueueFree();
    }

    public override void _ExitTree()
    {
        if(IsInstanceValid(_prop))_prop!.QueueFree();
        if(IsInstanceValid(_ik))_ik.QueueFree();
        if(IsInstanceValid(_rotation))_rotation.QueueFree();
    }

    private sealed partial class GripRotation : SkeletonModifier3D
    {
        public WeaponPickup Pickup=null!;
        public override void _ProcessModificationWithDelta(double delta)=>Pickup.OrientAndGrasp();
    }
}
