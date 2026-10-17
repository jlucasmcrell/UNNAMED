using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Presentation.Settings;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Tests;

/// <summary>The chat window (WP16): tabs, the owner's chatter setting (all, play-relevant only, off), the group board and the AI disclosure.</summary>
public class ChatModelTests
{
    // A day of 57,600 ticks: 40 ticks a game minute.
    private static readonly WorldClock Clock = WorldClock.Default;
    private static readonly EntityId Bram = EntityId.NewId(EntityKind.Npc);
    private static readonly EntityId Kester = EntityId.NewId(EntityKind.Npc);

    private static readonly ChatLine Ambient = new("chat.b0001.0001", "Cold today.", ChatChannel.Local) { AiGenerated = true, Batch = "b0001" };
    private static readonly ChatLine Offer = new("lfg.hunt.01", "Hunting {purpose} near {destination}.", ChatChannel.World) { Use = "lfg.hunt" };
    private static readonly ChatLine Joined = new("sys.lfg_joined.01", "You said you would go with {speaker}.", ChatChannel.System) { Use = "sys.lfg_joined" };

    private static ChatMessage Said(EntityId? speaker, ChatChannel channel, ChatLine line, long tick, bool relevant, params (string, string)[] facts) =>
        new(speaker, channel, line.Key, facts.ToImmutableSortedDictionary(f => f.Item1, f => f.Item2, StringComparer.Ordinal), tick)
        {
            SpeakerKind = speaker is null ? SpeakerKind.System : SpeakerKind.Npc,
            PlayRelevant = relevant,
        };

    private static readonly ChatMessage[] Heard =
    {
        Said(Kester, ChatChannel.Local, Ambient, 2_400, false),
        Said(Bram, ChatChannel.World, Offer, 4_800, true, ("speaker", "Bram Hallow"), ("purpose", "grey wolves"), ("destination", "the Old Den")),
        Said(null, ChatChannel.System, Joined, 5_000, true, ("speaker", "Bram Hallow")),
    };

    private static ImmutableArray<ChatRow> Rows(ChatTab tab, ChatSetting setting, int most = 50) =>
        ChatModel.Rows(Heard, tab, setting,
            m => ChatText.Render(new[] { Ambient, Offer, Joined }.First(l => l.Key == m.LineKey).Text, m.Params),
            key => new[] { Ambient, Offer, Joined }.FirstOrDefault(l => l.Key == key),
            id => id == Kester ? "Kester Vale-of-Stones" : null, Clock, most);

    [Fact]
    public void AllShowsEveryChannel_InOrder_WithTheTimeTheSpeakerAndTheWords()
    {
        var rows = Rows(ChatTab.All, ChatSetting.On);
        Assert.Equal(new[] { "Local", "World", "System" }, rows.Select(r => r.ChannelWord));
        Assert.Equal("01:00", rows[0].Time);   // 2,400 ticks: an hour into the day
        Assert.Equal("Kester Vale-of-Stones", rows[0].Speaker);
        Assert.Equal("Bram Hallow", rows[1].Speaker);   // no name for the instance now: the name the line was said with
        Assert.Equal("Hunting grey wolves near the Old Den.", rows[1].Text);
        Assert.Null(rows[2].Speaker);   // the system has no speaker
    }

    [Fact]
    public void EachChannelTab_KeepsItsOwnChannel_AndTheGroupBoardListsNoMessages()
    {
        Assert.Equal(ChatChannel.World, Assert.Single(Rows(ChatTab.World, ChatSetting.On)).Channel);
        Assert.Equal(ChatChannel.Local, Assert.Single(Rows(ChatTab.Local, ChatSetting.On)).Channel);
        Assert.Equal(ChatChannel.System, Assert.Single(Rows(ChatTab.System, ChatSetting.On)).Channel);
        Assert.Empty(Rows(ChatTab.Party, ChatSetting.On));
        Assert.Empty(Rows(ChatTab.Groups, ChatSetting.On));
    }

    [Fact]
    public void TheOwnersSetting_ShowsAll_OnlyWhatIsAboutPlay_OrNothing()
    {
        Assert.Equal(3, Rows(ChatTab.All, ChatSetting.On).Length);
        Assert.Equal(new[] { ChatChannel.World, ChatChannel.System }, Rows(ChatTab.All, ChatSetting.PlayRelevantOnly).Select(r => r.Channel));
        Assert.Empty(Rows(ChatTab.All, ChatSetting.Off));
        Assert.Equal(ChatMode.PlayRelevantOnly, ChatModel.Mode(ChatSetting.PlayRelevantOnly));
        Assert.Equal(ChatMode.Off, ChatModel.Mode(ChatSetting.Off));
        Assert.Equal("Play-relevant only", ChatModel.ModeWords(ChatSetting.PlayRelevantOnly));
    }

