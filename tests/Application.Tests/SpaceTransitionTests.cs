using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// M8 D10: entering an interior space through a portal, and leaving back out of the one that was used.
/// The Barrowworks' two entrances are the fixture, because they are the design's own example of why a
/// single return vector is not enough.
/// </summary>
/// <remarks>
/// These run against a bare <see cref="Arena"/> rather than a <see cref="GameSession"/>: the space system
/// is simulation-side, and an arena's own simulation is stepped directly, which is how the other arena
/// tests drive it. Arena metres are the hollow's, so a portal's region millimetres are converted through
/// <see cref="Harness.HollowOf"/>.
/// </remarks>
public class SpaceTransitionTests
{
    private static readonly SpaceId Barrowworks = SpaceId.Parse("space.barrowworks");

    // The two entrances from the frozen level design: the hill above Blackvein, and the Sinking Moss drain
    // mouth. Exterior anchors are region millimetres; interior anchors are room centres in local metres.
    private const string Threshold = "portal.barrowworks.threshold";
    private const string Drain = "portal.barrowworks.drain";

    private static readonly SpacePortalSet Portals = new SpacePortalSet(
    [
        new SpacePortal(Threshold, Barrowworks, ExteriorXMm: 438_000, ExteriorZMm: 648_000, InteriorXMm: 0, InteriorZMm: 0),
        new SpacePortal(Drain, Barrowworks, ExteriorXMm: 512_000, ExteriorZMm: 604_000, InteriorXMm: -42_000, InteriorZMm: 28_000),
    ]).Validate();

    /// <summary>The region's portal set with this project's two entrances added.</summary>
    private static SpacePortalSet WithPortals(GameSession session) =>
        new(session.Setup.Layout.Portals.Portals.AddRange(Portals.Portals));

    /// <summary>A running arena with the portals declared, the player standing at <paramref name="entrance"/>.</summary>
    private static (GameSession Session, Arena Arena) StandingAt(TempProfile profile, string? entrance)
    {
        var session = Harness.Boot(profile);
        var rules = session.Setup with { Layout = session.Setup.Layout with { Portals = WithPortals(session) } };
        var (ox, oz) = Harness.HollowOf(rules);
        var portal = entrance is null ? null : rules.Layout.Portals.Find(entrance)!;
        // Somewhere clear of both anchors when no entrance is named.
        var (px, pz) = portal is null
            ? (ox + 10, oz + 10)
            : ((double)portal.ExteriorXMm / 1000.0, (double)portal.ExteriorZMm / 1000.0);
        var arena = Arena.OpenWith(session, rules, (px - ox, pz - oz), 0, []);
        return (session, arena);
    }

    private static void Apply(Arena arena, GameCommand command)
    {
        arena.Simulation.Enqueue(command);
        arena.Simulation.DrainCommands();
    }

    private static (long X, long Z) At(Arena arena) =>
        (arena.Simulation.Player.Body.XMm, arena.Simulation.Player.Body.ZMm);

    [Fact]
    public void EnteringAPortal_PutsTheBodyAtItsInteriorAnchor()
    {
        using var profile = new TempProfile();
        var (_, arena) = StandingAt(profile, Drain);
        var simulation = arena.Simulation;

        Assert.True(simulation.PlayerInExterior);
        Apply(arena, new EnterSpaceCommand(simulation.PlayerId, Drain));

        Assert.False(simulation.PlayerInExterior);
        // The interior anchor, in interior-local metres - not the exterior one.
        Assert.Equal(-42_000, simulation.Player.Body.XMm);
        Assert.Equal(28_000, simulation.Player.Body.ZMm);
    }

