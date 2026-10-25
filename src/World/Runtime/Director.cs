// UNNAMED World - the World Event Director: seeded, controlled-random world events, the world's history, and where word of them goes
// (WP 1H; QST-48, QST-55..58, WLD-89; QUEST_DIRECTOR_AND_WORLD_STATE, WORLD_ARCHITECTURE RK-A9)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;

namespace UNNAMED.World.Runtime;

/// <summary>
/// How far an event reaches (QST-56). The scale is the event's own; the director treats every scale alike for now, and a consequence
/// system reads it to decide who hears and what changes.
/// </summary>
public enum WorldEventScale
{
    Personal,
    Local,
    Regional,
    Continental,
    Cosmological,
}

/// <summary>Where an event is announced (QST-48). Each is a stream a consumer reads; <see cref="WorldEventChannels.Known"/> lists them.</summary>
public static class WorldEventChannels
{
    /// <summary>The "while you were away" log: written by the director itself during an absence.</summary>
    public const string AwayLog = "away_log";

    public const string NoticeBoard = "notice_board";
    public const string Chat = "chat";
    public const string Crier = "crier";
    public const string Rumour = "rumour";
    public const string Letter = "letter";
    public const string FactionIntelligence = "faction_intelligence";

    /// <summary>Every channel an event may name. Content naming any other is refused (DIR001).</summary>
    public static readonly ImmutableArray<string> Known =
        ImmutableArray.Create(AwayLog, NoticeBoard, Chat, Crier, Rumour, Letter, FactionIntelligence);
}

/// <summary>One way an event can end, and how likely it is against the others.</summary>
public sealed record WorldEventOutcome(string Key, int Weight);

/// <summary>
/// One kind of world event (<c>config.director</c>): its scale, the regions it can happen in (none: any), when in the world's life it
/// can first happen, its odds per game hour in permille, how long it lasts, how long before its kind can come again, where it is
/// announced, and how it can end.
/// </summary>
public sealed record WorldEventDefinition(
    string Id,
    WorldEventScale Scale,
    ImmutableArray<string> Regions,
    long MinWorldDay,
    int ChancePermille,
    int MinHours,
    int MaxHours,
    int CooldownHours,
    ImmutableArray<string> Channels,
    ImmutableArray<WorldEventOutcome> Outcomes)
{
    public bool HappensIn(string region) => Regions.IsDefaultOrEmpty || Regions.Contains(region);
}

/// <summary>
/// The director's rules (<c>config.director</c>): the events, how many may run at once in one region (WLD-89: 8), how many resolved
/// events the world's history keeps, and how recent intensity damps the odds - every event started in the window lowers every kind's
/// odds by the damping, so a busy week is followed by a quieter one.
/// </summary>
public sealed record DirectorSetup(
    ImmutableArray<WorldEventDefinition> Events,
    int MaxActivePerRegion,
    int HistoryKept,
    int IntensityWindowHours,
    int IntensityDampingPermille)
{
    /// <summary>No events: a content pack without <c>config.director</c>.</summary>
    public static readonly DirectorSetup Empty = new(ImmutableArray<WorldEventDefinition>.Empty, 8, 64, 48, 0);
}

/// <summary>An event running now: what it is, where, and the game hours it began and ends.</summary>
public sealed record WorldEventState(EntityId Id, string DefId, string Region, long StartHour, long EndHour);

/// <summary>A resolved event, as the world's history keeps it (QST-52's record, for every kind of event): what, where, when, and how it ended.</summary>
public sealed record WorldEventRecord(EntityId Id, string DefId, string Region, long StartHour, long EndHour, string Outcome);

/// <summary>
/// The director's whole state, in the world delta: the last game hour it settled, the events running, and the history, oldest first.
/// Everything it does next is a function of this, the world seed and the hour, so a world loaded from a save goes on exactly as the
/// saved one would have, and an absence caught up hour by hour is the same as one lived.
/// </summary>
public sealed record DirectorState(long SettledHour, ImmutableArray<WorldEventState> Active, ImmutableArray<WorldEventRecord> History)
{
    public void AddTo(CanonicalHasher h)
    {
        h.Add("unnamed.director/v1").Add(SettledHour).Add(Active.Length);
        foreach (var e in Active)
            h.Add(e.Id.Value).Add(e.DefId).Add(e.Region).Add(e.StartHour).Add(e.EndHour);
        h.Add(History.Length);
        foreach (var e in History)
            h.Add(e.Id.Value).Add(e.DefId).Add(e.Region).Add(e.StartHour).Add(e.EndHour).Add(e.Outcome);
    }
}

