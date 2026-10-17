using System.Collections.Immutable;
using UNNAMED.Domain.Social;

namespace UNNAMED.Domain.Tests.Social;

/// <summary>NPC life's rules, pure (WP12, §2.13): fixed daily schedules, departures and arrivals (DN4), job output, and no cap.</summary>
public class NpcLifeTests
{
    private static readonly NpcLifeRules Rules = NpcLifeRules.Default;

    private static RoutineDefinition Routine(string id, string? npc = null, int from = 7, int to = 19, NpcJob? job = null) =>
        new(id, npc, "town", "worker", new Dictionary<string, NpcAnchor>
        {
            [NpcAnchorKinds.Home] = new(0, 0, 0),
            [NpcAnchorKinds.Work] = new(10_000, 0, 0),
        }.ToImmutableSortedDictionary(StringComparer.Ordinal), from, to)
        { PeopleId = npc is null ? "people.veth" : null, Job = job };

    private static NpcHourContext At(long hour) => new(hour, hour * 2_400, hour / 24, (int)(hour % 24));

    private static ImmutableSortedDictionary<string, RoutineDefinition> Routines(params RoutineDefinition[] routines) =>
        routines.ToImmutableSortedDictionary(r => r.Id, r => r, StringComparer.Ordinal);

    private static NpcLifeState Begin(ImmutableSortedDictionary<string, RoutineDefinition> routines, long hour) =>
        NpcLifeEngine.Begin(routines, Rules, At(hour), (r, g) => $"Name {g}", _ => null);

    private static NpcLifeState Live(NpcLifeState state, ImmutableSortedDictionary<string, RoutineDefinition> routines, long toHour,
        Func<RoutineDefinition, bool>? canArrive = null)
    {
        while (state.Hour < toHour)
            state = NpcLifeEngine.Hour(state, routines, Rules, At(state.Hour + 1), (r, g) => $"Name {g}", canArrive ?? (_ => true)).State;
        return state;
    }

    [Fact]
    public void AWorkerWorks_InWorkHours_AndSleeps_AtNight()
    {
        var routines = Routines(Routine("routine.town.smith", "npc.town.smith"));
        var life = Begin(routines, 8);
        Assert.Equal(NpcActivities.Work, life.Npcs["npc.town.smith"].Activity);
        Assert.Equal(NpcActivities.Sleep, Live(life, routines, 26).Npcs["npc.town.smith"].Activity);   // 02:00 the next night
        Assert.Throws<ArgumentOutOfRangeException>(() => NpcLifeEngine.Hour(life, routines, Rules, At(10), (r, g) => null, _ => true));
    }

    /// <summary>
    /// Owner, 2026-10-17: a fixed daily schedule, the same every day - work in work hours, a named spot in the bands that name one, home the
    /// rest, asleep there at night - and nothing else: no needs, no meals, no visits, no shelter.
    /// </summary>
    [Fact]
    public void TheDay_IsAFixedSchedule_TheSameEveryDay()
    {
        var routine = Routine("routine.town.smith", "npc.town.smith") with
        {
            Anchors = Routine("x").Anchors.Add("bench", new NpcAnchor(3_000, 3_000, 90_000)),
            Schedule = ImmutableArray.Create(new NpcScheduleBand(19, 21, "bench"), new NpcScheduleBand(5, 6, "bench")),
        };
        var routines = Routines(routine);
        var life = Begin(routines, 0);
        var days = new List<string>();
        for (long h = 1; h <= 72; h++)
        {
            life = Live(life, routines, h);
            days.Add(life.Npcs["npc.town.smith"].Activity);
        }
        Assert.Equal(days.Take(24), days.Skip(24).Take(24));
        Assert.Equal(days.Take(24), days.Skip(48));
        Assert.Equal(new[] { "idle", "sleep", "work", "idle", "sleep" },
            new[] { 5, 3, 12, 20, 23 }.Select(hour => routine.ActivityAt(hour, Rules)));
        Assert.Equal(new NpcAnchor(3_000, 3_000, 90_000), routine.AnchorFor(NpcActivities.Idle, 20));
        Assert.Equal(routine.Anchors[NpcAnchorKinds.Home], routine.AnchorFor(NpcActivities.Idle, 6));
        Assert.Equal(routine.Anchors[NpcAnchorKinds.Home], routine.AnchorFor(NpcActivities.Sleep, 2));
        Assert.Equal(new[] { "work", "sleep", "idle" }, NpcActivities.All);
    }

