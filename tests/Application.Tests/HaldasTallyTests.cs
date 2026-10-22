using UNNAMED.Domain;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Halda's Tally (DIRECTION_AND_CONTENT_PLAN.md C.2d) in region.starting_frontier: Renn knows the tally only while it is carried, and
/// the quest forks there - return it to Tam for her trust and Halda's Bow, or keep it and walk Halda's snares on the north slope to the den.
/// </summary>
public class HaldasTallyTests
{
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Tam = "npc.cairnwold.tam_crowden";
    private const string Quest = "quest.ashen_hollow.haldas_tally";
    private const string Token = "item.quest.halda_token";
    private const string Bow = "item.weapon.haldas_bow";
    private const string Kept = "world.hollow.halda_tally_kept";

    // Region metres: beside Renn in the longhouse, and before Tam at the granary.
    private static readonly (double X, double Z) AtRenn = (448, 726.2);
    private static readonly (double X, double Z) AtTam = (941, 679.8);

    // Out of the longhouse's east door and up the north slope, along Halda's snares either side of the den, and back to its mouth.
    private static readonly (double X, double Z)[] OutOfTheLonghouse = { (450.6, 728) };
    private static readonly (double X, double Z)[] ToTheWestPelts = { (455, 728), (470, 728), (485, 760), (477, 783.5) };
    private static readonly (double X, double Z)[] ToTheWestLarder = { (481.5, 791) };
    private static readonly (double X, double Z)[] ToTheEastPelts = { (488, 776), (520, 772), (530, 772), (541, 781), (537, 782.8) };
    private static readonly (double X, double Z)[] ToTheEastLarder = { (541, 781), (542.5, 784), (546.8, 790.8) };
    private static readonly (double X, double Z)[] ToTheDen = { (542.5, 784), (541, 781), (530, 772), (518, 772) };

