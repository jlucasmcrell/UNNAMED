// UNNAMED Domain Tests - the round a guard walks, and the wind a nose reads (STEALTH.md 4.6, 8.2)
// No Godot references

using System.Collections.Immutable;
using UNNAMED.Domain.Creatures;

namespace UNNAMED.Domain.Tests;

public class PatrolAndWindTests
{
    private static PatrolSite Round(string key = "patrol.test.yard", long period = 300) =>
        new(key, "npc.test.guard", ImmutableArray.Create(
            new PatrolStop(0, 0),
            new PatrolStop(10_000, 0),
            new PatrolStop(10_000, 10_000)), period)
        {
            BlindSpotTicks = 60,
        };

    [Fact]
    public void APatrol_WalksToItsStop_ThenStandsOnIt()
    {
        var patrol = Round();
        long step = 500;   // mm a tick: 10 m of ground takes 20 ticks

        // Each stop owns a third of the round: the walk in, then standing on it.
        var walking = PatrolRules.At(patrol, 1, step);
        Assert.True(walking.Walking);
        Assert.Equal(0, walking.Index);
        Assert.True(walking.XMm > 0 && walking.XMm < 10_000, $"walking position {walking.XMm} should be between the stops");

        var standing = PatrolRules.At(patrol, 60, step);
        Assert.False(standing.Walking);
        Assert.Equal((10_000L, 0L), (standing.XMm, standing.ZMm));

        // The second stop is where the round has her a third of the way in.
        var second = PatrolRules.At(patrol, 100 + 60, step);
        Assert.False(second.Walking);
        Assert.Equal((10_000L, 10_000L), (second.XMm, second.ZMm));
    }

    [Fact]
    public void APatrol_ComesRoundAgain_AfterItsPeriod()
    {
        var patrol = Round();
        var first = PatrolRules.At(patrol, 5, 500);
        var again = PatrolRules.At(patrol, 5 + patrol.PeriodTicks, 500);
        Assert.Equal(first, again);
    }

    [Fact]
    public void AnAuthoredWind_WinsOverTheDay_InsideItsGround_AndTheDayRulesOutsideIt()
    {
        var winds = ImmutableArray.Create(new WindSite("wind.gorge", 500_000, 500_000, 270_000, 12) { RadiusMm = 30_000 });
        long day = 90_000;

        Assert.Equal(270_000, WindRules.BearingAt(day, winds, 510_000, 500_000));     // inside the gorge
        Assert.Equal(day, WindRules.BearingAt(day, winds, 600_000, 500_000));         // downwind of it: the day's air
        Assert.Equal(12, WindRules.StrengthAt(3, winds, 510_000, 500_000));
        Assert.Equal(3, WindRules.StrengthAt(3, winds, 600_000, 500_000));
    }

    [Fact]
    public void TheNose_FollowsTheAir_SoTheAuthoredWindChangesWhatIsSmelled()
    {
        // Air blowing north comes from the south: the body it carries is the one south of the nose. Air blowing west carries from the east.
        long noseX = 0, noseZ = 0, southX = 0, southZ = -10_000, eastX = 10_000, eastZ = 0;
        long northMdeg = 0;      // blows toward +Z
        long westMdeg = 270_000; // blows toward -X
        Assert.True(ScentRules.Carries(northMdeg, noseX, noseZ, southX, southZ));
        Assert.False(ScentRules.Carries(northMdeg, noseX, noseZ, eastX, eastZ));
        Assert.True(ScentRules.Carries(westMdeg, noseX, noseZ, eastX, eastZ));

        // And the wind the ground gives is the one the nose uses: the same nose, the same body, a different answer because the air moved.
        var winds = ImmutableArray.Create(new WindSite("wind.gorge", 0, 0, northMdeg, 10) { RadiusMm = 50_000 });
        Assert.Equal(northMdeg, WindRules.BearingAt(westMdeg, winds, noseX, noseZ));
        // The same nose and the same body: the ground under it turned the air, so what is smelled changed with it.
        Assert.False(ScentRules.Carries(westMdeg, noseX, noseZ, southX, southZ));
        Assert.True(ScentRules.Carries(WindRules.BearingAt(westMdeg, winds, noseX, noseZ), noseX, noseZ, southX, southZ));
    }
}
