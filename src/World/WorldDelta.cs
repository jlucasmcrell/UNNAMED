// UNNAMED World - the sparse world delta (D-05, PERSISTENCE.md §1.1-§1.2, §5.2, §5.3, §5.6)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Spatial;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.World;

/// <summary>A harvested resource node (WORLD_ARCHITECTURE.md §5.4). Absent means available: the baseline.</summary>
public sealed record NodeHarvest(string NodeKey, long LastHarvestTick, int HarvestSeq);

/// <summary>
/// One diverged cell, as persisted in <c>cells.msgpack</c> (PERSISTENCE.md §5.2). Every collection is
/// sorted by key and holds only non-baseline values, so equal worlds serialize to equal bytes.
/// <see cref="BaselineHash"/> names the exact baseline the delta was made against (M2b).
/// </summary>
public sealed record CellDeltaRecord(
    string CellKey,
    string BaselineHash,
    ImmutableArray<string> DirtyReasons,
    ImmutableArray<KeyValuePair<string, long>> Flags,
    ImmutableArray<NodeHarvest> HarvestedNodes,
    ImmutableArray<KeyValuePair<string, int>> PopulationAlive);

/// <summary>
/// One diverged entity, as persisted in <c>entities.msgpack</c> (PERSISTENCE.md §5.3). It replaces the
/// baseline slot it names on load. Null state fields are unchanged from the baseline; only the
/// diverged fields are stored. <see cref="BaselineHash"/> is the host cell's baseline hash: the
/// slot is part of that baseline, so the record is proven against it exactly as a cell record is.
/// </summary>
public sealed record EntityDeltaRecord(
    EntityId InstanceId,
    string SlotKey,
    int GenerationSeq,
    string DefId,
    bool? Alive,
    int? XCm,
    int? ZCm,
    string? BaselineHash = null)
{
    public const int DirtyAlive = 1;
    public const int DirtyPosition = 2;

    public int DirtyMask => (Alive is null ? 0 : DirtyAlive) | (XCm is null ? 0 : DirtyPosition);
}

/// <summary>
/// A persistent instance that no baseline slot generates - a dropped item, a placed chest (M2b §6
/// "player-created instance added", PERSISTENCE.md §5.6). It is persisted whole, keyed by its ULID and
/// anchored to its host cell, and proven against that cell's baseline like a slot record: a changed
/// host cell needs a registered transition before the instance is placed in it again.
/// </summary>
public sealed record CreatedEntityRecord(EntityId InstanceId, string DefId, string HostCell, int XCm, int ZCm, string? BaselineHash = null)
{
    /// <summary>How many: a dropped stack keeps its count (schema 6; older saves held single items).</summary>
    public int Count { get; init; } = 1;

    /// <summary>The stack's quality (schema 9).</summary>
    public int Quality { get; init; }
}

/// <summary>An item inside a world container.</summary>
public sealed record ContainerItem(EntityId ItemId, string DefId, int Count)
{
    /// <summary>The stack's quality (schema 9).</summary>
    public int Quality { get; init; }
}

/// <summary>
/// An authored world container whose contents changed (SYSTEMS.md S-14: "contents of world containers that changed from
/// baseline"). Until it changes, a container's contents are its content-defined baseline and nothing is saved; the first
/// change gives it and everything in it an identity, and from then on the record holds its whole contents. It is proven
/// against its host cell's baseline like a created instance.
/// </summary>
public sealed record ContainerRecord(string Key, EntityId InstanceId, string HostCell, ImmutableArray<ContainerItem> Items, string? BaselineHash = null);

public enum CreatureCondition
{
    Alive,

    /// <summary>Dead, and its body lies where it fell until it is looted empty or its spawner brings it back.</summary>
    Corpse,

    /// <summary>Dead and gone: looted, or never to return.</summary>
    Gone,
}

/// <summary>What a creature is doing about what it knows (SYSTEMS.md S-23's decision state; saved from schema 8).</summary>
public enum CreatureMind
{
    /// <summary>Knows of nothing: holds, wanders, patrols or sleeps, as its role says.</summary>
    Unaware,

    /// <summary>Saw or heard something: goes to look.</summary>
    Suspicious,

    /// <summary>Has found its target and fights it.</summary>
    Engaged,

    /// <summary>Lost its target: searches where it was last known.</summary>
    Searching,

    /// <summary>Gave up, or was called home by its leash.</summary>
    Returning,

    /// <summary>Too hurt to fight on (STEALTH_DETECTION_AND_THREAT.md §16).</summary>
    Fleeing,
}

public static class CreatureMinds
{
    public static string Key(CreatureMind mind) => mind switch
    {
        CreatureMind.Unaware => "unaware",
        CreatureMind.Suspicious => "suspicious",
        CreatureMind.Engaged => "engaged",
        CreatureMind.Searching => "searching",
        CreatureMind.Returning => "returning",
        CreatureMind.Fleeing => "fleeing",
        _ => throw new ArgumentOutOfRangeException(nameof(mind), mind, "Unknown creature mind"),
    };

    public static CreatureMind Parse(string key)
    {
        foreach (var mind in Enum.GetValues<CreatureMind>())
        {
            if (Key(mind) == key)
                return mind;
        }
        throw new FormatException($"Unknown creature mind '{key}'");
    }
}

public static class CreatureConditions
{
    public static string Key(CreatureCondition condition) => condition switch
    {
        CreatureCondition.Alive => "alive",
        CreatureCondition.Corpse => "corpse",
        CreatureCondition.Gone => "gone",
        _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown creature condition"),
    };

    public static CreatureCondition Parse(string key) => key switch
    {
        "alive" => CreatureCondition.Alive,
        "corpse" => CreatureCondition.Corpse,
        "gone" => CreatureCondition.Gone,
        _ => throw new FormatException($"Unknown creature condition '{key}'"),
    };
}

