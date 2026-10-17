// UNNAMED World - NPC life's ties to the rest of the world: roles and hireability, the hired state, trips and the LFG, chatter speakers (WP12b)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// ── contracts (WP13 codes against these) ────────────────────────────────────

/// <summary>
/// Someone became hireable, or stopped being (WP12b): an arrival, a departure, a hire, a dismissal, an injury, a trip. Raised whenever the
/// derived answer of <see cref="INpcHireability.IsHireable"/> changes for a key; <see cref="Reason"/> is why not, when not.
/// </summary>
public sealed record HireabilityChanged(string Key, bool Hireable, string? Reason, long Tick);

/// <summary>Whether someone can be hired now, and why not; their town roles and the hireling roles those take on.</summary>
public sealed record NpcHireView(string Key, bool Hireable, string? Refusal, ImmutableArray<string> Roles, ImmutableArray<HirelingRole> HiresAs);

/// <summary>
/// Who can be hired (WP12b), derived and never stored: a vendor or a quest giver never; anyone else whose roles hire out, who has a body to
/// work with, and who is about their own life - living, in town, not injured, not already hired, not setting out. An NPC with no routine is
/// hireable exactly when their NPC file says they hire on, as before. <see cref="HireabilityChanged"/> follows every change.
/// </summary>
public interface INpcHireability
{
    bool IsHireable(string key);

    /// <summary>The whole answer for a key; null for someone the world does not hold.</summary>
    NpcHireView? Hireability(string key);
}

/// <summary>An NPC the party let go walks back into their own life (WP12b): who, and where their routine sends them first.</summary>
public sealed record NpcReturnedToLife(string Key, string Activity, long Tick);

// ── internal commands ───────────────────────────────────────────────────────

/// <summary>To <see cref="NpcSystem"/>: someone joined the party - hired, recruited or allied (WP12b). Their routine is suspended.</summary>
internal sealed record HandToParty(string Key) : InternalCommand;

/// <summary>
/// To <see cref="NpcSystem"/>: someone left the party alive at <paramref name="Tick"/> (WP12b). They take up their routine where they
/// stand and walk home to it; a companion gravely wounded rests at home until well.
/// </summary>
internal sealed record TakeBackFromParty(string Key, string Reason, long Tick) : InternalCommand;

/// <summary>
/// The party's hookup to NPC life (WP15b's <see cref="IPartyLife"/>, supplied by WP12b): joining suspends a life, leaving resumes it, and
/// someone with a life walks home rather than being put there. <paramref name="also"/> hears the same (a test's recorder).
/// </summary>
internal sealed class NpcPartyLife : IPartyLife
{
    private readonly Func<InternalCommand, string?> _dispatch;
    private readonly Func<string, bool> _lives;
    private readonly IPartyLife? _also;

    public NpcPartyLife(Func<InternalCommand, string?> dispatch, Func<string, bool> lives, IPartyLife? also)
    {
        _dispatch = dispatch;
        _lives = lives;
        _also = also;
    }

    public void TakeUp(PartyLifeEntry entry)
    {
        _dispatch(new TakeBackFromParty(entry.NpcId, entry.Reason, entry.Tick));
        _also?.TakeUp(entry);
    }

    public void Release(string npcId, long tick)
    {
        _dispatch(new HandToParty(npcId));
        _also?.Release(npcId, tick);
    }

    public bool Walks(string npcId) => _lives(npcId);
}

internal sealed partial class NpcSystem : INpcHireability, ISettingOutSource, IChatterSpeakers
{
    /// <summary>Who was hireable when last asked: what <see cref="HireabilityChanged"/> is raised against. Derived; rebuilt at load, quietly.</summary>
    private ImmutableSortedSet<string> _hireable = ImmutableSortedSet.Create<string>(StringComparer.Ordinal);

    // ── roles and hireability ──────────────────────────────────────────────

    /// <summary>The hireling roles a routine's town roles take on.</summary>
    private ImmutableArray<HirelingRole> HiresAs(RoutineDefinition routine) => NpcHireability.HiresAs(routine.Roles, Life.Roles);

    /// <summary>A resident's hire profile: their roles' hireling roles and the residents' body; null for one whose roles never hire out.</summary>
    private HirelingProfile? ResidentHire(RoutineDefinition routine) =>
        Life.ResidentBody is { } body && HiresAs(routine) is { IsEmpty: false } roles ? new HirelingProfile(roles, body) : null;

    /// <summary>
    /// Why someone with a life cannot be hired now (WP12b), worded after their name; null when they can, or have no life here (then their
    /// NPC file alone says). The party's own refusals - full, already with you, the wage - are the party's.
    /// </summary>
    public string? HireRefusal(string key)
    {
        if (LifeOf(key) is not { } life || !Life.Routines.TryGetValue(life.RoutineId, out var routine))
            return null;
        return NpcHireability.Refusal(life, routine, HiresAs(routine));
    }

