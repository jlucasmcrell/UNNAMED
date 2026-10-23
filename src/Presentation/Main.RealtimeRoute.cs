using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Owner finding 50: each turned rock's footprint as drawn - the hull of its full-detail mesh above the ground - for
    // tools/rocks/apply_rock_hulls.py to write into the region as hull_m. Metres, world XZ.
    private void WriteRockHulls(string path)
    {
        var hulls=new SortedDictionary<string,double[][]>(StringComparer.Ordinal);
        foreach(var (id,model) in _hollow.TurnedModels())
        {
            var points=new List<(long,long)>();
            // The full-detail level only: lighter levels begin further out.
            foreach(var mesh in model.FindChildren("*",nameof(MeshInstance3D),true,false).Cast<MeshInstance3D>())
            {
                if(mesh.Mesh is null||mesh.VisibilityRangeBegin>0)continue;
                var toWorld=mesh.GlobalTransform;
                foreach(var v in mesh.Mesh.GetFaces())
                {
                    var w=toWorld*v;
                    if(w.Y<_ground.Height(w.X,w.Z)+.05f)continue;   // buried rock is not met
                    points.Add(((long)Math.Round(w.X*1000),(long)Math.Round(w.Z*1000)));
                }
            }
            var hull=PolygonBlocker.Hull(points);
            if(hull.Length>=3)hulls[id]=hull.Select(c=>new[]{c.XMm/1000.0,c.ZMm/1000.0}).ToArray();
        }
        File.WriteAllText(path,JsonSerializer.Serialize(hulls));
        GD.Print($"UNNAMED rock hulls: {hulls.Count} written to {path}");
    }

    // Diagnostic input driver only: unchanged simulation pacing, renderer and movement commands.
    private async Task RunRcd6RealtimeRoute(string directory)
    {
        _rcd3Driving=true;
        DisplayServer.WindowSetTitle("Otherreach - "+Path.GetFileName(directory));
        var simulation=_session.Simulation ?? throw new InvalidOperationException("World missing");
        // A review can be scheduled while the boot canvas still covers the world.
        // Never spend part of the requested route behind the loading screen.
        while(GetViewport().Disable3D)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        ulong warm=Time.GetTicksMsec()+2000;
        while(Time.GetTicksMsec()<warm)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        if(OS.GetEnvironment("OTHERREACH_ROCK_HULLS_OUT") is {Length:>0} hullsOut)
        {
            WriteRockHulls(hullsOut);
            CaptureQuit.At(this,0);
            return;
        }
        string arm=OS.GetEnvironment("OTHERREACH_VISIBILITY_ARM");
        var arms=arm.Split(',');
        var scatter=_scatter.FindChildren("*",nameof(MultiMeshInstance3D),true,false).OfType<MultiMeshInstance3D>().ToArray();
        if(arms.Contains("scatter_single_lod"))
        {
            // Hold geometry constant without the invalid all-LODs-at-once experiment.
            // Godot renames duplicate form names; the existing common band AABB is the reliable family key.
            foreach(var family in scatter.Where(n=>n.CustomAabb.Size.LengthSquared()>0)
                .GroupBy(n=>(n.CustomAabb,n.Multimesh.InstanceCount)))
            {
                float reach=family.Max(n=>n.VisibilityRangeEnd);
                var chosen=family.OrderBy(n=>n.VisibilityRangeBegin).First();
                foreach(var n in family)
                {
                    bool near=n==chosen;
                    n.Visible=near;
                    if(near){n.VisibilityRangeBegin=0;n.VisibilityRangeEnd=reach;}
                }
            }
        }
        foreach(var n in scatter)
        {
            if(arms.Contains("scatter_self_fade"))
            {
                n.VisibilityRangeBeginMargin=n.VisibilityRangeBegin*.1f;
                n.VisibilityRangeEndMargin=n.VisibilityRangeEnd*.1f;
                n.VisibilityRangeFadeMode=GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            }
            if(arms.Contains("scatter_bounds"))
            {
                var box=n.CustomAabb.Size.LengthSquared()>0?n.CustomAabb:n.Multimesh.GetAabb();
                n.CustomAabb=box.Grow(8); // Preserve the existing common LOD-band centre.
            }
            if(arms.Contains("scatter_shadows_off"))n.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        }
        if(arms.Contains("directional_shadows_off"))
            foreach(var n in FindChildren("*",nameof(DirectionalLight3D),true,false).OfType<DirectionalLight3D>())n.ShadowEnabled=false;
        if(arms.Contains("sdfgi_off"))
            foreach(var n in FindChildren("*",nameof(WorldEnvironment),true,false).OfType<WorldEnvironment>())
                if(n.Environment is {} environment)environment.SdfgiEnabled=false;
        foreach(var n in FindChildren("*",nameof(WorldEnvironment),true,false).OfType<WorldEnvironment>())
            if(n.Environment is {} e)
            {
                if(arms.Contains("ssao_ssil_off")){e.SsaoEnabled=false;e.SsilEnabled=false;}
                if(arms.Contains("volumetric_off"))e.VolumetricFogEnabled=false;
                if(arms.Contains("ssr_off"))e.SsrEnabled=false;
            }
        if(arms.Contains("clouds_off"))
            foreach(var n in FindChildren("*",nameof(WorldEnvironment),true,false).OfType<WorldEnvironment>())
                if(n.Compositor is {} c)foreach(var effect in c.CompositorEffects)effect.Enabled=false;
        if(arms.Contains("occlusion_off"))
        {
            GetViewport().UseOcclusionCulling=false;
            foreach(var n in FindChildren("*",nameof(OccluderInstance3D),true,false).OfType<OccluderInstance3D>())n.Visible=false;
        }
        _camera.TargetDistance=3.5f;_camera.Pitch=-.18f;
        using var trace=new StreamWriter(Path.Combine(directory,"route.csv"));
        trace.WriteLine("frame,ms,player_x,player_z,camera_x,camera_y,camera_z,yaw,frame_cap,draw_calls");
        int frame=0;
        File.WriteAllText(Path.Combine(directory,"record_ready.json"),JsonSerializer.Serialize(new{
            arm,scatter_cells=scatter.Length,visible_scatter_nodes=scatter.Count(n=>n.Visible),residency=_scatter.ResidencyEnabled,max_fps=Engine.MaxFps,
            environments=FindChildren("*",nameof(WorldEnvironment),true,false).OfType<WorldEnvironment>()
                .Select(n=>new{path=n.GetPath().ToString(),sdfgi=n.Environment?.SdfgiEnabled}),
            directional_lights=FindChildren("*",nameof(DirectionalLight3D),true,false).OfType<DirectionalLight3D>()
                .Select(n=>new{path=n.GetPath().ToString(),shadows=n.ShadowEnabled,n.Visible}),
            lod_environment=OS.GetEnvironment("UNNAMED_LOD_STIPPLE"),
            runtime_mesh_lods=OS.GetEnvironment("UNNAMED_RUNTIME_MESH_LODS"),plant_lod=OS.GetEnvironment("UNNAMED_SCATTER_PLANT_LOD"),
            evidence="SOURCE DIAGNOSTIC, not unchanged packaged RCd5. Real-time normal controller/pacing; no screenshots during route."}));
        ulong waitUntil=Time.GetTicksMsec()+120000;
        while(!File.Exists(Path.Combine(directory,"recording.started")))
        {
            if(Time.GetTicksMsec()>waitUntil)throw new TimeoutException("Recorder did not start");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
        ulong start=Time.GetTicksMsec();
        bool failed=false;
        try
        {
            // Ashen Hollow approach, forest verge, eastern road, Cairnwold entry.
            foreach(var end in new[]{new Vector2(450,748),new Vector2(480,760),new Vector2(560,752),new Vector2(650,742),
                new Vector2(744.8f,732.5f),new Vector2(745.5f,731.8f),new Vector2(908.8f,705.5f),
                new Vector2(911.8f,702.8f),new Vector2(937,683)}.Take(int.TryParse(OS.GetEnvironment("OTHERREACH_ROUTE_LEGS"),out int legs)?legs:9))
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
                    // A fixed yaw (radians) holds the view still but for travel, so a tile that changes brightness is a pop, not a turn.
                    _camera.Yaw=float.TryParse(OS.GetEnvironment("OTHERREACH_ROUTE_YAW"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float fixedYaw)?fixedYaw
                        :Mathf.Atan2(-d.X,-d.Z)+.45f*Mathf.Sin((Time.GetTicksMsec()-start)/6000f);
                    _controller.SteerWorld(d.Normalized(),Gait.Run,_camera);
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                    var cam=_camera.Camera.GlobalPosition;
                    trace.WriteLine(FormattableString.Invariant($"{frame++},{Time.GetTicksMsec()},{at.X},{at.Y},{cam.X},{cam.Y},{cam.Z},{_camera.Yaw},{Engine.MaxFps},{Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}"));
                }
            }
            File.WriteAllText(Path.Combine(directory,"route_complete.json"),JsonSerializer.Serialize(new{frame,seconds=(Time.GetTicksMsec()-start)/1000.0,arm}));
        }
        catch(Exception error)
        {
            failed=true;
            File.WriteAllText(Path.Combine(directory,"route_failed.txt"),error.ToString());
            throw;
        }
        finally
        {
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);_rcd3Driving=false;trace.Flush();
            File.WriteAllText(Path.Combine(directory,"recording.finished"),DateTimeOffset.Now.ToString("o"));
            // Keep the game foreground until desktop capture has closed, including failure paths.
            ulong stopDeadline=Time.GetTicksMsec()+10000;
            while(!File.Exists(Path.Combine(directory,"recording.stopped")) && Time.GetTicksMsec()<stopDeadline)
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(failed)CaptureQuit.At(this, 1);
        }
    }
}
