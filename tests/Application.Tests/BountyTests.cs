using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The standing work (repeatable quests, <c>repeat_policy</c>): taken from its giver, paid on delivery, and posted again a world day
/// after it was done - never sooner. Every bounty is played here once, from its giver's own lines.
/// </summary>
public class BountyTests
{
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Sel = "npc.ashen_hollow.sel_arien";
    private const string Durn = "npc.cairnwold.durn_skell";
    private const string Tam = "npc.cairnwold.tam_crowden";
    private const string Timber = "item.material.timber";
    private const string Billet = "item.material.iron_ingot";
    private const string Roof = "quest.bounty.crossing_timber";

    // Where the character stands to speak to each giver: the hollow's three in the hollow's metres (as NpcTests), Cairnwold's two in the
    // region's (as CairnwoldQuestTests).
    private static readonly (double X, double Z) AtRenn = (48, 126.2);
    private static readonly (double X, double Z) AtKera = (60.3, 140.3);
    private static readonly (double X, double Z) AtSel = (71, 123);
    private static readonly (double X, double Z) AtDurn = (980.7, 674.4);
    private static readonly (double X, double Z) AtTam = (941, 679.8);

    // Durn's hearth, through the smithy door, and back out to him (CairnwoldQuestTests' own route).
    private static readonly (double X, double Z)[] ToTheSmithyDoor = { (982, 675.8), (982, 677.5) };
    private static readonly (double X, double Z)[] ToTheHearth = { (982.2, 679.8), (983.5, 683.5) };
    private static readonly (double X, double Z)[] OutToDurn = { (982.2, 679.8), (982, 677.5), (982, 675.8), AtDurn };

    private static string? Say(Arena arena, string reply) => arena.Submit(new ChooseCommand(arena.Player, reply));

    private static string[] Replies(Arena arena) => arena.Simulation.Conversation!.Replies.Select(r => r.Id).ToArray();

    private static QuestStatus? Status(Arena arena, string quest) => arena.Simulation.Quests.SingleOrDefault(q => q.Id == quest)?.Status;

    private static int Carried(Arena arena, string defId) => arena.Simulation.Player.Inventory.Where(e => e.DefId == defId).Sum(e => e.Count);

    private static long Coin(Arena arena) => arena.Simulation.CaptureRecord().Currency;

    private static Func<PlayerRecord, PlayerRecord> Carrying(params (string DefId, int Count)[] stacks) =>
        r => r.WithInventory(r.Inventory.Concat(stacks.Select(s => Arena.Stack(s.DefId, s.Count))));

