// UNNAMED Presentation - the chat window's rows, tabs and offers to set out together, from what the character heard (WP16)
// Godot-free: Presentation.Tests links this file and tests the mapping on its own (D-11)

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Presentation.Settings;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>The chat window's tabs: every channel, one channel each, and the offers to set out together (the group board).</summary>
public enum ChatTab { All, World, Local, Party, System, Groups }

/// <summary>
/// One message as the chat window lists it: when, where, who (their instance, for the actions on an offer, and their name), the words,
/// and whether it is about play or was written with AI.
/// </summary>
public sealed record ChatRow(long Tick, string Time, ChatChannel Channel, string ChannelWord, EntityId? SpeakerId, string? Speaker, string Text, bool PlayRelevant,
    bool AiGenerated, string LineKey);

/// <summary>
/// An open offer to set out together, as the group board lists it: who, what for and where, where to meet and when they leave (a clock
/// time and how long from now, in game time), whether the character has answered, and why Join is refused now, if it is.
/// </summary>
public sealed record GroupPost(EntityId Npc, string Who, string What, string Where, string Meet, string Leaves, string LeavesIn, bool Joined, string? Refusal)
{
    public bool CanJoin => !Joined && Refusal is null;
}

public static class ChatModel
{
    /// <summary>
    /// The disclosure the chat window carries (WP16; the chatter schema's <c>ai_generated</c>): which lines were written with generative AI.
    /// The ambient corpus is; offers, news and system lines are hand-written. Every AI line is also marked on its row.
    /// </summary>
    public const string AiDisclosure =
        "Some ambient talk on World and Local was written with the help of generative AI; those lines are marked AI. Offers to set out together, news and system lines are hand-written.";

    /// <summary>The chat setting as World's filter reads it (the members match in order; a mapping, never a cast).</summary>
    public static ChatMode Mode(ChatSetting setting) => setting switch
    {
        ChatSetting.PlayRelevantOnly => ChatMode.PlayRelevantOnly,
        ChatSetting.Off => ChatMode.Off,
        _ => ChatMode.On,
    };

    /// <summary>The setting in a few words, for the window's mode control and the HUD feed.</summary>
    public static string ModeWords(ChatSetting setting) => setting switch
    {
        ChatSetting.PlayRelevantOnly => "Play-relevant only",
        ChatSetting.Off => "Off",
        _ => "All",
    };

    public static string TabWord(ChatTab tab) => tab switch
    {
        ChatTab.All => "All",
        ChatTab.World => "World",
        ChatTab.Local => "Local",
        ChatTab.Party => "Party",
        ChatTab.System => "System",
        ChatTab.Groups => "Groups",
        _ => tab.ToString(),
    };

    public static string ChannelWord(ChatChannel channel) => channel switch
    {
        ChatChannel.World => "World",
        ChatChannel.Local => "Local",
        ChatChannel.Party => "Party",
        ChatChannel.System => "System",
        _ => channel.ToString(),
    };

    /// <summary>Whether a message belongs on a tab: All takes every channel, each channel tab its own, and the group board none (it lists offers).</summary>
    public static bool OnTab(ChatTab tab, ChatMessage message) => tab switch
    {
        ChatTab.All => true,
        ChatTab.World => message.Channel == ChatChannel.World,
        ChatTab.Local => message.Channel == ChatChannel.Local,
        ChatTab.Party => message.Channel == ChatChannel.Party,
        ChatTab.System => message.Channel == ChatChannel.System,
        _ => false,
    };

    /// <summary>
    /// The rows a tab shows under a setting, newest last, at most <paramref name="most"/>: the setting filters first (Off shows nothing,
    /// play-relevant only keeps offers, news and the system), then the tab. A message whose line content no longer has is left out rather
    /// than shown as a key. The speaker is their name now, or the name the line was said with; the system has none.
    /// </summary>
    public static ImmutableArray<ChatRow> Rows(IEnumerable<ChatMessage> heard, ChatTab tab, ChatSetting setting, Func<ChatMessage, string?> text,
        Func<string, ChatLine?> line, Func<EntityId, string?> name, WorldClock clock, int most)
    {
        var mode = Mode(setting);
        var rows = new List<ChatRow>();
        foreach (var message in heard)
        {
            if (!ChatFilter.Shows(mode, message) || !OnTab(tab, message) || text(message) is not { } words)
                continue;
            string? speaker = message.SpeakerKind == SpeakerKind.System || message.Speaker is null
                ? null
                : name(message.Speaker) ?? message.Params.GetValueOrDefault("speaker");
            rows.Add(new ChatRow(message.Tick, Clock(message.Tick, clock), message.Channel, ChannelWord(message.Channel), message.Speaker, speaker, words,
                message.PlayRelevant, line(message.LineKey)?.AiGenerated ?? false, message.LineKey));
        }
        return rows.OrderBy(r => r.Tick).Skip(Math.Max(0, rows.Count - most)).ToImmutableArray();
    }

