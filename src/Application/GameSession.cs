// UNNAMED Application - session lifecycle: boot, new game, load, save, the fixed-step loop (ARCHITECTURE.md §2, §7, §8)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Security.Cryptography;
using UNNAMED.Content;
using UNNAMED.Domain;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application;

/// <summary>Where a session finds its content and saves, and which region it plays.</summary>
public sealed record GameOptions(string ContentRoot, string ProfileRoot)
{
    public string RegionId { get; init; } = "region.ashen_hollow";

    /// <summary>The content pack's human label, written to <c>content_version</c>. Diagnostic only (PERSISTENCE.md §4.2).</summary>
    public string ContentVersion { get; init; } = "0.3.0";

    /// <summary>
    /// Hold the profile against a second copy of the game (M-07): the game's own runs do; a test that opens one profile from two sessions
    /// does not.
    /// </summary>
    public bool LockProfile { get; init; }

    /// <summary>Called at each step of every save's commit, on the thread writing it: fault injection for tests. The game leaves it null.</summary>
    public Action<SaveStep>? SaveStepHook { get; init; }
}

/// <summary>Content failed validation: the game refuses to start, naming every file and reason (ARCHITECTURE.md §8.1 step 1).</summary>
public sealed class ContentBootException : Exception
{
    public ContentBootException(IReadOnlyList<ValidationError> errors)
        : base("Content does not validate; refusing to start:\n  " + string.Join("\n  ", errors.Select(e => $"{e.Code} {e.FilePath}: {e.Message}"))) =>
        Errors = errors;

    public IReadOnlyList<ValidationError> Errors { get; }
}

/// <summary>What a view may do with the event bus: listen. Only the simulation publishes (ARCHITECTURE.md §4.3).</summary>
public interface IDomainEvents
{
    void Subscribe<T>(Action<T> handler);

    void Unsubscribe<T>(Action<T> handler);
}

/// <summary>
/// What the start screen offers (the Phase-1 technical audit, B-01). <see cref="Continue"/> is the newest save that can be loaded, an
/// autosave as readily as a quick or manual save; nothing starts until the player chooses.
/// </summary>
public sealed record StartChoice(SaveSummary? Continue, ImmutableArray<SaveSummary> Saves)
{
    /// <summary>Newer saves that Continue passes over because they cannot be loaded as they are. They are shown, never skipped silently.</summary>
    public ImmutableArray<SaveSummary> PassedOver => Saves.TakeWhile(s => s.Problem is not null).ToImmutableArray();

    /// <summary>A new game writes to the same slots as the saves already there, so it is confirmed first when there are any.</summary>
    public bool NewGameAsksFirst => !Saves.IsEmpty;
}

/// <summary>
/// How a save written in the background went (P-01): its slot, whether it was an autosave, the playtime its world was captured at, and
/// what went wrong - null when it was committed and verified.
/// </summary>
public sealed record SaveOutcome(string Slot, bool Auto, double CapturedAtPlaytime, string? Failure);

/// <summary>
/// One presentation frame's worth of simulation: how many fixed ticks ran, how far into the next one the frame is, and the saves that
/// finished since the last frame - each an outcome, never an exception.
/// </summary>
public readonly record struct FrameResult(int TicksRun, double Alpha, ImmutableArray<SaveOutcome> Saves)
{
    /// <summary>An autosave that finished writing, or null.</summary>
    public string? AutosavedTo => Saves.IsDefault ? null : Saves.FirstOrDefault(s => s.Auto && s.Failure is null)?.Slot;

    /// <summary>The autosave taken on this frame - captured here, to be written in the background - or null.</summary>
    public string? AutosaveTaken { get; init; }

    /// <summary>What went wrong with an autosave that failed, or null.</summary>
    public string? AutosaveFailed => Saves.IsDefault ? null : Saves.FirstOrDefault(s => s.Auto && s.Failure is not null)?.Failure;
}

/// <summary>
/// The composition root and session lifecycle. Presentation holds one, submits commands to it, advances it once
/// per rendered frame, and reads the simulation's read-only views; it never reaches state any other way (D-11).
/// </summary>
public sealed class GameSession : IDomainEvents, IDisposable
{
    /// <summary>A frame longer than this is clamped, so a stall or a long load cannot spiral (WORLD_ARCHITECTURE.md §6.1).</summary>
    public const double MaxFrameSeconds = 0.25;

    private readonly EventBus _bus;
    private readonly SaveStore _store;
    private readonly ProfileLock? _lock;
    private readonly IReadOnlyDictionary<string, string> _names;
    private readonly ImmutableArray<BaselineTransition> _transitions;
    private Simulation? _simulation;
    private double _accumulator;
    private double _lastAutosave;

