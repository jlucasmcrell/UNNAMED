// UNNAMED Persistence - schema 26-28 entities section shape (NPC life, WP12). FROZEN.
// No Godot references - pure C#
//
// The entities section schemas 26 to 28 wrote: written by the 25 -> 26 step and read by the 28 -> 29 step (schemas 27 and 28 left it
// alone); the schema-26 to -28 fixtures pin it. Its NPC life still holds the needs, the visits and the pairs schema 29 dropped (owner,
// 2026-10-17). Its parts that schema 29 did not change are the current DTOs (EntityDto, CreatedDto, CellBaselineDto, ContainerDto,
// CreatureDto, NoiseDto, EconomyDto, MerchantDto, NavRouteDto); the step that next changes one of those must freeze a copy of it first.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V28;

[MessagePackObject]
public sealed class EntitiesSection
{
    [Key("records")] public EntityDto[] Records { get; set; } = Array.Empty<EntityDto>();
    [Key("created")] public CreatedDto[] Created { get; set; } = Array.Empty<CreatedDto>();
    [Key("baselines")] public CellBaselineDto[] Baselines { get; set; } = Array.Empty<CellBaselineDto>();
    [Key("containers")] public ContainerDto[]? Containers { get; set; }
    [Key("creatures")] public UNNAMED.Persistence.Sections.V37.CreatureDto[]? Creatures { get; set; }
    [Key("noises")] public NoiseDto[]? Noises { get; set; }
    [Key("economy")] public EconomyDto? Economy { get; set; }
    [Key("merchants")] public MerchantDto[]? Merchants { get; set; }
    [Key("npc_life")] public NpcLife? NpcLife { get; set; }
}

[MessagePackObject]
public sealed class NpcLife
{
    [Key("hour")] public long Hour { get; set; }
    [Key("npcs")] public NpcLifeRecord[] Npcs { get; set; } = Array.Empty<NpcLifeRecord>();
    [Key("vacancies")] public NpcVacancy[] Vacancies { get; set; } = Array.Empty<NpcVacancy>();
    [Key("pairs_last_met")] public PairMet[] PairsLastMet { get; set; } = Array.Empty<PairMet>();
}

[MessagePackObject]
public sealed class NpcLifeRecord
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

[MessagePackObject]
public sealed class PairMet
{
    [Key("pair")] public string Pair { get; set; } = "";
    [Key("hour")] public long Hour { get; set; }
}

[MessagePackObject]
public sealed class NpcVacancy
{
    [Key("slot_id")] public string SlotId { get; set; } = "";
    [Key("left_day")] public long LeftDay { get; set; }
    [Key("arrival_day")] public long ArrivalDay { get; set; }
    [Key("next_generation")] public int NextGeneration { get; set; }
}
