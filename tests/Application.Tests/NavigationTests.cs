using UNNAMED.Content;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>Navigation over the game's own region (M7 design §3.20.3), through a real session.</summary>
public class NavigationTests
{
    private static readonly NavAgent Person = new(0, true);
    private static readonly Func<NavInput, bool> EveryGatePassable = _ => true;
    // N-A1
    [Fact]
    public void TheHollow_EveryProtectedPointIsReachable()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Tester", 42);
        var grid = simulation.Navigation.Grid;

        // NAV006's points, on the grid the running world built.
        Assert.Empty(NavigationContent.Unreachable(session.Setup.Layout, session.Setup.Navigation, grid));

        // The doorway lanes of §3.3, by centre: the lodge's and the smithy's 1.6 m doors, the gap by the smithy fence, and the beam
        // line at the Woundmoss, which a standing body cannot pass.
        Assert.Equal(new long[] { 127_625, 127_875, 128_125, 128_375 }, LanesAlongZ(grid, 51_800, 126_000, 130_000));
        Assert.Equal(new long[] { 141_625, 141_875, 142_125, 142_375 }, LanesAlongZ(grid, 53_200, 140_000, 144_000));
        Assert.Equal(new long[] { 51_875, 52_125, 52_375 }, LanesAlongX(grid, 146_200, 51_400, 53_000));
        foreach (long z in new long[] { 131_625, 131_875, 132_125, 132_375 })
            Assert.Empty(LanesAlongX(grid, z, 144_600, 153_400));

        Assert.Equal(0, session.SubscriberFailures);
    }

    /// <summary>The centres of the person-walkable nodes, every gate passable, in the node column holding x, with z in [z0, z1].</summary>
    private static long[] LanesAlongZ(NavGrid grid, long x, long z0, long z1) =>
        Range(grid, z0, z1).Where(j => grid.Walkable(grid.NodeOf(x), j, Person, EveryGatePassable)).Select(grid.CentreOf).ToArray();

    private static long[] LanesAlongX(NavGrid grid, long z, long x0, long x1) =>
        Range(grid, x0, x1).Where(i => grid.Walkable(i, grid.NodeOf(z), Person, EveryGatePassable)).Select(grid.CentreOf).ToArray();

    private static IEnumerable<long> Range(NavGrid grid, long min, long max)
    {
        for (long n = grid.NodeOf(min); n <= grid.NodeOf(max); n++)
        {
            if (grid.CentreOf(n) >= min && grid.CentreOf(n) <= max)
                yield return n;
        }
    }
}
