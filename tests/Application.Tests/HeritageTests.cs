using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Quests;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Heritage (owner, 2026-10-24; HERITAGE_DESIGN.md): a heritage item is used, bought, sold and handed over only between characters of its
/// people, is never dropped or junked, and its people's keeper re-issues a lost one - so What Was Kept, the Veth questline every new Veth
/// starts, can always be finished.
/// </summary>
public class HeritageTests
{
    private const string Roll = "item.heritage.kept_roll";
    private const string Quest = "quest.heritage.what_was_kept";
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private static readonly (double X, double Z) AtRenn = (48, 126.2);
    private static readonly (double X, double Z) AtKera = (60.3, 140.3);

    private static PlayerRecord WithRoll(PlayerRecord r) =>
        r.Inventory.Any(e => e.DefId == Roll) ? r : r.WithInventory(r.Inventory.Append(new InventoryEntry(EntityId.NewId(EntityKind.Item), Roll, 1)));

    private static PlayerRecord WithoutRoll(PlayerRecord r) => r.WithInventory(r.Inventory.Where(e => e.DefId != Roll));

    private static PlayerRecord Of(PlayerRecord r, string people) => r with { Identity = r.Identity with { PeopleId = people } };

    /// <summary>Kera made a Veth trader who buys keepsakes, so a Veth can sell a roll to her.</summary>
    private static SimulationSetup KeraVeth(SimulationSetup setup)
    {
        var kera = setup.Social.Npcs[Kera];
        var merchant = setup.Items.Merchants[kera.MerchantId!];
        return setup with
        {
            Social = setup.Social with { Npcs = setup.Social.Npcs.SetItem(Kera, kera with { PeopleId = "people.veth" }) },
            Items = setup.Items with { Merchants = setup.Items.Merchants.SetItem(merchant.Id, merchant with { BuysCategories = merchant.BuysCategories.Add("misc") }) },
        };
    }

    private static Arena At(GameSession session, SimulationSetup setup, (double X, double Z) place, Func<PlayerRecord, PlayerRecord> change) =>
        Arena.OpenCreatures(session, setup, place, 0, Array.Empty<(string, double, double, string)>(), change);

    private static InventoryEntry Carried(Arena arena) => arena.Simulation.Player.Inventory.Single(e => e.DefId == Roll);

    [Fact]
    public void ANewVeth_StartsWhatWasKept_AndItWaitsOnRenn()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame(new CharacterSpec("Wenna Lind", Pronouns.She, "people.veth", "body.veth_f.lean", "call.wayfarer"), seed: 42);

