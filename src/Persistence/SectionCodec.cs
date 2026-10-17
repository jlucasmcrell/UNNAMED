// UNNAMED Persistence - section encoding (PERSISTENCE.md §5.1-§5.3)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Text.Json;
using MessagePack;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Economy;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.Domain.Travel;
using UNNAMED.World;

namespace UNNAMED.Persistence.Sections;

// Section files are string-keyed MessagePack maps, so a dump is self-describing: an AI session
// diagnosing a persistence bug can read one (D-04's rationale for legible IDs applies here too).

[MessagePackObject]
public sealed class PlayerDto
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("name")] public string Name { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("y_mm")] public long YMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("appearance_seed")] public ulong AppearanceSeed { get; set; }
    [Key("inventory")] public InventoryDto[] Inventory { get; set; } = Array.Empty<InventoryDto>();

    /// <summary>Required from schema 4. The 3 -> 4 step gives older saves the empty record.</summary>
    [Key("progression")] public ProgressionDto? Progression { get; set; }

    /// <summary>Required from schema 5. The 4 -> 5 step gives older saves facing 0 (+Z).</summary>
    [Key("facing_mdeg")] public int? FacingMdeg { get; set; }

    /// <summary>Required from schema 5. The 4 -> 5 step gives older saves no discoveries.</summary>
    [Key("discoveries")] public DiscoveryDto[]? Discoveries { get; set; }

    /// <summary>Required from schema 6. The 5 -> 6 step gives older saves nothing equipped.</summary>
    [Key("equipment")] public EquipmentDto[]? Equipment { get; set; }

    /// <summary>Required from schema 6. The 5 -> 6 step gives older saves an empty purse.</summary>
    [Key("currency")] public long? Currency { get; set; }

    /// <summary>Required from schema 7. The 6 -> 7 step gives older saves no effects.</summary>
    [Key("effects")] public EffectDto[]? Effects { get; set; }

    /// <summary>Required from schema 10. The 9 -> 10 step gives older saves none: no NPC could think anything of anyone before M4.</summary>
    [Key("relationships")] public RelationshipDto[]? Relationships { get; set; }

    /// <summary>Required from schema 10. The 9 -> 10 step gives older saves none: there was no one to talk to before M4.</summary>
    [Key("conversations")] public ConversationDto[]? Conversations { get; set; }

    /// <summary>Required from schema 11. The 10 -> 11 step gives older saves none: there were no quests before M5.</summary>
    [Key("quests")] public QuestDto[]? Quests { get; set; }

    /// <summary>Required from schema 12. The 11 -> 12 step gives older saves none: no one could join before M6.</summary>
    [Key("companions")] public CompanionDto[]? Companions { get; set; }

    /// <summary>Required from schema 13. The 12 -> 13 step gives older saves a body standing on the ground (the owner's M6 playtest).</summary>
    [Key("posture")] public PostureDto? Posture { get; set; }

    /// <summary>Required from schema 15. The 14 -> 15 step gives older saves none: there was no stone to know before Waystone travel.</summary>
    [Key("waystones")] public WaystoneDto[]? Waystones { get; set; }

    /// <summary>From schema 15: the stone chosen as the return point, or null while none is (the region's spawn).</summary>
    [Key("return_point")] public string? ReturnPoint { get; set; }

    /// <summary>Required from schema 15. The 14 -> 15 step gives older saves none: no road was on the map before it.</summary>
    [Key("roads_walked")] public string[]? RoadsWalked { get; set; }

    /// <summary>Required from schema 16. The 15 -> 16 step gives older saves none: no deed was counted before the deed condition (E-7).</summary>
    [Key("deeds")] public DeedDto[]? Deeds { get; set; }

    /// <summary>
    /// Required from schema 17. The 16 -> 17 step gives older saves the identity of a character made before the creator: "they", a Veth of
    /// the default body, and no Call.
    /// </summary>
    [Key("identity")] public IdentityDto? Identity { get; set; }

    /// <summary>Required from schema 20. The 19 -> 20 step gives older saves the empty ledger: there was no faction to tell before WP6.</summary>
    [Key("factions")] public FactionLedgerDto? Factions { get; set; }

    /// <summary>
    /// Required from schema 21 (WP15a): companions recovering from a grave wound, and hirelings who died in the character's service. The
    /// 20 -> 21 step gives older saves none: no one could be wounded away or die in the party before.
    /// </summary>
    [Key("party")] public PartyLedgerDto? Party { get; set; }

    /// <summary>
    /// Required from schema 23 (WP9): the eight quick slots, and what the player took off the bar on purpose. The 22 -> 23 step gives older
    /// saves an empty bar with nothing taken off, which what they carry and know fills on load.
    /// </summary>
    [Key("quick_bar")] public QuickBarDto? QuickBar { get; set; }

    /// <summary>
    /// Required from schema 25 (WP15b): each past or present party member's progression, gear and belongings. The 24 -> 25 step gives older
    /// saves none: no party member had grown, worn or kept anything before.
    /// </summary>
    [Key("party_sheets")] public PartySheetDto[]? PartySheets { get; set; }

    /// <summary>
    /// From schema 27: the attack the character had under way, or null when none. The 26 -> 27 step gives older saves none: a load started
    /// every attack over.
    /// </summary>
    [Key("attack")] public AttackDto? Attack { get; set; }
}

/// <summary>An attack under way (schema 27): what makes the blow, the tick it began, and the creatures it has struck.</summary>
[MessagePackObject]
public sealed class AttackDto
{
    [Key("source")] public string Source { get; set; } = "";
    [Key("start_tick")] public long StartTick { get; set; }
    [Key("struck")] public string[] Struck { get; set; } = Array.Empty<string>();
}

/// <summary>
/// The quick bar (schema 23): each slot a definition ID or nil, and the IDs the player cleared off it on purpose, which a login does not
/// offer a slot again.
/// </summary>
[MessagePackObject]
public sealed class QuickBarDto
{
    [Key("slots")] public string?[] Slots { get; set; } = Array.Empty<string?>();
    [Key("cleared")] public string[] Cleared { get; set; } = Array.Empty<string>();
}

/// <summary>A party member's sheet (schema 25): their progression, what they wear and whether it was loaned, and their own things.</summary>
[MessagePackObject]
public sealed class PartySheetDto
{
    [Key("npc_id")] public string NpcId { get; set; } = "";
    [Key("progression")] public ProgressionDto Progression { get; set; } = new();
    [Key("gear")] public PartyGearDto[] Gear { get; set; } = Array.Empty<PartyGearDto>();
    [Key("belongings")] public InventoryDto[] Belongings { get; set; } = Array.Empty<InventoryDto>();
}

[MessagePackObject]
public sealed class PartyGearDto
{
    [Key("slot")] public string Slot { get; set; } = "";
    [Key("loaned")] public bool Loaned { get; set; }
    [Key("item")] public InventoryDto Item { get; set; } = new();
}

/// <summary>The character's faction ledger (schema 20): the acts logged, what each faction knows of them, and the standing each holds.</summary>
[MessagePackObject]
public sealed class FactionLedgerDto
{
    [Key("next_act_seq")] public long NextActSeq { get; set; }
    [Key("acts")] public ActDto[] Acts { get; set; } = Array.Empty<ActDto>();
    [Key("knowledge")] public FactionKnowledgeDto[] Knowledge { get; set; } = Array.Empty<FactionKnowledgeDto>();
    [Key("standing")] public FactionStandingDto[] Standing { get; set; } = Array.Empty<FactionStandingDto>();
}

/// <summary>An act some faction reacts to, where and when the character did it (schema 20).</summary>
[MessagePackObject]
public sealed class ActDto
{
    [Key("seq")] public long Seq { get; set; }
    [Key("kind")] public string Kind { get; set; } = "";
    [Key("subject")] public string Subject { get; set; } = "";
    [Key("cell")] public string Cell { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("tick")] public long Tick { get; set; }
}

/// <summary>What one faction knows of one act (schema 20).</summary>
[MessagePackObject]
public sealed class FactionKnowledgeDto
{
    [Key("knower")] public string Knower { get; set; } = "";
    [Key("act")] public long Act { get; set; }
    [Key("identity")] public string Identity { get; set; } = "";
    [Key("source")] public string Source { get; set; } = "";
    [Key("via")] public string? Via { get; set; }
    [Key("tick")] public long Tick { get; set; }
    [Key("delta")] public int Delta { get; set; }
}

/// <summary>The character's points with one faction, never 0 (schema 20).</summary>
[MessagePackObject]
public sealed class FactionStandingDto
{
    [Key("faction")] public string Faction { get; set; } = "";
    [Key("points")] public int Points { get; set; }
}

/// <summary>Who is recovering at home, until when, and who has fallen (schema 21).</summary>
[MessagePackObject]
public sealed class PartyLedgerDto
{
    [Key("recovering")] public RecoveryDto[] Recovering { get; set; } = Array.Empty<RecoveryDto>();
    [Key("fallen")] public string[] Fallen { get; set; } = Array.Empty<string>();
}

[MessagePackObject]
public sealed class RecoveryDto
{
    [Key("npc_id")] public string NpcId { get; set; } = "";
    [Key("until_tick")] public long UntilTick { get; set; }
}

/// <summary>A party member's standing rules (schema 21).</summary>
[MessagePackObject]
public sealed class TacticsDto
{
    [Key("keep_to_leash")] public bool KeepToLeash { get; set; }
    [Key("protect_injured")] public bool ProtectInjured { get; set; }
    [Key("hold_healing")] public bool HoldHealing { get; set; }
}

