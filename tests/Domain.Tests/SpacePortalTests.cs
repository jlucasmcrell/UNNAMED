using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// WORLD_ARCHITECTURE.md §8 and M8 D10: a space is entered at the anchor of the portal used and left
/// back out of the same one. Several portals may lead to one space, and the space is one identity.
/// </summary>
public class SpacePortalTests
{
    /// <summary>The reach the space system uses to decide a body is at a portal.</summary>
    private const long Reach = 2_000;

    // The Barrowworks' two entrances, from the frozen M8 level design: the hill above Blackvein and the
    // Sinking Moss drain mouth. They are the design's own example of why one return vector is not enough.
    private static readonly SpaceId Barrowworks = SpaceId.Parse("space.barrowworks");

    private static SpacePortalSet TwoEntrances() => new SpacePortalSet(
    [
        new SpacePortal("portal.barrowworks.threshold", Barrowworks, ExteriorXMm: 438_000, ExteriorZMm: 648_000,
            InteriorXMm: 0, InteriorZMm: 0),
        new SpacePortal("portal.barrowworks.drain", Barrowworks, ExteriorXMm: 512_000, ExteriorZMm: 604_000,
            InteriorXMm: -42_000, InteriorZMm: 28_000),
    ]).Validate();

    [Fact]
    public void BothEntrances_LeadToTheSameSpace()
    {
        var portals = TwoEntrances();
        var entrances = portals.Into(Barrowworks);

        Assert.Equal(2, entrances.Length);
        Assert.All(entrances, e => Assert.Equal(Barrowworks, e.Space));
    }

    [Fact]
    public void EnteringAnchorsTheBodyAtThatPortal()
    {
        var portals = TwoEntrances();
        var viaDrain = portals.Enter("portal.barrowworks.drain");

        Assert.Equal(-42_000, viaDrain.XMm);
        Assert.Equal(28_000, viaDrain.ZMm);
        Assert.Equal("portal.barrowworks.drain", viaDrain.Traversal.PortalKey);
    }

    /// <summary>
    /// The way out is the door underfoot, not the one the body walked in through. The design's named failure is
    /// the reverse: a character who came in by the drain being snapped to the formal entrance because a single
    /// global return vector was reused.
    /// </summary>
    [Fact]
    public void LeavingIsByThePortalTheBodyStandsAt_NotTheOneItCameInThrough()
    {
        var portals = TwoEntrances();
        var (dx, dz, _) = portals.Enter("portal.barrowworks.drain");
        var (tx, tz, _) = portals.Enter("portal.barrowworks.threshold");

        Assert.Equal("portal.barrowworks.drain", portals.AtInterior(dx, dz, Reach)?.Key);
        Assert.Equal("portal.barrowworks.threshold", portals.AtInterior(tx, tz, Reach)?.Key);
        Assert.NotEqual(portals.AtInterior(dx, dz, Reach), portals.AtInterior(tx, tz, Reach));
    }

    [Fact]
    public void RoundTrip_ThroughEitherEntrance_EndsWhereItStarted()
    {
        var portals = TwoEntrances();

        foreach (var portal in portals.Into(Barrowworks))
        {
            var portalAt = portals.AtInterior(portal.InteriorXMm, portal.InteriorZMm, Reach);

            Assert.Equal(portal.Key, portalAt?.Key);
            Assert.Equal((portal.ExteriorXMm, portal.ExteriorZMm), (portalAt!.ExteriorXMm, portalAt.ExteriorZMm));
        }
    }

    /// <summary>
    /// Standing nowhere near a portal answers with nothing. It does not throw, because the space system promises
    /// refusals as reasons and a body in the middle of a room is an ordinary thing to be.
    /// </summary>
    [Fact]
    public void StandingAtNoPortal_HasNoWayOut()
    {
        // The threshold anchors at the origin in this fixture, so stand clear of both.
        Assert.Null(TwoEntrances().AtInterior(100_000, 100_000, Reach));
    }

    /// <summary>
    /// And a portal key that is no longer content cannot crash anything, because nothing looks a portal up BY key
    /// to leave. This is the shape a renamed portal, or a save loaded with the gate off, used to throw
    /// <c>KeyNotFoundException</c> on.
    /// </summary>
    [Fact]
    public void AStoredPortalKeyIsNotConsultedToLeave()
    {
        var portals = TwoEntrances();
        var (x, z, _) = portals.Enter("portal.barrowworks.drain");
        var stale = new SpaceTraversal("portal.nowhere");

        Assert.False(stale.IsNone);
        Assert.Equal("portal.barrowworks.drain", portals.AtInterior(x, z, Reach)?.Key);
    }

    [Fact]
    public void TwoPortalsSharingAKey_AreRefused()
    {
        // The key is what a save records, so a duplicate makes an exit ambiguous.
        var duplicated = new SpacePortalSet(
        [
            new SpacePortal("portal.same", Barrowworks, 0, 0, 0, 0),
            new SpacePortal("portal.same", Barrowworks, 10_000, 10_000, 10_000, 10_000),
        ]);

        Assert.Throws<ArgumentException>(() => duplicated.Validate());
    }

    [Fact]
    public void APortalIntoTheExterior_IsRefused()
    {
        // The exterior is not entered through a portal; a record saying otherwise is a content error.
        Assert.Throws<ArgumentException>(() =>
            new SpacePortal("portal.bad", SpaceId.Exterior, 0, 0, 0, 0).Validate());
    }

    [Fact]
    public void APortalNeedsAKey() =>
        Assert.Throws<ArgumentException>(() =>
            new SpacePortal("", Barrowworks, 0, 0, 0, 0).Validate());
}
