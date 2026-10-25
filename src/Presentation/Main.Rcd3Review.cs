using Godot;
using System.Text.Json;
using UNNAMED.Presentation.MHCharacters;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Ui;
using UNNAMED.Presentation.Ui.Models;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Opt-in evidence through the running game's normal creator, input, simulation and figure.
    // No authoritative state writes, pacing override or visual-setting substitutions.
    private bool _rcd3Driving;
    private async void RunRcd3Review(string directory)
    {
        var rows = new List<object>();
        try
        {
            if (_avatar is SkinnedFigure comparison && VisualOptions.ComparisonCast is not null)
            {
                bool humgen = VisualOptions.ComparisonCast == "humgen";
                using var cast = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(humgen ? "res://Art/humgen_cast.json" : "res://Art/mpfb_cast.json"));
                var model = cast.RootElement.GetProperty("models").EnumerateArray().Single(m => m.GetProperty("id").GetString() == comparison.ModelId);
                string sex = model.GetProperty("sex").GetString()!;
                float expectedHeight = humgen ? model.GetProperty("height_m").GetSingle() : cast.RootElement.GetProperty("stature_m").GetProperty(sex).GetSingle();
                float drawnHeight = comparison.StatureScale * model.GetProperty("height_m").GetSingle();
                if (Math.Abs(drawnHeight - expectedHeight) > .01f) throw new InvalidOperationException("Comparison creator retained the previous body's scale");
                File.WriteAllText(Path.Combine(directory,"comparison_stature.json"),JsonSerializer.Serialize(new { model = comparison.ModelId, sex, expectedHeight, drawnHeight, scale = comparison.StatureScale }));
            }
            var source = _camera.Camera;
            var camera = new Camera3D { Fov = source.Fov, Near = source.Near, Far = source.Far, CullMask = source.CullMask,
                Attributes = source.Attributes, Environment = source.Environment };
            AddChild(camera);
            source.GetNode<OmniLight3D>("CharacterFill").Reparent(camera, false);
            camera.MakeCurrent();
            if(OS.GetEnvironment("OTHERREACH_RCD8_MOTION_REVIEW")=="1")
            {
                camera.GetNode<OmniLight3D>("CharacterFill").Reparent(source,false);
                source.MakeCurrent();camera.QueueFree();
                await RunFootstepReview(directory);GetTree().Quit();return;
            }
            if(OS.GetEnvironment("OTHERREACH_FIELD_RUINS_VIDEO")=="1")
            {
                camera.GetNode<OmniLight3D>("CharacterFill").Reparent(source,false);
                source.MakeCurrent();camera.QueueFree();
                await RunFieldRuinsVideo(directory);GetTree().Quit();return;
            }
            if(OS.GetEnvironment("OTHERREACH_RCD8_WAYSTONE_REVIEW")=="1")
            {
                await RunRcd8WaystoneReview(directory,camera);GetTree().Quit();return;
            }
            bool humgenStartup=OS.GetEnvironment("OTHERREACH_RCD8_STARTUP_REVIEW")=="1";
            if(humgenStartup || OS.GetEnvironment("OTHERREACH_RCD6_STARTUP_REVIEW")=="1")
            {
                _rcd3Driving=true;
                var sim=_session.Simulation ?? throw new InvalidDataException("Fresh startup has no world");
                if(humgenStartup)
                {
                    if(_avatar is not SkinnedFigure native || !native.ModelId.StartsWith("humgen_",StringComparison.Ordinal))throw new InvalidDataException("RC d8 did not draw a HumGen player");
                    if(sim.Setup.Pets is null)throw new InvalidDataException("Accepted pets are missing from RC d8");
                }
                else if(sim.Setup.Pets is not null || sim.Pets.Length!=0)throw new InvalidDataException("Pending pets were enabled in RC d6");
                var plants=_scatter.FindChildren("*",nameof(MultiMeshInstance3D),true,false).Count;
                if(plants==0)throw new InvalidDataException("Fresh startup has no scatter meshes");
                var aim=_avatar.GlobalPosition+Vector3.Up*1.1f;
                camera.GlobalPosition=aim+new Vector3(2,1.3f,3);camera.LookAt(aim);await Rcd3Wait(2);
                if(OS.GetEnvironment("OTHERREACH_RCD9_IDLE_REVIEW")=="1")
                {
                    if(sim.Combat.InCombat || _avatar.ClipState != "idle")throw new InvalidDataException("Peaceful spawn did not select relaxed idle");
                    File.WriteAllText(Path.Combine(directory,"relaxed_idle.json"),JsonSerializer.Serialize(new {
                        model=_avatarModel,combat=sim.Combat.InCombat,clip=_avatar.ClipState,weapon=sim.Combat.Weapon.Source,
                        npcs=_npcs.States().Select(n=>new {id=n.Id,speed=n.Speed,clip=n.State})
                    },new JsonSerializerOptions{WriteIndented=true}));
                }
                await Rcd3Shot(directory,"07_fresh_spawn_front");
                camera.GlobalPosition=aim+new Vector3(-2,1.3f,-3);camera.LookAt(aim);await Rcd3Wait(1);
                await Rcd3Shot(directory,"08_fresh_spawn_back_grass");
                var npc=_npcs.GetChildren().OfType<Figure>().Where(n=>n.IsVisibleInTree())
                    .OrderBy(n=>n.GlobalPosition.DistanceSquaredTo(_avatar.GlobalPosition)).FirstOrDefault()
                    ?? throw new InvalidDataException("Fresh startup has no visible people");
                aim=npc.GlobalPosition+Vector3.Up*1.1f;camera.GlobalPosition=aim+new Vector3(2,1.2f,3);camera.LookAt(aim);await Rcd3Wait(2);
                await Rcd3Shot(directory,"09_fresh_npc_clothing");
                _inventory.Open(null);await Rcd3Wait(1);await Rcd3Shot(directory,"10_fresh_inventory");
                File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
                    status="FRESH_PACKAGED_STARTUP_REQUIRES_CLAUDE_REVIEW",people=VisualOptions.People,model=_avatarModel,
                    scatter_meshes=plants,npcs=_npcs.States().ToArray(),pets_enabled=sim.Setup.Pets is not null,
                    inventory=sim.Player.Inventory.Select(e=>new {e.DefId,e.Count}),world_tick=sim.WorldTick,
                    engine_max_fps=Engine.MaxFps,profile="Fresh isolated profile; normal creator Begin; no desktop input"},new JsonSerializerOptions{WriteIndented=true}));
                File.WriteAllText(Path.Combine(directory,"vram.json"),JsonSerializer.Serialize(new {
                    video_mib=Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed)/1048576.0,
                    peak_video_mib=_mhPeakVideoMiB,
                    texture_mib=Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed)/1048576.0,
                    textures=VisualOptions.All.GetValueOrDefault("textures"),cache_hits=_art.Cache.Hits,cache_failed=_art.Cache.Failed,
                    source_resolution_preserved=true }));
                if(OS.GetEnvironment("OTHERREACH_RCD9_WARDROBE_REVIEW")=="1")
                {
                    _inventory.Close();
                    await RunRcd9WardrobeReview(directory,camera);
                }
                GetTree().Quit();return;
            }
            if(OS.GetEnvironment("OTHERREACH_RCD6_INVENTORY_REVIEW")=="1")
            {
                camera.GlobalTransform=source.GlobalTransform;
                if(OS.GetEnvironment("OTHERREACH_RCD6_UI_WITH_AWAY")=="1")await RunAwayReview(directory,complete:false);
                await RunInventoryReview(directory,camera);GetTree().Quit();return;
            }
            if (OS.GetEnvironment("OTHERREACH_RCD6_AWAY")=="1")
            {
                await RunAwayReview(directory);GetTree().Quit();return;
            }
            if (OS.GetEnvironment("OTHERREACH_RCD6_FURNITURE")=="1")
            {
                await RunFurnitureReview(directory,camera);
                if(OS.GetEnvironment("OTHERREACH_FOOTSTEP_AFTER_FURNITURE")=="1")
                {
                    _avatar.Visible=true;
                    camera.GetNode<OmniLight3D>("CharacterFill").Reparent(_camera.Camera,false);
                    _camera.Camera.MakeCurrent();
                    string feetDirectory=OS.GetEnvironment("OTHERREACH_FOOTSTEP_CHAIN_OUT");
                    if(!Path.IsPathFullyQualified(feetDirectory))throw new InvalidDataException("Footstep output must be absolute");
                    Directory.CreateDirectory(feetDirectory);
                    await RunFootstepReview(feetDirectory);
                }
                GetTree().Quit();return;
            }
            if (System.Environment.GetEnvironmentVariable("OTHERREACH_RCD6_GRIP") == "1")
            {
                await RunRcd6GripReview(directory, camera);GetTree().Quit();return;
            }
            if (System.Environment.GetEnvironmentVariable("OTHERREACH_RCD6_VISIBILITY") == "1")
            {
                await RunRcd6VisibilityReview(directory, camera);
                CaptureQuit.At(this);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("OTHERREACH_RCD6_NAMED_FACES") == "1")
            {
                await RunRcd6NamedFacesReview(directory, camera);
                CaptureQuit.At(this);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("OTHERREACH_RCD6_MPFB_MOTION") == "1")
            {
                await RunRcd6MpfbMotionReview(directory, camera);
                CaptureQuit.At(this);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("OTHERREACH_EFFECT_DELIVERY_REVIEW") == "1")
            {
                await RunEffectDeliveryReview(directory, camera);
                CaptureQuit.At(this);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("OTHERREACH_RCD5_STEALTH") == "1")
            {
                await RunRcd5StealthReview(directory, camera);
                CaptureQuit.At(this);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("OTHERREACH_GORGE_REVIEW") == "video")
            {
                camera.GetNode<OmniLight3D>("CharacterFill").Reparent(source, false);
                source.MakeCurrent(); camera.QueueFree();
                await RunGorgeVideo(directory); GetTree().Quit(); return;
            }
            if (System.Environment.GetEnvironmentVariable("OTHERREACH_GORGE_REVIEW") == "stills")
            {
                await RunGorgeStills(directory, camera);
                CaptureQuit.At(this);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("OTHERREACH_RCD4_WORLD") == "1")
            {
                await RunRcd4WorldReview(directory, camera);
                CaptureQuit.At(this);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("OTHERREACH_RCD4_HOVER") == "1")
            {
                SetProcess(false);
                var outline = new Outlines();
                var target = _npcs.GetChildren().OfType<Figure>().Where(f => f.IsVisibleInTree())
                    .OrderBy(f => f.GlobalPosition.DistanceSquaredTo(_avatar.GlobalPosition)).First();
                foreach (float distance in new[] { 3f, 6f, 10f })
                {
                    var aim = target.GlobalPosition + Vector3.Up;
                    camera.GlobalPosition = aim + target.GlobalBasis.Z * distance + Vector3.Up * .2f;
                    camera.LookAt(aim);
                    outline.Show(Array.Empty<(Node3D?, OutlineTone)>(), false);
                    await Rcd3Wait(.4);
                    await Rcd3Shot(directory, $"rim_{distance:0}_off");
                    outline.Show(new[] { ((Node3D?)target, OutlineTone.Focus) }, false);
                    await Rcd3Wait(.4);
                    await Rcd3Shot(directory, $"rim_{distance:0}_on");
                }
                File.WriteAllText(Path.Combine(directory, "complete.json"), "{\"fixture\":\"Frozen actual NPC, normal-distance hover pairs\"}");
                CaptureQuit.At(this);
                return;
            }
            var actions = new (string Name, string[] Keys, double Seconds)[] {
                ("idle", [], .7), ("walk", ["move_forward", "walk"], 1.2),
                ("forward_left", ["move_forward", "move_left"], 1.1), ("forward_right", ["move_forward", "move_right"], 1.1),
                ("left", ["move_left"], 1.1), ("right", ["move_right"], 1.1),
                ("back_left", ["move_back", "move_left"], 1.1), ("back_right", ["move_back", "move_right"], 1.1),
                ("run", ["move_forward"], 1.1), ("sprint", ["move_forward", "sprint"], 1.2), ("stop", [], .8),
                ("cursor_held", ["free_cursor"], .3), ("cursor_released", [], .3) };
            foreach (var action in actions)
            {
                foreach (string key in action.Keys) Input.ActionPress(key);
                double elapsed = 0;
                int sample = 0;
                while (elapsed < action.Seconds)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    elapsed += GetProcessDeltaTime();
                    var aim = _avatar.GlobalPosition + Vector3.Up * .95f;
                    camera.GlobalPosition = aim + new Vector3(2.5f, 1.1f, 3.5f);
                    camera.LookAt(aim);
                    var native = _avatar.GetNodeOrNull<Node>("Native");
                    rows.Add(new { action = action.Name, elapsed, dt = GetProcessDeltaTime(), feet = _avatar.GlobalPosition.ToString(),
                        left = _avatar.Foot("L")?.ToString(), right = _avatar.Foot("R")?.ToString(), yaw = _avatar.Rotation.Y,
                        clip = _avatar.ClipState, mouse = Input.MouseMode.ToString(),
                        metrics = sample == 0 && elapsed > action.Seconds * .45 ? native?.Call("native_metrics").ToString() : null });
                    if (elapsed > action.Seconds * .45 && sample++ < 4)
                    {
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, $"{action.Name}_{sample}.png"));
                    }
                }
                foreach (string key in action.Keys) Input.ActionRelease(key);
            }
            File.WriteAllText(Path.Combine(directory, "motion.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            if (System.Environment.GetEnvironmentVariable("OTHERREACH_RCD3_DETAILS") == "1")
                await Rcd3Details(directory, camera);
            File.WriteAllText(Path.Combine(directory, "complete.json"), JsonSerializer.Serialize(new { status = "CAPTURED_REQUIRES_CLAUDE_REVIEW", people = VisualOptions.People }));
            CaptureQuit.At(this);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(directory, "failed.txt"), error.ToString());
            CaptureQuit.At(this, 3);
        }
        finally
        {
            foreach (string key in new[] { "move_forward", "move_back", "move_left", "move_right", "walk", "sprint", "free_cursor" }) Input.ActionRelease(key);
        }
    }
    private async Task Rcd3Wait(double seconds)
    {
        ulong until = Time.GetTicksMsec() + (ulong)Math.Ceiling(seconds * 1000);
        while (Time.GetTicksMsec() < until) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task Rcd3Shot(string directory, string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if(OS.GetEnvironment("OTHERREACH_REVIEW_WGC_STILLS")=="1")
        { await RequestReviewWgcStill(directory,name);return; }
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, name + ".png"));
    }
    private async Task Rcd3Details(string directory, Camera3D camera)
    {
        var evidence = new List<object>();
        var faces = new[] { _avatar }.Concat(_npcs.GetChildren().OfType<Figure>())
            .Where(f => f.IsVisibleInTree()).GroupBy(f => f is MetaHumanFigure mh ? mh.CharacterId : f.Name.ToString()).Select(g => g.First()).ToArray();
        // A live world interval proves independent activity choices; no performance measurement.
        if (faces.Skip(1).OrderBy(f => f.GlobalPosition.DistanceSquaredTo(_avatar.GlobalPosition)).FirstOrDefault() is { } idleSubject)
        {
            var aim = idleSubject.GlobalPosition + Vector3.Up;
            camera.GlobalPosition = aim + new Vector3(4,1.5f,5);
            camera.LookAt(aim); // Observe NPCs on screen so the existing off-screen pause does not suppress their idle pool.
        }
        for (int second = 0; second <= 20; second += 5)
        {
            evidence.Add(new { idle_second = second, figures = faces.Select(f => new { name = f.Name.ToString(), clip = f.ClipState }).ToArray() });
            await Rcd3Shot(directory, $"idles_{second:00}");
            if (second < 20) await Rcd3Wait(5);
        }
        foreach (var figure in faces)
        {
            var aim = figure.Head ?? figure.GlobalPosition + Vector3.Up * 1.5f;
            camera.Fov = 32;
            camera.GlobalPosition = aim + figure.GlobalBasis.Z * .65f + Vector3.Up * .03f;
            camera.LookAt(aim);
            await Rcd3Wait(.2); // Normal NPC visibility/LOD sees this camera before the pose-only test.
            SetProcess(false); // Freeze the world for the explicitly labelled facial-channel fixture.
            if (figure.GetNodeOrNull<Node>("Native") is { } nativeActor) nativeActor.Call("sample_clip", "idle", .5);
            aim = figure.Head ?? aim;
            camera.GlobalPosition = aim + figure.GlobalBasis.Z * .65f + Vector3.Up * .03f;camera.LookAt(aim);
            var driver = figure.GetChildren().OfType<FaceDriver>().FirstOrDefault();
            string id = figure is MetaHumanFigure mh ? mh.CharacterId : figure.Name.ToString();
            await Rcd3Shot(directory, id + "_face_rest");
            bool blink = false;
            if (driver is not null)
            {
                double elapsed = 0;
                while (elapsed < 7 && !blink)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); elapsed += GetProcessDeltaTime();
                    if (driver.BlinkWeight > .65f) { await Rcd3Shot(directory, id + "_face_blink"); blink = true; }
                }
                figure.Say("A warm road. We can make our way home.");
                await Rcd3Wait(.35);
                await Rcd3Shot(directory, id + "_face_talk");
                evidence.Add(new { face = id, driver = true, blink, jaw = driver.JawWeight, fixture = "Actual game figure; world temporarily frozen; Say channel used by dialogue/barks" });
                figure.Say(null);
            }
            else evidence.Add(new { face = id, driver = false, reason = "No expression morphs or facial joints in this export; no fabricated blink/jaw" });
            SetProcess(true);
        }
        foreach (var figure in faces.Take(3))
        {
            SetProcess(false);
            bool grassWasVisible = _scatter.Visible;
            _scatter.Visible = false; // Labelled extremity fixture; keep grass from hiding the toes.
            var skeleton = figure.FindChildren("*", nameof(Skeleton3D), true, false).OfType<Skeleton3D>()
                .Where(s => s.FindBone("foot_l") >= 0 || s.FindBone("foot.L") >= 0)
                .OrderByDescending(s => s.GetBoneCount()).FirstOrDefault();
            if (skeleton is not null)
                foreach (var shot in MpfbShotList)
                {
                    int bone = shot.Bones.Select(skeleton.FindBone).FirstOrDefault(index => index >= 0, -1);
                    if (bone < 0) continue;
                    var aim = skeleton.ToGlobal(skeleton.GetBoneGlobalPose(bone).Origin) + Vector3.Up * shot.AimRise;
                    camera.Fov = 40;
                    camera.GlobalPosition = aim + figure.GlobalBasis.Z.Rotated(Vector3.Up,shot.Yaw) * shot.Distance;
                    camera.LookAt(aim);
                    await Rcd3Wait(.12);
                    await Rcd3Shot(directory, figure.Name + "_gate_" + shot.Name);
                }
            _scatter.Visible = grassWasVisible;
            evidence.Add(new { extremity_fixture = figure.Name.ToString(), grass_hidden_for_closeups_only = true,
                model = (figure as SkinnedFigure)?.ModelId, uniform_scale = (figure as SkinnedFigure)?.StatureScale });
            SetProcess(true);
        }
        camera.Fov = _camera.Camera.Fov;
        var outline = new Outlines();
        var lookAt = _avatar.GlobalPosition + Vector3.Up;
        camera.GlobalPosition = lookAt + _avatar.GlobalBasis.Z * 2.8f + Vector3.Up * .5f;
        camera.LookAt(lookAt);
        await Rcd3Wait(.3);
        await Rcd3Shot(directory, "hover_off");
        outline.Show(new[] { ((Node3D?)_avatar, OutlineTone.Focus) }, false);
        await Rcd3Wait(.2); // Allow the overlay's transparent passes to reach the rendered frame.
        await Rcd3Shot(directory, "hover_on");
        outline.Show(Array.Empty<(Node3D?, OutlineTone)>(), false);
        // Real HUD controls, with a labelled two-bearing text fixture. No quest/save state is changed.
        SetProcess(false);
        var window = GetWindow(); var originalSize = window.Size; var originalMode = window.Mode;
        var compass = _hud.GetChildren().OfType<Compass>().Single();
        var words = (Label)compass.FindChild("QuestBearings", true, false);
        var tracker = (Control)_hud.FindChild("QuestTracker", true, false);
        string oldWords = words.Text;
        window.Mode = Window.ModeEnum.Windowed;
        foreach (var size in new[] { new Vector2I(1920,1080), new Vector2I(2560,1440), new Vector2I(3840,2160) })
        {
            window.Size = size;
            _hud.SetMarkerWords("Quest: Renn Vale, 36 m\nNearest: Kera Voss, 28 m");
            await Rcd3Wait(.3);
            var bearings = words.GetGlobalRect(); var quest = tracker.GetGlobalRect();
            bool clear = quest.Position.Y >= bearings.End.Y;
            evidence.Add(new { hud_fixture = size.ToString(), logical_canvas = GetViewport().GetVisibleRect().Size.ToString(), texture_pixels = GetViewport().GetTexture().GetSize().ToString(),
                ui_scale = Settings.SettingsStore.Current.Profile.Interface.UiScale, bearings = bearings.ToString(), tracker = quest.ToString(), clear });
            if (!clear) throw new InvalidOperationException("Quest tracker overlaps compass bearings at " + size);
            await Rcd3Shot(directory, $"hud_{size.X}x{size.Y}");
        }
        _hud.SetMarkerWords(oldWords); window.Size = originalSize; window.Mode = originalMode; SetProcess(true);
        if (System.Environment.GetEnvironmentVariable("OTHERREACH_RCD3_FEEDBACK") == "1")
        {
            camera.Fov = _camera.Camera.Fov;
            var at = _avatar.GlobalPosition;
            camera.GlobalPosition = at + new Vector3(2,2,3);camera.LookAt(at+Vector3.Up*.7f);
            _successEffects.Waystone(at+Vector3.Left,at,false);
            _soundEvents.WaystoneUse(_session.Setup.Layout.Waystones.First().Key); // Labelled audio audition; no authoritative travel command.
            _hud.Announce("Waystone attuned","Ashen Waystone",HudStyle.Lichen);
            await Rcd3Wait(.35);await Rcd3Shot(directory,"fixture_waystone");await Rcd3Wait(1.8);
            _hud.Announce("Quest complete","Presentation fixture",HudStyle.Brass);
            _hud.AnnounceMore("Quest complete","Presentation fixture","+40 XP Â· +10 coin (fixture)");
            _hud.SuccessGlow(HudStyle.Brass);
            _sounds.Play("sfx.ui.quest_complete");
            await Rcd3Wait(.25);await Rcd3Shot(directory,"fixture_quest");await Rcd3Wait(1.8);
            _successEffects.Craft(at+Vector3.Up*.8f,true,2);
            _sounds.Play("sfx.crafting.anvil.strike",at+Vector3.Up*.8f);
            _sounds.Play("sfx.crafting.complete",at+Vector3.Up*.8f,.3);
            await Rcd3Wait(.2);await Rcd3Shot(directory,"fixture_craft");
            evidence.Add(new { feedback_fixture="Rendering only; no quest, Waystone or crafting state mutated. Actual event subscriptions require playthrough." });
            evidence.Add(new { audio_audition_resolved = _sounds.Resolved.ToArray(), audio_errors = _sounds.Errors });
        }
        // Walk to Kera through the existing navigation and command path, then inspect actual offered replies.
        _rcd3Driving = true;
        var simulation = _session.Simulation!;
        const string npcId = "npc.ashen_hollow.kera_voss";
        var npc = simulation.Npcs.Single(n => n.Id == npcId);
        var route = Playthrough.Hollow((40,155),(40,140),(47,139),(51.8,139),(51.8,142));
        bool enteredForge = false;
        int waypoint = 0, rowsForTravel = 0; double travel = 0;
        while (!_controller.InTalkReach(npc) && travel < 70 && waypoint < route.Length)
        {
            var body = _controller.Authoritative;
            var point = route[waypoint];
            var toward = new Vector3((float)(point.X-body.XMm/1000.0),0,(float)(point.Z-body.ZMm/1000.0));
            if (toward.Length()<.5f) waypoint++;
            if (waypoint >= route.Length && !enteredForge)
            {
                _controller.Interact("door.forge_shed");
                _controller.SteerWorld(Vector3.Zero,Gait.Run,_camera);
                await Rcd3Wait(.7);
                route = Playthrough.WayTo(simulation,_controller.Authoritative,npc.Body);waypoint=0;enteredForge=true;
            }
            else _controller.SteerWorld(toward.Normalized(),Gait.Run,_camera);
            _camera.Yaw=Mathf.Atan2(-toward.X,-toward.Z);
            var aim=_avatar.GlobalPosition+Vector3.Up;
            camera.GlobalPosition=aim+new Vector3(2,1.5f,3);camera.LookAt(aim);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);travel+=GetProcessDeltaTime();
            npc=simulation.Npcs.Single(n=>n.Id==npcId);
            if (rowsForTravel++ % 30 == 0) File.AppendAllText(Path.Combine(directory,"travel.log"),$"tick={simulation.WorldTick} from={_controller.Authoritative} waypoint={waypoint}/{route.Length} target={point} intent={_controller.Intent}\n");
        }
        _controller.SteerWorld(Vector3.Zero,Gait.Run,_camera);
        await Rcd3Wait(.3);
        if (_controller.InTalkReach(npc))
        {
            for(int visit=0;visit<2;visit++)
            {
                _controller.Talk(npcId);await Rcd3Wait(.4);
                await Rcd3Shot(directory,"kera_replies_"+visit);
                evidence.Add(new { dialogue_visit=visit, node=simulation.Conversation?.NodeId, replies=_dialogue.Replies.ToArray() });
                _dialogue.Leave();await Rcd3Wait(.3);
            }
        }
        else evidence.Add(new { dialogue_unverified="Kera path did not reach talking distance", travel, waypoint });
        _rcd3Driving = false;
        File.WriteAllText(Path.Combine(directory,"details.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
    }

}

