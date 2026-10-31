// UNNAMED Presentation - the violet edge of the Other's attention (STEALTH.md §2.7a)
// Godot presentation only: it draws what the simulation says has noticed the character, and decides nothing.

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// A violet rim round the screen when something that is not a person has found the character (the owner's ruling, STEALTH §2.7a): a hound of the ash,
/// a bone where a bone should not walk, the thing under the barrow. Magical detection has to read differently from a guard's eye at a glance, and the
/// one colour the rest of the HUD never uses is the cleanest way to say it. It is a border only - no vignette, no overlay - so the picture underneath
/// stays the picture.
/// </summary>
public partial class OtherEdge : Control
{
    /// <summary>Violet, the Other's own colour: deep enough to sit behind the HUD text and not to be mistaken for a status effect.</summary>
    private static readonly Color Other = new(0.46f, 0.28f, 0.72f, 0.85f);
    private static readonly Color OtherFaint = new(0.46f, 0.28f, 0.72f, 0.30f);

    private const float Width = 5f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Draw()
    {
        var size = Size;
        if (size.X <= 0 || size.Y <= 0)
            return;
        // Two passes: a soft inner line and a firm outer one, so the edge reads at any resolution without becoming a frame.
        DrawRect(new Rect2(Vector2.Zero, size), OtherFaint, false, Width + 4f);
        DrawRect(new Rect2(Vector2.Zero, size), Other, false, Width);
    }
}
