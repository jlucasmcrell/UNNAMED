using System.Text.Json;
using Godot;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.MHCharacters;
using UNNAMED.Presentation.Perf;
using UNNAMED.Presentation.Settings;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation;

public partial class Main
{
    // An opt-in capture of the normal creator/world. No visual settings or simulation fixtures are substituted.
    private string? _mhReviewDirectory;
    private int _mhReviewStep, _mhReviewShot;
    private ulong _mhReviewNext, _mhReviewLast;
    private bool _mhReviewBusy;
    private FrameStats? _mhReviewStats;
    private double _mhReviewSeconds;
    private readonly List<(MetaHumanFigure Figure, string Person, string Bone, float Distance, float Yaw)> _mhReviewShots = new();
    private (string Name, string[] Buttons, double Seconds, string Expected)[] _mhReviewActions = [];
    private int _mhReviewAction;
    private bool _mhReviewActionActive, _mhReviewButtonsApplied;
    private double _mhReviewActionSeconds;
    private int _mhReviewActionFrames;
    private bool _mhPerfSequence;
    private int _mhPerfPhase;
    private bool _mhPerfSweep;
    private bool _mhFootwearPreview;
    private Camera3D? _mhDetailCamera;
    private readonly List<MetaHumanFigure> _mhFootwearFigures = [];
    private static readonly string[] MhPerfNames = ["baseline_28npc", "visibility_ranges", "animation_lod", "shadows", "groom_range"];

    private void InstallMetaHumanReview()
    {
        // The lore capture is a review-mode pass with its own harness: it sets the review variable so a launcher would take it for review, and
        // this sequence would otherwise drive the same window.
        if (_options.ContainsKey(LoreShotsOption))
            return;
        if (System.Environment.GetEnvironmentVariable("OTHERREACH_MH_REVIEW_OUT") is not { Length: > 0 } directory)
            return;
        if ((!MetaHumanFigure.Available && VisualOptions.ComparisonCast is null) || DisplayServer.GetName() == "headless")
            throw new InvalidOperationException("MetaHuman review requires the native bundle and a real renderer");
        _mhReviewDirectory = Path.GetFullPath(directory);
        _mhFootwearPreview = System.Environment.GetEnvironmentVariable("OTHERREACH_MH_FOOTWEAR_PREVIEW") == "1";
        _mhPerfSequence = System.Environment.GetEnvironmentVariable("OTHERREACH_MH_PERF_SEQUENCE") == "1";
        _mhPerfSweep = System.Environment.GetEnvironmentVariable("OTHERREACH_MH_PERF_SWEEP") == "1";
        _npcs.SuppressPresentationForBenchmark = _mhPerfSequence
            || System.Environment.GetEnvironmentVariable("OTHERREACH_RCD6_GRIP") == "1" // Isolated hand gate never shows NPCs.
            || (System.Environment.GetEnvironmentVariable("OTHERREACH_RCD4_WORLD") == "1"
                && System.Environment.GetEnvironmentVariable("OTHERREACH_RCD4_KEEP_NPCS") != "1");
        bool crouchToggle = SettingsStore.Current.Profile.Controls.IsToggle("crouch");
        _mhReviewActions = [
            ("walk", ["move_forward", "walk"], 1.1, "walk"),
            ("run", ["move_forward"], .9, "run"),
            ("sprint", ["move_forward", "sprint"], 1.0, "sprint"),
            ("stop", [], .5, "sword_ready|idle"),
            ("crouch_idle", ["crouch"], .6, "crouch_idle"),
            ("crouch_walk", crouchToggle ? ["move_forward", "walk"] : ["move_forward", "walk", "crouch"], .8, "crouch_walk"),
            ("stand", crouchToggle ? ["crouch"] : [], .5, "sword_ready|idle"),
            ("jump", ["jump"], .28, "jump_start|fall"),
            ("recover", [], 1.2, "sword_ready|idle"),
            ("sword_attack", ["attack"], .25, "sword_attack"),
            ("attack_recover", [], 1.5, "sword_ready|idle"),
            ("block", ["guard"], .4, "sword_block"),
            ("unblock", [], .3, "sword_ready|idle"),
            ("dodge", ["dodge"], .15, "dodge"),
        ];
        Directory.CreateDirectory(_mhReviewDirectory);
        File.WriteAllText(Path.Combine(_mhReviewDirectory, "client_prefs_loaded.json"), SettingsStore.Current.ToJson());
        File.WriteAllText(Path.Combine(_mhReviewDirectory, "launch.json"), JsonSerializer.Serialize(new
        {
            executable = OS.GetExecutablePath(), arguments = OS.GetCmdlineUserArgs(),
            renderer = RenderingServer.GetCurrentRenderingMethod(), viewport = GetViewport().GetVisibleRect().Size.ToString(),
            assets = MetaHumanFigure.AssetRoot, settings = "Normal launch: loaded owner client_prefs; no harness defaults",
        }, new JsonSerializerOptions { WriteIndented = true }));
        _mhReviewNext = Time.GetTicksMsec() + 3000;
        if(OS.GetEnvironment("OTHERREACH_RCD6_EXISTING_PARTY_UI")=="1")
        {
            if(Continue(catchUp:false) is null)throw new InvalidDataException("Existing-party gate has no restorable copied save");
            _mhReviewStep=7;_mhReviewNext=Time.GetTicksMsec()+10000;
        }
        else if ((System.Environment.GetEnvironmentVariable("OTHERREACH_WORLD_LIGHTING_DIAGNOSTIC") == "1"
            || System.Environment.GetEnvironmentVariable("OTHERREACH_MH_OWNER_FIXES_DIAGNOSTIC") == "1"
            || System.Environment.GetEnvironmentVariable("OTHERREACH_RCD3_REVIEW") == "1")
            && System.Environment.GetEnvironmentVariable("OTHERREACH_LIGHTING_FAST") == "1")
        {
            OpenCreator();
            bool feminine = System.Environment.GetEnvironmentVariable("OTHERREACH_MH_REVIEW_PLAYER") != "mateo";
            _creator.Use(_creator.Draft with { Name = feminine ? "Ada" : "Mateo", BodyPresetId = feminine ? "body.veth_f.average" : "body.veth_m.average", Pronouns = feminine ? Pronouns.She : Pronouns.He, Start = StartPoint.Waystone });
            if (!_creator.PressBegin()) throw new InvalidOperationException("Lighting diagnostic Begin refused");
            _mhReviewStep = 7;
            _mhReviewNext = Time.GetTicksMsec() + 10000;
        }
        GD.Print("MH_NORMAL_GAME_REVIEW installed " + _mhReviewDirectory);
    }

