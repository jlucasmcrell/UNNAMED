// UNNAMED Persistence - schema 19 section shapes (notable instances, WP3a). FROZEN.
// No Godot references - pure C#
//
// The exact player shape schema 19 wrote: written by the 18 -> 19 step and read by the 19 -> 20 step; the schema-19 fixture pins it. Its
// companions are V20.Companion; its parts that schema 20 did not change are the current DTOs (InventoryDto, ProgressionDto, DiscoveryDto,
// EquipmentDto, EffectDto, RelationshipDto, ConversationDto, QuestDto, PostureDto, WaystoneDto, DeedDto, IdentityDto); the step that next
// changes one of those must freeze a copy of it first. Schema 20 left the other sections alone.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V19;

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
    [Key("companions")] public V20.Companion[]? Companions { get; set; }
    [Key("posture")] public PostureDto? Posture { get; set; }
    [Key("waystones")] public WaystoneDto[]? Waystones { get; set; }
    [Key("return_point")] public string? ReturnPoint { get; set; }
    [Key("roads_walked")] public string[]? RoadsWalked { get; set; }
    [Key("deeds")] public DeedDto[]? Deeds { get; set; }
    [Key("identity")] public IdentityDto? Identity { get; set; }
}
