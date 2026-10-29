using UNNAMED.Domain.Creatures;
using UNNAMED.World.Runtime;

namespace UNNAMED.World.Tests;

/// <summary>
/// The wind that carries a scent (canon skill.tracking's read from downwind; STEALTH_DETECTION_AND_THREAT.md §18). It is the front's
/// own draw, so it holds for the length of a front, turns when the weather does, and no two worlds blow alike - which is the whole of
/// what a scent read depends on outside the skill.
/// </summary>
public class TrackingTests
{
    private const ulong Seed = 0x5C1A9E7B4D2F0083;
    private const string Region = "region.starting_frontier";

    private static readonly CellKey RegionCell = new(RegionKey.Parse("r_0_0"), 0, 0);

    [Fact]
    public void AWind_HoldsForTheLengthOfItsFront_AndTurnsWithTheNext()
    {
        var fronts = Enumerable.Range(0, 20).SelectMany(span => Weather.Fronts(Seed, RegionCell, Region, span, WeatherRules.Default)).ToList();
        foreach (var front in fronts)
        {
            int wind = Weather.WindFromMdeg(Seed, RegionCell, Region, front.StartHour);
            Assert.InRange(wind, 0, 359_999);
            // Every hour of the front reads the same wind: the draw is addressed by the hour the front began.
            Assert.Equal(wind, Weather.WindFromMdeg(Seed, RegionCell, Region, front.StartHour));
        }
        // It is not one flat direction: the fronts of a day or two do not all blow the same way.
        Assert.True(fronts.Select(f => Weather.WindFromMdeg(Seed, RegionCell, Region, f.StartHour)).Distinct().Count() > fronts.Count / 2);
    }

    [Fact]
    public void AnotherSeed_OrAnotherRegion_BlowsAnotherWay()
    {
        int here = Weather.WindFromMdeg(Seed, RegionCell, Region, 0);
        Assert.NotEqual(here, Weather.WindFromMdeg(Seed + 1, RegionCell, Region, 0));
        Assert.NotEqual(here, Weather.WindFromMdeg(Seed, new CellKey(RegionKey.Parse("r_1_0"), 0, 0), "region.elsewhere", 0));
    }

    [Fact]
    public void TheWindOfAFront_IsPinned_SoAChangeToItsDrawIsSeen()
    {
        // Pinned from this build: the wind is a worldgen contract like the fronts themselves, and moving it moves every scent read.
        Assert.Equal(Pinned, Weather.WindFromMdeg(Seed, RegionCell, Region, 0));
    }

    private const int Pinned = 149_591;

    [Fact]
    public void TheWeathersOwnWind_CarriesAScentToAReaderDownwindAndPastOneUpwind()
    {
        int wind = Weather.WindFromMdeg(Seed, RegionCell, Region, 0);
        // The direction the wind comes from, as the world measures a bearing: from +Z towards +X.
        double radians = wind / 1000.0 * Math.PI / 180;
        long x = (long)Math.Round(5_000 * Math.Sin(radians)), z = (long)Math.Round(5_000 * Math.Cos(radians));
        var site = new TrailSite(0, 0, wind, TrackGround.Earth, Tick: 100);
        var scent = new SpoorMark(SpoorKind.Scent, TrackGround.Earth, 0, 0, 0, LeftTick: 0, Strength: 100) { LeftBy = "wolves/0" };

        Assert.True(Tracking.Reaches(site, scent with { XMm = x, ZMm = z }, TrackingRules.Default));
        Assert.False(Tracking.Reaches(site, scent with { XMm = -x, ZMm = -z }, TrackingRules.Default));
    }
}
