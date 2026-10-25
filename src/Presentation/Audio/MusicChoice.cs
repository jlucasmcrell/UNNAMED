// UNNAMED Presentation - which music cue the moment calls for (AUDIO_DESIGN.md, "Where music stops")
// Pure: no Godot, so the rule is tested directly

namespace UNNAMED.Presentation.Audio;

/// <summary>
/// The score's rule as one function. The start screen has the title theme, and so does the road in until it fades (owner ruling
/// 2026-10-24). Fighting has the combat cue, wherever it happens; something that has noticed the character, before the fight or searching
/// after it, has the tension layer. A settlement has its theme, by day or by night. A place already walked, on a later visit, has the
/// exploration cue now and then; a first visit is scored by ambience alone, and arriving somewhere new is marked by a stinger, never by a bed.
/// </summary>
public static class MusicChoice
{
    public const string Title = "mus.title";
    public const string Combat = "mus.combat";
    public const string Tension = "mus.tension";
    public const string Explore = "mus.explore";
    public const string Discovery = "mus.stinger.discovery";

    /// <summary>The bed to play (a family of the sound set), or null for silence.</summary>
    public static string? Bed(bool title, bool fighting, bool tense, string? settlementTheme, bool night, bool road, bool exploring) =>
        title ? Title
        : fighting ? Combat
        : tense ? Tension
        : settlementTheme is not null ? $"{settlementTheme}.{(night ? "night" : "day")}"
        : road ? Title
        : exploring ? Explore
        : null;
}
