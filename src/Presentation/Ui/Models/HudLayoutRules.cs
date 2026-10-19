// UNNAMED Presentation - the width rules that keep HUD text from wrapping a letter to a line (HUD text layout fixes)
// Godot-free so a test can hold them

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// A wrapping label in a container that can shrink collapses to zero width and sets one character per line. Every hint, toast, subtitle,
/// notification and reading line therefore has a floor: about <see cref="MinChars"/> characters of its own size.
/// </summary>
public static class HudLayoutRules
{
    /// <summary>The fewest characters of width a visible HUD label may be given.</summary>
    public const int MinChars = 12;

    /// <summary>The narrowest a label of <paramref name="designedSize"/> may be, in pixels (an average glyph is about half its size wide).</summary>
    public static float MinWidth(float designedSize) => MinChars * designedSize * 0.5f;

    /// <summary>A reading line's width: the viewport less <paramref name="margin"/>, at most <paramref name="max"/>, never under the floor for its size.</summary>
    public static float ReadingWidth(float viewportWidth, float margin, float max, float designedSize) =>
        Math.Clamp(viewportWidth - margin, MinWidth(designedSize), Math.Max(max, MinWidth(designedSize)));

    /// <summary>
    /// The span a floating line takes between its limits: the wanted one, unless it is narrower than <paramref name="minWidth"/> (the panels
    /// it keeps clear of leave no room), when the line keeps its full default span and overlaps rather than collapsing.
    /// </summary>
    public static (float Left, float Right) Span(float left, float right, float defaultLeft, float defaultRight, float minWidth) =>
        right - left < minWidth ? (defaultLeft, defaultRight) : (left, right);
}