    [Fact]
    public void ANamedNpc_NeverLeaves_AndAResident_IsReplaced3To7DaysLater()
    {
        var routines = Routines(Routine("routine.town.smith", "npc.town.smith"), Routine("routine.town.porter"));
        var life = Begin(routines, 8);
        Assert.Null(NpcLifeEngine.Depart(life, routines, Rules, "npc.town.smith", 0, 5, out var refusal));
        Assert.Contains("essential", refusal);

        for (ulong sample = 0; sample < 20; sample++)
        {
            var left = NpcLifeEngine.Depart(life, routines, Rules, "routine.town.porter", 0, sample, out _)!;
            var vacancy = left.Vacancies["routine.town.porter"];
            Assert.InRange(vacancy.ArrivalDay, 3, 7);
            Assert.False(left.Npcs.ContainsKey("routine.town.porter"));
            var refilled = Live(left, routines, vacancy.ArrivalDay * 24 + 1);
            var arrival = refilled.Npcs["routine.town.porter"];
            Assert.Equal(1, arrival.Generation);
            Assert.Equal(vacancy.ArrivalDay, arrival.ArrivedDay);
            Assert.Empty(refilled.Vacancies);
        }
    }

    [Fact]
    public void NobodyArrives_WhereTheyWouldBeSeen()
    {
        var routines = Routines(Routine("routine.town.porter"));
        var left = NpcLifeEngine.Depart(Begin(routines, 8), routines, Rules, "routine.town.porter", 0, 0, out _)!;
        long due = left.Vacancies["routine.town.porter"].ArrivalDay;
        var watched = Live(left, routines, (due + 2) * 24, canArrive: _ => false);
        Assert.True(watched.Vacancies.ContainsKey("routine.town.porter"));
        Assert.Equal(due + 2, Live(watched, routines, (due + 3) * 24).Npcs["routine.town.porter"].ArrivedDay);   // the first hour they may come
    }

    [Fact]
    public void JobOutput_FollowsWhoHeldTheJob_DayByDay()
    {
        var job = new NpcJob("market.town", "item.material.iron_ingot", 2);
        var routines = Routines(Routine("routine.town.miner", job: job));
        var life = Begin(routines, 8);
        Assert.Single(NpcLifeEngine.JobOutput(life, routines, 1, 1_000));
        var left = NpcLifeEngine.Depart(life, routines, Rules, "routine.town.miner", 2, 0, out _)!;
        long due = left.Vacancies["routine.town.miner"].ArrivalDay;
        Assert.Single(NpcLifeEngine.JobOutput(left, routines, 1, 1_000));   // a day before they left: the job was held
        Assert.Empty(NpcLifeEngine.JobOutput(left, routines, 3, 1_000));
        var refilled = Live(left, routines, due * 24 + 1);
        Assert.Empty(NpcLifeEngine.JobOutput(refilled, routines, due - 1, 1_000));
        Assert.Single(NpcLifeEngine.JobOutput(refilled, routines, due, 1_000));
    }

    [Fact]
    public void JobOutput_StopsForTheDaysTheHolderBeginsInTheCharactersHire()
    {
        const long Day = 1_000;
        var job = new NpcJob("market.town", "item.material.iron_ingot", 2);
        var routines = Routines(Routine("routine.town.miner", job: job));
        var life = Begin(routines, 8);
        NpcLifeState As(NpcCondition condition, long until) =>
            life with { Npcs = life.Npcs.SetItem("routine.town.miner", life.Npcs["routine.town.miner"] with { Condition = condition, ConditionUntil = until }) };
        Assert.Empty(NpcLifeEngine.JobOutput(As(NpcCondition.Hired, 0), routines, 1, Day));   // in the party
        // Let go at tick 2 500 - while the world catches up (still marked hired) or taken up again - the job is worked from day 3, the
        // first day that begins after it.
        foreach (var condition in new[] { NpcCondition.Hired, NpcCondition.Living })
        {
            Assert.Empty(NpcLifeEngine.JobOutput(As(condition, 2_500), routines, 2, Day));
            Assert.Single(NpcLifeEngine.JobOutput(As(condition, 2_500), routines, 3, Day));
        }
        Assert.Single(NpcLifeEngine.JobOutput(life, routines, 1, Day));
    }

