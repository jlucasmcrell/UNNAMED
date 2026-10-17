// UNNAMED World - NPC life in the running world: hours lived, arrivals, departures, the offline catch-up and who is at work (WP12)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>
/// To <see cref="NpcSystem"/>: a role resident leaves their slot by a rule event (DN4) - WP13's outcome tables, a hireling's death. The
/// slot waits 3-7 days for an arrival. Refused for a named NPC: they are essential.
/// </summary>
internal sealed record DepartNpc(string Key, string Reason) : InternalCommand;

/// <summary>To <see cref="NpcSystem"/>: an NPC is injured until a tick and rests at home until then (WP13's outcome tables).</summary>
internal sealed record InjureNpc(string Key, long UntilTick) : InternalCommand;

/// <summary>An NPC's schedule moved them on to a new activity at an hour's turn: what they were doing, and what now.</summary>
public sealed record NpcActivityChanged(string Key, string From, string To, long Tick);

/// <summary>Someone new took up a role slot (DN4): their key, name and role.</summary>
public sealed record NpcArrived(string Key, string Name, string Role, long Tick);

/// <summary>A role resident left their slot, and when someone new will take it.</summary>
public sealed record NpcDeparted(string Key, string Name, string Reason, long ArrivalDay, long Tick);

/// <summary>An NPC's life as a view: who, what they do, their condition, and whether they are at work now.</summary>
public sealed record NpcLifeView(string Key, string Name, string Role, string Settlement, string Activity, NpcCondition Condition, bool Resident,
    bool AtWork, long ArrivedDay);

/// <summary>The kinds of <see cref="AdvanceLine"/> NPC life writes, for the away sheet.</summary>
public static class NpcAdvanceKinds
{
    /// <summary>Someone new came to a town while away; <see cref="AdvanceLine.Ref"/> is their slot.</summary>
    public const string Arrived = "npc.arrived";
}

internal sealed partial class NpcSystem : IAbstractAdvance, IJobOutput, ITeachingHours
{
    /// <summary>How near their work anchor an NPC must stand to be at work: a counter's width.</summary>
    public const long AtWorkMm = 1_500;

    private NpcLifeSetup Life => _context.Setup.NpcLife;
    private WorldClock Clock => _context.Setup.Clock;

    /// <summary>An NPC's life by key; null for one with no routine here.</summary>
    private NpcLifeRecord? LifeOf(string key) => State.World.NpcLife?.Npcs.GetValueOrDefault(key);

    /// <summary>
    /// The lives the world delta holds, carried onto the routines content defines now, or a new world's: each named NPC where the region puts
    /// them, each slot's first resident where their schedule has them. Then every resident's body. A region with no routines holds no lives.
    /// </summary>
    private void PopulateLives()
    {
        long hour = Clock.Hour(State.WorldTick);
        if (Life.Routines.IsEmpty)
        {
            if (State.World.NpcLife is not null)
                State.SetNpcLife(_owner, NpcLifeState.Empty(hour));
            return;
        }
        var context = HourContext(hour);
        var given = new HashSet<string>(StringComparer.Ordinal);
        var life = State.World.NpcLife is { } saved
            ? NpcLifeEngine.Reconcile(saved, Life.Routines, Life.Rules, context, (r, g) => NameFor(r, g, given), PlaceOf)
            : NpcLifeEngine.Begin(Life.Routines, Life.Rules, context, (r, g) => NameFor(r, g, given), PlaceOf);
        // A named hireling who died in the character's service (DN9) has no life to go on with. A role slot's key is the slot's, not the
        // person's: a resident who died left a vacancy, and whoever arrives in it is someone new.
        var living = life.Npcs.Where(kv => !State.Fallen.Contains(kv.Key) || (Life.Routines.TryGetValue(kv.Value.RoutineId, out var r) && r.IsSlot));
        State.SetNpcLife(_owner, life with { Npcs = living.ToImmutableSortedDictionary(kv => kv.Key, kv => WithCell(kv.Value), StringComparer.Ordinal) });
        foreach (var npc in State.World.NpcLife!.Npcs.Values)
            Embody(npc);
    }