/// <summary>A hireling's contract, and how it stands (schema 21).</summary>
[MessagePackObject]
public sealed class ContractDto
{
    [Key("role")] public string Role { get; set; } = "";
    [Key("days")] public int Days { get; set; }
    [Key("daily_wage")] public long DailyWage { get; set; }
    [Key("loot_rule")] public string LootRule { get; set; } = "";
    [Key("days_served")] public int DaysServed { get; set; }
    [Key("unpaid_days")] public int UnpaidDays { get; set; }
    [Key("purse")] public long Purse { get; set; }
    [Key("work_site")] public string? WorkSite { get; set; }
    [Key("recipe_id")] public string? RecipeId { get; set; }
    [Key("supply")] public SupplyDto[] Supply { get; set; } = Array.Empty<SupplyDto>();
}

[MessagePackObject]
public sealed class SupplyDto
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("count")] public int Count { get; set; }
}

/// <summary>How many times the character has done one deed, and when last (schema 16).</summary>
[MessagePackObject]
public sealed class DeedDto
{
    [Key("deed")] public string Deed { get; set; } = "";
    [Key("subject")] public string Subject { get; set; } = "";
    [Key("npc")] public string? Npc { get; set; }
    [Key("count")] public int Count { get; set; }
    [Key("last_tick")] public long LastTick { get; set; }
}

/// <summary>Who the character is besides their name, as the creator made them (schema 17). No World Rules or difficulty: there are none.</summary>
[MessagePackObject]
public sealed class IdentityDto
{
    [Key("pronouns")] public string Pronouns { get; set; } = "";
    [Key("people")] public string People { get; set; } = "";
    [Key("body_preset")] public string BodyPreset { get; set; } = "";
    [Key("call")] public string? Call { get; set; }
}

/// <summary>A Waystone the character knows (schema 15).</summary>
[MessagePackObject]
public sealed class WaystoneDto
{
    [Key("stone")] public string Stone { get; set; } = "";
    [Key("region")] public string Region { get; set; } = "";
    [Key("method")] public string Method { get; set; } = "";
    [Key("tick")] public long Tick { get; set; }
}

/// <summary>Standing or crouched, and how far into a jump (schema 13).</summary>
[MessagePackObject]
public sealed class PostureDto
{
    [Key("stance")] public string Stance { get; set; } = "";
    [Key("airborne")] public bool Airborne { get; set; }
    [Key("air_ms")] public int AirMs { get; set; }
}

[MessagePackObject]
public sealed class CompanionDto
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

    /// <summary>The trail being walked, as x, z pairs in millimetres.</summary>
    [Key("trail_mm")] public long[] TrailMm { get; set; } = Array.Empty<long>();

    /// <summary>Required from schema 21 (WP15a): companion, hireling or temporary_ally. The 20 -> 21 step makes every older one a companion.</summary>
    [Key("membership")] public string? Membership { get; set; }

    /// <summary>Required from schema 21: aggressive, defensive or passive. The 20 -> 21 step gives aggressive, the behaviour as built.</summary>
    [Key("stance")] public string? Stance { get; set; }

    /// <summary>Required from schema 21. The 20 -> 21 step gives the leash kept and nothing else.</summary>
    [Key("tactics")] public TacticsDto? Tactics { get; set; }

    /// <summary>From schema 21: a hireling's contract; absent for everyone else.</summary>
    [Key("contract")] public ContractDto? Contract { get; set; }
}

[MessagePackObject]
public sealed class QuestDto
{
    [Key("quest_id")] public string QuestId { get; set; } = "";
    [Key("status")] public string Status { get; set; } = "";
    [Key("started_tick")] public long StartedTick { get; set; }
    [Key("ended_tick")] public long? EndedTick { get; set; }
    [Key("ended_by")] public string? EndedBy { get; set; }
    [Key("objectives")] public ObjectiveDto[] Objectives { get; set; } = Array.Empty<ObjectiveDto>();
}

[MessagePackObject]
public sealed class ObjectiveDto
{
    [Key("id")] public string Id { get; set; } = "";
    [Key("status")] public string Status { get; set; } = "";
    [Key("activated_tick")] public long ActivatedTick { get; set; }
    [Key("ended_tick")] public long? EndedTick { get; set; }
    [Key("progress")] public int Progress { get; set; }
}

[MessagePackObject]
public sealed class RelationshipDto
{
    [Key("npc_id")] public string NpcId { get; set; } = "";
    [Key("dimension")] public string Dimension { get; set; } = "";
    [Key("value")] public int Value { get; set; }
}

[MessagePackObject]
public sealed class ConversationDto
{
    [Key("dialogue_id")] public string DialogueId { get; set; } = "";
    [Key("heard")] public string[] Heard { get; set; } = Array.Empty<string>();
}

[MessagePackObject]
public sealed class EffectDto
{
    [Key("effect_id")] public string EffectId { get; set; } = "";
    [Key("stacks")] public int Stacks { get; set; }
    [Key("expires_tick")] public long ExpiresTick { get; set; }
    [Key("next_tick_at")] public long NextTickAt { get; set; }
}

[MessagePackObject]
public sealed class EquipmentDto
{
    [Key("slot")] public string Slot { get; set; } = "";
    [Key("item_id")] public string ItemId { get; set; } = "";
}

[MessagePackObject]
public sealed class DiscoveryDto
{
    [Key("location_id")] public string LocationId { get; set; } = "";
    [Key("method")] public string Method { get; set; } = "";
    [Key("tick")] public long Tick { get; set; }
}

[MessagePackObject]
public sealed class InventoryDto
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("count")] public int Count { get; set; }

    /// <summary>Required from schema 9: the stack's quality (-1 crude, 0 standard, 1 fine, 2 masterwork from schema 19). The 8 -> 9 step gives older stacks 0.</summary>
    [Key("quality")] public int? Quality { get; set; }

    /// <summary>From schema 19: a notable instance's record, or null for an ordinary stack.</summary>
    [Key("notable")] public NotableDto? Notable { get; set; }

    /// <summary>From schema 24: its wear, or null when it is unworn at its made quality.</summary>
    [Key("wear")] public WearDto? Wear { get; set; }
}

/// <summary>A stack's wear (schema 24; WP3b): the points taken, and the quality it was made at.</summary>
[MessagePackObject]
public sealed class WearDto
{
    [Key("points")] public int Points { get; set; }
    [Key("made_quality")] public int MadeQuality { get; set; }
}

/// <summary>A notable instance's record (schema 19): name, provenance, the maker's mark when made, familiarity.</summary>
[MessagePackObject]
public sealed class NotableDto
{
    [Key("name")] public string Name { get; set; } = "";
    [Key("provenance")] public string Provenance { get; set; } = "";
    [Key("source_ref")] public string SourceRef { get; set; } = "";
    [Key("tick")] public long Tick { get; set; }
    [Key("maker")] public string? Maker { get; set; }
    [Key("familiarity")] public int Familiarity { get; set; }
    [Key("uses")] public int Uses { get; set; }
}

[MessagePackObject]
public sealed class CellsSectionDto
{
    [Key("records")] public CellDto[] Records { get; set; } = Array.Empty<CellDto>();
}

[MessagePackObject]
public sealed class CellDto
{
    [Key("cell_key")] public string CellKey { get; set; } = "";
    [Key("baseline_hash")] public string BaselineHash { get; set; } = "";
    [Key("dirty_reasons")] public string[] DirtyReasons { get; set; } = Array.Empty<string>();
    [Key("flags")] public FlagDto[] Flags { get; set; } = Array.Empty<FlagDto>();
    [Key("harvested_nodes")] public NodeDto[] HarvestedNodes { get; set; } = Array.Empty<NodeDto>();
    [Key("population_alive")] public PopulationDto[] PopulationAlive { get; set; } = Array.Empty<PopulationDto>();
}

[MessagePackObject]
public sealed class FlagDto
{
    [Key("name")] public string Name { get; set; } = "";
    [Key("value")] public long Value { get; set; }
}

[MessagePackObject]
public sealed class NodeDto
{
    [Key("node_key")] public string NodeKey { get; set; } = "";
    [Key("last_harvest_tick")] public long LastHarvestTick { get; set; }
    [Key("harvest_seq")] public int HarvestSeq { get; set; }
}

[MessagePackObject]
public sealed class PopulationDto
{
    [Key("population_id")] public string PopulationId { get; set; } = "";
    [Key("alive")] public int Alive { get; set; }
}

[MessagePackObject]
public sealed class EntitiesSectionDto
{
    [Key("records")] public EntityDto[] Records { get; set; } = Array.Empty<EntityDto>();

    /// <summary>Persistent instances no baseline slot generates (schema 3).</summary>
    [Key("created")] public CreatedDto[] Created { get; set; } = Array.Empty<CreatedDto>();

    /// <summary>The baseline hash of every cell hosting a record: each record is proven against its host cell.</summary>
    [Key("baselines")] public CellBaselineDto[] Baselines { get; set; } = Array.Empty<CellBaselineDto>();

    /// <summary>Changed world containers with their whole contents. Required from schema 6.</summary>
    [Key("containers")] public ContainerDto[]? Containers { get; set; }

    /// <summary>Spawners' creatures that left their baseline. Required from schema 8.</summary>
    [Key("creatures")] public CreatureDto[]? Creatures { get; set; }

    /// <summary>The sounds made on the last tick that creatures hear on the next, in order. Required from schema 14.</summary>
    [Key("noises")] public NoiseDto[]? Noises { get; set; }

    /// <summary>The town markets, the road and the economy's day (WP14); null in a save from before the economy first ran (schema 22).</summary>
    [Key("economy")] public EconomyDto? Economy { get; set; }

    /// <summary>The merchants' purses and restocks (WP14). Required from schema 22.</summary>
    [Key("merchants")] public MerchantDto[]? Merchants { get; set; }

