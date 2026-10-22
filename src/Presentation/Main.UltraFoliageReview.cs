using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

public partial class Main
{
    private async Task RunUltraFoliageReview(string directory)
    {
        DisplayServer.WindowSetTitle("Otherreach - "+Path.GetFileName(directory));
        _rcd3Driving = true;
        var sim = _session.Simulation ?? throw new InvalidOperationException("World missing");
        var camera = _camera.Camera;
        string area = OS.GetEnvironment("OTHERREACH_ULTRA_FOLIAGE_AREA");
        var start = area == "wall" ? new Vector2(1001, 700) : new Vector2(981, 700);
        var finish = area == "wall" ? new Vector2(946, 700) : new Vector2(946, 700);
        using var trace = new StreamWriter(Path.Combine(directory, "foliage_route.csv"));
        trace.WriteLine("ms,phase,x,z,camera_x,camera_y,camera_z,heading,rim_distance,orange_stand_distance,frame_cap,frame_ms");
        string phase = "approach";
        async Task Frame()
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var b = sim.Player.Body; var c = camera.GlobalPosition;
            double rim = Math.Sqrt(Math.Pow(b.XMm / 1000.0 - 886, 2) + Math.Pow(b.ZMm / 1000.0 - 700, 2));
            trace.WriteLine(FormattableString.Invariant($"{Time.GetTicksMsec()},{phase},{b.XMm / 1000.0},{b.ZMm / 1000.0},{c.X},{c.Y},{c.Z},{(Mathf.RadToDeg(_camera.Yaw)+180)%360},{rim},{Math.Sqrt(Math.Pow(b.XMm/1000.0-941,2)+Math.Pow(b.ZMm/1000.0-699,2))},{Engine.MaxFps},{GetProcessDeltaTime()*1000}"));
        }
        async Task Go(Vector2 end, Gait gait)
        {
            var b = sim.Player.Body;
            if (new Vector2(b.XMm/1000f,b.ZMm/1000f).DistanceTo(end)<.4f) return;
            var path = Playthrough.WayTo(sim, b, new Body((long)(end.X * 1000), b.YMm, (long)(end.Y * 1000), b.FacingMdeg), standOff: 0);
            if (path.Length == 0) throw new InvalidOperationException("No foliage review path to " + end);
            ulong deadline = Time.GetTicksMsec() + 240000;
            foreach (var p in path)
            while (true)
            {
                b = sim.Player.Body;
                var d = new Vector3((float)(p.X-b.XMm/1000.0), 0, (float)(p.Z-b.ZMm/1000.0));
                if (d.Length() < .3f) break;
                if (Time.GetTicksMsec() > deadline) throw new TimeoutException("Foliage approach blocked: " + b);
                _camera.Yaw = phase == "walk" ? Mathf.Pi / 2 + Mathf.DegToRad(11) * Mathf.Sin((start.X-b.XMm/1000f)/(start.X-finish.X)*Mathf.Tau) : Mathf.Atan2(-d.X,-d.Z);
                _controller.SteerWorld(d.Normalized(), gait, _camera);
                await Frame();
            }
            _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
        }
        try
        {
            _camera.TargetDistance = 3.5f; _camera.Pitch = -.12f;
            // Normal navigation/movement throughout, including travel from the fresh spawn.
            await Go(start, Gait.Run);
            _camera.Yaw = Mathf.Pi / 2;
            for (int i = 0; i < 60; i++) await Frame();
            var meshes = new Node[] { _hollow, _scatter }.SelectMany(n => n.FindChildren("*", nameof(GeometryInstance3D), true, false))
                .OfType<GeometryInstance3D>().Where(n => n is MeshInstance3D or MultiMeshInstance3D).ToArray();
            Aabb Bounds(GeometryInstance3D n) => n.CustomAabb.Size.LengthSquared() > 0 ? n.CustomAabb
                : n is MeshInstance3D mesh ? mesh.GetAabb() : ((MultiMeshInstance3D)n).Multimesh.GetAabb();
            string Family(GeometryInstance3D n)
            {
                for(Node? p=n.GetParent();p is not null;p=p.GetParent())
                    if(p.GetMeta("otherreach_lod_root",false).AsBool())return p.GetInstanceId().ToString();
                return n.GetInstanceId().ToString();
            }
            var audit = new List<object>();
            foreach (var family in meshes.Where(n=>n.HasMeta("otherreach_lod_family")).GroupBy(Family))
            {
                var levels = family.GroupBy(n=>n.GetMeta("otherreach_lod_level").AsInt32()).OrderBy(g=>g.Key).ToArray();
                var centre = (family.First().GlobalTransform*Bounds(family.First())).GetCenter();
                float drift = family.Max(n=>(n.GlobalTransform*Bounds(n)).GetCenter().DistanceTo(centre));
                if (drift>.01f) throw new InvalidOperationException("LOD bounds centres disagree: "+family.Key+" "+drift);
                for(int i=1;i<levels.Length;i++)
                {
                    var near=levels[i-1].First();var far=levels[i].First();
                    if(near.VisibilityRangeEnd+near.VisibilityRangeEndMargin+.001f<far.VisibilityRangeBegin)
                        throw new InvalidOperationException("LOD range gap: "+near.GetPath());
                }
                audit.Add(new{family=family.Key,asset=family.First().GetMeta("otherreach_lod_asset").AsString(),levels=levels.Length,world_centre=centre.ToString(),max_centre_drift_m=drift});
            }
            File.WriteAllText(Path.Combine(directory,"chain_audit.json"),JsonSerializer.Serialize(audit));
            File.WriteAllText(Path.Combine(directory,"all_lod_chains.json"), JsonSerializer.Serialize(meshes.Select(n => new {
                path=n.GetPath().ToString(), family=Family(n), asset=n.GetMeta("otherreach_lod_asset", "").AsString(),
                tree=n.GetMeta("otherreach_tree", false).AsBool(), level=n.GetMeta("otherreach_lod_level", -1).AsInt32(),
                begin=n.VisibilityRangeBegin, end=n.VisibilityRangeEnd, begin_margin=n.VisibilityRangeBeginMargin, end_margin=n.VisibilityRangeEndMargin,
                fade=n.VisibilityRangeFadeMode.ToString(), bias=n.LodBias, bounds=Bounds(n).ToString(),
                world_centre=(n.GlobalTransform * Bounds(n)).GetCenter().ToString(), distance_basis="common transformed AABB centre",
                instances=n is MultiMeshInstance3D m ? m.Multimesh.InstanceCount : 1
            })));
            File.WriteAllText(Path.Combine(directory,"record_ready.json"), JsonSerializer.Serialize(new { area, start=start.ToString(), finish=finish.ToString(),
                tier=VisualOptions.Tier, hour=VisualOptions.Hour, max_fps=Engine.MaxFps, seed=OS.GetEnvironment("OTHERREACH_LIGHTING_SEED"), evidence="Uncapped normal controller; no screenshots or fixed timestep during route" }));
            ulong until=Time.GetTicksMsec()+120000;
            while(!File.Exists(Path.Combine(directory,"recording.started")))
            {
                if(Time.GetTicksMsec()>until) throw new TimeoutException("No WGC recorder");
                await Frame();
            }
            phase="walk";
            await Go(finish,Gait.Walk);
            phase="held";until=Time.GetTicksMsec()+3000;
            while(Time.GetTicksMsec()<until)await Frame();
            File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new { area,status="VIDEO_REQUIRES_REVIEW",player=sim.Player.Body.ToString() }));
        }
        finally
        {
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);_rcd3Driving=false;trace.Flush();
            File.WriteAllText(Path.Combine(directory,"recording.finished"),DateTimeOffset.Now.ToString("o"));
            ulong until=Time.GetTicksMsec()+20000;
            while(!File.Exists(Path.Combine(directory,"recording.stopped"))&&Time.GetTicksMsec()<until)
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
    }
}
