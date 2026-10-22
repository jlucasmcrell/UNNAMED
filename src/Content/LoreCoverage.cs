// UNNAMED Content - the lore coverage report: what canon holds, and how much of it the player can read (spec 41 pass 2 Â§1, Â§6)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Text;
using UNNAMED.Domain.Knowledge;
using YamlDotNet.Serialization;

namespace UNNAMED.Content;

/// <summary>One concept canon names, and whether a lore entry carries it.</summary>
public sealed record CanonConcept(string Id, string Name, string Kind, string Status, ImmutableArray<string> CoveredBy, string Fact, string Source);

/// <summary>What canon holds against what the player can read: the counts, and every problem that stops the index being trusted.</summary>
public sealed record LoreCoverage(int Covered, int Partly, int Missing, int Gaps, ImmutableArray<string> Problems, ImmutableArray<CanonConcept> MissingConcepts)
{
    public int Total => Covered + Partly + Missing;

    /// <summary>A report a person reads: the counts by status and kind, the entries on hand, and what is still missing.</summary>
    public string Text(ImmutableSortedDictionary<string, LoreEntryDefinition> entries)
    {
        var text = new StringBuilder("Lore coverage");
        text.AppendLine().AppendLine("=============").AppendLine();
        text.AppendLine($"Entries: {entries.Count}, pages: {entries.Values.Sum(e => e.PageCount)}");
        text.AppendLine($"Canon concepts: {Total} - covered {Covered}, partly {Partly}, missing {Missing}; gaps for the owner: {Gaps}");
        foreach (var group in MissingConcepts.GroupBy(c => c.Kind).OrderBy(g => g.Key, StringComparer.Ordinal))
            text.AppendLine($"  missing {group.Key}: {group.Count()}");
        if (!Problems.IsEmpty)
        {
            text.AppendLine().AppendLine("Problems:");
            foreach (string problem in Problems)
                text.AppendLine("  " + problem);
        }
        return text.ToString();
    }
}

/// <summary>
/// The canon index (<c>content/lore/_canon_index.yaml</c>) read against the lore entries: every concept's status, and a hard check that the
/// index cannot lie - a status of covered must name an entry that exists, and every entry's id must be one the index knows.
/// </summary>
public static class LoreCoverageReport
{
    public const string IndexFile = "_canon_index.yaml";

    public static LoreCoverage Read(string contentRoot, ImmutableSortedDictionary<string, LoreEntryDefinition> entries)
    {
        var problems = ImmutableArray.CreateBuilder<string>();
        string path = Path.Combine(contentRoot, "lore", IndexFile);
        if (!System.IO.File.Exists(path))
            return new LoreCoverage(0, 0, 0, 0, problems.ToImmutable().Add($"{path} is missing"), ImmutableArray<CanonConcept>.Empty);
        var doc = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(System.IO.File.ReadAllText(path)) ?? new Dictionary<object, object>();
        var concepts = new List<CanonConcept>();
        if (doc.GetValueOrDefault("concepts") is List<object> rows)
        {
            foreach (var row in rows.OfType<Dictionary<object, object>>())
            {
                var concept = new CanonConcept(
                    Text(row, "id"), Text(row, "name"), Text(row, "kind"), Text(row, "status"),
                    (row.GetValueOrDefault("covered_by") is List<object> covered
                        ? covered.OfType<string>().ToImmutableArray()
                        : ImmutableArray<string>.Empty),
                    Text(row, "fact"), Text(row, "source"));
                if (concept.Status is not ("covered" or "partly" or "missing"))
                    problems.Add($"{concept.Id} has status '{concept.Status}', which is not covered, partly or missing");
                if (concept.Status is "covered" or "partly")
                    foreach (string entry in concept.CoveredBy)
                        if (!entries.ContainsKey(entry))
                            problems.Add($"{concept.Id} says {entry} covers it, and no such lore entry exists");
                if (concept.Status is "covered" or "partly" && concept.CoveredBy.IsEmpty)
                    problems.Add($"{concept.Id} is {concept.Status} and names no entry");
                if (concept.Status == "missing" && !concept.CoveredBy.IsEmpty)
                    problems.Add($"{concept.Id} is missing and still names {string.Join(", ", concept.CoveredBy)}");
                concepts.Add(concept);
            }
        }
        else
        {
            problems.Add($"{IndexFile} has no 'concepts' list");
        }
        foreach (string id in concepts.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => g.Key))
            problems.Add($"{id} is in the index twice");
        foreach (string entry in entries.Keys.Where(id => concepts.All(c => !c.CoveredBy.Contains(id))))
            problems.Add($"{entry} is a lore entry no concept in the index covers - add it to the concept it answers for");
        int gaps = doc.GetValueOrDefault("gaps") is List<object> holes ? holes.Count : 0;
        return new LoreCoverage(
            concepts.Count(c => c.Status == "covered"),
            concepts.Count(c => c.Status == "partly"),
            concepts.Count(c => c.Status == "missing"),
            gaps,
            problems.ToImmutable(),
            concepts.Where(c => c.Status == "missing").ToImmutableArray());
    }

    private static string Text(Dictionary<object, object> map, string key) =>
        map.GetValueOrDefault(key) is string value ? value : "";
}
