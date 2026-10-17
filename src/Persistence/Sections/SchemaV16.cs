// UNNAMED Persistence - schema 16 section shapes (deed tallies, WP5). FROZEN.
// No Godot references - pure C#
//
// The exact player shape schema 16 wrote: written by the 15 -> 16 step and read by the 16 -> 17 step; the schema-16 fixture pins it. Its stacks are V18.Inventory; its
// parts that schema 17 did not change are the current DTOs (ProgressionDto, DiscoveryDto, EquipmentDto, EffectDto,
// RelationshipDto, ConversationDto, QuestDto, V20.Companion, PostureDto, WaystoneDto, DeedDto); the step that next changes one of those must
// freeze a copy of it first. Schema 17 left the other sections alone.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V16;

[MessagePackObject]
public sealed class Player
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("name")] public string Name { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("y_mm")] public long YMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("appearance_seed")] public ulong AppearanceSeed { get; set; }
    [Key("inventory")] public V18.Inventory[] Inventory { get; set; } = Array.Empty<V18.Inventory>();
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
}
