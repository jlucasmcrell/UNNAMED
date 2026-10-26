// UNNAMED World - the running simulation: composition root, command queue, fixed tick (ARCHITECTURE.md Â§4.5, Â§7, Â§8.1)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Economy;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Pets;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.Domain.Travel;

namespace UNNAMED.World.Runtime;

/// <summary>The rules a simulation runs under, built from content at boot.</summary>
public sealed record SimulationSetup(RegionLayout Layout, MovementRules Movement, ProgressionRules Progression, TierRules Tiers, int TickMilliseconds)
{
    /// <summary>Items, loot tables, carrying rules and the starting kit (M3b).</summary>
    public ItemSetup Items { get; init; } = ItemSetup.Empty;

    /// <summary>The lore entries, by ID (spec 41): what a discovery opens, and the words a page is read by.</summary>
    public ImmutableSortedDictionary<string, UNNAMED.Domain.Knowledge.LoreEntryDefinition> Lore { get; init; } =
        ImmutableSortedDictionary<string, UNNAMED.Domain.Knowledge.LoreEntryDefinition>.Empty;

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

    /// <summary>An interior this run can reach (any one; name one through <see cref="Layouts"/>), or null when it holds none.</summary>
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
        space == Layout.SpaceId ? Layout : Layouts.TryGetValue(space, out var held) ? held : Layout;

    /// <summary>
    /// The ground and walls of <paramref name="space"/>, or the layout's own when it names nothing this holds. The
    /// fallback is deliberate: a space that is not loaded has no ground, and a caller asking for one is asking where
    /// something in the loaded region would stand.
    /// </summary>
    public WalkSpace SpaceFor(SpaceId space) =>
        space == Layout.SpaceId ? Layout.Space : Spaces.TryGetValue(space, out var held) ? held : Layout.Space;

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

    /// <summary>
    /// Who sets out and will take company (the WP12 contract). Null, as content builds it: the NPC life simulation's trip planner (WP12b). A
    /// fixed schedule (<see cref="Runtime.SettingOuts.Of"/>) stands in for it in tests of the contract.
    /// </summary>
    public ISettingOutSource? SettingOuts { get; init; }

    /// <summary>
    /// Who may say an ambient line, and where (the WP12 contract). Null, as content builds it: the NPC life simulation's role residents
    /// (WP12b). A fixed set (<see cref="Runtime.ChatterSpeakers.Of"/>) stands in for them in tests of chat.
    /// </summary>
    public IChatterSpeakers? ChatterSpeakers { get; init; }

    /// <summary>Who the world is simulated around (WP12b): the character alone, unless a server - or a test - names others.</summary>
    public ITierObservers Observers { get; init; } = TierObservers.PlayerOnly;

    /// <summary>The navigation lattice and its limits (<c>config.navigation</c>; WP12).</summary>
    public NavConfig Navigation { get; init; } = NavConfig.Default;

    /// <summary>The routines, pairs and rules of NPC life in the region played (WP12).</summary>
    public NpcLifeSetup NpcLife { get; init; } = NpcLifeSetup.Empty;

    /// <summary>The NPC adventurers' settlements, rules and hiring (<c>config.adventurers</c>; WP13); null in a pack with none.</summary>
    public AdventurerSetup? Adventurers { get; init; }

    /// <summary>The pets' rules and the species a creature is a pet as (<c>config.pets</c> and the <c>pet:</c> blocks; spec 43); null in a pack with none.</summary>
    public UNNAMED.Domain.Pets.PetSetup? Pets { get; init; }
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

    /// <summary>The bag in each bag slot (the carried bag's identity), or null for an empty slot.</summary>
    public ImmutableArray<EntityId?> Bags { get; init; } = ImmutableArray<EntityId?>.Empty;
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
    private readonly PeopleSystem _people;
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
    private readonly LoreSystem _lore;
    private readonly NpcSystem _npcs;
    private readonly WorldNavigation _navigation;
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
    private readonly GearSetSystem _gearSets;
    private readonly TeachingSystem _teaching;
    private readonly DurabilitySystem _durability;
    private readonly LadenSystem _laden;
    private readonly PartySheetSystem _sheets;
    private readonly PetSystem _pets;
    private readonly ApprovalSystem _approval;
    private readonly LfgSystem _lfg;
    private readonly ChatSystem _chat;
    private readonly AdventurerSystem _adventurers;
    private readonly CombinedSettingOuts _trips;
    private readonly CombinedHireability _hiring;
    private readonly ImmutableArray<ITierSimulation> _tierSimulations;
    private readonly ImmutableArray<IAbstractAdvance> _advances;
    private long _sequence;
    private bool _stepping;
    private bool _advancing;

    private Simulation(SimulationSetup setup, PlayerRecord player, WorldDelta world, long worldTick, IEventBus events, IPartyLife? life, bool restoreAttempt)
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
        _context = new SystemContext(_state, setup, bus, Dispatch) { PlayerPeople = player.Identity.PeopleId };
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

