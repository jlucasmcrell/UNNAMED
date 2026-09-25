// UNNAMED Presentation - what the run drew from the asset library and what it drew in greybox, by semantic ID (Phase A, the owner's A3)
// Godot presentation only, and free of Godot itself so the tests can read it: no gameplay state lives here (D-11)

using System.Text;
using System.Text.Json;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// Every visual the views asked for, by what it stands for (<c>structure:rock_rim_01</c>, <c>creature:creature.beast.wolf_grey</c>,
/// <c>node:node.ore.iron_seam:spent</c>): the asset that drew it, or the greybox that stood in and why - the greybox the views build
/// themselves included (a structure's box, a creature's blocks, the mannequin, a door panel, the debug rings). A fallback the allowlist
/// names (<c>art_coverage_allowlist.json</c>: greybox kept on purpose, outside ordinary play, each with its reason) is allowed; any other is
/// unexpected, and the report's gate line counts it. Recording never fails a frame: it only counts.
/// </summary>
public sealed class ArtCoverage
{
    private sealed class State
    {
        public required string Kind;
        public required string Id;
        public string? Asset;
        public string? Why;
        public int Resolved;
        public int Fallbacks;
    }

    /// <summary>One visual, as the report lists it.</summary>
    public sealed record Entry(string Key, string Kind, string Id, string? Asset, string Outcome, string? Why, int Resolved, int Fallbacks, string? Allowed);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly SortedDictionary<string, State> _entries = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, string> _allowed = new Dictionary<string, string>();

    /// <summary>The allowlist: a key (or a prefix ending in <c>*</c>) to why its greybox is kept.</summary>
    public void Allow(IReadOnlyDictionary<string, string> allowed) => _allowed = allowed;

    /// <summary><paramref name="kind"/>:<paramref name="id"/> was drawn with <paramref name="asset"/>.</summary>
    public void Resolved(string kind, string id, string asset)
    {
        var state = Get(kind, id);
        state.Resolved++;
        state.Asset ??= asset;
    }

    /// <summary><paramref name="kind"/>:<paramref name="id"/> was drawn in greybox, and why (the asset it asked for, when it asked for one).</summary>
    public void Fallback(string kind, string id, string why, string? asset = null)
    {
        var state = Get(kind, id);
        state.Fallbacks++;
        state.Why = why;
        if (asset is not null)
            state.Asset = asset;
    }

    private State Get(string kind, string id)
    {
        string key = $"{kind}:{id}";
        if (!_entries.TryGetValue(key, out var state))
            _entries[key] = state = new State { Kind = kind, Id = id };
        return state;
    }

    /// <summary>Every visual asked for: a fallback on any occasion is a fallback (a view that once drew greybox for it drew it on screen).</summary>
    public IReadOnlyList<Entry> Entries => _entries.Select(e => new Entry(e.Key, e.Value.Kind, e.Value.Id, e.Value.Asset,
        e.Value.Fallbacks > 0 ? "fallback" : "resolved", e.Value.Fallbacks > 0 ? e.Value.Why : null, e.Value.Resolved, e.Value.Fallbacks,
        e.Value.Fallbacks > 0 ? AllowedWhy(e.Key) : null)).ToList();

    public int Requested => _entries.Count;

    public int ResolvedCount => _entries.Values.Count(s => s.Fallbacks == 0);

    public int FallbackCount => _entries.Values.Count(s => s.Fallbacks > 0);

    public int UnexpectedCount => _entries.Count(e => e.Value.Fallbacks > 0 && AllowedWhy(e.Key) is null);

    /// <summary>The line an orchestrator gates on.</summary>
    public string GateLine => $"UNNAMED art coverage: {Requested} requested, {ResolvedCount} resolved, {FallbackCount} fallbacks, {UnexpectedCount} unexpected";

    private string? AllowedWhy(string key)
    {
        if (_allowed.TryGetValue(key, out string? why))
            return why;
        foreach (var (pattern, reason) in _allowed)
        {
            if (pattern.EndsWith('*') && key.StartsWith(pattern[..^1], StringComparison.Ordinal))
                return reason;
        }
        return null;
    }

