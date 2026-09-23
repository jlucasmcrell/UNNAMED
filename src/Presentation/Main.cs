// UNNAMED Presentation - boot, the frame loop, and input (ARCHITECTURE.md §2, §8.1; D-11)
// Godot presentation only: this file renders state and submits commands; it never writes state.

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Perf;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Spike;
using UNNAMED.Presentation.Ui;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

/// <summary>
/// The presentation shell. Each frame: read input and submit commands, advance the session by the frame's time,
/// then draw - the body where the last tick put it plus the frame's share of the next, the camera behind or inside it.
/// Run modes come after <c>--</c> on the command line: <c>--smoke</c> (headless boot and save round trip),
/// <c>--perf [--perf-out dir] [--perf-seconds n]</c> (the performance capture), <c>--spike</c> (the 2x2 km greybox).
/// </summary>
public partial class Main : Node3D
{
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
    private GameSession _session = null!;
    private HollowView _hollow = null!;
    private Avatar _avatar = null!;
    private CameraRig _camera = null!;
    private PlayerController _controller = null!;
    private Hud _hud = null!;
    private FrameStats? _stats;
    private PerfRun? _perf;
    private Smoke? _smoke;
    private string _perfOut = string.Empty;
    private Vector3 _lastFeet;
    private double _lastAlpha;

