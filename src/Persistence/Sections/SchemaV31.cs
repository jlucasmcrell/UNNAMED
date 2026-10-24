// UNNAMED Persistence - schema 31 section shapes (the player of schema 31). FROZEN.
// No Godot references - pure C#
//
// The player shape schema 31 wrote: written by the 30 -> 31 step and read by the 31 -> 32 step; the schema-31 fixture pins it. Schema 32
// gave the live player its further action bars and gear sets and left every other part alone, so its other parts are the current DTOs; the
// step that next changes one of those must freeze a copy of it first. The quick bar of schema 31 is eight slots (QuickBarDto's own array;
// the 31 -> 32 step widens it to twelve). Schema 32 left the other sections alone.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V31;

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
    [Key("waystones")] public WaystoneDto[]? Waystones { get; set; }
    [Key("return_point")] public string? ReturnPoint { get; set; }
    [Key("roads_walked")] public string[]? RoadsWalked { get; set; }
    [Key("deeds")] public DeedDto[]? Deeds { get; set; }
    [Key("identity")] public V38.IdentityDto? Identity { get; set; }
    [Key("factions")] public FactionLedgerDto? Factions { get; set; }
    [Key("party")] public PartyLedgerDto? Party { get; set; }
    [Key("quick_bar")] public QuickBarDto? QuickBar { get; set; }
    [Key("party_sheets")] public PartySheetDto[]? PartySheets { get; set; }
    [Key("attack")] public AttackDto? Attack { get; set; }
    [Key("space_id")] public string? SpaceId { get; set; }
    [Key("space_traversal")] public string? SpaceTraversal { get; set; }
    [Key("findings")] public FindingDto[]? Findings { get; set; }
}
