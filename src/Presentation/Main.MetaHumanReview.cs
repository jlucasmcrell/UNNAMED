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

    private void InstallMetaHumanReview()
    {
        if (System.Environment.GetEnvironmentVariable("OTHERREACH_MH_REVIEW_OUT") is not { Length: > 0 } directory)
            return;
        if (!MetaHumanFigure.Available || DisplayServer.GetName() == "headless")
            throw new InvalidOperationException("MetaHuman review requires the native bundle and a real renderer");
        _mhReviewDirectory = Path.GetFullPath(directory);
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
            _mhReviewStats.Write(Path.Combine(_mhReviewDirectory, "performance"), new Dictionary<string, string>
            {
                ["settings"] = SettingsStore.Current.ToJson(), ["viewport"] = GetViewport().GetVisibleRect().Size.ToString(),
                ["npcs"] = JsonSerializer.Serialize(people), ["vsync"] = DisplayServer.WindowGetVsyncMode().ToString(),
                ["world"] = "Normal new game in starting area; simulation and native NPCs running; no capture readbacks during measurement",
            });
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
            File.WriteAllText(Path.Combine(_mhReviewDirectory, "complete.json"), JsonSerializer.Serialize(new
            {
                status = "CAPTURED_REQUIRES_FRAME_REVIEW_AND_OWNER_APPROVAL", worldShots = _mhReviewShot,
                player = ((MetaHumanFigure)_avatar).CharacterId, npcs = _npcs.States().Select(n => n.Id).ToArray(),
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("MH_NORMAL_GAME_REVIEW CAPTURED " + _mhReviewDirectory);
            GetTree().Quit();
            _mhReviewDirectory = null;
            return;
        }
        var shot = _mhReviewShots[_mhReviewShot];
        var native = shot.Figure.GetNode<Node3D>("Native");
        shot.Figure.SetFirstPerson(false);
        Vector3 aim = native.Call("bone_position", shot.Bone == "full" ? "pelvis" : shot.Bone == "hairline" ? "head" : shot.Bone).AsVector3();
        if (shot.Bone is "head" or "hairline") aim += Vector3.Up * (shot.Bone == "hairline" ? .15f : .10f);
        var direction = shot.Figure.GlobalBasis.Z.Rotated(Vector3.Up, shot.Yaw).Normalized();
        _camera.Camera.GlobalPosition = aim + direction * shot.Distance + Vector3.Up * (shot.Bone == "full" ? .10f : .025f);
        _camera.Camera.LookAt(aim);
        _mhReviewSeconds += GetProcessDeltaTime();
        if (_mhReviewSeconds < 1.2) return;
        string name = $"{_mhReviewShot + 10:00}_{shot.Person}_{shot.Figure.CharacterId}_{shot.Bone}_{(shot.Yaw == 0 ? "front" : "side")}";
        CaptureMetaHumanReview(name, new
        {
            person = shot.Person, character = shot.Figure.CharacterId, bone = shot.Bone, clip = shot.Figure.ClipState,
            actorPosition = shot.Figure.GlobalPosition.ToString(), cameraPosition = _camera.Camera.GlobalPosition.ToString(),
            worldTick = simulation.WorldTick, camera = "Normal game world and lighting; diagnostic camera only",
        }, () => { _mhReviewShot++; _mhReviewSeconds = 0; }, 0, () => Godot.Json.Stringify(native.Call("native_metrics")));
    }

    private void AddMetaHumanReviewShots(MetaHumanFigure figure, string person)
    {
        foreach (var (bone, distance) in new[] { ("full", 3.0f), ("head", .48f), ("hairline", .34f), ("neck_01", .36f), ("upperarm_l", .48f), ("hand_l", .38f), ("foot_l", .48f) })
            foreach (float yaw in new[] { 0f, Mathf.Pi / 2 })
                _mhReviewShots.Add((figure, person, bone, distance, yaw));
    }

    private async void CaptureMetaHumanReview(string name, object details, Action then, ulong waitMs, Func<string>? nativeMetrics = null)
    {
        _mhReviewBusy = true;
        try
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            SaveScreenshot(_mhReviewDirectory!, name);
            File.WriteAllText(Path.Combine(_mhReviewDirectory!, name + ".json"), JsonSerializer.Serialize(details, new JsonSerializerOptions { WriteIndented = true }));
            if (nativeMetrics is not null) File.WriteAllText(Path.Combine(_mhReviewDirectory!, name + ".native.json"), nativeMetrics());
            GD.Print("MH_NORMAL_GAME_REVIEW frame " + name);
            then();
            _mhReviewNext = Time.GetTicksMsec() + waitMs;
        }
        catch (Exception error)
        {
            GD.PushError("Native game review failed: " + error);
            GetTree().Quit(3);
        }
        finally { _mhReviewBusy = false; }
    }
}
