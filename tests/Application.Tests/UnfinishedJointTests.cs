using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Quests;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The Unfinished Joint (owner, 2026-10-25; HERITAGE_DESIGN.md §3.2), the Kal heritage questline every new Kal starts: Kera reads the
/// Finishing Tool's grip, Berrin's book says where the hold's stone went, the joint is finished in new iron or the stone is traced east,
/// and Kera marks the tool. No step takes the tool, and Kera re-issues a lost one.
/// </summary>
public class UnfinishedJointTests
{
    private const string Tool = "item.heritage.finishing_tool";
    private const string Quest = "quest.heritage.unfinished_joint";
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Berrin = "npc.cairnwold.berrin_garth";
    private const string Ingot = "item.material.iron_ingot";
    private static readonly (double X, double Z) AtKera = (60.3, 140.3);
    private static readonly (double X, double Z) AtBerrin = (959.0, 644.0);   // region metres

    private static PlayerRecord Kal(PlayerRecord r, bool tool = true, int ingots = 0)
    {
        var items = r.Inventory.Where(e => e.DefId != Tool).ToList();
        if (tool) items.Add(new InventoryEntry(EntityId.NewId(EntityKind.Item), Tool, 1));
        if (ingots > 0) items.Add(new InventoryEntry(EntityId.NewId(EntityKind.Item), Ingot, ingots));
        return (r with { Identity = r.Identity with { PeopleId = "people.kal" } }).WithInventory(items);
    }

    /// <summary>The Unfinished Joint as it stands after these objectives; the last of them (or the last two, at the branch) open.</summary>
    private static QuestState At(string[] reached, params string[] open) =>
        new(Quest, QuestStatus.Active, 0, null, null, reached.Select(id => new ObjectiveState(id, ObjectiveStatus.Satisfied, 0, 0, 0))
            .Concat(open.Select(id => new ObjectiveState(id, ObjectiveStatus.Active, 0, null, 0))).ToImmutableArray());

    private static Arena AtKeras(GameSession session, Func<PlayerRecord, PlayerRecord> change) =>
        Arena.OpenCreatures(session, session.Setup, AtKera, 0, Array.Empty<(string, double, double, string)>(), change);

