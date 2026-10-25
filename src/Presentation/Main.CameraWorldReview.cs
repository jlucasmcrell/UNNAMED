using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Opt-in evidence: normal controller walking and the actual player SpringArm.
    private async Task RunCameraWorldReview(string directory)
    {
        _rcd3Driving=true;
        var sim=_session.Simulation ?? throw new InvalidDataException("No camera review world");
        _camera.TargetDistance=4;
        var phases=new List<object>();
        void Mark(string phase)=>phases.Add(new {phase,uptime_ms=Time.GetTicksMsec(),body=_controller.Authoritative,
            camera=_camera.Camera.GlobalPosition.ToString(),engine_max_fps=Engine.MaxFps});
        async Task Go(double x,double z)
        {
            ulong deadline=Time.GetTicksMsec()+60000;
            var goal=new Body((long)Math.Round(x*1000),0,(long)Math.Round(z*1000),0);
            foreach(var corner in Playthrough.WayTo(sim,_controller.Authoritative,goal,0))
                while(true)
                {
                    var body=_controller.Authoritative;
                    var to=new Vector3((float)(corner.X-body.XMm/1000.0),0,(float)(corner.Z-body.ZMm/1000.0));
                    if(to.Length()<.15f)break;
                    if(Time.GetTicksMsec()>deadline)throw new TimeoutException("Camera route blocked at "+body);
                    _camera.Yaw=Mathf.Atan2(-to.X,-to.Z);_camera.Pitch=-.25f;
                    _controller.SteerWorld(to.Normalized(),Gait.Walk,_camera);
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);await Rcd3Wait(.5);
        }
        await Rcd3Wait(3);
        DisplayServer.WindowSetTitle("Otherreach - camera world collision review");
        File.WriteAllText(Path.Combine(directory,"record_ready.json"),"{}");
        ulong recordDeadline=Time.GetTicksMsec()+120000;
        while(!File.Exists(Path.Combine(directory,"recording.started")))
        {
            if(Time.GetTicksMsec()>recordDeadline)throw new TimeoutException("Camera WGC recorder not ready");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
        int bus=AudioServer.GetBusIndex("Master"),effectIndex=AudioServer.GetBusEffectCount(bus);
        using var recording=new AudioEffectRecord();AudioServer.AddBusEffect(bus,recording);
        recording.SetRecordingActive(true);
        using var sphere=new SphereShape3D {Radius=.18f};
        using var frames=new StreamWriter(Path.Combine(directory,"camera_frames.csv"));
        frames.WriteLine("usec,site,shoulder,pitch,yaw,camera_x,camera_y,camera_z,hit_length");
        try
        {
            await Go(425.1,758);
            Mark("normal_touch");_controller.TouchStone("waystone.ashen");await Rcd3Wait(3);CloseScreens();
            Mark("normal_bind");_controller.BindStone("waystone.ashen");await Rcd3Wait(3);CloseScreens();
            foreach(string family in new[]{"sfx.travel.waystone.touch","sfx.travel.waystone.bind"})
                if(!_sounds.Resolved.Contains(family))throw new InvalidDataException("Normal Waystone event did not resolve "+family);
            // Arrival is an explicit sound audition; this does not claim a TravelCommand journey.
            Mark("arrival_sound_audition");
            if(!_sounds.Play("sfx.travel.waystone.arrive",_avatar.GlobalPosition))throw new InvalidDataException("Arrival sound absent");
            await Rcd3Wait(3);
            foreach(var site in new[]{("forge_wall",452.55,740.0),("rock_well",447.55,751.0)})
            {
                await Go(site.Item2,site.Item3);Mark(site.Item1);
                foreach(var shoulder in new[]{Shoulder.Right,Shoulder.Left,Shoulder.Center})
                foreach(float pitch in new[]{-.8f,-.3f,.2f})
                {
                    _camera.Side=shoulder;_camera.Pitch=pitch;_camera.Yaw=0;await Rcd3Wait(.3);
                    ulong start=Time.GetTicksMsec();
                    while(Time.GetTicksMsec()-start<6000)
                    {
                        _camera.Yaw=(Time.GetTicksMsec()-start)/6000f*Mathf.Tau;
                        await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                        var position=_camera.Camera.GlobalPosition;
                        var query=new PhysicsShapeQueryParameters3D {Shape=sphere,CollisionMask=HollowView.CameraCollisionLayer,
                            Transform=new Transform3D(Basis.Identity,position)};
                        if(GetWorld3D().DirectSpaceState.IntersectShape(query).Count>0)
                            throw new InvalidDataException($"World camera overlaps geometry at {site.Item1}/{shoulder}/{pitch}: {position}");
                        var sight=PhysicsRayQueryParameters3D.Create(_camera.GlobalPosition,position,HollowView.CameraCollisionLayer);
                        if(GetWorld3D().DirectSpaceState.IntersectRay(sight).Count>0)
                            throw new InvalidDataException($"World camera passed through geometry at {site.Item1}/{shoulder}/{pitch}: {position}");
                        frames.WriteLine(FormattableString.Invariant($"{Time.GetTicksUsec()},{site.Item1},{shoulder},{pitch},{_camera.Yaw},{position.X},{position.Y},{position.Z},{_camera.EffectiveDistance}"));
                    }
                    await Rcd3Shot(directory,$"{site.Item1}_{shoulder}_{pitch:0.0}");
                }
            }
            File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
                status="NATIVE_WORLD_COLLISION_PASS_VISUAL_AUDITION_PENDING",phases,
                audio_resolved=_sounds.Resolved,audio_errors=_sounds.Errors,
                note="Normal touch and bind; arrival sound audition only. Normal controller walk, real camera, unchanged world/scatter."},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            recording.SetRecordingActive(false);recording.GetRecording()?.SaveToWav(Path.Combine(directory,"game_master.wav"));
            AudioServer.RemoveBusEffect(bus,effectIndex);File.WriteAllText(Path.Combine(directory,"recording.finished"),"done");
        }
        ulong stopped=Time.GetTicksMsec()+15000;
        while(!File.Exists(Path.Combine(directory,"recording.stopped")) && Time.GetTicksMsec()<stopped)
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    }
}
