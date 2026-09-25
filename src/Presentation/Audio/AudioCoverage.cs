// UNNAMED Presentation - whether the sound set and the event mapping meet: every ID reachable, every mapped name present (Phase A, the owner's A4)
// Godot presentation only, and free of Godot itself so the tests can read it: no gameplay state lives here (D-11)

using System.Text;
using System.Text.Json;

namespace UNNAMED.Presentation.Audio;

/// <summary>A name the event mapping can ask the sound set for (a family, or a single ID), the contract's event that asks, and whether it is asked only when present.</summary>
public sealed record MappedSound(string Family, string Event, bool Optional);

/// <summary>
/// The audio coverage report. Statically: every ID in the sound set, and whether the event mapping (<c>SoundEvents.Mapped</c>) can ever
/// ask for it - its family (<c>sfx.player.footstep.dirt.walk</c> for <c>...walk.01</c>) or the ID itself - and every name the mapping asks
/// for that the set does not have, or whose file is not on disk. At run time: what was asked for, what resolved to a sound that loaded,
/// what the set lacked, what the manifest got wrong (left out at load), and which files would not load. Nothing here plays or changes a sound.
/// </summary>
public static class AudioCoverage
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>An ID's family: <c>sfx.player.footstep.dirt.walk.01</c> is <c>sfx.player.footstep.dirt.walk</c>; an unnumbered ID is its own.</summary>
    public static string FamilyOf(string id) =>
        id.LastIndexOf('.') is var dot and > 0 && id[(dot + 1)..].Length > 0 && id[(dot + 1)..].All(char.IsDigit) ? id[..dot] : id;

    public sealed record StaticCheck(
        int Ids, IReadOnlyList<string> Reachable, IReadOnlyList<string> Unreachable, IReadOnlyList<MappedSound> MappedMissing,
        IReadOnlyList<MappedSound> OptionalAbsent, IReadOnlyList<string> WithoutFile);

    /// <summary>The set's IDs (with whether each file is on disk) against the mapping's names.</summary>
    public static StaticCheck Check(IReadOnlyDictionary<string, bool> idsWithFile, IReadOnlyList<MappedSound> mapped)
    {
        var names = mapped.Select(m => m.Family).ToHashSet(StringComparer.Ordinal);
        var families = idsWithFile.Keys.GroupBy(FamilyOf, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var reachable = idsWithFile.Keys.Where(id => names.Contains(id) || names.Contains(FamilyOf(id))).OrderBy(i => i, StringComparer.Ordinal).ToList();
        var unreachable = idsWithFile.Keys.Except(reachable).OrderBy(i => i, StringComparer.Ordinal).ToList();
        bool Present(string name) => idsWithFile.ContainsKey(name) || families.ContainsKey(name);
        var missing = mapped.Where(m => !m.Optional && !Present(m.Family)).OrderBy(m => m.Family, StringComparer.Ordinal).ToList();
        var absent = mapped.Where(m => m.Optional && !Present(m.Family)).OrderBy(m => m.Family, StringComparer.Ordinal).ToList();
        var withoutFile = reachable.Where(id => !idsWithFile[id]).ToList();
        return new StaticCheck(idsWithFile.Count, reachable, unreachable, missing, absent, withoutFile);
    }

    /// <summary>The gate line: the static check and the run's requests.</summary>
    public static string GateLine(StaticCheck check, int requested, int resolved, int missing, int malformed, int errors, int unmapped) =>
        $"UNNAMED audio coverage: {check.Ids} ids, {check.Reachable.Count} reachable, {check.Unreachable.Count} unreachable, "
        + $"{check.MappedMissing.Count} mapped missing, {check.WithoutFile.Count} without file; this run {requested} requested, {resolved} resolved, "
        + $"{missing} missing, {malformed} malformed, {errors} runtime errors, {unmapped} unmapped";

    /// <summary><c>&lt;name&gt;.json</c> and <c>&lt;name&gt;.md</c> in <paramref name="directory"/>; returns the gate line.</summary>
    public static string Write(string directory, string name, string run, StaticCheck check, IReadOnlyList<MappedSound> mapped,
        IReadOnlyDictionary<string, int> requested, IReadOnlyCollection<string> resolved, IReadOnlyCollection<string> missing,
        IReadOnlyList<string> malformed, IReadOnlyDictionary<string, string> errors)
    {
        Directory.CreateDirectory(directory);
        var names = mapped.Select(m => m.Family).ToHashSet(StringComparer.Ordinal);
        // Asked for at run time under a name the static list does not have: the handlers and the list have drifted apart.
        var unmapped = requested.Keys.Where(r => !names.Contains(r) && !names.Contains(FamilyOf(r))).ToList();
        string gate = GateLine(check, requested.Count, resolved.Count, missing.Count, malformed.Count, errors.Count, unmapped.Count);
        var report = new Dictionary<string, object?>
        {
            ["run"] = run,
            ["gate"] = gate,
            ["static"] = new Dictionary<string, object?>
            {
                ["ids"] = check.Ids,
                ["reachable"] = check.Reachable.Count,
                ["unreachable"] = check.Unreachable,
                ["mapped_missing"] = check.MappedMissing,
                ["optional_absent"] = check.OptionalAbsent,
                ["without_file"] = check.WithoutFile,
            },
            ["runtime"] = new Dictionary<string, object?>
            {
                ["requested"] = requested,
                ["resolved"] = resolved.OrderBy(r => r, StringComparer.Ordinal).ToList(),
                ["missing"] = missing.OrderBy(m => m, StringComparer.Ordinal).ToList(),
                ["malformed"] = malformed,
                ["errors"] = errors,
                ["unmapped"] = unmapped,
                ["mapped_never_requested"] = mapped.Where(m => !requested.ContainsKey(m.Family)).Select(m => m.Family).ToList(),
            },
            ["mapping"] = mapped,
        };
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(report, Json));
        var md = new StringBuilder();
        md.Append("# Audio coverage (").Append(run).Append(")\n\n").Append(gate).Append("\n\n");
        md.Append("Static: every ID in the sound set against the event mapping (`SoundEvents.Mapped`). Runtime: what this run asked the set for.\n\n");
        md.Append("## IDs the mapping never asks for\n\n").Append(check.Unreachable.Count == 0 ? "None.\n" : string.Concat(check.Unreachable.Select(u => $"- `{u}`\n")));
        md.Append("\n## Mapped names the set lacks\n\n").Append(check.MappedMissing.Count == 0 ? "None.\n" : string.Concat(check.MappedMissing.Select(m => $"- `{m.Family}` ({m.Event})\n")));
        md.Append("\n## Mapped IDs without a file\n\n").Append(check.WithoutFile.Count == 0 ? "None.\n" : string.Concat(check.WithoutFile.Select(w => $"- `{w}`\n")));
        md.Append("\n## This run\n\n");
        md.Append($"- requested {requested.Count} names, {requested.Values.Sum()} times; resolved {resolved.Count}\n");
        md.Append($"- missing: {(missing.Count == 0 ? "none" : string.Join(", ", missing.Select(m => $"`{m}`")))}\n");
        md.Append($"- malformed manifest entries: {(malformed.Count == 0 ? "none" : string.Join(", ", malformed.Select(m => $"`{m}`")))}\n");
        md.Append($"- runtime errors: {(errors.Count == 0 ? "none" : string.Join(", ", errors.Select(e => $"`{e.Key}` ({e.Value})")))}\n");
        md.Append($"- unmapped requests: {(unmapped.Count == 0 ? "none" : string.Join(", ", unmapped.Select(u => $"`{u}`")))}\n");
        File.WriteAllText(Path.Combine(directory, name + ".md"), md.ToString());
        return gate;
    }
}
