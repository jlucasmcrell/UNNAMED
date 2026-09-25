using UNNAMED.Domain.Quests;
using UNNAMED.Persistence;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// M6: the content bible's Quest 2, The Three Quiet Stones, over the game's own content - the stones and the Foldscar's heart as
/// switches that set world flags in the Foldscar's cell, the fold that holds Tavar until the heart is steadied, the quest played through
/// without a fight and in the orders the bible tolerates (§32), and all of it kept by a save.
/// </summary>
public class FoldscarTests
{
    private const string Quest = "quest.ashen_hollow.three_quiet_stones";
    private const string Sel = "npc.ashen_hollow.sel_arien";
    private const string Tavar = "npc.ashen_hollow.tavar_orr";
    private const string Spider = "creature.beast.cave_hunting_spider";
    private const string North = "world.foldscar.stone_north_aligned";
    private const string Steadied = "world.foldscar.steadied";
    private static readonly CellKey Foldscar = CellKey.Parse("r_0_0:c_01_00");
    private static readonly (string, double, double, string)[] NoCreatures = Array.Empty<(string, double, double, string)>();

    // Where the character stands to work each thing (content/regions/ashen_hollow.yaml), and the ways between them: down the road from
    // Sel's table into the Foldscar; the south-east stone by the bible's route round the spider (§8), beyond what it hears.
    private static readonly (double X, double Z) AtSel = (71, 123);
    private static readonly (double X, double Z)[] ToTheFoldscar = { (80, 118), (100, 100), (135, 70) };
    private static readonly (double X, double Z) AtNorth = (148.5, 78);
    private static readonly (double X, double Z)[] ToTheSouthWest = { (135, 60), (123.5, 38) };
    private static readonly (double X, double Z)[] ToTheSouthEast = { (140, 62), (160, 65), (187, 65), (194, 45), (182.5, 31) };
    private static readonly (double X, double Z)[] ToTheHeart = { (194, 45), (187, 65), (160, 65), (153, 50.2) };
    private static readonly (double X, double Z)[] ToTavar = { (150, 45), (146.2, 42.8) };
    private static readonly (double X, double Z)[] BackToSel = { (150, 45), (135, 70), (100, 100), (80, 118), AtSel };

    private static Arena At(GameSession session, (double X, double Z) place, bool keepSpawns = false) =>
        Arena.OpenCreatures(session, session.Setup, place, 90, NoCreatures, keepSpawns: keepSpawns);

    private static void Walk(Arena arena, params (double X, double Z)[] route)
    {
        foreach (var (x, z) in route)
            Assert.True(arena.WalkTo(x, z), $"never reached ({x}, {z})");
    }

    private static string? Work(Arena arena, string key) => arena.Submit(new InteractCommand(arena.Player, key));

    private static string[] Replies(Arena arena) => arena.Simulation.Conversation!.Replies.Select(r => r.Id).ToArray();

    private static (string, bool)[] Switches(Arena arena) => arena.Simulation.Switches.Select(s => (s.Site.Key, s.Set)).ToArray();

    private static bool FoldStands(Arena arena) => arena.Simulation.Barriers.Single().Standing;

    /// <summary>The three stones in any order and then the heart, walked the way the bible's acceptance path goes (§31).</summary>
    private static void TurnTheStonesAndSteadyTheHeart(Arena arena)
    {
        Walk(arena, AtNorth);
        Assert.Null(Work(arena, "switch.stone_north"));
        Walk(arena, ToTheSouthWest);
        Assert.Null(Work(arena, "switch.stone_southwest"));
        Walk(arena, ToTheSouthEast);
        Assert.Null(Work(arena, "switch.stone_southeast"));
        Walk(arena, ToTheHeart);
        Assert.Null(Work(arena, "switch.foldscar_heart"));
        arena.Tick();
    }

    private static QuestView Journal(Arena arena) => arena.Simulation.Quests.Single(q => q.Id == Quest);

