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
    private static readonly string[] MhPerfNames = ["baseline_28npc", "visibility_ranges", "animation_lod", "shadows", "groom_range"];

    private void InstallMetaHumanReview()
    {
        if (System.Environment.GetEnvironmentVariable("OTHERREACH_MH_REVIEW_OUT") is not { Length: > 0 } directory)
            return;
        if (!MetaHumanFigure.Available || DisplayServer.GetName() == "headless")
            throw new InvalidOperationException("MetaHuman review requires the native bundle and a real renderer");
        _mhReviewDirectory = Path.GetFullPath(directory);
        _mhPerfSequence = System.Environment.GetEnvironmentVariable("OTHERREACH_MH_PERF_SEQUENCE") == "1";
        _mhPerfSweep = System.Environment.GetEnvironmentVariable("OTHERREACH_MH_PERF_SWEEP") == "1";
        _npcs.SuppressPresentationForBenchmark = _mhPerfSequence;
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
        GD.Print("MH_NORMAL_GAME_REVIEW installed " + _mhReviewDirectory);
    }

    private void TickMetaHumanReview()
    {
        try { AdvanceMetaHumanReview(); }
        catch (Exception error)
        {
            GD.PushError("Native normal-game review failed: " + error);
            if (_mhReviewDirectory is { } directory) File.WriteAllText(Path.Combine(directory, "failed.txt"), error.ToString());
            _mhReviewDirectory = null;
            GetTree().Quit(3);
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
                    CaptureMetaHumanReview("02_creator_ada_full", new { character = "ada" }, () => _creator.Frame(1), 2000);
                    break;
                case 3:
                    CaptureMetaHumanReview("03_creator_ada_face", new { character = "ada" }, () =>
                    {
                        _creator.Use(_creator.Draft with { BodyPresetId = "body.veth_m.average" });
                        _creator.Frame(0);
                    }, 6000);
                    break;
                case 4:
                    CaptureMetaHumanReview("04_creator_mateo_full", new { character = "mateo" }, () => _creator.Frame(1), 2000);
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
        if (_mhReviewStep == 7)
        {
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
                        GetTree().Quit();
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
                        GetTree().Quit();
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
                player = ((MetaHumanFigure)_avatar).CharacterId, npcs = _npcs.States().Select(n => n.Id).ToArray(),
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("MH_NORMAL_GAME_REVIEW CAPTURED " + _mhReviewDirectory);
            GetTree().Quit();
            _mhReviewDirectory = null;
            return;
        }
        var shot = _mhReviewShots[_mhReviewShot];
        var native = shot.Figure.GetNode<Node3D>("Native");
        FrameMetaHumanReview(shot.Figure, shot.Bone, shot.Distance, shot.Yaw);
        _mhReviewSeconds += GetProcessDeltaTime();
        if (_mhReviewSeconds < 1.2) return;
        string name = $"{_mhReviewShot + 10:00}_{shot.Person}_{shot.Figure.CharacterId}_{shot.Bone}_{(shot.Yaw == 0 ? "front" : "side")}";
        CaptureMetaHumanReview(name, new
        {
            person = shot.Person, character = shot.Figure.CharacterId, bone = shot.Bone, clip = shot.Figure.ClipState,
            actorPosition = shot.Figure.GlobalPosition.ToString(), cameraPosition = _camera.Camera.GlobalPosition.ToString(),
            worldTick = simulation.WorldTick, camera = "Normal game world and lighting; diagnostic camera only",
            scatterVisible = shot.Bone == "full", closeupFoliageHiddenAfterPerformancePass = shot.Bone != "full",
        }, () => { _mhReviewShot++; _mhReviewSeconds = 0; }, 0, () => Godot.Json.Stringify(native.Call("native_metrics")));
    }

    private void FrameMetaHumanReview(MetaHumanFigure figure, string bone, float distance, float yaw)
    {
        figure.SetFirstPerson(false);
        var native = figure.GetNode<Node3D>("Native");
        _scatter.Visible = bone == "full"; // Close-up camera only: remove foliage occlusion after the full-world FPS pass.
        Vector3 aim = native.Call("bone_position", bone == "full" ? "pelvis" : bone == "head" ? "FACIAL_C_Nose" : bone == "hairline" ? "head" : bone).AsVector3();
        if (bone == "hairline") aim += Vector3.Up * .07f;
        if (bone == "foot_l") aim -= Vector3.Up * .05f;
        var direction = figure.GlobalBasis.Z.Rotated(Vector3.Up, yaw).Normalized();
        float rise = bone == "full" ? 1.55f : bone == "head" ? (yaw == 0 ? -.10f : .045f) : bone == "foot_l" ? .08f : .025f;
        _camera.Camera.GlobalPosition = aim + direction * distance + Vector3.Up * rise;
        _camera.Camera.LookAt(aim);
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
            GetTree().Quit(3);
        }
        finally { _mhReviewBusy = false; }
    }
}
