using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

public partial class Main
{
    private async Task RunRcd6VisibilityReview(string directory, Camera3D diagnostic)
    {
        // Use the real gameplay camera, spring arm and shadow/cell updates.
        diagnostic.GetNode<OmniLight3D>("CharacterFill").Reparent(_camera.Camera, false);
        var camera = _camera.Camera;camera.MakeCurrent();
        if(OS.GetEnvironment("OTHERREACH_ULTRA_FOLIAGE_REVIEW")=="1")
        {
            await RunUltraFoliageReview(directory);return;
        }
        if(OS.GetEnvironment("OTHERREACH_FOOTSTEP_REVIEW")=="1")
        {
            await RunFootstepReview(directory);return;
        }
        if(System.Environment.GetEnvironmentVariable("OTHERREACH_VISIBILITY_REALTIME_ROUTE")=="1")
        {
            await RunRcd6RealtimeRoute(directory);
            return;
        }
        _rcd3Driving = true;
        _camera.TargetDistance = 3.5f;_camera.Pitch = -.18f;
        var simulation = _session.Simulation ?? throw new InvalidOperationException("World missing");
        string area = System.Environment.GetEnvironmentVariable("OTHERREACH_VISIBILITY_AREA") ?? "village";
        if(System.Environment.GetEnvironmentVariable("OTHERREACH_VISIBILITY_RANGE_ONLY")=="1")
        {
            await Rcd6RangeGate(directory,camera);
            File.WriteAllText(Path.Combine(directory,"complete.json"),"{\"status\":\"RANGE_DIAGNOSTIC_REQUIRES_REVIEW\"}");
            return;
        }
        if(System.Environment.GetEnvironmentVariable("OTHERREACH_VISIBILITY_MOTION_ONLY")=="1")
        {
            await Rcd6MotionCullingGate(directory,camera);
            camera.GetNode<OmniLight3D>("CharacterFill").Reparent(diagnostic,false);diagnostic.MakeCurrent();
            var faces=Path.Combine(directory,"hair_faces");Directory.CreateDirectory(faces);await RunRcd6NamedFacesReview(faces,diagnostic);
            File.WriteAllText(Path.Combine(directory,"complete.json"),"{\"status\":\"MOTION_DIAGNOSTIC_REQUIRES_REVIEW\"}");return;
        }
        var allGeometry = new Node[] { _hollow, _scatter }.SelectMany(n => n.FindChildren("*", nameof(GeometryInstance3D), true, false))
            .OfType<GeometryInstance3D>().Where(n => n is MeshInstance3D or MultiMeshInstance3D).ToArray();
        Aabb Box(GeometryInstance3D n) => n.CustomAabb.Size.LengthSquared() > .000001f ? n.CustomAabb
            : n is MeshInstance3D m ? m.GetAabb() : ((MultiMeshInstance3D)n).Multimesh.GetAabb();
        // The route spans less than 80 m. Far scatter cannot enter its authored range;
        // retain every structure/tree and only instrument potentially reachable chunks.
        var geometries = allGeometry.Where(n => n is not MultiMeshInstance3D || n.VisibilityRangeEnd <= 0 ||
            camera.GlobalPosition.DistanceTo((n.GlobalTransform * Box(n)).GetCenter()) < n.VisibilityRangeEnd + 140).ToArray();
        var original = geometries.Select(n => (n.Visible, n.VisibilityRangeBegin, n.VisibilityRangeEnd, n.VisibilityRangeBeginMargin,
            n.VisibilityRangeEndMargin, n.VisibilityRangeFadeMode, n.LodBias)).ToArray();

        var seen = geometries.Select(n => { var notifier = new VisibleOnScreenNotifier3D { Aabb = Box(n) };n.AddChild(notifier);return notifier; }).ToArray();
        File.WriteAllText(Path.Combine(directory,"instances.json"), JsonSerializer.Serialize(geometries.Select((n,i) => new {
            id=i,path=n.GetPath().ToString(),type=n.GetClass().ToString(),box=Box(n).ToString(),global=n.GlobalTransform.ToString(),
            begin=n.VisibilityRangeBegin,end=n.VisibilityRangeEnd,beginMargin=n.VisibilityRangeBeginMargin,endMargin=n.VisibilityRangeEndMargin,
            fade=n.VisibilityRangeFadeMode.ToString(),lodBias=n.LodBias,shadow=n.CastShadow.ToString() })));
        File.WriteAllText(Path.Combine(directory,"occluders.json"),JsonSerializer.Serialize(FindChildren("*",nameof(OccluderInstance3D),true,false)
            .OfType<OccluderInstance3D>().Select(n=>new{path=n.GetPath().ToString(),n.Visible,transform=n.GlobalTransform.ToString(),vertices=n.Occluder?.GetVertices().Length,indices=n.Occluder?.GetIndices().Length})));
        using var events = new StreamWriter(Path.Combine(directory,"visibility_events.csv"));events.WriteLine("frame,mode,id,old_bits,new_bits,distance_m");
        using var frames = new StreamWriter(Path.Combine(directory,"frames.csv"));frames.WriteLine("frame,mode,ms,camera_x,camera_y,camera_z,yaw,visible_tree,in_range,on_screen,toggles,draw_calls,primitives,objects");
        var states = Enumerable.Repeat(-1,geometries.Length).ToArray();int frame = 0;string mode="travel";
        void Record()
        {
            int visible=0,inRange=0,onScreen=0,toggles=0;
            for(int i=0;i<geometries.Length;i++)
            {
                var n=geometries[i];float distance=camera.GlobalPosition.DistanceTo((n.GlobalTransform*Box(n)).GetCenter());
                bool shown=n.IsVisibleInTree(),range=(n.VisibilityRangeBegin<=0 || distance>=n.VisibilityRangeBegin)&&(n.VisibilityRangeEnd<=0 || distance<n.VisibilityRangeEnd);
                bool screen=seen[i].IsOnScreen();int bits=(shown?1:0)|(range?2:0)|(screen?4:0);
                if(shown)visible++;if(range)inRange++;if(screen)onScreen++;
                if(bits!=states[i]){events.WriteLine(FormattableString.Invariant($"{frame},{mode},{i},{states[i]},{bits},{distance:0.####}"));states[i]=bits;toggles++;}
            }
            var at=camera.GlobalPosition;
            frames.WriteLine(FormattableString.Invariant($"{frame},{mode},{Time.GetTicksMsec()},{at.X},{at.Y},{at.Z},{_camera.Yaw},{visible},{inRange},{onScreen},{toggles},{Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)},{Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame)},{Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame)}"));frame++;
        }
        async Task Step(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);Record();}
        async Task Go(float x,float z,double limit=65)
        {
            var body=simulation.Player.Body;var goal=new Body((long)(x*1000),body.YMm,(long)(z*1000),body.FacingMdeg);
            var route=Playthrough.WayTo(simulation,body,goal,standOff:0);ulong until=Time.GetTicksMsec()+(ulong)(limit*1000);
            foreach(var point in route)
                while(Time.GetTicksMsec()<until)
                {
                    body=simulation.Player.Body;var d=new Vector3((float)(point.X-body.XMm/1000.0),0,(float)(point.Z-body.ZMm/1000.0));if(d.Length()<.15f)break;
                    _controller.SteerWorld(d.Normalized(),Gait.Run,_camera);_camera.Yaw=Mathf.Atan2(-d.X,-d.Z);await Step();
                }
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);
            if(new Vector2(simulation.Player.Body.XMm/1000f-x,simulation.Player.Body.ZMm/1000f-z).Length()>2)throw new InvalidOperationException("Visibility route did not reach area");
        }
        try
        {
            await Go(area=="forest"?420:450,area=="forest"?780:748);
            mode="walk_baseline";
            var start=simulation.Player.Body;var endpoints=new[]{new Vector2(start.XMm/1000f,start.ZMm/1000f),new Vector2(start.XMm/1000f+20,start.ZMm/1000f+8)};
            int goalIndex=1,pointIndex=0,shot=0;ulong started=Time.GetTicksMsec();var path=Playthrough.WayTo(simulation,start,new Body((long)(endpoints[1].X*1000),start.YMm,(long)(endpoints[1].Y*1000),start.FacingMdeg),standOff:0);
            while(Time.GetTicksMsec()-started<60000)
            {
                var body=simulation.Player.Body;
                if(pointIndex>=path.Length){goalIndex=1-goalIndex;var end=endpoints[goalIndex];path=Playthrough.WayTo(simulation,body,new Body((long)(end.X*1000),body.YMm,(long)(end.Y*1000),body.FacingMdeg),standOff:0);pointIndex=0;}
                if(path.Length>0 && pointIndex<path.Length)
                {
                    var point=path[pointIndex];var d=new Vector3((float)(point.X-body.XMm/1000.0),0,(float)(point.Z-body.ZMm/1000.0));
                    if(d.Length()<.15f)pointIndex++;else{_controller.SteerWorld(d.Normalized(),Gait.Walk,_camera);_camera.Yaw=Mathf.Atan2(-d.X,-d.Z);}
                }
                await Step();
                if(Time.GetTicksMsec()-started>(ulong)(shot*6000) && shot<10){await Rcd3Shot(directory,$"walk_{shot++:00}");}
            }
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);mode="spin_baseline";started=Time.GetTicksMsec();shot=0;
            while(Time.GetTicksMsec()-started<12000){_camera.Yaw=Mathf.Tau*(Time.GetTicksMsec()-started)/12000f;await Step();if(Time.GetTicksMsec()-started>(ulong)(shot*1200) && shot<10)await Rcd3Shot(directory,$"spin_{shot++:00}");}
            // Native culling notifier includes frustum AND occlusion; range state is logged separately.
            bool occlusion=GetViewport().UseOcclusionCulling;
            foreach(string axis in new[]{"baseline","occlusion_off","range_self","fixed_lod"})
            {
                mode=axis;GetViewport().UseOcclusionCulling=axis=="occlusion_off"?false:occlusion;
                for(int i=0;i<geometries.Length;i++)
                {
                    var n=geometries[i];var o=original[i];n.Visible=o.Visible;n.VisibilityRangeBegin=o.VisibilityRangeBegin;n.VisibilityRangeEnd=o.VisibilityRangeEnd;
                    n.VisibilityRangeBeginMargin=o.VisibilityRangeBeginMargin;n.VisibilityRangeEndMargin=o.VisibilityRangeEndMargin;n.VisibilityRangeFadeMode=o.VisibilityRangeFadeMode;n.LodBias=o.LodBias;
                    if(axis=="range_self"){n.VisibilityRangeFadeMode=GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;n.VisibilityRangeBeginMargin=o.VisibilityRangeBegin*.1f;n.VisibilityRangeEndMargin=o.VisibilityRangeEnd*.1f;}
                    if(axis=="fixed_lod")
                    {
                        float distance=camera.GlobalPosition.DistanceTo((n.GlobalTransform*Box(n)).GetCenter());
                        n.Visible=o.Visible&&(o.VisibilityRangeBegin<=0||distance>=o.VisibilityRangeBegin)&&(o.VisibilityRangeEnd<=0||distance<o.VisibilityRangeEnd);
                        n.VisibilityRangeBegin=0;n.VisibilityRangeEnd=0;n.LodBias=1024;
                    }
                }
                for(int yaw=0;yaw<8;yaw++){_camera.Yaw=yaw*Mathf.Tau/8;ulong until=Time.GetTicksMsec()+350;while(Time.GetTicksMsec()<until)await Step();await Rcd3Shot(directory,$"{axis}_yaw{yaw*45:000}");}
            }
            GetViewport().UseOcclusionCulling=occlusion;
            for(int i=0;i<geometries.Length;i++)
            {
                var n=geometries[i];var o=original[i];n.Visible=o.Visible;n.VisibilityRangeBegin=o.VisibilityRangeBegin;n.VisibilityRangeEnd=o.VisibilityRangeEnd;
                n.VisibilityRangeBeginMargin=o.VisibilityRangeBeginMargin;n.VisibilityRangeEndMargin=o.VisibilityRangeEndMargin;n.VisibilityRangeFadeMode=o.VisibilityRangeFadeMode;n.LodBias=o.LodBias;
            }
            await Rcd6RangeGate(directory, camera);
            File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new{status="DIAGNOSTIC_REQUIRES_REVIEW",area,frame,instances=geometries.Length,
                residency=_scatter.ResidencyEnabled,occlusion,route="Normal gameplay movement and spring-arm camera; 60s walk, 360 spin, independent native culling/range/LOD toggles; not a performance measurement"}));
        }
        finally{_controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);_rcd3Driving=false;}
    }
    private async Task Rcd6RangeGate(string directory, Camera3D source)
    {
        var camera=new Camera3D { Fov=source.Fov,Near=source.Near,Far=source.Far,CullMask=source.CullMask,Attributes=source.Attributes,Environment=source.Environment };
        AddChild(camera);camera.MakeCurrent();
        foreach(var terrain in FindChildren("*","Terrain3D",true,false))terrain.Call("set_camera",camera);
        var candidates=_hollow.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>()
            .Where(n=>n.IsVisibleInTree() && n.VisibilityRangeBegin==0 && n.VisibilityRangeEnd>0).ToArray();
        Aabb Box(MeshInstance3D n)=>n.GlobalTransform*(n.CustomAabb.Size.LengthSquared()>0?n.CustomAabb:n.GetAabb());
        var tree=candidates.Where(n=>n.GetPath().ToString().Contains("/Hollow/tree_") && Box(n).Size.Y>7 && Box(n).Size.Y<17)
            .OrderBy(n=>Box(n).GetCenter().DistanceTo(source.GlobalPosition)).First();
        var prop=candidates.First(n=>n.GetPath().ToString().Contains("/Hollow/rock_well/"));
        var rows=new List<object>();
        foreach(var (label,selected) in new[]{("prop",prop),("tree",tree)})
        {
            Node family=selected;while(family.GetParent()!=_hollow)family=family.GetParent();
            var meshes=family.FindChildren("*",nameof(GeometryInstance3D),true,false).OfType<GeometryInstance3D>().ToArray();
            var saved=meshes.Select(n=>(n.VisibilityRangeBeginMargin,n.VisibilityRangeEndMargin,n.VisibilityRangeFadeMode)).ToArray();
            var centre=Box(selected).GetCenter();float boundary=selected.VisibilityRangeEnd;
            foreach(bool fade in new[]{false,true})
            {
                foreach(var n in meshes){n.VisibilityRangeBeginMargin=fade?n.VisibilityRangeBegin*.1f:0;n.VisibilityRangeEndMargin=fade?n.VisibilityRangeEnd*.1f:0;n.VisibilityRangeFadeMode=fade?GeometryInstance3D.VisibilityRangeFadeModeEnum.Self:GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;}
                for(int i=0;i<10;i++)
                {
                    float distance=boundary+Mathf.Lerp(-boundary*.15f,boundary*.15f,i/9f);
                    float eyeOffset=label=="prop"?1.5f:.3f;
                    camera.GlobalPosition=centre+new Vector3(0,eyeOffset,-Mathf.Sqrt(distance*distance-eyeOffset*eyeOffset));camera.LookAt(centre);
                    for(int f=0;f<(i==0?20:5);f++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    string file=$"range_{label}_{(fade?"fade":"hard")}_{i:00}";await Rcd3Shot(directory,file);
                    rows.Add(new{label,fade,frame=i,distance,camera=camera.GlobalPosition.ToString(),family=family.GetPath().ToString(),boundary,file=file+".png"});
                }
            }
            for(int i=0;i<meshes.Length;i++){meshes[i].VisibilityRangeBeginMargin=saved[i].VisibilityRangeBeginMargin;meshes[i].VisibilityRangeEndMargin=saved[i].VisibilityRangeEndMargin;meshes[i].VisibilityRangeFadeMode=saved[i].VisibilityRangeFadeMode;}
        }
        File.WriteAllText(Path.Combine(directory,"range_gate.json"),JsonSerializer.Serialize(rows));
    }

    private async Task Rcd6MotionCullingGate(string directory,Camera3D camera)
    {
        var viewport=GetViewport();bool original=viewport.UseOcclusionCulling;var rows=new List<object>();
        var occluders=FindChildren("*",nameof(OccluderInstance3D),true,false).OfType<OccluderInstance3D>().ToArray();
        var enabled=occluders.Select(n=>n.Visible).ToArray();
        foreach(bool culling in new[]{true,false})
        {
            viewport.UseOcclusionCulling=culling;
            for(int i=0;i<occluders.Length;i++)occluders[i].Visible=culling&&enabled[i];
            _camera.Yaw=0;for(int warm=0;warm<12;warm++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            for(int i=0;i<60;i++)
            {
                _camera.Yaw=i*Mathf.Tau/60;
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                string file=$"motion_{(culling?"occlusion":"none")}_{i:00}.png";
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory,file));
                rows.Add(new{frame=i,culling,camera=camera.GlobalPosition.ToString(),yaw=_camera.Yaw,ms=Time.GetTicksMsec(),file,draw_calls=Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)});
            }
        }
        viewport.UseOcclusionCulling=original;
        for(int i=0;i<occluders.Length;i++)occluders[i].Visible=enabled[i];
        File.WriteAllText(Path.Combine(directory,"motion_culling.json"),JsonSerializer.Serialize(new{lod_environment=OS.GetEnvironment("UNNAMED_LOD_STIPPLE"),residency=_scatter.ResidencyEnabled,rows}));
    }

}