    /// <summary>A life's body in the world: a named NPC's moved to their saved pose; a resident made, with their own instance.</summary>
    private void Embody(NpcLifeRecord life)
    {
        if (!Life.Routines.TryGetValue(life.RoutineId, out var routine))
            return;
        if (routine.NpcId is { } npcId)
        {
            if (State.Npcs.TryGetValue(npcId, out var named) && !State.Companions.ContainsKey(npcId))
                Place(named, BodyAt(life.XMm, life.ZMm, life.FacingMdeg));
            return;
        }
        var id = ResidentIdOf(routine.Id, life.Generation);
        if (!State.World.Registry.Exists(id))
            State.World.Registry.CreateEntity(DefinitionId.Parse(routine.Id), id);
        var home = routine.Anchors[NpcAnchorKinds.Home];
        // Residents have no NPC file: what they hire on as comes from their town roles (WP12b), and their body from config.
        var definition = new NpcDefinition(routine.Id, life.Name ?? routine.Role, routine.Role, ImmutableArray<string>.Empty, null, null) { Hireling = ResidentHire(routine) };
        State.SetNpc(_owner, new NpcState(definition, id, new NpcSite(routine.Id, home.XMm, home.ZMm, home.FacingMdeg), BodyAt(life.XMm, life.ZMm, life.FacingMdeg)));
    }

    /// <summary>Where a new life starts: a named NPC at their place in the region; a resident wherever their routine first sends them.</summary>
    private (long X, long Z, int Facing)? PlaceOf(string key) =>
        _context.Setup.Layout.Npcs.FirstOrDefault(n => n.NpcId == key) is { } site ? (site.XMm, site.ZMm, site.FacingMdeg) : null;

    /// <summary>
    /// A resident's name (WP2's Suggest name source): their people's name a sample picks, passing over names their settlement's residents
    /// already carry while the list lasts. Never invented: with no names, the role.
    /// </summary>
    private string? NameFor(RoutineDefinition routine, int generation, HashSet<string> given)
    {
        if (routine.PeopleId is not { } people || !_context.Setup.Character.Peoples.TryGetValue(people, out var definition) || definition.Names.IsEmpty)
            return null;
        var taken = (State.World.NpcLife?.Npcs.Values ?? Enumerable.Empty<NpcLifeRecord>())
            .Where(n => n.Name is not null && Life.Routines.TryGetValue(n.RoutineId, out var r) && r.Settlement == routine.Settlement)
            .Select(n => n.Name!).ToHashSet(StringComparer.Ordinal);
        // The state is the batch's start: the names this batch has already given are taken too.
        taken.UnionWith(given);
        ulong sample = RngChannel.Open(State.World.WorldSeed, RegionCell, "npc_life", $"name:{routine.Id}#{generation}").UInt64(0);
        string name = definition.Names.PickUnused(sample, taken) ?? definition.Names.Pick(sample);
        given.Add(name);
        return name;
    }

    private CellKey RegionCell => new(CellKey.Parse(_context.Setup.Layout.CellKeys[0]).Region, 0, 0);

    /// <summary>What an hour's step reads: its first tick, day and hour of day, and what the trip planner has each routine doing.</summary>
    private NpcHourContext HourContext(long hour)
    {
        long tick = Math.Max(0, Clock.TickOfHour(hour));
        int hourOfDay = (int)(((hour % Clock.HoursPerDay) + Clock.HoursPerDay) % Clock.HoursPerDay);
        return new NpcHourContext(hour, tick, Clock.Day(tick), hourOfDay) { Trip = routine => TripActivity(routine, hour) };
    }

    /// <summary>FNV-1a over a key's characters: stable across runs and machines, unlike <see cref="string.GetHashCode()"/>.</summary>
    private static uint KeyHash(string key)
    {
        uint h = 2_166_136_261;
        foreach (char c in key)
            h = (h ^ c) * 16_777_619;
        return h;
    }

    /// <summary>Lives cross every hour that has begun by <paramref name="tick"/>, then the bodies of those far off are put where they are headed.</summary>
    private void TickLives(long tick)
    {
        if (State.World.NpcLife is not { } life || Clock.Hour(tick) <= life.Hour)
            return;
        LiveTo(Clock.Hour(tick), tick, publish: true);
        Park();
        Rehire(tick);
    }

