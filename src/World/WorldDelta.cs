// UNNAMED World - the sparse world delta (D-05, PERSISTENCE.md §1.1-§1.2, §5.2, §5.3, §5.6)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
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

/// <summary>What <see cref="WorldDelta.TakeSnapshot"/> captures: the whole persisted world delta.</summary>
public sealed record DeltaSnapshot(ImmutableArray<CellDeltaRecord> Cells, ImmutableArray<EntityDeltaRecord> Entities)
{
    public static DeltaSnapshot Empty { get; } = new(ImmutableArray<CellDeltaRecord>.Empty, ImmutableArray<EntityDeltaRecord>.Empty);
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

    public WorldDelta(ICellBaselineGenerator generator, ulong worldSeed, Registry registry)
    {
        Generator = generator;
        WorldSeed = worldSeed;
        _registry = registry;
    }

    public ICellBaselineGenerator Generator { get; }

    public ulong WorldSeed { get; }

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

    internal void HarvestNode(CellKey cell, string nodeKey, long tick)
    {
        if (Baseline(cell).FindNode(nodeKey) is null)
            throw new InvalidOperationException($"Cell {cell} has no node '{nodeKey}'");
        var state = Cell(cell);
        if (state.Nodes.ContainsKey(nodeKey))
            throw new InvalidOperationException($"Node '{nodeKey}' is already harvested");
        state.Nodes[nodeKey] = new NodeHarvest(nodeKey, tick, state.NextHarvestSeq(nodeKey));
    }

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

        return new DeltaSnapshot(cells.ToImmutable(), entities.ToImmutable());
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
        h.Add("unnamed.effective-cell/v1").Add(baseline.Digest);

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

        foreach (var population in baseline.Populations)
        {
            h.Add(GetPopulationAlive(cell, population.PopulationId));
            foreach (var slot in population.Slots)
            {
                var occupant = Occupant(slot.SlotKey);
                h.Add(occupant.InstanceId?.Value ?? "-").Add(occupant.Alive).Add(occupant.XCm).Add(occupant.ZCm);
            }
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
