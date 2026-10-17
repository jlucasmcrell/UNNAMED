// UNNAMED Persistence - schema 13 section shapes (the owner's M6 playtest). FROZEN.
// No Godot references - pure C#
//
// The exact entities shape schemas 9 to 13 wrote: written by the 8 -> 9 step and read by the 13 -> 14 step; the schema-9 to -13
// fixtures pin it. Its creature records are V8.Creature, and its parts that schema 14 did not change are the current DTOs
// (EntityDto, CellBaselineDto) and the schema-18 stack shapes (V18.Created, V18.Container); the step that next changes one of those
// must freeze a copy of it first. Schema 14 left the other sections alone.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V13;

[MessagePackObject]
public sealed class EntitiesSection
{
    [Key("records")] public EntityDto[] Records { get; set; } = Array.Empty<EntityDto>();
    [Key("created")] public V18.Created[] Created { get; set; } = Array.Empty<V18.Created>();
    [Key("baselines")] public CellBaselineDto[] Baselines { get; set; } = Array.Empty<CellBaselineDto>();
    [Key("containers")] public V18.Container[]? Containers { get; set; }
    [Key("creatures")] public V8.Creature[]? Creatures { get; set; }
}
