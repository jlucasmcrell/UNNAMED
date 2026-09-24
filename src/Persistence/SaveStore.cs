// UNNAMED Persistence - the save store (PERSISTENCE.md §3, §7, §8)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        WorldDelta world, PlayerRecord player, ContentIdentity content, long worldTick, double playtimeSeconds,
        long worldTimeAdvancedTicks = 0) =>
        new(world.WorldSeed, WorldgenIdentity.Of(world.Generator), content.Version, content.Hash, worldTick,
            worldTimeAdvancedTicks, playtimeSeconds, player, world.TakeSnapshot());
}

/// <summary>
/// One profile's saves: <c>saves/&lt;profile&gt;/&lt;slot&gt;/</c>. Implements the atomic write sequence
/// (§7.1), integrity and quarantine (§7.2), backup rotation (§7.3), the load sequence (§7.4), the
/// boot-time recovery of interrupted commits, and migration (§6.2) through that same write sequence. Capture a <see cref="SaveDocument"/> on the simulation
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
    /// <c>.prev-&lt;slot&gt;</c>: the save a quick or manual save displaced before any load had proven it (§7.3). It is kept one deep
    /// rather than deleted, so a new save never destroys the last one outright (the Phase-1 technical audit, B-01). It stays outside the
    /// backup chain, so it can never push a proven backup out, and it loads only when the player chooses it.
    /// </summary>
    public string PreviousPath(string slot) => Path.Combine(Root, $".prev-{slot}");

    public string CopyPath(string slot, SaveCopy copy) => copy switch
    {
        SaveCopy.Current => SlotPath(slot),
        SaveCopy.Backup1 => BackupPath(slot, 1),
        SaveCopy.Backup2 => BackupPath(slot, 2),
        _ => PreviousPath(slot),
    };

    /// <summary>Every slot's save, newest first, each read without loading it: what the start screen offers (B-01).</summary>
    public ImmutableArray<SaveSummary> Summaries()
    {
        lock (_gate)
            return ListSlots().Select(slot => Summarize(slot, SaveCopy.Current))
                .OrderByDescending(s => s.WrittenAt).ThenByDescending(s => s.PlaytimeSeconds).ThenBy(s => s.Slot, StringComparer.Ordinal)
                .ToImmutableArray();
    }

    /// <summary>A slot's other copies that exist - its backups, then the save it displaced unproven - each read without loading it.</summary>
    public ImmutableArray<SaveSummary> OtherCopies(string slot)
    {
        RequireValidSlot(slot);
        lock (_gate)
            return new[] { SaveCopy.Backup1, SaveCopy.Backup2, SaveCopy.Previous }.Where(c => Directory.Exists(CopyPath(slot, c)))
                .Select(c => Summarize(slot, c)).ToImmutableArray();
    }

    private SaveSummary Summarize(string slot, SaveCopy copy)
    {
        string directory = CopyPath(slot, copy);
        JsonNode? manifest;
        try
        {
            manifest = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, SaveFormat.Manifest)));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new SaveSummary(slot, copy, $"its manifest cannot be read ({e.Message})", DateTimeOffset.MinValue, 0, 0);
        }
        try
        {
            int format = manifest?["save_format"]?.GetValue<int>() ?? 0;
            int schema = manifest?["schema_version"]?.GetValue<int>() ?? 0;
            var written = DateTimeOffset.Parse(manifest?["build_timestamp"]?.GetValue<string>() ?? "", CultureInfo.InvariantCulture);
            double playtime = manifest?["playtime_seconds"]?.GetValue<double>() ?? 0;
            long tick = manifest?["world_tick"]?.GetValue<long>() ?? 0;
            string? problem = format != SaveFormat.Current ? $"save format {format} cannot be read by this build"
                : schema > SaveFormat.SchemaVersion ? $"it was written by a newer build (schema {schema})"
                : schema < SaveFormat.OldestSupportedSchema ? $"schema {schema} is older than this build reads"
                : SaveIntegrity.Verify(directory) is { } damage ? $"it is damaged: {damage}"
                : null;
            return new SaveSummary(slot, copy, problem, written, playtime, tick);
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException)
        {
            return new SaveSummary(slot, copy, $"its manifest cannot be read ({e.Message})", DateTimeOffset.MinValue, 0, 0);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SaveSummary(slot, copy, $"it could not be read ({e.Message})", DateTimeOffset.MinValue, 0, 0);
        }
    }

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
            var manifest = JsonNode.Parse(File.ReadAllBytes(Path.Combine(SlotPath(slot), SaveFormat.Manifest)));
            return DateTimeOffset.Parse(manifest?["build_timestamp"]?.GetValue<string>() ?? "", CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is IOException or JsonException or FormatException or InvalidOperationException or UnauthorizedAccessException)
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
        Guarded(slot, () =>
        {
            Directory.CreateDirectory(staging);
            foreach (var (name, bytes) in files)
                WriteDurably(Path.Combine(staging, name), bytes);
        }, undo: () => DeleteWithRetry(staging));
        Step(SaveStep.StagingWritten);

        // 4. The integrity root, hashed from what is ON DISK, so a write that corrupted data is caught.
        Guarded(slot, () => WriteDurably(Path.Combine(staging, SaveFormat.IntegrityRoot), SaveIntegrity.BuildRoot(staging)),
            undo: () => DeleteWithRetry(staging));
        Step(SaveStep.IntegrityRootWritten);

        // 5. Commit. Never delete first: the previous save is moved aside, then the new one promoted.
        bool hadPrevious = Directory.Exists(slotPath);
        if (hadPrevious)
        {
            Guarded(slot, () => MoveWithRetry(slotPath, trash), undo: () => DeleteWithRetry(staging));
            Step(SaveStep.PreviousMovedToTrash);
        }
        Guarded(slot, () => MoveWithRetry(staging, slotPath), undo: () =>
        {
            if (hadPrevious && !Directory.Exists(slotPath))
                MoveWithRetry(trash, slotPath);
            DeleteWithRetry(staging);
        });
        Step(SaveStep.StagingPromoted);

        // 6. Verify the committed slot by re-reading and re-hashing it.
        string? problem;
        try
        {
            problem = SaveIntegrity.Verify(slotPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = $"it could not be read back ({e.Message})";
        }
        if (problem is not null)
        {
            MoveWithRetry(slotPath, Path.Combine(Root, $".failed-{slot}-{token}"));
            if (hadPrevious)
                MoveWithRetry(trash, slotPath);
            throw new SaveException($"Save to '{slot}' failed verification ({problem}); the previous save was restored");
        }
        Step(SaveStep.Verified);

        // 7. Only now: rotation, then the previous save leaves the slot. The new save is committed and verified whatever happens here:
        // a displaced save that cannot be moved on yet waits in its trash directory for the boot sweep to retire it.
        if (hadPrevious)
        {
            try
            {
                RetirePrevious(slot, trash);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SaveException)
            {
            }
        }
        Step(SaveStep.Rotated);
    }

    /// <summary>
    /// One IO step of the commit. A full disk, a folder the player may not write to or a file held open elsewhere is not a crash: the step
    /// is undone, the previous save is left as it was, and the failure is a <see cref="SaveException"/> saying so (the Phase-1
    /// technical audit, L-03). Anything the undo cannot finish, the boot sweep does.
    /// </summary>
    private static void Guarded(string slot, Action step, Action undo)
    {
        try
        {
            step();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SaveException)
        {
            try
            {
                undo();
            }
            catch (Exception again) when (again is IOException or UnauthorizedAccessException or SaveException)
            {
            }
            throw new SaveException($"Save to '{slot}' failed: {e.Message} The previous save is untouched.", e);
        }
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

    /// <summary>Load one copy of a slot. Any copy but the save itself is loaded only when the player chooses it, and proves nothing.</summary>
    public LoadResult Load(string slot, SaveCopy copy, LoadContext context)
    {
        RequireValidSlot(slot);
        lock (_gate)
            return LoadFrom(CopyPath(slot, copy), slot, context, isBackup: copy != SaveCopy.Current);
    }

    private LoadResult LoadFrom(string directory, string slot, LoadContext context, bool isBackup)
    {
        var report = SaveLoader.NewReport(slot, context);
        LoadResult result;
        try
        {
            result = SaveLoader.Run(directory, slot, context, AvailableBackups(slot), report)
                     ?? throw new SaveCompatibilityException(report);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new SaveException($"'{slot}' could not be read: {e.Message}", e);   // a file held open elsewhere, a folder not ours to read (L-03)
        }

        // A complete, clean load of the bytes as they are on disk proves this save good: it may become a
        // backup (§7.3). A save that needed migrating is proven only once the migrated form is written.
        if (!isBackup && result.IsComplete && report.Result == MigrationResult.UpToDate)
        {
            try
            {
                MarkLoadVerified(slot, SaveIntegrity.RootDigest(directory));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The load succeeded; only its proof went unrecorded, so this save waits for a later load before it can become a backup.
                report.Warnings.Add($"the load could not be recorded in {RotationFile} ({e.Message}); '{slot}' is not yet proven for the backup chain");
            }
        }
        return result;
    }

    // ── §6.2 migration ──────────────────────────────────────────────────────

    /// <summary>
    /// The dry run: what loading and migrating the slot under this build would do. Reads only; writes
    /// nothing, not even the rotation metadata a load records.
    /// </summary>
    public MigrationReport PlanMigration(string slot, LoadContext context)
    {
        RequireValidSlot(slot);
        lock (_gate)
            return SaveLoader.Plan(SlotPath(slot), slot, context);
    }

    /// <summary>The dry run for any save directory, without a store: for tools (<c>save:migrate --dry-run</c>).</summary>
    public static MigrationReport PlanMigrationAt(string saveDirectory, LoadContext context)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(saveDirectory));
        return SaveLoader.Plan(full, Path.GetFileName(full), context);
    }

    /// <summary>
    /// Migrate the slot to the current schema, content and baseline, and commit it through the §7.1
    /// write sequence - the same staging, verification and recovery as any save - so an interruption
    /// leaves the original or the migrated save, never a partial one. The original is kept as
    /// <c>pre_migration_&lt;schema&gt;_&lt;slot&gt;</c>. A save that cannot be proven compatible is refused
    /// with its report; one that loaded with quarantine or invalid records is refused too, because
    /// committing it would make that loss permanent.
    /// </summary>
    public MigrationReport Migrate(string slot, LoadContext context)
    {
        RequireValidSlot(slot);
        lock (_gate)
        {
            var report = SaveLoader.NewReport(slot, context);
            var result = SaveLoader.Run(SlotPath(slot), slot, context, AvailableBackups(slot), report)
                         ?? throw new SaveCompatibilityException(report);
            if (report.Result == MigrationResult.UpToDate)
                return report;
            if (!result.QuarantinedSections.IsEmpty || !result.RejectedRecords.IsEmpty || result.IntegrityRootRederived)
                throw new SaveException(
                    $"'{slot}' did not load cleanly (a quarantined section or invalid records); migrating it would make that loss " +
                    "permanent. Load it in the game and save to accept the loss.");

            Commit(slot, SaveDocuments.Capture(result.World, result.Player, context.Content,
                result.Manifest.WorldTick, result.Manifest.PlaytimeSeconds, result.Manifest.WorldTimeAdvancedTicks));
            return report;
        }
    }

    /// <summary>Null when every file of a save directory matches its <c>sections.sha256</c>; otherwise what failed.</summary>
    public static string? VerifyIntegrity(string saveDirectory) => SaveIntegrity.Verify(saveDirectory);

    /// <summary><c>pre_migration_&lt;schema&gt;_&lt;slot&gt;</c> (§3.2, §7.3): the original of a migrated save.</summary>
    public string PreMigrationPath(string slot, int schema) =>
        Path.Combine(Root, $"pre_migration_{schema.ToString(CultureInfo.InvariantCulture)}_{slot}");

    public IReadOnlyList<string> PreMigrationBackups(string slot) =>
        Directory.EnumerateDirectories(Root, "pre_migration_*_" + slot)
            .Where(p => Path.GetFileName(p).EndsWith("_" + slot, StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

    // ── §7.3 backup rotation ────────────────────────────────────────────────

    /// <summary>
    /// "Two backup generations, retired on a verified LOAD, not a verified write." The displaced save
    /// enters the backup chain only if it was proven good by a clean load; an unproven save is dropped
    /// and never pushes a proven backup out. So a run of saves after a silent problem cannot discard
    /// the last save known to be good.
    /// </summary>
    private void RetirePrevious(string slot, string trash)
    {
        // A save displaced by a newer schema is the original of a migration: kept once, as its
        // pre-migration copy (§6.2), rather than rotated or dropped.
        if (SchemaOf(trash) is int schema && schema < SaveFormat.SchemaVersion && !Directory.Exists(PreMigrationPath(slot, schema)))
        {
            MoveWithRetry(trash, PreMigrationPath(slot, schema));
            return;
        }

        var rotation = ReadRotation();
        if (rotation.TryGetValue(slot, out string? verified) && verified == SaveIntegrity.RootDigest(trash))
        {
            string first = BackupPath(slot, 1), second = BackupPath(slot, 2);
            DeleteWithRetry(second);
            if (Directory.Exists(first))
                MoveWithRetry(first, second);
            MoveWithRetry(trash, first);
        }
        else if (!slot.StartsWith("auto_", StringComparison.Ordinal))
        {
            // Unproven, it cannot enter the chain. But it was the player's save until a moment ago - a relaunch's first quicksave displaces
            // the last session's - so it is kept aside, one deep (B-01). An autosave's history is its four siblings.
            DeleteWithRetry(PreviousPath(slot));
            MoveWithRetry(trash, PreviousPath(slot));
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
            foreach (string path in new[] { SlotPath(slot), BackupPath(slot, 1), BackupPath(slot, 2), PreviousPath(slot) }
                         .Concat(PreMigrationBackups(slot)).Concat(Leftovers(slot)))
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
                string? complete = stagings.LastOrDefault(s => SaveIntegrity.Verify(s) is null);
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
                if (SaveIntegrity.Verify(slotPath) is null)
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
        ContentHash = document.ContentHash,
        WorldSeed = WorldSeed.Format(document.WorldSeed),
        WorldgenVersion = document.Worldgen.Version,
        WorldgenFingerprint = document.Worldgen.Fingerprint,
        RngContractVersion = document.Worldgen.RngContractVersion,
        WorldTick = document.WorldTick,
        WorldTimeAdvancedTicks = document.WorldTimeAdvancedTicks,
        CommandLogSha256 = null,
        BuildTimestamp = _clock().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        PlaytimeSeconds = document.PlaytimeSeconds,
        Flags = new ManifestFlags { QuarantinedSections = ImmutableArray<string>.Empty },
    };

    private void Step(SaveStep step) => _onStep?.Invoke(step);

    private static int? SchemaOf(string directory)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, SaveFormat.Manifest)))?["schema_version"]?.GetValue<int>();
        }
        catch (Exception e) when (e is IOException or JsonException or FormatException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

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
        // The same sharing violations as a commit's renames (§7.1): an indexer or scanner holding rotation.json a moment (L-03). Windows
        // refuses to replace a file held open without delete sharing as access denied, so that is retried too - briefly, since it is
        // also what a folder the player may not write to says.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception e) when (attempt < (e is UnauthorizedAccessException ? 6 : MoveAttempts) && e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(Math.Min(10 << attempt, 2000));
            }
            catch
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
                throw;
            }
        }
    }

    private void MarkLoadVerified(string slot, string integrityRootDigest)
    {
        var rotation = ReadRotation();
        rotation[slot] = integrityRootDigest;
        WriteRotation(rotation);
    }
}