    /// <summary>Every hour from the state's to <paramref name="hour"/>, in turn; the arrivals take their bodies as they come.</summary>
    private int LiveTo(long hour, long tick, bool publish, AdvanceLog? log = null)
    {
        int arrivals = 0;
        while (State.World.NpcLife is { } life && life.Hour < hour)
        {
            var before = life.Npcs;
            var given = new HashSet<string>(StringComparer.Ordinal);
            var (next, happenings) = NpcLifeEngine.Hour(life, Life.Routines, Life.Rules, HourContext(life.Hour + 1), (r, g) => NameFor(r, g, given), CanArrive);
            State.SetNpcLife(_owner, next);
            foreach (var happening in happenings.Where(h => h.Kind == NpcLifeEngine.Arrived))
            {
                var npc = next.Npcs[happening.Key];
                SetLife(WithCell(npc));
                Embody(npc);
                arrivals++;
                var routine = Life.Routines[npc.RoutineId];
                _context.Events.Publish(new NpcArrived(npc.Key, npc.Name ?? routine.Role, routine.Role, tick));
                log?.Add(new AdvanceLine(tick, NpcAdvanceKinds.Arrived, npc.Key));
            }
            if (!publish)
                continue;
            foreach (var npc in next.Npcs.Values)
            {
                if (before.TryGetValue(npc.Key, out var was) && was.Activity != npc.Activity)
                    _context.Events.Publish(new NpcActivityChanged(npc.Key, was.Activity, npc.Activity, tick));
            }
        }
        return arrivals;
    }

    /// <summary>Spawn suppression (WORLD_ARCHITECTURE.md §5): nobody arrives in sight. A slot whose home lies in a tier-A cell waits an hour.</summary>
    private bool CanArrive(RoutineDefinition slot)
    {
        var home = slot.Anchors[NpcAnchorKinds.Home];
        return TierAt(home.XMm, home.ZMm) != SimulationTier.A;
    }

