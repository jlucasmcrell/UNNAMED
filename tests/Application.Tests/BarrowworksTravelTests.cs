// UNNAMED Application tests - travel and death against a dungeon (M8 D10; travel M-something)
// Canon: no road runs under the hill, and a character who dies inside comes back outside at the stone. Both
// are enforced where the state is, not in a panel, so no UI path can reach round them.

using UNNAMED.Application;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit;

namespace UNNAMED.Application.Tests;

public class BarrowworksTravelTests
{
    private const string Barrowworks = "region.barrowworks";
    private const string Threshold = "portal.barrowworks.threshold";

    /// <summary>The threshold portal's exterior anchor: beside Blackvein's spoil heaps.</summary>
    private static readonly (double X, double Z) ThresholdAnchor = (438, 663);

    /// <summary>
    /// The world's own region with the gate on, so the two entrances exist and the player walks in through one
    /// - which is the only way to be inside WITH a traversal. An arena opened straight into the Barrowworks
    /// stands the player inside knowing no way out, which `Leave` refuses by name.
    /// </summary>
    private static GameSession Boot(TempProfile profile) =>
        GameSession.Boot(new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root)
        {
            LockProfile = true,
            RegionId = "region.starting_frontier",
            Barrowworks = true,
        });

    private static Arena StandingAt(GameSession session, (double X, double Z) anchor)
    {
        var (ox, oz) = Harness.HollowOf(session.Setup);
        return Arena.OpenWith(session, session.Setup, (anchor.X - ox, anchor.Z - oz), 0, []);
    }

    /// <summary>Inside the dungeon, having walked in through the threshold portal.</summary>
    private static Arena Inside(GameSession session)
    {
        var arena = StandingAt(session, ThresholdAnchor);
        var simulation = arena.Simulation;
        Assert.True(simulation.PlayerInExterior);
        Assert.Null(arena.Submit(new EnterSpaceCommand(simulation.PlayerId, Threshold)));
        Assert.False(simulation.PlayerInExterior);
        return arena;
    }

    /// <summary>
    /// No road runs under the hill. A stone's arrival point is a place in the region's own space, so travelling
    /// from an interior would move the body to exterior metres while the simulation still called it inside.
    /// </summary>
    [Fact]
    public void TravellingFromInsideTheDungeon_IsRefusedAndTheBodyStaysPut()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var arena = Inside(session);
        var simulation = arena.Simulation;

        var before = simulation.Player.Body;
        Assert.NotNull(arena.Submit(new TravelCommand(simulation.PlayerId, "waystone.ashen", "waystone.ashen")));

        Assert.False(simulation.PlayerInExterior);
        Assert.Equal(before.XMm, simulation.Player.Body.XMm);
        Assert.Equal(before.ZMm, simulation.Player.Body.ZMm);
    }

    /// <summary>
    /// Walk in and back out, and the space is the exterior again.
    /// </summary>
    /// <remarks>
    /// This covers the state reset that dying also needs. It is NOT a death test: nothing in the Barrowworks
    /// can kill the player yet - the region declares a player spawn and no creature spawns, so there is no
    /// hostile to do it. That test waits on the content port (P4). `DeathSystem` calling
    /// `SpaceSystem.LeaveForDeath` before it relocates is build-verified rather than exercised, and saying so is
    /// better than a test named for death that never kills anybody.
    /// </remarks>
    [Fact]
    public void LeavingTheInterior_PutsTheSpaceBackToTheExterior()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var arena = Inside(session);
        var simulation = arena.Simulation;

        Assert.Null(arena.Submit(new LeaveSpaceCommand(simulation.PlayerId)));

        Assert.True(simulation.PlayerInExterior);
    }

    /// <summary>
    /// A body that opens inside without having come in by a portal - a harness, or a save written inside - can
    /// look around and cannot leave, because leaving has to know where "out" is. The refusal names itself
    /// rather than throwing.
    /// </summary>
    [Fact]
    public void ABodyOpenedInsideWithNoWayIn_CannotLeave()
    {
        using var profile = new TempProfile();
        using var session = GameSession.Boot(new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root)
        {
            LockProfile = true,
            RegionId = Barrowworks,
            Barrowworks = true,
        });
        var (ox, oz) = Harness.HollowOf(session.Setup);
        var arena = Arena.OpenWith(session, session.Setup, (50 - ox + 0, 10 - oz + 0), 0, []);
        var simulation = arena.Simulation;
        Assert.False(simulation.PlayerInExterior);

        Assert.NotNull(arena.Submit(new LeaveSpaceCommand(simulation.PlayerId)));
        Assert.False(simulation.PlayerInExterior);
    }
}
