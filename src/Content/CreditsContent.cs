// UNNAMED Content - the credits data file (content/credits.yaml): read for the Credits screen, and checked against the art bindings

using YamlDotNet.Serialization;

namespace UNNAMED.Content;

/// <summary>One credited source: the words shown, any indented lines under them, and the bound model ids and ledger rows it covers.</summary>
public sealed record CreditEntry(string Text, IReadOnlyList<string> Lines, IReadOnlyList<string> Binds, IReadOnlyList<string> Ledger);

/// <summary>A group on the Credits screen, in file order.</summary>
public sealed record CreditSection(string Id, string Title, string Note, IReadOnlyList<CreditEntry> Entries);

/// <summary>
/// <c>content/credits.yaml</c>: the studio line first, then the third-party attributions in the form each licence requires, the code
/// notices, as data. The Credits screen draws nothing that is not here; <see cref="Credit"/> is how a bound
/// model finds its entry, so a test can fail a build that binds a source nobody credits.
/// </summary>
public sealed class CreditsContent
{
    public string Studio { get; }
    public string StudioNote { get; }
    public IReadOnlyList<CreditSection> Sections { get; }
    /// <summary>Model-id prefixes (or ids) built in house, or made with the tools the source ledger names: no third-party entry needed.</summary>
    public IReadOnlyList<string> FirstParty { get; }

    private CreditsContent(string studio, string studioNote, IReadOnlyList<CreditSection> sections, IReadOnlyList<string> firstParty)
    {
        Studio = studio;
        StudioNote = studioNote;
        Sections = sections;
        FirstParty = firstParty;
    }

    public static CreditsContent Load(string path) => Parse(File.ReadAllText(path), path);

    public static CreditsContent Parse(string yaml, string source = "credits.yaml")
    {
        var root = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object?>>(yaml)
            ?? throw new InvalidDataException($"{source}: empty");
        string studio = Str(root, "studio") ?? throw new InvalidDataException($"{source}: no studio line");
        var sections = new List<CreditSection>();
        foreach (var raw in Seq(root, "sections"))
        {
            var section = (Dictionary<object, object?>)raw!;
            var entries = new List<CreditEntry>();
            foreach (var rawEntry in Seq(section, "entries"))
            {
                var entry = (Dictionary<object, object?>)rawEntry!;
                entries.Add(new CreditEntry(
                    Str(entry, "text") ?? throw new InvalidDataException($"{source}: an entry in '{Str(section, "title")}' has no text"),
                    Strings(entry, "lines"), Strings(entry, "binds"), Strings(entry, "ledger")));
            }
            sections.Add(new CreditSection(Str(section, "id") ?? "", Str(section, "title") ?? "", Str(section, "note") ?? "", entries));
        }
        if (sections.Count == 0)
            throw new InvalidDataException($"{source}: no sections");
        return new CreditsContent(studio, Str(root, "studio_note") ?? "", sections, Strings(root, "first_party"));
    }

    /// <summary>Every entry, section by section.</summary>
    public IEnumerable<CreditEntry> Entries => Sections.SelectMany(s => s.Entries);

    /// <summary>
    /// The entry that credits a bound model id: the one with the longest <c>binds</c> prefix (or exact id) that matches it, or null.
    /// A first-party id is not credited here: see <see cref="IsFirstParty"/>.
    /// </summary>
    public CreditEntry? Credit(string modelId)
    {
        CreditEntry? best = null;
        int bestLength = -1;
        foreach (var entry in Entries)
            foreach (string bind in entry.Binds)
                if (Matches(modelId, bind) && bind.Length > bestLength)
                    (best, bestLength) = (entry, bind.Length);
        return best;
    }

    /// <summary>True when the id is under a first-party prefix, and no credited source claims it more specifically.</summary>
    public bool IsFirstParty(string modelId)
    {
        int credited = Entries.SelectMany(e => e.Binds).Where(b => Matches(modelId, b)).Select(b => b.Length).DefaultIfEmpty(-1).Max();
        int own = FirstParty.Where(p => Matches(modelId, p)).Select(p => p.Length).DefaultIfEmpty(-1).Max();
        return own > credited;
    }

    /// <summary>A bind is a prefix of the id (a whole id is its own prefix).</summary>
    private static bool Matches(string id, string bind) => id.StartsWith(bind, StringComparison.Ordinal);

    private static string? Str(IDictionary<string, object?> map, string key) => map.TryGetValue(key, out var v) ? v as string : null;
    private static string? Str(IDictionary<object, object?> map, string key) => map.TryGetValue(key, out var v) ? v as string : null;

    private static IEnumerable<object?> Seq(IDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out var v) && v is IEnumerable<object?> list ? list : Enumerable.Empty<object?>();
    private static IEnumerable<object?> Seq(IDictionary<object, object?> map, string key) =>
        map.TryGetValue(key, out var v) && v is IEnumerable<object?> list ? list : Enumerable.Empty<object?>();

    private static IReadOnlyList<string> Strings(IDictionary<string, object?> map, string key) => Seq(map, key).Select(o => o?.ToString() ?? "").ToList();
    private static IReadOnlyList<string> Strings(IDictionary<object, object?> map, string key) => Seq(map, key).Select(o => o?.ToString() ?? "").ToList();
}
