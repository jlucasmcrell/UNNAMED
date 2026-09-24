// UNNAMED Persistence - schema 9 section shapes (M3f). FROZEN.
// No Godot references - pure C#
//
// The exact player shape schema 9 wrote: written by the 8 -> 9 step and read by the 9 -> 10 step; the schema-9 fixture pins
// it. Its parts that schema 10 did not change are the current DTOs (InventoryDto, ProgressionDto, DiscoveryDto, EquipmentDto,
// EffectDto); the step that next changes one of those must freeze a copy of it first. Schema 10 left the other sections alone,
// and so did schema 11.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V9;

[MessagePackObject]
public sealed class Player
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("name")] public string Name { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("y_mm")] public long YMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("appearance_seed")] public ulong AppearanceSeed { get; set; }
    [Key("inventory")] public InventoryDto[] Inventory { get; set; } = Array.Empty<InventoryDto>();
    [Key("progression")] public ProgressionDto? Progression { get; set; }
    [Key("facing_mdeg")] public int? FacingMdeg { get; set; }
    [Key("discoveries")] public DiscoveryDto[]? Discoveries { get; set; }
    [Key("equipment")] public EquipmentDto[]? Equipment { get; set; }
    [Key("currency")] public long? Currency { get; set; }
    [Key("effects")] public EffectDto[]? Effects { get; set; }
}
