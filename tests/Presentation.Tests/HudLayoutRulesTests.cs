using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

/// <summary>HUD text never collapses to one character per line: floors on width for hints, toasts, subtitles and reading lines.</summary>
public class HudLayoutRulesTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(18)]
    [InlineData(20)]
    [InlineData(32)]
    public void MinWidthHoldsTwelveCharacters(int size) =>
        Assert.True(HudLayoutRules.MinWidth(size) >= HudLayoutRules.MinChars * size * 0.5f);

    [Theory]
    [InlineData(0f)]
    [InlineData(81f)]
    [InlineData(300f)]
    [InlineData(1920f)]
    public void ReadingWidthNeverCollapses(float viewport) =>
        Assert.True(HudLayoutRules.ReadingWidth(viewport, 80, 600, 20) >= HudLayoutRules.MinWidth(20));

    [Fact]
    public void ReadingWidthStillCapsAtMax() => Assert.Equal(600f, HudLayoutRules.ReadingWidth(4000, 80, 600, 20));

    [Fact]
    public void ASpanNarrowerThanTheFloorFallsBackToTheDefault()
    {
        // The inventory's side panels left a negative span: one letter per line.
        Assert.Equal((600f, 1320f), HudLayoutRules.Span(900, 500, 600, 1320, 320));
        Assert.Equal((600f, 1320f), HudLayoutRules.Span(1000, 1100, 600, 1320, 320));
        Assert.Equal((700f, 1200f), HudLayoutRules.Span(700, 1200, 600, 1320, 320));
    }

    [Fact]
    public void NoWidthIsClampedDownToOnePixelInPresentation()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Presentation"));
        Assert.True(Directory.Exists(root), root);
        var bad = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((l, i) => (f, l, i + 1)))
            .Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.l, @"Math\.Clamp\([^;]*,\s*1\s*,\s*\d+\)\s*,\s*0\)") )
            .Select(t => $"{t.f}:{t.Item3}").ToArray();
        Assert.Empty(bad);
    }
}