    /// <summary>
    /// The open offer a row posted, if it is one still open: an NPC's offer to set out together is said on World as an <c>lfg.</c> line by
    /// the one setting out, at the tick the offer went up. Its row then carries Join and Ask.
    /// </summary>
    public static LfgOfferView? Offer(ChatRow row, IEnumerable<LfgOfferView> offers) =>
        row.Channel == ChatChannel.World && row.SpeakerId is { } speaker && row.LineKey.StartsWith("lfg.", StringComparison.Ordinal)
            ? offers.FirstOrDefault(o => o.Npc == speaker && o.PostedTick == row.Tick)
            : null;

    /// <summary>Whether any row shown was written with AI: the window then keeps its disclosure in view, above the rows.</summary>
    public static bool Discloses(IEnumerable<ChatRow> rows) => rows.Any(r => r.AiGenerated);

    /// <summary>
    /// The group board: every open offer, soonest first, with Join refused as the simulation refuses it - once they have left (an answered
    /// offer needs no second answer).
    /// </summary>
    public static ImmutableArray<GroupPost> Posts(IEnumerable<LfgOfferView> offers, long now, WorldClock clock)
    {
        var list = offers.OrderBy(o => o.DepartTick).ThenBy(o => o.Npc).ToList();
        return list.Select(o => new GroupPost(o.Npc, o.NpcName, Purpose(o), o.Destination, o.AnchorName, Clock(o.DepartTick, clock),
                LeavesIn(o.DepartTick - now, clock), o.Joined,
                o.Joined ? null : o.DepartTick <= now ? $"{o.NpcName} has left" : null))
            .ToImmutableArray();
    }

    /// <summary>
    /// Ask about an offer: the facts of it, as a line the window shows the character (never said into the world, and never saved) - what
    /// for, where, the meeting place and how far off it is, and when they leave.
    /// </summary>
    public static string Ask(LfgOfferView offer, long now, WorldClock clock, long fromXMm, long fromZMm)
    {
        double metres = Math.Sqrt(Math.Pow(offer.MeetXMm - fromXMm, 2) + Math.Pow(offer.MeetZMm - fromZMm, 2)) / 1000.0;
        string far = metres < 15 ? "you are there" : $"about {Math.Round(metres / 10) * 10:0} m from you";
        string when = offer.DepartTick <= now ? "they have left" : $"they leave at {Clock(offer.DepartTick, clock)}, {LeavesIn(offer.DepartTick - now, clock)}";
        return $"{offer.NpcName}: {Purpose(offer)}, at {offer.Destination}. Meet at {offer.AnchorName} ({far}); {when}." +
               (offer.Joined ? " You said you would go." : " No wage: a share of what you find together.");
    }

    /// <summary>What the trip is for, in words: the purpose's own name, or its kind when it has none.</summary>
    public static string Purpose(LfgOfferView offer) =>
        !string.IsNullOrWhiteSpace(offer.PurposeName) ? offer.PurposeName : PurposeKind(offer.Purpose);

    /// <summary>A purpose's kind in words (<see cref="LfgPurposes"/>): "a hunt", "a bounty", "an errand".</summary>
    public static string PurposeKind(string purpose) =>
        purpose switch { "hunt" => "a hunt", "bounty" => "a bounty", "errand" => "an errand", _ => Names.Words(purpose) };

    /// <summary>A world tick as a clock time: "14:05".</summary>
    public static string Clock(long tick, WorldClock clock)
    {
        int minute = clock.MinuteOfDay(tick);
        return $"{minute / 60:00}:{minute % 60:00}";
    }

    /// <summary>A span of ticks ahead in game time, to five minutes: "in about 1 h 10 min", "in a few minutes", "now".</summary>
    public static string LeavesIn(long ticks, WorldClock clock)
    {
        if (ticks <= 0)
            return "now";
        long minutes = (long)Math.Round(ticks * clock.HoursPerDay * 60.0 / clock.TicksPerDay / 5) * 5;
        if (minutes < 5)
            return "in a few minutes";
        return minutes < 60 ? $"in about {minutes} min"
            : minutes % 60 == 0 ? $"in about {minutes / 60} h"
            : $"in about {minutes / 60} h {minutes % 60} min";
    }
}