    /// <summary>Every NPC life, the vacancies and the pairs (WP12); null in a save from before lives first ran (schema 26).</summary>
    [Key("npc_life")] public NpcLifeDto? NpcLife { get; set; }

    /// <summary>Every NPC adventurer and the slots awaiting arrivals (WP13); null in a save from before adventurers first lived (schema 29).</summary>
    [Key("adventurers")] public AdventurersDto? Adventurers { get; set; }
}

/// <summary>
/// The NPC adventurers (schema 29; WP13): the last day lived, every adventurer - keyed with no limit on how many (DN5) - and the slots waiting
/// for an arrival.
/// </summary>
[MessagePackObject]
public sealed class AdventurersDto
{
    [Key("day")] public long Day { get; set; }
    [Key("agents")] public AdventurerDto[] Agents { get; set; } = Array.Empty<AdventurerDto>();
    [Key("vacancies")] public AdventurerVacancyDto[] Vacancies { get; set; } = Array.Empty<AdventurerVacancyDto>();
    [Key("bodies")] public AdventurerBodyDto[] Bodies { get; set; } = Array.Empty<AdventurerBodyDto>();
}

/// <summary>An adventurer's body standing outside the party: whose, by name, and where - a load stands exactly these.</summary>
[MessagePackObject]
public sealed class AdventurerBodyDto
{
    [Key("key")] public string Key { get; set; } = "";
    [Key("name")] public string Name { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("facing_mdeg")] public int FacingMdeg { get; set; }
}

/// <summary>One adventurer: who, where, their progression on the character's rules, gear as real items, coin, pack, condition, plan and party.</summary>
[MessagePackObject]
public sealed class AdventurerDto
{
    [Key("key")] public string Key { get; set; } = "";
    [Key("slot")] public string Slot { get; set; } = "";
    [Key("generation")] public int Generation { get; set; }
    [Key("name")] public string Name { get; set; } = "";
    [Key("people_id")] public string? PeopleId { get; set; }
    [Key("settlement")] public string Settlement { get; set; } = "";
    [Key("at")] public string At { get; set; } = "";
    [Key("progression")] public ProgressionDto Progression { get; set; } = new();
    [Key("coin")] public long Coin { get; set; }
    [Key("risk_percent")] public int RiskPercent { get; set; }
    [Key("arrived_day")] public long ArrivedDay { get; set; }
    [Key("gear")] public AdventurerGearDto[] Gear { get; set; } = Array.Empty<AdventurerGearDto>();
    [Key("pack")] public GoodCountDto[] Pack { get; set; } = Array.Empty<GoodCountDto>();
    [Key("condition")] public string Condition { get; set; } = "active";
    [Key("injured_until_day")] public long InjuredUntilDay { get; set; }
    [Key("plan")] public AdventurerPlanDto Plan { get; set; } = new();
    [Key("party")] public string? Party { get; set; }
    [Key("acquired")] public int Acquired { get; set; }
    [Key("kills")] public long Kills { get; set; }
    [Key("bounties_claimed")] public int BountiesClaimed { get; set; }
}

[MessagePackObject]
public sealed class AdventurerGearDto
{
    [Key("slot")] public string Slot { get; set; } = "";
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("quality")] public int Quality { get; set; }
}

[MessagePackObject]
public sealed class AdventurerPlanDto
{
    [Key("day")] public long Day { get; set; }
    [Key("goal")] public string Goal { get; set; } = "rest";
    [Key("ref")] public string? Ref { get; set; }
    [Key("group")] public string[] Group { get; set; } = Array.Empty<string>();
    [Key("post_tick")] public long PostTick { get; set; }
    [Key("depart_tick")] public long DepartTick { get; set; }
}

[MessagePackObject]
public sealed class AdventurerVacancyDto
{
    [Key("slot")] public string Slot { get; set; } = "";
    [Key("settlement")] public string Settlement { get; set; } = "";
    [Key("left_day")] public long LeftDay { get; set; }
    [Key("arrival_day")] public long ArrivalDay { get; set; }
    [Key("next_generation")] public int NextGeneration { get; set; }
}

/// <summary>
/// NPC life (schema 26; WP12): the last game hour settled, every life - stored sparsely by the cell its body stands in, sorted by
/// (cell, key), with no limit on how many - the role slots waiting for an arrival, and when each pair last met.
/// </summary>
[MessagePackObject]
public sealed class NpcLifeDto
{
    [Key("hour")] public long Hour { get; set; }
    [Key("npcs")] public NpcLifeRecordDto[] Npcs { get; set; } = Array.Empty<NpcLifeRecordDto>();
    [Key("vacancies")] public NpcVacancyDto[] Vacancies { get; set; } = Array.Empty<NpcVacancyDto>();
    [Key("pairs_last_met")] public PairMetDto[] PairsLastMet { get; set; } = Array.Empty<PairMetDto>();
}

/// <summary>One NPC's life: routine, a resident's name and generation, needs, activity, condition, and the body's pose and route.</summary>
[MessagePackObject]
public sealed class NpcLifeRecordDto
{
    [Key("key")] public string Key { get; set; } = "";
    [Key("routine_id")] public string RoutineId { get; set; } = "";
    [Key("cell")] public string Cell { get; set; } = "";
    [Key("name")] public string? Name { get; set; }
    [Key("generation")] public int Generation { get; set; }
    [Key("arrived_day")] public long ArrivedDay { get; set; }
    [Key("hunger")] public int Hunger { get; set; }
    [Key("rest")] public int Rest { get; set; }
    [Key("social")] public int Social { get; set; }
    [Key("coin")] public int Coin { get; set; }
    [Key("activity")] public string Activity { get; set; } = "";
    [Key("with")] public string? With { get; set; }
    [Key("condition")] public string Condition { get; set; } = "living";
    [Key("condition_until")] public long ConditionUntil { get; set; }
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("facing_mdeg")] public int FacingMdeg { get; set; }
    [Key("route")] public NavRouteDto Route { get; set; } = new();
    [Key("stuck_ticks")] public int StuckTicks { get; set; }
}

/// <summary>A mover's committed route (the M7 route record, WP12): status, goal, corners, when planned, the window's stamp and rectangle.</summary>
[MessagePackObject]
public sealed class NavRouteDto
{
    [Key("status")] public string Status { get; set; } = "none";
    [Key("goal_x_mm")] public long GoalXMm { get; set; }
    [Key("goal_z_mm")] public long GoalZMm { get; set; }
    [Key("corners")] public long[] Corners { get; set; } = Array.Empty<long>();
    [Key("planned_tick")] public long PlannedTick { get; set; }
    [Key("stamp")] public ulong Stamp { get; set; }
    [Key("watch")] public long[] Watch { get; set; } = { 0, 0, 0, 0 };
    [Key("partial")] public bool Partial { get; set; }
}

[MessagePackObject]
public sealed class NpcVacancyDto
{
    [Key("slot_id")] public string SlotId { get; set; } = "";
    [Key("left_day")] public long LeftDay { get; set; }
    [Key("arrival_day")] public long ArrivalDay { get; set; }
    [Key("next_generation")] public int NextGeneration { get; set; }

    /// <summary>From schema 29: the retiring adventurer promised the slot (WP13), or null for a newcomer named from their people.</summary>
    [Key("arriving_name")] public string? ArrivingName { get; set; }
}

[MessagePackObject]
public sealed class PairMetDto
{
    [Key("pair")] public string Pair { get; set; } = "";
    [Key("hour")] public long Hour { get; set; }
}

/// <summary>The economy (schema 22; WP14): the last day settled, every market, and what is on the road.</summary>
[MessagePackObject]
public sealed class EconomyDto
{
    [Key("day")] public long Day { get; set; }
    [Key("markets")] public MarketDto[] Markets { get; set; } = Array.Empty<MarketDto>();
    [Key("shipments")] public ShipmentDto[] Shipments { get; set; } = Array.Empty<ShipmentDto>();
}

/// <summary>A town market: its stock of each good, and the character's recent sells there (sell history), both sorted by good.</summary>
[MessagePackObject]
public sealed class MarketDto
{
    [Key("market_id")] public string MarketId { get; set; } = "";
    [Key("stock")] public GoodCountDto[] Stock { get; set; } = Array.Empty<GoodCountDto>();
    [Key("sold_by_character")] public GoodCountDto[] SoldByCharacter { get; set; } = Array.Empty<GoodCountDto>();
}

[MessagePackObject]
public sealed class GoodCountDto
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("count")] public long Count { get; set; }
}

[MessagePackObject]
public sealed class ShipmentDto
{
    [Key("link_id")] public string LinkId { get; set; } = "";
    [Key("from_market")] public string FromMarket { get; set; } = "";
    [Key("to_market")] public string ToMarket { get; set; } = "";
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("count")] public long Count { get; set; }
    [Key("arrives_day")] public long ArrivesDay { get; set; }
}

/// <summary>A merchant's purse, the day they last restocked, the restock their shelf was drawn for, and whether the character traded with them.</summary>
[MessagePackObject]
public sealed class MerchantDto
{
    [Key("merchant_id")] public string MerchantId { get; set; } = "";
    [Key("purse")] public long Purse { get; set; }
    [Key("restocked_day")] public long RestockedDay { get; set; }
    [Key("shelf_day")] public long ShelfDay { get; set; }
    [Key("traded")] public bool Traded { get; set; }
}

/// <summary>A sound waiting to be heard (schema 14): where, how far it carries, and - for a howl - whose kind answers it.</summary>
[MessagePackObject]
public sealed class NoiseDto
{
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("radius_mm")] public long RadiusMm { get; set; }
    [Key("call")] public bool Call { get; set; }
    [Key("caller_kind")] public string? CallerKind { get; set; }
}

