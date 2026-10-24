// UNNAMED Persistence - schema 21 section shapes (the party, WP15a). FROZEN.
// No Godot references - pure C#
//
// The entities section schemas 19 to 21 wrote: written by the 18 -> 19 step and read by the 21 -> 22 step; the schema-19 to -21 fixtures
// pin it. Its created instances and containers are V23's; its parts that schema 22 did not change are the current DTOs (EntityDto,
// CellBaselineDto, CreatureDto, NoiseDto); the step that next changes one of those must freeze a copy of it first.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V21;

[MessagePackObject]
public sealed class EntitiesSection
{
    [Key("records")] public EntityDto[] Records { get; set; } = Array.Empty<EntityDto>();
    [Key("created")] public V23.Created[] Created { get; set; } = Array.Empty<V23.Created>();
    [Key("baselines")] public CellBaselineDto[] Baselines { get; set; } = Array.Empty<CellBaselineDto>();
    [Key("containers")] public V23.Container[]? Containers { get; set; }
    [Key("creatures")] public UNNAMED.Persistence.Sections.V37.CreatureDto[]? Creatures { get; set; }
    [Key("noises")] public NoiseDto[]? Noises { get; set; }
}