    [Fact]
    public void LeavingReturnsToTheEntranceThatWasUsed_NotTheOther()
    {
        using var profile = new TempProfile();
        var (_, arena) = StandingAt(profile, Drain);
        var simulation = arena.Simulation;

        Apply(arena, new EnterSpaceCommand(simulation.PlayerId, Drain));
        Apply(arena, new LeaveSpaceCommand(simulation.PlayerId));

        Assert.True(simulation.PlayerInExterior);
        // Out by the drain, which is where this character came in.
        Assert.Equal(512_000, simulation.Player.Body.XMm);
        Assert.Equal(604_000, simulation.Player.Body.ZMm);
    }

    [Fact]
    public void EnteringByOneEntrance_AndLeaving_DoesNotArriveAtTheOther()
    {
        // The design's named failure: a player who came in by the drain must not be snapped to the
        // formal entrance because a single global return vector was reused.
        using var profile = new TempProfile();
        var (_, arena) = StandingAt(profile, Threshold);
        var simulation = arena.Simulation;

        Apply(arena, new EnterSpaceCommand(simulation.PlayerId, Threshold));
        Apply(arena, new LeaveSpaceCommand(simulation.PlayerId));

        Assert.Equal(438_000, simulation.Player.Body.XMm);
        Assert.Equal(648_000, simulation.Player.Body.ZMm);
        Assert.NotEqual((512_000L, 604_000L), At(arena));
    }

    [Fact]
    public void RoundTrip_ThroughEitherEntrance_EndsWhereItStarted()
    {
        using var profile = new TempProfile();

        foreach (string entrance in new[] { Threshold, Drain })
        {
            var (_, arena) = StandingAt(profile, entrance);
            var simulation = arena.Simulation;
            var start = At(arena);

            Apply(arena, new EnterSpaceCommand(simulation.PlayerId, entrance));
            Assert.False(simulation.PlayerInExterior);

            Apply(arena, new LeaveSpaceCommand(simulation.PlayerId));
            Assert.True(simulation.PlayerInExterior);
            Assert.Equal(start, At(arena));
        }
    }

    [Fact]
    public void BeingTooFarFromAPortal_IsRefused_AndLeavesTheCharacterWhereTheyWere()
    {
        using var profile = new TempProfile();
        var (_, arena) = StandingAt(profile, entrance: null);
        var simulation = arena.Simulation;
        var before = At(arena);

        Apply(arena, new EnterSpaceCommand(simulation.PlayerId, Threshold));

        Assert.True(simulation.PlayerInExterior);
        Assert.Equal(before, At(arena));
    }

    [Fact]
    public void AnUnknownPortal_IsRefused()
    {
        using var profile = new TempProfile();
        var (_, arena) = StandingAt(profile, Threshold);

        Apply(arena, new EnterSpaceCommand(arena.Simulation.PlayerId, "portal.nowhere"));

        Assert.True(arena.Simulation.PlayerInExterior);
    }

    [Fact]
    public void LeavingWhileOutside_IsRefused()
    {
        using var profile = new TempProfile();
        var (_, arena) = StandingAt(profile, Threshold);
        var before = At(arena);

        Apply(arena, new LeaveSpaceCommand(arena.Simulation.PlayerId));

        Assert.True(arena.Simulation.PlayerInExterior);
        Assert.Equal(before, At(arena));
    }

    [Fact]
    public void EnteringTwice_IsRefused_AndDoesNotMoveTheBody()
    {
        using var profile = new TempProfile();
        var (_, arena) = StandingAt(profile, Drain);
        var simulation = arena.Simulation;

        Apply(arena, new EnterSpaceCommand(simulation.PlayerId, Drain));
        var inside = At(arena);

        Apply(arena, new EnterSpaceCommand(simulation.PlayerId, Drain));

        Assert.Equal(inside, At(arena));
    }

    [Fact]
    public void WithNoPortalsDeclared_NothingChanges()
    {
        // The default-off guarantee: a world with no portals has the player in the exterior, and the
        // space system does nothing. Every region is in this state until the Barrowworks is gated on.
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        Assert.Empty(session.Setup.Layout.Portals.Portals);
        Assert.True(session.Setup.Layout.Portals.Into(Barrowworks).IsEmpty);
    }
}