    private static Arena Hollow(GameSession session, (double X, double Z) place, Func<PlayerRecord, PlayerRecord>? change = null,
        params (string DefId, double X, double Z, string Role)[] creatures) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, creatures, change);

    private static Arena Cairnwold(GameSession session, (double X, double Z) place, Func<PlayerRecord, PlayerRecord>? change = null,
        params (string DefId, double X, double Z, string Role)[] creatures) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, creatures, change, origin: Harness.Region);

    private static void Walk(Arena arena, params (double X, double Z)[] route)
    {
        foreach (var (x, z) in route)
            Assert.True(arena.WalkTo(x, z), $"never reached ({x}, {z}): stopped at {arena.Simulation.Player.Body}");
    }

    /// <summary>Open a giver's conversation past their once-only greeting, and choose the line that leads to their work.</summary>
    private static void ToTheWork(Arena arena, string npc, string work)
    {
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, npc)));
        if (arena.Simulation.Conversation!.NodeId == "greet")
        {
            Assert.Null(Say(arena, "leave"));
            Assert.Null(arena.Submit(new TalkCommand(arena.Player, npc)));
        }
        Assert.Null(Say(arena, work));
    }

    private static void Leave(Arena arena)
    {
        if (arena.Simulation.Conversation is not null)
            Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
        arena.Tick();
    }

    private static void Kill(Arena arena, CreatureView creature)
    {
        arena.Fight(creature, 1_200);
        Assert.False(arena.Simulation.Creatures.Single(c => c.Key == creature.Key).Alive, $"{creature.DefId} at {creature.Body} still stands");
        arena.Tick(2);
    }

    /// <summary>
    /// A delivery bounty from take to pay: offered while available, not offered again once taken, the hand-over pays the quest's rewards
    /// and takes the goods, and the work is not posted again the same day.
    /// </summary>
    private static void Deliver(Arena arena, string npc, string work, string quest, string take, string give, string item, int count, long pay)
    {
        var paid = arena.Record<RewardGranted>();
        long coin = Coin(arena);
        int carried = Carried(arena, item);

        ToTheWork(arena, npc, work);
        Assert.Contains(take, Replies(arena));
        Assert.DoesNotContain(give, Replies(arena));   // not before it is taken
        Assert.Null(Say(arena, take));
        Assert.Equal(QuestStatus.Active, Status(arena, quest));
        Assert.Null(Say(arena, "back"));
        Assert.Null(Say(arena, work));
        Assert.DoesNotContain(take, Replies(arena));
        Assert.Null(Say(arena, give));
        arena.Tick(2);

        Assert.Equal(QuestStatus.Completed, Status(arena, quest));
        Assert.Equal(new[] { "xp", "currency" }, paid.Where(p => p.QuestId == quest).Select(p => p.Kind));
        Assert.Equal(coin + pay, Coin(arena));
        Assert.Equal(carried - count, Carried(arena, item));
        Assert.Null(Say(arena, "back"));
        Assert.Null(Say(arena, work));
        Assert.DoesNotContain(take, Replies(arena));   // done today
        Leave(arena);
    }

    [Fact]
    public void RennsTimberWork_IsPaidOnDelivery_AndPostedAgainADayLater()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // This checks the bounty's daily cooldown, independently of Renn walking to bed.
        var rules=session.Setup with { NpcLife=NpcLifeSetup.Empty };
        var arena = Arena.OpenCreatures(session, rules, AtRenn, 0, Array.Empty<(string, double, double, string)>(), r =>
            new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed,
                r.Inventory.Append(new InventoryEntry(EntityId.NewId(EntityKind.Item), Timber, 12)), r.Progression, r.FacingMdeg, r.Discoveries,
                r.Equipment, r.Currency, r.Effects, r.Relationships, r.Conversations));

        Deliver(arena, Renn, "notices", Roof, "take_timber", "give_timber", Timber, 6, pay: 8);

        arena.Tick((int)session.Setup.Quests.Quests[Roof].RepeatAfterTicks!.Value);
        ToTheWork(arena, Renn, "notices");
        Assert.Contains("take_timber", Replies(arena));
        Assert.Null(Say(arena, "take_timber"));
        Assert.Equal(QuestStatus.Active, Status(arena, Roof));
    }

    [Fact]
    public void KerasOreRun_IsPaidForTwoLumps()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Hollow(session, AtKera, Carrying(("item.material.iron_ore", 3)));
        Deliver(arena, Kera, "work", "quest.bounty.shelf_iron", "take_ore", "give_ore", "item.material.iron_ore", 2, pay: 10);
    }

    [Fact]
    public void SelsWoundmoss_IsPaidForTwoHandfuls()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Hollow(session, AtSel, Carrying(("item.material.herb_woundmoss", 2)));
        Deliver(arena, Sel, "survey", "quest.bounty.survey_herbs", "take_moss", "give_moss", "item.material.herb_woundmoss", 2, pay: 6);
    }

    [Fact]
    public void SelsBoneleaf_IsPaidForTwoLeaves()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Hollow(session, AtSel, Carrying(("item.material.boneleaf", 2)));
        Deliver(arena, Sel, "survey", "quest.bounty.wrong_leaves", "take_boneleaf", "give_boneleaf", "item.material.boneleaf", 2, pay: 12);
    }

    [Fact]
    public void TamsPot_IsPaidForTwoPortionsOfMeat()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Cairnwold(session, AtTam, Carrying(("item.material.raw_meat", 2)));
        Deliver(arena, Tam, "work", "quest.bounty.lodge_supplies", "take_pot", "give_meat", "item.material.raw_meat", 2, pay: 5);
    }

    [Fact]
    public void TamsWinterHides_ArePaidForTwoSmallHides()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Cairnwold(session, AtTam, Carrying(("item.material.small_hide", 2)));
        Deliver(arena, Tam, "work", "quest.bounty.winter_hides", "take_hides", "give_hides", "item.material.small_hide", 2, pay: 12);
    }

    /// <summary>
    /// Durn wants a billet smelted while the work is taken. Before it is smelted, a billet from the pack is not asked for - it would be
    /// taken and count for nothing; once one is smelted, the hand-over pays.
    /// </summary>
    [Fact]
    public void DurnsBillet_MustBeSmeltedFirst_AndAStockBilletBeforeThenIsNotTaken()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const string quest = "quest.bounty.moor_billets";
        var arena = Cairnwold(session, AtDurn, Carrying((Billet, 1), ("item.material.iron_ore", 1)));
        var paid = arena.Record<RewardGranted>();

        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Durn)));
        Assert.Null(Say(arena, "teach"));   // the billet recipe
        Assert.Null(Say(arena, "thanks"));
        Assert.Null(Say(arena, "work"));
        Assert.Null(Say(arena, "take_billet"));
        Assert.Equal(QuestStatus.Active, Status(arena, quest));
        Assert.Null(Say(arena, "back"));
        Assert.Null(Say(arena, "work"));
        Assert.DoesNotContain("give_billet", Replies(arena));   // a stock billet, carried, but none smelted yet
        Assert.NotNull(Say(arena, "give_billet"));
        Assert.Equal(1, Carried(arena, Billet));
        Leave(arena);

        Walk(arena, ToTheSmithyDoor);
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "door.smithy")));
        arena.Tick();
        Walk(arena, ToTheHearth);
        Assert.Null(arena.Submit(new CraftCommand(arena.Player, "recipe.smithing.iron_billet")));
        arena.Tick(2);
        Assert.Equal(2, Carried(arena, Billet));
        Walk(arena, OutToDurn);

        long coin = Coin(arena);
        ToTheWork(arena, Durn, "work");
        Assert.Null(Say(arena, "give_billet"));
        arena.Tick(2);
        Assert.Equal(QuestStatus.Completed, Status(arena, quest));
        Assert.Equal(new[] { "xp", "currency" }, paid.Where(p => p.QuestId == quest).Select(p => p.Kind));
        Assert.Equal(coin + 10, Coin(arena));
        Assert.Equal(1, Carried(arena, Billet));
    }

    /// <summary>Renn's wolf cull pays the moment the third wolf falls - and the spear that does it trains its own skill.</summary>
    [Fact]
    public void RennsWolfBounty_PaysAtTheThirdKill()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const string quest = "quest.bounty.road_wolves";
        const string spear = "item.weapon.march_spear";

        var atRenn = Hollow(session, AtRenn);
        ToTheWork(atRenn, Renn, "notices");
        Assert.Null(Say(atRenn, "take_wolves"));
        Assert.Equal(QuestStatus.Active, Status(atRenn, quest));

        // Taken, out on the open ground south-east of the yard (CombatTests' own), with three lone wolves each holding its post apart.
        var taken = atRenn.Simulation.CaptureRecord().Quests;
        var arena = Hollow(session, (120, 60), r => Carrying((spear, 1))(r).WithQuests(taken),
            (Arena.Wolf, 120, 64, "sentinel"), (Arena.Wolf, 120, 84, "sentinel"), (Arena.Wolf, 120, 40, "sentinel"));
        var paid = arena.Record<RewardGranted>();
        long coin = Coin(arena);
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, arena.Simulation.Player.Inventory.Single(e => e.DefId == spear).ItemId)));
        arena.Tick();

        var wolves = arena.Simulation.Creatures.OrderBy(c => c.Key, StringComparer.Ordinal).ToList();
        for (int i = 0; i < wolves.Count; i++)
        {
            Kill(arena, wolves[i]);
            Assert.Equal(i < 2 ? QuestStatus.Active : QuestStatus.Completed, Status(arena, quest));
        }
        Assert.Equal(new[] { "xp", "currency" }, paid.Where(p => p.QuestId == quest).Select(p => p.Kind));
        Assert.Equal(coin + 15, Coin(arena));
        Assert.True(ProgressionEngine.SkillLevel(arena.Simulation.Player.Progression, "skill.polearm") >= 1);
    }

    /// <summary>Durn's night work: any bone-walker husk put down while it is taken.</summary>
    [Fact]
    public void DurnsCragWalker_PaysForAHuskPutDown()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const string quest = "quest.bounty.crag_walker";
        var arena = Cairnwold(session, AtDurn, null, ("creature.undead.bone_walker_husk", 980.8, 655, "sentinel"));
        var paid = arena.Record<RewardGranted>();
        long coin = Coin(arena);

        ToTheWork(arena, Durn, "work");
        Assert.Null(Say(arena, "take_walker"));
        Assert.Equal(QuestStatus.Active, Status(arena, quest));
        Leave(arena);

        Kill(arena, arena.Creature());
        Assert.Equal(QuestStatus.Completed, Status(arena, quest));
        Assert.Equal(new[] { "xp", "currency" }, paid.Where(p => p.QuestId == quest).Select(p => p.Kind));
        Assert.Equal(coin + 20, Coin(arena));
    }
}
