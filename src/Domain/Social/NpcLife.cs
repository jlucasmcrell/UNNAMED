// UNNAMED Domain - NPC life: fixed daily schedules, homes and work, role slots and arrivals (WP12; SYSTEMS_COMPLETENESS_PLAN §2.13)
// No Godot references - pure C#, integer only

using System.Collections.Immutable;
using UNNAMED.Domain.Companions;

namespace UNNAMED.Domain.Social;

/// <summary>
/// What an NPC is doing: working at their work site in work hours, asleep at home at night, or about the rest of their day at home or at a
/// spot their schedule names (owner, 2026-10-17: fixed daily schedules, no needs simulation). Presentation picks the idles.
/// </summary>
public static class NpcActivities
{
    public const string Work = "work";
    public const string Sleep = "sleep";
    public const string Idle = "idle";

    /// <summary>Every activity a schedule gives.</summary>
    public static readonly ImmutableArray<string> All = ImmutableArray.Create(Work, Sleep, Idle);

    /// <summary>
    /// Waiting at a trip's meeting place for its departure (WP12b): imposed by the trip planner, never by the schedule. Its SettingOut is
    /// raised during this hour, at least the trip lead before it ends.
    /// </summary>
    public const string SetOut = "set_out";

    /// <summary>Gone on a trip (WP12b): walking to its destination, or there; imposed by the trip planner, never by the schedule.</summary>
    public const string Away = "away";

    /// <summary>Every activity a life may be found doing: the schedule's, then the trip planner's.</summary>
    public static readonly ImmutableArray<string> Known = All.Add(SetOut).Add(Away);

    public static bool IsActivity(string activity) => All.Contains(activity);

    public static bool IsKnown(string activity) => Known.Contains(activity);
}

/// <summary>The two places every NPC's day is anchored to: where they sleep and where they work. A schedule may name more spots.</summary>
public static class NpcAnchorKinds
{
    public const string Home = "home";
    public const string Work = "work";
}

/// <summary>An hour band of a fixed daily schedule: from one hour of the day up to another (a span past midnight wraps), at an anchor.</summary>
public sealed record NpcScheduleBand(int FromHour, int ToHour, string Anchor)
{
    public bool Covers(int hourOfDay) =>
        FromHour == ToHour ? false
        : FromHour < ToHour ? hourOfDay >= FromHour && hourOfDay < ToHour
        : hourOfDay >= FromHour || hourOfDay < ToHour;
}

/// <summary>A place on the ground an NPC goes to, and which way they face there.</summary>
public sealed record NpcAnchor(long XMm, long ZMm, int FacingMdeg);

/// <summary>What an NPC's job makes for their town's market each day they hold it (§2.13 "work produces"; WP14's job hook).</summary>
public sealed record NpcJob(string MarketId, string ItemId, long PerDay);

/// <summary>
/// A trip an NPC sets out on now and then (WP12b): what for (an <c>LfgPurposes</c> key) and its words, where they wait to leave and where
/// they go - each a place's name, and a spot on the ground - how likely a day is to hold one, the hours of the day it may leave in, and how
/// many hours it keeps them away. Every word is content's own: a creature's, a bounty's, an item's, a location's name.
/// </summary>
public sealed record NpcTrip(
    string Purpose,
    string PurposeName,
    string MeetId,
    string MeetName,
    NpcAnchor Meet,
    string DestinationName,
    NpcAnchor Destination,
    int ChancePercent,
    int FromHour,
    int ToHour,
    int AwayHours)
{
    /// <summary>The longest a trip may keep someone away: it never reaches past the next day's own trip.</summary>
    public const int MaxAwayHours = 12;

    public string? Problem() =>
        ChancePercent is < 0 or > 100 ? "a trip's chance_percent is 0 to 100"
        : FromHour is < 1 or > 23 || ToHour < FromHour || ToHour > 23 ? "a trip leaves within [from, to] of one day, from 01:00 (its wait is the hour before)"
        : AwayHours is < 1 or > MaxAwayHours ? $"a trip keeps them away 1 to {MaxAwayHours} hours"
        : ToHour + AwayHours > 24 + FromHour - 1 ? "a trip comes back before the next day's trip could wait to leave"
        : null;
}

