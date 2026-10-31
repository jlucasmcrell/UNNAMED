using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Travel;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The quests and story framework (WP4) in the game's own region: the opening that every new character starts, a creature's defeat written
/// as a flag from its death, choices the story remembers, a reply given and an arrival by the stones counted, competitive bounties that go
/// to whoever finishes first, the journal's tracks and Recent list, the compass's mark, notes, and the event autosaves.
/// </summary>
public class StoryFrameworkTests
{
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string RennsTalk = "dialogue.ashen_hollow.renn_vale";
    private const string Wolves = "quest.bounty.road_wolves";
    private const string Outpost = "location.outpost";
    private const string Flag = "world.test.story_flag";

    // Hollow metres: beside Renn in the longhouse (BountyTests), and the open ground south-east of the yard (CombatTests).
    private static readonly (double X, double Z) AtRenn = (48, 126.2);
    private static readonly (double X, double Z) OpenGround = (120, 60);

    // Region metres: within a hand's reach of each stone (TravelTests).
    private static readonly (double X, double Z) AtAshen = (428.9, 758.0);
    private static readonly (double X, double Z) AtRim = (880.6, 702.5);

    private static ObjectiveDefinition O(string id, ObjectiveCondition condition, params string[] next) => new(id, id, condition, next.ToImmutableArray());

    private static QuestDefinition Quest(string id, params ObjectiveDefinition[] objectives) =>
        new(id, "Test " + id, "A quest the test plays.", null, objectives[0].Id, objectives.ToImmutableArray(), ImmutableArray<ObjectiveCondition>.Empty,
            ImmutableArray<QuestReward>.Empty);

    /// <summary>The game's rules, with these quests added to its own and its own opening (The Road In) left out, so a test's opening is the only one.</summary>
    private static SimulationSetup With(GameSession session, params QuestDefinition[] quests) =>
        session.Setup with
        {
            Quests = new QuestSetup(session.Setup.Quests.Quests.RemoveRange(session.Setup.Quests.Quests.Values.Where(q => q.OnNewGame).Select(q => q.Id))
                .SetItems(quests.Select(q => KeyValuePair.Create(q.Id, q)))),
        };

    private static Arena Hollow(GameSession session, SimulationSetup setup, (double X, double Z) place, Func<PlayerRecord, PlayerRecord>? change = null,
        params (string DefId, double X, double Z, string Role)[] creatures) =>
        Arena.OpenCreatures(session, setup, place, 0, creatures, change);

    private static Arena Region(GameSession session, SimulationSetup setup, (double X, double Z) place, Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, setup, place, 0, Array.Empty<(string, double, double, string)>(), change, origin: Harness.Region);

    private static QuestStatus? Status(Arena arena, string quest) =>
        arena.Simulation.CaptureRecord().Quests.SingleOrDefault(q => q.QuestId == quest)?.Status;

    private static long FlagAt(Arena arena, string flag, string location)
    {
        var place = arena.Simulation.Setup.Layout.Locations.Single(l => l.Id == location);
        return arena.Simulation.World.GetFlag(CellKey.OfWorld(place.XMm / 1000.0, place.ZMm / 1000.0), flag);
    }

    private static string? Say(Arena arena, string reply) => arena.Submit(new ChooseCommand(arena.Player, reply));