    public bool IsHireable(string key) => Hireability(key)?.Hireable ?? false;

    public NpcHireView? Hireability(string key)
    {
        // An adventurer's body is theirs to answer for (WP13).
        if (!State.Npcs.TryGetValue(key, out var npc) || npc.Definition.Role == AdventurerSystem.Role)
            return null;
        var routine = LifeOf(key) is { } life ? Life.Routines.GetValueOrDefault(life.RoutineId) : null;
        var roles = routine?.Roles ?? ImmutableArray<string>.Empty;
        var hires = routine is not null ? HiresAs(routine) : npc.Definition.Hireling?.Roles ?? ImmutableArray<HirelingRole>.Empty;
        string? refusal = HireRefusal(key)
            ?? (npc.Definition.Hireling is null ? "does not hire on"
                : State.Companions.ContainsKey(key) ? "is already in someone's hire"
                : null);
        return new NpcHireView(key, refusal is null, refusal, roles, hires);
    }

    /// <summary>Raise <see cref="HireabilityChanged"/> for every key whose answer changed since last asked; quietly at load.</summary>
    private void Rehire(long tick, bool publish = true)
    {
        var now = State.Npcs.Keys.Where(IsHireable).ToImmutableSortedSet(StringComparer.Ordinal);
        if (publish)
        {
            foreach (string key in _hireable.Union(now).Where(k => _hireable.Contains(k) != now.Contains(k)))
                _context.Events.Publish(new HireabilityChanged(key, now.Contains(key), now.Contains(key) ? null : Hireability(key)?.Refusal ?? "is gone", tick));
        }
        _hireable = now;
    }

    // ── the hired state (party-life hookup) ────────────────────────────────

    public string? Handle(HandToParty command, long tick)
    {
        if (LifeOf(command.Key) is not { } life)
            return $"no one called {command.Key} lives here";
        SetLife(life with { Condition = NpcCondition.Hired, ConditionUntil = 0, Activity = NpcActivities.Idle, With = null, Route = NavRoute.None, StuckTicks = 0 });
        Rehire(tick);
        return null;
    }

    /// <summary>
    /// Someone the party let go takes their life up again where they stand (their body is where the party left it; nothing puts them home):
    /// at once, choosing the hour's activity and walking to it. Let go while the world catches up (lives not yet at that hour), they stay
    /// hired until the first hour that begins after it, which lets them go - so an absence in pieces lets them go at the same hour (T-22). A
    /// companion gravely wounded rests at home until they are well.
    /// </summary>
    public string? Handle(TakeBackFromParty command, long tick)
    {
        if (LifeOf(command.Key) is not { } life || State.World.NpcLife is not { } lives || !Life.Routines.TryGetValue(life.RoutineId, out var routine))
            return $"no one called {command.Key} lives here";
        if (State.Npcs.TryGetValue(command.Key, out var body))
            life = WithCell(life with { XMm = body.Body.XMm, ZMm = body.Body.ZMm, FacingMdeg = body.Body.FacingMdeg });
        if (Clock.Hour(command.Tick) > lives.Hour)
        {
            SetLife(life with { Condition = NpcCondition.Hired, ConditionUntil = Math.Max(1, command.Tick), Route = NavRoute.None, StuckTicks = 0 });
            return null;
        }
        life = life with { Condition = NpcCondition.Living, ConditionUntil = 0, Route = NavRoute.None, StuckTicks = 0 };
        if (command.Reason == "wounded" && State.Recovering.TryGetValue(command.Key, out long until) && until > tick)
            life = life with { Condition = NpcCondition.Injured, ConditionUntil = until };
        var context = HourContext(lives.Hour);
        var (activity, with) = life.Condition == NpcCondition.Living && context.Trip(routine) is { } trip
            ? (trip, null)
            : NpcLifeEngine.Choose(life, routine, Life.Routines, Life.Pairs, lives.PairsLastMet, lives.Npcs, Life.Rules, context);
        SetLife(life with { Activity = activity, With = with });
        _context.Events.Publish(new NpcReturnedToLife(command.Key, activity, tick));
        Rehire(tick);
        return null;
    }