    [Fact]
    public void AQuietStone_IsASwitch_ThatSetsItsFlagOnce_InTheFoldscarsCell()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtNorth);
        var set = arena.Record<SwitchSet>();
        var changed = arena.Record<WorldFlagChanged>();

        // Measured from the body, like a door: the south-west stone is 45 m off.
        Assert.StartsWith("the south-west Quiet Stone is 4", Work(arena, "switch.stone_southwest"));
        Assert.Null(Work(arena, "switch.stone_north"));

        Assert.Equal(new SwitchSet(arena.Player, "switch.stone_north", 0), Assert.Single(set));
        Assert.Equal(new WorldFlagChanged(Foldscar.ToString(), North, 0, 1, 0), Assert.Single(changed));
        Assert.Equal(1, arena.Simulation.World.GetFlag(Foldscar, North));
        Assert.Equal(new[] { ("switch.stone_north", true), ("switch.stone_southwest", false), ("switch.stone_southeast", false),
            ("switch.foldscar_heart", false) }, Switches(arena));
        // Set for good: a stone turned into line is not turned out of it again.
        Assert.Equal("the north Quiet Stone is already set", Work(arena, "switch.stone_north"));
        Assert.Single(set);
    }

    [Fact]
    public void TheHeart_WillNotSteady_UntilAllThreeStonesAreInLine_AndTheFold_HoldsTavarUntilItIs()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtNorth);
        long talkReach = session.Setup.Items.Inventory.ReachMm + session.Setup.Movement.BodyRadiusMm;

        // The fold: a body cannot get within speaking distance of Tavar, and he cannot be spoken to from where it stops.
        Walk(arena, (150, 45));
        Assert.False(arena.WalkTo(145, 42, maxTicks: 200));
        var body = arena.Simulation.Player.Body;
        Assert.True(Math.Sqrt(Math.Pow(body.XMm - 145_000, 2) + Math.Pow(body.ZMm - 42_000, 2)) >= 3_000 + session.Setup.Movement.BodyRadiusMm - 1,
            "the fold let the body through");
        Assert.True(3_000 + session.Setup.Movement.BodyRadiusMm > talkReach, "the fold is narrower than a hand's reach");
        Assert.Equal("Tavar Orr is out of reach", arena.Submit(new TalkCommand(arena.Player, Tavar)));
        Assert.True(FoldStands(arena));

        // Two stones in line are not enough.
        Walk(arena, AtNorth);
        Assert.Null(Work(arena, "switch.stone_north"));
        Walk(arena, ToTheSouthWest);
        Assert.Null(Work(arena, "switch.stone_southwest"));
        Walk(arena, (135, 60), (150, 52), (153, 50.2));
        Assert.Equal("It slides out from under your hand. Somewhere in the ring a Quiet Stone is still out of line.", Work(arena, "switch.foldscar_heart"));
        Assert.Equal(0, arena.Simulation.World.GetFlag(Foldscar, Steadied));

        // The third, by the route round the spider, and the heart holds; the fold lifts with it.
        Walk(arena, ToTheSouthEast);
        Assert.Null(Work(arena, "switch.stone_southeast"));
        Walk(arena, ToTheHeart);
        Assert.Null(Work(arena, "switch.foldscar_heart"));
        Assert.Equal(1, arena.Simulation.World.GetFlag(Foldscar, Steadied));
        Assert.False(FoldStands(arena));
        Assert.DoesNotContain(arena.Simulation.DynamicBlockers, b => b.Id == "barrier.foldscar_fold");

        Walk(arena, ToTavar);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Tavar)));
        Assert.Equal("greet", arena.Simulation.Conversation!.NodeId);
    }

    /// <summary>
    /// The content bible's Quest 2 end to end in the real world, with every creature in it: Sel tells of Tavar, the Foldscar is reached,
    /// the stones are turned - the south-east one by the way round the spider - the heart steadied and Tavar spoken to. No objective asks
    /// for a fight (§16: "There is no mandatory combat objective"), no blow is struck, and the spider lives.
    /// </summary>
    [Fact]
    public void TheThreeQuietStones_PlaysEndToEnd_WithTheSpiderAlive_AndPaysOnce()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtSel, keepSpawns: true);
        var satisfied = arena.Record<ObjectiveSatisfied>();
        var completed = arena.Record<QuestCompleted>();
        var rewards = arena.Record<RewardGranted>();
        var hits = arena.Record<HitResolved>();

        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Sel)));
        Assert.Contains("ruin", Replies(arena));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "ruin")));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "tavar")));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "stones")));
        Assert.Contains("walk past it, don't run", arena.Simulation.Conversation!.Text);
        Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
        arena.Tick();
        Assert.Equal(("The Three Quiet Stones", QuestStatus.Active), (Journal(arena).Title, Journal(arena).Status));
        Assert.Equal(new[] { ("o_learn", ObjectiveStatus.Satisfied), ("o_reach", ObjectiveStatus.Active) },
            Journal(arena).Objectives.Select(o => (o.Id, o.Status)));

        Walk(arena, ToTheFoldscar);
        // Inside the Foldscar the stones may be turned in any order: all three are asked for at once.
        Assert.Equal(new[] { "o_north", "o_southwest", "o_southeast" },
            Journal(arena).Objectives.Where(o => o.Status == ObjectiveStatus.Active).Select(o => o.Id));
        TurnTheStonesAndSteadyTheHeart(arena);
        Walk(arena, ToTavar);
        long xp = arena.Simulation.Player.Progression.LifetimeXp.GetValueOrDefault(UNNAMED.Domain.Progression.XpSource.QuestObjective);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Tavar)));
        // Hearing his first line completes the quest; the reply that says Sel sent the character is still there a moment later.
        arena.Tick(20);
        Assert.Equal(QuestStatus.Completed, Journal(arena).Status);
        Assert.Equal(new[] { "sent" }, Replies(arena));   // "Sel sent me": the quest is hers
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "sent")));
        arena.Tick();

        Assert.Equal(new[] { "o_learn", "o_reach", "o_north", "o_southwest", "o_southeast", "o_steady", "o_tavar" }, satisfied.Select(s => s.ObjectiveId));
        Assert.Equal("o_tavar", Assert.Single(completed).ByObjective);
        Assert.Equal(new[] { ("xp", "150 XP"), ("relationship", $"{Sel} trust +10") }, rewards.Select(r => (r.Kind, r.What)));
        Assert.Equal(xp + 150, arena.Simulation.Player.Progression.LifetimeXp[UNNAMED.Domain.Progression.XpSource.QuestObjective]);
        Assert.Empty(hits);
        Assert.True(arena.Simulation.Creatures.Single(c => c.DefId == Spider).Alive);

        arena.Tick(100);
        Assert.Single(completed);
    }

    /// <summary>
    /// Bible §32: "Foldscar visit before receiving Sel's quest". Stones turned and Tavar freed before Sel says a word are not wasted: Tavar
    /// is told the stones were put right, Sel is told he is free - her line that gives the quest is not offered - and the quest starts
    /// there and completes at once, every objective already satisfied by the world.
    /// </summary>
    [Fact]
    public void AFoldscarSteadiedBeforeTheQuest_CountsAtOnce_AndSelHearsTavarIsFree()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, (135, 70));
        var completed = arena.Record<QuestCompleted>();

        TurnTheStonesAndSteadyTheHeart(arena);
        Walk(arena, ToTavar);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Tavar)));
        Assert.Equal(new[] { "found" }, Replies(arena));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "found")));
        Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
        arena.Tick();
        Assert.Empty(arena.Simulation.Quests);

        Walk(arena, BackToSel);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Sel)));
        Assert.Equal(new[] { "books", "ruin", "tavar_back", "leave" }, Replies(arena));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "ruin")));
        Assert.DoesNotContain("tavar", Replies(arena));   // she does not ask for help with what is already done
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "back")));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "tavar_back")));
        arena.Tick();

        Assert.Equal(QuestStatus.Completed, Journal(arena).Status);
        Assert.All(Journal(arena).Objectives, o => Assert.Equal(ObjectiveStatus.Satisfied, o.Status));
        Assert.Single(completed);
    }

    /// <summary>
    /// The Phase-1 technical audit, L-17: Sel's conditions read flags in her own cell only, so with the Foldscar steadied and Tavar not yet
    /// spoken to she still asked for help with a man who was stuck - and he no longer was. She reads the fold's state in the Foldscar.
    /// </summary>
    [Fact]
    public void TheFoldscarSteadied_SelNoLongerAsksForHelpWithTavar()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, (135, 70));
        TurnTheStonesAndSteadyTheHeart(arena);

        Walk(arena, BackToSel);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Sel)));
        Assert.DoesNotContain("tavar", Replies(arena));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "ruin")));
        Assert.DoesNotContain("tavar", Replies(arena));
    }

    /// <summary>
    /// The Phase-1 technical audit, L-18: Tavar's first line is spent once heard, but his thanks are not - walked away from before a reply,
    /// he is still to be answered, once, for the same trust.
    /// </summary>
    [Fact]
    public void TavarWalkedAwayFrom_BeforeAReply_CanStillBeAnswered_Once()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, (135, 70));
        int Trust() => arena.Simulation.CaptureRecord().Relationships.FirstOrDefault(r => r.NpcId == Tavar && r.Dimension == "trust")?.Value ?? 0;

        TurnTheStonesAndSteadyTheHeart(arena);
        Walk(arena, ToTavar);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Tavar)));
        Assert.Equal(new[] { "found" }, Replies(arena));
        Walk(arena, (150, 45));
        Assert.Null(arena.Simulation.Conversation);
        Assert.Equal(0, Trust());

        Walk(arena, ToTavar);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Tavar)));
        Assert.Equal("again", arena.Simulation.Conversation!.NodeId);
        Assert.Contains("found", Replies(arena));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "found")));
        Assert.Equal(("caught", 10), (arena.Simulation.Conversation!.NodeId, Trust()));
        Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Tavar)));
        Assert.DoesNotContain("found", Replies(arena));
        Assert.DoesNotContain("sent", Replies(arena));
        Assert.Equal(10, Trust());
    }

    [Fact]
    public void TheStonesAndTheFold_AreKeptByASave_AsTheFoldscarCellsDelta()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, (135, 70));
        TurnTheStonesAndSteadyTheHeart(arena);

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("foldscar"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        var loaded = Arena.Resume(session.Setup, store.Load(SaveSlots.Manual("foldscar"), new LoadContext(session.Generator, session.Content, new Registry())));

        Assert.Equal(arena.Simulation.StateDigest(), loaded.Simulation.StateDigest());
        Assert.All(Switches(loaded), s => Assert.True(s.Item2));
        Assert.False(FoldStands(loaded));
        var cells = SectionCodec.DecodeCells(File.ReadAllBytes(Path.Combine(profile.Root, SaveSlots.Manual("foldscar"), SaveFormat.Cells)));
        var foldscar = Assert.Single(cells);
        Assert.Equal(Foldscar.ToString(), foldscar.CellKey);
        Assert.Equal(new[] { Steadied, "world.foldscar.stone_north_aligned", "world.foldscar.stone_southeast_aligned", "world.foldscar.stone_southwest_aligned" },
            foldscar.Flags.Select(f => f.Key).OrderBy(k => k, StringComparer.Ordinal));
        Walk(loaded, ToTavar);
        Assert.Null(loaded.Submit(new TalkCommand(loaded.Player, Tavar)));
    }

    [Fact]
    public void TheDebugger_SaysWhichStoneToTurn_AndWhere()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtSel);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Sel)));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "ruin")));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "tavar")));
        Walk(arena, ToTheFoldscar);
        Walk(arena, AtNorth);
        Assert.Null(Work(arena, "switch.stone_north"));
        arena.Tick();

        var diagnosis = arena.Simulation.Diagnose(Quest);
        Assert.Equal(new[] { "o_southwest", "o_southeast" }, diagnosis.Waiting.Select(w => w.Id));
        Assert.Contains("turn the south-west Quiet Stone at (122.0, 38.0): switch.stone_southwest, in cell r_0_0:c_01_00", diagnosis.Waiting[0].SatisfiedBy);
        Assert.Contains("world.foldscar.stone_southwest_aligned is read in cell r_0_0:c_01_00 (the cell of location.foldscar)", diagnosis.Waiting[0].SatisfiedBy);
        Assert.Empty(diagnosis.Problems);
    }
}
