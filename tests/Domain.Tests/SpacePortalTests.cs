using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// WORLD_ARCHITECTURE.md §8 and M8 D10: a space is entered at the anchor of the portal used and left
/// back out of the same one. Several portals may lead to one space, and the space is one identity.
/// </summary>
public class SpacePortalTests
{
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

    [Fact]
    public void LeavingReturnsToTheAnchorActuallyUsed_NotTheOtherOne()
    {
        // The design's named failure: a player who came in by the drain must not be snapped to the
        // formal entrance because a single global return vector was reused.
        var portals = TwoEntrances();

        var (_, _, viaDrain) = portals.Enter("portal.barrowworks.drain");
        var (_, _, viaThreshold) = portals.Enter("portal.barrowworks.threshold");

        Assert.Equal((512_000L, 604_000L), portals.Leave(viaDrain));
        Assert.Equal((438_000L, 648_000L), portals.Leave(viaThreshold));
        Assert.NotEqual(portals.Leave(viaDrain), portals.Leave(viaThreshold));
    }

    [Fact]
    public void RoundTrip_ThroughEitherEntrance_EndsWhereItStarted()
    {
        var portals = TwoEntrances();

        foreach (var portal in portals.Into(Barrowworks))
        {
            var (_, _, traversal) = portals.Enter(portal.Key);
            var (x, z) = portals.Leave(traversal);

            Assert.Equal((portal.ExteriorXMm, portal.ExteriorZMm), (x, z));
        }
    }

    [Fact]
    public void LeavingWithoutHavingEntered_IsRefused()
    {
        // A refused exit is better than an invented anchor.
        Assert.Throws<InvalidOperationException>(() => TwoEntrances().Leave(SpaceTraversal.None));
    }

    [Fact]
    public void LeavingByAPortalThatIsNotDeclared_IsRefused() =>
        Assert.Throws<KeyNotFoundException>(() => TwoEntrances().Leave(new SpaceTraversal("portal.nowhere")));

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
