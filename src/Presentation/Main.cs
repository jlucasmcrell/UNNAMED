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
/// <c>--ui-shots dir</c>, and <c>--playthrough dir</c> then <c>--playthrough-verify dir</c> (M6: the acceptance run, and its relaunch);
/// <c>--realtime</c> plays the acceptance run on the frame's own clock, its script stepped once per tick, for real-time recording.
/// <c>--menu-escape back</c> makes Escape from a screen the System Menu opened return to the menu (the owner's open UX option).
/// <c>--asset-root dir</c> names the asset pipeline's workspace, for its HUD and effect art (the owner's M6 playtest). The smoke, the
/// playthrough, its relaunch and the delta shots end by writing the art and audio coverage reports (Phase A) into their directory - the
/// smoke's beside the log - or into <c>--coverage-out dir</c>.
/// </summary>
public partial class Main : Node3D
{
    /// <summary>Input actions for the formula keys, 4 to 6.</summary>
    private static readonly string[] CastKeys = { "cast_1", "cast_2", "cast_3" };
    private static readonly string[] QuickKeys = { "quick_1", "quick_2", "quick_3" };

    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
    private GameSession _session = null!;
    private HollowView _hollow = null!;
    private Figure _avatar = null!;
    private Art.ArtLibrary _art = Art.ArtLibrary.Empty;
    private Art.ArtBindings _bindings = Art.ArtBindings.Empty;
    private CameraRig _camera = null!;
    private TalkCamera _talkCamera = null!;
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
    private SystemMenuPanel _menu = null!;
    private CanvasLayer? _openedFromMenu;
    private bool _escapeToMenu;
    private SaveOutcome? _lastSave;
    private string? _loadedFrom;
    private EffectsPanel _effectsPanel = null!;
    private CompanionsPanel _companionsPanel = null!;
    private WaystonePanel _waystones = null!;
    private MapPanel _map = null!;

    /// <summary>Autorun (the owner's second-play brief §23): on, the character goes forward until a key, a screen or a death stops them.</summary>
    private bool _autorun;
    private ProjectilesView _projectiles = null!;
    private Art.MagicEffects _magicEffects = null!;
    private Art.HitImpacts _hits = null!;
    private Audio.SoundBank _sounds = null!;
    private Audio.SoundEvents _soundEvents = null!;
    private AssetCatalog _assets = AssetCatalog.Empty;
    private FrameStats? _stats;
    private PerfRun? _perf;
    private Smoke? _smoke;
    private ConnectedCheck? _connectedCheck;
    private InputCheck? _inputCheck;
    private UiShots? _shots;
    private Showcase? _showcase;
    private Playthrough? _play;

    /// <summary>--realtime: the playthrough on the frame's clock (its script run on the frames that ran a tick), not one tick a frame.</summary>
    private bool _realtime;

    private int _ticksLastFrame = 1;
    private DeltaShots? _delta;
    private SurveyShots? _survey;
    /// <summary>Where periodic watch shots go, and how many frames have been counted. A run that is wedged stops
    /// writing them, which is the signal a window grab cannot give for a Vulkan surface.</summary>
    private string? _watchShots => _options.GetValueOrDefault("--watch-shots");

    /// <summary>One game day in world ticks, from content/config/time.yaml: 1440 game minutes at 2 real seconds each,
    /// at 20 ticks a second. The sky's clock is driven from it so a day and night take 48 real minutes.</summary>
    public const double TicksPerDay = 1440.0 * 2.0 * 20.0;

    /// <summary>
    /// How many frames between watch shots, from <c>--watch-every</c>, default 40.
    ///
    /// Read here rather than assigned in the harness setup chain, because the first attempt assigned it inside the
    /// <c>--shot</c> branch - where it compiled, read correctly, and silently did nothing for every other run mode.
    /// The default of 40 is a "is it stuck" instrument: one frame every two thirds of a second. Motion cannot be
    /// judged from that, and the owner's own instruction is to record video and have a model that can watch it
    /// review the result - a blink, a pop at an LOD band, a foot slide and a stutter are all things a still cannot
    /// show and a 0.67 s strobe cannot either. <c>--watch-every 4</c> turns the same instrument into a clip.
    /// </summary>
    private int WatchEvery =>
        int.TryParse(_options.GetValueOrDefault("--watch-every"), out int every) && every > 0 ? every : 40;
    private int _watchTick;
    private LayoutCheck? _layout;
    private VisualAudit? _audit;
    private VisualAuditAB? _auditAb;
    private GroundField _ground = null!;
    private ScatterView _scatter = null!;
    private Art.ScatterRules _scatterRules = Art.ScatterRules.None;
    private ulong _sceneMs, _groundMs;
    private string _perfOut = string.Empty;
    private int _perfStruck, _perfDied;
    private bool _scripted;
    private (string Slot, SaveCopy Copy, LoadResult Result)? _continued;
    private Vector3 _lastFeet;
    private double _lastAlpha;
    private float _drawnSpeed;

    /// <summary>
    /// The ceiling on the speed the STRIDE is fed, in metres per second.
    ///
    /// **IT IS A FILTER, NOT A LIMIT, AND THE MEASUREMENT THAT SHOWS IT IS WHY IT IS STILL 8.** It looks too low -
    /// only 1.56x the fastest gait this game configures (`base_m_s` 3.2 at `sprint_percent` 160 is 5.12) - and the
    /// windowed smoke does sit at exactly 8.00 on 106 of 1033 frames, so raising it looks like an obvious fix. **It was
    /// raised to 20 and measured:**
    ///     ceiling  8 : stride median **5.81**, body median **5.80**, ratio **1.002**
    ///     ceiling 20 : stride median **6.84**, body median **5.95**, ratio **1.150**
    /// **Raising it made the stride 15 per cent faster than the body.** The eased per-frame travel is contaminated by
    /// tick boundaries - a tick snaps the prediction back to zero progress and the next frame reads a whole tick's
    /// travel as one frame's, which is why the unclamped figure's p99 is **13.1 m/s** on a body moving at 5.8 - and
    /// **this ceiling is what suppresses those spikes.** *The eased value then converges on the true speed, which is
    /// what the 1.002 ratio means.* **A teleport, a respawn or a cell transition is still caught by it.**
    /// </summary>
    private const float SpeedCeiling = 8f;

    /// <summary>
    /// The same eased frame speed as <see cref="_drawnSpeed"/> with NO ceiling, written to the state log only.
    /// *A diagnostic that saturates cannot diagnose.* See the note where it is computed.
    /// </summary>
    private float _rawSpeed;

    /// <summary>
    /// The predicted body's world position in millimetres, carried from <c>Draw</c> to the state log, which runs in a
    /// different method and cannot see the prediction. Two columns rather than a speed: a position needs no easing to
    /// interpret, so whatever the log's other columns do, consecutive positions divided by the wall clock give the
    /// body's real travel directly.
    /// </summary>
    private long _playerXmm;
    private long _playerZmm;
    private double _drawnAlpha;
    private StreamWriter? _stateLog;
    private double _stateClock;

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
        // A player's run draws at the tier they saved on the start screen; a harness run never reads it (its numbers stay comparable).
        bool harness = _flags.Count > 0 || _options.Keys.Any(k => k is not ("--asset-root" or "--asset-fallback" or "--content-root" or "--profile" or "--visual" or "--region" or "--bindings-overlay" or "--scatter-rules" or "--menu-escape"));
        _escapeToMenu = _options.GetValueOrDefault("--menu-escape") == "back";
        VisualOptions.Parse(_options.GetValueOrDefault("--visual"), harness ? null : Ui.GraphicsPanel.SavedTier());
        Art.ArtLibrary.BuildTextureCache = _flags.Contains("--texture-cache");
        VisualOptions.Apply(GetViewport());
        if (_flags.Contains("--spike"))
        {
            AddChild(new SpikeScene(_options.GetValueOrDefault("--perf-out", DefaultPerfOut("spike")), Seconds()));
            return;
        }
        if (_options.TryGetValue("--art-gallery", out string? gallery))
        {
            var catalog = AssetCatalog.Load(_options.GetValueOrDefault("--asset-root"), Home());
            AddChild(new Art.ArtGallery(new Art.ArtLibrary(catalog.Root),
                Art.ArtBindings.LoadWithOverlay(Art.ArtBindings.ResourcePath, _options.GetValueOrDefault("--bindings-overlay")), Path.GetFullPath(gallery)));
            return;
        }
        if (_options.TryGetValue("--kit-sheet", out string? kit))
        {
            var catalog = AssetCatalog.Load(_options.GetValueOrDefault("--asset-root"), Home());
            AddChild(new Art.KitSheet(new Art.ArtLibrary(catalog.Root), Path.GetFullPath(kit)));
            return;
        }
        if (_options.TryGetValue("--vfx-sheet", out string? vfx))
        {
            var catalog = AssetCatalog.Load(_options.GetValueOrDefault("--asset-root"), Home());
            AddChild(new Art.VfxSheet(catalog, Art.ParticleRecipes.Load(catalog.Root), Path.GetFullPath(vfx)));
            return;
        }
        if (_options.TryGetValue("--anim-sheet", out string? sheet))
        {
            var catalog = AssetCatalog.Load(_options.GetValueOrDefault("--asset-root"), Home());
            AddChild(new Art.AnimationSheet(new Art.ArtLibrary(catalog.Root), Art.ArtBindings.Load(Art.ArtBindings.ResourcePath), Path.GetFullPath(sheet)));
            return;
        }