        var quest = simulation.Quests.Single(q => q.Id == Quest);
        Assert.Equal(QuestStatus.Active, quest.Status);
        Assert.Equal("What Was Kept", quest.Title);
    }

    [Fact]
    public void TheRoll_CannotBeDropped_OrJunked()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, session.Setup, AtRenn, WithRoll);
        var roll = Carried(arena);

        Assert.NotNull(arena.Submit(new MoveItemCommand(arena.Player, roll.ItemId.Value, ItemPlace.Carried, ItemPlace.Ground, 1)));
        Assert.NotNull(arena.Submit(new MarkJunkCommand(arena.Player, roll.ItemId, true)));
        Assert.Equal(roll.ItemId, Carried(arena).ItemId);
    }

    [Fact]
    public void TheRoll_IsNeverSalvaged()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, session.Setup, AtRenn, WithRoll);

        Assert.Equal("Veth heritage is never salvaged.", arena.Simulation.Salvage(Carried(arena).ItemId).Refusal);
    }

    [Fact]
    public void AVeth_SellsTheRoll_ToAVethTrader_ButNotToAnother()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        var toKal = At(session, session.Setup, AtKera, WithRoll);   // Kera is no Veth
        Assert.Null(toKal.Simulation.Offer(Kera, Roll));   // she names no price for it
        Assert.Equal("Kera Voss will not trade in Veth heritage", toKal.Simulation.HeritageRefusal(Kera, Roll));   // the reason the disabled Sell shows
        Assert.Equal("Kera Voss will not trade in Veth heritage", toKal.Submit(new SellCommand(toKal.Player, Kera, Carried(toKal).ItemId, 1)));

        var toVeth = At(session, KeraVeth(session.Setup), AtKera, r => WithRoll(r).WithCurrency(20));   // a trader asks more than she pays
        Assert.Null(toVeth.Simulation.HeritageRefusal(Kera, Roll));
        long coin = toVeth.Simulation.Player.Currency;
        Assert.Null(toVeth.Submit(new SellCommand(toVeth.Player, Kera, Carried(toVeth).ItemId, 1)));
        Assert.DoesNotContain(toVeth.Simulation.Player.Inventory, e => e.DefId == Roll);
        Assert.True(toVeth.Simulation.Player.Currency > coin);
        // and buys it back from her shelf
        var ware = toVeth.Simulation.Wares(Kera)!.Wares.Single(w => w.ItemId == Roll);
        Assert.Null(toVeth.Submit(new BuyCommand(toVeth.Player, Kera, ware.Ref, 1)));
        Assert.Single(toVeth.Simulation.Player.Inventory, e => e.DefId == Roll);
    }

    [Fact]
    public void AnotherPeople_CanCarryARoll_ButNeitherSellsNorUsesNorBuysIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var outsider = At(session, KeraVeth(session.Setup), AtKera, r => Of(WithRoll(r), "people.ondrek"));
        var roll = Carried(outsider);

        Assert.Equal("that is Veth heritage: only their own people use or trade it", outsider.Submit(new SellCommand(outsider.Player, Kera, roll.ItemId, 1)));
        Assert.Equal("that is Veth heritage: only their own people use or trade it", outsider.Submit(new UseItemCommand(outsider.Player, roll.ItemId)));
        Assert.Equal("that is Veth heritage: only their own people use or trade it", outsider.Submit(new EquipCommand(outsider.Player, roll.ItemId)));

        // A roll on a Veth trader's shelf is not offered to them at all.
        var veth = At(session, KeraVeth(session.Setup), AtKera, WithRoll);
        Assert.Null(veth.Submit(new SellCommand(veth.Player, Kera, Carried(veth).ItemId, 1)));
        var shelfRef = veth.Simulation.Wares(Kera)!.Wares.Single(w => w.ItemId == Roll).Ref;
        var stranger = At(session, KeraVeth(session.Setup), AtKera, r => Of(r, "people.ondrek"));
        Assert.DoesNotContain(stranger.Simulation.Wares(Kera)!.Wares, w => w.ItemId == Roll);
        Assert.NotNull(stranger.Submit(new BuyCommand(stranger.Player, Kera, shelfRef, 1)));
    }

    [Fact]
    public void AVethWhoLostTheRoll_GetsItRecopiedByRenn_AndOnlyThen()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var carrying = At(session, session.Setup, AtRenn, WithRoll);
        Assert.DoesNotContain("recopy", Replies(carrying));

        var lost = At(session, session.Setup, AtRenn, WithoutRoll);
        Assert.Contains("recopy", Replies(lost));
        Assert.Null(lost.Submit(new ChooseCommand(lost.Player, "recopy")));
        Assert.Single(lost.Simulation.Player.Inventory, e => e.DefId == Roll);

        var outsider = At(session, session.Setup, AtRenn, r => Of(r, "people.ondrek"));
        Assert.DoesNotContain("recopy", Replies(outsider));
    }

    [Fact]
    public void WhatWasKept_StaysCompletable_AfterTheRollIsLostAndRecopied()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var quest = session.Setup.Quests.Quests[Quest];
        var arena = At(session, session.Setup, AtRenn, r => WithoutRoll(r).WithQuests(new[] { QuestRules.Start(quest, 0) }));

        // Without the roll Renn cannot read it - but he can re-copy it, and then he reads it.
        Assert.DoesNotContain("roll", Replies(arena));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "recopy")));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "back")));
        Assert.Contains("roll", arena.Simulation.Conversation!.Replies.Select(r => r.Id));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "roll")));
        Assert.Equal("kept_roll", arena.Simulation.Conversation!.NodeId);
        arena.Tick(2);

        var state = arena.Simulation.Quests.Single(q => q.Id == Quest);
        Assert.Equal(QuestStatus.Active, state.Status);
        Assert.Contains("Cairnwold", state.Objectives.Single(o => o.Status == ObjectiveStatus.Active).Description);
        Assert.Single(arena.Simulation.Player.Inventory, e => e.DefId == Roll);   // reading it took nothing
    }

    [Fact]
    public void WhatWasKept_IsVethHeritage_AndAnotherPeopleCannotStartIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var setup = session.Setup;
        var outsider = Of(Simulation.NewCharacter(setup, EntityId.NewId(EntityKind.Character), new CharacterSpec("Wenna Lind", Pronouns.She, "people.veth", "body.veth_f.lean", null), 7),
            "people.ondrek");
        var simulation = Simulation.Start(setup, outsider, new WorldDelta(session.Generator, 42, new Registry()), 0, new EventBus());

        simulation.Enqueue(new BeginStoryCommand(simulation.PlayerId));
        simulation.DrainCommands();

        Assert.DoesNotContain(simulation.Quests, q => q.Id == Quest);
        Assert.Contains(simulation.Quests, q => q.Id == "quest.main.the_road_in");
    }

    // ── What Was Kept, steps 3-5: Berrin's book, the keeping, and Renn's record ──

    private const string Berrin = "npc.cairnwold.berrin_garth";
    private const string Timber = "item.material.timber";
    private static readonly (double X, double Z) AtBerrin = (959.0, 644.0);   // region metres

    /// <summary>What Was Kept as it stands after these objectives, the last of them the one now open.</summary>
    private static QuestState At(params string[] reached)
    {
        var objectives = reached.Select((id, i) => i < reached.Length - 1
            ? new ObjectiveState(id, ObjectiveStatus.Satisfied, 0, 0, 0)
            : new ObjectiveState(id, ObjectiveStatus.Active, 0, null, 0));
        return new QuestState(Quest, QuestStatus.Active, 0, null, null, objectives.ToImmutableArray());
    }

    private static Arena AtBerrins(GameSession session, QuestState quest, int timber) =>
        Arena.OpenCreatures(session, session.Setup, AtBerrin, 0, Array.Empty<(string, double, double, string)>(),
            r => WithRoll(r).WithInventory(WithRoll(r).Inventory.Concat(timber > 0 ? new[] { new InventoryEntry(EntityId.NewId(EntityKind.Item), Timber, timber) } : Array.Empty<InventoryEntry>()))
                .WithQuests(new[] { quest }), origin: Harness.Region);

    private static string[] Talk(Arena arena, string npc)
    {
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, npc)));
        return arena.Simulation.Conversation!.Replies.Select(r => r.Id).ToArray();
    }

    private static QuestView Kept(Arena arena) => arena.Simulation.Quests.Single(q => q.Id == Quest);

    [Fact]
    public void Berrin_FindsTheOtherHalf_AndTakesTheTimber_AndTheLineGoesBackToRenn()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AtBerrins(session, At("o_roll", "o_other_half", "o_lodge_book"), timber: 4);

        Assert.Contains("oath", Talk(arena, Berrin));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "oath")));
        Assert.Equal(new[] { "keep", "keep_later", "release", "claim" }, arena.Simulation.Conversation!.Replies.Select(r => r.Id));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "keep")));
        Assert.Equal("oath_kept", arena.Simulation.Conversation!.NodeId);
        arena.Tick(2);

        Assert.DoesNotContain(arena.Simulation.Player.Inventory, e => e.DefId == Timber);
        var quest = Kept(arena);
        Assert.Equal(QuestStatus.Active, quest.Status);
        Assert.Equal("o_recorded", quest.Objectives.Single(o => o.Status == ObjectiveStatus.Active).Id);
    }

    [Fact]
    public void WithoutFourTimber_TheOathCannotBeKeptYet_ButCanBeReleased()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AtBerrins(session, At("o_roll", "o_other_half", "o_lodge_book"), timber: 3);

        Talk(arena, Berrin);
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "oath")));
        Assert.DoesNotContain("keep", arena.Simulation.Conversation!.Replies.Select(r => r.Id));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "release")));
        Assert.Equal("oath_released", arena.Simulation.Conversation!.NodeId);
        arena.Tick(2);

        Assert.Equal("o_recorded", Kept(arena).Objectives.Single(o => o.Status == ObjectiveStatus.Active).Id);
        Assert.Equal(-3, arena.Simulation.CaptureRecord().Relationships.Single(r => r.NpcId == Berrin && r.Dimension == "respect").Value);
    }

    [Fact]
    public void ClaimingDescent_FailsWhatWasKept_AndNoOneCallsYouKeeper()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AtBerrins(session, At("o_roll", "o_other_half", "o_lodge_book"), timber: 0);

        Talk(arena, Berrin);
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "oath")));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "claim")));
        Assert.Equal("oath_claimed", arena.Simulation.Conversation!.NodeId);
        arena.Tick(2);

        Assert.Equal(QuestStatus.Failed, Kept(arena).Status);
        Assert.Empty(arena.Simulation.Titles);
    }

    [Theory]
    [InlineData("o_keep", "'Kept,'")]
    [InlineData("o_release", "'Released.'")]
    public void Renn_ClosesTheLine_AndTheCharacterIsCalledItsKeeper(string settled, string says)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var quest = At("o_roll", "o_other_half", "o_lodge_book", settled, "o_recorded");
        var arena = At(session, session.Setup, AtRenn, r => WithRoll(r).WithQuests(new[] { quest }));

        Assert.Contains("record", Replies(arena));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "record")));
        Assert.Contains(says, arena.Simulation.Conversation!.Text);
        arena.Tick(2);

        Assert.Equal(QuestStatus.Completed, Kept(arena).Status);
        Assert.Equal(new[] { "Keeper of the Wanderer roll" }, arena.Simulation.Titles);
        Assert.Single(arena.Simulation.Player.Inventory, e => e.DefId == Roll);   // the roll stays the character's
    }

    private static string[] Replies(Arena arena)
    {
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Renn)));
        return arena.Simulation.Conversation!.Replies.Select(r => r.Id).ToArray();
    }
}
