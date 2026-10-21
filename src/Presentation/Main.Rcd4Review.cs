using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Opt-in scenery A/B. NPC drawing is suppressed only in this labelled diagnostic;
    // the normal world, terrain, materials, owner settings and gameplay pacing are retained.
    private async Task RunRcd4WorldReview(string directory, Camera3D camera)
    {
        SetProcess(false);
        if (System.Environment.GetEnvironmentVariable("OTHERREACH_RCD4_TAVAR_ONLY") == "1")
        {
            if (_npcs.SuppressPresentationForBenchmark) throw new InvalidOperationException("Tavar gate requires NPCs");
            await Rcd4Tavar(directory,camera);
            File.WriteAllText(Path.Combine(directory,"complete.json"),"{\"tavar_gate\":true,\"npc_suppressed\":false}");
            return;
        }
        var rows = new List<object>();
        Vector3 At(float x,float rise,float z) => new(x,
            _session.Setup.Layout.Space.Terrain.HeightAtMm((long)(x*1000),(long)(z*1000))/1000f+rise,z);
        var trees = _hollow.FindChildren("*", "Node3D", true, false).OfType<Node3D>()
            .Where(n => n.Name.ToString().StartsWith("cw_birch_", StringComparison.Ordinal)
                || n.Name.ToString().StartsWith("tree_", StringComparison.Ordinal)).ToArray();
        File.WriteAllText(Path.Combine(directory,"tree_nodes.json"), JsonSerializer.Serialize(trees.Select(t => new {
            name=t.Name.ToString(), position=t.GlobalPosition.ToString(), bounds=Art.ArtGallery.Bounds(t).ToString() })));
        if (System.Environment.GetEnvironmentVariable("OTHERREACH_RCD4_TREE_SHAPES") == "1")
        {
            var bands = _scatter.FindChildren("saplings_*", "MultiMeshInstance3D", true, false).OfType<MultiMeshInstance3D>().ToArray();
            // Fix the world instance, independent of a changed LOD chain's shared bounds.
            var reference = new Vector3(422.56113f,8.494504f,756.07996f);
            var plant = bands.Where(m => m.Name.ToString().EndsWith("_lod0",StringComparison.Ordinal))
                .SelectMany(m => Enumerable.Range(0,m.Multimesh.InstanceCount)
                    .Select(i => m.GlobalTransform*m.Multimesh.GetInstanceTransform(i).Origin))
                .OrderBy(p => p.DistanceSquaredTo(reference)).First();
            if (plant.DistanceTo(reference) > .01f) throw new InvalidOperationException("Sapling fixture world instance changed");
            foreach (float bias in new[] { 1f, 1024f })
            {
                foreach (var band in bands) band.LodBias=bias;
                foreach (float distance in new[] { 5f,12f,22f,42f })
                {
                    var aim=plant+Vector3.Up;
                    camera.GlobalPosition=aim+new Vector3(distance*.3f,.6f,distance*.954f);camera.LookAt(aim);
                    _shadowLods.Update(camera.GlobalPosition);
                    await Rcd3Wait(.4);
                    string name=$"sapling_bias{bias:0}_{distance:0}m";
                    await Rcd3Shot(directory,name);
                    rows.Add(new { name,plant=plant.ToString(),eye=camera.GlobalPosition.ToString(),bias,
                        note="Actual scatter plant; only automatic mesh LOD bias changes; authored distance bands retained" });
                }
            }
        }
        foreach (var tree in trees.OrderBy(t => t.GlobalPosition.DistanceSquaredTo(_avatar.GlobalPosition))
            .Take(System.Environment.GetEnvironmentVariable("OTHERREACH_RCD4_TREE_SHAPES") == "1"
                || System.Environment.GetEnvironmentVariable("OTHERREACH_RCD4_ROOT_WALK") == "1"
                || System.Environment.GetEnvironmentVariable("OTHERREACH_RCD4_SCENERY_ONLY") == "1" ? 0 : 2))
        {
            var bounds = Art.ArtGallery.Bounds(tree);
            var aim = tree.ToGlobal(bounds.GetCenter());
            var meshes = tree.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
            var ends = meshes.Select(m => m.VisibilityRangeEnd).Where(x => x > 0).Distinct().Order().ToArray();
            foreach (bool fade in new[] { false, true })
            {
                _shadowLods.Unwatch();
                foreach (var mesh in meshes)
                {
                    mesh.VisibilityRangeFadeMode = fade ? GeometryInstance3D.VisibilityRangeFadeModeEnum.Self
                        : GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
                    mesh.VisibilityRangeBeginMargin = fade ? mesh.VisibilityRangeBegin * .1f : 0;
                    mesh.VisibilityRangeEndMargin = fade ? mesh.VisibilityRangeEnd * .1f : 0;
                }
                _shadowLods.Watch(this);
                foreach (float end in ends)
                    foreach (float offset in new[] { -2f, 0f, 2f })
                    {
                        float distance = end + offset;
                        camera.GlobalPosition = aim + new Vector3(distance * .3f, 2, distance * .954f);
                        camera.LookAt(aim);
                        _shadowLods.Update(camera.GlobalPosition);
                        await Rcd3Wait(.35);
                        string name = $"{tree.Name}_{(fade ? "fade" : "hard")}_{distance:0.0}";
                        await Rcd3Shot(directory,name);
                        rows.Add(new { name, distance, eye=camera.GlobalPosition.ToString(), target=aim.ToString(),
                            bands=meshes.Select(m=>new { name=m.Name.ToString(), begin=m.VisibilityRangeBegin, end=m.VisibilityRangeEnd,
                                fade=m.VisibilityRangeFadeMode.ToString(), shadow=m.CastShadow.ToString(), gi=m.GIMode.ToString() }).ToArray() });
                    }
            }
        }
        foreach (var shot in new[] {
            ("old_late_full", At(680,23,790),At(650,25,765)),
            ("old_late_roots",At(666,2,779),At(650,2,765)),
            ("den_gorge",At(535,2,799.5f),At(512,-20,824)),
            ("den_gorge_floor",At(535,2,799.5f),At(535,-18,817)) })
        {
            camera.GlobalPosition=shot.Item2;camera.LookAt(shot.Item3);
            _shadowLods.Update(camera.GlobalPosition);
            await Rcd3Wait(.5);await Rcd3Shot(directory,shot.Item1);
            rows.Add(new { name=shot.Item1, eye=shot.Item2.ToString(), target=shot.Item3.ToString(),
                npc_suppressed_fixture=_npcs.SuppressPresentationForBenchmark });
        }
        if (System.Environment.GetEnvironmentVariable("OTHERREACH_RCD4_KEEP_NPCS") == "1")
            await Rcd4Tavar(directory,camera);
        foreach(string id in new[]{"creature.beast.wolf_grey","creature.beast.wolf_grey_alpha"})
        {
            var wolf=_session.Simulation!.Creatures.FirstOrDefault(c=>c.Alive && c.DefId==id && c.Key.StartsWith("spawn.hollow.den_pack#",StringComparison.Ordinal));
            if(wolf is null) throw new InvalidOperationException("Den creature missing: "+id);
            var aim=Greybox.HollowView.ToGodot(wolf.Body.XMm,wolf.Body.YMm,wolf.Body.ZMm)+Vector3.Up*.7f;
            camera.GlobalPosition=At(512,2,775);camera.LookAt(aim);
            _creatures.Draw(_session.Simulation,1,.04);
            _hud.SetTarget(new Ui.TargetPlate(Ui.Names.Of(_session,wolf.DefId),wolf.Level,wolf.Health,wolf.MaxHealth,wolf.Threat,
                wolf.Temperament,wolf.Hostile,wolf.Huntable));
            _shadowLods.Update(camera.GlobalPosition);await Rcd3Wait(.3);
            await Rcd3Shot(directory,id.EndsWith("alpha",StringComparison.Ordinal)?"alpha_plate_focus_fixture":"wolf_plate_focus_fixture");
            rows.Add(new { creature=id,level=wolf.Level,threat=wolf.Threat.ToString(),
                fixture="Manual presentation focus using actual creature state; automatic combat target selection unchanged" });
        }
        if (System.Environment.GetEnvironmentVariable("OTHERREACH_RCD4_ROOT_WALK") == "1")
            await Rcd4RootWalk(directory,camera);
        File.WriteAllText(Path.Combine(directory,"world_review.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
            fixture="Scenery review; no authoritative state writes", npc_suppressed=_npcs.SuppressPresentationForBenchmark }));
    }

    private async Task Rcd4Tavar(string directory,Camera3D camera)
    {
        var simulation=_session.Simulation!;
        var held=simulation.Npcs.Single(n=>n.Id==Greybox.FoldEcho.Held);
        var centre=Greybox.HollowView.ToGodot(held.Body.XMm,held.Body.YMm,held.Body.ZMm);
        var ward=(Node3D?)_hollow.FindChild("WardRunes",true,false)
            ?? throw new InvalidOperationException("Tavar ward missing");
        var decal=ward.GetNode<Decal>("EmissiveRuneDecal");
        var motes=ward.GetNode<GpuParticles3D>("DriftingWardMotes");
        var light=ward.GetNode<OmniLight3D>("WardSoftLight");
        var shots=new List<object>();
        foreach(float distance in new[] {30f,20f,10f,5f})
        {
            camera.GlobalPosition=centre+new Vector3(-distance*.3f,distance*.12f+1.6f,distance*.954f);
            camera.LookAt(centre+Vector3.Up*.8f);
            for(int i=0;i<25;i++) { _npcs.Draw(simulation,.04,_session.TickSeconds); await Rcd3Wait(.04); }
            _shadowLods.Update(camera.GlobalPosition);
            await Rcd3Shot(directory,$"tavar_held_{distance:0}m");
            if(distance==10) await Rcd3Shot(directory,"tavar");
            shots.Add(new {distance,eye=camera.GlobalPosition.ToString(),target=(centre+Vector3.Up*.8f).ToString(),
                decal_energy=decal.EmissionEnergy,motes=motes.Amount,soft_light_energy=light.LightEnergy });
        }
        var figure=(Player.Figure)_npcs.FigureOf(Greybox.FoldEcho.Held)!;
        string? heldClip=figure.ClipState;
        string? nativeMetrics=figure.GetNodeOrNull<Node>("Native")?.Call("native_metrics").ToString();
        float restingEnergy=decal.EmissionEnergy;
        // Explicit presentation-only fixtures of proximity and release. The saved barrier stays closed.
        _hollow.SetWardPlayer(centre+Vector3.Back*3.5f);await Rcd3Wait(.15);
        float touchedEnergy=decal.EmissionEnergy;
        await Rcd3Shot(directory,"tavar_touch_visual_fixture");
        var released=simulation.Barriers.Select(b=>b.Site.Key==Greybox.FoldEcho.Barrier ? b with {Standing=false} : b).ToArray();
        _hollow.SetFlags(simulation.Switches,released,inPlay:true);
        await Rcd3Wait(.8);await Rcd3Shot(directory,"tavar_release_visual_fixture");
        await Rcd3Wait(2.5);
        if(light.LightEnergy>.001f || motes.Visible || decal.EmissionEnergy>.001f)
            throw new InvalidOperationException("Ward effects survived release");
        figure.SetPosture(false,false);
        for(int i=0;i<35;i++) { figure.Pose(centre,Player.PlayerController.FacingRadians(held.Body.FacingMdeg),0,.04);await Rcd3Wait(.04); }
        await Rcd3Shot(directory,"tavar_freed_pose_visual_fixture");
        File.WriteAllText(Path.Combine(directory,"tavar_review.json"),JsonSerializer.Serialize(new {
            held_clip=heldClip, released_clip=figure.ClipState,shots, npc_suppressed=_npcs.SuppressPresentationForBenchmark,
            native_metrics=nativeMetrics,resting_decal_energy=restingEnergy,touch_decal_energy=touchedEnergy,
            released_light_energy=light.LightEnergy,released_motes_visible=motes.Visible,
            barrier_still_standing=simulation.Barriers.Single(b=>b.Site.Key==Greybox.FoldEcho.Barrier).Standing,
            note="Held views are real NPC state. Touch/release are labelled presentation-only previews; simulation barrier and quest unchanged." }));
        if(heldClip!="crouch_idle") throw new InvalidOperationException("Tavar captive clip not selected");
        if(touchedEnergy<=restingEnergy) throw new InvalidOperationException("Ward proximity pulse did not brighten");
    }

    private async Task Rcd4RootWalk(string directory,Camera3D camera)
    {
        var simulation=_session.Simulation!;var space=simulation.ActiveLayout.Space;
        var surface=space.WalkableSurfaces.Single();
        var points=Enumerable.Range(0,surface.Rows).SelectMany(z=>Enumerable.Range(0,surface.Columns).Select(x=>
            (X:surface.OriginXMm+x*surface.SpacingMm,Z:surface.OriginZMm+z*surface.SpacingMm)));
        var neighbours=new[] {(-250L,-250L),(0L,-250L),(250L,-250L),(-250L,0L),(0L,0L),(250L,0L),(-250L,250L),(0L,250L),(250L,250L)};
        var goal=points.Where(p=>Math.Pow(p.X-650000,2)+Math.Pow(p.Z-765000,2)>36000000)
            .Where(p=>space.HeightAtMm(p.X,p.Z)-space.Terrain.HeightAtMm(p.X,p.Z)>600)
            .Where(p=>neighbours.Max(n=>space.HeightAtMm(p.X+n.Item1,p.Z+n.Item2))
                -neighbours.Min(n=>space.HeightAtMm(p.X+n.Item1,p.Z+n.Item2))<250)
            .OrderByDescending(p=>space.HeightAtMm(p.X,p.Z)-space.Terrain.HeightAtMm(p.X,p.Z)).First();
        var destination=new Body(goal.X,space.HeightAtMm(goal.X,goal.Z),goal.Z,0);
        // Local navigation windows, around the village and south of the occupied wolf den.
        var anchors=new[] { (440000L,755000L),(465000L,755000L),(495000L,755000L),
            (525000L,755000L),(555000L,755000L),(585000L,755000L),(615000L,755000L),(goal.X,goal.Z) };
        var path=new List<(double X,double Z)>();var from=_controller.Authoritative;
        foreach(var anchor in anchors)
        {
            var to=new Body(anchor.Item1,space.HeightAtMm(anchor.Item1,anchor.Item2),anchor.Item2,0);
            path.AddRange(Playthrough.WayTo(simulation,from,to,standOff:0));from=to;
        }
        var route=path.ToArray();
        int waypoint=0,shots=0;float maxRise=0;ulong start=Time.GetTicksMsec(),lastShot=0;
        _rcd3Driving=true;SetProcess(true);
        while(waypoint<route.Length && Time.GetTicksMsec()-start<140000)
        {
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            var body=_controller.Authoritative;var point=route[waypoint];
            var toward=new Vector3((float)(point.X-body.XMm/1000.0),0,(float)(point.Z-body.ZMm/1000.0));
            if(toward.Length()<.12f){waypoint++;continue;}
            _controller.SteerWorld(toward.Normalized(),toward.Length()<1 ? Gait.Walk : Gait.Run,_camera);
            var outward=(_avatar.GlobalPosition-new Vector3(650,_avatar.GlobalPosition.Y,765)).Normalized();
            var eye=_avatar.GlobalPosition+outward*6+Vector3.Up*3;
            eye.Y=Math.Max(eye.Y,space.HeightAtMm((long)(eye.X*1000),(long)(eye.Z*1000))/1000f+2);
            camera.GlobalPosition=eye;camera.LookAt(_avatar.GlobalPosition+Vector3.Up);
            _shadowLods.Update(eye);
            float rise=(body.YMm-space.Terrain.HeightAtMm(body.XMm,body.ZMm))/1000f;maxRise=Math.Max(maxRise,rise);
            if(rise>.4f && shots<3 && Time.GetTicksMsec()-lastShot>800)
            { await Rcd3Shot(directory,$"old_late_climb_{++shots}");lastShot=Time.GetTicksMsec(); }
        }
        _controller.SteerWorld(Vector3.Zero,Gait.Run,_camera);await Rcd3Wait(1);
        await Rcd3Shot(directory,"old_late_standing");
        var feet=_controller.Authoritative;
        bool known=simulation.Map.Places.Any(p=>p.LocationId=="location.old_late");
        File.WriteAllText(Path.Combine(directory,"root_walk.json"),JsonSerializer.Serialize(new {
            destination,feet,waypoint,waypoints=route.Length,max_rise_m=maxRise,known,
            support_y_mm=space.HeightAtMm(feet.XMm,feet.ZMm),
            native_metrics=_avatar.GetNodeOrNull<Node>("Native")?.Call("native_metrics").ToString(),
            proof="Actual controller movement and simulation collision; no actor teleport or authoritative state writes" },new JsonSerializerOptions{WriteIndented=true}));
        if(maxRise<.4f || !known) throw new InvalidOperationException("Old Late walk did not prove root support and discovery");
        Show(_map);await Rcd3Wait(.3);await Rcd3Shot(directory,"old_late_discovery_map");
        _rcd3Driving=false;SetProcess(false);
    }
}
