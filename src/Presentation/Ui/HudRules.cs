// UNNAMED Presentation - what the HUD's player options decide: how an enemy's condition reads, and what a blow's number says (WP-1E)
// Godot-free: Presentation.Tests links this file and tests the rules on their own (D-11)

using UNNAMED.Presentation.Settings;

namespace UNNAMED.Presentation.Ui;

/// <summary>An enemy's condition as the plate shows it: a word (or none), whether the bar is drawn, and the numbers (or none).</summary>
public readonly record struct ConditionRead(string? Word, bool Bar, string? Numbers);

public static class HudRules
{
    /// <summary>
    /// How hurt something is, in a word, from its health: unhurt, grazed (more than three quarters left), wounded (more than two fifths),
    /// badly wounded (more than a sixth), near death, and down at none.
    /// </summary>
    public static string ConditionWord(int health, int maxHealth)
    {
        int max = Math.Max(1, maxHealth);
        if (health <= 0)
            return "Down";
        if (health >= max)
            return "Unhurt";
        double share = (double)health / max;
        return share > 0.75 ? "Grazed" : share > 0.4 ? "Wounded" : share > 1.0 / 6 ? "Badly wounded" : "Near death";
    }

    /// <summary>
    /// What the plate shows of a creature's condition under the player's choice. Before the fight is on nothing is shown (finding 24: at full
    /// health a bar tells the player nothing); once it is, Words gives the word alone, Words and bar adds the bar, Numbers the bar and its
    /// health in figures.
    /// </summary>
    public static ConditionRead Condition(EnemyCondition mode, int health, int maxHealth, bool fightOn)
    {
        if (!fightOn)
            return new ConditionRead(null, false, null);
        string word = ConditionWord(health, maxHealth);
        return mode switch
        {
            EnemyCondition.Words => new ConditionRead(word, false, null),
            EnemyCondition.Numbers => new ConditionRead(null, true, $"{Math.Max(0, health)} / {Math.Max(1, maxHealth)}"),
            _ => new ConditionRead(word, true, null),
        };
    }

    /// <summary>
    /// What a blow's number says over the plate, or null when none is shown: only with the option on, only for a blow the character's side
    /// struck, and a dodged blow says so rather than showing a nought; a critical one is marked.
    /// </summary>
    public static string? DamageNumber(HudPrefs prefs, bool struckByOurSide, int damage, bool critical, bool dodged, bool blocked)
    {
        if (!prefs.DamageNumbers || !struckByOurSide)
            return null;
        if (dodged)
            return "Dodged";
        string number = $"{Math.Max(0, damage)}";
        return critical ? number + "!" : blocked ? number + " (blocked)" : number;
    }
}
