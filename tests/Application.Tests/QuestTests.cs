using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// M5: quests over the game's own content - Iron Under Ash played through as the content bible's Quest 1, with no fight in it and
/// in the orders the bible tolerates; quest state across a save; and the quest debugger answering "what is this quest waiting on
/// right now?" for quests that are deliberately broken.
/// </summary>
public class QuestTests
{
    private const string IronUnderAsh = "quest.ashen_hollow.iron_under_ash";
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Ore = "item.material.iron_ore";
    private const string Haft = "item.material.ash_haft";
    private const string Spear = "item.weapon.march_spear";
    private const string BilletRecipe = "recipe.smithing.iron_billet";
    private const string SpearRecipe = "recipe.smithing.march_spear";

    // Where the character stands (content/regions/ashen_hollow.yaml; CraftingTests uses the same places).
    private static readonly (double X, double Z) AtKera = (60.3, 140.3);
    private static readonly (double X, double Z) AtHearth = (59.5, 143);
    private static readonly (double X, double Z) AtAnvil = (58.2, 141.4);
    private static readonly (double X, double Z) AtSeam = (28, 44.1);

    // Out of the smithy, down the road to the quarry mouth and the ramp past the overlook to the seam, and back.
    private static readonly (double X, double Z)[] ToTheSeam =
        { (54.5, 142), (51.8, 142), (51.8, 136), (58, 134), (64, 112), (63, 98), (60, 90), (54, 74), (46, 60), (36, 50), (34, 44), AtSeam };
    private static readonly (double X, double Z)[] BackToTheForge =
        { (34, 44), (36, 50), (46, 60), (54, 74), (60, 90), (63, 98), (64, 112), (58, 134), (51.8, 136), (51.8, 142), (54.5, 142) };