    /// <summary>Renn's notices, past his once-only greeting.</summary>
    private static void ToTheNotices(Arena arena)
    {
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Renn)));
        if (arena.Simulation.Conversation!.NodeId == "greet")
        {
            Assert.Null(Say(arena, "leave"));
            Assert.Null(arena.Submit(new TalkCommand(arena.Player, Renn)));
        }
        Assert.Null(Say(arena, "notices"));
        Assert.Equal("notices", arena.Simulation.Conversation!.NodeId);
    }

    private static PlayerRecord Knowing(PlayerRecord record, params string[] stones) =>
        record with { Waystones = stones.Select(s => new WaystoneRecord(s, "region.starting_frontier", WaystoneMethod.Touched, 0)).ToImmutableArray() };

    private static EntityId InstanceOf(Arena arena, string npc) => arena.Simulation.Npcs.Single(n => n.Id == npc).InstanceId;

    // ── the opening ─────────────────────────────────────────────────────────

    [Fact]
    public void BeginningTheStory_StartsEveryOpeningQuestOnce_OnTheMainTrack()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var opening = Quest("quest.test.opening", O("o_look", new VisitLocation("location.test.nowhere"))) with
        {
            OnNewGame = true, Track = QuestTrack.Main, Act = 1,
        };
        var arena = Hollow(session, With(session, opening), AtRenn);
        Assert.True(arena.Simulation.HasOpeningQuests);
        var started = arena.Record<QuestStarted>();

        Assert.Null(arena.Submit(new BeginStoryCommand(arena.Player)));
        Assert.Null(arena.Submit(new BeginStoryCommand(arena.Player)));   // a story begins once: a second asking starts nothing
        Assert.Equal(opening.Id, Assert.Single(started).QuestId);
        Assert.Contains("unknown actor", arena.Submit(new BeginStoryCommand(InstanceOf(arena, Renn))));

        var journal = arena.Simulation.Journal;
        var main = Assert.Single(journal.Main);
        Assert.Equal((opening.Id, QuestTrack.Main, (int?)1), (main.Id, main.Track, main.Act));
        Assert.Empty(journal.Side);
        Assert.Equal($"Took up {opening.Title}.", journal.Recent.First().Text);
        // The tracker follows the main story before anything else under way.
        Assert.Equal(opening.Id, arena.Simulation.TrackedQuest());
    }

    [Fact]
    public void ANewGame_BeginsTheStoryBeforeTheFirstTick_OnlyWhenContentHasAnOpening()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The game's own opening (WP2's local part): The Road In, on the main track, begun before the first tick.
        var begun = session.NewGame(Harness.Named("Wanderer"), seed: 42);
        Assert.True(begun.HasOpeningQuests);
        Assert.Equal(0, begun.WorldTick);
        // and, for a Veth, their heritage questline What Was Kept (owner, 2026-10-24)
        Assert.Equal(new[] { "quest.heritage.what_was_kept", "quest.main.the_road_in" }, begun.Quests.Select(q => q.Id).Order(StringComparer.Ordinal));
        var road = begun.Quests.Single(q => q.Id == "quest.main.the_road_in");
        Assert.Equal(("quest.main.the_road_in", QuestStatus.Active), (road.Id, road.Status));
        Assert.IsType<BeginStoryCommand>(Assert.Single(begun.CommandLog).Command);
        Assert.Equal(new[] { "Took up The Road In.", "Took up What Was Kept." }, session.Journal!.Recent.Select(e => e.Text));

        // Content with no opening: a new game submits nothing. (A heritage questline opens a game too, so here it is the Ondrek's, not this Veth's.)
        using var bare = EditedContent.Without("quests/main/the_road_in.yaml");
        string heritage = Path.Combine(bare.Root, "quests", "heritage", "what_was_kept.yaml");
        File.WriteAllText(heritage, File.ReadAllText(heritage).Replace("people_ref: people.veth", "people_ref: people.ondrek"));
        using var second = new TempProfile();
        var simulation = GameSession.Boot(new GameOptions(bare.Root, second.Root)).NewGame(Harness.Named("Wanderer"), seed: 42);
        Assert.False(simulation.HasOpeningQuests);
        Assert.Empty(simulation.CommandLog);
    }

    // ── defeat, choices, replies, stones ────────────────────────────────────

    [Fact]
    public void ACreaturesDeath_WritesItsDefeatFlag_ByAnyHand_AndADefeatBeforeTheQuestCounts()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var hunt = Quest("quest.test.defeat", O("o_wolf", new DefeatCreature(Arena.Wolf, Flag, Outpost, Boss: false))) with { OnNewGame = true };
        var arena = Hollow(session, With(session, hunt), OpenGround, null, (Arena.Wolf, 120, 64, "sentinel"));
        var flags = arena.Record<WorldFlagChanged>();

        arena.Fight(arena.Creature(), 1_200);
        Assert.False(arena.Creature().Alive);
        Assert.Equal(1, FlagAt(arena, Flag, Outpost));
        Assert.Equal(Flag, Assert.Single(flags).FlagId);
        Assert.Null(Status(arena, hunt.Id));   // the quest was never started; the defeat is on record all the same

        Assert.Null(arena.Submit(new BeginStoryCommand(arena.Player)));
        arena.Tick();
        Assert.Equal(QuestStatus.Completed, Status(arena, hunt.Id));
    }

    [Fact]
    public void AChoice_SetsItsNamespacedFlag_OnceItsObjectiveIsSatisfied_AndTheJournalKeepsItsLine()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const string line = "You chose the test's way.";
        var fork = Quest("quest.test.choice", O("o_choose", new WaitUntil(0), "o_after") with
        {
            Effects = ImmutableArray.Create<QuestEffect>(new ChoiceEffect(Flag, Outpost, 2, line)),
        }, O("o_after", new WaitUntil(100))) with { OnNewGame = true, Track = QuestTrack.Main, Act = 1 };
        var arena = Hollow(session, With(session, fork), AtRenn);
        var made = arena.Record<ChoiceMade>();
        Assert.Null(arena.Submit(new BeginStoryCommand(arena.Player)));
        arena.Tick(3);

        var choice = Assert.Single(made);
        Assert.Equal((fork.Id, "o_choose", Flag, 2L, line), (choice.QuestId, choice.ObjectiveId, choice.FlagId, choice.Value, choice.Line));
        Assert.Equal(2, FlagAt(arena, Flag, Outpost));
        var recent = arena.Simulation.Journal.Recent;
        Assert.Equal(new[] { line, "o_choose", $"Took up {fork.Title}." }, recent.Select(e => e.Text));
        Assert.Equal(JournalEntryKind.Choice, recent[0].Kind);
    }

    [Fact]
    public void AReplyGiven_CountsForTheDialogueChoiceWaitingForIt_AndNoOtherReplyDoes()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var asked = Quest("quest.test.reply", O("o_reply", new ChoiceGiven(Renn, RennsTalk, "notices", "take_wolves"))) with { OnNewGame = true };
        var arena = Hollow(session, With(session, asked), AtRenn);
        Assert.Null(arena.Submit(new BeginStoryCommand(arena.Player)));
        ToTheNotices(arena);
        arena.Tick();
        Assert.Equal(QuestStatus.Active, Status(arena, asked.Id));   // the reply that led here was "notices", not this one

        Assert.Null(Say(arena, "take_wolves"));
        arena.Tick();
        Assert.Equal(QuestStatus.Completed, Status(arena, asked.Id));
        Assert.Equal(QuestStatus.Active, Status(arena, Wolves));   // the reply did what it does as well
    }

    [Fact]
    public void ArrivingByTheStones_CountsForTravelTo_AndTouchingAStone_SatisfiesWaystoneKnown()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var journey = Quest("quest.test.journey", O("o_known", new WaystoneKnown("waystone.ashen"), "o_go"), O("o_go", new TravelTo("waystone.ashen")))
            with { OnNewGame = true };
        var setup = With(session, journey);

        var ashen = Region(session, setup, AtAshen);
        Assert.Null(ashen.Submit(new BeginStoryCommand(ashen.Player)));
        ashen.Tick();
        Assert.Null(ashen.Simulation.CaptureRecord().Quests.Single().Objective("o_go"));
        Assert.Null(ashen.Submit(new TouchStoneCommand(ashen.Player, "waystone.ashen")));
        ashen.Tick();
        Assert.Equal(ObjectiveStatus.Satisfied, ashen.Simulation.CaptureRecord().Quests.Single().Objective("o_known")!.Status);

        // Walking up to the stone is not arriving by the stones.
        var rim = Region(session, setup, AtRim, r => Knowing(r, "waystone.ashen", "waystone.rim"));
        Assert.Null(rim.Submit(new BeginStoryCommand(rim.Player)));
        rim.Tick();
        Assert.Equal(ObjectiveStatus.Active, rim.Simulation.CaptureRecord().Quests.Single().Objective("o_go")!.Status);
        Assert.Null(rim.Submit(new TravelCommand(rim.Player, "waystone.rim", "waystone.ashen")));
        rim.Tick();
        Assert.Equal(QuestStatus.Completed, Status(rim, journey.Id));
    }


    /// <summary>
    /// R12: a named rival who takes a competitive posting is remembered for it and says so in their own voice. The rivals are ordinary NPCs
    /// with a rival block in content, so the claim goes through the same command the character and every other NPC use.
    /// </summary>
    [Fact]
    public void ARivalWhoClaimsACompetitiveBounty_IsRememberedForIt_AndSaysSo()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Hollow(session, Competitive(session, AVethRival()), AtRenn);
        ToTheNotices(arena);

        var rival = arena.Simulation.Rivals().Single();
        var taunted = arena.Record<RivalTaunted>();

        Assert.Null(arena.Submit(new ClaimBountyCommand(InstanceOf(arena, rival.NpcId), Wolves)));

        var said = Assert.Single(taunted);
        Assert.Equal(rival.NpcId, said.NpcId);
        Assert.Equal(RivalMoment.Beaten, said.Moment);
        Assert.False(string.IsNullOrWhiteSpace(said.Line));

        var after = arena.Simulation.Rivals().Single(r => r.NpcId == rival.NpcId);
        Assert.Equal(1, after.Wins);
        Assert.Equal(0, after.Losses);
        Assert.Equal(RivalRegard.Wary, after.Regard);
    }

    // ── competitive bounties ────────────────────────────────────────────────

    private static SimulationSetup Competitive(GameSession session) =>
        With(session, session.Setup.Quests.Quests[Wolves] with { Competitive = true });

    /// <summary>The same competitive setup, with the smith carrying a rival block: her own instantiation is what races.</summary>
    private static SimulationSetup Competitive(GameSession session, RivalProfile rival) =>
        Competitive(session) with
        {
            Social = session.Setup.Social with
            {
                Npcs = session.Setup.Social.Npcs.SetItem(Kera, session.Setup.Social.Npcs[Kera] with { Rival = rival }),
            },
        };

    private static RivalProfile AVethRival() => new(
        Kera, People: "veth", Call: "call.wayfarer", PacePermille: 1000, SkillPermille: 500,
        Bounties: System.Collections.Immutable.ImmutableArray<string>.Empty,
        Taunts: new RivalTaunts(
            System.Collections.Immutable.ImmutableArray.Create("Met. I was here first, which is the whole of my case."),
            System.Collections.Immutable.ImmutableArray.Create("Taken. You were still asking directions."),
            System.Collections.Immutable.ImmutableArray.Create("Yours, then. I'll say I let you.")));

    [Fact]
    public void ACompetitiveBounty_HeldByTheCharacter_GoesToWhoeverFinishesFirst_AndTheBoardShowsThem()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Hollow(session, Competitive(session), AtRenn);
        ToTheNotices(arena);
        Assert.Null(Say(arena, "take_wolves"));
        Assert.Equal(BountyStanding.Held, arena.Simulation.Bounties.Single(b => b.QuestId == Wolves).Standing);

        var failed = arena.Record<QuestFailed>();
        var claimed = arena.Record<BountyClaimed>();
        var kera = InstanceOf(arena, Kera);
        Assert.Equal("the character claims a bounty by finishing it", arena.Submit(new ClaimBountyCommand(arena.Player, Wolves)));
        Assert.Contains("unknown actor", arena.Submit(new ClaimBountyCommand(EntityId.NewId(EntityKind.Character), Wolves)));
        Assert.Contains("not open to anyone else", arena.Submit(new ClaimBountyCommand(kera, "quest.bounty.crossing_timber")));
        Assert.Null(arena.Submit(new ClaimBountyCommand(kera, Wolves)));
        Assert.Contains("already claimed", arena.Submit(new ClaimBountyCommand(kera, Wolves)));

        Assert.Equal(Wolves, Assert.Single(failed).QuestId);
        Assert.Equal((Wolves, kera.Value), (Assert.Single(claimed).QuestId, claimed[0].ClaimantId));
        var held = arena.Simulation.Journal.Bounties.Single(q => q.Id == Wolves);
        Assert.Equal((QuestStatus.Failed, "Kera Voss"), (held.Status, held.LostTo));
        Assert.Equal("Kera Voss finished Wolves on the Road first.", arena.Simulation.Journal.Recent.First().Text);

        var board = arena.Simulation.Bounties.Single(b => b.QuestId == Wolves);
        long reposted = arena.Simulation.WorldTick + session.Setup.Quests.Quests[Wolves].RepeatAfterTicks!.Value;
        Assert.Equal((BountyStanding.Claimed, kera.Value, "Kera Voss", (long?)reposted),
            (board.Standing, board.ClaimantId, board.ClaimantName, board.PostedAgainTick));
        // Renn does not post again what someone else has done until its time comes round.
        Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
        ToTheNotices(arena);
        Assert.DoesNotContain("take_wolves", arena.Simulation.Conversation!.Replies.Select(r => r.Id));
    }

    [Fact]
    public void ACompetitiveBountyNoOneHeld_IsTakenOffTheBoard_UntilItIsPostedAgain_AndTheJournalLeavesItOut()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var setup = Competitive(session);
        var arena = Hollow(session, setup, AtRenn);
        var kera = InstanceOf(arena, Kera);
        Assert.Equal(BountyStanding.Open, arena.Simulation.Bounties.Single(b => b.QuestId == Wolves).Standing);
        Assert.Null(arena.Submit(new ClaimBountyCommand(kera, Wolves)));

        Assert.Empty(arena.Simulation.Quests);
        Assert.Empty(arena.Simulation.Journal.Recent);
        Assert.Equal((BountyStanding.Claimed, "Kera Voss"),
            (arena.Simulation.Bounties.Single(b => b.QuestId == Wolves).Standing, arena.Simulation.Bounties.Single(b => b.QuestId == Wolves).ClaimantName));
        ToTheNotices(arena);
        Assert.DoesNotContain("take_wolves", arena.Simulation.Conversation!.Replies.Select(r => r.Id));

        // A world day on, it is posted again, and the character may take it.
        var record = arena.Simulation.CaptureRecord();
        long later = arena.Simulation.WorldTick + setup.Quests.Quests[Wolves].RepeatAfterTicks!.Value;
        var next = Arena.OpenCreatures(session, setup, AtRenn, 0, Array.Empty<(string, double, double, string)>(), r => r.WithQuests(record.Quests),
            startTick: later);
        Assert.Equal(BountyStanding.Open, next.Simulation.Bounties.Single(b => b.QuestId == Wolves).Standing);
        ToTheNotices(next);
        Assert.Null(Say(next, "take_wolves"));
        Assert.Equal(QuestStatus.Active, Status(next, Wolves));
    }

    [Fact]
    public void ACompetitiveBountyTheCharacterFinishes_IsTheirs_AndNoOneElseCanClaimIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var setup = Competitive(session);
        var atRenn = Hollow(session, setup, AtRenn);
        ToTheNotices(atRenn);
        Assert.Null(Say(atRenn, "take_wolves"));
        var taken = atRenn.Simulation.CaptureRecord().Quests;

        var arena = Hollow(session, setup, OpenGround, r => r.WithQuests(taken),
            (Arena.Wolf, 120, 64, "sentinel"), (Arena.Wolf, 120, 84, "sentinel"), (Arena.Wolf, 120, 40, "sentinel"));
        var claimed = arena.Record<BountyClaimed>();
        foreach (var wolf in arena.Simulation.Creatures.OrderBy(c => c.Key, StringComparer.Ordinal).ToList())
        {
            arena.Fight(wolf, 1_200);
            arena.Tick(2);
        }
        Assert.Equal(QuestStatus.Completed, Status(arena, Wolves));
        Assert.Equal(arena.Player.Value, Assert.Single(claimed).ClaimantId);
        var board = arena.Simulation.Bounties.Single(b => b.QuestId == Wolves);
        Assert.Equal((BountyStanding.Claimed, arena.Player.Value, "You"), (board.Standing, board.ClaimantId, board.ClaimantName));
        Assert.Contains("already claimed", arena.Submit(new ClaimBountyCommand(InstanceOf(arena, Kera), Wolves)));
    }

    [Fact]
    public void AClaim_IsKeptByASave_AndLoads_WithTheSchemaAsItIs()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var setup = Competitive(session);
        var arena = Hollow(session, setup, AtRenn);
        Assert.Null(arena.Submit(new ClaimBountyCommand(InstanceOf(arena, Kera), Wolves)));
        var store = new Persistence.SaveStore(profile.Root);
        store.Save(Persistence.SaveSlots.Manual("claimed"), Persistence.SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(),
            session.Content, arena.Simulation.WorldTick, 0));

        var loaded = store.Load(Persistence.SaveSlots.Manual("claimed"),
            new Persistence.LoadContext(session.Generator, session.Content, new UNNAMED.EntityRegistry.EntityRegistry()) { Transitions = session.Transitions });
        var resumed = Arena.Resume(setup, loaded);
        var board = resumed.Simulation.Bounties.Single(b => b.QuestId == Wolves);
        Assert.Equal((BountyStanding.Claimed, "Kera Voss"), (board.Standing, board.ClaimantName));
        Assert.Equal(Persistence.SaveFormat.SchemaVersion, loaded.Manifest.SchemaVersion);
    }

    // ── the tracker and the compass ─────────────────────────────────────────

    [Fact]
    public void TheCompassMarksTheTrackedBeat_OnlyOnceTheCharacterKnowsWhereItIs()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var errand = Quest("quest.test.errand", O("o_outpost", new ExploreLocation(Outpost, 1_000), "o_renn"), O("o_renn", new TalkTo(Renn, RennsTalk,
            ImmutableArray.Create("farewell")))) with { OnNewGame = true };
        var setup = With(session, errand);

        var stranger = Hollow(session, setup, OpenGround);
        Assert.Null(stranger.Submit(new BeginStoryCommand(stranger.Player)));
        Assert.Null(stranger.Simulation.Marker(errand.Id));   // the outpost not yet found: no mark

        var place = setup.Layout.Locations.Single(l => l.Id == Outpost);
        var found = Hollow(session, setup, OpenGround, r => r.WithDiscoveries(new[] { new DiscoveryRecord(Outpost, DiscoveryMethod.Sighted, 0) }));
        Assert.Null(found.Submit(new BeginStoryCommand(found.Player)));
        var marker = found.Simulation.Marker(errand.Id)!;
        Assert.Equal(("o_outpost", TargetKind.Location, Outpost, place.XMm, place.ZMm), (marker.ObjectiveId, marker.Kind, marker.Ref, marker.XMm, marker.ZMm));
        Assert.Null(found.Simulation.Marker(Wolves));   // a quest not under way has no mark

        // Past the outpost, to Renn: marked only once the character has spoken with him.
        var met = Hollow(session, setup, OpenGround, r => r.WithSocial(r.Relationships,
            new[] { new ConversationMemory(RennsTalk, ImmutableArray.Create("greet")) }).WithQuests(new[]
            {
                new QuestState(errand.Id, QuestStatus.Active, 0, null, null, ImmutableArray.Create(
                    new ObjectiveState("o_outpost", ObjectiveStatus.Satisfied, 0, 0, 0), new ObjectiveState("o_renn", ObjectiveStatus.Active, 0, null, 0))),
            }));
        var renn = met.Simulation.Npcs.Single(n => n.Id == Renn);
        Assert.Equal((TargetKind.Npc, "Renn Vale", renn.Body.XMm), (met.Simulation.Marker(errand.Id)!.Kind, met.Simulation.Marker(errand.Id)!.Label,
            met.Simulation.Marker(errand.Id)!.XMm));
    }

    [Fact]
    public void TheTracker_FollowsTheQuestAskedFor_WhileItIsActive()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var story = Quest("quest.test.story", O("o_wait", new WaitUntil(1_000))) with { OnNewGame = true, Track = QuestTrack.Main, Act = 1 };
        var side = Quest("quest.test.side", O("o_wait", new WaitUntil(1_000))) with { OnNewGame = true };
        var arena = Hollow(session, With(session, story, side), AtRenn);
        Assert.Null(arena.Submit(new BeginStoryCommand(arena.Player)));

        Assert.Equal(story.Id, arena.Simulation.TrackedQuest());
        Assert.Equal(side.Id, arena.Simulation.TrackedQuest(side.Id));
        Assert.Equal(story.Id, arena.Simulation.TrackedQuest(Wolves));   // not under way: the story again
    }

    // ── the session: Recent with the away log, notes, event autosaves ──────

    [Fact]
    public void TheAwayLog_JoinsTheRecentList_NewestFirst_AndANewWorldStartsWithoutIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        session.NewGame(Harness.Named("Wanderer"), seed: 42);
        string[] opening = { "Took up The Road In.", "Took up What Was Kept." };   // the opening's lines, there from the start (a Veth's heritage too)
        Assert.Equal(opening, session.Journal!.Recent.Select(e => e.Text));
        session.NoteAway(0, new[] { "Three days passed.", " ", "Renn sold the last of the salt." });
        Assert.Equal(new[] { "Renn sold the last of the salt.", "Three days passed." }.Concat(opening), session.Journal!.Recent.Select(e => e.Text));
        Assert.All(session.Journal.Recent.Take(2), e => Assert.Equal(JournalEntryKind.Away, e.Kind));

        session.NewGame(Harness.Named("Wanderer"), seed: 43);
        Assert.Equal(opening, session.Journal!.Recent.Select(e => e.Text));
    }

    [Fact]
    public void Notes_AreThePlayersOwn_KeptPerCharacter_BesideTheSaves()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var first = session.NewGame(Harness.Named("Wanderer"), seed: 42);
        Assert.Equal("a note has words", session.AddNote("   "));
        Assert.NotNull(session.AddNote(new string('x', JournalNotes.MaxLength + 1)));
        Assert.Null(session.AddNote("Ask Renn about the den."));
        Assert.Null(session.AddNote("Kera wants ore."));
        Assert.Null(session.RemoveNote(0));
        Assert.Equal("there is no note 5", session.RemoveNote(5));
        Assert.Equal(new[] { "Kera wants ore." }, session.Notes!.All.Select(n => n.Text));

        // The same character's notes come back from the file; another character's journal starts empty.
        Assert.Equal(new[] { "Kera wants ore." }, new JournalNotes(profile.Root, first.PlayerId).All.Select(n => n.Text));
        session.NewGame(Harness.Named("Wanderer"), seed: 43);
        Assert.Empty(session.Notes!.All);
        Assert.Empty(session.SaveSlots());   // notes are not a save
    }

    [Fact]
    public void AQuestCompleted_AsksForAnAutosave_TakenOnceTheSpacingHasPassed()
    {
        using var profile = new TempProfile();
        var session = Harness.Playing(profile, AtRenn, 0, r => r.WithSocial(r.Relationships,
            new[] { new ConversationMemory(RennsTalk, ImmutableArray.Create("farewell")) }).WithQuests(new[]
            {
                new QuestState("quest.ashen_hollow.end_of_playtest", QuestStatus.Active, 0, null, null,
                    ImmutableArray.Create(new ObjectiveState("o_book", ObjectiveStatus.Active, 0, null, 0))),
            }));
        var completed = Harness.Record<QuestCompleted>(session);
        var frames = new List<FrameResult>();
        int spacing = (int)Math.Ceiling(GameSession.EventAutosaveSpacingSeconds / session.TickSeconds);
        for (int i = 0; i < spacing + 2; i++)
            frames.Add(session.Frame(session.TickSeconds));

        Assert.Single(completed);
        Assert.Single(frames, f => f.AutosaveTaken is not null);
        int at = frames.FindIndex(f => f.AutosaveTaken is not null);
        Assert.InRange(at, spacing - 2, spacing);   // not at the completion, but once the spacing from the load has passed
        Assert.False(session.AutosaveRequested);
        Assert.True(session.WaitForSaves(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void AnAutosaveAskedFor_IsTakenAtAFrame_AndOnlyOnce()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        session.RequestAutosave();
        Assert.False(session.AutosaveRequested);   // no world yet: nothing to save
        session.NewGame(Harness.Named("Wanderer"), seed: 42);
        Harness.Ticks(session, (int)Math.Ceiling(GameSession.EventAutosaveSpacingSeconds / session.TickSeconds));
        session.RequestAutosave();
        var taken = session.Frame(session.TickSeconds);
        Assert.NotNull(taken.AutosaveTaken);
        Assert.Null(session.Frame(session.TickSeconds).AutosaveTaken);
        Assert.True(session.WaitForSaves(TimeSpan.FromSeconds(30)));
    }

    /// <summary>A copy of the game's content with files added, in a directory removed afterwards.</summary>
    private sealed class EditedContent : IDisposable
    {
        /// <summary>The game's content without these files.</summary>
        public static EditedContent Without(params string[] removed)
        {
            var content = new EditedContent();
            foreach (string relative in removed)
                File.Delete(Path.Combine(content.Root, relative));
            return content;
        }

        public EditedContent(params (string Path, string Text)[] added)
        {
            string source = Path.Combine(Harness.RepoRoot(), "content");
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            foreach (var (relative, text) in added)
                File.WriteAllText(Path.Combine(Root, relative), text);
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-story-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
