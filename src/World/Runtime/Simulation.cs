// UNNAMED World - the running simulation: composition root, command queue, fixed tick (ARCHITECTURE.md §4.5, §7, §8.1)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Economy;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.Domain.Travel;

namespace UNNAMED.World.Runtime;

/// <summary>The rules a simulation runs under, built from content at boot.</summary>
public sealed record SimulationSetup(RegionLayout Layout, MovementRules Movement, ProgressionRules Progression, TierRules Tiers, int TickMilliseconds)
{
    /// <summary>Items, loot tables, carrying rules and the starting kit (M3b).</summary>
    public ItemSetup Items { get; init; } = ItemSetup.Empty;

    /// <summary>Combat constants, status effects, creatures and where they stand (M3c).</summary>
    public CombatSetup Combat { get; init; } = CombatSetup.Empty;

    /// <summary>
    /// Every space this simulation holds the ground and walls of, by identity (M8 D10). The region's own is always
    /// here; an interior reachable from it is added when the gate lets it in. Before this there was one
    /// <c>WalkSpace</c> - <see cref="RegionLayout.Space"/> - and every height and every wall test came from it, which
    /// is only correct while one space is loaded.
    /// </summary>
    public ImmutableDictionary<SpaceId, WalkSpace> Spaces { get; init; } =
        ImmutableDictionary<SpaceId, WalkSpace>.Empty;

    /// <summary>
    /// Every layout this run holds, by the space it stands in (B6). The region's own is always here; an interior
    /// reachable from it is added when the gate lets it in.
    /// </summary>
    /// <remarks>
    /// A layout is one space: its switches, doors, containers, nodes, stations, places and NPCs are authored together
    /// in its own metres. So a consumer that wants "every switch" wants every layout's switches, and one that wants
    /// the cell of a site wants that site's OWN layout's space - never the player's, which is what made the ring's
    /// switches invisible and, later, buried the exterior's NPCs (review 3, B6 and B8).
    /// </remarks>
    public ImmutableDictionary<SpaceId, RegionLayout> Layouts { get; init; } =
        ImmutableDictionary<SpaceId, RegionLayout>.Empty;

    /// <summary>The interior this run can reach, or null when the gate let none in.</summary>
    public RegionLayout? Interior =>
        Layouts.Values.FirstOrDefault(l => l.SpaceId != Layout.SpaceId);

    /// <summary>Every layout held: the region's own first, then any interior.</summary>
    public IEnumerable<RegionLayout> AllLayouts =>
        Layouts.Count == 0 ? new[] { Layout } : new[] { Layout }.Concat(Layouts.Values.Where(l => l.SpaceId != Layout.SpaceId));

    /// <summary>
    /// Every authored site of a kind, each with the layout that stands it. A site has a space only through its
    /// layout, which is why this pairs them rather than returning the sites alone.
    /// </summary>
    public IEnumerable<(RegionLayout Layout, T Site)> Sites<T>(Func<RegionLayout, IEnumerable<T>> pick)
    {
        foreach (var layout in AllLayouts)
        {
            foreach (var site in pick(layout))
                yield return (layout, site);
        }
    }

    /// <summary>
    /// The space an NPC's own site stands in, or the exterior when no layout lists it. An entity's space is its own,
    /// never the player's: the two overlay in local metres, so the player's space is only right by coincidence and
    /// is wrong on every load and every walk through a door (review 3, B8).
    /// </summary>
    public SpaceId SpaceOfNpc(string npcId)
    {
        foreach (var (layout, site) in Sites(l => l.Npcs))
        {
            if (site.NpcId == npcId)
                return layout.SpaceId;
        }
        return SpaceId.Exterior;
    }

    /// <summary>The layout that stands in <paramref name="space"/>, or the region's own when none does.</summary>
    public RegionLayout LayoutFor(SpaceId space) =>
        Layouts.TryGetValue(space, out var held) ? held : Layout;

    /// <summary>
    /// The ground and walls of <paramref name="space"/>, or the layout's own when it names nothing this holds. The
    /// fallback is deliberate: a space that is not loaded has no ground, and a caller asking for one is asking where
    /// something in the loaded region would stand.
    /// </summary>
    public WalkSpace SpaceFor(SpaceId space) =>
        Spaces.TryGetValue(space, out var held) ? held : Layout.Space;

    /// <summary>The formulas, the tuning of casting, and what books teach (M3e).</summary>
    public MagicSetup Magic { get; init; } = MagicSetup.Empty;

    /// <summary>Resource nodes, recipes, and the tuning of quality (M3f).</summary>
    public CraftingSetup Crafting { get; init; } = CraftingSetup.Empty;

    /// <summary>The named NPCs and their conversations (M4).</summary>
    public SocialSetup Social { get; init; } = SocialSetup.Empty;

    /// <summary>The quests (M5).</summary>
    public QuestSetup Quests { get; init; } = QuestSetup.Empty;

    /// <summary>How Waystone travel is tuned (S-38).</summary>
    public TravelRules Travel { get; init; } = TravelRules.Default;

    /// <summary>The peoples and Calls the character creator offers (WP2).</summary>
    public CharacterSetup Character { get; init; } = CharacterSetup.None;
    /// <summary>The world clock's day (config.time): what the calendar counts.</summary>
    public WorldClock Clock { get; init; } = WorldClock.Default;

    /// <summary>The catch-up cap and the login grace (config.time; WP11).</summary>
    public OfflineRules Offline { get; init; } = OfflineRules.None;

    /// <summary>How the calendar counts the clock's days (config.time; WP7).</summary>
    public CalendarRules Calendar { get; init; } = CalendarRules.Default;

