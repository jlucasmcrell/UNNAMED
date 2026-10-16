// UNNAMED Application tests - the capture's start position must be unreachable in ordinary play (M8 D10, P2i)
// A capture may open the player where it stands so it does not have to walk him there. That is a harness
// affordance, and the one thing that must be true of it is that ordinary play cannot reach it.

using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using Xunit;

namespace UNNAMED.Application.Tests;

public class CaptureStartTests
{
    private const string Barrowworks = "region.barrowworks";

    private static GameSession Boot(TempProfile profile, string region, long? startX = null, long? startZ = null) =>
        GameSession.Boot(new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root)
        {
            LockProfile = true,
            RegionId = region,
            Barrowworks = region == Barrowworks,
            StartXMm = startX,
            StartZMm = startZ,
        });

    [Fact]
    public void WithoutACapture_ThePlayerOpensAtTheRegionsOwnSpawn()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile, "region.starting_frontier");
        var simulation = session.NewGame("Tester", seed: 0x5eed_1234);

        var spawn = session.Setup.Layout.Spawn;
        Assert.Equal(spawn.XMm, simulation.Player.Body.XMm);
        Assert.Equal(spawn.ZMm, simulation.Player.Body.ZMm);
    }

    [Fact]
    public void ACapture_OpensThePlayerWhereItAsked()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile, Barrowworks, startX: 50_000, startZ: 28_000);
        var simulation = session.NewGame("Tester", seed: 0x5eed_1234);

        Assert.Equal(50_000, simulation.Player.Body.XMm);
        Assert.Equal(28_000, simulation.Player.Body.ZMm);
    }

    /// <summary>
    /// The affordance names a place, not a space. A capture opening the player inside the Barrowworks still
    /// opens him in the Barrowworks, because the space comes from the region - so this cannot be used to stand
    /// an interior position in the exterior, or the reverse.
    /// </summary>
    [Fact]
    public void ACapturesPosition_DoesNotChangeWhichSpaceThePlayerIsIn()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile, Barrowworks, startX: 50_000, startZ: 28_000);
        var simulation = session.NewGame("Tester", seed: 0x5eed_1234);

        Assert.False(simulation.PlayerInExterior);
        Assert.False(session.Setup.Layout.SpaceId.IsExterior);
    }

    /// <summary>
    /// And with no option at all - a normal launch - the player is in the exterior at the region's spawn, so
    /// nothing about ordinary play depends on any of this existing.
    /// </summary>
    [Fact]
    public void AnOrdinaryRegion_IsStillTheExteriorAtItsOwnSpawn()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile, "region.starting_frontier");
        var simulation = session.NewGame("Tester", seed: 0x5eed_1234);

        Assert.True(simulation.PlayerInExterior);
        Assert.Equal(SpaceId.Exterior, session.Setup.Layout.SpaceId);
        Assert.Equal(session.Setup.Layout.Spawn.XMm, simulation.Player.Body.XMm);
    }
}