/// <summary>An event began or ended: the notice every channel it names receives.</summary>
public enum WorldEventPhase
{
    Began,
    Ended,
}

/// <summary>
/// Word of a world event (QST-48): the event, the phase, the channels it goes out on, and on its end how it ended. One notice per phase;
/// each channel's consumer takes the notices that name it.
/// </summary>
public sealed record WorldEventNotice(EntityId EventId, string DefId, string Region, WorldEventPhase Phase, ImmutableArray<string> Channels,
    string? Outcome, long Tick);

/// <summary>What the director's rules report as they settle an hour: an event began or ended. The system publishes it as a notice.</summary>
public sealed record WorldEventHappening(EntityId EventId, string DefId, string Region, WorldEventPhase Phase, ImmutableArray<string> Channels,
    string? Outcome, long Tick);

/// <summary>A world event as presentation reads it.</summary>
public sealed record WorldEventView(EntityId Id, string DefId, WorldEventScale Scale, string Region, long StartTick, long EndTick);

/// <summary>The kinds of away-log line the director writes (<see cref="AdvanceLine.Ref"/> is the event's definition).</summary>
public static class DirectorAdvanceKinds
{
    public const string Began = "event.began";
    public const string Ended = "event.ended";
}

/// <summary>
/// The director's rules as pure functions (QST-55). It settles one game hour at a time, on the world clock's absolute hours: first the
/// events whose time is up end, then each kind of event, in content order, may begin - if its region is under the cap, the world is old
/// enough, no event of its kind runs or began within its cooldown, and a seeded roll falls under its odds as recent intensity damps
/// them. Every roll is keyed on the world seed, the region, the kind and the hour (<see cref="RngChannel"/>), never on call order.
/// </summary>
public static class DirectorEngine
{
    private const uint StartSample = 0, DurationSample = 1, OutcomeSample = 2, IdSample = 3;

    /// <summary>The state a world starts the director with: nothing has happened, and the hour now is settled.</summary>
    public static DirectorState Start(long hour) =>
        new(hour, ImmutableArray<WorldEventState>.Empty, ImmutableArray<WorldEventRecord>.Empty);

    /// <summary>Settle every game hour after <see cref="DirectorState.SettledHour"/> up to <paramref name="toHour"/>, telling each notice.</summary>
    public static DirectorState Settle(DirectorState state, DirectorSetup setup, ulong worldSeed, string region, WorldClock clock, long toHour,
        Action<WorldEventHappening> notice)
    {
        if (setup.Events.IsEmpty)
            return toHour > state.SettledHour ? state with { SettledHour = toHour } : state;
        var active = state.Active.ToBuilder();
        var history = state.History.ToList();
        for (long hour = state.SettledHour + 1; hour <= toHour; hour++)
        {
            long tick = Math.Max(0, clock.TickOfHour(hour));
            foreach (var ended in active.Where(e => e.EndHour <= hour).ToList())
            {
                active.Remove(ended);
                var definition = setup.Events.FirstOrDefault(d => d.Id == ended.DefId);
                string outcome = definition is null ? "unknown" : Outcome(definition, Channel(worldSeed, ended.Region, ended.DefId, ended.StartHour));
                history.Add(new WorldEventRecord(ended.Id, ended.DefId, ended.Region, ended.StartHour, ended.EndHour, outcome));
                notice(new WorldEventHappening(ended.Id, ended.DefId, ended.Region, WorldEventPhase.Ended,
                    definition?.Channels ?? ImmutableArray<string>.Empty, outcome, tick));
            }
            if (history.Count > setup.HistoryKept)
                history.RemoveRange(0, history.Count - setup.HistoryKept);

            int recent = active.Count(e => e.StartHour > hour - setup.IntensityWindowHours)
                         + history.Count(e => e.StartHour > hour - setup.IntensityWindowHours);
            int damping = Math.Max(0, 1000 - recent * setup.IntensityDampingPermille);
            foreach (var definition in setup.Events)
            {
                if (!definition.HappensIn(region) || active.Count(e => e.Region == region) >= setup.MaxActivePerRegion)
                    continue;
                if (clock.Day(tick) < definition.MinWorldDay)
                    continue;
                if (active.Any(e => e.DefId == definition.Id)
                    || history.Any(e => e.DefId == definition.Id && e.StartHour > hour - definition.CooldownHours))
                    continue;
                var channel = Channel(worldSeed, region, definition.Id, hour);
                if ((long)(channel.UInt64(StartSample) % 1_000_000) >= (long)definition.ChancePermille * damping)
                    continue;
                long hours = definition.MinHours + (long)(channel.UInt64(DurationSample) % (ulong)(definition.MaxHours - definition.MinHours + 1));
                var started = new WorldEventState(Identity(channel), definition.Id, region, hour, hour + hours);
                active.Add(started);
                notice(new WorldEventHappening(started.Id, definition.Id, region, WorldEventPhase.Began, definition.Channels, null, tick));
            }
        }
        return new DirectorState(Math.Max(state.SettledHour, toHour), active.ToImmutable(), history.ToImmutableArray());
    }

