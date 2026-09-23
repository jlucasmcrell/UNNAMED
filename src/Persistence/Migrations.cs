// UNNAMED Persistence - the schema migration chain (PERSISTENCE.md §6.2, M2b §10)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Text.Json.Nodes;
using MessagePack;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using UNNAMED.World.Legacy;
using V1 = UNNAMED.Persistence.Sections.V1;

namespace UNNAMED.Persistence;

/// <summary>
/// A save in transit between schema versions: the manifest as JSON and each section as bytes. A
/// migration rewrites only the sections whose shape changes; the rest pass through untouched.
/// </summary>
public sealed class MigrationDocument
{
    public MigrationDocument(int schema, JsonObject manifest, IDictionary<string, byte[]?> sections)
    {
        Schema = schema;
        Manifest = manifest;
        Sections = new Dictionary<string, byte[]?>(sections, StringComparer.Ordinal);
    }

    public int Schema { get; internal set; }

    public JsonObject Manifest { get; }

    /// <summary>Section file name to bytes. Null for a quarantined section, which a migration passes through as null.</summary>
    public Dictionary<string, byte[]?> Sections { get; }
}

/// <summary>What a migration may consult besides the save itself.</summary>
public sealed record MigrationEnvironment(ICellBaselineGenerator Generator);

/// <summary>
/// One step of the ordered chain: schema <see cref="From"/> to <see cref="To"/>, as a pure
/// function of the document. A step never touches the filesystem - the migrated save is committed
/// by the §7.1 write sequence - and never resolves definition IDs, which the alias pass does once,
/// on the current shape. A problem it cannot resolve is a blocker in the report, not a guess.
/// </summary>
public abstract class SchemaMigration
{
    public abstract int From { get; }

    public int To => From + 1;

    public abstract string Summary { get; }

    public abstract void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report);
}

public static class SchemaMigrations
{
    /// <summary>The single ordered table (§6.2). A schema bump adds exactly one step here, plus a fixture.</summary>
    public static readonly ImmutableArray<SchemaMigration> Production = ImmutableArray.Create<SchemaMigration>(
        new SchemaV1ToV2());

    /// <summary>The steps from one schema to another, in order - or empty and false when the table has a gap.</summary>
    public static bool TryChain(ImmutableArray<SchemaMigration> table, int from, int to, out ImmutableArray<SchemaMigration> chain)
    {
        var steps = ImmutableArray.CreateBuilder<SchemaMigration>();
        for (int version = from; version < to; version++)
        {
            var step = table.SingleOrDefault(m => m.From == version);
            if (step is null)
            {
                chain = ImmutableArray<SchemaMigration>.Empty;
                return false;
            }
            steps.Add(step);
        }
        chain = steps.ToImmutable();
        return true;
    }
}

/// <summary>
/// Schema 1 (M2) to 2 (M2b), which is also worldgen 1 to 2. Worldgen 1 seeded every random draw with
/// the whole content_hash and keyed nodes by generation index; worldgen 2 draws from semantic channels
/// and keys nodes by rule name and ordinal. So this step reinterprets each saved node key through
/// the frozen worldgen-1 generator, moves every record onto the worldgen-2 baseline by semantic
/// identity, and records the baseline hash each record is now proven against. A record whose target
/// has no worldgen-2 counterpart is dropped and reported as loss, never re-aimed at something else.
/// </summary>
public sealed class SchemaV1ToV2 : SchemaMigration
{
    public override int From => 1;

    public override string Summary =>
        "schema 1 -> 2: worldgen 1 -> 2 (semantic random channels, node keys by rule name), " +
        "per-cell baseline hashes, worldgen_fingerprint and rng_contract_version replace worldgen_digest";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var manifest = document.Manifest;
        int worldgen = manifest["worldgen_version"]?.GetValue<int>() ?? 0;
        string? worldgenDigest = manifest["worldgen_digest"]?.GetValue<string>();
        string? contentHash = manifest["content_hash"]?.GetValue<string>();
        string? seedText = manifest["world_seed"]?.GetValue<string>();
        if (worldgen != CellBaselineGeneratorV1.Version || worldgenDigest is null || contentHash is null || seedText is null)
        {
            report.Blockers.Add("schema-1 manifest must carry worldgen_version 1, worldgen_digest, content_hash and world_seed");
            return;
        }

        // Worldgen 1's output depended on the placement data; it can be regenerated only from the same
        // data, which its worldgen_digest pins.
        var legacy = new CellBaselineGeneratorV1(environment.Generator.Profile);
        if (legacy.WorldgenDigest != worldgenDigest)
        {
            report.Blockers.Add(
                $"the worldgen-1 baseline this save was made against cannot be regenerated: the save's worldgen_digest is " +
                $"{worldgenDigest}, the running placement data gives {legacy.WorldgenDigest}");
            return;
        }

        ulong seed = WorldSeed.Parse(seedText);
        var tuple = new BaselineTuple(seed, CellBaselineGeneratorV1.Version, contentHash);
        var baselines = new Dictionary<CellKey, CellBaseline>();
        CellBaseline Current(CellKey cell) =>
            baselines.TryGetValue(cell, out var b) ? b : baselines[cell] = environment.Generator.Generate(seed, cell);

