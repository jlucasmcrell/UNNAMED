// UNNAMED Persistence - schema 46: the party out on an expedition (GEN-06)
// No Godot references - pure C#

using MessagePack;
using UNNAMED.Persistence.Sections;

namespace UNNAMED.Persistence;

// The numbers 41 to 45 are charged to other lanes' save changes in the plan; their steps are not on this branch, and a chain from a save made
// before them has to be whole for this build to read old saves at all. So this branch carries the reserved numbers as pass-throughs - each
// rewrites the manifest's schema_version and nothing else - and does its real work in the last step, 45 -> 46. At merge: drop the reserved
// steps for the real lanes' steps, keep 45 -> 46 and the expeditions field it adds.

/// <summary>Reserved: the number belongs to another lane, and nothing in this build's shape moves.</summary>
public abstract class ReservedSchemaStep : SchemaMigration
{
    public override string Summary => $"schema {From} -> {To}: reserved for a save change charged to another lane; this build's shape does not move";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

public sealed class SchemaV40ToV41 : ReservedSchemaStep
{
    public override int From => 40;
}

public sealed class SchemaV41ToV42 : ReservedSchemaStep
{
    public override int From => 41;
}

public sealed class SchemaV42ToV43 : ReservedSchemaStep
{
    public override int From => 42;
}

public sealed class SchemaV43ToV44 : ReservedSchemaStep
{
    public override int From => 43;
}

public sealed class SchemaV44ToV45 : ReservedSchemaStep
{
    public override int From => 44;
}

/// <summary>
/// The party out from each settlement joins the adventurers' section, and with it the day each adventurer was last home: an older save has
/// nobody on the road (nobody had gone before), so its parties are empty and every adventurer has been home since the beginning of the world.
/// </summary>
public sealed class SchemaV45ToV46 : SchemaMigration
{
    public override int From => 45;

    public override string Summary => "schema 45 -> 46: adventurers keep the party out from each settlement (none out before), and the day each was last home (always, before)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        if (document.Sections.GetValueOrDefault(SaveFormat.Entities) is { } bytes)
        {
            // The section this build writes is the schema-46 shape: re-serializing an older one fills the new fields at their empty values.
            var section = MessagePackSerializer.Deserialize<EntitiesSectionDto>(bytes, SectionCodec.MessagePackOptions);
            if (section.Adventurers is { } adventurers)
                adventurers.Expeditions = Array.Empty<ExpeditionDto>();
            document.Sections[SaveFormat.Entities] = MessagePackSerializer.Serialize(section, SectionCodec.MessagePackOptions);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}
