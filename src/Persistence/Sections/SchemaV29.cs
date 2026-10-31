// UNNAMED Persistence - schema 29 section shapes (the player of schemas 27-29, and its companion record). FROZEN.
// No Godot references - pure C#
//
// The player shape schemas 27 to 29 wrote: written by the 26 -> 27 step and read by the 29 -> 30 step; the schema-27, -28 and -29
// fixtures pin it. Its companion record is frozen here too, since schema 30 gave the live one its space: every older frozen player
// names this one. Their other parts that schema 30 did not change are the current DTOs (InventoryDto, ProgressionDto, DiscoveryDto,
// EquipmentDto, EffectDto, RelationshipDto, ConversationDto, QuestDto, PostureDto, WaystoneDto, DeedDto, IdentityDto, FactionLedgerDto,
// PartyLedgerDto, QuickBarDto, PartySheetDto, AttackDto, TacticsDto, ContractDto); the step that next changes one of those must freeze a
// copy of it first. Schema 30 left the other sections alone.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V29;

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
    [Key("companions")] public Companion[]? Companions { get; set; }
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
}

/// <summary>The companion record of schemas 21 to 29 (and, with no party fields, of 12 to 20 as the 20 -> 21 step writes it).</summary>
[MessagePackObject]
public sealed class Companion
{
    [Key("npc_id")] public string NpcId { get; set; } = "";
    [Key("order")] public string Order { get; set; } = "";
    [Key("condition")] public string Condition { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("facing_mdeg")] public int FacingMdeg { get; set; }
    [Key("health")] public int Health { get; set; }
    [Key("downed_tick")] public long DownedTick { get; set; }
    [Key("stuck_ticks")] public int StuckTicks { get; set; }
    [Key("last_combat_tick")] public long LastCombatTick { get; set; }
    [Key("trail_mm")] public long[] TrailMm { get; set; } = Array.Empty<long>();
    [Key("membership")] public string? Membership { get; set; }
    [Key("stance")] public string? Stance { get; set; }
    [Key("tactics")] public TacticsDto? Tactics { get; set; }
    [Key("contract")] public ContractDto? Contract { get; set; }
}
