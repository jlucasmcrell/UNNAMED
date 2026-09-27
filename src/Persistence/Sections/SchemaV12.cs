// UNNAMED Persistence - schema 12 section shapes (M6). FROZEN.
// No Godot references - pure C#
//
// The exact player shape schema 12 wrote: written by the 11 -> 12 step and read by the 12 -> 13 step; the schema-12 fixture pins
// it. Its parts that schema 13 did not change are the current DTOs (InventoryDto, ProgressionDto, DiscoveryDto, EquipmentDto,
// EffectDto, RelationshipDto, ConversationDto, QuestDto), and its companions are V14.Companion, the shape schemas 12-14 wrote (schema
// 15 froze it); the step that next changes one of those must freeze a copy of it first. Schema 13 left the other sections alone.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V12;

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
    [Key("companions")] public V14.Companion[]? Companions { get; set; }
}
