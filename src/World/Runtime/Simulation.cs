// UNNAMED World - the running simulation: composition root, command queue, fixed tick (ARCHITECTURE.md §4.5, §7, §8.1)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>The rules a simulation runs under, built from content at boot.</summary>
public sealed record SimulationSetup(RegionLayout Layout, MovementRules Movement, ProgressionRules Progression, TierRules Tiers, int TickMilliseconds)
{
    /// <summary>Items, loot tables, carrying rules and the starting kit (M3b).</summary>
    public ItemSetup Items { get; init; } = ItemSetup.Empty;
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
    private readonly ImmutableArray<ITierSimulation> _tierSimulations;
    private long _sequence;
    private bool _stepping;

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
        _tierSimulations = ImmutableArray.Create<ITierSimulation>(new StubTierSimulation(SimulationTier.B), new StubTierSimulation(SimulationTier.C));
        _state.RequireEverySliceOwned();
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

    public SimulationSetup Setup { get; }

    public EntityId PlayerId => _identity.Id;

    /// <summary>Completed ticks: <c>world_tick</c>.</summary>
    public long WorldTick => _state.WorldTick;

    /// <summary>The sparse world delta. Its public members only read (tests/Architecture.Tests).</summary>
    public WorldDelta World => _state.World;

    public PlayerView Player => new(_identity.Id, _identity.Name, _state.Body, _movement.Intent, _state.Progression,
        ProgressionEngine.Derive(_state.Progression, Setup.Progression), _state.Discoveries.Values.ToImmutableArray(),
        _state.Inventory, _state.Equipment, _state.Currency, _inventory.CarriedGrams(),
        Setup.Items.Inventory.CarryLimitGrams(_state.Progression, Setup.Progression), _equipment.Armor());

    /// <summary>Every authored container and what it holds now.</summary>
    public ImmutableArray<ContainerView> Containers => Setup.Layout.Containers.Select(_inventory.View).ToImmutableArray();

    /// <summary>Items lying in the region.</summary>
    public ImmutableArray<WorldItemView> WorldItems => _inventory.WorldItems();

    public ImmutableArray<DoorView> Doors => Setup.Layout.Doors.Select(d => new DoorView(d, _context.IsOpen(d))).ToImmutableArray();

    public ImmutableSortedDictionary<string, SimulationTier> CellTiers => _state.Tiers;

    /// <summary>The footprints that currently block movement besides the static ones: closed doors. Prediction needs them.</summary>
    public ImmutableArray<Blocker> DynamicBlockers => _context.ClosedDoors();

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
                InteractCommand interact => _interaction.Handle(interact, WorldTick),
                MoveItemCommand item => _inventory.Handle(item, WorldTick),
                EquipCommand equip => _equipment.Handle(equip, WorldTick),
                UnequipCommand unequip => _equipment.Handle(unequip, WorldTick),
                _ => $"no system handles {command.GetType().Name}",
            };
            _log.Add(new LoggedCommand(WorldTick, _sequence++, command, rejected));
            if (rejected is not null)
                _context.Events.Publish(new CommandRejected(command, rejected, WorldTick));
            applied++;
        }
        return applied;
    }

    /// <summary>Advance one fixed tick. The order is data, fixed here: movement, tiers, the tier simulations, discovery, clock.</summary>
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
            _discovery.Tick(tick);
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
            _state.Progression, body.FacingMdeg, _state.Discoveries.Values, _state.Equipment, _state.Currency);
    }

    /// <summary>
    /// A digest of every piece of persistent state: the tick, the whole player record, and the effective state of
    /// every cell of the region. Two simulations are in the same persistent state exactly when these match.
    /// </summary>
    public string StateDigest()
    {
        using var h = new CanonicalHasher();
        h.Add("unnamed.simulation/v1").Add(WorldTick).Add(CaptureRecord().Digest).Add(_cells.Length);
        foreach (var cell in _cells)
            h.Add(cell.ToString()).Add(World.EffectiveCellDigest(cell));
        return h.Finish();
    }

    private string? Dispatch(InternalCommand command) => command switch
    {
        SetWorldFlag set => _flags.Handle(set, _stepping ? WorldTick + 1 : WorldTick),
        AwardExperience award => _progression.Handle(award),
        _ => throw new InvalidOperationException($"No system handles {command.GetType().Name}"),
    };
}
