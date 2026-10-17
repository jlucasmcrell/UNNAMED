// UNNAMED Presentation - Main's creator and opening: New Game's creator, the character's own body, and the road in (WP2's local part)
// Godot presentation only: the creator hands the session a spec; the opening only draws and words what the simulation holds (D-11)

using Godot;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Quests;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Settings;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation;

public partial class Main
{
    /// <summary>The opening's quest (content/quests/main/the_road_in.yaml) and the objective that is walking in.</summary>
    private const string RoadIn = "quest.main.the_road_in", Arrived = "o_arrive";

    /// <summary>PJ06 §5.2's zoom hint, once per profile: this long unzoomed, how to zoom. How to walk is WP10's new-game hint.</summary>
    private const double ZoomHintAfter = 60;

    /// <summary>How close the camera is over the body while it lies, in metres.</summary>
    private const float LyingZoom = 2.2f;

    private CreatorScreen _creator = null!;

    /// <summary>
    /// The world a new game will be (its seed, chosen at random as the session would choose it): the title's hollow is drawn with its
    /// ground scatter, so Begin finds the scatter already placed and the road is in the player's hands at once (PJ06 §5.4: within 10 s).
    /// </summary>
    private ulong _nextWorld;

    private ulong NextWorldSeed
    {
        get
        {
            var random = new RandomNumberGenerator();
            random.Randomize();
            while (_nextWorld == 0)
                _nextWorld = ((ulong)random.Randi() << 32) | random.Randi();
            return _nextWorld;
        }
    }

    /// <summary>The model the character is drawn with now, and its build pose (null when the figure cannot take one).</summary>
    private string? _avatarModel;
    private BuildPose? _avatarPose;

    /// <summary>The road in, while it is being walked: when Begin was pressed, whether the body still lies, how far it has got up.</summary>
    private ulong _begunMs;
    private bool _onTheRoad;
    private bool _lying;
    private double _rising = -1;
    private double _sinceBegin;
    private float _startZoom;
    private double _roadCheck;

    private void BuildCreator(HudIcons icons)
    {
        _creator = new CreatorScreen { Name = "Creator" };
        _creator.Bind(_session, _art, _bindings, icons);
        _creator.Begin = BeginCharacter;
        _creator.Leave = () => BackToTitle(CloseCreator);
        AddChild(_creator);
    }

    /// <summary>New Game: the creator, over a still world (its own set is drawn; the hollow behind it is not).</summary>
    private void OpenCreator()
    {
        var profile = SettingsStore.Current.Profile;
        _creator.RoadWalked = profile.RoadWalked;
        _creator.SkipRoad = profile.SkipRoad;
        GetViewport().Disable3D = true;
        FromTitle(_creator.Open);
    }

    private void CloseCreator()
    {
        _creator.Close();
        GetViewport().Disable3D = false;
    }

    /// <summary>Begin: the new character in a new world, drawn in their own body, and on the road in unless they skipped it.</summary>
    private void BeginCharacter(CharacterSpec spec)
    {
        _begunMs = Time.GetTicksMsec();
        _session.NewGame(spec, NextWorldSeed);
        _nextWorld = 0;
        ulong made = Time.GetTicksMsec();
        CloseCreator();
        _creator.Forget();
        if (_creator.RoadWalked && SettingsStore.Current.Profile.SkipRoad != (spec.Start == StartPoint.Waystone))
            SettingsStore.Set(SettingsStore.Current with { Profile = SettingsStore.Current.Profile with { SkipRoad = spec.Start == StartPoint.Waystone } });
        ulong drawn = Time.GetTicksMsec();
        Started();
        // A new game happens once: every hint waiting on it is queued now, not one (WP10).
        while (Hint(Domain.Guidance.HintTriggers.NewGame)) { }
        if (spec.Start == StartPoint.Road)
            BeginTheRoad();
        GD.Print($"UNNAMED creator: began {spec.Name} ({spec.BodyPresetId}, {spec.CallId ?? "no Call"}, {spec.Start} start): the new game in {made - _begunMs} ms, the world drawn in {Time.GetTicksMsec() - drawn} ms");
    }

    /// <summary>
    /// The character's own body, as their identity names it (content <c>body.*</c>, drawn by <c>player_bodies</c>): the model drawn again only
    /// when it is not the one already drawn, with its build's arm splay. A preset content does not define, or whose model this build does
    /// not have, keeps the body drawn at boot, so a save made with a body another build had still loads and plays.
    /// </summary>
    private void UseBody()
    {
        if (_session.Simulation is not { } simulation)
            return;
        // A character made before the creator (and every harness's) keeps the body the run was bound with.
        string preset = simulation.Player.Identity.BodyPresetId;
        var body = _session.Characters.Bodies.GetValueOrDefault(preset);
        string? model = body is not null ? _bindings.Bodies.Model(body, m => _art.RiggedFile(m, m + "_rigged.glb") is not null) : null;
        if (model is not null && model != _avatarModel && SkinnedFigure.Create(_art, _bindings, "player", model) is { } skinned)
        {
            var feet = _avatar.Position;
            _avatar.QueueFree();
            if (VisualOptions.BodyModifiers)
                BodyModifiers.PlantFeet(skinned.Skeleton, skinned, _ground.Height);
            _avatar = skinned;
            _avatar.Name = "Player";
            _avatar.Position = feet;
            AddChild(_avatar);
            _avatarModel = model;
            _avatarPose = null;
            _art.Coverage.Resolved("person", "player", model);
        }
        if (_avatarPose is null && _avatar is SkinnedFigure figure)
            _avatarPose = BuildPose.On(figure.Skeleton);
        if (_avatarPose is not null)
            _avatarPose.ArmSplayDegrees = body?.ArmSplayDegrees ?? 0;
    }

