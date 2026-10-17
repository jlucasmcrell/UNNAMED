// UNNAMED Application tests - the party follows through a portal (B1)
// A follower left behind would be in a space the player has left, and every rule about who can see and reach whom now
// depends on sharing it. So going through a door brings the party.

using System.Collections.Immutable;
using UNNAMED.Application;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit;

namespace UNNAMED.Application.Tests;

public class SpaceCompanionTests
{
    private const string Tavar = "npc.ashen_hollow.tavar_orr";
    private const string Ring = "space.barrowworks";

    /// <summary>A follower at the player's side, in Follow order.</summary>
    private static PlayerRecord WithTavar(PlayerRecord record) =>
        record.WithCompanions(new[]
        {
            new CompanionRecord(Tavar, CompanionOrder.Follow, CompanionCondition.Up,
                record.XMm + 2_000, record.ZMm + 2_000, 90_000, 100),
        });

    [Fact]
    public void GoingThroughAPortal_BringsTheFollower()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile, barrowworks: true);
        // The exterior anchor of the ring's threshold, so the player stands at the door.
        var portal = session.Setup.Layout.Portals.Portals.First(p => p.Space == SpaceId.Parse(Ring));
        var origin = Harness.HollowOf(session.Setup);
        var at = ((portal.ExteriorXMm / 1000.0) - origin.X, (portal.ExteriorZMm / 1000.0) - origin.Z);

        var arena = Arena.OpenCreatures(session, session.Setup, at, 90,
            Array.Empty<(string, double, double, string)>(), WithTavar, keepSpawns: true);
        var him = arena.Simulation.Companions.Single().Body;

        Assert.Null(arena.Submit(new EnterSpaceCommand(arena.Simulation.PlayerId, portal.Key)));
        arena.Tick();

        var me = arena.Simulation.Player.Body;
        var ringGround = session.Setup.SpaceFor(SpaceId.Parse(Ring));
        var after = arena.Simulation.Companions.Single().Body;
        // The real invariant is that a follower is BESIDE you and you are in the ring. Being merely inside the
        // ring's bounds would be true by accident, because the interior's local metres overlap the exterior's.
        Assert.InRange(me.XMm, ringGround.MinXMm, ringGround.MaxXMm);
        var apart = Math.Sqrt(Math.Pow(after.XMm - me.XMm, 2) + Math.Pow(after.ZMm - me.ZMm, 2));
        Assert.True(apart < 5_000, "the follower is {apart:0} mm from the player, so he did not come through");
        Assert.NotEqual(him.XMm, after.XMm);
    }
}
