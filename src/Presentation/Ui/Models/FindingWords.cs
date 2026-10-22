// UNNAMED Presentation - Findings in words: what an experiment at a station taught, as Skills > Recipes lists it
// (UI_PROGRESSION_AUDIT item F; canon 26 W7 "Findings (what failing taught you)")
// Godot-free: Presentation.Tests links this file and tests the mapping on its own (D-11)

using UNNAMED.Domain.Crafting;

namespace UNNAMED.Presentation.Ui;

/// <summary>One Finding as a line: what it says, and where it was learned.</summary>
public sealed record FindingLine(string Says, string Where);

public static class FindingWords
{
    /// <summary>
    /// A Finding in two lines. Found names the recipe; a hint names what was carried and says there is more to it, never what the more is;
    /// nothing says it came to nothing. The station is named by its kind ("the workbench"), as the screen names it everywhere.
    /// </summary>
    public static FindingLine Line(Finding finding, Func<string, string> name)
    {
        if (finding.StationKind == "salvage")
            return new($"Dismantling {name(finding.StationKey)} suggests {name(finding.RecipeId!)}", "A construction hint, recorded through Salvage.");
        string station = name(finding.StationKind).ToLowerInvariant();
        string materials = List(finding.Materials.Select(m => name(m).ToLowerInvariant()).ToList());
        return finding.Kind switch
        {
            FindingKind.Found => new($"{name(finding.RecipeId!)} worked out", $"At the {station}, working {materials}."),
            FindingKind.Hint => new($"There is more to {materials} at the {station}", "It hints at something, but it wants more than you carried."),
            _ => new($"Nothing came of it at the {station}", "What you carried suggested nothing new there."),
        };
    }

    /// <summary>The toast when a Finding is recorded: a found recipe is toasted as learned, so only the other two speak here.</summary>
    public static string? Toast(Finding finding, Func<string, string> name) => finding.Kind switch
    {
        _ when finding.StationKind == "salvage" => "Salvage revealed a construction hint: noted under Recipes",
        FindingKind.Found => null,
        FindingKind.Hint => $"There is more to this at the {name(finding.StationKind).ToLowerInvariant()}: noted under Recipes",
        _ => $"Nothing came of it at the {name(finding.StationKind).ToLowerInvariant()}: noted under Recipes",
    };

    private static string List(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "what you carried",
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };
}