    private GameSession(GameOptions options, SimulationSetup setup, ContentIdentity content, ICellBaselineGenerator generator,
        IReadOnlyDictionary<string, string> names, ImmutableArray<BaselineTransition> transitions)
    {
        Options = options;
        _bus = new EventBus(OnSubscriberFailed);
        _transitions = transitions;
        _names = names;
        Setup = setup;
        Content = content;
        Generator = generator;
        // Before the boot sweep: a second game's sweep would discard the first's staging directory mid-commit (M-07).
        _lock = options.LockProfile ? ProfileLock.Acquire(options.ProfileRoot) : null;
        try
        {
            _store = new SaveStore(options.ProfileRoot, options.SaveStepHook);
            _store.RecoverInterruptedCommits();
        }
        catch
        {
            _lock?.Dispose();
            throw;
        }
    }

    /// <summary>Finish the saves being written (up to 10 s), and let go of the profile if this session held it.</summary>
    public void Dispose()
    {
        WaitForSaves(TimeSpan.FromSeconds(10));
        _lock?.Dispose();
    }

    /// <summary>Boot (ARCHITECTURE.md §8.1): load and validate content, build the rules, open the save profile.</summary>
    /// <summary>The generator fingerprint of M3's layout, the one M3f to M5 saves were written against. Frozen: it names a past layout.</summary>
    private const string M3LayoutFingerprint = "sha256:4c97504c3d0db625e2447230087d2eabb56b4427c486a790f541d677a01b9a1b";

    public static GameSession Boot(GameOptions options)
    {
        var loader = new ContentLoader();
        loader.LoadAll(options.ContentRoot);
        if (loader.HasErrors)
            throw new ContentBootException(loader.Errors.ToList());

        var layout = WorldContent.BuildLayout(loader, options.RegionId);
        var movement = WorldContent.BuildMovement(loader);
        var setup = new SimulationSetup(layout, movement, ProgressionContent.BuildRules(loader),
            WorldContent.BuildTiers(loader), WorldContent.TickMilliseconds(loader))
        {
            Items = new ItemSetup(ItemContent.BuildCatalog(loader), ItemContent.BuildLootTables(loader),
                ItemContent.BuildInventoryRules(loader, movement.InteractReachMm), ItemContent.BuildStartingKit(loader),
                ItemContent.BuildPricing(loader), ItemContent.BuildMerchants(loader)),
            Combat = CombatContent.Build(loader, options.RegionId),
            Magic = MagicContent.Build(loader),
            Crafting = CraftingContent.Build(loader),
            Social = new SocialSetup(SocialContent.BuildNpcs(loader), SocialContent.BuildDialogues(loader)) { Companions = SocialContent.BuildCompanionTuning(loader) },
            Quests = new QuestSetup(QuestContent.BuildQuests(loader)),
        };
        var content = new ContentIdentity(options.ContentVersion, loader.ComputeContentHash(), loader.Definitions.Keys,
            loader.Aliases, loader.Removed, loader.Discarded);
        var terrain = layout.Generation;
        var terrainRule = new TerrainRule(terrain.TerrainBaseHeightMm, terrain.TerrainAmplitudeMm, terrain.TerrainSamplesPerAxis);
        // The region's authored nodes are part of its baseline (M3f), each where it was put.
        var fixedNodes = layout.Nodes.Select(site => new FixedNode(site.Name, site.NodeDefId, CellKey.OfWorld(site.XMm / 1000.0, site.ZMm / 1000.0),
            (int)WorldMath.FloorMod(site.XMm / 10, WorldMath.CellSizeCm), (int)WorldMath.FloorMod(site.ZMm / 10, WorldMath.CellSizeCm)));
        var generator = new CellBaselineGenerator(new GenerationProfile(Array.Empty<NodeRule>(), Array.Empty<PopulationRule>(), terrainRule, fixedNodes));
        // A save from before the region placed its nodes carries onto the baseline that has them: nothing it holds was a node.
        var before = new CellBaselineGenerator(new GenerationProfile(Array.Empty<NodeRule>(), Array.Empty<PopulationRule>(), terrainRule));
        var transitions = before.Fingerprint == generator.Fingerprint
            ? ImmutableArray<BaselineTransition>.Empty
            : ImmutableArray.Create(new BaselineTransition("M3f: the region's resource nodes", before.Fingerprint, generator.Fingerprint));
        // A save from M3's layout (M3f to M5) carries onto the content bible's four cells (M6). The iron seam moved from the north
        // shelf to Blackvein Cut, so a strike recorded against the old seam is declared lost; everything else carries as it is.
        if (generator.Fingerprint != M3LayoutFingerprint)
            transitions = transitions.Add(new BaselineTransition("M6: the content bible's four cells", M3LayoutFingerprint, generator.Fingerprint,
                DropVanishedTargets: true));
        return new GameSession(options, setup, content, generator, WorldContent.DisplayNames(loader), transitions);
    }