    /// <summary>How the weather's fronts are laid out and drawn (config.weather; WP7).</summary>
    public WeatherRules Weather { get; init; } = WeatherRules.Default;

    /// <summary>The factions, the standing ladder and the act log's capacity (WP6).</summary>
    public FactionSetup Factions { get; init; } = FactionSetup.Empty;

    /// <summary>The town markets, the road trade link and the merchants' purses (WP14).</summary>
    public EconomySetup Economy { get; init; } = EconomySetup.Empty;
}

/// <summary>A read-only view of the player for presentation. A copy: nothing done to it reaches the simulation.</summary>
public sealed record PlayerView(
    EntityId Id,
    string Name,
    Body Body,
    MoveIntent Intent,
    CharacterProgression Progression,
    DerivedStats Stats,
    ImmutableArray<DiscoveryRecord> Discoveries,
    ImmutableArray<InventoryEntry> Inventory,
    ImmutableSortedDictionary<EquipSlot, EntityId> Equipment,
    long Currency,
    long CarriedGrams,
    long CarryLimitGrams,
    int Armor)
{
    /// <summary>Pronouns, people, body preset and Call, as the creator made them: what presentation draws and words the character by.</summary>
    public CharacterIdentity Identity { get; init; } = CharacterIdentity.BeforeTheCreator;
}

/// <summary>A door and whether it is open.</summary>
public sealed record DoorView(DoorSite Site, bool Open);

/// <summary>A switch and whether it has been set (M6).</summary>
public sealed record SwitchView(SwitchSite Site, bool Set);

/// <summary>A barrier and whether it still stands (M6).</summary>
public sealed record BarrierView(BarrierSite Site, bool Standing);

/// <summary>
/// One running world. Commands are queued and applied at a tick boundary by <see cref="DrainCommands"/>; time
/// advances only through <see cref="Step"/>, one fixed tick at a time. The same commands at the same boundaries
/// produce the same state, so a session delivered through the UI and the same session replayed from its command
/// log end identical (PROTOTYPE.md C3). Everything public here reads or submits; nothing writes state.
/// </summary>
public sealed class Simulation
{
    private readonly RuntimeState _state;
    private readonly SystemContext _context;
    private readonly PlayerRecord _identity;
    private readonly ImmutableArray<CellKey> _cells;
    private readonly Queue<GameCommand> _queue = new();
    private readonly List<LoggedCommand> _log = new();
    private readonly ClockSystem _clock;
    private readonly MovementSystem _movement;
    private readonly InteractionSystem _interaction;
    private readonly WorldFlagSystem _flags;
    private readonly ProgressionSystem _progression;
    private readonly DiscoverySystem _discovery;
    private readonly TierSystem _tiers;
    private readonly InventorySystem _inventory;
    private readonly EquipmentSystem _equipment;
    private readonly CombatSystem _combat;
    private readonly CreatureSystem _creatures;
    private readonly StatusEffectSystem _effects;
    private readonly DeathSystem _death;
    private readonly GatheringSystem _gathering;
    private readonly CraftingSystem _crafting;
    private readonly NpcSystem _npcs;
    private readonly RelationshipSystem _relationships;
    private readonly DialogueSystem _dialogue;
    private readonly TradeSystem _trade;
    private readonly QuestSystem _quests;
    private readonly QuestDebugger _debugger;
    private readonly CompanionSystem _companions;
    private readonly SpaceSystem _spaces;
    private readonly TravelSystem _travel;
    private readonly DeedSystem _deeds;
    private readonly WeatherSystem _weather;
    private readonly FactionSystem _factions;
    private readonly MarketSystem _markets;
    private readonly MerchantSystem _merchants;
    private readonly QuickBarSystem _quickBar;
    private readonly ImmutableArray<ITierSimulation> _tierSimulations;
    private readonly ImmutableArray<IAbstractAdvance> _advances;
    private long _sequence;
    private bool _stepping;
    private bool _advancing;

