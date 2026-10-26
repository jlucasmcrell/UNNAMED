using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using Xunit;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Kera offers the ring BEFORE you go in (review 3, B3e). Canon has her tell you about it and send you; the offer
/// used to wait on a flag set by reading the notice INSIDE the intake hall, so it could only be shown after you had
/// already been in - and its text said "You went in".
///
/// The notice is now a fact of a world the character can reach: seeded when the interior is co-loaded, absent when it
/// is not, which is what keeps the option-off guarantee.
/// </summary>
public class KeraOfferTests
{
    private const string Notice = "world.barrowworks.ring_accessible";
    private const string Hall = "location.barrowworks.intake_hall";

    private static long NoticeValue(GameSession session)
    {
        var ring = session.Setup.Layouts.GetValueOrDefault(SpaceId.Parse("space.barrowworks"));
        if (ring is null)
            return 0;
        var hall = ring.Locations.Single(l => l.Id == Hall);
        var cell = CellKey.OfInterior(ring.SpaceId, hall.XMm / 1000.0, hall.ZMm / 1000.0);
        return session.Simulation!.World.GetFlag(cell, Notice);
    }

    [Fact]
    public void WithTheGateOn_TheNoticeIsReadableBeforeGoingIn()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile, barrowworks: true);
        session.NewGame(Harness.Named("Wanderer"), seed: 42);

        // The character has not entered the ring; the offer must still be available to her.
        Assert.True(session.Simulation!.PlayerInExterior);
        Assert.Equal(1, NoticeValue(session));
    }

    [Fact]
    public void WithTheGateOff_NothingIsSeeded_AndTheOfferStaysClosed()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        session.NewGame(Harness.Named("Wanderer"), seed: 42);

        Assert.DoesNotContain(SpaceId.Parse("space.barrowworks"), session.Setup.Layouts.Keys);
        Assert.Equal(0, NoticeValue(session));
    }
}
