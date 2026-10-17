// UNNAMED Presentation - which disciplines the Skills list shows by default: those practised and those linked to something known, with an
// explicit "Show all disciplines" (UI_PROGRESSION_AUDIT item H; canon 26 W2, UI-8)
// Godot-free: Presentation.Tests links this file and tests the mapping on its own (D-11)

using UNNAMED.Domain.Progression;

namespace UNNAMED.Presentation.Ui;

public static class DisciplineRoster
{
    /// <summary>
    /// The disciplines to list. By default only the practised and the linked (a known formula, recipe or technique of that discipline);
    /// <paramref name="showAll"/> lists every one. <paramref name="keep"/> is a discipline that must stay listed (the one chosen), so a
    /// selection is never hidden under the row that holds it.
    /// </summary>
    public static IReadOnlyList<SkillEntry> Shown(IReadOnlyList<SkillEntry> roster, Func<string, bool> practised, IReadOnlySet<string> linked, bool showAll, string? keep = null) =>
        showAll ? roster : roster.Where(s => practised(s.Id) || linked.Contains(s.Id) || s.Id == keep).ToList();

    /// <summary>How many disciplines the default list leaves out.</summary>
    public static int Hidden(IReadOnlyList<SkillEntry> roster, IReadOnlyList<SkillEntry> shown) => roster.Count - shown.Count;

    /// <summary>The toggle's words: how many are out of sight, or the way back.</summary>
    public static string Toggle(bool showAll, int hidden) =>
        showAll ? "Show only what you practise" : hidden == 1 ? "Show all disciplines (1 more)" : $"Show all disciplines ({hidden} more)";
}
