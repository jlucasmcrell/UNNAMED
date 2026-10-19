// UNNAMED Presentation - subtitles: the lines on screen at the foot, each with who says it (accessibility floor; canon 26)
// Pure: no Godot, so the rules are tested directly

namespace UNNAMED.Presentation.Ui.Models;

/// <summary>One caption: who says it, what, and until when (seconds on the model's own clock).</summary>
public sealed record Caption(string Speaker, string Text, double Until);

/// <summary>
/// The captions on screen. A spoken line, a bark said in passing and ambient chatter all arrive through <see cref="Say"/>; the newest
/// <see cref="MaxLines"/> that have not run out show, oldest first; a speaker repeating the same words is one caption, kept alive. Nothing
/// here knows the player's settings: whether captions show at all is the view's, from <c>Interface.Subtitles</c>.
/// </summary>
public sealed class SubtitleModel
{
    public const int MaxLines = 3;

    /// <summary>A caption stays at least this long however short the line, so it can be read.</summary>
    public const double MinSeconds = 2.5;

    /// <summary>How many characters a second a reader takes in, for how long a line holds.</summary>
    public const double CharsPerSecond = 17;

    private readonly List<Caption> _lines = new();

    /// <summary>How long a line of <paramref name="text"/> is held: its reading time, never under <see cref="MinSeconds"/>, and never under the time it is spoken for.</summary>
    public static double Hold(string text, double spokenSeconds = 0) => Math.Max(Math.Max(MinSeconds, text.Length / CharsPerSecond), spokenSeconds);

    public void Say(string speaker, string text, double now, double spokenSeconds = 0)
    {
        text = text.Trim();
        if (text.Length == 0)
            return;
        speaker = speaker.Trim();
        double until = now + Hold(text, spokenSeconds);
        int same = _lines.FindIndex(l => l.Speaker == speaker && l.Text == text);
        if (same >= 0)
            _lines[same] = _lines[same] with { Until = until };
        else
            _lines.Add(new Caption(speaker, text, until));
    }

    /// <summary>The captions to show at <paramref name="now"/>: those not run out, the newest <see cref="MaxLines"/>, oldest first.</summary>
    public IReadOnlyList<Caption> Lines(double now)
    {
        _lines.RemoveAll(l => l.Until <= now);
        while (_lines.Count > MaxLines)
            _lines.RemoveAt(0);
        return _lines.ToArray();
    }

    public void Clear() => _lines.Clear();

    /// <summary>
    /// The words of one caption: the speaker's name in capitals, a colon, then the line. A speaker is always named - never only a colour - so
    /// who is talking does not depend on seeing it.
    /// </summary>
    public static string Words(Caption caption) => caption.Speaker.Length == 0 ? caption.Text : $"{caption.Speaker.ToUpperInvariant()}: {caption.Text}";

    /// <summary>
    /// The font size a caption is set at: the base size times the player's text size and the subtitle size, but never so large that the
    /// <see cref="MaxLines"/> lines, each wrapped to two, could stand taller than a third of the screen (<paramref name="screenHeight"/>, in interface pixels) - at an
    /// enlarged interface the screen is short, and captions that cover it are no captions. Never under 16.
    /// </summary>
    public static int FontSize(int baseSize, double textSize, double subtitleSize, double screenHeight = 1080)
    {
        int wanted = (int)Math.Round(baseSize * Math.Max(1, textSize) * subtitleSize);
        int room = (int)(screenHeight / 3 / (MaxLines * 2 * 1.3));   // each of the lines may wrap to two
        return Math.Max(16, Math.Min(wanted, room));
    }

    /// <summary>The band the captions stand in, in interface pixels: <see cref="MaxLines"/> captions of one line at <paramref name="fontSize"/>, with their padding and gaps. A longer caption wraps and the oldest line scrolls off the top.</summary>
    public static float BandHeight(int fontSize) => MaxLines * (fontSize * 1.3f + 8) + (MaxLines - 1) * 4;
}