    /// <summary>
    /// At load, after the party is back, lives and the party are made to agree: someone in the party whose life is not marked hired (a save
    /// from before schema 27, or a new world started with a party) is handed to it; a life still marked hired whose NPC the party no longer
    /// holds (their definition changed, a save edited) takes their life up again where they stand. Then who is hireable is noted, quietly.
    /// </summary>
    public void SettleParty()
    {
        foreach (string key in State.Companions.Keys.Where(k => LifeOf(k) is { Condition: not NpcCondition.Hired }).ToList())
            Handle(new HandToParty(key), State.WorldTick);
        foreach (var life in (State.World.NpcLife?.Npcs.Values ?? Enumerable.Empty<NpcLifeRecord>()).ToList())
        {
            if (life.Condition == NpcCondition.Hired && life.ConditionUntil == 0 && !State.Companions.ContainsKey(life.Key))
                Handle(new TakeBackFromParty(life.Key, "dismissed", State.WorldTick), State.WorldTick);
        }
        Rehire(State.WorldTick, publish: false);
    }

    /// <summary>Whether someone has a life here to walk back to when the party lets them go.</summary>
    public bool Lives(string key) => LifeOf(key) is not null;

    // ── trips (the SettingOut source) ──────────────────────────────────────

    private ulong TripDraw(RoutineDefinition routine, long day) =>
        RngChannel.Open(State.World.WorldSeed, RegionCell, "npc_life", $"trip:{routine.Id}@{day}").UInt64(0);

    /// <summary>What the trip planner has a routine doing in an hour: waiting to set out, away, or nothing of a trip's.</summary>
    private string? TripActivity(RoutineDefinition routine, long hour) =>
        NpcTrips.Covering(routine, hour, Clock.HoursPerDay, d => TripDraw(routine, d))?.ActivityAt(hour);

    private long TicksPerMinute => Math.Max(1, Clock.TicksPerDay / (Clock.HoursPerDay * 60L));

    /// <summary>
    /// Every SettingOut whose offer goes up in (fromTick, toTick], in tick order (#17's contract): each routine's planned trips, for whoever
    /// holds that routine and is living now - not hired, not injured. A pure read: the planner is a draw per routine and day, so any
    /// question about any span answers the same in the same world, and an absence and a load re-read the same offers.
    /// </summary>
    public IEnumerable<SettingOut> Between(long fromTick, long toTick)
    {
        if (toTick <= fromTick || State.World.NpcLife is not { } lives)
            return Enumerable.Empty<SettingOut>();
        long lead = Life.TripLeadMinutes * TicksPerMinute;
        var found = new List<SettingOut>();
        foreach (var routine in Life.Routines.Values.Where(r => r.Trip is not null))
        {
            if (!lives.Npcs.TryGetValue(routine.Key, out var life) || life.RoutineId != routine.Id || life.Condition != NpcCondition.Living
                || !State.Npcs.TryGetValue(routine.Key, out var npc))
                continue;
            var trip = routine.Trip!;
            for (long day = Clock.Day(Math.Max(0, fromTick)); day <= Clock.Day(toTick); day++)
            {
                if (NpcTrips.On(routine, day, Clock.HoursPerDay, TripDraw(routine, day)) is not { } planned)
                    continue;
                long depart = Clock.TickOfHour(planned.DepartHour), posted = depart - lead;
                if (posted <= fromTick || posted > toTick)
                    continue;
                found.Add(new SettingOut(npc.InstanceId, npc.Definition.Name, trip.Purpose, trip.PurposeName, trip.DestinationName, trip.MeetId, trip.MeetName,
                    trip.Meet.XMm, trip.Meet.ZMm, depart, posted));
            }
        }
        return found.OrderBy(s => s.Tick).ThenBy(s => s.Npc).ToList();
    }

    // ── chatter speakers ───────────────────────────────────────────────────

    /// <summary>
    /// Who may say an ambient line now (#17's contract): the role residents about their own lives - not hired - each as their roles' speaker
    /// type, in their town, or in the wilds while away on a trip. Named NPCs never are. A pure read of the world as it stands.
    /// </summary>
    public IEnumerable<ChatterSpeaker> At(long tick)
    {
        if (State.World.NpcLife is not { } lives)
            return Enumerable.Empty<ChatterSpeaker>();
        return lives.Npcs.Values
            .Where(l => l.Condition != NpcCondition.Hired && Life.Routines.TryGetValue(l.RoutineId, out var r) && r.IsSlot && State.Npcs.ContainsKey(l.Key))
            .Select(l =>
            {
                var routine = Life.Routines[l.RoutineId];
                var npc = State.Npcs[l.Key];
                string type = routine.Roles.Select(r => Life.Roles.GetValueOrDefault(r)?.SpeakerType).FirstOrDefault(t => t is not null) ?? "villager";
                string place = l.Activity == NpcActivities.Away ? "wilds" : routine.Settlement;
                return new ChatterSpeaker(npc.InstanceId, npc.Definition.Name, type, place, npc.Body.XMm, npc.Body.ZMm);
            })
            .OrderBy(s => s.Id).ToList();
    }
}