    public override void _Ready()
    {
        ParseArguments(OS.GetCmdlineUserArgs());
        if (_flags.Contains("--spike"))
        {
            AddChild(new SpikeScene(_options.GetValueOrDefault("--perf-out", DefaultPerfOut("spike")), Seconds()));
            return;
        }

        string contentRoot = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "content"));
        string profile = _flags.Contains("--smoke") || _flags.Contains("--perf")
            ? Path.Combine(OS.GetUserDataDir(), "scratch", $"run-{System.Environment.ProcessId}")
            : Path.Combine(OS.GetUserDataDir(), "saves", "default");
        try
        {
            _session = GameSession.Boot(new GameOptions(contentRoot, profile));
        }
        catch (ContentBootException e)
        {
            GD.PushError(e.Message);   // ARCHITECTURE.md §8.1: refuse to start, naming every file
            GetTree().Quit(2);
            return;
        }
        _session.NewGame("Wanderer");
        GD.Print($"UNNAMED boot: content {_session.Content.Version} ({_session.Content.Hash[..19]}...), region {_session.Setup.Layout.Id}, " +
                 $"{_session.Setup.Layout.CellKeys.Length} cells, seed {WorldSeed.Format(_session.Simulation!.World.WorldSeed)}");

        _hollow = new HollowView { Name = "Hollow" };
        AddChild(_hollow);
        _hollow.Build(_session.Setup.Layout);
        _avatar = new Avatar { Name = "Player" };
        AddChild(_avatar);
        _camera = new CameraRig { Name = "CameraRig" };
        AddChild(_camera);
        _hud = new Hud { Name = "Hud" };
        AddChild(_hud);
        _controller = new PlayerController(_session);
        Subscribe();
        Resync();
        DefineInput();

        if (_flags.Contains("--smoke"))
        {
            _smoke = new Smoke(_session, _controller, _camera, profile);
        }
        else if (_flags.Contains("--perf"))
        {
            _perfOut = _options.GetValueOrDefault("--perf-out", DefaultPerfOut("prototype"));
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);   // measure the headroom, not the refresh rate
            _stats = new FrameStats(GetViewport());
            _perf = new PerfRun(Seconds());
            _perf.SpawnProxies(this, _session.Setup.Layout);
        }
        else if (DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _Process(double delta)
    {
        if (_session?.Simulation is null)
            return;

        if (_perf is not null)
        {
            if (!_perf.Update(delta, _controller, _camera, _stats!, _session.Setup.Layout))
            {
                FinishPerf();
                return;
            }
        }
        else if (_smoke is not null)
        {
            if (_smoke.Update() is { } code)
            {
                GetTree().Quit(code);
                return;
            }
        }
        else
        {
            ReadInput();
        }

        // The smoke runs one tick per frame, so it finishes in a fraction of real time.
        var frame = _session.Frame(_smoke is not null ? _session.TickSeconds : delta);
        if (frame.AutosavedTo is { } slot)
            _hud.Toast($"Autosaved ({slot})", 2);
        Draw(frame.Alpha, delta);
        _stats?.Record(delta);
        if (_perf is { ScreenshotDue: true })
        {
            SaveScreenshot(_perfOut, _perf.SegmentName);
            _stats!.SkipNext();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_session?.Simulation is null || _perf is not null || _smoke is not null)
            return;
        switch (@event)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                _camera.Look(motion.Relative);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _camera.Zoom(-0.35f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _camera.Zoom(0.35f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when Input.MouseMode != Input.MouseModeEnum.Captured:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
        }
    }

    private void ReadInput()
    {
        var stick = new Vector2(
            Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
            Input.GetActionStrength("move_forward") - Input.GetActionStrength("move_back"));
        var gait = Input.IsActionPressed("sprint") ? Gait.Sprint : Input.IsActionPressed("walk") ? Gait.Walk : Gait.Run;
        _controller.Steer(_camera, stick, gait);

        if (Input.IsActionJustPressed("interact") && _controller.Focus(_camera) is { } door)
            _controller.Interact(door);
        if (Input.IsActionJustPressed("jump"))
            _avatar.Hop();
        if (Input.IsActionJustPressed("first_person"))
            _camera.ToggleFirstPerson();
        if (Input.IsActionJustPressed("shoulder_swap"))
            _camera.SwapShoulder();
        if (Input.IsActionJustPressed("debug_overlay"))
            _hud.DebugVisible = _hollow.DebugVisible = !_hud.DebugVisible;
        if (Input.IsActionJustPressed("quicksave"))
            QuickSave();
        if (Input.IsActionJustPressed("quickload"))
            QuickLoad();
        if (Input.IsActionJustPressed("release_mouse"))
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void Draw(double alpha, double delta)
    {
        var predicted = _controller.Predict(alpha);
        var feet = HollowView.ToGodot(predicted.XMm, predicted.YMm, predicted.ZMm);
        float speed = delta > 0 ? new Vector2(feet.X - _lastFeet.X, feet.Z - _lastFeet.Z).Length() / (float)delta : 0;
        // A tick boundary snaps the prediction back to zero progress; speed across it would read as a stumble.
        if (alpha < _lastAlpha)
            speed = _controller.Intent.IsMoving ? (float)_session.Setup.Movement.SpeedMmPerSecond(_controller.Intent.Gait) / 1000f : 0;
        _lastFeet = feet;
        _lastAlpha = alpha;

        _avatar.Pose(feet, PlayerController.FacingRadians(predicted.FacingMdeg), Math.Min(speed, 8f), delta);
        _camera.Follow(_avatar.Position, delta);
        _avatar.SetFirstPerson(_camera.EffectiveDistance < 0.4f);
        _hud.SetCrosshair(_camera.IsFirstPerson);

        var focus = _controller.Focus(_camera);
        _hud.SetPrompt(focus is null ? null : $"[E] {(_controller.IsOpen(focus.Key) ? "Close" : "Open")} the {Describe(focus.Key)}");

        var view = _session.Simulation!.Player;
        var stats = view.Stats;
        var pools = view.Progression.Pools;
        _hud.SetStatus(
            $"{view.Name}   Level {view.Progression.Level}   XP {view.Progression.LevelProgressXp}/{_session.Setup.Progression.Curve.ToReach(view.Progression.Level + 1)}" +
            (view.Progression.XpDebt > 0 ? $"   debt {view.Progression.XpDebt}" : "") +
            $"\nHealth {pools.Health ?? stats.HealthMax}/{stats.HealthMax}   Stamina {pools.Stamina ?? stats.StaminaMax}/{stats.StaminaMax}" +
            $"   Focus {pools.Focus ?? stats.FocusMax}/{stats.FocusMax}   Strain {pools.Strain}/{stats.StrainTolerance}");

        if (_hud.DebugVisible)
        {
            var body = _controller.Authoritative;
            var simulation = _session.Simulation;
            _hud.SetDebug(
                $"{Engine.GetFramesPerSecond()} fps   {delta * 1000:0.0} ms\n" +
                $"tick {simulation.WorldTick}   alpha {alpha:0.00}   pending {simulation.PendingCommands}\n" +
                $"body ({body.XMm / 1000.0:0.00}, {body.YMm / 1000.0:0.00}, {body.ZMm / 1000.0:0.00}) facing {body.FacingMdeg / 1000.0:0.0}\n" +
                $"cell {CellKey.OfWorld(body.XMm / 1000.0, body.ZMm / 1000.0)}\n" +
                $"camera {(_camera.IsFirstPerson ? "first person" : "third person")} {_camera.EffectiveDistance:0.00}/{_camera.TargetDistance:0.00} m, {_camera.Side}\n" +
                "tiers:\n" + string.Join("\n", simulation.CellTiers.Select(kv => $"  {kv.Key}  {kv.Value}")) +
                "\ndiscovered:\n" + string.Join("\n", view.Discoveries.Select(d => $"  {_session.DisplayName(d.LocationId)} (tick {d.Tick})")));
        }
    }

    private void Subscribe()
    {
        _session.Subscribe<BodyMoved>(_controller.OnBodyMoved);
        _session.Subscribe<DoorToggled>(e =>
        {
            _controller.OnDoorToggled(e);
            _hollow.SetDoor(e.DoorKey, e.Open);
        });
        _session.Subscribe<LocationDiscovered>(e => _hud.Toast($"Discovered: {_session.DisplayName(e.LocationId)}"));
        _session.Subscribe<ExperienceGained>(e => _hud.Toast(e.LevelsGained > 0 ? $"+{e.Awarded} XP - level {e.Level}!" : $"+{e.Awarded} XP", 3));
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is InteractCommand)
                _hud.Toast(e.Reason, 3);
        });
    }

    /// <summary>After a new game or a load: the only moments presentation copies the whole state.</summary>
    private void Resync()
    {
        _controller.Resync();
        foreach (var door in _session.Simulation!.Doors)
            _hollow.SetDoor(door.Site.Key, door.Open);
        var body = _controller.Authoritative;
        _camera.Yaw = PlayerController.FacingRadians(body.FacingMdeg) + Mathf.Pi;
        _lastFeet = HollowView.ToGodot(body.XMm, body.YMm, body.ZMm);
    }

    private void QuickSave()
    {
        try
        {
            _session.Save(SaveSlots.Quick);
            _hud.Toast("Saved");
        }
        catch (SaveException e)
        {
            _hud.Toast($"Save failed: {e.Message}");
        }
    }

    private void QuickLoad()
    {
        try
        {
            var result = _session.Load(SaveSlots.Quick);
            Resync();
            _hud.Toast(result.IsComplete ? "Loaded" : "Loaded, with losses - see the log");
            foreach (string problem in result.Report.Loss.Concat(result.Report.Warnings))
                GD.PushWarning(problem);
        }
        catch (SaveCorruptionException e)
        {
            _hud.Toast(e.BackupGenerations.IsEmpty ? "The save is corrupt" : "The save is corrupt; a backup exists");
        }
        catch (SaveException e)
        {
            _hud.Toast($"Load failed: {e.Message}");
        }
    }

    private void FinishPerf()
    {
        var notes = new Dictionary<string, string>
        {
            ["scene"] = "PROTOTYPE greybox: Ashen Hollow, 200 m x 200 m, 4 cells",
            ["script"] = _perf!.Summary,
            ["vsync"] = "disabled for the capture, so frame times show headroom rather than the refresh rate",
            ["screenshots"] = "one per segment, halfway through; the frame after each is left out of the numbers (the capture stalls the GPU)",
            ["gate"] = "owner ruling 2026-09-23: sustained 60 FPS at 1080p on RAZER's RTX 4070 Ti with OBS, H3 and other GPU workloads stopped",
        };
        string summary = _stats!.Write(_perfOut, notes);
        GD.Print($"UNNAMED perf capture written to {_perfOut}\n{summary}");
        GetTree().Quit(0);
    }

    /// <summary>Keep a picture of what a capture segment was measuring, next to its numbers.</summary>
    public static void SaveScreenshot(Viewport viewport, string directory, string name)
    {
        Directory.CreateDirectory(directory);
        viewport.GetTexture().GetImage().SavePng(Path.Combine(directory, name + ".png"));
    }

    private void SaveScreenshot(string directory, string name) => SaveScreenshot(GetViewport(), directory, name);

    private string Describe(string doorKey) => doorKey switch
    {
        "door.longhouse" => "longhouse door",
        "door.forge_shed" => "forge shed door",
        _ => doorKey,
    };

    private void ParseArguments(string[] arguments)
    {
        for (int i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] is "--perf-out" or "--perf-seconds" && i + 1 < arguments.Length)
                _options[arguments[i]] = arguments[++i];
            else
                _flags.Add(arguments[i]);
        }
    }

    private double Seconds() =>
        double.TryParse(_options.GetValueOrDefault("--perf-seconds"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double seconds) ? seconds : 75;

    private static string DefaultPerfOut(string name) =>
        Path.Combine(OS.GetUserDataDir(), "perf", $"{name}-{DateTime.UtcNow:yyyyMMdd-HHmmss}");

    private static void DefineInput()
    {
        void Bind(string action, params Key[] keys)
        {
            if (!InputMap.HasAction(action))
                InputMap.AddAction(action);
            foreach (var key in keys)
                InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
        Bind("move_forward", Key.W, Key.Up);
        Bind("move_back", Key.S, Key.Down);
        Bind("move_left", Key.A, Key.Left);
        Bind("move_right", Key.D, Key.Right);
        Bind("sprint", Key.Shift);
        Bind("walk", Key.Ctrl);
        Bind("interact", Key.E);
        Bind("jump", Key.Space);
        Bind("first_person", Key.V);
        Bind("shoulder_swap", Key.Q);
        Bind("debug_overlay", Key.F3);
        Bind("quicksave", Key.F5);
        Bind("quickload", Key.F9);
        Bind("release_mouse", Key.Escape);
    }
}
