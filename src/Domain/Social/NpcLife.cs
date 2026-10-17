// UNNAMED Domain - NPC life: needs, routines, homes and work, role slots and arrivals, pairs (WP12; SYSTEMS_COMPLETENESS_PLAN §2.13)
// No Godot references - pure C#, integer only

using System.Collections.Immutable;

namespace UNNAMED.Domain.Social;

/// <summary>What an NPC can be doing (the routine chooser's activity set). Presentation picks the idles; the domain picks the activity.</summary>
public static class NpcActivities
{
    public const string Work = "work";
    public const string Eat = "eat";
    public const string Sleep = "sleep";
    public const string Socialise = "socialise";
    public const string Shop = "shop";
    public const string Shelter = "shelter";
    public const string Idle = "idle";

    /// <summary>Every activity, in the chooser's tie-break order.</summary>
    public static readonly ImmutableArray<string> All = ImmutableArray.Create(Work, Eat, Sleep, Socialise, Shop, Shelter, Idle);

    public static bool IsActivity(string activity) => All.Contains(activity);
}

/// <summary>The places an NPC's day is anchored to: where they sleep, work, eat and meet people (§2.13 "homes and anchors").</summary>
public static class NpcAnchorKinds
{
    public const string Home = "home";
    public const string Work = "work";
    public const string Eat = "eat";
    public const string Social = "social";

    public static readonly ImmutableArray<string> All = ImmutableArray.Create(Home, Work, Eat, Social);
}

/// <summary>A place on the ground an NPC goes to, and which way they face there.</summary>
public sealed record NpcAnchor(long XMm, long ZMm, int FacingMdeg);

/// <summary>What an NPC's job makes for their town's market each day they hold it (§2.13 "work produces"; WP14's job hook).</summary>
public sealed record NpcJob(string MarketId, string ItemId, long PerDay);

/// <summary>The kinds of NPC-NPC pair (§2.13): they drive who visits whom, and barks. Not romance (DN10).</summary>
public static class NpcPairKinds
{
    public const string Friend = "friend";
    public const string Rival = "rival";
    public const string Partner = "partner";

    public static readonly ImmutableArray<string> All = ImmutableArray.Create(Friend, Rival, Partner);
}

/// <summary>Two NPCs (by life key) and what they are to each other; <see cref="A"/> sorts before <see cref="B"/>.</summary>
public sealed record NpcPair(string A, string B, string Kind)
{
    public string Key => $"{A}|{B}";

    public bool Involves(string key) => A == key || B == key;

    public string Other(string key) => A == key ? B : A;
}

/// <summary>
/// One NPC's routine (a <c>routine</c> definition): who it is for - a named NPC (<see cref="NpcId"/>), or a role slot that residents
/// fill one after another (<see cref="NpcId"/> null; the slot is the routine's own ID) - their settlement and role, their anchors, the
/// hours they work, how much each activity weighs with them, and their job. A named NPC is essential (DN4): no rule ever kills them.
/// </summary>
public sealed record RoutineDefinition(
    string Id,
    string? NpcId,
    string Settlement,
    string Role,
    ImmutableSortedDictionary<string, NpcAnchor> Anchors,
    int WorkFromHour,
    int WorkToHour,
    ImmutableSortedDictionary<string, int> Weights)
{
    /// <summary>The people a slot's residents are drawn from, whose Suggest names they take (WP2's name source).</summary>
    public string? PeopleId { get; init; }

    public NpcJob? Job { get; init; }

    /// <summary>The key the NPC is known by in the world: the named NPC's ID, or the slot's.</summary>
    public string Key => NpcId ?? Id;

    public bool IsSlot => NpcId is null;

    /// <summary>The 8 named NPCs are essential (DN4): never killed by rule, on-screen or off.</summary>
    public bool Essential => NpcId is not null;

    /// <summary>Whether the hour of the day falls in the work hours; a span past midnight wraps.</summary>
    public bool WorksAt(int hourOfDay) =>
        WorkFromHour == WorkToHour ? false
        : WorkFromHour < WorkToHour ? hourOfDay >= WorkFromHour && hourOfDay < WorkToHour
        : hourOfDay >= WorkFromHour || hourOfDay < WorkToHour;

    public NpcAnchor AnchorFor(string activity) => activity switch
    {
        NpcActivities.Work => Anchors[NpcAnchorKinds.Work],
        NpcActivities.Eat => Anchors.GetValueOrDefault(NpcAnchorKinds.Eat) ?? Anchors[NpcAnchorKinds.Home],
        NpcActivities.Socialise => Anchors.GetValueOrDefault(NpcAnchorKinds.Social) ?? Anchors[NpcAnchorKinds.Home],
        NpcActivities.Idle => Anchors.GetValueOrDefault(NpcAnchorKinds.Social) ?? Anchors[NpcAnchorKinds.Home],
        _ => Anchors[NpcAnchorKinds.Home],
    };
}

