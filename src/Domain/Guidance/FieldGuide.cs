// UNNAMED Domain - guidance: the Field Guide's searchable entries and how a search ranks them (1I: F1, searchable)
// No Godot references - pure C#, so the guide's matching is testable without the engine

using System.Collections.Immutable;

namespace UNNAMED.Domain.Guidance;

/// <summary>
/// One line of the Field Guide: a section it sits under, a title that says what it is, and the words that explain it. The same
/// entries serve the controls sheet, the hints and the three things this world does differently, so F1 answers a question with
/// what the game actually does rather than with a second copy of it.
/// </summary>
public sealed record GuideEntry(string Section, string Title, string Body);

/// <summary>
/// Searching the Field Guide: every word typed must be found, case does not matter, and a hit in the title outranks one in the
/// body so the entry that names what was asked for comes first. An empty search lists everything, in the order the sheet shows.
/// </summary>
public static class FieldGuide
{
    /// <summary>The entries matching <paramref name="query"/>, best first; every entry when the query is blank.</summary>
    public static ImmutableArray<GuideEntry> Search(IEnumerable<GuideEntry> entries, string? query)
    {
        var all = entries as IReadOnlyCollection<GuideEntry> ?? entries.ToList();
        string[] tokens = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return all.OrderBy(entry => entry.Section, StringComparer.Ordinal)
                .ThenBy(entry => entry.Title, StringComparer.Ordinal)
                .ToImmutableArray();
        return all
            .Select(entry => (Entry: entry, Score: Score(entry, tokens)))
            .Where(pair => pair.Score >= 0)
            .OrderByDescending(pair => pair.Score)
            .ThenBy(pair => pair.Entry.Section, StringComparer.Ordinal)
            .ThenBy(pair => pair.Entry.Title, StringComparer.Ordinal)
            .Select(pair => pair.Entry)
            .ToImmutableArray();
    }

    /// <summary>
    /// The entry's score, or -1 when a word is missing. A word in the title is worth four, one in the section two, one in the body
    /// one: asking for "journal" finds the key in the title rather than the hint that happens to mention it in passing.
    /// </summary>
    private static int Score(GuideEntry entry, string[] tokens)
    {
        int score = 0;
        foreach (string token in tokens)
        {
            int found = 0;
            if (entry.Title.Contains(token, StringComparison.OrdinalIgnoreCase))
                found += 4;
            if (entry.Section.Contains(token, StringComparison.OrdinalIgnoreCase))
                found += 2;
            if (entry.Body.Contains(token, StringComparison.OrdinalIgnoreCase))
                found += 1;
            if (found == 0)
                return -1;
            score += found;
        }
        return score;
    }

    /// <summary>
    /// A hint as a guide entry: its category heads the section, its words are the title once the key tokens are spelled out, and the
    /// moment it waits for is the body, so searching "pack" finds the pack hint and searching "prompt" finds where it comes from.
    /// </summary>
    public static GuideEntry Of(HintDefinition hint, Func<string, string> key) => new(
        "Guidance", HintRules.Words(hint.Text, key), $"{hint.Category}: waits until {hint.Trigger.Replace('_', ' ')}");
}
