using System.Collections.Immutable;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// Canon's tracking skill, read without a world (canon skill.tracking; STEALTH_DETECTION_AND_THREAT.md §2's tracks and scent, §4's last
/// known position, §18's scent and wind): a mark ages and fades, a cold trail is Follow the Quarry's to read, Read the Ground says which
/// way and how long ago and how many, a scent is only read from downwind, and a reader with the last technique can say what the ground
/// refused to tell them.
/// </summary>
public class TrackingTests
{
    /// <summary>The tune the content gives it, as whole ticks at 20 Hz: fresh for 30 s, gone by 150 s, a scent by 60 s.</summary>
    private static readonly TrackingRules Rules = new(
        FreshTicks: 600, FadeTicks: 3_000, ScentTicks: 1_200, ScentConeMdeg: 90_000, MarkSpacingMm: 1_500, Techniques: TrackingRules.Default.Techniques);

    /// <summary>The canon's tracks' own noise (config.creature_behaviour): a walk carries 3 m, a run 8 m.</summary>
    private static readonly NoiseRules Noise = new(WalkMm: 3_000, RunMm: 8_000, SprintMm: 16_000, SwingMm: 10_000, BlowMm: 20_000, CallMm: 45_000);

    [Fact]
    public void AMarkFadesFromTheStrengthItWasLeftAt_ToNothing()
    {
        var print = Mark(SpoorKind.Print, TrackGround.Earth, tick: 0, strength: 100);
        Assert.Equal(100, Rules.StrengthAt(print, 0));
        Assert.Equal(50, Rules.StrengthAt(print, 1_500));
        Assert.Equal(0, Rules.StrengthAt(print, 3_000));
        Assert.Equal(0, Rules.StrengthAt(print, 10_000));
    }

    [Fact]
    public void AScent_HangsLessLongThanAPrint()
    {
        var print = Mark(SpoorKind.Print, TrackGround.Earth, tick: 0, strength: 100);
        var scent = Mark(SpoorKind.Scent, TrackGround.Earth, tick: 0, strength: 100);
        Assert.Equal(0, Rules.StrengthAt(scent, 1_200));    // a scent is gone by its own life's end
        Assert.Equal(60, Rules.StrengthAt(print, 1_200));   // the same age, on a mark the ground keeps

        // Half a scent's life: half of it left, and less than the print of the same age still says.
        var younger = Mark(SpoorKind.Scent, TrackGround.Earth, tick: 600, strength: 100);
        var printOfSameAge = younger with { Kind = SpoorKind.Print };
        Assert.Equal(50, Rules.StrengthAt(younger, 1_200));
        Assert.Equal(80, Rules.StrengthAt(printOfSameAge, 1_200));
    }

    [Fact]
    public void AColdTrailIsBeyondAReaderWhoCannotFollowIt_AndFollowTheQuarryRecoversIt()
    {
        // 15 s old: fresh, and anyone can see that something came this way.
        var cold = Mark(SpoorKind.Print, TrackGround.Earth, tick: 0, strength: 100);
        var site = new TrailSite(0, 0, 0, TrackGround.Earth, Tick: 900);

        var untrained = Reader();
        Assert.False(Tracking.Read(new[] { cold }, untrained, site, Rules).Readable);

        // Below canon's needs for Follow the Quarry (tracking 30, survival 30) the trail stays cold.
        Assert.False(Tracking.Read(new[] { cold }, Reader(tracking: 29, survival: 30, insight: 12), site, Rules).Readable);

        var followed = Tracking.Read(new[] { cold }, Reader(tracking: 30, survival: 30, insight: 12), site, Rules);
        Assert.True(followed.Readable);
        Assert.Equal(900, followed.AgeTicks);
    }

    [Fact]
    public void AFreshTrail_IsFoundWithoutTheSkill_AndSaysNothingUntilReadTheGround()
    {
        var print = Mark(SpoorKind.Print, TrackGround.Earth, tick: 0, bearing: 90_000);
        var site = new TrailSite(0, 0, 0, TrackGround.Earth, Tick: 300);

        var unread = Tracking.Read(new[] { print }, Reader(), site, Rules);
        Assert.True(unread.Readable);
        Assert.Equal(0, unread.BearingMdeg);
        Assert.Equal(0, unread.Count);

        // Read the Ground is canon's novice rung: tracking 15, insight 12.
        Assert.Equal(0, Tracking.Read(new[] { print }, Reader(tracking: 15, insight: 11), site, Rules).BearingMdeg);
        Assert.Equal(90_000, Tracking.Read(new[] { print }, Reader(tracking: 15, insight: 12), site, Rules).BearingMdeg);
    }

    [Fact]
    public void ReadTheGround_SaysWhichWay_HowLongAgo_AndHowMany()
    {
        var reader = Reader(tracking: 15, insight: 12);
        var site = new TrailSite(0, 0, 0, TrackGround.Mud, Tick: 400);
        var read = Tracking.Read(new[]
        {
            Mark(SpoorKind.Print, TrackGround.Mud, tick: 0, bearing: 180_000, leftBy: "wolves/0"),
            Mark(SpoorKind.Print, TrackGround.Mud, tick: 0, bearing: 180_000, leftBy: "wolves/1"),
            Mark(SpoorKind.Print, TrackGround.Mud, tick: 300, bearing: 181_500, leftBy: "wolves/0"),
        }, reader, site, Rules);

        Assert.True(read.Readable);
        Assert.Equal(181_500, read.BearingMdeg);   // the freshest mark is the way they were going
        Assert.Equal(100, read.AgeTicks);
        Assert.Equal(2, read.Count);                // two animals, three marks
        Assert.False(read.Uncertain);
    }

