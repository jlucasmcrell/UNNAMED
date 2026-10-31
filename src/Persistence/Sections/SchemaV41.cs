// UNNAMED Persistence - schema 41 section shapes (the stacks and the entity section as schema 41 wrote them). FROZEN.
// No Godot references - pure C#
//
// Schema 42 gave a carried stack and a stack in a container its gear build (D21). The stack shapes as schema 41 wrote them are frozen here, and
// every older frozen shape that named the live stack DTOs now names these; the entity section is frozen because its containers hold the stacks.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V41;

[MessagePackObject]
public sealed class InventoryDto
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("count")] public int Count { get; set; }
    [Key("quality")] public int? Quality { get; set; }
    [Key("notable")] public NotableDto? Notable { get; set; }
    [Key("wear")] public WearDto? Wear { get; set; }
}

[MessagePackObject]
public sealed class ContainerDto
{
    [Key("key")] public string Key { get; set; } = "";
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("items")] public ContainerItemDto[] Items { get; set; } = Array.Empty<ContainerItemDto>();
}

[MessagePackObject]
public sealed class ContainerItemDto
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("count")] public int Count { get; set; }
    [Key("quality")] public int? Quality { get; set; }
    [Key("notable")] public NotableDto? Notable { get; set; }
    [Key("wear")] public WearDto? Wear { get; set; }
}

[MessagePackObject]
public sealed class EntitySection
{
    [Key("records")] public EntityDto[] Records { get; set; } = Array.Empty<EntityDto>();
    [Key("created")] public CreatedDto[] Created { get; set; } = Array.Empty<CreatedDto>();
    [Key("baselines")] public CellBaselineDto[] Baselines { get; set; } = Array.Empty<CellBaselineDto>();
    [Key("containers")] public ContainerDto[]? Containers { get; set; }
    [Key("creatures")] public CreatureDto[]? Creatures { get; set; }
    [Key("noises")] public NoiseDto[]? Noises { get; set; }
    [Key("economy")] public EconomyDto? Economy { get; set; }
    [Key("merchants")] public MerchantDto[]? Merchants { get; set; }
    [Key("npc_life")] public NpcLifeDto? NpcLife { get; set; }
    [Key("adventurers")] public AdventurersDto? Adventurers { get; set; }
}
