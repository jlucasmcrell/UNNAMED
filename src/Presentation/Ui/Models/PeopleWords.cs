// UNNAMED Presentation - a people's traits in the creator's and the character sheet's words (RACE_ABILITIES_PROPOSAL.md §1-§2)
// Godot-free, so the tests compile it as the game does: it reads the content's traits and words them, nothing more

using UNNAMED.Domain.Character;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Presentation.Ui.Models;

/// <summary>
/// What a people's biology and aptitude come to, one short line each, only where they differ from the usual: the creator's card and the
/// character sheet show the same words. Never the Orenth toll - that stays hidden.
/// </summary>
public static class PeopleWords
{
    public static IEnumerable<(string Caption, string Words)> Traits(PeopleTraits traits)
    {
        if (traits.AttributeGrant.Count > 0 || traits.FreeAttributePoints > 0)
            yield return ("Born with", Grant(traits));
        if (traits.HealthPercent != 100)
            yield return ("Health", Percent(traits.HealthPercent));
        foreach (var (taken, types) in traits.DamageTakenPercent.GroupBy(d => d.Value).Select(g => (g.Key, g.Select(d => d.Key).ToList())))
            yield return (Damage(types), taken == 0 ? "immune" : $"takes {taken}%");
        if (traits.SpeedPercent != 100)
            yield return ("Pace", Percent(traits.SpeedPercent));
        if (traits.CarryGrams != 0)
            yield return ("Carries", $"{(traits.CarryGrams > 0 ? "+" : "")}{traits.CarryGrams / 1000} kg");
        if (traits.ResonancePercent != 100)
            yield return ("Magic", traits.ResonancePercent == 0 ? "none, ever: no Resonance to work with" : $"Resonance {Percent(traits.ResonancePercent)}");
        if (traits.StaggerThresholdPercent != 100)
            yield return ("Footing", traits.StaggerThresholdPercent > 100 ? "hard to stagger" : "easily staggered");
        if (!traits.Regenerates)
            yield return ("Mending", "does not heal on its own: repaired");
        if (traits.NoisePercent != 100)
            yield return ("Noise", traits.NoisePercent > 100 ? $"loud ({traits.NoisePercent}%)" : $"quiet ({traits.NoisePercent}%)");
        if (Learning(traits.Learning) is { } learning)
            yield return ("Learns", learning);
    }

    private static string Grant(PeopleTraits traits)
    {
        var parts = traits.AttributeGrant.Select(g => $"{(g.Value > 0 ? "+" : "")}{g.Value} {Capital(ProgressionKeys.Key(g.Key))}").ToList();
        if (traits.FreeAttributePoints > 0)
            parts.Add($"+{traits.FreeAttributePoints} to any attribute, your choice");
        return string.Join(", ", parts);
    }

    /// <summary>How the people learns: its pace by skill band, its ceilings, and where it leans.</summary>
    private static string? Learning(LearningAptitude learning)
    {
        var parts = new List<string>();
        if (!learning.RateBands.IsEmpty)
            parts.Add(string.Join(", ", learning.RateBands.Select((b, i) =>
                $"{(i + 1 < learning.RateBands.Length ? $"{b.From}-{learning.RateBands[i + 1].From - 1}" : $"{b.From}+")} at {b.Percent}%")));
        if (learning.CommonCeiling is { } common)
            parts.Add($"skills reach {common} before mastery");
        if (learning.MasteryCeiling is { } mastery && mastery < 100)
            parts.Add($"mastery stops at {mastery}");
        if (learning.NoveltyPercent != 100)
            parts.Add($"first successes teach {learning.NoveltyPercent}%");
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private static string Damage(List<string> types) =>
        types.Count == 3 && types.All(t => t.StartsWith("physical_", StringComparison.Ordinal)) ? "Physical blows" : string.Join(", ", types.Select(Capital)) + " damage";

    private static string Percent(int percent) => $"{(percent >= 100 ? "+" : "")}{percent - 100}%";

    private static string Capital(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
}