    private static Arena At(GameSession session, (double X, double Z) place, Func<PlayerRecord, PlayerRecord>? change = null, SimulationSetup? rules = null) =>
        Arena.OpenCreatures(session, rules ?? session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), change);

    private static InventoryEntry Stack(string defId, int count, int quality = Quality.Standard) =>
        new(EntityId.NewId(EntityKind.Item), defId, count) { Quality = quality };

    private static PlayerRecord Carrying(PlayerRecord r, params InventoryEntry[] extra) => r.WithInventory(r.Inventory.Concat(extra));

    private static QuestView Journal(Arena arena, string quest = IronUnderAsh) => arena.Simulation.Quests.Single(q => q.Id == quest);

    private static (string, ObjectiveStatus)[] Shown(Arena arena, string quest = IronUnderAsh) =>
        Journal(arena, quest).Objectives.Select(o => (o.Id, o.Status)).ToArray();

    private static int Carried(Arena arena, string defId) => arena.Simulation.Player.Inventory.Where(e => e.DefId == defId).Sum(e => e.Count);

    private static void Walk(Arena arena, params (double X, double Z)[] route)
    {
        foreach (var (x, z) in route)
            Assert.True(arena.WalkTo(x, z), $"never reached ({x}, {z})");
    }

    /// <summary>Ask Kera to teach the forge: the reply that starts Iron Under Ash.</summary>
    private static void AskKeraToTeach(Arena arena)
    {
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "teach")));
        Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
    }

    private static SimulationSetup WithQuests(GameSession session, params QuestDefinition[] extra) =>
        session.Setup with { Quests = new QuestSetup(extra.Aggregate(session.Setup.Quests.Quests, (all, q) => all.SetItem(q.Id, q))) };

    private static QuestDefinition Broken(string id, string title, params ObjectiveDefinition[] objectives) =>
        new(id, title, title + ".", null, objectives[0].Id, objectives.ToImmutableArray(), ImmutableArray<ObjectiveCondition>.Empty,
            ImmutableArray<QuestReward>.Empty);

    private static ObjectiveDefinition Objective(string id, string description, ObjectiveCondition condition, params string[] next) =>
        new(id, description, condition, next.ToImmutableArray());

    // ── Iron Under Ash ──────────────────────────────────────────────────────

    [Fact]
    public void AskingKeraToTeach_StartsIronUnderAsh_AndTheJournalShowsWhatIsNext()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtKera);
        var started = arena.Record<QuestStarted>();
        Assert.Empty(arena.Simulation.Quests);

        AskKeraToTeach(arena);
        Assert.Equal(new QuestStarted(IronUnderAsh, Kera, 0), Assert.Single(started));
        arena.Tick();

        var journal = Journal(arena);
        Assert.Equal(("Iron Under Ash", QuestStatus.Active), (journal.Title, journal.Status));
        // The lesson was heard, so speaking to Kera is done; the rest waits, and the journal shows nothing further ahead.
        Assert.Equal(new[] { ("o_speak", ObjectiveStatus.Satisfied), ("o_shelf", ObjectiveStatus.Active) }, Shown(arena));
        Assert.Equal("Reach Blackvein Cut, the old quarry south of the waystation.", journal.Objectives[1].Description);
        // Knowing the forge is part of the lesson; asking again starts nothing twice.
        Assert.True(ProgressionEngine.Knows(arena.Simulation.Player.Progression, SpearRecipe));
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
        Assert.Contains("where", arena.Simulation.Conversation!.Replies.Select(r => r.Id));   // she says where the iron is while it is needed
        Assert.DoesNotContain("teach", arena.Simulation.Conversation!.Replies.Select(r => r.Id));
        Assert.Single(started);
    }

    /// <summary>
    /// The content bible's Quest 1 end to end in the real world, and without a fight (bible §15: "There is no objective to kill a
    /// fixed number of enemies"): out of the forge to the iron shelf, a strike of the seam, back to the waystation, a billet at the
    /// hearth, the spear at the anvil, and the spear shown to Kera - then the rewards, once.
    /// </summary>
    [Fact]
    public void IronUnderAsh_PlaysEndToEnd_WithoutAFight_AndPaysOnce()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtKera, r => Carrying(r, Stack(Haft, 1)));
        var satisfied = arena.Record<ObjectiveSatisfied>();
        var completed = arena.Record<QuestCompleted>();
        var rewards = arena.Record<RewardGranted>();
        var hits = arena.Record<HitResolved>();

        AskKeraToTeach(arena);
        Walk(arena, (54.5, 142));
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "door.forge_shed")));
        Walk(arena, ToTheSeam.Skip(1).ToArray());
        Assert.Null(arena.Submit(new GatherCommand(arena.Player, arena.Simulation.Nodes.Single(n => n.Name == "iron_seam").Key)));
        arena.Tick();
        Assert.Equal(new[] { "o_speak", "o_shelf", "o_ore" }, satisfied.Select(s => s.ObjectiveId));
        Assert.Equal(("o_return", ObjectiveStatus.Active), Shown(arena)[^1]);

        Walk(arena, BackToTheForge);
        Assert.Equal(("o_billet", ObjectiveStatus.Active), Shown(arena)[^1]);
        Walk(arena, AtHearth);
        Assert.Null(arena.Submit(new CraftCommand(arena.Player, BilletRecipe)));
        arena.Tick();
        Walk(arena, AtAnvil);
        Assert.Null(arena.Submit(new CraftCommand(arena.Player, SpearRecipe)));
        arena.Tick();
        Assert.Equal(("o_show", ObjectiveStatus.Active), Shown(arena)[^1]);

        long xp = arena.Simulation.Player.Progression.LifetimeXp.GetValueOrDefault(XpSource.QuestObjective);
        long coin = arena.Simulation.Player.Currency;
        Walk(arena, AtKera);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
        string show = arena.Simulation.Conversation!.Replies.Select(r => r.Id).Single(r => r.StartsWith("show", StringComparison.Ordinal));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, show)));
        arena.Tick();

        Assert.Equal(new QuestCompleted(IronUnderAsh, "o_show", arena.Simulation.WorldTick), Assert.Single(completed));
        Assert.Equal(QuestStatus.Completed, Journal(arena).Status);
        Assert.Equal(new[] { ("xp", "120 XP"), ("currency", "25 coin"), ("relationship", $"{Kera} respect +5") }, rewards.Select(r => (r.Kind, r.What)));
        Assert.Equal(xp + 120, arena.Simulation.Player.Progression.LifetimeXp[XpSource.QuestObjective]);
        Assert.Equal(coin + 25, arena.Simulation.Player.Currency);
        Assert.Empty(hits);   // no blow was struck on the way

        arena.Tick(100);
        Assert.Single(completed);   // a completed quest is never evaluated again, so it pays once
        Assert.Equal(coin + 25, arena.Simulation.Player.Currency);
    }

    /// <summary>
    /// Bible §32: the quest tolerates the shelf visited before it was given and ore got before it was asked for. Whatever the world
    /// already satisfies is satisfied at once, in order, and the quest waits where the world does not.
    /// </summary>
    [Fact]
    public void AShelfVisitedAndOreCarriedBeforeTheQuest_CountAtOnce()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtKera, r => Carrying(r, Stack(Ore, 2))
            .WithDiscoveries(r.Discoveries.Append(new DiscoveryRecord("location.blackvein_cut", DiscoveryMethod.Visited, 0))));

        AskKeraToTeach(arena);
        arena.Tick();

        // Kera's forge is inside the waystation, so "return" holds too; making a billet cannot have happened yet.
        Assert.Equal(new[]
        {
            ("o_speak", ObjectiveStatus.Satisfied), ("o_shelf", ObjectiveStatus.Satisfied), ("o_ore", ObjectiveStatus.Satisfied),
            ("o_return", ObjectiveStatus.Satisfied), ("o_billet", ObjectiveStatus.Active),
        }, Shown(arena));
    }

    // Out of the smithy and down the quarry's west slope to the seam, never past the overlook (the audit's C-01 probe), and back.
    private static readonly (double X, double Z)[] DownTheWestSlope =
        { (51.8, 142), (51.8, 136), (34, 136), (27, 120), (27, 100), (27, 80), (27, 60), (24, 50), (24, 44.1), AtSeam };
    private static readonly (double X, double Z)[] UpTheWestSlope =
        { (24, 44.1), (24, 50), (27, 60), (27, 80), (27, 100), (27, 120), (34, 136), (51.8, 136), (51.8, 142), (54.5, 142) };

    /// <summary>Work the seam until it is worked out; what it gave.</summary>
    private static int WorkOut(Arena arena)
    {
        var seam = () => arena.Simulation.Nodes.Single(n => n.Name == "iron_seam");
        while (seam().Ready)
        {
            Assert.Null(arena.Submit(new GatherCommand(arena.Player, seam().Key)));
            arena.Tick();
        }
        return Carried(arena, Ore);
    }

    /// <summary>Smelt every lump carried, at the hearth.</summary>
    private static void SmeltAll(Arena arena)
    {
        Walk(arena, AtHearth);
        while (Carried(arena, Ore) > 0)
        {
            Assert.Null(arena.Submit(new CraftCommand(arena.Player, BilletRecipe)));
            arena.Tick();
        }
    }

    private static void ForgeAndShow(Arena arena)
    {
        Walk(arena, AtAnvil);
        Assert.Null(arena.Submit(new CraftCommand(arena.Player, SpearRecipe)));
        arena.Tick();
        Walk(arena, AtKera);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
        string show = arena.Simulation.Conversation!.Replies.Select(r => r.Id).Single(r => r.StartsWith("show", StringComparison.Ordinal));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, show)));
        arena.Tick();
    }

    /// <summary>
    /// The Phase-1 technical audit, C-01, with the game's own content: Blackvein Cut reached down its west slope rather than past the
    /// overlook, the seam worked out, every lump smelted - the quest finishes. Being in the quarry discovers the Cut.
    /// </summary>
    [Fact]
    public void IronUnderAsh_DownTheWestSlope_WithEveryLumpSmelted_StillCompletes()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtKera, r => Carrying(r, Stack(Haft, 1)));
        var completed = arena.Record<QuestCompleted>();

        AskKeraToTeach(arena);
        Walk(arena, (54.5, 142));
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "door.forge_shed")));
        Walk(arena, DownTheWestSlope);
        Assert.Contains(arena.Simulation.Player.Discoveries, d => d.LocationId == "location.blackvein_cut");   // no overlook needed
        Assert.InRange(WorkOut(arena), 3, 6);
        Walk(arena, UpTheWestSlope);
        SmeltAll(arena);
        Assert.Empty(arena.Simulation.Diagnose(IronUnderAsh).Problems);
        ForgeAndShow(arena);

        Assert.Equal(new QuestCompleted(IronUnderAsh, "o_show", arena.Simulation.WorldTick), Assert.Single(completed));
    }

    /// <summary>
    /// The audit's C-01 probe as it ran, kept as a regression: under M6's small discovery circle round the overlook, the west slope reaches
    /// the seam unseen, every lump is smelted, and only then is the Cut discovered - which stranded the quest on "obtain raw iron ore" for
    /// good. Ore made into a billet or a spear is ore obtained, and a billet or spear made ahead still counts, so it finishes.
    /// </summary>
    [Fact]
    public void IronUnderAsh_WithTheCutDiscoveredOnlyAfterEveryLumpIsSmelted_StillCompletes()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var layout = session.Setup.Layout;
        var overlookOnly = session.Setup with
        {
            Layout = layout with
            {
                Locations = layout.Locations.Select(l => l.Id == "location.blackvein_cut" ? l with { XMm = 54_000, ZMm = 74_000, DiscoveryRadiusMm = 20_000 } : l)
                    .ToImmutableArray(),
            },
        };
        var arena = At(session, AtKera, r => Carrying(r, Stack(Haft, 1)), overlookOnly);
        var completed = arena.Record<QuestCompleted>();

        AskKeraToTeach(arena);
        Walk(arena, (54.5, 142));
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "door.forge_shed")));
        Walk(arena, DownTheWestSlope);
        Assert.DoesNotContain(arena.Simulation.Player.Discoveries, d => d.LocationId == "location.blackvein_cut");
        int ore = WorkOut(arena);
        Walk(arena, UpTheWestSlope);
        SmeltAll(arena);
        Assert.Equal((0, ore), (Carried(arena, Ore), Carried(arena, "item.material.iron_ingot")));
        Assert.Equal(("o_shelf", ObjectiveStatus.Active), Shown(arena)[^1]);   // the Cut still unseen: every billet was made before it counted

        // Now to the Cut, past the overlook, and home: the billets carried are the ore obtained and the billet made.
        Walk(arena, (51.8, 142), (51.8, 136), (58, 134), (64, 112), (63, 98), (60, 90), (54, 74));
        arena.Tick();
        Assert.Empty(arena.Simulation.Diagnose(IronUnderAsh).Problems);
        Walk(arena, (60, 90), (63, 98), (64, 112), (58, 134), (51.8, 136), (51.8, 142), (54.5, 142));
        arena.Tick();
        Assert.Equal(new[] { ("o_ore", ObjectiveStatus.Satisfied), ("o_return", ObjectiveStatus.Satisfied), ("o_billet", ObjectiveStatus.Satisfied),
            ("o_spear", ObjectiveStatus.Active) }, Shown(arena)[^4..]);
        ForgeAndShow(arena);

        Assert.Equal(new QuestCompleted(IronUnderAsh, "o_show", arena.Simulation.WorldTick), Assert.Single(completed));
    }

    [Fact]
    public void QuestState_ContinuesAcrossASaveAndLoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtHearth, r => Carrying(r, Stack(Ore, 1))
            .WithDiscoveries(r.Discoveries.Append(new DiscoveryRecord("location.blackvein_cut", DiscoveryMethod.Visited, 0))));
        Walk(arena, AtKera);
        AskKeraToTeach(arena);
        arena.Tick();
        Assert.Equal(("o_billet", ObjectiveStatus.Active), Shown(arena)[^1]);

        new SaveStore(profile.Root).Save(SaveSlots.Manual("quest"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(),
            session.Content, arena.Simulation.WorldTick, 0));
        var loaded = Arena.Resume(session.Setup,
            new SaveStore(profile.Root).Load(SaveSlots.Manual("quest"), new LoadContext(session.Generator, session.Content, new Registry())));

        Assert.Equal(arena.Simulation.StateDigest(), loaded.Simulation.StateDigest());
        Assert.Equal(Shown(arena), Shown(loaded));

        // And it goes on from there: the billet made after the load is the one the quest was waiting for.
        Walk(loaded, AtHearth);
        Assert.Null(loaded.Submit(new CraftCommand(loaded.Player, BilletRecipe)));
        loaded.Tick();
        Assert.Equal(new[] { ("o_billet", ObjectiveStatus.Satisfied), ("o_spear", ObjectiveStatus.Active) }, Shown(loaded)[^2..]);
    }

    // ── the quest debugger ──────────────────────────────────────────────────

    [Fact]
    public void TheDebugger_SaysWhatIronUnderAshIsWaitingOn_AndHowItStarts()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtKera);

        var before = arena.Simulation.Diagnose(IronUnderAsh);
        Assert.Equal("not_started", before.Status);
        Assert.StartsWith("Its start: ", before.Answer);
        Assert.Contains("Kera Voss's reply 'teach' at the line 'greet' (dialogue.ashen_hollow.kera_voss): offered now", before.Answer);

        AskKeraToTeach(arena);
        arena.Tick(3);
        var now = arena.Simulation.Diagnose(IronUnderAsh);

        Assert.Equal("active", now.Status);
        Assert.Equal("Waiting on o_shelf (Reach Blackvein Cut, the old quarry south of the waystation.): location.blackvein_cut discovered = no, wanted yes.", now.Answer);
        var waiting = Assert.Single(now.Waiting);
        Assert.Contains(waiting.SatisfiedBy, w => w.StartsWith("walk within 38.0 m of location.blackvein_cut (38.0, 48.0); now ", StringComparison.Ordinal));
        Assert.Empty(now.Problems);
        // The trace: the start, the evaluation that moved it, and the evaluations since, collapsed because nothing changed.
        Assert.Contains(now.Trace, t => t.Lines.Contains("-> o_speak satisfied"));
        Assert.Contains("o_shelf: location.blackvein_cut discovered = no (wanted yes) waiting", now.Trace[^1].Lines);
        Assert.True(now.Trace[^1].Evaluations >= 2);
    }

    /// <summary>Deliberately broken 1: a quest wants more ore than the world holds. The seam is finite, and nothing else gives ore.</summary>
    [Fact]
    public void TheDebugger_ExplainsAQuestTheWorldCanNoLongerSatisfy()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var greedy = Broken("quest.test.greedy", "The Greedy Smith", Objective("o_ore", "Bring nine lumps of ore.", new AcquireItem(Ore, 9, Quality.Crude)));
        var arena = At(session, AtSeam, r => r.WithQuests(new[] { QuestRules.Start(greedy, 0) }), WithQuests(session, greedy));

        var seam = () => arena.Simulation.Nodes.Single(n => n.Name == "iron_seam");
        while (seam().Ready)
            Assert.Null(arena.Submit(new GatherCommand(arena.Player, seam().Key)));
        arena.Tick();
        var diagnosis = arena.Simulation.Diagnose(greedy.Id);

        var term = Assert.Single(Assert.Single(diagnosis.Waiting).Terms);
        Assert.Equal(("item.material.iron_ore carried", Carried(arena, Ore).ToString(), ">= 9", false), (term.Label, term.Value, term.Wanted, term.Holds));
        Assert.Contains(diagnosis.Waiting[0].SatisfiedBy, w => w.Contains("iron_seam", StringComparison.Ordinal) && w.EndsWith("worked out for good", StringComparison.Ordinal));
        Assert.Contains("nothing in this world can supply item.material.iron_ore now", diagnosis.Problems);
    }

    /// <summary>
    /// Deliberately broken 2: a quest waits for Kera's praise, which only a fine spear earns, and the character's spear is not fine.
    /// The debugger names the reply that would reach the line and the condition stopping it; the fix - a fine spear - completes it.
    /// </summary>
    [Fact]
    public void TheDebugger_ExplainsAConversationThatCannotReachItsLine_AndTheFixCompletesIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var praise = Broken("quest.test.praise", "Worth Praising",
            Objective("o_praise", "Earn Kera's praise.", new TalkTo(Kera, "dialogue.ashen_hollow.kera_voss", ImmutableArray.Create("praised"))));
        var rules = WithQuests(session, praise);
        PlayerRecord Holding(PlayerRecord r, int quality) =>
            Carrying(r, Stack(Spear, 1, quality)).WithQuests(new[] { QuestRules.Start(praise, 0) });

        var plain = At(session, AtKera, r => Holding(r, Quality.Standard), rules);
        Assert.Null(plain.Submit(new TalkCommand(plain.Player, Kera)));   // greeted, so her other lines open
        Assert.Null(plain.Submit(new LeaveCommand(plain.Player)));
        plain.Tick();
        var stuck = plain.Simulation.Diagnose(praise.Id);
        string transcript = stuck.ToText();

        Assert.Equal("Waiting on o_praise (Earn Kera's praise.): heard 'praised' (dialogue.ashen_hollow.kera_voss) = no, wanted yes.", stuck.Answer);
        Assert.Contains(stuck.Waiting[0].SatisfiedBy, w => w == "'praised' is reached by Kera Voss's reply 'show_fine' at the line 'again' " +
            "(dialogue.ashen_hollow.kera_voss): not offered now - needs 1 item.weapon.march_spear of quality >= 1 carried; 0 are");

        // The fix the debugger points at: a fine spear.
        var fine = At(session, AtKera, r => Holding(r, Quality.Fine), rules);
        Assert.Null(fine.Submit(new TalkCommand(fine.Player, Kera)));
        Assert.Null(fine.Submit(new LeaveCommand(fine.Player)));
        Assert.Null(fine.Submit(new TalkCommand(fine.Player, Kera)));   // past the greeting, to the line the reply is at
        Assert.Null(fine.Submit(new ChooseCommand(fine.Player, "show_fine")));
        fine.Tick();
        var done = fine.Simulation.Diagnose(praise.Id);
        Assert.Equal($"Nothing: completed at tick {fine.Simulation.WorldTick} by o_praise.", done.Answer);

        if (Environment.GetEnvironmentVariable("UNNAMED_QUEST_TRANSCRIPT") is { Length: > 0 } path)
            File.WriteAllText(path, transcript + "\n--- after the fix (a fine spear shown) ---\n" + done.ToText());
    }

    /// <summary>Deliberately broken 3: a timed objective nobody could meet. The debugger says why it failed, and the trace shows the wait.</summary>
    [Fact]
    public void TheDebugger_ExplainsAQuestThatRanOutOfTime()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var race = Broken("quest.test.race", "The Race",
            Objective("o_run", "Reach Blackvein Cut before the fire dies.", new ExploreLocation("location.blackvein_cut", 10_000)) with
            {
                TimeLimitTicks = 40, OnFail = QuestRules.FailQuest,
            });
        var arena = At(session, AtKera, r => r.WithQuests(new[] { QuestRules.Start(race, 0) }), WithQuests(session, race));
        var failed = arena.Record<QuestFailed>();

        arena.Tick(20);
        var halfway = arena.Simulation.Diagnose(race.Id);
        Assert.Equal("1 s left before it fails to fail_quest", Assert.Single(halfway.Waiting).TimeLeft);

        arena.Tick(30);
        Assert.Equal(new QuestFailed(race.Id, "o_run", 40), Assert.Single(failed));
        var diagnosis = arena.Simulation.Diagnose(race.Id);
        Assert.Equal("failed", diagnosis.Status);
        Assert.Equal("Nothing: failed at tick 40 - o_run (Reach Blackvein Cut before the fire dies.) was not done within its time limit of 2 s.",
            diagnosis.Answer);
        var waited = diagnosis.Trace.Single(t => t.Evaluations > 1);   // forty evaluations that all said the same
        Assert.Contains(waited.Lines, l => l.StartsWith("o_run: distance to location.blackvein_cut = ", StringComparison.Ordinal) && l.EndsWith("(wanted <= 10.0 m) waiting", StringComparison.Ordinal));
        Assert.Equal(new[] { "-> o_run failed (time limit)", "-> quest failed by o_run" }, diagnosis.Trace[^1].Lines.TakeLast(2));
    }

    /// <summary>
    /// A shape the lint refuses - a join on both alternatives of one branch - still diagnosed, because a save made under older content
    /// can hold one: the branch taken closed a prerequisite, so the join can never become active and the quest is stalled.
    /// </summary>
    [Fact]
    public void TheDebugger_FindsAJoinThatCanNeverHappen()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var stalled = Broken("quest.test.stalled", "Two Roads",
            Objective("o_start", "Stand in the waystation.", new ExploreLocation("location.outpost", 30_000), "o_left", "o_right") with { FirstBranch = true },
            Objective("o_left", "Carry the primer.", new AcquireItem("item.tome.resonance_primer", 1, Quality.Crude), "o_end"),
            Objective("o_right", "Stand in the waystation still.", new ExploreLocation("location.outpost", 30_000), "o_end"),
            Objective("o_end", "Both roads walked.", new WaitUntil(0)) with { AllOf = ImmutableArray.Create("o_left", "o_right") });
        var arena = At(session, AtKera, r => r.WithQuests(new[] { QuestRules.Start(stalled, 0) }), WithQuests(session, stalled));

        arena.Tick();
        var diagnosis = arena.Simulation.Diagnose(stalled.Id);

        Assert.Equal("Nothing can move it: no objective is active.", diagnosis.Answer);
        Assert.Contains("o_end can never become active: it waits on o_left, which was closed at tick 1", diagnosis.Problems);
        Assert.Contains("no objective is active and the quest is not finished: it is stalled", diagnosis.Problems);
        // The journal never shows a branch not taken.
        Assert.Equal(new[] { ("o_start", ObjectiveStatus.Satisfied), ("o_right", ObjectiveStatus.Satisfied) }, Shown(arena, stalled.Id));
    }

    /// <summary>
    /// F4 (M7 design §2.18, §5.7.1): a reply gated by standing or by the act log is described in words - the faction's tier and points,
    /// the act the log lacks - never by the condition's record.
    /// </summary>
    [Fact]
    public void DescribeCondition_NamesStandingAndActDone()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var notes = Broken("quest.test.notes", "The Notes",
            Objective("o_notes", "Hear Sel's notes.", new TalkTo("npc.ashen_hollow.sel_arien", "dialogue.ashen_hollow.sel_arien", ImmutableArray.Create("notes"))));
        var armour = Broken("quest.test.armour", "The Armour",
            Objective("o_armour", "Tell Kera of the armour.", new TalkTo(Kera, "dialogue.ashen_hollow.kera_voss", ImmutableArray.Create("armour_down"))));
        var arena = At(session, AtKera, r => r.WithQuests(new[] { QuestRules.Start(notes, 0), QuestRules.Start(armour, 0) }), WithQuests(session, notes, armour));

        var standing = Assert.Single(arena.Simulation.Diagnose(notes.Id).Waiting).SatisfiedBy;
        Assert.Contains(standing, w => w == "'notes' is reached by Sel Arien's reply 'notes' at the line 'again' (dialogue.ashen_hollow.sel_arien): " +
            "not offered now - needs the Survey at accepted or better; it is neutral (0)");
        var act = Assert.Single(arena.Simulation.Diagnose(armour.Id).Waiting).SatisfiedBy;
        Assert.Contains(act, w => w == "'armour_down' is reached by Kera Voss's reply 'armour' at the line 'again' (dialogue.ashen_hollow.kera_voss): " +
            "not offered now - needs the character to have killed creature.construct.animated_armour; the act log holds none");
        Assert.DoesNotContain(standing.Concat(act), w => w.Contains("Condition {", StringComparison.Ordinal));
    }
}