    private Simulation(SimulationSetup setup, PlayerRecord player, WorldDelta world, long worldTick, IEventBus events)
    {
        Setup = setup;
        _identity = player;
        // Every space's cells. A run that holds the ring must hold its cells too: a creature's AI keys on the
        // tier of the cell it stands in, and a space with no cells has no tiers, so the Custodian was never woken.
        _cells = setup.AllLayouts
            .SelectMany(l => l.CellKeys)
            .Select(CellKey.Parse)
            .Distinct()
            .OrderBy(c => c)
            .ToImmutableArray();
        // The session opens in the region's own space (M8 D10): the exterior for the world's regions, its own for
        // an interior. A body placed in an interior's metres while the simulation believes it is outside is the
        // collision the space identity exists to prevent, in reverse.
        _state = new RuntimeState(world, worldTick, new Body(player.XMm, player.YMm, player.ZMm, player.FacingMdeg), player,
            setup.Layout.SpaceId);
        var bus = new SimulationEvents(events);
        _context = new SystemContext(_state, setup, bus, Dispatch);
        // Everything carried is an instance like any other: the registry knows it, and an ID held twice is an error (D-10).
        foreach (var entry in player.Inventory)
        {
            if (world.Registry.Exists(entry.ItemId))
                throw new InvalidOperationException($"{entry.ItemId} is carried and also somewhere in the world");
            world.Registry.CreateEntity(DefinitionId.Parse(entry.DefId), entry.ItemId);
        }

        // The composition root: an explicit, ordered list, and every state slice claimed exactly once (§4.2, §5).
        _clock = new ClockSystem(_context, _state.Claim(nameof(ClockSystem), StateSlice.Clock));
        _movement = new MovementSystem(_context, _state.Claim(nameof(MovementSystem), StateSlice.PlayerBody), player.Id);
        _interaction = new InteractionSystem(_context, player.Id);
        _flags = new WorldFlagSystem(_context, _state.Claim(nameof(WorldFlagSystem), StateSlice.WorldFlags));
        _progression = new ProgressionSystem(_context, _state.Claim(nameof(ProgressionSystem), StateSlice.PlayerProgression));
        _discovery = new DiscoverySystem(_context, _state.Claim(nameof(DiscoverySystem), StateSlice.Discoveries));
        _tiers = new TierSystem(_context, _state.Claim(nameof(TierSystem), StateSlice.CellTiers), _cells);
        _inventory = new InventorySystem(_context, _state.Claim(nameof(InventorySystem), StateSlice.PlayerInventory, StateSlice.WorldItems), player.Id);
        _equipment = new EquipmentSystem(_context, _state.Claim(nameof(EquipmentSystem), StateSlice.PlayerEquipment), player.Id);
        _combat = new CombatSystem(_context, _state.Claim(nameof(CombatSystem), StateSlice.Combat), player.Id, () => _movement.Effective);
        _creatures = new CreatureSystem(_context, _state.Claim(nameof(CreatureSystem), StateSlice.Creatures), player.Id, () => _movement.Effective);
        _effects = new StatusEffectSystem(_context, _state.Claim(nameof(StatusEffectSystem), StateSlice.Effects), player.Id);
        _travel = new TravelSystem(_context, _state.Claim(nameof(TravelSystem), StateSlice.Waystones), player.Id);
        _gathering = new GatheringSystem(_context, _state.Claim(nameof(GatheringSystem), StateSlice.Nodes), player.Id);
        _crafting = new CraftingSystem(_context, player.Id, player.Name);
        _npcs = new NpcSystem(_context, _state.Claim(nameof(NpcSystem), StateSlice.Npcs));
        _relationships = new RelationshipSystem(_context, _state.Claim(nameof(RelationshipSystem), StateSlice.Relationships));
        _dialogue = new DialogueSystem(_context, _state.Claim(nameof(DialogueSystem), StateSlice.Conversations), player.Id);
        _markets = new MarketSystem(_context, _state.Claim(nameof(MarketSystem), StateSlice.Markets));
        _merchants = new MerchantSystem(_context, _state.Claim(nameof(MerchantSystem), StateSlice.Merchants), _markets);
        _merchants.Listen(bus);
        _trade = new TradeSystem(_context, player.Id, _inventory.View, _markets, _merchants);
        _quests = new QuestSystem(_context, _state.Claim(nameof(QuestSystem), StateSlice.Quests), player.Id);
        _quests.Listen(bus);
        _companions = new CompanionSystem(_context, _state.Claim(nameof(CompanionSystem), StateSlice.Companions), player.Id, player.Companions);
        _spaces = new SpaceSystem(_context, _state.Claim(nameof(SpaceSystem), StateSlice.PlayerSpace), player.Id);
        // A loaded body stands where its save left it, and half of that is the space (schema 16). Without this the
        // ring's local coordinates load into the exterior - the Vestibule's (50, 84) would put a body in the Ashen
        // Hollow - because the space system starts at the exterior and nothing told it otherwise.
        _spaces.Adopt(player.SpaceId, player.SpaceTraversal);
        // After `_spaces`, which it captures: a delegate to an instance method taken before the instance exists
        // throws at construction, and taking it here is what keeps death's space reset honest (M8 D10).
        _death = new DeathSystem(_context, player.Id, _travel.ReturnBody, _spaces.LeaveForDeath);
        _debugger = new QuestDebugger(_context, _quests, _dialogue, _gathering.Views, _trade.View, () => Containers, _creatures.Views, _inventory.WorldItems);
        _deeds = new DeedSystem(_context, _state.Claim(nameof(DeedSystem), StateSlice.Deeds));
        _weather = new WeatherSystem(_context, _state.Claim(nameof(WeatherSystem), StateSlice.Weather), new CellKey(_cells[0].Region, 0, 0));
        _factions = new FactionSystem(_context, _state.Claim(nameof(FactionSystem), StateSlice.Factions));
        _quickBar = new QuickBarSystem(_context, _state.Claim(nameof(QuickBarSystem), StateSlice.QuickBar), player.Id);
        _tierSimulations = ImmutableArray.Create<ITierSimulation>(new StubTierSimulation(SimulationTier.B), new StubTierSimulation(SimulationTier.C));
        // The abstract advance (WP11): the clock first, so every other system brings itself to the tick it reads. A new world system
        // that goes on while the character is away adds itself here.
        _advances = ImmutableArray.Create<IAbstractAdvance>(_clock, _weather, _creatures, _effects, _gathering, _quests, _factions, _companions, _markets);
        _state.RequireEverySliceOwned();
        _effects.Seed(player.Id, player.Effects);
        _npcs.Populate();
        _markets.Populate();
        _merchants.Populate();
        _companions.Populate();
        _creatures.Populate();
        _tiers.Settle();
    }

    /// <summary>
    /// Start simulating a world: a new game's, or one a save just loaded. The world tick continues from the save; the weather cursor starts
    /// empty, so the first tick announces the front overhead.
    /// </summary>
    public static Simulation Start(SimulationSetup setup, PlayerRecord player, WorldDelta world, long worldTick, IEventBus events)
    {
        if (worldTick < 0)
            throw new ArgumentOutOfRangeException(nameof(worldTick), worldTick, "The world tick is never negative");
        return new Simulation(setup, player, world, worldTick, events);
    }

