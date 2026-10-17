using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP16 in the running world: an NPC setting out asks along on World, the character answers with Join or <c>/join</c>, and they leave
/// together as temporary allies when the character is at the meeting place; bounty claims are news; ambient chatter is drawn from the
/// corpus by its tags, deterministically, rate-limited and never repeated too soon, and never by a named NPC. Chat writes no world fact.
/// </summary>
public class ChatTests
{
    private const long Hour = 2_400;   // the game's: a 57 600-tick day of 24 hours
    private const long QuickHour = 100;
    private static readonly (double X, double Z) Spot = (120, 60);

    private static SettingOut Offer(EntityId npc, long tick, long depart, (long X, long Z) meet, string purpose = LfgPurposes.Hunt, string name = "Brannoc") =>
        new(npc, name, purpose, "the boar in the birch", "the North Ash Stand", "location.cairnwold.lodge", "the Long Lodge", meet.X, meet.Z, depart, tick);

    private static (long X, long Z) Here(Arena arena) => (arena.Simulation.Player.Body.XMm, arena.Simulation.Player.Body.ZMm);

    private static Arena Open(GameSession session, Func<SimulationSetup, SimulationSetup> change, long startTick = 0, ulong seed = 42) =>
        Arena.OpenWith(session, change(session.Setup), Spot, 0, Array.Empty<(double, double)>(), seed: seed, startTick: startTick);

    private static (long X, long Z) SpotMm(GameSession session) =>
        ((long)((Spot.X + Harness.HollowOf(session.Setup).X) * 1000), (long)((Spot.Z + Harness.HollowOf(session.Setup).Z) * 1000));

    [Fact]
    public void AnOffer_IsPostedOnWorld_JoinedWithSlashJoin_AndTheGroupSetsOutFromTheMeetingPlace()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var npc = EntityId.NewId(EntityKind.Npc);
        var arena = Open(session, s => s with { SettingOuts = SettingOuts.Of(new[] { Offer(npc, 5, 5 + Hour, SpotMm(session)) }) });
        var formed = arena.Record<AllyGroupFormed>();
        var heard = arena.Record<ChatMessage>();
        long digest = 0;

        arena.Tick(6);
        var post = Assert.Single(heard);
        Assert.Equal((npc, ChatChannel.World, SpeakerKind.Npc, true), (post.Speaker, post.Channel, post.SpeakerKind, post.PlayRelevant));
        Assert.StartsWith("lfg.hunt.", post.LineKey);
        Assert.Equal("region.starting_frontier", post.RegionId);
        string words = arena.Simulation.ChatText(post)!;
        Assert.DoesNotContain("{", words);
        Assert.Contains("Long Lodge", words);
        Assert.Equal(("Brannoc", "08:30"), (post.Params["speaker"], post.Params["depart_time"]));   // an hour after the world starts, at 07:30 (config.time)
        var offer = Assert.Single(arena.Simulation.GroupOffers);
        Assert.Equal((npc, false, 5 + Hour), (offer.Npc, offer.Joined, offer.DepartTick));
        digest = arena.Simulation.StateDigest().GetHashCode();

        var (command, problem) = ChatCommands.Parse("/join", arena.Player, arena.Simulation.GroupOffers);
        Assert.Null(problem);
        Assert.Null(arena.Submit(command!));
        Assert.Contains("already expects you", arena.Submit(new JoinGroupCommand(arena.Player, npc)));
        Assert.True(Assert.Single(arena.Simulation.GroupOffers).Joined);
        Assert.Equal(new[] { ChatChannel.Party, ChatChannel.System }, heard.Skip(1).Select(m => m.Channel));
        Assert.Equal(("lfg.joined", "sys.lfg_joined"), (heard[1].LineKey[..10], heard[2].LineKey[..14]));
        // Chat writes nothing that is saved: what was said leaves the persistent state as it was.
        Assert.Equal(digest, arena.Simulation.StateDigest().GetHashCode());