    private object[] ReviewBodyChoices(string expectedType)
    {
        var choices = _creator.FindChildren("*", nameof(Button), true, false).OfType<Button>()
            .Where(b => b.GetMeta("ui_id", "").AsString().StartsWith("creator.type.", StringComparison.Ordinal)).ToArray();
        if (choices.Count(b => b.ButtonPressed) != 1 || !choices.Single(b => b.ButtonPressed).GetMeta("ui_id").AsString().EndsWith(expectedType, StringComparison.Ordinal))
            throw new InvalidDataException("Creator body selection does not match its displayed identity");
        return choices.Select(b => (object)new { id = b.GetMeta("ui_id").AsString(), text = b.Text, selected = b.ButtonPressed }).ToArray();
    }

    private void TickMetaHumanReview()
    {
        try { AdvanceMetaHumanReview(); }
        catch (Exception error)
        {
            GD.PushError("Native normal-game review failed: " + error);
            if (_mhReviewDirectory is { } directory) File.WriteAllText(Path.Combine(directory, "failed.txt"), error.ToString());
            _mhReviewDirectory = null;
            CaptureQuit.At(this, 3);
        }
    }

    private void AdvanceMetaHumanReview()
    {
        if (_mhReviewDirectory is null || _mhReviewBusy || Time.GetTicksMsec() < _mhReviewNext)
            return;
        if (_mhReviewStep < 7)
        {
            string chosen = System.Environment.GetEnvironmentVariable("OTHERREACH_MH_REVIEW_PLAYER") == "ada" ? "ada" : "mateo";
            switch (_mhReviewStep++)
            {
                case 0:
                    CaptureMetaHumanReview("00_title", new { stage = "normal title" }, OpenCreator, 6000);
                    break;
                case 1:
                    CaptureMetaHumanReview("01_creator_people", new { stage = "normal creator" }, () =>
                    {
                        _creator.Go(CreatorStep.Body);
                        _creator.Use(_creator.Draft with { BodyPresetId = "body.veth_f.average" });
                        _creator.Frame(0);
                    }, 6000);
                    break;
                case 2:
                    CaptureMetaHumanReview("02_creator_ada_full", new { character = "ada", body_choices = ReviewBodyChoices("veth_f") }, () => _creator.Frame(1), 2000);
                    break;
                case 3:
                    CaptureMetaHumanReview("03_creator_ada_face", new { character = "ada" }, () =>
                    {
                        _creator.Use(_creator.Draft with { BodyPresetId = "body.veth_m.average" });
                        _creator.Frame(0);
                    }, 6000);
                    break;
                case 4:
                    CaptureMetaHumanReview("04_creator_mateo_full", new { character = "mateo", body_choices = ReviewBodyChoices("veth_m") }, () => _creator.Frame(1), 2000);
                    break;
                case 5:
                    CaptureMetaHumanReview("05_creator_mateo_face", new { character = "mateo" }, () =>
                    {
                        _creator.Use(_creator.Draft with
                        {
                            Name = chosen == "ada" ? "Ada" : "Mateo", BodyPresetId = chosen == "ada" ? "body.veth_f.average" : "body.veth_m.average",
                            Pronouns = chosen == "ada" ? Pronouns.She : Pronouns.He,
                            Start = _creator.SkipRoad ? StartPoint.Waystone : StartPoint.Road,
                        });
                        _creator.Go(CreatorStep.Confirm);
                        _creator.Frame(0);
                    }, 2000);
                    break;
                case 6:
                    CaptureMetaHumanReview("06_creator_confirm", new { character = chosen }, () =>
                    {
                        if (!_creator.PressBegin()) throw new InvalidOperationException("Native creator refused Begin");
                    }, 10000);
                    break;
            }
            return;
        }
        if (_session.Simulation is not { } simulation)
            throw new InvalidOperationException("Native review has no world after Begin");
        if (_mhReviewStep == 7 && System.Environment.GetEnvironmentVariable("OTHERREACH_RCD3_REVIEW") == "1")
        {
            _mhReviewBusy = true;
            RunRcd3Review(_mhReviewDirectory);
            return;
        }
        if (_mhReviewStep == 7 && System.Environment.GetEnvironmentVariable("OTHERREACH_MH_OWNER_FIXES_DIAGNOSTIC") == "1")
        {
            var source = _camera.Camera;
            var camera = new Camera3D { Fov = source.Fov, Near = source.Near, Far = source.Far, CullMask = source.CullMask, Attributes = source.Attributes, Environment = source.Environment };
            AddChild(camera);
            source.GetNode<OmniLight3D>("CharacterFill").Reparent(camera, false);
            camera.MakeCurrent();
            _scatter.Visible = false; // Staged close-ups; not a gameplay change.
            _avatar.Visible = false; // The staged figure occupies the same inspection point.
            var figures = new Godot.Collections.Array<MetaHumanFigure>();
            foreach (string id in new[] { "ada", "mateo" })
            {
                var figure = MetaHumanFigure.Create(_art, _bindings, "player", "mh:" + id)
                    ?? throw new InvalidOperationException("Missing owner-fix review character");
                AddChild(figure);
                var feet = new Vector3(430, _ground.Height(430, 758) + .35f, 758);
                figure.Pose(feet, 0, 0, 0);
                figure.Hold("item.weapon.rusted_sword");
                figure.Visible = false;
                figures.Add(figure);
            }
            var diagnostic = (Node)GD.Load<GDScript>("res://MHCharacters/Tests/OwnerFixesReview.gd").New();
            AddChild(diagnostic);
            diagnostic.CallDeferred("run", figures, camera, _mhReviewDirectory);
            _mhReviewDirectory = null;
            return;
        }
        if (_mhReviewStep == 7 && System.Environment.GetEnvironmentVariable("OTHERREACH_WORLD_LIGHTING_DIAGNOSTIC") == "1")
        {
            var diagnostic = (Node)GD.Load<GDScript>("res://capture_tools/world_lighting_probe.gd").New();
            AddChild(diagnostic);
            var positions = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            foreach (var (pointName, x, z) in new[] { ("spawn", 430f, 758f), ("lit_edge_provisional", 480f, 758f), ("blackvein", 438f, 666f), ("ember_hound", 528f, 750f), ("out_200m", 630f, 758f) })
                positions.Add(new Godot.Collections.Dictionary { ["name"] = pointName, ["position"] = new Vector3(x, _ground.Height(x, z) + 1.7f, z) });
            diagnostic.CallDeferred("run", this, _camera.Camera, _mhReviewDirectory, positions, _avatar);
            _mhReviewDirectory = null;
            return;
        }
        if (_mhReviewStep == 7 && _mhFootwearPreview)
        {
            // Owner's footwear close-ups in the normal game world; no FPS measurement or pacing changes.
            _mhReviewActions = [];
            var source = _camera.Camera;
            _mhDetailCamera = new Camera3D { Name = "FootwearAuditCamera", Fov = source.Fov, Near = source.Near, Far = source.Far, CullMask = source.CullMask, Attributes = source.Attributes, Environment = source.Environment };
            AddChild(_mhDetailCamera);
            _mhDetailCamera.MakeCurrent();
            source.GetNode<OmniLight3D>("CharacterFill").Reparent(_mhDetailCamera, false);
            if (System.Environment.GetEnvironmentVariable("OTHERREACH_MH_EXTREMITIES_DIAGNOSTIC") == "1")
            {
                // Bare/native and dressed source comparisons in this packaged world, without a perf phase.
                _scatter.Visible = false;
                var diagnostic = (Node3D)GD.Load<GDScript>("res://MHCharacters/Tests/ExtremitiesReview.gd").New();
                AddChild(diagnostic);
                Vector3 feet = _avatar.Position + Vector3.Right * 5;
                feet.Y = _ground.Height(feet.X, feet.Z);
                diagnostic.CallDeferred("run", MetaHumanFigure.AssetRoot, _mhReviewDirectory, _mhDetailCamera, feet);
                _mhReviewDirectory = null;
                return;
            }
            foreach (string id in new[] { "ada", "mateo" })
            {
                var figure = MetaHumanFigure.Create(_art, _bindings, "player", "mh:" + id)
                    ?? throw new InvalidOperationException("Missing footwear review character");
                figure.Name = $"Review_{id}_leather";
                AddChild(figure);
                figure.Visible = false;
                _mhFootwearFigures.Add(figure);
                Vector3 feet = _avatar.Position + Vector3.Right * 5;
                feet.Y = _ground.Height(feet.X, feet.Z);
                figure.Pose(feet, 0, 0, 0);
                foreach (var (bone, distance) in new[] { ("full", 3.8f), ("hand_l", .4f), ("hand_r", .4f), ("foot_l", .65f), ("foot_r", .65f), ("pelvis", .95f) })
                    foreach (float yaw in new[] { 0f, Mathf.Pi / 2 })
                        _mhReviewShots.Add((figure, $"{id}_leather", bone, distance, yaw));
                foreach (float yaw in new[] { 0f, Mathf.Pi / 2 })
                    _mhReviewShots.Add((figure, $"{id}_leather_walk", "foot_l", .65f, yaw));
            }
            _mhReviewStep = 9;
        }
        if (_mhReviewStep == 7)
        {
            // Optional headroom measurement, matching FrontierPerf. Capture pacing and saved preferences stay unchanged.
            if (System.Environment.GetEnvironmentVariable("OTHERREACH_MH_PERF_UNCAPPED") == "1")
            {
                string pacingPath = Path.Combine(_mhReviewDirectory, $"pacing_{_mhPerfPhase}.json");
                int beforeCap = Engine.MaxFps;
                string beforeVsync = DisplayServer.WindowGetVsyncMode().ToString();
                Engine.MaxFps = 0;
                DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
                File.WriteAllText(pacingPath, JsonSerializer.Serialize(new
                {
                    beforeCap, beforeVsync, engineMaxFps = Engine.MaxFps,
                    vsync = DisplayServer.WindowGetVsyncMode().ToString(), simulationTickSeconds = _session.TickSeconds,
                    mode = "Normal creator Begin path; owner visual preferences; FrontierPerf-style uncapped headroom; no capture branch",
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            // Start warming after the first real world Draw has created all native NPCs.
            _mhReviewNext = Time.GetTicksMsec() + 10000;
            _mhReviewStep = 8;
            return;
        }
        if (_mhReviewStep == 8)
        {
            if (_mhReviewStats is null)
            {
                _mhReviewStats = new FrameStats(GetViewport()) { Segment = "starting_area_owner_settings" };
                _mhReviewLast = Time.GetTicksUsec();
                return;
            }
            ulong now = Time.GetTicksUsec();
            double seconds = (now - _mhReviewLast) / 1_000_000.0;
            _mhReviewLast = now;
            _mhReviewSeconds += seconds;
            _mhReviewStats!.Record(seconds);
            if (_mhReviewSeconds < 60) return;
            var people = _npcs.States().Select(n => new { n.Id, n.State, n.Speed }).ToArray();
            _mhReviewStats.Write(Path.Combine(_mhReviewDirectory, _mhPerfSweep ? $"performance_{_mhPerfPhase}_{MhPerfNames[_mhPerfPhase]}" : _mhPerfSequence ? $"performance_{(_mhPerfPhase == 0 ? "0npc" : "28npc")}" : "performance"), new Dictionary<string, string>
            {
                ["settings"] = SettingsStore.Current.ToJson(), ["viewport"] = GetViewport().GetVisibleRect().Size.ToString(),
                ["npcs"] = JsonSerializer.Serialize(people), ["simulation_npc_count"] = simulation.Npcs.Count().ToString(),
                ["npc_presentation_count"] = people.Length.ToString(), ["camera_transform"] = _camera.Camera.GlobalTransform.ToString(), ["vsync"] = DisplayServer.WindowGetVsyncMode().ToString(),
                ["world"] = "Normal new game in starting area; simulation and native NPCs running; no capture readbacks during measurement",
                ["npc_performance_stage"] = _npcs.PerformanceStage.ToString(),
                ["engine_max_fps"] = Engine.MaxFps.ToString(), ["simulation_tick_seconds"] = _session.TickSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["uncapped_headroom"] = (System.Environment.GetEnvironmentVariable("OTHERREACH_MH_PERF_UNCAPPED") == "1").ToString(),
            });
            SaveMetaHumanMaterialAudit(_mhPerfSequence || _mhPerfSweep ? $"world_{_mhPerfPhase}" : "world");
            if (_mhPerfSweep)
            {
                int phase = _mhPerfPhase;
                CaptureMetaHumanReview($"benchmark_{phase}_{MhPerfNames[phase]}", new
                {
                    stage = phase, description = MhPerfNames[phase], presentedNpcs = people.Length,
                    camera = _camera.Camera.GlobalTransform.ToString(), foliage = "Normal owner settings",
                }, () =>
                {
                    if (phase < 4)
                    {
                        _mhPerfPhase++;
                        _npcs.SetPerformanceStage(_mhPerfPhase);
                        _mhReviewStats = null;
                        _mhReviewSeconds = 0;
                        _mhReviewStep = 7;
                    }
                    else
                    {
                        File.WriteAllText(Path.Combine(_mhReviewDirectory!, "complete.json"), "{\"status\":\"PERFORMANCE_SWEEP_COMPLETE\",\"phases\":[0,1,2,3,4]}");
                        _mhReviewDirectory = null;
                        CaptureQuit.At(this);
                    }
                }, 0);
                return;
            }
            if (_mhPerfSequence)
            {
                int phase = _mhPerfPhase;
                CaptureMetaHumanReview($"benchmark_{(phase == 0 ? "0npc" : "28npc")}", new
                {
                    presentedNpcs = people.Length, simulatedNpcs = simulation.Npcs.Count(),
                    camera = _camera.Camera.GlobalTransform.ToString(), foliage = "Normal owner density; no capture exclusions",
                }, () =>
                {
                    if (phase == 0)
                    {
                        _npcs.SuppressPresentationForBenchmark = false;
                        _mhPerfPhase = 1;
                        _mhReviewStats = null;
                        _mhReviewSeconds = 0;
                        _mhReviewStep = 7;
                    }
                    else
                    {
                        File.WriteAllText(Path.Combine(_mhReviewDirectory!, "complete.json"), "{\"status\":\"BASELINE_SEQUENCE_COMPLETE\",\"phases\":[0,28]}");
                        _mhReviewDirectory = null;
                        CaptureQuit.At(this);
                    }
                }, 0);
                return;
            }
            if (_avatar is not MetaHumanFigure player) throw new InvalidOperationException("Player is not a native MetaHuman");
            var actors = people.Select(n => (Id: n.Id, Figure: _npcs.FigureOf(n.Id) as MetaHumanFigure))
                .Where(n => n.Figure is not null && n.Figure.Visible)
                .OrderBy(n => n.Figure!.GlobalPosition.DistanceSquaredTo(player.GlobalPosition)).Take(4).ToArray();
            if (actors.Length < 4) throw new InvalidOperationException($"Only {actors.Length} native NPCs present; four required");
            AddMetaHumanReviewShots(player, "player");
            foreach (var actor in actors) AddMetaHumanReviewShots(actor.Figure!, actor.Id);
            _mhReviewStep = 9;
            _mhReviewSeconds = 0;
        }
        if (_mhReviewShot >= _mhReviewShots.Count)
        {
            if (_mhReviewAction < _mhReviewActions.Length)
            {
                AdvanceMetaHumanReviewAction(simulation.WorldTick);
                return;
            }
            ReleaseMetaHumanReviewButtons();
            _scatter.Visible = true;
            File.WriteAllText(Path.Combine(_mhReviewDirectory, "complete.json"), JsonSerializer.Serialize(new
            {
                status = "CAPTURED_REQUIRES_FRAME_REVIEW_AND_OWNER_APPROVAL", worldShots = _mhReviewShot,
                normalInputChecks = _mhReviewAction,
                footwearPreview = _mhFootwearPreview, fpsMeasured = !_mhFootwearPreview,
                player = ((MetaHumanFigure)_avatar).CharacterId, npcs = _npcs.States().Select(n => n.Id).ToArray(),
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("MH_NORMAL_GAME_REVIEW CAPTURED " + _mhReviewDirectory);
            CaptureQuit.At(this);
            _mhReviewDirectory = null;
            return;
        }
        var shot = _mhReviewShots[_mhReviewShot];
        var native = shot.Figure.GetNode<Node3D>("Native");
        if (_mhFootwearPreview)
        {
            foreach (var figure in _mhFootwearFigures) figure.Visible = figure == shot.Figure;
            native.Call("sample_clip", shot.Person.EndsWith("_walk", StringComparison.Ordinal) ? "walk" : "idle", .5);
        }
        FrameMetaHumanReview(shot.Figure, shot.Bone, shot.Distance, shot.Yaw);
        _mhReviewSeconds += GetProcessDeltaTime();
        if (_mhReviewSeconds < 1.2) return;
        string name = $"{_mhReviewShot + 10:00}_{shot.Person}_{shot.Figure.CharacterId}_{shot.Bone}_{(shot.Yaw == 0 ? "front" : "side")}";
        CaptureMetaHumanReview(name, new
        {
            person = shot.Person, character = shot.Figure.CharacterId, bone = shot.Bone, clip = shot.Figure.ClipState,
            actorPosition = shot.Figure.GlobalPosition.ToString(), cameraPosition = (_mhDetailCamera ?? _camera.Camera).GlobalPosition.ToString(),
            worldTick = simulation.WorldTick, camera = "Normal game world and lighting; diagnostic camera only",
            scatterVisible = _scatter.Visible, closeupFoliageHiddenAfterPerformancePass = !_mhFootwearPreview && shot.Bone != "full", footwearPreview = _mhFootwearPreview,
        }, () => { _mhReviewShot++; _mhReviewSeconds = 0; }, 0, () => Godot.Json.Stringify(native.Call("native_metrics")));
    }

    private void FrameMetaHumanReview(MetaHumanFigure figure, string bone, float distance, float yaw)
    {
        figure.SetFirstPerson(false);
        var native = figure.GetNode<Node3D>("Native");
        _scatter.Visible = bone == "full" && !_mhFootwearPreview; // Diagnostic close-ups keep the grass out of the camera.
        Vector3 aim = native.Call("bone_position", bone == "full" ? "pelvis" : bone == "head" ? "FACIAL_C_Nose" : bone == "hairline" ? "head" : bone).AsVector3();
        if (bone == "hairline") aim += Vector3.Up * .07f;
        if (bone.StartsWith("foot_", StringComparison.Ordinal)) aim -= Vector3.Up * .05f;
        var direction = figure.GlobalBasis.Z.Rotated(Vector3.Up, yaw).Normalized();
        float rise = bone == "full" ? 1.55f : bone == "head" ? (yaw == 0 ? -.10f : .045f) : bone.StartsWith("foot_", StringComparison.Ordinal) ? .08f : .025f;
        _camera.Camera.GlobalPosition = aim + direction * distance + Vector3.Up * rise;
        _camera.Camera.LookAt(aim);
        if (_mhDetailCamera is not null) _mhDetailCamera.GlobalTransform = _camera.Camera.GlobalTransform;
    }

    // Inject before ReadInput so just-pressed actions are read on their actual engine frame.
    private void ApplyMetaHumanReviewInputs()
    {
        if (_mhReviewDirectory is null || !_mhReviewActionActive || _mhReviewButtonsApplied) return;
        ReleaseMetaHumanReviewButtons();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        foreach (string action in _mhReviewActions[_mhReviewAction].Buttons) Input.ActionPress(action);
        _mhReviewButtonsApplied = true;
    }

    private void ReleaseMetaHumanReviewButtons()
    {
        foreach (string action in _mhReviewActions.SelectMany(a => a.Buttons).Distinct()) Input.ActionRelease(action);
    }

    private void AdvanceMetaHumanReviewAction(long worldTick)
    {
        if (!_mhReviewActionActive)
        {
            _mhReviewActionActive = true;
            _mhReviewButtonsApplied = false;
            _mhReviewActionSeconds = 0;
            _mhReviewActionFrames = 0;
            return;
        }
        if (!_mhReviewButtonsApplied) return;
        var player = (MetaHumanFigure)_avatar;
        FrameMetaHumanReview(player, "full", 3.0f, 0);
        // The first delta can contain the previous screenshot/GPU skin readback stall.
        // Let input reach an ordinary rendered frame before timing its held duration.
        if (++_mhReviewActionFrames == 1) return;
        _mhReviewActionSeconds += GetProcessDeltaTime();
        var action = _mhReviewActions[_mhReviewAction];
        if (_mhReviewActionSeconds < action.Seconds) return;
        string actual = player.ClipState;
        bool matches = action.Expected.Split('|').Any(expected => actual.StartsWith(expected, StringComparison.Ordinal));
        ReleaseMetaHumanReviewButtons();
        CaptureMetaHumanReview($"{80 + _mhReviewAction:00}_input_{action.Name}", new
        {
            normalInput = true, buttons = action.Buttons, expectedClip = action.Expected, actualClip = actual,
            matches, worldTick, actorPosition = player.GlobalPosition.ToString(), rawSpeed = _rawSpeed,
            measuredFrames = _mhReviewActionFrames - 1, activeSeconds = _mhReviewActionSeconds,
        }, () =>
        {
            _mhReviewAction++;
            _mhReviewActionActive = false;
            if (!matches) throw new InvalidOperationException($"Normal {action.Name} input played {actual}; expected {action.Expected}");
        }, 0, () => Godot.Json.Stringify(player.GetNode<Node3D>("Native").Call("native_metrics")));
    }

    private void AddMetaHumanReviewShots(MetaHumanFigure figure, string person)
    {
        foreach (var (bone, distance) in new[] { ("full", 3.0f), ("head", .48f), ("hairline", .34f), ("neck_01", .36f), ("upperarm_l", .48f), ("hand_l", .38f), ("foot_l", .70f) })
            foreach (float yaw in new[] { 0f, Mathf.Pi / 2 })
                _mhReviewShots.Add((figure, person, bone, distance, yaw));
    }

    private void SaveMetaHumanMaterialAudit(string name)
    {
        var rows = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (Node native in GetTree().Root.FindChildren("Native", "Node3D", true, false))
        {
            if (!native.HasMethod("material_audit")) continue;
            var row = native.Call("material_audit").AsGodotDictionary();
            row["node_path"] = native.GetPath().ToString();
            rows.Add(row);
        }
        File.WriteAllText(Path.Combine(_mhReviewDirectory!, name + ".materials.json"), Godot.Json.Stringify(rows, "  "));
        var environments = GetTree().Root.FindChildren("*", "WorldEnvironment", true, false).OfType<WorldEnvironment>()
            .Where(node => node.Environment is not null).Select(node => new
            {
                path = node.GetPath().ToString(), ambientSource = node.Environment.AmbientLightSource.ToString(),
                ambientEnergy = node.Environment.AmbientLightEnergy, skyContribution = node.Environment.AmbientLightSkyContribution,
                sdfgi = node.Environment.SdfgiEnabled, sdfgiEnergy = node.Environment.SdfgiEnergy,
                skyLight = node.Environment.SdfgiReadSkyLight, exposure = node.Environment.TonemapExposure,
                tonemap = node.Environment.TonemapMode.ToString(),
            });
        var lights = GetTree().Root.FindChildren("*", "Light3D", true, false).OfType<Light3D>().Select(node => new
        {
            path = node.GetPath().ToString(), visible = node.IsVisibleInTree(), energy = node.LightEnergy,
            colour = node.LightColor.ToString(), shadows = node.ShadowEnabled, transform = node.GlobalTransform.ToString(),
        });
        var viewports = GetTree().Root.FindChildren("*", "SubViewport", true, false).OfType<SubViewport>()
            .Select(node => new { path = node.GetPath().ToString(), updateMode = node.RenderTargetUpdateMode.ToString(), processMode = node.ProcessMode.ToString() });
        File.WriteAllText(Path.Combine(_mhReviewDirectory!, name + ".lighting.json"), JsonSerializer.Serialize(new { environments, lights, viewports }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async void CaptureMetaHumanReview(string name, object details, Action then, ulong waitMs, Func<string>? nativeMetrics = null)
    {
        _mhReviewBusy = true;
        try
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (OS.GetEnvironment("OTHERREACH_REVIEW_WGC_STILLS") == "1")
                await RequestReviewWgcStill(_mhReviewDirectory!, name);
            else
                SaveScreenshot(_mhReviewDirectory!, name);
            if (name is "03_creator_ada_face" or "05_creator_mateo_face") SaveMetaHumanMaterialAudit(name);
            File.WriteAllText(Path.Combine(_mhReviewDirectory!, name + ".json"), JsonSerializer.Serialize(details, new JsonSerializerOptions { WriteIndented = true }));
            if (nativeMetrics is not null) File.WriteAllText(Path.Combine(_mhReviewDirectory!, name + ".native.json"), nativeMetrics());
            GD.Print("MH_NORMAL_GAME_REVIEW frame " + name);
            then();
            _mhReviewNext = Time.GetTicksMsec() + waitMs;
        }
        catch (Exception error)
        {
            GD.PushError("Native game review failed: " + error);
            if (_mhReviewDirectory is { } directory) File.WriteAllText(Path.Combine(directory, "failed.txt"), error.ToString());
            CaptureQuit.At(this, 3);
        }
        finally { _mhReviewBusy = false; }
    }
}

