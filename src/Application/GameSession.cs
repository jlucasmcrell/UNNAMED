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

/// <summary>One presentation frame's worth of simulation: how many fixed ticks ran, and how far into the next one the frame is.</summary>
public readonly record struct FrameResult(int TicksRun, double Alpha, string? AutosavedTo);

/// <summary>
/// The composition root and session lifecycle. Presentation holds one, submits commands to it, advances it once
/// per rendered frame, and reads the simulation's read-only views; it never reaches state any other way (D-11).
/// </summary>
public sealed class GameSession : IDomainEvents
{
    /// <summary>A frame longer than this is clamped, so a stall or a long load cannot spiral (WORLD_ARCHITECTURE.md §6.1).</summary>
    public const double MaxFrameSeconds = 0.25;

    private readonly EventBus _bus = new();
    private readonly SaveStore _store;
    private readonly IReadOnlyDictionary<string, string> _names;
    private Simulation? _simulation;
    private double _accumulator;
    private double _lastAutosave;

    private GameSession(GameOptions options, SimulationSetup setup, ContentIdentity content, ICellBaselineGenerator generator,
        IReadOnlyDictionary<string, string> names)
    {
        Options = options;
        _names = names;
        Setup = setup;
        Content = content;
        Generator = generator;
        _store = new SaveStore(options.ProfileRoot);
        _store.RecoverInterruptedCommits();
    }

    /// <summary>Boot (ARCHITECTURE.md §8.1): load and validate content, build the rules, open the save profile.</summary>
    public static GameSession Boot(GameOptions options)
    {
        var loader = new ContentLoader();
        loader.LoadAll(options.ContentRoot);
        if (loader.HasErrors)
            throw new ContentBootException(loader.Errors.ToList());

        var layout = WorldContent.BuildLayout(loader, options.RegionId);
        var setup = new SimulationSetup(layout, WorldContent.BuildMovement(loader), ProgressionContent.BuildRules(loader),
            WorldContent.BuildTiers(loader), WorldContent.TickMilliseconds(loader));
        var content = new ContentIdentity(options.ContentVersion, loader.ComputeContentHash(), loader.Definitions.Keys,
            loader.Aliases, loader.Removed, loader.Discarded);
        var terrain = layout.Generation;
        var generator = new CellBaselineGenerator(new GenerationProfile(
            Array.Empty<NodeRule>(), Array.Empty<PopulationRule>(),
            new TerrainRule(terrain.TerrainBaseHeightMm, terrain.TerrainAmplitudeMm, terrain.TerrainSamplesPerAxis)));
        return new GameSession(options, setup, content, generator, WorldContent.DisplayNames(loader));
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
    public LoadResult Load(string slot)
    {
        var result = _store.Load(slot, new LoadContext(Generator, Content, new Registry()));
        Begin(Simulation.Start(Setup, result.Player, result.World, result.Manifest.WorldTick, _bus), result.Manifest.PlaytimeSeconds);
        return result;
    }

    /// <summary>Save the running world to a slot (the §7.1 write sequence). The world tick is saved, never reset or advanced.</summary>
    public void Save(string slot)
    {
        var simulation = _simulation ?? throw new InvalidOperationException("There is no running world to save");
        _store.Save(slot, SaveDocuments.Capture(simulation.World, simulation.CaptureRecord(), Content, simulation.WorldTick, PlaytimeSeconds));
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

        string? autosaved = null;
        if (AutosaveCadence.IsDue(PlaytimeSeconds, _lastAutosave))
        {
            autosaved = _store.NextAutosaveSlot();
            Save(autosaved);
            _lastAutosave = PlaytimeSeconds;
        }
        return new FrameResult(ticks, _accumulator / TickSeconds, autosaved);
    }

    public void Subscribe<T>(Action<T> handler) => _bus.Subscribe(handler);

    public void Unsubscribe<T>(Action<T> handler) => _bus.Unsubscribe(handler);

    private void Begin(Simulation simulation, double playtime)
    {
        _simulation = simulation;
        _accumulator = 0;
        PlaytimeSeconds = playtime;
        _lastAutosave = playtime;
    }
}
