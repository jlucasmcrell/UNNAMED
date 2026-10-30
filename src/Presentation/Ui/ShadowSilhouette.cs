// UNNAMED Presentation - the stealth read: the character's own silhouette and the noise they are making (STEALTH.md §2.7a)
// Godot presentation only: it draws the numbers the simulation already keeps, and decides nothing.

using Godot;
using UNNAMED.Domain.Combat;
using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The stealth read (STEALTH §2.7a, the owner's ruling): the character's own shadow instead of a gem. It stands black in the dark and washes out as
/// the light finds them, four clear steps rather than a gradient, and it ripples with the noise they are making - so the thing on the HUD is the
/// thing the watch is looking at. Beside it the noise ring keeps its own drawn radius. In the minimal setting the silhouette and the ring are gone
/// and the player reads the sound itself and the bodies around them.
/// </summary>
public partial class ShadowSilhouette : Control
{
    private const float Tile = 44f;
    private const float RingRadius = 9f;
    private static readonly Color Track = new(1f, 1f, 1f, 0.12f);
    private static readonly Color Words = new(0.82f, 0.84f, 0.86f, 0.9f);

    /// <summary>The people's own hue, laid over the black: Kal stone, Siann chalk, Veth rust, Ashborn ash.</summary>
    private static readonly Color Stone = new(0.62f, 0.66f, 0.72f);
    private static readonly Color Chalk = new(0.86f, 0.86f, 0.80f);
    private static readonly Color Rust = new(0.66f, 0.48f, 0.40f);
    private static readonly Color Ash = new(0.55f, 0.55f, 0.58f);

    private ShadowModel _shadow = ShadowModel.From(LightBand.Lit);
    private NoiseRingModel _noise = NoiseRingModel.From(0);
    private StealthHudMode _mode = StealthHudMode.Full;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(190, 46);
        Size = CustomMinimumSize;
    }

    /// <summary>Hand it the band, the noise radius and the mode; all three are the real quantities.</summary>
    public void Set(ShadowModel shadow, NoiseRingModel noise, StealthHudMode mode)
    {
        if (_shadow == shadow && _noise == noise && _mode == mode)
            return;
        _shadow = shadow;
        _noise = noise;
        _mode = mode;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_mode == StealthHudMode.Minimal)
            return;
        var font = GetThemeDefaultFont();
        var centre = new Vector2(Tile / 2 + 4, 30);
        var tint = Tint(ShadowModel.Tint(_shadow.People));
        DrawRipples(centre);
        DrawBody(centre, tint);
        DrawString(font, new Vector2(Tile + 12, 20), _shadow.Words, HorizontalAlignment.Left, -1, 13, new Color(tint, 0.95f));
        DrawString(font, new Vector2(Tile + 12, 36), _noise.Words, HorizontalAlignment.Left, -1, 11, Words);
        DrawNoiseRing(new Vector2(Tile + 12 + 150, 23), _noise.RadiusMetres);
    }

    private static Color Tint(string token) => token switch
    {
        "chalk" => Chalk,
        "rust" => Rust,
        "ash" => Ash,
        _ => Stone,
    };

    /// <summary>
    /// The character's own body, drawn as one filled shape: a head, shoulders, a torso and two legs. Its ink is the band - solid black hidden, almost
    /// gone in the light - with the people's hue over it, so the figure is the same figure the player is steering.
    /// </summary>
    private void DrawBody(Vector2 centre, Color tint)
    {
        // Ink 1 is a black shape on a dark tile; ink 0 is a washed-out figure. The alpha and the lightening move together, so a bright band is a body
        // the light has found rather than a body that has disappeared.
        float ink = Mathf.Clamp(_shadow.Ink, 0.08f, 1f);
        var body = new Color(tint.Lerp(Colors.White, (1f - ink) * 0.7f), ink);

        // Head, then shoulders and torso, then the legs: a humanoid outline readable at forty-four pixels.
        DrawCircle(centre + new Vector2(0, -13), 5.4f, body);
        DrawColoredPolygon(new[]
        {
            centre + new Vector2(-8.5f, -7),
            centre + new Vector2(8.5f, -7),
            centre + new Vector2(6.2f, 7),
            centre + new Vector2(-6.2f, 7),
        }, body);
        DrawColoredPolygon(new[]
        {
            centre + new Vector2(-6.2f, 7),
            centre + new Vector2(-1.2f, 7),
            centre + new Vector2(-2.4f, 18),
            centre + new Vector2(-6.0f, 18),
        }, body);
        DrawColoredPolygon(new[]
        {
            centre + new Vector2(1.2f, 7),
            centre + new Vector2(6.2f, 7),
            centre + new Vector2(6.0f, 18),
            centre + new Vector2(2.4f, 18),
        }, body);

        // A washed-out figure keeps its edge, or it stops being readable in the bright bands at all.
        if (ink < 0.5f)
        {
            var edge = new Color(tint, 0.9f);
            DrawArc(centre + new Vector2(0, -13), 5.4f, 0, Mathf.Tau, 18, edge, 1.2f, true);
        }
    }

    /// <summary>
    /// The noise, as ripples leaving the body: one ripple per metre of radius, capped, so standing still is a still figure and a run is a body in rings.
    /// This is the "ripples with noise" half of the ruling, and it is drawn off the same radius the noise ring reads.
    /// </summary>
    private void DrawRipples(Vector2 centre)
    {
        if (_noise.RadiusMetres <= 0.05f)
            return;
        int rings = Mathf.Clamp((int)Mathf.Ceil(_noise.RadiusMetres / 3f), 1, 3);
        var colour = new Color(0.86f, 0.78f, 0.52f, 0.55f);
        for (int i = 1; i <= rings; i++)
            DrawArc(centre + new Vector2(0, 2), Tile / 2f + i * 4f, 0, Mathf.Tau, 28, new Color(colour, 0.42f / i), 1.2f, true);
    }

    /// <summary>
    /// The noise ring: a circle whose radius IS the noise radius, scaled so a sprint fits the tile. The player reads how big a circle they are
    /// drawing, which is the whole point of showing it - crouch-walking is a small one and sprinting is a large one, and gravel changes it.
    /// </summary>
    private void DrawNoiseRing(Vector2 centre, float metres)
    {
        float scaled = Mathf.Clamp(metres / 16f, 0f, 1f) * RingRadius;
        DrawArc(centre, RingRadius, 0, Mathf.Tau, 24, Track, 1f, true);
        if (scaled > 0.4f)
            DrawArc(centre, scaled, 0, Mathf.Tau, 24, new Color(0.86f, 0.78f, 0.52f, 0.9f), 1.8f, true);
    }
}