    /// <summary>The allowlist file's <c>allowed</c> map (key or prefix* to reason); empty, with why, when it cannot be read.</summary>
    public static IReadOnlyDictionary<string, string> ParseAllowlist(string json, out string? problem)
    {
        problem = null;
        var allowed = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("allowed", out var map)
                || map.ValueKind != JsonValueKind.Object)
            {
                problem = "no \"allowed\" object";
                return allowed;
            }
            foreach (var entry in map.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(entry.Value.GetString()))
                    allowed[entry.Name] = entry.Value.GetString()!;
                else
                    problem = $"{entry.Name}: an allowed fallback needs its reason";
            }
        }
        catch (JsonException e)
        {
            problem = $"not JSON ({e.Message})";
        }
        return allowed;
    }

    /// <summary>
    /// <c>&lt;name&gt;.json</c> and <c>&lt;name&gt;.md</c> in <paramref name="directory"/>: the counts, every unexpected and allowed fallback with why,
    /// and every visual; <paramref name="extra"/> is added to the JSON as it is (the library's problems, its load time). Returns the gate line.
    /// </summary>
    public string Write(string directory, string name, string run, IReadOnlyDictionary<string, object?> extra)
    {
        Directory.CreateDirectory(directory);
        var entries = Entries;
        var unexpected = entries.Where(e => e.Outcome == "fallback" && e.Allowed is null).ToList();
        var allowed = entries.Where(e => e.Outcome == "fallback" && e.Allowed is not null).ToList();
        var report = new Dictionary<string, object?>
        {
            ["run"] = run,
            ["gate"] = GateLine,
            ["counts"] = new Dictionary<string, int>
            {
                ["requested"] = Requested, ["resolved"] = ResolvedCount, ["fallbacks"] = FallbackCount, ["unexpected"] = UnexpectedCount,
            },
            ["by_kind"] = entries.GroupBy(e => e.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => new Dictionary<string, int>
            {
                ["requested"] = g.Count(), ["resolved"] = g.Count(e => e.Outcome == "resolved"), ["fallbacks"] = g.Count(e => e.Outcome == "fallback"),
                ["unexpected"] = g.Count(e => e.Outcome == "fallback" && e.Allowed is null),
            }),
            ["unexpected"] = unexpected.Select(e => new Dictionary<string, object?> { ["key"] = e.Key, ["asset"] = e.Asset, ["why"] = e.Why }).ToList(),
            ["allowed"] = allowed.Select(e => new Dictionary<string, object?> { ["key"] = e.Key, ["asset"] = e.Asset, ["why"] = e.Why, ["allowed_because"] = e.Allowed }).ToList(),
            ["entries"] = entries,
        };
        foreach (var (key, value) in extra)
            report[key] = value;
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(report, Json));

        var md = new StringBuilder();
        md.Append("# Art coverage (").Append(run).Append(")\n\n").Append(GateLine).Append("\n\n");
        md.Append("A visual is a thing the game draws, by what it stands for; resolved is drawn with an asset from the library, a fallback is drawn in greybox. ")
          .Append("Allowed fallbacks are named in `src/Presentation/Art/art_coverage_allowlist.json`, each with its reason.\n\n");
        md.Append("| Kind | Requested | Resolved | Fallbacks | Unexpected |\n|---|---|---|---|---|\n");
        foreach (var group in entries.GroupBy(e => e.Kind).OrderBy(g => g.Key, StringComparer.Ordinal))
            md.Append($"| {group.Key} | {group.Count()} | {group.Count(e => e.Outcome == "resolved")} | {group.Count(e => e.Outcome == "fallback")} | {group.Count(e => e.Outcome == "fallback" && e.Allowed is null)} |\n");
        md.Append("\n## Unexpected fallbacks\n\n");
        md.Append(unexpected.Count == 0 ? "None.\n" : string.Concat(unexpected.Select(e => $"- `{e.Key}`{(e.Asset is { } a ? $" (asked for `{a}`)" : "")}: {e.Why}\n")));
        md.Append("\n## Allowed fallbacks\n\n");
        md.Append(allowed.Count == 0 ? "None.\n" : string.Concat(allowed.Select(e => $"- `{e.Key}`: {e.Why} - allowed: {e.Allowed}\n")));
        File.WriteAllText(Path.Combine(directory, name + ".md"), md.ToString());
        return GateLine;
    }
}