/// <summary>
/// A spawner's creature that has left its baseline - alive where it was placed, whole, unaware, first of its line
/// (SYSTEMS.md S-31, M3d). Until then nothing is saved; from then on the record holds where it is, how hurt, whether it
/// lives, when its spawner brings it back, and what it knows: its mind, its awareness, where its target was last known
/// (S-23's durable divergence - "a target an actor still hunts"). <see cref="Key"/> is its stable name,
/// <c>spawner#member</c>; <see cref="Generation"/> counts its respawns, and each generation has its own identity. It is
/// proven against its home cell's baseline.
/// </summary>
public sealed record CreatureRecord(
    string Key,
    string DefId,
    EntityId InstanceId,
    string HostCell,
    int Generation,
    CreatureCondition Condition,
    long XMm,
    long ZMm,
    int FacingMdeg,
    int Health,
    long DiedTick,
    long RespawnTick,
    string? BaselineHash = null)
{
    public CreatureMind Mind { get; init; }
    public int Awareness { get; init; }
    public bool Knows { get; init; }
    public long KnownXMm { get; init; }
    public long KnownZMm { get; init; }
    public long LastSeenTick { get; init; }
    public long SearchUntil { get; init; }
    public bool HasCalled { get; init; }

    // What its next ticks depend on beyond its body and mind (schema 14; the Phase-1 technical audit, L-06). Each is written only
    // while it still matters, so a creature over its charge, stagger or stun has the record of one that never had them.

    /// <summary>The first tick it may charge again; 0 when it already may.</summary>
    public long NextChargeTick { get; init; }

    /// <summary>The first tick a blow may stagger it again; 0 when one already may.</summary>
    public long StaggerImmuneUntil { get; init; }

    /// <summary>The tick the stagger it is in began - a charger's stun among them - or null.</summary>
    public long? StaggeredTick { get; init; }

    /// <summary>How long that stagger lasts; 0 for the usual.</summary>
    public int StaggerLastsTicks { get; init; }

    // Its ordinary attack in progress (schema 17; the owner's ruling on the second M7 E8.5 STOP): what the uninterrupted attack's next
    // ticks depend on beyond its body. The blow is its definition's one ordinary attack, and whom it strikes is decided at the impact
    // tick, as it always was, so neither is stored. Written only while the attack still matters.

    /// <summary>The tick the attack it is in began, or null.</summary>
    public long? AttackTick { get; init; }

    /// <summary>Whom that attack has already landed on - the character or a companion - or null while it has not: a swing lands once.</summary>
    public EntityId? AttackStruck { get; init; }
}

/// <summary>
/// A player-placed building piece (M7): one row, anchored to its anchor's cell and proven against that cell's baseline. Its shape is its
/// definition's; only what the definition cannot know is stored. It exposes no computed public property (the <c>StateDump</c> rule).
/// </summary>
public sealed record PieceRecord(
    EntityId InstanceId,
    string DefId,
    string HostCell,
    long XMm,
    long ZMm,
    int Rotation,
    EntityId Owner,
    int HealthCurrent,
    string? BaselineHash = null)
{
    /// <summary>Whether a door piece stands open; false for every other piece.</summary>
    public bool DoorOpen { get; init; }
}

/// <summary>Where a named NPC on an errand is in it (M7): walking to work, at work, or walking home.</summary>
public enum NpcErrandPhase { ToWork, AtWork, ToHome }

public static class NpcErrandPhases
{
    public static string Key(NpcErrandPhase phase) => phase switch
    {
        NpcErrandPhase.ToWork => "to_work",
        NpcErrandPhase.AtWork => "at_work",
        NpcErrandPhase.ToHome => "to_home",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown errand phase"),
    };

    public static NpcErrandPhase Parse(string key) => key switch
    {
        "to_work" => NpcErrandPhase.ToWork,
        "at_work" => NpcErrandPhase.AtWork,
        "to_home" => NpcErrandPhase.ToHome,
        _ => throw new FormatException($"Unknown errand phase '{key}'"),
    };
}

/// <summary>
/// A named NPC away from their place (M7), anchored to the cell of the NPC's authored site, with absolute mm. Absent means the NPC stands
/// at their site: the baseline. <see cref="PieceId"/> is null exactly when walking home; <see cref="WorkOwner"/> is the piece's owner.
/// </summary>
public sealed record NpcErrandRecord(string NpcId, string HostCell, NpcErrandPhase Phase, EntityId? PieceId, EntityId? WorkOwner, long XMm, long ZMm,
    int FacingMdeg, string? BaselineHash = null)
{
    /// <summary>The route the NPC has committed to; required on every errand.</summary>
    public NavRoute Route { get; init; } = NavRoute.None;

    public int StuckTicks { get; init; }
}

/// <summary>What <see cref="WorldDelta.TakeSnapshot"/> captures: the whole persisted world delta.</summary>
public sealed record DeltaSnapshot(ImmutableArray<CellDeltaRecord> Cells, ImmutableArray<EntityDeltaRecord> Entities)
{
    public static DeltaSnapshot Empty { get; } = new(ImmutableArray<CellDeltaRecord>.Empty, ImmutableArray<EntityDeltaRecord>.Empty);

    /// <summary>Created instances, sorted by instance ID.</summary>
    public ImmutableArray<CreatedEntityRecord> Created { get; init; } = ImmutableArray<CreatedEntityRecord>.Empty;

    /// <summary>Changed world containers, sorted by key (schema 6).</summary>
    public ImmutableArray<ContainerRecord> Containers { get; init; } = ImmutableArray<ContainerRecord>.Empty;

    /// <summary>Spawners' creatures that left their baseline, sorted by key (schema 8).</summary>
    public ImmutableArray<CreatureRecord> Creatures { get; init; } = ImmutableArray<CreatureRecord>.Empty;

    /// <summary>The sounds made on the last tick - a blow, a howl - that creatures hear on the next, in the order made (schema 14).</summary>
    public ImmutableArray<Noise> Noises { get; init; } = ImmutableArray<Noise>.Empty;

    /// <summary>Player-placed pieces, sorted by instance ID (schema 15).</summary>
    public ImmutableArray<PieceRecord> Pieces { get; init; } = ImmutableArray<PieceRecord>.Empty;

    /// <summary>The structure sequence: every piece ID's ordinal, and the structure revision (schema 15).</summary>
    public long StructureSequence { get; init; }

    /// <summary>Named NPCs away from their site, sorted by NPC ID (schema 15).</summary>
    public ImmutableArray<NpcErrandRecord> NpcErrands { get; init; } = ImmutableArray<NpcErrandRecord>.Empty;
}

/// <summary>A record that failed post-load invariant validation and was dropped (PERSISTENCE.md §7.2).</summary>
public sealed record RejectedRecord(string Section, string Key, string Reason);

/// <summary>The effective state of one slot's occupant: baseline, or its persisted divergence.</summary>
public sealed record OccupantView(string SlotKey, EntityId? InstanceId, bool Alive, int XCm, int ZCm);

/// <summary>
/// The authoritative sparse world delta. Load = generate(baseline) + apply(delta); save = diff against
/// the regenerated baseline (PERSISTENCE.md §1.2). Nothing that equals the baseline is stored.
/// Reading is public; mutation is internal (ARCHITECTURE.md §5: nothing but the owning system writes
/// authoritative state). The systems that own node and spawn state receive write access when they
/// are built; until then only World and its tests can mutate a world.
/// </summary>
public sealed class WorldDelta
{
    private readonly Registry _registry;
    private readonly Dictionary<CellKey, CellBaseline> _baselines = new();
    private readonly Dictionary<CellKey, MutableCell> _cells = new();
    private readonly Dictionary<string, EntityDeltaRecord> _entities = new(StringComparer.Ordinal);
    private readonly Dictionary<EntityId, CreatedEntityRecord> _created = new();
    private readonly SortedDictionary<string, ContainerRecord> _containers = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, CreatureRecord> _creatures = new(StringComparer.Ordinal);
    private ImmutableArray<Noise> _noises = ImmutableArray<Noise>.Empty;
    private readonly SortedDictionary<string, PieceRecord> _pieces = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, NpcErrandRecord> _errands = new(StringComparer.Ordinal);
    private long _structureSequence;

