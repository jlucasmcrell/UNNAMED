using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Ui;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Tests;

/// <summary>The Party screen's cards (WP15a, WP15b): companions and hirelings, wages, orders, gear, and regard and disposition as words.</summary>
public class PartyModelTests
{
    private static readonly PartyRules Rules = new(3, 1, 2, 4, 1, 30, ImmutableSortedDictionary.CreateRange(new[]
    {
        KeyValuePair.Create(HirelingRole.Guard, new RoleTerms(8, 0, 0, 0, CompanionStance.Aggressive)),
        KeyValuePair.Create(HirelingRole.Porter, new RoleTerms(4, 30_000, 0, 0, CompanionStance.Passive)),
        KeyValuePair.Create(HirelingRole.Gatherer, new RoleTerms(5, 0, 3, 0, CompanionStance.Passive)),
        KeyValuePair.Create(HirelingRole.Crafter, new RoleTerms(7, 0, 0, 2, CompanionStance.Passive)),
    }));

    private static CompanionView Member(string id, string name, CompanionCondition condition = CompanionCondition.Up, string doing = "following") =>
        new(id, EntityId.NewId(EntityKind.Npc), name, CompanionOrder.Follow, condition, 30, 40, new Body(0, 0, 0, 0), doing);

    private static PartySheetView Sheet(string id, string name, Disposition disposition, string regard, params PartyGearView[] gear) =>
        new(id, name, 3, 40, 300, 1, ImmutableSortedDictionary<string, SkillState>.Empty, 40, gear.ToImmutableArray(),
            ImmutableArray<InventoryEntry>.Empty, regard, disposition);

    private static string Item(string defId, int quality) => (quality > 0 ? "fine " : "") + Names.Words(defId);

    [Fact]
    public void ACompanionsCard_SaysWhoTheyAreHowTheyAreAndHowTheyRegardYou_InWords()
    {
        var card = PartyModel.Card(Member("npc.tavar", "Tavar"), Sheet("npc.tavar", "Tavar", Disposition.Comply, "Tavar trusts you."), Rules, Item);
        Assert.Equal("Companion", card.Kind);
        Assert.Equal("Following", card.Doing);
        Assert.Equal("Tavar trusts you.", card.Regard);
        Assert.Equal("Tavar does as you ask.", card.Goes);
        Assert.Equal(WordTone.Good, card.GoesTone);
        Assert.Equal("Level 3 · 40 of 300 XP to 4 · a point to spend", card.Level);
        Assert.Null(card.Contract);
        Assert.Null(card.Wage);
        Assert.True(card.CanDismiss);
        Assert.DoesNotContain(card.Regard + card.Goes, char.IsDigit);   // approval and loyalty are never numbers
    }

    [Fact]
    public void AMemberWithNoSheetYet_HasNoStrongViewAndComplies()
    {
        var card = PartyModel.Card(Member("npc.sel", "Sel"), null, Rules, Item);
        Assert.Equal("Sel has no strong view of you yet.", card.Regard);
        Assert.Equal("Sel does as you ask.", card.Goes);
        Assert.Null(card.Level);
        Assert.Empty(card.Gear);
    }

    [Theory]
    [InlineData(Disposition.Hesitate, "Mira hesitates at what you ask, but does it.", WordTone.Warning)]
    [InlineData(Disposition.Refuse, "Mira refuses what you ask.", WordTone.Refusal)]
    [InlineData(Disposition.Leave, "Mira means to leave you.", WordTone.Refusal)]
    public void Disposition_IsASentence_AndItsTone(Disposition disposition, string words, WordTone tone)
    {
        var card = PartyModel.Card(Member("routine.hunter", "Mira"), Sheet("routine.hunter", "Mira", disposition, ""), Rules, Item);
        Assert.Equal(words, card.Goes);
        Assert.Equal(tone, card.GoesTone);
    }

    [Fact]
    public void AHirelingsCard_StatesTheRoleTheDaysLeftTheWageAndWhatIsOwed()
    {
        var hireling = Member("routine.porter", "Aldo Renning") with
        {
            Membership = PartyMembership.Hireling,
            Contract = new HirelingContract(HirelingRole.Porter, 7, 4, PartyRules.WageOnly) { DaysServed = 3, Purse = 16, UnpaidDays = 1 },
        };
        var card = PartyModel.Card(hireling, null, Rules, Item);
        Assert.True(card.Hired);
        Assert.Equal("Hireling · porter", card.Kind);
        Assert.Equal("Porter, 4 of 7 days left", card.Contract);
        Assert.Equal("4 coin a day, paid at midnight; 16 paid so far", card.Wage);
        Assert.Equal("Unpaid for a day: they leave at midnight unless you carry their wage.", card.Owed);
    }

