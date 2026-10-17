using System.Collections.Immutable;
using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Quests;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP6: the Wayhold and Long Survey chapters over the game's own content. An act a faction reacts to is logged when the character does
/// it, and moves standing only when the character tells a member (report-only, PJ07); a member's quest may pay standing; a quest may
/// wait on it; and nothing moves while the character is away.
/// </summary>
public class FactionTests
{
    private const string Sel = "npc.ashen_hollow.sel_arien";
    private const string Tavar = "npc.ashen_hollow.tavar_orr";
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Wayhold = "faction.ashen_hollow.wayhold";
    private const string Survey = "faction.ashen_hollow.long_survey";
    private const string Steadied = "world.foldscar.steadied";
    private const long Day = 57_600;
    private static readonly (string, double, double, string)[] NoCreatures = Array.Empty<(string, double, double, string)>();

    // FoldscarTests' ways round the Foldscar (content/regions/ashen_hollow.yaml).
    private static readonly (double X, double Z) AtSel = (71, 123);
    private static readonly (double X, double Z) AtNorth = (148.5, 78);
    private static readonly (double X, double Z)[] ToTheSouthWest = { (135, 60), (123.5, 38) };
    private static readonly (double X, double Z)[] ToTheSouthEast = { (140, 62), (160, 65), (187, 65), (194, 45), (182.5, 31) };
    private static readonly (double X, double Z)[] ToTheHeart = { (194, 45), (187, 65), (160, 65), (153, 50.2) };
    private static readonly (double X, double Z)[] ToTavar = { (150, 45), (146.2, 42.8) };
    private static readonly (double X, double Z)[] BackToSel = { (150, 45), (135, 70), (100, 100), (80, 118), AtSel };

    private static void Walk(Arena arena, params (double X, double Z)[] route)
    {
        foreach (var (x, z) in route)
            Assert.True(arena.WalkTo(x, z), $"never reached ({x}, {z})");
    }

