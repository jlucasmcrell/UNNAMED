// UNNAMED Presentation - the awareness indicator (STEALTH_DETECTION_AND_THREAT.md §20)
// Godot presentation only: it draws the awareness creatures already carry; no simulation state lives here.

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// What one nearby creature has noticed, as the indicator shows it: the real awareness number, the two thresholds it is measured against, and
/// the scale it runs on. Presentation only - the numbers come from the creature view.
/// </summary>
public readonly record struct AwarenessCue(string Name, int Awareness, int HeardNoise, int Suspicious, int Full, bool Engaged);

/// <summary>
/// §20's design rule made visible: the awareness of the nearest few creatures, each an arc that fills with the real number and empties as it
/// decays. There is deliberately no open/closed eye and no "hidden" light - those would be the hidden flag the doc forbids. A player reads how
/// close each body is to noticing them, and why: the tick at the second threshold is where it stops and looks.
/// </summary>
public partial class AwarenessMeter : Control
{
    /// <summary>How many creatures the indicator shows; more than three and it stops being readable at a glance.</summary>
    public const int Rows = 3;

    private const float RowHeight = 26, Radius = 10;
    private const int Ticks = 30;
    private static readonly Color Calm = new(0.62f, 0.68f, 0.75f, 0.55f);
    private static readonly Color Heard = new(0.55f, 0.72f, 0.88f, 0.9f);
    private static readonly Color Suspicious = new(0.93f, 0.76f, 0.28f, 0.95f);
    private static readonly Color Engaged = new(0.86f, 0.24f, 0.2f, 0.95f);
    private static readonly Color Track = new(1f, 1f, 1f, 0.13f);
    private static readonly Color Words = new(0.82f, 0.84f, 0.86f, 0.85f);

    private readonly List<AwarenessCue> _cues = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(300, Rows * RowHeight);
        Size = CustomMinimumSize;
    }

    /// <summary>The nearest creatures, nearest first. Fewer than <see cref="Rows"/> is fine: only what is near is shown.</summary>
    public void Set(IReadOnlyList<AwarenessCue> cues)
    {
        if (_cues.Count == cues.Count && _cues.Zip(cues).All(pair => pair.First == pair.Second))
            return;
        _cues.Clear();
        for (int i = 0; i < cues.Count && i < Rows; i++)
            _cues.Add(cues[i]);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        for (int row = 0; row < _cues.Count; row++)
        {
            var cue = _cues[row];
            float scale = Math.Max(1, cue.Full);
            float filled = Mathf.Clamp(cue.Awareness / scale, 0f, 1f);
            var centre = new Vector2(Radius + 6, row * RowHeight + RowHeight / 2);
            const float from = -135f, sweep = 270f;
            DrawArc(centre, Radius, Mathf.DegToRad(from), Mathf.DegToRad(from + sweep), Ticks, Track, 2f, true);
            var colour = cue.Engaged ? Engaged
                : cue.Awareness >= cue.Suspicious ? Suspicious
                : cue.Awareness >= cue.HeardNoise ? Heard
                : Calm;
            if (filled > 0)
                DrawArc(centre, Radius, Mathf.DegToRad(from), Mathf.DegToRad(from + sweep * filled), Ticks, colour, 2.5f, true);
            // The two thresholds, as ticks: at the first it has heard something, at the second it stops and looks.
            foreach (var (value, weight) in new[] { (cue.HeardNoise, 1.5f), (cue.Suspicious, 2.5f) })
            {
                float at = from + sweep * Mathf.Clamp(value / scale, 0f, 1f);
                var direction = new Vector2(Mathf.Cos(Mathf.DegToRad(at)), Mathf.Sin(Mathf.DegToRad(at)));
                DrawLine(centre + direction * (Radius - 2.5f), centre + direction * (Radius + 2.5f), new Color(1, 1, 1, 0.35f), weight, true);
            }
            DrawString(font, new Vector2(2 * Radius + 16, row * RowHeight + 18), cue.Name, HorizontalAlignment.Left, -1, 13, Words);
            DrawString(font, new Vector2(2 * Radius + 16 + 190, row * RowHeight + 18), cue.Awareness.ToString(),
                HorizontalAlignment.Right, 60, 12, colour);
        }
    }
}