    public WorldDelta(ICellBaselineGenerator generator, ulong worldSeed, Registry registry)
    {
        Generator = generator;
        WorldSeed = worldSeed;
        _registry = registry;
    }

    public ICellBaselineGenerator Generator { get; }

    public ulong WorldSeed { get; }

    /// <summary>The identity authority this world registers its instances with (D-10). Only the runtime reaches it.</summary>
    internal Registry Registry => _registry;

    /// <summary>The regenerated baseline. A transient cache: never persisted, always reproducible.</summary>
    public CellBaseline Baseline(CellKey cell)
    {
        if (!_baselines.TryGetValue(cell, out var baseline))
            _baselines[cell] = baseline = Generator.Generate(WorldSeed, cell);
        return baseline;
    }

    // ── world flags ─────────────────────────────────────────────────────────

    /// <summary>Set a cell-level world flag; 0 is the baseline and removes the entry.</summary>
    internal void SetFlag(CellKey cell, string flagId, long value)
    {
        if (!DefinitionId.IsValid(flagId) || !flagId.StartsWith("world.", StringComparison.Ordinal))
            throw new ArgumentException($"World flags are 'world.*' definition IDs (DATA_MODEL.md §1), got '{flagId}'", nameof(flagId));
        var state = Cell(cell);
        if (value == 0)
            state.Flags.Remove(flagId);
        else
            state.Flags[flagId] = value;
    }

    public long GetFlag(CellKey cell, string flagId) =>
        _cells.TryGetValue(cell, out var state) && state.Flags.TryGetValue(flagId, out long value) ? value : 0;

    // ── resource nodes ──────────────────────────────────────────────────────

    /// <summary>
    /// Record a harvest: the node's record holds its last harvest tick and how many harvests it has had (its sequence). A
    /// node with charges is harvested again by the next harvest (M3f); whether it may be is the gathering rules' call.
    /// </summary>
    internal void HarvestNode(CellKey cell, string nodeKey, long tick)
    {
        if (Baseline(cell).FindNode(nodeKey) is null)
            throw new InvalidOperationException($"Cell {cell} has no node '{nodeKey}'");
        var state = Cell(cell);
        state.Nodes[nodeKey] = new NodeHarvest(nodeKey, tick, state.NextHarvestSeq(nodeKey));
    }

    /// <summary>A node's harvest record, if it has one.</summary>
    public NodeHarvest? NodeRecord(CellKey cell, string nodeKey) =>
        _cells.TryGetValue(cell, out var state) ? state.Nodes.GetValueOrDefault(nodeKey) : null;

    /// <summary>Return a node to its baseline (available), which retires its record.</summary>
    internal void RegrowNode(CellKey cell, string nodeKey)
    {
        if (_cells.TryGetValue(cell, out var state))
            state.Nodes.Remove(nodeKey);
    }

    public bool IsHarvested(CellKey cell, string nodeKey) =>
        _cells.TryGetValue(cell, out var state) && state.Nodes.ContainsKey(nodeKey);

    // ── populations ─────────────────────────────────────────────────────────

    /// <summary>
    /// Set a population's alive count. The only persisted quantity for an undiverged population
    /// (WORLD_ARCHITECTURE.md §5.5); its target is the baseline and removes the entry.
    /// </summary>
    internal void SetPopulationAlive(CellKey cell, string populationId, int alive)
    {
        var population = Baseline(cell).FindPopulation(populationId)
            ?? throw new InvalidOperationException($"Cell {cell} has no population '{populationId}'");
        if (alive < 0 || alive > population.Max)
            throw new ArgumentOutOfRangeException(nameof(alive), alive, $"Population budget is [0, {population.Max}]");
        var state = Cell(cell);
        if (alive == population.Target)
            state.PopulationAlive.Remove(populationId);
        else
            state.PopulationAlive[populationId] = alive;
    }

    public int GetPopulationAlive(CellKey cell, string populationId)
    {
        var population = Baseline(cell).FindPopulation(populationId)
            ?? throw new InvalidOperationException($"Cell {cell} has no population '{populationId}'");
        return _cells.TryGetValue(cell, out var state) && state.PopulationAlive.TryGetValue(populationId, out int alive)
            ? alive
            : population.Target;
    }

    // ── slot occupants (entities) ───────────────────────────────────────────

    /// <summary>Kill a population member. It is promoted to an individual record and stays dead.</summary>
    internal EntityId KillOccupant(string slotKey) =>
        Diverge(slotKey, r => r with { Alive = false }).InstanceId;

    internal EntityId MoveOccupant(string slotKey, int xCm, int zCm) =>
        Diverge(slotKey, r => r with { XCm = xCm, ZCm = zCm }).InstanceId;

    /// <summary>Return an occupant to its baseline state; the next save rebases its record away.</summary>
    internal void RestoreOccupant(string slotKey)
    {
        if (_entities.TryGetValue(slotKey, out var record))
            _entities[slotKey] = record with { Alive = null, XCm = null, ZCm = null };
    }

    public OccupantView Occupant(string slotKey)
    {
        var slot = SlotOf(slotKey);
        if (!_entities.TryGetValue(slotKey, out var record))
            return new OccupantView(slotKey, null, true, slot.XCm, slot.ZCm);
        return new OccupantView(slotKey, record.InstanceId, record.Alive ?? true, record.XCm ?? slot.XCm, record.ZCm ?? slot.ZCm);
    }

    // ── created instances ───────────────────────────────────────────────────

    /// <summary>Place a new persistent instance in a cell; the registry assigns its identity (D-10).</summary>
    internal EntityId PlaceCreated(CellKey hostCell, string defId, int xCm, int zCm)
    {
        if (!InCell(xCm) || !InCell(zCm))
            throw new ArgumentOutOfRangeException(nameof(xCm), $"({xCm}, {zCm}) is outside the cell's [0, {WorldMath.CellSizeCm}) square");
        var instance = _registry.CreateEntity(DefinitionId.Parse(defId));
        _created[instance.InstanceId] = new CreatedEntityRecord(instance.InstanceId, defId, hostCell.ToString(), xCm, zCm);
        return instance.InstanceId;
    }

