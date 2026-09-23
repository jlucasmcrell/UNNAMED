// UNNAMED Persistence - section encoding (PERSISTENCE.md §5.1-§5.3)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Text.Json;
using MessagePack;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.World;

namespace UNNAMED.Persistence.Sections;

// Section files are string-keyed MessagePack maps, so a dump is self-describing: an AI session
// diagnosing a persistence bug can read one (D-04's rationale for legible IDs applies here too).

[MessagePackObject]
public sealed class PlayerDto
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("name")] public string Name { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("y_mm")] public long YMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("appearance_seed")] public ulong AppearanceSeed { get; set; }
    [Key("inventory")] public InventoryDto[] Inventory { get; set; } = Array.Empty<InventoryDto>();

    /// <summary>Required from schema 4. The 3 -> 4 step gives older saves the empty record.</summary>
    [Key("progression")] public ProgressionDto? Progression { get; set; }

    /// <summary>Required from schema 5. The 4 -> 5 step gives older saves facing 0 (+Z).</summary>
    [Key("facing_mdeg")] public int? FacingMdeg { get; set; }

    /// <summary>Required from schema 5. The 4 -> 5 step gives older saves no discoveries.</summary>
    [Key("discoveries")] public DiscoveryDto[]? Discoveries { get; set; }

    /// <summary>Required from schema 6. The 5 -> 6 step gives older saves nothing equipped.</summary>
    [Key("equipment")] public EquipmentDto[]? Equipment { get; set; }

    /// <summary>Required from schema 6. The 5 -> 6 step gives older saves an empty purse.</summary>
    [Key("currency")] public long? Currency { get; set; }
}

[MessagePackObject]
public sealed class EquipmentDto
{
    [Key("slot")] public string Slot { get; set; } = "";
    [Key("item_id")] public string ItemId { get; set; } = "";
}

[MessagePackObject]
public sealed class DiscoveryDto
{
    [Key("location_id")] public string LocationId { get; set; } = "";
    [Key("method")] public string Method { get; set; } = "";
    [Key("tick")] public long Tick { get; set; }
}

[MessagePackObject]
public sealed class InventoryDto
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("count")] public int Count { get; set; }
}

[MessagePackObject]
public sealed class CellsSectionDto
{
    [Key("records")] public CellDto[] Records { get; set; } = Array.Empty<CellDto>();
}

[MessagePackObject]
public sealed class CellDto
{
    [Key("cell_key")] public string CellKey { get; set; } = "";
    [Key("baseline_hash")] public string BaselineHash { get; set; } = "";
    [Key("dirty_reasons")] public string[] DirtyReasons { get; set; } = Array.Empty<string>();
    [Key("flags")] public FlagDto[] Flags { get; set; } = Array.Empty<FlagDto>();
    [Key("harvested_nodes")] public NodeDto[] HarvestedNodes { get; set; } = Array.Empty<NodeDto>();
    [Key("population_alive")] public PopulationDto[] PopulationAlive { get; set; } = Array.Empty<PopulationDto>();
}

[MessagePackObject]
public sealed class FlagDto
{
    [Key("name")] public string Name { get; set; } = "";
    [Key("value")] public long Value { get; set; }
}

[MessagePackObject]
public sealed class NodeDto
{
    [Key("node_key")] public string NodeKey { get; set; } = "";
    [Key("last_harvest_tick")] public long LastHarvestTick { get; set; }
    [Key("harvest_seq")] public int HarvestSeq { get; set; }
}

[MessagePackObject]
public sealed class PopulationDto
{
    [Key("population_id")] public string PopulationId { get; set; } = "";
    [Key("alive")] public int Alive { get; set; }
}

[MessagePackObject]
public sealed class EntitiesSectionDto
{
    [Key("records")] public EntityDto[] Records { get; set; } = Array.Empty<EntityDto>();

