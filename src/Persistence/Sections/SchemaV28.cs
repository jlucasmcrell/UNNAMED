// UNNAMED Persistence - schema 26-28 entities section shape (NPC life, WP12). FROZEN.
// No Godot references - pure C#
//
// The entities section schemas 26 to 28 wrote: written by the 25 -> 26 step and read by the 28 -> 29 step (schemas 27 and 28 left it
// alone); the schema-26 to -28 fixtures pin it. Its parts that schema 29 did not change are the current DTOs (EntityDto, CreatedDto,
// CellBaselineDto, ContainerDto, CreatureDto, NoiseDto, EconomyDto, MerchantDto, NpcLifeRecordDto, PairMetDto); the step that next changes
// one of those must freeze a copy of it first.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V28;

[MessagePackObject]
public sealed class EntitiesSection
{
    [Key("records")] public EntityDto[] Records { get; set; } = Array.Empty<EntityDto>();
    [Key("created")] public CreatedDto[] Created { get; set; } = Array.Empty<CreatedDto>();
    [Key("baselines")] public CellBaselineDto[] Baselines { get; set; } = Array.Empty<CellBaselineDto>();
    [Key("containers")] public ContainerDto[]? Containers { get; set; }
    [Key("creatures")] public CreatureDto[]? Creatures { get; set; }
    [Key("noises")] public NoiseDto[]? Noises { get; set; }
    [Key("economy")] public EconomyDto? Economy { get; set; }
    [Key("merchants")] public MerchantDto[]? Merchants { get; set; }
    [Key("npc_life")] public NpcLife? NpcLife { get; set; }
}

[MessagePackObject]
public sealed class NpcLife
{
    [Key("hour")] public long Hour { get; set; }
    [Key("npcs")] public NpcLifeRecordDto[] Npcs { get; set; } = Array.Empty<NpcLifeRecordDto>();
    [Key("vacancies")] public NpcVacancy[] Vacancies { get; set; } = Array.Empty<NpcVacancy>();
    [Key("pairs_last_met")] public PairMetDto[] PairsLastMet { get; set; } = Array.Empty<PairMetDto>();
}

[MessagePackObject]
public sealed class NpcVacancy
{
    [Key("slot_id")] public string SlotId { get; set; } = "";
    [Key("left_day")] public long LeftDay { get; set; }
    [Key("arrival_day")] public long ArrivalDay { get; set; }
    [Key("next_generation")] public int NextGeneration { get; set; }
}
