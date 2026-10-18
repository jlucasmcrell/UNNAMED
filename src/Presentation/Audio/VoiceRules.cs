// UNNAMED Presentation - voice-over and subtitle rules: where a line's voice lies, when its words show, how a bark fades (WP5, E-5, E-10)
// Pure: no Godot, so the rules are tested directly

namespace UNNAMED.Presentation.Audio;

/// <summary>
/// The voice's rules as functions. A line's voice-over is optional: <c>vo/&lt;line-id&gt;.ogg</c> under the asset workspace, played when it
/// exists. Words always show for a line without a voice (the line would otherwise be lost); a voiced line shows them while subtitles are
/// on. Music ducks under a voice. A bark said in passing fades in, fades as the character walks off, and fades out at its end.
/// </summary>
public static class VoiceRules
{
    public const string Folder = "vo";

    /// <summary>How far the music drops under a voice, in dB, and how fast it moves there and back (dB a second).</summary>
    public const float DuckDb = -9f;
    public const float DuckRiseDbPerSecond = 30f;
    public const float DuckFallDbPerSecond = 12f;

    /// <summary>Within this many metres a bark is read at full strength; at <see cref="BarkGoneM"/> it is gone.</summary>
    public const float BarkFullM = 5f;
    public const float BarkGoneM = 11f;

    /// <summary>How long a bark takes to appear and to go, in seconds.</summary>
    public const double BarkEdgeSeconds = 0.35;

    /// <summary>Where a line's voice-over would lie, or null without an asset workspace or for an ID that could leave the voice folder.</summary>
    public static string? VoicePath(string? assetRoot, string lineId)
    {
        if (assetRoot is null || lineId.Length == 0 || lineId.Contains("..", StringComparison.Ordinal) || lineId.Contains('\\')
            || lineId.StartsWith('/') || lineId.Any(char.IsWhiteSpace))
            return null;
        return Path.Combine(assetRoot, Folder, lineId.Replace('/', Path.DirectorySeparatorChar) + ".ogg");
    }

    /// <summary>Whether a line's words show: always without a voice, and with one while subtitles are on.</summary>
    public static bool ShowWords(bool voiced, bool subtitles) => !voiced || subtitles;

    /// <summary>
    /// Whether a bark's words stand over the speaker's head. While subtitles are on the captions carry them (with the speaker's name, so a
    /// line from behind the camera is read too); with subtitles off, a line without a voice still shows overhead - it would otherwise be lost.
    /// </summary>
    public static bool ShowOverhead(bool voiced, bool subtitles) => !voiced && !subtitles;

    /// <summary>The music's duck after <paramref name="seconds"/> more: towards <see cref="DuckDb"/> while a voice speaks, back to 0 after.</summary>
    public static float Duck(float currentDb, bool speaking, double seconds)
    {
        float target = speaking ? DuckDb : 0f;
        float step = (float)seconds * (speaking ? DuckRiseDbPerSecond : DuckFallDbPerSecond);
        return currentDb > target ? Math.Max(target, currentDb - step) : Math.Min(target, currentDb + step);
    }

    /// <summary>
    /// How strongly a bark shows (0..1): by the character's distance from the speaker, and by its own time - fading in over its first
    /// moments and out over its last.
    /// </summary>
    public static float BarkAlpha(float distanceM, double shownSeconds, double leftSeconds)
    {
        if (leftSeconds <= 0)
            return 0f;
        float near = Math.Clamp((BarkGoneM - distanceM) / (BarkGoneM - BarkFullM), 0f, 1f);
        double edge = Math.Min(Math.Clamp(shownSeconds / BarkEdgeSeconds, 0, 1), Math.Clamp(leftSeconds / BarkEdgeSeconds, 0, 1));
        return near * (float)edge;
    }
}
