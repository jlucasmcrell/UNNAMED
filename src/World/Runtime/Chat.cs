// UNNAMED World - chat: the channels, the message, who hears it, and the lines content gives it (WP16)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using UNNAMED.Domain;

namespace UNNAMED.World.Runtime;

/// <summary>
/// Where a message is said (WP16): <see cref="World"/> to the whole region, <see cref="Local"/> to whoever is near the speaker,
/// <see cref="Party"/> to the members of one group, <see cref="System"/> to the character alone, from no speaker.
/// </summary>
public enum ChatChannel
{
    World,
    Local,
    Party,
    System,
}

/// <summary>
/// Who said it. The game has only NPC and system speakers; <see cref="Player"/> is the slot a human speaker takes when the world is
/// served to more than one person (an Otherserver): their words travel as a line key and params like anyone's.
/// </summary>
public enum SpeakerKind
{
    System,
    Npc,
    Player,
}

/// <summary>Where a local message was said, in region millimetres.</summary>
public readonly record struct ChatOrigin(long XMm, long ZMm);

/// <summary>
/// One chat message (WP16): who said it, on which channel, which line (its key in <see cref="ChatSetup"/>) with which params, and the
/// world tick it was said at. Chat is presentation of simulation facts: a message never creates a world fact, nothing reads one back
/// into the simulation, and none is saved. Raised as an event for every message the character hears.
/// </summary>
/// <param name="Speaker">The speaker's instance; null for the system.</param>
/// <param name="Params">The values of the line's <c>{slot}</c>s, already words (names, a clock time) - never raw IDs.</param>
public sealed record ChatMessage(EntityId? Speaker, ChatChannel Channel, string LineKey, ImmutableSortedDictionary<string, string> Params, long Tick)
{
    public SpeakerKind SpeakerKind { get; init; } = SpeakerKind.Npc;

    /// <summary>The region a World message is said to.</summary>
    public string? RegionId { get; init; }

    /// <summary>Where a Local message was said; null on other channels.</summary>
    public ChatOrigin? Origin { get; init; }

    /// <summary>The group a Party message is said to (<see cref="AllyGroupView.GroupKey"/>, or the character's own party); null otherwise.</summary>
    public string? GroupKey { get; init; }

    /// <summary>The message is about play - an offer to join, a bounty, the system - rather than ambient talk: what "Play-relevant only" keeps.</summary>
    public bool PlayRelevant { get; init; }
}

/// <summary>Who could hear a message: their instance, region, position and groups. One per listener when the world is served to many.</summary>
public sealed record ChatListener(EntityId Id, string RegionId, long XMm, long ZMm, ImmutableHashSet<string> Groups);

public static class ChatAudience
{
    /// <summary>Whether a listener hears a message: the region for World, the speaker's reach for Local, membership for Party, the character for System.</summary>
    public static bool Hears(ChatMessage message, ChatListener listener, long localRadiusMm) => message.Channel switch
    {
        ChatChannel.World => message.RegionId is null || message.RegionId == listener.RegionId,
        ChatChannel.Local => message.Origin is { } at && Within(at.XMm - listener.XMm, at.ZMm - listener.ZMm, localRadiusMm),
        ChatChannel.Party => message.GroupKey is { } group && listener.Groups.Contains(group),
        ChatChannel.System => true,
        _ => false,
    };

    private static bool Within(long dx, long dz, long radius) => dx * dx + dz * dz <= radius * radius;
}

/// <summary>The chat setting (WP16; Settings, Interface): every message, only the play-relevant ones, or none. On by default.</summary>
public enum ChatMode
{
    On,
    PlayRelevantOnly,
    Off,
}

public static class ChatFilter
{
    public static bool Shows(ChatMode mode, ChatMessage message) => mode switch
    {
        ChatMode.On => true,
        ChatMode.PlayRelevantOnly => message.PlayRelevant,
        _ => false,
    };
}

