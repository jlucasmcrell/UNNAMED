// UNNAMED World - the running simulation: composition root, command queue, fixed tick (ARCHITECTURE.md §4.5, §7, §8.1)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

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

    /// <summary>The navigation lattice and its limits (M7; D-13).</summary>
    public NavConfig Navigation { get; init; } = NavConfig.Default;

    /// <summary>Building (M7): the pieces and building's numbers.</summary>
    public BuildingSetup Building { get; init; } = BuildingSetup.Empty;

    /// <summary>The factions, the standing ladder and the act log's capacity (M7).</summary>
    public FactionSetup Factions { get; init; } = FactionSetup.Empty;
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
    int Armor);

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
    private readonly NavigationSystem _navigation;
    private readonly BuildingSystem _building;
    private readonly NpcSystem _npcs;
    private readonly RelationshipSystem _relationships;
    private readonly FactionSystem _factions;
    private readonly DialogueSystem _dialogue;
    private readonly TradeSystem _trade;
    private readonly QuestSystem _quests;
    private readonly QuestDebugger _debugger;
    private readonly CompanionSystem _companions;
    private readonly ImmutableArray<ITierSimulation> _tierSimulations;
    private long _sequence;
    private bool _stepping;
    private NavScratch? _previewScratch;

    private Simulation(SimulationSetup setup, PlayerRecord player, WorldDelta world, long worldTick, IEventBus events)
    {
        Setup = setup;
        _identity = player;
        _cells = setup.Layout.CellKeys.Select(CellKey.Parse).OrderBy(c => c).ToImmutableArray();
        _state = new RuntimeState(world, worldTick, new Body(player.XMm, player.YMm, player.ZMm, player.FacingMdeg), player);
        _context = new SystemContext(_state, setup, events, Dispatch);
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
        _effects = new StatusEffectSystem(_context, _state.Claim(nameof(StatusEffectSystem), StateSlice.Effects));
        _death = new DeathSystem(_context, player.Id);
        _gathering = new GatheringSystem(_context, _state.Claim(nameof(GatheringSystem), StateSlice.Nodes), player.Id);
        _crafting = new CraftingSystem(_context, player.Id);
        _navigation = new NavigationSystem(_context, _state.Claim(nameof(NavigationSystem), StateSlice.Navigation));
        _building = new BuildingSystem(_context, _state.Claim(nameof(BuildingSystem), StateSlice.Structures), player.Id, _navigation);
        _npcs = new NpcSystem(_context, _state.Claim(nameof(NpcSystem), StateSlice.Npcs));
        _relationships = new RelationshipSystem(_context, _state.Claim(nameof(RelationshipSystem), StateSlice.Relationships));
        _factions = new FactionSystem(_context, _state.Claim(nameof(FactionSystem), StateSlice.Factions));
        _dialogue = new DialogueSystem(_context, _state.Claim(nameof(DialogueSystem), StateSlice.Conversations), player.Id);
        _trade = new TradeSystem(_context, player.Id, _inventory.View);
        _quests = new QuestSystem(_context, _state.Claim(nameof(QuestSystem), StateSlice.Quests));
        _companions = new CompanionSystem(_context, _state.Claim(nameof(CompanionSystem), StateSlice.Companions), player.Id, player.Companions, _navigation);
        _debugger = new QuestDebugger(_context, _quests, _dialogue, _gathering.Views, _trade.View, () => Containers, _creatures.Views, _inventory.WorldItems);
        _tierSimulations = ImmutableArray.Create<ITierSimulation>(new StubTierSimulation(SimulationTier.B), new StubTierSimulation(SimulationTier.C));
        _state.RequireEverySliceOwned();
        _effects.Seed(player.Id, player.Effects);
        _building.Populate();
        _navigation.Build();
        _npcs.Populate();
        _companions.Populate();
        _creatures.Populate();
        _tiers.Settle();
    }

    /// <summary>Start simulating a world: a new game's, or one a save just loaded. The world tick continues from the save.</summary>
    public static Simulation Start(SimulationSetup setup, PlayerRecord player, WorldDelta world, long worldTick, IEventBus events)
    {
        if (worldTick < 0)
            throw new ArgumentOutOfRangeException(nameof(worldTick), worldTick, "The world tick is never negative");
        return new Simulation(setup, player, world, worldTick, events);
    }

    /// <summary>
    /// A new character standing at the region's spawn point, with the starting package (PROGRESSION.md §5) and the
    /// starting kit (PROTOTYPE.md §5 step 1: the sword equipped).
    /// </summary>
    public static PlayerRecord NewCharacter(SimulationSetup setup, EntityId id, string name, ulong appearanceSeed)
    {
        var spawn = setup.Layout.Spawn;
        var kit = setup.Items.StartingKit.Select(item => (Item: item, Entry: new InventoryEntry(EntityId.NewId(EntityKind.Item), item.ItemId, item.Count))).ToList();
        var equipment = kit.Where(k => k.Item.Equip)
            .Select(k => KeyValuePair.Create(setup.Items.Catalog.Get(k.Item.ItemId).Slot!.Value, k.Entry.ItemId));
        return new PlayerRecord(id, name, spawn.XMm, spawn.YMm, spawn.ZMm, appearanceSeed, kit.Select(k => k.Entry),
            ProgressionEngine.Create(setup.Progression), spawn.FacingMdeg, equipment: equipment);
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
        Setup.Items.Inventory.CarryLimitGrams(_state.Progression, Setup.Progression), _equipment.Armor());

    /// <summary>Every authored container and what it holds now.</summary>
    public ImmutableArray<ContainerView> Containers => Setup.Layout.Containers.Concat(_context.CorpseSites()).Select(_inventory.View).ToImmutableArray();

    /// <summary>Items lying in the region.</summary>
    public ImmutableArray<WorldItemView> WorldItems => _inventory.WorldItems();

    public ImmutableArray<DoorView> Doors => Setup.Layout.Doors.Select(d => new DoorView(d, _context.IsOpen(d))).ToImmutableArray();

    public ImmutableArray<SwitchView> Switches => Setup.Layout.Switches.Select(s => new SwitchView(s, _context.IsSet(s))).ToImmutableArray();

    public ImmutableArray<BarrierView> Barriers => Setup.Layout.Barriers.Select(b => new BarrierView(b, !_context.IsLifted(b))).ToImmutableArray();

    /// <summary>The player in combat: phase, guard, weapon, pools and effects.</summary>
    public CombatView Combat => _combat.View();

    /// <summary>The region's resource nodes, and which can be harvested now (M3f).</summary>
    public ImmutableArray<NodeView> Nodes => _gathering.Views();

    /// <summary>Every creature the region holds, living or dead.</summary>
    public ImmutableArray<CreatureView> Creatures => _creatures.Views();

    /// <summary>The region's named NPCs (M4).</summary>
    public ImmutableArray<NpcView> Npcs => _npcs.Views();

    /// <summary>The character's companions (M6).</summary>
    public ImmutableArray<CompanionView> Companions => _companions.Views();

    /// <summary>The conversation open now, if any (M4).</summary>
    public ConversationView? Conversation => _dialogue.View();

    /// <summary>A trader's wares at their prices; null when that NPC does not trade (M4).</summary>
    public WaresView? Wares(string npcId) => _trade.View(npcId);

    /// <summary>The quests the character has started, as the journal shows them: active first (M5).</summary>
    public ImmutableArray<QuestView> Quests => _quests.Views();

    /// <summary>The quest debugger (M5): what a quest is waiting on right now, every term's value, and its recent trace.</summary>
    public QuestDiagnosis Diagnose(string questId) => _debugger.Diagnose(questId);

    public ImmutableSortedDictionary<string, SimulationTier> CellTiers => _state.Tiers;

    /// <summary>Navigation (M7): the grid, the gates and their state, the movers' routes and the work counts. Read-only.</summary>
    public NavigationView Navigation => _navigation.View();

    /// <summary>Every placed piece, by ID (M7).</summary>
    public ImmutableArray<PieceView> Pieces => _building.Views();

    /// <summary>Where bodies move (M7): the authored space and every placed piece's solid parts.</summary>
    public WalkSpace Space => _context.Space;

    /// <summary>The structure sequence (M7): one more on every place and take-down.</summary>
    public long StructureRevision => _state.World.StructureSequence;

    /// <summary>What the load found wrong with the saved pieces, kept and reported (M7).</summary>
    public ImmutableArray<StructureConflict> StructureAudit => _state.StructureAudit;

    /// <summary>Every placed part as navigation reads it (M7).</summary>
    public ImmutableArray<NavFootprint> StructureFootprints => _context.StructureFootprints;

    /// <summary>
    /// The placement ghost (M7 design §4.6): what placing this piece here would do, by the command's own rules on a scratch of its own.
    /// Advisory and read-only - it dispatches, publishes, counts and writes nothing.
    /// </summary>
    public PlacementPreview PreviewPlacement(string pieceDefId, long xMm, long zMm, int rotation, bool checkNavigability)
    {
        _previewScratch ??= new NavScratch();
        var check = BuildingRules.Validate(new PlacementContext(_context, _navigation, _identity.Id, _identity.Id, _previewScratch, null, checkNavigability),
            pieceDefId, xMm, zMm, rotation);
        return BuildingRules.Preview(_context, check, pieceDefId);
    }

    /// <summary>Every faction as the character stands with it, in ordinal ID order (M7).</summary>
    public ImmutableArray<FactionView> Factions => _factions.Views();

    /// <summary>The character's act log, with what each faction knows of each act (M7).</summary>
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
                PlacePieceCommand place => _building.Handle(place, WorldTick),
                DismantlePieceCommand dismantle => _building.Handle(dismantle, WorldTick),
                MoveItemCommand item => _inventory.Handle(item, WorldTick),
                EquipCommand equip => _equipment.Handle(equip, WorldTick),
                UnequipCommand unequip => _equipment.Handle(unequip, WorldTick),
                AttackCommand attack => _combat.Handle(attack, WorldTick),
                BlockCommand block => _combat.Handle(block, WorldTick),
                DodgeCommand dodge => _combat.Handle(dodge, WorldTick),
                CastCommand cast => _combat.Handle(cast, WorldTick),
                GatherCommand gather => _gathering.Handle(gather, WorldTick),
                CraftCommand craft => _crafting.Handle(craft, WorldTick),
                UseItemCommand use => _inventory.Handle(use, WorldTick),
                TalkCommand talk => _dialogue.Handle(talk, WorldTick),
                ChooseCommand choose => _dialogue.Handle(choose, WorldTick),
                LeaveCommand leave => _dialogue.Handle(leave, WorldTick),
                BuyCommand buy => _trade.Handle(buy, WorldTick),
                SellCommand sell => _trade.Handle(sell, WorldTick),
                OrderCompanionCommand order => _companions.Handle(order, WorldTick),
                ReviveCommand revive => _companions.Handle(revive, WorldTick),
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
    /// Advance one fixed tick. The order is data, fixed here: movement, tiers, the tier simulations, the player's combat,
    /// creatures, companions, NPCs, conversations, status effects, death, discovery, quests (which read what all of that did), clock.
    /// </summary>
    public void Step()
    {
        if (_stepping)
            throw new InvalidOperationException("Step is not re-entrant: an event handler may enqueue commands, never step the world");
        _stepping = true;
        try
        {
            long tick = WorldTick + 1;
            _movement.Tick(tick);
            _tiers.Tick(tick);
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
            _clock.Tick();
        }
        finally
        {
            _stepping = false;
        }
    }

    /// <summary>The player as a save records them: identity from the loaded record, everything else from live state.</summary>
    public PlayerRecord CaptureRecord()
    {
        var body = _state.Body;
        return new PlayerRecord(_identity.Id, _identity.Name, body.XMm, body.YMm, body.ZMm, _identity.AppearanceSeed, _state.Inventory,
            _state.Progression, body.FacingMdeg, _state.Discoveries.Values, _state.Equipment, _state.Currency,
            _state.Effects.GetValueOrDefault(_identity.Id, ImmutableArray<ActiveEffect>.Empty),
            _state.Relationships.SelectMany(n => n.Value.Select(d => new RelationshipValue(n.Key, d.Key, d.Value))),
            _state.Conversations.Select(c => new ConversationMemory(c.Key, c.Value.ToImmutableArray())),
            _state.Quests.Values, _companions.Records()) { Posture = _state.Posture, Factions = _state.Factions };
    }

    /// <summary>
    /// A digest of every piece of persistent state: the tick, the whole player record, and the effective state of
    /// every cell of the region. Two simulations are in the same persistent state exactly when these match.
    /// </summary>
    public string StateDigest()
    {
        using var h = new CanonicalHasher();
        h.Add("unnamed.simulation/v3").Add(WorldTick).Add(CaptureRecord().Digest).Add(World.StructureSequence).Add(_cells.Length);
        foreach (var cell in _cells)
            h.Add(cell.ToString()).Add(World.EffectiveCellDigest(cell));
        h.Add(World.Noises.Length);
        foreach (var noise in World.Noises)
            h.Add(noise.XMm).Add(noise.ZMm).Add(noise.RadiusMm).Add(noise.Call).Add(noise.CallerKind ?? "-");
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
        AddCurrency add => _inventory.Handle(add),
        GrantItem grant => _inventory.Handle(grant, Now),
        ChangeRelationship change => _relationships.Handle(change, Now),
        StartQuest start => _quests.Handle(start, Now),
        RecordDeed deed => _quests.Handle(deed),
        Recruit recruit => _companions.Handle(recruit, Now),
        OrderCompanion order => _companions.Handle(order, Now),
        CompanionStruck struck => _companions.Handle(struck, Now),
        PlaceNpc place => _npcs.Handle(place),
        RecordAct act => _factions.Handle(act, Now),
        ReportAct report => _factions.Handle(report, Now),
        OpenDoor open => _interaction.Handle(open, Now),
        _ => throw new InvalidOperationException($"No system handles {command.GetType().Name}"),
    };
}
