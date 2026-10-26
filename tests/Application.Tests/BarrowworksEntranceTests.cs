using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// P2: the Barrowworks' two entrances, and the gate over them. Content declares both; a run that does not
/// ask for the Barrowworks registers neither, so the interior is unreachable and the playtest is unchanged.
/// </summary>
public class BarrowworksEntranceTests
{
    private static readonly SpaceId Barrowworks = SpaceId.Parse("space.barrowworks");

    private const string Threshold = "portal.barrowworks.threshold";
    private const string Drain = "portal.barrowworks.drain";

    /// <summary>The two anchors as authored, in region metres - the same numbers as in the content.</summary>
    private static readonly (double X, double Z) ThresholdAt = (438, 663);
    private static readonly (double X, double Z) DrainAt = (1050, 770);

    private static GameSession Boot(TempProfile profile, bool gated) =>
        GameSession.Boot(new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root)
        {
            RegionId = "region.starting_frontier",
            Barrowworks = gated,
        });

    /// <summary>An arena with the player standing on one of the anchors, drifting the given distance away.</summary>
    private static Arena StandingAt(GameSession session, (double X, double Z) anchor, double driftM = 0)
    {
        var (ox, oz) = Harness.HollowOf(session.Setup);
        // Arena metres are the hollow's, so a region anchor is converted through the hollow origin.
        return Arena.OpenWith(session, session.Setup, (anchor.X - ox + driftM, anchor.Z - oz), 0, []);
    }

    private static void Apply(Arena arena, GameCommand command)
    {
        arena.Simulation.Enqueue(command);
        arena.Simulation.DrainCommands();
    }

    [Fact]
    public void WithTheGateOn_BothEntrancesAreRegistered()
    {
        using var profile = new TempProfile();
        var session = Boot(profile, gated: true);

        var portals = session.Setup.Layout.Portals;
        Assert.Equal(2, portals.Portals.Count(p => p.Space == Barrowworks));
        Assert.Equal(2, portals.Into(Barrowworks).Length);
        Assert.NotNull(portals.Find(Threshold));
        Assert.NotNull(portals.Find(Drain));
    }

    [Fact]
    public void WithTheGateOff_NeitherEntranceIsRegistered()
    {
        // The default-off guarantee, at the content level: these two portals are authored in
        // starting_frontier and inert unless the run asks for the Barrowworks.
        using var profile = new TempProfile();
        var session = Boot(profile, gated: false);

        Assert.DoesNotContain(session.Setup.Layout.Portals.Portals, p => p.Space == Barrowworks);
    }

    [Fact]
    public void EnteringByTheThreshold_ArrivesInTheInterior()
    {
        using var profile = new TempProfile();
        var session = Boot(profile, gated: true);
        var arena = StandingAt(session, ThresholdAt);
        var simulation = arena.Simulation;

        Assert.True(simulation.PlayerInExterior);
        Apply(arena, new EnterSpaceCommand(simulation.PlayerId, Threshold));

        Assert.False(simulation.PlayerInExterior);
        // R01's centre in the blockout: the interior anchor the content names.
        Assert.Equal(50_000, simulation.Player.Body.XMm);
        Assert.Equal(10_000, simulation.Player.Body.ZMm);
    }

    [Fact]
    public void CreatureViews_FollowSpaceChangesWithoutATick()
    {
        using var profile = new TempProfile();
        var session = Boot(profile, gated: true);
        var (ox, oz) = Harness.HollowOf(session.Setup);
        var arena = Arena.OpenWith(session, session.Setup, (ThresholdAt.X - ox, ThresholdAt.Z - oz), 0, [], keepSpawns: true);
        var simulation = arena.Simulation;
        long tick = simulation.WorldTick;
        var exterior = simulation.Creatures;
        Assert.NotEmpty(exterior);
        Assert.Null(arena.Submit(new EnterSpaceCommand(simulation.PlayerId, Threshold)));
        var interior = simulation.Creatures;
        Assert.NotEmpty(interior);
        Assert.DoesNotContain(interior, c => exterior.Any(e => e.Id == c.Id));
        Assert.Null(arena.Submit(new LeaveSpaceCommand(simulation.PlayerId)));
        Assert.Equal(exterior.Select(c => c.Id), simulation.Creatures.Select(c => c.Id));
        Assert.Equal(tick, simulation.WorldTick);
    }

    [Fact]
    public void EachEntrance_LeavesToItsOwnAnchor()
    {
        // The design's named failure, now through real content rather than a test fixture.
        using var profile = new TempProfile();

        foreach (var (key, anchor) in new[] { (Threshold, ThresholdAt), (Drain, DrainAt) })
        {
            var session = Boot(profile, gated: true);
            var arena = StandingAt(session, anchor);
            var simulation = arena.Simulation;

            Apply(arena, new EnterSpaceCommand(simulation.PlayerId, key));
            Assert.False(simulation.PlayerInExterior, $"{key} did not enter");

            Apply(arena, new LeaveSpaceCommand(simulation.PlayerId));
            Assert.True(simulation.PlayerInExterior, $"{key} did not leave");
            Assert.Equal((long)(anchor.X * 1000), simulation.Player.Body.XMm);
            Assert.Equal((long)(anchor.Z * 1000), simulation.Player.Body.ZMm);
        }
    }

    [Fact]
    public void TheTwoEntrances_AreNotAtTheSamePlace()
    {
        // Recorded rather than assumed: the canon anchors these are placed on are far apart, which is a
        // design question for the owner (handoff OQ-11) and not something to discover later.
        using var profile = new TempProfile();
        var session = Boot(profile, gated: true);

        var a = session.Setup.Layout.Portals.Find(Threshold)!;
        var b = session.Setup.Layout.Portals.Find(Drain)!;
        double apart = Math.Sqrt(Math.Pow(a.ExteriorXMm - b.ExteriorXMm, 2) + Math.Pow(a.ExteriorZMm - b.ExteriorZMm, 2)) / 1000.0;

        Assert.True(apart > 100, $"the two entrances are {apart:F0} m apart");
    }

    [Fact]
    public void WithTheGateOff_EnteringIsImpossible()
    {
        using var profile = new TempProfile();
        var session = Boot(profile, gated: false);
        var arena = StandingAt(session, ThresholdAt);
        var before = (arena.Simulation.Player.Body.XMm, arena.Simulation.Player.Body.ZMm);

        Apply(arena, new EnterSpaceCommand(arena.Simulation.PlayerId, Threshold));

        Assert.True(arena.Simulation.PlayerInExterior);
        Assert.Equal(before, (arena.Simulation.Player.Body.XMm, arena.Simulation.Player.Body.ZMm));
    }
}
