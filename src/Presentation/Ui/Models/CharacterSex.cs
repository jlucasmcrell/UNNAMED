namespace UNNAMED.Presentation.Ui.Models;

/// <summary>Authored named-person metadata constrains body and groom choice.</summary>
public static class CharacterSex
{
    private static string? Normal(string? sex) => sex switch { "f" or "feminine" or "female" => "f", "m" or "masculine" or "male" => "m", _ => sex };
    public static bool Matches(string? expected, string? actual) => expected is null || Normal(expected) == Normal(actual);
    public static bool AllowsGroom(string? sex, string name) => Normal(sex) != "f"
        || !new[] { "beard", "moustache", "mustache", "goatee" }.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));
}