    public GameOptions Options { get; }
    public SimulationSetup Setup { get; }
    public ContentIdentity Content { get; }
    public ICellBaselineGenerator Generator { get; }

    /// <summary>The running world; null before a new game or a load.</summary>
    public Simulation? Simulation => _simulation;

    /// <summary>Seconds of play, carried through saves (<c>playtime_seconds</c>).</summary>
    public double PlaytimeSeconds { get; private set; }

    public double TickSeconds => Setup.TickMilliseconds / 1000.0;

    /// <summary>A new character in a new world. A seed of 0 or none picks one at random (PERSISTENCE.md §4.2).</summary>
    public Simulation NewGame(string characterName, ulong seed = 0)
    {
        while (seed == 0)
            seed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
        var id = EntityId.NewId(EntityKind.Character);
        var player = Simulation.NewCharacter(Setup, id, characterName, PlayerRecord.DerivedAppearanceSeed(id));
        var world = new WorldDelta(Generator, seed, new Registry());
        Begin(Simulation.Start(Setup, player, world, 0, _bus), playtime: 0);
        return _simulation!;
    }

    /// <summary>Load a slot through the normative load sequence (PERSISTENCE.md §7.4). Throws a <see cref="SaveException"/> when it cannot.</summary>
    public LoadResult Load(string slot) => Load(slot, SaveCopy.Current);

    /// <summary>
    /// Load one copy of a slot: the save, a backup, or the save it displaced (B-01). A copy other than the save itself is loaded only
    /// because the player chose it (§7.2). Throws a <see cref="SaveException"/> when it cannot.
    /// </summary>
    public LoadResult Load(string slot, SaveCopy copy)
    {
        WaitForSaves(Timeout.InfiniteTimeSpan);   // a save being written finishes first: the slot loaded is the one it committed
        var result = _store.Load(slot, copy, new LoadContext(Generator, Content, new Registry()) { Transitions = _transitions });
        Begin(Simulation.Start(Setup, result.Player, result.World, result.Manifest.WorldTick, _bus), result.Manifest.PlaytimeSeconds);
        return result;
    }

    /// <summary>What the start screen offers: the saves in the profile, newest first, and which one Continue loads (B-01).</summary>
    public StartChoice StartChoice()
    {
        var saves = _store.Summaries();
        return new StartChoice(saves.FirstOrDefault(s => s.Problem is null), saves);
    }

    /// <summary>A slot's backups and the save it displaced unproven, for a player choosing one after the save itself failed.</summary>
    public ImmutableArray<SaveSummary> OtherCopies(string slot) => _store.OtherCopies(slot);

    /// <summary>Save the running world to a slot (the §7.1 write sequence). The world tick is saved, never reset or advanced.</summary>
    public void Save(string slot)
    {
        var document = Capture();
        WaitForSaves(Timeout.InfiniteTimeSpan);   // after any save taken before it, never in front of one
        _store.Save(slot, document);
    }

    /// <summary>
    /// Save the running world without holding up the frame (the Phase-1 technical audit, P-01). The world is captured here, on this thread,
    /// at this tick boundary: an immutable snapshot. It is encoded, written, hashed and verified on a worker, through the same atomic
    /// sequence as any save, one save after another in the order they were captured. Its outcome comes back in a later frame's
    /// <see cref="FrameResult.Saves"/>.
    /// </summary>
    public void SaveInBackground(string slot) => Queue(slot, auto: false);

    /// <summary>
    /// Wait for the saves being written: before quitting, so a save in flight is finished rather than cut off (its commit is atomic
    /// either way). False when they are still running at the timeout.
    /// </summary>
    public bool WaitForSaves(TimeSpan timeout)
    {
        try
        {
            return _writer.Wait(timeout);
        }
        catch (AggregateException)
        {
            return true;   // finished, and failed: its outcome says so
        }
    }

    private sealed record PendingSave(string Slot, bool Auto, double Playtime, Task Written);

    private readonly List<PendingSave> _pending = new();
    private Task _writer = Task.CompletedTask;

    private SaveDocument Capture()
    {
        var simulation = _simulation ?? throw new InvalidOperationException("There is no running world to save");
        return SaveDocuments.Capture(simulation.World, simulation.CaptureRecord(), Content, simulation.WorldTick, PlaytimeSeconds)
            with { CapturedAt = DateTimeOffset.UtcNow };
    }