    private static Arena At((double X, double Z) place, GameSession session, Func<PlayerRecord, PlayerRecord>? change = null, long startTick = 0) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), change, startTick: startTick,
            origin: Harness.Region);

    private static PlayerRecord Carrying(PlayerRecord r, params string[] items) =>
        new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(items.Select(i => Arena.Stack(i, 1))), r.Progression, r.FacingMdeg,
            r.Discoveries, r.Equipment, r.Currency, r.Effects, r.Relationships, r.Conversations, r.Quests, r.Companions);

    /// <summary>The same character, everything they carry and remember, stood somewhere else in a fresh world.</summary>
    private static Func<PlayerRecord, PlayerRecord> Moved(PlayerRecord c, (double X, double Z) place) => fresh =>
        new(c.Id, c.Name, (long)(place.X * 1000), fresh.YMm, (long)(place.Z * 1000), c.AppearanceSeed, c.Inventory, c.Progression, c.FacingMdeg,
            c.Discoveries, c.Equipment, c.Currency, c.Effects, c.Relationships, c.Conversations, c.Quests, c.Companions);

    private static string? Say(Arena arena, string reply) => arena.Submit(new ChooseCommand(arena.Player, reply));

    private static ConversationView Now(Arena arena) => arena.Simulation.Conversation ?? throw new InvalidOperationException("no conversation");

    private static string[] Replies(Arena arena) => Now(arena).Replies.Select(r => r.Id).ToArray();

    /// <summary>Open a conversation past a spent greeting, at its again-line.</summary>
    private static void TalkAgain(Arena arena, string npc)
    {
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, npc)));
        if (Now(arena).NodeId == "greet")
        {
            Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
            Assert.Null(arena.Submit(new TalkCommand(arena.Player, npc)));
        }
        Assert.Equal("again", Now(arena).NodeId);
    }

    private static void Walk(Arena arena, params (double X, double Z)[] route)
    {
        foreach (var (x, z) in route)
            Assert.True(arena.WalkTo(x, z), $"never reached ({x}, {z}): stopped at {arena.Simulation.Player.Body}");
    }

    private static void Gather(Arena arena, string node)
    {
        var site = arena.Simulation.Nodes.Single(n => n.Name == node);
        string? refused = arena.Submit(new GatherCommand(arena.Player, site.Key));
        Assert.True(refused is null, $"{node} at ({site.XMm}, {site.ZMm}): {refused}, from {arena.Simulation.Player.Body}");
        arena.Tick();
    }

    private static int Carried(Arena arena, string defId) => arena.Simulation.Player.Inventory.Where(e => e.DefId == defId).Sum(e => e.Count);

    private static QuestView State(Arena arena) => arena.Simulation.Quests.Single(q => q.Id == Quest);

    /// <summary>An objective as the quest holds it - the journal leaves out the alternative a branch closed.</summary>
    private static ObjectiveStatus Objective(Arena arena, string id) =>
        arena.Simulation.CaptureRecord().Quests.Single(q => q.QuestId == Quest).Objectives.Single(o => o.Id == id).Status;

    private static int Regard(Arena arena, string npc, string dimension) =>
        arena.Simulation.CaptureRecord().Relationships.FirstOrDefault(r => r.NpcId == npc && r.Dimension == dimension)?.Value ?? 0;

    [Fact]
    public void Renn_KnowsTheTally_OnlyWhileItIsCarried_AndHisOtherRepliesAreUnchanged()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        var empty = At(AtRenn, session);
        TalkAgain(empty, Renn);
        Assert.Equal(new[] { "place", "work", "bed", "notices", "ask_old_late", "ask_quiet_folk", "ask_ashen_hollow", "ask_veth", "ask_wayhold", "ask_ashen_hollow_p2", "ask_old_late_p2", "ask_veth_p2", "ask_wayhold_p2", "leave", "story_begin_accounts", "story_note" }, Replies(empty));
        Assert.Empty(empty.Simulation.Quests.Where(q => q.Id == Quest));

        var bearer = At(AtRenn, session, r => Carrying(r, Token));
        TalkAgain(bearer, Renn);
        Assert.Equal(new[] { "place", "work", "bed", "tally", "notices", "ask_old_late", "ask_quiet_folk", "ask_ashen_hollow", "ask_veth", "ask_wayhold", "ask_ashen_hollow_p2", "ask_old_late_p2", "ask_veth_p2", "ask_wayhold_p2", "leave", "story_begin_accounts", "story_note" }, Replies(bearer));
        Assert.Null(Say(bearer, "tally"));
        bearer.Tick();

        Assert.Equal("tally", Now(bearer).NodeId);
        Assert.Equal(new[] { "keep", "tam", "back" }, Replies(bearer));
        Assert.Equal(QuestStatus.Active, State(bearer).Status);
        Assert.Equal((ObjectiveStatus.Satisfied, ObjectiveStatus.Active, ObjectiveStatus.Active),
            (Objective(bearer, "o_tally"), Objective(bearer, "o_return"), Objective(bearer, "o_keep")));
        // Thinking on it changes nothing: he offers it again, and asking again does not start the quest twice.
        Assert.Null(Say(bearer, "back"));
        Assert.Contains("tally", Replies(bearer));
        Assert.Null(Say(bearer, "tally"));
        bearer.Tick();
        Assert.Single(bearer.Simulation.Quests.Where(q => q.Id == Quest));
    }

    [Fact]
    public void ReturningIt_ToTam_RaisesHerTrust_GivesHaldasBow_AndCompletesTheQuest()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var atRenn = At(AtRenn, session, r => Carrying(r, Token));
        TalkAgain(atRenn, Renn);
        Assert.Null(Say(atRenn, "tally"));
        Assert.Null(Say(atRenn, "tam"));
        Assert.Equal("to_tam", Now(atRenn).NodeId);
        Assert.Null(Say(atRenn, "back"));
        Assert.Null(atRenn.Submit(new LeaveCommand(atRenn.Player)));
        atRenn.Tick();

        var arena = At(AtTam, session, Moved(atRenn.Simulation.CaptureRecord(), AtTam));
        Assert.Equal(QuestStatus.Active, State(arena).Status);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Tam)));
        Assert.Contains("halda_token", Replies(arena));
        Assert.Null(Say(arena, "halda_token"));
        Assert.Equal("halda", Now(arena).NodeId);
        Assert.Null(Say(arena, "back"));
        Assert.Equal("halda_bow", Now(arena).NodeId);
        Assert.Null(Say(arena, "bow"));
        Assert.Equal("bow_given", Now(arena).NodeId);
        arena.Tick(2);

        Assert.Equal((0, 1), (Carried(arena, Token), Carried(arena, Bow)));
        Assert.Equal(10, Regard(arena, Tam, "trust"));
        Assert.Equal(QuestStatus.Completed, State(arena).Status);
        Assert.Equal(ObjectiveStatus.Closed, Objective(arena, "o_keep"));
        // Given once: her again-line does not offer it a second time.
        Assert.Null(Say(arena, "back"));
        Assert.DoesNotContain("bow", Replies(arena));
        Assert.DoesNotContain("halda_token", Replies(arena));
    }

    [Fact]
    public void ABowNotTakenAtOnce_IsStillOfferedFromTamsAgainLine()
    {
        using var profile = new TempProfile();
        var arena = At(AtTam, Harness.Boot(profile), r => Carrying(r, Token));
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Tam)));
        Assert.Null(Say(arena, "halda_token"));
        Assert.Null(Say(arena, "back"));
        Assert.Null(Say(arena, "later"));
        Assert.Equal("again", Now(arena).NodeId);

        Assert.Contains("bow", Replies(arena));
        Assert.Null(Say(arena, "bow"));
        Assert.Equal(1, Carried(arena, Bow));
        // Returned without Renn's word first, the tally still goes home and the bow still comes out; the quest was never started.
        Assert.Empty(arena.Simulation.Quests.Where(q => q.Id == Quest));
    }

    [Fact]
    public void HaldasBow_IsAStepPastTheHuntingBow_OnTheSameArrows()
    {
        using var profile = new TempProfile();
        var catalog = Harness.Boot(profile).Setup.Items.Catalog;
        var hunting = catalog.Get("item.weapon.hunting_bow");
        var halda = catalog.Get(Bow);

        Assert.True(halda.Weapon!.DamageMin > hunting.Weapon!.DamageMin && halda.Weapon.DrawMs < hunting.Weapon.DrawMs);
        Assert.True(halda.ValueBase > hunting.ValueBase);
        Assert.Equal((hunting.Weapon.AmmoDefId, hunting.Weapon.TwoHanded, hunting.Requirements.Attributes.Single()),
            (halda.Weapon.AmmoDefId, halda.Weapon.TwoHanded, halda.Requirements.Attributes.Single()));
    }

    [Fact]
    public void KeepingIt_WalksHaldasSnaresToTheDen_AndTheLineRenewsEachDay()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The walk is done before the world day turns, so the snares' renewal can be watched after it.
        long day = session.Setup.Progression.TicksPerWorldDay;
        var arena = At(AtRenn, session, r => Carrying(r, Token), startTick: day - 6000);
        var flags = arena.Record<WorldFlagChanged>();
        int hides = Carried(arena, "item.material.small_hide"), meat = Carried(arena, "item.material.raw_meat");

        TalkAgain(arena, Renn);
        Assert.Null(Say(arena, "tally"));
        Assert.Null(Say(arena, "keep"));
        Assert.Equal("kept", Now(arena).NodeId);
        Assert.Null(Say(arena, "back"));
        arena.Tick();

        Assert.Equal((Kept, 0L, 1L), Assert.Single(flags.Select(f => (f.FlagId, f.From, f.To))));
        Assert.Equal((ObjectiveStatus.Satisfied, ObjectiveStatus.Closed), (Objective(arena, "o_keep"), Objective(arena, "o_return")));
        // Kept: the tally stays in the pack, he no longer asks about it, and he will talk about her line instead.
        Assert.Equal(1, Carried(arena, Token));
        Assert.Equal(new[] { "place", "work", "bed", "trapline", "notices", "ask_old_late", "ask_quiet_folk", "ask_ashen_hollow", "ask_veth", "ask_wayhold", "ask_ashen_hollow_p2", "ask_old_late_p2", "ask_veth_p2", "ask_wayhold_p2", "leave", "story_begin_accounts", "story_note" }, Replies(arena));
        Assert.Contains("North slope. Before breakfast, Halda said. I wrote it down.", arena.Simulation.Barks(Renn).Select(b => b.Text));
        Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));

        Walk(arena, OutOfTheLonghouse);
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "door.longhouse")));
        arena.Tick();
        Walk(arena, ToTheWestPelts);
        Gather(arena, "halda_pelt_snare_west");
        Walk(arena, ToTheWestLarder);
        Gather(arena, "halda_larder_snare_west");
        Walk(arena, ToTheEastPelts);
        Gather(arena, "halda_pelt_snare_east");
        Walk(arena, ToTheEastLarder);
        Gather(arena, "halda_larder_snare_east");
        // A pelt and a portion a snare at least - survival's yield bonus can add to a catch.
        Assert.True(Carried(arena, "item.material.small_hide") >= hides + 2 && Carried(arena, "item.material.raw_meat") >= meat + 2);
        Assert.Equal(QuestStatus.Active, State(arena).Status);   // the line ends at the den
        Walk(arena, ToTheDen);
        arena.Tick();
        Assert.Equal(QuestStatus.Completed, State(arena).Status);

        // The line is a route, not a one-off: checked, a snare is empty until the day turns, then it holds again.
        var snare = arena.Simulation.Nodes.Single(n => n.Name == "halda_larder_snare_east");
        Assert.False(snare.Ready);
        Assert.True(arena.Simulation.WorldTick < day, $"the walk ran past the day's turn, at tick {arena.Simulation.WorldTick}");
        arena.Tick((int)(day - arena.Simulation.WorldTick) + 2);
        Assert.True(arena.Simulation.Nodes.Single(n => n.Name == "halda_larder_snare_east").Ready);
    }

    /// <summary>Tam is offered the tally only while it has not been kept: the reply reads Renn's flag in the waystation's cell.</summary>
    [Fact]
    public void TamsTallyReplies_AreGatedOnItNotBeingKept()
    {
        using var profile = new TempProfile();
        var tam = Harness.Boot(profile).Setup.Social.Dialogues["dialogue.cairnwold.tam_crowden"];

        foreach (string node in new[] { "greet", "again" })
        {
            var reply = tam.Nodes[node].Choices.Single(c => c.Id == "halda_token");
            var gate = Assert.Single(reply.Conditions.OfType<WorldStateCondition>());
            Assert.Equal((Kept, "location.outpost", 0L, 0L), (gate.FlagId, gate.LocationId, gate.Min, gate.Max));
        }
    }
}
