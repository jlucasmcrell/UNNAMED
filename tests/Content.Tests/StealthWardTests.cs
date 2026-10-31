// UNNAMED Content - D23 S7: the wards are placed, and they name the bell they ring

using UNNAMED.Content;
using UNNAMED.Domain.Combat;
using Xunit;

namespace UNNAMED.Content.Tests;

/// <summary>
/// S7 of D23 (canon/STEALTH.md 10). A ward is content: a point, a radius and an alarm. This checks the two the world has, that the alarm each names is
/// a real world flag (so a listener can act on it), and that a rune key answers one without the working.
/// </summary>
public class StealthWardTests
{
    private static ContentLoader Content()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        return loader;
    }

    [Fact]
    public void TheBarrowworksAndTheFrontier_HaveWards_ThatRingARealBell()
    {
        var loader = Content();
        var flags = loader.GetByKind("world_flag");
        var wards = new[] { "region.barrowworks", "region.starting_frontier" }
            .Select(id => WorldContent.BuildLayout(loader, id))
            .SelectMany(l => l.Wards)
            .ToArray();

        Assert.NotEmpty(wards);
        foreach (var ward in wards)
        {
            Assert.StartsWith("ward.", ward.Key);
            Assert.True(ward.RadiusMm > 0);
            Assert.False(string.IsNullOrWhiteSpace(ward.DisplayName));
            if (ward.AlarmFlag.Length > 0)
                Assert.True(flags.ContainsKey(ward.AlarmFlag), $"{ward.Key} rings {ward.AlarmFlag}, which is not a world flag");
        }
        // At least one ward is answerable without the working, or the rune key is decoration.
        Assert.Contains(wards, w => w.RuneKey.Length > 0);
    }

    [Fact]
    public void AWardAlarmsInsideItsRadius_AndNotOutsideIt()
    {
        // The reach is the rule: a ward is a place, and crossing it is being inside it (STEALTH.md 10).
        var ward = new WardSite("ward.test", 0, 0, 2_000);
        Assert.True(WardRules.Alarms(toWardMm: 50, radiusMm: ward.RadiusMm, quieted: false, crossedByDistraction: false, hasRuneKey: false));
        Assert.False(WardRules.Alarms(toWardMm: 2_500, radiusMm: ward.RadiusMm, quieted: false, crossedByDistraction: false, hasRuneKey: false));
    }
}
