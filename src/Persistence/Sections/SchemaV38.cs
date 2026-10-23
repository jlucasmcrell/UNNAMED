// UNNAMED Persistence - schema 38 section shapes (the player of schemas 36-38, and the identity of schemas 17-38). FROZEN.
// No Godot references - pure C#
//
// The player shape schemas 36 to 38 wrote: written by the 35 -> 36 step and read by the 38 -> 39 step; the schema-38 fixture pins it. Schema 39
// gave the live identity the creator's appearance and left every other part alone, so its other parts are the current DTOs; the step that next
// changes one of those must freeze a copy of it first. Every frozen player from schema 17 on names this identity.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V38;

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
    [Key("identity")] public IdentityDto? Identity { get; set; }
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
    [Key("pets")] public PetDto[]? Pets { get; set; }
}

/// <summary>The identity schemas 17-38 wrote: no appearance.</summary>
[MessagePackObject]
public sealed class IdentityDto
{
    [Key("pronouns")] public string Pronouns { get; set; } = "";
    [Key("people")] public string People { get; set; } = "";
    [Key("body_preset")] public string BodyPreset { get; set; } = "";
    [Key("call")] public string? Call { get; set; }
}
