// UNNAMED Persistence - schema 2 section shapes (M2b stage 1). FROZEN.
// No Godot references - pure C#
//
// The exact shapes the schema-2 writer produced: written by the 1 -> 2 step and read by the 2 -> 3
// step. The schema-2 fixture pins them. At each schema bump the previous current shape is frozen here
// the same way, and the step that produces it is repointed to the frozen types.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V2;

[MessagePackObject]
public sealed class Player
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("name")] public string Name { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("y_mm")] public long YMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
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
public sealed class CellsSection
{
    [Key("records")] public Cell[] Records { get; set; } = Array.Empty<Cell>();
}

[MessagePackObject]
public sealed class Cell
{
    [Key("cell_key")] public string CellKey { get; set; } = "";
    [Key("baseline_hash")] public string BaselineHash { get; set; } = "";
    [Key("dirty_reasons")] public string[] DirtyReasons { get; set; } = Array.Empty<string>();
    [Key("flags")] public Flag[] Flags { get; set; } = Array.Empty<Flag>();
    [Key("harvested_nodes")] public Node[] HarvestedNodes { get; set; } = Array.Empty<Node>();
    [Key("population_alive")] public Population[] PopulationAlive { get; set; } = Array.Empty<Population>();
}

[MessagePackObject]
public sealed class Flag
{
    [Key("name")] public string Name { get; set; } = "";
    [Key("value")] public long Value { get; set; }
}

[MessagePackObject]
public sealed class Node
{
    [Key("node_key")] public string NodeKey { get; set; } = "";
    [Key("last_harvest_tick")] public long LastHarvestTick { get; set; }
    [Key("harvest_seq")] public int HarvestSeq { get; set; }
}

[MessagePackObject]
public sealed class Population
{
    [Key("population_id")] public string PopulationId { get; set; } = "";
    [Key("alive")] public int Alive { get; set; }
}

[MessagePackObject]
public sealed class EntitiesSection
{
    [Key("records")] public Entity[] Records { get; set; } = Array.Empty<Entity>();
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
public sealed class CellBaseline
{
    [Key("cell_key")] public string CellKey { get; set; } = "";
    [Key("baseline_hash")] public string BaselineHash { get; set; } = "";
}