        if (document.Sections.GetValueOrDefault(SaveFormat.Cells) is { } cells)
            document.Sections[SaveFormat.Cells] = MigrateCells(cells, legacy, tuple, Current, report);
        if (document.Sections.GetValueOrDefault(SaveFormat.Entities) is { } entities)
            document.Sections[SaveFormat.Entities] = MigrateEntities(entities, Current, report);

        manifest.Remove("worldgen_digest");
        manifest["worldgen_version"] = environment.Generator.WorldgenVersion;
        manifest["worldgen_fingerprint"] = environment.Generator.Fingerprint;
        manifest["rng_contract_version"] = environment.Generator.RngContractVersion;
        manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }

    private static byte[] MigrateCells(
        byte[] bytes, CellBaselineGeneratorV1 legacy, BaselineTuple tuple, Func<CellKey, CellBaseline> current, MigrationReport report)
    {
        var section = MessagePackSerializer.Deserialize<V1.CellsSection>(bytes, SectionCodec.MessagePackOptions);
        var records = new List<CellDto>();
        foreach (var cell in section.Records)
        {
            if (!CellKey.TryParse(cell.CellKey, out var key))
            {
                report.Loss.Add($"cell record '{cell.CellKey}' has no valid cell key; dropped");
                continue;
            }
            var origins = legacy.NodeOrigins(tuple, key).ToDictionary(o => o.NodeKey, StringComparer.Ordinal);
            var baseline = current(key);

            var nodes = new List<NodeDto>();
            foreach (var node in cell.HarvestedNodes)
            {
                if (!origins.TryGetValue(node.NodeKey, out var origin))
                {
                    report.Loss.Add($"{cell.CellKey}: harvested node '{node.NodeKey}' is not a worldgen-1 node of that cell; dropped");
                    continue;
                }
                string semanticKey = Keys.NodeKey(key, origin.Rule.Name, origin.Ordinal);
                if (baseline.FindNode(semanticKey) is null)
                {
                    report.Loss.Add($"{cell.CellKey}: harvested {origin.Rule.Name} #{origin.Ordinal} ('{node.NodeKey}') has no worldgen-2 counterpart; dropped");
                    continue;
                }
                nodes.Add(new NodeDto { NodeKey = semanticKey, LastHarvestTick = node.LastHarvestTick, HarvestSeq = node.HarvestSeq });
            }

            var flags = cell.Flags.Select(f => new FlagDto { Name = f.Name, Value = f.Value }).ToArray();
            var populations = cell.PopulationAlive.Select(p => new PopulationDto { PopulationId = p.PopulationId, Alive = p.Alive }).ToArray();
            if (flags.Length == 0 && nodes.Count == 0 && populations.Length == 0)
                continue;   // nothing left diverges: the record is rebased away

            var reasons = new List<string>();
            if (flags.Length > 0) reasons.Add("flags");
            if (nodes.Count > 0) reasons.Add("nodes");
            if (populations.Length > 0) reasons.Add("spawns");
            if (cell.DirtyReasons.Contains("entities")) reasons.Add("entities");
            reasons.Sort(StringComparer.Ordinal);

            records.Add(new CellDto
            {
                CellKey = cell.CellKey,
                BaselineHash = baseline.Digest,
                DirtyReasons = reasons.ToArray(),
                Flags = flags,
                HarvestedNodes = nodes.OrderBy(n => n.NodeKey, StringComparer.Ordinal).ToArray(),
                PopulationAlive = populations,
            });
        }
        return MessagePackSerializer.Serialize(new CellsSectionDto { Records = records.ToArray() }, SectionCodec.MessagePackOptions);
    }

    private static byte[] MigrateEntities(byte[] bytes, Func<CellKey, CellBaseline> current, MigrationReport report)
    {
        var section = MessagePackSerializer.Deserialize<V1.EntitiesSection>(bytes, SectionCodec.MessagePackOptions);
        var records = new List<EntityDto>();
        var hostBaselines = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in section.Records)
        {
            string host = SectionCodec.HostCell(e.SlotKey);
            if (!CellKey.TryParse(host, out var cell) || current(cell).FindSlot(e.SlotKey) is null)
            {
                report.Loss.Add($"entity {e.InstanceId}: slot '{e.SlotKey}' has no worldgen-2 counterpart; dropped");
                continue;
            }
            // Slot keys name a population and an ordinal, both unchanged by worldgen 2: identity carries
            // over. Positions are absolute, so a moved occupant keeps where it was moved to.
            hostBaselines[host] = current(cell).Digest;
            records.Add(new EntityDto
            {
                InstanceId = e.InstanceId,
                SlotKey = e.SlotKey,
                GenerationSeq = e.GenerationSeq,
                DefId = e.DefId,
                DirtyMask = e.DirtyMask,
                State = new EntityStateDto { Alive = e.State.Alive, XCm = e.State.XCm, ZCm = e.State.ZCm },
            });
        }
        return MessagePackSerializer.Serialize(new EntitiesSectionDto
        {
            Records = records.ToArray(),
            Baselines = hostBaselines.Select(kv => new CellBaselineDto { CellKey = kv.Key, BaselineHash = kv.Value }).ToArray(),
        }, SectionCodec.MessagePackOptions);
    }
}
