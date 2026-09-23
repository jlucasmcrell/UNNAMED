// UNNAMED Persistence - the load sequence (PERSISTENCE.md §7.4, M2b §16)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MessagePack;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence;

/// <summary>
/// The load sequence, as one pipeline shared by a load, a dry run and a migration. It reads files and
/// never writes them: a migrated save reaches disk only through the §7.1 write sequence.
/// Order (M2b §16): container, integrity, schema chain, definition IDs, baseline proof, invariants.
/// </summary>
internal static class SaveLoader
{
    public static MigrationReport NewReport(string saveName, LoadContext context) => new(saveName)
    {
        CurrentSchema = context.SchemaVersion,
        CurrentContentVersion = context.Content.Version,
        CurrentContentHash = context.Content.Hash,
        CurrentWorldgenVersion = context.Generator.WorldgenVersion,
        CurrentFingerprint = context.Generator.Fingerprint,
        CurrentRngContract = context.Generator.RngContractVersion,
    };

    /// <summary>
    /// Load a save directory. Returns a null result when the report has blockers; throws for a save
    /// that is not readable as a save at all (§7.2). The report is filled in either way.
    /// </summary>
    public static LoadResult? Run(string directory, string saveName, LoadContext context, ImmutableArray<int> backups, MigrationReport report)
    {
        if (!Directory.Exists(directory))
            throw new SaveException($"There is no save at '{directory}'");
        var generator = context.Generator;

        // a. manifest.json: the container version must match exactly, else refuse.
        JsonObject manifestJson;
        try
        {
            manifestJson = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, SaveFormat.Manifest))) as JsonObject
                ?? throw new JsonException("manifest.json is not a JSON object");
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new SaveCorruptionException($"manifest.json in '{saveName}' is unreadable; the slot cannot be loaded", backups, e);
        }
        int saveFormat = RequiredInt(manifestJson, "save_format", saveName, backups);
        report.SourceSaveFormat = saveFormat;
        if (saveFormat != SaveFormat.Current)
            throw new SaveFormatMismatchException(saveFormat, SaveFormat.Current);
        int schema = RequiredInt(manifestJson, "schema_version", saveName, backups);
        report.SourceSchema = schema;
        report.SourceContentVersion = OptionalString(manifestJson, "content_version");
        report.SourceContentHash = OptionalString(manifestJson, "content_hash");
        report.SourceWorldgenVersion = OptionalInt(manifestJson, "worldgen_version");
        report.SourceFingerprint = OptionalString(manifestJson, "worldgen_fingerprint");
        report.SourceRngContract = OptionalInt(manifestJson, "rng_contract_version") ?? (schema == 1 ? RngContract.V1 : null);

        // b. sections.sha256, per section: quarantine what can be dropped; the rest is fatal.
        var expected = SaveIntegrity.TryReadRoot(directory);
        bool rederived = expected is null;   // §3.3: an unreadable integrity root is re-derived, and reported
        expected ??= SaveFormat.CheckedFiles.ToDictionary(f => f, f => SaveIntegrity.HashFileOrNull(Path.Combine(directory, f)) ?? "", StringComparer.Ordinal);
        var quarantined = new List<string>();
        foreach (string file in SaveFormat.CheckedFiles)
        {
            if (SaveIntegrity.HashFileOrNull(Path.Combine(directory, file)) == expected[file])
                continue;
            switch (file)
            {
                case SaveFormat.Manifest:
                    throw new SaveCorruptionException($"manifest.json in '{saveName}' fails its integrity check", backups);
                case SaveFormat.Player:
                    throw new SaveCorruptionException(
                        $"player.msgpack in '{saveName}' is corrupt. " + (backups.IsEmpty ? "No backup exists." : $"Backups available: {string.Join(", ", backups)}."),
                        backups);
                default:
                    quarantined.Add(SectionName(file));
                    break;
            }
        }
        if (rederived)
            report.Warnings.Add("sections.sha256 was unreadable and was re-derived from the files");
        foreach (string section in quarantined)
            report.Warnings.Add($"section '{section}' fails its integrity check and is loaded without (§7.2)");

        var sections = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (string file in new[] { SaveFormat.Player, SaveFormat.Cells, SaveFormat.Entities })
            sections[file] = quarantined.Contains(SectionName(file)) ? null : File.ReadAllBytes(Path.Combine(directory, file));

        // c. The schema migration chain, one version per step, in memory (§6.2).
        if (schema > context.SchemaVersion)
        {
            report.Blockers.Add($"schema {schema} is newer than this build, which reads up to schema {context.SchemaVersion}");
            return null;
        }
        if (schema < context.SchemaVersion)
        {
            if (!SchemaMigrations.TryChain(context.Migrations, schema, context.SchemaVersion, out var chain))
            {
                report.Blockers.Add($"no registered migration chain from schema {schema} to schema {context.SchemaVersion}");
                return null;
            }
            var document = new MigrationDocument(schema, manifestJson, sections);
            var environment = new MigrationEnvironment(generator);
            foreach (var step in chain)
            {
                try
                {
                    step.Apply(document, environment, report);
                }
                catch (Exception e) when (e is InvalidOperationException or FormatException or ArgumentException or MessagePackSerializationException)
                {
                    report.Blockers.Add($"migration schema {step.From} -> {step.To} failed: {e.Message}");
                }
                if (report.Blockers.Count > 0)
                    return null;
                document.Schema = step.To;
            }
            sections = document.Sections;
        }

        // d. Decode the current shape.
        SaveManifest manifest;
        try
        {
            manifest = manifestJson.Deserialize<SaveManifest>(SectionCodec.ManifestOptions)
                ?? throw new JsonException("manifest.json is empty");
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException)
        {
            throw new SaveCorruptionException($"manifest.json in '{saveName}' does not have the schema-{context.SchemaVersion} shape: {e.Message}", backups, e);
        }
        var cells = DecodeOrQuarantine(sections[SaveFormat.Cells], SaveFormat.Cells, SectionCodec.DecodeCells, quarantined, report);
        var (entities, created) = DecodeOrQuarantine(sections[SaveFormat.Entities], SaveFormat.Entities, SectionCodec.DecodeEntitySection,
            quarantined, report, (ImmutableArray<EntityDeltaRecord>.Empty, ImmutableArray<CreatedEntityRecord>.Empty));
        var delta = new DeltaSnapshot(cells, entities) { Created = created };
        PlayerRecord player;
        try
        {
            player = SectionCodec.DecodePlayer(sections[SaveFormat.Player]!);
        }
        catch (Exception e) when (e is MessagePackSerializationException or FormatException or ArgumentException)
        {
            throw new SaveCorruptionException($"player.msgpack in '{saveName}' could not be decoded", backups, e);
        }

        // e. Definition IDs (§6.3): a changed content pack resolves every stored ID, or the load stops.
        if (manifest.ContentHash != context.Content.Hash)
            (player, delta) = ResolveDefinitions(player, delta, context.Content, report);
        if (report.Blockers.Count > 0)
            return null;

        // f. The generator contract. A different epoch needs a registered worldgen migration.
        if (manifest.WorldgenVersion != generator.WorldgenVersion || manifest.RngContractVersion != generator.RngContractVersion)
        {
            report.Blockers.Add(
                $"the save is worldgen {manifest.WorldgenVersion} (RNG contract {manifest.RngContractVersion}); this build is worldgen " +
                $"{generator.WorldgenVersion} (RNG contract {generator.RngContractVersion}), and no migration is registered between them");
            return null;
        }
        if (manifest.WorldgenFingerprint != generator.Fingerprint)
            report.Warnings.Add("the world generator changed since this save (worldgen_fingerprint differs); every changed cell is checked against its own baseline hash");

        // g. Baseline proof: every changed cell's delta targets exactly the baseline generated now.
        ulong seed = WorldSeed.Parse(manifest.WorldSeed);
        delta = ProveBaselines(delta, seed, manifest.WorldgenFingerprint, context, report);
        if (report.Blockers.Count > 0)
            return null;

        // h-i. Regenerate, apply the cell delta, merge entities on slot key. Invalid records are reported.
        var world = WorldDelta.FromSnapshot(generator, seed, context.Registry, delta, out var rejected);
        foreach (var r in rejected)
            report.Warnings.Add($"invalid {r.Section} record '{r.Key}' dropped: {r.Reason}");

        var quarantinedSections = quarantined.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToImmutableArray();
        return new LoadResult(
            manifest with { Flags = new ManifestFlags { QuarantinedSections = quarantinedSections } },
            player, world, quarantinedSections, rejected, rederived, report);
    }

    /// <summary>A dry run: the whole load, into a throwaway registry, reporting instead of throwing.</summary>
    public static MigrationReport Plan(string directory, string saveName, LoadContext context)
    {
        var report = NewReport(saveName, context);
        try
        {
            Run(directory, saveName, context with { Registry = new Registry() }, ImmutableArray<int>.Empty, report);
        }
        catch (SaveException e)
        {
            report.Blockers.Add(e.Message);
        }
        return report;
    }

    private static (PlayerRecord, DeltaSnapshot) ResolveDefinitions(
        PlayerRecord player, DeltaSnapshot delta, ContentIdentity content, MigrationReport report)
    {
        string? Resolve(string id, string referencedBy)
        {
            var resolution = content.Resolve(id);
            switch (resolution.Outcome)
            {
                case IdOutcome.Current:
                    return id;
                case IdOutcome.Renamed:
                    report.CountAlias(id, resolution.CurrentId!);
                    return resolution.CurrentId;
                case IdOutcome.Replaced:
                    report.CountReplacement(id, resolution.CurrentId!);
                    return resolution.CurrentId;
                case IdOutcome.Discarded:
                    report.CountDiscard(id);
                    report.Loss.Add($"{referencedBy}: '{id}' was removed with no replacement; dropped");
                    return null;
                default:
                    report.Blockers.Add(
                        $"unresolved definition ID '{id}' ({referencedBy}): it is not defined and content/_aliases.yaml " +
                        $"does not map it ({resolution.Path})");
                    return null;
            }
        }

        var inventory = new List<InventoryEntry>();
        foreach (var entry in player.Inventory)
        {
            string? id = Resolve(entry.DefId, $"player inventory item {entry.ItemId}");
            if (id is not null)
                inventory.Add(entry with { DefId = id });
        }

        var cells = delta.Cells.Select(cell =>
        {
            var flags = new List<KeyValuePair<string, long>>();
            foreach (var (flag, value) in cell.Flags)
            {
                string? id = Resolve(flag, $"world flag in cell {cell.CellKey}");
                if (id is not null)
                    flags.Add(KeyValuePair.Create(id, value));
            }
            return cell with { Flags = flags.OrderBy(f => f.Key, StringComparer.Ordinal).ToImmutableArray() };
        }).ToImmutableArray();

        var entities = ImmutableArray.CreateBuilder<EntityDeltaRecord>();
        foreach (var record in delta.Entities)
        {
            string? id = Resolve(record.DefId, $"entity {record.InstanceId} in slot {record.SlotKey}");
            if (id is not null)
                entities.Add(record with { DefId = id });
        }

        var created = ImmutableArray.CreateBuilder<CreatedEntityRecord>();
        foreach (var record in delta.Created)
        {
            string? id = Resolve(record.DefId, $"created instance {record.InstanceId} in cell {record.HostCell}");
            if (id is not null)
                created.Add(record with { DefId = id });
        }

        // Skills, known techniques, first-time records and kill records name definitions too (schema 4).
        var progression = player.Progression.RewriteDefinitionIds((id, role) => Resolve(id, $"player {role}"));

        return (player.WithInventory(inventory).WithProgression(progression),
            new DeltaSnapshot(cells, entities.ToImmutable()) { Created = created.ToImmutable() });
    }

    private static DeltaSnapshot ProveBaselines(
        DeltaSnapshot delta, ulong seed, string saveFingerprint, LoadContext context, MigrationReport report)
    {
        var cache = new Dictionary<CellKey, CellBaseline>();
        CellBaseline Baseline(CellKey cell) =>
            cache.TryGetValue(cell, out var b) ? b : cache[cell] = context.Generator.Generate(seed, cell);

        var proven = new SortedSet<string>(StringComparer.Ordinal);
        var mismatched = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Check(string cellKey, string? saved)
        {
            if (!CellKey.TryParse(cellKey, out var cell))
                return;   // an unparseable key is rejected, with its reason, when the world is built
            string now = Baseline(cell).Digest;
            if (saved == now)
                proven.Add(cellKey);
            else
                mismatched.TryAdd(cellKey, $"saved baseline_hash {saved ?? "(none)"}, generated now {now}");
        }
        foreach (var record in delta.Cells)
            Check(record.CellKey, record.BaselineHash);
        foreach (var record in delta.Entities)
            Check(SectionCodec.HostCell(record.SlotKey), record.BaselineHash);
        foreach (var record in delta.Created)
            Check(record.HostCell, record.BaselineHash);
        proven.ExceptWith(mismatched.Keys);
        report.CellsMatched = proven.Count;
        if (mismatched.Count == 0)
            return delta;

        var transition = context.Transitions.FirstOrDefault(t =>
            t.FromFingerprint == saveFingerprint && t.ToFingerprint == context.Generator.Fingerprint);
        if (transition is null)
        {
            foreach (var (cell, detail) in mismatched)
                report.CellsMismatched.Add($"{cell}: {detail}");
            report.Blockers.Add(
                $"{mismatched.Count} changed cell(s) were saved against a baseline this build no longer generates, and no transition " +
                $"is registered from worldgen fingerprint {saveFingerprint} to {context.Generator.Fingerprint}. " +
                "Their deltas are not applied to a different baseline.");
            return delta;
        }
        return SemanticRebase.Apply(transition, mismatched.Keys.ToHashSet(StringComparer.Ordinal), delta, Baseline, report);
    }

    private static ImmutableArray<T> DecodeOrQuarantine<T>(
        byte[]? bytes, string file, Func<byte[], ImmutableArray<T>> decode, List<string> quarantined, MigrationReport report) =>
        DecodeOrQuarantine(bytes, file, decode, quarantined, report, ImmutableArray<T>.Empty);

    private static T DecodeOrQuarantine<T>(
        byte[]? bytes, string file, Func<byte[], T> decode, List<string> quarantined, MigrationReport report, T empty)
    {
        if (bytes is null)
            return empty;
        try
        {
            return decode(bytes);
        }
        catch (Exception e) when (e is MessagePackSerializationException or FormatException or ArgumentException)
        {
            // Hash-valid but undecodable (for example written by a buggy build): drop it, and say so.
            quarantined.Add(SectionName(file));
            report.Warnings.Add($"section '{SectionName(file)}' could not be decoded and is loaded without: {e.Message}");
            return empty;
        }
    }

    internal static string SectionName(string file) => Path.GetFileNameWithoutExtension(file);

    private static int RequiredInt(JsonObject manifest, string field, string saveName, ImmutableArray<int> backups) =>
        OptionalInt(manifest, field)
        ?? throw new SaveCorruptionException($"manifest.json in '{saveName}' has no valid '{field}'", backups);

    private static int? OptionalInt(JsonObject manifest, string field) =>
        manifest[field] is JsonValue value && value.TryGetValue(out int number) ? number : null;

    private static string? OptionalString(JsonObject manifest, string field) =>
        manifest[field] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}