    [Fact]
    public void AnAiLine_IsMarked_AndTheWindowDisclosesIt_OnlyWhileOneIsShown()
    {
        var all = Rows(ChatTab.All, ChatSetting.On);
        Assert.True(all[0].AiGenerated);
        Assert.False(all[1].AiGenerated);
        Assert.True(ChatModel.Discloses(all));
        Assert.False(ChatModel.Discloses(Rows(ChatTab.All, ChatSetting.PlayRelevantOnly)));   // offers and the system are hand-written
        Assert.Contains("generative AI", ChatModel.AiDisclosure);
    }

    [Fact]
    public void TheNewestRows_AreKept_WhenThereAreMoreThanTheWindowHolds() =>
        Assert.Equal(new[] { 4_800L, 5_000L }, Rows(ChatTab.All, ChatSetting.On, most: 2).Select(r => r.Tick));

    [Fact]
    public void AMessageWhoseLineIsGone_IsLeftOut_NeverShownAsAKey()
    {
        var gone = Said(Bram, ChatChannel.World, new ChatLine("lfg.gone.01", "x", ChatChannel.World), 10, true);
        var rows = ChatModel.Rows(new[] { gone }, ChatTab.All, ChatSetting.On, _ => null, _ => null, _ => null, Clock, 10);
        Assert.Empty(rows);
    }

    private static LfgOfferView OfferBy(EntityId npc, string name, long depart, bool joined) =>
        new(npc, name, "hunt", "grey wolves", "the Old Den", "the North Gate", 10_000, 0, depart, 4_800, joined);

    [Fact]
    public void TheGroupBoard_ListsOffersSoonestFirst_WithWhenTheyLeave_AndJoinRefusedOnceGone()
    {
        var posts = ChatModel.Posts(new[] { OfferBy(Kester, "Kester", 9_600, false), OfferBy(Bram, "Bram Hallow", 7_200, false) }, 4_800, Clock);
        Assert.Equal(new[] { "Bram Hallow", "Kester" }, posts.Select(p => p.Who));
        Assert.Equal("03:00", posts[0].Leaves);
        Assert.Equal("in about 1 h", posts[0].LeavesIn);   // 2,400 ticks
        Assert.Equal("grey wolves", posts[0].What);
        Assert.True(posts[0].CanJoin);

        var gone = Assert.Single(ChatModel.Posts(new[] { OfferBy(Bram, "Bram Hallow", 4_000, false) }, 4_800, Clock));
        Assert.False(gone.CanJoin);
        Assert.Equal("Bram Hallow has left", gone.Refusal);

        var answered = Assert.Single(ChatModel.Posts(new[] { OfferBy(Bram, "Bram Hallow", 7_200, true) }, 4_800, Clock));
        Assert.True(answered.Joined);
        Assert.False(answered.CanJoin);
    }

    [Fact]
    public void Ask_SaysWhatForWhereToMeetHowFarAndWhen()
    {
        string asked = ChatModel.Ask(OfferBy(Bram, "Bram Hallow", 7_200, false), 4_800, Clock, 10_000, 120_000);
        Assert.Equal("Bram Hallow: grey wolves, at the Old Den. Meet at the North Gate (about 120 m from you); they leave at 03:00, in about 1 h. " +
                     "No wage: a share of what you find together.", asked);
        Assert.Contains("you are there", ChatModel.Ask(OfferBy(Bram, "Bram Hallow", 7_200, true), 4_800, Clock, 10_000, 5_000));
        Assert.EndsWith("You said you would go.", ChatModel.Ask(OfferBy(Bram, "Bram Hallow", 7_200, true), 4_800, Clock, 10_000, 5_000));
    }

    [Fact]
    public void AnOffersRow_CarriesItsOffer_WhileItIsOpen_AndNoOtherRowDoes()
    {
        var rows = Rows(ChatTab.All, ChatSetting.On);
        var open = new LfgOfferView(Bram, "Bram Hallow", "hunt", "grey wolves", "the Old Den", "the North Gate", 0, 0, 7_200, 4_800, false);
        Assert.Same(open, ChatModel.Offer(rows[1], new[] { open }));
        Assert.Null(ChatModel.Offer(rows[0], new[] { open }));   // ambient talk
        Assert.Null(ChatModel.Offer(rows[2], new[] { open }));   // the system's line about it
        Assert.Null(ChatModel.Offer(rows[1], new[] { open with { PostedTick = 1 } }));   // a later offer by the same person is another post
        Assert.Null(ChatModel.Offer(rows[1], Array.Empty<LfgOfferView>()));   // gone: the row stays, without its actions
    }

    [Theory]
    [InlineData(0, "now")]
    [InlineData(40, "in a few minutes")]
    [InlineData(800, "in about 20 min")]
    [InlineData(3_400, "in about 1 h 25 min")]
    public void LeavingIn_IsSaidInGameTime(long ticks, string words) => Assert.Equal(words, ChatModel.LeavesIn(ticks, Clock));

    [Fact]
    public void APurposeWithNoName_IsSaidByItsKind() =>
        Assert.Equal("an errand", ChatModel.Purpose(new LfgOfferView(Bram, "Bram", "errand", "", "Rim", "Gate", 0, 0, 10, 0, false)));
}