        arena.Tick((int)Hour);
        var group = Assert.Single(arena.Simulation.AllyGroups);
        Assert.Equal((npc, PartyMembership.TemporaryAlly), (group.Leader, group.Membership));
        Assert.Equal(new[] { npc, arena.Player }, group.Members);
        Assert.Equal(group, Assert.Single(formed).Group);
        Assert.Empty(arena.Simulation.GroupOffers);
        var setOut = heard.Last();
        Assert.Equal((ChatChannel.Party, group.GroupKey), (setOut.Channel, setOut.GroupKey));
        Assert.StartsWith("party.set_out.", setOut.LineKey);
        Assert.Equal(heard, arena.Simulation.Chat);
    }

    [Fact]
    public void AnAnswerWithNoOneAtTheMeetingPlace_LeavesTheNpcToGoAlone_AndNoOneUnasked_IsWaitedFor()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (x, z) = SpotMm(session);
        var far = EntityId.NewId(EntityKind.Npc);
        var unasked = EntityId.NewId(EntityKind.Npc);
        var arena = Open(session, s => s with
        {
            SettingOuts = SettingOuts.Of(new[] { Offer(far, 3, 3 + Hour, (x + 200_000, z)), Offer(unasked, 4, 4 + Hour, (x, z), LfgPurposes.Bounty, "Ysolde") }),
        });
        var missed = arena.Record<LfgMissed>();
        arena.Tick(5);
        Assert.Equal(2, arena.Simulation.GroupOffers.Length);
        Assert.Contains("not looking for company", ChatCommands.Parse("/join Hal", arena.Player, arena.Simulation.GroupOffers).Problem);
        var (join, _) = ChatCommands.Parse("/join bran", arena.Player, arena.Simulation.GroupOffers);
        Assert.Equal(far, ((JoinGroupCommand)join!).Npc);
        Assert.Null(arena.Submit(join));

        arena.Tick((int)Hour);
        Assert.Equal(far, Assert.Single(missed).Npc);
        Assert.Empty(arena.Simulation.AllyGroups);
        Assert.Empty(arena.Simulation.GroupOffers);
        Assert.StartsWith("sys.lfg_missed.", arena.Simulation.Chat.Last().LineKey);
        // The NPC said so where they waited, far off: not heard.
        Assert.DoesNotContain(arena.Simulation.Chat, m => m.LineKey.StartsWith("lfg.left_alone.", StringComparison.Ordinal));
        Assert.Contains("no one is looking for company", ChatCommands.Parse("/join", arena.Player, arena.Simulation.GroupOffers).Problem);
        Assert.Contains("not a chat command", ChatCommands.Parse("/dance", arena.Player, arena.Simulation.GroupOffers).Problem);
    }

    [Fact]
    public void T22_Offers_CaughtUpInAnyPieces_LeaveTheSameOffersAndGroups()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var npcs = Enumerable.Range(0, 6).Select(_ => EntityId.NewId(EntityKind.Npc)).ToList();
        var schedule = npcs.Select((n, i) => Offer(n, 10 + i * 3 * Hour, 10 + i * 3 * Hour + (i % 3 + 1) * 2 * Hour, SpotMm(session), LfgPurposes.All[i % 3])).ToList();
        Func<SimulationSetup, SimulationSetup> change = s => s with { SettingOuts = SettingOuts.Of(schedule) };
        string Seen(Arena a) => string.Join(";", a.Simulation.GroupOffers.Select(o => $"{o.Npc}:{o.DepartTick}:{o.Joined}"))
                                + "|" + string.Join(";", a.Simulation.AllyGroups.Select(g => g.GroupKey)) + "|" + a.Simulation.WorldTick;

        var once = Open(session, change);
        var pieces = Open(session, change);
        once.Tick(11);
        pieces.Tick(11);
        Assert.Null(once.Submit(new JoinGroupCommand(once.Player, npcs[0])));
        Assert.Null(pieces.Submit(new JoinGroupCommand(pieces.Player, npcs[0])));
        Assert.Equal(Seen(once), Seen(pieces));

        long absence = 13 * Hour + 7;
        once.Simulation.Advance(absence);
        foreach (long piece in new[] { 1, Hour - 1, 4 * Hour + 3, absence - 5 * Hour - 3 })
            pieces.Simulation.Advance(piece);
        Assert.Equal(Seen(once), Seen(pieces));
        // Those still waiting when the character came back are waiting; the one they answered left without them.
        long now = once.Simulation.WorldTick;
        Assert.Equal(schedule.Where(s => s.Tick <= now && s.DepartTick > now).Select(s => s.Npc).OrderBy(n => n), once.Simulation.GroupOffers.Select(o => o.Npc).OrderBy(n => n));
        Assert.Empty(once.Simulation.AllyGroups);
    }

    [Fact]
    public void AtLoad_OffersStillOpen_AreReadAgain_Quietly()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var npc = EntityId.NewId(EntityKind.Npc);
        var arena = Open(session, s => s with { SettingOuts = SettingOuts.Of(new[] { Offer(npc, 100, 100 + 2 * Hour, SpotMm(session)) }) }, startTick: 100 + Hour);
        var heard = arena.Record<ChatMessage>();
        Assert.Equal(npc, Assert.Single(arena.Simulation.GroupOffers).Npc);
        arena.Tick(2);
        Assert.DoesNotContain(heard, m => m.LineKey.StartsWith("lfg.", StringComparison.Ordinal));
    }

    [Fact]
    public void ABountyClaimedBySomeoneElse_IsNewsOnWorld_AndSaidOnSystem()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const string wolves = "quest.bounty.road_wolves";
        var quest = session.Setup.Quests.Quests[wolves] with { Competitive = true };
        var arena = Open(session, s => s with { Quests = new QuestSetup(s.Quests.Quests.SetItem(wolves, quest)) });
        var kera = arena.Simulation.Npcs.Single(n => n.Id == "npc.ashen_hollow.kera_voss").InstanceId;
        var heard = arena.Record<ChatMessage>();

        Assert.Null(arena.Submit(new ClaimBountyCommand(kera, wolves)));
        Assert.Equal(new[] { ChatChannel.World, ChatChannel.System }, heard.Select(m => m.Channel));
        Assert.StartsWith("news.bounty_claimed.", heard[0].LineKey);
        Assert.StartsWith("sys.bounty_claimed.", heard[1].LineKey);
        Assert.All(heard, m => Assert.Equal(("Kera Voss", quest.Title), (m.Params["claimant"], m.Params["bounty"])));
        Assert.All(heard, m => Assert.True(m.PlayRelevant));
        Assert.Contains("Kera Voss", arena.Simulation.ChatText(heard[0]));
    }

    // ── ambient chatter ────────────────────────────────────────────────────

    private static ChatLine Line(string key, string text, ChatChannel channel = ChatChannel.Local, string[]? speakers = null, string[]? times = null,
        string[]? events = null) => new(key, text, channel)
    {
        Speakers = (speakers ?? Array.Empty<string>()).ToImmutableArray(),
        Times = (times ?? Array.Empty<string>()).ToImmutableArray(),
        Events = (events ?? Array.Empty<string>()).ToImmutableArray(),
        AiGenerated = true,
        Batch = "t0001",
    };

    private static ImmutableArray<ChatLine> Corpus(int count) => Enumerable.Range(1, count)
        .Select(i => Line($"chat.t0001.{i:0000}", $"Line {i} about {{place}} in the {{time_band}}.", i % 4 == 0 ? ChatChannel.World : ChatChannel.Local,
            speakers: i % 5 == 0 ? new[] { "hunter" } : null, times: i % 7 == 0 ? new[] { "night" } : null))
        .ToImmutableArray();

    private static Arena Chattering(GameSession session, ImmutableArray<ChatLine> corpus, IEnumerable<ChatterSpeaker> speakers, ulong seed = 42,
        Func<ChatRules, ChatRules>? rules = null) =>
        Open(session, s => s with
        {
            Clock = new WorldClock(24 * QuickHour, 0),
            Chat = s.Chat with { Corpus = corpus, Rules = (rules ?? (r => r))(s.Chat.Rules with { LocalChancePercent = 60, WorldChancePercent = 30, HistoryMessages = 100_000 }) },
            ChatterSpeakers = ChatterSpeakers.Of(speakers),
        }, seed: seed);

    private static List<ChatterSpeaker> Speakers(GameSession session, int count)
    {
        var (x, z) = SpotMm(session);
        string[] types = { "farmer", "hunter", "guard", "carter", "villager" };
        return Enumerable.Range(0, count).Select(i => new ChatterSpeaker(EntityId.NewId(EntityKind.Npc), $"Speaker {i}", types[i % types.Length],
            i % 2 == 0 ? "ashen_hollow" : "cairnwold", x + i * 100, z)).ToList();
    }

    [Fact]
    public void Chatter_FollowsItsTags_ItsRateLimits_AndNeverRepeatsTooSoon()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var speakers = Speakers(session, 10);
        var corpus = Corpus(60);
        var arena = Chattering(session, corpus, speakers);
        var rules = arena.Simulation.Setup.Chat.Rules;
        arena.Tick((int)(72 * QuickHour));

        var said = arena.Simulation.Chat.Where(m => m.LineKey.StartsWith("chat.", StringComparison.Ordinal))
            .Select(m => (Message: m, Line: corpus.Single(l => l.Key == m.LineKey), Hour: m.Tick / QuickHour,
                Speaker: speakers.Single(s => s.Id == m.Speaker))).ToList();
        Assert.True(said.Count > 50, $"only {said.Count} lines in three days");
        foreach (var s in said)
        {
            Assert.Equal(s.Line.Channel, s.Message.Channel);
            Assert.True(s.Line.Speakers.IsEmpty || s.Line.Speakers.Contains(s.Speaker.SpeakerType));
            string band = arena.Simulation.Setup.Chat.TimeBandOf((int)(s.Hour % 24))!;
            Assert.True(s.Line.Times.IsEmpty || s.Line.Times.Contains(band), $"{s.Line.Key} said at {band}");
            Assert.Equal(arena.Simulation.Setup.Chat.Places[s.Speaker.Place], s.Message.Params["place"]);
            Assert.DoesNotContain("{", arena.Simulation.ChatText(s.Message));
            Assert.False(s.Message.PlayRelevant);
        }
        Assert.All(said.GroupBy(s => (s.Speaker.Id, s.Hour)), g => Assert.Single(g));
        Assert.All(said.Where(s => s.Message.Channel == ChatChannel.World).GroupBy(s => s.Hour), g => Assert.InRange(g.Count(), 1, rules.WorldLinesPerHour));
        foreach (var byKey in said.GroupBy(s => s.Line.Key))
        {
            var hours = byKey.Select(s => s.Hour).OrderBy(h => h).ToList();
            Assert.All(hours.Zip(hours.Skip(1)), p => Assert.True(p.Second - p.First >= rules.RegionRepeatHours, $"{byKey.Key} again after {p.Second - p.First} hours"));
        }
    }

    [Fact]
    public void Chatter_IsTheSameInTheSameWorld_AndAnotherWorldTalksOtherwise()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var speakers = Speakers(session, 6);
        string Talk(Arena a) => string.Join(";", a.Simulation.Chat.Select(m => $"{m.Tick}:{m.Speaker}:{m.LineKey}"));

        var a = Chattering(session, Corpus(40), speakers);
        var b = Chattering(session, Corpus(40), speakers);
        var other = Chattering(session, Corpus(40), speakers, seed: 43);
        foreach (var arena in new[] { a, b, other })
            arena.Tick((int)(30 * QuickHour));

        Assert.NotEmpty(a.Simulation.Chat);
        Assert.Equal(Talk(a), Talk(b));
        Assert.NotEqual(Talk(a), Talk(other));
    }

    [Fact]
    public void ANamedNpc_NeverSaysACorpusLine_HoweverTheyAreHandedIn()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var probe = Open(session, s => s);
        var renn = probe.Simulation.Npcs.Single(n => n.Id == "npc.ashen_hollow.renn_vale").InstanceId;
        var (x, z) = SpotMm(session);
        var speakers = new[]
        {
            new ChatterSpeaker(renn, "Renn Vale", "villager", "ashen_hollow", x, z),
            new ChatterSpeaker(EntityId.NewId(EntityKind.Npc), "Kept apart", "villager", "ashen_hollow", x, z) { Named = true },
        };
        var arena = Chattering(session, Corpus(40), speakers, rules: r => r with { LocalChancePercent = 100, WorldChancePercent = 100 });
        arena.Tick((int)(30 * QuickHour));
        Assert.Empty(arena.Simulation.Chat);
    }

    [Fact]
    public void ALineAboutNews_IsSaidOnlyAfterTheNews_AndALongAbsenceIsNews()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var corpus = ImmutableArray.Create(Line("chat.t0001.0001", "Back at last, are you? Things moved on without you.", events: new[] { "long_absence" }));
        var arena = Chattering(session, corpus, Speakers(session, 3), rules: r => r with { LocalChancePercent = 100, RecentEventHours = 4 });
        arena.Tick((int)(5 * QuickHour));
        Assert.Empty(arena.Simulation.Chat);

        arena.Simulation.Advance(26 * QuickHour);
        arena.Tick((int)(2 * QuickHour));
        var said = Assert.Single(arena.Simulation.Chat);   // one speaker says it; nobody in the region says it again for a day
        Assert.Equal("chat.t0001.0001", said.LineKey);
        arena.Tick((int)(6 * QuickHour));
        Assert.Single(arena.Simulation.Chat);
    }

    [Fact]
    public void TheChatSetting_KeepsAll_OnlyPlay_OrNothing()
    {
        var talk = new ChatMessage(null, ChatChannel.Local, "chat.t0001.0001", ImmutableSortedDictionary<string, string>.Empty, 1);
        var offer = talk with { Channel = ChatChannel.World, LineKey = "lfg.hunt.01", PlayRelevant = true };
        Assert.Equal((true, true), (ChatFilter.Shows(ChatMode.On, talk), ChatFilter.Shows(ChatMode.On, offer)));
        Assert.Equal((false, true), (ChatFilter.Shows(ChatMode.PlayRelevantOnly, talk), ChatFilter.Shows(ChatMode.PlayRelevantOnly, offer)));
        Assert.Equal((false, false), (ChatFilter.Shows(ChatMode.Off, talk), ChatFilter.Shows(ChatMode.Off, offer)));

        var listener = new ChatListener(EntityId.NewId(EntityKind.Character), "region.a", 0, 0, ImmutableHashSet.Create("g1"));
        Assert.True(ChatAudience.Hears(talk with { Origin = new ChatOrigin(3_000, 4_000) }, listener, 5_000));
        Assert.False(ChatAudience.Hears(talk with { Origin = new ChatOrigin(3_000, 4_001) }, listener, 5_000));
        Assert.True(ChatAudience.Hears(offer with { RegionId = "region.a" }, listener, 5_000));
        Assert.False(ChatAudience.Hears(offer with { RegionId = "region.b" }, listener, 5_000));
        Assert.True(ChatAudience.Hears(talk with { Channel = ChatChannel.Party, GroupKey = "g1" }, listener, 5_000));
        Assert.False(ChatAudience.Hears(talk with { Channel = ChatChannel.Party, GroupKey = "g2" }, listener, 5_000));
    }
}
