// UNNAMED World - the chat system: what is said on each channel, worded from simulation facts and the chatter corpus (WP16)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Quests;

namespace UNNAMED.World.Runtime;

/// <summary>
/// Chat (WP16): presentation of simulation facts, never a source of them. It owns no state slice, writes nothing, dispatches nothing, and
/// nothing it holds is saved; a load starts with an empty history. It words three kinds of message:
/// <list type="bullet">
/// <item>offers to set out together (<see cref="SettingOut"/>, from <see cref="LfgSystem"/>), their answers and departures;</item>
/// <item>bounty news: a claim by someone else on World and System, the character's own completions on System;</item>
/// <item>ambient chatter: each game hour, each speaker (<see cref="IChatterSpeakers"/>) may say one corpus line whose tags fit them, the
/// hour, the weather and the region's recent events. The roll and the pick come from <see cref="RngChannel"/> keyed by speaker, hour and
/// channel; World lines are capped per hour; a speaker never repeats a line within <see cref="ChatRules.SpeakerRepeatHours"/>, nor the
/// region within <see cref="ChatRules.RegionRepeatHours"/>; and a named NPC never says a corpus line.</item>
/// </list>
/// Everything is said whoever hears it; the character is told what they hear (<see cref="ChatAudience"/>), as an Otherserver would tell
/// each of its listeners.
/// </summary>
internal sealed class ChatSystem
{
    private const string Subsystem = "chat";
    private const string SystemSpeaker = "system";

    private readonly SystemContext _context;
    private readonly EntityId _player;
    private readonly CellKey _region;
    private readonly LfgSystem _lfg;
    private readonly Func<long, WeatherState> _weather;
    private readonly IChatterSpeakers _speakers;
    private readonly List<ChatMessage> _heard = new();
    private readonly List<ChatMessage> _pending = new();
    private readonly List<(string Event, long Tick, ImmutableSortedDictionary<string, string> Facts)> _recent = new();
    private readonly List<(string Speaker, string Key, long Hour)> _said = new();
    private readonly HashSet<(string QuestId, long Tick)> _lost = new();
    private long _plannedHour = long.MinValue;

    public ChatSystem(SystemContext context, EntityId player, CellKey region, LfgSystem lfg, Func<long, WeatherState> weather, IChatterSpeakers speakers)
    {
        _speakers = speakers;
        _context = context;
        _player = player;
        _region = region;
        _lfg = lfg;
        _weather = weather;
    }

    private RuntimeState State => _context.State;
    private ChatSetup Chat => _context.Setup.Chat;
    private WorldClock Clock => _context.Setup.Clock;
    private string RegionId => _context.Setup.Layout.Id;
    private long TicksPerHour => Clock.TicksPerDay / Clock.HoursPerDay;

    /// <summary>What the character heard, oldest first, at most <see cref="ChatRules.HistoryMessages"/>.</summary>
    public ImmutableArray<ChatMessage> Heard => _heard.ToImmutableArray();

    public void Listen(SimulationEvents events)
    {
        events.Listen<SettingOut>(SetOut);
        events.Listen<LfgJoined>(Joined);
        events.Listen<LfgMissed>(Missed);
        events.Listen<AllyGroupFormed>(Formed);
        events.Listen<QuestFailed>(e => _lost.Add((e.QuestId, e.Tick)));
        events.Listen<BountyClaimed>(Claimed);
        events.Listen<QuestCompleted>(Completed);
        events.Listen<WeatherChanged>(e =>
        {
            if (e.From is not null)
                Remember("weather_turned", e.Tick, Facts());
        });
        events.Listen<CreatureKilled>(e => Remember("creature_slain", e.Tick, Facts(("creature", Name(e.DefId, "a beast")))));
        events.Listen<HirelingDied>(e => Remember("hireling_died", e.Tick, Facts()));
        events.Listen<MarketsSettled>(e => Remember("market_day", e.Tick, Facts()));
    }

    public void Tick(long tick)
    {
        long hour = Clock.Hour(tick);
        if (hour != _plannedHour)
        {
            _plannedHour = hour;
            Plan(hour, tick);
        }
        foreach (var message in _pending.Where(m => m.Tick <= tick).ToList())
        {
            _pending.Remove(message);
            Say(message);
        }
    }

    /// <summary>
    /// After an absence (WP11): nobody heard the hours the character was away, so nothing is said for them; the hour they come back to is
    /// planned afresh, and a day or more away is itself news. Chat is not world state, so this is not an abstract advance of its own.
    /// </summary>
    public void AfterAbsence(AdvanceLog log)
    {
        _pending.Clear();
        // What the catch-up raised (an offer posted, a bounty taken while away) was said to no one.
        _heard.RemoveAll(m => m.Tick > log.FromTick && m.Tick <= log.ToTick);
        _plannedHour = long.MinValue;
        if (log.Ticks >= Clock.TicksPerDay)
            Remember("long_absence", log.ToTick, Facts());
    }

    // ---- facts worded as templates ----

