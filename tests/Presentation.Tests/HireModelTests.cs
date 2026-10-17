using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Social;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Tests;

/// <summary>The hiring sheet (WP12b's hireability, WP13's adventurers, WP15a's terms): stated before the character agrees (DN9, DN11).</summary>
public class HireModelTests
{
    private static readonly PartyRules Rules = new(3, 1, 2, 4, 1, 30, ImmutableSortedDictionary.CreateRange(new[]
    {
        KeyValuePair.Create(HirelingRole.Guard, new RoleTerms(8, 0, 0, 0, CompanionStance.Aggressive)),
        KeyValuePair.Create(HirelingRole.Porter, new RoleTerms(4, 30_000, 0, 0, CompanionStance.Passive)),
        KeyValuePair.Create(HirelingRole.Gatherer, new RoleTerms(5, 0, 3, 0, CompanionStance.Passive)),
        KeyValuePair.Create(HirelingRole.Crafter, new RoleTerms(7, 0, 0, 2, CompanionStance.Passive)),
    }));

    private static string Name(string id) => Names.Words(id);

    private static NpcHireView Resident(bool hireable, string? refusal = null) =>
        new("routine.fixture.hunter", hireable, refusal, ImmutableArray.Create("role.hunter"), ImmutableArray.Create(HirelingRole.Guard, HirelingRole.Gatherer));

    [Fact]
    public void AHireableResident_OffersTheirRoles_WhatEachDoes_AndTheRulesDays()
    {
        var sheet = HireModel.Sheet("Mira Tolle", Resident(true), Rules, null, null, Name);
        Assert.True(sheet.Hireable);
        Assert.Null(sheet.Refusal);
        Assert.Equal("Hunter · hires on for a wage.", sheet.Who);
        Assert.Equal(new[] { "Guard", "Gatherer" }, sheet.Roles.Select(r => r.Word));
        Assert.Equal("Fights beside you.", sheet.Roles[0].Does);
        Assert.Equal("Works a node you choose, 3 times a day, and brings you what it gives.", sheet.Roles[1].Does);
        Assert.Equal((1, 30), (sheet.MinDays, sheet.MaxDays));
    }

    [Fact]
    public void SomeoneNotHireableNow_SaysWhy_AsASentenceAfterTheirName()
    {
        var sheet = HireModel.Sheet("Mira Tolle", Resident(false, "is injured"), Rules, null, null, Name);
        Assert.False(sheet.Hireable);
        Assert.Equal("Mira Tolle is injured.", sheet.Refusal);
        Assert.Equal("Bram Hallow is out.", HireModel.Sentence("Bram Hallow", "Bram Hallow is out"));
        var none = HireModel.Sheet("Oswin", new NpcHireView("k", true, null, ImmutableArray<string>.Empty, ImmutableArray<HirelingRole>.Empty), Rules, null, null, Name);
        Assert.Equal("Oswin does not hire on.", none.Refusal);
    }

    [Fact]
    public void AnAdventurer_IsSaidByLevelAndDaring_NeverARiskNumber_AndKeepsTheirGear()
    {
        var bram = new AdventurerView("npc.adventurer.vale.slot_1_g0", EntityId.NewId(EntityKind.Npc), "Bram Hallow", "fixture_vale", "fixture_vale", 3, 42,
            AdventurerCondition.Active, "hunt", null, null, true, 7, 2, 55, ImmutableArray.Create("item.weapon.iron_sword"));
        var hire = new NpcHireView(bram.Key, true, null, ImmutableArray.Create("adventurer"), ImmutableArray.Create(HirelingRole.Guard));
        var sheet = HireModel.Sheet(bram.Name, hire, Rules, bram, null, Name);
        Assert.Equal("Adventurer, level 3 · bold · carries iron sword. Keeps their own gear and level when hired.", sheet.Who);
        Assert.DoesNotContain("55", sheet.Who);
        Assert.Equal("careful", HireModel.Caution(10));
        Assert.Equal("reckless", HireModel.Caution(90));
    }

    [Fact]
    public void TheTerms_StateTheWageTheWholeCostTheLootRuleAndTheRisk()
    {
        var offer = HireModel.Offer(new HireTerms("routine.fixture.hunter", HirelingRole.Guard, 7, 8, 56, PartyRules.WageOnly, true, null));
        Assert.Equal("8 coin a day", offer.Wage);
        Assert.Equal("7 days: 56 coin in all - the first day's now, then each midnight", offer.Total);
        Assert.Equal("They take only their wage: what the party finds is yours.", offer.Loot);
        Assert.Equal(HireModel.RiskWords, offer.Risk);
        Assert.Contains("can die", offer.Risk);
        Assert.Equal("1 day: 8 coin, paid now", HireModel.Offer(new HireTerms("k", HirelingRole.Guard, 1, 8, 8, PartyRules.WageOnly, true, null)).Total);
    }

    [Fact]
    public void TheHire_IsOfferedOnlyWithTheRiskAccepted_AndNothingRefusingIt()
    {
        var open = HireModel.Offer(new HireTerms("k", HirelingRole.Guard, 7, 8, 56, PartyRules.WageOnly, true, null));
        Assert.False(open.CanHire(riskAccepted: false));
        Assert.True(open.CanHire(riskAccepted: true));
        var poor = HireModel.Offer(new HireTerms("k", HirelingRole.Guard, 7, 8, 56, PartyRules.WageOnly, true, "not enough coin: the first day's wage is 8, 3 carried"));
        Assert.False(poor.CanHire(riskAccepted: true));
        Assert.Equal("Not enough coin: the first day's wage is 8, 3 carried.", poor.Refusal);
    }

    [Fact]
    public void TheDays_AreHeldToTheContractsRange() =>
        Assert.Equal((1, 7, 30), (HireModel.Days(0, 1, 30), HireModel.Days(HireModel.DefaultDays, 1, 30), HireModel.Days(99, 1, 30)));

    [Fact]
    public void ATripTheyAskCompanyFor_RidesOnTheSheet_ForAGroupUpWithNoWage()
    {
        var trip = new GroupPost(EntityId.NewId(EntityKind.Npc), "Bram Hallow", "grey wolves", "the Old Den", "the North Gate", "03:00", "in about 1 h", false, null);
        var sheet = HireModel.Sheet("Bram Hallow", Resident(true), Rules, null, trip, Name);
        Assert.Same(trip, sheet.Trip);
        Assert.True(sheet.Trip!.CanJoin);
    }
}