/// <summary>What a creature's next ticks depend on beyond its body and mind (schema 14): each only while it still matters.</summary>
[MessagePackObject]
public sealed class CreatureContinuationDto
{
    [Key("next_charge_tick")] public long NextChargeTick { get; set; }
    [Key("stagger_immune_until")] public long StaggerImmuneUntil { get; set; }
    [Key("staggered_tick")] public long? StaggeredTick { get; set; }
    [Key("stagger_lasts_ticks")] public int StaggerLastsTicks { get; set; }
}

[MessagePackObject]
public sealed class CreatureDto
{
    [Key("key")] public string Key { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("generation")] public int Generation { get; set; }
    [Key("condition")] public string Condition { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("facing_mdeg")] public int FacingMdeg { get; set; }
    [Key("health")] public int Health { get; set; }
    [Key("died_tick")] public long DiedTick { get; set; }
    [Key("respawn_tick")] public long RespawnTick { get; set; }
    [Key("mind")] public string Mind { get; set; } = "";
    [Key("awareness")] public int Awareness { get; set; }
    [Key("knows")] public bool Knows { get; set; }
    [Key("known_x_mm")] public long KnownXMm { get; set; }
    [Key("known_z_mm")] public long KnownZMm { get; set; }
    [Key("last_seen_tick")] public long LastSeenTick { get; set; }
    [Key("search_until")] public long SearchUntil { get; set; }
    [Key("has_called")] public bool HasCalled { get; set; }

    /// <summary>Required from schema 14. The 13 -> 14 step gives older saves none: the charge, the stagger and the stun were not kept.</summary>
    [Key("continuation")] public CreatureContinuationDto? Continuation { get; set; }
}

[MessagePackObject]
public sealed class ContainerDto
{
    [Key("key")] public string Key { get; set; } = "";
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("items")] public ContainerItemDto[] Items { get; set; } = Array.Empty<ContainerItemDto>();
}

[MessagePackObject]
public sealed class ContainerItemDto
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("count")] public int Count { get; set; }

    /// <summary>Required from schema 9. The 8 -> 9 step gives older stacks 0.</summary>
    [Key("quality")] public int? Quality { get; set; }

    /// <summary>From schema 19: a notable instance's record, or null.</summary>
    [Key("notable")] public NotableDto? Notable { get; set; }

    /// <summary>From schema 24: its wear, or null.</summary>
    [Key("wear")] public WearDto? Wear { get; set; }
}

[MessagePackObject]
public sealed class CreatedDto
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("x_cm")] public int XCm { get; set; }
    [Key("z_cm")] public int ZCm { get; set; }

    /// <summary>Required from schema 6: a dropped stack keeps its count. The 5 -> 6 step gives older records 1.</summary>
    [Key("count")] public int? Count { get; set; }

    /// <summary>Required from schema 9. The 8 -> 9 step gives older records 0.</summary>
    [Key("quality")] public int? Quality { get; set; }

    /// <summary>From schema 19: a notable instance's record, or null.</summary>
    [Key("notable")] public NotableDto? Notable { get; set; }

    /// <summary>From schema 24: its wear, or null.</summary>
    [Key("wear")] public WearDto? Wear { get; set; }
}

[MessagePackObject]
public sealed class CellBaselineDto
{
    [Key("cell_key")] public string CellKey { get; set; } = "";
    [Key("baseline_hash")] public string BaselineHash { get; set; } = "";
}

/// <summary>§5.3's record: instance_id, slot_key, generation_seq, def_id, dirty_mask, state.</summary>
[MessagePackObject]
public sealed class EntityDto
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("slot_key")] public string SlotKey { get; set; } = "";
    [Key("generation_seq")] public int GenerationSeq { get; set; }
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("dirty_mask")] public int DirtyMask { get; set; }
    [Key("state")] public EntityStateDto State { get; set; } = new();
}

/// <summary>Only the diverged fields: a field that equals the baseline is nil.</summary>
[MessagePackObject]
public sealed class EntityStateDto
{
    [Key("alive")] public bool? Alive { get; set; }
    [Key("x_cm")] public int? XCm { get; set; }
    [Key("z_cm")] public int? ZCm { get; set; }
}