    /// <summary>
    /// The world going on while the character was away (WP11): lives cross every hour of the absence at once, by the same step the running
    /// world takes, on absolute hour boundaries (T-22); then, if any hour passed, every body is where its routine has it now, standing, its
    /// route forgotten - behind the loading screen, never in sight. A companion's body is the party's.
    /// </summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
        if (State.World.NpcLife is not { } life || Clock.Hour(toTick) <= life.Hour)
            return;
        LiveTo(Clock.Hour(toTick), toTick, publish: false, log);
        foreach (var npc in State.World.NpcLife!.Npcs.Values)
        {
            // The party's, or let go by it and not yet back to their life: their body stays where the party left it.
            if (State.Companions.ContainsKey(npc.Key) || npc.Condition == NpcCondition.Hired)
                continue;
            var (x, z, facing) = Goal(npc);
            Settle(npc, x, z, facing);
        }
        Rehire(toTick);
    }

    /// <summary>Put a body down at a pose, standing, its route forgotten: only ever out of sight (C or D, or during the catch-up).</summary>
    private void Settle(NpcLifeRecord npc, long x, long z, int facing)
    {
        SetLife(WithCell(npc with { XMm = x, ZMm = z, FacingMdeg = facing, Route = NavRoute.None, StuckTicks = 0 }));
        if (State.Npcs.TryGetValue(npc.Key, out var body))
            Place(body, BodyAt(x, z, facing));
    }

    /// <summary>A resident leaves (DN4). A named NPC never does by rule.</summary>
    public string? Handle(DepartNpc command, long tick)
    {
        if (State.World.NpcLife is not { } life || !life.Npcs.TryGetValue(command.Key, out var npc))
            return $"no one called {command.Key} lives here";
        if (npc.Condition == NpcCondition.Hired)
            return $"{command.Key} is in the party: what befalls them there is the party's";
        ulong sample = RngChannel.Open(State.World.WorldSeed, RegionCell, "npc_life", $"arrival:{npc.RoutineId}#{npc.Generation + 1}").UInt64(0);
        var next = NpcLifeEngine.Depart(life, Life.Routines, Life.Rules, command.Key, Clock.Day(tick), sample, out string? refusal);
        if (next is null)
            return refusal;
        State.SetNpcLife(_owner, next);
        if (State.Npcs.TryGetValue(command.Key, out var body))
        {
            State.RemoveNpc(_owner, command.Key);
            if (State.World.Registry.Exists(body.InstanceId))
                State.World.Registry.DestroyEntity(body.InstanceId);
        }
        var vacancy = next.Vacancies[npc.RoutineId];
        _context.Events.Publish(new NpcDeparted(command.Key, npc.Name ?? command.Key, command.Reason, vacancy.ArrivalDay, tick));
        Rehire(tick);
        return null;
    }

    public string? Handle(InjureNpc command)
    {
        if (LifeOf(command.Key) is not { } npc)
            return $"no one called {command.Key} lives here";
        if (command.UntilTick <= State.WorldTick)
            return "an injury lasts into the future";
        if (npc.Condition == NpcCondition.Hired)
            return $"{command.Key} is in the party: what befalls them there is the party's";
        SetLife(npc with { Condition = NpcCondition.Injured, ConditionUntil = command.UntilTick, Activity = NpcActivities.Sleep });
        Rehire(State.WorldTick);
        return null;
    }

    /// <summary>What the jobs make on a day (WP14's hook): each job's daily output for each day its holder held it.</summary>
    public IEnumerable<(string MarketId, string ItemId, long Count)> On(long day) =>
        State.World.NpcLife is { } life
            ? NpcLifeEngine.JobOutput(life, Life.Routines, day, Clock.TicksPerDay).Select(job => (job.MarketId, job.ItemId, job.PerDay)).ToList()
            : Enumerable.Empty<(string, string, long)>();

    // ── who is where, doing what ────────────────────────────────────────────

    /// <summary>Whether an NPC with a routine is working at their work anchor now. One with no routine always is.</summary>
    public bool AtWork(string key)
    {
        if (LifeOf(key) is not { } life || !Life.Routines.TryGetValue(life.RoutineId, out var routine))
            return true;
        if (life.Activity != NpcActivities.Work || !routine.Anchors.TryGetValue(NpcAnchorKinds.Work, out var anchor)
            || !State.Npcs.TryGetValue(key, out var npc))
            return false;
        long dx = npc.Body.XMm - anchor.XMm, dz = npc.Body.ZMm - anchor.ZMm;
        return dx * dx + dz * dz <= AtWorkMm * AtWorkMm;
    }

    /// <summary>
    /// Why a merchant will not trade now (§2.13: only at work, 06:00-20:00), worded as a reason the UI can show; null when they will. An NPC
    /// with no routine trades whenever they are spoken to, as before.
    /// </summary>
    public string? TradeRefusal(NpcState npc)
    {
        if (LifeOf(npc.Definition.Id) is null)
            return null;
        var rules = Life.Rules;
        if (!rules.IsTradingMinute(Clock.MinuteOfDay(State.WorldTick)))
            return $"{npc.Definition.Name} has shut up for the night: trading is {Time(rules.TradeFromMinute)}-{Time(rules.TradeToMinute)}; come back in the morning";
        return AtWork(npc.Definition.Id) ? null : $"{npc.Definition.Name} is away from their work: they trade at it, {Time(rules.TradeFromMinute)}-{Time(rules.TradeToMinute)}";
    }

    /// <summary>
    /// Why an NPC will not teach now (WP9's hook): a teacher teaches only at their work anchor, in their work hours, and not while in the
    /// character's hire (DN13). Null when they will; an NPC with no routine teaches whenever spoken to, unless hired.
    /// </summary>
    string? ITeachingHours.Unavailable(string npcId, long tick) => TeachRefusal(npcId);

    public string? TeachRefusal(string npcId)
    {
        string name = State.Npcs.TryGetValue(npcId, out var npc) ? npc.Definition.Name : npcId;
        if (State.Companions.ContainsKey(npcId))
            return $"{name} is in your hire: they teach only at their work";
        if (LifeOf(npcId) is not { } life || !Life.Routines.TryGetValue(life.RoutineId, out var routine))
            return null;
        int hourOfDay = Clock.MinuteOfDay(State.WorldTick) / 60;
        if (!routine.WorksAt(hourOfDay))
            return $"{name} teaches at their work, {routine.WorkFromHour:00}:00-{routine.WorkToHour:00}:00";
        return AtWork(npcId) ? null : $"{name} is away from their work: they teach there, {routine.WorkFromHour:00}:00-{routine.WorkToHour:00}:00";
    }

    public ImmutableArray<NpcLifeView> Lives() =>
        (State.World.NpcLife?.Npcs.Values ?? Enumerable.Empty<NpcLifeRecord>()).Where(n => Life.Routines.ContainsKey(n.RoutineId)).Select(n =>
        {
            var routine = Life.Routines[n.RoutineId];
            string name = n.Name ?? (State.Npcs.TryGetValue(n.Key, out var npc) ? npc.Definition.Name : n.Key);
            return new NpcLifeView(n.Key, name, routine.Role, routine.Settlement, n.Activity, n.Condition, routine.IsSlot, AtWork(n.Key), n.ArrivedDay);
        }).ToImmutableArray();

    private static string Time(int minute) => $"{minute / 60:00}:{minute % 60:00}";

    private void SetLife(NpcLifeRecord npc)
    {
        if (State.World.NpcLife is { } life && life.Npcs.GetValueOrDefault(npc.Key) != npc)
            State.SetNpcLife(_owner, life with { Npcs = life.Npcs.SetItem(npc.Key, npc) });
    }

    private static NpcLifeRecord WithCell(NpcLifeRecord npc) =>
        npc with { Cell = CellKey.OfWorld(npc.XMm / 1000.0, npc.ZMm / 1000.0).ToString() };
}