/// <summary>
/// How lives run (<c>config.npc_life</c>): how fast each need rises an hour and how much its activity takes off it, in thousandths of a
/// band; the hours of the night; the merchants' trading hours (06:00-20:00); and how many days after someone leaves an arrival takes their
/// place (DN4: 3-7). Each is content, provisional, and tunable.
/// </summary>
public sealed record NpcLifeRules(
    ImmutableSortedDictionary<NpcNeed, int> RisePerHour,
    ImmutableSortedDictionary<NpcNeed, int> SatisfyPerHour,
    int NightFromHour,
    int NightToHour,
    int TradeFromMinute,
    int TradeToMinute,
    int ArrivalMinDays,
    int ArrivalMaxDays)
{
    /// <summary>The hours of the day people sit down to eat: the chooser favours eating then.</summary>
    public ImmutableArray<int> MealHours { get; init; } = ImmutableArray.Create(6, 12, 18);

    public static NpcLifeRules Default { get; } = new(
        new Dictionary<NpcNeed, int> { [NpcNeed.Hunger] = 250, [NpcNeed.Rest] = 150, [NpcNeed.Social] = 120, [NpcNeed.Coin] = 100 }.ToImmutableSortedDictionary(),
        new Dictionary<NpcNeed, int> { [NpcNeed.Hunger] = 2000, [NpcNeed.Rest] = 450, [NpcNeed.Social] = 900, [NpcNeed.Coin] = 400 }.ToImmutableSortedDictionary(),
        22, 6, 6 * 60, 20 * 60, 3, 7);

    public bool IsNight(int hourOfDay) =>
        NightFromHour < NightToHour ? hourOfDay >= NightFromHour && hourOfDay < NightToHour : hourOfDay >= NightFromHour || hourOfDay < NightToHour;

    public bool IsTradingMinute(int minuteOfDay) => minuteOfDay >= TradeFromMinute && minuteOfDay < TradeToMinute;

    /// <summary>The first broken rule; null when there is none.</summary>
    public string? Problem()
    {
        foreach (var need in Enum.GetValues<NpcNeed>())
        {
            if (!RisePerHour.ContainsKey(need) || !SatisfyPerHour.ContainsKey(need) || RisePerHour[need] < 0 || SatisfyPerHour[need] < 0)
                return $"need '{NpcNeeds.Key(need)}' needs a rise and a satisfaction of at least 0";
        }
        if (NightFromHour is < 0 or > 23 || NightToHour is < 0 or > 23)
            return "the night's hours lie in 0..23";
        if (TradeFromMinute < 0 || TradeToMinute > 24 * 60 || TradeFromMinute >= TradeToMinute)
            return "trading hours open before they close, within one day";
        if (ArrivalMinDays < 1 || ArrivalMaxDays < ArrivalMinDays)
            return "an arrival comes at least a day after someone leaves, within [min, max]";
        if (MealHours.Any(h => h is < 0 or > 23))
            return "meal hours lie in 0..23";
        return null;
    }
}