    [Fact]
    public void ThereIsNoCap_AThousandLivesStepAndNoneIsRefused()
    {
        var routines = Enumerable.Range(0, 1_000).Select(i => Routine($"routine.town.r_{i:0000}")).ToArray();
        var all = Routines(routines);
        var life = Live(Begin(all, 8), all, 8 + 48);
        Assert.Equal(1_000, life.Npcs.Count);
        Assert.Equal(1_000, life.Npcs.Values.Select(n => n.Key).Distinct().Count());
    }

    [Fact]
    public void AnInjuredNpc_SleepsUntilItPasses()
    {
        var routines = Routines(Routine("routine.town.smith", "npc.town.smith"));
        var life = Begin(routines, 8);
        var npc = life.Npcs["npc.town.smith"] with { Condition = NpcCondition.Injured, ConditionUntil = 12 * 2_400 };
        life = life with { Npcs = life.Npcs.SetItem(npc.Key, npc) };
        Assert.Equal(NpcActivities.Sleep, Live(life, routines, 11).Npcs[npc.Key].Activity);
        var well = Live(life, routines, 13).Npcs[npc.Key];
        Assert.Equal(NpcCondition.Living, well.Condition);
        Assert.Equal(NpcActivities.Work, well.Activity);
    }

    // ── WP12b: roles and hireability, the hired state, trips ────────────────

    private static readonly ImmutableSortedDictionary<string, NpcRole> Vocabulary = new[]
    {
        new NpcRole(NpcRoleKeys.Vendor, ImmutableArray<UNNAMED.Domain.Companions.HirelingRole>.Empty, "villager"),
        new NpcRole(NpcRoleKeys.QuestGiver, ImmutableArray<UNNAMED.Domain.Companions.HirelingRole>.Empty, "villager"),
        new NpcRole("hunter", ImmutableArray.Create(UNNAMED.Domain.Companions.HirelingRole.Guard, UNNAMED.Domain.Companions.HirelingRole.Gatherer), "hunter"),
        new NpcRole("steward", ImmutableArray<UNNAMED.Domain.Companions.HirelingRole>.Empty, "villager"),
    }.ToImmutableSortedDictionary(r => r.Id, r => r, StringComparer.Ordinal);

    [Fact]
    public void Hireability_IsDerivedFromRoles_AndAVendorOrQuestGiverNeverHiresOut()
    {
        Assert.Equal(new[] { UNNAMED.Domain.Companions.HirelingRole.Guard, UNNAMED.Domain.Companions.HirelingRole.Gatherer },
            NpcHireability.HiresAs(new[] { "hunter" }, Vocabulary));
        Assert.Empty(NpcHireability.HiresAs(new[] { "hunter", NpcRoleKeys.Vendor }, Vocabulary));
        Assert.Empty(NpcHireability.HiresAs(new[] { "hunter", NpcRoleKeys.QuestGiver }, Vocabulary));
        Assert.Empty(NpcHireability.HiresAs(new[] { "steward" }, Vocabulary));

        var hunter = Routine("routine.town.hunter") with { Roles = ImmutableArray.Create("hunter") };
        var vendor = hunter with { Roles = ImmutableArray.Create("hunter", NpcRoleKeys.Vendor) };
        var life = new NpcLifeRecord("routine.town.hunter", hunter.Id, NpcActivities.Work, 0, 0, 0);
        var hires = NpcHireability.HiresAs(hunter.Roles, Vocabulary);
        Assert.Null(NpcHireability.Refusal(life, hunter, hires));
        Assert.NotNull(NpcHireability.Refusal(life, vendor, NpcHireability.HiresAs(vendor.Roles, Vocabulary)));
        Assert.NotNull(NpcHireability.Refusal(life with { Condition = NpcCondition.Hired }, hunter, hires));
        Assert.NotNull(NpcHireability.Refusal(life with { Condition = NpcCondition.Injured, ConditionUntil = 99 }, hunter, hires));
        Assert.NotNull(NpcHireability.Refusal(life with { Activity = NpcActivities.SetOut }, hunter, hires));
        Assert.NotNull(NpcHireability.Refusal(life with { Activity = NpcActivities.Away }, hunter, hires));
    }

