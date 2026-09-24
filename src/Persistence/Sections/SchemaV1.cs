// UNNAMED Persistence - schema 1 section shapes (M2). FROZEN.
// No Godot references - pure C#
//
// The exact shapes M2 wrote, read only by the schema 1 -> 2 migration. The current codec may change
// freely; these may not, or schema-1 saves would be misread. The schema-1 fixture pins them.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V1;

[MessagePackObject]
public sealed class CellsSection
{
    [Key("records")] public Cell[] Records { get; set; } = Array.Empty<Cell>();
}

[MessagePackObject]
public sealed class Cell
{
    [Key("cell_key")] public string CellKey { get; set; } = "";
    [Key("dirty_reasons")] public string[] DirtyReasons { get; set; } = Array.Empty<string>();
    [Key("flags")] public Flag[] Flags { get; set; } = Array.Empty<Flag>();

    /// <summary>Keys are <c>node.&lt;cell_key&gt;.&lt;generation index&gt;</c>: positions in worldgen 1's node list.</summary>
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
