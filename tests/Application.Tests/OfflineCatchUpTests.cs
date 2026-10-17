namespace UNNAMED.Application.Tests;

/// <summary>WP11: an absence on the wall clock becomes world ticks, capped at one Reckoning (DN1); a clock set back asks for nothing.</summary>
public class OfflineCatchUpTests
{
    private const int TicksPerSecond = 20;
    private const long Reckoning = 183 * 57_600L;
    private static readonly DateTimeOffset Saved = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnAbsence_IsTwentyTicksASecond_OfRealTime()
    {
        var catchUp = OfflineCatchUp.Compute(Saved, Saved.AddMinutes(48).AddMilliseconds(125), TicksPerSecond, Reckoning);
        Assert.Equal(new CatchUp(57_602, 57_602), catchUp);   // 48 real minutes is one game day; 125 ms is two whole ticks
        Assert.False(catchUp.Capped);
    }

    [Fact]
    public void AClockMovedBack_GivesNothing()
    {
        Assert.Equal(CatchUp.None, OfflineCatchUp.Compute(Saved, Saved.AddDays(-3), TicksPerSecond, Reckoning));
        Assert.Equal(CatchUp.None, OfflineCatchUp.Compute(Saved, Saved, TicksPerSecond, Reckoning));
        Assert.Equal(CatchUp.None, OfflineCatchUp.Compute(Saved, Saved.AddTicks(-1), TicksPerSecond, Reckoning));
    }

    [Fact]
    public void TheCap_HonoursOneReckoning_AndCountsALongerAbsenceAsExactlyOne()
    {
        var atCap = OfflineCatchUp.Compute(Saved, Saved.AddMinutes(48 * 183), TicksPerSecond, Reckoning);
        Assert.Equal(new CatchUp(Reckoning, Reckoning), atCap);
        Assert.False(atCap.Capped);

        var month = OfflineCatchUp.Compute(Saved, Saved.AddDays(30), TicksPerSecond, Reckoning);
        Assert.Equal((30L * 86_400 * TicksPerSecond, Reckoning), (month.RequestedTicks, month.HonouredTicks));
        Assert.True(month.Capped);

        var decades = OfflineCatchUp.Compute(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, TicksPerSecond, Reckoning);
        Assert.Equal(Reckoning, decades.HonouredTicks);
        Assert.Equal(0, OfflineCatchUp.Compute(Saved, Saved.AddDays(1), TicksPerSecond, 0).HonouredTicks);
    }

    [Fact]
    public void TheGamesOwnContent_CapsAtOneReckoning_AndGivesTenSecondsOfGrace()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        Assert.Equal(Reckoning, session.Setup.Offline.CapTicks);
        Assert.Equal(10 * TicksPerSecond, session.Setup.Offline.LoginGraceTicks);
    }
}
