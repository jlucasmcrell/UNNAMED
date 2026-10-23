// UNNAMED Persistence - schema 23 section shapes (the quick bar, WP9). FROZEN.
// No Godot references - pure C#
//
// The exact stack shapes schemas 19 to 23 wrote - a carried stack, a container and its items, a created instance - and the player shape
// schema 23 and the entities shape schemas 22 and 23 wrote around them: written by the steps that produce those schemas and read by the
// 23 -> 24 step; the schema-19 to -23 fixtures pin them. Their parts that schema 24 did not change are the current DTOs (NotableDto,
// ProgressionDto, DiscoveryDto, EquipmentDto, EffectDto, RelationshipDto, ConversationDto, QuestDto, the V29 companion, PostureDto, WaystoneDto,
// DeedDto, IdentityDto, FactionLedgerDto, PartyLedgerDto, QuickBarDto, EntityDto, CellBaselineDto, CreatureDto, NoiseDto, EconomyDto,
// MerchantDto); the step that next changes one of those must freeze a copy of it first.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V23;

[MessagePackObject]
public sealed class Inventory
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("count")] public int Count { get; set; }
    [Key("quality")] public int? Quality { get; set; }
    [Key("notable")] public NotableDto? Notable { get; set; }
}

[MessagePackObject]
public sealed class ContainerItem
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("count")] public int Count { get; set; }
    [Key("quality")] public int? Quality { get; set; }
    [Key("notable")] public NotableDto? Notable { get; set; }
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
    [Key("notable")] public NotableDto? Notable { get; set; }
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
    [Key("companions")] public V29.Companion[]? Companions { get; set; }
    [Key("posture")] public PostureDto? Posture { get; set; }
    [Key("waystones")] public WaystoneDto[]? Waystones { get; set; }
    [Key("return_point")] public string? ReturnPoint { get; set; }
    [Key("roads_walked")] public string[]? RoadsWalked { get; set; }
    [Key("deeds")] public DeedDto[]? Deeds { get; set; }
    [Key("identity")] public IdentityDto? Identity { get; set; }
    [Key("factions")] public FactionLedgerDto? Factions { get; set; }
    [Key("party")] public PartyLedgerDto? Party { get; set; }
    [Key("quick_bar")] public QuickBarDto? QuickBar { get; set; }
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
    [Key("economy")] public EconomyDto? Economy { get; set; }
    [Key("merchants")] public MerchantDto[]? Merchants { get; set; }
}