    /// <summary>
    /// A new character standing at the region's spawn point, with the starting package (PROGRESSION.md §5) and the
    /// starting kit (PROTOTYPE.md §5 step 1: the sword equipped), and no Call.
    /// </summary>
    public static PlayerRecord NewCharacter(SimulationSetup setup, EntityId id, string name, ulong appearanceSeed) =>
        NewCharacter(setup, id, name, null, appearanceSeed, null, null);

    /// <summary>
    /// A new character made from the creator's spec: the starting package and kit, plus its Call's package, kit and contact (accepted
    /// OD-C03). The spec must name a playable people and, if any, a defined Call.
    /// </summary>
    /// <param name="atXMm">Where a capture stands the character; null uses the region's spawn, as every ordinary run does.</param>
    /// <param name="atZMm">The other half of <paramref name="atXMm"/>.</param>
    public static PlayerRecord NewCharacter(SimulationSetup setup, EntityId id, CharacterSpec spec, ulong appearanceSeed,
        long? atXMm = null, long? atZMm = null)
    {
        spec = spec.Validated();
        setup.Character.Check(spec);
        var call = spec.CallId is { } callId ? setup.Character.Calls[callId] : null;
        var start = spec.Start == StartPoint.Road ? setup.Layout.Opening ?? setup.Layout.Spawn : setup.Layout.Spawn;
        return NewCharacter(setup, id, spec.Name, call, appearanceSeed, start, atXMm, atZMm) with { Identity = CharacterIdentity.Of(spec) };
    }

    private static PlayerRecord NewCharacter(SimulationSetup setup, EntityId id, string name, CallDefinition? call, ulong appearanceSeed,
        Body? start = null, long? atXMm = null, long? atZMm = null)
    {
        var spawn = start ?? setup.Layout.Spawn;
        // A capture opens the character where it asked to rather than walking him there, which in the Barrowworks
        // measured about a quarter of an hour per room (plan P2h). Null is the chosen start, so ordinary runs are
        // untouched - the road start still decides where a character is made.
        long xMm = atXMm ?? spawn.XMm;
        long zMm = atZMm ?? spawn.ZMm;
        var lines = call is null ? setup.Items.StartingKit : setup.Items.StartingKit.AddRange(call.Kit);
        // A notable piece of the kit carries its record from the start (WP3a).
        var kit = lines.Select(item => (Item: item, Entry: new InventoryEntry(EntityId.NewId(EntityKind.Item), item.ItemId, item.Count)
        {
            Notable = ItemClasses.KeepsRecord(setup.Items.Catalog.Get(item.ItemId), Domain.Crafting.Quality.Standard)
                ? new NotableRecord(setup.Items.Catalog.Get(item.ItemId).Name ?? item.ItemId, ProvenanceKind.Kit, "kit", 0)
                : null,
        })).ToList();
        var equipment = kit.Where(k => k.Item.Equip)
            .Select(k => KeyValuePair.Create(setup.Items.Catalog.Get(k.Item.ItemId).Slot!.Value, k.Entry.ItemId));
        var contact = call is null
            ? Array.Empty<RelationshipValue>()
            : new[] { new RelationshipValue(call.Contact.NpcId, "trust", call.Contact.Trust) };
        return new PlayerRecord(id, name, xMm, spawn.YMm, zMm, appearanceSeed, kit.Select(k => k.Entry),
            ProgressionEngine.Create(call is null ? setup.Progression : setup.Progression with { StartingPackage = setup.Progression.StartingPackage.With(call.Package) }),
            spawn.FacingMdeg, equipment: equipment, relationships: contact);
    }

    /// <summary>The cell a door stands in, in the space its layout stands in: its flag lives in that cell's delta.</summary>
    public static CellKey CellOf(SpaceId space, DoorSite door) => CellOf(space, door.ClosedFootprint);

    /// <summary>The cell a switch stands in (M6): its flag, and every flag it requires, live in that cell's delta.</summary>
    public static CellKey CellOf(SpaceId space, SwitchSite site) => CellOf(space, site.Body);

    /// <summary>The cell a barrier stands in (M6): the flag that lifts it lives in that cell's delta.</summary>
    public static CellKey CellOf(SpaceId space, BarrierSite barrier) => CellOf(space, barrier.Footprint);

    /// <summary>
    /// A site's cell, in the space the site stands in. <see cref="CellKey.OfInterior"/> with the exterior
    /// space is <see cref="CellKey.OfWorld"/>, so an authored exterior site still resolves to exactly the
    /// cell it always did (M8 D10).
    /// </summary>
    private static CellKey CellOf(SpaceId space, Blocker footprint)
    {
        var (x, z) = Footprints.Center(footprint);
        return CellKey.OfInterior(space, x / 1000.0, z / 1000.0);
    }

    public SimulationSetup Setup { get; }

    public EntityId PlayerId => _identity.Id;

    /// <summary>Completed ticks: <c>world_tick</c>.</summary>
    public long WorldTick => _state.WorldTick;

    /// <summary>The sparse world delta. Its public members only read (tests/Architecture.Tests).</summary>
    public WorldDelta World => _state.World;

    /// <summary>Standing or crouched, and how far into a jump (the owner's M6 playtest): cheap to read every frame.</summary>
    public Posture Posture => _state.Posture;

    /// <summary>
    /// Where a shot loosed now along this facing would stop (the owner's M6 playtest): on a creature, a wall, or at the end of its range.
    /// Read-only: the aiming reticle sits on this point.
    /// </summary>
    public (long XMm, long ZMm, bool OnCreature) Aim(int facingMdeg, long rangeMm)
    {
        var (x, z, target) = _combat.Trace(_state.Body with { FacingMdeg = facingMdeg }, rangeMm);
        return (x, z, target is not null);
    }

