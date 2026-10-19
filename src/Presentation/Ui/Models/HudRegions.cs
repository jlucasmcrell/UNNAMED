// UNNAMED Presentation - the HUD's named regions: each reserves its space, so a panel never draws over another (HUD overlap fixes)
// Godot-free so a test can hold them

namespace UNNAMED.Presentation.Ui;

/// <summary>A rectangle of the interface canvas, in interface pixels.</summary>
public readonly record struct Region(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;

    public bool Intersects(Region other) => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}

/// <summary>
/// Where the HUD's crowded parts stand: the status at the top left, the compass, tracker and notices column at the top right, the action
/// dock at the bottom centre, the subtitle band just above it, and the chat feed and combat log at either side of the dock. The dock keeps
/// its size and the band comes next; the status and the right column take what is left above them, and the side panels what is left beside
/// the dock, so their contents are clipped to a region, never drawn over a neighbour. Sizes are wanted sizes: a region is never larger than
/// the room for it, and may be empty.
/// </summary>
public readonly record struct HudRegions(Region TopLeft, Region TopRight, Region Dock, Region Subtitles, Region BottomLeft, Region BottomRight)
{
    /// <summary>The distance kept from every screen edge.</summary>
    public const float Margin = 28;

    /// <summary>The gap kept between two regions.</summary>
    public const float Gap = 8;

    /// <summary>Where the compass starts at the top right; the tracker column is under it.</summary>
    public const float RightTop = 22;

    /// <summary>The interaction prompt ends above both captions and the action dock.</summary>
    public float PromptBottom => Math.Min(Dock.Y, Subtitles.Y) - Gap;

    private const float LeftTop = 16;

    /// <summary>
    /// The regions on a canvas of <paramref name="width"/> by <paramref name="height"/>. <paramref name="sideBars"/> is the width of the further
    /// bars when they stand at the right edge (0 when they do not): the right column and the band keep clear of them.
    /// </summary>
    public static HudRegions Of(float width, float height, (float Width, float Height) dock, float bandHeight, (float Width, float Height) topLeft, (float Width, float Height) topRight, float sideBars = 0)
    {
        float side = sideBars > 0 ? sideBars + Gap : 0;
        float dockWidth = Math.Clamp(dock.Width, 0, Math.Max(0, width - 2 * Margin));
        float dockHeight = Math.Clamp(dock.Height, 0, Math.Max(0, height - 2 * Margin));
        var dockRegion = new Region((width - dockWidth) / 2, height - Margin - dockHeight, dockWidth, dockHeight);
        float floor = dockRegion.Y - Gap;

        // The band: a third of the screen at most, and never above the top of the screen.
        float bandWidth = Math.Clamp(Math.Min(width * 0.7f, 1100f), 0, Math.Max(0, width - 2 * (Margin + side)));
        float band = Math.Clamp(bandHeight, 0, Math.Max(0, Math.Min(height / 3, floor - RightTop)));
        var subtitles = new Region((width - bandWidth) / 2, floor - band, bandWidth, band);

        float topFloor = band > 0 ? subtitles.Y - Gap : floor;
        float rightWidth = Math.Clamp(topRight.Width, 0, width * 0.4f);
        var right = new Region(width - Margin - side - rightWidth, RightTop, rightWidth, Math.Clamp(topRight.Height, 0, Math.Max(0, topFloor - RightTop)));
        float leftWidth = Math.Clamp(topLeft.Width, 0, Math.Max(0, width - 2 * Margin - side - rightWidth - Gap));
        var left = new Region(Margin, LeftTop, leftWidth, Math.Clamp(topLeft.Height, 0, Math.Max(0, topFloor - LeftTop)));
        var bottomLeft = new Region(Margin, dockRegion.Y, Math.Max(0, dockRegion.X - Gap - Margin), dockRegion.Height);
        var bottomRight = new Region(dockRegion.Right + Gap, dockRegion.Y, Math.Max(0, width - Margin - dockRegion.Right - Gap), dockRegion.Height);
        return new HudRegions(left, right, dockRegion, subtitles, bottomLeft, bottomRight);
    }
}
