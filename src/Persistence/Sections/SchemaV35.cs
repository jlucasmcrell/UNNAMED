// UNNAMED Persistence - schema 35 section shapes (the player of schema 35). FROZEN.
// No Godot references - pure C#
//
// The player shape schema 35 wrote: written by the 34 -> 35 step and read by the 35 -> 36 step; the schema-35 fixture pins it. Schema 36 gave the
// live player the animals that travel with it (spec 43), and left every other part alone, so its other parts are the current DTOs; the step that next
// changes one of those must freeze a copy of it first.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V35;

using InventoryDto = UNNAMED.Persistence.Sections.V41.InventoryDto;

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
    [Key("extra_bars")] public string?[][]? ExtraBars { get; set; }
    [Key("gear_sets")] public GearSetDto[]? GearSets { get; set; }
    [Key("pack")] public PackDto? Pack { get; set; }
    [Key("held_family")] public string? HeldFamily { get; set; }
    [Key("family_bars")] public FamilyBarDto[]? FamilyBars { get; set; }
    [Key("focus_targets")] public string[]? FocusTargets { get; set; }
    [Key("lore")] public LorePageDto[]? Lore { get; set; }
}
