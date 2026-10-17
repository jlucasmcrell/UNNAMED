// UNNAMED Persistence - schema 24 section shapes (durability, WP3b). FROZEN.
// No Godot references - pure C#
//
// The exact player shape schema 24 wrote: written by the 23 -> 24 step and read by the 24 -> 25 step; the schema-24 fixture pins it. Its
// parts that schema 25 did not change are the current DTOs (InventoryDto, ProgressionDto, DiscoveryDto, EquipmentDto, EffectDto,
// RelationshipDto, ConversationDto, QuestDto, the V29 companion, PostureDto, WaystoneDto, DeedDto, IdentityDto, FactionLedgerDto,
// PartyLedgerDto, QuickBarDto); the step that next changes one of those must freeze a copy of it first. Schema 25 left the other sections
// alone.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V24;

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
