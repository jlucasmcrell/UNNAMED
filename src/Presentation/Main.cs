// UNNAMED Presentation - boot, the frame loop, and input (ARCHITECTURE.md §2, §8.1; D-11)
// Godot presentation only: this file renders state and submits commands; it never writes state.

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Perf;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Spike;
using UNNAMED.Presentation.Ui;
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
    /// <summary>Input actions for the eight quick slots (WP9), on 1 to 8 by default: items, formulas and techniques alike.</summary>
    private static readonly string[] QuickKeys = Enumerable.Range(1, UNNAMED.Domain.Progression.QuickBar.Slots).Select(n => $"quick_{n}").ToArray();

    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

    /// <summary>F3 and F4 work in debug builds, or on a release build launched with <c>--dev</c>.</summary>
    private bool _developer;
    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
    private GameSession _session = null!;
    private HollowView _hollow = null!;

    /// <summary>
    /// The ring, built from the interior layout with its own bindings overlay and hidden until the body goes in
    /// (review 3, B6). Null when this run holds no interior, which is every run without the gate.
    /// </summary>
    private HollowView? _interior;
    private Art.ArtBindings? _interiorBindings;
    private Figure _avatar = null!;
    private Art.ArtLibrary _art = Art.ArtLibrary.Empty;
    private Art.ArtBindings _bindings = Art.ArtBindings.Empty;
    private CameraRig _camera = null!;
    private TalkCamera _talkCamera = null!;
    private PlayerController _controller = null!;
    private Hud _hud = null!;
    private ItemsView _items = null!;
    private CreaturesView _creatures = null!;
    private CreaturesView _exteriorCreatures = null!;
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
    private ReputationPanel _reputation = null!;
    private AwaySheet _awaySheet = null!;
    private CompanionsPanel _companionsPanel = null!;
    private WaystonePanel _waystones = null!;
    private MapPanel _map = null!;

    /// <summary>Autorun (the owner's second-play brief §23): on, the character goes forward until a key, a screen or a death stops them.</summary>
    private bool _autorun;

    /// <summary>Whether the player was in the exterior last frame, so entering or leaving a space is noticed.</summary>
    private bool _wasOutside = true;

    private ProjectilesView _projectiles = null!;
    private Art.MagicEffects _magicEffects = null!;
    private Art.HitImpacts _hits = null!;
    private Audio.SoundBank _sounds = null!;
    private Audio.SoundEvents _soundEvents = null!;
    private Audio.MusicPlayer _music = null!;
    private Audio.VoicePlayer _voice = null!;
    private AssetCatalog _assets = AssetCatalog.Empty;
    private FrameStats? _stats;
    private PerfRun? _perf;
    private Smoke? _smoke;
    private ConnectedCheck? _connectedCheck;
    private InputCheck? _inputCheck;
    private UiShots? _shots;
    private BarrowworksReview? _barrowworksReview;
    private FrontierPerf? _frontierPerf;
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
    /// <summary>
    /// The ring's region id. A run of it takes the ring's own bindings (<c>content/_art_bindings_barrowworks.json</c>)
    /// rather than the region overlay the shipped launch passes, because a region's art belongs in the region's file
    /// (review B4). A normal launch plays <c>region.starting_frontier</c> and never reaches that branch.
    /// </summary>
    private const string BarrowworksRegion = "region.barrowworks";

    private const float SpeedCeiling = 8f;
    /// <summary>How long before the respawn the screen starts to go black (the fade itself takes a second).</summary>
    private const double DeathFadeLeadSeconds = 1.2;

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
        // An exception nothing catches is written to a local crash file, and the next launch's title says so (WP10; never sent anywhere).
        InstallCrashLog();
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
        // A player's run plays with the player's settings (the window, the tier they chose, the dials); a harness run never reads or
        // writes them (its numbers stay comparable).
        _developer = OS.IsDebugBuild() || _flags.Contains("--dev");
        bool harness = _flags.Any(f => f != "--dev") || _options.Keys.Any(k => k is not ("--asset-root" or "--asset-fallback" or "--content-root" or "--profile" or "--visual" or "--region" or "--bindings-overlay" or "--scatter-rules" or "--menu-escape"));
        _escapeToMenu = _options.GetValueOrDefault("--menu-escape") == "back";
        Settings.SettingsStore.Load(persist: !harness);
        if (harness)
        {
            VisualOptions.Parse(_options.GetValueOrDefault("--visual"));
        }
        else
        {
            VisualOptions.Parse(Settings.SettingsStore.StartVisuals(_options.GetValueOrDefault("--visual")), Settings.SettingsStore.Current.Machine.Tier);
            Settings.SettingsStore.ApplyWindow();
        }
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
            AddChild(new Art.AnimationSheet(new Art.ArtLibrary(catalog.Root),
                Art.ArtBindings.LoadWithOverlay(Art.ArtBindings.ResourcePath, _options.GetValueOrDefault("--bindings-overlay")), Path.GetFullPath(sheet),
                _flags.Contains("--anim-profiles"), _options.GetValueOrDefault("--anim-states")));
            SetProcess(false); // This sheet owns the frame loop; front-end input actions have not been bound.
            return;
        }

        // --content-root: another copy of the content, for a harness that needs different data (the layout check's full pack).
        string contentRoot = _options.GetValueOrDefault("--content-root") is { } content ? Path.GetFullPath(content) : Path.Combine(Home(), "content");
        string? playthrough = _options.GetValueOrDefault("--playthrough") ?? _options.GetValueOrDefault("--playthrough-verify");
        string profile = playthrough is not null ? Path.Combine(Path.GetFullPath(playthrough), "profile")
            : _flags.Contains("--smoke") || _options.ContainsKey("--connected-check") || _flags.Contains("--input-check") || _flags.Contains("--perf")
              || (_options.ContainsKey("--ui-shots") && !_flags.Contains("--ui-shots-continue"))
              || _options.ContainsKey("--delta-shots") || _options.ContainsKey("--layout-check") || _options.ContainsKey("--survey")
              || _options.ContainsKey("--shot") || _options.ContainsKey("--barrowworks-review") || _options.ContainsKey("--frontier-perf")
              || _options.ContainsKey("--visual-audit") || _options.ContainsKey("--visual-audit-ab") || _options.ContainsKey("--audio-audition")
              || (_options.ContainsKey("--showcase") && !_options.ContainsKey("--profile"))
              || (_options.ContainsKey("--front-shots") && !_options.ContainsKey("--profile"))
              || _options.ContainsKey("--creator-shots")
            ? Path.Combine(OS.GetUserDataDir(), "scratch", $"run-{System.Environment.ProcessId}")
            : _options.GetValueOrDefault("--profile") is { } chosen ? Path.GetFullPath(chosen)
            : Path.Combine(OS.GetUserDataDir(), "saves", "default");
        if (_options.GetValueOrDefault("--playthrough") is { } run)
            Playthrough.Clear(Path.GetFullPath(run));
        // --region: which region the run plays. Content is chosen by id, so a new region needs no code
        // change - its region file, its cells and its bindings are the whole of it.
        string region = _options.GetValueOrDefault("--region") ?? "region.starting_frontier";
        var shotStart = VisualOptions.Barrowworks && region == BarrowworksRegion ? ShotStart() : null;
        if (_options.ContainsKey("--barrowworks-review") && (VisualOptions.Barrowworks
            || OS.GetEnvironment("BARROWWORKS_REVIEW_RETURN_BASELINE") == "1"))
            shotStart = OS.GetEnvironment("BARROWWORKS_REVIEW_RETURN_BASELINE") == "1"
                ? (1_050_000, 770_000) : (438_000, 664_000);
        if (_options.ContainsKey("--frontier-perf"))
            shotStart = FrontierPerf.ParsePoint(_options.GetValueOrDefault("--frontier-point")
                ?? throw new ArgumentException("--frontier-perf requires --frontier-point x,z"));
        // Bad content refuses to start, naming every file (ARCHITECTURE.md §8.1); so does a profile another copy of the game holds.
        // The Barrowworks switch is read here, once, and passed down: it is parsed as --visual (this layer's own
        // options) but what it gates is content the simulation holds, so GameOptions carries it (M8 D10). Off
        // unless asked for, so a normal launch is the launch it always was.
        _session = GameSession.Boot(new GameOptions(contentRoot, profile)
        {
            LockProfile = true,
            RegionId = region,
            Barrowworks = VisualOptions.Barrowworks,
            // A --shot names a place to stand, so the session opens the player there instead of making the
            // capture walk him across the region - which measured about a quarter of an hour per room in the
            // Barrowworks (plan P2h). Absent, the region's own spawn is used and nothing changes.
            StartXMm = shotStart?.X,
            StartZMm = shotStart?.Z,
        });
        SkyView.ClockAtWorldStart = (float)_session.ClockAtWorldStartHours;
        _session.SubscriberFailed += SubscriberFailed;
        bool verify = _options.ContainsKey("--playthrough-verify");
        bool scripted = playthrough is not null || _flags.Contains("--smoke") || _options.ContainsKey("--connected-check") || _flags.Contains("--input-check") || _flags.Contains("--perf")
                        || _options.ContainsKey("--ui-shots")
                        || _options.ContainsKey("--delta-shots") || _options.ContainsKey("--layout-check") || _options.ContainsKey("--survey")
                        || _options.ContainsKey("--shot") || _options.ContainsKey("--barrowworks-review") || _options.ContainsKey("--frontier-perf")
                        || _options.ContainsKey("--visual-audit") || _options.ContainsKey("--visual-audit-ab") || _options.ContainsKey("--audio-audition")
                        || _options.ContainsKey("--showcase");
        // No run of a harness takes the mouse - but the input check, which checks who has it.
        _scripted = (scripted && !_flags.Contains("--input-check")) || _options.ContainsKey("--resume-shots");
        // A scripted run plays one world from its start - the acceptance playthrough a fixed one, so it is the same run every time (M6).
        // A player's run begins at the start screen (the Phase-1 technical audit, B-01); the relaunch check continues as a player would.
        // --ui-shots-continue pictures the screens over the profile's newest save instead of a new character (WP9: one with skills,
        // formulas and recipes to show).
        bool showcaseContinues = (_options.ContainsKey("--showcase") || _flags.Contains("--ui-shots-continue")) && _options.ContainsKey("--profile");
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
                          || _options.ContainsKey("--shot") || _options.ContainsKey("--barrowworks-review") || _options.ContainsKey("--frontier-perf")
                          || _flags.Contains("--perf")
                          || _options.ContainsKey("--resume-shots");
        if (scripted && !verify && !showcaseContinues)
            _session.NewGame(DefaultCharacter, fixedWorld ? Playthrough.Seed : 0);
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
        // The opt-in co-loaded ring uses the existing signed BC7 cache; off retains the original upload path.
        _art = new Art.ArtLibrary(_assets.Root, useTextureCache: VisualOptions.Barrowworks,
            sceneCachePrefix: VisualOptions.Barrowworks ? "" : null, buildSceneCache: _flags.Contains("--build-scene-cache"));
        if (Art.ArtLibrary.FallbackRoot is { } second)
            GD.Print($"UNNAMED assets: falling back to {second} for anything {_assets.Root ?? "the region"} does not hold");
        // A region's own art, layered over the bindings every region shares (the HUD, the icons, the effect
        // sheets, the audio beds). The asset workspace carries it, so a region is chosen by --asset-root and
        // --region together and neither half reaches into the other's files.
        // A run of the ring takes the ring's own bindings, because a region's art belongs in the region's file.
        // The ring's used to live in Cairnwold's overlay, which the shipped PLAY.bat passes on EVERY launch - and
        // `ArtBindings` replaces a top-level `roof_groups`, so the longhouse's and forge's roofs stopped being
        // built with the option off (review B4). A normal launch plays region.starting_frontier, so it never
        // reaches this and its overlay is exactly what it always was.
        string? regionBindings = region == BarrowworksRegion
            ? Path.Combine(contentRoot, "_art_bindings_barrowworks.json")
            : _options.GetValueOrDefault("--bindings-overlay")
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

        // A player's run parses the model files on workers ahead of the build, in the order the last boot asked for them (WP1's
        // startup measurement); a harness run builds as it always has, so its load numbers stay comparable - unless it names its own
        // order file with --load-order, which is how the two are measured against each other.
        string? loadOrder = _options.GetValueOrDefault("--load-order") is { } order ? Path.GetFullPath(order)
            : harness ? null : ProjectSettings.GlobalizePath(LoadOrderPath);
        if (loadOrder is not null)
            Prefetch(loadOrder);
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
        _art.EndPrefetch();
        if (loadOrder is not null)
            KeepLoadOrder(loadOrder);
        var cost = _art.Cost;
        GD.Print($"UNNAMED load: the scene built in {_sceneMs} ms - {cost.Scenes} model files in {cost.SceneMs:0} ms (their {cost.ModelTextures} textures "
                 + $"given mipmaps in {cost.MipmapMs:0} ms), {cost.MaterialMaps} material maps in {cost.MaterialMapMs:0} ms, the ground field in {_groundMs} ms; "
                 + $"texture memory {cost.VramMbWithMipmaps} MB with mipmaps ({cost.VramMbAsLoaded} MB as loaded)");
        if (_art.Cache is { Hits: > 0 } or { Added: > 0 } or { Failed: > 0 })
            GD.Print($"UNNAMED texture cache: {_art.Cache.Hits} textures from the cache, {_art.Cache.Added} added, {_art.Cache.Failed} failed; "
                     + $"{_art.Cache.CompressedBytes / 1048576.0:0.0} MB compressed");
        _art.Cache.Save();
        // The player's mix; a harness run keeps the defaults so its captures stay comparable.
        Settings.SettingsStore.ApplyAudio();
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
        _soundEvents.Bind(_session, _bindings, _sounds, _ground);
        _music = new Audio.MusicPlayer { Name = "Music" };
        AddChild(_music);
        _music.Bind(_sounds, _session, _bindings);
        // Voice-over (WP5): a line's recording, when the asset workspace has one, on the Voice bus with the music ducked under it.
        _voice = new Audio.VoicePlayer { Name = "Voice" };
        _voice.Load(_assets.Root);
        AddChild(_voice);
        _npcs.Voice = _voice;
        _projectiles.Arrived = _soundEvents.Arrived;
        GD.Print(_sounds.Count > 0 ? $"UNNAMED audio: {_sounds.Count} sounds from {Audio.SoundBank.Manifest}" : "UNNAMED audio: no sound set - silent");
        _controller = new PlayerController(_session);
        _controller.Reached += () => _avatar.Interact();
        BuildPanels(profile);
        Subscribe();
        DefineInput();
        UseSettings(applyPicture: !harness);

        if (verify || showcaseContinues)
        {
            // The playthrough's relaunch proves the save came back exactly, so it restores the newest save with no catch-up (DN7); the
            // showcase logs in as a player would.
            _continued = Continue(catchUp: !verify);
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
            OpenFrontEnd();
            _music.Title(true);
            // Phase B: behind the choice, a slow establishing shot over the hollow instead of whatever the game camera faces.
            if (VisualOptions.Tier != "phase_a")
            {
                _startCamera = new StartCamera { Name = "StartCamera", Ground = _ground.Height,
                    Offset = region == "region.starting_frontier" ? new Vector2(400, 600) : Vector2.Zero };
                AddChild(_startCamera);
                // The ground's scatter of the world a new game will be (Main.Creator), so the shot is not bare earth and Begin finds it placed.
                _scatter.Build(NextWorldSeed);
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
                // Headless acceptance still runs exactly one fixed simulation tick per script frame;
                // it needs no display pacing or GPU readback.
                Engine.MaxFps = DisplayServer.GetName() == "headless" ? 0 : (int)Math.Round(1 / _session.TickSeconds);
            _play = new Playthrough(_session, _controller, _camera, _dialogue, Path.GetFullPath(playthrough), verify, _continued, ScreenByName);
        }
        else if (_options.TryGetValue("--delta-shots", out string? deltaShots))
        {
            // One tick a frame at the tick rate, like the playthrough: each picture is taken at the same moment every run.
            Engine.MaxFps = (int)Math.Round(1 / _session.TickSeconds);
            _delta = new DeltaShots(_session, _controller, _camera, _inventory, _dialogue, _character, _help, _projectiles, Path.GetFullPath(deltaShots), _assets.Root);
        }
        else if (_options.TryGetValue("--barrowworks-review", out string? review) && (VisualOptions.Barrowworks
            || OS.GetEnvironment("BARROWWORKS_REVIEW_RETURN_BASELINE") == "1"))
        {
            SizeForCapture();
            _realtime = _flags.Contains("--realtime");
            _barrowworksReview = new BarrowworksReview(_session, _controller, _camera, Path.GetFullPath(review), _realtime);
        }
        else if (_options.TryGetValue("--frontier-perf", out string? frontierPerf))
        {
            SizeForCapture();
            Engine.MaxFps = 0;
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            _frontierPerf = new FrontierPerf(_session, _controller, _camera, GetViewport(), Path.GetFullPath(frontierPerf));
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

    /// <summary>The model files the last boot loaded, in its order, one path a line: the next boot's prefetch (machine-local, rebuilt each boot).</summary>
    private const string LoadOrderPath = "user://model_load_order.txt";

    private void Prefetch(string path)
    {
        if (!File.Exists(path))
            return;
        var order = File.ReadAllLines(path);
        int ahead = Math.Clamp(System.Environment.ProcessorCount / 2, 2, 8);
        _art.Prefetch(order, ahead);
        GD.Print($"UNNAMED load: {order.Length} model files parsed ahead on {ahead} workers, in the last boot's order");
    }

    private void KeepLoadOrder(string path)
    {
        try
        {
            File.WriteAllLines(path, _art.LoadOrder);
        }
        catch (IOException e)
        {
            GD.PushWarning($"UNNAMED load: the model load order could not be kept ({e.Message}); the next start parses in place");
        }
    }


    /// <summary>
    /// The held ring's bindings. Its GPU resources are loaded at entry and released on departure. Sharing the
    /// frontier's overlay would draw the Ashen Hollow's props in the ring's rooms; sharing the frontier's view would
    /// put the ring at the frontier's metres, which is where the body used to stand after a portal (review 3, B6).
    /// </summary>
    private void BuildInterior()
    {
        if (_session.Setup.Interior is not { } ring)
            return;
        string? overlay = Path.Combine(_session.Options.ContentRoot, "_art_bindings_barrowworks.json");
        var bindings = Art.ArtBindings.LoadWithOverlay(
            Art.ArtBindings.ResourcePath, System.IO.File.Exists(overlay) ? overlay : null);
        _interiorBindings = bindings;
        BuildPortalEntrances(_session.Setup.Layout);
    }

    private void LoadInterior()
    {
        if (_interior is not null || _session.Setup.Interior is not { } ring || _interiorBindings is not { } bindings)
            return;
        ulong started = Time.GetTicksUsec();
        _interior = new HollowView { Name = "Barrowworks" };
        _interior.Bind(_art, bindings, new GroundField(ring, bindings, _scatterRules));
        AddChild(_interior);
        _interior.Build(ring);
        _interior.SetFlags(_session.Simulation!.Switches, _session.Simulation.Barriers, inPlay: false);
        foreach (var site in ring.Doors)
        {
            bool open = _session.Simulation.Doors.Single(d => d.Site == site).Open;
            _interior.SetDoor(site.Key, open);
            if (_barrowworksReview is not null)
            {
                if (_interior.DoorShownOpen(site.Key) != open)
                    throw new InvalidOperationException($"Door projection differs from the world: {site.Key}");
                GD.Print($"BARROWWORKS door projection: {site.Key} open={open}");
            }
        }
        if (OS.GetEnvironment("UNNAMED_LOAD_REPORT") == "1")
            GD.Print($"UNNAMED interior build: {(Time.GetTicksUsec() - started) / 1000.0:0.0} ms");
    }

    /// <summary>
    /// A doorway at each exterior anchor, so the way in is a thing in the world rather than a prompt in mid-air.
    /// </summary>
    private void BuildPortalEntrances(RegionLayout layout)
    {
        foreach (var portal in layout.Portals.Portals.Where(p => p.Space != layout.SpaceId))
            _hollow.AddChild(Greybox.PortalEntrance.Build(portal, layout, _art, _interiorBindings ?? _bindings));
    }

    /// <summary>
    /// Show the space the body is in and hide the other (review 3, B6). Called on the space change.
    /// </summary>
    private void ShowSpace(bool outside)
    {
        if (!outside) LoadInterior();
        _hollow.SetSpaceActive(outside);
        if (_scatter is not null)
            _scatter.Visible = outside;
        if (_interior is { } ring)
            ring.SetSpaceActive(!outside);
        var bindings = outside ? _bindings : _interiorBindings ?? _bindings;
        _items.Bind(_art, bindings);
        _items.BuildContainers(_session.Simulation!.ActiveLayout);
        _items.Refresh(_session.Simulation!);
        _crafting.Bind(_art, bindings);
        _crafting.Build(_session.Simulation!.ActiveLayout);
        _crafting.Refresh(_session.Simulation!.Nodes);
        _exteriorCreatures.SetSpaceActive(outside);
        if (outside)
        {
            _creatures = _exteriorCreatures;
            _creatures.ResyncPositions(_session.Simulation!);
        }
        else
        {
            if (_creatures != _exteriorCreatures) _creatures.Free();
            _creatures = new CreaturesView { Name = "InteriorCreatures" };
            _creatures.Bind(_art, bindings);
            _interior!.AddChild(_creatures);
            if (_sounds is not null && IsInstanceValid(_sounds))
            {
                ulong audioStarted = Time.GetTicksUsec();
                _sounds.Prepare(_bindings.InteriorBed);
                foreach (var portal in _session.Setup.Layout.Portals.Portals)
                {
                    string cell = UNNAMED.World.CellKey.OfWorld(portal.ExteriorXMm / 1000.0, portal.ExteriorZMm / 1000.0).ToString();
                    _sounds.Prepare(_bindings.CellSounds.GetValueOrDefault(cell)?.Bed);
                }
                if (OS.GetEnvironment("UNNAMED_LOAD_REPORT") == "1")
                    GD.Print($"UNNAMED portal audio preparation: {(Time.GetTicksUsec() - audioStarted) / 1000.0:0.0} ms");
            }
            PrewarmPortal();
        }
        if (outside && _interior is not null)
        {
            _interior.Free();
            _interior = null;
            int released = _art.ReleaseModels("bw_codex_");
            GD.Print($"UNNAMED interior release: {released} cached models; scene and creature instances freed");
        }
    }

    private void PrewarmPortal()
    {
        if (_interior is null || DisplayServer.GetName() == "headless" || GetViewport().GetCamera3D() is not { } camera) return;
        // Real draws in the actual High viewport prime its AA, skin, shadow and GI variants. Nothing is presented
        // to the window during these draws. Only presentation transforms change; no commands or state are written.
        ulong started = Time.GetTicksMsec();
        double draws = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw);
        double surfaces = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSurface);
        var transform = camera.GlobalTransform;
        var environment = camera.Environment;
        var bodyPosition = _avatar.Position;
        var bursts = new Node3D { Name = "PreparedImpactResources" };
        _interior.AddChild(bursts);
        void Draw(Vector3 at, Vector3 target, Godot.Environment? light)
        {
            ulong drawStarted = Time.GetTicksUsec();
            camera.Environment = light;
            camera.GlobalPosition = at;
            camera.LookAt(target);
            // Node3D normally defers these notifications until frame end; ForceDraw does not advance the tree.
            camera.ForceUpdateTransform();
            _avatar.ForceUpdateTransform();
            for (int i = 0; i < 3; i++) RenderingServer.ForceDraw(false, 1.0 / 60);
            if (OS.GetEnvironment("UNNAMED_LOAD_REPORT") == "1")
                GD.Print($"UNNAMED portal draw at {at}: {(Time.GetTicksUsec() - drawStarted) / 1000.0:0.0} ms");
        }
        try
        {
            // Reuse the main viewport's existing outdoor GI allocation before the first indoor draw; an auxiliary
            // viewport would allocate another volume and compile different shadow/AA specializations.
            _interior.Visible = false;
            _hollow.Visible = true;
            if (_scatter is not null) _scatter.Visible = true;
            _exteriorCreatures.Visible = true;
            _creatures.Visible = false;
            foreach (var portal in _session.Setup.Layout.Portals.Portals)
            {
                float x = portal.ExteriorXMm / 1000f, z = portal.ExteriorZMm / 1000f;
                float y = _ground.Height(x, z);
                _avatar.Position = new Vector3(x, y, z);
                _exteriorCreatures.PrewarmCached(() =>
                    Draw(new Vector3(x, y + 2, z + 5), new Vector3(x, y + 1.5f, z), _hollow.SpaceEnvironment));
            }
            _hollow.Visible = false;
            _exteriorCreatures.Visible = false;
            _creatures.Visible = true;
            if (_scatter is not null) _scatter.Visible = false;
            _interior.Visible = true;
            _avatar.Position = new Vector3(50, 0, 106);
            _creatures.Draw(_session.Simulation!, 0, 0);
            foreach (var room in _session.Setup.Interior!.Space.Blockers.OfType<BoxBlocker>()
                         .Where(b => b.Id.Length > 4 && b.Id[0] == 'r' && b.Id.Contains("_wall", StringComparison.Ordinal))
                         .GroupBy(b => b.Id[..3]))
            {
                float x = (room.Min(b => b.MinXMm) + room.Max(b => b.MaxXMm)) / 2000f;
                float z = (room.Min(b => b.MinZMm) + room.Max(b => b.MaxZMm)) / 2000f;
                Draw(new Vector3(x, 2, z + 3), new Vector3(x, 1.5f, z), _interior.SpaceEnvironment);
            }
            foreach (var at in new[] { new Vector3(40, 2, 101), new Vector3(60, 2, 114), new Vector3(50, 2, 119) })
                Draw(at, new Vector3(50, 1.5f, 108), _interior.SpaceEnvironment);
            void CourtDraw() => Draw(new Vector3(50, 2, 101), new Vector3(50, 1.5f, 106), _interior.SpaceEnvironment);
            _creatures.PrewarmMarks(CourtDraw);
            if (VisualOptions.Recipes)
            {
                _hits.Prepare(bursts, new Vector3(50, 1, 106));
                CourtDraw();
            }
            var body = _controller.Authoritative;
            var feet = HollowView.ToGodot(body.XMm, body.YMm, body.ZMm);
            _avatar.Position = feet;
            Draw(feet + transform.Origin - bodyPosition, feet + new Vector3(0, 1.5f, 0), _interior.SpaceEnvironment);
        }
        finally
        {
            camera.GlobalTransform = transform;
            camera.Environment = environment;
            _avatar.Position = bodyPosition;
            camera.ForceUpdateTransform();
            _avatar.ForceUpdateTransform();
            _hollow.Visible = false;
            _interior.Visible = true;
            _exteriorCreatures.Visible = false;
            _creatures.Visible = true;
            if (_scatter is not null) _scatter.Visible = false;
        }
        GD.Print($"UNNAMED portal prewarm: {Time.GetTicksMsec() - started} ms; court and exterior-return draws; "
                 + $"draw pipelines +{Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw) - draws}, "
                 + $"surfaces +{Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSurface) - surfaces}");
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
        BuildInterior();
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
            _avatarModel = body;
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
        _exteriorCreatures = _creatures;
        _crafting = new CraftingView { Name = "Crafting" };
        _crafting.Bind(_art, _bindings);
        AddChild(_crafting);
        _crafting.Build(_session.Setup.Layout);
        _npcs = new NpcsView { Name = "Npcs", Ground = VisualOptions.BodyModifiers ? _ground.Height : null };
        _npcs.Bind(_art, _bindings);
        if (_session.Setup.Interior is not null && VisualOptions.BodyModifiers)
            _npcs.Ground = (x, z) => _session.Simulation!.ActiveLayout.Space.Terrain.HeightAtMm((long)(x * 1000), (long)(z * 1000)) / 1000f;
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
        if (_options.GetValueOrDefault("--front-shots") is { } frontShots && _session is not null)
            FrontShots(frontShots);
        if (_options.GetValueOrDefault("--creator-shots") is { } creatorShots && _session is not null)
            CreatorShots(creatorShots);
        if (_session?.Simulation is null)
        {
            ReadFrontInput();
            return;
        }

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
            // First, the no-pause doctrine (the owner's ruling, 2026-10-17): every screen open in turn while the world ticks under it.
            if (NoPauseStep() is false)
            {
                GetTree().Quit(1);
                return;
            }
            if (NoPauseDone && _smoke.Update() is { } code)
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
        else if (_barrowworksReview is not null)
        {
            ulong reviewStarted = Time.GetTicksUsec();
            _barrowworksReview.RecordFrame(delta);
            _barrowworksReview.Phase("sample collection", reviewStarted);
            reviewStarted = Time.GetTicksUsec();
            string? reviewShot = _barrowworksReview.Update();
            _barrowworksReview.Phase("driver", reviewStarted);
            switch (reviewShot)
            {
                case "done":
                    _barrowworksReview.WriteTiming();
                    WriteReports(_barrowworksReview.Directory, "barrowworks-frontier-review");
                    GetTree().Quit(0);
                    return;
                case "failed":
                    if (DisplayServer.GetName() != "headless")
                        SaveScreenshot(_barrowworksReview.Directory, "failed");
                    GetTree().Quit(1);
                    return;
                case { } shot:
                    if (DisplayServer.GetName() != "headless")
                    {
                        _barrowworksReview.Screenshot();
                        SaveScreenshot(_barrowworksReview.Directory, shot);
                    }
                    break;
            }
        }
        else if (_frontierPerf is not null)
        {
            string? result = _frontierPerf.Update();
            switch (result)
            {
                case "done":
                case "failed":
                    _frontierPerf.Write();
                    WriteReports(_frontierPerf.Directory, "frontier-rendering-benchmark");
                    GetTree().Quit(result == "done" ? 0 : 1);
                    return;
                case { } shot:
                    if (DisplayServer.GetName() != "headless")
                    {
                        _frontierPerf.Screenshot();
                        SaveScreenshot(_frontierPerf.Directory, shot);
                    }
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
        KeepHireInReach();
        // THE SKY RUNS ON THE WORLD CLOCK. content/config/time.yaml: 20 ticks a second, 1 game minute = 2 real
        // seconds, 1440 game minutes a day - so 57,600 ticks is one full day and night, 48 real minutes. Before
        // this the sun was set once at build time and never moved, which is the owner's "lighting in general is
        // pretty static". Only Sky3D has a clock to move; an HDRI run ignores it.
        if (_session.Simulation is { } clock)
            SkyView.Advance(clock.WorldTick, TicksPerDay);

        // The smoke and the playthrough run one tick per frame: the smoke finishes in a fraction of real time, and the playthrough is
        // the same run every time, whatever the frame rate.
        ulong sessionStarted = _barrowworksReview is null ? 0 : Time.GetTicksUsec();
        var frame = _session.Frame(_smoke is not null || _connectedCheck is not null || (_barrowworksReview is not null && !_realtime) || (_play is not null && !_realtime) || _delta is not null || _survey is not null || _audit is not null || _auditAb is not null
            ? _session.TickSeconds : delta);
        _barrowworksReview?.Phase("simulation", sessionStarted);
        _ticksLastFrame = frame.TicksRun;
        bool outside = _session.Simulation!.PlayerInExterior;
        if (outside != _wasOutside)
        {
            _wasOutside = outside;
            ulong transitionStarted = _barrowworksReview is null ? 0 : Time.GetTicksUsec();
            ShowSpace(outside);
            _barrowworksReview?.Phase(outside ? "exit projections" : "entry projections", transitionStarted);
            _autorun = false;
        }
        if (_stats is not null || _barrowworksReview is not null)
        {
            if (frame.AutosaveTaken is { } taken)
            {
                string note = $"autosave to {taken} taken at {_session.PlaytimeSeconds:0.0} s of play";
                _stats?.Mark(note);
                _barrowworksReview?.Mark(note);
            }
            foreach (var save in frame.Saves)
            {
                string note = $"{save.Slot} written in the background{(save.Failure is { } why ? $" - FAILED: {why}" : "")}";
                _stats?.Mark(note);
                _barrowworksReview?.Mark(note);
            }
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
        ulong drawStarted = _barrowworksReview is null ? 0 : Time.GetTicksUsec();
        Draw(frame.Alpha, delta);
        _barrowworksReview?.Phase("presentation", drawStarted);
        UpdateMouse();
        _stats?.Record(delta);
        if (_perf is { ScreenshotDue: true })
        {
            SaveScreenshot(_perfOut, _perf.SegmentName);
            _stats!.SkipNext();
        }
    }

    /// <summary>
    /// The place a <c>--shot "x,z,..."</c> names, in millimetres, or null when no shot was asked for.
    /// </summary>
    /// <remarks>
    /// Only the first two fields are read here - the place to stand. Everything after them belongs to
    /// <see cref="SurveyShots.One"/>, which parses the spec again for the yaw, pitch, hold and standoff. Null on
    /// anything unparseable, so a bad spec falls back to the region's spawn and <c>SurveyShots.One</c> is still
    /// the one place that reports it.
    /// </remarks>
    private (long X, long Z)? ShotStart()
    {
        if (!_options.TryGetValue("--shot", out string? spec))
            return null;
        var parts = spec.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !double.TryParse(parts[0], out double x) || !double.TryParse(parts[1], out double z))
            return null;
        return ((long)Math.Round(x * 1000), (long)Math.Round(z * 1000));
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
        ulong phaseStart = _barrowworksReview is null ? 0 : Time.GetTicksUsec();
        void Probe(string phase)
        {
            if (_barrowworksReview is null) return;
            _barrowworksReview.Phase(phase, phaseStart);
            phaseStart = Time.GetTicksUsec();
        }
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
        _interior?.DrawWorld(simulation.ActiveSpaceTick, _session.TickSeconds);
        var combat = simulation.Combat;
        _avatar.SetStance(Stance(combat, alpha));
        _avatar.SetDowned(combat.Defeated);
        DrawOpening(delta);
        // Downed is not dead (OD-13 = B): the character lies in view until helped up, and the fade waits for a death.
        _hud.FadeToBlack(combat.Defeated && !combat.Downed && combat.RespawnTicksLeft * _session.TickSeconds <= DeathFadeLeadSeconds);
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
        Probe("avatar/world");
        _creatures.Draw(simulation, alpha, delta);
        Probe("creatures");
        _magicEffects.Draw(_avatar, combat.Casting, combat.Phase,
            combat.Effects.Select(e => (e.EffectId, Math.Max(0, e.ExpiresTick - simulation.WorldTick) * _session.TickSeconds)),
            combat.Strain / (double)Math.Max(1, combat.StrainTolerance), delta);
        Probe("magic effects");
        _soundEvents.SetStation(_inventory.OpenStation?.Kind);
        _soundEvents.Update(feet, speed, simulation.Posture.Airborne, combat.Strain / (double)Math.Max(1, combat.StrainTolerance),
            _inventory.OpenContainer, Screen is not null, delta);
        Probe("sound events");
        _music.Update(feet, delta);
        Probe("music");
        _crafting.Refresh(simulation.Nodes);
        _npcs.Draw(simulation, delta, _session.TickSeconds);
        Probe("crafting/NPCs");
        LogStates(simulation, delta, speed);
        _camera.Follow(_shots?.Viewpoint ?? _avatar.Position, delta);
        _avatar.SetFirstPerson(_camera.EffectiveDistance < 0.4f);
        // A conversation frames the speaker's face over the character's shoulder (not in first person).
        _talkCamera.Frame(simulation.Conversation is { } talk && !_camera.IsFirstPerson ? _npcs.HeadOf(talk.NpcId) : null,
            _avatar.Head, _camera.Camera, delta);
        Probe("camera/state log");
        DrawHud(simulation, combat, posture, alpha, delta);
        Probe("HUD");
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
        if (DisplayServer.GetName() == "headless")
            return;
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
                    or "--front-shots" or "--creator-shots" or "--load-order" or "--barrowworks-review" or "--anim-states" or "--frontier-perf" or "--frontier-point"
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
}
