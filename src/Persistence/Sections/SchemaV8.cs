// UNNAMED Persistence - schema 8 section shapes (M3c, M3d). FROZEN.
// No Godot references - pure C#
//
// The exact player shape schemas 7 and 8 wrote: written by the 6 -> 7 step and read by the 8 -> 9 step; and the entities
// shape schema 8 wrote: written by the 7 -> 8 step and read by the 8 -> 9 step. The schema-7 and -8 fixtures pin them.
// Their parts that schema 9 did not change are the current DTOs (ProgressionDto, DiscoveryDto, EquipmentDto, EffectDto,
// EntityDto, CellBaselineDto, CreatureDto); the step that next changes one of those must freeze a copy of it first.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V8;

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
    [Key("discoveries")] public DiscoveryDto[]? Discoveries { get; set; }
    [Key("equipment")] public EquipmentDto[]? Equipment { get; set; }
    [Key("currency")] public long? Currency { get; set; }
    [Key("effects")] public EffectDto[]? Effects { get; set; }
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
    [Key("records")] public EntityDto[] Records { get; set; } = Array.Empty<EntityDto>();
    [Key("created")] public Created[] Created { get; set; } = Array.Empty<Created>();
    [Key("baselines")] public CellBaselineDto[] Baselines { get; set; } = Array.Empty<CellBaselineDto>();
    [Key("containers")] public Container[]? Containers { get; set; }
    [Key("creatures")] public CreatureDto[]? Creatures { get; set; }
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
