// UNNAMED Content - the tracking skill's own numbers and its technique catalogue (config.tracking; canon skill.tracking)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Creatures;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds the domain's <see cref="TrackingRules"/> from <c>config.tracking</c> and from the <c>ability</c> definitions of
/// <c>class: technique</c> that the tracking skill owns, and lints them as TRK errors. The needs a technique carries are canon's own
/// gate: a reader whose competences meet them has the technique, so no teacher has to exist for the skill to work.
/// </summary>
public static class TrackingContent
{
    /// <summary>The skill that owns these techniques, and the competence every one of them has to need.</summary>
    private const string Skill = "skill.tracking";

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        if (!loader.Definitions.ContainsKey("config.tracking"))
            return errors;
        try
        {
            Build(loader);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(new ValidationError { SeverityLevel = ValidationError.Severity.Error, Code = "TRK001", Message = $"tracking: {e.Message}" });
        }
        return errors;
    }

    /// <summary>The rules this content defines. Throws with the problem named when the config cannot be read.</summary>
    public static TrackingRules Build(ContentLoader loader)
    {
        var spoor = Map(Config(loader, "config.tracking"), "spoor");
        int tickMs = WorldContent.TickMilliseconds(loader);
        return new TrackingRules(
            FreshTicks: ToTicks(Number(spoor, "fresh_s"), tickMs),
            FadeTicks: ToTicks(Number(spoor, "fade_s"), tickMs),
            ScentTicks: ToTicks(Number(spoor, "scent_s"), tickMs),
            ScentConeMdeg: Int(spoor, "cone_mdeg"),
            MarkSpacingMm: Mm(spoor, "spacing_m"),
            Techniques: Techniques(loader)).Validated();
    }

    /// <summary>
    /// The tracking techniques, by ID: an <c>ability</c> of <c>class: technique</c> naming the tracking skill as its discipline. A
    /// technique of another discipline has no builder yet, and the lint says so rather than passing over it in silence.
    /// </summary>
    public static ImmutableSortedDictionary<string, TrackingTechnique> Techniques(ContentLoader loader)
    {
        var built = ImmutableSortedDictionary.CreateBuilder<string, TrackingTechnique>(StringComparer.Ordinal);
        foreach (var definition in loader.GetByKind("ability").Values.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            var map = Read(definition.YamlSource);
            if (map.GetValueOrDefault("class") as string != "technique")
                continue;
            if (map.GetValueOrDefault("discipline") as string != Skill)
                throw new FormatException($"{definition.Id}: no builder for {map.GetValueOrDefault("discipline") as string}'s techniques yet");
            built.Add(definition.Id, Technique(definition.Id, map));
        }
        return built.ToImmutable();
    }

    private static TrackingTechnique Technique(string id, Dictionary<object, object> map)
    {
        var needs = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        foreach (var (key, value) in Map(map, "needs"))
        {
            string competence = key as string ?? "";
            if (competence.Length == 0)
                throw new FormatException($"{id}: a need with no competence to name");
            needs[competence] = int.TryParse(value as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) && level > 0
                ? level
                : throw new FormatException($"{id}: the {competence} need is a level, 1 or more");
        }
        // The skill's own leg is what makes it a tracking technique: without it a reader with none of the skill would have it.
        if (!needs.ContainsKey(Skill["skill.".Length..]))
            throw new FormatException($"{id}: needs no tracking level, so a reader with none of the skill would have it");
        return new TrackingTechnique(id, Text(map, "name"), Text(map, "grade"), needs.ToImmutable());
    }
}
