// UNNAMED Persistence - schema 6 section shapes (M3b). FROZEN.
// No Godot references - pure C#
//
// The exact player shape the schema-6 writer produced: written by the 5 -> 6 step and read by the 6 -> 7 step; and the
// entities shape schemas 6 and 7 wrote: written by the 5 -> 6 step and read by the 7 -> 8 step. The schema-6 and -7
// fixtures pin them. The progression record's shape did not change, so it is the current ProgressionDto; the step that
// next changes that DTO must freeze a copy of it first.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V6;

[MessagePackObject]
public sealed class Player
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("name")] public string Name { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("y_mm")] public long YMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("appearance_seed")] public ulong AppearanceSeed { get; set; }
    [Key("inventory")] public Inventory[] Inventory { get; set; } = Array.Empty<Inventory>();
    [Key("progression")] public ProgressionDto? Progression { get; set; }
    [Key("facing_mdeg")] public int? FacingMdeg { get; set; }
    [Key("discoveries")] public Discovery[]? Discoveries { get; set; }
    [Key("equipment")] public Equipment[]? Equipment { get; set; }
    [Key("currency")] public long? Currency { get; set; }
}

[MessagePackObject]
public sealed class Inventory
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("count")] public int Count { get; set; }
}

[MessagePackObject]
public sealed class Discovery
{
    [Key("location_id")] public string LocationId { get; set; } = "";
    [Key("method")] public string Method { get; set; } = "";
    [Key("tick")] public long Tick { get; set; }
}

[MessagePackObject]
public sealed class Equipment
{
    [Key("slot")] public string Slot { get; set; } = "";
    [Key("item_id")] public string ItemId { get; set; } = "";
}

[MessagePackObject]
public sealed class EntitiesSection
{
    [Key("records")] public Entity[] Records { get; set; } = Array.Empty<Entity>();
    [Key("created")] public Created[] Created { get; set; } = Array.Empty<Created>();
    [Key("baselines")] public CellBaseline[] Baselines { get; set; } = Array.Empty<CellBaseline>();
    [Key("containers")] public Container[]? Containers { get; set; }
}

[MessagePackObject]
public sealed class Entity
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("slot_key")] public string SlotKey { get; set; } = "";
    [Key("generation_seq")] public int GenerationSeq { get; set; }
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("dirty_mask")] public int DirtyMask { get; set; }
    [Key("state")] public EntityState State { get; set; } = new();
}

[MessagePackObject]
public sealed class EntityState
{
    [Key("alive")] public bool? Alive { get; set; }
    [Key("x_cm")] public int? XCm { get; set; }
    [Key("z_cm")] public int? ZCm { get; set; }
}

[MessagePackObject]
public sealed class Created
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("x_cm")] public int XCm { get; set; }
    [Key("z_cm")] public int ZCm { get; set; }
    [Key("count")] public int? Count { get; set; }
}

[MessagePackObject]
public sealed class CellBaseline
{
    [Key("cell_key")] public string CellKey { get; set; } = "";
    [Key("baseline_hash")] public string BaselineHash { get; set; } = "";
}

[MessagePackObject]
public sealed class Container
{
    [Key("key")] public string Key { get; set; } = "";
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("items")] public ContainerItem[] Items { get; set; } = Array.Empty<ContainerItem>();
}

[MessagePackObject]
public sealed class ContainerItem
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("count")] public int Count { get; set; }
}
