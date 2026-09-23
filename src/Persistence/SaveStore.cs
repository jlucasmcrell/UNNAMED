// UNNAMED Persistence - the save store (PERSISTENCE.md §3, §7, §8)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UNNAMED.Domain;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;

namespace UNNAMED.Persistence;

/// <summary>Slot names (PERSISTENCE.md §3.2, §8.2): quick, manual_&lt;slug&gt;, auto_NN.</summary>
public static class SaveSlots
{
    public const string Quick = "quick";
    public const int AutosaveCount = 5;

    public static string Manual(string slug)
    {
        if (string.IsNullOrEmpty(slug) || !slug.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
            throw new ArgumentException($"A manual slot slug is lowercase snake_case, got '{slug}'", nameof(slug));
        return "manual_" + slug;
    }

    public static string Auto(int number) => number is >= 1 and <= AutosaveCount
        ? $"auto_{number:00}"
        : throw new ArgumentOutOfRangeException(nameof(number), number, $"Autosave slots are 1..{AutosaveCount}");

    public static bool IsValid(string? slot) =>
        slot == Quick
        || (slot is not null && slot.StartsWith("manual_", StringComparison.Ordinal) && slot.Length > 7
            && slot[7..].All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
        || (slot is not null && Enumerable.Range(1, AutosaveCount).Any(n => slot == $"auto_{n:00}"));
}

/// <summary>Autosave cadence (§8.2): every five minutes of PLAYTIME; real time does not count.</summary>
public static class AutosaveCadence
{
    public const double IntervalSeconds = 300;

    /// <summary>Major transitions also autosave; the caller triggers those directly.</summary>
    public static bool IsDue(double playtimeSeconds, double lastAutosavePlaytimeSeconds) =>
        playtimeSeconds - lastAutosavePlaytimeSeconds >= IntervalSeconds;
}

/// <summary>Builds a <see cref="SaveDocument"/> from live state: the save-time diff runs here (§5.2).</summary>
public static class SaveDocuments
{
    public static SaveDocument Capture(
        WorldDelta world, PlayerRecord player, string contentVersion, long worldTick, double playtimeSeconds,
        long worldTimeAdvancedTicks = 0) =>
        new(world.Tuple, world.Generator.WorldgenDigest, contentVersion, worldTick, worldTimeAdvancedTicks,
            playtimeSeconds, player, world.TakeSnapshot());
}

/// <summary>
/// One profile's saves: <c>saves/&lt;profile&gt;/&lt;slot&gt;/</c>. Implements the atomic write sequence
/// (§7.1), integrity and quarantine (§7.2), backup rotation (§7.3), the load sequence (§7.4) and the
/// boot-time recovery of interrupted commits. Capture a <see cref="SaveDocument"/> on the simulation
/// thread; <see cref="Save"/> may then run on any thread (§8.2). Use one store per profile root: its
/// operations are serialized, so an autosave in flight and a quicksave cannot interleave.
/// </summary>
public sealed class SaveStore
{
    private const string RotationFile = "rotation.json";
    private const int MoveAttempts = 10;

    private readonly Action<SaveStep>? _onStep;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();

    /// <param name="profileRoot">The profile directory. Per §3.2 it must sit outside cloud-sync roots.</param>
    /// <param name="onStep">Called at each §7.1 step boundary; tests use it to crash the process there.</param>
    /// <param name="clock">Source of <c>build_timestamp</c>.</param>
    public SaveStore(string profileRoot, Action<SaveStep>? onStep = null, Func<DateTimeOffset>? clock = null)
    {
        Root = Path.GetFullPath(profileRoot);
        Directory.CreateDirectory(Root);
        _onStep = onStep;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        CloudSyncRoot = SaveLocation.CloudSyncRootOf(Root);
    }

    public string Root { get; }

    /// <summary>Non-null when <see cref="Root"/> is inside a cloud-sync folder: the game must warn (§3.2).</summary>
    public string? CloudSyncRoot { get; }

    public string SlotPath(string slot) => Path.Combine(Root, slot);

    public string BackupPath(string slot, int generation) => Path.Combine(Root, $".bak-{slot}.{generation}");

    public IReadOnlyList<string> ListSlots() =>
        Directory.EnumerateDirectories(Root)
            .Select(Path.GetFileName)
            .Where(SaveSlots.IsValid)
            .Cast<string>()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    public ImmutableArray<int> AvailableBackups(string slot) =>
        new[] { 1, 2 }.Where(g => Directory.Exists(BackupPath(slot, g))).ToImmutableArray();

    /// <summary>
    /// The rolling autosave slot to write next (§8.2): an empty one if any, else the least recently
    /// written. A slot whose manifest cannot be read counts as oldest, so it is replaced first.
    /// </summary>
    public string NextAutosaveSlot()
    {
        var slots = Enumerable.Range(1, SaveSlots.AutosaveCount).Select(SaveSlots.Auto).ToList();
        return slots.FirstOrDefault(s => !Directory.Exists(SlotPath(s)))
               ?? slots.OrderBy(WrittenAt).ThenBy(s => s, StringComparer.Ordinal).First();
    }

    private DateTimeOffset WrittenAt(string slot)
    {
        try
        {
            var manifest = SectionCodec.DecodeManifest(File.ReadAllBytes(Path.Combine(SlotPath(slot), SaveFormat.Manifest)));
            return DateTimeOffset.Parse(manifest.BuildTimestamp, CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is IOException or JsonException or FormatException or NotSupportedException or UnauthorizedAccessException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    // ── §7.1 atomic write ───────────────────────────────────────────────────

    public void Save(string slot, SaveDocument document)
    {
        RequireValidSlot(slot);
        lock (_gate)
            Commit(slot, document);
    }

    private void Commit(string slot, SaveDocument document)
    {
        string token = EntityId.NewId(EntityKind.WorldEvent).Ulid;
        string staging = Path.Combine(Root, $".staging-{slot}-{token}");
        string trash = Path.Combine(Root, $".trash-{slot}-{token}");
        string slotPath = SlotPath(slot);

        // 1. Serialize every section into memory.
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [SaveFormat.Manifest] = SectionCodec.EncodeManifest(BuildManifest(document)),
            [SaveFormat.Player] = SectionCodec.EncodePlayer(document.Player),
            [SaveFormat.Cells] = SectionCodec.EncodeCells(document.Delta),
            [SaveFormat.Entities] = SectionCodec.EncodeEntities(document.Delta),
        };

        // 2-3. Write to a staging directory, flushing each file to disk.
        Directory.CreateDirectory(staging);
        foreach (var (name, bytes) in files)
            WriteDurably(Path.Combine(staging, name), bytes);
        Step(SaveStep.StagingWritten);

        // 4. The integrity root, hashed from what is ON DISK, so a write that corrupted data is caught.
        WriteDurably(Path.Combine(staging, SaveFormat.IntegrityRoot), BuildIntegrityRoot(staging));
        Step(SaveStep.IntegrityRootWritten);

        // 5. Commit. Never delete first: the previous save is moved aside, then the new one promoted.
        bool hadPrevious = Directory.Exists(slotPath);
        if (hadPrevious)
        {
            MoveWithRetry(slotPath, trash);
            Step(SaveStep.PreviousMovedToTrash);
        }
        MoveWithRetry(staging, slotPath);
        Step(SaveStep.StagingPromoted);

        // 6. Verify the committed slot by re-reading and re-hashing it.
        string? problem = VerifyIntegrity(slotPath);
        if (problem is not null)
        {
            MoveWithRetry(slotPath, Path.Combine(Root, $".failed-{slot}-{token}"));
            if (hadPrevious)
                MoveWithRetry(trash, slotPath);
            throw new SaveException($"Save to '{slot}' failed verification ({problem}); the previous save was restored");
        }
        Step(SaveStep.Verified);

        // 7. Only now: rotation, then the previous save leaves the slot.
        if (hadPrevious)
            RetirePrevious(slot, trash);
        Step(SaveStep.Rotated);
    }

    // ── §7.4 load ───────────────────────────────────────────────────────────

    public LoadResult Load(string slot, LoadContext context)
    {
        RequireValidSlot(slot);
        lock (_gate)
            return LoadFrom(SlotPath(slot), slot, context, isBackup: false);
    }

    /// <summary>Load a backup generation. Never automatic: the player chooses it (§7.2).</summary>
    public LoadResult LoadBackup(string slot, int generation, LoadContext context)
    {
        RequireValidSlot(slot);
        lock (_gate)
            return LoadFrom(BackupPath(slot, generation), slot, context, isBackup: true);
    }

    private LoadResult LoadFrom(string directory, string slot, LoadContext context, bool isBackup)
    {
        if (!Directory.Exists(directory))
            throw new SaveException($"There is no save at '{directory}'");
        var backups = AvailableBackups(slot);

        // a. Read and validate manifest.json; save_format must match exactly, else refuse.
        SaveManifest manifest;
        try
        {
            manifest = SectionCodec.DecodeManifest(File.ReadAllBytes(Path.Combine(directory, SaveFormat.Manifest)));
        }
        catch (Exception e) when (e is IOException or JsonException or NotSupportedException or UnauthorizedAccessException)
        {
            throw new SaveCorruptionException($"manifest.json in '{slot}' is unreadable; the slot cannot be loaded", backups, e);
        }
        if (manifest.SaveFormatVersion != SaveFormat.Current)
            throw new SaveFormatMismatchException(manifest.SaveFormatVersion, SaveFormat.Current);

        // b. Verify sections.sha256 per section. Quarantine what can be dropped; the rest is fatal.
        var expected = TryReadIntegrityRoot(directory);
        bool rederived = expected is null;   // §3.3: an unreadable integrity root is re-derived, and reported
        expected ??= SaveFormat.CheckedFiles.ToDictionary(f => f, f => HashFileOrNull(Path.Combine(directory, f)) ?? "", StringComparer.Ordinal);
        var quarantined = new List<string>();
        foreach (string file in SaveFormat.CheckedFiles)
        {
            if (HashFileOrNull(Path.Combine(directory, file)) == expected[file])
                continue;
            switch (file)
            {
                case SaveFormat.Manifest:
                    throw new SaveCorruptionException($"manifest.json in '{slot}' fails its integrity check", backups);
                case SaveFormat.Player:
                    throw new SaveCorruptionException(
                        $"player.msgpack in '{slot}' is corrupt. " + (backups.IsEmpty ? "No backup exists." : $"Backups available: {string.Join(", ", backups)}."),
                        backups);
                default:
                    quarantined.Add(SectionName(file));
                    break;
            }
        }

        // c. The baseline this save was made against must be this build's baseline.
        if (manifest.WorldgenDigest != context.Generator.WorldgenDigest || manifest.WorldgenVersion != context.Generator.WorldgenVersion)
            throw new BaselineMismatchException("worldgen_digest", manifest.WorldgenDigest, context.Generator.WorldgenDigest, migratable: false);
        if (manifest.SchemaVersion != context.SchemaVersion)
            throw new BaselineMismatchException("schema_version", manifest.SchemaVersion.ToString(CultureInfo.InvariantCulture),
                context.SchemaVersion.ToString(CultureInfo.InvariantCulture), migratable: true);
        if (manifest.ContentHash != context.ContentHash)
            throw new BaselineMismatchException("content_hash", manifest.ContentHash, context.ContentHash, migratable: true);

        // d-e. Alias/tombstone resolution and the migration chain belong to M2b. With every identity
        // check above passing, there is nothing for them to do.

        // f-i. Regenerate the baseline (lazily, per cell), apply the cell delta, merge entities on slot key.
        var cells = DecodeOrQuarantine(directory, SaveFormat.Cells, SectionCodec.DecodeCells, quarantined);
        var entities = DecodeOrQuarantine(directory, SaveFormat.Entities, SectionCodec.DecodeEntities, quarantined);
        var tuple = new BaselineTuple(BaselineTuple.ParseSeed(manifest.WorldSeed), manifest.WorldgenVersion, manifest.ContentHash);
        var world = WorldDelta.FromSnapshot(context.Generator, tuple, context.Registry, new DeltaSnapshot(cells, entities), out var rejected);

        // k. Player state.
        PlayerRecord player;
        try
        {
            player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(directory, SaveFormat.Player)));
        }
        catch (Exception e) when (e is IOException or MessagePack.MessagePackSerializationException or FormatException or ArgumentException)
        {
            throw new SaveCorruptionException($"player.msgpack in '{slot}' could not be decoded", backups, e);
        }

        // l-m. Invariant failures were rejected in FromSnapshot and are reported, not hidden. One result.
        var quarantinedSections = quarantined.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToImmutableArray();
        var result = new LoadResult(
            manifest with { Flags = new ManifestFlags { QuarantinedSections = quarantinedSections } },
            player, world, quarantinedSections, rejected, rederived);

        // A complete, clean load proves this save good: it may now become a backup (§7.3).
        if (!isBackup && result.IsComplete)
            MarkLoadVerified(slot, IntegrityRootDigest(directory));
        return result;
    }

    // ── §7.3 backup rotation ────────────────────────────────────────────────

    /// <summary>
    /// "Two backup generations, retired on a verified LOAD, not a verified write." The displaced save
    /// enters the backup chain only if it was proven good by a clean load; an unproven save is dropped
    /// and never pushes a proven backup out. So a run of saves after a silent problem cannot discard
    /// the last save known to be good.
    /// </summary>
    private void RetirePrevious(string slot, string trash)
    {
        var rotation = ReadRotation();
        if (rotation.TryGetValue(slot, out string? verified) && verified == IntegrityRootDigest(trash))
        {
            string first = BackupPath(slot, 1), second = BackupPath(slot, 2);
            DeleteWithRetry(second);
            if (Directory.Exists(first))
                MoveWithRetry(first, second);
            MoveWithRetry(trash, first);
        }
        else
        {
            DeleteWithRetry(trash);
        }
    }

    public void Delete(string slot)
    {
        RequireValidSlot(slot);
        lock (_gate)
        {
            foreach (string path in new[] { SlotPath(slot), BackupPath(slot, 1), BackupPath(slot, 2) }.Concat(Leftovers(slot)))
                DeleteWithRetry(path);
            var rotation = ReadRotation();
            if (rotation.Remove(slot))
                WriteRotation(rotation);
        }
    }

    // ── boot sweep ──────────────────────────────────────────────────────────

    /// <summary>
    /// Recover saves interrupted mid-commit (§7.1: "the boot sweep must be able to recover that
    /// state"). Afterwards every slot holds either its previous complete save or its new complete save,
    /// never a partial one. Returns what was done, one line per action.
    /// </summary>
    public ImmutableArray<string> RecoverInterruptedCommits()
    {
        lock (_gate)
            return Recover();
    }

    private ImmutableArray<string> Recover()
    {
        var actions = ImmutableArray.CreateBuilder<string>();
        var slots = Directory.EnumerateDirectories(Root, ".staging-*").Concat(Directory.EnumerateDirectories(Root, ".trash-*"))
            .Select(p => ParseLeftover(Path.GetFileName(p)!).Slot)
            .Where(s => s is not null)
            .Distinct()
            .Cast<string>()
            .ToList();

        foreach (string slot in slots)
        {
            string slotPath = SlotPath(slot);
            var stagings = LeftoversOf(slot, ".staging-");
            var trashes = LeftoversOf(slot, ".trash-");

            if (!Directory.Exists(slotPath))
            {
                // Interrupted between moving the old save aside and promoting the new one.
                string? complete = stagings.LastOrDefault(s => VerifyIntegrity(s) is null);
                if (complete is not null)
                {
                    MoveWithRetry(complete, slotPath);
                    stagings.Remove(complete);
                    actions.Add($"{slot}: promoted the complete new save");
                    foreach (string t in trashes)
                        RetirePrevious(slot, t);
                    trashes.Clear();
                }
                else if (trashes.Count > 0)
                {
                    MoveWithRetry(trashes[^1], slotPath);
                    trashes.RemoveAt(trashes.Count - 1);
                    actions.Add($"{slot}: restored the previous save");
                }
            }
            else if (trashes.Count > 0)
            {
                // The new save was promoted; verification or rotation had not finished.
                if (VerifyIntegrity(slotPath) is null)
                {
                    foreach (string t in trashes)
                        RetirePrevious(slot, t);
                    actions.Add($"{slot}: completed an interrupted commit");
                }
                else
                {
                    MoveWithRetry(slotPath, Path.Combine(Root, $".failed-{slot}-{EntityId.NewId(EntityKind.WorldEvent).Ulid}"));
                    MoveWithRetry(trashes[^1], slotPath);
                    actions.Add($"{slot}: rolled back an unverifiable commit");
                }
                trashes.Clear();
            }

            foreach (string leftover in stagings.Concat(trashes))
            {
                DeleteWithRetry(leftover);
                actions.Add($"{slot}: discarded {Path.GetFileName(leftover)}");
            }
        }
        return actions.ToImmutable();
    }

    // ── integrity helpers ───────────────────────────────────────────────────

    /// <summary>
    /// <c>sections.sha256</c>: one <c>&lt;sha256&gt;  &lt;file&gt;</c> line per checked file, in canonical
    /// order (the <c>sha256sum</c> layout, so it can be checked with standard tools).
    /// </summary>
    private static byte[] BuildIntegrityRoot(string directory)
    {
        var text = new StringBuilder();
        foreach (string file in SaveFormat.CheckedFiles)
            text.Append(HashFile(Path.Combine(directory, file))).Append("  ").Append(file).Append('\n');
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static Dictionary<string, string>? TryReadIntegrityRoot(string directory)
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
    private static string? VerifyIntegrity(string directory)
    {
        var expected = TryReadIntegrityRoot(directory);
        if (expected is null)
            return "integrity root missing or malformed";
        foreach (string file in SaveFormat.CheckedFiles)
        {
            if (HashFileOrNull(Path.Combine(directory, file)) != expected[file])
                return $"{file} does not match its recorded hash";
        }
        return null;
    }

    private static string IntegrityRootDigest(string directory) =>
        "sha256:" + (HashFileOrNull(Path.Combine(directory, SaveFormat.IntegrityRoot)) ?? "missing");

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string? HashFileOrNull(string path) => File.Exists(path) ? HashFile(path) : null;

    private static ImmutableArray<T> DecodeOrQuarantine<T>(
        string directory, string file, Func<byte[], ImmutableArray<T>> decode, List<string> quarantined)
    {
        if (quarantined.Contains(SectionName(file)))
            return ImmutableArray<T>.Empty;
        try
        {
            return decode(File.ReadAllBytes(Path.Combine(directory, file)));
        }
        catch (Exception e) when (e is IOException or MessagePack.MessagePackSerializationException or FormatException)
        {
            // Hash-valid but undecodable (for example written by a buggy build): drop it, and say so.
            quarantined.Add(SectionName(file));
            return ImmutableArray<T>.Empty;
        }
    }

    private static string SectionName(string file) => Path.GetFileNameWithoutExtension(file);

    // ── IO helpers ──────────────────────────────────────────────────────────

    private static void WriteDurably(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// A rename that retries. On Windows the realistic commit failures are not power loss but sharing
    /// violations - antivirus and the search indexer open freshly written files - so an IOException
    /// retries with bounded backoff (§7.1). An access denial is a policy decision and is surfaced with
    /// the path instead.
    /// </summary>
    private static void MoveWithRetry(string from, string to)
    {
        if (!Directory.Exists(from))
            throw new DirectoryNotFoundException($"Cannot move '{from}': it does not exist");
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (IOException) when (attempt < MoveAttempts && !Directory.Exists(to))
            {
                Thread.Sleep(Math.Min(10 << attempt, 2000));
            }
            catch (UnauthorizedAccessException e)
            {
                throw new SaveException($"Access to '{from}' or '{to}' was denied: {e.Message}", e);
            }
        }
    }

    // The same sharing violations hit deletes; a verified save must not be reported failed over them.
    private static void DeleteWithRetry(string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < MoveAttempts)
            {
                Thread.Sleep(Math.Min(10 << attempt, 2000));
            }
        }
    }

    private SaveManifest BuildManifest(SaveDocument document) => new()
    {
        SaveFormatVersion = SaveFormat.Current,
        SchemaVersion = SaveFormat.SchemaVersion,
        ContentVersion = document.ContentVersion,
        ContentHash = document.Baseline.ContentHash,
        WorldgenVersion = document.Baseline.WorldgenVersion,
        WorldgenDigest = document.WorldgenDigest,
        WorldSeed = BaselineTuple.FormatSeed(document.Baseline.WorldSeed),
        WorldTick = document.WorldTick,
        WorldTimeAdvancedTicks = document.WorldTimeAdvancedTicks,
        CommandLogSha256 = null,
        BuildTimestamp = _clock().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        PlaytimeSeconds = document.PlaytimeSeconds,
        Flags = new ManifestFlags { QuarantinedSections = ImmutableArray<string>.Empty },
    };

    private void Step(SaveStep step) => _onStep?.Invoke(step);

    private static void RequireValidSlot(string slot)
    {
        if (!SaveSlots.IsValid(slot))
            throw new ArgumentException($"'{slot}' is not a valid slot name (quick, manual_<slug>, auto_01..auto_05)", nameof(slot));
    }

    // Leftover names: .staging-<slot>-<ULID> and .trash-<slot>-<ULID>
    private static (string? Slot, string? Token) ParseLeftover(string name)
    {
        foreach (string prefix in new[] { ".staging-", ".trash-" })
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length + 1 + EntityId.UlidLength
                && name[^(EntityId.UlidLength + 1)] == '-')
            {
                string slot = name[prefix.Length..^(EntityId.UlidLength + 1)];
                return SaveSlots.IsValid(slot) ? (slot, name[^EntityId.UlidLength..]) : (null, null);
            }
        }
        return (null, null);
    }

    private List<string> LeftoversOf(string slot, string prefix) =>
        Directory.EnumerateDirectories(Root, prefix + "*")
            .Where(p => ParseLeftover(Path.GetFileName(p)!).Slot == slot)
            .OrderBy(p => ParseLeftover(Path.GetFileName(p)!).Token, StringComparer.Ordinal)   // ULIDs sort by time
            .ToList();

    private IEnumerable<string> Leftovers(string slot) => LeftoversOf(slot, ".staging-").Concat(LeftoversOf(slot, ".trash-"));

    // ── rotation metadata ───────────────────────────────────────────────────

    private Dictionary<string, string> ReadRotation()
    {
        string path = Path.Combine(Root, RotationFile);
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(path))
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);   // losing it only forgets proof, never data
        }
    }

    private void WriteRotation(Dictionary<string, string> rotation)
    {
        string path = Path.Combine(Root, RotationFile);
        string temp = path + ".tmp-" + EntityId.NewId(EntityKind.WorldEvent).Ulid;
        WriteDurably(temp, JsonSerializer.SerializeToUtf8Bytes(
            new SortedDictionary<string, string>(rotation, StringComparer.Ordinal), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }

    private void MarkLoadVerified(string slot, string integrityRootDigest)
    {
        var rotation = ReadRotation();
        rotation[slot] = integrityRootDigest;
        WriteRotation(rotation);
    }
}
