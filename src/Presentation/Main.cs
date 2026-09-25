// UNNAMED Presentation - boot, the frame loop, and input (ARCHITECTURE.md §2, §8.1; D-11)
// Godot presentation only: this file renders state and submits commands; it never writes state.

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
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
/// <c>--perf [--perf-out dir] [--perf-seconds n]</c> (the performance capture), <c>--spike</c> (the 2x2 km greybox),
/// <c>--ui-shots dir</c>, and <c>--playthrough dir</c> then <c>--playthrough-verify dir</c> (M6: the acceptance run, and its relaunch).
/// <c>--asset-root dir</c> names the asset pipeline's workspace, for its HUD and effect art (the owner's M6 playtest).
/// </summary>
public partial class Main : Node3D
{
    /// <summary>Input actions for the formula keys, 4 to 6.</summary>
    private static readonly string[] CastKeys = { "cast_1", "cast_2", "cast_3" };

    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
    private GameSession _session = null!;
    private HollowView _hollow = null!;
    private Figure _avatar = null!;
    private Art.ArtLibrary _art = Art.ArtLibrary.Empty;
    private Art.ArtBindings _bindings = Art.ArtBindings.Empty;
    private CameraRig _camera = null!;
    private PlayerController _controller = null!;
    private Hud _hud = null!;
    private ItemsView _items = null!;
    private CreaturesView _creatures = null!;
    private CraftingView _crafting = null!;
    private NpcsView _npcs = null!;
    private InventoryPanel _inventory = null!;
    private DialoguePanel _dialogue = null!;
    private JournalPanel _journal = null!;
    private QuestDebugPanel _questDebug = null!;
    private CharacterPanel _character = null!;
    private HelpPanel _help = null!;
    private SavesPanel _saves = null!;
    private ProjectilesView _projectiles = null!;
    private Art.MagicEffects _magicEffects = null!;
    private Audio.SoundBank _sounds = null!;
    private Audio.SoundEvents _soundEvents = null!;
    private AssetCatalog _assets = AssetCatalog.Empty;
    private FrameStats? _stats;
    private PerfRun? _perf;
    private Smoke? _smoke;
    private InputCheck? _inputCheck;
    private UiShots? _shots;
    private Playthrough? _play;
    private DeltaShots? _delta;
    private LayoutCheck? _layout;
    private string _perfOut = string.Empty;
    private int _perfStruck, _perfDied;
    private bool _scripted;
    private (string Slot, SaveCopy Copy, LoadResult Result)? _continued;
    private Vector3 _lastFeet;
    private double _lastAlpha;

    public override void _Ready()
    {
        // Whatever stops the game starting is said to the player, with where the log is, rather than leaving a blank window (M-07).
        try
        {
            Start();
        }
        catch (Exception e)
        {
            CannotStart(e is ContentBootException ? "Its content does not validate." : e.Message, e);
        }
    }

    /// <summary>Where Godot writes this run's log (<c>debug/file_logging/log_path</c>): named wherever a failure asks a tester to look (M-07).</summary>
    public static string LogPath =>
        ProjectSettings.GlobalizePath(ProjectSettings.GetSetting("debug/file_logging/log_path", "user://logs/godot.log").AsString());

    /// <summary>The game cannot start: the log gets everything, the player a window saying why and where the log is; then it quits.</summary>
    private void CannotStart(string why, Exception e)
    {
        GD.PushError($"UNNAMED cannot start: {e}");
        if (DisplayServer.GetName() != "headless")
            OS.Alert($"Otherreach cannot start.\n\n{why}\n\nThe log, with the details: {LogPath}", "Otherreach");
        GetTree().Quit(2);
    }

