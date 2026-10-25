// UNNAMED Content - the World Event Director from content (WP 1H; QST-48, QST-55, WLD-89): config.director's events, cap, history and
// intensity
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.World.Runtime;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds the director's rules (<c>config.director</c>) and lints them (DIR001): every event has a unique ID, a scale this build knows,
/// odds in permille, a duration range of at least an hour, channels this build announces on, and at least one outcome with a positive
/// weight; the cap is between 1 and 8 (WLD-89).
/// </summary>
public static class DirectorContent
{
    /// <summary>WORLD_ARCHITECTURE.md RK-A9: no more than eight world events per region at once.</summary>
    public const int MostPerRegion = 8;

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        if (!loader.Definitions.ContainsKey("config.director"))
            return errors;
        try
        {
            Build(loader);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException or OverflowException)
        {
            errors.Add(new ValidationError
            {
                SeverityLevel = ValidationError.Severity.Error, Code = "DIR001", Message = $"config.director: {e.Message}",
                FilePath = loader.Definitions["config.director"].SourceFile,
            });
        }
        return errors;
    }

    /// <summary>The director's rules, or <see cref="DirectorSetup.Empty"/> for a pack without <c>config.director</c>.</summary>
    public static DirectorSetup Build(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey("config.director"))
            return DirectorSetup.Empty;
        var map = Config(loader, "config.director");
        var events = List(map, "events").Select(e => Event(e as Dictionary<object, object> ?? throw new FormatException("an event must be a map")))
            .ToImmutableArray();
        var duplicate = events.GroupBy(e => e.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new FormatException($"{duplicate.Key} is defined twice");
        var setup = new DirectorSetup(events, Int(map, "max_active_per_region"), Int(map, "history_kept"), Int(map, "intensity_window_h"),
            Int(map, "intensity_damping_permille"));
        if (setup.MaxActivePerRegion is < 1 or > MostPerRegion)
            throw new FormatException($"max_active_per_region {setup.MaxActivePerRegion} must be 1 to {MostPerRegion} (WLD-89)");
        if (setup.HistoryKept < 1 || setup.IntensityWindowHours < 0 || setup.IntensityDampingPermille is < 0 or > 1000)
            throw new FormatException("history_kept is at least 1, intensity_window_h is never negative, intensity_damping_permille is 0 to 1000");
        return setup;
    }

    private static WorldEventDefinition Event(Dictionary<object, object> map)
    {
        string id = Text(map, "id");
        if (!id.StartsWith("event.", StringComparison.Ordinal))
            throw new FormatException($"{id}: an event's ID starts with 'event.'");
        string scale = Text(map, "scale");
        if (!Enum.TryParse<WorldEventScale>(scale, ignoreCase: true, out var parsed) || !char.IsLower(scale[0]))
            throw new FormatException($"{id}: scale '{scale}' is not personal, local, regional, continental or cosmological");
        var hours = List(map, "hours");
        if (hours.Count != 2)
            throw new FormatException($"{id}: hours is [min, max]");
        int min = Whole(hours[0], $"{id} hours"), max = Whole(hours[1], $"{id} hours");
        var channels = List(map, "channels").Select(c => c as string ?? "").ToImmutableArray();
        var unknown = channels.FirstOrDefault(c => !WorldEventChannels.Known.Contains(c));
        if (unknown is not null)
            throw new FormatException($"{id}: channel '{unknown}' is not one of {string.Join(", ", WorldEventChannels.Known)}");
        var outcomes = List(map, "outcomes").Select(o => o as Dictionary<object, object> ?? throw new FormatException($"{id}: an outcome must be a map"))
            .Select(o => new WorldEventOutcome(Text(o, "key"), Int(o, "weight"))).ToImmutableArray();
        var regions = map.ContainsKey("regions") ? List(map, "regions").Select(r => r as string ?? "").ToImmutableArray() : ImmutableArray<string>.Empty;
        var definition = new WorldEventDefinition(id, parsed, regions, Int(map, "min_world_day"), Int(map, "chance_permille"), min, max,
            Int(map, "cooldown_h"), channels, outcomes);
        if (definition.ChancePermille is < 0 or > 1000 || min < 1 || max < min || definition.CooldownHours < 0 || definition.MinWorldDay < 0)
            throw new FormatException($"{id}: chance_permille is 0 to 1000, hours run from at least 1 up, cooldown_h and min_world_day are never negative");
        if (outcomes.IsEmpty || outcomes.Any(o => o.Weight <= 0) || outcomes.Select(o => o.Key).Distinct().Count() != outcomes.Length)
            throw new FormatException($"{id}: at least one outcome, each with a positive weight and its own key");
        return definition;
    }

    private static int Whole(object? value, string what) => value switch
    {
        int i => i,
        long l => checked((int)l),
        string s when int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed) => parsed,
        double d when d == Math.Floor(d) => checked((int)d),
        _ => throw new FormatException($"{what}: '{value}' is not a whole number"),
    };
}
