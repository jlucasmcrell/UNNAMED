using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit.Abstractions;

namespace UNNAMED.Application.Tests;

/// <summary>
/// B-028: Cairnwold's home-to-work lines cross its buildings (the guard's 41.5 m line crosses twelve structures, the routine audit's
/// §3.2), so a body that walked them straight would go through walls and furniture. The routine's walk is planned on the navigation grid:
/// checked here against the authored footprints themselves, not the grid, so the check does not share the planner's view of the world.
/// </summary>
public class NpcRoutePathTests
{
    private const long Hour = 57_600 / 24;
    private const string Guard = "routine.cairnwold.guard";
    private readonly ITestOutputHelper _output;

    public NpcRoutePathTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A world with the character standing in Cairnwold, so its people walk at tier A, a tick at a time: by default off the guard's way,
    /// or wherever <paramref name="at"/> puts them.
    /// </summary>
    private static Arena InCairnwold(GameSession session, (double X, double Z)? at = null) =>
        Arena.OpenCreatures(session, session.Setup, at ?? (905, 650), 90, Array.Empty<(string, double, double, string)>(), keepSpawns: true, origin: (0, 0));

    /// <summary>The guard's body every tick for three hours from the world's start: home, the walk, and work.</summary>
    private static List<Body> GuardsMorning(Simulation simulation)
    {
        var bodies = new List<Body>();
        for (int i = 0; i < 3 * Hour; i++)
        {
            simulation.Step();
            bodies.Add(simulation.Npcs.Single(n => n.Id == Guard).Body);
        }
        return bodies;
    }

    [Fact]
    public void TheGuard_WalksToWork_AroundTheBuildings_TheStraightLineCrosses()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var setup = session.Setup;
        var routine = setup.NpcLife.Routines[Guard];
        var home = routine.Anchors["home"];
        var work = routine.Anchors["work"];
        var space = setup.Layout.Space;
        long height = setup.Navigation.AgentHeightMm;
        long radius = setup.Navigation.Classes[setup.Navigation.PersonClass].RadiusMm - setup.Navigation.Limits.OffLineToleranceMm;
        bool Clear(long x, long z) => Kinematics.IsClear(x, z, radius, space, ImmutableArray<Blocker>.Empty, height);

        // The straight line does cross the town: the bug's premise, on the authored footprints.
        double straight = Math.Sqrt(Math.Pow(work.XMm - home.XMm, 2) + Math.Pow(work.ZMm - home.ZMm, 2));
        int blockedOnLine = Enumerable.Range(0, 401).Count(i => !Clear(home.XMm + (work.XMm - home.XMm) * i / 400, home.ZMm + (work.ZMm - home.ZMm) * i / 400));
        Assert.True(blockedOnLine > 0, "the guard's straight line no longer crosses anything; this test no longer shows a detour");

        var bodies = GuardsMorning(InCairnwold(session).Simulation);
        var inside = bodies.Where(b => !Clear(b.XMm, b.ZMm)).ToList();
        double walked = bodies.Zip(bodies.Skip(1), (a, b) => Math.Sqrt(Math.Pow(b.XMm - a.XMm, 2) + Math.Pow(b.ZMm - a.ZMm, 2))).Sum();
        _output.WriteLine($"straight {straight / 1000:0.0} m, {blockedOnLine}/401 line samples blocked; walked {walked / 1000:0.0} m; "
            + $"{inside.Count} ticks inside a footprint; ends at ({bodies[^1].XMm}, {bodies[^1].ZMm}), work ({work.XMm}, {work.ZMm})");
        Assert.Empty(inside);
        Assert.Equal((work.XMm, work.ZMm), (bodies[^1].XMm, bodies[^1].ZMm));
        Assert.True(walked > straight * 1.05, $"walked {walked:0} mm against a {straight:0} mm straight line: no detour");
    }

    [Fact]
    public void TheGuardsWalk_IsTheSameWalk_InEveryReplay()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var first = GuardsMorning(InCairnwold(session).Simulation);
        var again = GuardsMorning(InCairnwold(session).Simulation);
        Assert.Equal(first, again);
    }

    [Fact]
    public void ACharacterStandingOnTheGuardsWay_IsWalkedAround()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var work = session.Setup.NpcLife.Routines[Guard].Anchors["work"];
        // Where the guard's route passes the lodge's corner. Standing there, the character pushed him off his line into a spot the grid
        // has no room for (start_blocked, for ever), and once he was walked out, he pressed into them head-on for the rest of the morning.
        var arena = InCairnwold(session, (955, 660));
        var bodies = GuardsMorning(arena.Simulation);
        Assert.Equal((work.XMm, work.ZMm), (bodies[^1].XMm, bodies[^1].ZMm));
        Assert.All(bodies.Zip(bodies.Skip(1)), p => Assert.True(Math.Pow(p.Second.XMm - p.First.XMm, 2) + Math.Pow(p.Second.ZMm - p.First.ZMm, 2) < 500.0 * 500.0));
    }
}