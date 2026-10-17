// UNNAMED Persistence - the schema migration chain (PERSISTENCE.md §6.2, M2b §10)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using MessagePack;
using UNNAMED.Domain;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using UNNAMED.World.Legacy;
using V1 = UNNAMED.Persistence.Sections.V1;
using V2 = UNNAMED.Persistence.Sections.V2;
using V3 = UNNAMED.Persistence.Sections.V3;
using V4 = UNNAMED.Persistence.Sections.V4;
using V5 = UNNAMED.Persistence.Sections.V5;
using V6 = UNNAMED.Persistence.Sections.V6;
using V8 = UNNAMED.Persistence.Sections.V8;
using V9 = UNNAMED.Persistence.Sections.V9;
using V10 = UNNAMED.Persistence.Sections.V10;
using V11 = UNNAMED.Persistence.Sections.V11;
using V12 = UNNAMED.Persistence.Sections.V12;
using V13 = UNNAMED.Persistence.Sections.V13;
using V14 = UNNAMED.Persistence.Sections.V14;
using V15 = UNNAMED.Persistence.Sections.V15;
using V16 = UNNAMED.Persistence.Sections.V16;
using V18 = UNNAMED.Persistence.Sections.V18;
using V19 = UNNAMED.Persistence.Sections.V19;
using V20 = UNNAMED.Persistence.Sections.V20;
using V21 = UNNAMED.Persistence.Sections.V21;
using V22 = UNNAMED.Persistence.Sections.V22;
using V23 = UNNAMED.Persistence.Sections.V23;
using V24 = UNNAMED.Persistence.Sections.V24;
using V25 = UNNAMED.Persistence.Sections.V25;
using V26 = UNNAMED.Persistence.Sections.V26;
using V28 = UNNAMED.Persistence.Sections.V28;

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
        new SchemaV1ToV2(),
        new SchemaV2ToV3(),
        new SchemaV3ToV4(),
        new SchemaV4ToV5(),
        new SchemaV5ToV6(),
        new SchemaV6ToV7(),
        new SchemaV7ToV8(),
        new SchemaV8ToV9(),
        new SchemaV9ToV10(),
        new SchemaV10ToV11(),
        new SchemaV11ToV12(),
        new SchemaV12ToV13(),
        new SchemaV13ToV14(),
        new SchemaV14ToV15(),
        new SchemaV15ToV16(),
        new SchemaV16ToV17(),
        new SchemaV17ToV18(),
        new SchemaV18ToV19(),
        new SchemaV19ToV20(),
        new SchemaV20ToV21(),
        new SchemaV21ToV22(),
        new SchemaV22ToV23(),
        new SchemaV23ToV24(),
        new SchemaV24ToV25(),
        new SchemaV25ToV26(),
        new SchemaV26ToV27(),
        new SchemaV27ToV28(),
        new SchemaV28ToV29());

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
        var records = new List<V2.Cell>();
        foreach (var cell in section.Records)
        {
            if (!CellKey.TryParse(cell.CellKey, out var key))
            {
                report.Loss.Add($"cell record '{cell.CellKey}' has no valid cell key; dropped");
                continue;
            }
            var origins = legacy.NodeOrigins(tuple, key).ToDictionary(o => o.NodeKey, StringComparer.Ordinal);
            var baseline = current(key);

            var nodes = new List<V2.Node>();
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
                nodes.Add(new V2.Node { NodeKey = semanticKey, LastHarvestTick = node.LastHarvestTick, HarvestSeq = node.HarvestSeq });
            }

            var flags = cell.Flags.Select(f => new V2.Flag { Name = f.Name, Value = f.Value }).ToArray();
            var populations = cell.PopulationAlive.Select(p => new V2.Population { PopulationId = p.PopulationId, Alive = p.Alive }).ToArray();
            if (flags.Length == 0 && nodes.Count == 0 && populations.Length == 0)
                continue;   // nothing left diverges: the record is rebased away

            var reasons = new List<string>();
            if (flags.Length > 0) reasons.Add("flags");
            if (nodes.Count > 0) reasons.Add("nodes");
            if (populations.Length > 0) reasons.Add("spawns");
            if (cell.DirtyReasons.Contains("entities")) reasons.Add("entities");
            reasons.Sort(StringComparer.Ordinal);

            records.Add(new V2.Cell
            {
                CellKey = cell.CellKey,
                BaselineHash = baseline.Digest,
                DirtyReasons = reasons.ToArray(),
                Flags = flags,
                HarvestedNodes = nodes.OrderBy(n => n.NodeKey, StringComparer.Ordinal).ToArray(),
                PopulationAlive = populations,
            });
        }
        return MessagePackSerializer.Serialize(new V2.CellsSection { Records = records.ToArray() }, SectionCodec.MessagePackOptions);
    }

    private static byte[] MigrateEntities(byte[] bytes, Func<CellKey, CellBaseline> current, MigrationReport report)
    {
        var section = MessagePackSerializer.Deserialize<V1.EntitiesSection>(bytes, SectionCodec.MessagePackOptions);
        var records = new List<V2.Entity>();
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
            records.Add(new V2.Entity
            {
                InstanceId = e.InstanceId,
                SlotKey = e.SlotKey,
                GenerationSeq = e.GenerationSeq,
                DefId = e.DefId,
                DirtyMask = e.DirtyMask,
                State = new V2.EntityState { Alive = e.State.Alive, XCm = e.State.XCm, ZCm = e.State.ZCm },
            });
        }
        return MessagePackSerializer.Serialize(new V2.EntitiesSection
        {
            Records = records.ToArray(),
            Baselines = hostBaselines.Select(kv => new V2.CellBaseline { CellKey = kv.Key, BaselineHash = kv.Value }).ToArray(),
        }, SectionCodec.MessagePackOptions);
    }
}

