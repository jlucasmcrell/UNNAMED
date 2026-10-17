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
using UNNAMED.Domain.Social;
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

    /// <summary>The player's active techniques, one weapon family's each (WP9, D11).</summary>
    public ImmutableSortedDictionary<string, TechniqueDefinition> Techniques { get; init; } =
        ImmutableSortedDictionary.Create<string, TechniqueDefinition>(StringComparer.Ordinal);

    /// <summary>The chatter corpus, the LFG, news and system templates, and chat's tuning (WP16).</summary>
    public ChatSetup Chat { get; init; } = ChatSetup.Empty;

    /// <summary>Who sets out and will take company (the WP12 contract): nobody, until the NPC life simulation lands.</summary>
    public ISettingOutSource SettingOuts { get; init; } = Runtime.SettingOuts.None;

    /// <summary>Who may say an ambient line, and where (the WP12 contract): nobody, until the NPC life simulation lands.</summary>
    public IChatterSpeakers ChatterSpeakers { get; init; } = Runtime.ChatterSpeakers.None;

    /// <summary>The navigation lattice and its limits (<c>config.navigation</c>; WP12).</summary>
    public NavConfig Navigation { get; init; } = NavConfig.Default;

    /// <summary>The routines, pairs and rules of NPC life in the region played (WP12).</summary>
    public NpcLifeSetup NpcLife { get; init; } = NpcLifeSetup.Empty;

    /// <summary>The NPC adventurers' settlements, rules and hiring (<c>config.adventurers</c>; WP13); null in a pack with none.</summary>
    public AdventurerSetup? Adventurers { get; init; }
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
    private readonly WorldNavigation _navigation;
    private readonly RelationshipSystem _relationships;
    private readonly DialogueSystem _dialogue;
    private readonly TradeSystem _trade;
    private readonly QuestSystem _quests;
    private readonly QuestDebugger _debugger;
    private readonly CompanionSystem _companions;
    private readonly TravelSystem _travel;
    private readonly DeedSystem _deeds;
    private readonly WeatherSystem _weather;
    private readonly FactionSystem _factions;
    private readonly MarketSystem _markets;
    private readonly MerchantSystem _merchants;
    private readonly QuickBarSystem _quickBar;
    private readonly TeachingSystem _teaching;
    private readonly DurabilitySystem _durability;
    private readonly LadenSystem _laden;
    private readonly PartySheetSystem _sheets;
    private readonly ApprovalSystem _approval;
    private readonly LfgSystem _lfg;
    private readonly ChatSystem _chat;
    private readonly AdventurerSystem _adventurers;
    private readonly ImmutableArray<ITierSimulation> _tierSimulations;
    private readonly ImmutableArray<IAbstractAdvance> _advances;
    private long _sequence;
    private bool _stepping;
    private bool _advancing;

    private Simulation(SimulationSetup setup, PlayerRecord player, WorldDelta world, long worldTick, IEventBus events, IPartyLife life)
    {
        Setup = setup;
        _identity = player;
        _cells = setup.Layout.CellKeys.Select(CellKey.Parse).OrderBy(c => c).ToImmutableArray();
        _state = new RuntimeState(world, worldTick, new Body(player.XMm, player.YMm, player.ZMm, player.FacingMdeg), player);
        var bus = new SimulationEvents(events);
        _context = new SystemContext(_state, setup, bus, Dispatch);
        // Everything carried is an instance like any other: the registry knows it, and an ID held twice is an error (D-10).
        foreach (var entry in player.Inventory)
        {
            if (world.Registry.Exists(entry.ItemId))
                throw new InvalidOperationException($"{entry.ItemId} is carried and also somewhere in the world");
            world.Registry.CreateEntity(DefinitionId.Parse(entry.DefId), entry.ItemId);
        }
        // So is what the party wears and keeps (WP15b): handed over, an item keeps its identity.
        foreach (var entry in player.PartySheets.SelectMany(p => p.Items))
        {
            if (world.Registry.Exists(entry.ItemId))
                throw new InvalidOperationException($"{entry.ItemId} is held by a party member and also somewhere in the world");
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
        _death = new DeathSystem(_context, player.Id, _travel.ReturnBody);
        _gathering = new GatheringSystem(_context, _state.Claim(nameof(GatheringSystem), StateSlice.Nodes), player.Id);
        _crafting = new CraftingSystem(_context, player.Id, player.Name);
        _navigation = new WorldNavigation(_context);
        _npcs = new NpcSystem(_context, _state.Claim(nameof(NpcSystem), StateSlice.Npcs, StateSlice.NpcLife), _navigation);
        _relationships = new RelationshipSystem(_context, _state.Claim(nameof(RelationshipSystem), StateSlice.Relationships));
        _dialogue = new DialogueSystem(_context, _state.Claim(nameof(DialogueSystem), StateSlice.Conversations), player.Id);
        _markets = new MarketSystem(_context, _state.Claim(nameof(MarketSystem), StateSlice.Markets));
        _merchants = new MerchantSystem(_context, _state.Claim(nameof(MerchantSystem), StateSlice.Merchants), _markets);
        _merchants.Listen(bus);
        _trade = new TradeSystem(_context, player.Id, _inventory.View, _markets, _merchants) { Closed = _npcs.TradeRefusal };
        _markets.Jobs = _npcs;
        _quests = new QuestSystem(_context, _state.Claim(nameof(QuestSystem), StateSlice.Quests), player.Id);
        _quests.Listen(bus);
        _companions = new CompanionSystem(_context, _state.Claim(nameof(CompanionSystem), StateSlice.Companions), player.Id, player.Companions, life);
        _sheets = new PartySheetSystem(_context, _state.Claim(nameof(PartySheetSystem), StateSlice.PartySheets), player.Id);
        _sheets.Listen(bus);
        _debugger = new QuestDebugger(_context, _quests, _dialogue, _gathering.Views, _trade.View, () => Containers, _creatures.Views, _inventory.WorldItems);
        _deeds = new DeedSystem(_context, _state.Claim(nameof(DeedSystem), StateSlice.Deeds));
        _weather = new WeatherSystem(_context, _state.Claim(nameof(WeatherSystem), StateSlice.Weather), new CellKey(_cells[0].Region, 0, 0));
        _factions = new FactionSystem(_context, _state.Claim(nameof(FactionSystem), StateSlice.Factions));
        _quickBar = new QuickBarSystem(_context, _state.Claim(nameof(QuickBarSystem), StateSlice.QuickBar), player.Id);
        _teaching = new TeachingSystem(_context, player.Id, _dialogue.FactsOf, _npcs);
        _durability = new DurabilitySystem(_context, player.Id);
        _durability.Listen(bus);
        _laden = new LadenSystem(_context, () => _movement.Effective, _inventory.CarriedGrams, _inventory.CarryLimitGrams);
        _approval = new ApprovalSystem(_context, _dialogue.Banter);
        _approval.Listen(bus);
        _lfg = new LfgSystem(_context, _state.Claim(nameof(LfgSystem), StateSlice.Lfg), player.Id);
        _chat = new ChatSystem(_context, player.Id, new CellKey(_cells[0].Region, 0, 0), _lfg, t => _weather.FrontAt(t).State);
        _chat.Listen(bus);
        _adventurers = new AdventurerSystem(_context, _state.Claim(nameof(AdventurerSystem), StateSlice.Adventurers), player.Id);
        _adventurers.Listen(bus);
        _markets.Agents = _adventurers;
        _lfg.Source = new CombinedSettingOuts(setup.SettingOuts, _adventurers);
        _context.OtherActorName = _adventurers.NameOf;
        _tierSimulations = ImmutableArray.Create<ITierSimulation>(new NpcTierSimulation(SimulationTier.B, _npcs), new NpcTierSimulation(SimulationTier.C, _npcs));
        // The abstract advance (WP11): the clock first, so every other system brings itself to the tick it reads. A new world system
        // that goes on while the character is away adds itself here.
        _advances = ImmutableArray.Create<IAbstractAdvance>(_clock, _weather, _adventurers, _creatures, _effects, _gathering, _quests, _factions, _companions, _sheets, _approval, _npcs, _markets, _teaching, _durability, _laden, _lfg);
        _state.RequireEverySliceOwned();
        _effects.Seed(player.Id, player.Effects);
        _npcs.Populate();
        _adventurers.Populate();
        _markets.Populate();
        _merchants.Populate();
        _companions.Populate();
        _creatures.Populate();
        _lfg.Populate();
        _tiers.Settle();
    }

    /// <summary>
    /// Start simulating a world: a new game's, or one a save just loaded. The world tick continues from the save; the weather cursor starts
    /// empty, so the first tick announces the front overhead.
    /// </summary>
    /// <param name="life">
    /// The life party members go back to when they leave the party (WP15b): the NPC life simulation once WP12 merges; until then, and when
    /// none is given, <see cref="HomeLife"/> - they wait at home.
    /// </param>
    public static Simulation Start(SimulationSetup setup, PlayerRecord player, WorldDelta world, long worldTick, IEventBus events, IPartyLife? life = null)
    {
        if (worldTick < 0)
            throw new ArgumentOutOfRangeException(nameof(worldTick), worldTick, "The world tick is never negative");
        return new Simulation(setup, player, world, worldTick, events, life ?? new HomeLife());
    }

    /// <summary>
    /// A new character standing at the region's spawn point, with the starting package (PROGRESSION.md §5) and the
    /// starting kit (PROTOTYPE.md §5 step 1: the sword equipped), and no Call.
    /// </summary>
    public static PlayerRecord NewCharacter(SimulationSetup setup, EntityId id, string name, ulong appearanceSeed) =>
        NewCharacter(setup, id, name, null, appearanceSeed);

    /// <summary>
    /// A new character made from the creator's spec: the starting package and kit, plus its Call's package, kit and contact (accepted
    /// OD-C03). The spec must name a playable people and, if any, a defined Call.
    /// </summary>
    public static PlayerRecord NewCharacter(SimulationSetup setup, EntityId id, CharacterSpec spec, ulong appearanceSeed)
    {
        spec = spec.Validated();
        setup.Character.Check(spec);
        var call = spec.CallId is { } callId ? setup.Character.Calls[callId] : null;
        var start = spec.Start == StartPoint.Road ? setup.Layout.Opening ?? setup.Layout.Spawn : setup.Layout.Spawn;
        return NewCharacter(setup, id, spec.Name, call, appearanceSeed, start) with { Identity = CharacterIdentity.Of(spec) };
    }

    private static PlayerRecord NewCharacter(SimulationSetup setup, EntityId id, string name, CallDefinition? call, ulong appearanceSeed,
        Body? start = null)
    {
        var spawn = start ?? setup.Layout.Spawn;
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
        return new PlayerRecord(id, name, spawn.XMm, spawn.YMm, spawn.ZMm, appearanceSeed, kit.Select(k => k.Entry),
            ProgressionEngine.Create(call is null ? setup.Progression : setup.Progression with { StartingPackage = setup.Progression.StartingPackage.With(call.Package) }),
            spawn.FacingMdeg, equipment: equipment, relationships: contact);
    }

    /// <summary>The cell a door stands in: its flag lives in that cell's delta.</summary>
    public static CellKey CellOf(DoorSite door) =>
        CellKey.OfWorld(door.ClosedFootprint.CenterXMm / 1000.0, door.ClosedFootprint.CenterZMm / 1000.0);

    /// <summary>The cell a switch stands in (M6): its flag, and every flag it requires, live in that cell's delta.</summary>
    public static CellKey CellOf(SwitchSite site) => CellOf(site.Body);

    /// <summary>The cell a barrier stands in (M6): the flag that lifts it lives in that cell's delta.</summary>
    public static CellKey CellOf(BarrierSite barrier) => CellOf(barrier.Footprint);

    private static CellKey CellOf(Blocker footprint)
    {
        var (x, z) = Footprints.Center(footprint);
        return CellKey.OfWorld(x / 1000.0, z / 1000.0);
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

    public PlayerView Player => new(_identity.Id, _identity.Name, _state.Body, _movement.Intent, _state.Progression,
        ProgressionEngine.Derive(_state.Progression, Setup.Progression), _state.Discoveries.Values.ToImmutableArray(),
        _state.Inventory, _state.Equipment, _state.Currency, _inventory.CarriedGrams(),
        _inventory.CarryLimitGrams(), _equipment.Armor()) { Identity = _identity.Identity };

    /// <summary>Every authored container and what it holds now.</summary>
    public ImmutableArray<ContainerView> Containers => Setup.Layout.Containers.Concat(_context.CorpseSites()).Select(_inventory.View).ToImmutableArray();

    /// <summary>Items lying in the region.</summary>
    public ImmutableArray<WorldItemView> WorldItems => _inventory.WorldItems();

    public ImmutableArray<DoorView> Doors => Setup.Layout.Doors.Select(d => new DoorView(d, _context.IsOpen(d))).ToImmutableArray();

    public ImmutableArray<SwitchView> Switches => Setup.Layout.Switches.Select(s => new SwitchView(s, _context.IsSet(s))).ToImmutableArray();

    public ImmutableArray<BarrierView> Barriers => Setup.Layout.Barriers.Select(b => new BarrierView(b, !_context.IsLifted(b))).ToImmutableArray();

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

    /// <summary>Every NPC life (WP12): what each is doing and with whom, their needs as bands, their condition, and whether they are at work.</summary>
    public ImmutableArray<NpcLifeView> Lives => _npcs.Lives();

    /// <summary>The pairs an NPC is in (WP12): whom they know and as what.</summary>
    public ImmutableArray<NpcPair> NpcPairs(string npcId) => _npcs.PairsOf(npcId);

    /// <summary>Whether an NPC is working at their work anchor now (WP12); one with no routine always is.</summary>
    public bool AtWork(string npcId) => _npcs.AtWork(npcId);

    /// <summary>Why a merchant will not trade now (WP12: only at work, in trading hours); null when they will, or there is no such merchant.</summary>
    public string? TradeRefusal(string npcId) => _state.Npcs.TryGetValue(npcId, out var npc) ? _npcs.TradeRefusal(npc) : null;

    /// <summary>
    /// Why an NPC will not teach now (WP12's hook for WP9's trainers): a teacher teaches only at their work anchor, in their work hours. Null
    /// when they will; an NPC with no routine teaches whenever spoken to.
    /// </summary>
    public string? TeachRefusal(string npcId) => _npcs.TeachRefusal(npcId);

    /// <summary>Navigation, read-only (WP12): the grid, every gate and its state, the walkers' routes and the work counts.</summary>
    public NavigationView Navigation => new(_navigation.Grid, _navigation.Gates(),
        (_state.World.NpcLife?.Npcs.Values ?? Enumerable.Empty<NpcLifeRecord>())
            .Select(n => new NavMoverView(n.Key, n.Route, n.StuckTicks >= Setup.Navigation.Limits.BlockedViewTicks)).ToImmutableArray(),
        _navigation.Counters.Snapshot());

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

    /// <summary>
    /// A party member's sheet (WP15b), past or present: level and skills, unspent points, health on their feet, gear given or loaned, their
    /// own things, how they regard the character in words and whether they would go along with what they are asked. Null for someone who
    /// has never travelled with the character.
    /// </summary>
    public PartySheetView? PartySheet(string npcId) => _sheets.View(npcId, id =>
    {
        // Someone the region does not place (an adventurer, WP13) is known by their body's definition.
        var definition = Setup.Social.Npcs.GetValueOrDefault(id) ?? (_state.Npcs.TryGetValue(id, out var npc) ? npc.Definition : null);
        var name = definition?.Name ?? id;
        var (regard, disposition) = _approval.Standing(id, name);
        int health = 0;
        if (_companions.Views().FirstOrDefault(c => c.NpcId == id) is { } member)
            health = member.MaxHealth;
        else if ((definition?.Companion ?? definition?.Hireling?.Body) is { } body && _state.PartySheets.TryGetValue(id, out var sheet))
            health = Domain.Companions.Approval.MaxHealth(body.MaxHealth, sheet.Progression, Setup.Progression);
        return (health, regard, disposition);
    });

    /// <summary>Companions gravely wounded and recovering at home (DN9), and when each is well again.</summary>
    public ImmutableArray<RecoveryView> Recovering => _companions.Recoveries();

    /// <summary>The conversation open now, if any (M4).</summary>
    public ConversationView? Conversation => _dialogue.View();

    /// <summary>
    /// What an NPC could say in passing now (their barks whose conditions hold), each with its line ID (E-10); presentation picks one and
    /// when, and voices it when its recording exists.
    /// </summary>
    public ImmutableArray<Domain.Social.SpokenLine> Barks(string npcId) => _dialogue.Barks(npcId);

    /// <summary>What an NPC teaches now and on what terms, for the Learn column (WP9); null when they teach nothing.</summary>
    public TeachingView? Teaching(string npcId) => _teaching.View(npcId, WorldTick);

    /// <summary>Ticks until a technique is ready again (WP9); 0 when it is ready now.</summary>
    public int TechniqueCooldown(string techniqueId) => _combat.TechniqueCooldown(techniqueId, WorldTick);

    /// <summary>A trader's wares at their prices; null when that NPC does not trade (M4).</summary>
    public WaresView? Wares(string npcId) => _trade.View(npcId);

    /// <summary>What a trader would pay for one of an item now (WP14); null when they do not trade, or do not buy it.</summary>
    public long? Offer(string npcId, string itemId) => _trade.Offer(npcId, itemId);

    /// <summary>What a trader would charge to mend a carried piece now (WP3b); null when they do not repair, or it needs none.</summary>
    public RepairQuote? RepairQuote(string npcId, EntityId item) => _trade.RepairQuote(npcId, item);

    /// <summary>Above the laden band now (WP3b, D4): sprinting and dodging cost more stamina. The HUD's weight chip reads it.</summary>
    public bool Laden => _laden.Laden;

    /// <summary>What the character heard on chat (WP16), oldest first: World, Local, Party and System. Not saved; a load starts it empty.</summary>
    public ImmutableArray<ChatMessage> Chat => _chat.Heard;

    /// <summary>A chat message in words, from the content's lines; null when content no longer has its line.</summary>
    public string? ChatText(ChatMessage message) => Runtime.ChatText.Render(Setup.Chat, message);

    /// <summary>The open offers to set out together (WP16): what the Join action and <c>/join</c> answer.</summary>
    public ImmutableArray<LfgOfferView> GroupOffers => _lfg.Offers();

    /// <summary>The temporary-ally groups that set out (WP16); WP13 gives them their life.</summary>
    public ImmutableArray<AllyGroupView> AllyGroups => _lfg.Groups();

    /// <summary>The region's NPC adventurers (WP13): level, coin, gear, what they are about today, and whether they are in town or with the party.</summary>
    public ImmutableArray<AdventurerView> Adventurers => _adventurers.Views();

    /// <summary>Whether an adventurer can be hired now, and why not (WP12b's <see cref="INpcHireability"/>, answered for adventurers).</summary>
    public NpcHireView? AdventurerHireability(string key) => _adventurers.Hireability(key);

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
                MoveItemCommand item => _inventory.Handle(item, WorldTick),
                EquipCommand equip => _equipment.Handle(equip, WorldTick),
                UnequipCommand unequip => _equipment.Handle(unequip, WorldTick),
                AttackCommand attack => _combat.Handle(attack, WorldTick),
                BlockCommand block => _combat.Handle(block, WorldTick),
                DodgeCommand dodge => _laden.Dodge(() => _combat.Handle(dodge, WorldTick)),
                CastCommand cast => _combat.Handle(cast, WorldTick),
                TechniqueCommand technique => _combat.Handle(technique, WorldTick),
                LearnFromTeacherCommand learn => _teaching.Handle(learn, WorldTick),
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
                GiveGearCommand give => _sheets.Handle(give, WorldTick),
                TakeBackGearCommand takeBack => _sheets.Handle(takeBack, WorldTick),
                SpendPartyAttributeCommand spendParty => _sheets.Handle(spendParty, WorldTick),
                TouchStoneCommand touch => _travel.Handle(touch, WorldTick),
                BindStoneCommand bind => _travel.Handle(bind, WorldTick),
                TravelCommand travel => _travel.Handle(travel, WorldTick),
                BeginStoryCommand begin => _quests.Handle(begin, WorldTick),
                ClaimBountyCommand claim => _quests.Handle(claim, WorldTick),
                AssignQuickSlotCommand assign => _quickBar.Handle(assign, WorldTick),
                RepairCommand repair => _durability.Handle(repair, WorldTick),
                MerchantRepairCommand repair => _trade.Handle(repair, WorldTick),
                JoinGroupCommand join => _lfg.Handle(join, WorldTick),
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
    /// Advance one fixed tick. The order is data, fixed here: movement, tiers, the weather, the tier simulations, the player's combat and
    /// the laden band's extra drain, creatures, companions, the party's regard and banter, NPCs, conversations, status effects, death,
    /// discovery, quests (which read what all of that did), the markets' day, the quick bar (which offers a slot to what came to hand),
    /// offers to set out together, chat (which words what all of that did), clock.
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
            _laden.Tick(tick);
            _creatures.Tick(tick);
            _companions.Tick(tick);
            _approval.Tick(tick);
            _npcs.Tick(tick);
            _dialogue.Tick(tick);
            _effects.Tick(tick);
            _death.Tick(tick);
            _discovery.Tick(tick);
            _quests.Tick(tick);
            _adventurers.Tick(tick);
            _markets.Tick(tick);
            _quickBar.Tick(tick);
            _lfg.Tick(tick);
            _chat.Tick(tick);
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
            _chat.AfterAbsence(log);
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
            Factions = _state.Factions,
            Party = _companions.Ledger(),
            QuickBar = _state.QuickBar,
            QuickBarCleared = _state.QuickBarCleared.ToImmutableArray(),
            PartySheets = _sheets.Records(),
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
        // NPC life (WP12): every life, the slots awaiting arrivals, the pairs.
        h.Add(World.NpcLife is not null);
        World.NpcLife?.AddTo(h);
        // Adventurers (WP13): every life and the slots awaiting arrivals.
        h.Add(World.Adventurers is not null);
        World.Adventurers?.AddTo(h);
        return h.Finish();
    }

    /// <summary>
    /// An internal command at the tick boundary, as another system would send it: for tests and the fixture probe only (internal), standing
    /// in for systems not built yet - residents' departures and injuries by rule.
    /// </summary>
    internal string? Submit(InternalCommand command)
    {
        if (_stepping || _advancing)
            throw new InvalidOperationException("Submit applies at a tick boundary");
        return Dispatch(command);
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
        WearEquipped wear => _inventory.Handle(wear, Now),
        RepairItem repair => _inventory.Handle(repair),
        ChangeRelationship change => _relationships.Handle(change, Now),
        StartQuest start => _quests.Handle(start, Now),
        RecordDeed deed => _deeds.Handle(deed) ?? _quests.Handle(deed),
        Recruit recruit => _companions.Handle(recruit, Now),
        OrderCompanion order => _companions.Handle(order, Now),
        CompanionStruck struck => _companions.Handle(struck, Now),
        PlaceNpc place => _npcs.Handle(place),
        RemoveNpc remove => _npcs.Handle(remove),
        DepartNpc depart => _npcs.Handle(depart, Now),
        InjureNpc injure => _npcs.Handle(injure),
        Hire hire => _companions.Handle(hire, Now),
        Dismiss dismiss => _companions.Handle(dismiss, Now),
        RefitMember refit => _companions.Handle(refit),
        LeaveParty leave => _companions.Handle(leave, Now),
        PracticePartySkill practice => _sheets.Handle(practice),
        ShareKill share => _sheets.Handle(share),
        ReturnLoans loans => _sheets.Handle(loans, Now),
        SettleFallen fallen => _sheets.Handle(fallen, Now),
        HandOver hand => _inventory.Handle(hand),
        ReceiveStack receive => _inventory.Handle(receive, Now),
        ReleaseItem release => _equipment.Handle(release, Now),
        ArriveWithParty arrive => _companions.Handle(arrive),
        RecordAct act => _factions.Handle(act, Now),
        ReportAct report => _factions.Handle(report, Now),
        GrantStanding grant => _factions.Handle(grant, Now),
        Teach teach => _teaching.Handle(teach, Now),
        ClaimBounty claim => _quests.Handle(claim),
        CullCreatures cull => _creatures.Handle(cull),
        AdoptSheet adopt => _sheets.Handle(adopt),
        ReleaseSheet release => _sheets.Handle(release),
        EmbodyNpc embody => _npcs.Handle(embody),
        SettleRetiree retiree => _npcs.Handle(retiree),
        AdventurerPartyChanged changed => _adventurers.Handle(changed, Now),
        ShareBounty share => _adventurers.Handle(share, Now),
        _ => throw new InvalidOperationException($"No system handles {command.GetType().Name}"),
    };
}