    private void Start()
    {
        ParseArguments(OS.GetCmdlineUserArgs());
        if (_flags.Contains("--spike"))
        {
            AddChild(new SpikeScene(_options.GetValueOrDefault("--perf-out", DefaultPerfOut("spike")), Seconds()));
            return;
        }
        if (_options.TryGetValue("--art-gallery", out string? gallery))
        {
            var catalog = AssetCatalog.Load(_options.GetValueOrDefault("--asset-root"), Home());
            AddChild(new Art.ArtGallery(new Art.ArtLibrary(catalog.Root), Art.ArtBindings.Load(Art.ArtBindings.ResourcePath), Path.GetFullPath(gallery)));
            return;
        }

        // --content-root: another copy of the content, for a harness that needs different data (the layout check's full pack).
        string contentRoot = _options.GetValueOrDefault("--content-root") is { } content ? Path.GetFullPath(content) : Path.Combine(Home(), "content");
        string? playthrough = _options.GetValueOrDefault("--playthrough") ?? _options.GetValueOrDefault("--playthrough-verify");
        string profile = playthrough is not null ? Path.Combine(Path.GetFullPath(playthrough), "profile")
            : _flags.Contains("--smoke") || _flags.Contains("--input-check") || _flags.Contains("--perf") || _options.ContainsKey("--ui-shots")
              || _options.ContainsKey("--delta-shots") || _options.ContainsKey("--layout-check")
            ? Path.Combine(OS.GetUserDataDir(), "scratch", $"run-{System.Environment.ProcessId}")
            : _options.GetValueOrDefault("--profile") is { } chosen ? Path.GetFullPath(chosen)
            : Path.Combine(OS.GetUserDataDir(), "saves", "default");
        // Bad content refuses to start, naming every file (ARCHITECTURE.md §8.1); so does a profile another copy of the game holds.
        _session = GameSession.Boot(new GameOptions(contentRoot, profile) { LockProfile = true });
        _session.SubscriberFailed += SubscriberFailed;
        bool verify = _options.ContainsKey("--playthrough-verify");
        bool scripted = playthrough is not null || _flags.Contains("--smoke") || _flags.Contains("--input-check") || _flags.Contains("--perf")
                        || _options.ContainsKey("--ui-shots")
                        || _options.ContainsKey("--delta-shots") || _options.ContainsKey("--layout-check");
        // No run of a harness takes the mouse - but the input check, which checks who has it.
        _scripted = (scripted && !_flags.Contains("--input-check")) || _options.ContainsKey("--resume-shots");
        // A scripted run plays one world from its start - the acceptance playthrough a fixed one, so it is the same run every time (M6).
        // A player's run begins at the start screen (the Phase-1 technical audit, B-01); the relaunch check continues as a player would.
        if (scripted && !verify)
            _session.NewGame("Wanderer", _options.ContainsKey("--playthrough") || _options.ContainsKey("--delta-shots") ? Playthrough.Seed : 0);
        GD.Print($"UNNAMED boot: content {_session.Content.Version} ({_session.Content.Hash[..19]}...), region {_session.Setup.Layout.Id}, " +
                 $"{_session.Setup.Layout.CellKeys.Length} cells");

        _assets = AssetCatalog.Load(_options.GetValueOrDefault("--asset-root"), Path.GetDirectoryName(contentRoot)!);
        GD.Print(_assets.Root is { } art ? $"UNNAMED assets: HUD and effect art from {art}" : "UNNAMED assets: no asset workspace - greybox HUD and effects");
        if (_assets.Problems.Count > 0)
            GD.PushWarning($"UNNAMED assets: {_assets.Problems.Count} manifest entries withheld, greybox stands in: {string.Join("; ", _assets.Problems)}");
        // The asset library's models, clips and materials, by the presentation's bindings (the Phase-1 asset integration).
        _art = new Art.ArtLibrary(_assets.Root);
        _bindings = Art.ArtBindings.Load(Art.ArtBindings.ResourcePath);
        _art.Withhold(_bindings.Withheld);

        int before = GetChildCount();
        try
        {
            BuildScene();
        }
        catch (Exception e)
        {
            // The asset library is generated elsewhere: a record it cannot read must cost its art, never the game (the Phase-1 technical
            // audit, H-02). Whatever was built goes, and the scene is built again in greybox.
            GD.PushError($"UNNAMED art: building the scene from the asset library failed ({e.GetType().Name}: {e.Message}); drawing greybox");
            for (int i = GetChildCount() - 1; i >= before; i--)
            {
                var child = GetChild(i);
                RemoveChild(child);
                child.QueueFree();
            }
            _art = Art.ArtLibrary.Empty;
            _bindings = Art.ArtBindings.Empty;
            _assets = AssetCatalog.Empty;
            BuildScene();
        }
        _sounds = new Audio.SoundBank { Name = "Sounds" };
        _sounds.Load(_assets.Root);
        if (_sounds.Problems.Count > 0)
            GD.PushWarning($"UNNAMED audio: {_sounds.Problems.Count} sound entries left out, malformed: {string.Join(", ", _sounds.Problems)}");
        AddChild(_sounds);
        _soundEvents = new Audio.SoundEvents { Name = "SoundEvents" };
        AddChild(_soundEvents);
        _soundEvents.Bind(_session, _bindings, _sounds);
        _projectiles.Arrived = _soundEvents.Arrived;
        GD.Print(_sounds.Count > 0 ? $"UNNAMED audio: {_sounds.Count} sounds from {Audio.SoundBank.Manifest}" : "UNNAMED audio: no sound set - silent");
        _controller = new PlayerController(_session);
        _inventory = new InventoryPanel { Name = "Inventory" };
        _inventory.Bind(_session, _controller);
        AddChild(_inventory);
        _dialogue = new DialoguePanel { Name = "Dialogue" };
        _dialogue.Bind(_session);
        AddChild(_dialogue);
        _journal = new JournalPanel { Name = "Journal" };
        AddChild(_journal);
        _questDebug = new QuestDebugPanel { Name = "QuestDebug" };
        AddChild(_questDebug);
        _character = new CharacterPanel { Name = "Character" };
        _character.Bind(_session);
        AddChild(_character);
        _help = new HelpPanel { Name = "Help", Files = $"Saves: {profile}\nThe log, to send with a problem report: {LogPath}" };
        GD.Print($"UNNAMED files: saves in {profile}; the log at {LogPath}");
        AddChild(_help);
        _hud.UseCompassDial(_assets.Icon("ui.hud.compass"));
        _hud.UseIcons(new HudIcons(_assets, _bindings.Icons));
        _inventory.UseIcons(new HudIcons(_assets, _bindings.Icons));
        _saves = new SavesPanel { Name = "Saves" };
        _saves.Bind(_session);
        _saves.NewGame = () =>
        {
            _session.NewGame("Wanderer");
            Started();
        };
        _saves.Load = (slot, copy) => LoadChosen(slot, copy);
        _saves.Quit = () => GetTree().Quit(0);
        _saves.Closed = _saves.Close;
        AddChild(_saves);
        Subscribe();
        DefineInput();

        if (verify)
        {
            _continued = Continue();
            if (_continued is null)
            {
                GD.PushError("UNNAMED playthrough verification: Continue loaded nothing");
                GetTree().Quit(1);
                return;
            }
        }
        else if (!scripted)
        {
            var choice = _session.StartChoice();
            GD.Print(choice.Continue is { } next
                ? $"UNNAMED start screen: Continue would load {next.Slot} ({next.Copy}); {choice.Saves.Length} saves in {profile}"
                : $"UNNAMED start screen: no save to continue; {choice.Saves.Length} saves in {profile}");
            _hud.Visible = false;   // no world yet: nothing to show but the choice
            _saves.OpenStart();
            return;
        }
        else
        {
            Started();
        }

        if (_flags.Contains("--smoke"))
        {
            _smoke = new Smoke(_session, _controller, _camera, profile);
        }
        else if (_options.TryGetValue("--layout-check", out string? layout))
        {
            _layout = new LayoutCheck(_session, _controller, _camera, GetViewport(), _inventory, _dialogue, _saves, _help, _character, Path.GetFullPath(layout));
        }
        else if (_flags.Contains("--input-check"))
        {
            _inputCheck = new InputCheck(_session, _controller, _camera, () => Modal, slot => LoadChosen(slot, SaveCopy.Current), _dialogue, _inventory,
                _character, _saves);
        }
        else if (_options.TryGetValue("--ui-shots", out string? shots))
        {
            _shots = new UiShots(_session, _controller, _camera, _inventory, _dialogue, _journal, _questDebug, shots);
        }
        else if (playthrough is not null)
        {
            // One tick a frame, at the tick rate: the run is the same every time, and plays in real time (toasts and all).
            Engine.MaxFps = (int)Math.Round(1 / _session.TickSeconds);
            _play = new Playthrough(_session, _controller, _camera, _dialogue, Path.GetFullPath(playthrough), verify, _continued);
        }
        else if (_options.TryGetValue("--delta-shots", out string? deltaShots))
        {
            // One tick a frame at the tick rate, like the playthrough: each picture is taken at the same moment every run.
            Engine.MaxFps = (int)Math.Round(1 / _session.TickSeconds);
            _delta = new DeltaShots(_session, _controller, _camera, _inventory, _dialogue, _character, _help, _projectiles, Path.GetFullPath(deltaShots), _assets.Root);
        }
        else if (_flags.Contains("--perf"))
        {
            _perfOut = _options.GetValueOrDefault("--perf-out", DefaultPerfOut("prototype"));
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);   // measure the headroom, not the refresh rate
            _stats = new FrameStats(GetViewport());
            // --perf-route extended: the play the gate's route avoids, and past 300 s so the autosave is in it (the Phase-1 audit, P-01, P-07).
            _perf = new PerfRun(Seconds(), _options.GetValueOrDefault("--perf-route") == "extended"
                ? new PerfActivities(_session, _controller, _camera, _dialogue, _inventory)
                : null);
            _perf.SpawnProxies(this, _session.Setup.Layout);
            _session.Subscribe<HitResolved>(e => _perfStruck += e.Target == _session.Simulation!.PlayerId ? 1 : 0);
            _session.Subscribe<PlayerDied>(_ => _perfDied++);
        }
    }

    private readonly Dictionary<Type, int> _handlerFailures = new();

    /// <summary>
    /// A view's handler threw while the tick ran (H-02). The session's bus isolated it, so the tick went on; this says so - the first time
    /// for each kind of event, then every hundredth.
    /// </summary>
    private void SubscriberFailed(Exception exception, object @event)
    {
        var kind = @event.GetType();
        int seen = _handlerFailures[kind] = _handlerFailures.GetValueOrDefault(kind) + 1;
        if (seen == 1 || seen % 100 == 0)
            GD.PushError($"UNNAMED: a handler for {kind.Name} threw ({seen} so far); the tick went on without it: {exception}");
    }

    /// <summary>The world's scene - ground, buildings, figures, effects - drawn from the asset library where it can be, greybox where not.</summary>
    private void BuildScene()
    {
        _hollow = new HollowView { Name = "Hollow" };
        _hollow.Bind(_art, _bindings);
        AddChild(_hollow);
        _hollow.Build(_session.Setup.Layout);
        if (Art.SkinnedFigure.Create(_art, _bindings, "player") is { } skinned)
        {
            _avatar = skinned;
        }
        else
        {
            var greybox = new Avatar();
            Art.HeldWeapon.Arm(greybox, _art, _bindings);
            _avatar = greybox;
        }
        _avatar.Name = "Player";
        AddChild(_avatar);
        _camera = new CameraRig { Name = "CameraRig" };
        AddChild(_camera);
        _hud = new Hud { Name = "Hud" };
        AddChild(_hud);
        _items = new ItemsView { Name = "Items" };
        _items.Bind(_art, _bindings);
        AddChild(_items);
        _items.BuildContainers(_session.Setup.Layout);
        _creatures = new CreaturesView { Name = "Creatures" };
        _creatures.Bind(_art, _bindings);
        AddChild(_creatures);
        _crafting = new CraftingView { Name = "Crafting" };
        _crafting.Bind(_art, _bindings);
        AddChild(_crafting);
        _crafting.Build(_session.Setup.Layout);
        _npcs = new NpcsView { Name = "Npcs" };
        _npcs.Bind(_art, _bindings);
        AddChild(_npcs);
        GD.Print($"UNNAMED art: {_art.Used.Count} assets drawn from the library at boot; {_art.Problems.Count} withheld or unavailable (greybox stands in)");
        _projectiles = new ProjectilesView { Name = "Projectiles" };
        AddChild(_projectiles);
        _projectiles.Bind(_assets);
        _magicEffects = new Art.MagicEffects { Name = "MagicEffects" };
        _magicEffects.Bind(_assets, _bindings);
        AddChild(_magicEffects);
        if (_avatar is Avatar glowing && _magicEffects.HasCastCharge)
            glowing.WorkingGlow = false;
    }

    public override void _Process(double delta)
    {
        if (_options.GetValueOrDefault("--resume-shots") is { } resumeShots && _session is not null)
            ResumeShots(resumeShots);
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
        else if (_layout is not null)
        {
            switch (_layout.Update())
            {
                case "done":
                    GetTree().Quit(0);
                    return;
                case "failed":
                    GetTree().Quit(1);
                    return;
                case { } shot:
                    SaveScreenshot(_layout.Directory, shot);
                    break;
            }
        }
        else if (_inputCheck is not null)
        {
            if (_inputCheck.Update() is { } code)
            {
                GetTree().Quit(code);
                return;
            }
            if (_inputCheck.Reading)
                ReadInput();
        }
        else if (_shots is not null)
        {
            switch (_shots.Update())
            {
                case "done":
                    GD.Print($"UNNAMED ui shots written to {_shots.Directory}");
                    GetTree().Quit(0);
                    return;
                case "failed":
                    SaveScreenshot(_shots.Directory, "failed");
                    GetTree().Quit(1);
                    return;
                case { } shot:
                    SaveScreenshot(_shots.Directory, shot);
                    break;
            }
        }
        else if (_delta is not null)
        {
            switch (_delta.Update())
            {
                case "done":
                    GD.Print($"UNNAMED delta shots written to {_delta.Directory}");
                    GetTree().Quit(0);
                    return;
                case "failed":
                    SaveScreenshot(_delta.Directory, "failed");
                    GetTree().Quit(1);
                    return;
                case { } shot:
                    SaveScreenshot(_delta.Directory, shot);
                    break;
            }
        }
        else if (_play is not null)
        {
            switch (_play.Update())
            {
                case "done":
                    GetTree().Quit(0);
                    return;
                case "failed":
                    SaveScreenshot(_play.Directory, "failed");
                    GetTree().Quit(1);
                    return;
                case { } shot:
                    SaveScreenshot(_play.Directory, shot);
                    break;
            }
        }
        else
        {
            ReadInput();
        }
        KeepContainerInReach();

        // The smoke and the playthrough run one tick per frame: the smoke finishes in a fraction of real time, and the playthrough is
        // the same run every time, whatever the frame rate.
        var frame = _session.Frame(_smoke is not null || _play is not null || _delta is not null ? _session.TickSeconds : delta);
        if (_stats is not null)
        {
            if (frame.AutosaveTaken is { } taken)
                _stats.Mark($"autosave to {taken} taken at {_session.PlaytimeSeconds:0.0} s of play");
            foreach (var save in frame.Saves)
                _stats.Mark($"{save.Slot} written in the background{(save.Failure is { } why ? $" - FAILED: {why}" : "")}");
        }
        foreach (var save in frame.Saves)
        {
            if (save.Failure is not { } failed)
            {
                _hud.Toast(save.Auto ? $"Autosaved ({save.Slot})" : "Saved", 2);
                continue;
            }
            GD.PushError($"UNNAMED {(save.Auto ? "autosave" : "save")} to {save.Slot} failed: {failed}");
            _hud.Toast(save.Auto ? $"Autosave failed: {failed} It is tried again shortly. The log: {LogPath}" : $"Save failed: {failed} (the log: {LogPath})", 8);
        }
        Draw(frame.Alpha, delta);
        UpdateMouse();
        _stats?.Record(delta);
        if (_perf is { ScreenshotDue: true })
        {
            SaveScreenshot(_perfOut, _perf.SegmentName);
            _stats!.SkipNext();
        }
    }

    /// <summary>
    /// Where the game's files lie: the repository in the editor (<c>content/</c> and the untracked <c>assets/</c> at its root), and in an
    /// exported build the executable's own folder, which carries <c>content/</c> and the asset workspace's <c>assets/</c> beside it.
    /// </summary>
    private static string Home() => OS.HasFeature("template")
        ? OS.GetExecutablePath().GetBaseDir()
        : Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", ".."));

    /// <summary>What the run heard, for the harnesses: how much of the sound set played, and any family the mapping asked for that the set lacks.</summary>
    public override void _ExitTree()
    {
        // Quitting while a save is being written finishes it first (P-01); its commit is atomic even if it were cut off.
        if (_session is not null && !_session.WaitForSaves(TimeSpan.FromSeconds(10)))
            GD.PushWarning("UNNAMED: a save was still being written after 10 s at quit; the previous save stands until the next boot finishes it");
        if (_sounds is { Count: > 0 })
            GD.Print($"UNNAMED audio: {_sounds.Played.Count} of {_sounds.Count} sounds played this run; asked for and missing: " +
                     (_sounds.Unknown.Count == 0 ? "none" : string.Join(", ", _sounds.Unknown)) +
                     (OS.GetEnvironment("UNNAMED_AUDIO_LOG") is { Length: > 0 } log ? WriteAudioLog(log) : ""));
    }

    private string WriteAudioLog(string path)
    {
        File.WriteAllLines(path, _sounds.Played);
        return $" (played IDs in {path})";
    }

    /// <summary>
    /// A panel that takes the keys and the mouse is open: a conversation, the inventory (on the character, a container, a trader or a
    /// station), the character sheet or the saves list (the Phase-1 technical audit, L-27). While one is, no gameplay key reaches the world,
    /// and the world runs on (the owner's ruling: nothing pauses for a panel). The journal, the help and the quest debugger are overlays:
    /// they take no keys, and play goes on under them.
    /// </summary>
    public bool Modal => _inventory.Visible || _dialogue.Visible || _character.Visible || _saves.Visible;

    /// <summary>Escape, outside any panel: the player wants the pointer until they click back into the world.</summary>
    private bool _mouseFreed;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_session?.Simulation is null || _scripted)
            return;
        switch (@event)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured && !Modal:
                _camera.Look(motion.Relative);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } when !Modal:
                _camera.Zoom(-0.35f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } when !Modal:
                _camera.Zoom(0.35f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when _mouseFreed && !Modal:
                _mouseFreed = false;   // the mouse is taken back at the end of the frame, so this click is not also a swing
                break;
        }
    }

    /// <summary>
    /// Who has the mouse, decided once a frame from what is open - never set here and there (L-27): the game's while nothing modal is open
    /// and the player has not freed it, the pointer's otherwise. A harness run and a headless one leave it alone.
    /// </summary>
    private void UpdateMouse()
    {
        if (_scripted || DisplayServer.GetName() == "headless")
            return;
        var wanted = Modal || _mouseFreed ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        if (Input.MouseMode != wanted)
            Input.MouseMode = wanted;
    }

    private void ReadInput()
    {
        var combat = _session.Simulation!.Combat;
        bool modal = Modal;

        // A panel's own keys: the saves list's, a conversation's (the number keys answer, Escape walks away - M4), the inventory's and
        // the sheet's; and the overlays, open over anything.
        if (_saves.Visible && (Input.IsActionJustPressed("saves") || Input.IsActionJustPressed("release_mouse")))
            _saves.Close();
        if (_dialogue.Visible)
        {
            for (int n = 1; n <= 9; n++)
            {
                if (Input.IsActionJustPressed($"reply_{n}"))
                    _dialogue.AnswerNumber(n);
            }
            if (Input.IsActionJustPressed("release_mouse"))
                _dialogue.Leave();
        }
        if (Input.IsActionJustPressed("inventory") && !_dialogue.Visible && !_saves.Visible)
        {
            if (_inventory.Visible)
                CloseInventory();
            else
                OpenInventory(null);
        }
        if (Input.IsActionJustPressed("take_all") && _inventory.Visible)
            _inventory.TakeAll();
        if (Input.IsActionJustPressed("character") && !_dialogue.Visible && !_saves.Visible)
        {
            _character.Visible = !_character.Visible;
            _character.Refresh();
        }
        if (Input.IsActionJustPressed("help"))
            _help.Toggle();
        if (Input.IsActionJustPressed("debug_overlay"))
            _hud.DebugVisible = _hollow.DebugVisible = !_hud.DebugVisible;
        if (Input.IsActionJustPressed("journal"))
            _journal.Visible = !_journal.Visible;
        if (Input.IsActionJustPressed("quest_debug"))
        {
            _questDebug.Visible = !_questDebug.Visible;
            _questDebug.Refresh(_session, 0, now: true);
        }
        if (modal)
        {
            // Nothing reaches the world: the character stands, and lowers a raised guard.
            _controller.Steer(_camera, Vector2.Zero, Gait.Run);
            if (combat.Blocking)
                _controller.Guard(false);
            return;
        }

        if (Input.IsActionJustPressed("saves"))
        {
            _saves.OpenInGame();
            return;
        }
        var stick = new Vector2(
            Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
            Input.GetActionStrength("move_forward") - Input.GetActionStrength("move_back"));
        var gait = Input.IsActionPressed("sprint") ? Gait.Sprint : Input.IsActionPressed("walk") ? Gait.Walk : Gait.Run;

        // Combat: the left button swings or shoots, the right holds a guard (or aims a bow), C dodges, H uses a salve.
        // A swing, a guard and an aimed bow all go where the camera looks.
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        bool holding = captured && Input.IsActionPressed("guard");
        bool swing = captured && Input.IsActionJustPressed("attack");
        int slot = captured ? Array.FindIndex(CastKeys, key => Input.IsActionJustPressed(key)) : -1;
        _controller.Steer(_camera, stick, gait, faceCamera: holding || swing || slot >= 0 || combat.Phase == CombatPhase.Windup);
        if (swing)
            _controller.Attack();
        if (slot >= 0)
        {
            // A working goes where the camera looks, like a swing (M3e).
            var hotbar = _controller.Formulas();
            if (slot < hotbar.Length)
                _controller.Cast(hotbar[slot]);
            else
                _hud.Toast("No formula known for that key", 2);
        }
        bool guard = holding && !combat.Weapon.Ranged;
        if (guard != combat.Blocking && (!guard || combat.Phase == CombatPhase.Idle))
            _controller.Guard(guard);
        if (captured && Input.IsActionJustPressed("dodge"))
        {
            var wish = stick.LengthSquared() > 0.01f ? stick.Normalized() : Vector2.Zero;
            _controller.Dodge(_camera.GroundForward * wish.Y + _camera.GroundRight * wish.X);
        }
        if (Input.IsActionJustPressed("use") && !_controller.UseConsumable())
            _hud.Toast("Nothing to use", 2);

        if (Input.IsActionJustPressed("interact") && _controller.FocusOn(_camera) is { } focus)
        {
            switch (focus.Kind)
            {
                case FocusKind.Door:
                    _controller.Interact(focus.Key);
                    break;
                case FocusKind.Container:
                    OpenInventory(focus.Key);
                    break;
                case FocusKind.Item:
                    _controller.PickUp(focus.Key);
                    break;
                case FocusKind.Node:
                    _controller.Gather(focus.Key);
                    break;
                case FocusKind.Station:
                    OpenStation(focus.Key);
                    break;
                case FocusKind.Npc:
                    if (DownedCompanion(focus.Key) is not null)
                        _controller.Revive(focus.Key);
                    else
                        _controller.Talk(focus.Key);
                    break;
                case FocusKind.Switch:
                    _controller.Interact(focus.Key);
                    break;
            }
        }
        if (Input.IsActionJustPressed("companion_order"))
        {
            // One key for every companion on their feet: those following wait, those waiting follow.
            var up = _session.Simulation!.Companions.Where(c => c.Condition == CompanionCondition.Up).ToList();
            if (up.Count == 0)
                _hud.Toast("No one is with you", 2);
            foreach (var companion in up)
                _controller.Order(companion.NpcId, companion.Order == CompanionOrder.Follow ? CompanionOrder.Wait : CompanionOrder.Follow);
        }
        if (Input.IsActionJustPressed("jump"))
            _controller.Jump();
        if (Input.IsActionJustPressed("crouch"))
            _controller.Crouch(_session.Simulation!.Posture.Stance != UNNAMED.Domain.Spatial.Stance.Crouched);
        if (Input.IsActionJustPressed("first_person"))
            _camera.ToggleFirstPerson();
        if (Input.IsActionJustPressed("shoulder_swap"))
            _camera.SwapShoulder();
        if (Input.IsActionJustPressed("quicksave"))
            QuickSave();
        if (Input.IsActionJustPressed("quickload"))
            QuickLoad();
        if (Input.IsActionJustPressed("release_mouse"))
            _mouseFreed = true;
    }

    /// <summary>
    /// Whatever the panel was opened from - a container, a corpse, a station, a trader - it stays open only while the body is in reach
    /// of where that was (the owner's M6 playtest: the same rule for all of them). A corpse searched bare closes its column, and the
    /// panel still closes once the body walks away. The inventory opened with its own key has nowhere to be near, and stays.
    /// </summary>
    private void KeepContainerInReach()
    {
        if (!_inventory.Visible || _inventory.Anchor is not { } anchor)
            return;
        if (_inventory.OpenContainer is { } open && _session.Simulation!.Containers.All(c => c.Site.Key != open))
            _inventory.ContainerGone();
        long reach = _session.Setup.Items.Inventory.ReachMm + (_inventory.OpenTrader is not null ? _session.Setup.Movement.BodyRadiusMm : 0);
        if (Math.Sqrt(Math.Pow(anchor.XMm - _controller.Authoritative.XMm, 2) + Math.Pow(anchor.ZMm - _controller.Authoritative.ZMm, 2)) > reach)
            CloseInventory();
    }

    /// <summary>
    /// The aiming reticle (the owner's M6 playtest): while a bow is drawn or held aimed, or a thrown working's tell runs - and always in
    /// first person with a bow - a ring on the point the simulation says the shot would stop at, projected from the shoulder's height.
    /// With the debug overlay on, a line from the bow to that point; never otherwise. True while aiming.
    /// </summary>
    private bool Aim(Simulation simulation, CombatView combat)
    {
        long range = 0;
        bool free = Input.MouseMode == Input.MouseModeEnum.Captured && !Modal;
        if (combat.Casting is { } casting && _session.Setup.Magic.Formulas.TryGetValue(casting, out var formula) && formula.Targeting == UNNAMED.Domain.Magic.Targeting.Projectile
            && combat.Phase is CombatPhase.Windup)
            range = formula.Blow!.ReachMm;
        else if (combat.Weapon.Ranged && (combat.Phase is CombatPhase.Windup || _camera.IsFirstPerson || free && Input.IsActionPressed("guard")))
            range = combat.Weapon.ReachMm;
        if (range == 0)
        {
            _hud.SetReticle(null, false);
            _projectiles.ShowAimLine(null, null);
            return false;
        }
        var body = simulation.Player.Body;
        var terrain = _session.Setup.Layout.Space.Terrain;
        var (x, z, onCreature) = simulation.Aim(PlayerController.FacingOf(_camera.GroundForward), range);
        float height = onCreature ? 0.7f : 1.4f;
        var point = new Vector3(x / 1000f, terrain.HeightAtMm(x, z) / 1000f + height, z / 1000f);
        var camera = _camera.Camera;
        _hud.SetReticle(camera.IsPositionBehind(point) ? null : camera.UnprojectPosition(point), onCreature);
        _projectiles.ShowAimLine(_hud.DebugVisible ? new Vector3(body.XMm / 1000f, body.YMm / 1000f + 1.4f, body.ZMm / 1000f) : null, _hud.DebugVisible ? point : null);
        return true;
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

        var simulation = _session.Simulation!;
        var combat = simulation.Combat;
        _avatar.SetStance(Stance(combat, alpha));
        _avatar.Hold(combat.Weapon.Source == "unarmed" ? null : combat.Weapon.Source);
        var posture = simulation.Posture;
        _avatar.SetPosture(posture.Stance == UNNAMED.Domain.Spatial.Stance.Crouched, posture.Airborne);
        _avatar.Pose(feet, PlayerController.FacingRadians(predicted.FacingMdeg), Math.Min(speed, 8f), delta);
        _camera.Crouch = _avatar.Crouch;
        _creatures.Draw(simulation, alpha, delta);
        _magicEffects.Draw(_avatar, combat.Casting, combat.Phase, combat.Effects.Select(e => e.EffectId),
            combat.Strain / (double)Math.Max(1, combat.StrainTolerance), delta);
        _soundEvents.SetStation(_inventory.OpenStation?.Kind);
        _soundEvents.Update(feet, speed, simulation.Posture.Airborne, combat.Strain / (double)Math.Max(1, combat.StrainTolerance),
            _inventory.OpenContainer, _inventory.Visible || _journal.Visible || _character.Visible || _help.Visible, delta);
        _crafting.Refresh(simulation.Nodes);
        _npcs.Draw(simulation, delta);
        _camera.Follow(_shots?.Viewpoint ?? _avatar.Position, delta);
        _avatar.SetFirstPerson(_camera.EffectiveDistance < 0.4f);
        _hud.SetHeading(PlayerController.FacingOf(_camera.GroundForward) / 1000f);
        bool aiming = Aim(simulation, combat);
        _hud.SetCrosshair(_camera.IsFirstPerson && !aiming);

        _hud.SetPrompt(_controller.FocusOn(_camera) switch
        {
            null => null,
            { Kind: FocusKind.Door } door => $"[{HelpPanel.Key("interact")}] {(_controller.IsOpen(door.Key) ? "Close" : "Open")} the {Describe(door.Key)}",
            { Kind: FocusKind.Container } container => container.DefId == container.Key
                ? $"[{HelpPanel.Key("interact")}] Open the {Describe(container.Key)}"
                : $"[{HelpPanel.Key("interact")}] Search the {_session.DisplayName(container.DefId)}",
            { Kind: FocusKind.Node } node => NodePrompt(simulation, node),
            { Kind: FocusKind.Station } station => $"[{HelpPanel.Key("interact")}] Work at the {Describe(station.Key)}",
            { Kind: FocusKind.Npc } npc when DownedCompanion(npc.Key) is not null => $"[{HelpPanel.Key("interact")}] Help {_session.DisplayName(npc.Key)} up",
            { Kind: FocusKind.Npc } npc => simulation.Conversation?.NpcId == npc.Key ? null : $"[{HelpPanel.Key("interact")}] Talk to {_session.DisplayName(npc.Key)}",
            { Kind: FocusKind.Switch } site => _session.Setup.Layout.FindSwitch(site.Key) is { } s ? $"[{HelpPanel.Key("interact")}] {s.Verb} the {s.Name}" : null,
            { Kind: FocusKind.Barrier } barrier => _session.Setup.Layout.Barriers.First(b => b.Key == barrier.Key).Prompt,
            { } item => $"[{HelpPanel.Key("interact")}] Pick up {ItemName(_session, item.DefId, item.Quality)}",
        });

        var view = simulation.Player;
        var stats = view.Stats;
        var pools = view.Progression.Pools;
        _hud.SetStatus(
            $"{view.Name}   Level {view.Progression.Level}   XP {view.Progression.LevelProgressXp}/{_session.Setup.Progression.Curve.ToReach(view.Progression.Level + 1)}" +
            (view.Progression.XpDebt > 0 ? $"   debt {view.Progression.XpDebt}" : "") +
            (view.Progression.UnspentAttributePoints > 0 ? $"   [{HelpPanel.Key("character")}] a point to spend" : "") +
            (posture.Stance == UNNAMED.Domain.Spatial.Stance.Crouched ? "   Crouched" : "") +
            $"\nHealth {combat.Health}/{combat.MaxHealth}   Stamina {combat.Stamina}/{combat.MaxStamina}" +
            $"   Focus {pools.Focus ?? stats.FocusMax}/{stats.FocusMax}   Strain {pools.Strain}/{stats.StrainTolerance}   Resonance {stats.Resonance}" +
            $"\n{ItemName(_session, combat.Weapon.Source, Wielded(view)?.Quality ?? 0)}{(combat.Blocking ? " (guarding)" : "")}   Coin {view.Currency}   Armor {view.Armor}" +
            $"   Carrying {view.CarriedGrams / 1000.0:0.#}/{view.CarryLimitGrams / 1000.0:0.#} kg   [{HelpPanel.Key("inventory")}] inventory");
        _hud.SetVitals(combat.Health, combat.MaxHealth, combat.Stamina, combat.MaxStamina);
        _hud.SetMagicPools(combat.Focus, combat.MaxFocus, combat.Strain, combat.StrainTolerance, combat.Strained);
        var formulas = _controller.Formulas();
        _hud.SetMagic((combat.Casting is { } casting ? $"Casting {_session.DisplayName(casting)}\n" : "") +
            string.Join("   ", formulas.Select((f, i) => $"[{i + 4}] {_session.DisplayName(f)} {_session.Setup.Magic.Formulas[f].FocusCost}F")) +
            (combat.Strained ? "   Strained" : ""), formulas);
        _hud.SetEffects(string.Join("   ", combat.Effects.Select(e =>
            $"{_session.DisplayName(e.EffectId)}{(e.Stacks > 1 ? $" x{e.Stacks}" : "")} {Math.Max(0, e.ExpiresTick - simulation.WorldTick) * _session.TickSeconds:0}s")),
            combat.Effects.Select(e => e.EffectId).Concat(combat.Strained ? new[] { "strained" } : Array.Empty<string>()).ToList());
        if (Target(simulation) is { } target)
            _hud.SetTarget(_session.DisplayName(target.DefId), target.Health, target.MaxHealth);
        else
            _hud.SetTarget(null, 0, 0);
        var quests = simulation.Quests;
        _hud.SetTracker(JournalPanel.Tracker(quests));
        _hud.SetCompanions(string.Join("\n", simulation.Companions.Select(c =>
            $"{c.Name} - {c.Doing}, {c.Standing}" +
            (c.FallsAtTick is { } falls ? $" - falls in {Math.Max(0, falls - simulation.WorldTick) * _session.TickSeconds:0} s unless helped up" : "") +
            (c.Condition == CompanionCondition.Up ? $"   [{HelpPanel.Key("companion_order")}] {(c.Order == CompanionOrder.Follow ? "wait" : "follow")}" : ""))),
            simulation.Companions.Select(c => c.Condition != CompanionCondition.Up ? "downed" : c.Order == CompanionOrder.Follow ? "follow" : "wait").FirstOrDefault());
        _journal.Refresh(_session);
        _questDebug.Refresh(_session, delta);
        _character.Refresh();

        if (_hud.DebugVisible)
        {
            var body = _controller.Authoritative;
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
        _session.Subscribe<PlayerRespawned>(_controller.OnRespawned);
        _session.Subscribe<DoorToggled>(e =>
        {
            _controller.OnDoorToggled(e);
            _hollow.SetDoor(e.DoorKey, e.Open);
        });
        // Switches and barriers (M6): what a switch did, and every flag change shown as it now stands.
        _session.Subscribe<SwitchSet>(e =>
        {
            if (_session.Setup.Layout.FindSwitch(e.SwitchKey) is { } site)
                _hud.Toast(site.DoneText, 6);
        });
        _session.Subscribe<WorldFlagChanged>(_ => _hollow.SetFlags(_session.Simulation!.Switches, _session.Simulation!.Barriers));
        _session.Subscribe<LocationDiscovered>(e => _hud.Toast($"Discovered: {_session.DisplayName(e.LocationId)}"));
        // Companions (M6): joined, told, downed, helped up, fallen back to the Waystone.
        _session.Subscribe<CompanionRecruited>(e => _hud.Toast($"{_session.DisplayName(e.NpcId)} joins you"));
        _session.Subscribe<CompanionOrdered>(e => _hud.Toast($"{_session.DisplayName(e.NpcId)}: {(e.Order == CompanionOrder.Follow ? "following" : "waiting")}", 2));
        _session.Subscribe<CompanionDowned>(e => _hud.Toast($"{_session.DisplayName(e.NpcId)} is down - reach them and press {HelpPanel.Key("interact")}", 6));
        _session.Subscribe<CompanionRevived>(e => _hud.Toast($"{_session.DisplayName(e.NpcId)} is back on their feet"));
        _session.Subscribe<CompanionFell>(e => _hud.Toast($"{_session.DisplayName(e.NpcId)} fell, and will be waiting at the Ashen Waystone", 6));
        _session.Subscribe<ExperienceGained>(e => _hud.Toast(e.LevelsGained > 0 ? $"+{e.Awarded} XP - level {e.Level}!" : $"+{e.Awarded} XP", 3));
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is InteractCommand or MoveItemCommand or EquipCommand or UnequipCommand or GatherCommand or CraftCommand or TakeAllCommand
                or SpendAttributeCommand)
                _hud.Toast(e.Reason, 3);
            // A jump or a stand refused under the beam says why; one refused mid-action or in the air stays quiet.
            if (e.Command is JumpCommand or CrouchCommand && e.Reason == "no room to stand")
                _hud.Toast("No room to stand", 2);
        });
        // The owner's M6 playtest: a take-all's tally, a point spent, and every shot drawn along the path it took.
        _session.Subscribe<TookAll>(e => _hud.Toast(e.Left == 0 ? $"Took everything ({e.Taken})" : $"Took {e.Taken}; {e.Left} left - {e.Why}", 3));
        _session.Subscribe<AttributeSpent>(e => _hud.Toast($"{char.ToUpperInvariant(UNNAMED.Domain.Progression.ProgressionKeys.Key(e.Attribute)[0])}" +
            $"{UNNAMED.Domain.Progression.ProgressionKeys.Key(e.Attribute)[1..]} {e.Value}", 3));
        _session.Subscribe<ShotLoosed>(Shot);
        _session.Subscribe<ItemMoved>(e =>
        {
            _inventory.Refresh();
            if (e.From.Kind == PlaceKind.Ground || e.To.Kind == PlaceKind.Ground)
                _items.Refresh(_session.Simulation!);
        });
        _session.Subscribe<ItemEquipped>(_ => _inventory.Refresh());
        _session.Subscribe<ItemUnequipped>(_ => _inventory.Refresh());
        _session.Subscribe<NodeGathered>(e =>
        {
            _inventory.Refresh();
            string spent = !e.Spent ? ""
                : _session.Setup.Crafting.Nodes[e.NodeDefId].Respawn == Respawn.None ? $" - the {_session.DisplayName(e.NodeDefId)} is worked out"
                : $" - nothing more until the {_session.DisplayName(e.NodeDefId)} grows back";
            _hud.Toast($"+{e.Count} {_session.DisplayName(e.ItemId)}{spent}", 3);
        });
        _session.Subscribe<ItemCrafted>(e =>
        {
            _inventory.Refresh();
            _hud.Toast($"Made {ItemName(_session, e.ItemId, e.Quality)}{(e.Count > 1 ? $" x{e.Count}" : "")}", 3);
        });
        SubscribeSocial();
        SubscribeQuests();
        SubscribeCombat();
    }

    /// <summary>
    /// A shot drawn along the path the simulation resolved (the owner's M6 playtest): from the shoulder to where it stopped - into a
    /// creature at its middle, a wall at chest height, or at the end of its range into the ground (an arrow) or bursting (a working).
    /// A working's art is named for it by the pipeline's convention: <c>spell.force.impulse_bolt</c> is <c>vfx.force.impulse_bolt_*</c>.
    /// </summary>
    private void Shot(ShotLoosed shot)
    {
        var terrain = _session.Setup.Layout.Space.Terrain;
        bool working = _session.Setup.Magic.Formulas.TryGetValue(shot.Source, out var formula);
        long range = working ? formula!.Blow?.ReachMm ?? 0 : _session.Simulation!.Combat.Weapon.ReachMm;
        double length = Math.Sqrt(Math.Pow(shot.ToXMm - shot.FromXMm, 2) + Math.Pow(shot.ToZMm - shot.FromZMm, 2));
        bool atRange = Math.Abs(length - range) < 20;
        var from = new Vector3(shot.FromXMm / 1000f, terrain.HeightAtMm(shot.FromXMm, shot.FromZMm) / 1000f + 1.4f, shot.FromZMm / 1000f);
        float height = shot.Target is not null ? 0.7f : atRange ? (working ? 1.2f : 0.05f) : 1.3f;
        var to = new Vector3(shot.ToXMm / 1000f, terrain.HeightAtMm(shot.ToXMm, shot.ToZMm) / 1000f + height, shot.ToZMm / 1000f);
        // Out of the hands, not the chest.
        from += (to - from).Normalized() * 0.4f;
        string? stem = working && shot.Source.IndexOf('.') is > 0 and var dot ? "vfx." + shot.Source[(dot + 1)..] : null;
        _projectiles.Loose(from, to, working, shot.Target is not null, stem);
    }

    /// <summary>Quests (M5): a toast when one starts, moves or ends; what it pays in the log. The tracker and journal redraw each frame.</summary>
    private void SubscribeQuests()
    {
        var quests = _session.Setup.Quests.Quests;
        string Title(string id) => quests.TryGetValue(id, out var q) ? q.Title : id;
        string Step(string quest, string objective) =>
            quests.TryGetValue(quest, out var q) && q.Objectives.TryGetValue(objective, out var o) ? o.Description : objective;
        _session.Subscribe<QuestStarted>(e => _hud.Toast($"New quest: {Title(e.QuestId)}", 5));
        _session.Subscribe<ObjectiveSatisfied>(e =>
        {
            if (_session.Simulation!.Quests.FirstOrDefault(q => q.Id == e.QuestId)?.Status == Domain.Quests.QuestStatus.Active)
                _hud.Toast($"Done: {Step(e.QuestId, e.ObjectiveId)}", 3);
        });
        _session.Subscribe<QuestCompleted>(e => _hud.Toast($"Quest complete: {Title(e.QuestId)}", 6));
        _session.Subscribe<QuestFailed>(e => _hud.Toast($"Quest failed: {Title(e.QuestId)}", 6));
        // XP, a technique and a relationship already speak for themselves (their own toast or log line); coin and goods do not.
        _session.Subscribe<RewardGranted>(e =>
        {
            if (e.Kind == "currency")
                _hud.Log($"{Title(e.QuestId)}: +{e.Amount} coin");
            else if (e.Kind == "item" && e.Ref is { } item)
                _hud.Log($"{Title(e.QuestId)}: {_session.DisplayName(item)}{(e.Amount > 1 ? $" x{e.Amount}" : "")}");
        });
    }

    /// <summary>The waystation's people (M4): conversations on their panel, trade on the inventory's, and what they think in the log.</summary>
    private void SubscribeSocial()
    {
        _session.Subscribe<ConversationLine>(_ => _dialogue.Refresh());
        _session.Subscribe<ConversationEnded>(_ => _dialogue.Refresh());
        _session.Subscribe<ServiceOpened>(e =>
        {
            if (e.Service == Domain.Social.NpcServices.Trade)
                OpenTrade(e.NpcId);
        });
        _session.Subscribe<ItemBought>(e =>
        {
            _inventory.Refresh();
            _hud.Toast($"Bought {_session.DisplayName(e.ItemId)}{(e.Count > 1 ? $" x{e.Count}" : "")} for {e.Price}", 3);
        });
        _session.Subscribe<ItemSold>(e =>
        {
            _inventory.Refresh();
            _hud.Toast($"Sold {_session.DisplayName(e.ItemId)}{(e.Count > 1 ? $" x{e.Count}" : "")} for {e.Price}", 3);
        });
        _session.Subscribe<RelationshipChanged>(e =>
            _hud.Log($"{_session.DisplayName(e.NpcId)}: {e.Dimension} {(e.To >= e.From ? "+" : "")}{e.To - e.From}"));
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is TalkCommand or ChooseCommand or BuyCommand or SellCommand)
                _hud.Toast(e.Reason, 3);
        });
    }

    /// <summary>A node's prompt: what it gives now, or why it gives nothing.</summary>
    private string NodePrompt(Simulation simulation, Focus focus)
    {
        string name = _session.DisplayName(focus.DefId);
        if (simulation.Nodes.FirstOrDefault(n => n.Key == focus.Key) is { Ready: true })
            return $"[{HelpPanel.Key("interact")}] Gather from the {name}";
        return _session.Setup.Crafting.Nodes[focus.DefId].Respawn == Respawn.None
            ? $"The {name} is worked out"
            : $"The {name} has nothing to take until it grows back";
    }

    /// <summary>The carried entry in the main hand, if any.</summary>
    private static InventoryEntry? Wielded(PlayerView view) =>
        view.Equipment.TryGetValue(EquipSlot.MainHand, out var id) ? view.Inventory.FirstOrDefault(e => e.ItemId == id) : null;

    /// <summary>Combat reads in words as well as poses: every blow, effect, kill and death goes to the log (ROADMAP.md M3c).</summary>
    private void SubscribeCombat()
    {
        string Name(string id) => _session.DisplayName(id);
        // A companion's blow lands as it strikes: their figure plays its attack (the Phase-1 asset integration).
        _session.Subscribe<HitResolved>(e =>
        {
            if (_session.Simulation!.Companions.FirstOrDefault(c => c.InstanceId == e.Attacker) is { } companion)
                _npcs.Strike(companion.NpcId);
        });
        _session.Subscribe<HitResolved>(e =>
        {
            var player = _session.Simulation!.PlayerId;
            string how = (e.Critical ? ", critical" : "") + (e.Blocked ? ", blocked" : "") + (e.Staggered ? ", staggered" : "");
            if (e.Attacker == player)
                _hud.Log($"You hit the {Name(CreatureDef(e.Target))} ({BodyRegions.Key(e.Region)}) for {e.Damage}{how}");
            else if (e.Dodged)
                _hud.Log($"You dodge the {Name(e.AttackerDefId)}'s {Name(e.Source).ToLowerInvariant()}");
            else
                _hud.Log($"The {Name(e.AttackerDefId)}'s {Name(e.Source).ToLowerInvariant()} hits your {BodyRegions.Key(e.Region)} for {e.Damage}{how}");
        });
        _session.Subscribe<AttackMissed>(e =>
        {
            if (e.Attacker != _session.Simulation!.PlayerId)
                return;
            if (_session.Setup.Magic.Formulas.ContainsKey(e.Source))
                _hud.Log($"Your {_session.DisplayName(e.Source)} finds nothing");
            else
                _hud.Log(_session.Setup.Items.Catalog.Find(e.Source)?.Weapon?.Ranged == true ? "Your arrow finds nothing" : "Your swing finds nothing");
        });
        _session.Subscribe<HealthChanged>(e =>
        {
            if (e.Target == _session.Simulation!.PlayerId && e.Source != "strain")
                _hud.Log($"{Name(e.Source)}: {(e.Delta > 0 ? "+" : "")}{e.Delta}");
        });
        _session.Subscribe<CastCompleted>(e => _hud.Log($"You work {Name(e.FormulaId)} (+{e.Strain} Strain)"));
        _session.Subscribe<CastFizzled>(e => _hud.Log($"Your {Name(e.FormulaId)} fizzles (+{e.Strain} Strain)"));
        _session.Subscribe<CastInterrupted>(e => _hud.Log($"Your {Name(e.FormulaId)} is broken off ({e.Reason})"));
        _session.Subscribe<StrainBacklash>(e => _hud.Log($"Strain backlash: -{e.Damage} health"));
        _session.Subscribe<TechniqueLearned>(e => _hud.Toast($"Learned {Name(e.DefinitionId)}", 4));
        _session.Subscribe<EffectApplied>(e =>
        {
            if (e.Target == _session.Simulation!.PlayerId)
                _hud.Log($"You are {Name(e.EffectId).ToLowerInvariant()}{(e.Stacks > 1 ? $" (x{e.Stacks})" : "")}");
        });
        _session.Subscribe<GuardBroken>(_ => _hud.Toast("Guard broken - no stamina behind it", 2));
        _session.Subscribe<CreatureKilled>(e => _hud.Log($"The {Name(e.DefId)} dies"));
        _session.Subscribe<SkillPracticed>(e =>
        {
            if (e.Xp > 0)
                _hud.Log($"{Name(e.SkillId)} +{e.Xp} XP (level {e.Level})");
        });
        _session.Subscribe<ItemUsed>(e => _hud.Toast(e.EffectId is null ? $"Read {Name(e.DefId)}" : $"Used {Name(e.DefId)}", 2));
        _session.Subscribe<ItemConsumed>(_ => _inventory.Refresh());
        _session.Subscribe<PlayerDied>(e =>
        {
            string recap = string.Join("\n", e.Recap.Select(r =>
                r.AttackerDefId == r.Source ? $"{Name(r.Source)}: {r.Damage}" : $"{Name(r.AttackerDefId)}, {Name(r.Source).ToLowerInvariant()}: {r.Damage}"));
            string killer = e.KillerDefId == e.Cause ? Name(e.Cause) : $"the {Name(e.KillerDefId)} ({Name(e.Cause).ToLowerInvariant()})";
            _hud.ShowDeath($"You died - killed by {killer}.\n\nThe last blows:\n{recap}\n\nXP debt +{e.DebtAdded} (nothing earned is lost). " +
                           "You return at the Ashen Waystone, weakened for a minute.");   // content bible §18
        });
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is AttackCommand or DodgeCommand or UseItemCommand or CastCommand && !e.Reason.StartsWith("already", StringComparison.Ordinal)
                && e.Reason is not ("staggered" or "dodging" or "dead"))
                _hud.Toast(e.Reason, 2);
        });
    }

    private string CreatureDef(Domain.EntityId id) =>
        _session.Simulation!.Creatures.FirstOrDefault(c => c.Id == id)?.DefId ?? "creature";

    /// <summary>The creature to show: the nearest one hunting the character, or the nearest living one the camera faces.</summary>
    private CreatureView? Target(Simulation simulation)
    {
        var body = _controller.Authoritative;
        double Distance(CreatureView c) => Math.Sqrt(Math.Pow(c.Body.XMm - body.XMm, 2) + Math.Pow(c.Body.ZMm - body.ZMm, 2));
        var living = simulation.Creatures.Where(c => c.Alive).ToList();
        var hunting = living.Where(c => c.Hostile && Distance(c) < 30_000).OrderBy(Distance).FirstOrDefault();
        if (hunting is not null)
            return hunting;
        return living.Where(c => Distance(c) < 20_000)
            .Where(c => new Vector3(c.Body.XMm - body.XMm, 0, c.Body.ZMm - body.ZMm).Normalized().Dot(_camera.GroundForward) > 0.85f)
            .OrderBy(Distance).FirstOrDefault();
    }

    /// <summary>How far the body is through its combat phase, from the simulation's own phase lengths, for the pose.</summary>
    private CombatStance Stance(CombatView combat, double alpha)
    {
        var constants = _session.Setup.Combat.Constants;
        var weapon = combat.Weapon;
        var working = combat.Casting is { } casting ? _session.Setup.Magic.Formulas[casting] : null;
        int length = combat.Phase switch
        {
            CombatPhase.Windup when working is not null => working.CastTicks,
            CombatPhase.Active when working is not null => 1,
            CombatPhase.Recovery when working is not null => working.RecoveryTicks,
            CombatPhase.Windup => weapon.WindupTicks,
            CombatPhase.Active => weapon.ActiveTicks,
            CombatPhase.Recovery => combat.AttackSource is null ? constants.DodgeRecoveryTicks : weapon.RecoveryTicks,
            CombatPhase.Dodge => constants.DodgeTicks,
            CombatPhase.Staggered => constants.StaggerTicks,
            _ => 1,
        };
        float progress = (float)((length - combat.PhaseTicksLeft + alpha) / Math.Max(1, length));
        var held = weapon.Ranged ? Held.Bow
            : weapon.Source == "unarmed" ? Held.Nothing
            : _session.Setup.Items.Catalog.Find(weapon.Source)?.Weapon?.TwoHanded == true ? Held.Spear   // the greybox draws a two-hander as a spear
            : Held.Sword;
        return new CombatStance(combat.Phase, progress, held, combat.Blocking, working is not null);
    }

    /// <summary>After a new game or a load: the only moments presentation copies the whole state.</summary>
    private void Resync()
    {
        _controller.Resync();
        _creatures.Reset();
        foreach (var door in _session.Simulation!.Doors)
            _hollow.SetDoor(door.Site.Key, door.Open);
        _hollow.SetFlags(_session.Simulation!.Switches, _session.Simulation!.Barriers);
        _items.Refresh(_session.Simulation!);
        // Panels open on the world before are closed on this one: a conversation it does not have, a container, a trader or a station
        // that was the other world's (the Phase-1 technical audit, M-03).
        _dialogue.Refresh();
        _inventory.Close();
        _character.Visible = false;
        var body = _controller.Authoritative;
        _camera.Yaw = PlayerController.FacingRadians(body.FacingMdeg) + Mathf.Pi;
        _lastFeet = HollowView.ToGodot(body.XMm, body.YMm, body.ZMm);
    }

    /// <summary>F5: taken now, written in the background (P-01); "Saved", or why not, when it has been.</summary>
    private void QuickSave() => _session.SaveInBackground(SaveSlots.Quick);

    private void QuickLoad() => LoadChosen(SaveSlots.Quick, SaveCopy.Current);

    /// <summary>
    /// <c>--resume-shots dir</c>, with <c>--profile</c>: the start screen as a player meets it, Continue pressed, and the saves list opened
    /// in the game - each pictured - then quit. The Phase-1 technical audit's B-01, shown.
    /// </summary>
    private void ResumeShots(string directory)
    {
        switch (++_resumeFrame)
        {
            case 20:
                SaveScreenshot(directory, "01_start_screen");
                if (!_saves.PressContinue())
                {
                    GD.PushError("UNNAMED resume shots: the start screen offered no Continue");
                    GetTree().Quit(1);
                }
                break;
            case 80:
                SaveScreenshot(directory, "02_continued");
                _saves.OpenInGame();
                break;
            case 100:
                SaveScreenshot(directory, "03_saves_in_game");
                GD.Print($"UNNAMED resume shots written to {directory}: continued at world tick {_session.Simulation?.WorldTick}");
                GetTree().Quit(_session.Simulation is null ? 1 : 0);
                break;
        }
    }

    private int _resumeFrame;

    /// <summary>A world began - a new game or a load: the views copy it whole, and a player's mouse is the game's again.</summary>
    private void Started()
    {
        _saves.Close();
        _hud.Visible = true;
        _mouseFreed = false;
        Resync();
        GD.Print($"UNNAMED world: seed {WorldSeed.Format(_session.Simulation!.World.WorldSeed)}, tick {_session.Simulation.WorldTick}");
    }

    /// <summary>Continue (B-01): the newest save that can be loaded, as the start screen offers it. Null when there is none, or it failed.</summary>
    private (string Slot, SaveCopy Copy, LoadResult Result)? Continue() =>
        _session.StartChoice().Continue is { } next && LoadChosen(next.Slot, next.Copy) is { } result ? (next.Slot, next.Copy, result) : null;

    /// <summary>
    /// Load the copy of a save the player chose. When it cannot be loaded, the saves list opens saying why, with that save's backups and
    /// the save it displaced beneath it - offered, never loaded in its place (PERSISTENCE.md §7.2).
    /// </summary>
    private LoadResult? LoadChosen(string slot, SaveCopy copy)
    {
        string what = SavesPanel.Describe(slot, copy);
        try
        {
            var result = _session.Load(slot, copy);
            Started();
            _hud.Toast(result.IsComplete ? $"Loaded: {what}" : $"Loaded {what}, with losses - see the log");
            foreach (string problem in result.Report.Loss.Concat(result.Report.Warnings))
                GD.PushWarning(problem);
            return result;
        }
        catch (Exception e) when (e is SaveException or IOException or UnauthorizedAccessException)
        {
            GD.PushError($"UNNAMED load of {slot} ({copy}) failed: {e.Message}");
            string failure = $"{what} could not be loaded: {e.Message}";
            if (_session.Simulation is null)
                _saves.ShowFailure(failure);
            else
                _saves.OpenInGame(failure);
            return null;
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
            ["route"] = $"{_perf.Progress}; the character was struck {_perfStruck} times and died {_perfDied} times (a clean capture: 0 and 0)",
        };
        string summary = _stats!.Write(_perfOut, notes);
        GD.Print($"UNNAMED perf capture written to {_perfOut}\n{summary}");
        if (_perfStruck + _perfDied > 0)
            GD.PushWarning($"UNNAMED perf: a creature reached the character ({_perfStruck} blows, {_perfDied} deaths) - the capture measured a fight");
        GetTree().Quit(0);
    }

    /// <summary>Keep a picture of what a capture segment was measuring, next to its numbers.</summary>
    public static void SaveScreenshot(Viewport viewport, string directory, string name)
    {
        Directory.CreateDirectory(directory);
        viewport.GetTexture().GetImage().SavePng(Path.Combine(directory, name + ".png"));
    }

    private void SaveScreenshot(string directory, string name) => SaveScreenshot(GetViewport(), directory, name);

    private void OpenInventory(string? container)
    {
        _inventory.Open(container);
    }

    private void OpenTrade(string npcId)
    {
        _inventory.OpenTrade(npcId);
    }

    private void OpenStation(string key)
    {
        _inventory.OpenAt(_session.Setup.Layout.Stations.Single(s => s.Key == key));
    }

    private void CloseInventory()
    {
        _inventory.Close();
    }

    /// <summary>The companion lying downed as this NPC, or null (M6).</summary>
    private CompanionView? DownedCompanion(string npcId) =>
        _session.Simulation!.Companions.FirstOrDefault(c => c.NpcId == npcId && c.Condition == CompanionCondition.Downed);

    /// <summary>A container read as words; a corpse is named for the creature it was.</summary>
    internal static string Describe(GameSession session, string key) =>
        session.Simulation?.Creatures.FirstOrDefault(c => c.CorpseKey == key) is { } dead ? $"{session.DisplayName(dead.DefId)} remains" : Describe(key);

    /// <summary>A door, container or station key read as words: <c>door.forge_shed</c> is the forge shed door.</summary>
    internal static string Describe(string key) => key switch
    {
        _ when key.StartsWith("door.", StringComparison.Ordinal) => key["door.".Length..].Replace('_', ' ') + " door",
        _ when key.StartsWith("container.", StringComparison.Ordinal) => key["container.".Length..].Replace('_', ' '),
        _ when key.StartsWith("station.", StringComparison.Ordinal) => key["station.".Length..].Replace('_', ' '),
        _ => key,
    };

    /// <summary>An item's name with its quality (M3f): a fine or crude one says so; a standard one is just itself.</summary>
    internal static string ItemName(GameSession session, string defId, int quality) => quality switch
    {
        Quality.Fine => "Fine " + session.DisplayName(defId),
        Quality.Crude => "Crude " + session.DisplayName(defId),
        _ => session.DisplayName(defId),
    };

    private void ParseArguments(string[] arguments)
    {
        for (int i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] is "--perf-out" or "--perf-seconds" or "--ui-shots" or "--playthrough" or "--playthrough-verify" or "--asset-root" or "--delta-shots"
                    or "--profile" or "--resume-shots" or "--content-root" or "--layout-check" or "--perf-route"
                    or "--art-gallery"
                && i + 1 < arguments.Length)
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
        Bind("crouch", Key.X);            // a toggle (the owner's M6 playtest); C stays the dodge
        Bind("character", Key.K);
        Bind("help", Key.F1);
        Bind("take_all", Key.R);          // in a container's panel
        Bind("first_person", Key.V);
        Bind("shoulder_swap", Key.Q);
        Bind("debug_overlay", Key.F3);
        Bind("quest_debug", Key.F4);
        Bind("journal", Key.J);
        Bind("quicksave", Key.F5);
        Bind("quickload", Key.F9);
        Bind("saves", Key.L);
        Bind("release_mouse", Key.Escape);
        Bind("inventory", Key.Tab, Key.I);
        Bind("dodge", Key.C);
        Bind("use", Key.H);
        Bind("companion_order", Key.G);   // follow or wait (content bible §9: no radial menu)
        for (int i = 0; i < CastKeys.Length; i++)
            Bind(CastKeys[i], Key.Key4 + i);   // the content bible's hotbar: 4 to 6 are the formulas
        for (int n = 1; n <= 9; n++)
            Bind($"reply_{n}", Key.Key1 + n - 1);   // in a conversation, the number keys answer
        foreach (var (action, button) in new[] { ("attack", MouseButton.Left), ("guard", MouseButton.Right) })
        {
            if (!InputMap.HasAction(action))
                InputMap.AddAction(action);
            InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
        }
    }
}
