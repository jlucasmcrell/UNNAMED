// UNNAMED Application - session lifecycle: boot, new game, load, save, the fixed-step loop (ARCHITECTURE.md §2, §7, §8)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Security.Cryptography;
using UNNAMED.Content;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Guidance;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application;

/// <summary>Where a session finds its content and saves, and which region it plays.</summary>
public sealed record GameOptions(string ContentRoot, string ProfileRoot)
{
    public string RegionId { get; init; } = "region.starting_frontier";

    /// <summary>The content pack's human label, written to <c>content_version</c>. Diagnostic only (PERSISTENCE.md §4.2).</summary>
    public string ContentVersion { get; init; } = "0.3.0";

    /// <summary>
    /// Hold the profile against a second copy of the game (M-07): the game's own runs do; a test that opens one profile from two sessions
    /// does not.
    /// </summary>
    public bool LockProfile { get; init; }

    /// <summary>Called at each step of every save's commit, on the thread writing it: fault injection for tests. The game leaves it null.</summary>
    public Action<SaveStep>? SaveStepHook { get; init; }

    /// <summary>
    /// Whether this run includes the Barrowworks (M8 D10). False registers no portal, so the interior cannot be entered.
    /// The game sets it from <c>--visual barrowworks</c>, which is on unless a run turns it off; a tool or test that
    /// builds its own options asks for the ring explicitly.
    /// </summary>
    public bool Barrowworks { get; init; }

    /// <summary>Candidate pet mechanics may be excluded by the playtest frontend while review is pending.</summary>
    public bool EnablePets { get; init; } = true;

    /// <summary>Where a capture stands a new character; null uses the region's spawn, as every ordinary run does.</summary>
    public long? StartXMm { get; init; }

    /// <inheritdoc cref="StartXMm"/>
    public long? StartZMm { get; init; }

    /// <summary>
    /// What a capture's new character carries in coin; 0 - a player's own opening, as it always was - unless a capture fixture asks for
    /// more. A capture that has to show a purchase (spec 43's hound pup is 60 coin, and a first-evening character carries none) is given a
    /// purse here, at new game, rather than by changing the price of what it buys.
    /// </summary>
    public long StartCurrency { get; init; }

    /// <summary>
    /// What a capture's new character carries besides the starting kit, by the same fixture door as <see cref="StartCurrency"/>: the
    /// Barrowworks review walks in with the band's reference kit (docs/BARROWWORKS_BALANCE.md) and equips it by ordinary commands.
    /// </summary>
    public ImmutableArray<(string ItemId, int Count)> StartItems { get; init; } = [];

    /// <summary>
    /// The level a capture's new character starts at; 0 - a player's own level 1 - unless a capture fixture asks for more. A capture that
    /// has to show a fight in its band (the Stopped Gate's level-5 court) is given the TTK table's character - level L, its L-1 points in
    /// Might - rather than a softer creature.
    /// </summary>
    public int StartLevel { get; init; }

    /// <summary>Where the session reads the real time: the wall clock unless a test gives another (WP11).</summary>
    public Func<DateTimeOffset>? Clock { get; init; }
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
}

/// <summary>A character's save folder as the start screen lists it: whose it is, and its saves, newest first.</summary>
public sealed record CharacterSaves(string? Character, string? Name, ImmutableArray<SaveSummary> Saves);

/// <summary>
/// A login (WP11): the live save Continue loaded, when it was saved and when it was loaded by the session's clock, the absence that asked
/// for (<see cref="CatchUp.RequestedTicks"/>) and what the world honoured (DN1), and what happened meanwhile that the character could know.
/// </summary>
public sealed record Login(string Slot, LoadResult Result, DateTimeOffset SavedUtc, DateTimeOffset NowUtc, CatchUp CatchUp, AdvanceLog Away);