    /// <summary>
    /// Whether the player is in the exterior world (M8 D10). Read-only: what presentation needs to know so
    /// a HUD or a map prompt can say which space the character is in, and so a test can assert a transition
    /// without reaching into state.
    /// </summary>
    /// <summary>Every cell this run holds a tier for, across every space it holds (review 3, B6).</summary>
    public ImmutableArray<CellKey> HeldCells => _cells;

    public bool PlayerInExterior => _state.Space.IsExterior;

    public PlayerView Player => new(_identity.Id, _identity.Name, _state.Body, _movement.Intent, _state.Progression,
        ProgressionEngine.Derive(_state.Progression, Setup.Progression), _state.Discoveries.Values.ToImmutableArray(),
        _state.Inventory, _state.Equipment, _state.Currency, _inventory.CarriedGrams(),
        _inventory.CarryLimitGrams(), _equipment.Armor()) { Identity = _identity.Identity };

    /// <summary>Every authored container and what it holds now.</summary>
    public ImmutableArray<ContainerView> Containers =>
        Setup.AllLayouts.SelectMany(l => l.Containers).Concat(_context.CorpseSites()).Select(_inventory.View).ToImmutableArray();

    /// <summary>Items lying in the region.</summary>
    public ImmutableArray<WorldItemView> WorldItems => _inventory.WorldItems();

    public ImmutableArray<DoorView> Doors =>
        Setup.AllLayouts.SelectMany(l => l.Doors).Select(d => new DoorView(d, _context.IsOpen(d))).ToImmutableArray();

    public ImmutableArray<SwitchView> Switches =>
        Setup.AllLayouts.SelectMany(l => l.Switches).Select(s => new SwitchView(s, _context.IsSet(s))).ToImmutableArray();

    public ImmutableArray<BarrierView> Barriers =>
        Setup.AllLayouts.SelectMany(l => l.Barriers).Select(b => new BarrierView(b, !_context.IsLifted(b))).ToImmutableArray();

    /// <summary>The player in combat: phase, guard, weapon, pools and effects.</summary>
    public CombatView Combat => _combat.View();

    /// <summary>
    /// How much of a station's experiment set the character has found (WP3a): known of the set, and its size. The recipes themselves are
    /// not shown until found. (0, 0) for a station with no experiments, or no such station.
    /// </summary>
    public (int Found, int Total) Experiments(string stationKey) =>
        Setup.Layout.Stations.FirstOrDefault(s => s.Key == stationKey) is { } station && _crafting.ExperimentSet(station) is var set
            ? (set.Count(id => ProgressionEngine.Knows(_state.Progression, id)), set.Length)
            : (0, 0);

    /// <summary>The region's resource nodes, and which can be harvested now (M3f).</summary>
    public ImmutableArray<NodeView> Nodes => _gathering.Views();

    /// <summary>Every creature the region holds, living or dead.</summary>
    public ImmutableArray<CreatureView> Creatures => _creatures.Views();

    /// <summary>The region's named NPCs (M4).</summary>
    public ImmutableArray<NpcView> Npcs => _npcs.Views();

    /// <summary>The region's Waystones: awake or dormant, known or not, and which is the return point (S-38).</summary>
    public ImmutableArray<WaystoneView> Waystones => _travel.Views();

    /// <summary>The journeys the stones offer from this one to every other stone the character knows, with their costs (S-38).</summary>
    public ImmutableArray<TravelOption> TravelOptions(string fromKey) => _travel.Options(fromKey);

    /// <summary>What the character's map holds: the places they discovered, the roads they walked, the stones they know.</summary>
    public KnownMap Map => Cartography.Known(Setup.Layout, _state.Discoveries.Keys, _state.RoadsWalked, _state.KnownStones.Keys);

    /// <summary>The date the world clock reads now (WP7): a pure function of the tick.</summary>
    public CalendarDate Calendar => Runtime.Calendar.Of(WorldTick, Setup.Clock, Setup.Calendar);

    /// <summary>The weather over the region played now (WP7): its state and the ticks its front holds.</summary>
    public WeatherView Weather => _weather.View(WorldTick);

    /// <summary>The weather's seed cursor (WP7): the game hour the front last announced began; null before the first. What a save keeps of the weather.</summary>
    public long? WeatherCursor => _state.WeatherCursor;

    /// <summary>The eight quick slots (WP9): each empty, or the item, formula or technique it uses.</summary>
    public ImmutableArray<string?> QuickBar => _state.QuickBar;

    /// <summary>The character's companions (M6): the whole party - companions, hirelings and allies (WP15a).</summary>
    public ImmutableArray<CompanionView> Companions => _companions.Views();

    /// <summary>The party's rules (<c>config.party</c>): its size, the grace on wages, the roles' terms. Null in a pack with none.</summary>
    public PartyRules? Party => Setup.Social.Party;

    /// <summary>
    /// What hiring someone would mean (DN9, DN11), for the hiring sheet to state before the character agrees: the wage, the whole cost,
    /// the loot rule, that the hireling can die - and why it would be refused now, if it would.
    /// </summary>
    public HireTerms HireTerms(string npcId, HirelingRole role, int days) => _companions.Terms(npcId, role, days);

    /// <summary>Companions gravely wounded and recovering at home (DN9), and when each is well again.</summary>
    public ImmutableArray<RecoveryView> Recovering => _companions.Recoveries();

    /// <summary>The conversation open now, if any (M4).</summary>
    public ConversationView? Conversation => _dialogue.View();

    /// <summary>
    /// What an NPC could say in passing now (their barks whose conditions hold), each with its line ID (E-10); presentation picks one and
    /// when, and voices it when its recording exists.
    /// </summary>
    public ImmutableArray<Domain.Social.SpokenLine> Barks(string npcId) => _dialogue.Barks(npcId);