/// <summary><c>sections.sha256</c>: build, read and verify the integrity root (§3.3, §7.1).</summary>
internal static class SaveIntegrity
{
    /// <summary>One <c>&lt;sha256&gt;  &lt;file&gt;</c> line per checked file, in canonical order (the sha256sum layout).</summary>
    public static byte[] BuildRoot(string directory)
    {
        var text = new StringBuilder();
        foreach (string file in SaveFormat.CheckedFiles)
            text.Append(HashFile(Path.Combine(directory, file))).Append("  ").Append(file).Append('\n');
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    public static Dictionary<string, string>? TryReadRoot(string directory)
    {
        try
        {
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(Path.Combine(directory, SaveFormat.IntegrityRoot)))
            {
                int gap = line.IndexOf("  ", StringComparison.Ordinal);
                if (gap != 64 || !line[..64].All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'))
                    return null;
                entries[line[(gap + 2)..]] = line[..64];
            }
            return SaveFormat.CheckedFiles.All(entries.ContainsKey) ? entries : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Null when every checked file matches the integrity root; otherwise what failed.</summary>
    public static string? Verify(string directory)
    {
        var expected = TryReadRoot(directory);
        if (expected is null)
            return "integrity root missing or malformed";
        foreach (string file in SaveFormat.CheckedFiles)
        {
            if (HashFileOrNull(Path.Combine(directory, file)) != expected[file])
                return $"{file} does not match its recorded hash";
        }
        return null;
    }

    public static string RootDigest(string directory) =>
        "sha256:" + (HashFileOrNull(Path.Combine(directory, SaveFormat.IntegrityRoot)) ?? "missing");

    public static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    public static string? HashFileOrNull(string path) => File.Exists(path) ? HashFile(path) : null;
}