/// <summary>The four coarse needs (canon §7.1): each 0-3 bands, held as thousandths of a band (0..3999).</summary>
public enum NpcNeed { Hunger, Rest, Social, Coin }

/// <summary>An NPC's needs, each 0..3999 thousandths of a band; the band is the value over 1000.</summary>
public sealed record NpcNeeds(int Hunger, int Rest, int Social, int Coin)
{
    public const int Max = 3_999;

    public static NpcNeeds Sated { get; } = new(0, 0, 0, 0);

    public static string Key(NpcNeed need) => need switch
    {
        NpcNeed.Hunger => "hunger",
        NpcNeed.Rest => "rest",
        NpcNeed.Social => "social",
        NpcNeed.Coin => "coin",
        _ => throw new ArgumentOutOfRangeException(nameof(need), need, "Unknown need"),
    };

    public static NpcNeed? Parse(string key) => Enum.GetValues<NpcNeed>().Cast<NpcNeed?>().FirstOrDefault(n => Key(n!.Value) == key);

    public int this[NpcNeed need] => need switch
    {
        NpcNeed.Hunger => Hunger,
        NpcNeed.Rest => Rest,
        NpcNeed.Social => Social,
        _ => Coin,
    };

    public int Band(NpcNeed need) => this[need] / 1000;

    public NpcNeeds With(NpcNeed need, int value)
    {
        int v = Math.Clamp(value, 0, Max);
        return need switch
        {
            NpcNeed.Hunger => this with { Hunger = v },
            NpcNeed.Rest => this with { Rest = v },
            NpcNeed.Social => this with { Social = v },
            _ => this with { Coin = v },
        };
    }

    public bool Valid => new[] { Hunger, Rest, Social, Coin }.All(v => v is >= 0 and <= Max);
}

/// <summary>An NPC's condition (§2.13 life states). An injured NPC rests at home until their injury passes (WP13's outcome tables set it).</summary>
public enum NpcCondition { Living, Injured }

/// <summary>
/// One NPC's life as the world holds it (saved, WP12): their routine, a resident's own name and generation, when they arrived, their
/// needs, what they are doing and with whom, their condition, and their body's pose with the route it walks - so a load resumes them
/// where they were, never teleported.
/// </summary>
public sealed record NpcLifeRecord(string Key, string RoutineId, NpcNeeds Needs, string Activity, long XMm, long ZMm, int FacingMdeg)
{
    /// <summary>A resident's own name, drawn from their people's names; null for a named NPC, who is called what their definition says.</summary>
    public string? Name { get; init; }

    /// <summary>How many residents have held the slot before this one (0 for the first, and for a named NPC).</summary>
    public int Generation { get; init; }

    /// <summary>The calendar day they took up their place (day 0 for the world's first residents).</summary>
    public long ArrivedDay { get; init; }

    /// <summary>The NPC they are visiting while socialising, by key; null otherwise.</summary>
    public string? With { get; init; }

    public NpcCondition Condition { get; init; } = NpcCondition.Living;

    /// <summary>The tick an injury passes; 0 when living.</summary>
    public long ConditionUntil { get; init; }

    public Spatial.NavRoute Route { get; init; } = Spatial.NavRoute.None;

    public int StuckTicks { get; init; }

    /// <summary>The cell their body stands in, kept with the record so a save stores lives sparsely by cell.</summary>
    public string Cell { get; init; } = string.Empty;
}

/// <summary>A role slot whose resident has left (DN4): an arrival takes the place on <see cref="ArrivalDay"/>.</summary>
public sealed record NpcVacancy(string SlotId, long LeftDay, long ArrivalDay, int NextGeneration);

