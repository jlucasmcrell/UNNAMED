// UNNAMED Presentation - the awareness indicator (STEALTH_DETECTION_AND_THREAT.md §20; STEALTH.md §2.7)
// Godot presentation only: it draws the awareness creatures already carry; no simulation state lives here.

using Godot;
using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// What one nearby creature has noticed, as the indicator shows it: the real awareness number, the scale it runs on, what told it (an eye saw
/// the character, an ear heard something, a nose scented it), and the bearing to the place it thinks they are - which for a noise is the
/// noise's own place, not the body. Presentation only; every number comes from the creature view.
/// </summary>
public readonly record struct AwarenessCue(string Name, int Awareness, int HeardNoise, int Suspicious, int Full, bool Engaged,
    NoticeSense Sense = NoticeSense.Eye, float BearingDegrees = 0, bool HasDirection = false);

/// <summary>
/// §20's design rule made visible: each nearby body is an arc that fills with the real number and empties as it decays, with what told it and
/// which way it is looking. There is deliberately no open/closed eye and no "hidden" light - those would be the hidden flag the doc forbids.
/// A player reads how close each body is to noticing them, why, and in which direction to move out of it.
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
        CustomMinimumSize = new Vector2(320, Rows * RowHeight);
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
            // What told it: an eye saw the character, an ear heard something, a nose scented it. A glyph, not a colour, because the glyph is the
            // part a player can read at a glance in the middle of a fight.
            DrawSense(centre + new Vector2(0, -Radius - 8), cue.Sense, colour);
            // Which way it is looking: a wedge on the rim, at the bearing to the place it thinks the character is.
            if (cue.HasDirection)
                DrawWedge(centre, cue.BearingDegrees, colour);
            DrawString(font, new Vector2(2 * Radius + 26, row * RowHeight + 18), cue.Name, HorizontalAlignment.Left, -1, 13, Words);
            DrawString(font, new Vector2(2 * Radius + 26 + 200, row * RowHeight + 18), cue.Awareness.ToString(),
                HorizontalAlignment.Right, 60, 12, colour);
        }
    }

    /// <summary>An eye (a lens on a line), an ear (a hook and a bowl), or three motes for a scent: tiny, but each is its own silhouette.</summary>
    private void DrawSense(Vector2 at, NoticeSense sense, Color colour)
    {
        switch (sense)
        {
            case NoticeSense.Ear:
                DrawArc(at, 3.5f, Mathf.Pi * 0.15f, Mathf.Pi * 1.15f, 10, colour, 1.2f, true);
                DrawLine(at + new Vector2(0, 3.5f), at + new Vector2(0, 6.5f), colour, 1.2f, true);
                break;
            case NoticeSense.Nose:
                foreach (float dy in new[] { -3f, 0f, 3f })
                    DrawCircle(at + new Vector2(0, dy), 1f, colour);
                break;
            default:
                DrawArc(at, 3.5f, Mathf.Pi, Mathf.Tau, 10, colour, 1.2f, true);
                DrawArc(at, 3.5f, 0, Mathf.Pi, 10, colour, 1.2f, true);
                DrawCircle(at, 1.1f, colour);
                break;
        }
    }

    /// <summary>A three-pixel arrow on the rim pointing at the noticed place: direction, never an exact cone (the WEB rule is kept).</summary>
    private void DrawWedge(Vector2 centre, float bearingDegrees, Color colour)
    {
        // The world's bearings run clockwise from +Z; on screen +Z is away from the camera, so north is drawn upward.
        float angle = Mathf.DegToRad(bearingDegrees);
        var direction = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
        var tip = centre + direction * (Radius + 5);
        var side = new Vector2(-direction.Y, direction.X) * 2.5f;
        DrawColoredPolygon(new[] { tip, centre + direction * (Radius + 0.5f) + side, centre + direction * (Radius + 0.5f) - side }, colour);
    }
}