/// <summary>
/// A line of chat from content (<c>content/chatter</c>): its key and text, and what it may be said under. An ambient corpus line names
/// tags - an empty tag list matches everything; a template names its <see cref="Use"/> and is said only for that fact.
/// </summary>
public sealed record ChatLine(string Key, string Text, ChatChannel Channel)
{
    public ImmutableArray<string> Speakers { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> Places { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> Times { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> Weather { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> Events { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>The template use (<c>lfg.hunt</c>, <c>news.bounty_claimed</c>); null on an ambient corpus line.</summary>
    public string? Use { get; init; }

    public bool AiGenerated { get; init; }

    /// <summary>The provenance batch an AI-generated line came from.</summary>
    public string? Batch { get; init; }

    public bool IsTemplate => Use is not null;

    /// <summary>The <c>{slot}</c>s the text uses, in order of first use.</summary>
    public ImmutableArray<string> Slots => ChatText.Slots(Text);
}

/// <summary>A band of the day (the chatter schema's <c>time_bands</c>): the game hours from <see cref="From"/> up to <see cref="To"/>, wrapping midnight when To is earlier.</summary>
public sealed record TimeBand(string Id, int From, int To)
{
    public bool Contains(int hour) => From <= To ? hour >= From && hour < To : hour >= From || hour < To;
}

/// <summary>
/// How chat is tuned (<c>config.chat</c>): how far a local line carries, how often a speaker talks, how much the region's World channel
/// takes an hour, how long a line is not said again, how long an event stays news, and how close the character must be to a meeting
/// place when a group sets out.
/// </summary>
public sealed record ChatRules(
    long LocalRadiusMm,
    int LocalChancePercent,
    int WorldChancePercent,
    int WorldLinesPerHour,
    int SpeakerRepeatHours,
    int RegionRepeatHours,
    int RecentEventHours,
    int EventWeight,
    int HistoryMessages,
    long JoinRadiusMm,
    int OfferLookbackHours)
{
    public static readonly ChatRules Default = new(20_000, 25, 4, 2, 6, 24, 6, 3, 200, 15_000, 24);

    public ChatRules Validated()
    {
        if (LocalRadiusMm <= 0 || JoinRadiusMm <= 0)
            throw new FormatException("chat radii are more than nothing");
        if (LocalChancePercent is < 0 or > 100 || WorldChancePercent is < 0 or > 100)
            throw new FormatException("a chance to speak is a percent, 0 to 100");
        if (WorldLinesPerHour < 0 || SpeakerRepeatHours < 0 || RegionRepeatHours < 0 || RecentEventHours < 0 || OfferLookbackHours < 0)
            throw new FormatException("chat's counts and windows are never negative");
        if (EventWeight < 1 || HistoryMessages < 1)
            throw new FormatException("chat's event weight and history are at least 1");
        return this;
    }
}

/// <summary>Chat from content (WP16): the ambient corpus, the hand-written templates, the places and time bands lines are tagged with, and the tuning.</summary>
public sealed record ChatSetup(ImmutableArray<ChatLine> Corpus, ImmutableArray<ChatLine> Templates, ChatRules Rules)
{
    /// <summary>The places a speaker can stand in, by tag, with their display names (<c>ashen_hollow</c>: Ashen Hollow).</summary>
    public ImmutableSortedDictionary<string, string> Places { get; init; } = ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);

    public ImmutableArray<TimeBand> TimeBands { get; init; } = ImmutableArray<TimeBand>.Empty;

    /// <summary>Display names of definitions a line's params may name (creatures, places), by definition ID.</summary>
    public ImmutableSortedDictionary<string, string> Names { get; init; } = ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);

    public static ChatSetup Empty { get; } = new(ImmutableArray<ChatLine>.Empty, ImmutableArray<ChatLine>.Empty, ChatRules.Default);

    /// <summary>The line a message's <see cref="ChatMessage.LineKey"/> names; null when content no longer has it.</summary>
    public ChatLine? Line(string key) => Corpus.Concat(Templates).FirstOrDefault(l => l.Key == key);

    /// <summary>The templates of one use, in key order; empty when content has none.</summary>
    public ImmutableArray<ChatLine> TemplatesFor(string use) =>
        Templates.Where(t => t.Use == use).OrderBy(t => t.Key, StringComparer.Ordinal).ToImmutableArray();

    public string? TimeBandOf(int hourOfDay) => TimeBands.FirstOrDefault(b => b.Contains(hourOfDay))?.Id;
}

/// <summary>A line's words: its <c>{slot}</c>s, and the text with them filled.</summary>
public static partial class ChatText
{
    [GeneratedRegex(@"\{([a-z_]+)\}")]
    private static partial Regex SlotPattern();

    public static ImmutableArray<string> Slots(string text) =>
        SlotPattern().Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToImmutableArray();

    /// <summary>The text with each slot replaced by its param; a slot with no param is left empty, never shown as a brace.</summary>
    public static string Render(string text, IReadOnlyDictionary<string, string> values) =>
        SlotPattern().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : string.Empty);

    /// <summary>A message in words, from the setup's lines; null when content no longer has the line.</summary>
    public static string? Render(ChatSetup setup, ChatMessage message) =>
        setup.Line(message.LineKey) is { } line ? Render(line.Text, message.Params) : null;
}

/// <summary>
/// Someone who can say an ambient line: their instance, name, speaker type and place (chatter schema tags) and where they stand. A
/// named NPC is never handed a corpus line (<see cref="Named"/>), whoever supplies them.
/// </summary>
public sealed record ChatterSpeaker(EntityId Id, string Name, string SpeakerType, string Place, long XMm, long ZMm)
{
    public bool Named { get; init; }
}

/// <summary>
/// Who is about to talk at a tick: the NPC life simulation's residents and travellers (WP12), off-screen ones included. A pure read:
/// the same world at the same tick gives the same speakers. The NPC life simulation's role residents are they (WP12b); a fixed set
/// (<see cref="ChatterSpeakers.Of"/>) stands in for them in tests.
/// </summary>
public interface IChatterSpeakers
{
    IEnumerable<ChatterSpeaker> At(long tick);
}

public static class ChatterSpeakers
{
    /// <summary>The same speakers at every tick: for tests and for a pack whose talkers stand still.</summary>
    public static IChatterSpeakers Of(IEnumerable<ChatterSpeaker> speakers) => new Fixed(speakers.ToImmutableArray());

    private sealed class Fixed : IChatterSpeakers
    {
        private readonly ImmutableArray<ChatterSpeaker> _speakers;

        public Fixed(ImmutableArray<ChatterSpeaker> speakers) => _speakers = speakers;

        public IEnumerable<ChatterSpeaker> At(long tick) => _speakers;
    }
}

/// <summary>What the character typed into chat, as a command: <c>/join</c> answers an offer to set out together (WP16).</summary>
public static class ChatCommands
{
    /// <summary>
    /// The command a line of typed chat stands for, or why it stands for none. <c>/join</c> takes the newest open offer the character
    /// has not joined; <c>/join &lt;name&gt;</c> the one whose speaker's name starts so. Anything else is not a command chat knows.
    /// </summary>
    public static (GameCommand? Command, string? Problem) Parse(string input, EntityId actor, IEnumerable<LfgOfferView> offers)
    {
        string text = (input ?? string.Empty).Trim();
        if (!text.StartsWith('/'))
            return (null, "chat has no one to say that to yet");
        string[] words = text[1..].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0 || !string.Equals(words[0], "join", StringComparison.OrdinalIgnoreCase))
            return (null, $"/{(words.Length == 0 ? string.Empty : words[0])} is not a chat command");
        var open = offers.Where(o => !o.Joined).OrderByDescending(o => o.PostedTick).ThenBy(o => o.Npc).ToList();
        var offer = words.Length == 1
            ? open.FirstOrDefault()
            : open.FirstOrDefault(o => o.NpcName.StartsWith(words[1], StringComparison.OrdinalIgnoreCase));
        return offer is null
            ? (null, words.Length == 1 ? "no one is looking for company" : $"{words[1]} is not looking for company")
            : (new JoinGroupCommand(actor, offer.Npc), null);
    }
}