    private static void SteadyTheHeart(Arena arena)
    {
        Walk(arena, AtNorth);
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "switch.stone_north")));
        Walk(arena, ToTheSouthWest);
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "switch.stone_southwest")));
        Walk(arena, ToTheSouthEast);
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "switch.stone_southeast")));
        Walk(arena, ToTheHeart);
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "switch.foldscar_heart")));
        arena.Tick();
    }

    private static (int Points, string Tier) Standing(Simulation simulation, string faction) =>
        simulation.Factions.Single(f => f.Id == faction) is var view ? (view.Points, view.Tier) : default;

    private static string Ledger(Simulation simulation) =>
        string.Join(";", simulation.Factions.Select(f => $"{f.Id}:{f.Points}:{f.Tier}")) + "|" +
        string.Join(";", simulation.Acts.Select(a => $"{a.Seq}:{a.Kind}:{a.Subject}:{a.Tick}:{string.Join(",", a.Known.Select(k => k.Knower + k.Delta))}"));

    [Fact]
    public void TheFoldSteadied_IsLogged_AndTheLongSurveyLearnsOfIt_OnlyWhenSelIsTold()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (135, 70), 90, NoCreatures);
        var recorded = arena.Record<ActRecorded>();
        var learned = arena.Record<FactionLearned>();
        var changed = arena.Record<ReputationChanged>();
        Assert.Equal(new[] { Survey, Wayhold }, arena.Simulation.Factions.Select(f => f.Id));
        Assert.Equal(new[] { Sel }, arena.Simulation.Factions.Single(f => f.Id == Survey).Members);
        Assert.Equal(new[] { "npc.ashen_hollow.kera_voss", Renn }, arena.Simulation.Factions.Single(f => f.Id == Wayhold).Members);

        SteadyTheHeart(arena);
        // The stones are switches nobody reacts to; the heart is the one act logged, and no faction knows of it yet.
        var act = Assert.Single(arena.Simulation.Acts);
        Assert.Equal((ActKinds.SwitchSet, Steadied), (act.Kind, act.Subject));
        Assert.Empty(act.Known);
        Assert.Single(recorded);
        Assert.All(arena.Simulation.Factions, f => Assert.Equal((0, "neutral"), (f.Points, f.Tier)));

        // A day away: nobody saw, so nobody learns, and nothing moves (the abstract advance is a no-op).
        string before = Ledger(arena.Simulation);
        arena.Simulation.Advance(Day);
        Assert.Equal(before, Ledger(arena.Simulation));

        Walk(arena, ToTavar);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Tavar)));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "found")));
        Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
        Walk(arena, BackToSel);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Sel)));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "tavar_back")));
        Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
        arena.Tick();

        Assert.Equal((100, "accepted"), Standing(arena.Simulation, Survey));
        Assert.Equal((0, "neutral"), Standing(arena.Simulation, Wayhold));     // the Wayhold was not told, and has no reaction to it
        var row = Assert.Single(Assert.Single(arena.Simulation.Acts).Known);
        Assert.Equal((Survey, KnowledgeSources.Reported, Sel, Identities.Identified, 100), (row.Knower, row.Source, row.Via, row.Identity, row.Delta));
        Assert.Equal((Survey, KnowledgeSources.Reported, Sel), (Assert.Single(learned).FactionId, learned[0].Source, learned[0].Via));
        var change = Assert.Single(changed);
        Assert.Equal((Survey, 0, 100, "neutral", "accepted", act.Seq), (change.FactionId, change.From, change.To, change.TierFrom, change.TierTo, change.ActSeq));
    }

    [Fact]
    public void AKillNoFactionReactsTo_IsNotLogged()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (120, 60), 0, new[] { (Arena.Wolf, 120.0, 62.0, "pack_hunter") });
        arena.Fight(arena.Creature(0), 800);
        Assert.False(arena.Creature(0).Alive);
        Assert.Empty(arena.Simulation.Acts);
        Assert.All(arena.Simulation.Factions, f => Assert.Equal(0, f.Points));
    }

    /// <summary>
    /// Renn's quest pays the Wayhold's regard, and a second waits on it: a <c>reputation</c> reward moves standing once, clamped, and a
    /// <c>faction_reputation</c> objective holds as soon as the tier is reached.
    /// </summary>
    [Fact]
    public void AMembersQuest_PaysStanding_AndAQuestCanWaitOnIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        static ObjectiveDefinition O(string id, ObjectiveCondition condition) => new(id, id + ".", condition, ImmutableArray<string>.Empty);
        var paid = new QuestDefinition("quest.test.wayhold_errand", "Errand", "Errand.", Renn, "o_wait", ImmutableArray.Create(O("o_wait", new WaitUntil(2))),
            ImmutableArray<ObjectiveCondition>.Empty, ImmutableArray.Create<QuestReward>(new ReputationReward(Wayhold, 150))) { OnNewGame = true };
        var trusted = new QuestDefinition("quest.test.wayhold_trust", "Trust", "Trust.", Renn, "o_accepted",
            ImmutableArray.Create(O("o_accepted", new FactionReputation(Wayhold, 1, 5))), ImmutableArray<ObjectiveCondition>.Empty,
            ImmutableArray<QuestReward>.Empty) { OnNewGame = true };
        var setup = session.Setup with
        {
            Quests = new QuestSetup(session.Setup.Quests.Quests.SetItem(paid.Id, paid).SetItem(trusted.Id, trusted)),
        };
        var arena = Arena.OpenCreatures(session, setup, (46, 128), 0, NoCreatures);
        var changed = arena.Record<ReputationChanged>();
        var rewards = arena.Record<RewardGranted>();

        Assert.Null(arena.Submit(new BeginStoryCommand(arena.Player)));
        arena.Tick();
        Assert.Equal(QuestStatus.Active, arena.Simulation.Quests.Single(q => q.Id == trusted.Id).Status);
        arena.Tick(5);

        Assert.Equal((150, "accepted"), Standing(arena.Simulation, Wayhold));
        Assert.Empty(arena.Simulation.Acts);                                    // a reward is no act
        Assert.Equal((Wayhold, 0, 150, paid.Id), (Assert.Single(changed).FactionId, changed[0].From, changed[0].To, changed[0].QuestId));
        Assert.Contains(rewards, r => r.Kind == "reputation" && r.Ref == Wayhold && r.Amount == 150);
        arena.Tick(2);
        Assert.Equal(QuestStatus.Completed, arena.Simulation.Quests.Single(q => q.Id == trusted.Id).Status);
    }

    /// <summary>T-22 for factions: nothing they hold moves while the character is away, so any split of an absence leaves them alike.</summary>
    [Fact]
    public void TheFactionAdvance_IsANoOp_AndChunkInvariant()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var paid = new QuestDefinition("quest.test.wayhold_errand", "Errand", "Errand.", Renn, "o_wait",
            ImmutableArray.Create(new ObjectiveDefinition("o_wait", "Wait.", new WaitUntil(1), ImmutableArray<string>.Empty)),
            ImmutableArray<ObjectiveCondition>.Empty, ImmutableArray.Create<QuestReward>(new ReputationReward(Wayhold, -120))) { OnNewGame = true };
        var setup = session.Setup with { Quests = new QuestSetup(session.Setup.Quests.Quests.SetItem(paid.Id, paid)) };
        Arena Started()
        {
            var arena = Arena.OpenCreatures(session, setup, (46, 128), 0, NoCreatures, seed: 7);
            Assert.Null(arena.Submit(new BeginStoryCommand(arena.Player)));
            arena.Tick(4);
            Assert.Equal((-120, "wary"), Standing(arena.Simulation, Wayhold));
            return arena;
        }

        var once = Started().Simulation;
        var pieces = Started().Simulation;
        string before = Ledger(once);
        Assert.Equal(before, Ledger(pieces));

        once.Advance(10 * Day + 17);
        pieces.Advance(3);
        pieces.Advance(Day);
        pieces.Advance(9 * Day + 14);

        Assert.Equal(before, Ledger(once));
        Assert.Equal(Ledger(once), Ledger(pieces));
        Assert.Equal(once.WorldTick, pieces.WorldTick);
    }
}