    [Fact]
    public void AHiredLife_IsSuspended_AndTakenUpAtTheFirstHourAfterThePartyLetsThemGo()
    {
        var routines = Routines(Routine("routine.town.smith", "npc.town.smith"));
        var life = Begin(routines, 8);
        var hired = life with { Npcs = life.Npcs.SetItem("npc.town.smith", life.Npcs["npc.town.smith"] with { Condition = NpcCondition.Hired, Activity = NpcActivities.Idle }) };
        var later = Live(hired, routines, 8 + 30);
        Assert.Equal(hired.Npcs["npc.town.smith"] with { }, later.Npcs["npc.town.smith"]);   // no needs, no choices: the party's

        // Let go at a tick within hour 40: the life waits until hour 41 begins, then lives again.
        var letGo = later with { Npcs = later.Npcs.SetItem("npc.town.smith", later.Npcs["npc.town.smith"] with { ConditionUntil = 40 * 2_400 + 7 }) };
        Assert.Equal(NpcCondition.Hired, Live(letGo, routines, 40).Npcs["npc.town.smith"].Condition);
        var back = Live(letGo, routines, 41).Npcs["npc.town.smith"];
        // The tick they were let go is kept: their job is not worked on a day that began with them in the party.
        Assert.Equal((NpcCondition.Living, 40L * 2_400 + 7), (back.Condition, back.ConditionUntil));
    }

    private static RoutineDefinition Tripper(int chance = 100, int from = 9, int to = 9, int away = 3) => Routine("routine.town.hunter") with
    {
        Trip = new NpcTrip("hunt", "Grey Wolf", "location.gate", "The Gate", new NpcAnchor(1_000, 0, 0), "The Den", new NpcAnchor(50_000, 0, 0),
            chance, from, to, away),
    };

    [Fact]
    public void TheTripPlanner_IsADrawPerDay_AndHasThemWaitAnHour_ThenGoAway()
    {
        var routine = Tripper();
        var planned = NpcTrips.On(routine, 3, 24, 12345)!;
        Assert.Equal(new PlannedTrip(routine.Id, 3 * 24 + 9, 3 * 24 + 12), planned);
        Assert.Equal(NpcActivities.SetOut, planned.ActivityAt(3 * 24 + 8));
        Assert.Equal(NpcActivities.Away, planned.ActivityAt(3 * 24 + 9));
        Assert.Equal(NpcActivities.Away, planned.ActivityAt(3 * 24 + 11));
        Assert.Null(planned.ActivityAt(3 * 24 + 12));
        Assert.Null(NpcTrips.On(Tripper(chance: 0), 3, 24, 12345));
        Assert.Null(NpcTrips.On(Routine("routine.town.idler"), 3, 24, 0));
        // Covering asks the day itself and its neighbours: a trip found for any hour is the day's own.
        Assert.Equal(planned, NpcTrips.Covering(routine, 3 * 24 + 10, 24, _ => 12345));
        Assert.Null(NpcTrips.Covering(routine, 3 * 24 + 15, 24, _ => 12345));
        Assert.NotNull(new NpcTrip("hunt", "x", "m", "M", new NpcAnchor(0, 0, 0), "D", new NpcAnchor(0, 0, 0), 50, 0, 3, 2).Problem());   // no hour to wait in
        Assert.NotNull(Tripper(away: NpcTrip.MaxAwayHours + 1).Trip!.Problem());
        // A trip back after the next day's earliest wait would overlap that day's trip: leaving at 23:00 for 12 hours, with trips from 02:00.
        Assert.NotNull(Tripper(from: 2, to: 23, away: 12).Trip!.Problem());
        Assert.Null(Tripper(from: 8, to: 16, away: 12).Trip!.Problem());
    }

    [Fact]
    public void ATrip_ComesBeforeTheRoutine_ForSomeoneWellEnoughToGo()
    {
        var routine = Tripper();
        var routines = Routines(routine);
        string? Trip(RoutineDefinition r, long hour) => NpcTrips.Covering(r, hour, 24, _ => 777)?.ActivityAt(hour);
        var life = NpcLifeEngine.Begin(routines, Rules, At(6), (r, g) => "Ash", _ => null);
        for (long h = 7; h <= 13; h++)
        {
            long hour = h;
            life = NpcLifeEngine.Hour(life, routines, Rules, At(hour) with { Trip = r => Trip(r, hour) }, (r, g) => "Ash", _ => true).State;
            string expected = hour == 8 ? NpcActivities.SetOut : hour is >= 9 and < 12 ? NpcActivities.Away : life.Npcs[routine.Key].Activity;
            Assert.Equal(expected, life.Npcs[routine.Key].Activity);
        }
        Assert.Equal(NpcActivities.Work, life.Npcs[routine.Key].Activity);   // back at 13:00, to work
    }
}