/// <summary>A trip the planner set for a day: the routine, the absolute game hour it leaves, and the hour they are back.</summary>
public sealed record PlannedTrip(string RoutineId, long DepartHour, long ReturnHour)
{
    /// <summary>The hour they wait at the meeting place: the one before they leave.</summary>
    public long WaitHour => DepartHour - 1;

    /// <summary>What the trip has them doing in an hour: waiting, away, or nothing of the trip's.</summary>
    public string? ActivityAt(long hour) =>
        hour == WaitHour ? NpcActivities.SetOut : hour >= DepartHour && hour < ReturnHour ? NpcActivities.Away : null;
}

/// <summary>
/// The trip planner (WP12b), pure: whether a routine's trip happens on a day, and when, from a draw for that routine and day alone - so the
/// same world plans the same trips whenever it is asked, whatever came before (T-22), and an offer posted while the character was away
/// reads the same on a load.
/// </summary>
public static class NpcTrips
{
    /// <summary>The trip of a day, from its draw: <paramref name="draw"/> the routine's sample for the day.</summary>
    public static PlannedTrip? On(RoutineDefinition routine, long day, int hoursPerDay, ulong draw)
    {
        if (routine.Trip is not { } trip || (int)(draw % 100) >= trip.ChancePercent)
            return null;
        int span = trip.ToHour - trip.FromHour + 1;
        long depart = day * hoursPerDay + trip.FromHour + (long)((draw / 100) % (ulong)span);
        return new PlannedTrip(routine.Id, depart, depart + trip.AwayHours);
    }

    /// <summary>
    /// The trip that covers an hour, if any: the hour's own day's, or the day before's still away. <paramref name="drawOn"/> gives the
    /// routine's sample for a day.
    /// </summary>
    public static PlannedTrip? Covering(RoutineDefinition routine, long hour, int hoursPerDay, Func<long, ulong> drawOn)
    {
        if (routine.Trip is null)
            return null;
        long day = Math.DivRem(hour, hoursPerDay, out long within) - (within < 0 ? 1 : 0);
        foreach (long d in new[] { day + 1, day, day - 1 })
        {
            if (On(routine, d, hoursPerDay, drawOn(d)) is { } planned && planned.ActivityAt(hour) is not null)
                return planned;
        }
        return null;
    }
}

/// <summary>
/// A town role (<c>config.npc_life</c>'s <c>roles</c>; WP12b): the hireling roles someone with it takes on, and the chatter speaker type
/// they talk as. <see cref="NpcRoleKeys.Vendor"/> and <see cref="NpcRoleKeys.QuestGiver"/> are never hired, whatever else they are.
/// </summary>
public sealed record NpcRole(string Id, ImmutableArray<HirelingRole> HiresAs, string? SpeakerType);

/// <summary>The two roles that rule out hiring (WP12b): the town needs its vendors and its quest givers where they are.</summary>
public static class NpcRoleKeys
{
    public const string Vendor = "vendor";
    public const string QuestGiver = "quest_giver";

    public static readonly ImmutableArray<string> NeverHired = ImmutableArray.Create(Vendor, QuestGiver);
}

/// <summary>
/// Whether someone can be hired now, derived (WP12b), never stored: never a vendor or a quest giver; otherwise whoever has a role that
/// hires out, a body to work and fight with, and is about their life - living, in town, not already hired, not injured, not setting out.
/// </summary>
public static class NpcHireability
{
    /// <summary>The hireling roles a set of town roles takes on: none for a vendor or quest giver, else every role's, in role order.</summary>
    public static ImmutableArray<HirelingRole> HiresAs(IEnumerable<string> roles, IReadOnlyDictionary<string, NpcRole> vocabulary)
    {
        var list = roles.ToList();
        if (list.Any(NpcRoleKeys.NeverHired.Contains))
            return ImmutableArray<HirelingRole>.Empty;
        return list.SelectMany(r => vocabulary.TryGetValue(r, out var role) ? role.HiresAs : ImmutableArray<HirelingRole>.Empty)
            .Distinct().OrderBy(r => r).ToImmutableArray();
    }

    /// <summary>Why a life cannot be hired now; null when they can. <paramref name="hiresAs"/> is what their roles take on.</summary>
    public static string? Refusal(NpcLifeRecord life, RoutineDefinition routine, ImmutableArray<HirelingRole> hiresAs)
    {
        if (routine.Roles.Any(NpcRoleKeys.NeverHired.Contains))
            return "is needed in town and does not hire on";
        if (hiresAs.IsEmpty)
            return "does not hire on";
        return life.Condition switch
        {
            NpcCondition.Hired => "is already in someone's hire",
            NpcCondition.Injured => "is injured and resting",
            _ when life.Activity is NpcActivities.SetOut or NpcActivities.Away => "is away on their own business",
            _ => null,
        };
    }
}

