// UNNAMED Persistence - save model (PERSISTENCE.md §3, §4, §7.2)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Text.Json.Serialization;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence;

public static class SaveFormat
{
    /// <summary>CONTAINER version (§6.1): a mismatch refuses to load, because a layout cannot be guessed.</summary>
    public const int Current = 1;

    /// <summary>GAMEPLAY STATE schema (§6.1): a mismatch needs the migration chain (M2b).</summary>
    public const int SchemaVersion = 1;

    public const string Manifest = "manifest.json";
    public const string Player = "player.msgpack";
    public const string Cells = "cells.msgpack";
    public const string Entities = "entities.msgpack";
    public const string IntegrityRoot = "sections.sha256";

    /// <summary>Every file the integrity root covers, in canonical order.</summary>
    public static readonly ImmutableArray<string> CheckedFiles =
        ImmutableArray.Create(Cells, Entities, Manifest, Player);
}

/// <summary>
/// <c>manifest.json</c> (§4.2): the deterministic load key. It carries every field the load sequence
/// reads before player state. It deliberately has no checksum of itself: the integrity root is
/// <c>sections.sha256</c>, which covers the manifest too (§4.2 "a self-referential checksum is a trap").
/// </summary>
public sealed record SaveManifest
{
    [JsonPropertyName("save_format")] public required int SaveFormatVersion { get; init; }
    [JsonPropertyName("schema_version")] public required int SchemaVersion { get; init; }
    [JsonPropertyName("content_version")] public required string ContentVersion { get; init; }
    [JsonPropertyName("content_hash")] public required string ContentHash { get; init; }
    [JsonPropertyName("worldgen_version")] public required int WorldgenVersion { get; init; }
    [JsonPropertyName("worldgen_digest")] public required string WorldgenDigest { get; init; }
    [JsonPropertyName("world_seed")] public required string WorldSeed { get; init; }
    [JsonPropertyName("world_tick")] public required long WorldTick { get; init; }
    [JsonPropertyName("world_time_advanced_ticks")] public required long WorldTimeAdvancedTicks { get; init; }

    /// <summary>Absent: this save carries no command log, so replay is unavailable (§4.2).</summary>
    [JsonPropertyName("command_log_sha256")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CommandLogSha256 { get; init; }

    [JsonPropertyName("build_timestamp")] public required string BuildTimestamp { get; init; }
    [JsonPropertyName("playtime_seconds")] public required double PlaytimeSeconds { get; init; }
    [JsonPropertyName("flags")] public required ManifestFlags Flags { get; init; }
}

public sealed record ManifestFlags
{
    [JsonPropertyName("quarantined_sections")] public required ImmutableArray<string> QuarantinedSections { get; init; }
}

/// <summary>Everything a save records: the load key's inputs, the player, and the world delta.</summary>
public sealed record SaveDocument(
    BaselineTuple Baseline,
    string WorldgenDigest,
    string ContentVersion,
    long WorldTick,
    long WorldTimeAdvancedTicks,
    double PlaytimeSeconds,
    PlayerRecord Player,
    DeltaSnapshot Delta);

/// <summary>
/// What the running build is: the load sequence compares a save against it and refuses, rather than
/// silently regenerating, when the save was made against a different baseline (§6.1, D-05).
/// </summary>
public sealed record LoadContext(
    ICellBaselineGenerator Generator,
    string ContentHash,
    Registry Registry,
    int SchemaVersion = SaveFormat.SchemaVersion);

/// <summary>The outcome of a load. Quarantined sections and rejected records are reported, never hidden.</summary>
public sealed record LoadResult(
    SaveManifest Manifest,
    PlayerRecord Player,
    WorldDelta World,
    ImmutableArray<string> QuarantinedSections,
    ImmutableArray<RejectedRecord> RejectedRecords,
    bool IntegrityRootRederived)
{
    /// <summary>True when nothing was lost: no quarantine, no rejected record.</summary>
    public bool IsComplete => QuarantinedSections.IsEmpty && RejectedRecords.IsEmpty && !IntegrityRootRederived;
}

/// <summary>A step boundary in the §7.1 write sequence. Tests inject a crash at each one.</summary>
public enum SaveStep
{
    StagingWritten,
    IntegrityRootWritten,
    PreviousMovedToTrash,
    StagingPromoted,
    Verified,
    Rotated,
}

public class SaveException : Exception
{
    public SaveException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The save cannot be read as a save at all (missing or unreadable manifest, player, or layout).</summary>
public sealed class SaveCorruptionException : SaveException
{
    public SaveCorruptionException(string message, ImmutableArray<int> backupGenerations, Exception? inner = null)
        : base(message, inner) => BackupGenerations = backupGenerations;

    /// <summary>Backup generations that exist for this slot - offered, never loaded automatically (§7.2).</summary>
    public ImmutableArray<int> BackupGenerations { get; }
}

/// <summary><c>save_format</c> differs: refuse, a container layout cannot be guessed (§6.1).</summary>
public sealed class SaveFormatMismatchException : SaveException
{
    public SaveFormatMismatchException(int found, int expected)
        : base($"save_format {found} cannot be read by this build (expects {expected})") { }
}

/// <summary>
/// The save was made against a different baseline. Loading it would apply its delta to the wrong
/// world, so the load stops here instead of regenerating silently (§6.1, RK-01).
/// </summary>
public sealed class BaselineMismatchException : SaveException
{
    public BaselineMismatchException(string field, string saved, string current, bool migratable)
        : base($"{field} differs: save has {saved}, this build has {current}. " +
               (migratable ? "The save needs migration." : "Refusing to apply the delta to a different baseline."))
    {
        Field = field;
        Migratable = migratable;
    }

    public string Field { get; }

    /// <summary>
    /// True for content_hash and schema_version, which the migration chain can bridge (M2b). False for
    /// worldgen_digest, which §6.1 refuses outright.
    /// </summary>
    public bool Migratable { get; }
}