    private void SetOut(SettingOut s)
    {
        Remember("setting_out", s.Tick, Facts());
        Template($"lfg.{s.Purpose}", s.Npc, ChatChannel.World, s.Tick, Facts(("speaker", s.NpcName), ("purpose", s.PurposeName),
            ("destination", s.Destination), ("anchor", s.AnchorName), ("depart_time", ClockTime(s.DepartTick))));
    }

    private void Joined(LfgJoined e)
    {
        var facts = Facts(("speaker", e.NpcName), ("anchor", e.AnchorName), ("depart_time", ClockTime(e.DepartTick)));
        Template("lfg.joined", e.Npc, ChatChannel.Party, e.Tick, facts, group: LfgSystem.GroupKey(e.Npc, e.DepartTick));
        Template("sys.lfg_joined", null, ChatChannel.System, e.Tick, facts);
    }

    private void Missed(LfgMissed e)
    {
        Template("lfg.left_alone", e.Npc, ChatChannel.Local, e.Tick, Facts(("speaker", e.NpcName), ("destination", e.Destination)),
            origin: new ChatOrigin(e.MeetXMm, e.MeetZMm));
        Template("sys.lfg_missed", null, ChatChannel.System, e.Tick, Facts(("speaker", e.NpcName)));
    }

    private void Formed(AllyGroupFormed e)
    {
        Remember("party_set_out", e.Tick, Facts());
        Template("party.set_out", e.Group.Leader, ChatChannel.Party, e.Tick,
            Facts(("speaker", e.LeaderName), ("destination", e.Group.Destination), ("members", $"{e.LeaderName} and you")), group: e.Group.GroupKey);
    }

    private void Claimed(BountyClaimed e)
    {
        string title = _context.Setup.Quests.Quests.TryGetValue(e.QuestId, out var quest) ? quest.Title : "a bounty";
        if (e.ClaimantId == _player.Value)
        {
            Remember("bounty_completed", e.Tick, Facts(("bounty", title)));
            Template("sys.bounty_completed", null, ChatChannel.System, e.Tick, Facts(("bounty", title)));
            return;
        }
        var facts = Facts(("claimant", ClaimantName(e.ClaimantId)), ("bounty", title));
        Remember("bounty_claimed", e.Tick, facts);
        Template("news.bounty_claimed", null, ChatChannel.World, e.Tick, facts);
        Template(_lost.Remove((e.QuestId, e.Tick)) ? "sys.bounty_lost" : "sys.bounty_claimed", null, ChatChannel.System, e.Tick, facts);
    }

    private void Completed(QuestCompleted e)
    {
        if (!_context.Setup.Quests.Quests.TryGetValue(e.QuestId, out var quest))
            return;
        if (quest.Track != QuestTrack.Bounty)
        {
            Remember("quest_completed", e.Tick, Facts());
            return;
        }
        // A competitive bounty's completion is its claim, worded there; an ordinary one is worded here.
        if (!quest.Competitive)
        {
            Remember("bounty_completed", e.Tick, Facts(("bounty", quest.Title)));
            Template("sys.bounty_completed", null, ChatChannel.System, e.Tick, Facts(("bounty", quest.Title)));
        }
    }

    private void Template(string use, EntityId? speaker, ChatChannel channel, long tick, ImmutableSortedDictionary<string, string> facts,
        ChatOrigin? origin = null, string? group = null)
    {
        var templates = Chat.TemplatesFor(use);
        if (templates.IsEmpty)
            return;
        var rng = Channel(speaker?.Value ?? SystemSpeaker, Clock.Hour(tick), channel, use);
        var line = templates[rng.Int(0, 0, templates.Length)];
        Say(Message(speaker, channel, line.Key, facts, tick, origin, group) with { PlayRelevant = true });
    }

    // ---- ambient chatter ----

    /// <summary>The hour's ambient lines: each speaker rolls on each channel, and the first roll that finds a line is what they say this hour.</summary>
    private void Plan(long hour, long tick)
    {
        if (Chat.Corpus.IsEmpty)
            return;
        long hourStart = Clock.TickOfHour(hour);
        long window = Chat.Rules.RecentEventHours * TicksPerHour;
        _recent.RemoveAll(e => tick - e.Tick > window);
        _said.RemoveAll(s => hour - s.Hour >= Math.Max(Chat.Rules.SpeakerRepeatHours, Chat.Rules.RegionRepeatHours));
        string? band = Chat.TimeBandOf((int)(((hour % Clock.HoursPerDay) + Clock.HoursPerDay) % Clock.HoursPerDay));
        string weather = WeatherStates.Key(_weather(tick));
        var named = _context.Setup.Social.Npcs.Keys.Select(NpcSystem.InstanceIdOf).ToHashSet();
        int worldLines = 0;
        foreach (var speaker in _speakers.At(tick).Where(s => !s.Named && !named.Contains(s.Id)).OrderBy(s => s.Id))
        {
            foreach (var channel in new[] { ChatChannel.Local, ChatChannel.World })
            {
                var rng = Channel(speaker.Id.Value, hour, channel, null);
                int chance = channel == ChatChannel.Local ? Chat.Rules.LocalChancePercent : Chat.Rules.WorldChancePercent;
                if (rng.Int(0, 0, 100) >= chance || (channel == ChatChannel.World && worldLines >= Chat.Rules.WorldLinesPerHour))
                    continue;
                var candidates = Candidates(speaker, channel, band, weather, hour).ToList();
                if (candidates.Count == 0)
                    continue;
                int pick = rng.Int(1, 0, candidates.Sum(c => c.Weight));
                var (line, _, facts) = candidates.First(c => (pick -= c.Weight) < 0);
                // Planned part-way into the hour (a load, a return): what would have been said already is said now.
                long at = Math.Max(tick, hourStart + rng.Int(2, 0, (int)Math.Max(1, TicksPerHour)));
                _said.Add((speaker.Id.Value, line.Key, hour));
                if (channel == ChatChannel.World)
                    worldLines++;
                var values = facts.SetItem("place", Chat.Places.GetValueOrDefault(speaker.Place, speaker.Place))
                    .SetItem("weather", weather).SetItem("time_band", band ?? string.Empty);
                _pending.Add(Message(speaker.Id, channel, line.Key, values, at, channel == ChatChannel.Local ? new ChatOrigin(speaker.XMm, speaker.ZMm) : null, null));
                break;
            }
        }
    }