    /// <summary>A trader's wares at their prices; null when that NPC does not trade (M4).</summary>
    public WaresView? Wares(string npcId) => _trade.View(npcId);

    /// <summary>What a trader would pay for one of an item now (WP14); null when they do not trade, or do not buy it.</summary>
    public long? Offer(string npcId, string itemId) => _trade.Offer(npcId, itemId);

    /// <summary>The town markets: each good's stock and price against base, and what is on the road to them (WP14).</summary>
    public ImmutableArray<MarketView> Markets => _markets.Views();

    /// <summary>The quests the character has started, as the journal shows them: active first (M5).</summary>
    public ImmutableArray<QuestView> Quests => _quests.Views();

    /// <summary>The journal (WP4): the quests by track, and the Recent list their records give, newest first.</summary>
    public JournalView Journal => _quests.Journal();

    /// <summary>Every bounty on its giver's board: open, held by the character, or claimed - by whom, and until when.</summary>
    public ImmutableArray<BountyView> Bounties => _quests.Bounties(WorldTick);

    /// <summary>The quest the tracker follows: <paramref name="preferred"/> while it is active, else the main story's, else the first active.</summary>
    public string? TrackedQuest(string? preferred = null) => _quests.Tracked(preferred);

    /// <summary>Where the compass marks a quest's current beat, when the character knows where that is.</summary>
    public QuestMarker? Marker(string questId) => _quests.Marker(questId);

    /// <summary>Whether a new game has a story to begin: some quest starts for every new character (<c>on_new_game</c>).</summary>
    public bool HasOpeningQuests => Setup.Quests.Quests.Values.Any(q => q.OnNewGame);

    /// <summary>The quest debugger (M5): what a quest is waiting on right now, every term's value, and its recent trace.</summary>
    public QuestDiagnosis Diagnose(string questId) => _debugger.Diagnose(questId);

    public ImmutableSortedDictionary<string, SimulationTier> CellTiers => _state.Tiers;

    /// <summary>Every faction as the character stands with it, in ordinal ID order (WP6).</summary>
    public ImmutableArray<FactionView> Factions => _factions.Views();

    /// <summary>The character's act log, with what each faction knows of each act (WP6).</summary>
    public ImmutableArray<ActView> Acts => _factions.Acts();

    /// <summary>The footprints that currently block movement besides the static ones: closed doors, standing barriers, living creatures and NPCs. Prediction needs them.</summary>
    public ImmutableArray<Blocker> DynamicBlockers => _context.Obstacles();

    /// <summary>Read-only: a wall, a structure or a closed door lies across the line - so an NPC beyond it is not spoken to (L-09).</summary>
    public bool Walled(long x0, long z0, long x1, long z1) => _context.Walled(x0, z0, x1, z1);

    /// <summary>Which system owns each slice of state (ARCHITECTURE.md §5).</summary>
    public IReadOnlyDictionary<StateSlice, string> SliceOwners => _state.Owners;

    public IReadOnlyList<LoggedCommand> CommandLog => _log;

    public int PendingCommands => _queue.Count;

    /// <summary>Queue a command for the next tick boundary. Nothing changes until <see cref="DrainCommands"/>.</summary>
    public void Enqueue(GameCommand command) => _queue.Enqueue(command ?? throw new ArgumentNullException(nameof(command)));

    /// <summary>Apply every queued command, in order, at the current tick boundary. Returns how many were applied.</summary>
    public int DrainCommands()
    {
        int applied = 0;
        while (_queue.TryDequeue(out var command))
        {
            string? rejected = command switch
            {
                MoveCommand move => _movement.Handle(move),
                JumpCommand jump => _movement.Handle(jump, WorldTick),
                CrouchCommand crouch => _movement.Handle(crouch, WorldTick),
                TakeAllCommand takeAll => _inventory.Handle(takeAll, WorldTick),
                SpendAttributeCommand spend => spend.Actor != PlayerId ? $"unknown actor {spend.Actor}" : _progression.Handle(spend, WorldTick),
                InteractCommand interact => _interaction.Handle(interact, WorldTick),
                EnterSpaceCommand enter => _spaces.Enter(enter),
                LeaveSpaceCommand leave => _spaces.Leave(leave),
                MoveItemCommand item => _inventory.Handle(item, WorldTick),
                EquipCommand equip => _equipment.Handle(equip, WorldTick),
                UnequipCommand unequip => _equipment.Handle(unequip, WorldTick),
                AttackCommand attack => _combat.Handle(attack, WorldTick),
                BlockCommand block => _combat.Handle(block, WorldTick),
                DodgeCommand dodge => _combat.Handle(dodge, WorldTick),
                CastCommand cast => _combat.Handle(cast, WorldTick),
                GatherCommand gather => _gathering.Handle(gather, WorldTick),
                CraftCommand craft => _crafting.Handle(craft, WorldTick),
                ExperimentCommand experiment => _crafting.Handle(experiment, WorldTick),
                UseItemCommand use => _inventory.Handle(use, WorldTick),
                TalkCommand talk => _dialogue.Handle(talk, WorldTick),
                ChooseCommand choose => _dialogue.Handle(choose, WorldTick),
                LeaveCommand leave => _dialogue.Handle(leave, WorldTick),
                BuyCommand buy => _trade.Handle(buy, WorldTick),
                SellCommand sell => _trade.Handle(sell, WorldTick),
                OrderCompanionCommand order => _companions.Handle(order, WorldTick),
                ReviveCommand revive => _companions.Handle(revive, WorldTick),
                HireCommand hire => _companions.Handle(hire, WorldTick),
                DismissCommand dismiss => _companions.Handle(dismiss, WorldTick),
                SetStanceCommand stance => _companions.Handle(stance, WorldTick),
                SetTacticsCommand tactics => _companions.Handle(tactics, WorldTick),
                AssignWorkCommand work => _companions.Handle(work, WorldTick),
                SupplyHirelingCommand supply => _companions.Handle(supply, WorldTick),
                TouchStoneCommand touch => _travel.Handle(touch, WorldTick),
                BindStoneCommand bind => _travel.Handle(bind, WorldTick),
                TravelCommand travel => _travel.Handle(travel, WorldTick),
                BeginStoryCommand begin => _quests.Handle(begin, WorldTick),
                ClaimBountyCommand claim => _quests.Handle(claim, WorldTick),
                AssignQuickSlotCommand assign => _quickBar.Handle(assign, WorldTick),
                _ => $"no system handles {command.GetType().Name}",
            };
            _log.Add(new LoggedCommand(WorldTick, _sequence++, command, rejected));
            if (rejected is not null)
                _context.Events.Publish(new CommandRejected(command, rejected, WorldTick));
            applied++;
        }
        return applied;
    }

