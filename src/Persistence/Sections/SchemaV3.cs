// UNNAMED Persistence - schema 3 section shapes (M2b stage 2). FROZEN.
// No Godot references - pure C#
//
// The exact shapes the schema-3 writer produced: written by the 2 -> 3 step and read by the 3 -> 4
// step. The schema-3 fixture pins them. Schema 4 changed only the player section; the entities shape is
// frozen too so the 2 -> 3 step stays a fixed function whatever the current entities shape becomes.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V3;

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
}

[MessagePackObject]
public sealed class Inventory
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("count")] public int Count { get; set; }
}

[MessagePackObject]
public sealed class EntitiesSection
{
    [Key("records")] public Entity[] Records { get; set; } = Array.Empty<Entity>();
    [Key("created")] public Created[] Created { get; set; } = Array.Empty<Created>();
    [Key("baselines")] public CellBaseline[] Baselines { get; set; } = Array.Empty<CellBaseline>();
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
}

[MessagePackObject]
public sealed class CellBaseline
{
    [Key("cell_key")] public string CellKey { get; set; } = "";
    [Key("baseline_hash")] public string BaselineHash { get; set; } = "";
}