    /// <summary>
    /// The road in (PJ06 §5.1, OD-01 B-min; the "Leak" staging of DCP C.1a): the character lies in the grass below the crest with the
    /// hollow beyond, already in control; the first step gets them up. The location's line comes in, and nothing else is said.
    /// </summary>
    private void BeginTheRoad()
    {
        _onTheRoad = true;
        _sinceBegin = 0;
        _rising = -1;
        _lying = _avatar.SetWaking(0) && _avatar.GetUpSeconds > 0;
        _startZoom = _camera.TargetDistance;
        if (_lying)
        {
            // Close over the body in the grass, the hollow beyond it; the camera goes back to where it was once the body stands.
            _camera.Pitch = -0.42f;
            _camera.TargetDistance = LyingZoom;
        }
        _hud.Announce("Arrival", "The road to Ashen Hollow", HudStyle.Brass);
    }

    /// <summary>
    /// Read with the movement keys (Main.Input): while the body lies, the first step or jump gets it up instead of moving it, and it moves
    /// once it stands. True while the opening holds the body still.
    /// </summary>
    private bool HeldByTheOpening(Vector2 stick)
    {
        if (_begunMs != 0)
        {
            GD.Print($"UNNAMED opening: in control {Time.GetTicksMsec() - _begunMs} ms after Begin");
            _begunMs = 0;
        }
        if (_lying && _rising < 0 && (stick != Vector2.Zero || Input.IsActionJustPressed("jump")))
            GetUp();
        return _lying;
    }

    /// <summary>Start getting up (the first step, or a harness).</summary>
    private void GetUp()
    {
        if (_lying && _rising < 0)
            _rising = 0;
    }

    /// <summary>Each frame of the road in: the body getting up, the two hints when they are due, and the walk's end once the waystation is reached.</summary>
    private void DrawOpening(double delta)
    {
        if (!_onTheRoad || _session.Simulation is not { } simulation)
            return;
        _sinceBegin += delta;
        if (_lying)
        {
            if (_rising >= 0)
                _rising += delta;
            double length = Math.Max(0.1, _avatar.GetUpSeconds);
            float through = _rising < 0 ? 0 : (float)Math.Min(1, _rising / length);
            _avatar.SetWaking(through);
            if (through >= 1)
            {
                _avatar.SetWaking(null);
                _lying = false;
                if (Mathf.IsEqualApprox(_camera.TargetDistance, LyingZoom))
                    _camera.TargetDistance = _startZoom;
            }
        }
        // The movement keys are WP10's new-game hint (hint.moving); the opening adds only the zoom.
        if (SettingsStore.Current.Profile.Interface.Guidance != Guidance.Off && !_lying && _sinceBegin > ZoomHintAfter
            && Mathf.IsEqualApprox(_camera.TargetDistance, _startZoom))
            OpeningHint("opening.zoom", "Mouse wheel: closer or farther · all the way in for first person");
        _roadCheck += delta;
        if (_roadCheck < 0.5)
            return;
        _roadCheck = 0;
        var road = simulation.Quests.FirstOrDefault(q => q.Id == RoadIn);
        if (road is null || road.Objectives.FirstOrDefault(o => o.Id == Arrived)?.Status == ObjectiveStatus.Satisfied)
        {
            _onTheRoad = false;
            if (road is not null && !SettingsStore.Current.Profile.RoadWalked)
                SettingsStore.Set(SettingsStore.Current with { Profile = SettingsStore.Current.Profile with { RoadWalked = true } });
        }
    }

    /// <summary>A hint shown once to a profile (Settings' "show my learned hints again" brings it back).</summary>
    private void OpeningHint(string key, string words)
    {
        var prefs = SettingsStore.Current;
        if (prefs.Profile.Interface.SeenHints.Contains(key))
            return;
        _hud.Toast(words, 6);
        SettingsStore.Set(prefs with
        {
            Profile = prefs.Profile with { Interface = prefs.Profile.Interface with { SeenHints = prefs.Profile.Interface.SeenHints.Append(key).ToArray() } },
        });
    }

    private ulong _creatorNext;
    private int _creatorStep;