/// <summary>
/// How a save written in the background went (P-01): its slot, whether it was an autosave, the playtime its world was captured at, and
/// what went wrong - null when it was committed and verified.
/// </summary>
public sealed record SaveOutcome(string Slot, bool Auto, double CapturedAtPlaytime, string? Failure)
{
    /// <summary>What went wrong, in a player's words, with a report they can copy (PJ10 §6.3.1); null when the save was committed.</summary>
    public SaveTrouble? Trouble { get; init; }
}

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

    /// <summary>
    /// The least playtime between an event's autosave and the autosave before it: a run of quests completed together, or a stone travelled
    /// straight after a quest, is one save, not a burst.
    /// </summary>
    public const double EventAutosaveSpacingSeconds = 10;

    private readonly EventBus _bus;
    private readonly SaveStore _profileStore;
    private readonly Dictionary<string, SaveStore> _characterStores = new(StringComparer.Ordinal);
    private SaveStore _store;
    private ReplayLogWriter? _replay;
    private string? _character;
    private CharacterCard? _card;
    private readonly ProfileLock? _lock;
    private readonly IReadOnlyDictionary<string, string> _names;
    private readonly ImmutableArray<BaselineTransition> _transitions;
    private Simulation? _simulation;
    private double _accumulator;
    private double _lastAutosave;
    private long _worldTimeAdvanced;
    private ImmutableArray<AdvanceLine> _away = ImmutableArray<AdvanceLine>.Empty;

    private GameSession(GameOptions options, SimulationSetup setup, ContentIdentity content, ICellBaselineGenerator generator,
        IReadOnlyDictionary<string, string> names, ImmutableArray<BaselineTransition> transitions)
    {
        Options = options;
        _bus = new EventBus(OnSubscriberFailed);
        // Event autosaves (WP4; PERSISTENCE.md §7.4): a quest completed and an arrival by the stones each save the world soon after.
        _bus.Subscribe<QuestCompleted>(_ => RequestAutosave());
        _bus.Subscribe<PlayerTravelled>(_ => RequestAutosave());
        _transitions = transitions;
        _names = names;
        Setup = setup;
        Content = content;
        Generator = generator;
        // Before the boot sweep: a second game's sweep would discard the first's staging directory mid-commit (M-07).
        _lock = options.LockProfile ? ProfileLock.Acquire(options.ProfileRoot) : null;
        try
        {
            _profileStore = new SaveStore(options.ProfileRoot, options.SaveStepHook);
            _profileStore.RecoverInterruptedCommits();
            CharacterFolders.SweepDeleted(options.ProfileRoot);   // a deletion a crash cut short is finished, never listed
            foreach (var character in CharacterFolders.List(options.ProfileRoot))
                StoreFor(character.Value).RecoverInterruptedCommits();
            // Until a game starts, the saves in play are those Continue would load: the newest loadable save's character.
            _character = StartChoice().Continue?.Character;
            _store = StoreOf(_character);
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

/// <summary>
    /// The region's combat setup, with each reachable interior's spawns added (B1). A spawn carries the space it stands in, so the
    /// added creatures are that interior's and are never seen or struck from the exterior.
    /// </summary>
    private static CombatSetup CoLoadSpawns(CombatSetup primary, ContentLoader loader, RegionLayout layout, IEnumerable<(string RegionId, RegionLayout Layout)> interiors)
    {
        foreach (var (regionId, interior) in interiors)
        {
            var inner = CombatContent.Build(loader, regionId, interior.SpaceId).Spawns;
            primary = primary with { Spawns = primary.Spawns.AddRange(inner.Where(s => s.Space != layout.SpaceId)) };
        }
        return primary;
    }

    /// <summary>The Barrowworks' space: the one interior the run's gate holds back (M8 D10).</summary>
    private static readonly SpaceId BarrowworksSpace = SpaceId.Parse("space.barrowworks");

    public static GameSession Boot(GameOptions options)
    {
        var loader = new ContentLoader();
        loader.LoadAll(options.ContentRoot);
        if (loader.HasErrors)
            throw new ContentBootException(loader.Errors.ToList());

        var layout = WorldContent.BuildLayout(loader, options.RegionId);
        // The Barrowworks is gated here (M8 D10). Content may declare its entrances whenever they are
        // authored; without the gate none of them is registered, so no portal exists, EnterSpaceCommand
        // cannot succeed, and the interior is unreachable. The default-off guarantee is therefore enforced
        // in code rather than by content discipline - a forgotten flag cannot leak the interior into a
        // normal launch, and `--visual phase_a` (which restores every default) closes it too.
        if (!options.Barrowworks)
            layout = layout with { Portals = new SpacePortalSet(layout.Portals.Portals.Where(p => p.Space != BarrowworksSpace).ToImmutableArray()) };
        var movement = WorldContent.BuildMovement(loader);
        // Every space this run holds the ground of. The region's own always; the interior's when the gate is on and
        // the region declares a way in. Its CONTENT - creatures, nodes, NPCs - is still the region's alone until B1
        // routes those per space; what this makes possible is a height or a wall test in the interior's own space
        // rather than the exterior's metres under it.
        var spaces = ImmutableDictionary<SpaceId, WalkSpace>.Empty.Add(layout.SpaceId, layout.Space);
        // Every interior this run can reach, built once and shared by the ground map and the spawn co-load. An interior's region is
        // named for its space: space.barrowworks is region.barrowworks.
        var layouts = ImmutableDictionary<SpaceId, RegionLayout>.Empty.Add(layout.SpaceId, layout);
        var interiors = new List<(string RegionId, RegionLayout Layout)>();
        foreach (var space in layout.Portals.Portals.Select(p => p.Space).Where(s => s != layout.SpaceId).Distinct())
        {
            string regionId = "region." + space.Name["space.".Length..];
            var inner = WorldContent.BuildLayout(loader, regionId);
            if (inner.SpaceId != space)
                throw new InvalidDataException($"{regionId} stands in {inner.SpaceId}, not the {space} its portals lead to");
            interiors.Add((regionId, inner));
            layouts = layouts.SetItem(inner.SpaceId, inner);
            spaces = spaces.SetItem(space, inner.Space);
        }

        var catalog = ItemContent.BuildCatalog(loader);
        var merchants = ItemContent.BuildMerchants(loader);
        if (!options.EnablePets)
            merchants = merchants.ToImmutableSortedDictionary(p => p.Key,
                p => p.Value with { Stock = p.Value.Stock.Where(s => catalog.Find(s.ItemId)?.PetCreature is null).ToImmutableArray() }, StringComparer.Ordinal);

        var setup = new SimulationSetup(layout, movement, ProgressionContent.BuildRules(loader),
            WorldContent.BuildTiers(loader), WorldContent.TickMilliseconds(loader))
        {
            Items = new ItemSetup(catalog, ItemContent.BuildLootTables(loader),
                ItemContent.BuildInventoryRules(loader, movement.InteractReachMm), ItemContent.BuildStartingKit(loader),
                ItemContent.BuildPricing(loader), merchants),
            // The region's spawns carry the region's space, and the ring's carry the ring's, so a creature is
            // only ever seen, struck or advanced in the space it stands in (B1).
            Combat = CoLoadSpawns(CombatContent.Build(loader, options.RegionId, layout.SpaceId), loader, layout, interiors),
            Magic = MagicContent.Build(loader),
            Crafting = CraftingContent.Build(loader),
            Social = new SocialSetup(SocialContent.BuildNpcs(loader), SocialContent.BuildDialogues(loader))
            {
                Companions = SocialContent.BuildCompanionTuning(loader),
                Party = SocialContent.BuildPartyRules(loader),
            },
            Quests = new QuestSetup(QuestContent.BuildQuests(loader)),
            Factions = FactionContent.Build(loader),
            Travel = WorldContent.BuildTravel(loader),
            Character = CharacterContent.Build(loader),
            Clock = WorldContent.BuildClock(loader),
            Offline = WorldContent.BuildOffline(loader),
            Calendar = WorldContent.BuildCalendar(loader),
            Weather = WorldContent.BuildWeather(loader),
            Economy = EconomyContent.Build(loader),
            Techniques = TeachingContent.BuildTechniques(loader),
            Chat = ChatterContent.Build(loader),
            Navigation = NavigationContent.Build(loader),
            NpcLife = NpcLifeContent.Build(loader, options.RegionId),
            Adventurers = AdventurerContent.Build(loader),
            Lore = LoreContent.Build(loader),
            // Pets (spec 43): the taming rules and the species a creature is a pet as; null in a pack with none.
            Pets = options.EnablePets && PetContent.BuildSpecies(loader) is { Count: > 0 } species
                ? new UNNAMED.Domain.Pets.PetSetup(PetContent.BuildRules(loader), species)
                : null,
            Spaces = spaces,
            Layouts = layouts,
        };
        var content = new ContentIdentity(options.ContentVersion, loader.ComputeContentHash(), loader.Definitions.Keys,
            loader.Aliases, loader.Removed, loader.Discarded);
        var terrain = layout.Generation;
        var terrainRule = new TerrainRule(terrain.TerrainBaseHeightMm, terrain.TerrainAmplitudeMm, terrain.TerrainSamplesPerAxis);
        // The region's authored nodes are part of its baseline (M3f), each where it was put.
        var fixedNodes = setup.Sites(l => l.Nodes).Select(pair => new FixedNode(pair.Site.Name, pair.Site.NodeDefId,
            CellKey.OfInterior(pair.Layout.SpaceId, pair.Site.XMm / 1000.0, pair.Site.ZMm / 1000.0),
            (int)WorldMath.FloorMod(pair.Site.XMm / 10, WorldMath.CellSizeCm), (int)WorldMath.FloorMod(pair.Site.ZMm / 10, WorldMath.CellSizeCm)));
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
        return new GameSession(options, setup, content, generator, WorldContent.DisplayNames(loader), transitions)
        {
            ClockAtWorldStartHours = WorldContent.ClockAtWorldStartHours(loader),
            Hints = HintContent.Build(loader),
        };
    }

    public GameOptions Options { get; }
    public SimulationSetup Setup { get; }

    /// <summary>What the world clock reads at tick 0, in hours (config.time): the sky shows it and the creatures keep their hours by it.</summary>
    public double ClockAtWorldStartHours { get; private init; }

    /// <summary>The guidance hints (WP10): presentation shows them once each, as the player's guidance level allows. Not game state.</summary>
    public ImmutableSortedDictionary<string, HintDefinition> Hints { get; private init; } =
        ImmutableSortedDictionary.Create<string, HintDefinition>(StringComparer.Ordinal);
    public ContentIdentity Content { get; }
    public ICellBaselineGenerator Generator { get; }

    /// <summary>The running world; null before a new game or a load.</summary>
    public Simulation? Simulation => _simulation;

    /// <summary>Seconds of play, carried through saves (<c>playtime_seconds</c>).</summary>
    public double PlaytimeSeconds { get; private set; }

    /// <summary>Every world tick an absence has honoured in this world's life (<c>world_time_advanced_ticks</c>; canon PERSISTENCE.md).</summary>
    public long WorldTimeAdvancedTicks => _worldTimeAdvanced;

    /// <summary>The last login's away log, as the running world's saves carry it (its tail); empty before any catch-up.</summary>
    public ImmutableArray<AdvanceLine> AwayLog => _away;

    /// <summary>The last <see cref="Continue"/> in this session, or null.</summary>
    public Login? LastLogin { get; private set; }

    /// <summary>The real time now: the one place the wall clock is read (WP11).</summary>
    private DateTimeOffset Now() => Options.Clock?.Invoke() ?? DateTimeOffset.UtcNow;

    public double TickSeconds => Setup.TickMilliseconds / 1000.0;

    public ImmutableArray<BaselineTransition> Transitions => _transitions;

    /// <summary>The terrain-only baseline fingerprint: the same region generation with no fixed nodes or populations.</summary>
    public string FingerprintWithoutNodes
    {
        get
        {
            var generation = Setup.Layout.Generation;
            var terrain = new TerrainRule(generation.TerrainBaseHeightMm, generation.TerrainAmplitudeMm, generation.TerrainSamplesPerAxis);
            return new CellBaselineGenerator(new GenerationProfile(Array.Empty<NodeRule>(), Array.Empty<PopulationRule>(), terrain)).Fingerprint;
        }
    }

    /// <summary>The peoples a character can be made from and the Calls they can choose: what the creator offers (WP2).</summary>
    public CharacterSetup Characters => Setup.Character;

    /// <summary>
    /// The character folder whose saves are in play (OD-14): the running character's, or before a game starts the one Continue would load.
    /// Null for the saves written at the profile root before folders existed.
    /// </summary>
    public string? Character => _character;

    /// <summary>The folder the character in play saves to: its own under <c>characters/</c>, or the profile root for a save from before folders.</summary>
    public string SaveFolder => _store.Root;

    /// <summary>
    /// Capture-only: the review world. The character starts on one quarter of flat open grass with the creatures it names standing where it names
    /// them, and NOT ONE other spawner in the region - so a review's beats happen where the camera puts them instead of wherever the open world
    /// happened to be (spec 43's stills were lost to hound dens and forge yards for seven rounds). Every spawner is replaced, which is why this is
    /// a capture's entry point and never a player's: a launch with <c>--pet-review</c> builds this world and no other launch can.
    /// </summary>
    public Simulation OpenReview(CharacterSpec spec, (long XMm, long ZMm) player, IEnumerable<(string CreatureId, long XMm, long ZMm)> creatures,
        ulong seed = 42)
    {
        var spawns = creatures.Select((c, i) => new SpawnSite($"spawn.review.{i}", c.XMm, c.ZMm, 0,
                ImmutableArray.Create(new SpawnMember(c.CreatureId, "pack_hunter"))))
            .ToImmutableArray();
        var setup = Setup with { Combat = Setup.Combat with { Spawns = spawns } };
        var id = EntityId.NewId(EntityKind.Character);
        var record = Simulation.NewCharacter(setup, id, spec, PlayerRecord.DerivedAppearanceSeed(id), player.XMm, player.ZMm);
        if (Options.StartCurrency > 0)
            record = record.WithCurrency(Options.StartCurrency);
        record = record.WithInventory(record.Inventory
            .Add(new InventoryEntry(EntityId.NewId(EntityKind.Item), "item.misc.hound_pup", 1))
            .Add(new InventoryEntry(EntityId.NewId(EntityKind.Item), "item.material.raw_meat", 8)));
        var world = new WorldDelta(Generator, seed, new Registry());
        var simulation = Simulation.Start(setup, record, world, 0, _bus);
        _character = id.Value;
        _store = StoreFor(id.Value);
        _card = new CharacterCard(id.Value, record.Name, spec.PeopleId, spec.CallId);
        Begin(simulation, playtime: 0, worldTimeAdvanced: 0, ImmutableArray<AdvanceLine>.Empty);
        return _simulation!;
    }

    /// <summary>
    /// A new character, made from the creator's spec, in a new world. A seed of 0 or none picks one at random (PERSISTENCE.md §4.2). The
    /// character's saves go to a folder of their own (OD-14). Throws an <see cref="ArgumentException"/> when the spec is invalid or names
    /// a people or Call this content does not offer.
    /// </summary>
    public Simulation NewGame(CharacterSpec spec, ulong seed = 0)
    {
        while (seed == 0)
            seed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
        var id = EntityId.NewId(EntityKind.Character);
        var player = Simulation.NewCharacter(Setup, id, spec, PlayerRecord.DerivedAppearanceSeed(id),
            Options.StartXMm, Options.StartZMm);
        if (Options.StartCurrency > 0)
            player = player.WithCurrency(Options.StartCurrency);
        if (!Options.StartItems.IsEmpty)
            player = player.WithInventory(player.Inventory.Concat(Options.StartItems.Select(i => new InventoryEntry(EntityId.NewId(EntityKind.Item), i.ItemId, i.Count))));
        if (Options.StartLevel > 1)
            player = player.WithProgression(player.Progression with
            {
                Level = Options.StartLevel,
                Allocation = ImmutableSortedDictionary.CreateRange(new[] { KeyValuePair.Create(CharacterAttribute.Might, Options.StartLevel - 1) }),
            });
        var world = new WorldDelta(Generator, seed, new Registry());
        var simulation = Simulation.Start(Setup, player, world, 0, _bus);
        _character = id.Value;   // a save of the last character still being written goes to that character's folder: Queue kept it
        _store = StoreFor(id.Value);
        _card = new CharacterCard(id.Value, player.Name, spec.PeopleId, spec.CallId);
        Begin(simulation, playtime: 0, worldTimeAdvanced: 0, ImmutableArray<AdvanceLine>.Empty);
        OpenReplayLog(new ReplayLogStart(seed, spec, Content.Hash, WorldgenIdentity.Of(simulation.World.Generator).Fingerprint), resume: false);
        // The story begins before the first tick: the journal's first entries are there when the character first looks (WP4).
        if (_simulation!.HasOpeningQuests)
        {
            _simulation.Enqueue(new BeginStoryCommand(id));
            _simulation.DrainCommands();
        }
        return _simulation!;
    }

    /// <summary>
    /// Log in (WP11; DN7): load the character's live state - the newest save that loads, Continue on the start screen - and let the world
    /// go on for as long as they were away. The absence is measured from the save's <c>saved_utc</c> by the session's clock, capped at
    /// one Reckoning (config.time), and applied through <see cref="Simulation.Advance"/>, every system bringing itself to the new tick;
    /// then the login grace begins. A clock that reads earlier than the save catches nothing up. Throws a <see cref="SaveException"/> when
    /// there is nothing to continue or it cannot be loaded.
    /// </summary>
    public Login Continue()
    {
        var live = StartChoice().Continue ?? throw new SaveException("There is no save to continue");
        var result = Load(live);
        var now = Now();
        var catchUp = OfflineCatchUp.Compute(result.Manifest.SavedAt, now, 1000 / Setup.TickMilliseconds, Setup.Offline.CapTicks);
        var away = _simulation!.Advance(catchUp.HonouredTicks);
        _simulation.BeginLoginGrace();
        _worldTimeAdvanced += catchUp.HonouredTicks;
        _away = away.Tail(AwayLogEntry.Kept);
        return LastLogin = new Login(live.Slot, result, result.Manifest.SavedAt, now, catchUp, away);
    }

    /// <summary>Restore a slot of the character in play through the normative load sequence (PERSISTENCE.md §7.4). Throws a <see cref="SaveException"/> when it cannot.</summary>
    public LoadResult Load(string slot) => Load(slot, SaveCopy.Current);

    /// <summary>
    /// Restore one copy of a slot of the character in play: the save, a backup, or the save it displaced (B-01) - the moment it holds,
    /// exactly, with no catch-up (DN7: a restore is for recovery; only <see cref="Continue"/> lets the world go on). A copy other than the
    /// save itself is loaded only because the player chose it (§7.2). Throws a <see cref="SaveException"/> when it cannot.
    /// </summary>
    public LoadResult Load(string slot, SaveCopy copy) => Load(_character, slot, copy);

    /// <summary>Load a save as the start screen or the saves list offered it, from whichever character's folder holds it.</summary>
    public LoadResult Load(SaveSummary save) => Load(save.Character, save.Slot, save.Copy);

    private LoadResult Load(string? character, string slot, SaveCopy copy)
    {
        WaitForSaves(Timeout.InfiniteTimeSpan);   // a save being written finishes first: the slot loaded is the one it committed
        var store = StoreOf(character);
        var result = store.Load(slot, copy, new LoadContext(Generator, Content, new Registry()) { Transitions = _transitions });
        // Only once it loaded: a refused load leaves the running world, and the folder it saves to, as they were.
        _character = character;
        _store = store;
        _card = character is null ? null
            : CharacterFolders.ReadCard(store.Root) ?? new CharacterCard(character, result.Player.Name, result.Player.Identity.PeopleId, result.Player.Identity.CallId);
        Begin(Simulation.Start(Setup, result.Player, result.World, result.Manifest.WorldTick, _bus, restoreAttempt: true), result.Manifest.PlaytimeSeconds,
            result.Manifest.WorldTimeAdvancedTicks, result.Manifest.AwayLog.Select(e => e.ToLine()).ToImmutableArray());
        OpenReplayLog(new ReplayLogStart(result.World.WorldSeed, null, Content.Hash, WorldgenIdentity.Of(result.World.Generator).Fingerprint, slot),
            resume: true);
        if (_simulation!.Player.Body.XMm != result.Player.XMm || _simulation.Player.Body.ZMm != result.Player.ZMm)
            NoteAway(_simulation.WorldTick, new[] { "Active boss encounters restart when loaded. You return outside the court with your saved resources." });
        return result;
    }

    /// <summary>Run the whole load pipeline against a save directory without writing anything.</summary>
    public MigrationReport PlanLoadAt(string saveDirectory) =>
        SaveStore.PlanMigrationAt(saveDirectory, new LoadContext(Generator, Content, new Registry()) { Transitions = _transitions });

    /// <summary>The saves of the character in play, newest first.</summary>
    public ImmutableArray<SaveSummary> SaveSlots() => Summaries(_character);

    /// <summary>
    /// What the start screen offers: every character's saves, newest first, and which one Continue loads (B-01). Each save names the
    /// folder it is in, so loading it reaches the right character.
    /// </summary>
    public StartChoice StartChoice()
    {
        var saves = Folders().SelectMany(folder => Summaries(folder))
            .OrderByDescending(s => s.WrittenAt).ThenByDescending(s => s.PlaytimeSeconds).ThenBy(s => s.Slot, StringComparer.Ordinal)
            .ThenBy(s => s.Character, StringComparer.Ordinal)
            .ToImmutableArray();
        return new StartChoice(saves.FirstOrDefault(s => s.Problem is null), saves);
    }

    /// <summary>Every character with saves, the one with the newest save first; the saves from before folders, if any, as one more.</summary>
    public ImmutableArray<CharacterSaves> CharacterSaves() =>
        StartChoice().Saves.GroupBy(s => s.Character ?? "")   // in the start screen's order: the first group holds the newest save
            .Select(g => new CharacterSaves(g.First().Character,
                g.First().Character is { } folder ? CharacterFolders.ReadCard(StoreOf(folder).Root)?.Name : null, g.ToImmutableArray()))
            .ToImmutableArray();

    /// <summary>A slot's backups and the save it displaced unproven, of the character in play, for a player choosing one after the save itself failed.</summary>
    public ImmutableArray<SaveSummary> OtherCopies(string slot) => Tagged(_store.OtherCopies(slot), _character);

    /// <summary>The other copies of a save as the saves list offered it, from whichever character's folder holds it.</summary>
    public ImmutableArray<SaveSummary> OtherCopies(SaveSummary save) => Tagged(StoreOf(save.Character).OtherCopies(save.Slot), save.Character);

    /// <summary>
    /// Delete a character and all its saves (PJ10 §5.2), from the title or the saves list. Refused for the character in play while its world
    /// runs: its next save would bring the folder back. Saves still being written finish first. False when it had no folder; the saves
    /// from before folders (null) are not a character and cannot be deleted this way.
    /// </summary>
    public bool DeleteCharacter(string character)
    {
        if (_simulation is not null && character == _character)
            throw new InvalidOperationException("The character in play cannot be deleted while its world runs");
        var id = EntityId.TryParse(character, out var parsed) && parsed.Kind == EntityKind.Character
            ? parsed
            : throw new ArgumentException($"'{character}' is not a character's save folder", nameof(character));
        WaitForSaves(Timeout.InfiniteTimeSpan);
        lock (_characterStores)
            _characterStores.Remove(character);
        bool deleted = CharacterFolders.Delete(Options.ProfileRoot, id);
        if (_simulation is null && character == _character)
        {
            _character = StartChoice().Continue?.Character;
            _store = StoreOf(_character);
        }
        return deleted;
    }

    /// <summary>The folder a character's saves are in (null: the saves from before folders, at the profile root): what "Open saves folder" opens.</summary>
    public string FolderOf(string? character) => StoreOf(character).Root;

    /// <summary>A save is being written in the background now: what the "Saving…" indicator shows (PJ10 §6.2).</summary>
    public bool IsSaving => _pending.Count > 0 && !_pending[^1].Written.IsCompleted;

    /// <summary>The profile root's saves (null), then each character's folder.</summary>
    private IEnumerable<string?> Folders() =>
        new string?[] { null }.Concat(CharacterFolders.List(Options.ProfileRoot).Select(id => (string?)id.Value));

    private ImmutableArray<SaveSummary> Summaries(string? character) => Tagged(StoreOf(character).Summaries(), character);

    private static ImmutableArray<SaveSummary> Tagged(ImmutableArray<SaveSummary> saves, string? character) =>
        saves.Select(s => s with { Character = character }).ToImmutableArray();

    private SaveStore StoreOf(string? character) => character is null ? _profileStore : StoreFor(character);

    /// <summary>One store per folder for the session (a store serializes its own writes), made on first use.</summary>
    private SaveStore StoreFor(string character)
    {
        lock (_characterStores)
        {
            if (!_characterStores.TryGetValue(character, out var store))
            {
                var id = EntityId.TryParse(character, out var parsed) && parsed.Kind == EntityKind.Character
                    ? parsed
                    : throw new ArgumentException($"'{character}' is not a character's save folder", nameof(character));
                _characterStores[character] = store = new SaveStore(CharacterFolders.PathOf(Options.ProfileRoot, id), Options.SaveStepHook);
            }
            return store;
        }
    }

    /// <summary>Save the running world to a slot (the §7.1 write sequence). The world tick is saved, never reset or advanced.</summary>
    public void Save(string slot)
    {
        var document = Capture();
        WaitForSaves(Timeout.InfiniteTimeSpan);   // after any save taken before it, never in front of one
        _store.Save(slot, document);
        RecordCard(_store, _card);
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
        var now = Now();
        _replay?.Sync(simulation.CommandLog);
        return SaveDocuments.Capture(simulation.World, simulation.CaptureRecord(), Content, simulation.WorldTick, PlaytimeSeconds, _worldTimeAdvanced)
            with
            {
                CapturedAt = now, SavedUtc = now, AwayLog = _away,
                CommandLogSha256 = _store is null ? null : ReplayLog.Digest(_store.Root, simulation.WorldTick),
            };
    }

    /// <summary>
    /// Begins the session's command log (PERSISTENCE.md §5.7): the inputs a replay needs, kept beside the character's saves. A load
    /// begins a new session in a folder that already holds one, so the part it finds is rotated aside rather than appended to.
    /// </summary>
    private void OpenReplayLog(ReplayLogStart start, bool resume)
    {
        _replay?.Dispose();
        _replay = new ReplayLogWriter(_store.Root, start, _simulation?.PlayerId, resume);
    }

    private void Queue(string slot, bool auto)
    {
        var document = Capture();
        var (store, card) = (_store, _card);   // the folder of the character it was taken from, whatever is in play when it is written
        // One writer, in capture order: each save starts once the one before it has finished, however that one went.
        var written = _writer.ContinueWith(_ =>
        {
            store.Save(slot, document);
            RecordCard(store, card);
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
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
            var error = save.Written.Exception?.InnerException;
            string? failure = error?.Message;
            outcomes.Add(new SaveOutcome(save.Slot, save.Auto, save.Playtime, failure) { Trouble = error is null ? null : SaveTrouble.OfSave(error) });
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

    /// <summary>
    /// After a character's save is committed, its folder's card, if the folder has none yet, so the start screen can name whose saves they
    /// are. Only a label: a card that cannot be written is left for the next save, and never fails the save.
    /// </summary>
    private static void RecordCard(SaveStore store, CharacterCard? card)
    {
        if (card is null || File.Exists(Path.Combine(store.Root, CharacterFolders.CardFile)))
            return;
        try
        {
            CharacterFolders.WriteCard(store.Root, card);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The saves are there; the start screen lists them unnamed until a later save writes it.
        }
    }

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

        _replay?.Sync(simulation.CommandLog);
        var saves = Finished();
        // An autosave is taken at this boundary and written in the background (P-01) - but not while the last is still being written: the
        // next is counted from that one's capture. A failure never stops the frame (M-02); it comes back as an outcome.
        string? taken = null;
        bool writing = _pending.Any(p => p.Auto);
        if (!writing && (AutosaveCadence.IsDue(PlaytimeSeconds, _lastAutosave)
                         || _autosaveRequested && PlaytimeSeconds - _lastAutosave >= EventAutosaveSpacingSeconds))
        {
            Queue(taken = _store.NextAutosaveSlot(), auto: true);
            _lastAutosave = PlaytimeSeconds;
            _autosaveRequested = false;
        }
        return new FrameResult(ticks, _accumulator / TickSeconds, saves) { AutosaveTaken = taken };
    }

    private int _autosaveFailures;
    private bool _autosaveRequested;

    /// <summary>
    /// Autosave soon, because something worth keeping just happened (WP4's event autosaves): a quest completed and an arrival by the stones
    /// ask through the events; entering or leaving a space and finishing the creator ask here. It is taken at a frame's tick boundary and
    /// written in the background like any autosave, no sooner than <see cref="EventAutosaveSpacingSeconds"/> after the last one.
    /// </summary>
    public void RequestAutosave() => _autosaveRequested = _simulation is not null;

    /// <summary>Whether an event's autosave is waiting to be taken.</summary>
    public bool AutosaveRequested => _autosaveRequested;

    private readonly List<JournalEntry> _awayNotes = new();

    /// <summary>
    /// The journal, its Recent list holding the away log's lines among the quests' own (WP11 writes them through <see cref="NoteAway"/>);
    /// null before a world runs.
    /// </summary>
    public JournalView? Journal
    {
        get
        {
            if (_simulation?.Journal is not { } journal)
                return null;
            // Oldest first into the merge, which puts what came later first.
            return _awayNotes.Count == 0 ? journal : journal with { Recent = QuestJournal.Recent(journal.Recent.Reverse().Concat(_awayNotes)) };
        }
    }

    /// <summary>Lines of the "while you were away" log, for the journal's Recent list, at the tick the character came back.</summary>
    public void NoteAway(long tick, IEnumerable<string> lines) =>
        _awayNotes.AddRange(lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => new JournalEntry(tick, JournalEntryKind.Away, l)));

    /// <summary>The running character's journal notes; null before a world runs.</summary>
    public JournalNotes? Notes { get; private set; }

    /// <summary>Write a note in the journal, at the current world tick; the reason it was refused, or null.</summary>
    public string? AddNote(string text) =>
        Notes is { } notes ? notes.Add(text, _simulation!.WorldTick) : "there is no running world";

    /// <summary>Cross out a journal note; the reason it was refused, or null.</summary>
    public string? RemoveNote(int index) => Notes is { } notes ? notes.Remove(index) : "there is no running world";

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

    private void Begin(Simulation simulation, double playtime, long worldTimeAdvanced, ImmutableArray<AdvanceLine> away)
    {
        _simulation = simulation;
        _accumulator = 0;
        PlaytimeSeconds = playtime;
        _lastAutosave = playtime;
        _autosaveRequested = false;
        _awayNotes.Clear();
        Notes = new JournalNotes(Options.ProfileRoot, simulation.PlayerId);
        _worldTimeAdvanced = worldTimeAdvanced;
        _away = away;
        LastLogin = null;
    }
}
