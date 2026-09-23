// UNNAMED Persistence - schema 6 section shapes (M3b). FROZEN.
// No Godot references - pure C#
//
// The exact player shape the schema-6 writer produced: written by the 5 -> 6 step and read by the 6 -> 7 step. The
// schema-6 fixture pins it. Schema 7 changed only the player section, so the entities section shape is still the
// current EntitiesSectionDto; the progression record's shape did not change either, so it is the current ProgressionDto.
// The step that next changes either DTO must freeze a copy of it first.

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