    private void Queue(string slot, bool auto)
    {
        var document = Capture();
        // One writer, in capture order: each save starts once the one before it has finished, however that one went.
        var written = _writer.ContinueWith(_ => _store.Save(slot, document), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        _writer = written;
        _pending.Add(new PendingSave(slot, auto, document.PlaytimeSeconds, written));
    }

    /// <summary>The saves that finished since the last frame. A failed autosave is tried again sooner than the interval (M-02).</summary>
    private ImmutableArray<SaveOutcome> Finished()
    {
        if (_pending.Count == 0 || !_pending[0].Written.IsCompleted)
            return ImmutableArray<SaveOutcome>.Empty;
        var outcomes = ImmutableArray.CreateBuilder<SaveOutcome>();
        while (_pending.Count > 0 && _pending[0].Written.IsCompleted)
        {
            var save = _pending[0];
            _pending.RemoveAt(0);
            string? failure = save.Written.Exception?.InnerException?.Message;
            outcomes.Add(new SaveOutcome(save.Slot, save.Auto, save.Playtime, failure));
            if (!save.Auto)
                continue;
            if (failure is null)
            {
                _autosaveFailures = 0;
                continue;
            }
            // 30 s after the failed one was taken, then 60, 120, 240: a file held a moment costs little, and a full disk is not hammered.
            _autosaveFailures++;
            _lastAutosave = save.Playtime - AutosaveCadence.IntervalSeconds
                            + Math.Min(AutosaveCadence.IntervalSeconds, 30 * Math.Pow(2, _autosaveFailures - 1));
        }
        return outcomes.ToImmutable();
    }

    /// <summary>A definition's display name from content (its <c>name</c> field), or its ID when it has none.</summary>
    public string DisplayName(string definitionId) => _names.GetValueOrDefault(definitionId, definitionId);

    public IReadOnlyList<string> Slots() => _store.ListSlots();

    public ImmutableArray<int> Backups(string slot) => _store.AvailableBackups(slot);

    /// <summary>Queue a command for the next tick boundary.</summary>
    public void Submit(GameCommand command) =>
        (_simulation ?? throw new InvalidOperationException("There is no running world")).Enqueue(command);

    /// <summary>
    /// Advance by one presentation frame: apply queued commands at the tick boundary, then run as many fixed ticks as
    /// the elapsed time owes. Commands apply in the frame they were submitted in, which keeps input-to-event latency
    /// within one frame (PROTOTYPE.md §6.3). <see cref="FrameResult.Alpha"/> is how far the frame is into the next tick.
    /// </summary>
    public FrameResult Frame(double realSeconds)
    {
        var simulation = _simulation ?? throw new InvalidOperationException("There is no running world");
        double elapsed = Math.Clamp(realSeconds, 0, MaxFrameSeconds);
        _accumulator += elapsed;
        PlaytimeSeconds += elapsed;
        simulation.DrainCommands();
        int ticks = 0;
        while (_accumulator >= TickSeconds)
        {
            simulation.Step();
            _accumulator -= TickSeconds;
            ticks++;
            simulation.DrainCommands();   // anything an event handler queued applies at the next boundary
        }

        var saves = Finished();
        // An autosave is taken at this boundary and written in the background (P-01) - but not while the last is still being written: the
        // next is counted from that one's capture. A failure never stops the frame (M-02); it comes back as an outcome.
        string? taken = null;
        if (!_pending.Any(p => p.Auto) && AutosaveCadence.IsDue(PlaytimeSeconds, _lastAutosave))
        {
            Queue(taken = _store.NextAutosaveSlot(), auto: true);
            _lastAutosave = PlaytimeSeconds;
        }
        return new FrameResult(ticks, _accumulator / TickSeconds, saves) { AutosaveTaken = taken };
    }

    private int _autosaveFailures;

    public void Subscribe<T>(Action<T> handler) => _bus.Subscribe(handler);

    public void Unsubscribe<T>(Action<T> handler) => _bus.Unsubscribe(handler);

    /// <summary>
    /// A subscriber that threw, and the event it threw on (H-02). It was isolated: the tick completed and nothing in it ran twice. The
    /// world never sees it; presentation logs it.
    /// </summary>
    public event Action<Exception, object>? SubscriberFailed;

    /// <summary>How many times a subscriber has thrown this session.</summary>
    public int SubscriberFailures { get; private set; }

    private void OnSubscriberFailed(Exception exception, object @event)
    {
        SubscriberFailures++;
        SubscriberFailed?.Invoke(exception, @event);
    }

    private void Begin(Simulation simulation, double playtime)
    {
        _simulation = simulation;
        _accumulator = 0;
        PlaytimeSeconds = playtime;
        _lastAutosave = playtime;
    }
}
