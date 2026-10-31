// UNNAMED Persistence - schema 25 section shapes (the party's sheets, WP15b). FROZEN.
// No Godot references - pure C#
//
// The entities section schemas 24 and 25 wrote: written by the 23 -> 24 step and read by the 25 -> 26 step; the schema-24 and -25 fixtures
// pin it. Its parts that schema 26 did not change are the current DTOs (EntityDto, CreatedDto, CellBaselineDto, ContainerDto, CreatureDto,
// NoiseDto, EconomyDto, MerchantDto); the step that next changes one of those must freeze a copy of it first. Schema 26 left the other
// sections alone.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V25;

using ContainerDto = UNNAMED.Persistence.Sections.V41.ContainerDto;

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
}
