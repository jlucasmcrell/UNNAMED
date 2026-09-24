// UNNAMED Persistence - schema 4 section shapes (M2c). FROZEN.
// No Godot references - pure C#
//
// The exact player shape the schema-4 writer produced: written by the 3 -> 4 step and read by the 4 -> 5
// step. The schema-4 fixture pins it. Schema 5 changed only the player section. The progression record's
// shape did not change at schema 5, so it is the current ProgressionDto; the step that next changes that
// DTO must freeze a copy of it here first.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V4;

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
}

[MessagePackObject]
public sealed class Inventory
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("count")] public int Count { get; set; }
}