    private static string[] Talk(Arena arena, string npc)
    {
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, npc)));
        return arena.Simulation.Conversation!.Replies.Select(r => r.Id).ToArray();
    }

    private static string Open(Arena arena) =>
        string.Join(",", arena.Simulation.Quests.Single(q => q.Id == Quest).Objectives.Where(o => o.Status == ObjectiveStatus.Active).Select(o => o.Id));

    [Fact]
    public void ANewKal_StartsTheUnfinishedJoint_CarryingTheTool()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame(new CharacterSpec("Tovra Keld", Pronouns.She, "people.kal", "body.kal_f.average", "call.wayfarer"), seed: 42);

        var quest = simulation.Quests.Single(q => q.Id == Quest);
        Assert.Equal(QuestStatus.Active, quest.Status);
        Assert.Equal("The Unfinished Joint", quest.Title);
        Assert.Single(simulation.Player.Inventory, e => e.DefId == Tool);
    }

    [Fact]
    public void Kera_ReadsOttrasGrip_AndTheHoldIsNext()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AtKeras(session, r => Kal(r).WithQuests(new[] { At(Array.Empty<string>(), "o_grip") }));

        Assert.Contains("grip", Talk(arena, Kera));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "grip")));
        Assert.Contains("Ottra Brannock", arena.Simulation.Conversation!.Text);
        arena.Tick(2);

        Assert.Equal("o_hold", Open(arena));
        Assert.Single(arena.Simulation.Player.Inventory, e => e.DefId == Tool);
    }

    [Fact]
    public void AKalWhoLostTheTool_GetsAnotherFromKera_AndOnlyThen()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        Assert.DoesNotContain("reissue", Talk(AtKeras(session, r => Kal(r)), Kera));

        var lost = AtKeras(session, r => Kal(r, tool: false).WithQuests(new[] { At(Array.Empty<string>(), "o_grip") }));
        var replies = Talk(lost, Kera);
        Assert.Contains("reissue", replies);
        Assert.DoesNotContain("grip", replies);   // she reads the grip of a tool in hand
        Assert.Null(lost.Submit(new ChooseCommand(lost.Player, "reissue")));
        Assert.Null(lost.Submit(new ChooseCommand(lost.Player, "back")));
        Assert.Single(lost.Simulation.Player.Inventory, e => e.DefId == Tool);
        Assert.Contains("grip", lost.Simulation.Conversation!.Replies.Select(r => r.Id));   // and the quest goes on

        var veth = AtKeras(session, r => Kal(r, tool: false) with { Identity = r.Identity with { PeopleId = "people.veth" } });
        Assert.DoesNotContain("reissue", Talk(veth, Kera));
    }

    [Fact]
    public void Berrin_TellsAKalWhereTheStoneWent_AndOnlyAKal()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var quest = At(new[] { "o_grip", "o_hold" }, "o_why");
        var arena = Arena.OpenCreatures(session, session.Setup, AtBerrin, 0, Array.Empty<(string, double, double, string)>(),
            r => Kal(r).WithQuests(new[] { quest }), origin: Harness.Region);

        Assert.Contains("crag_stone", Talk(arena, Berrin));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "crag_stone")));
        Assert.Contains("Ondrek", arena.Simulation.Conversation!.Text);
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "back")));
        arena.Tick(2);
        Assert.Equal("o_finish,o_trace", Open(arena));
        // East is a rumour in the journal, never an objective (owner K-1, 2026-10-26).
        Assert.Contains(arena.Simulation.Lore, p => p.EntryId == "lore.brannock_stone");
        Assert.Equal(UNNAMED.Domain.Knowledge.LoreCategory.Rumours, session.Setup.Lore["lore.brannock_stone"].Category);
        Assert.DoesNotContain(arena.Simulation.Quests.SelectMany(q => q.Objectives), o => o.Description.Contains("Ondrek") && o.Id is not ("o_trace"));

        var veth = Arena.OpenCreatures(session, session.Setup, AtBerrin, 0, Array.Empty<(string, double, double, string)>(),
            r => r.WithQuests(new[] { quest }), origin: Harness.Region);
        Assert.DoesNotContain("crag_stone", Talk(veth, Berrin));
    }

    [Fact]
    public void WithoutTwoIngots_TheJointCannotBeFinishedYet_ButTheStoneCanBeTraced()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AtKeras(session, r => Kal(r, ingots: 1).WithQuests(new[] { At(new[] { "o_grip", "o_hold", "o_why" }, "o_finish", "o_trace") }));

        Talk(arena, Kera);
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "joint")));
        Assert.Equal(new[] { "finish_later", "trace" }, arena.Simulation.Conversation!.Replies.Select(r => r.Id));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "trace")));
        Assert.Equal("stone_traced", arena.Simulation.Conversation!.NodeId);
        arena.Tick(2);

        Assert.Equal("o_marked", Open(arena));
        Assert.Single(arena.Simulation.Player.Inventory, e => e.DefId == Ingot);
    }

    [Fact]
    public void TwoIngots_FinishTheJoint_TheKalWay()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AtKeras(session, r => Kal(r, ingots: 2).WithQuests(new[] { At(new[] { "o_grip", "o_hold", "o_why" }, "o_finish", "o_trace") }));

        Talk(arena, Kera);
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "joint")));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "finish")));
        Assert.Equal("joint_finished", arena.Simulation.Conversation!.NodeId);
        arena.Tick(2);

        Assert.Equal("o_marked", Open(arena));
        Assert.DoesNotContain(arena.Simulation.Player.Inventory, e => e.DefId == Ingot);
        Assert.DoesNotContain("joint", Talk(arena, Kera));   // the joint is settled once
    }

    [Theory]
    [InlineData("o_finish")]
    [InlineData("o_trace")]
    public void Kera_MarksTheTool_AndTheCharacterIsNamedInTheHold(string settled)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AtKeras(session, r => Kal(r).WithQuests(new[] { At(new[] { "o_grip", "o_hold", "o_why", settled }, "o_marked") }));

        Assert.Contains("mark", Talk(arena, Kera));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "mark")));
        Assert.Equal("maker_mark", arena.Simulation.Conversation!.NodeId);
        arena.Tick(2);

        var done = arena.Simulation.Quests.Single(q => q.Id == Quest);
        Assert.Equal(QuestStatus.Completed, done.Status);
        Assert.DoesNotContain(done.Objectives, o => o.Status == ObjectiveStatus.Active);   // the trace branch too ends here, nothing left open east
        Assert.Equal(new[] { "Named in the Brannock hold" }, arena.Simulation.Titles);
        Assert.Single(arena.Simulation.Player.Inventory, e => e.DefId == Tool);   // the tool stays the character's
    }
}
