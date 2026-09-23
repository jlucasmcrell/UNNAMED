// UNNAMED Persistence - section encoding (PERSISTENCE.md §5.1-§5.3)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Text.Json;
using MessagePack;
using UNNAMED.Domain;
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
    [Key("inventory")] public InventoryDto[] Inventory { get; set; } = Array.Empty<InventoryDto>();
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
        Inventory = player.Inventory
            .Select(e => new InventoryDto { ItemId = e.ItemId.Value, DefId = e.DefId, Count = e.Count })
            .ToArray(),
    }, Options);

    public static PlayerRecord DecodePlayer(byte[] bytes)
    {
        var dto = MessagePackSerializer.Deserialize<PlayerDto>(bytes, Options);
        return new PlayerRecord(EntityId.Parse(dto.InstanceId), dto.Name, dto.XMm, dto.YMm, dto.ZMm,
            dto.Inventory.Select(e => new InventoryEntry(EntityId.Parse(e.ItemId), e.DefId, e.Count)));
    }

    public static byte[] EncodeCells(DeltaSnapshot snapshot) => MessagePackSerializer.Serialize(new CellsSectionDto
    {
        Records = snapshot.Cells.Select(c => new CellDto
        {
            CellKey = c.CellKey,
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
                c.DirtyReasons.ToImmutableArray(),
                c.Flags.Select(f => KeyValuePair.Create(f.Name, f.Value)).ToImmutableArray(),
                c.HarvestedNodes.Select(n => new NodeHarvest(n.NodeKey, n.LastHarvestTick, n.HarvestSeq)).ToImmutableArray(),
                c.PopulationAlive.Select(p => KeyValuePair.Create(p.PopulationId, p.Alive)).ToImmutableArray()))
            .ToImmutableArray();

    public static byte[] EncodeEntities(DeltaSnapshot snapshot) => MessagePackSerializer.Serialize(new EntitiesSectionDto
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
    }, Options);

    public static ImmutableArray<EntityDeltaRecord> DecodeEntities(byte[] bytes) =>
        MessagePackSerializer.Deserialize<EntitiesSectionDto>(bytes, Options).Records
            .Select(e => new EntityDeltaRecord(
                EntityId.Parse(e.InstanceId), e.SlotKey, e.GenerationSeq, e.DefId,
                e.State.Alive, e.State.XCm, e.State.ZCm))
            .ToImmutableArray();
}