    [Fact]
    public void Wages_OweNothingWhenPaid_AndCountTheGraceWhenNot()
    {
        var paid = new HirelingContract(HirelingRole.Guard, 7, 8, PartyRules.WageOnly);
        Assert.Null(PartyModel.OwedWords(paid, Rules));
        var patient = Rules with { GraceDays = 3 };
        Assert.Equal("Unpaid for a day: they leave if not paid within 2 days. Carry the coin and they are paid at midnight.",
            PartyModel.OwedWords(paid with { UnpaidDays = 1 }, patient));
        Assert.Equal("Guard, the last day", PartyModel.ContractWords(paid with { DaysServed = 6 }));
        Assert.Equal("Gatherer, 7 of 7 days left; works iron vein",
            PartyModel.ContractWords(new HirelingContract(HirelingRole.Gatherer, 7, 5, PartyRules.WageOnly) { WorkSite = "node.iron_vein" }));
    }

    [Fact]
    public void Gear_IsListedBySlot_WithWhatIsOnlyLoaned()
    {
        var sheet = Sheet("npc.tavar", "Tavar", Disposition.Comply, "",
            new PartyGearView(EquipSlot.Chest, EntityId.NewId(EntityKind.Item), "item.armor.quilted_coat", 0, Loaned: false),
            new PartyGearView(EquipSlot.MainHand, EntityId.NewId(EntityKind.Item), "item.weapon.iron_sword", 1, Loaned: true));
        var card = PartyModel.Card(Member("npc.tavar", "Tavar"), sheet, Rules, Item);
        Assert.Equal(new[] { EquipSlot.MainHand, EquipSlot.Chest }, card.Gear.Select(g => g.Slot));
        Assert.Equal(("main hand", "fine iron sword", "Loaned"), (card.Gear[0].SlotWord, card.Gear[0].Item, card.Gear[0].Whose));
        Assert.Equal("Theirs", card.Gear[1].Whose);
    }

    [Fact]
    public void ADownedMember_CannotBeDismissed_AndSaysWhy()
    {
        var card = PartyModel.Card(Member("npc.tavar", "Tavar", CompanionCondition.Downed, "downed"), null, Rules, Item);
        Assert.True(card.Downed);
        Assert.False(card.CanDismiss);
        Assert.Equal("Tavar is down: help them up first", card.Dismiss);
    }

    [Fact]
    public void Orders_AreWords_AndATacticSwitchesAlone()
    {
        Assert.Equal("Keep out of fights", PartyModel.StanceWord(CompanionStance.Passive));
        var tactics = PartyModel.Tactics(PartyTactics.Default);
        Assert.Equal(new[] { true, false, false }, tactics.Select(t => t.On));
        Assert.Equal(PartyTactics.Default with { ProtectInjured = true }, PartyModel.Toggled(PartyTactics.Default, "injured"));
        Assert.Equal(PartyTactics.Default with { KeepToLeash = false }, PartyModel.Toggled(PartyTactics.Default, "leash"));
        Assert.Equal(PartyTactics.Default, PartyModel.Toggled(PartyTactics.Default, "unknown"));
    }

    [Fact]
    public void TheHeadLine_RecoveryAndGroups_AreWords()
    {
        Assert.Equal("2 of 3 with you", PartyModel.Count(2, Rules));
        Assert.Equal("Tavar is recovering at home: back in about 2 days.", PartyModel.Recovering(new RecoveryView("npc.tavar", "Tavar", 1_000 + 57_600 + 10), 1_000, 57_600));
        Assert.Equal("Tavar is well again, and at home.", PartyModel.Recovering(new RecoveryView("npc.tavar", "Tavar", 500), 1_000, 57_600));
        var group = new AllyGroupView("g", EntityId.NewId(EntityKind.Npc), ImmutableArray<EntityId>.Empty, "bounty", "the Old Den", 10);
        Assert.Equal("With Bram: a bounty, at the Old Den. No wage: a share of what you find.", PartyModel.Group(group, "Bram"));
    }
}
