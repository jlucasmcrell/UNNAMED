// UNNAMED Content - hints from content: the guidance lines and the moments they wait for (SYSTEMS_COMPLETENESS_PLAN_20261017.md §2.10 O1, WP10)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Guidance;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds the hints and lints what the reference pass cannot see (HNT001): each names a category and a trigger the game knows
/// (<see cref="HintTriggers.All"/>), a whole-number order, and text that fits one line, its only braces <c>{key:action}</c> tokens. Two
/// hints never share a trigger at the same order, so which shows first is the content's choice, not the ID's.
/// </summary>
public static class HintContent
{
    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        try
        {
            Build(loader);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(new ValidationError { SeverityLevel = ValidationError.Severity.Error, Code = "HNT001", Message = $"hints: {e.Message}" });
        }
        return errors;
    }

    /// <summary>The hints by ID; empty for a pack with none (test fixtures).</summary>
    public static ImmutableSortedDictionary<string, HintDefinition> Build(ContentLoader loader)
    {
        var hints = loader.GetByKind("hint").Values.Select(definition =>
        {
            string id = definition.Id;
            var map = Read(definition.YamlSource);
            string category = Text(map, "category");
            if (category.Length == 0)
                throw new FormatException($"{id} names no category");
            string trigger = Text(map, "trigger");
            if (!HintTriggers.All.Contains(trigger))
                throw new FormatException($"{id} waits on '{trigger}', which is not a moment the game names ({string.Join(", ", HintTriggers.All)})");
            if (!int.TryParse(map.GetValueOrDefault("order") as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out int order) || order < 0)
                throw new FormatException($"{id} order must be a whole number of 0 or more");
            string text = Text(map, "text").Trim();
            if (HintRules.Problem(text) is { } problem)
                throw new FormatException($"{id} {problem}");
            // Who the hint is for (note 3): none listed means everyone, and a listed people must be one the content defines.
            var people = map.GetValueOrDefault("people") is System.Collections.IEnumerable listed and not string
                ? listed.Cast<object>().Select(p => Convert.ToString(p, CultureInfo.InvariantCulture) ?? "").Where(p => p.Length > 0).ToImmutableArray()
                : ImmutableArray<string>.Empty;
            foreach (string person in people)
                if (!loader.Definitions.TryGetValue(person, out var defined) || defined.Kind != "people")
                    throw new FormatException($"{id} people: {person} is not a defined people");
            return new HintDefinition(id, category, trigger, text, order) { People = people };
        }).ToImmutableSortedDictionary(h => h.Id, h => h, StringComparer.Ordinal);
        foreach (var clash in hints.Values.GroupBy(h => (h.Trigger, h.Order)).Where(g => g.Count() > 1))
            throw new FormatException($"{string.Join(" and ", clash.Select(h => h.Id))} wait on '{clash.Key.Trigger}' at the same order {clash.Key.Order}");
        return hints;
    }
}