    /// <summary>
    /// Place an existing item instance in the world (a drop): it keeps its identity, which the caller has already
    /// registered, and its count.
    /// </summary>
    internal void PlaceItem(CellKey hostCell, EntityId itemId, string defId, int count, int xCm, int zCm, int quality = 0)
    {
        if (!InCell(xCm) || !InCell(zCm))
            throw new ArgumentOutOfRangeException(nameof(xCm), $"({xCm}, {zCm}) is outside the cell's [0, {WorldMath.CellSizeCm}) square");
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "A placed stack holds at least one");
        _created[itemId] = new CreatedEntityRecord(itemId, defId, hostCell.ToString(), xCm, zCm) { Count = count, Quality = quality };
    }

    /// <summary>Take a placed item out of the world (a pick-up); its identity lives on in whatever holds it next.</summary>
    internal CreatedEntityRecord TakeCreated(EntityId instanceId) =>
        _created.Remove(instanceId, out var record) ? record : throw new InvalidOperationException($"{instanceId} is not in the world");

    public CreatedEntityRecord? FindCreated(EntityId instanceId) => _created.GetValueOrDefault(instanceId);

    // ── world containers (schema 6) ─────────────────────────────────────────

    /// <summary>A changed container's record, or null while it still holds its baseline contents.</summary>
    public ContainerRecord? Container(string key) => _containers.GetValueOrDefault(key);

    /// <summary>Record a container's whole current contents; the runtime has registered every identity in it.</summary>
    internal void SetContainer(ContainerRecord record) => _containers[record.Key] = record;

    public IReadOnlyList<ContainerRecord> ContainersIn(CellKey cell)
    {
        string key = cell.ToString();
        return _containers.Values.Where(c => c.HostCell == key).ToList();
    }

    /// <summary>A container that no longer exists - a corpse looted empty. Its identity retires with it.</summary>
    internal void RemoveContainer(string key)
    {
        if (_containers.Remove(key, out var record))
        {
            foreach (var id in record.Items.Select(i => i.ItemId).Prepend(record.InstanceId))
            {
                if (_registry.Exists(id))
                    _registry.DestroyEntity(id);
            }
        }
    }

    // ── spawners' creatures ─────────────────────────────────────────────────

    /// <summary>A creature's record, or null while it is still its spawner's baseline.</summary>
    public CreatureRecord? Creature(string key) => _creatures.GetValueOrDefault(key);

    public IReadOnlyList<CreatureRecord> CreaturesIn(CellKey cell)
    {
        string key = cell.ToString();
        return _creatures.Values.Where(c => c.HostCell == key).ToList();
    }

    /// <summary>Record a creature's divergence. A new generation's identity is registered and the old one's retired.</summary>
    internal void SetCreature(CreatureRecord record)
    {
        if (_creatures.TryGetValue(record.Key, out var previous) && previous.InstanceId != record.InstanceId && _registry.Exists(previous.InstanceId))
            _registry.DestroyEntity(previous.InstanceId);
        if (!_registry.Exists(record.InstanceId))
            _registry.CreateEntity(DefinitionId.Parse(record.DefId), record.InstanceId);
        _creatures[record.Key] = record;
    }

    /// <summary>The creature is its baseline again: its record, and its individual identity, go.</summary>
    internal void RemoveCreature(string key)
    {
        if (_creatures.Remove(key, out var record) && _registry.Exists(record.InstanceId))
            _registry.DestroyEntity(record.InstanceId);
    }

    /// <summary>The sounds made on the last tick, which creatures hear on the next (schema 14), in the order they were made.</summary>
    public ImmutableArray<Noise> Noises => _noises;

    internal void SetNoises(ImmutableArray<Noise> noises) => _noises = noises;

    // ── placed pieces and NPC errands (M7, schema 15) ───────────────────────

    /// <summary>A placed piece, or null.</summary>
    public PieceRecord? Piece(EntityId id) => _pieces.GetValueOrDefault(id.Value);

    /// <summary>Every placed piece, sorted by instance ID.</summary>
    public IReadOnlyList<PieceRecord> Pieces => _pieces.Values.ToList();

    /// <summary>The pieces anchored in a cell, sorted by instance ID.</summary>
    public IReadOnlyList<PieceRecord> PiecesIn(CellKey cell)
    {
        string key = cell.ToString();
        return _pieces.Values.Where(p => p.HostCell == key).ToList();
    }

    /// <summary>+1 on every place, dismantle and destroy; never reused, never decreasing. The next piece's ordinal is this plus one.</summary>
    public long StructureSequence => _structureSequence;

    /// <summary>A new piece: its derived identity is registered, and the sequence moves to <paramref name="sequence"/>.</summary>
    internal void PlacePiece(PieceRecord record, long sequence)
    {
        if (sequence <= _structureSequence)
            throw new ArgumentException($"The structure sequence only rises: {sequence} after {_structureSequence}", nameof(sequence));
        if (_pieces.ContainsKey(record.InstanceId.Value))
            throw new InvalidOperationException($"{record.InstanceId} is already placed");
        _registry.CreateEntity(DefinitionId.Parse(record.DefId), record.InstanceId);
        _pieces[record.InstanceId.Value] = record;
        _structureSequence = sequence;
    }

    /// <summary>A placed piece's row changed - its health, whether its door stands open.</summary>
    internal void SetPiece(PieceRecord record)
    {
        if (!_pieces.ContainsKey(record.InstanceId.Value))
            throw new InvalidOperationException($"{record.InstanceId} is not placed");
        _pieces[record.InstanceId.Value] = record;
    }

    /// <summary>A piece taken down or destroyed: its identity retires, and the sequence moves to <paramref name="sequence"/>.</summary>
    internal void RemovePiece(EntityId id, long sequence)
    {
        if (sequence <= _structureSequence)
            throw new ArgumentException($"The structure sequence only rises: {sequence} after {_structureSequence}", nameof(sequence));
        if (!_pieces.Remove(id.Value))
            throw new InvalidOperationException($"{id} is not placed");
        if (_registry.Exists(id))
            _registry.DestroyEntity(id);
        _structureSequence = sequence;
    }

    /// <summary>A named NPC's errand, or null while they stand at their site.</summary>
    public NpcErrandRecord? NpcErrand(string npcId) => _errands.GetValueOrDefault(npcId);

    /// <summary>Every errand, sorted by NPC ID.</summary>
    public IReadOnlyList<NpcErrandRecord> NpcErrands => _errands.Values.ToList();

    /// <summary>The errands hosted in a cell, sorted by NPC ID.</summary>
    public IReadOnlyList<NpcErrandRecord> NpcErrandsIn(CellKey cell)
    {
        string key = cell.ToString();
        return _errands.Values.Where(e => e.HostCell == key).ToList();
    }

    /// <summary>Record an errand. Its route must be one a factory built.</summary>
    internal void SetNpcErrand(NpcErrandRecord record)
    {
        if ((record.Route is null ? "none" : record.Route.Problem()) is { } problem)
            throw new ArgumentException($"npc errand {record.NpcId} has an invalid route: {problem}", nameof(record));
        _errands[record.NpcId] = record;
    }

    /// <summary>The NPC is back at their site: the baseline again.</summary>
    internal void RemoveNpcErrand(string npcId) => _errands.Remove(npcId);

    /// <summary>A container whose record goes while its items live on elsewhere: only the container's own identity retires.</summary>
    internal void ReleaseContainer(string key)
    {
        if (_containers.Remove(key, out var record) && _registry.Exists(record.InstanceId))
            _registry.DestroyEntity(record.InstanceId);
    }

    /// <summary>Remove a created instance. It existed nowhere else, so its identity retires with it.</summary>
    internal void RemoveCreated(EntityId instanceId)
    {
        if (_created.Remove(instanceId) && _registry.Exists(instanceId))
            _registry.DestroyEntity(instanceId);
    }

    public IReadOnlyList<CreatedEntityRecord> CreatedIn(CellKey cell)
    {
        string key = cell.ToString();
        return _created.Values.Where(c => c.HostCell == key).OrderBy(c => c.InstanceId.Value, StringComparer.Ordinal).ToList();
    }

    private static bool InCell(int cm) => cm >= 0 && cm < WorldMath.CellSizeCm;

    // ── save / load ─────────────────────────────────────────────────────────

    /// <summary>
    /// Diff against the regenerated baseline and rebase (PERSISTENCE.md §5.6): every record whose state
    /// equals its baseline is deleted, here and in the returned snapshot. The mutation paths keep the
    /// store close to minimal, but this save-time diff is the authority - a divergence that returned to
    /// baseline through any path is found here.
    /// </summary>
    public DeltaSnapshot TakeSnapshot()
    {
        var entities = ImmutableArray.CreateBuilder<EntityDeltaRecord>();
        foreach (string slotKey in _entities.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList())
        {
            var slot = SlotOf(slotKey);
            var record = _entities[slotKey];
            var normalized = record with
            {
                Alive = record.Alive == true ? null : record.Alive,
                XCm = record.XCm == slot.XCm && record.ZCm == slot.ZCm ? null : record.XCm,
                ZCm = record.XCm == slot.XCm && record.ZCm == slot.ZCm ? null : record.ZCm,
            };
            if (normalized.DirtyMask == 0)
            {
                // Back to baseline: the slot is regenerable again, so its individual identity retires.
                _entities.Remove(slotKey);
                if (_registry.Exists(record.InstanceId))
                    _registry.DestroyEntity(record.InstanceId);
                continue;
            }
            var proven = normalized with { BaselineHash = Baseline(CellOfSlot(slotKey)).Digest };
            _entities[slotKey] = proven;
            entities.Add(proven);
        }

        var entityCells = entities.Select(e => CellOfSlot(e.SlotKey)).ToHashSet();
        var cells = ImmutableArray.CreateBuilder<CellDeltaRecord>();
        foreach (var cell in _cells.Keys.OrderBy(k => k).ToList())
        {
            var state = _cells[cell];
            if (state.IsBaseline)
            {
                _cells.Remove(cell);
                continue;
            }
            var reasons = new List<string>();
            if (state.Flags.Count > 0) reasons.Add("flags");
            if (state.Nodes.Count > 0) reasons.Add("nodes");
            if (state.PopulationAlive.Count > 0) reasons.Add("spawns");
            if (entityCells.Contains(cell)) reasons.Add("entities");
            reasons.Sort(StringComparer.Ordinal);

            cells.Add(new CellDeltaRecord(
                cell.ToString(),
                Baseline(cell).Digest,
                reasons.ToImmutableArray(),
                state.Flags.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToImmutableArray(),
                state.Nodes.Values.OrderBy(n => n.NodeKey, StringComparer.Ordinal).ToImmutableArray(),
                state.PopulationAlive.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToImmutableArray()));
        }

        var created = _created.Values
            .OrderBy(c => c.InstanceId.Value, StringComparer.Ordinal)
            .Select(c => c with { BaselineHash = Baseline(CellKey.Parse(c.HostCell)).Digest })
            .ToImmutableArray();

        // A changed container stays recorded even if its contents come back to what they were: its baseline is content,
        // which this layer never sees, and one small record per touched container is the cost.
        var containers = _containers.Values
            .Select(c => c with { BaselineHash = Baseline(CellKey.Parse(c.HostCell)).Digest })
            .ToImmutableArray();

        var creatures = _creatures.Values
            .Select(c => c with { BaselineHash = Baseline(CellKey.Parse(c.HostCell)).Digest })
            .ToImmutableArray();

        // New arrays, never views of the stores: a captured snapshot is encoded later, off the frame thread (G29).
        var pieces = _pieces.Values
            .Select(p => p with { BaselineHash = Baseline(CellKey.Parse(p.HostCell)).Digest })
            .ToImmutableArray();
        var errands = _errands.Values
            .Select(e => e with { BaselineHash = Baseline(CellKey.Parse(e.HostCell)).Digest })
            .ToImmutableArray();

        return new DeltaSnapshot(cells.ToImmutable(), entities.ToImmutable())
        {
            Created = created, Containers = containers, Creatures = creatures, Noises = _noises,
            Pieces = pieces, StructureSequence = _structureSequence, NpcErrands = errands,
        };
    }

    /// <summary>
    /// Rebuild a world from a persisted delta: regenerate the baseline, apply the cell delta, then merge
    /// the entity delta on slot key (PERSISTENCE.md §7.4). Records that fail invariant validation are
    /// dropped and reported rather than trusted or silently ignored (§7.2) - including any record whose
    /// baseline_hash is not the hash of the baseline regenerated here. The loader proves baselines
    /// before it gets this far; this check is the last line, so no path can apply a delta to a
    /// baseline it was not made against.
    /// </summary>
    public static WorldDelta FromSnapshot(
        ICellBaselineGenerator generator, ulong worldSeed, Registry registry, DeltaSnapshot snapshot,
        out ImmutableArray<RejectedRecord> rejected)
    {
        var world = new WorldDelta(generator, worldSeed, registry);
        var problems = ImmutableArray.CreateBuilder<RejectedRecord>();
        world._structureSequence = snapshot.StructureSequence;

        foreach (var record in snapshot.Cells)
        {
            string? reason = world.TryApplyCell(record);
            if (reason is not null)
                problems.Add(new RejectedRecord("cells", record.CellKey, reason));
        }

        var seenIds = new HashSet<EntityId>();
        foreach (var record in snapshot.Entities)
        {
            string? reason = world.TryApplyEntity(record, seenIds);
            if (reason is not null)
                problems.Add(new RejectedRecord("entities", record.SlotKey, reason));
        }

        foreach (var record in snapshot.Created)
        {
            string? reason = world.TryApplyCreated(record, seenIds);
            if (reason is not null)
                problems.Add(new RejectedRecord("entities", record.InstanceId.Value, reason));
        }

        // Pieces before the containers and errands that name them (M7).
        foreach (var record in snapshot.Pieces)
        {
            string? reason = world.TryApplyPiece(record, seenIds);
            if (reason is not null)
                problems.Add(new RejectedRecord("entities", record.InstanceId.Value, reason));
        }

        foreach (var record in snapshot.Containers)
        {
            string? reason = world.TryApplyContainer(record, seenIds);
            if (reason is not null)
                problems.Add(new RejectedRecord("entities", record.Key, reason));
        }

        foreach (var record in snapshot.Creatures)
        {
            string? reason = world.TryApplyCreature(record, seenIds);
            if (reason is not null)
                problems.Add(new RejectedRecord("entities", record.Key, reason));
        }

        var working = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in snapshot.NpcErrands)
        {
            string? reason = world.TryApplyNpcErrand(record, working);
            if (reason is not null)
                problems.Add(new RejectedRecord("entities", record.NpcId, reason));
        }

        var noises = ImmutableArray.CreateBuilder<Noise>();
        for (int i = 0; i < snapshot.Noises.Length; i++)
        {
            var noise = snapshot.Noises[i];
            if (noise.RadiusMm <= 0 || noise.Call != (noise.CallerKind is not null) || (noise.CallerKind is { } kind && !DefinitionId.IsValid(kind)))
                problems.Add(new RejectedRecord("entities", $"noise {i}", "a sound with no reach, or a call without its caller's kind"));
            else
                noises.Add(noise);
        }
        world._noises = noises.ToImmutable();

        rejected = problems.ToImmutable();
        return world;
    }

    /// <summary>
    /// A digest of a cell's effective state - baseline plus every divergence, including the occupants of
    /// its slots. Two worlds are equal on a cell exactly when these match.
    /// </summary>
    public string EffectiveCellDigest(CellKey cell)
    {
        var baseline = Baseline(cell);
        _cells.TryGetValue(cell, out var state);
        using var h = new CanonicalHasher();
        h.Add("unnamed.effective-cell/v4").Add(baseline.Digest);

        var flags = state?.Flags.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList() ?? new();
        h.Add(flags.Count);
        foreach (var (name, value) in flags)
            h.Add(name).Add(value);

        foreach (var node in baseline.Nodes)
        {
            if (state is not null && state.Nodes.TryGetValue(node.NodeKey, out var harvest))
                h.Add(true).Add(harvest.LastHarvestTick).Add(harvest.HarvestSeq);
            else
                h.Add(false);
        }

        var created = CreatedIn(cell);
        h.Add(created.Count);
        foreach (var c in created)
            h.Add(c.InstanceId.Value).Add(c.DefId).Add(c.XCm).Add(c.ZCm).Add(c.Count).Add(c.Quality);

        var containers = ContainersIn(cell);
        h.Add(containers.Count);
        foreach (var c in containers)
        {
            h.Add(c.Key).Add(c.InstanceId.Value).Add(c.Items.Length);
            foreach (var item in c.Items)
                h.Add(item.ItemId.Value).Add(item.DefId).Add(item.Count).Add(item.Quality);
        }

        var creatures = CreaturesIn(cell);
        h.Add(creatures.Count);
        foreach (var c in creatures)
        {
            h.Add(c.Key).Add(c.DefId).Add(c.InstanceId.Value).Add(c.Generation).Add((int)c.Condition).Add(c.XMm).Add(c.ZMm).Add(c.FacingMdeg)
                .Add(c.Health).Add(c.DiedTick).Add(c.RespawnTick).Add((int)c.Mind).Add(c.Awareness).Add(c.Knows).Add(c.KnownXMm).Add(c.KnownZMm)
                .Add(c.LastSeenTick).Add(c.SearchUntil).Add(c.HasCalled).Add(c.NextChargeTick).Add(c.StaggerImmuneUntil).Add(c.StaggeredTick ?? -1)
                .Add(c.StaggerLastsTicks).Add(c.AttackTick ?? -1).Add(c.AttackStruck?.Value ?? "-");
        }

        foreach (var population in baseline.Populations)
        {
            h.Add(GetPopulationAlive(cell, population.PopulationId));
            foreach (var slot in population.Slots)
            {
                var occupant = Occupant(slot.SlotKey);
                h.Add(occupant.InstanceId?.Value ?? "-").Add(occupant.Alive).Add(occupant.XCm).Add(occupant.ZCm);
            }
        }

        // M7 (v3): the pieces anchored here, and the errands hosted here. A straddling piece is hashed once, in its anchor's cell.
        var pieces = PiecesIn(cell);
        h.Add(pieces.Count);
        foreach (var p in pieces)
            h.Add(p.InstanceId.Value).Add(p.DefId).Add(p.XMm).Add(p.ZMm).Add(p.Rotation).Add(p.Owner.Value).Add(p.HealthCurrent).Add(p.DoorOpen);

        var errands = NpcErrandsIn(cell);
        h.Add(errands.Count);
        foreach (var e in errands)
        {
            h.Add(e.NpcId).Add(NpcErrandPhases.Key(e.Phase)).Add(e.PieceId?.Value ?? "-").Add(e.WorkOwner?.Value ?? "-").Add(e.XMm).Add(e.ZMm)
                .Add(e.FacingMdeg);
            e.Route.AddTo(h);
            h.Add(e.StuckTicks);
        }
        return h.Finish();
    }

    // ── internals ───────────────────────────────────────────────────────────

    private MutableCell Cell(CellKey cell)
    {
        if (!_cells.TryGetValue(cell, out var state))
            _cells[cell] = state = new MutableCell();
        return state;
    }

    private EntityDeltaRecord Diverge(string slotKey, Func<EntityDeltaRecord, EntityDeltaRecord> change)
    {
        var slot = SlotOf(slotKey);
        if (!_entities.TryGetValue(slotKey, out var record))
        {
            // Promotion: the slot's occupant becomes an individual with its own identity (D-10).
            var definition = DefinitionId.Parse(slot.FamilyDefId);
            var instance = _registry.CreateEntity(definition);
            record = new EntityDeltaRecord(instance.InstanceId, slotKey, 0, slot.FamilyDefId, null, null, null);
        }
        record = change(record);
        _entities[slotKey] = record;
        return record;
    }

    private BaselineSlot SlotOf(string slotKey) =>
        Baseline(CellOfSlot(slotKey)).FindSlot(slotKey)
        ?? throw new InvalidOperationException($"No baseline slot '{slotKey}'");

    // slot_key = <cell_key>.<population_id>.<ordinal>; a cell key contains no '.', so it is the prefix.
    private static CellKey CellOfSlot(string slotKey)
    {
        int dot = slotKey.IndexOf('.');
        return dot > 0 && CellKey.TryParse(slotKey[..dot], out var cell)
            ? cell
            : throw new FormatException($"Not a slot key: '{slotKey}'");
    }

    private string? TryApplyCell(CellDeltaRecord record)
    {
        if (!CellKey.TryParse(record.CellKey, out var cell))
            return "unparseable cell key";
        if (_cells.ContainsKey(cell))
            return "duplicate cell record";
        var baseline = Baseline(cell);
        if (record.BaselineHash != baseline.Digest)
            return $"baseline_hash {record.BaselineHash} is not the regenerated baseline {baseline.Digest}";
        var state = new MutableCell();
        foreach (var (flag, value) in record.Flags)
        {
            if (!DefinitionId.IsValid(flag) || !flag.StartsWith("world.", StringComparison.Ordinal))
                return $"invalid world flag '{flag}'";
            if (value != 0)
                state.Flags[flag] = value;
        }
        foreach (var node in record.HarvestedNodes)
        {
            if (baseline.FindNode(node.NodeKey) is null)
                return $"node '{node.NodeKey}' does not exist in the regenerated baseline";
            state.RestoreHarvest(node);
        }
        foreach (var (populationId, alive) in record.PopulationAlive)
        {
            var population = baseline.FindPopulation(populationId);
            if (population is null)
                return $"population '{populationId}' does not exist in the regenerated baseline";
            if (alive < 0 || alive > population.Max)
                return $"population '{populationId}' alive count {alive} is outside [0, {population.Max}]";
            if (alive != population.Target)
                state.PopulationAlive[populationId] = alive;
        }
        _cells[cell] = state;
        return null;
    }

    private string? TryApplyEntity(EntityDeltaRecord record, HashSet<EntityId> seenIds)
    {
        BaselineSlot? slot;
        try
        {
            slot = Baseline(CellOfSlot(record.SlotKey)).FindSlot(record.SlotKey);
        }
        catch (FormatException)
        {
            return "unparseable slot key";
        }
        if (slot is null)
            return "slot does not exist in the regenerated baseline";
        string hostBaseline = Baseline(CellOfSlot(record.SlotKey)).Digest;
        if (record.BaselineHash != hostBaseline)
            return $"baseline_hash {record.BaselineHash ?? "(none)"} is not the host cell's regenerated baseline {hostBaseline}";
        if (!string.Equals(slot.FamilyDefId, record.DefId, StringComparison.Ordinal))
            return $"def_id '{record.DefId}' does not match the slot's family '{slot.FamilyDefId}'";
        if (_entities.ContainsKey(record.SlotKey))
            return "two records claim one slot";
        if (!seenIds.Add(record.InstanceId))
            return $"instance ID {record.InstanceId} appears twice";
        if ((record.XCm is null) != (record.ZCm is null))
            return "position is half-set";
        if (_registry.Exists(record.InstanceId))
            return $"instance ID {record.InstanceId} is already registered";

        _registry.CreateEntity(DefinitionId.Parse(record.DefId), record.InstanceId);
        _entities[record.SlotKey] = record;
        return null;
    }

    private string? TryApplyCreated(CreatedEntityRecord record, HashSet<EntityId> seenIds)
    {
        if (!CellKey.TryParse(record.HostCell, out var cell))
            return "unparseable host cell";
        string hostBaseline = Baseline(cell).Digest;
        if (record.BaselineHash != hostBaseline)
            return $"baseline_hash {record.BaselineHash ?? "(none)"} is not the host cell's regenerated baseline {hostBaseline}";
        if (!DefinitionId.IsValid(record.DefId))
            return $"invalid definition ID '{record.DefId}'";
        if (!InCell(record.XCm) || !InCell(record.ZCm))
            return $"position ({record.XCm}, {record.ZCm}) is outside the host cell";
        if (!seenIds.Add(record.InstanceId))
            return $"instance ID {record.InstanceId} appears twice";
        if (_registry.Exists(record.InstanceId))
            return $"instance ID {record.InstanceId} is already registered";
        if (record.Count <= 0)
            return $"count {record.Count} is not positive";

        _registry.CreateEntity(DefinitionId.Parse(record.DefId), record.InstanceId);
        _created[record.InstanceId] = record;
        return null;
    }

    /// <summary>
    /// A placed piece (M7). Its ID's timestamp is read as the derivation ordinal (the D-04 note), which never exceeds the saved sequence.
    /// It is not compared with <c>Derived(..., Owner)</c>, so a later change of owner is a row edit, not a re-key.
    /// </summary>
    private string? TryApplyPiece(PieceRecord record, HashSet<EntityId> seenIds)
    {
        if (!CellKey.TryParse(record.HostCell, out var cell))
            return "unparseable host cell";
        string hostBaseline = Baseline(cell).Digest;
        if (record.BaselineHash != hostBaseline)
            return $"baseline_hash {record.BaselineHash ?? "(none)"} is not the host cell's regenerated baseline {hostBaseline}";
        if (record.InstanceId.Kind != EntityKind.Piece)
            return $"{record.InstanceId} is not a piece ID";
        if (!DefinitionId.IsValid(record.DefId) || !record.DefId.StartsWith("piece.", StringComparison.Ordinal))
            return $"'{record.DefId}' is not a piece definition ID";
        if (record.Owner.Kind != EntityKind.Character)
            return $"its owner {record.Owner} is not a character";
        if (record.Rotation is < 0 or > 3)
            return $"rotation {record.Rotation} is not a quarter turn 0-3";
        if (record.HealthCurrent < 1)
            return $"health {record.HealthCurrent} is below 1";
        if (record.HostCell != CellKey.OfWorld(record.XMm / 1000.0, record.ZMm / 1000.0).ToString())
            return $"host cell {record.HostCell} is not the cell of its anchor";
        if (record.InstanceId.Timestamp < 1 || record.InstanceId.Timestamp > _structureSequence)
            return $"ordinal {record.InstanceId.Timestamp} is outside 1..{_structureSequence}, the structure sequence";
        if (!seenIds.Add(record.InstanceId) || _pieces.ContainsKey(record.InstanceId.Value))
            return $"instance ID {record.InstanceId} appears twice";
        if (_registry.Exists(record.InstanceId))
            return $"instance ID {record.InstanceId} is already registered";

        _registry.CreateEntity(DefinitionId.Parse(record.DefId), record.InstanceId);
        _pieces[record.InstanceId.Value] = record;
        return null;
    }

    /// <summary>A named NPC's errand (M7). It registers nothing: NPC identities are derived and registered as the NPCs are placed.</summary>
    private string? TryApplyNpcErrand(NpcErrandRecord record, HashSet<string> working)
    {
        if (!CellKey.TryParse(record.HostCell, out var cell))
            return "unparseable host cell";
        string hostBaseline = Baseline(cell).Digest;
        if (record.BaselineHash != hostBaseline)
            return $"baseline_hash {record.BaselineHash ?? "(none)"} is not the host cell's regenerated baseline {hostBaseline}";
        if (!DefinitionId.IsValid(record.NpcId) || !record.NpcId.StartsWith("npc.", StringComparison.Ordinal) || _errands.ContainsKey(record.NpcId))
            return $"'{record.NpcId}' is not an NPC ID, or has two errands";
        bool working_ = record.Phase is NpcErrandPhase.ToWork or NpcErrandPhase.AtWork;
        if (!Enum.IsDefined(record.Phase) || (working_ && (record.PieceId is null || record.WorkOwner is null)) || (!working_ && record.PieceId is not null))
            return $"phase {record.Phase} does not match its piece and owner";
        if (record.FacingMdeg is < 0 or >= 360_000 || record.StuckTicks < 0)
            return "its facing or stuck count is out of range";
        if (record.Route is null || record.Route.Problem() is not null)
            return "its route is invalid";
        if (working_)
        {
            if (Piece(record.PieceId!) is not { } piece)
                return $"its piece {record.PieceId} is not placed";
            if (piece.Owner != record.WorkOwner)
                return $"its work owner {record.WorkOwner} is not the owner of {record.PieceId}";
            if (!working.Add(record.PieceId!.Value))
                return $"{record.PieceId} already has a worker";
        }
        _errands[record.NpcId] = record;
        return null;
    }

    private string? TryApplyContainer(ContainerRecord record, HashSet<EntityId> seenIds)
    {
        if (!CellKey.TryParse(record.HostCell, out var cell))
            return "unparseable host cell";
        string hostBaseline = Baseline(cell).Digest;
        if (record.BaselineHash != hostBaseline)
            return $"baseline_hash {record.BaselineHash ?? "(none)"} is not the host cell's regenerated baseline {hostBaseline}";
        if (!DefinitionId.IsValid(record.Key) || _containers.ContainsKey(record.Key))
            return $"container key '{record.Key}' is invalid or appears twice";
        if (record.InstanceId.Kind != EntityKind.Container)
            return $"{record.InstanceId} is not a container ID";
        // A piece chest (M7): its piece must stand, and its identity is the one derived from that piece.
        if (record.Key.StartsWith(PieceChestPrefix, StringComparison.Ordinal))
        {
            if (!EntityId.TryParse("pce_" + record.Key[PieceChestPrefix.Length..].ToUpperInvariant(), out var pieceId) || Piece(pieceId) is not { } piece)
                return "its chest is gone";
            if (record.InstanceId != PieceChestId(piece.InstanceId))
                return "not its chest's identity";
        }
        var ids = record.Items.Select(i => i.ItemId).Prepend(record.InstanceId).ToList();
        foreach (var id in ids)
        {
            if (!seenIds.Add(id))
                return $"instance ID {id} appears twice";
            if (_registry.Exists(id))
                return $"instance ID {id} is already registered";
        }
        if (record.Items.Any(i => i.ItemId.Kind != EntityKind.Item || !DefinitionId.IsValid(i.DefId) || i.Count <= 0))
            return "an item in it is malformed";

        _registry.CreateEntity(DefinitionId.Parse(record.Key), record.InstanceId);
        foreach (var item in record.Items)
            _registry.CreateEntity(DefinitionId.Parse(item.DefId), item.ItemId);
        _containers[record.Key] = record;
        return null;
    }

    private string? TryApplyCreature(CreatureRecord record, HashSet<EntityId> seenIds)
    {
        if (!CellKey.TryParse(record.HostCell, out var cell))
            return "unparseable host cell";
        string hostBaseline = Baseline(cell).Digest;
        if (record.BaselineHash != hostBaseline)
            return $"baseline_hash {record.BaselineHash ?? "(none)"} is not the host cell's regenerated baseline {hostBaseline}";
        int hash = record.Key.LastIndexOf('#');
        if (hash <= 0 || !int.TryParse(record.Key[(hash + 1)..], out int member) || member < 0 || _creatures.ContainsKey(record.Key))
            return $"creature key '{record.Key}' is not spawner#member, or appears twice";
        if (record.InstanceId.Kind != EntityKind.Creature || !DefinitionId.IsValid(record.DefId))
            return $"{record.InstanceId} of {record.DefId} is not a creature";
        if (!Enum.IsDefined(record.Condition) || !Enum.IsDefined(record.Mind) || record.Generation < 0 || record.Health < 0 || record.DiedTick < 0
            || record.RespawnTick < 0 || record.FacingMdeg is < 0 or >= 360_000 || record.Awareness is < 0 or > 100 || record.LastSeenTick < 0
            || record.SearchUntil < 0 || record.NextChargeTick < 0 || record.StaggerImmuneUntil < 0 || record.StaggeredTick < 0
            || record.StaggerLastsTicks < 0 || (record.StaggeredTick is null && record.StaggerLastsTicks != 0))
            return "its state is out of range";
        // An attack in progress is the living creature's one action, and what it landed on is a body that can be struck.
        if (record.AttackTick is < 0 || (record.AttackTick is null && record.AttackStruck is not null)
            || (record.AttackTick is not null && (record.Condition != CreatureCondition.Alive || record.StaggeredTick is not null))
            || record.AttackStruck is { Kind: not (EntityKind.Character or EntityKind.Npc) })
            return "its attack in progress is impossible";
        if (!seenIds.Add(record.InstanceId))
            return $"instance ID {record.InstanceId} appears twice";
        if (_registry.Exists(record.InstanceId))
            return $"instance ID {record.InstanceId} is already registered";
        _registry.CreateEntity(DefinitionId.Parse(record.DefId), record.InstanceId);
        _creatures[record.Key] = record;
        return null;
    }

    /// <summary>The key prefix of a piece chest's container: <c>container.pce_</c> and the piece's ULID in lower case.</summary>
    internal const string PieceChestPrefix = "container.pce_";

    /// <summary>A piece chest's container key.</summary>
    internal static string PieceChestKey(EntityId pieceId) => "container." + pieceId.Value.ToLowerInvariant();

    /// <summary>A piece chest's container identity, derived from its piece's.</summary>
    internal static EntityId PieceChestId(EntityId pieceId) =>
        EntityId.Derived(EntityKind.Container, pieceId.Timestamp, "unnamed.piece-container/v1", pieceId.Value);

    private sealed class MutableCell
    {
        private readonly Dictionary<string, int> _harvestSeq = new(StringComparer.Ordinal);

        public SortedDictionary<string, long> Flags { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, NodeHarvest> Nodes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> PopulationAlive { get; } = new(StringComparer.Ordinal);

        public bool IsBaseline => Flags.Count == 0 && Nodes.Count == 0 && PopulationAlive.Count == 0;

        public int NextHarvestSeq(string nodeKey)
        {
            int next = _harvestSeq.TryGetValue(nodeKey, out int seq) ? seq + 1 : 1;
            _harvestSeq[nodeKey] = next;
            return next;
        }

        public void RestoreHarvest(NodeHarvest harvest)
        {
            Nodes[harvest.NodeKey] = harvest;
            _harvestSeq[harvest.NodeKey] = Math.Max(_harvestSeq.GetValueOrDefault(harvest.NodeKey), harvest.HarvestSeq);
        }
    }
}