        // --content-root: another copy of the content, for a harness that needs different data (the layout check's full pack).
        string contentRoot = _options.GetValueOrDefault("--content-root") is { } content ? Path.GetFullPath(content) : Path.Combine(Home(), "content");
        string? playthrough = _options.GetValueOrDefault("--playthrough") ?? _options.GetValueOrDefault("--playthrough-verify");
        string profile = playthrough is not null ? Path.Combine(Path.GetFullPath(playthrough), "profile")
            : _flags.Contains("--smoke") || _options.ContainsKey("--connected-check") || _flags.Contains("--input-check") || _flags.Contains("--perf") || _options.ContainsKey("--ui-shots")
              || _options.ContainsKey("--delta-shots") || _options.ContainsKey("--layout-check") || _options.ContainsKey("--survey")
              || _options.ContainsKey("--shot")
              || _options.ContainsKey("--visual-audit") || _options.ContainsKey("--visual-audit-ab") || _options.ContainsKey("--audio-audition")
              || (_options.ContainsKey("--showcase") && !_options.ContainsKey("--profile"))
            ? Path.Combine(OS.GetUserDataDir(), "scratch", $"run-{System.Environment.ProcessId}")
            : _options.GetValueOrDefault("--profile") is { } chosen ? Path.GetFullPath(chosen)
            : Path.Combine(OS.GetUserDataDir(), "saves", "default");
        if (_options.GetValueOrDefault("--playthrough") is { } run)
            Playthrough.Clear(Path.GetFullPath(run));
        // --region: which region the run plays. Content is chosen by id, so a new region needs no code
        // change - its region file, its cells and its bindings are the whole of it.
        string region = _options.GetValueOrDefault("--region") ?? "region.starting_frontier";
        // Bad content refuses to start, naming every file (ARCHITECTURE.md §8.1); so does a profile another copy of the game holds.
        _session = GameSession.Boot(new GameOptions(contentRoot, profile) { LockProfile = true, RegionId = region });
        SkyView.ClockAtWorldStart = (float)_session.ClockAtWorldStartHours;
        _session.SubscriberFailed += SubscriberFailed;
        bool verify = _options.ContainsKey("--playthrough-verify");
        bool scripted = playthrough is not null || _flags.Contains("--smoke") || _options.ContainsKey("--connected-check") || _flags.Contains("--input-check") || _flags.Contains("--perf")
                        || _options.ContainsKey("--ui-shots")
                        || _options.ContainsKey("--delta-shots") || _options.ContainsKey("--layout-check") || _options.ContainsKey("--survey")
                        || _options.ContainsKey("--shot")
                        || _options.ContainsKey("--visual-audit") || _options.ContainsKey("--visual-audit-ab") || _options.ContainsKey("--audio-audition")
                        || _options.ContainsKey("--showcase");
        // No run of a harness takes the mouse - but the input check, which checks who has it.
        _scripted = (scripted && !_flags.Contains("--input-check")) || _options.ContainsKey("--resume-shots");
        // A scripted run plays one world from its start - the acceptance playthrough a fixed one, so it is the same run every time (M6).
        // A player's run begins at the start screen (the Phase-1 technical audit, B-01); the relaunch check continues as a player would.
        bool showcaseContinues = _options.ContainsKey("--showcase") && _options.ContainsKey("--profile");
        // Every harness run takes the SAME world, which is what the line above intends. The audit and
        // audition runs were left out of this and so fell to seed 0 - a fresh random world each time -
        // which made their stills incomparable: a before/after pair was the same camera over the same
        // ground with different scatter in it (evidence/FINDING_audit_seed_not_pinned.md). Pinned here
        // so a fixed-camera pair differs only in the thing under test. --perf was left out too, and it
        // matters more there than anywhere: a performance capture is a number, and two captures of two
        // different worlds are not a before and an after, they are two unrelated measurements. Every
        // frame-rate figure this region has ever quoted was taken on a world nobody can return to.
        // (--perf is a FLAG, not an option: `_options.ContainsKey` on it is silently false, which is how
        // the first attempt at this fix did nothing at all.)
        bool fixedWorld = _options.ContainsKey("--playthrough") || _options.ContainsKey("--delta-shots")
                          || _options.ContainsKey("--showcase") || _options.ContainsKey("--visual-audit")
                          || _options.ContainsKey("--visual-audit-ab") || _options.ContainsKey("--audio-audition")
                          || _options.ContainsKey("--survey")
                          || _options.ContainsKey("--connected-check")
                          || _options.ContainsKey("--shot")
                          || _flags.Contains("--perf")
                          || _options.ContainsKey("--resume-shots");
        if (scripted && !verify && !showcaseContinues)
            _session.NewGame("Wanderer", fixedWorld ? Playthrough.Seed : 0);
        GD.Print($"UNNAMED boot: content {_session.Content.Version} ({_session.Content.Hash[..19]}...), region {_session.Setup.Layout.Id}, " +
                 $"{_session.Setup.Layout.CellKeys.Length} cells");

        _assets = AssetCatalog.Load(_options.GetValueOrDefault("--asset-root"), Path.GetDirectoryName(contentRoot)!);
        GD.Print(_assets.Root is { } art ? $"UNNAMED assets: HUD and effect art from {art}" : "UNNAMED assets: no asset workspace - greybox HUD and effects");
        if (_assets.Problems.Count > 0)
            GD.PushWarning($"UNNAMED assets: {_assets.Problems.Count} manifest entries withheld, greybox stands in: {string.Join("; ", _assets.Problems)}");
        // The asset library's models, clips and materials, by the presentation's bindings (the Phase-1 asset integration).
        // --asset-fallback names a second workspace (the shared depot) searched for whatever the region's own root does not hold: a
        // region root carries the region's art and is small, the depot holds every body the pipeline has generated. The owner's
        // ruling is to use what already exists, and a generated rig that is on disk but not copied into a region is exactly that.
        Art.ArtLibrary.FallbackRoot = _options.GetValueOrDefault("--asset-fallback")
            ?? System.Environment.GetEnvironmentVariable("UNNAMED_ASSET_FALLBACK")
            ?? SiblingDepot(_assets.Root);
        if (Art.ArtLibrary.FallbackRoot is { } fallback && !Directory.Exists(fallback))
        {
            GD.PushWarning($"UNNAMED assets: --asset-fallback {fallback} does not exist; ignored");
            Art.ArtLibrary.FallbackRoot = null;
        }
        _art = new Art.ArtLibrary(_assets.Root);
        if (Art.ArtLibrary.FallbackRoot is { } second)
            GD.Print($"UNNAMED assets: falling back to {second} for anything {_assets.Root ?? "the region"} does not hold");
        // A region's own art, layered over the bindings every region shares (the HUD, the icons, the effect
        // sheets, the audio beds). The asset workspace carries it, so a region is chosen by --asset-root and
        // --region together and neither half reaches into the other's files.
        string? regionBindings = _options.GetValueOrDefault("--bindings-overlay")
            ?? (_assets.Root is { } artRoot ? Path.Combine(artRoot, "bindings.json") : null);
        if (regionBindings is not null && System.IO.File.Exists(regionBindings))
            GD.Print($"UNNAMED art: regional bindings layered over the shared ones from {regionBindings}");
        _bindings = Art.ArtBindings.LoadWithOverlay(Art.ArtBindings.ResourcePath, regionBindings);
        _art.Withhold(_bindings.Withheld);
        _art.Coverage.Allow(CoverageAllowlist());
        _scatterRules = ScatterRules(_assets.Root, _options.GetValueOrDefault("--scatter-rules"));
        GD.Print($"UNNAMED renderer: {RenderingServer.GetVideoAdapterName()} ({RenderingServer.GetCurrentRenderingMethod()}); MSAA 3D "
                 + $"{ProjectSettings.GetSetting("rendering/anti_aliasing/quality/msaa_3d")}, TAA {ProjectSettings.GetSetting("rendering/anti_aliasing/quality/use_taa")}, "
                 + $"anisotropy {ProjectSettings.GetSetting("rendering/textures/default_filters/anisotropic_filtering_level")}, directional shadow atlas "
                 + $"{ProjectSettings.GetSetting("rendering/lights_and_shadows/directional_shadow/size")}, soft shadows "
                 + $"{ProjectSettings.GetSetting("rendering/lights_and_shadows/directional_shadow/soft_shadow_filter_quality")}");

