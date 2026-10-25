// UNNAMED Persistence - schema 43 -> 44: placed pieces (M7 design §4.18, re-ported by WP-1G), and the reserved Wave 1 slots
// No Godot references - pure C#

using MessagePack;
using UNNAMED.Persistence.Sections;

namespace UNNAMED.Persistence;

/// <summary>
/// A schema reserved for another Wave 1 package (<see cref="SaveFormat.ReservedSchemas"/>): until that package merges and its own step takes
/// this place, the save is carried across it unchanged.
/// </summary>
public sealed class ReservedSchemaStep : SchemaMigration
{
    private readonly int _from;

    public ReservedSchemaStep(int from) => _from = from;

    public override int From => _from;
    public override string Summary => $"schema {_from} -> {_from + 1}: reserved for another Wave 1 package; the save is carried unchanged";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>Placed pieces and the structure sequence join the entities section; nothing was built before them, so older saves have none and 0.</summary>
public sealed class SchemaV43ToV44 : SchemaMigration
{
    public override int From => 43;
    public override string Summary => "schema 43 -> 44: placed pieces and the structure sequence (none and 0 before: nothing was built)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        if (document.Sections.GetValueOrDefault(SaveFormat.Entities) is { } bytes)
        {
            var old = MessagePackSerializer.Deserialize<Sections.V43.EntitiesSection>(bytes, SectionCodec.MessagePackOptions);
            var next = MessagePackSerializer.Deserialize<EntitiesSectionDto>(MessagePackSerializer.Serialize(old, SectionCodec.MessagePackOptions), SectionCodec.MessagePackOptions);
            next.Pieces = Array.Empty<PieceDto>();
            next.StructureSeq = 0;
            document.Sections[SaveFormat.Entities] = MessagePackSerializer.Serialize(next, SectionCodec.MessagePackOptions);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}