        // The composition root: an explicit, ordered list, and every state slice claimed exactly once (Â§4.2, Â§5).
        _clock = new ClockSystem(_context, _state.Claim(nameof(ClockSystem), StateSlice.Clock));
        _movement = new MovementSystem(_context, _state.Claim(nameof(MovementSystem), StateSlice.PlayerBody), player.Id);
        _interaction = new InteractionSystem(_context, player.Id);
        _people = new PeopleSystem(_context, _state.Claim(nameof(PeopleSystem), StateSlice.Crossing), player.Id, () => _movement.Effective);
        _flags = new WorldFlagSystem(_context, _state.Claim(nameof(WorldFlagSystem), StateSlice.WorldFlags));
        _progression = new ProgressionSystem(_context, _state.Claim(nameof(ProgressionSystem), StateSlice.PlayerProgression));
        _discovery = new DiscoverySystem(_context, _state.Claim(nameof(DiscoverySystem), StateSlice.Discoveries));
        _tiers = new TierSystem(_context, _state.Claim(nameof(TierSystem), StateSlice.CellTiers), _cells, setup.Observers);
        _inventory = new InventorySystem(_context, _state.Claim(nameof(InventorySystem), StateSlice.PlayerInventory, StateSlice.WorldItems), player.Id);
        _equipment = new EquipmentSystem(_context, _state.Claim(nameof(EquipmentSystem), StateSlice.PlayerEquipment), player.Id);
        _combat = new CombatSystem(_context, _state.Claim(nameof(CombatSystem), StateSlice.Combat), player.Id, () => _movement.Effective);
        _creatures = new CreatureSystem(_context, _state.Claim(nameof(CreatureSystem), StateSlice.Creatures), player.Id, () => _movement.Effective);
        _effects = new StatusEffectSystem(_context, _state.Claim(nameof(StatusEffectSystem), StateSlice.Effects), player.Id);
        _travel = new TravelSystem(_context, _state.Claim(nameof(TravelSystem), StateSlice.Waystones), player.Id);
        _gathering = new GatheringSystem(_context, _state.Claim(nameof(GatheringSystem), StateSlice.Nodes), player.Id);
        _crafting = new CraftingSystem(_context, _state.Claim(nameof(CraftingSystem), StateSlice.Findings), player.Id, player.Name) { Contents = site => _inventory.View(site) };
        _lore = new LoreSystem(_context, _state.Claim(nameof(LoreSystem), StateSlice.Lore), player.Id);
        _lore.Listen(bus);
        _navigation = new WorldNavigation(_context);
        _npcs = new NpcSystem(_context, _state.Claim(nameof(NpcSystem), StateSlice.Npcs, StateSlice.NpcLife), _navigation, _tiers.Observers,
            () => _movement.Effective);
        _relationships = new RelationshipSystem(_context, _state.Claim(nameof(RelationshipSystem), StateSlice.Relationships));
        _relationships.Listen(bus);
        _dialogue = new DialogueSystem(_context, _state.Claim(nameof(DialogueSystem), StateSlice.Conversations), player.Id);
        _markets = new MarketSystem(_context, _state.Claim(nameof(MarketSystem), StateSlice.Markets));
        _merchants = new MerchantSystem(_context, _state.Claim(nameof(MerchantSystem), StateSlice.Merchants), _markets);
        _merchants.Listen(bus);
        _trade = new TradeSystem(_context, player.Id, _inventory.View, _markets, _merchants) { Closed = _npcs.TradeRefusal };
        _markets.Jobs = _npcs;
        _quests = new QuestSystem(_context, _state.Claim(nameof(QuestSystem), StateSlice.Quests), player.Id);
        _quests.Listen(bus);
        _companions = new CompanionSystem(_context, _state.Claim(nameof(CompanionSystem), StateSlice.Companions), player.Id, player.Companions,
            new NpcPartyLife(Dispatch, _npcs.Lives, life))
        {
            Unhireable = key => _adventurers.HireRefusal(key) ?? _npcs.HireRefusal(key),
            NameOf = key => _adventurers.NameOfKey(key),
            PlayerFocus = () => _creatures.PlayerFocus(WorldTick),
        };
        _sheets = new PartySheetSystem(_context, _state.Claim(nameof(PartySheetSystem), StateSlice.PartySheets), player.Id);
        _sheets.Listen(bus);
        // The party's animals (spec 43). Their own slice: an animal's body is its own, moved here, and its rules are their own.
        _pets = new PetSystem(_context, _state.Claim(nameof(PetSystem), StateSlice.Pets), player.Id, player.Pets)
        {
            // Only the companions on their feet are worth anything to an encounter: one down and waiting to be helped up is not fighting.
            Companions = () => _companions.Views().Count(c => c.Condition == CompanionCondition.Up),
            PlayerFocus = () => _creatures.PlayerFocus(WorldTick),
        };
        _spaces = new SpaceSystem(_context, _state.Claim(nameof(SpaceSystem), StateSlice.PlayerSpace), player.Id);
        // A loaded body stands where its save left it, and half of that is the space (the interior-space save extension). Without this the
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
        _gearSets = new GearSetSystem(_context, _state.Claim(nameof(GearSetSystem), StateSlice.GearSets), player.Id,
            (command, tick) => _equipment.Handle(command, tick), (command, tick) => _equipment.Handle(command, tick),
            () => _state.PlayerCombat.Action.PhaseAt(WorldTick, setup.Combat.Constants).Phase != CombatPhase.Idle || _creatures.Views().Any(c => c.Alive && c.Hostile));
        _teaching = new TeachingSystem(_context, player.Id, _dialogue.FactsOf, _npcs);
        _durability = new DurabilitySystem(_context, player.Id);
        _durability.Listen(bus);
        _laden = new LadenSystem(_context, () => _movement.Effective, _inventory.CarriedGrams, _inventory.CarryLimitGrams);
        _approval = new ApprovalSystem(_context, _dialogue.Banter, () => _quests.Tracked(null) is { } tracked ? _quests.Marker(tracked) : null);
        _approval.Listen(bus);
        _adventurers = new AdventurerSystem(_context, _state.Claim(nameof(AdventurerSystem), StateSlice.Adventurers), player.Id);
        _trips = new CombinedSettingOuts(setup.SettingOuts ?? _npcs, _adventurers);
        _hiring = new CombinedHireability(_adventurers, _npcs);
        _lfg = new LfgSystem(_context, _state.Claim(nameof(LfgSystem), StateSlice.Lfg), player.Id, _trips);
        _chat = new ChatSystem(_context, player.Id, new CellKey(_cells[0].Region, 0, 0), _lfg, t => _weather.FrontAt(t).State, setup.ChatterSpeakers ?? _npcs);
        _chat.Listen(bus);
        _adventurers.Listen(bus);
        _markets.Agents = _adventurers;
        _context.OtherActorName = _adventurers.NameOf;
        _tierSimulations = ImmutableArray.Create<ITierSimulation>(new NpcTierSimulation(SimulationTier.B, _npcs), new NpcTierSimulation(SimulationTier.C, _npcs));
        // The abstract advance (WP11): the clock first, so every other system brings itself to the tick it reads. A new world system
        // that goes on while the character is away adds itself here. The party goes before the adventurers: an adventurer it lets go or
        // loses at a midnight of the absence takes up their life from that midnight, however the absence is split (T-22).
        _advances = ImmutableArray.Create<IAbstractAdvance>(_clock, _weather, _companions, _adventurers, _creatures, _effects, _gathering, _quests, _factions, _sheets, _approval, _npcs, _markets, _teaching, _durability, _laden, _lfg, _pets);
        _state.RequireEverySliceOwned();
        _effects.Seed(player.Id, player.Effects);
        _combat.Seed(player);
        _npcs.Populate();
        _adventurers.Populate();
        _markets.Populate();
        _merchants.Populate();
        _companions.Populate();
        _npcs.SettleParty();
        // The animals before the world they were taken out of: an encounter is scaled to the party as it stands, and an animal taken as a
        // pet leaves its spawn slot empty instead of standing there again (spec 43).
        _pets.Populate();
        _creatures.Scale = () => _pets.Encounter;
        _creatures.TamedAway = _pets.TamedAway;
        // What a creature fights when the party has animals in it (spec 43 ruling 5): the handler behind an animal that savaged it, the animal
        // that savaged it, the weakest of the party, or the one nearest.
        _creatures.PetsOnFeet = () => _pets.Views().Where(p => p.Condition == PetCondition.Up)
            .Select(p => (p.Body, p.Id, p.Health, p.MaxHealth)).ToImmutableArray();
        _creatures.Afterimages = () => _people.Ghost(Now) is { } ghost ? _companions.Afterimages(Now).Append(ghost) : _companions.Afterimages(Now);
        _creatures.Populate();
        _lfg.Populate();
        if (restoreAttempt) _creatures.RecoverLoadedAttempt();
        _tiers.Settle();
        SeedReachableNotices();
    }

    /// <summary>Whether a way that gives under its first crossing (a rotten rope) has already given.</summary>
    public bool PortalGone(SpacePortal portal) => portal.GivesWay is { } gives && _context.RequirementMet(gives.Flag);

    /// <summary>Whether a way can be entered from outside: one found from the other side opens with its flag.</summary>
    public bool PortalOpen(SpacePortal portal) => portal.OpensWith is not { } opens || _context.RequirementMet(opens);

    /// <summary>
    /// What is readable in a world the character can reach as it starts. Only an interior this run holds contributes:
    /// with the gate off no interior is loaded, so nothing is seeded and Kera's offer stays closed (B3e).
    /// </summary>
    private void SeedReachableNotices()
    {
        foreach (var (_, portal) in Setup.Sites(l => l.Portals.Portals))
        {
            if (portal.ReachableFlag is not { } flag) continue;
            foreach (var (layout, place) in Setup.Sites(l => l.Locations))
                if (layout.SpaceId == portal.Space && place.Id == flag.LocationId)
                    _flags.Seed(_context.CellOfSite(layout.SpaceId, place.XMm, place.ZMm), flag.FlagId, 1);
        }
    }

    /// <summary>
    /// Start simulating a world: a new game's, or one a save just loaded. The world tick continues from the save; the weather cursor starts
    /// empty, so the first tick announces the front overhead.
    /// </summary>
    /// <param name="life">
    /// Hears party members join and leave (WP15b's <see cref="IPartyLife"/>) besides the NPC life simulation, which always takes them up
    /// (WP12b): a recorder for tests, or null.
    /// </param>
    public static Simulation Start(SimulationSetup setup, PlayerRecord player, WorldDelta world, long worldTick, IEventBus events, IPartyLife? life = null,
        bool restoreAttempt = false)
    {
        if (worldTick < 0)
            throw new ArgumentOutOfRangeException(nameof(worldTick), worldTick, "The world tick is never negative");
        return new Simulation(setup, player, world, worldTick, events, life, restoreAttempt);
    }

    /// <summary>
    /// A new character standing at the region's spawn point, with the starting package (PROGRESSION.md Â§5) and the
    /// starting kit (PROTOTYPE.md Â§5 step 1: the sword equipped), and no Call.
    /// </summary>
    public static PlayerRecord NewCharacter(SimulationSetup setup, EntityId id, string name, ulong appearanceSeed) =>
        NewCharacter(setup, id, name, null, [], appearanceSeed, null, null);

    /// <summary>
    /// A new character made from the creator's spec: the starting package and kit, plus its Call's package, kit and contact (accepted
    /// OD-C03), and its people's creation grant (RACE_ABILITIES_PROPOSAL.md §1), and its people's heritage. The spec must name a playable people and, if any, a defined
    /// Call. Its origin, not its people, decides where it starts (CHARACTER_CREATION_AND_LINEAGE.md §4): the origin's place, or the road in;
    /// the Waystone start skips either.
    /// </summary>
    /// <param name="atXMm">Where a capture stands the character; null uses the region's spawn, as every ordinary run does.</param>
    /// <param name="atZMm">The other half of <paramref name="atXMm"/>.</param>
    public static PlayerRecord NewCharacter(SimulationSetup setup, EntityId id, CharacterSpec spec, ulong appearanceSeed,
        long? atXMm = null, long? atZMm = null)
    {
        spec = spec.Validated();
        setup.Character.Check(spec);
        var call = spec.CallId is { } callId ? setup.Character.Calls[callId] : null;
        var heritage = setup.Character.Peoples[spec.PeopleId].Heritage;
        var start = spec.Start == StartPoint.Road ? OriginStart(setup, spec.OriginId) : setup.Layout.Spawn;
        var record = NewCharacter(setup, id, spec.Name, call, heritage, appearanceSeed, start, atXMm, atZMm) with { Identity = CharacterIdentity.Of(spec) };
        return record.WithProgression(setup.Character.RaceGrants(spec).Aggregate(record.Progression, (p, grant) => ProgressionEngine.Grant(p, grant, setup.Progression)));
    }

    /// <summary>Where an origin's road in begins: its start place, facing as the road in does, or the region's road in itself.</summary>
    private static Body OriginStart(SimulationSetup setup, string originId)
    {
        var road = setup.Layout.Opening ?? setup.Layout.Spawn;
        if (setup.Character.Origins.GetValueOrDefault(originId)?.StartLocationId is not { } place
            || setup.Layout.Locations.FirstOrDefault(l => l.Id == place) is not { } site)
            return road;
        return new Body(site.XMm, setup.SpaceFor(setup.Layout.SpaceId).HeightAtMm(site.XMm, site.ZMm), site.ZMm, road.FacingMdeg);
    }

    private static PlayerRecord NewCharacter(SimulationSetup setup, EntityId id, string name, CallDefinition? call, ImmutableArray<StartingItem> heritage,
        ulong appearanceSeed, Body? start = null, long? atXMm = null, long? atZMm = null)
    {
        var spawn = start ?? setup.Layout.Spawn;
        // A capture opens the character where it asked to rather than walking him there, which in the Barrowworks
        // measured about a quarter of an hour per room (plan P2h). Null is the chosen start, so ordinary runs are
        // untouched - the road start still decides where a character is made.
        long xMm = atXMm ?? spawn.XMm;
        long zMm = atZMm ?? spawn.ZMm;
        var lines = (call is null ? setup.Items.StartingKit : setup.Items.StartingKit.AddRange(call.Kit)).AddRange(heritage);
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

    public RegionLayout ActiveLayout => Setup.LayoutFor(_state.Space);
    public long ActiveSpaceTick => _context.SpaceTick(_state.Space, _state.WorldTick);

    public PlayerView Player => new(_identity.Id, _identity.Name, _state.Body, _movement.Intent, _state.Progression,
        _context.PlayerStats(), _state.Discoveries.Values.ToImmutableArray(),
        _state.Inventory, _state.Equipment, _state.Currency, _inventory.CarriedGrams(),
        _inventory.CarryLimitGrams(), _equipment.Armor()) { Identity = _identity.Identity, Bags = _state.Bags };

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

    /// <summary>Every NPC life (WP12): what each is doing and with whom, their needs as bands, their condition, and whether they are at work.</summary>
    public ImmutableArray<NpcLifeView> Lives => _npcs.Lives();


    /// <summary>
    /// Who can be hired (WP12b), derived: never a vendor or a quest giver; anyone else whose town roles hire out and who is about their own
    /// life; and the adventurers in town (WP13), whom their own system answers for. <see cref="HireabilityChanged"/> follows each change.
    /// </summary>
    public INpcHireability Hiring => _hiring;

    /// <summary>Whether someone can be hired now (WP12b).</summary>
    public bool IsHireable(string npcId) => _hiring.IsHireable(npcId);

    /// <summary>Who sets out on a trip and asks along whoever will come (WP12b's planner): every SettingOut in (from, to], in tick order.</summary>
    public ISettingOutSource Trips => _trips;

    /// <summary>Who the world is simulated around now (WP12b): the character, and any other observer.</summary>
    public IReadOnlyList<TierObserver> Observers => _tiers.Observers(WorldTick);

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

    /// <summary>The six action bars, the quick bar first, each twelve slots.</summary>
    public ImmutableArray<ImmutableArray<string?>> ActionBars => _state.ActionBars;

    /// <summary>The weapon family whose layout the main bar shows ("" before the first tick adopts the one held), and the layout of each family held so far.</summary>
    public string HeldFamily => _state.HeldFamily;

    public ImmutableSortedDictionary<string, ImmutableArray<string?>> FamilyBars => _state.FamilyBars;

    /// <summary>The character's saved gear sets, by place.</summary>
    public ImmutableArray<GearSet> GearSets => _state.GearSets;

    /// <summary>A change of gear under way, or null.</summary>
    public GearSwapView? GearSwap => _gearSets.Swap;

    /// <summary>What the character's experiments taught, oldest first (UI progression F).</summary>
    public ImmutableArray<Finding> Findings => _state.Findings;
    public SalvageView Salvage(EntityId item) => _crafting.Salvage(item, WorldTick);

    /// <summary>Why a trader and the character may not trade an item, by its heritage; null when they may.</summary>
    public string? HeritageRefusal(string npcId, string itemId) => _trade.HeritageRefusal(npcId, itemId);

    /// <summary>The lore pages the character has found, in entry and page order (spec 41).</summary>
    public ImmutableArray<UNNAMED.Domain.Knowledge.LorePageRecord> Lore => _state.Lore;

    /// <summary>The character's companions (M6): the whole party - companions, hirelings and allies (WP15a).</summary>
    public ImmutableArray<CompanionView> Companions => _companions.Views();

    /// <summary>The animals that travel with the character, on their feet or home badly wounded (spec 43).</summary>
    public ImmutableArray<PetView> Pets => _pets.Views();

    /// <summary>The pet rules (<c>config.pets</c>): the taming odds, the skills, the lure, the wound that sends one home, the encounter scale.</summary>
    public UNNAMED.Domain.Pets.PetRules PetRules => _pets.Rules;

    /// <summary>The offer of food under way on a creature, or null: what the HUD shows while the hand is held out.</summary>
    public TameProgress? TameOf(EntityId creature) => _pets.Offer(creature);

    /// <summary>What the party is worth to an encounter, in points: one a follower, a quarter a level past the first (owner ruling 5).</summary>
    public double PartyStrength => _pets.StrengthPoints();

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
            health = Domain.Companions.Approval.MaxHealth(body.MaxHealth, sheet.Progression, Setup.Progression)
                     * Setup.Character.TraitsOf(_context.PeopleOf(id)).HealthPercent / 100;
        return (health, regard, disposition);
    });

    /// <summary>Companions gravely wounded and recovering at home (DN9), and when each is well again.</summary>
    public ImmutableArray<RecoveryView> Recovering => _companions.Recoveries();

    /// <summary>The conversation open now, if any (M4).</summary>
    public ConversationView? Conversation => _dialogue.View();

    /// <summary>Read-only dialogue history for the presentation's unread-topic badge.</summary>
    public bool DialogueVisited(string dialogueId, string nodeId) => _dialogue.Visited(dialogueId, nodeId);

    /// <summary>
    /// What an NPC could say in passing now (their barks whose conditions hold), each with its line ID (E-10); presentation picks one and
    /// when, and voices it when its recording exists.
    /// </summary>
    public ImmutableArray<Domain.Social.SpokenLine> Barks(string npcId) => _dialogue.Barks(npcId);

    /// <summary>What an NPC teaches now and on what terms, for the Learn column (WP9); null when they teach nothing.</summary>
    public TeachingView? Teaching(string npcId) => _teaching.View(npcId, WorldTick);

    /// <summary>Ticks until a technique is ready again (WP9); 0 when it is ready now.</summary>
    public int TechniqueCooldown(string techniqueId) => _combat.TechniqueCooldown(techniqueId, WorldTick);

    /// <summary>Ticks until a racial ability is ready again; 0 when it is. The wings' are the movement system's.</summary>
    public int PeopleAbilityCooldown(string abilityId) =>
        _context.PlayerAbilities.FirstOrDefault(a => a.Id == abilityId) is { Action: PeopleAction.Leap or PeopleAction.Flare } wing
            ? _movement.Cooldown(wing.Action, WorldTick)
            : _people.Cooldown(abilityId, WorldTick);

    /// <summary>The player's racial abilities, in content order (the creator's card and the character sheet list them).</summary>
    public ImmutableArray<PeopleAbility> PlayerAbilities => _context.PlayerAbilities;

    /// <summary>The movement rules the player moves by - its people's speed and wings - which presentation predicts with.</summary>
    public MovementRules PlayerMovement => _context.PlayerMovement;

    /// <summary>Whether the planar anchor is set (Orenth), for the quick bar's recall slot. Never the toll: that stays hidden.</summary>
    public bool AnchorSet => _state.Crossing.Anchor is not null;

    /// <summary>What the crossings have taken from the character's memory (Orenth): places and people, by ID. Never how much is left.</summary>
    public ImmutableSortedSet<string> Forgotten => _state.Crossing.Costs.Select(c => c.Ref).OfType<string>().ToImmutableSortedSet(StringComparer.Ordinal);

    /// <summary>The crossing's tier (Orenth): how many phase scars the body carries. Presentation draws it; nothing ever prints it.</summary>
    public int PhaseScars => _state.Crossing.Tier;

    /// <summary>A trader's wares at their prices; null when that NPC does not trade (M4).</summary>
    public WaresView? Wares(string npcId) => _trade.View(npcId);

    /// <summary>The pack as it lies now: the backpack and each bag with its stacks by slot.</summary>
    public PackView Pack => _inventory.Pack();

    /// <summary>The stashes in a crafting station's settlement and what each holds, nearest the station first: what the station also works from.</summary>
    public ImmutableArray<ContainerView> StashesAt(StationSite station) =>
        _context.StashesFor(station).OrderBy(s => Math.Pow(s.XMm - station.XMm, 2) + Math.Pow(s.ZMm - station.ZMm, 2)).ThenBy(s => s.Key, StringComparer.Ordinal)
            .Select(_inventory.View).ToImmutableArray();

    /// <summary>What a trader would pay for one of an item now (WP14); null when they do not trade, or do not buy it.</summary>
    public long? Offer(string npcId, string itemId, ItemWear? wear = null, int quality = 0, NotableRecord? notable = null) =>
        _trade.Offer(npcId, itemId, wear, quality, notable);

    /// <summary>What a trader would charge to mend a carried piece now (WP3b); null when they do not repair, or it needs none.</summary>
    public RepairQuote? RepairQuote(string npcId, EntityId item) => _trade.RepairQuote(npcId, item);

    /// <summary>Above the laden band now (WP3b, D4): sprinting and dodging cost more stamina. The HUD's weight chip reads it.</summary>
    public bool Laden => _laden.Laden;

    /// <summary>What the character heard on chat (WP16), oldest first: World, Local, Party and System. Not saved; a load starts it empty.</summary>
    public ImmutableArray<ChatMessage> Chat => _chat.Heard;

    /// <summary>Every line of every conversation the character has heard, by dialogue (the Siann's Perfect Recall reads them back).</summary>
    public ImmutableSortedDictionary<string, ImmutableSortedSet<string>> ConversationsHeard => _state.Conversations;

    /// <summary>A chat message in words, from the content's lines; null when content no longer has its line.</summary>
    public string? ChatText(ChatMessage message) => Runtime.ChatText.Render(Setup.Chat, message);

    /// <summary>The open offers to set out together (WP16): what the Join action and <c>/join</c> answer.</summary>
    public ImmutableArray<LfgOfferView> GroupOffers => _lfg.Offers();

    /// <summary>The temporary-ally groups that set out (WP16); WP13 gives them their life.</summary>
    public ImmutableArray<AllyGroupView> AllyGroups => _lfg.Groups();

    /// <summary>The region's NPC adventurers (WP13): level, coin, gear, what they are about today, and whether they are in town or with the party.</summary>
    public ImmutableArray<AdventurerView> Adventurers => _adventurers.Views();


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

    /// <summary>What the character is called for what they have finished (<c>title</c> rewards of completed quests), in the order they were earned.</summary>
    public ImmutableArray<string> Titles =>
        _state.Quests.Values.Where(q => q.Status == QuestStatus.Completed && Setup.Quests.Quests.ContainsKey(q.QuestId)).OrderBy(q => q.EndedTick)
            .SelectMany(q => Setup.Quests.Quests[q.QuestId].Rewards.OfType<TitleReward>()).Select(t => t.For(_identity.Name)).ToImmutableArray();

    /// <summary>Whether a new game has a story to begin: some quest starts for a new character of this people (<c>on_new_game</c>).</summary>
    public bool HasOpeningQuests => Setup.Quests.Quests.Values.Any(q => q.OnNewGame && (q.PeopleId is null || q.PeopleId == _identity.Identity.PeopleId));

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

    /// <summary>Which system owns each slice of state (ARCHITECTURE.md Â§5).</summary>
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
                GlideCommand glide => _movement.Handle(glide, WorldTick),
                PeopleAbilityCommand ability => _people.Handle(ability, WorldTick),
                TakeAllCommand takeAll => _inventory.Handle(takeAll, WorldTick),
                SpendAttributeCommand spend => spend.Actor != PlayerId ? $"unknown actor {spend.Actor}" : _progression.Handle(spend, WorldTick),
                InteractCommand interact => _interaction.Handle(interact, WorldTick),
                EnterSpaceCommand enter => _spaces.Enter(enter, WorldTick),
                LeaveSpaceCommand leave => _spaces.Leave(leave),
                MoveItemCommand item => _inventory.Handle(item, WorldTick),
                EquipBagCommand equipBag => _inventory.Handle(equipBag, WorldTick),
                UnequipBagCommand unequipBag => _inventory.Handle(unequipBag, WorldTick),
                PlaceStackCommand place => _inventory.Handle(place, WorldTick),
                SortPackCommand sort => _inventory.Handle(sort, WorldTick),
                MarkJunkCommand junk => _inventory.Handle(junk, WorldTick),
                LockStackCommand locked => _inventory.Handle(locked, WorldTick),
                SellJunkCommand sellJunk => _trade.Handle(sellJunk, WorldTick),
                EquipCommand equip => _equipment.Handle(equip, WorldTick),
                UnequipCommand unequip => _equipment.Handle(unequip, WorldTick),
                AttackCommand attack => _combat.Handle(attack, WorldTick),
                TakedownCommand takedown => _combat.Handle(takedown, WorldTick),
                BlockCommand block => _combat.Handle(block, WorldTick),
                DodgeCommand dodge => _laden.Dodge(() => _combat.Handle(dodge, WorldTick)),
                CastCommand cast => _combat.Handle(cast, WorldTick),
                TechniqueCommand technique => _combat.Handle(technique, WorldTick),
                LearnFromTeacherCommand learn => _teaching.Handle(learn, WorldTick),
                GatherCommand gather => _gathering.Handle(gather, WorldTick),
                CraftCommand craft => _crafting.Handle(craft, WorldTick),
                SalvageCommand salvage => _crafting.Handle(salvage, WorldTick),
                ExperimentCommand experiment => _crafting.Handle(experiment, WorldTick),
                UseItemCommand use => _inventory.Handle(use, WorldTick),
                TalkCommand talk => _dialogue.Handle(talk, WorldTick),
                ChooseCommand choose => _dialogue.Handle(choose, WorldTick),
                LeaveCommand leave => _dialogue.Handle(leave, WorldTick),
                BuyCommand buy => _trade.Handle(buy, WorldTick),
                SellCommand sell => _trade.Handle(sell, WorldTick),
                OrderCompanionCommand order => _companions.Handle(order, WorldTick),
                ReviveCommand revive => _companions.Handle(revive, WorldTick),
                TameCommand tame => _pets.Handle(tame, WorldTick),
                AdoptPetCommand adopt => _pets.Handle(adopt, WorldTick),
                OrderPetCommand petOrder => _pets.Handle(petOrder, WorldTick),
                FeedPetCommand feed => _pets.Handle(feed, WorldTick),
                HireCommand hire => _companions.Handle(hire, WorldTick),
                DismissCommand dismiss => _companions.Handle(dismiss, WorldTick),
                SetStanceCommand stance => _companions.Handle(stance, WorldTick),
                SetTacticsCommand tactics => _companions.Handle(tactics, WorldTick),
                AssignWorkCommand work => _companions.Handle(work, WorldTick),
                SupplyHirelingCommand supply => _companions.Handle(supply, WorldTick),
                GiveGearCommand give => _sheets.Handle(give, WorldTick),
                TakeBackGearCommand takeBack => _sheets.Handle(takeBack, WorldTick),
                TransferPartyItemCommand transferParty=>_sheets.Handle(transferParty,WorldTick),
                EquipPartyPackCommand equipParty=>_sheets.Handle(equipParty,WorldTick),
                UnequipPartyGearCommand unequipParty=>_sheets.Handle(unequipParty,WorldTick),
                SpendPartyAttributeCommand spendParty => _sheets.Handle(spendParty, WorldTick),
                TouchStoneCommand touch => _travel.Handle(touch, WorldTick),
                BindStoneCommand bind => _travel.Handle(bind, WorldTick),
                TravelCommand travel => _travel.Handle(travel, WorldTick),
                BeginStoryCommand begin => _quests.Handle(begin, WorldTick),
                ClaimBountyCommand claim => _quests.Handle(claim, WorldTick),
                AssignQuickSlotCommand assign => _quickBar.Handle(assign, WorldTick),
                SaveGearSetCommand save => _gearSets.Handle(save, WorldTick),
                DeleteGearSetCommand delete => _gearSets.Handle(delete, WorldTick),
                SwapGearSetCommand swap => _gearSets.Handle(swap, WorldTick),
                CancelGearSwapCommand cancel => _gearSets.Handle(cancel, WorldTick),
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
            _people.Tick(tick);
            _tiers.Tick(tick);
            _weather.Tick(tick);
            foreach (var simulation in _tierSimulations)
                simulation.Tick(tick, _cells.Where(c => _state.Tiers.GetValueOrDefault(c.ToString(), SimulationTier.D) == simulation.Tier).ToList());
            _combat.Tick(tick);
            _laden.Tick(tick);
            _creatures.Tick(tick);
            _companions.Tick(tick);
            _pets.Tick(tick);
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
            _gearSets.Tick(tick);
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
            // A respawn reads party strength. Advance the other systems to that
            // instant first, rather than letting a month-long companion expiry
            // resize an animal that respawned on day two. Pet recovery is another
            // boundary because it changes the party seen by a later respawn.
            long cursor = from;
            while (cursor < to)
            {
                long boundary = to;
                foreach (var creature in _state.Creatures.Values)
                    if (creature.Site.Space.IsExterior && !creature.Alive && creature.RespawnTick > 0)
                        boundary = Math.Min(boundary, Math.Max(cursor + 1, creature.RespawnTick));
                foreach (var pet in _state.Pets.Values)
                    if (!pet.Up && pet.RecoversAtTick > 0)
                        boundary = Math.Min(boundary, Math.Max(cursor + 1, pet.RecoversAtTick));
                AbstractAdvance.Run(cursor, boundary, _advances, log);
                cursor = boundary;
            }
            _chat.AfterAbsence(log);
        }
        finally
        {
            _advancing = false;
        }
        return log;
    }

    /// <summary>
    /// Paid inn rest restores the character without advancing the shared world (DN8; owner ruling 2026-10-24).
    /// </summary>
    private string? RentRoom(RentRoom command)
    {
        if (_state.World.NpcLife is not { } lives || !lives.Npcs.TryGetValue(command.KeeperId, out var lived)
            || !Setup.NpcLife.Routines.TryGetValue(lived.RoutineId, out var routine) || routine.Settlement is not { } settlement)
            return "no one keeps a room here";
        if (!Setup.Adventurers.Settlements.TryGetValue(settlement, out var town) || town.Inn is not { } inn)
            return "there is no room to let here";
        if (inn.KeeperId != command.KeeperId)
            return "no one keeps a room here";
        if (_state.PlayerCombat.Defeated || _combat.View().InCombat)
            return "you cannot rest while fighting or downed";
        if (inn.RoomCoin > _state.Currency)
            return $"not enough coin: the room is {inn.RoomCoin}, {_state.Currency} carried";
        if (Dispatch(new SpendCurrency(inn.RoomCoin)) is { } refused)
            return refused;
        Dispatch(new Heal(PlayerId, "inn rest", _combat.MaxHealth()));
        Dispatch(new ChangePools(0, _combat.MaxStamina()));
        _context.Events.Publish(new RoomRented(command.KeeperId,
            _state.Npcs.TryGetValue(command.KeeperId, out var keeper) ? keeper.Definition.Name : command.KeeperId,
            settlement, inn.RoomCoin, WorldTick));
        return null;
    }

    /// <summary>
    /// The login grace (Â§2.12 item 6): for config.time's <c>login_grace_seconds</c> from now, creatures neither see nor hear the character,
    /// so nothing that came back while they were away is upon them before the world is on screen. Not saved: every login starts its own.
    /// </summary>
    public void BeginLoginGrace() => _creatures.BeginGrace(WorldTick + Setup.Offline.LoginGraceTicks);

    /// <summary>The last tick of the login grace; at or before the world tick when there is none.</summary>
    public long LoginGraceUntil => _creatures.GraceUntil;

    /// <summary>The player as a save records them: identity from the loaded record, everything else from live state.</summary>
    public PlayerRecord CaptureRecord()
    {
        // A save made while the character lies dead is the save of their respawn: it loads at the return point. One made while they lie
        // downed is saved as it is, at no health where they lie, and loads as the death (DN15: logging out can never escape one).
        bool dead = _state.PlayerCombat.Defeated && _state.PlayerCombat.DownedUntil == 0;
        var body = dead ? _travel.ReturnBody() : _state.Body;
        return new PlayerRecord(_identity.Id, _identity.Name, body.XMm, body.YMm, body.ZMm, _identity.AppearanceSeed, _state.Inventory,
            _state.Progression, body.FacingMdeg, _state.Discoveries.Values, _state.Equipment, _state.Currency,
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
            Crossing = _state.Crossing,
            // From LIVE state, not from _identity: a body moves between spaces during a run and its identity does
            // not, so a save that read these from the identity would record the exterior for a body standing inside.
            SpaceId = _state.Space.Name ?? "",
            SpaceTraversal = _state.Traversal.IsNone ? null : _state.Traversal.PortalKey,
            Factions = _state.Factions,
            Party = _companions.Ledger(),
            QuickBar = _state.QuickBar,
            QuickBarCleared = _state.QuickBarCleared.ToImmutableArray(),
            ExtraBars = _state.ExtraBars,
            HeldFamily = _state.HeldFamily,
            FamilyBars = _state.FamilyBars,
            GearSets = _state.GearSets,
            Bags = _state.Bags,
            PartySheets = _sheets.Records(),
            Attack = _combat.AttackUnderWay(),
            Findings = _state.Findings,
            Lore = _state.Lore,
            Pets = _pets.Records(),
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
        UnlockLore unlock => _lore.Handle(unlock, Now),
        ReadLore read => _lore.Handle(read, Now),
        TellLore tellLore => _lore.Handle(tellLore, Now),
        TellOfPlace told => _discovery.Handle(told, Now),
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
        DrawCreature draw => _creatures.Handle(draw, Now),
        TakeCreature take => _creatures.Handle(take, Now),
        RouseCreature rouse => _creatures.Handle(rouse, Now),
        ForgetPlayer forget => _creatures.Handle(forget),
        CorpseEmptied emptied => _creatures.Handle(emptied, Now),
        DiscardContainer discard => _inventory.Handle(discard),
        ConsumeItem consume => _inventory.Handle(consume, Now),
        ExchangeItems exchange => _inventory.Handle(exchange, Now),
        ExchangeItemBatch batch => _inventory.Handle(batch,Now),
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
        ForgetRelationships forget => _relationships.Handle(forget, Now),
        StartQuest start => _quests.Handle(start, Now),
        RecordDeed deed => _deeds.Handle(deed) ?? _quests.Handle(deed),
        Recruit recruit => _companions.Handle(recruit, Now),
        OrderCompanion order => _companions.Handle(order, Now),
        CompanionStruck struck => _companions.Handle(struck, Now),
        StrikeAfterimage ghost => ghost.Of == PlayerId ? _people.Handle(ghost, Now) : _companions.Handle(ghost, Now),
        PlaceNpc place => _npcs.Handle(place),
        RemoveNpc remove => _npcs.Handle(remove),
        DepartNpc depart => _npcs.Handle(depart, Now),
        InjureNpc injure => _npcs.Handle(injure),
        HandToParty hand => _npcs.Handle(hand, Now),
        TakeBackFromParty back => _npcs.Handle(back, Now),
        Hire hire => _companions.Handle(hire, Now),
        Dismiss dismiss => _companions.Handle(dismiss, Now),
        RefitMember refit => _companions.Handle(refit),
        LeaveParty leave => _companions.Handle(leave, Now),
        PracticePartySkill practice => _sheets.Handle(practice),
        ShareKill share => _sheets.Handle(share),
        ReturnLoans loans => _sheets.Handle(loans, Now),
        SettleFallen fallen => _sheets.Handle(fallen, Now),
        HandOver hand => _inventory.Handle(hand),
        CheckEquipmentPack equipment=>_inventory.Handle(equipment),
        RepackInventory repack=>_inventory.Handle(repack),
        CanReceiveStack receive=>_inventory.Handle(receive),
        ReceiveStack receive => _inventory.Handle(receive, Now),
        ReleaseItem release => _equipment.Handle(release, Now),
        ArriveWithParty arrive => _companions.Handle(arrive),
        RestageEncounterParty restage => _companions.Handle(restage),
        RecordAct act => _factions.Handle(act, Now),
        ReportAct report => _factions.Handle(report, Now),
        GrantStanding grant => _factions.Handle(grant, Now),
        Teach teach => _teaching.Handle(teach, Now),
        ClaimBounty claim => _quests.Handle(claim),
        CullCreatures cull => _creatures.Handle(cull),
        AdoptSheet adopt => _sheets.Handle(adopt),
        ReleaseSheet release => _sheets.Handle(release),
        EmbodyNpc embody => _npcs.Handle(embody),
                SiteNpc site => _npcs.Handle(site),
        SettleRetiree retiree => _npcs.Handle(retiree),
        AdventurerPartyChanged changed => _adventurers.Handle(changed),
        ShareBounty share => _adventurers.Handle(share, Now),
        RentRoom rent => RentRoom(rent),
        _ => throw new InvalidOperationException($"No system handles {command.GetType().Name}"),
    };
}
