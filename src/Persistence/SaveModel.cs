// UNNAMED Persistence - save model (PERSISTENCE.md §3, §4, §6, §7.2)
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

    /// <summary>
    /// GAMEPLAY STATE schema (§6.1). Every older version back to <see cref="OldestSupportedSchema"/>
    /// migrates step by step (<see cref="SchemaMigrations"/>), and every one has a committed fixture.
    /// </summary>
    public const int SchemaVersion = 2;

    public const int OldestSupportedSchema = 1;

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
/// <remarks>
/// Schema 1 (M2) recorded <c>worldgen_digest</c>; schema 2 replaced it with
/// <c>worldgen_fingerprint</c> plus <c>rng_contract_version</c> (M2b). The fingerprint covers what the
/// digest covered and the generator's actual output on the canonical probe cells, and it is no longer
/// the authority for applying a delta: each cell's <c>baseline_hash</c> is.
/// </remarks>
public sealed record SaveManifest
{
    [JsonPropertyName("save_format")] public required int SaveFormatVersion { get; init; }
    [JsonPropertyName("schema_version")] public required int SchemaVersion { get; init; }

    /// <summary>Human-readable content release label. Diagnostic; never a compatibility authority.</summary>
    [JsonPropertyName("content_version")] public required string ContentVersion { get; init; }

    /// <summary>Exactly which compiled content pack wrote this save. Not an input to generation.</summary>
    [JsonPropertyName("content_hash")] public required string ContentHash { get; init; }

    [JsonPropertyName("world_seed")] public required string WorldSeed { get; init; }

    /// <summary>Human compatibility epoch of the generator contract.</summary>
    [JsonPropertyName("worldgen_version")] public required int WorldgenVersion { get; init; }

    /// <summary>Computed generator identity (<see cref="World.WorldgenFingerprint"/>): drift detection.</summary>
    [JsonPropertyName("worldgen_fingerprint")] public required string WorldgenFingerprint { get; init; }

    [JsonPropertyName("rng_contract_version")] public required int RngContractVersion { get; init; }

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

/// <summary>The generator contract a save was written against.</summary>
public sealed record WorldgenIdentity(int Version, string Fingerprint, int RngContractVersion)
{
    public static WorldgenIdentity Of(ICellBaselineGenerator generator) =>
        new(generator.WorldgenVersion, generator.Fingerprint, generator.RngContractVersion);
}

/// <summary>Everything a save records: the load key's inputs, the player, and the world delta.</summary>
public sealed record SaveDocument(
    ulong WorldSeed,
    WorldgenIdentity Worldgen,
    string ContentVersion,
    string ContentHash,
    long WorldTick,
    long WorldTimeAdvancedTicks,
    double PlaytimeSeconds,
    PlayerRecord Player,
    DeltaSnapshot Delta);

/// <summary>
/// What the running build is. A save is compared against it and migrated, rebased through a
/// registered transition, or refused - never silently regenerated (§6, M2b).
/// </summary>
public sealed record LoadContext(ICellBaselineGenerator Generator, ContentIdentity Content, Registry Registry)
{
    /// <summary>The schema this build reads and writes.</summary>
    public int SchemaVersion { get; init; } = SaveFormat.SchemaVersion;

    /// <summary>The ordered schema migration table (§6.2). Tests substitute synthetic tables.</summary>
    public ImmutableArray<SchemaMigration> Migrations { get; init; } = SchemaMigrations.Production;

    /// <summary>Registered baseline transitions: the only way a delta reaches a changed baseline (M2b §7).</summary>
    public ImmutableArray<BaselineTransition> Transitions { get; init; } = ImmutableArray<BaselineTransition>.Empty;
}

/// <summary>The outcome of a load. Quarantined sections, rejected records and migration are reported, never hidden.</summary>
public sealed record LoadResult(
    SaveManifest Manifest,
    PlayerRecord Player,
    WorldDelta World,
    ImmutableArray<string> QuarantinedSections,
    ImmutableArray<RejectedRecord> RejectedRecords,
    bool IntegrityRootRederived,
    MigrationReport Report)
{
    /// <summary>True when nothing was lost: no quarantine, no rejected record, no reported loss.</summary>
    public bool IsComplete =>
        QuarantinedSections.IsEmpty && RejectedRecords.IsEmpty && !IntegrityRootRederived && Report.Loss.Count == 0;
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
/// The save cannot be proven compatible with this build and no registered migration makes it so: an
/// unresolved definition ID, a schema with no chain, a changed baseline with no transition. The
/// report names every blocker. Loud and recoverable, never silent (M2b §8).
/// </summary>
public sealed class SaveCompatibilityException : SaveException
{
    public SaveCompatibilityException(MigrationReport report)
        : base("The save cannot be loaded by this build:\n  " + string.Join("\n  ", report.Blockers)) => Report = report;

    public MigrationReport Report { get; }
}
