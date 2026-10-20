using UNNAMED.Presentation.Ui;
using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Tests;

/// <summary>The HUD's regions (status, compass and tracker column, dock, subtitle band, chat and log beside the dock) reserve their space: none meets another at any interface size.</summary>
public class HudRegionsTests
{
    public static IEnumerable<object[]> Screens() =>
        from size in new[] { (1280, 720), (1920, 1080), (2560, 1440), (3840, 2160) }
        from scale in new[] { 0.75f, 1f, 1.5f, 2f }
        select new object[] { size.Item1, size.Item2, scale };

    private static HudRegions Place(int pixelsWide, int pixelsHigh, float scale, float textScale, bool crowded)
    {
        // The canvas is drawn for 1080 lines and scaled with the window, then by the interface size.
        float height = 1080 / scale;
        float width = height * pixelsWide / pixelsHigh;
        int font = SubtitleModel.FontSize(20, textScale, 2, height);
        var dock = crowded ? (1200f, 420f) : (700f, 210f);
        var left = crowded ? (900f, 400f) : (420f, 100f);
        var right = crowded ? (900f, 900f) : (380f, 420f);
        return HudRegions.Of(width, height, dock, SubtitleModel.BandHeight(font), left, right);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void NoRegionMeetsAnother(int pixelsWide, int pixelsHigh, float scale)
    {
        foreach (var (text, crowded) in new[] { (1f, false), (2f, true) })
        {
            var r = Place(pixelsWide, pixelsHigh, scale, text, crowded);
            var all = new[] { ("top left", r.TopLeft), ("top right", r.TopRight), ("dock", r.Dock), ("subtitles", r.Subtitles), ("bottom left", r.BottomLeft), ("bottom right", r.BottomRight) };
            for (int i = 0; i < all.Length; i++)
                for (int j = i + 1; j < all.Length; j++)
                    Assert.False(all[i].Item2.Intersects(all[j].Item2), $"{all[i].Item1} {all[i].Item2} meets {all[j].Item1} {all[j].Item2} at {pixelsWide}x{pixelsHigh}, {scale:P0}, text {text}");
        }
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void EveryRegionStaysOnTheScreen(int pixelsWide, int pixelsHigh, float scale)
    {
        float height = 1080 / scale, width = height * pixelsWide / pixelsHigh;
        foreach (var (text, crowded) in new[] { (1f, false), (2f, true) })
        {
            var r = Place(pixelsWide, pixelsHigh, scale, text, crowded);
            foreach (var region in new[] { r.TopLeft, r.TopRight, r.Dock, r.Subtitles, r.BottomLeft, r.BottomRight })
            {
                Assert.True(region.X >= 0 && region.Y >= 0 && region.Right <= width + 0.01f && region.Bottom <= height + 0.01f, $"{region} off a {width}x{height} canvas");
                Assert.True(region.Width >= 0 && region.Height >= 0);
            }
            Assert.True(r.Subtitles.Height <= height / 3 + 0.01f);
        }
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void BarsAtTheRightEdgeKeepTheColumnAndTheBandClear(int pixelsWide, int pixelsHigh, float scale)
    {
        float height = 1080 / scale, width = height * pixelsWide / pixelsHigh;
        foreach (float sideWidth in new[] { 50f, 100f, 190f })
        {
            var r = HudRegions.Of(width, height, (700, 210), SubtitleModel.BandHeight(24), (420, 100), (900, 900), sideWidth);
            var strip = new Region(width - HudRegions.Margin - sideWidth, 0, sideWidth, height);
            Assert.False(strip.Intersects(r.TopRight), $"column {r.TopRight} meets bars {strip}");
            Assert.False(strip.Intersects(r.TopLeft), $"status {r.TopLeft} meets bars {strip}");
            Assert.False(strip.Intersects(r.Subtitles), $"band {r.Subtitles} meets bars {strip}");
            Assert.False(r.TopLeft.Intersects(r.TopRight));
        }
    }

    [Fact]
    public void TheBandSitsOnTheDockAndKeepsItsHeightWhenThereIsRoom()
    {
        var r = HudRegions.Of(1920, 1080, (700, 210), 100, (420, 100), (380, 300));
        Assert.Equal(100f, r.Subtitles.Height);
        Assert.Equal(r.Dock.Y - HudRegions.Gap, r.Subtitles.Bottom, 0.01f);
        Assert.Equal(300f, r.TopRight.Height);
    }

    [Fact]
    public void WithNoCaptionsTheColumnsTakeTheRoom()
    {
        var shown = HudRegions.Of(960, 540, (800, 200), 120, (420, 400), (380, 400));
        var quiet = HudRegions.Of(960, 540, (800, 200), 0, (420, 400), (380, 400));
        Assert.True(quiet.TopRight.Height > shown.TopRight.Height);
        Assert.Equal(0f, quiet.Subtitles.Height);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void TheTwoLineInteractionPromptEndsAboveCaptionsAndBars(int pixelsWide, int pixelsHigh, float scale)
    {
        foreach (var (text, crowded) in new[] { (1f, false), (2f, true) })
        {
            var r = Place(pixelsWide, pixelsHigh, scale, text, crowded);
            var prompt = new Region(r.Dock.X, r.PromptBottom - 64, r.Dock.Width, 64);
            Assert.False(prompt.Intersects(r.Subtitles));
            Assert.False(prompt.Intersects(r.Dock));
        }
    }
}
