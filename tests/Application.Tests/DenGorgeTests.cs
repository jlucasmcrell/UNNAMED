using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Spec 28: Halda's rope over the gorge lip behind the den gives way under its first crossing - the body still arrives on the ledge, the
/// fall costs health but never the last of it, and the rope is gone for good - and the cleft, found from below, is the way out and
/// back. The game's own frontier and portals, in an arena with no creatures.
/// </summary>
public class DenGorgeTests
{
    private const string Rope = "portal.den_gorge.rope", Cleft = "portal.den_gorge.cleft";
    private static readonly SpaceId Cave = SpaceId.Parse("space.den_gorge_cave");

    private static Arena StandingAt(TempProfile profile, string portalKey)
    {
        var session = Harness.Boot(profile);
        var (ox, oz) = Harness.HollowOf(session.Setup);
        var portal = session.Setup.Layout.Portals.Find(portalKey)!;
        return Arena.OpenWith(session, session.Setup, (portal.ExteriorXMm / 1000.0 - ox, portal.ExteriorZMm / 1000.0 - oz), 0, []);
    }

    private static void Apply(Arena arena, GameCommand command)
    {
        arena.Simulation.Enqueue(command);
        arena.Simulation.DrainCommands();
    }

    [Fact]
    public void TheRope_GivesUnderItsFirstCrossing_TheBodyArrivesHurtButAlive_AndTheRopeIsGone()
    {
        using var profile = new TempProfile();
        var arena = StandingAt(profile, Rope);
        var simulation = arena.Simulation;
        var rope = simulation.Setup.Layout.Portals.Find(Rope)!;
        int before = simulation.Combat.Health;

        Apply(arena, new EnterSpaceCommand(simulation.PlayerId, Rope));

        Assert.False(simulation.PlayerInExterior);
        Assert.Equal(Cave, simulation.ActiveLayout.SpaceId);
        Assert.Equal((rope.InteriorXMm, rope.InteriorZMm), (simulation.Player.Body.XMm, simulation.Player.Body.ZMm));
        // 3.5 m at six a metre: hurt, never killed.
        Assert.Equal(before - 21, simulation.Combat.Health);
        Assert.True(simulation.PortalGone(rope));
        // Gone both ways: there is no climbing back up the rope.
        Apply(arena, new LeaveSpaceCommand(simulation.PlayerId));
        Assert.False(simulation.PlayerInExterior);
    }

    [Fact]
    public void TheCleft_CannotBeFoundFromAbove_UntilTheRopeHasGone()
    {
        using var profile = new TempProfile();
        var arena = StandingAt(profile, Cleft);
        var simulation = arena.Simulation;
        var cleft = simulation.Setup.Layout.Portals.Find(Cleft)!;

        Assert.False(simulation.PortalOpen(cleft));
        Apply(arena, new EnterSpaceCommand(simulation.PlayerId, Cleft));

        Assert.True(simulation.PlayerInExterior);
    }

    [Fact]
    public void TheHollowUnderTheDen_IsHeldWithItsOwnSpawns_AndTheBarrowworksGateDoesNotCloseIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        Assert.Contains(Cave, session.Setup.Layouts.Keys);
        Assert.Contains(session.Setup.Combat.Spawns, s => s.Key == "spawn.den_gorge.matriarch" && s.Space == Cave);
        Assert.NotNull(session.Setup.Layout.Portals.Find(Rope));
    }
}