    /// <summary>Persistent instances no baseline slot generates (schema 3).</summary>
    [Key("created")] public CreatedDto[] Created { get; set; } = Array.Empty<CreatedDto>();

    /// <summary>The baseline hash of every cell hosting a record: each record is proven against its host cell.</summary>
    [Key("baselines")] public CellBaselineDto[] Baselines { get; set; } = Array.Empty<CellBaselineDto>();

    /// <summary>Changed world containers with their whole contents. Required from schema 6.</summary>
    [Key("containers")] public ContainerDto[]? Containers { get; set; }
}

[MessagePackObject]
public sealed class ContainerDto
{
    [Key("key")] public string Key { get; set; } = "";
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("items")] public ContainerItemDto[] Items { get; set; } = Array.Empty<ContainerItemDto>();
}

[MessagePackObject]
public sealed class ContainerItemDto
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("count")] public int Count { get; set; }
}

[MessagePackObject]
public sealed class CreatedDto
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("x_cm")] public int XCm { get; set; }
    [Key("z_cm")] public int ZCm { get; set; }

    /// <summary>Required from schema 6: a dropped stack keeps its count. The 5 -> 6 step gives older records 1.</summary>
    [Key("count")] public int? Count { get; set; }
}

[MessagePackObject]
public sealed class CellBaselineDto
{
    [Key("cell_key")] public string CellKey { get; set; } = "";
    [Key("baseline_hash")] public string BaselineHash { get; set; } = "";
}

/// <summary>§5.3's record: instance_id, slot_key, generation_seq, def_id, dirty_mask, state.</summary>
[MessagePackObject]
public sealed class EntityDto
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("slot_key")] public string SlotKey { get; set; } = "";
    [Key("generation_seq")] public int GenerationSeq { get; set; }
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("dirty_mask")] public int DirtyMask { get; set; }
    [Key("state")] public EntityStateDto State { get; set; } = new();
}

/// <summary>Only the diverged fields: a field that equals the baseline is nil.</summary>
[MessagePackObject]
public sealed class EntityStateDto
{
    [Key("alive")] public bool? Alive { get; set; }
    [Key("x_cm")] public int? XCm { get; set; }
    [Key("z_cm")] public int? ZCm { get; set; }
}

