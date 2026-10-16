// UNNAMED Persistence - schema 14 section shapes (the Phase-1 technical audit). FROZEN.
// No Godot references - pure C#
//
// The exact player shape schemas 13 and 14 wrote: written by the 12 -> 13 step and read by the 14 -> 15 step; the schema-13 and -14
// fixtures pin it. Its parts that schema 15 did not change are the current DTOs (InventoryDto, ProgressionDto, DiscoveryDto, EquipmentDto,
// EffectDto, RelationshipDto, ConversationDto, QuestDto, CompanionDto, PostureDto); the step that next changes one of those must freeze a
// copy of it first. Schema 15 left the other sections alone.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V14;

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
    [Key("companions")] public CompanionDto[]? Companions { get; set; }
    [Key("posture")] public PostureDto? Posture { get; set; }
}