    /// <summary>
    /// <c>--creator-shots dir</c>: every creator step pictured as a player meets it - People, Body (each type and build), Identity (empty,
    /// then a suggested name), Call, Confirm with and without the skip - then Begin on the road: lying, getting up, standing. Each picture
    /// is taken a while after the step before it (a new body loads first). A harness: it writes no player settings, and quits when done.
    /// </summary>
    private void CreatorShots(string directory)
    {
        if (_creatorNext == 0)
        {
            _creatorNext = Time.GetTicksMsec() + 8000;
            SizeForCapture();
            SettingsStore.Set(new ClientPrefs { Profile = new ProfilePrefs { FirstLaunchDone = true } });
            _firstLaunch.Visible = false;
            OpenCreator();
        }
        if (Time.GetTicksMsec() < _creatorNext)
            return;
        // Every body content offers, type by type and build by build, the second type's middle build last: each its own model, whose
        // first showing loads it. A shot shows what the step before it chose.
        var typeOrder = _bindings.Bodies.Types.Select(t => t.Type).ToList();
        var bodies = _session.Characters.OfferedBodies(CharacterSpec.DefaultPeople)
            .OrderBy(b => typeOrder.IndexOf(b.Type) is var i && i >= 0 ? i : typeOrder.Count).ThenBy(b => b.Order).ToList();
        var walk = bodies.Where(b => b.Id != _bindings.Bodies.Default).ToList();
        if (bodies.Count > 0)
        {
            var lastType = bodies.Where(b => b.Type == bodies[^1].Type).ToList();
            walk.Add(lastType[lastType.Count / 2]);
        }
        string shown = "default";
        var list = new List<(string Shot, Action? Then, int NextMs)> { ("01_people", () => _creator.Go(CreatorStep.Body), 2000) };
        // each body full length from the front, three-quarter and side, then at the upper-body and face framings (the clipping QA's
        // contact sheet), the face again in the overcast light (the side-by-side with the Blender renders: soft light, no warm low sun)
        foreach (var body in walk)
        {
            list.Add(($"{list.Count + 1:00}_body_{shown}", () => _creator.Turn(-0.61f), 1500));
            list.Add(($"{list.Count + 1:00}_body_{shown}_three", () => _creator.Turn(-0.96f), 1500));
            list.Add(($"{list.Count + 1:00}_body_{shown}_side", () =>
            {
                _creator.Turn(1.57f);
                _creator.Frame(CreatorPreview.Framings[1].Zoom);
            }, 2500));
            list.Add(($"{list.Count + 1:00}_body_{shown}_upper", () => _creator.Frame(CreatorPreview.Framings[2].Zoom), 2500));
            list.Add(($"{list.Count + 1:00}_body_{shown}_face", () => _creator.Light(1), 1500));
            list.Add(($"{list.Count + 1:00}_body_{shown}_face_overcast", () =>
            {
                _creator.Light(0);
                _creator.Frame(CreatorPreview.Framings[0].Zoom);
                _creator.Use(_creator.Draft with { BodyPresetId = body.Id });
            }, 6000));
            shown = $"{body.Type}_{body.Build}";
        }
        list.Add(($"{list.Count + 1:00}_body_{shown}", () => _creator.Go(CreatorStep.Identity), 1500));
        int n = list.Count;
        string N(string shot) => $"{++n:00}_{shot}";
        var steps = list.Concat(new (string Shot, Action? Then, int NextMs)[]
        {
            (N("identity_empty"), () => _creator.Use(_creator.Draft with { Name = "Wenna Lind", Pronouns = Pronouns.She }), 1500),
            (N("identity_named"), () => _creator.Go(CreatorStep.Call), 1500),
            (N("call_wayfarer"), () => _creator.Use(_creator.Draft with { CallId = "call.smiths_hand" }), 1500),
            (N("call_smiths_hand"), () => _creator.Go(CreatorStep.Confirm), 1500),
            (N("confirm"), () =>
            {
                _creator.RoadWalked = true;
                _creator.Use(_creator.Draft with { Start = StartPoint.Waystone });
            }, 1500),
            (N("confirm_veteran"), () =>
            {
                _creator.RoadWalked = false;
                _creator.Use(_creator.Draft with { Start = StartPoint.Road });
                if (!_creator.PressBegin())
                    GD.PushError("UNNAMED creator shots: Begin was refused");
            }, 6000),
            (N("road_lying"), GetUp, 700),
            (N("road_getting_up"), null, 3000),
            (N("road_standing"), null, 0),
        }).ToArray();
        if (_creatorStep >= steps.Length)
        {
            GD.Print($"UNNAMED creator shots written to {directory}");
            GetTree().Quit(0);
            return;
        }
        var (shot, then, next) = steps[_creatorStep++];
        // A headless run has no frame to save: it walks the same steps, as a check that each one runs.
        if (DisplayServer.GetName() != "headless")
            SaveScreenshot(directory, shot);
        GD.Print($"UNNAMED creator shots: {shot}");
        then?.Invoke();
        _creatorNext = Time.GetTicksMsec() + (ulong)next;
    }
}