/// <summary>
/// Every NPC life the world holds, keyed with no limit (owner, 2026-10-17: no population cap in data, schema or code), the role slots
/// waiting for an arrival, when each pair last met, and the last game hour settled.
/// </summary>
public sealed record NpcLifeState(
    long Hour,
    ImmutableSortedDictionary<string, NpcLifeRecord> Npcs,
    ImmutableSortedDictionary<string, NpcVacancy> Vacancies,
    ImmutableSortedDictionary<string, long> PairsLastMet)
{
    public static NpcLifeState Empty(long hour) => new(hour,
        ImmutableSortedDictionary.Create<string, NpcLifeRecord>(StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<string, NpcVacancy>(StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<string, long>(StringComparer.Ordinal));

    public void AddTo(CanonicalHasher h)
    {
        h.Add("unnamed.npc-life/v1").Add(Hour).Add(Npcs.Count);
        foreach (var n in Npcs.Values)
        {
            h.Add(n.Key).Add(n.RoutineId).Add(n.Name ?? "-").Add(n.Generation).Add(n.ArrivedDay)
                .Add(n.Needs.Hunger).Add(n.Needs.Rest).Add(n.Needs.Social).Add(n.Needs.Coin)
                .Add(n.Activity).Add(n.With ?? "-").Add((int)n.Condition).Add(n.ConditionUntil)
                .Add(n.XMm).Add(n.ZMm).Add(n.FacingMdeg).Add(n.StuckTicks).Add(n.Cell);
            n.Route.AddTo(h);
        }
        h.Add(Vacancies.Count);
        foreach (var v in Vacancies.Values)
            h.Add(v.SlotId).Add(v.LeftDay).Add(v.ArrivalDay).Add(v.NextGeneration);
        h.Add(PairsLastMet.Count);
        foreach (var (pair, hour) in PairsLastMet)
            h.Add(pair).Add(hour);
    }
}

/// <summary>What an hour of lives did that the world (and the away sheet) may want to know.</summary>
public sealed record NpcLifeHappening(string Kind, string Key, string Detail);

/// <summary>Everything an hour's step reads besides the state: the hour, its first tick, its day and hour of day, the weather, and the draws.</summary>
public sealed record NpcHourContext(long Hour, long Tick, long Day, int HourOfDay, bool Raining, Func<string, ulong> Sample);

/// <summary>
/// The rules of NPC life, pure (§2.13). Lives advance one game hour at a time, on absolute hour boundaries, so the same hours give the
/// same lives however they are split (T-22): a running world and the offline catch-up call the same <see cref="Hour"/>. An hour raises
/// every need, lets the activity just done take off its own, chooses the next activity by utility under the hour, the needs and the
/// weather, remembers who met whom, and fills any role slot whose arrival is due. Bodies are the runtime's: this only says where each NPC
/// is headed.
/// </summary>
public static class NpcLifeEngine
{
    public const string Arrived = "arrived";
    public const string Departed = "departed";

    /// <summary>A world's first lives: every routine's NPC at their routine's first choice, needs sated. Residents take names from their people.</summary>
    public static NpcLifeState Begin(IReadOnlyDictionary<string, RoutineDefinition> routines, NpcLifeRules rules, NpcHourContext hour,
        Func<RoutineDefinition, int, string?> nameFor, Func<string, (long X, long Z, int Facing)?> placeOf)
    {
        var npcs = ImmutableSortedDictionary.CreateBuilder<string, NpcLifeRecord>(StringComparer.Ordinal);
        foreach (var routine in routines.Values.OrderBy(r => r.Id, StringComparer.Ordinal))
            npcs[routine.Key] = Newcomer(routine, rules, hour, 0, nameFor(routine, 0), placeOf(routine.Key));
        return NpcLifeState.Empty(hour.Hour) with { Npcs = npcs.ToImmutable() };
    }

    /// <summary>
    /// The lives a save holds, carried onto the routines content defines now: a life whose routine is gone is dropped, a routine with no
    /// life and no vacancy gets one. Nothing is refused for how many there are.
    /// </summary>
    public static NpcLifeState Reconcile(NpcLifeState saved, IReadOnlyDictionary<string, RoutineDefinition> routines, NpcLifeRules rules, NpcHourContext hour,
        Func<RoutineDefinition, int, string?> nameFor, Func<string, (long X, long Z, int Facing)?> placeOf)
    {
        var byKey = routines.Values.ToDictionary(r => r.Key, StringComparer.Ordinal);
        var npcs = saved.Npcs.Where(n => byKey.TryGetValue(n.Key, out var r) && r.Id == n.Value.RoutineId).ToImmutableSortedDictionary(StringComparer.Ordinal);
        var vacancies = saved.Vacancies.Where(v => routines.TryGetValue(v.Key, out var r) && r.IsSlot && !npcs.ContainsKey(r.Key))
            .ToImmutableSortedDictionary(StringComparer.Ordinal);
        foreach (var routine in routines.Values.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            if (!npcs.ContainsKey(routine.Key) && !vacancies.ContainsKey(routine.Id))
                npcs = npcs.SetItem(routine.Key, Newcomer(routine, rules, hour, 0, nameFor(routine, 0), placeOf(routine.Key)));
        }
        return saved with { Npcs = npcs, Vacancies = vacancies };
    }

    /// <summary>One game hour of every life: <paramref name="hour"/> must be the hour after the state's.</summary>
    public static (NpcLifeState State, ImmutableArray<NpcLifeHappening> Happenings) Hour(NpcLifeState state, IReadOnlyDictionary<string, RoutineDefinition> routines,
        IReadOnlyList<NpcPair> pairs, NpcLifeRules rules, NpcHourContext hour, Func<RoutineDefinition, int, string?> nameFor,
        Func<RoutineDefinition, bool> canArrive)
    {
        if (hour.Hour != state.Hour + 1)
            throw new ArgumentOutOfRangeException(nameof(hour), hour.Hour, $"Lives step one hour at a time: they stand at hour {state.Hour}");
        var happenings = ImmutableArray.CreateBuilder<NpcLifeHappening>();
        var npcs = state.Npcs.ToBuilder();
        var met = state.PairsLastMet.ToBuilder();

        // Needs rise, and the hour just spent takes off what its activity answers.
        foreach (var npc in state.Npcs.Values)
        {
            var needs = npc.Needs;
            foreach (var need in Enum.GetValues<NpcNeed>())
            {
                int value = needs[need] + rules.RisePerHour[need];
                if (Answers(npc.Activity) == need)
                    value -= rules.SatisfyPerHour[need];
                needs = needs.With(need, value);
            }
            npcs[npc.Key] = npc with { Needs = needs };
            if (npc.Activity == NpcActivities.Socialise && npc.With is { } other && npcs.ContainsKey(other)
                && pairs.FirstOrDefault(p => p.Involves(npc.Key) && p.Involves(other)) is { } pair)
                met[pair.Key] = state.Hour;
        }

        // Each NPC chooses what the next hour is for, in key order.
        foreach (string key in npcs.Keys.ToList())
        {
            var npc = npcs[key];
            if (!routines.TryGetValue(npc.RoutineId, out var routine))
                continue;
            if (npc.Condition == NpcCondition.Injured && npc.ConditionUntil <= hour.Tick)
                npc = npc with { Condition = NpcCondition.Living, ConditionUntil = 0 };
            var (activity, with) = Choose(npc, routine, routines, pairs, met, npcs, rules, hour);
            npcs[key] = npc with { Activity = activity, With = with };
        }

        // Arrivals (DN4): a slot whose day has come is filled by someone new, unless the place cannot take them yet (spawn suppression).
        var vacancies = state.Vacancies.ToBuilder();
        foreach (var vacancy in state.Vacancies.Values)
        {
            if (vacancy.ArrivalDay > hour.Day || !routines.TryGetValue(vacancy.SlotId, out var slot) || !canArrive(slot))
                continue;
            var anchor = slot.Anchors[NpcAnchorKinds.Home];
            var arrival = Newcomer(slot, rules, hour, vacancy.NextGeneration, nameFor(slot, vacancy.NextGeneration), (anchor.XMm, anchor.ZMm, anchor.FacingMdeg))
                with { ArrivedDay = hour.Day };
            npcs[slot.Key] = arrival;
            vacancies.Remove(vacancy.SlotId);
            happenings.Add(new NpcLifeHappening(Arrived, slot.Key, arrival.Name ?? slot.Key));
        }
        return (state with { Hour = hour.Hour, Npcs = npcs.ToImmutable(), Vacancies = vacancies.ToImmutable(), PairsLastMet = met.ToImmutable() },
            happenings.ToImmutable());
    }

    /// <summary>
    /// A resident leaves their slot (DN4: by a rule event - WP13's outcome tables, a hireling's death): the slot waits 3-7 days, drawn
    /// from <paramref name="sample"/>, for an arrival. A named NPC never leaves by rule: refused.
    /// </summary>
    public static NpcLifeState? Depart(NpcLifeState state, IReadOnlyDictionary<string, RoutineDefinition> routines, NpcLifeRules rules, string key, long day,
        ulong sample, out string? refusal)
    {
        refusal = null;
        if (!state.Npcs.TryGetValue(key, out var npc) || !routines.TryGetValue(npc.RoutineId, out var routine))
        {
            refusal = $"no life called {key}";
            return null;
        }
        if (routine.Essential)
        {
            refusal = $"{key} is essential: no rule takes them (DN4)";
            return null;
        }
        long wait = rules.ArrivalMinDays + (long)(sample % (ulong)(rules.ArrivalMaxDays - rules.ArrivalMinDays + 1));
        return state with
        {
            Npcs = state.Npcs.Remove(key),
            Vacancies = state.Vacancies.SetItem(routine.Id, new NpcVacancy(routine.Id, day, day + wait, npc.Generation + 1)),
        };
    }

    /// <summary>
    /// What the towns' jobs make on a calendar day (WP14's job hook): each routine's job, for each day its holder held it. A pure function of
    /// when each holder arrived and each slot fell vacant, so the markets read the same output whenever they settle the day (T-22).
    /// </summary>
    public static IEnumerable<NpcJob> JobOutput(NpcLifeState state, IReadOnlyDictionary<string, RoutineDefinition> routines, long day)
    {
        foreach (var routine in routines.Values.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            if (routine.Job is not { PerDay: > 0 } job)
                continue;
            bool held = state.Npcs.TryGetValue(routine.Key, out var npc)
                ? npc.RoutineId == routine.Id && npc.ArrivedDay <= day
                : state.Vacancies.TryGetValue(routine.Id, out var vacancy) && day < vacancy.LeftDay;
            if (held)
                yield return job;
        }
    }

    /// <summary>The need an activity answers; null for one that answers none.</summary>
    public static NpcNeed? Answers(string activity) => activity switch
    {
        NpcActivities.Eat => NpcNeed.Hunger,
        NpcActivities.Sleep => NpcNeed.Rest,
        NpcActivities.Shelter => NpcNeed.Rest,
        NpcActivities.Socialise => NpcNeed.Social,
        NpcActivities.Work => NpcNeed.Coin,
        _ => null,
    };

    /// <summary>
    /// The routine chooser: a utility over the activities this hour allows - work only in work hours, socialising only with someone to
    /// visit, shopping only in trading hours, shelter only in the rain - each the routine's weight, plus what the needs and the hour make of
    /// it, plus a little of the hour's own draw and a little for going on as before. Ties go to the earlier activity. An injured NPC sleeps.
    /// </summary>
    public static (string Activity, string? With) Choose(NpcLifeRecord npc, RoutineDefinition routine, IReadOnlyDictionary<string, RoutineDefinition> routines,
        IReadOnlyList<NpcPair> pairs, IReadOnlyDictionary<string, long> met, IReadOnlyDictionary<string, NpcLifeRecord> npcs, NpcLifeRules rules, NpcHourContext hour)
    {
        if (npc.Condition == NpcCondition.Injured)
            return (NpcActivities.Sleep, null);
        bool night = rules.IsNight(hour.HourOfDay);
        string? visit = pairs.Where(p => p.Involves(npc.Key) && p.Kind != NpcPairKinds.Rival && npcs.ContainsKey(p.Other(npc.Key)))
            .OrderBy(p => met.TryGetValue(p.Key, out long last) ? last : long.MinValue).ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Other(npc.Key)).FirstOrDefault();
        ulong draw = hour.Sample(npc.Key);
        string best = NpcActivities.Idle;
        long bestScore = long.MinValue;
        for (int i = 0; i < NpcActivities.All.Length; i++)
        {
            string activity = NpcActivities.All[i];
            long? score = activity switch
            {
                NpcActivities.Work when routine.WorksAt(hour.HourOfDay) && routine.Anchors.ContainsKey(NpcAnchorKinds.Work) => 300 + 60L * npc.Needs.Band(NpcNeed.Coin),
                NpcActivities.Work => null,
                NpcActivities.Eat => 140L * npc.Needs.Band(NpcNeed.Hunger) - (night ? 200 : 0) + (rules.MealHours.Contains(hour.HourOfDay) && npc.Needs.Band(NpcNeed.Hunger) > 0 ? 260 : 0),
                NpcActivities.Sleep => 120L * npc.Needs.Band(NpcNeed.Rest) + (night ? 420 : -260),
                NpcActivities.Socialise when visit is not null && !night => 110L * npc.Needs.Band(NpcNeed.Social) + (hour.Raining ? -150 : 0),
                NpcActivities.Socialise => null,
                NpcActivities.Shop when !night && rules.IsTradingMinute(hour.HourOfDay * 60) && npc.Needs.Band(NpcNeed.Coin) == 0 => hour.Raining ? -100 : 40,
                NpcActivities.Shop => null,
                NpcActivities.Shelter when hour.Raining => 380,
                NpcActivities.Shelter => null,
                _ => hour.Raining ? -200 : 20,
            };
            if (score is not { } s)
                continue;
            s += routine.Weights.GetValueOrDefault(activity) + (long)((draw >> (i * 8)) % 30) + (activity == npc.Activity ? 45 : 0);
            if (s > bestScore)
                (best, bestScore) = (activity, s);
        }
        return (best, best == NpcActivities.Socialise ? visit : null);
    }

    private static NpcLifeRecord Newcomer(RoutineDefinition routine, NpcLifeRules rules, NpcHourContext hour, int generation, string? name,
        (long X, long Z, int Facing)? place)
    {
        var anchor = routine.Anchors.GetValueOrDefault(NpcAnchorKinds.Work) ?? routine.Anchors[NpcAnchorKinds.Home];
        var (x, z, facing) = place ?? (anchor.XMm, anchor.ZMm, anchor.FacingMdeg);
        var record = new NpcLifeRecord(routine.Key, routine.Id, NpcNeeds.Sated, NpcActivities.Idle, x, z, facing)
        {
            Name = routine.IsSlot ? name : null,
            Generation = generation,
        };
        var empty = ImmutableDictionary<string, long>.Empty;
        var (activity, _) = Choose(record, routine, new Dictionary<string, RoutineDefinition>(), Array.Empty<NpcPair>(), empty,
            ImmutableDictionary<string, NpcLifeRecord>.Empty, rules, hour);
        return record with { Activity = activity };
    }
}

/// <summary>The lives content defines for the region played: its routines by ID, the NPC-NPC pairs, and the rules (WP12).</summary>
public sealed record NpcLifeSetup(ImmutableSortedDictionary<string, RoutineDefinition> Routines, ImmutableArray<NpcPair> Pairs, NpcLifeRules Rules)
{
    public static NpcLifeSetup Empty { get; } = new(ImmutableSortedDictionary.Create<string, RoutineDefinition>(StringComparer.Ordinal),
        ImmutableArray<NpcPair>.Empty, NpcLifeRules.Default);

    /// <summary>A named NPC's routine, or a slot's, by the key the NPC is known by; null for an NPC with none.</summary>
    public RoutineDefinition? RoutineOf(string key) =>
        Routines.TryGetValue(key, out var slot) && slot.IsSlot ? slot : Routines.Values.FirstOrDefault(r => r.NpcId == key);
}
