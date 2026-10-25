using Godot;
using System.Text.Json;
using System.Security.Cryptography;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Player;
using UNNAMED.Domain.Spatial;
namespace UNNAMED.Presentation;
public partial class Main
{
    // Actual creator-selected player and ordinary controller commands; only the final jaw fixture is posed.
    private async Task RunMateoTeethReview(string directory,Camera3D gameplay)
    {
        var player=_avatar as SkinnedFigure ?? throw new InvalidDataException("Expected native HumGen player");
        bool feminine=OS.GetEnvironment("OTHERREACH_CREATOR_BASE_REVIEW")=="female";
        string expected=feminine?"humgen_player_ada_f":"humgen_player_mateo_m";
        if(player.ModelId!=expected)throw new InvalidDataException("Not the creator-selected base body");
        var face=player.FindChildren("*","",true,false).OfType<FaceDriver>().Single();
        var lifts=player.FindChildren("*","",true,false).OfType<HumGenHeadLevel>().ToArray();
        if(lifts.Length!=1 || lifts[0].Influence<.99f)throw new InvalidDataException("Head lift must be enabled");
        string body=_art.RiggedFile(player.ModelId,player.ModelId+"_rigged.glb") ?? throw new FileNotFoundException("Mateo body");
        string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(body))).ToLowerInvariant();
        _rcd3Driving=true;_innFreeCamera=false;_camera.TargetDistance=3.2f;_camera.Side=Shoulder.Left;_camera.Yaw=0;_camera.Pitch=-.2f;
        var phases=new List<object>();
        File.WriteAllText(Path.Combine(directory,"record_ready.json"),"{}");
        ulong deadline=Time.GetTicksMsec()+120000;
        while(!File.Exists(Path.Combine(directory,"recording.started")))
        {if(Time.GetTicksMsec()>deadline)throw new TimeoutException("Mateo recorder");await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Phase(string name,double seconds,Vector3 direction,Gait gait,Action<double>? frame=null)
        {
            var start=player.GlobalPosition;ulong began=Time.GetTicksMsec();var utc=DateTimeOffset.UtcNow;
            while(Time.GetTicksMsec()-began<seconds*1000)
            {
                _controller.SteerWorld(direction,gait,_camera);frame?.Invoke((Time.GetTicksMsec()-began)/1000.0);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            phases.Add(new {name,utc,until=DateTimeOffset.UtcNow,seconds=(Time.GetTicksMsec()-began)/1000.0,
                travelled_m=player.GlobalPosition.DistanceTo(start),clip=player.ClipState,position=player.GlobalPosition.ToString(),
                jaw=face.JawWeight,head_lift_influence=lifts[0].Influence,
                teeth=player.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>()
                    .Where(m=>m.Name.ToString().Contains("Teeth",StringComparison.OrdinalIgnoreCase))
                    .Select(m=>new {name=m.Name.ToString(),visible=m.IsVisibleInTree()}).ToArray()});
        }
        Camera3D? close=null;
        try
        {
            await Phase("normal_idle_rear_left",3,Vector3.Zero,Gait.Walk);
            await Phase("normal_walk_rear_left",5,Vector3.Forward,Gait.Walk);
            await Phase("normal_run_rear_left",5,Vector3.Forward,Gait.Run);
            await Phase("normal_stop",1,Vector3.Zero,Gait.Walk);
            await Phase("normal_camera_look_up_down",6,Vector3.Zero,Gait.Walk,t=>_camera.Pitch=.65f*Mathf.Sin((float)t*Mathf.Tau/6));
            _camera.Pitch=-.15f;_camera.Yaw=.4f;_camera.TargetDistance=2;
            _controller.Attack();await Phase("normal_swing_rear_left_shoulder",3,Vector3.Zero,Gait.Walk);
            close=new Camera3D {Fov=32,Near=gameplay.Near,Far=gameplay.Far,CullMask=gameplay.CullMask,Attributes=gameplay.Attributes,Environment=gameplay.Environment};
            AddChild(close);gameplay.GetNode<OmniLight3D>("CharacterFill").Reparent(close,false);_innFreeCamera=true;
            var rig=player.Skeleton;int l=rig.FindBone("eye.L"),r=rig.FindBone("eye.R"),h=rig.FindBone("Head");
            if(l<0 || r<0 || h<0)throw new InvalidDataException("Native facial bones missing");
            void Near(double t)
            {
                var eyes=rig.GlobalTransform*((rig.GetBoneGlobalPose(l).Origin+rig.GetBoneGlobalPose(r).Origin)*.5f);
                var head=rig.GlobalTransform*rig.GetBoneGlobalPose(h).Origin;var ahead=eyes-head;ahead.Y=0;
                if(ahead.LengthSquared()<.00001f)throw new InvalidDataException("Cannot derive actual facial direction");
                var aim=eyes-Vector3.Up*.07f;close.GlobalPosition=aim+ahead.Normalized()*.7f+Vector3.Up*(float)(.3*Math.Sin(t*Math.Tau/6));
                close.LookAt(aim);close.MakeCurrent();
            }
            face.ReviewJawOpen=0;await Phase("prepared_jaw_closed_front",2,Vector3.Zero,Gait.Walk,Near);await Rcd3Shot(directory,"jaw_closed");
            face.ReviewJawOpen=.65f;await Phase("prepared_jaw_open_throat_view_sweep",6,Vector3.Zero,Gait.Walk,Near);await Rcd3Shot(directory,"jaw_open_throat");
            File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {status="CREATED_PLAYER_BASE_REQUIRES_OWNER_REVIEW",model=player.ModelId,body,hash,head_lift=true,created_name=_session.Simulation!.Player.Identity.Name,
                phases,limits="Actual normal creator/controller idle/walk/run/camera look/swing. Final two phases are explicitly prepared jaw-open fixtures, not player speech. Shoulder unchanged; DeepSeek owns its fix."},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            face.ReviewJawOpen=null;_controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);_innFreeCamera=false;
            if(close is not null){close.GetNode<OmniLight3D>("CharacterFill").Reparent(gameplay,false);close.QueueFree();}
            gameplay.MakeCurrent();File.WriteAllText(Path.Combine(directory,"recording.finished"),"done");
        }
        deadline=Time.GetTicksMsec()+15000;
        while(!File.Exists(Path.Combine(directory,"recording.stopped")) && Time.GetTicksMsec()<deadline)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    }
}