/// <summary>
/// Schema 2 to 3 (M2b): the player gains the required <c>appearance_seed</c> (PERSISTENCE.md §5.1), and
/// the entities section gains created persistent instances. An older save has no appearance seed, so
/// it gets the value a character gets when nothing chose one - derived from its ULID, deterministic,
/// never random - and it has no created instances, because schema 2 could not record any.
/// </summary>
public sealed class SchemaV2ToV3 : SchemaMigration
{
    public override int From => 2;

    public override string Summary =>
        "schema 2 -> 3: the player gains the required appearance_seed (derived from the player's ULID for saves " +
        "that predate it); the entities section gains created persistent instances (none before schema 3)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V2.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V3.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = PlayerRecord.DerivedAppearanceSeed(EntityId.Parse(old.InstanceId)),
                Inventory = old.Inventory.Select(i => new V3.Inventory { ItemId = i.ItemId, DefId = i.DefId, Count = i.Count }).ToArray(),
            }, options);
        }
        if (document.Sections.GetValueOrDefault(SaveFormat.Entities) is { } entities)
        {
            var old = MessagePackSerializer.Deserialize<V2.EntitiesSection>(entities, options);
            document.Sections[SaveFormat.Entities] = MessagePackSerializer.Serialize(new V3.EntitiesSection
            {
                Records = old.Records.Select(e => new V3.Entity
                {
                    InstanceId = e.InstanceId,
                    SlotKey = e.SlotKey,
                    GenerationSeq = e.GenerationSeq,
                    DefId = e.DefId,
                    DirtyMask = e.DirtyMask,
                    State = new V3.EntityState { Alive = e.State.Alive, XCm = e.State.XCm, ZCm = e.State.ZCm },
                }).ToArray(),
                Created = Array.Empty<V3.Created>(),
                Baselines = old.Baselines.Select(b => new V3.CellBaseline { CellKey = b.CellKey, BaselineHash = b.BaselineHash }).ToArray(),
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 3 to 4 (M2c): the player gains the progression record (PROGRESSION.md §3-§4). A save that predates
/// progression had none, so its character becomes what the record's empty value says - level 1, no XP or
/// debt, nothing allocated, no skills, nothing known, full pools, no guard history. The step needs no content:
/// attribute bases and pool maxima are derived at run time, never stored.
/// </summary>
public sealed class SchemaV3ToV4 : SchemaMigration
{
    public override int From => 3;

    public override string Summary =>
        "schema 3 -> 4: the player gains the progression record (level 1, nothing learned or allocated, full pools, " +
        "for saves that predate progression)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V3.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V4.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory.Select(i => new V4.Inventory { ItemId = i.ItemId, DefId = i.DefId, Count = i.Count }).ToArray(),
                Progression = ProgressionCodec.ToDto(UNNAMED.Domain.Progression.CharacterProgression.Empty),
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 4 to 5 (M3): the player gains facing and discovered-location records (PROTOTYPE.md §7.4, PERSISTENCE.md
/// §5.1). A save that predates them faced +Z (0) - nothing recorded otherwise - and had discovered nothing, because
/// nothing could be discovered before M3.
/// </summary>
public sealed class SchemaV4ToV5 : SchemaMigration
{
    public override int From => 4;

    public override string Summary =>
        "schema 4 -> 5: the player gains facing (0, +Z, for saves that predate it) and discovered-location records (none before M3)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V4.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V5.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory.Select(i => new V5.Inventory { ItemId = i.ItemId, DefId = i.DefId, Count = i.Count }).ToArray(),
                Progression = old.Progression,
                FacingMdeg = 0,
                Discoveries = Array.Empty<V5.Discovery>(),
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 5 to 6 (M3b): the player gains equipment slots and a purse, a created instance gains its count, and the
/// entities section gains changed world containers. An older save had nothing equipped (equipment did not exist), no
/// coin, single created items, and untouched containers.
/// </summary>
public sealed class SchemaV5ToV6 : SchemaMigration
{
    public override int From => 5;

    public override string Summary =>
        "schema 5 -> 6: the player gains equipment (none) and currency (0); created instances gain a count (1); " +
        "the entities section gains changed world containers (none before M3b)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V5.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V6.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory.Select(i => new V6.Inventory { ItemId = i.ItemId, DefId = i.DefId, Count = i.Count }).ToArray(),
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = (old.Discoveries ?? Array.Empty<V5.Discovery>())
                    .Select(d => new V6.Discovery { LocationId = d.LocationId, Method = d.Method, Tick = d.Tick }).ToArray(),
                Equipment = Array.Empty<V6.Equipment>(),
                Currency = 0,
            }, options);
        }
        if (document.Sections.GetValueOrDefault(SaveFormat.Entities) is { } entities)
        {
            var old = MessagePackSerializer.Deserialize<V3.EntitiesSection>(entities, options);
            document.Sections[SaveFormat.Entities] = MessagePackSerializer.Serialize(new V6.EntitiesSection
            {
                Records = old.Records.Select(e => new V6.Entity
                {
                    InstanceId = e.InstanceId,
                    SlotKey = e.SlotKey,
                    GenerationSeq = e.GenerationSeq,
                    DefId = e.DefId,
                    DirtyMask = e.DirtyMask,
                    State = new V6.EntityState { Alive = e.State.Alive, XCm = e.State.XCm, ZCm = e.State.ZCm },
                }).ToArray(),
                Created = old.Created.Select(c => new V6.Created
                {
                    InstanceId = c.InstanceId, DefId = c.DefId, HostCell = c.HostCell, XCm = c.XCm, ZCm = c.ZCm, Count = 1,
                }).ToArray(),
                Baselines = old.Baselines.Select(b => new V6.CellBaseline { CellKey = b.CellKey, BaselineHash = b.BaselineHash }).ToArray(),
                Containers = Array.Empty<V6.Container>(),
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 6 to 7 (M3c): the player gains active status effects (SYSTEMS.md S-11). A save that predates them had none,
/// since nothing could apply one before M3c.
/// </summary>
public sealed class SchemaV6ToV7 : SchemaMigration
{
    public override int From => 6;

    public override string Summary => "schema 6 -> 7: the player gains active status effects (none before M3c)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V6.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V8.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory.Select(i => new V8.Inventory { ItemId = i.ItemId, DefId = i.DefId, Count = i.Count }).ToArray(),
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = (old.Discoveries ?? Array.Empty<V6.Discovery>())
                    .Select(d => new DiscoveryDto { LocationId = d.LocationId, Method = d.Method, Tick = d.Tick }).ToArray(),
                Equipment = (old.Equipment ?? Array.Empty<V6.Equipment>())
                    .Select(e => new EquipmentDto { Slot = e.Slot, ItemId = e.ItemId }).ToArray(),
                Currency = old.Currency,
                Effects = Array.Empty<EffectDto>(),
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 7 to 8 (M3d): the entities section gains the records of spawners' creatures that left their baseline. A save
/// that predates them had none: before M3d no creature state was saved, so every creature is where its spawner puts it.
/// </summary>
public sealed class SchemaV7ToV8 : SchemaMigration
{
    public override int From => 7;

    public override string Summary => "schema 7 -> 8: the entities section gains creature records (none before M3d)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Entities) is { } entities)
        {
            var old = MessagePackSerializer.Deserialize<V6.EntitiesSection>(entities, options);
            document.Sections[SaveFormat.Entities] = MessagePackSerializer.Serialize(new V8.EntitiesSection
            {
                Records = old.Records.Select(e => new EntityDto
                {
                    InstanceId = e.InstanceId,
                    SlotKey = e.SlotKey,
                    GenerationSeq = e.GenerationSeq,
                    DefId = e.DefId,
                    DirtyMask = e.DirtyMask,
                    State = new EntityStateDto { Alive = e.State.Alive, XCm = e.State.XCm, ZCm = e.State.ZCm },
                }).ToArray(),
                Created = old.Created.Select(c => new V8.Created
                {
                    InstanceId = c.InstanceId, DefId = c.DefId, HostCell = c.HostCell, XCm = c.XCm, ZCm = c.ZCm, Count = c.Count,
                }).ToArray(),
                Baselines = old.Baselines.Select(b => new CellBaselineDto { CellKey = b.CellKey, BaselineHash = b.BaselineHash }).ToArray(),
                Containers = (old.Containers ?? Array.Empty<V6.Container>()).Select(c => new V8.Container
                {
                    Key = c.Key,
                    InstanceId = c.InstanceId,
                    HostCell = c.HostCell,
                    Items = c.Items.Select(i => new V8.ContainerItem { ItemId = i.ItemId, DefId = i.DefId, Count = i.Count }).ToArray(),
                }).ToArray(),
                Creatures = Array.Empty<V8.Creature>(),
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 8 to 9 (M3f): every item stack gains its quality - carried, in a changed container, or lying in the world. A
/// save that predates quality held only standard stacks, since nothing could make another before M3f.
/// </summary>
public sealed class SchemaV8ToV9 : SchemaMigration
{
    public override int From => 8;

    public override string Summary => "schema 8 -> 9: every item stack gains its quality (standard before M3f)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V8.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V9.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory.Select(i => new V18.Inventory { ItemId = i.ItemId, DefId = i.DefId, Count = i.Count, Quality = 0 }).ToArray(),
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = old.Discoveries,
                Equipment = old.Equipment,
                Currency = old.Currency,
                Effects = old.Effects,
            }, options);
        }
        if (document.Sections.GetValueOrDefault(SaveFormat.Entities) is { } entities)
        {
            var old = MessagePackSerializer.Deserialize<V8.EntitiesSection>(entities, options);
            document.Sections[SaveFormat.Entities] = MessagePackSerializer.Serialize(new V13.EntitiesSection
            {
                Records = old.Records,
                Created = old.Created.Select(c => new V18.Created
                {
                    InstanceId = c.InstanceId, DefId = c.DefId, HostCell = c.HostCell, XCm = c.XCm, ZCm = c.ZCm, Count = c.Count, Quality = 0,
                }).ToArray(),
                Baselines = old.Baselines,
                Containers = old.Containers?.Select(c => new V18.Container
                {
                    Key = c.Key,
                    InstanceId = c.InstanceId,
                    HostCell = c.HostCell,
                    Items = c.Items.Select(i => new V18.ContainerItem { ItemId = i.ItemId, DefId = i.DefId, Count = i.Count, Quality = 0 }).ToArray(),
                }).ToArray(),
                Creatures = old.Creatures,
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 9 to 10 (M4): the player gains what each NPC thinks of them and the lines of each conversation they have heard. A
/// save that predates the waystation's people has neither.
/// </summary>
public sealed class SchemaV9ToV10 : SchemaMigration
{
    public override int From => 9;

    public override string Summary => "schema 9 -> 10: the player gains relationships and conversation memory (none before M4)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V9.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V10.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory,
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = old.Discoveries,
                Equipment = old.Equipment,
                Currency = old.Currency,
                Effects = old.Effects,
                Relationships = Array.Empty<RelationshipDto>(),
                Conversations = Array.Empty<ConversationDto>(),
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 10 to 11 (M5): the player gains their quests - each started quest's state and objectives. A save that predates quests has
/// none.
/// </summary>
public sealed class SchemaV10ToV11 : SchemaMigration
{
    public override int From => 10;

    public override string Summary => "schema 10 -> 11: the player gains quests (none before M5)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V10.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V11.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory,
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = old.Discoveries,
                Equipment = old.Equipment,
                Currency = old.Currency,
                Effects = old.Effects,
                Relationships = old.Relationships,
                Conversations = old.Conversations,
                Quests = Array.Empty<QuestDto>(),
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>Schema 11 to 12 (M6): the player gains their companions. A save that predates companions has none.</summary>
public sealed class SchemaV11ToV12 : SchemaMigration
{
    public override int From => 11;

    public override string Summary => "schema 11 -> 12: the player gains companions (none before M6)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V11.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V12.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory,
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = old.Discoveries,
                Equipment = old.Equipment,
                Currency = old.Currency,
                Effects = old.Effects,
                Relationships = old.Relationships,
                Conversations = old.Conversations,
                Quests = old.Quests,
                Companions = Array.Empty<V20.Companion>(),
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>Schema 12 to 13 (the owner's M6 playtest): the player gains a posture. A save that predates crouching and jumping stands on the ground.</summary>
public sealed class SchemaV12ToV13 : SchemaMigration
{
    public override int From => 12;

    public override string Summary => "schema 12 -> 13: the player gains a posture (standing, on the ground, before the M6 playtest)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V12.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V14.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory,
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = old.Discoveries,
                Equipment = old.Equipment,
                Currency = old.Currency,
                Effects = old.Effects,
                Relationships = old.Relationships,
                Conversations = old.Conversations,
                Quests = old.Quests,
                Companions = old.Companions,
                Posture = new PostureDto { Stance = "standing", Airborne = false, AirMs = 0 },
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 13 to 14 (the Phase-1 technical audit, L-06): each creature record gains what its next ticks depend on - the charge it
/// must wait out, the stagger it is in and the one it is immune to - and the entities section the sounds the next tick hears. A save
/// that predates them kept none of it: its creatures resume free of all three, and nothing waits to be heard.
/// </summary>
public sealed class SchemaV13ToV14 : SchemaMigration
{
    public override int From => 13;

    public override string Summary => "schema 13 -> 14: creature records gain their charge cooldown, stagger and stagger immunity, and the entities " +
                                      "section the sounds the next tick hears (none before)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Entities) is { } entities)
        {
            var old = MessagePackSerializer.Deserialize<V13.EntitiesSection>(entities, options);
            document.Sections[SaveFormat.Entities] = MessagePackSerializer.Serialize(new V18.EntitiesSection
            {
                Records = old.Records,
                Created = old.Created,
                Baselines = old.Baselines,
                Containers = old.Containers,
                Creatures = old.Creatures?.Select(c => new CreatureDto
                {
                    Key = c.Key,
                    DefId = c.DefId,
                    InstanceId = c.InstanceId,
                    HostCell = c.HostCell,
                    Generation = c.Generation,
                    Condition = c.Condition,
                    XMm = c.XMm,
                    ZMm = c.ZMm,
                    FacingMdeg = c.FacingMdeg,
                    Health = c.Health,
                    DiedTick = c.DiedTick,
                    RespawnTick = c.RespawnTick,
                    Mind = c.Mind,
                    Awareness = c.Awareness,
                    Knows = c.Knows,
                    KnownXMm = c.KnownXMm,
                    KnownZMm = c.KnownZMm,
                    LastSeenTick = c.LastSeenTick,
                    SearchUntil = c.SearchUntil,
                    HasCalled = c.HasCalled,
                    Continuation = new CreatureContinuationDto(),
                }).ToArray(),
                Noises = Array.Empty<NoiseDto>(),
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 14 to 15 (Waystone travel, S-38): the player gains the Waystones they know, the one they chose to return to, and the stretches of
/// road they have walked. A save that predates them knew no stone, chose none - so it returns at the region's spawn, as it always did -
/// and has no road on its map.
/// </summary>
public sealed class SchemaV14ToV15 : SchemaMigration
{
    public override int From => 14;

    public override string Summary => "schema 14 -> 15: the player gains the Waystones they know (none), a return point (none: the region's spawn) " +
                                      "and the roads they have walked (none)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V14.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V15.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory,
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = old.Discoveries,
                Equipment = old.Equipment,
                Currency = old.Currency,
                Effects = old.Effects,
                Relationships = old.Relationships,
                Conversations = old.Conversations,
                Quests = old.Quests,
                Companions = old.Companions,
                Posture = old.Posture,
                Waystones = Array.Empty<WaystoneDto>(),
                ReturnPoint = null,
                RoadsWalked = Array.Empty<string>(),
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 15 to 16 (WP5, E-7): the player gains the deeds they have done - how many times they killed, crafted, harvested or delivered
/// each thing - which dialogue's deed condition reads. A save that predates them counted none: it starts the tally at nothing.
/// </summary>
public sealed class SchemaV15ToV16 : SchemaMigration
{
    public override int From => 15;

    public override string Summary => "schema 15 -> 16: the player gains the deeds they have done (none counted)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V15.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V16.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory,
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = old.Discoveries,
                Equipment = old.Equipment,
                Currency = old.Currency,
                Effects = old.Effects,
                Relationships = old.Relationships,
                Conversations = old.Conversations,
                Quests = old.Quests,
                Companions = old.Companions,
                Posture = old.Posture,
                Waystones = old.Waystones,
                ReturnPoint = old.ReturnPoint,
                RoadsWalked = old.RoadsWalked,
                Deeds = Array.Empty<DeedDto>(),
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 16 to 17 (WP2, the character creator): the player gains an identity besides their name - pronouns, people, body preset and Call.
/// A save that predates the creator keeps its name (the game's own were all "Wanderer") and is the character the game always made: "they",
/// a Veth of the default body, and no Call.
/// </summary>
public sealed class SchemaV16ToV17 : SchemaMigration
{
    public override int From => 16;

    public override string Summary => "schema 16 -> 17: the player gains an identity (they, people.veth, body.default) and no Call; the name is kept";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V16.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V18.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory,
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = old.Discoveries,
                Equipment = old.Equipment,
                Currency = old.Currency,
                Effects = old.Effects,
                Relationships = old.Relationships,
                Conversations = old.Conversations,
                Quests = old.Quests,
                Companions = old.Companions,
                Posture = old.Posture,
                Waystones = old.Waystones,
                ReturnPoint = old.ReturnPoint,
                RoadsWalked = old.RoadsWalked,
                Deeds = old.Deeds,
                // Literal, not CharacterIdentity.BeforeTheCreator: a step is a fixed function of the schema it reads.
                Identity = new IdentityDto { Pronouns = "they", People = "people.veth", BodyPreset = "body.default", Call = null },
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 17 to 18 (the persistent clock, WP11): the manifest gains <c>saved_utc</c>, the real moment the world was captured, which a login
/// measures the absence from - before it, <c>build_timestamp</c> was that moment to the second, so it is carried over - and <c>away_log</c>,
/// the tail of the last login's "while you were away" log: none, since no login before it caught anything up. No section changes.
/// </summary>
public sealed class SchemaV17ToV18 : SchemaMigration
{
    public override int From => 17;

    public override string Summary => "schema 17 -> 18: the manifest gains saved_utc (from build_timestamp) and an away log (empty)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        string? built = document.Manifest["build_timestamp"]?.GetValue<string>();
        if (built is null || !DateTimeOffset.TryParse(built, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var captured))
        {
            report.Blockers.Add($"schema-17 manifest has no readable build_timestamp ('{built}') to carry over as saved_utc");
            return;
        }
        document.Manifest["saved_utc"] = SaveManifest.FormatSavedUtc(captured);
        document.Manifest["away_log"] = new JsonArray();
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 18 to 19 (WP3a, itemization): a stack - carried, in a container, or lying in the world - may carry a notable instance's record
/// (name, provenance, familiarity). A save that predates records has none: a notable item from before it takes its record the next time
/// it moves, as one found then. Masterwork quality (+2) is new too; no older stack holds it.
/// </summary>
public sealed class SchemaV18ToV19 : SchemaMigration
{
    public override int From => 18;

    public override string Summary => "schema 18 -> 19: stacks gain a notable instance's record (none before)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V18.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V19.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory.Select(i => new V23.Inventory { ItemId = i.ItemId, DefId = i.DefId, Count = i.Count, Quality = i.Quality }).ToArray(),
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = old.Discoveries,
                Equipment = old.Equipment,
                Currency = old.Currency,
                Effects = old.Effects,
                Relationships = old.Relationships,
                Conversations = old.Conversations,
                Quests = old.Quests,
                Companions = old.Companions,
                Posture = old.Posture,
                Waystones = old.Waystones,
                ReturnPoint = old.ReturnPoint,
                RoadsWalked = old.RoadsWalked,
                Deeds = old.Deeds,
                Identity = old.Identity,
            }, options);
        }
        if (document.Sections.GetValueOrDefault(SaveFormat.Entities) is { } entities)
        {
            var old = MessagePackSerializer.Deserialize<V18.EntitiesSection>(entities, options);
            document.Sections[SaveFormat.Entities] = MessagePackSerializer.Serialize(new V21.EntitiesSection
            {
                Records = old.Records,
                Created = old.Created.Select(c => new V23.Created
                {
                    InstanceId = c.InstanceId, DefId = c.DefId, HostCell = c.HostCell, XCm = c.XCm, ZCm = c.ZCm, Count = c.Count, Quality = c.Quality,
                }).ToArray(),
                Baselines = old.Baselines,
                Containers = old.Containers?.Select(c => new V23.Container
                {
                    Key = c.Key,
                    InstanceId = c.InstanceId,
                    HostCell = c.HostCell,
                    Items = c.Items.Select(i => new V23.ContainerItem { ItemId = i.ItemId, DefId = i.DefId, Count = i.Count, Quality = i.Quality }).ToArray(),
                }).ToArray(),
                Creatures = old.Creatures,
                Noises = old.Noises,
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 19 to 20 (WP6): the player gains a faction ledger - the acts some faction reacts to, what each faction knows of them, and the
/// standing each holds. A save that predates factions told none of anything: it starts with the empty ledger, every faction neutral.
/// </summary>
public sealed class SchemaV19ToV20 : SchemaMigration
{
    public override int From => 19;

    public override string Summary => "schema 19 -> 20: the player gains a faction ledger (no act, every faction neutral)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V19.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V20.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory,
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = old.Discoveries,
                Equipment = old.Equipment,
                Currency = old.Currency,
                Effects = old.Effects,
                Relationships = old.Relationships,
                Conversations = old.Conversations,
                Quests = old.Quests,
                Companions = old.Companions,
                Posture = old.Posture,
                Waystones = old.Waystones,
                ReturnPoint = old.ReturnPoint,
                RoadsWalked = old.RoadsWalked,
                Deeds = old.Deeds,
                Identity = old.Identity,
                // Literal, not FactionLedger.Empty: a step is a fixed function of the schema it reads.
                Factions = new FactionLedgerDto { NextActSeq = 1 },
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 20 to 21 (WP15a, the party): each companion gains why they travel with the character (companion, hireling or temporary ally), a
/// stance, standing rules and - a hireling - a contract; the player gains the party's ledger of who is recovering from a grave wound and
/// who has fallen. Before it, everyone in the party was a companion, aggressive, keeping to the leash, and no one had been wounded away or
/// had died.
/// </summary>
public sealed class SchemaV20ToV21 : SchemaMigration
{
    public override int From => 20;

    public override string Summary => "schema 20 -> 21: companions gain membership, stance and tactics; the player gains the party ledger (none before)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V20.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V22.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory,
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = old.Discoveries,
                Equipment = old.Equipment,
                Currency = old.Currency,
                Effects = old.Effects,
                Relationships = old.Relationships,
                Conversations = old.Conversations,
                Quests = old.Quests,
                Companions = old.Companions?.Select(c => new CompanionDto
                {
                    NpcId = c.NpcId,
                    Order = c.Order,
                    Condition = c.Condition,
                    XMm = c.XMm,
                    ZMm = c.ZMm,
                    FacingMdeg = c.FacingMdeg,
                    Health = c.Health,
                    DownedTick = c.DownedTick,
                    StuckTicks = c.StuckTicks,
                    LastCombatTick = c.LastCombatTick,
                    TrailMm = c.TrailMm,
                    Membership = "companion",
                    Stance = "aggressive",
                    Tactics = new TacticsDto { KeepToLeash = true },
                }).ToArray(),
                Posture = old.Posture,
                Waystones = old.Waystones,
                ReturnPoint = old.ReturnPoint,
                RoadsWalked = old.RoadsWalked,
                Deeds = old.Deeds,
                Identity = old.Identity,
                Factions = old.Factions,
                Party = new PartyLedgerDto(),
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 21 to 22 (WP14, the living economy): the entities section gains the town markets, the goods on the road and the merchants'
/// purses. A save that predates them held no economy: its markets start at their targets and its merchants with full purses on load, as a
/// new world's do, and nothing is on the road.
/// </summary>
public sealed class SchemaV21ToV22 : SchemaMigration
{
    public override int From => 21;

    public override string Summary => "schema 21 -> 22: the entities section gains the town markets and the merchants' purses (none before)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Entities) is { } entities)
        {
            var old = MessagePackSerializer.Deserialize<V21.EntitiesSection>(entities, options);
            document.Sections[SaveFormat.Entities] = MessagePackSerializer.Serialize(new V23.EntitiesSection
            {
                Records = old.Records,
                Created = old.Created,
                Baselines = old.Baselines,
                Containers = old.Containers,
                Creatures = old.Creatures,
                Noises = old.Noises,
                Economy = null,
                Merchants = Array.Empty<MerchantDto>(),
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 22 to 23 (WP9, the quick bar): the player gains eight quick slots and the record of what they took off it on purpose. A save from
/// before them has an empty bar and took nothing off; when it loads, what the character carries to use and the formulas they know are
/// offered slots as the fixed keys had them - items on 1 to 3, formulas on 4 to 6 - by the quick bar's own rule, so nothing here reads
/// content.
/// </summary>
public sealed class SchemaV22ToV23 : SchemaMigration
{
    public override int From => 22;

    public override string Summary => "schema 22 -> 23: the player gains an empty eight-slot quick bar";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V22.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V23.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory,
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = old.Discoveries,
                Equipment = old.Equipment,
                Currency = old.Currency,
                Effects = old.Effects,
                Relationships = old.Relationships,
                Conversations = old.Conversations,
                Quests = old.Quests,
                Companions = old.Companions,
                Posture = old.Posture,
                Waystones = old.Waystones,
                ReturnPoint = old.ReturnPoint,
                RoadsWalked = old.RoadsWalked,
                Deeds = old.Deeds,
                Identity = old.Identity,
                Factions = old.Factions,
                Party = old.Party,
                // Literal, not QuickBar.Empty: a step is a fixed function of the schema it reads.
                QuickBar = new QuickBarDto { Slots = new string?[8], Cleared = Array.Empty<string>() },
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 23 to 24 (WP3b, durability): a stack - carried, in a container, or lying in the world - may carry its wear: the points taken and
/// the quality it was made at. A save that predates wear held nothing worn: every older stack is unworn, at the quality it has.
/// </summary>
public sealed class SchemaV23ToV24 : SchemaMigration
{
    public override int From => 23;

    public override string Summary => "schema 23 -> 24: stacks gain their wear (none before)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V23.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V24.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory.Select(i => new InventoryDto { ItemId = i.ItemId, DefId = i.DefId, Count = i.Count, Quality = i.Quality, Notable = i.Notable })
                    .ToArray(),
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = old.Discoveries,
                Equipment = old.Equipment,
                Currency = old.Currency,
                Effects = old.Effects,
                Relationships = old.Relationships,
                Conversations = old.Conversations,
                Quests = old.Quests,
                Companions = old.Companions,
                Posture = old.Posture,
                Waystones = old.Waystones,
                ReturnPoint = old.ReturnPoint,
                RoadsWalked = old.RoadsWalked,
                Deeds = old.Deeds,
                Identity = old.Identity,
                Factions = old.Factions,
                Party = old.Party,
                QuickBar = old.QuickBar,
            }, options);
        }
        if (document.Sections.GetValueOrDefault(SaveFormat.Entities) is { } entities)
        {
            var old = MessagePackSerializer.Deserialize<V23.EntitiesSection>(entities, options);
            document.Sections[SaveFormat.Entities] = MessagePackSerializer.Serialize(new V25.EntitiesSection
            {
                Records = old.Records,
                Created = old.Created.Select(c => new CreatedDto
                {
                    InstanceId = c.InstanceId, DefId = c.DefId, HostCell = c.HostCell, XCm = c.XCm, ZCm = c.ZCm, Count = c.Count, Quality = c.Quality, Notable = c.Notable,
                }).ToArray(),
                Baselines = old.Baselines,
                Containers = old.Containers?.Select(c => new ContainerDto
                {
                    Key = c.Key,
                    InstanceId = c.InstanceId,
                    HostCell = c.HostCell,
                    Items = c.Items.Select(i => new ContainerItemDto { ItemId = i.ItemId, DefId = i.DefId, Count = i.Count, Quality = i.Quality, Notable = i.Notable })
                        .ToArray(),
                }).ToArray(),
                Creatures = old.Creatures,
                Noises = old.Noises,
                Economy = old.Economy,
                Merchants = old.Merchants,
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 24 to 25 (WP15b): the player gains the party's sheets - each past or present party member's progression on the character's rules,
/// the gear they wear in the ten slots, given or loaned, and their own things. A save from before them has none: no party member had grown,
/// worn or kept anything.
/// </summary>
public sealed class SchemaV24ToV25 : SchemaMigration
{
    public override int From => 24;

    public override string Summary => "schema 24 -> 25: the player gains the party's sheets (none before)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V24.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new V26.Player
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory,
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = old.Discoveries,
                Equipment = old.Equipment,
                Currency = old.Currency,
                Effects = old.Effects,
                Relationships = old.Relationships,
                Conversations = old.Conversations,
                Quests = old.Quests,
                Companions = old.Companions,
                Posture = old.Posture,
                Waystones = old.Waystones,
                ReturnPoint = old.ReturnPoint,
                RoadsWalked = old.RoadsWalked,
                Deeds = old.Deeds,
                Identity = old.Identity,
                Factions = old.Factions,
                Party = old.Party,
                QuickBar = old.QuickBar,
                PartySheets = Array.Empty<PartySheetDto>(),
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 25 to 26 (WP12, NPC life): the entities section gains every NPC's life - needs, activity, condition, the body's pose and route -
/// the role slots awaiting an arrival, and when each NPC-NPC pair last met. A save from before them held no lives: its NPCs begin their
/// routines on load, each named NPC from where the region places them.
/// </summary>
public sealed class SchemaV25ToV26 : SchemaMigration
{
    public override int From => 25;

    public override string Summary => "schema 25 -> 26: the entities section gains the NPCs' lives (none before)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Entities) is { } entities)
        {
            var old = MessagePackSerializer.Deserialize<V25.EntitiesSection>(entities, options);
            document.Sections[SaveFormat.Entities] = MessagePackSerializer.Serialize(new V28.EntitiesSection
            {
                Records = old.Records,
                Created = old.Created,
                Baselines = old.Baselines,
                Containers = old.Containers,
                Creatures = old.Creatures,
                Noises = old.Noises,
                Economy = old.Economy,
                Merchants = old.Merchants,
                NpcLife = null,
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 26 to 27 (the soak's W4-SAVE-01): the player gains the attack they had under way. A save from before it held none: its load started
/// every attack over, at rest.
/// </summary>
public sealed class SchemaV26ToV27 : SchemaMigration
{
    public override int From => 26;

    public override string Summary => "schema 26 -> 27: the player gains the attack under way (none before)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V26.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new PlayerDto
            {
                InstanceId = old.InstanceId,
                Name = old.Name,
                XMm = old.XMm,
                YMm = old.YMm,
                ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed,
                Inventory = old.Inventory,
                Progression = old.Progression,
                FacingMdeg = old.FacingMdeg,
                Discoveries = old.Discoveries,
                Equipment = old.Equipment,
                Currency = old.Currency,
                Effects = old.Effects,
                Relationships = old.Relationships,
                Conversations = old.Conversations,
                Quests = old.Quests,
                Companions = old.Companions,
                Posture = old.Posture,
                Waystones = old.Waystones,
                ReturnPoint = old.ReturnPoint,
                RoadsWalked = old.RoadsWalked,
                Deeds = old.Deeds,
                Identity = old.Identity,
                Factions = old.Factions,
                Party = old.Party,
                QuickBar = old.QuickBar,
                PartySheets = old.PartySheets,
                Attack = null,
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 27 to 28 (WP12b, NPC life's ties): an NPC life may be <c>hired</c> - the party's, their routine suspended - or doing a trip's
/// <c>set_out</c> or <c>away</c>, and the party's records may name a role resident by their slot's key (<c>routine.</c>). No section's
/// shape changes and no older save held any of them, so the step only moves the version.
/// </summary>
public sealed class SchemaV27ToV28 : SchemaMigration
{
    public override int From => 27;

    public override string Summary => "schema 27 -> 28: lives may be hired or on a trip, and residents may be in the party (no change to older data)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}

/// <summary>
/// Schema 28 to 29 (WP13, NPC adventurers): the entities section gains every adventurer and the slots awaiting arrivals, and a role slot's
/// vacancy may name the retiring adventurer promised it. A save from before them held no adventurers: each settlement's slots fill on load,
/// and no vacancy was promised to anyone. With it, NPC lives lose their needs, who they were visiting and when each pair last met (owner,
/// 2026-10-17: fixed daily schedules); a meal, a visit, shopping or shelter under way becomes time about their day.
/// </summary>
public sealed class SchemaV28ToV29 : SchemaMigration
{
    public override int From => 28;

    public override string Summary => "schema 28 -> 29: the entities section gains the adventurers (none before), and NPC lives lose their needs, visits and pairs";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Entities) is { } entities)
        {
            var old = MessagePackSerializer.Deserialize<V28.EntitiesSection>(entities, options);
            document.Sections[SaveFormat.Entities] = MessagePackSerializer.Serialize(new EntitiesSectionDto
            {
                Records = old.Records,
                Created = old.Created,
                Baselines = old.Baselines,
                Containers = old.Containers,
                Creatures = old.Creatures,
                Noises = old.Noises,
                Economy = old.Economy,
                Merchants = old.Merchants,
                NpcLife = old.NpcLife is { } life
                    ? new NpcLifeDto
                    {
                        Hour = life.Hour,
                        // The needs, the visits and the pairs are gone (owner, 2026-10-17): a meal, a visit, a bit of shopping or shelter
                        // becomes time about their day, and the schedule takes them on at the next hour.
                        Npcs = life.Npcs.Select(n => new NpcLifeRecordDto
                        {
                            Key = n.Key, RoutineId = n.RoutineId, Cell = n.Cell, Name = n.Name, Generation = n.Generation, ArrivedDay = n.ArrivedDay,
                            Activity = n.Activity is "eat" or "socialise" or "shop" or "shelter" ? "idle" : n.Activity,
                            Condition = n.Condition, ConditionUntil = n.ConditionUntil, XMm = n.XMm, ZMm = n.ZMm, FacingMdeg = n.FacingMdeg,
                            Route = n.Route, StuckTicks = n.StuckTicks,
                        }).ToArray(),
                        Vacancies = life.Vacancies.Select(v => new NpcVacancyDto
                        {
                            SlotId = v.SlotId, LeftDay = v.LeftDay, ArrivalDay = v.ArrivalDay, NextGeneration = v.NextGeneration,
                        }).ToArray(),
                    }
                    : null,
                Adventurers = null,
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}
