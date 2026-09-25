// UNNAMED Persistence - schema 14 section shapes (the Phase-1 technical audit). FROZEN.
// No Godot references - pure C#
//
// The player shape schemas 13 and 14 wrote (12 -> 13 writes it; 13 -> 14 does not touch it), the companion shape schemas 12-14 wrote,
// and the entities shape schema 14 wrote (13 -> 14 writes it, with the creature continuation and the sounds waiting to be heard). The
// v12-v14 fixtures pin them. Their parts that schema 15 did not change are the current DTOs (InventoryDto, ProgressionDto,
// DiscoveryDto, EquipmentDto, EffectDto, RelationshipDto, ConversationDto, QuestDto, PostureDto, EntityDto, CreatedDto,
// CellBaselineDto, ContainerDto, CreatureDto, NoiseDto); the step that next changes one of those must freeze a copy of it first.

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
    [Key("companions")] public Companion[]? Companions { get; set; }
    [Key("posture")] public PostureDto? Posture { get; set; }
}

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
}

[MessagePackObject]
public sealed class EntitiesSection
{
    [Key("records")] public EntityDto[] Records { get; set; } = Array.Empty<EntityDto>();
    [Key("created")] public CreatedDto[] Created { get; set; } = Array.Empty<CreatedDto>();
    [Key("baselines")] public CellBaselineDto[] Baselines { get; set; } = Array.Empty<CellBaselineDto>();
    [Key("containers")] public ContainerDto[]? Containers { get; set; }
    [Key("creatures")] public CreatureDto[]? Creatures { get; set; }
    [Key("noises")] public NoiseDto[]? Noises { get; set; }
}
