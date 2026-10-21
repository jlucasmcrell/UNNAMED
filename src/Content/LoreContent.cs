// UNNAMED Content - lore from content: what the world can be known by, in pages, and the discoveries that open them (spec 41)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain;
using UNNAMED.Domain.Knowledge;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds the lore entries and lints what the reference pass cannot see (LOR001): each names one of the journal's headings, its pages are
/// numbered 1..n in order, every page reads as prose of <see cref="LoreRules.MinWords"/> to <see cref="LoreRules.MaxWords"/> words, and every
/// source it names is a thing the game can report - a place, a readable, a conversation, a quest, or a creature.
/// </summary>
public static class LoreContent
{
    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        try
        {
            Build(loader, check: true);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(new ValidationError { SeverityLevel = ValidationError.Severity.Error, Code = "LOR001", Message = $"lore: {e.Message}" });
        }
        return errors;
    }

    /// <summary>The entries by ID; empty for a pack with none (test fixtures).</summary>
    public static ImmutableSortedDictionary<string, LoreEntryDefinition> Build(ContentLoader loader, bool check = false)
    {
        var entries = loader.GetByKind("lore").Values.Select(definition => Entry(loader, definition, check))
            .ToImmutableSortedDictionary(e => e.Id, e => e, StringComparer.Ordinal);
        // A readable serves one entry, and one entry is told by one readable, so the journal never shows the same words twice.
        var claimed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries.Values)
            foreach (var source in entry.Pages.SelectMany(p => p.Sources).Where(s => s.Kind == LoreSourceKind.Inspected))
                if (claimed.TryGetValue(source.Ref, out string? other) && other != entry.Id)
                    throw new FormatException($"{source.Ref} is the readable of both {other} and {entry.Id}");
                else
                    claimed[source.Ref] = entry.Id;
        return entries;
    }

    private static LoreEntryDefinition Entry(ContentLoader loader, ContentEnvelope definition, bool check)
    {
        string id = definition.Id;
        var map = Read(definition.YamlSource);
        string title = Text(map, "title").Trim();
        if (title.Length == 0 || title.Contains('\n'))
            throw new FormatException($"{id} title must be one line");
        if (LoreCategories.Parse(Text(map, "category")) is not { } category)
            throw new FormatException($"{id} names the heading '{map.GetValueOrDefault("category")}', which is not one of {string.Join(", ", LoreCategories.All.Select(LoreCategories.Key))}");

        var pages = new List<LorePageDefinition>();
        foreach (var raw in List(map, "pages"))
        {
            if (raw is not Dictionary<object, object> pageMap)
                throw new FormatException($"{id} has a page that is not a map");
            if (!int.TryParse(pageMap.GetValueOrDefault("page") as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) || number < 1)
                throw new FormatException($"{id} has a page with no whole-number 'page'");
            if (pages.Any(p => p.Page == number))
                throw new FormatException($"{id} numbers page {number} twice");
            string text = Text(pageMap, "text").Replace("\r", "").Replace("\n", " ").Trim();
            while (text.Contains("  "))
                text = text.Replace("  ", " ");
            int words = LoreRules.WordCount(text);
            if (words is < LoreRules.MinWords or > LoreRules.MaxWords)
                throw new FormatException($"{id} page {number} reads {words} words; a page is {LoreRules.MinWords} to {LoreRules.MaxWords}");
            var sources = new List<LoreSource>();
            foreach (var rawSource in List(pageMap, "sources"))
            {
                if (rawSource is not Dictionary<object, object> sourceMap)
                    throw new FormatException($"{id} page {number} has a source that is not a map");
                string kindName = Text(sourceMap, "how");
                if (LoreSources.Parse(kindName) is not { } kind)
                    throw new FormatException($"{id} page {number} is opened by '{kindName}', which is not a way the world reports ({string.Join(", ", Enum.GetValues<LoreSourceKind>().Select(LoreSources.Key))})");
                string sourceRef = Text(sourceMap, "ref");
                if (check)
                    Check(loader, id, number, kind, sourceRef);
                if (sources.Any(s => s.Kind == kind && s.Ref == sourceRef))
                    throw new FormatException($"{id} page {number} names {sourceRef} twice");
                sources.Add(new LoreSource(kind, sourceRef));
            }
            if (sources.Count == 0)
                throw new FormatException($"{id} page {number} can never be found: nothing opens it");
            pages.Add(new LorePageDefinition(number, text, sources.ToImmutableArray()));
        }
        if (pages.Count == 0)
            throw new FormatException($"{id} has no pages");
        if (!pages.Select(p => p.Page).OrderBy(p => p).SequenceEqual(Enumerable.Range(1, pages.Count)))
            throw new FormatException($"{id} numbers its pages {string.Join(", ", pages.Select(p => p.Page))}; they must run 1 to {pages.Count}");
        return new LoreEntryDefinition(id, title, category, pages.OrderBy(p => p.Page).ToImmutableArray());
    }

    /// <summary>That what a page names is a thing this pack has, so a page cannot be authored against nothing.</summary>
    private static void Check(ContentLoader loader, string id, int page, LoreSourceKind kind, string sourceRef)
    {
        if (!DefinitionId.IsValid(sourceRef))
            throw new FormatException($"{id} page {page} names '{sourceRef}', which is not a definition ID");
        switch (kind)
        {
            case LoreSourceKind.Inspected:
                if (!sourceRef.StartsWith("readable.", StringComparison.Ordinal))
                    throw new FormatException($"{id} page {page} is read at '{sourceRef}'; a readable's key is readable.<name>");
                return;
            case LoreSourceKind.Visited when !sourceRef.StartsWith("location.", StringComparison.Ordinal):
                throw new FormatException($"{id} page {page} is found by visiting '{sourceRef}', which is not a place");
            case LoreSourceKind.Quest when !sourceRef.StartsWith("quest.", StringComparison.Ordinal):
                throw new FormatException($"{id} page {page} is opened by '{sourceRef}', which is not a quest");
            case LoreSourceKind.Studied when !sourceRef.StartsWith("creature.", StringComparison.Ordinal):
                throw new FormatException($"{id} page {page} is opened by studying '{sourceRef}', which is not a creature");
            case LoreSourceKind.Told when !sourceRef.StartsWith("dialogue.", StringComparison.Ordinal):
                throw new FormatException($"{id} page {page} is told by '{sourceRef}'; a told page names the conversation it is heard in");
        }
        if (!loader.Definitions.ContainsKey(sourceRef))
            throw new FormatException($"{id} page {page} names '{sourceRef}', which this pack does not have");
    }
}