/// <summary>Encodes and decodes save sections. Output is byte-stable: equal input, equal bytes (T-03).</summary>
public static class SectionCodec
{
    // A save is untrusted input from disk: it may be damaged or hand-edited.
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);

    private static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = true };

    /// <summary>The MessagePack options every section is read and written with, shared with the migration chain.</summary>
    internal static MessagePackSerializerOptions MessagePackOptions => Options;

    public static byte[] EncodeManifest(SaveManifest manifest) => JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJson);

    public static SaveManifest DecodeManifest(byte[] bytes) =>
        JsonSerializer.Deserialize<SaveManifest>(bytes, ManifestJson)
        ?? throw new JsonException("manifest.json is empty");

    public static byte[] EncodePlayer(PlayerRecord player) => MessagePackSerializer.Serialize(new PlayerDto
    {
        InstanceId = player.Id.Value,
        Name = player.Name,
        XMm = player.XMm,
        YMm = player.YMm,
        ZMm = player.ZMm,
        AppearanceSeed = player.AppearanceSeed,
        Inventory = player.Inventory
            .Select(e => new InventoryDto { ItemId = e.ItemId.Value, DefId = e.DefId, Count = e.Count, Quality = e.Quality, Notable = NotableToDto(e.Notable), Wear = WearToDto(e.Wear) })
            .ToArray(),
        Progression = ProgressionCodec.ToDto(player.Progression),
        FacingMdeg = player.FacingMdeg,
        Discoveries = player.Discoveries
            .Select(d => new DiscoveryDto { LocationId = d.LocationId, Method = DiscoveryMethods.Key(d.Method), Tick = d.Tick })
            .ToArray(),
        Equipment = player.Equipment
            .Select(kv => new EquipmentDto { Slot = EquipSlots.Key(kv.Key), ItemId = kv.Value.Value })
            .ToArray(),
        Currency = player.Currency,
        Effects = player.Effects
            .Select(e => new EffectDto { EffectId = e.EffectId, Stacks = e.Stacks, ExpiresTick = e.ExpiresTick, NextTickAt = e.NextTickAt })
            .ToArray(),
        Relationships = player.Relationships
            .Select(r => new RelationshipDto { NpcId = r.NpcId, Dimension = r.Dimension, Value = r.Value })
            .ToArray(),
        Conversations = player.Conversations
            .Select(c => new ConversationDto { DialogueId = c.DialogueId, Heard = c.Heard.ToArray() })
            .ToArray(),
        Quests = player.Quests
            .Select(q => new QuestDto
            {
                QuestId = q.QuestId,
                Status = QuestKeys.Key(q.Status),
                StartedTick = q.StartedTick,
                EndedTick = q.EndedTick,
                EndedBy = q.EndedBy,
                Objectives = q.Objectives
                    .Select(o => new ObjectiveDto { Id = o.Id, Status = QuestKeys.Key(o.Status), ActivatedTick = o.ActivatedTick, EndedTick = o.EndedTick, Progress = o.Progress })
                    .ToArray(),
            })
            .ToArray(),
        Companions = player.Companions
            .Select(c => new CompanionDto
            {
                NpcId = c.NpcId,
                Order = CompanionKeys.Key(c.Order),
                Condition = CompanionKeys.Key(c.Condition),
                XMm = c.XMm,
                ZMm = c.ZMm,
                FacingMdeg = c.FacingMdeg,
                Health = c.Health,
                DownedTick = c.DownedTick,
                StuckTicks = c.StuckTicks,
                LastCombatTick = c.LastCombatTick,
                TrailMm = c.Trail.SelectMany(m => new[] { m.XMm, m.ZMm }).ToArray(),
                Membership = PartyKeys.Key(c.Membership),
                Stance = PartyKeys.Key(c.Stance),
                Tactics = new TacticsDto { KeepToLeash = c.Tactics.KeepToLeash, ProtectInjured = c.Tactics.ProtectInjured, HoldHealing = c.Tactics.HoldHealing },
                Contract = c.Contract is not { } k ? null : new ContractDto
                {
                    Role = PartyKeys.Key(k.Role),
                    Days = k.Days,
                    DailyWage = k.DailyWage,
                    LootRule = k.LootRule,
                    DaysServed = k.DaysServed,
                    UnpaidDays = k.UnpaidDays,
                    Purse = k.Purse,
                    WorkSite = k.WorkSite,
                    RecipeId = k.RecipeId,
                    Supply = k.Supply.Select(kv => new SupplyDto { ItemId = kv.Key, Count = kv.Value }).ToArray(),
                },
            })
            .ToArray(),
        Posture = new PostureDto { Stance = StanceKeys.Key(player.Posture.Stance), Airborne = player.Posture.Airborne, AirMs = player.Posture.AirMs },
        Waystones = player.Waystones.Select(w => new WaystoneDto { Stone = w.StoneKey, Region = w.RegionId, Method = WaystoneMethods.Key(w.Method), Tick = w.Tick }).ToArray(),
        ReturnPoint = player.ReturnPoint,
        RoadsWalked = player.RoadsWalked.ToArray(),
        Deeds = player.Deeds.Select(d => new DeedDto { Deed = DeedKeys.Key(d.Kind), Subject = d.SubjectId, Npc = d.NpcId, Count = d.Count, LastTick = d.LastTick }).ToArray(),
        Identity = new IdentityDto
        {
            Pronouns = PronounKeys.Key(player.Identity.Pronouns),
            People = player.Identity.PeopleId,
            BodyPreset = player.Identity.BodyPresetId,
            Call = player.Identity.CallId,
        },
        Factions = new FactionLedgerDto
        {
            NextActSeq = player.Factions.NextActSeq,
            Acts = player.Factions.Acts.Select(a => new ActDto { Seq = a.Seq, Kind = a.Kind, Subject = a.Subject, Cell = a.CellKey, XMm = a.XMm, ZMm = a.ZMm, Tick = a.Tick }).ToArray(),
            Knowledge = player.Factions.Knowledge.Select(k => new FactionKnowledgeDto
            {
                Knower = k.Knower, Act = k.Act, Identity = k.Identity, Source = k.Source, Via = k.Via, Tick = k.Tick, Delta = k.Delta,
            }).ToArray(),
            Standing = player.Factions.Standing.Select(st => new FactionStandingDto { Faction = st.FactionId, Points = st.Points }).ToArray(),
        },
        Party = new PartyLedgerDto
        {
            Recovering = player.Party.Recovering.Select(r => new RecoveryDto { NpcId = r.NpcId, UntilTick = r.UntilTick }).ToArray(),
            Fallen = player.Party.Fallen.ToArray(),
        },
        QuickBar = new QuickBarDto { Slots = player.QuickBar.ToArray(), Cleared = player.QuickBarCleared.ToArray() },
        PartySheets = player.PartySheets.Select(p => new PartySheetDto
        {
            NpcId = p.NpcId,
            Progression = ProgressionCodec.ToDto(p.Progression),
            Gear = p.Gear.Select(g => new PartyGearDto { Slot = EquipSlots.Key(g.Key), Loaned = g.Value.Loaned, Item = StackToDto(g.Value.Item) }).ToArray(),
            Belongings = p.Belongings.Select(StackToDto).ToArray(),
        }).ToArray(),
        Attack = player.Attack is not { } attack ? null
            : new AttackDto { Source = attack.Source, StartTick = attack.StartTick, Struck = attack.Struck.Select(e => e.Value).ToArray() },
    }, Options);

    /// <summary>A stack's saved quality: present from schema 9, and one of crude, standard, fine or (schema 19) masterwork.</summary>
    private static int QualityOf(int? quality, string what) => quality switch
    {
        null => throw new FormatException($"{what} has no quality (required from schema 9)"),
        var q when UNNAMED.Domain.Crafting.Quality.IsValid(q.Value) => q.Value,
        var q => throw new FormatException($"{what} has quality {q}, which is not -1, 0, 1 or 2"),
    };

    private static WearDto? WearToDto(ItemWear? wear) => wear is null ? null : new WearDto { Points = wear.Points, MadeQuality = wear.MadeQuality };

    /// <summary>A stack's saved wear (schema 24), in range, or null for an unworn stack.</summary>
    private static ItemWear? WearOf(WearDto? dto, string what)
    {
        if (dto is null)
            return null;
        var wear = new ItemWear(dto.Points, dto.MadeQuality);
        return wear.IsValid ? wear : throw new FormatException($"{what} has invalid wear ({dto.Points} points, made at {dto.MadeQuality})");
    }

    private static NotableDto? NotableToDto(NotableRecord? notable) => notable is null ? null : new NotableDto
    {
        Name = notable.Name,
        Provenance = ProvenanceKinds.Key(notable.Provenance),
        SourceRef = notable.SourceRef,
        Tick = notable.Tick,
        Maker = notable.Maker,
        Familiarity = notable.Familiarity,
        Uses = notable.Uses,
    };

    /// <summary>A stack's saved notable record (schema 19), whole and in range, or null for an ordinary stack.</summary>
    private static NotableRecord? NotableOf(NotableDto? dto, string what)
    {
        if (dto is null)
            return null;
        var record = new NotableRecord(dto.Name, ProvenanceKinds.Parse(dto.Provenance), dto.SourceRef, dto.Tick)
        {
            Maker = dto.Maker,
            Familiarity = dto.Familiarity,
            Uses = dto.Uses,
        };
        return record.IsValid ? record : throw new FormatException($"{what} has an invalid notable record");
    }

    public static PlayerRecord DecodePlayer(byte[] bytes)
    {
        var dto = MessagePackSerializer.Deserialize<PlayerDto>(bytes, Options);
        var progression = dto.Progression ?? throw new FormatException("player.msgpack has no progression record (required from schema 4)");
        int facing = dto.FacingMdeg ?? throw new FormatException("player.msgpack has no facing (required from schema 5)");
        var discoveries = dto.Discoveries ?? throw new FormatException("player.msgpack has no discovery records (required from schema 5)");
        var equipment = dto.Equipment ?? throw new FormatException("player.msgpack has no equipment (required from schema 6)");
        long currency = dto.Currency ?? throw new FormatException("player.msgpack has no currency (required from schema 6)");
        var effects = dto.Effects ?? throw new FormatException("player.msgpack has no effects (required from schema 7)");
        var relationships = dto.Relationships ?? throw new FormatException("player.msgpack has no relationships (required from schema 10)");
        var conversations = dto.Conversations ?? throw new FormatException("player.msgpack has no conversations (required from schema 10)");
        var quests = dto.Quests ?? throw new FormatException("player.msgpack has no quests (required from schema 11)");
        var companions = dto.Companions ?? throw new FormatException("player.msgpack has no companions (required from schema 12)");
        var posture = dto.Posture ?? throw new FormatException("player.msgpack has no posture (required from schema 13)");
        var waystones = dto.Waystones ?? throw new FormatException("player.msgpack has no Waystone records (required from schema 15)");
        var roads = dto.RoadsWalked ?? throw new FormatException("player.msgpack has no walked roads (required from schema 15)");
        var deeds = dto.Deeds ?? throw new FormatException("player.msgpack has no deed tallies (required from schema 16)");
        var identity = dto.Identity ?? throw new FormatException("player.msgpack has no identity (required from schema 17)");
        var factions = dto.Factions ?? throw new FormatException("player.msgpack has no faction ledger (required from schema 20)");
        var party = dto.Party ?? throw new FormatException("player.msgpack has no party ledger (required from schema 21)");
        var quickBar = dto.QuickBar ?? throw new FormatException("player.msgpack has no quick bar (required from schema 23)");
        var sheets = dto.PartySheets ?? throw new FormatException("player.msgpack has no party sheets (required from schema 25)");
        var pronouns = PronounKeys.Parse(identity.Pronouns) ?? throw new FormatException($"player identity has pronouns '{identity.Pronouns}'");
        var stance = posture.Stance switch
        {
            "standing" or "crouched" => StanceKeys.Parse(posture.Stance),
            _ => throw new FormatException($"player posture has stance '{posture.Stance}'"),
        };
        if (posture.AirMs < 0 || (!posture.Airborne && posture.AirMs != 0))
            throw new FormatException($"player posture has air_ms {posture.AirMs} while {(posture.Airborne ? "airborne" : "on the ground")}");
        return new PlayerRecord(EntityId.Parse(dto.InstanceId), dto.Name, dto.XMm, dto.YMm, dto.ZMm, dto.AppearanceSeed,
            dto.Inventory.Select(e => new InventoryEntry(EntityId.Parse(e.ItemId), e.DefId, e.Count) { Quality = QualityOf(e.Quality, $"carried {e.ItemId}"), Notable = NotableOf(e.Notable, $"carried {e.ItemId}"), Wear = WearOf(e.Wear, $"carried {e.ItemId}") }),
            ProgressionCodec.FromDto(progression), facing,
            discoveries.Select(d => new DiscoveryRecord(d.LocationId, DiscoveryMethods.Parse(d.Method), d.Tick)),
            equipment.Select(e => KeyValuePair.Create(EquipSlots.Parse(e.Slot), EntityId.Parse(e.ItemId))), currency,
            effects.Select(e => new ActiveEffect(e.EffectId, e.Stacks, e.ExpiresTick, e.NextTickAt)),
            relationships.Select(r => new RelationshipValue(r.NpcId, r.Dimension, r.Value)),
            conversations.Select(c => new ConversationMemory(c.DialogueId, c.Heard.ToImmutableArray())),
            quests.Select(q => new QuestState(q.QuestId,
                QuestKeys.ParseQuest(q.Status) ?? throw new FormatException($"quest {q.QuestId} has status '{q.Status}'"),
                q.StartedTick, q.EndedTick, q.EndedBy,
                q.Objectives.Select(o => new ObjectiveState(o.Id,
                    QuestKeys.ParseObjective(o.Status) ?? throw new FormatException($"objective {o.Id} of {q.QuestId} has status '{o.Status}'"),
                    o.ActivatedTick, o.EndedTick, o.Progress)).ToImmutableArray())),
            companions.Select(Companion))
        {
            Posture = new Posture(stance, posture.Airborne, posture.AirMs),
            Waystones = waystones.Select(w => new WaystoneRecord(w.Stone, w.Region,
                WaystoneMethods.Parse(w.Method) ?? throw new FormatException($"Waystone {w.Stone} was known by '{w.Method}'"), w.Tick)).ToImmutableArray(),
            ReturnPoint = dto.ReturnPoint,
            RoadsWalked = roads.ToImmutableArray(),
            Deeds = deeds.Select(d => new DeedTally(DeedKeys.Parse(d.Deed) ?? throw new FormatException($"a deed tally of {d.Subject} is '{d.Deed}'"),
                d.Subject, d.Npc, d.Count, d.LastTick)).ToImmutableArray(),
            Identity = new CharacterIdentity(pronouns, identity.People, identity.BodyPreset, identity.Call),
            Factions = new FactionLedger(factions.NextActSeq,
                factions.Acts.Select(a => new ActRecord(a.Seq, a.Kind, a.Subject, a.Cell, a.XMm, a.ZMm, a.Tick)).ToImmutableArray(),
                factions.Knowledge.Select(k => new FactionKnowledge(k.Knower, k.Act, k.Identity, k.Source, k.Via, k.Tick, k.Delta)).ToImmutableArray(),
                factions.Standing.Select(st => new FactionStanding(st.Faction, st.Points)).ToImmutableArray()),
            Party = new PartyLedger(party.Recovering.Select(r => new RecoveryRecord(r.NpcId, r.UntilTick)).ToImmutableArray(), party.Fallen.ToImmutableArray()),
            QuickBar = quickBar.Slots.ToImmutableArray(),
            QuickBarCleared = quickBar.Cleared.ToImmutableArray(),
            PartySheets = sheets.Select(p => new PartySheet(p.NpcId, ProgressionCodec.FromDto(p.Progression))
            {
                Gear = p.Gear.ToImmutableSortedDictionary(g => EquipSlots.Parse(g.Slot), g => new PartyGear(StackOf(g.Item, $"{p.NpcId}'s {g.Slot}"), g.Loaned)),
                Belongings = p.Belongings.Select(e => StackOf(e, $"{p.NpcId}'s {e.ItemId}")).ToImmutableArray(),
            }).ToImmutableArray(),
            Attack = dto.Attack is not { } attack ? null
                : new AttackRecord(attack.Source, attack.StartTick, attack.Struck.Select(EntityId.Parse).ToImmutableArray()),
        };
    }

    private static InventoryDto StackToDto(InventoryEntry e) =>
        new() { ItemId = e.ItemId.Value, DefId = e.DefId, Count = e.Count, Quality = e.Quality, Notable = NotableToDto(e.Notable), Wear = WearToDto(e.Wear) };

    private static InventoryEntry StackOf(InventoryDto e, string what) =>
        new(EntityId.Parse(e.ItemId), e.DefId, e.Count) { Quality = QualityOf(e.Quality, what), Notable = NotableOf(e.Notable, what), Wear = WearOf(e.Wear, what) };

    private static CompanionRecord Companion(CompanionDto c)
    {
        if (c.TrailMm.Length % 2 != 0)
            throw new FormatException($"companion {c.NpcId}'s trail is x, z pairs; it has {c.TrailMm.Length} numbers");
        return new CompanionRecord(c.NpcId,
            CompanionKeys.ParseOrder(c.Order) ?? throw new FormatException($"companion {c.NpcId} has order '{c.Order}'"),
            CompanionKeys.ParseCondition(c.Condition) ?? throw new FormatException($"companion {c.NpcId} has condition '{c.Condition}'"),
            c.XMm, c.ZMm, c.FacingMdeg, c.Health)
        {
            DownedTick = c.DownedTick,
            StuckTicks = c.StuckTicks,
            LastCombatTick = c.LastCombatTick,
            Trail = Enumerable.Range(0, c.TrailMm.Length / 2).Select(i => new TrailMark(c.TrailMm[2 * i], c.TrailMm[2 * i + 1])).ToImmutableArray(),
            Membership = PartyKeys.ParseMembership(c.Membership ?? throw new FormatException($"companion {c.NpcId} has no membership (required from schema 21)"))
                ?? throw new FormatException($"companion {c.NpcId} has membership '{c.Membership}'"),
            Stance = PartyKeys.ParseStance(c.Stance ?? throw new FormatException($"companion {c.NpcId} has no stance (required from schema 21)"))
                ?? throw new FormatException($"companion {c.NpcId} has stance '{c.Stance}'"),
            Tactics = c.Tactics is { } t
                ? new PartyTactics(t.KeepToLeash, t.ProtectInjured, t.HoldHealing)
                : throw new FormatException($"companion {c.NpcId} has no tactics (required from schema 21)"),
            Contract = c.Contract is not { } k ? null : new HirelingContract(
                PartyKeys.ParseRole(k.Role) ?? throw new FormatException($"companion {c.NpcId}'s contract has role '{k.Role}'"), k.Days, k.DailyWage, k.LootRule)
            {
                DaysServed = k.DaysServed,
                UnpaidDays = k.UnpaidDays,
                Purse = k.Purse,
                WorkSite = k.WorkSite,
                RecipeId = k.RecipeId,
                Supply = k.Supply.ToImmutableSortedDictionary(i => i.ItemId, i => i.Count, StringComparer.Ordinal),
            },
        };
    }

    public static byte[] EncodeCells(DeltaSnapshot snapshot) => MessagePackSerializer.Serialize(new CellsSectionDto
    {
        Records = snapshot.Cells.Select(c => new CellDto
        {
            CellKey = c.CellKey,
            BaselineHash = c.BaselineHash,
            DirtyReasons = c.DirtyReasons.ToArray(),
            Flags = c.Flags.Select(f => new FlagDto { Name = f.Key, Value = f.Value }).ToArray(),
            HarvestedNodes = c.HarvestedNodes
                .Select(n => new NodeDto { NodeKey = n.NodeKey, LastHarvestTick = n.LastHarvestTick, HarvestSeq = n.HarvestSeq })
                .ToArray(),
            PopulationAlive = c.PopulationAlive
                .Select(p => new PopulationDto { PopulationId = p.Key, Alive = p.Value })
                .ToArray(),
        }).ToArray(),
    }, Options);

    public static ImmutableArray<CellDeltaRecord> DecodeCells(byte[] bytes) =>
        MessagePackSerializer.Deserialize<CellsSectionDto>(bytes, Options).Records
            .Select(c => new CellDeltaRecord(
                c.CellKey,
                c.BaselineHash,
                c.DirtyReasons.ToImmutableArray(),
                c.Flags.Select(f => KeyValuePair.Create(f.Name, f.Value)).ToImmutableArray(),
                c.HarvestedNodes.Select(n => new NodeHarvest(n.NodeKey, n.LastHarvestTick, n.HarvestSeq)).ToImmutableArray(),
                c.PopulationAlive.Select(p => KeyValuePair.Create(p.PopulationId, p.Alive)).ToImmutableArray()))
            .ToImmutableArray();

    public static byte[] EncodeEntities(DeltaSnapshot snapshot)
    {
        var baselines = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Prove(string cell, string? hash, EntityId instance)
        {
            if (hash is null || (baselines.TryGetValue(cell, out string? known) && known != hash))
                throw new InvalidOperationException($"Entity record {instance} carries no single baseline hash for its host cell {cell}");
            baselines[cell] = hash;
        }
        foreach (var e in snapshot.Entities)
            Prove(HostCell(e.SlotKey), e.BaselineHash, e.InstanceId);
        foreach (var c in snapshot.Created)
            Prove(c.HostCell, c.BaselineHash, c.InstanceId);
        foreach (var c in snapshot.Containers)
            Prove(c.HostCell, c.BaselineHash, c.InstanceId);
        foreach (var c in snapshot.Creatures)
            Prove(c.HostCell, c.BaselineHash, c.InstanceId);

        return MessagePackSerializer.Serialize(new EntitiesSectionDto
        {
            Records = snapshot.Entities.Select(e => new EntityDto
            {
                InstanceId = e.InstanceId.Value,
                SlotKey = e.SlotKey,
                GenerationSeq = e.GenerationSeq,
                DefId = e.DefId,
                DirtyMask = e.DirtyMask,
                State = new EntityStateDto { Alive = e.Alive, XCm = e.XCm, ZCm = e.ZCm },
            }).ToArray(),
            Created = snapshot.Created.Select(c => new CreatedDto
            {
                InstanceId = c.InstanceId.Value,
                DefId = c.DefId,
                HostCell = c.HostCell,
                XCm = c.XCm,
                ZCm = c.ZCm,
                Count = c.Count,
                Quality = c.Quality,
                Notable = NotableToDto(c.Notable),
                Wear = WearToDto(c.Wear),
            }).ToArray(),
            Baselines = baselines.Select(kv => new CellBaselineDto { CellKey = kv.Key, BaselineHash = kv.Value }).ToArray(),
            Containers = snapshot.Containers.Select(c => new ContainerDto
            {
                Key = c.Key,
                InstanceId = c.InstanceId.Value,
                HostCell = c.HostCell,
                Items = c.Items.Select(i => new ContainerItemDto { ItemId = i.ItemId.Value, DefId = i.DefId, Count = i.Count, Quality = i.Quality, Notable = NotableToDto(i.Notable), Wear = WearToDto(i.Wear) }).ToArray(),
            }).ToArray(),
            Creatures = snapshot.Creatures.Select(c => new CreatureDto
            {
                Key = c.Key,
                DefId = c.DefId,
                InstanceId = c.InstanceId.Value,
                HostCell = c.HostCell,
                Generation = c.Generation,
                Condition = CreatureConditions.Key(c.Condition),
                XMm = c.XMm,
                ZMm = c.ZMm,
                FacingMdeg = c.FacingMdeg,
                Health = c.Health,
                DiedTick = c.DiedTick,
                RespawnTick = c.RespawnTick,
                Mind = CreatureMinds.Key(c.Mind),
                Awareness = c.Awareness,
                Knows = c.Knows,
                KnownXMm = c.KnownXMm,
                KnownZMm = c.KnownZMm,
                LastSeenTick = c.LastSeenTick,
                SearchUntil = c.SearchUntil,
                HasCalled = c.HasCalled,
                Continuation = new CreatureContinuationDto
                {
                    NextChargeTick = c.NextChargeTick,
                    StaggerImmuneUntil = c.StaggerImmuneUntil,
                    StaggeredTick = c.StaggeredTick,
                    StaggerLastsTicks = c.StaggerLastsTicks,
                },
            }).ToArray(),
            Noises = snapshot.Noises.Select(n => new NoiseDto { XMm = n.XMm, ZMm = n.ZMm, RadiusMm = n.RadiusMm, Call = n.Call, CallerKind = n.CallerKind })
                .ToArray(),
            Economy = snapshot.Economy is { } economy
                ? new EconomyDto
                {
                    Day = economy.Day,
                    Markets = economy.Markets.Values.Select(m => new MarketDto
                    {
                        MarketId = m.MarketId,
                        Stock = m.Stock.Select(kv => new GoodCountDto { ItemId = kv.Key, Count = kv.Value }).ToArray(),
                        SoldByCharacter = m.SoldByCharacter.Select(kv => new GoodCountDto { ItemId = kv.Key, Count = kv.Value }).ToArray(),
                    }).ToArray(),
                    Shipments = economy.Shipments.Select(s => new ShipmentDto
                    {
                        LinkId = s.LinkId, FromMarket = s.FromMarket, ToMarket = s.ToMarket, ItemId = s.ItemId, Count = s.Count, ArrivesDay = s.ArrivesDay,
                    }).ToArray(),
                }
                : null,
            Merchants = snapshot.Merchants.OrderBy(m => m.MerchantId, StringComparer.Ordinal).Select(m => new MerchantDto
            {
                MerchantId = m.MerchantId, Purse = m.Purse, RestockedDay = m.RestockedDay, ShelfDay = m.ShelfDay, Traded = m.Traded,
            }).ToArray(),
            NpcLife = snapshot.NpcLife is { } life ? NpcLifeToDto(life) : null,
            Adventurers = snapshot.Adventurers is { } adventurers ? AdventurersToDto(adventurers) : null,
        }, Options);
    }

    private static AdventurersDto AdventurersToDto(AdventurerState state) => new()
    {
        Day = state.Day,
        Agents = state.Agents.Values.Select(a => new AdventurerDto
        {
            Key = a.Key, Slot = a.Slot, Generation = a.Generation, Name = a.Name, PeopleId = a.PeopleId, Settlement = a.Settlement, At = a.At,
            Progression = ProgressionCodec.ToDto(a.Progression), Coin = a.Coin, RiskPercent = a.RiskPercent, ArrivedDay = a.ArrivedDay,
            Gear = a.Gear.Select(g => new AdventurerGearDto { Slot = EquipSlots.Key(g.Key), ItemId = g.Value.ItemId.Value, DefId = g.Value.DefId, Quality = g.Value.Quality })
                .ToArray(),
            Pack = a.Pack.Select(kv => new GoodCountDto { ItemId = kv.Key, Count = kv.Value }).ToArray(),
            Condition = a.Condition == AdventurerCondition.Injured ? "injured" : "active", InjuredUntilDay = a.InjuredUntilDay,
            Plan = new AdventurerPlanDto
            {
                Day = a.Plan.Day, Goal = a.Plan.Goal, Ref = a.Plan.Ref, Group = a.Plan.Group.ToArray(), PostTick = a.Plan.PostTick, DepartTick = a.Plan.DepartTick,
            },
            Party = a.Party, Acquired = a.Acquired, Kills = a.Kills, BountiesClaimed = a.BountiesClaimed,
        }).ToArray(),
        Vacancies = state.Vacancies.Values.Select(v => new AdventurerVacancyDto
        {
            Slot = v.Slot, Settlement = v.Settlement, LeftDay = v.LeftDay, ArrivalDay = v.ArrivalDay, NextGeneration = v.NextGeneration,
        }).ToArray(),
        Bodies = state.Bodies.Select(b => new AdventurerBodyDto { Key = b.Key, Name = b.Value.Name, XMm = b.Value.XMm, ZMm = b.Value.ZMm, FacingMdeg = b.Value.FacingMdeg })
            .ToArray(),
    };

    private static NpcLifeDto NpcLifeToDto(NpcLifeState life) => new()
    {
        Hour = life.Hour,
        Npcs = life.Npcs.Values.OrderBy(n => n.Cell, StringComparer.Ordinal).ThenBy(n => n.Key, StringComparer.Ordinal).Select(n => new NpcLifeRecordDto
        {
            Key = n.Key, RoutineId = n.RoutineId, Cell = n.Cell, Name = n.Name, Generation = n.Generation, ArrivedDay = n.ArrivedDay,
            Hunger = n.Needs.Hunger, Rest = n.Needs.Rest, Social = n.Needs.Social, Coin = n.Needs.Coin, Activity = n.Activity, With = n.With,
            Condition = n.Condition switch { NpcCondition.Injured => "injured", NpcCondition.Hired => "hired", _ => "living" }, ConditionUntil = n.ConditionUntil,
            XMm = n.XMm, ZMm = n.ZMm, FacingMdeg = n.FacingMdeg, StuckTicks = n.StuckTicks,
            Route = new NavRouteDto
            {
                Status = NavRoute.StatusKey(n.Route.Status), GoalXMm = n.Route.GoalXMm, GoalZMm = n.Route.GoalZMm,
                Corners = n.Route.Corners.SelectMany(c => new[] { c.XMm, c.ZMm }).ToArray(), PlannedTick = n.Route.PlannedTick, Stamp = n.Route.Stamp,
                Watch = new[] { n.Route.Watch.MinXMm, n.Route.Watch.MinZMm, n.Route.Watch.MaxXMm, n.Route.Watch.MaxZMm }, Partial = n.Route.Partial,
            },
        }).ToArray(),
        Vacancies = life.Vacancies.Values.Select(v => new NpcVacancyDto
            {
                SlotId = v.SlotId, LeftDay = v.LeftDay, ArrivalDay = v.ArrivalDay, NextGeneration = v.NextGeneration, ArrivingName = v.ArrivingName,
            })
            .ToArray(),
        PairsLastMet = life.PairsLastMet.Select(kv => new PairMetDto { Pair = kv.Key, Hour = kv.Value }).ToArray(),
    };

    /// <summary>
    /// The entities section: slot-keyed records, created instances, changed containers and creature records, each with its
    /// host cell's baseline hash, and the sounds waiting to be heard.
    /// </summary>
    public static (ImmutableArray<EntityDeltaRecord> Entities, ImmutableArray<CreatedEntityRecord> Created, ImmutableArray<ContainerRecord> Containers,
        ImmutableArray<CreatureRecord> Creatures, ImmutableArray<Noise> Noises) DecodeEntitySection(byte[] bytes)
    {
        var section = MessagePackSerializer.Deserialize<EntitiesSectionDto>(bytes, Options);
        var baselines = section.Baselines.ToDictionary(b => b.CellKey, b => b.BaselineHash, StringComparer.Ordinal);
        var entities = section.Records
            .Select(e => new EntityDeltaRecord(
                EntityId.Parse(e.InstanceId), e.SlotKey, e.GenerationSeq, e.DefId,
                e.State.Alive, e.State.XCm, e.State.ZCm,
                baselines.GetValueOrDefault(HostCell(e.SlotKey))))
            .ToImmutableArray();
        var created = section.Created
            .Select(c => new CreatedEntityRecord(
                EntityId.Parse(c.InstanceId), c.DefId, c.HostCell, c.XCm, c.ZCm, baselines.GetValueOrDefault(c.HostCell))
            {
                Count = c.Count ?? throw new FormatException($"created instance {c.InstanceId} has no count (required from schema 6)"),
                Quality = QualityOf(c.Quality, $"created instance {c.InstanceId}"),
                Notable = NotableOf(c.Notable, $"created instance {c.InstanceId}"),
                Wear = WearOf(c.Wear, $"created instance {c.InstanceId}"),
            })
            .ToImmutableArray();
        var containers = (section.Containers ?? throw new FormatException("entities.msgpack has no containers list (required from schema 6)"))
            .Select(c => new ContainerRecord(
                c.Key, EntityId.Parse(c.InstanceId), c.HostCell,
                c.Items.Select(i => new ContainerItem(EntityId.Parse(i.ItemId), i.DefId, i.Count) { Quality = QualityOf(i.Quality, $"{c.Key} item {i.ItemId}"), Notable = NotableOf(i.Notable, $"{c.Key} item {i.ItemId}"), Wear = WearOf(i.Wear, $"{c.Key} item {i.ItemId}") })
                    .ToImmutableArray(),
                baselines.GetValueOrDefault(c.HostCell)))
            .ToImmutableArray();
        var creatures = (section.Creatures ?? throw new FormatException("entities.msgpack has no creatures list (required from schema 8)"))
            .Select(c =>
            {
                var continuation = c.Continuation ?? throw new FormatException($"creature {c.Key} has no continuation (required from schema 14)");
                return new CreatureRecord(c.Key, c.DefId, EntityId.Parse(c.InstanceId), c.HostCell, c.Generation, CreatureConditions.Parse(c.Condition),
                    c.XMm, c.ZMm, c.FacingMdeg, c.Health, c.DiedTick, c.RespawnTick, baselines.GetValueOrDefault(c.HostCell))
                {
                    Mind = CreatureMinds.Parse(c.Mind),
                    Awareness = c.Awareness,
                    Knows = c.Knows,
                    KnownXMm = c.KnownXMm,
                    KnownZMm = c.KnownZMm,
                    LastSeenTick = c.LastSeenTick,
                    SearchUntil = c.SearchUntil,
                    HasCalled = c.HasCalled,
                    NextChargeTick = continuation.NextChargeTick,
                    StaggerImmuneUntil = continuation.StaggerImmuneUntil,
                    StaggeredTick = continuation.StaggeredTick,
                    StaggerLastsTicks = continuation.StaggerLastsTicks,
                };
            })
            .ToImmutableArray();
        var noises = (section.Noises ?? throw new FormatException("entities.msgpack has no noises list (required from schema 14)"))
            .Select(n => new Noise(n.XMm, n.ZMm, n.RadiusMm, n.Call, n.CallerKind))
            .ToImmutableArray();
        return (entities, created, containers, creatures, noises);
    }

    public static ImmutableArray<EntityDeltaRecord> DecodeEntities(byte[] bytes) => DecodeEntitySection(bytes).Entities;

    /// <summary>The entities section's economy (schema 22; WP14): the markets and the road (null before the economy first ran), and the merchants.</summary>
    public static (EconomyState? Economy, ImmutableArray<MerchantState> Merchants) DecodeEconomy(byte[] bytes)
    {
        var section = MessagePackSerializer.Deserialize<EntitiesSectionDto>(bytes, Options);
        var merchants = (section.Merchants ?? throw new FormatException("entities.msgpack has no merchants list (required from schema 22)"))
            .Select(m => new MerchantState(m.MerchantId, m.Purse, m.RestockedDay, m.ShelfDay, m.Traded))
            .ToImmutableArray();
        if (section.Economy is not { } economy)
            return (null, merchants);
        static ImmutableSortedDictionary<string, long> Counts(GoodCountDto[] counts, string what) =>
            counts.Select(c => KeyValuePair.Create(c.ItemId, c.Count)).ToImmutableSortedDictionary(StringComparer.Ordinal) is var built
                && built.Count == counts.Length ? built : throw new FormatException($"{what} names a good twice");
        var markets = economy.Markets.Select(m => new MarketState(m.MarketId, Counts(m.Stock, $"market {m.MarketId} stock"),
            Counts(m.SoldByCharacter, $"market {m.MarketId} sell history"))).ToList();
        if (markets.Select(m => m.MarketId).Distinct(StringComparer.Ordinal).Count() != markets.Count)
            throw new FormatException("entities.msgpack names a market twice");
        return (new EconomyState(economy.Day, markets.ToImmutableSortedDictionary(m => m.MarketId, m => m, StringComparer.Ordinal),
            economy.Shipments.Select(s => new Shipment(s.LinkId, s.FromMarket, s.ToMarket, s.ItemId, s.Count, s.ArrivesDay)).ToImmutableArray()), merchants);
    }

    /// <summary>
    /// The entities section's NPC life (schema 26; WP12): null in a save from before lives first ran. A malformed life (a route that cannot be
    /// one, an unknown condition, a key held twice) fails the section; a life content no longer defines is dropped when the world starts.
    /// </summary>
    public static NpcLifeState? DecodeNpcLife(byte[] bytes)
    {
        var section = MessagePackSerializer.Deserialize<EntitiesSectionDto>(bytes, Options);
        if (section.NpcLife is not { } life)
            return null;
        var npcs = ImmutableSortedDictionary.CreateBuilder<string, NpcLifeRecord>(StringComparer.Ordinal);
        foreach (var n in life.Npcs)
        {
            if (NavRoute.ParseStatus(n.Route.Status) is not { } status || n.Route.Corners.Length % 2 != 0 || n.Route.Watch.Length != 4)
                throw new FormatException($"npc life {n.Key} has a malformed route");
            var corners = Enumerable.Range(0, n.Route.Corners.Length / 2).Select(i => new NavPoint(n.Route.Corners[2 * i], n.Route.Corners[2 * i + 1])).ToImmutableArray();
            var route = NavRoute.TryCreate(status, n.Route.GoalXMm, n.Route.GoalZMm, corners, n.Route.PlannedTick, n.Route.Stamp,
                new NavRect(n.Route.Watch[0], n.Route.Watch[1], n.Route.Watch[2], n.Route.Watch[3]), n.Route.Partial, out string? problem)
                ?? throw new FormatException($"npc life {n.Key}: {problem}");
            var condition = n.Condition switch
            {
                "living" => NpcCondition.Living,
                "injured" => NpcCondition.Injured,
                "hired" => NpcCondition.Hired,   // schema 28 (WP12b): the party's, their routine suspended
                _ => throw new FormatException($"npc life {n.Key} has an unknown condition '{n.Condition}'"),
            };
            var record = new NpcLifeRecord(n.Key, n.RoutineId, new NpcNeeds(n.Hunger, n.Rest, n.Social, n.Coin), n.Activity, n.XMm, n.ZMm, n.FacingMdeg)
            {
                Name = n.Name, Generation = n.Generation, ArrivedDay = n.ArrivedDay, With = n.With, Condition = condition, ConditionUntil = n.ConditionUntil,
                Route = route, StuckTicks = n.StuckTicks, Cell = n.Cell,
            };
            if (!npcs.TryAdd(n.Key, record))
                throw new FormatException($"entities.msgpack holds npc life {n.Key} twice");
        }
        var vacancies = ImmutableSortedDictionary.CreateBuilder<string, NpcVacancy>(StringComparer.Ordinal);
        foreach (var v in life.Vacancies)
        {
            if (!vacancies.TryAdd(v.SlotId, new NpcVacancy(v.SlotId, v.LeftDay, v.ArrivalDay, v.NextGeneration) { ArrivingName = v.ArrivingName }))
                throw new FormatException($"entities.msgpack holds the vacancy of {v.SlotId} twice");
        }
        var met = ImmutableSortedDictionary.CreateBuilder<string, long>(StringComparer.Ordinal);
        foreach (var p in life.PairsLastMet)
        {
            if (!met.TryAdd(p.Pair, p.Hour))
                throw new FormatException($"entities.msgpack holds the pair {p.Pair} twice");
        }
        return new NpcLifeState(life.Hour, npcs.ToImmutable(), vacancies.ToImmutable(), met.ToImmutable());
    }

    /// <summary>
    /// The entities section's adventurers (schema 29; WP13): null in a save from before adventurers first lived. A malformed adventurer (an
    /// unknown condition, slot or gear ID, a key held twice) fails the section; one content no longer places is dropped when the world starts.
    /// </summary>
    public static AdventurerState? DecodeAdventurers(byte[] bytes)
    {
        var section = MessagePackSerializer.Deserialize<EntitiesSectionDto>(bytes, Options);
        if (section.Adventurers is not { } dto)
            return null;
        var agents = ImmutableSortedDictionary.CreateBuilder<string, AdventurerRecord>(StringComparer.Ordinal);
        foreach (var a in dto.Agents)
        {
            var condition = a.Condition switch
            {
                "active" => AdventurerCondition.Active,
                "injured" => AdventurerCondition.Injured,
                _ => throw new FormatException($"adventurer {a.Key} has an unknown condition '{a.Condition}'"),
            };
            var gear = ImmutableSortedDictionary.CreateBuilder<EquipSlot, AdventurerGear>();
            foreach (var g in a.Gear)
            {
                if (!EntityId.TryParse(g.ItemId, out var id) || id.Kind != EntityKind.Item || !DefinitionId.IsValid(g.DefId) || !gear.TryAdd(EquipSlots.Parse(g.Slot), new AdventurerGear(id, g.DefId, g.Quality)))
                    throw new FormatException($"adventurer {a.Key} has a malformed or doubled gear piece in {g.Slot}");
            }
            var pack = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
            foreach (var item in a.Pack)
            {
                if (item.Count > int.MaxValue || !pack.TryAdd(item.ItemId, (int)item.Count))
                    throw new FormatException($"adventurer {a.Key} carries {item.ItemId} twice");
            }
            var record = new AdventurerRecord(a.Key, a.Slot, a.Generation, a.Name, a.Settlement, a.At, ProgressionCodec.FromDto(a.Progression), a.Coin, a.RiskPercent)
            {
                PeopleId = a.PeopleId, ArrivedDay = a.ArrivedDay, Gear = gear.ToImmutable(), Pack = pack.ToImmutable(), Condition = condition,
                InjuredUntilDay = a.InjuredUntilDay, Party = a.Party, Acquired = a.Acquired, Kills = a.Kills, BountiesClaimed = a.BountiesClaimed,
                Plan = new AdventurerPlan(a.Plan.Day, a.Plan.Goal, a.Plan.Ref)
                {
                    Group = a.Plan.Group.ToImmutableArray(), PostTick = a.Plan.PostTick, DepartTick = a.Plan.DepartTick,
                },
            };
            if (!agents.TryAdd(a.Key, record))
                throw new FormatException($"entities.msgpack holds adventurer {a.Key} twice");
        }
        var vacancies = ImmutableSortedDictionary.CreateBuilder<string, AdventurerVacancy>(StringComparer.Ordinal);
        foreach (var v in dto.Vacancies)
        {
            if (!vacancies.TryAdd(v.Slot, new AdventurerVacancy(v.Slot, v.Settlement, v.LeftDay, v.ArrivalDay, v.NextGeneration)))
                throw new FormatException($"entities.msgpack holds the adventurer slot {v.Slot} twice");
        }
        var bodies = ImmutableSortedDictionary.CreateBuilder<string, AdventurerBody>(StringComparer.Ordinal);
        foreach (var b in dto.Bodies)
        {
            if (!DefinitionId.IsValid(b.Key) || !bodies.TryAdd(b.Key, new AdventurerBody(b.Name, b.XMm, b.ZMm, b.FacingMdeg)))
                throw new FormatException($"entities.msgpack holds the adventurer body {b.Key} twice, or under a malformed key");
        }
        return new AdventurerState(dto.Day, agents.ToImmutable(), vacancies.ToImmutable()) { Bodies = bodies.ToImmutable() };
    }

    /// <summary>The cell key of a slot key: <c>&lt;cell_key&gt;.&lt;population_id&gt;.&lt;ordinal&gt;</c>, and a cell key has no '.'.</summary>
    public static string HostCell(string slotKey)
    {
        int dot = slotKey.IndexOf('.');
        return dot > 0 ? slotKey[..dot] : slotKey;
    }

    /// <summary>Manifest JSON options, shared with the migration chain, which edits the manifest as JSON.</summary>
    public static JsonSerializerOptions ManifestOptions => ManifestJson;
}
