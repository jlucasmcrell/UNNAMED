using System.Collections.Immutable;
using UNNAMED.Domain.Social;

namespace UNNAMED.Domain.Tests.Social;

/// <summary>NPC life's rules, pure (WP12, §2.13): needs, the routine chooser, departures and arrivals (DN4), job output, and no cap.</summary>
public class NpcLifeTests
{
    private static readonly NpcLifeRules Rules = NpcLifeRules.Default;

    private static RoutineDefinition Routine(string id, string? npc = null, int from = 7, int to = 19, NpcJob? job = null) =>
        new(id, npc, "town", "worker", new Dictionary<string, NpcAnchor>
        {
            [NpcAnchorKinds.Home] = new(0, 0, 0),
            [NpcAnchorKinds.Work] = new(10_000, 0, 0),
            [NpcAnchorKinds.Eat] = new(5_000, 5_000, 0),
        }.ToImmutableSortedDictionary(StringComparer.Ordinal), from, to, ImmutableSortedDictionary<string, int>.Empty)
        { PeopleId = npc is null ? "people.veth" : null, Job = job };

    private static NpcHourContext At(long hour, bool raining = false) =>
        new(hour, hour * 2_400, hour / 24, (int)(hour % 24), raining, key => (ulong)key.Length * 7919UL + (ulong)hour);

    private static ImmutableSortedDictionary<string, RoutineDefinition> Routines(params RoutineDefinition[] routines) =>
        routines.ToImmutableSortedDictionary(r => r.Id, r => r, StringComparer.Ordinal);

    private static NpcLifeState Begin(ImmutableSortedDictionary<string, RoutineDefinition> routines, long hour) =>
        NpcLifeEngine.Begin(routines, Rules, At(hour), (r, g) => $"Name {g}", _ => null);

    private static NpcLifeState Live(NpcLifeState state, ImmutableSortedDictionary<string, RoutineDefinition> routines, long toHour,
        IReadOnlyList<NpcPair>? pairs = null, Func<long, bool>? rain = null, Func<RoutineDefinition, bool>? canArrive = null)
    {
        while (state.Hour < toHour)
            state = NpcLifeEngine.Hour(state, routines, pairs ?? Array.Empty<NpcPair>(), Rules, At(state.Hour + 1, rain?.Invoke(state.Hour + 1) ?? false),
                (r, g) => $"Name {g}", canArrive ?? (_ => true)).State;
        return state;
    }

    [Fact]
    public void AWorkerWorks_InWorkHours_AndSleeps_AtNight()
    {
        var routines = Routines(Routine("routine.town.smith", "npc.town.smith"));
        var life = Begin(routines, 8);
        Assert.Equal(NpcActivities.Work, life.Npcs["npc.town.smith"].Activity);
        Assert.Equal(NpcActivities.Sleep, Live(life, routines, 26).Npcs["npc.town.smith"].Activity);   // 02:00 the next night
        Assert.Throws<ArgumentOutOfRangeException>(() => NpcLifeEngine.Hour(life, routines, Array.Empty<NpcPair>(), Rules, At(10), (r, g) => null, _ => true));
    }

    [Fact]
    public void NeedsRise_AndTheirActivityAnswersThem()
    {
        var routines = Routines(Routine("routine.town.smith", "npc.town.smith"));
        var life = Live(Begin(routines, 8), routines, 12);
        var npc = life.Npcs["npc.town.smith"];
        Assert.True(npc.Needs.Hunger >= 900);   // four hours at the anvil
        Assert.True(npc.Needs.Coin == 0);       // work answers coin
        var later = Live(life, routines, 8 + 24 * 3);
        Assert.True(later.Npcs.Values.All(n => n.Needs.Valid));
    }

    [Fact]
    public void Rain_SendsEveryoneOutdoorsToShelter()
    {
        var routines = Routines(Routine("routine.town.idler", "npc.town.idler", 0, 0));
        var life = Live(Begin(routines, 9), routines, 12, rain: h => h >= 11);
        Assert.Equal(NpcActivities.Shelter, life.Npcs["npc.town.idler"].Activity);
    }

    [Fact]
    public void Friends_VisitEachOther_AndRivalsDoNot()
    {
        var routines = Routines(Routine("routine.town.a", "npc.town.a", 0, 0), Routine("routine.town.b", "npc.town.b", 0, 0), Routine("routine.town.c", "npc.town.c", 0, 0));
        var pairs = new[] { new NpcPair("npc.town.a", "npc.town.b", NpcPairKinds.Friend), new NpcPair("npc.town.a", "npc.town.c", NpcPairKinds.Rival) };
        var life = Begin(routines, 9);
        bool visited = false;
        for (long h = 10; h < 9 + 24 * 3; h++)
        {
            life = Live(life, routines, h, pairs);
            var a = life.Npcs["npc.town.a"];
            Assert.NotEqual("npc.town.c", a.With);
            visited |= a.Activity == NpcActivities.Socialise && a.With == "npc.town.b";
        }
        Assert.True(visited);
        Assert.True(life.PairsLastMet.ContainsKey("npc.town.a|npc.town.b"));
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
        Assert.Single(NpcLifeEngine.JobOutput(life, routines, 1));
        var left = NpcLifeEngine.Depart(life, routines, Rules, "routine.town.miner", 2, 0, out _)!;
        long due = left.Vacancies["routine.town.miner"].ArrivalDay;
        Assert.Single(NpcLifeEngine.JobOutput(left, routines, 1));   // a day before they left: the job was held
        Assert.Empty(NpcLifeEngine.JobOutput(left, routines, 3));
        var refilled = Live(left, routines, due * 24 + 1);
        Assert.Empty(NpcLifeEngine.JobOutput(refilled, routines, due - 1));
        Assert.Single(NpcLifeEngine.JobOutput(refilled, routines, due));
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
}