/// <summary>Encodes and decodes save sections. Output is byte-stable: equal input, equal bytes (T-03).</summary>
public static class SectionCodec
{
    // A save is untrusted input from disk: it may be damaged or hand-edited.
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);

    private static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = true };

    /// <summary>The MessagePack options every section is read and written with, shared with the migration chain.</summary>
    internal static MessagePackSerializerOptions MessagePackOptions => Options;

    public static byte[] EncodeManifest(SaveManifest manifest) => JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJson);

    public static SaveManifest DecodeManifest(byte[] bytes) =>
        JsonSerializer.Deserialize<SaveManifest>(bytes, ManifestJson)
        ?? throw new JsonException("manifest.json is empty");

    public static byte[] EncodePlayer(PlayerRecord player) => MessagePackSerializer.Serialize(new PlayerDto
    {
        InstanceId = player.Id.Value,
        Name = player.Name,
        XMm = player.XMm,
        YMm = player.YMm,
        ZMm = player.ZMm,
        AppearanceSeed = player.AppearanceSeed,
        Inventory = player.Inventory
            .Select(e => new InventoryDto { ItemId = e.ItemId.Value, DefId = e.DefId, Count = e.Count })
            .ToArray(),
        Progression = ProgressionCodec.ToDto(player.Progression),
        FacingMdeg = player.FacingMdeg,
        Discoveries = player.Discoveries
            .Select(d => new DiscoveryDto { LocationId = d.LocationId, Method = DiscoveryMethods.Key(d.Method), Tick = d.Tick })
            .ToArray(),
        Equipment = player.Equipment
            .Select(kv => new EquipmentDto { Slot = EquipSlots.Key(kv.Key), ItemId = kv.Value.Value })
            .ToArray(),
        Currency = player.Currency,
    }, Options);

    public static PlayerRecord DecodePlayer(byte[] bytes)
    {
        var dto = MessagePackSerializer.Deserialize<PlayerDto>(bytes, Options);
        var progression = dto.Progression ?? throw new FormatException("player.msgpack has no progression record (required from schema 4)");
        int facing = dto.FacingMdeg ?? throw new FormatException("player.msgpack has no facing (required from schema 5)");
        var discoveries = dto.Discoveries ?? throw new FormatException("player.msgpack has no discovery records (required from schema 5)");
        var equipment = dto.Equipment ?? throw new FormatException("player.msgpack has no equipment (required from schema 6)");
        long currency = dto.Currency ?? throw new FormatException("player.msgpack has no currency (required from schema 6)");
        return new PlayerRecord(EntityId.Parse(dto.InstanceId), dto.Name, dto.XMm, dto.YMm, dto.ZMm, dto.AppearanceSeed,
            dto.Inventory.Select(e => new InventoryEntry(EntityId.Parse(e.ItemId), e.DefId, e.Count)),
            ProgressionCodec.FromDto(progression), facing,
            discoveries.Select(d => new DiscoveryRecord(d.LocationId, DiscoveryMethods.Parse(d.Method), d.Tick)),
            equipment.Select(e => KeyValuePair.Create(EquipSlots.Parse(e.Slot), EntityId.Parse(e.ItemId))), currency);
    }

    public static byte[] EncodeCells(DeltaSnapshot snapshot) => MessagePackSerializer.Serialize(new CellsSectionDto
    {
        Records = snapshot.Cells.Select(c => new CellDto
        {
            CellKey = c.CellKey,
            BaselineHash = c.BaselineHash,
            DirtyReasons = c.DirtyReasons.ToArray(),
            Flags = c.Flags.Select(f => new FlagDto { Name = f.Key, Value = f.Value }).ToArray(),
            HarvestedNodes = c.HarvestedNodes
                .Select(n => new NodeDto { NodeKey = n.NodeKey, LastHarvestTick = n.LastHarvestTick, HarvestSeq = n.HarvestSeq })
                .ToArray(),
            PopulationAlive = c.PopulationAlive
                .Select(p => new PopulationDto { PopulationId = p.Key, Alive = p.Value })
                .ToArray(),
        }).ToArray(),
    }, Options);

    public static ImmutableArray<CellDeltaRecord> DecodeCells(byte[] bytes) =>
        MessagePackSerializer.Deserialize<CellsSectionDto>(bytes, Options).Records
            .Select(c => new CellDeltaRecord(
                c.CellKey,
                c.BaselineHash,
                c.DirtyReasons.ToImmutableArray(),
                c.Flags.Select(f => KeyValuePair.Create(f.Name, f.Value)).ToImmutableArray(),
                c.HarvestedNodes.Select(n => new NodeHarvest(n.NodeKey, n.LastHarvestTick, n.HarvestSeq)).ToImmutableArray(),
                c.PopulationAlive.Select(p => KeyValuePair.Create(p.PopulationId, p.Alive)).ToImmutableArray()))
            .ToImmutableArray();

    public static byte[] EncodeEntities(DeltaSnapshot snapshot)
    {
        var baselines = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Prove(string cell, string? hash, EntityId instance)
        {
            if (hash is null || (baselines.TryGetValue(cell, out string? known) && known != hash))
                throw new InvalidOperationException($"Entity record {instance} carries no single baseline hash for its host cell {cell}");
            baselines[cell] = hash;
        }
        foreach (var e in snapshot.Entities)
            Prove(HostCell(e.SlotKey), e.BaselineHash, e.InstanceId);
        foreach (var c in snapshot.Created)
            Prove(c.HostCell, c.BaselineHash, c.InstanceId);
        foreach (var c in snapshot.Containers)
            Prove(c.HostCell, c.BaselineHash, c.InstanceId);

        return MessagePackSerializer.Serialize(new EntitiesSectionDto
        {
            Records = snapshot.Entities.Select(e => new EntityDto
            {
                InstanceId = e.InstanceId.Value,
                SlotKey = e.SlotKey,
                GenerationSeq = e.GenerationSeq,
                DefId = e.DefId,
                DirtyMask = e.DirtyMask,
                State = new EntityStateDto { Alive = e.Alive, XCm = e.XCm, ZCm = e.ZCm },
            }).ToArray(),
            Created = snapshot.Created.Select(c => new CreatedDto
            {
                InstanceId = c.InstanceId.Value,
                DefId = c.DefId,
                HostCell = c.HostCell,
                XCm = c.XCm,
                ZCm = c.ZCm,
                Count = c.Count,
            }).ToArray(),
            Baselines = baselines.Select(kv => new CellBaselineDto { CellKey = kv.Key, BaselineHash = kv.Value }).ToArray(),
            Containers = snapshot.Containers.Select(c => new ContainerDto
            {
                Key = c.Key,
                InstanceId = c.InstanceId.Value,
                HostCell = c.HostCell,
                Items = c.Items.Select(i => new ContainerItemDto { ItemId = i.ItemId.Value, DefId = i.DefId, Count = i.Count }).ToArray(),
            }).ToArray(),
        }, Options);
    }

    /// <summary>The entities section: slot-keyed records, created instances and changed containers, each with its host cell's baseline hash.</summary>
    public static (ImmutableArray<EntityDeltaRecord> Entities, ImmutableArray<CreatedEntityRecord> Created, ImmutableArray<ContainerRecord> Containers)
        DecodeEntitySection(byte[] bytes)
    {
        var section = MessagePackSerializer.Deserialize<EntitiesSectionDto>(bytes, Options);
        var baselines = section.Baselines.ToDictionary(b => b.CellKey, b => b.BaselineHash, StringComparer.Ordinal);
        var entities = section.Records
            .Select(e => new EntityDeltaRecord(
                EntityId.Parse(e.InstanceId), e.SlotKey, e.GenerationSeq, e.DefId,
                e.State.Alive, e.State.XCm, e.State.ZCm,
                baselines.GetValueOrDefault(HostCell(e.SlotKey))))
            .ToImmutableArray();
        var created = section.Created
            .Select(c => new CreatedEntityRecord(
                EntityId.Parse(c.InstanceId), c.DefId, c.HostCell, c.XCm, c.ZCm, baselines.GetValueOrDefault(c.HostCell))
            {
                Count = c.Count ?? throw new FormatException($"created instance {c.InstanceId} has no count (required from schema 6)"),
            })
            .ToImmutableArray();
        var containers = (section.Containers ?? throw new FormatException("entities.msgpack has no containers list (required from schema 6)"))
            .Select(c => new ContainerRecord(
                c.Key, EntityId.Parse(c.InstanceId), c.HostCell,
                c.Items.Select(i => new ContainerItem(EntityId.Parse(i.ItemId), i.DefId, i.Count)).ToImmutableArray(),
                baselines.GetValueOrDefault(c.HostCell)))
            .ToImmutableArray();
        return (entities, created, containers);
    }

    public static ImmutableArray<EntityDeltaRecord> DecodeEntities(byte[] bytes) => DecodeEntitySection(bytes).Entities;

    /// <summary>The cell key of a slot key: <c>&lt;cell_key&gt;.&lt;population_id&gt;.&lt;ordinal&gt;</c>, and a cell key has no '.'.</summary>
    public static string HostCell(string slotKey)
    {
        int dot = slotKey.IndexOf('.');
        return dot > 0 ? slotKey[..dot] : slotKey;
    }

    /// <summary>Manifest JSON options, shared with the migration chain, which edits the manifest as JSON.</summary>
    public static JsonSerializerOptions ManifestOptions => ManifestJson;
}