/// <summary>
/// One NPC's routine (a <c>routine</c> definition): who it is for - a named NPC (<see cref="NpcId"/>), or a role slot that residents
/// fill one after another (<see cref="NpcId"/> null; the slot is the routine's own ID) - their settlement and role, their anchors, the
/// hours they work, their fixed schedule outside them, and their job. A named NPC is essential (DN4): no rule ever kills them.
/// </summary>
public sealed record RoutineDefinition(
    string Id,
    string? NpcId,
    string Settlement,
    string Role,
    ImmutableSortedDictionary<string, NpcAnchor> Anchors,
    int WorkFromHour,
    int WorkToHour)
{
    /// <summary>The people a slot's residents are drawn from, whose Suggest names they take (WP2's name source).</summary>
    public string? PeopleId { get; init; }

    public NpcJob? Job { get; init; }

    /// <summary>A captive stays at their authored site until this layout barrier lifts.</summary>
    public string? ReleaseBarrier { get; init; }

    /// <summary>
    /// What they are in their town (WP12b; <c>config.npc_life</c>'s <c>roles</c> names each): what they are hired as, if anything, and how
    /// they talk. Whether they can be hired is derived from these (<see cref="NpcHireability"/>): a vendor or a quest giver never is.
    /// </summary>
    public ImmutableArray<string> Roles { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>The trip they sometimes set out on, asking along whoever will come (WP12b); null for one who keeps to town.</summary>
    public NpcTrip? Trip { get; init; }

    /// <summary>
    /// Where they spend hours outside work besides home (a band names one of <see cref="Anchors"/>): authored, fixed, the same every day.
    /// An hour no band covers is spent at home.
    /// </summary>
    public ImmutableArray<NpcScheduleBand> Schedule { get; init; } = ImmutableArray<NpcScheduleBand>.Empty;

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

    /// <summary>
    /// What the schedule has them doing at an hour of the day. An authored band wins over the work hours: a band naming the work site
    /// is work, a band naming home is home (asleep at night), and a band naming any other anchor is the rest of their day spent there.
    /// Work hours then hold for every hour no band covers, and the hours outside them are home.
    /// </summary>
    public string ActivityAt(int hourOfDay, NpcLifeRules rules)
    {
        switch (BandAt(hourOfDay))
        {
            case NpcAnchorKinds.Work: return NpcActivities.Work;
            case NpcAnchorKinds.Home: return rules.IsNight(hourOfDay) ? NpcActivities.Sleep : NpcActivities.Idle;
            case not null: return NpcActivities.Idle;
        }
        return WorksAt(hourOfDay) && Anchors.ContainsKey(NpcAnchorKinds.Work) ? NpcActivities.Work
            : rules.IsNight(hourOfDay) ? NpcActivities.Sleep
            : NpcActivities.Idle;
    }

    /// <summary>Where an activity puts them at an hour of the day: the work site, home, or the spot the schedule names.</summary>
    public NpcAnchor AnchorFor(string activity, int hourOfDay) => activity switch
    {
        NpcActivities.Work => Anchors[NpcAnchorKinds.Work],
        NpcActivities.Idle when SpotAt(hourOfDay) is { } spot => Anchors[spot],
        _ => Anchors[NpcAnchorKinds.Home],
    };

    /// <summary>The anchor the band covering this hour names, home included; null when no band covers it.</summary>
    private string? BandAt(int hourOfDay) =>
        Schedule.FirstOrDefault(b => b.Covers(hourOfDay))?.Anchor;

    private string? SpotAt(int hourOfDay) =>
        BandAt(hourOfDay) is { } spot && spot != NpcAnchorKinds.Home ? spot : null;
}

/// <summary>
/// How lives run (<c>config.npc_life</c>): the hours of the night (asleep at home), the merchants' trading hours (06:00-20:00), and how many
/// days after someone leaves an arrival takes their place (DN4: 3-7). Each is content, provisional, and tunable.
/// </summary>
public sealed record NpcLifeRules(
    int NightFromHour,
    int NightToHour,
    int TradeFromMinute,
    int TradeToMinute,
    int ArrivalMinDays,
    int ArrivalMaxDays)
{
    public static NpcLifeRules Default { get; } = new(22, 6, 6 * 60, 20 * 60, 3, 7);

    public bool IsNight(int hourOfDay) =>
        NightFromHour < NightToHour ? hourOfDay >= NightFromHour && hourOfDay < NightToHour : hourOfDay >= NightFromHour || hourOfDay < NightToHour;

    public bool IsTradingMinute(int minuteOfDay) => minuteOfDay >= TradeFromMinute && minuteOfDay < TradeToMinute;

    /// <summary>The first broken rule; null when there is none.</summary>
    public string? Problem()
    {
        if (NightFromHour is < 0 or > 23 || NightToHour is < 0 or > 23)
            return "the night's hours lie in 0..23";
        if (TradeFromMinute < 0 || TradeToMinute > 24 * 60 || TradeFromMinute >= TradeToMinute)
            return "trading hours open before they close, within one day";
        if (ArrivalMinDays < 1 || ArrivalMaxDays < ArrivalMinDays)
            return "an arrival comes at least a day after someone leaves, within [min, max]";
        return null;
    }
}

/// <summary>
/// An NPC's condition (§2.13 life states). An injured NPC rests at home until their injury passes (WP13's outcome tables set it). A hired
/// one (WP12b) is the party's: their schedule is suspended - no walking of their own - until they leave the party and
/// walk home to take it up again.
/// </summary>
public enum NpcCondition { Living, Injured, Hired }

/// <summary>
/// One NPC's life as the world holds it (saved, WP12): their routine, a resident's own name and generation, when they arrived, what they
/// are doing, their condition, and their body's pose with the route it walks - so a load resumes them where they were, never teleported.
/// </summary>
public sealed record NpcLifeRecord(string Key, string RoutineId, string Activity, long XMm, long ZMm, int FacingMdeg)
{
    /// <summary>A resident's own name, drawn from their people's names; null for a named NPC, who is called what their definition says.</summary>
    public string? Name { get; init; }

    /// <summary>How many residents have held the slot before this one (0 for the first, and for a named NPC).</summary>
    public int Generation { get; init; }

    /// <summary>The calendar day they took up their place (day 0 for the world's first residents).</summary>
    public long ArrivedDay { get; init; }

    public NpcCondition Condition { get; init; } = NpcCondition.Living;

    /// <summary>
    /// The tick an injury passes. Hired (WP12b): the tick the party let them go, while the world catches up to it; 0 while in the party.
    /// Living: the tick the party let them go, kept so their job is not worked on a day that began with them in the party; 0 otherwise.
    /// </summary>
    public long ConditionUntil { get; init; }

    public Spatial.NavRoute Route { get; init; } = Spatial.NavRoute.None;

    public int StuckTicks { get; init; }

    /// <summary>The cell their body stands in, kept with the record so a save stores lives sparsely by cell.</summary>
    public string Cell { get; init; } = string.Empty;
}

/// <summary>A role slot whose resident has left (DN4): an arrival takes the place on <see cref="ArrivalDay"/>.</summary>
public sealed record NpcVacancy(string SlotId, long LeftDay, long ArrivalDay, int NextGeneration)
{
    /// <summary>Who arrives, when someone is already promised the slot - a retiring adventurer (WP13); null for a newcomer named from their people.</summary>
    public string? ArrivingName { get; init; }
}

/// <summary>
/// Every NPC life the world holds, keyed with no limit (owner, 2026-10-17: no population cap in data, schema or code), the role slots
/// waiting for an arrival, and the last game hour settled.
/// </summary>
public sealed record NpcLifeState(
    long Hour,
    ImmutableSortedDictionary<string, NpcLifeRecord> Npcs,
    ImmutableSortedDictionary<string, NpcVacancy> Vacancies)
{
    public static NpcLifeState Empty(long hour) => new(hour,
        ImmutableSortedDictionary.Create<string, NpcLifeRecord>(StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<string, NpcVacancy>(StringComparer.Ordinal));

    public void AddTo(CanonicalHasher h)
    {
        h.Add("unnamed.npc-life/v2").Add(Hour).Add(Npcs.Count);
        foreach (var n in Npcs.Values)
        {
            h.Add(n.Key).Add(n.RoutineId).Add(n.Name ?? "-").Add(n.Generation).Add(n.ArrivedDay)
                .Add(n.Activity).Add((int)n.Condition).Add(n.ConditionUntil)
                .Add(n.XMm).Add(n.ZMm).Add(n.FacingMdeg).Add(n.StuckTicks).Add(n.Cell);
            n.Route.AddTo(h);
        }
        h.Add(Vacancies.Count);
        foreach (var v in Vacancies.Values)
        {
            h.Add(v.SlotId).Add(v.LeftDay).Add(v.ArrivalDay).Add(v.NextGeneration);
            if (v.ArrivingName is { } arriving)
                h.Add(arriving);
        }
    }
}

/// <summary>What an hour of lives did that the world (and the away sheet) may want to know.</summary>
public sealed record NpcLifeHappening(string Kind, string Key, string Detail);

/// <summary>Everything an hour's step reads besides the state: the hour, its first tick, its day and hour of day.</summary>
public sealed record NpcHourContext(long Hour, long Tick, long Day, int HourOfDay)
{
    /// <summary>What the trip planner has a routine doing this hour (<see cref="NpcActivities.SetOut"/> or <see cref="NpcActivities.Away"/>), or null.</summary>
    public Func<RoutineDefinition, string?> Trip { get; init; } = _ => null;
}

/// <summary>
/// The rules of NPC life, pure (§2.13). Lives advance one game hour at a time, on absolute hour boundaries, so the same hours give the
/// same lives however they are split (T-22): a running world and the offline catch-up call the same <see cref="Hour"/>. An hour sets each
/// life to what their fixed schedule (or a planned trip) has them doing, and fills any role slot whose arrival is due. Bodies are the
/// runtime's: this only says where each NPC is headed.
/// </summary>
public static class NpcLifeEngine
{
    public const string Arrived = "arrived";
    public const string Departed = "departed";

    /// <summary>A world's first lives: every routine's NPC at what their schedule has them doing. Residents take names from their people.</summary>
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
        NpcLifeRules rules, NpcHourContext hour, Func<RoutineDefinition, int, string?> nameFor, Func<RoutineDefinition, bool> canArrive)
    {
        if (hour.Hour != state.Hour + 1)
            throw new ArgumentOutOfRangeException(nameof(hour), hour.Hour, $"Lives step one hour at a time: they stand at hour {state.Hour}");
        var happenings = ImmutableArray.CreateBuilder<NpcLifeHappening>();
        var npcs = state.Npcs.ToBuilder();

        foreach (string key in npcs.Keys.ToList())
        {
            var npc = npcs[key];
            if (!routines.TryGetValue(npc.RoutineId, out var routine))
                continue;
            // Someone the party let go while the world caught up (WP12b) takes their life up at the first hour after it; until then, and
            // while still in the party, their life waits.
            if (npc.Condition == NpcCondition.Hired)
            {
                if (npc.ConditionUntil <= 0 || npc.ConditionUntil > hour.Tick)
                    continue;
                npc = npc with { Condition = NpcCondition.Living };
            }
            if (npc.Condition == NpcCondition.Injured && npc.ConditionUntil <= hour.Tick)
                npc = npc with { Condition = NpcCondition.Living, ConditionUntil = 0 };
            npcs[key] = npc with { Activity = ActivityOf(npc, routine, rules, hour) };
        }

        // Arrivals (DN4): a slot whose day has come is filled by someone new, unless the place cannot take them yet (spawn suppression).
        var vacancies = state.Vacancies.ToBuilder();
        foreach (var vacancy in state.Vacancies.Values)
        {
            if (vacancy.ArrivalDay > hour.Day || !routines.TryGetValue(vacancy.SlotId, out var slot) || !canArrive(slot))
                continue;
            var anchor = slot.Anchors[NpcAnchorKinds.Home];
            var arrival = Newcomer(slot, rules, hour, vacancy.NextGeneration, vacancy.ArrivingName ?? nameFor(slot, vacancy.NextGeneration), (anchor.XMm, anchor.ZMm, anchor.FacingMdeg))
                with { ArrivedDay = hour.Day };
            npcs[slot.Key] = arrival;
            vacancies.Remove(vacancy.SlotId);
            happenings.Add(new NpcLifeHappening(Arrived, slot.Key, arrival.Name ?? slot.Key));
        }
        return (state with { Hour = hour.Hour, Npcs = npcs.ToImmutable(), Vacancies = vacancies.ToImmutable() }, happenings.ToImmutable());
    }

    /// <summary>
    /// What an hour has someone doing: an injured NPC sleeps; a trip the planner set for the hour comes first for someone well enough to
    /// go; otherwise their fixed schedule.
    /// </summary>
    public static string ActivityOf(NpcLifeRecord npc, RoutineDefinition routine, NpcLifeRules rules, NpcHourContext hour) =>
        npc.Condition == NpcCondition.Injured ? NpcActivities.Sleep
        : npc.Condition == NpcCondition.Living && hour.Trip(routine) is { } trip ? trip
        : routine.ActivityAt(hour.HourOfDay, rules);

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
    /// What the towns' jobs make on a calendar day (WP14's job hook): each routine's job, for each day its holder held it and began out of the
    /// character's hire (owner, 2026-10-17: a hired resident makes nothing for the market). A pure function of when each holder arrived, each
    /// slot fell vacant and each hireling was let go, so the markets read the same output whenever they settle the day (T-22).
    /// </summary>
    public static IEnumerable<NpcJob> JobOutput(NpcLifeState state, IReadOnlyDictionary<string, RoutineDefinition> routines, long day, long ticksPerDay)
    {
        foreach (var routine in routines.Values.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            if (routine.Job is not { PerDay: > 0 } job)
                continue;
            bool held = state.Npcs.TryGetValue(routine.Key, out var npc)
                ? npc.RoutineId == routine.Id && npc.ArrivedDay <= day && !InHireAt(npc, day * ticksPerDay)
                : state.Vacancies.TryGetValue(routine.Id, out var vacancy) && day < vacancy.LeftDay;
            if (held)
                yield return job;
        }
    }

    /// <summary>Whether someone was in the character's hire at a tick: in the party, or let go only later.</summary>
    private static bool InHireAt(NpcLifeRecord npc, long tick) =>
        npc.Condition == NpcCondition.Hired && (npc.ConditionUntil <= 0 || npc.ConditionUntil > tick)
        || npc.Condition == NpcCondition.Living && npc.ConditionUntil > tick;

    private static NpcLifeRecord Newcomer(RoutineDefinition routine, NpcLifeRules rules, NpcHourContext hour, int generation, string? name,
        (long X, long Z, int Facing)? place)
    {
        // Where the schedule has them this hour, unless the region places them.
        string activity = routine.ActivityAt(hour.HourOfDay, rules);
        var anchor = routine.AnchorFor(activity, hour.HourOfDay);
        var (x, z, facing) = place ?? (anchor.XMm, anchor.ZMm, anchor.FacingMdeg);
        return new NpcLifeRecord(routine.Key, routine.Id, activity, x, z, facing)
        {
            Name = routine.IsSlot ? name : null,
            Generation = generation,
        };
    }
}

/// <summary>The lives content defines for the region played: its routines by ID, and the rules (WP12).</summary>
public sealed record NpcLifeSetup(ImmutableSortedDictionary<string, RoutineDefinition> Routines, NpcLifeRules Rules)
{
    /// <summary>The town roles routines name (WP12b): what each is hired as and talks as.</summary>
    public ImmutableSortedDictionary<string, NpcRole> Roles { get; init; } = ImmutableSortedDictionary.Create<string, NpcRole>(StringComparer.Ordinal);

    /// <summary>
    /// The body a hired resident works and fights with (WP12b; <c>config.npc_life</c>'s <c>resident_hireling</c>): residents have no NPC
    /// file of their own. Null: no resident can be hired.
    /// </summary>
    public CompanionProfile? ResidentBody { get; init; }

    /// <summary>How many game minutes before a trip leaves its offer goes up (WP12b; at least 20, at most the hour of waiting).</summary>
    public int TripLeadMinutes { get; init; } = 40;

    public static NpcLifeSetup Empty { get; } = new(ImmutableSortedDictionary.Create<string, RoutineDefinition>(StringComparer.Ordinal), NpcLifeRules.Default);

    /// <summary>A named NPC's routine, or a slot's, by the key the NPC is known by; null for an NPC with none.</summary>
    public RoutineDefinition? RoutineOf(string key) =>
        Routines.TryGetValue(key, out var slot) && slot.IsSlot ? slot : Routines.Values.FirstOrDefault(r => r.NpcId == key);
}
