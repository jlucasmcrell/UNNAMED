// UNNAMED Presentation - the light gem and the noise ring (STEALTH.md §2.7)
// Godot presentation only: it draws the numbers the simulation already keeps, and decides nothing.

using Godot;
using UNNAMED.Domain.Combat;
using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The stealth read, in one place at the bottom of the screen: a gem whose fill and cut are the band the character is standing in (Dark, Dim,
/// Lit, Bright - four shapes, not a gradient, so it reads without colour), and beside it the noise ring, drawn at the actual radius the
/// creatures are hearing this tick. Neither is a hidden meter: the gem is the band the sight check uses and the ring is the footfall's own
/// radius, so the player sees what the watch sees.
/// </summary>
public partial class LightGem : Control
{
    private const float GemRadius = 13f;
    private const float RingRadius = 9f;
    private static readonly Color Track = new(1f, 1f, 1f, 0.12f);
    private static readonly Color Words = new(0.82f, 0.84f, 0.86f, 0.9f);

    /// <summary>The four bands, dark to bright: cool and dim, then warm and bright. The shape carries the same message.</summary>
    private static readonly Color[] BandColour =
    {
        new(0.30f, 0.38f, 0.52f, 0.95f),
        new(0.48f, 0.56f, 0.62f, 0.95f),
        new(0.86f, 0.78f, 0.52f, 0.95f),
        new(1f, 0.93f, 0.72f, 1f),
    };

    private LightGemModel _gem = LightGemModel.From(LightBand.Lit);
    private NoiseRingModel _noise = NoiseRingModel.From(0);
    private bool _crouched;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(190, 46);
        Size = CustomMinimumSize;
    }

    /// <summary>Hand it the band and the noise radius from the simulation; both are already the real quantities.</summary>
    public void Set(LightGemModel gem, NoiseRingModel noise, bool crouched)
    {
        if (_gem == gem && _noise == noise && _crouched == crouched)
            return;
        _gem = gem;
        _noise = noise;
        _crouched = crouched;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        int index = Array.IndexOf(LightGemModel.Bands, _gem.Band);
        var colour = BandColour[Math.Clamp(index, 0, BandColour.Length - 1)];
        var centre = new Vector2(GemRadius + 6, 23);
        DrawGem(centre, _gem.Shape, colour, _gem.Fill);
        DrawString(font, new Vector2(2 * GemRadius + 18, 20), _gem.Words, HorizontalAlignment.Left, -1, 13, colour);
        DrawString(font, new Vector2(2 * GemRadius + 18, 36), _crouched ? "Crouched" : _noise.Words, HorizontalAlignment.Left, -1, 11, Words);
        DrawNoiseRing(new Vector2(2 * GemRadius + 18 + 150, 23), _noise.RadiusMetres);
    }

    /// <summary>
    /// The gem itself: one outline per band - closed, half cut, open, radiant spikes - filled from the bottom by the band's weight. The four
    /// silhouettes are the accessibility floor: no one has to tell two shades of amber apart.
    /// </summary>
    private void DrawGem(Vector2 centre, string shape, Color colour, float fill)
    {
        var rim = new Color(colour, 0.85f);
        DrawCircle(centre, GemRadius, new Color(0.08f, 0.09f, 0.11f, 0.72f));
        // The fill rises inside the gem, so a brighter band is a fuller stone.
        float height = GemRadius * 2 * Math.Clamp(fill, 0f, 1f);
        DrawRect(new Rect2(centre.X - GemRadius, centre.Y + GemRadius - height, GemRadius * 2, height), new Color(colour, 0.55f));
        DrawArc(centre, GemRadius, 0, Mathf.Tau, 28, rim, 1.6f, true);
        switch (shape)
        {
            case "closed":
                // Shut: the eye of the stone is a single line, so "Shrouded" is a shape before it is a colour.
                DrawLine(centre + new Vector2(-6, 0), centre + new Vector2(6, 0), rim, 1.8f, true);
                break;
            case "half":
                DrawLine(centre + new Vector2(-6, 2), centre + new Vector2(6, -2), rim, 1.8f, true);
                break;
            case "open":
                DrawArc(centre, GemRadius - 5, Mathf.Pi, Mathf.Tau, 16, rim, 1.8f, true);
                break;
            default:
                // Radiant: four short rays, the only band that reaches past its own rim.
                foreach (var direction in new[] { Vector2.Up, Vector2.Down, Vector2.Left, Vector2.Right })
                    DrawLine(centre + direction * (GemRadius - 1), centre + direction * (GemRadius + 4), rim, 1.6f, true);
                break;
        }
    }

    /// <summary>
    /// The noise ring: a circle whose radius IS the noise radius, scaled so a sprint fits the tile. The player reads how big a circle they are
    /// drawing, which is the whole point of showing it - crouch-walking is a small one and sprinting is a large one, and gravel changes it.
    /// </summary>
    private void DrawNoiseRing(Vector2 centre, float metres)
    {
        // 16 m - the loudest footfall the build authors - is the full tile, so a 1.5 m crouch-walk is a visibly small mark.
        float scaled = Mathf.Clamp(metres / 16f, 0f, 1f) * RingRadius;
        DrawArc(centre, RingRadius, 0, Mathf.Tau, 24, Track, 1f, true);
        if (scaled > 0.4f)
            DrawArc(centre, scaled, 0, Mathf.Tau, 24, new Color(0.86f, 0.78f, 0.52f, 0.9f), 1.8f, true);
    }
}