    /// <summary>The corpus lines a speaker may say now, each with its weight and the event facts its slots read.</summary>
    private IEnumerable<(ChatLine Line, int Weight, ImmutableSortedDictionary<string, string> Facts)> Candidates(ChatterSpeaker speaker,
        ChatChannel channel, string? band, string weather, long hour)
    {
        foreach (var line in Chat.Corpus)
        {
            if (line.Channel != channel || !Fits(line.Speakers, speaker.SpeakerType) || !Fits(line.Places, speaker.Place)
                || !Fits(line.Times, band) || !Fits(line.Weather, weather) || Repeated(line.Key, speaker.Id.Value, hour))
                continue;
            if (line.Events.IsEmpty)
            {
                yield return (line, 1, Facts());
                continue;
            }
            // The newest of the events it waits on is what it speaks of.
            var recent = _recent.LastOrDefault(e => line.Events.Contains(e.Event));
            if (recent.Event is not null && line.Slots.All(slot => IsAmbientSlot(slot) || recent.Facts.ContainsKey(slot)))
                yield return (line, Chat.Rules.EventWeight, recent.Facts);
        }
    }

    private static bool IsAmbientSlot(string slot) => slot is "place" or "weather" or "time_band";

    private static bool Fits(ImmutableArray<string> tags, string? value) => tags.IsEmpty || (value is not null && tags.Contains(value));

    private bool Repeated(string key, string speaker, long hour) =>
        _said.Any(s => s.Key == key && ((s.Speaker == speaker && hour - s.Hour < Chat.Rules.SpeakerRepeatHours) || hour - s.Hour < Chat.Rules.RegionRepeatHours));

    // ---- saying and hearing ----

    private ChatMessage Message(EntityId? speaker, ChatChannel channel, string key, ImmutableSortedDictionary<string, string> values, long tick,
        ChatOrigin? origin, string? group) => new(speaker, channel, key, values, tick)
    {
        SpeakerKind = speaker is null ? SpeakerKind.System : speaker == _player ? SpeakerKind.Player : SpeakerKind.Npc,
        RegionId = channel == ChatChannel.World ? RegionId : null,
        Origin = channel == ChatChannel.Local ? origin : null,
        GroupKey = channel == ChatChannel.Party ? group : null,
    };

    private void Say(ChatMessage message)
    {
        var listener = new ChatListener(_player, RegionId, State.Body.XMm, State.Body.ZMm, _lfg.GroupsOf(_player));
        if (!ChatAudience.Hears(message, listener, Chat.Rules.LocalRadiusMm))
            return;
        _heard.Add(message);
        if (_heard.Count > Chat.Rules.HistoryMessages)
            _heard.RemoveRange(0, _heard.Count - Chat.Rules.HistoryMessages);
        _context.Events.Publish(message);
    }

    private void Remember(string @event, long tick, ImmutableSortedDictionary<string, string> facts) => _recent.Add((@event, tick, facts));

    private RngChannel Channel(string speaker, long hour, ChatChannel channel, string? use) =>
        RngChannel.Open(State.World.WorldSeed, _region, Subsystem, $"{speaker}/{hour}/{channel.ToString().ToLowerInvariant()}{(use is null ? "" : "/" + use)}");

    private string ClockTime(long tick)
    {
        int minute = Clock.MinuteOfDay(tick);
        return $"{minute / 60:00}:{minute % 60:00}";
    }

    private string Name(string definitionId, string otherwise) => Chat.Names.GetValueOrDefault(definitionId, otherwise);

    private string ClaimantName(string claimantId) =>
        State.Npcs.Values.FirstOrDefault(n => n.InstanceId.Value == claimantId)?.Definition.Name ?? _context.OtherActorName(claimantId) ?? "someone";

    private static ImmutableSortedDictionary<string, string> Facts(params (string Slot, string Value)[] facts) =>
        facts.ToImmutableSortedDictionary(f => f.Slot, f => f.Value, StringComparer.Ordinal);
}
