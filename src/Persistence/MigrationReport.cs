// UNNAMED Persistence - the migration report (M2b §13-§14)
// No Godot references - pure C#

using System.Globalization;
using System.Text;

namespace UNNAMED.Persistence;

public enum MigrationResult
{
    /// <summary>The save is already current: nothing to migrate.</summary>
    UpToDate,

    /// <summary>Every difference has a defined path; migration can be committed.</summary>
    Ready,

    /// <summary>At least one blocker: the save cannot be loaded by this build without a new migration.</summary>
    Blocked,
}

/// <summary>
/// What loading a save under the running build does to it: every step, alias, removal, rebase, loss
/// and blocker. Produced by every load and by the dry run, so migration behaviour is never buried in
/// logs (M2b §14).
/// </summary>
public sealed class MigrationReport
{
    private readonly SortedDictionary<string, int> _aliases = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _removals = new(StringComparer.Ordinal);

    public MigrationReport(string save) => Save = save;

    public string Save { get; }

    public int CurrentSaveFormat => SaveFormat.Current;
    public int? SourceSaveFormat { get; internal set; }

    public int? SourceSchema { get; internal set; }
    public int CurrentSchema { get; internal set; }

    public string? SourceContentVersion { get; internal set; }
    public string? CurrentContentVersion { get; internal set; }
    public string? SourceContentHash { get; internal set; }
    public string? CurrentContentHash { get; internal set; }

    public int? SourceWorldgenVersion { get; internal set; }
    public int CurrentWorldgenVersion { get; internal set; }
    public string? SourceFingerprint { get; internal set; }
    public string? CurrentFingerprint { get; internal set; }
    public int? SourceRngContract { get; internal set; }
    public int CurrentRngContract { get; internal set; }

    /// <summary>Schema and worldgen migration steps applied, in order.</summary>
    public List<string> Steps { get; } = new();

    /// <summary>Renamed definition IDs applied, with how many references each rewrote.</summary>
    public IReadOnlyList<string> Aliases => Render(_aliases);

    /// <summary>Removed definition IDs applied: replacements and discards.</summary>
    public IReadOnlyList<string> Removals => Render(_removals);

    /// <summary>Changed cells whose saved baseline hash equals the regenerated one.</summary>
    public int CellsMatched { get; internal set; }

    /// <summary>Changed cells moved onto a changed baseline by a registered transition.</summary>
    public List<string> CellsRebased { get; } = new();

    /// <summary>Changed cells whose baseline changed with no registered transition (each is also a blocker).</summary>
    public List<string> CellsMismatched { get; } = new();

    public List<string> Warnings { get; } = new();
    public List<string> Blockers { get; } = new();

    /// <summary>Data that migration drops by a declared rule (a discarded definition, a vanished target).</summary>
    public List<string> Loss { get; } = new();

    public bool ContentChanged => SourceContentHash is not null && SourceContentHash != CurrentContentHash;

    public bool FingerprintChanged => SourceFingerprint is not null && SourceFingerprint != CurrentFingerprint;

    /// <summary>True when committing the migrated save would change what is on disk.</summary>
    public bool ChangesSave =>
        Steps.Count > 0 || _aliases.Count > 0 || _removals.Count > 0 || CellsRebased.Count > 0 || Loss.Count > 0
        || SourceSchema != CurrentSchema || ContentChanged || FingerprintChanged;

    public MigrationResult Result =>
        Blockers.Count > 0 ? MigrationResult.Blocked : ChangesSave ? MigrationResult.Ready : MigrationResult.UpToDate;

    internal void CountAlias(string from, string to) => Count(_aliases, $"{from} -> {to}");

    internal void CountReplacement(string from, string to) => Count(_removals, $"{from} -> {to} (removed, replaced)");

    internal void CountDiscard(string id) => Count(_removals, $"{id} (removed, discarded)");

    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine($"Save: {Save}");
        text.AppendLine($"Save format: {Describe(SourceSaveFormat)} (this build reads {CurrentSaveFormat})");
        text.AppendLine($"Schema: {Describe(SourceSchema)} -> {CurrentSchema}");
        text.AppendLine($"Content version: {SourceContentVersion ?? "?"} -> {CurrentContentVersion ?? "?"}");
        text.AppendLine($"Content hash: {(ContentChanged ? "changed" : "unchanged")}");
        text.AppendLine($"  save:    {SourceContentHash ?? "?"}");
        text.AppendLine($"  current: {CurrentContentHash ?? "?"}");
        text.AppendLine($"Worldgen: {Describe(SourceWorldgenVersion)} -> {CurrentWorldgenVersion}, RNG contract {Describe(SourceRngContract)} -> {CurrentRngContract}");
        text.AppendLine($"Worldgen fingerprint: {(SourceFingerprint is null ? "not recorded (schema 1)" : FingerprintChanged ? "changed" : "unchanged")}");
        text.AppendLine($"  save:    {SourceFingerprint ?? "-"}");
        text.AppendLine($"  current: {CurrentFingerprint ?? "?"}");
        Section(text, "Migration steps", Steps);
        Section(text, "Definition aliases", Aliases);
        Section(text, "Definition removals", Removals);
        text.AppendLine("Changed cells:");
        text.AppendLine($"- {CellsMatched.ToString(CultureInfo.InvariantCulture)} baseline hash matches");
        text.AppendLine($"- {CellsRebased.Count.ToString(CultureInfo.InvariantCulture)} rebased by a registered transition");
        text.AppendLine($"- {CellsMismatched.Count.ToString(CultureInfo.InvariantCulture)} baseline mismatches with no transition");
        foreach (string cell in CellsRebased)
            text.AppendLine($"  rebased: {cell}");
        foreach (string cell in CellsMismatched)
            text.AppendLine($"  mismatch: {cell}");
        Section(text, "Warnings", Warnings);
        Section(text, "Blockers", Blockers);
        Section(text, "Expected loss", Loss);
        text.AppendLine();
        text.AppendLine("Result: " + Result switch
        {
            MigrationResult.UpToDate => "UP TO DATE - nothing to migrate",
            MigrationResult.Ready => "READY TO MIGRATE",
            _ => "BLOCKED - this build cannot load the save",
        });
        return text.ToString();
    }

    private static void Section(StringBuilder text, string title, IReadOnlyList<string> lines)
    {
        text.AppendLine($"{title}:");
        if (lines.Count == 0)
            text.AppendLine("- none");
        foreach (string line in lines)
            text.AppendLine("- " + line);
    }

    private static string Describe(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "?";

    private static void Count(SortedDictionary<string, int> counts, string key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;

    private static IReadOnlyList<string> Render(SortedDictionary<string, int> counts) =>
        counts.Select(kv => kv.Value == 1 ? kv.Key : $"{kv.Key} x{kv.Value.ToString(CultureInfo.InvariantCulture)}").ToList();
}