    [Fact]
    public void AScentIsReadOnlyFromDownwind_AndAPrintIsReadFromAnywhere()
    {
        var gusting = Mark(SpoorKind.Scent, TrackGround.Earth, tick: 0, bearing: 0);
        // The wind blows from +Z (bearing 180): it carries what lies behind the reader, at -Z, to them.
        var downwind = new TrailSite(0, 0, WindFromMdeg: 180_000, TrackGround.Earth, Tick: 100);
        var upwind = new TrailSite(0, 0, WindFromMdeg: 0, TrackGround.Earth, Tick: 100);
        Assert.True(Tracking.Reaches(downwind, gusting with { ZMm = -5_000 }, Rules));
        Assert.False(Tracking.Reaches(upwind, gusting with { ZMm = -5_000 }, Rules));
        // And the read itself finds nothing at all when the wind is wrong, though the ground under the animal is untouched.
        Assert.False(Tracking.Read(new[] { gusting with { ZMm = -5_000 } }, Reader(tracking: 15, insight: 12), upwind, Rules).Readable);
        Assert.True(Tracking.Read(new[] { gusting with { ZMm = -5_000 } }, Reader(tracking: 15, insight: 12), downwind, Rules).Readable);
        // A print is on the ground: no wind carries it away.
        Assert.True(Tracking.Reaches(upwind, Mark(SpoorKind.Print, TrackGround.Earth, tick: 0), Rules));
    }

    [Fact]
    public void TheGroundThatKeepsNothing_IsReportedAsUncertainOnlyByTheLastTechnique()
    {
        var reader = Reader(tracking: 60, insight: 30);
        var stone = new TrailSite(0, 0, 0, TrackGround.Stone, Tick: 100);

        var honest = Tracking.Read(Array.Empty<SpoorMark>(), reader, stone, Rules);
        Assert.False(honest.Readable);
        Assert.True(honest.Uncertain);
        Assert.Equal("The ground here keeps nothing", honest.Reason);

        // The same reader without What the Trail Refuses to Say gets the same nothing, and no doubt with it.
        var sure = Tracking.Read(Array.Empty<SpoorMark>(), Reader(tracking: 45, insight: 20), stone, Rules);
        Assert.False(sure.Readable);
        Assert.False(sure.Uncertain);
        Assert.Equal("", sure.Reason);
    }

    [Fact]
    public void ATrailOlderThanTheReaderCanDate_IsReadAndOwnedAsUncertain()
    {
        var cold = Mark(SpoorKind.Print, TrackGround.Earth, tick: 0, bearing: 90_000, strength: 100);
        var site = new TrailSite(0, 0, 0, TrackGround.Earth, Tick: 900);

        // Follow the Quarry and Read the Ground recover the trail; the age is beyond them, and they do not know it.
        var confident = Tracking.Read(new[] { cold }, Reader(tracking: 30, survival: 30, insight: 12), site, Rules);
        Assert.True(confident.Readable);
        Assert.Equal(900, confident.AgeTicks);
        Assert.False(confident.Uncertain);
        Assert.Equal("", confident.Reason);

        var honest = Tracking.Read(new[] { cold }, Reader(tracking: 60, survival: 30, insight: 30), site, Rules);
        Assert.True(honest.Readable);
        Assert.True(honest.Uncertain);
        Assert.Equal("Older than I can date", honest.Reason);
    }

    [Theory]
    [InlineData(0, true, 3_000L)]
    [InlineData(0, false, 8_000L)]
    [InlineData(1, true, 2_550L)]
    [InlineData(4, false, 5_600L)]
    [InlineData(8, true, 1_650L)]
    public void FollowingATrailIsAWalk_NotTheRunAnOrdinaryFollowHasToMake(int stealthLevel, bool walkTheirRoad, long carries)
    {
        Assert.Equal(carries, Tracking.FollowingNoiseMm(Noise, stealthLevel, walkTheirRoad));
    }

    [Fact]
    public void AFollowedStep_IsNotHeardByAWolfFiveMetresOff_TheHurryIsHeard()
    {
        var wolf = new Body(5_000, 0, 0, 0);
        var quietly = new Noise(0, 0, Tracking.FollowingNoiseMm(Noise, 0, walkTheirRoad: true));
        var hurrying = new Noise(0, 0, Tracking.FollowingNoiseMm(Noise, 0, walkTheirRoad: false));
        Assert.False(Perception.Hears(wolf, hearingMm: 20_000, quietly));
        Assert.True(Perception.Hears(wolf, hearingMm: 20_000, hurrying));
    }

    [Fact]
    public void ACatalogueIsRefused_WithAGradeCanonDoesNotUse()
    {
        var broken = TrackingRules.Default with
        {
            Techniques = TrackingRules.Default.Techniques.SetItem(Tracking.ReadTheGround,
                TrackingRules.Default.Techniques[Tracking.ReadTheGround] with { Grade = "X" }),
        };
        Assert.Throws<FormatException>(() => broken.Validated());
        Assert.Same(TrackingRules.Default, TrackingRules.Default.Validated());
        Assert.Equal(7, TrackingRules.Default.Techniques.Count);
    }

    private static SpoorMark Mark(SpoorKind kind, TrackGround ground, long tick, int bearing = 0, int strength = 100, string leftBy = "wolves/0") =>
        new(kind, ground, 0, 0, bearing, tick, strength) { LeftBy = leftBy };

    /// <summary>A reader's competences, by the names canon's needs column uses. Everything unnamed is none.</summary>
    private static Tracker Reader(int tracking = 0, int insight = 0, int survival = 0, int stealth = 0) =>
        new(new Dictionary<string, int>
        {
            ["tracking"] = tracking,
            ["insight"] = insight,
            ["survival"] = survival,
            ["stealth"] = stealth,
        }.ToImmutableSortedDictionary(StringComparer.Ordinal));
}
