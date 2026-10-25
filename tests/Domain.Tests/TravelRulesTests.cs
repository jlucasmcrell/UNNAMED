using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;
using UNNAMED.Domain.Travel;
using Xunit;

namespace UNNAMED.Domain.Tests;

/// <summary>Waystone travel's pure rules (S-38): distances by road, the cost of a journey, and what the map holds.</summary>
public class TravelRulesTests
{
    private static RoadSite Road(string id, params (long X, long Z)[] metres) =>
        new(id, id, metres.Select(p => (p.X * 1000, p.Z * 1000)).ToImmutableArray());

    private static WaystoneSite Stone(string key, long x, long z) =>
        new(key, key, new CircleBlocker(key, x * 1000, z * 1000, 500, 2000), "location.test", x * 1000, (z + 2) * 1000);

    [Fact]
    public void TheWalk_FollowsTheRoad_NotTheStraightLine()
    {
        // An L-shaped road: 100 m east, then 100 m north. Its ends are 141 m apart as the crow flies; the road is 200 m.
        var roads = new[] { Road("road.l", (0, 0), (100, 0), (100, 100)) };
        Assert.Equal(200_000, Roads.DistanceMm(roads, (0, 0), (100_000, 100_000)));
        // Off the road at both ends: 10 m in, along it, 10 m out.
        Assert.Equal(220_000, Roads.DistanceMm(roads, (0, -10_000), (110_000, 100_000)));
    }

    [Fact]
    public void TwoPlacesOnOneStretch_GoAlongIt_AndRoadsJoinWhereTheyMeet()
    {
        var roads = new[] { Road("road.a", (0, 0), (100, 0)), Road("road.b", (110, 0), (110, 300)) };
        Assert.Equal(40_000, Roads.DistanceMm(roads, (20_000, 0), (60_000, 0)));
        // From road.a's start to road.b's far end: 100 m, the 10 m gap where they meet, then 300 m.
        Assert.Equal(410_000, Roads.DistanceMm(roads, (0, 0), (110_000, 300_000)));
    }

    [Fact]
    public void WithNoRoadNearBoth_TheWalkIsTheStraightLine()
    {
        var roads = new[] { Road("road.far", (0, 0), (10, 0)) };
        Assert.Equal(500_000, Roads.DistanceMm(roads, (1_000_000, 0), (1_300_000, 400_000)));
        Assert.Equal(500_000, Roads.DistanceMm(Array.Empty<RoadSite>(), (0, 0), (300_000, 400_000)));
    }

    [Fact]
    public void AJourney_QuotesTheEquivalentWalkWithoutAWorldTimeCharge()
    {
        var roads = new[] { Road("road.s", (0, 2), (800, 2)) };
        var cost = Journeys.Cost(roads, Stone("waystone.a", 0, 0), Stone("waystone.b", 800, 0), 1_600, 50);
        // 800 m at a 1.6 m/s walk is quoted as 500 s: 10,000 ticks of 50 ms.
        Assert.Equal(new TravelCost(800_000, 10_000), cost);
        Assert.Equal(1, Journeys.Cost(roads, Stone("waystone.a", 0, 0), Stone("waystone.a", 0, 0), 1_600, 50).WalkTicks);
    }

    [Fact]
    public void AStretchIsWalked_OnlyWithinItsReach()
    {
        var roads = new[] { Road("road.s", (0, 0), (100, 0), (200, 0)) };
        Assert.Equal(new[] { "road.s#0" }, Roads.StretchesNear(roads, 50_000, 5_000, 12_000));
        Assert.Equal(new[] { "road.s#0", "road.s#1" }, Roads.StretchesNear(roads, 100_000, 0, 12_000));
        Assert.Empty(Roads.StretchesNear(roads, 50_000, 20_000, 12_000));
    }

    [Fact]
    public void TheMap_HoldsOnlyWhatIsKnown()
    {
        var layout = new RegionLayout("region.test", ImmutableArray<string>.Empty, null!, ImmutableArray<DoorSite>.Empty,
            ImmutableArray.Create(new LocationSite("location.a", 10_000, 10_000, 5_000, 0) { RevealRadiusMm = 60_000 },
                new LocationSite("location.b", 90_000, 10_000, 5_000, 0)),
            new Body(0, 0, 0, 0), new RegionGeneration(0, 0, 2))
        {
            Roads = ImmutableArray.Create(Road("road.s", (0, 0), (100, 0), (200, 0))),
            Waystones = ImmutableArray.Create(Stone("waystone.a", 10, 10), Stone("waystone.b", 190, 10)),
        };

        var map = Cartography.Known(layout, new[] { "location.a" }, new[] { "road.s#1" }, new[] { "waystone.a" });

        Assert.Equal(new[] { new KnownPlace("location.a", 10_000, 10_000, 60_000) }, map.Places.ToArray());
        Assert.Equal(new[] { new KnownRoad("road.s", "road.s", 100_000, 0, 200_000, 0) }, map.Roads.ToArray());
        Assert.Equal(new[] { "waystone.a" }, map.Stones.ToArray());
    }
}