        int before = GetChildCount();
        ulong building = Time.GetTicksMsec();
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
            _art = new Art.ArtLibrary(null);
            _art.Coverage.Allow(CoverageAllowlist());
            _bindings = Art.ArtBindings.Empty;
            _assets = AssetCatalog.Empty;
            BuildScene();
        }
        _sceneMs = Time.GetTicksMsec() - building;
        var cost = _art.Cost;
        GD.Print($"UNNAMED load: the scene built in {_sceneMs} ms - {cost.Scenes} model files in {cost.SceneMs:0} ms (their {cost.ModelTextures} textures "
                 + $"given mipmaps in {cost.MipmapMs:0} ms), {cost.MaterialMaps} material maps in {cost.MaterialMapMs:0} ms, the ground field in {_groundMs} ms; "
                 + $"texture memory {cost.VramMbWithMipmaps} MB with mipmaps ({cost.VramMbAsLoaded} MB as loaded)");
        if (_art.Cache is { Hits: > 0 } or { Added: > 0 } or { Failed: > 0 })
            GD.Print($"UNNAMED texture cache: {_art.Cache.Hits} textures from the cache, {_art.Cache.Added} added, {_art.Cache.Failed} failed; "
                     + $"{_art.Cache.CompressedBytes / 1048576.0:0.0} MB compressed");
        _art.Cache.Save();
        _sounds = new Audio.SoundBank { Name = "Sounds" };
        _sounds.Load(_assets.Root);
        if (_sounds.Problems.Count > 0)
            GD.PushWarning($"UNNAMED audio: {_sounds.Problems.Count} sound entries left out, malformed: {string.Join(", ", _sounds.Problems)}");
        AddChild(_sounds);
        // Phase B: the environmental audio hooks (wind, rain, the river, insects...), silent until their audio exists.
        if (VisualOptions.All.GetValueOrDefault("hooks") == "on" && _ground is not null)
            AddChild(new Audio.AmbientAudio(_sounds, _ground));
        _soundEvents = new Audio.SoundEvents { Name = "SoundEvents" };
        AddChild(_soundEvents);
        _soundEvents.Bind(_session, _bindings, _sounds);
        _projectiles.Arrived = _soundEvents.Arrived;
        GD.Print(_sounds.Count > 0 ? $"UNNAMED audio: {_sounds.Count} sounds from {Audio.SoundBank.Manifest}" : "UNNAMED audio: no sound set - silent");
        _controller = new PlayerController(_session);
        _controller.Reached += () => _avatar.Interact();
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
        _effectsPanel = new EffectsPanel { Name = "Effects" };
        _effectsPanel.Bind(_session);
        AddChild(_effectsPanel);
        _companionsPanel = new CompanionsPanel { Name = "Companions" };
        _companionsPanel.Bind(_session, _controller);
        AddChild(_companionsPanel);
        _waystones = new WaystonePanel { Name = "Waystones" };
        _waystones.Bind(_session, _controller);
        AddChild(_waystones);
        _map = new MapPanel { Name = "Map" };
        _map.Bind(_session);
        AddChild(_map);
        _menu = new SystemMenuPanel
        {
            Name = "SystemMenu",
            Entries = new SystemMenuPanel.Entry[]
            {
                new("You", "Character", "character", "panel.character", FromMenu(() => Show(_character))),
                new("You", "Inventory", "inventory", "panel.inventory", FromMenu(() => OpenInventory(null))),
                new("You", "Effects", null, "panel.effects", FromMenu(() => Show(_effectsPanel))),
                new("The world", "Journal", "journal", "panel.journal", FromMenu(() => Show(_journal))),
                new("The world", "Map", "map", "panel.map", FromMenu(() => Show(_map))),
                new("The world", "Companions", null, "panel.companions", FromMenu(() => Show(_companionsPanel))),
                new("System", "Save and load", "saves", "panel.saves", FromMenu(() => _saves.OpenInGame())),
                new("System", "Controls and help", "help", "panel.help", FromMenu(_help.Toggle)),
            },
            Quit = () => GetTree().Quit(0),
            Status = MenuStatus,
        };
        AddChild(_menu);
        var dial = _assets.Icon("ui.hud.compass");
        if (dial is null)
            _art.Coverage.Fallback("icon", "compass", "no compass dial in the icon manifest: a plain strip", "ui.hud.compass");
        else
            _art.Coverage.Resolved("icon", "compass", "ui.hud.compass");
        _hud.UseCompassDial(dial);
        var icons = new HudIcons(_assets, _bindings.Icons, _art.Coverage);
        _hud.UseIcons(icons);
        _inventory.UseIcons(icons);
        _character.UseIcons(icons);
        _effectsPanel.UseIcons(icons);
        _companionsPanel.UseIcons(icons);
        _waystones.UseIcons(icons);
        _map.UseIcons(icons);
        _menu.UseIcons(icons);
        _journal.UseIcons(icons);
        _help.UseIcons(icons);
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
        _saves.UseIcons(icons);
        Subscribe();
        DefineInput();

        if (verify || showcaseContinues)
        {
            _continued = Continue();
            if (_continued is null)
            {
                GD.PushError(verify ? "UNNAMED playthrough verification: Continue loaded nothing" : "UNNAMED showcase: the profile has no save to continue");
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
            // Phase B: behind the choice, a slow establishing shot over the hollow instead of whatever the game camera faces.
            if (VisualOptions.Tier != "phase_a")
            {
                _startCamera = new StartCamera { Name = "StartCamera", Ground = _ground.Height,
                    Offset = region == "region.starting_frontier" ? new Vector2(400, 600) : Vector2.Zero };
                _graphics = new Ui.GraphicsPanel { Name = "Graphics" };
                AddChild(_graphics);
                AddChild(_startCamera);
                // The ground's scatter from a preview seed, so the shot is not bare earth; a world places its own on start.
                _scatter.Build(0x5CE4E5EED);
            }
            return;
        }
        else
        {
            Started();
        }

        if (_flags.Contains("--smoke"))
        {
            _smoke = new Smoke(_session, _controller, _camera, profile, region);
        }
        else if (_options.TryGetValue("--connected-check", out string? connectedRoute))
        {
            _connectedCheck = new ConnectedCheck(_session, _controller, _camera, profile, Path.GetFullPath(connectedRoute));
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
        else if (_options.TryGetValue("--showcase", out string? showcase))
        {
            _showcase = new Showcase(_session, _controller, _camera, _dialogue, Path.GetFullPath(showcase), _options.GetValueOrDefault("--showcase-scene", "locomotion"));
        }        else if (_options.TryGetValue("--ui-shots", out string? shots))
        {
            SizeForCapture();
            _shots = new UiShots(_session, _controller, _camera, _inventory, _dialogue, _journal, _questDebug, shots, ScreenByName);
        }
        else if (playthrough is not null)
        {
            // One tick a frame, at the tick rate: the run is the same every time, and plays in real time (toasts and all). With --realtime
            // the frame rate is the display's and the session follows the frame's clock (under Movie Maker's fixed rate, as repeatable).
            _realtime = _flags.Contains("--realtime");
            if (!_realtime)
                Engine.MaxFps = (int)Math.Round(1 / _session.TickSeconds);
            _play = new Playthrough(_session, _controller, _camera, _dialogue, Path.GetFullPath(playthrough), verify, _continued, ScreenByName);
        }
        else if (_options.TryGetValue("--delta-shots", out string? deltaShots))
        {
            // One tick a frame at the tick rate, like the playthrough: each picture is taken at the same moment every run.
            Engine.MaxFps = (int)Math.Round(1 / _session.TickSeconds);
            _delta = new DeltaShots(_session, _controller, _camera, _inventory, _dialogue, _character, _help, _projectiles, Path.GetFullPath(deltaShots), _assets.Root);
        }
        else if (_options.TryGetValue("--shot", out string? shotSpec))
        {
            // --shot "<x,z,yaw_deg[,pitch][,seconds]>" is WHERE TO STAND AND LOOK; the frames go to --shot-out.
            // (First cut passed the directory positionally, so the spec was taken as the output path and the
            // capture wrote its report into a folder named after the coordinates. Two different things: a place
            // and a destination.)
            Engine.MaxFps = (int)Math.Round(1 / _session.TickSeconds);
            string shotDir = _options.GetValueOrDefault("--shot-out") ?? Path.Combine(OS.GetUserDataDir(), "scratch", "shot");
            _survey = SurveyShots.One(_session, _controller, _camera, Path.GetFullPath(shotDir), shotSpec);
        }
        else if (_options.TryGetValue("--survey", out string? survey))
        {
            // A survey is judged on gameplay's own light, so it does NOT take the audit's detour through a built
            // Environment: it runs the real world at the tick rate and drives the ordinary stick and camera.
            Engine.MaxFps = (int)Math.Round(1 / _session.TickSeconds);
            _survey = new SurveyShots(_session, _controller, _camera, Path.GetFullPath(survey));
        }
        else if (_options.TryGetValue("--visual-audit", out string? audit))
        {
            _audit = new VisualAudit(this, _session, _art, _bindings, _camera, Path.GetFullPath(audit),
                _options.GetValueOrDefault("--audit-shots") is { } list ? Path.GetFullPath(list) : null);
        }
        else if (_options.TryGetValue("--audio-audition", out string? audition) && _assets.Root is { } audioRoot)
        {
            AddChild(new Audio.AudioAudition(_sounds, Path.GetFullPath(audition), audioRoot, _flags.Contains("--audition-open-only")));
        }
        else if (_options.TryGetValue("--visual-audit-ab", out string? auditAb))
        {
            _auditAb = new VisualAuditAB(this, _session, _art, _bindings, _camera, Path.GetFullPath(auditAb));
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
        var layout = _session.Setup.Layout;
        ulong started = Time.GetTicksMsec();
        _ground = new GroundField(layout, _bindings, _scatterRules);
        _groundMs = Time.GetTicksMsec() - started;
        _hollow = new HollowView { Name = "Hollow" };
        _hollow.Bind(_art, _bindings, _ground);
        AddChild(_hollow);
        _hollow.Build(layout);
        // What grows and lies on the ground (Phase A, the visual audit's V3): placed once a world, and so its seed, is known (Resync).
        _scatter = new ScatterView { Name = "Scatter" };
        // The card grass is thrown away; --grass=cards restores it for a single comparison run and nothing else.
        // --grass=blades draws ONLY the blade field: no stones, no heather, no nettles, no leaf litter. Judging a
        // sward is impossible with rock scatter standing in front of it, which is what every previous frame had.
        string grassMode = _options.GetValueOrDefault("--grass") ?? "models";
        _scatter.Bind(_art, _ground, _scatterRules, layout, _bindings.BuildingPrefixes,
            cardsOff: grassMode != "cards", bladesOnly: grassMode == "blades");
        AddChild(_scatter);
        string? body = _bindings.People.GetValueOrDefault("player")?.Model;
        if (Art.SkinnedFigure.Create(_art, _bindings, "player") is { } skinned)
        {
            _avatar = skinned;
            // Phase B: the feet planted on the ground under them (the ground field the terrain is drawn from).
            if (VisualOptions.BodyModifiers)
                Art.BodyModifiers.PlantFeet(skinned.Skeleton, skinned, _ground.Height);
            _art.Coverage.Resolved("person", "player", body!);
        }
        else
        {
            var greybox = new Avatar();
            Art.HeldWeapon.Arm(greybox, _art, _bindings);
            _avatar = greybox;
            _art.Coverage.Fallback("person", "player", body is null ? "no binding: the greybox mannequin" : $"{_art.Why(body) ?? "not drawn"}: the greybox mannequin", body);
        }
        _avatar.Name = "Player";
        AddChild(_avatar);
        _camera = new CameraRig { Name = "CameraRig" };
        AddChild(_camera);
        _talkCamera = new TalkCamera { Name = "TalkCamera" };
        AddChild(_talkCamera);
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
        _npcs = new NpcsView { Name = "Npcs", Ground = VisualOptions.BodyModifiers ? _ground.Height : null };
        _npcs.Bind(_art, _bindings);
        AddChild(_npcs);
        // The character budget's harness (MetaHuman pipeline, Phase 1.4): bound people stood along the camera's view.
        if (OS.GetEnvironment("UNNAMED_PERF_CROWD") is { Length: > 0 } crowd)
            AddChild(new CrowdLoad(crowd, _art, _bindings, _ground.Height) { Name = "PerfCrowd" });
        GD.Print($"UNNAMED art: {_art.Used.Count} assets drawn from the library at boot; {_art.Problems.Count} withheld or unavailable (greybox stands in)");
            GD.Print($"UNNAMED art: baked occlusion drawn on {Art.ArtLibrary.OcclusionDrawn} material(s), suppressed on {Art.ArtLibrary.OcclusionSuppressed}");
        _projectiles = new ProjectilesView { Name = "Projectiles" };
        AddChild(_projectiles);
        _projectiles.Bind(_assets, _art, _bindings, _art.Coverage);
        _magicEffects = new Art.MagicEffects { Name = "MagicEffects" };
        _magicEffects.Bind(_assets, _bindings, _art.Coverage);
        AddChild(_magicEffects);
        _hits = new Art.HitImpacts { Name = "HitImpacts" };
        AddChild(_hits);
        _hits.Bind(_assets.Root);
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
                // The smoke's profile is removed as it ends: its reports go beside the log (or to --coverage-out).
                WriteReports(Path.GetDirectoryName(LogPath) ?? OS.GetUserDataDir(), "smoke");
                GetTree().Quit(code);
                return;
            }
        }
        else if (_connectedCheck is not null)
        {
            if (_connectedCheck.Update() is { } code)
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
        else if (_showcase is not null)
        {
            switch (_showcase.Update(delta))
            {
                case "done":
                    WriteReports(_showcase.Directory, "showcase");
                    GetTree().Quit(0);
                    return;
                case "failed":
                    SaveScreenshot(_showcase.Directory, "failed");
                    WriteReports(_showcase.Directory, "showcase (failed)");
                    GetTree().Quit(1);
                    return;
                case { } shot:
                    SaveScreenshot(_showcase.Directory, shot);
                    break;
            }
        }
        else if (_delta is not null)
        {
            switch (_delta.Update())
            {
                case "done":
                    GD.Print($"UNNAMED delta shots written to {_delta.Directory}");
                    WriteReports(_delta.Directory, "delta-shots");
                    GetTree().Quit(0);
                    return;
                case "failed":
                    SaveScreenshot(_delta.Directory, "failed");
                    WriteReports(_delta.Directory, "delta-shots (failed)");
                    GetTree().Quit(1);
                    return;
                case { } shot:
                    SaveScreenshot(_delta.Directory, shot);
                    break;
            }
        }
        else if (_audit is not null)
        {
            if (_audit.Update() is { } finished)
            {
                GetTree().Quit(finished == "done" ? 0 : 1);
                return;
            }
        }
        else if (_auditAb is not null)
        {
            if (_auditAb.Update() is { } finished)
            {
                GetTree().Quit(finished == "done" ? 0 : 1);
                return;
            }
        }
        else if (_play is not null)
        {
            // --realtime: the script steps on the frames that ran a tick (it steps once a tick); the others read no input either.
            string? step = !_realtime || _ticksLastFrame > 0 ? _play.Update() : null;
            switch (step)
            {
                case "done":
                    WriteReports(_play.Directory, _continued is null ? "playthrough" : "playthrough-verify", _continued is null ? "" : "_relaunch");
                    GetTree().Quit(0);
                    return;
                case "failed":
                    SaveScreenshot(_play.Directory, "failed");
                    WriteReports(_play.Directory, _continued is null ? "playthrough (failed)" : "playthrough-verify (failed)", _continued is null ? "" : "_relaunch");
                    GetTree().Quit(1);
                    return;
                case { } shot:
                    SaveScreenshot(_play.Directory, shot);
                    break;
            }
        }
        else if (_survey is not null)
        {
            switch (_survey.Update())
            {
                case "done":
                    GetTree().Quit(0);
                    return;
                case { } shot:
                    SaveScreenshot(_survey.Directory, shot);
                    break;
            }
        }
        else
        {
            ReadInput();
        }
        // WATCH SHOTS. A window grab of a Vulkan surface comes back blank, so the only way to see what a run is
        // doing WHILE it runs is to have the run itself write frames. The owner's instruction: "take real
        // screenshots when you are waiting for something in an open Godot window to happen so you know whether
        // you've gotten stuck or not - that's happened more than once." This does it from inside.
        if (_watchShots is { } watch && ++_watchTick % WatchEvery == 0)
        {
            Directory.CreateDirectory(watch);
            SaveScreenshot(watch, $"watch_{_watchTick / WatchEvery:0000}");
            GD.Print($"UNNAMED watch shot {_watchTick / WatchEvery:0000} at frame {_watchTick} written");
        }
        KeepContainerInReach();
        // THE SKY RUNS ON THE WORLD CLOCK. content/config/time.yaml: 20 ticks a second, 1 game minute = 2 real
        // seconds, 1440 game minutes a day - so 57,600 ticks is one full day and night, 48 real minutes. Before
        // this the sun was set once at build time and never moved, which is the owner's "lighting in general is
        // pretty static". Only Sky3D has a clock to move; an HDRI run ignores it.
        if (_session.Simulation is { } clock)
            SkyView.Advance(clock.WorldTick, TicksPerDay);

        // The smoke and the playthrough run one tick per frame: the smoke finishes in a fraction of real time, and the playthrough is
        // the same run every time, whatever the frame rate.
        var frame = _session.Frame(_smoke is not null || _connectedCheck is not null || (_play is not null && !_realtime) || _delta is not null || _survey is not null || _audit is not null || _auditAb is not null
            ? _session.TickSeconds : delta);
        _ticksLastFrame = frame.TicksRun;
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
                _lastSave = save;
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
    /// A panel that takes the keys and the mouse is open: a conversation, or a management screen - the System Menu, the inventory (on the
    /// character, a container, a trader or a station), the character sheet, the journal, effects, companions, the controls or the saves
    /// list (the Phase-1 technical audit, L-27; the Field Instrument UI). While one is, no gameplay key reaches the world, and the world
    /// runs on (the owner's ruling: nothing pauses for a panel). The quest debugger is an overlay: it takes no keys.
    /// </summary>
    public bool Modal => _dialogue.Visible || Screen is not null;

    /// <summary>The management screen open, if any: one at a time.</summary>
    private CanvasLayer? Screen =>
        new CanvasLayer[] { _menu, _inventory, _character, _journal, _effectsPanel, _companionsPanel, _waystones, _map, _help, _saves }.FirstOrDefault(s => s.Visible);

    /// <summary>Close whatever management screen is open, back to play.</summary>
    private void CloseScreens()
    {
        if (_openedFromMenu is { } opened)
            UiKit.SheetOf(opened)?.CloseWord("Close");
        _openedFromMenu = null;
        CloseInventory();
        _saves.Close();
        if (_help.Visible)
            _help.Toggle();
        _waystones.Close();
        foreach (var screen in new CanvasLayer[] { _menu, _character, _journal, _effectsPanel, _companionsPanel, _map })
            screen.Visible = false;
    }

    /// <summary>
    /// A screen opened by name, for the harnesses that picture them (the UI shots, the playthrough): Escape is pressed on whatever is open;
    /// every other name opens its screen in place of the last, and "" closes them.
    /// </summary>
    private void ScreenByName(string name)
    {
        if (name != "escape")
            CloseScreens();
        switch (name)
        {
            case "menu_character":
                // A destination chosen on the System Menu, as its row's press chooses it.
                _menu.Visible = true;
                _menu.Choose("Character");
                break;
            case "escape":
                Escape();
                break;
            case "menu":
                _menu.Visible = true;
                break;
            case "character":
                Show(_character);
                break;
            case "inventory":
                OpenInventory(null);
                break;
            case "journal":
                Show(_journal);
                break;
            case "effects":
                Show(_effectsPanel);
                break;
            case "companions":
                Show(_companionsPanel);
                break;
            case "help":
                _help.Toggle();
                break;
            case "saves":
                _saves.OpenInGame();
                break;
            case "announce":
                // The announcement as a level-up shows it, pictured without waiting a level (a preview: nothing is awarded).
                _hud.Announce("Level up", "Level 2", HudStyle.Ember, ("A point to spend on the character sheet", HelpPanel.Key("character")));
                break;
            case "map":
                Show(_map);
                break;
        }
    }

    /// <summary>The Waystone panel, at the stone the character stands at.</summary>
    private void Show(WaystonePanel panel, string stoneKey)
    {
        CloseScreens();
        panel.Open(stoneKey);
    }

    /// <summary>Open a screen in place of whatever was open: a destination replaces the menu, and a screen's own key replaces another.</summary>
    private void Show(CanvasLayer screen)
    {
        CloseScreens();
        screen.Visible = true;
        if (screen == _character)
            _character.Refresh();
        if (screen == _journal)
            _journal.Refresh(_session);
    }

    /// <summary>
    /// A System Menu destination: opens its screen and remembers that the menu opened it. With <c>--menu-escape back</c> (the owner's open
    /// UX option, 08 §11.3) Escape then returns to the menu, and the screen's title bar says BACK; by default Escape closes it to play.
    /// </summary>
    private Action FromMenu(Action open) => () =>
    {
        open();
        _openedFromMenu = Screen;
        if (_escapeToMenu && _openedFromMenu is { } screen)
            UiKit.SheetOf(screen)?.CloseWord("Back");
    };

    /// <summary>The System Menu's strip: who the character is and how far along, the time played and the last save this session.</summary>
    private SystemMenuPanel.StatusLine? MenuStatus()
    {
        if (_session?.Simulation is not { } simulation)
            return null;
        var progression = simulation.Player.Progression;
        var rules = _session.Setup.Progression;
        bool capped = progression.Level >= rules.LevelCap;
        string saved = _lastSave is { } last
            ? $"{(last.Auto ? "Autosaved" : "Saved")} {Ago(_session.PlaytimeSeconds - last.CapturedAtPlaytime)}"
            : _loadedFrom is { } loaded ? $"Loaded {loaded}, not saved since" : "Not saved yet";
        return new SystemMenuPanel.StatusLine(simulation.Player.Name, progression.Level, capped, progression.LevelProgressXp,
            capped ? 0 : rules.Curve.ToReach(progression.Level + 1), progression.XpDebt, progression.UnspentAttributePoints,
            $"Played {Span(_session.PlaytimeSeconds)}", saved);
    }

    /// <summary>A stretch of play in words: "12 min", "1 h 04 min".</summary>
    private static string Span(double seconds)
    {
        long minutes = (long)Math.Max(0, seconds) / 60;
        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00} min";
    }

    private static string Ago(double seconds) => seconds < 60 ? "just now" : $"{Span(seconds)} ago";

    /// <summary>
    /// Escape outside a conversation: closes the screen open back to play (the accepted C4 semantics), and with nothing open opens the
    /// System Menu. With <c>--menu-escape back</c>, a screen the menu opened closes back to the menu instead.
    /// </summary>
    private void Escape()
    {
        var open = Screen;
        bool back = _escapeToMenu && open is not null && open != _menu && open == _openedFromMenu;
        if (open is not null)
            CloseScreens();
        if (open is null || back)
            _menu.Visible = true;
    }

    /// <summary>A screen's own key: closes it when it is the one open, otherwise opens it in place of any other.</summary>
    private void Toggle(CanvasLayer screen, Action open)
    {
        bool was = screen.Visible;
        CloseScreens();
        if (!was)
            open();
    }

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
        }
    }

    /// <summary>
    /// Who has the mouse, decided once a frame from what is open - never set here and there (L-27): the game's while nothing modal is open,
    /// the pointer's otherwise. A harness run and a headless one leave it alone.
    /// </summary>
    private void UpdateMouse()
    {
        if (_scripted || DisplayServer.GetName() == "headless")
            return;
        var wanted = Modal ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        if (Input.MouseMode != wanted)
            Input.MouseMode = wanted;
    }

    private void ReadInput()
    {
        var combat = _session.Simulation!.Combat;
        bool modal = Modal;
        bool looting = _inventory.Visible && _inventory.Anchor is not null && _inventory.OpenStation is null &&
                       _inventory.OpenTrader is null && !_dialogue.Visible;

        // A conversation's keys (the number keys answer, Escape walks away - M4); then Escape and the screens' own keys (the Field
        // Instrument UI): Escape closes the screen open back to play, and with nothing open it opens the System Menu. A screen's key
        // opens it in place of any other, and closes it again.
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
        else if (Input.IsActionJustPressed("release_mouse"))
        {
            Escape();
        }
        else
        {
            if (Input.IsActionJustPressed("inventory"))
                Toggle(_inventory, () => OpenInventory(null));
            if (Input.IsActionJustPressed("character"))
                Toggle(_character, () => Show(_character));
            if (Input.IsActionJustPressed("journal"))
                Toggle(_journal, () => Show(_journal));
            if (Input.IsActionJustPressed("map"))
                Toggle(_map, () => Show(_map));
            if (Input.IsActionJustPressed("help"))
                Toggle(_help, _help.Toggle);
            if (Input.IsActionJustPressed("saves"))
                Toggle(_saves, () => _saves.OpenInGame());
        }
        if (Input.IsActionJustPressed("take_all") && _inventory.Visible)
            _inventory.TakeAll();
        if (looting && Input.IsActionJustPressed("interact"))
            CloseInventory();
        if (Input.IsActionJustPressed("debug_overlay"))
            _hud.DebugVisible = _hollow.DebugVisible = !_hud.DebugVisible;
        if (Input.IsActionJustPressed("quest_debug"))
        {
            _questDebug.Visible = !_questDebug.Visible;
            _questDebug.Refresh(_session, 0, now: true);
        }
        if (modal)
        {
            _autorun = false;
            // Loot stays within reach: walking is allowed, while other panels and gameplay actions remain blocked.
            var lootStick = looting && _inventory.Visible ? new Vector2(
                Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
                Input.GetActionStrength("move_forward") - Input.GetActionStrength("move_back")) : Vector2.Zero;
            var lootGait = Input.IsActionPressed("sprint") ? Gait.Sprint : Input.IsActionPressed("walk") ? Gait.Walk : Gait.Run;
            _controller.Steer(_camera, lootStick, lootGait);
            if (combat.Blocking)
                _controller.Guard(false);
            return;
        }

        var stick = new Vector2(
            Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
            Input.GetActionStrength("move_forward") - Input.GetActionStrength("move_back"));
        var gait = Input.IsActionPressed("sprint") ? Gait.Sprint : Input.IsActionPressed("walk") ? Gait.Walk : Gait.Run;
        // Autorun: the key turns it on and off, and stepping back turns it off; while on, the character goes forward, still steered by
        // the camera and the side keys, at whatever gait is held.
        if (Input.IsActionJustPressed("autorun"))
        {
            _autorun = !_autorun;
            _hud.Toast(_autorun ? $"Autorun - [{HelpPanel.Key("autorun")}] or a step back stops it" : "Autorun off", 2);
        }
        if (Input.IsActionPressed("move_back"))
            _autorun = false;
        if (_autorun)
            stick = new Vector2(stick.X, 1);

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
        for (int q = 0; q < QuickKeys.Length; q++)
        {
            if (Input.IsActionJustPressed(QuickKeys[q]) && !_controller.UseQuick(q))
                _hud.Toast($"Quick slot {q + 1} is empty - it fills with what you carry to use", 2);
        }

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
                case FocusKind.Waystone:
                    if (_session.Simulation!.Waystones.Any(w => w.Site.Key == focus.Key && w.Known))
                        Show(_waystones, focus.Key);
                    else
                        _controller.TouchStone(focus.Key);
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
    }

    /// <summary>
    /// Whatever the panel was opened from - a container, a corpse, a station, a trader - it stays open only while the body is in reach
    /// of where that was (the owner's M6 playtest: the same rule for all of them), and a corpse searched bare closes it. The inventory
    /// opened with its own key has nowhere to be near, and stays.
    /// </summary>
    private void KeepContainerInReach()
    {
        if (!_inventory.Visible || _inventory.Anchor is not { } anchor)
            return;
        // A corpse searched bare is gone with what it held: the panel opened for it goes too, not back to the pack (the owner's first playtest).
        if (_inventory.OpenContainer is { } open && _session.Simulation!.Containers.All(c => c.Site.Key != open))
        {
            CloseInventory();
            return;
        }
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
        // Drawn, the shot goes along the body's own facing - every turn of the camera is sent, and the release holds the facing of the
        // last tick of the draw - so the ring shows where it will stop (the Phase-1 technical audit, L-13). Not yet drawn, the camera's.
        int facing = combat.Phase == CombatPhase.Windup ? body.FacingMdeg : PlayerController.FacingOf(_camera.GroundForward);
        var (x, z, onCreature) = simulation.Aim(facing, range);
        float height = onCreature ? 0.7f : 1.4f;
        var point = new Vector3(x / 1000f, terrain.HeightAtMm(x, z) / 1000f + height, z / 1000f);
        var camera = _camera.Camera;
        _hud.SetReticle(camera.IsPositionBehind(point) ? null : camera.UnprojectPosition(point), onCreature);
        _projectiles.ShowAimLine(_hud.DebugVisible ? new Vector3(body.XMm / 1000f, body.YMm / 1000f + 1.4f, body.ZMm / 1000f) : null, _hud.DebugVisible ? point : null);
        return true;
    }

    /// <summary>
    /// The per-frame state log (--state-log FILE, any mode, normal play included): the frame, the tick fraction, the player's stride
    /// speed, clip state and combat phase, and every drawn NPC's speed and clip state - the evidence that ordinary play selects a
    /// state (the acceptance contract's G4), not that a clip merely loaded.
    /// </summary>
    private void LogStates(Simulation simulation, double delta, float speed)
    {
        if (!_options.TryGetValue("--state-log", out var path))
            return;
        if (_stateLog is null)
        {
            _stateLog = new StreamWriter(Path.GetFullPath(path), append: false) { AutoFlush = true };
            _stateLog.WriteLine("t_s,frame_s,alpha,player_speed,player_state,player_phase,cam_dist,cam_pitch,fov,effects,npcs,foot_l,foot_r,player_x,player_z,player_raw_speed");
        }
        _stateClock += delta;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string npcs = string.Join(";", _npcs.States().Select(n => $"{n.Id}={n.Speed.ToString("0.00", inv)}:{n.State}"));
        _stateLog.WriteLine(string.Join(",", _stateClock.ToString("0.000", inv), delta.ToString("0.0000", inv), _drawnAlpha.ToString("0.00", inv),
            speed.ToString("0.00", inv), _avatar.ClipState ?? "-", simulation.Combat.Phase, _camera.EffectiveDistance.ToString("0.00", inv),
            _camera.Pitch.ToString("0.00", inv), _camera.Camera.Fov.ToString("0", inv), string.Join("|", simulation.Combat.Effects.Select(e => e.EffectId)), npcs,
            Point(_avatar.Foot("L")), Point(_avatar.Foot("R")),
            (_playerXmm / 1000.0).ToString("0.000", inv), (_playerZmm / 1000.0).ToString("0.000", inv),
            _rawSpeed.ToString("0.00", inv)));
        string Point(Vector3? p) => p is { } v ? $"{v.X.ToString("0.000", inv)} {v.Y.ToString("0.000", inv)} {v.Z.ToString("0.000", inv)}" : "-";
    }

    private void Draw(double alpha, double delta)
    {
        var predicted = _controller.Predict(alpha);
        var feet = HollowView.ToGodot(predicted.XMm, predicted.YMm, predicted.ZMm);
        // The stride's speed: the predicted body's movement this frame, eased. A tick boundary snaps the prediction back to zero
        // progress - that frame's movement is not a speed, so the eased value holds (it used to take the intended gait's speed,
        // which ran a blocked body on those frames: the Codex audit, H04).
        if (delta > 0 && alpha >= _lastAlpha)
        {
            float moved = new Vector2(feet.X - _lastFeet.X, feet.Z - _lastFeet.Z).Length() / (float)delta;
            // THE CEILING LOOKED TOO LOW AND IT IS NOT - I RAISED IT TO 20, MEASURED, AND PUT IT BACK. At 8 m/s it is
            // only 1.56x the fastest gait this game configures (3.2 at sprint_percent 160 = 5.12), so it looked as
            // though an ordinary sprint must cross it and feed the stride a speed slower than the body was travelling.
            // **Measured, windowed, before and after:**
            //     ceiling  8 : stride median 5.81, body median 5.80, RATIO 1.002
            //     ceiling 20 : stride median 6.84, body median 5.95, RATIO 1.150
            // **Raising it made the stride 15 per cent FASTER than the body - the opposite of the intent.** The reason
            // is that the eased per-frame `moved` is contaminated by tick boundaries: a tick snaps the prediction back
            // to zero progress and the next frame reads a whole tick's travel as one frame's, which is why the
            // unclamped figure's p99 is 13.1 m/s on a body moving at 5.8. **The ceiling at 8 is what suppresses those
            // spikes**, and the eased value then converges on the true speed. *It is a filter, not a limit.*
            // So it stays, and the "sprint speed cap" is NOT a defect: the stride is fed the body's own speed to
            // within 0.2 per cent. `SpeedCeilingNote` below records the measurement; the constant is unchanged.
            _drawnSpeed = Mathf.Lerp(_drawnSpeed, Math.Min(moved, SpeedCeiling), 1f - Mathf.Exp(-(float)delta / 0.08f));
            // THE SAME NUMBER WITHOUT THE CLAMP, for the state log only. `_drawnSpeed` is what the animation is fed and
            // the clamp at 8 m/s is deliberate - it is a backstop against a teleport or a cell transition being read as
            // a speed. But it also means the log CANNOT REPORT the thing it is used to investigate: in the sprint logs
            // the `player_speed` column sits at exactly 8.00 for 460-609 frames, and the avatar's real travel has been
            // recorded at 14.9-16.1 m/s against a configured 5.12. A diagnostic that saturates is not a diagnostic, so
            // this column records the same eased figure with no ceiling, beside `player_x`/`player_z` which give the
            // position directly and need no easing to interpret.
            _rawSpeed = Mathf.Lerp(_rawSpeed, moved, 1f - Mathf.Exp(-(float)delta / 0.08f));
        }
        float speed = _drawnSpeed;
        _playerXmm = predicted.XMm;
        _playerZmm = predicted.ZMm;
        _lastFeet = feet;
        _lastAlpha = alpha;
        _drawnAlpha = alpha;

        var simulation = _session.Simulation!;
        var combat = simulation.Combat;
        _avatar.SetStance(Stance(combat, alpha));
        _avatar.Hold(combat.Weapon.Source == "unarmed" ? null : combat.Weapon.Source);
        string? Worn(EquipSlot slot) => simulation.Player.Equipment.TryGetValue(slot, out var id)
            ? simulation.Player.Inventory.FirstOrDefault(e => e.ItemId == id)?.DefId : null;
        // The figure's outfit variant: the chest item, with "_cap" when a head item is worn too (hide_vest_cap).
        string? chest = Worn(EquipSlot.Chest), head = Worn(EquipSlot.Head);
        _avatar.Wear(chest is not null && head is not null ? $"{chest}_cap" : chest ?? head);
        var posture = simulation.Posture;
        _avatar.SetPosture(posture.Stance == UNNAMED.Domain.Spatial.Stance.Crouched, posture.Airborne);
        _avatar.Pose(feet, PlayerController.FacingRadians(predicted.FacingMdeg), speed, delta);
        _camera.Crouch = _avatar.Crouch;
        _creatures.Draw(simulation, alpha, delta);
        _magicEffects.Draw(_avatar, combat.Casting, combat.Phase,
            combat.Effects.Select(e => (e.EffectId, Math.Max(0, e.ExpiresTick - simulation.WorldTick) * _session.TickSeconds)),
            combat.Strain / (double)Math.Max(1, combat.StrainTolerance), delta);
        _soundEvents.SetStation(_inventory.OpenStation?.Kind);
        _soundEvents.Update(feet, speed, simulation.Posture.Airborne, combat.Strain / (double)Math.Max(1, combat.StrainTolerance),
            _inventory.OpenContainer, Screen is not null, delta);
        _crafting.Refresh(simulation.Nodes);
        _npcs.Draw(simulation, delta, _session.TickSeconds);
        LogStates(simulation, delta, speed);
        _camera.Follow(_shots?.Viewpoint ?? _avatar.Position, delta);
        _avatar.SetFirstPerson(_camera.EffectiveDistance < 0.4f);
        // A conversation frames the speaker's face over the character's shoulder (not in first person).
        _talkCamera.Frame(simulation.Conversation is { } talk && !_camera.IsFirstPerson ? _npcs.HeadOf(talk.NpcId) : null,
            _avatar.Head, _camera.Camera, delta);
        _hud.SetHeading(PlayerController.FacingOf(_camera.GroundForward) / 1000f);
        bool aiming = Aim(simulation, combat);
        _hud.SetCrosshair(_camera.IsFirstPerson && !aiming);

        _hud.SetPrompt(_controller.FocusOn(_camera) switch
        {
            null => null,
            { Kind: FocusKind.Door } door => $"[{HelpPanel.Key("interact")}] {(_controller.IsOpen(door.Key) ? "Close" : "Open")} the {Names.Place(_session, door.Key, door.DefId)}",
            { Kind: FocusKind.Container } container => container.DefId == container.Key
                ? $"[{HelpPanel.Key("interact")}] Open the {Names.Place(_session, container.Key, container.DefId)}"
                : $"[{HelpPanel.Key("interact")}] Search the {Names.Of(_session, container.DefId)}",
            { Kind: FocusKind.Node } node => NodePrompt(simulation, node),
            { Kind: FocusKind.Station } station => $"[{HelpPanel.Key("interact")}] Work at the {Names.Place(_session, station.Key, station.DefId)}",
            { Kind: FocusKind.Npc } npc when DownedCompanion(npc.Key) is not null => $"[{HelpPanel.Key("interact")}] Help {Names.Of(_session, npc.Key)} up",
            { Kind: FocusKind.Npc } npc => simulation.Conversation?.NpcId == npc.Key ? null : $"[{HelpPanel.Key("interact")}] Talk to {Names.Of(_session, npc.Key)}",
            { Kind: FocusKind.Switch } site => _session.Setup.Layout.FindSwitch(site.Key) is { } s ? $"[{HelpPanel.Key("interact")}] {s.Verb} the {s.Name}" : null,
            { Kind: FocusKind.Waystone } stone => simulation.Waystones.First(w => w.Site.Key == stone.Key) is var w && w.Known
                ? $"[{HelpPanel.Key("interact")}] The {w.Site.Name}: Waystones you know"
                : $"[{HelpPanel.Key("interact")}] Touch the stone",
            { Kind: FocusKind.Barrier } barrier => _session.Setup.Layout.Barriers.First(b => b.Key == barrier.Key).Prompt,
            { } item => $"[{HelpPanel.Key("interact")}] Pick up {Names.Item(_session, item.DefId, item.Quality)}",
        });

        var view = simulation.Player;
        var stats = view.Stats;
        var pools = view.Progression.Pools;
        _hud.SetStatus(
            $"{view.Name}   Level {view.Progression.Level}" +
            (view.Progression.Level >= _session.Setup.Progression.LevelCap ? " (the highest)"
                : $"   XP {view.Progression.LevelProgressXp}/{_session.Setup.Progression.Curve.ToReach(view.Progression.Level + 1)}") +
            (view.Progression.XpDebt > 0 ? $"   debt {view.Progression.XpDebt}" : "") +
            (view.Progression.UnspentAttributePoints > 0 ? $"   [{HelpPanel.Key("character")}] a point to spend" : "") +
            (posture.Stance == UNNAMED.Domain.Spatial.Stance.Crouched ? "   Crouched" : "") +
            $"\nHealth {combat.Health}/{combat.MaxHealth}   Stamina {combat.Stamina}/{combat.MaxStamina}" +
            $"   Focus {pools.Focus ?? stats.FocusMax}/{stats.FocusMax}   Strain {pools.Strain}/{stats.StrainTolerance}   Resonance {stats.Resonance}" +
            $"\n{Names.Item(_session, combat.Weapon.Source, Wielded(view)?.Quality ?? 0)}{(combat.Blocking ? " (guarding)" : "")}   Coin {view.Currency}   Armor {view.Armor}" +
            $"   Carrying {view.CarriedGrams / 1000.0:0.#}/{view.CarryLimitGrams / 1000.0:0.#} kg   [{HelpPanel.Key("inventory")}] inventory");
        _hud.SetVitals(combat.Health, combat.MaxHealth, combat.Stamina, combat.MaxStamina);
        _hud.SetMagicPools(combat.Focus, combat.MaxFocus, combat.Strain, combat.StrainTolerance, combat.Strained);
        var formulas = _controller.Formulas();
        _hud.SetMagic((combat.Casting is { } casting ? $"Casting {Names.Of(_session, casting)}\n" : "") +
            string.Join("   ", formulas.Select((f, i) => $"[{i + 4}] {Names.Of(_session, f)} {_session.Setup.Magic.Formulas[f].FocusCost}F")) +
            (combat.Strained ? "   Strained" : ""), formulas);
        _hud.SetEffects(string.Join("   ", combat.Effects.Select(e =>
            $"{Names.Of(_session, e.EffectId)}{(e.Stacks > 1 ? $" x{e.Stacks}" : "")} {Math.Max(0, e.ExpiresTick - simulation.WorldTick) * _session.TickSeconds:0}s")),
            combat.Effects.Select(e => e.EffectId).Concat(combat.Strained ? new[] { "strained" } : Array.Empty<string>()).ToList());
        bool capped = view.Progression.Level >= _session.Setup.Progression.LevelCap;
        _hud.SetSheet(new HudSheet(view.Name, view.Progression.Level, capped, view.Progression.LevelProgressXp,
            capped ? 0 : _session.Setup.Progression.Curve.ToReach(view.Progression.Level + 1), view.Progression.XpDebt,
            view.Progression.UnspentAttributePoints > 0 ? HelpPanel.Key("character") : null, posture.Stance == UNNAMED.Domain.Spatial.Stance.Crouched,
            pools.Focus ?? stats.FocusMax, stats.FocusMax, pools.Strain, stats.StrainTolerance, stats.Resonance,
            Names.Item(_session, combat.Weapon.Source, Wielded(view)?.Quality ?? 0), combat.Weapon.Source, combat.Blocking, view.Currency, view.Armor,
            view.CarriedGrams / 1000.0, view.CarryLimitGrams / 1000.0, HelpPanel.Key("inventory"),
            combat.Casting is { } worked ? Names.Of(_session, worked) : null,
            formulas.Select(f => new HudFormula(f, Names.Of(_session, f), _session.Setup.Magic.Formulas[f].FocusCost)).ToList(), combat.Strained,
            QuickSlots()));
        if (Target(simulation) is { } target)
            _hud.SetTarget(Names.Of(_session, target.DefId), target.Health, target.MaxHealth);
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
                "\ndiscovered:\n" + string.Join("\n", view.Discoveries.Select(d => $"  {Names.Of(_session, d.LocationId)} (tick {d.Tick})")));
        }
    }

    /// <summary>The last reply given, and when: what a hand-over in the same tick was for.</summary>
    private (string NpcId, long Tick)? _reply;

    private void Subscribe()
    {
        _session.Subscribe<BodyMoved>(_controller.OnBodyMoved);
        _session.Subscribe<PlayerRespawned>(_controller.OnRespawned);
        // Waystones (S-38): what a hand on a stone found, a return point chosen, and a journey's arrival with the time it took.
        _session.Subscribe<StoneTouched>(e =>
        {
            _hud.Toast(e.Text, 7);
            Show(_waystones, e.StoneKey);
        });
        _session.Subscribe<ReturnPointBound>(e => _hud.Toast($"The {StoneName(e.StoneKey)} is your return point. When you fall, you come back here.", 5));
        _session.Subscribe<PlayerTravelled>(e =>
        {
            _controller.OnTravelled(e);
            _autorun = false;
            CloseScreens();
            _hud.Toast($"The {StoneName(e.ToKey)}. {TravelWords.Journey(e.Cost.WalkTicks, e.Cost.WorldTicks, TicksPerDay).Replace("You arrive", "You arrived").Replace("would take", "would have taken")}", 7);
        });        _session.Subscribe<DoorToggled>(e =>
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
        _session.Subscribe<WorldFlagChanged>(_ => _hollow.SetFlags(_session.Simulation!.Switches, _session.Simulation!.Barriers, inPlay: true));
        _session.Subscribe<LocationDiscovered>(e => _hud.Toast($"Discovered: {Names.Of(_session, e.LocationId)}"));
        // Companions (M6): joined, told, downed, helped up, fallen back to the Waystone.
        _session.Subscribe<CompanionRecruited>(e => _hud.Toast($"{Names.Of(_session, e.NpcId)} joins you"));
        _session.Subscribe<CompanionOrdered>(e => _hud.Toast($"{Names.Of(_session, e.NpcId)}: {(e.Order == CompanionOrder.Follow ? "following" : "waiting")}", 2));
        _session.Subscribe<CompanionDowned>(e => _hud.Toast($"{Names.Of(_session, e.NpcId)} is down - reach them and press {HelpPanel.Key("interact")}", 6));
        _session.Subscribe<CompanionRevived>(e => _hud.Toast($"{Names.Of(_session, e.NpcId)} is back on their feet"));
        _session.Subscribe<CompanionFell>(e => _hud.Toast($"{Names.Of(_session, e.NpcId)} fell, and will be waiting at the Ashen Waystone", 6));
        // What the award did: what is left once the XP debt took its share, and that share (the Phase-1 technical audit, L-12).
        _session.Subscribe<ExperienceGained>(e =>
        {
            long kept = e.Awarded - e.Repaid;
            string xp = e.Repaid == 0 ? $"+{e.Awarded} XP" : kept > 0 ? $"+{kept} XP, {e.Repaid} to the XP debt" : $"{e.Repaid} XP to the XP debt";
            _hud.Toast(xp, 3);
            if (e.LevelsGained > 0)
                _hud.Announce("Level up", $"Level {e.Level}", HudStyle.Ember, ("A point to spend on the character sheet", HelpPanel.Key("character")));
        });
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is InteractCommand or MoveItemCommand or EquipCommand or UnequipCommand or GatherCommand or CraftCommand or TakeAllCommand
                or SpendAttributeCommand or TouchStoneCommand or BindStoneCommand or TravelCommand)
                _hud.Toast(e.Reason, 3);
            // A jump or a stand refused under the beam says why; one refused mid-action or in the air stays quiet.
            if (e.Command is JumpCommand or CrouchCommand && e.Reason == "no room to stand")
                _hud.Toast("No room to stand", 2);
        });
        // The owner's M6 playtest: a take-all's tally, a point spent, and every shot drawn along the path it took.
        _session.Subscribe<TookAll>(e =>
        {
            _hud.Toast(e.Left == 0 ? $"Took everything ({e.Taken})" : $"Took {e.Taken}; {e.Left} left - {e.Why}", 3);
            // Emptied, the loot is done: the panel closes rather than leaving the pack open behind it (the owner's first playtest).
            if (e.Left == 0 && _inventory.OpenContainer == e.ContainerKey)
                CloseInventory();
        });
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
                : _session.Setup.Crafting.Nodes[e.NodeDefId].Respawn == Respawn.None ? $" - the {Names.Of(_session, e.NodeDefId)} is worked out"
                : $" - nothing more until the {Names.Of(_session, e.NodeDefId)} grows back";
            _hud.Toast($"+{e.Count} {Names.Of(_session, e.ItemId)}{spent}", 3);
        });
        _session.Subscribe<ItemCrafted>(e =>
        {
            _inventory.Refresh();
            _hud.Toast($"Made {Names.Item(_session, e.ItemId, e.Quality)}{(e.Count > 1 ? $" x{e.Count}" : "")}", 3);
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
        _session.Subscribe<QuestCompleted>(e => _hud.Announce("Quest complete", Title(e.QuestId), HudStyle.Brass));
        _session.Subscribe<QuestFailed>(e => _hud.Toast($"Quest failed: {Title(e.QuestId)}", 6));
        // XP, a technique and a relationship already speak for themselves (their own toast or log line); coin and goods do not.
        _session.Subscribe<RewardGranted>(e =>
        {
            string? paid = e.Kind switch
            {
                "xp" => $"+{e.Amount} XP",
                "currency" => $"+{e.Amount} coin",
                "item" when e.Ref is { } item => $"+ {Names.Of(_session, item)}{(e.Amount > 1 ? $" ×{e.Amount}" : "")}",
                "recipe" or "spell" when e.Ref is { } known => $"Learned {Names.Of(_session, known)}",
                _ => null,
            };
            if (paid is null)
                return;
            _hud.AnnounceMore("Quest complete", Title(e.QuestId), paid);
            if (e.Kind is "currency" or "item")
                _hud.Log($"{Title(e.QuestId)}: {paid}");
        });
    }

    /// <summary>The waystation's people (M4): conversations on their panel, trade on the inventory's, and what they think in the log.</summary>
    private void SubscribeSocial()
    {
        _session.Subscribe<ConversationLine>(_ => _dialogue.Refresh());
        // What a reply hands over is said, by name and count (the owner's first playtest: "it doesn't say what you're giving them").
        _session.Subscribe<ReplyChosen>(e => _reply = (e.NpcId, e.Tick));
        _session.Subscribe<ItemConsumed>(e =>
        {
            if (_reply is { } reply && reply.Tick == e.Tick)
                _hud.Toast($"Handed to {Names.Of(_session, reply.NpcId)}: {e.Count} × {Names.Of(_session, e.DefId)}", 4);
        });
        _session.Subscribe<ConversationEnded>(_ => _dialogue.Refresh());
        _session.Subscribe<ServiceOpened>(e =>
        {
            if (e.Service == Domain.Social.NpcServices.Trade)
                OpenTrade(e.NpcId);
        });
        _session.Subscribe<ItemBought>(e =>
        {
            _inventory.Refresh();
            _hud.Toast($"Bought {Names.Of(_session, e.ItemId)}{(e.Count > 1 ? $" x{e.Count}" : "")} for {e.Price}", 3);
        });
        _session.Subscribe<ItemSold>(e =>
        {
            _inventory.Refresh();
            _hud.Toast($"Sold {Names.Of(_session, e.ItemId)}{(e.Count > 1 ? $" x{e.Count}" : "")} for {e.Price}", 3);
        });
        _session.Subscribe<RelationshipChanged>(e =>
            _hud.Log($"{Names.Of(_session, e.NpcId)}: {e.Dimension} {(e.To >= e.From ? "+" : "")}{e.To - e.From}"));
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is TalkCommand or ChooseCommand or BuyCommand or SellCommand)
                _hud.Toast(e.Reason, 3);
        });
    }

    /// <summary>A node's prompt: what it gives now, or why it gives nothing.</summary>
    private string NodePrompt(Simulation simulation, Focus focus)
    {
        string name = Names.Of(_session, focus.DefId);
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
        string Name(string id) => Names.Of(_session, id);
        // A companion's blow lands as it strikes: their figure plays its attack (the Phase-1 asset integration).
        _session.Subscribe<HitResolved>(e =>
        {
            if (_session.Simulation!.Companions.FirstOrDefault(c => c.InstanceId == e.Attacker) is { } companion)
                _npcs.Strike(companion.NpcId);
            // A blow on the character: what a working left on the body answers where it came from (the ward's shell rings there).
            if (e.Target == _session.Simulation!.PlayerId && !e.Dodged
                && _session.Simulation!.Creatures.FirstOrDefault(c => c.Id == e.Attacker) is { } attacker)
                _magicEffects.Struck(HollowView.ToGodot(attacker.Body.XMm, attacker.Body.YMm, attacker.Body.ZMm) + new Vector3(0, 0.9f, 0));
        });
        // What the blow throws off the body it landed on (blood, sparks, bone dust): only for a blow the simulation resolved.
        _session.Subscribe<HitResolved>(e =>
        {
            var simulation = _session.Simulation!;
            var target = e.Target == simulation.PlayerId ? simulation.Player.Body : simulation.Creatures.FirstOrDefault(c => c.Id == e.Target)?.Body;
            if (e.Dodged || target is null)
                return;
            var attacker = e.Attacker == simulation.PlayerId ? simulation.Player.Body : simulation.Creatures.FirstOrDefault(c => c.Id == e.Attacker)?.Body;
            var at = HollowView.ToGodot(target.XMm, target.YMm, target.ZMm) + new Vector3(0, 1.0f, 0);
            var from = attacker is null ? at + Vector3.Forward : HollowView.ToGodot(attacker.XMm, attacker.YMm, attacker.ZMm) + new Vector3(0, 1.0f, 0);
            _hits.Hit(e.Target == simulation.PlayerId ? null : CreatureDef(e.Target), at, from, e.Blocked);
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
                _hud.Log($"Your {Names.Of(_session, e.Source)} finds nothing");
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
            _autorun = false;
            string returnAt = _session.Simulation!.Waystones.FirstOrDefault(w => w.ReturnPoint)?.Site.Name ?? "Ashen Waystone";
            _hud.ShowDeath($"You died - killed by {killer}.\n\nThe last blows:\n{recap}\n\nXP debt +{e.DebtAdded} (nothing earned is lost). " +
                           $"You return at the {returnAt}, weakened for a minute.");   // content bible §18; the return point chosen (S-38)
        });
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is AttackCommand or DodgeCommand or UseItemCommand or CastCommand && !e.Reason.StartsWith("already", StringComparison.Ordinal)
                && e.Reason is not ("staggered" or "dodging" or "dead"))
                _hud.Toast(e.Reason, 2);
        });
    }

    private string StoneName(string key) => _session.Setup.Layout.FindWaystone(key)?.Name ?? key;

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
        CloseScreens();
        var body = _controller.Authoritative;
        _camera.Yaw = PlayerController.FacingRadians(body.FacingMdeg) + Mathf.Pi;
        _lastFeet = HollowView.ToGodot(body.XMm, body.YMm, body.ZMm);
        // The ground scatter is this world's: placed from its seed (the same seed again keeps what is placed).
        _scatter.Build(_session.Simulation!.World.WorldSeed);
    }

    /// <summary>The coverage report's allowlist (<c>res://Art/art_coverage_allowlist.json</c>): greybox kept on purpose, each with its reason.</summary>
    private static IReadOnlyDictionary<string, string> CoverageAllowlist()
    {
        const string path = "res://Art/art_coverage_allowlist.json";
        if (!Godot.FileAccess.FileExists(path))
            return new Dictionary<string, string>();
        var allowed = Art.ArtCoverage.ParseAllowlist(Godot.FileAccess.GetFileAsString(path), out string? problem);
        if (problem is not null)
            GD.PushWarning($"UNNAMED art coverage: the allowlist at {path}: {problem}");
        return allowed;
    }

    /// <summary>
    /// The ground scatter's rules (<c>res://Art/scatter_rules.json</c>, or the region's own at
    /// <c>&lt;asset-root&gt;/scatter_rules.json</c>); none, and nothing scattered, without them. What grows on
    /// the ground is keyed by the ground's own material, so a second region's rules name its own materials and
    /// its own models - without which nothing is placed at all, however many models it has shipped.
    /// </summary>
    private static Art.ScatterRules ScatterRules(string? assetRoot, string? overridePath)
    {
        if (overridePath is not null)
        {
            if (!System.IO.File.Exists(overridePath))
                throw new FileNotFoundException("Requested scatter rules do not exist", overridePath);
            GD.Print($"UNNAMED scatter rules: explicit {overridePath}");
            return Art.ScatterRules.Parse(System.IO.File.ReadAllText(overridePath));
        }
        string? region = assetRoot is null ? null : Path.Combine(assetRoot, "scatter_rules.json");
        if (region is not null && Godot.FileAccess.FileExists(region))
        {
            GD.Print($"UNNAMED scatter rules: the region's own from {region}");
            return Art.ScatterRules.Parse(Godot.FileAccess.GetFileAsString(region));
        }
        const string path = "res://Art/scatter_rules.json";
        return Godot.FileAccess.FileExists(path) ? Art.ScatterRules.Parse(Godot.FileAccess.GetFileAsString(path)) : Art.ScatterRules.None;
    }

    /// <summary>
    /// The shared depot a region's own asset root sits inside, when there is one: a region's art lives at
    /// <c>&lt;depot&gt;\&lt;region&gt;\assets</c> and the depot every region draws from is <c>&lt;depot&gt;\assets</c>. That layout is the
    /// convention on disk, so a region run gets the depot's bodies without having to remember <c>--asset-fallback</c> - which, forgotten,
    /// silently draws eleven generated models as greybox. Null unless the sibling really is a workspace, so no other layout is affected.
    /// </summary>
    private static string? SiblingDepot(string? root)
    {
        if (root is null || !string.Equals(Path.GetFileName(root), "assets", StringComparison.OrdinalIgnoreCase))
            return null;
        if (Path.GetDirectoryName(root) is not { } region || Path.GetDirectoryName(region) is not { } depot)
            return null;
        string sibling = Path.Combine(depot, "assets");
        bool workspace = Directory.Exists(Path.Combine(sibling, "ready")) || Directory.Exists(Path.Combine(sibling, "manifests"));
        return workspace && !string.Equals(sibling, root, StringComparison.OrdinalIgnoreCase) ? sibling : null;
    }

    /// <summary>
    /// The end of a harness run (Phase A, the owner's A3 and A4): the art coverage report (every visual by semantic ID, what drew it, every
    /// greybox and why) and the audio coverage report (the sound set against the event mapping, and what this run asked for), as JSON and
    /// a short Markdown each, in <paramref name="directory"/> (or <c>--coverage-out</c>); their gate lines go to the log.
    /// </summary>
    private void WriteReports(string directory, string run, string suffix = "")
    {
        directory = _options.GetValueOrDefault("--coverage-out") is { } chosen ? Path.GetFullPath(chosen) : directory;
        try
        {
            var cost = _art.Cost;
            var extra = new Dictionary<string, object?>
            {
                ["asset_root"] = _assets.Root,
                ["library_problems"] = _art.Problems,
                ["library_used"] = _art.Used.OrderBy(u => u, StringComparer.Ordinal).ToList(),
                ["load"] = new Dictionary<string, object?>
                {
                    ["scene_build_ms"] = _sceneMs, ["ground_field_ms"] = _groundMs, ["model_files"] = cost.Scenes, ["model_files_ms"] = cost.SceneMs,
                    ["model_textures_mipmapped"] = cost.ModelTextures, ["mipmap_pass_ms"] = cost.MipmapMs, ["material_maps"] = cost.MaterialMaps,
                    ["material_maps_ms"] = cost.MaterialMapMs, ["texture_mb_as_loaded"] = cost.VramMbAsLoaded, ["texture_mb_with_mipmaps"] = cost.VramMbWithMipmaps,
                    ["scatter_ms"] = _scatter.BuildMs, ["scatter_instances"] = _scatter.Counts,
                },
            };
            GD.Print(_art.Coverage.Write(directory, "art_coverage" + suffix, run, extra));
            var mapped = Audio.SoundEvents.Mapped(_bindings);
            var check = Audio.AudioCoverage.Check(_sounds.Files.ToDictionary(f => f.Key, f => File.Exists(f.Value), StringComparer.Ordinal), mapped);
            GD.Print(Audio.AudioCoverage.Write(directory, "audio_coverage" + suffix, run, check, mapped, _sounds.Requested, _sounds.Resolved, _sounds.Unknown,
                _sounds.Problems, _sounds.Errors));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            GD.PushError($"UNNAMED coverage reports could not be written to {directory}: {e.Message}");
        }
    }

    /// <summary>F5: taken now, written in the background (P-01); "Saved", or why not, when it has been.</summary>
    private void QuickSave() => _session.SaveInBackground(SaveSlots.Quick);

    /// <summary>The three quick slots on their keys: what is in each, or an empty ID for an empty one.</summary>
    private List<HudQuick> QuickSlots()
    {
        var quick = _controller.QuickItems();
        return QuickKeys.Select((key, i) => i < quick.Count
            ? new HudQuick(HelpPanel.Key(key), quick[i].DefId, Names.Of(_session, quick[i].DefId), quick[i].Count)
            : new HudQuick(HelpPanel.Key(key), string.Empty, string.Empty, 0)).ToList();
    }

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
    private StartCamera? _startCamera;
    private Ui.GraphicsPanel? _graphics;

    private void Started()
    {
        if (_startCamera is not null)
        {
            _startCamera.QueueFree();
            _startCamera = null;
            _camera.Camera.MakeCurrent();
            StartCamera.Track(this, _camera.Camera);
            _graphics?.QueueFree();
            _graphics = null;
        }
        _saves.Close();
        _lastSave = null;
        _loadedFrom = null;
        _hud.Visible = true;
        Resync();
        GD.Print($"UNNAMED world: seed {WorldSeed.Format(_session.Simulation!.World.WorldSeed)}, tick {_session.Simulation.WorldTick}");
        // THE BASELINE TUPLE, PRINTED - so a change can be PROVED baseline-neutral by two boots instead of
        // argued to be from the code. `worldgen_fingerprint` plus every cell's `baseline_hash` is what
        // PERSISTENCE.md 6.1/6.4 decides save compatibility on; if a change moves neither, no transition is
        // required and no existing save can refuse. One line per cell, so the diff is the evidence.
        // (The smoke DIGEST is not this: it compares a run's save against its own load.)
        if (System.Environment.GetEnvironmentVariable("UNNAMED_WORLDGEN_REPORT") is { Length: > 0 })
        {
            // The EXACT tuple PERSISTENCE.md 6.1/6.4 decides save compatibility on: the fingerprint
            // (generator identity + version + rng contract + profile digest + canonical probes) and every
            // cell's own baseline_hash. Two boots are the measurement; this is the instrument.
            var generator = _session.Generator;
            ulong seed = _session.Simulation!.World.WorldSeed;
            GD.Print($"UNNAMED worldgen: version {generator.WorldgenVersion}, rng contract {generator.RngContractVersion}, fingerprint {generator.Fingerprint}");
            foreach (var cell in _session.Setup.Layout.CellKeys)
                GD.Print($"UNNAMED worldgen:   baseline_hash {cell} = {generator.Generate(seed, UNNAMED.World.CellKey.Parse(cell)).Digest}");
            // WHICH ANCHORS THIS BUILD ACCEPTS A SAVE THROUGH. A refusal names a FromFingerprint that is in
            // neither list, so printing the lists is what turns "refused" into "refused because nothing covers it".
            GD.Print($"UNNAMED worldgen:   terrain-only fingerprint (no fixed nodes, the M3f FromFingerprint) = {_session.FingerprintWithoutNodes}");
            foreach (var t in _session.Transitions)
                GD.Print($"UNNAMED worldgen:   transition '{t.Name}': {t.FromFingerprint} -> {t.ToFingerprint}");
        }
        // SURVEY EVERY SAVE, WRITING NOTHING. PlanLoad runs the whole load pipeline (version gate, migration
        // chain, per-cell baseline proof, transition lookup) into a throwaway registry and reports instead of
        // throwing - and it does NOT record the load in the rotation metadata the way a real Load does (see
        // the store's PlanMigration). So this answers "does the owner have usable saves" without modifying the
        // saves it is asking about. A Load per save would have written to every one of them.
        if (System.Environment.GetEnvironmentVariable("UNNAMED_LOAD_CHECK") is { Length: > 0 } loadCheckDir)
        {
            // SURVEY A SAVE DIRECTORY, WRITING NOTHING. PlanLoadAt runs the whole load pipeline (version gate,
            // migration chain, per-cell baseline proof, transition lookup) into a throwaway registry and reports
            // instead of throwing - and it does NOT record the load in the rotation metadata the way a real Load
            // does. It also reaches saves/default, which no scripted mode can: every one of them routes its
            // profile to scratch/run-<pid> (Main.cs:185-191), so no harness has ever read the owner's saves.
            foreach (string dir in Directory.EnumerateDirectories(loadCheckDir).OrderBy(d => d, StringComparer.Ordinal))
            {
                string name = Path.GetFileName(dir);
                try
                {
                    var plan = _session.PlanLoadAt(dir);
                    GD.Print($"UNNAMED loadcheck: {name}|{plan.Result}|schema {plan.SourceSchema}->{plan.CurrentSchema}|" +
                             $"worldgen {plan.SourceWorldgenVersion}->{plan.CurrentWorldgenVersion}|rng {plan.SourceRngContract}->{plan.CurrentRngContract}|" +
                             $"fingerprint {(plan.FingerprintChanged ? "CHANGED" : "same")}|content {(plan.ContentChanged ? "changed" : "same")}|" +
                             $"loss {plan.Loss.Count}|blockers {plan.Blockers.Count}|warnings {plan.Warnings.Count}");
                    foreach (string b in plan.Blockers)
                        GD.Print($"UNNAMED loadcheck:   BLOCKER {name}: {b}");
                    foreach (string w in plan.Warnings)
                        GD.Print($"UNNAMED loadcheck:   warning {name}: {w}");
                    // THE REPORT'S OWN TEXT BODY, which is where the cells are actually NAMED.
                    // MigrationReport.CellsMismatched holds "{cell}: {detail}" per cell and ToText() prints each
                    // one (MigrationReport.cs:118-119); the blocker line carries only a COUNT. Capturing the
                    // console summary alone loses the list - the information was there, the capture missed it.
                    GD.Print($"UNNAMED loadcheck: --- report text for {name} ---");
                    foreach (string line in plan.ToText().Split('\n'))
                        GD.Print($"UNNAMED loadcheck: | {line.TrimEnd('\r')}");
                }
                catch (Exception e)
                {
                    GD.Print($"UNNAMED loadcheck: {name}|THREW|{e.GetType().Name}: {e.Message}");
                }
            }
        }
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
            _loadedFrom = what;
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

    /// <summary>
    /// <c>--capture-size WxH</c>: a capture larger than the display (the UI's 2560 x 1440 acceptance size on a 1080p screen). The system
    /// fits the window to the screen once it is shown (and the engine keeps <c>--resolution</c> to itself), so a second after the start
    /// the window is sized to it, and the screenshots read the whole viewport.
    /// </summary>
    private void SizeForCapture()
    {
        if (_options.GetValueOrDefault("--capture-size")?.Split('x') is not [var w, var h] || !int.TryParse(w, out int width) || !int.TryParse(h, out int height))
            return;
        var wanted = new Vector2I(width, height);
        GetTree().CreateTimer(1.0).Timeout += () =>
        {
            if (DisplayServer.WindowGetSize() != wanted)
            {
                DisplayServer.WindowSetPosition(Vector2I.Zero);
                DisplayServer.WindowSetSize(wanted);
            }
            GD.Print($"UNNAMED ui shots: capturing at {DisplayServer.WindowGetSize().X} x {DisplayServer.WindowGetSize().Y} (asked for {width} x {height})");
        };
    }

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

    private void ParseArguments(string[] arguments)
    {
        for (int i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] is "--perf-out" or "--perf-seconds" or "--ui-shots" or "--playthrough" or "--playthrough-verify" or "--asset-root" or "--delta-shots"
                    or "--profile" or "--resume-shots" or "--content-root" or "--layout-check" or "--perf-route"
                    or "--art-gallery" or "--visual-audit" or "--visual-audit-ab" or "--audio-audition" or "--vfx-sheet" or "--kit-sheet" or "--coverage-out" or "--anim-sheet" or "--audit-shots" or "--visual"
                    or "--showcase" or "--showcase-scene" or "--state-log" or "--region" or "--survey" or "--grass" or "--shot" or "--shot-out" or "--watch-shots" or "--watch-every"
                    or "--asset-fallback" or "--bindings-overlay" or "--scatter-rules" or "--connected-check" or "--menu-escape" or "--capture-size"
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
        Bind("map", Key.M);
        Bind("autorun", Key.Quoteleft);   // the owner's second-play brief §23: autorun on tilde
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
        for (int i = 0; i < QuickKeys.Length; i++)
            Bind(QuickKeys[i], Key.Key1 + i);   // outside one, 1 to 3 are the quick slots (the owner's first playtest)
        foreach (var (action, button) in new[] { ("attack", MouseButton.Left), ("guard", MouseButton.Right) })
        {
            if (!InputMap.HasAction(action))
                InputMap.AddAction(action);
            InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
        }
    }
}
