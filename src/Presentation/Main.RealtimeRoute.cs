using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Diagnostic input driver only: unchanged simulation pacing, renderer and movement commands.
    private async Task RunRcd6RealtimeRoute(string directory)
    {
        _rcd3Driving=true;
        var simulation=_session.Simulation ?? throw new InvalidOperationException("World missing");
        string arm=OS.GetEnvironment("OTHERREACH_VISIBILITY_ARM");
        var scatter=_scatter.FindChildren("*",nameof(MultiMeshInstance3D),true,false).OfType<MultiMeshInstance3D>().ToArray();
        foreach(var n in scatter)
        {
            if(arm=="scatter_self_fade")
            {
                n.VisibilityRangeBeginMargin=n.VisibilityRangeBegin*.1f;
                n.VisibilityRangeEndMargin=n.VisibilityRangeEnd*.1f;
                n.VisibilityRangeFadeMode=GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            }
            if(arm=="scatter_bounds")
            {
                var box=n.CustomAabb.Size.LengthSquared()>0?n.CustomAabb:n.Multimesh.GetAabb();
                n.CustomAabb=box.Grow(8); // Preserve the existing common LOD-band centre.
            }
            if(arm=="scatter_shadows_off")n.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        }
        if(arm=="directional_shadows_off")
            foreach(var n in FindChildren("*",nameof(DirectionalLight3D),true,false).OfType<DirectionalLight3D>())n.ShadowEnabled=false;
        if(arm=="sdfgi_off")
            foreach(var n in FindChildren("*",nameof(WorldEnvironment),true,false).OfType<WorldEnvironment>())
                if(n.Environment is {} environment)environment.SdfgiEnabled=false;
        if(arm=="occlusion_off")
        {
            GetViewport().UseOcclusionCulling=false;
            foreach(var n in FindChildren("*",nameof(OccluderInstance3D),true,false).OfType<OccluderInstance3D>())n.Visible=false;
        }
        _camera.TargetDistance=3.5f;_camera.Pitch=-.18f;
        using var trace=new StreamWriter(Path.Combine(directory,"route.csv"));
        trace.WriteLine("frame,ms,player_x,player_z,camera_x,camera_y,camera_z,yaw,frame_cap,draw_calls");
        int frame=0;
        File.WriteAllText(Path.Combine(directory,"record_ready.json"),JsonSerializer.Serialize(new{
            arm,scatter_cells=scatter.Length,residency=_scatter.ResidencyEnabled,max_fps=Engine.MaxFps,
            lod_environment=OS.GetEnvironment("UNNAMED_LOD_STIPPLE"),
            evidence="SOURCE DIAGNOSTIC, not unchanged packaged RCd5. Real-time normal controller/pacing; no screenshots during route."}));
        ulong waitUntil=Time.GetTicksMsec()+120000;
        while(!File.Exists(Path.Combine(directory,"recording.started")))
        {
            if(Time.GetTicksMsec()>waitUntil)throw new TimeoutException("Recorder did not start");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
        ulong start=Time.GetTicksMsec();
        try
        {
            // Ashen Hollow approach, forest verge, eastern road, Cairnwold entry.
            foreach(var end in new[]{new Vector2(450,748),new Vector2(480,760),new Vector2(560,752),new Vector2(650,742),
                new Vector2(744.8f,732.5f),new Vector2(745.5f,731.8f),new Vector2(908.8f,705.5f),
                new Vector2(911.8f,702.8f),new Vector2(937,683)})
            {
                var body=simulation.Player.Body;
                var path=Playthrough.WayTo(simulation,body,new Body((long)(end.X*1000),body.YMm,(long)(end.Y*1000),body.FacingMdeg),standOff:0);
                if(path.Length==0)throw new InvalidOperationException("No route to "+end);
                ulong progressAt=Time.GetTicksMsec();var last=new Vector2(body.XMm/1000f,body.ZMm/1000f);
                foreach(var point in path)
                while(true)
                {
                    body=simulation.Player.Body;var at=new Vector2(body.XMm/1000f,body.ZMm/1000f);
                    var d=new Vector3((float)(point.X-at.X),0,(float)(point.Z-at.Y));if(d.Length()<.3f)break;
                    if(at.DistanceTo(last)>1){last=at;progressAt=Time.GetTicksMsec();}
                    if(Time.GetTicksMsec()-progressAt>12000 || Time.GetTicksMsec()-start>420000)
                        throw new TimeoutException("Route blocked at "+at+" toward "+point);
                    // Gentle periodic camera turns expose cell/shadow boundaries during actual running.
                    _camera.Yaw=Mathf.Atan2(-d.X,-d.Z)+.45f*Mathf.Sin((Time.GetTicksMsec()-start)/6000f);
                    _controller.SteerWorld(d.Normalized(),Gait.Run,_camera);
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                    var cam=_camera.Camera.GlobalPosition;
                    trace.WriteLine(FormattableString.Invariant($"{frame++},{Time.GetTicksMsec()},{at.X},{at.Y},{cam.X},{cam.Y},{cam.Z},{_camera.Yaw},{Engine.MaxFps},{Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}"));
                }
            }
            File.WriteAllText(Path.Combine(directory,"route_complete.json"),JsonSerializer.Serialize(new{frame,seconds=(Time.GetTicksMsec()-start)/1000.0,arm}));
        }
        finally{_controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);_rcd3Driving=false;trace.Flush();}
    }
}