    private static RngChannel Channel(ulong worldSeed, string region, string defId, long hour) =>
        RngChannel.Open(worldSeed, CellKey.OfWorld(0, 0), "director", $"{region}/{defId}/{hour}");

    private static string Outcome(WorldEventDefinition definition, RngChannel channel)
    {
        int total = definition.Outcomes.Sum(o => o.Weight);
        long roll = (long)(channel.UInt64(OutcomeSample) % (ulong)total);
        foreach (var outcome in definition.Outcomes)
        {
            roll -= outcome.Weight;
            if (roll < 0)
                return outcome.Key;
        }
        return definition.Outcomes[^1].Key;
    }

    /// <summary>The event's identity, from its roll: the same event is the same ID however the hour was reached.</summary>
    private static EntityId Identity(RngChannel channel)
    {
        Span<byte> bytes = stackalloc byte[10];
        BitConverter.TryWriteBytes(bytes, channel.UInt64(IdSample));
        BitConverter.TryWriteBytes(bytes[8..], (ushort)channel.UInt64(IdSample + 1));
        return EntityId.Create(EntityKind.WorldEvent, 1, bytes);
    }
}

/// <summary>
/// Owns: <see cref="StateSlice.Director"/>, the director's state in the world delta (WP 1H). Live, it settles each game hour as the
/// clock reaches it; through an absence it settles the same hours at once, so offline catch-up is live play, hour for hour. Every
/// notice is published; during an absence the ones naming the away log are also written to it.
/// </summary>
internal sealed class DirectorSystem : IAbstractAdvance
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;

    public DirectorSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }

    private RuntimeState State => _context.State;
    private DirectorSetup Setup => _context.Setup.Director;
    private WorldClock Clock => _context.Setup.Clock;
    private string Region => _context.Setup.Layout.Id;

    /// <summary>A world without director state starts it now: nothing is back-filled from before.</summary>
    public void Populate()
    {
        if (State.World.Director is null)
            State.SetDirector(_owner, DirectorEngine.Start(Clock.Hour(State.WorldTick)));
    }

    public void Tick(long tick)
    {
        if (Clock.Hour(tick) > State.World.Director!.SettledHour)
            SettleTo(Clock.Hour(tick), null);
    }

    public void Advance(long fromTick, long toTick, AdvanceLog log) => SettleTo(Clock.Hour(toTick), log);

    private void SettleTo(long hour, AdvanceLog? log)
    {
        var next = DirectorEngine.Settle(State.World.Director!, Setup, State.World.WorldSeed, Region, Clock, hour, happened =>
        {
            _context.Events.Publish(new WorldEventNotice(happened.EventId, happened.DefId, happened.Region, happened.Phase, happened.Channels,
                happened.Outcome, happened.Tick));
            if (log is not null && happened.Channels.Contains(WorldEventChannels.AwayLog))
                log.Add(new AdvanceLine(happened.Tick, happened.Phase == WorldEventPhase.Began ? DirectorAdvanceKinds.Began : DirectorAdvanceKinds.Ended,
                    happened.DefId) { PlaceId = happened.Region, SubjectId = happened.EventId.Value, Outcome = happened.Outcome });
        });
        State.SetDirector(_owner, next);
    }

    public ImmutableArray<WorldEventView> Views() => State.World.Director!.Active
        .Select(e => new WorldEventView(e.Id, e.DefId, Setup.Events.FirstOrDefault(d => d.Id == e.DefId)?.Scale ?? WorldEventScale.Local,
            e.Region, Math.Max(0, Clock.TickOfHour(e.StartHour)), Clock.TickOfHour(e.EndHour)))
        .ToImmutableArray();
}
