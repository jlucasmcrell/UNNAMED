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
using UNNAMED.Persistence;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

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

    [Theory]
    [InlineData(CompanionOrder.Follow)]
    [InlineData(CompanionOrder.Wait)]
    public void SaveInside_KeepsEachPartyMembersActualSpace(CompanionOrder order)
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, barrowworks: true);
        var arena = Arena.OpenCreatures(session, session.Setup, (440, 622), 90,
            Array.Empty<(string, double, double, string)>(),
            r => r.WithCompanions(new[] { new CompanionRecord(Tavar, order, CompanionCondition.Up, 440_000, 624_000, 90_000, 100) }),
            origin: (0, 0));
        Assert.Null(arena.Submit(new EnterSpaceCommand(arena.Player, "portal.barrowworks.threshold")));
        arena.Tick();
        var expectedSpace = order == CompanionOrder.Follow ? SpaceId.Parse(Ring) : SpaceId.Exterior;
        Assert.Equal(expectedSpace, arena.Simulation.Npcs.Single(n => n.Id == Tavar).Space);
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("party_inside"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(),
            session.Content, arena.Simulation.WorldTick, 0));
        var loaded = Arena.Resume(arena.Simulation.Setup, store.Load(SaveSlots.Manual("party_inside"),
            new LoadContext(session.Generator, session.Content, new Registry())));
        Assert.Equal(arena.Simulation.StateDigest(), loaded.Simulation.StateDigest());
        var before = loaded.Simulation.Npcs.Single(n => n.Id == Tavar);
        Assert.Equal(expectedSpace, before.Space);
        Assert.Equal(session.Setup.SpaceFor(expectedSpace).Terrain.HeightAtMm(before.Body.XMm, before.Body.ZMm), before.Body.YMm);
        loaded.Tick(20);
        Assert.Equal(expectedSpace, loaded.Simulation.Npcs.Single(n => n.Id == Tavar).Space);
        if (order == CompanionOrder.Wait)
            Assert.Equal(before.Body, loaded.Simulation.Npcs.Single(n => n.Id == Tavar).Body);
    }

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
