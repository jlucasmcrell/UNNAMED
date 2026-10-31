using System.Collections.Immutable;
using UNNAMED.Domain.Social;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// The schedule bands: an authored band wins over the work hours, so a day can hold more than one post (quay in the morning, cistern
/// at midday, the work site after). Work hours still hold for every hour no band covers, and a day that only bands its margins - the
/// shape every existing routine used - behaves exactly as it did before.
/// </summary>
public class NpcScheduleBandTests
{
    private static readonly NpcLifeRules Rules = NpcLifeRules.Default;   // night 22:00-06:00

    private static RoutineDefinition Routine(int from, int to, params (int From, int To, string Anchor)[] bands)
    {
        var anchors = new Dictionary<string, NpcAnchor>(StringComparer.Ordinal)
        {
            [NpcAnchorKinds.Home] = new(1_000, 2_000, 0),
            [NpcAnchorKinds.Work] = new(3_000, 4_000, 0),
            ["quay"] = new(5_000, 6_000, 0),
            ["cistern"] = new(7_000, 8_000, 0),
            ["lane"] = new(9_000, 10_000, 0),
            ["inn"] = new(11_000, 12_000, 0),
        };
        return new RoutineDefinition(
            "routine.test.day",
            "npc.test.day",
            "settlement.test",
            "porter",
            anchors.ToImmutableSortedDictionary(StringComparer.Ordinal),
            from,
            to)
        {
            Schedule = bands.Select(b => new NpcScheduleBand(b.From, b.To, b.Anchor)).ToImmutableArray(),
        };
    }

    [Fact]
    public void AMultiBandDay_IsWhereTheBandsSay()
    {
        // the day the Steps needs: the quay at first light, the cistern through the middle, the flights after
        var routine = Routine(6, 19, (6, 9, "quay"), (9, 15, "cistern"), (15, 19, NpcAnchorKinds.Work));

        Assert.Equal(NpcActivities.Idle, routine.ActivityAt(7, Rules));
        Assert.Equal(new NpcAnchor(5_000, 6_000, 0), routine.AnchorFor(routine.ActivityAt(7, Rules), 7));

        Assert.Equal(NpcActivities.Idle, routine.ActivityAt(10, Rules));
        Assert.Equal(new NpcAnchor(7_000, 8_000, 0), routine.AnchorFor(routine.ActivityAt(10, Rules), 10));

        Assert.Equal(NpcActivities.Work, routine.ActivityAt(16, Rules));
        Assert.Equal(new NpcAnchor(3_000, 4_000, 0), routine.AnchorFor(routine.ActivityAt(16, Rules), 16));
    }

    [Fact]
    public void ABandEatsTheWorkHoursItCovers_AndTheHoursAroundItStillFollowTheRules()
    {
        var routine = Routine(6, 19, (6, 9, "quay"), (9, 15, "cistern"), (15, 19, NpcAnchorKinds.Work));

        // 5 is before the day and before night ends: asleep at home
        Assert.Equal(NpcActivities.Sleep, routine.ActivityAt(5, Rules));
        Assert.Equal(new NpcAnchor(1_000, 2_000, 0), routine.AnchorFor(routine.ActivityAt(5, Rules), 5));

        // 20 is after the last band and after work: about their day, at home
        Assert.Equal(NpcActivities.Idle, routine.ActivityAt(20, Rules));
        Assert.Equal(new NpcAnchor(1_000, 2_000, 0), routine.AnchorFor(routine.ActivityAt(20, Rules), 20));

        // 22 is night: asleep
        Assert.Equal(NpcActivities.Sleep, routine.ActivityAt(22, Rules));
    }

    [Fact]
    public void ASingleWindowDayWithMarginBands_IsUnchanged()
    {
        // the shape every routine used before bands could override: work 8-20, bands only at the edges of the day
        var routine = Routine(8, 20, (6, 8, "lane"), (20, 22, "inn"));

        Assert.Equal(NpcActivities.Idle, routine.ActivityAt(7, Rules));
        Assert.Equal(new NpcAnchor(9_000, 10_000, 0), routine.AnchorFor(NpcActivities.Idle, 7));

        Assert.Equal(NpcActivities.Work, routine.ActivityAt(10, Rules));
        Assert.Equal(NpcActivities.Work, routine.ActivityAt(19, Rules));

        Assert.Equal(NpcActivities.Idle, routine.ActivityAt(21, Rules));
        Assert.Equal(new NpcAnchor(11_000, 12_000, 0), routine.AnchorFor(NpcActivities.Idle, 21));

        Assert.Equal(NpcActivities.Sleep, routine.ActivityAt(23, Rules));
        Assert.Equal(NpcActivities.Sleep, routine.ActivityAt(3, Rules));
    }

    [Fact]
    public void ABandNamingTheWorkSite_IsStillWork()
    {
        var routine = Routine(7, 19, (7, 12, NpcAnchorKinds.Work));

        Assert.Equal(NpcActivities.Work, routine.ActivityAt(9, Rules));
        Assert.Equal(new NpcAnchor(3_000, 4_000, 0), routine.AnchorFor(NpcActivities.Work, 9));
    }

    [Fact]
    public void AHomeBandInDaylight_IsHome_AndAtNight_SleepsThere()
    {
        var daylight = Routine(7, 19, (16, 19, NpcAnchorKinds.Home));
        Assert.Equal(NpcActivities.Idle, daylight.ActivityAt(17, Rules));
        Assert.Equal(new NpcAnchor(1_000, 2_000, 0), daylight.AnchorFor(NpcActivities.Idle, 17));

        var night = Routine(7, 19, (22, 23, NpcAnchorKinds.Home));
        Assert.Equal(NpcActivities.Sleep, night.ActivityAt(22, Rules));
        Assert.Equal(new NpcAnchor(1_000, 2_000, 0), night.AnchorFor(NpcActivities.Sleep, 22));
    }

    [Fact]
    public void NoBandsAtAll_IsThePlainWorkDayItAlwaysWas()
    {
        var routine = Routine(6, 18);

        Assert.Equal(NpcActivities.Sleep, routine.ActivityAt(5, Rules));
        Assert.Equal(NpcActivities.Work, routine.ActivityAt(6, Rules));
        Assert.Equal(NpcActivities.Work, routine.ActivityAt(17, Rules));
        Assert.Equal(NpcActivities.Idle, routine.ActivityAt(18, Rules));
        Assert.Equal(NpcActivities.Sleep, routine.ActivityAt(23, Rules));
    }
}
