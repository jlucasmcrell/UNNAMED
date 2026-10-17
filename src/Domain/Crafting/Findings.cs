// UNNAMED Domain - Findings: what an experiment at a station taught, kept so the player can read it again (UI_PROGRESSION_AUDIT item F;
// canon 12 §5.3 "one attempt, one Finding", canon 26 W7 "Findings (what failing taught you)")
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Crafting;

/// <summary>What an experiment came to: it found a recipe, it hinted at one (what is carried is part of the way there), or nothing came of it.</summary>
public enum FindingKind { Found, Hint, Nothing }

/// <summary>
/// One Finding (schema 31). Its <see cref="Key"/> is the one question it closes: a station, an outcome and what it was about, so the same
/// attempt is never recorded twice and cannot be farmed. <see cref="RecipeId"/> is the recipe found, or the recipe the attempt hinted at
/// (null when nothing came of it); <see cref="Materials"/> are the carried items that bore on it, sorted. Its words are the screen's: the
/// record holds what happened, never a sentence.
/// </summary>
public sealed record Finding(FindingKind Kind, string StationKey, string StationKind, string? RecipeId, ImmutableArray<string> Materials, long Tick)
{
    /// <summary>The question this Finding closes (see <see cref="FindingKeys.Of"/>): never stored, always derived.</summary>
    public string Key => FindingKeys.Of(StationKey, Kind, RecipeId);
}

public static class FindingKeys
{
    public static string Key(FindingKind kind) => kind switch
    {
        FindingKind.Found => "found",
        FindingKind.Hint => "hint",
        FindingKind.Nothing => "nothing",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static FindingKind? Parse(string key) => key switch
    {
        "found" => FindingKind.Found,
        "hint" => FindingKind.Hint,
        "nothing" => FindingKind.Nothing,
        _ => null,
    };

    /// <summary>The question a Finding closes: the station, what came of it, and the recipe it was about.</summary>
    public static string Of(string stationKey, FindingKind kind, string? recipeId) => $"{stationKey}|{Key(kind)}|{recipeId ?? "-"}";
}

/// <summary>An experiment's outcome before it is recorded.</summary>
public sealed record ExperimentOutcome(FindingKind Kind, RecipeDefinition? Recipe, ImmutableArray<string> Materials);

public static class FindingRules
{
    /// <summary>
    /// What trying what is carried at a station comes to, against the recipes its set still hides. A recipe whose inputs are all carried is
    /// found; failing that, a recipe some of whose inputs are carried is a hint (the Finding says there is more to it and names nothing it
    /// would reveal beyond what the player already carries); failing that, nothing came of it. <paramref name="fresh"/> is in the set's order.
    /// </summary>
    public static ExperimentOutcome Assess(IReadOnlyList<RecipeDefinition> fresh, IReadOnlyDictionary<string, int> carried)
    {
        int Have(string item) => carried.GetValueOrDefault(item);
        if (fresh.FirstOrDefault(r => r.Inputs.All(i => Have(i.ItemId) >= i.Count)) is { } found)
            return new ExperimentOutcome(FindingKind.Found, found, Materials(found, Have));
        if (fresh.FirstOrDefault(r => r.Inputs.Any(i => Have(i.ItemId) > 0)) is { } near)
            return new ExperimentOutcome(FindingKind.Hint, near, Materials(near, Have));
        return new ExperimentOutcome(FindingKind.Nothing, null, ImmutableArray<string>.Empty);
    }

    private static ImmutableArray<string> Materials(RecipeDefinition recipe, Func<string, int> have) =>
        recipe.Inputs.Where(i => have(i.ItemId) > 0).Select(i => i.ItemId).Distinct(StringComparer.Ordinal).OrderBy(i => i, StringComparer.Ordinal).ToImmutableArray();
}