    /// <summary>
    /// Advance one fixed tick. The order is data, fixed here: movement, tiers, the weather, the tier simulations, the player's combat,
    /// creatures, companions, NPCs, conversations, status effects, death, discovery, quests (which read what all of that did), the markets'
    /// day, the quick bar (which offers a slot to what came to hand), clock.
    /// </summary>
    public void Step()
    {
        if (_stepping || _advancing)
            throw new InvalidOperationException("Step is not re-entrant: an event handler may enqueue commands, never step the world");
        _stepping = true;
        try
        {
            long tick = WorldTick + 1;
            _movement.Tick(tick);
            _tiers.Tick(tick);
            _weather.Tick(tick);
            foreach (var simulation in _tierSimulations)
                simulation.Tick(tick, _cells.Where(c => _state.Tiers.GetValueOrDefault(c.ToString(), SimulationTier.D) == simulation.Tier).ToList());
            _combat.Tick(tick);
            _creatures.Tick(tick);
            _companions.Tick(tick);
            _npcs.Tick(tick);
            _dialogue.Tick(tick);
            _effects.Tick(tick);
            _death.Tick(tick);
            _discovery.Tick(tick);
            _quests.Tick(tick);
            _markets.Tick(tick);
            _quickBar.Tick(tick);
            _clock.Tick();
        }
        finally
        {
            _stepping = false;
        }
    }

