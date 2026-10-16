// UNNAMED Presentation - which music cue the moment calls for (AUDIO_DESIGN.md, "Where music stops")
// Pure: no Godot, so the rule is tested directly

namespace UNNAMED.Presentation.Audio;

/// <summary>
/// The score's rule as one function. The start screen has the title theme. Fighting has the combat cue, wherever it happens. A
/// settlement has its theme, by day or by night. Everywhere else is scored by ambience alone (no music while exploring); arriving
/// somewhere new is marked by a stinger, never by a bed.
/// </summary>
public static class MusicChoice
{
    public const string Title = "mus.title";
    public const string Combat = "mus.combat";
    public const string Discovery = "mus.stinger.discovery";

    /// <summary>The bed to play (a family of the sound set), or null for silence.</summary>
    public static string? Bed(bool title, bool fighting, string? settlementTheme, bool night) =>
        title ? Title
        : fighting ? Combat
        : settlementTheme is null ? null
        : $"{settlementTheme}.{(night ? "night" : "day")}";
}
