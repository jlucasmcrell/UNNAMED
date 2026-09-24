// UNNAMED Persistence - schema 5 section shapes (M3). FROZEN.
// No Godot references - pure C#
//
// The exact player shape the schema-5 writer produced: written by the 4 -> 5 step and read by the 5 -> 6 step. The
// schema-5 fixture pins it. Schema 6 changed the player and entities sections; the entities shape it read is the
// schema-3 one, unchanged through schema 5 (Sections/V3). The progression record's shape did not change at schema 6,
// so it is the current ProgressionDto; the step that next changes that DTO must freeze a copy of it first.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V5;

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