    /// <summary>
    /// The world going on while the character was away (WP11): every system brings itself <paramref name="ticks"/> later at once, through
    /// its <see cref="IAbstractAdvance"/>, the clock first. Chunk-invariant: one advance of n ticks leaves the state that advances summing
    /// to n do (T-22). Called at a tick boundary, never from inside <see cref="Step"/>.
    /// </summary>
    public AdvanceLog Advance(long ticks)
    {
        if (ticks < 0)
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "The world never goes back in time");
        if (_stepping || _advancing)
            throw new InvalidOperationException("Advance is not re-entrant: an event handler may enqueue commands, never advance the world");
        long from = WorldTick, to = checked(from + ticks);
        var log = new AdvanceLog(from, to);
        if (ticks == 0)
            return log;
        _advancing = true;
        try
        {
            AbstractAdvance.Run(from, to, _advances, log);
        }
        finally
        {
            _advancing = false;
        }
        return log;
    }

    /// <summary>
    /// The login grace (§2.12 item 6): for config.time's <c>login_grace_seconds</c> from now, creatures neither see nor hear the character,
    /// so nothing that came back while they were away is upon them before the world is on screen. Not saved: every login starts its own.
    /// </summary>
    public void BeginLoginGrace() => _creatures.BeginGrace(WorldTick + Setup.Offline.LoginGraceTicks);

    /// <summary>The last tick of the login grace; at or before the world tick when there is none.</summary>
    public long LoginGraceUntil => _creatures.GraceUntil;

    /// <summary>The player as a save records them: identity from the loaded record, everything else from live state.</summary>
    public PlayerRecord CaptureRecord()
    {
        // A save made while the character lies dead is the save of their respawn: it loads at the return point. One made while they lie
        // downed with a hand near is the save of being helped up where they lie (OD-13 = B).
        bool downed = _state.PlayerCombat.DownedUntil > 0;
        bool dead = _state.PlayerCombat.Defeated && !downed;
        var body = dead ? _travel.ReturnBody() : _state.Body;
        return new PlayerRecord(_identity.Id, _identity.Name, body.XMm, body.YMm, body.ZMm, _identity.AppearanceSeed, _state.Inventory,
            downed ? _combat.HelpedUp(_state.Progression) : _state.Progression, body.FacingMdeg, _state.Discoveries.Values, _state.Equipment, _state.Currency,
            _state.Effects.GetValueOrDefault(_identity.Id, ImmutableArray<ActiveEffect>.Empty),
            _state.Relationships.SelectMany(n => n.Value.Select(d => new RelationshipValue(n.Key, d.Key, d.Value))),
            _state.Conversations.Select(c => new ConversationMemory(c.Key, c.Value.ToImmutableArray())),
            _state.Quests.Values, _companions.Records())
        {
            Posture = dead ? Posture.Grounded : _state.Posture,
            Waystones = _travel.Records(),
            ReturnPoint = _state.ReturnPoint,
            RoadsWalked = _state.RoadsWalked.ToImmutableArray(),
            Deeds = _state.Deeds.Values.ToImmutableArray(),
            Identity = _identity.Identity,   // as the creator made it: nothing in the world changes it
            // From LIVE state, not from _identity: a body moves between spaces during a run and its identity does
            // not, so a save that read these from the identity would record the exterior for a body standing inside.
            SpaceId = _state.Space.Name ?? "",
            SpaceTraversal = _state.Traversal.IsNone ? null : _state.Traversal.PortalKey,
            Factions = _state.Factions,
            Party = _companions.Ledger(),
            QuickBar = _state.QuickBar,
            QuickBarCleared = _state.QuickBarCleared.ToImmutableArray(),
        };
    }

    /// <summary>
    /// A digest of every piece of persistent state: the tick, the whole player record, and the effective state of
    /// every cell of the region. Two simulations are in the same persistent state exactly when these match.
    /// </summary>
    public string StateDigest()
    {
        using var h = new CanonicalHasher();
        h.Add("unnamed.simulation/v2").Add(WorldTick).Add(CaptureRecord().Digest).Add(_cells.Length);
        foreach (var cell in _cells)
            h.Add(cell.ToString()).Add(World.EffectiveCellDigest(cell));
        h.Add(World.Noises.Length);
        foreach (var noise in World.Noises)
            h.Add(noise.XMm).Add(noise.ZMm).Add(noise.RadiusMm).Add(noise.Call).Add(noise.CallerKind ?? "-");
        // The economy (WP14): every market's stock and memory, the road, and every purse.
        var economy = World.Economy!;
        h.Add(economy.Day).Add(economy.Markets.Count);
        foreach (var market in economy.Markets.Values)
        {
            h.Add(market.MarketId).Add(market.Stock.Count);
            foreach (var (item, stock) in market.Stock)
                h.Add(item).Add(stock);
            h.Add(market.SoldByCharacter.Count);
            foreach (var (item, sold) in market.SoldByCharacter)
                h.Add(item).Add(sold);
        }
        h.Add(economy.Shipments.Length);
        foreach (var shipment in economy.Shipments)
            h.Add(shipment.LinkId).Add(shipment.FromMarket).Add(shipment.ToMarket).Add(shipment.ItemId).Add(shipment.Count).Add(shipment.ArrivesDay);
        h.Add(World.Merchants.Count);
        foreach (var merchant in World.Merchants)
            h.Add(merchant.MerchantId).Add(merchant.Purse).Add(merchant.RestockedDay).Add(merchant.ShelfDay).Add(merchant.Traded);
        return h.Finish();
    }

    /// <summary>The tick an internal command belongs to: the one being stepped, or the boundary commands apply at.</summary>
    private long Now => _stepping ? WorldTick + 1 : WorldTick;

    private string? Dispatch(InternalCommand command) => command switch
    {
        SetWorldFlag set => _flags.Handle(set, Now),
        AwardExperience award => _progression.Handle(award),
        ChangePools pools => _progression.Handle(pools),
        PracticeSkill practice => _progression.Handle(practice),
        LearnTechnique learn => _progression.Handle(learn),
        RecordDeath death => _progression.Handle(death),
        Relocate relocate => _movement.Handle(relocate, Now),
        ApplyEffect apply => _effects.Handle(apply, Now),
        ClearEffects clear => _effects.Handle(clear, Now),
        RemoveEffect remove => _effects.Handle(remove, Now),
        Harm harm => _combat.Handle(harm, Now),
        Heal heal => _combat.Handle(heal, Now),
        EndFight end => _combat.Handle(end),
        AwaitRespawn await => _combat.Handle(await),
        LieDowned downed => _combat.Handle(downed, Now),
        CreatureStrike strike => _combat.Handle(strike, Now),
        WoundCreature wound => _creatures.Handle(wound, Now),
        HarmCreature harm => _creatures.Handle(harm, Now),
        HealCreature heal => _creatures.Handle(heal, Now),
        ForgetPlayer forget => _creatures.Handle(forget),
        CorpseEmptied emptied => _creatures.Handle(emptied, Now),
        DiscardContainer discard => _inventory.Handle(discard),
        ConsumeItem consume => _inventory.Handle(consume, Now),
        ExchangeItems exchange => _inventory.Handle(exchange, Now),
        Trade trade => _inventory.Handle(trade, Now),
        RestockWares restock => _inventory.Handle(restock),
        MarketTrade trade => _markets.Handle(trade),
        ProduceGoods produce => _markets.Handle(produce),
        SettleMerchants settle => _merchants.Handle(settle),
        PayMerchant pay => _merchants.Handle(pay),
        PayFromPurse pay => _merchants.Handle(pay),
        AddCurrency add => _inventory.Handle(add),
        SpendCurrency spend => _inventory.Handle(spend),
        GrantItem grant => _inventory.Handle(grant, Now),
        CountUse use => _inventory.Handle(use),
        ChangeRelationship change => _relationships.Handle(change, Now),
        StartQuest start => _quests.Handle(start, Now),
        RecordDeed deed => _deeds.Handle(deed) ?? _quests.Handle(deed),
        Recruit recruit => _companions.Handle(recruit, Now),
        OrderCompanion order => _companions.Handle(order, Now),
        CompanionStruck struck => _companions.Handle(struck, Now),
        PlaceNpc place => _npcs.Handle(place),
        RemoveNpc remove => _npcs.Handle(remove),
        Hire hire => _companions.Handle(hire, Now),
        Dismiss dismiss => _companions.Handle(dismiss, Now),
        ArriveWithParty arrive => _companions.Handle(arrive),
        RecordAct act => _factions.Handle(act, Now),
        ReportAct report => _factions.Handle(report, Now),
        GrantStanding grant => _factions.Handle(grant, Now),
        _ => throw new InvalidOperationException($"No system handles {command.GetType().Name}"),
    };
}
