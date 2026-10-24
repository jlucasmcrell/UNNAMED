// UNNAMED Persistence - schema 18 section shapes (the persistent clock, WP11). FROZEN.
// No Godot references - pure C#
//
// The exact stack shapes schemas 9 to 18 wrote - a carried stack, a container's item, a created instance - and the player and entities
// shapes schemas 17-18 (player) and 14-18 (entities) wrote around them: written by the steps that produce those schemas and read by the
// 18 -> 19 step; the schema-9 to -18 fixtures pin them. Their parts that schema 19 did not change are the current DTOs (ProgressionDto,
// DiscoveryDto, EquipmentDto, EffectDto, RelationshipDto, ConversationDto, QuestDto, V20.Companion, PostureDto, WaystoneDto, DeedDto,
// IdentityDto, EntityDto, CellBaselineDto, CreatureDto, NoiseDto); the step that next changes one of those must freeze a copy of it first.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V18;

[MessagePackObject]
public sealed class Inventory
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("count")] public int Count { get; set; }
    [Key("quality")] public int? Quality { get; set; }
}

[MessagePackObject]
public sealed class ContainerItem
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("count")] public int Count { get; set; }
    [Key("quality")] public int? Quality { get; set; }
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
public sealed class Created
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("x_cm")] public int XCm { get; set; }
    [Key("z_cm")] public int ZCm { get; set; }
    [Key("count")] public int? Count { get; set; }
    [Key("quality")] public int? Quality { get; set; }
}

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
    [Key("relationships")] public RelationshipDto[]? Relationships { get; set; }
    [Key("conversations")] public ConversationDto[]? Conversations { get; set; }
    [Key("quests")] public QuestDto[]? Quests { get; set; }
    [Key("companions")] public V20.Companion[]? Companions { get; set; }
    [Key("posture")] public PostureDto? Posture { get; set; }
    [Key("waystones")] public WaystoneDto[]? Waystones { get; set; }
    [Key("return_point")] public string? ReturnPoint { get; set; }
    [Key("roads_walked")] public string[]? RoadsWalked { get; set; }
    [Key("deeds")] public DeedDto[]? Deeds { get; set; }
    [Key("identity")] public V38.IdentityDto? Identity { get; set; }
}

[MessagePackObject]
public sealed class EntitiesSection
{
    [Key("records")] public EntityDto[] Records { get; set; } = Array.Empty<EntityDto>();
    [Key("created")] public Created[] Created { get; set; } = Array.Empty<Created>();
    [Key("baselines")] public CellBaselineDto[] Baselines { get; set; } = Array.Empty<CellBaselineDto>();
    [Key("containers")] public Container[]? Containers { get; set; }
    [Key("creatures")] public UNNAMED.Persistence.Sections.V37.CreatureDto[]? Creatures { get; set; }
    [Key("noises")] public NoiseDto[]? Noises { get; set; }
}
