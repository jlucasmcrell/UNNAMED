using System.Collections.Immutable;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Social;
using static UNNAMED.Domain.Tests.Progression.TestRules;

namespace UNNAMED.Domain.Tests.Social;

/// <summary>
/// Adventurers' rules, pure (WP13, §2.13): the outcome table (DN2), the goal chain, first claimant wins, injury, death and arrivals (DN4,
/// DN5), the gear ladder bought and commissioned, retirement, and no cap.
/// </summary>
public class AdventurerTests
{
    private const string Sword = "item.weapon.rusted_sword";
    private const string Cap = "item.armor.hide_cap";
    private const string Hide = "item.material.wolf_hide";
    private const string Pelt = "item.material.small_hide";
    private const string Wolf = "creature.beast.wolf_grey";
    private const string Wolves = "spawn.town.wolves";
    private const string Hares = "spawn.town.hares";

    private static readonly ImmutableSortedDictionary<string, ItemDefinition> Items = new[]
    {
        new ItemDefinition(Sword, "weapon", 1, 1000, 15, "common", false, false, EquipSlot.MainHand, Requirements.None, null, 0),
        new ItemDefinition(Cap, "armor", 1, 300, 12, "common", false, false, EquipSlot.Head, Requirements.None, null, 1),
        new ItemDefinition(Hide, "material", 20, 500, 6, "common", false, false, null, Requirements.None, null, 0),
        new ItemDefinition(Pelt, "material", 20, 200, 3, "common", false, false, null, Requirements.None, null, 0),
    }.ToImmutableSortedDictionary(i => i.Id, i => i, StringComparer.Ordinal);

    private static AdventurerRules Rules(int target = 2, OutcomeRow? row = null) =>
        new(target, 1, 1, 10, 10, 50, 50, 3, 7, 2, 2, ImmutableArray.Create(row ?? new OutcomeRow(-99, 0, 0, 100)), ImmutableArray.Create(Sword, Cap))
        {
            RetireCoin = 1_000_000,
            RetireDays = 100_000,
            LfgPercent = 0,
        };

    private static readonly AdventurerSettlement Town = new("town", "market.town", "merchant.town.smith", 0, 0, 0, ImmutableArray.Create(Wolves, Hares));

    private static AdventurerDay At(long day, AdventurerRules rules, IEnumerable<AdventurerBounty>? bounties = null, Func<string, bool>? canClaim = null,
        Func<string, string, bool>? keeps = null, Func<string, ImmutableArray<(string, int)>?>? recipe = null) =>
        new(day, day * 57_600, 2_400, use => (ulong)use.Length * 2_654_435_761UL + (ulong)day * 40_503UL)
        {
            Settlements = ImmutableSortedDictionary.Create<string, AdventurerSettlement>(StringComparer.Ordinal).Add(Town.Id, Town),
            Roads = ImmutableArray<AdventurerRoad>.Empty,
            Grounds = new[] { new AdventurerGround(Wolves, "town", Wolf, 2, 3), new AdventurerGround(Hares, "town", "creature.beast.rabbit", 1, 1) }
                .ToImmutableSortedDictionary(g => g.SpawnKey, g => g, StringComparer.Ordinal),
            Bounties = (bounties ?? Enumerable.Empty<AdventurerBounty>()).ToImmutableArray(),
            CanClaim = canClaim ?? (_ => true),
            Item = id => Items.GetValueOrDefault(id),
            Keeps = keeps ?? ((_, item) => item == Hide || item == Cap),
            Recipe = recipe ?? (_ => null),
            Loot = (spawn, _) => spawn == Wolves ? ImmutableArray.Create((Hide, 1)) : ImmutableArray.Create((Pelt, 1)),
            Yields = (spawn, item) => spawn == Wolves ? item == Hide : item == Pelt,
            NameFor = (key, generation, taken) => ($"Name {key}", "people.veth"),
            CanArrive = _ => true,
            Progression = Default(levelCap: 10),
            KillXp = _ => 30,
            GearId = (key, n) => EntityId.Create(EntityKind.Item, 1, Enumerable.Repeat((byte)(key.Length + n), 10).ToArray()),
        };

    private static AdventurerState Begin(AdventurerRules rules) => AdventurerEngine.Reconcile(null, rules, At(0, rules)).State;

    private static (AdventurerState State, List<AdventurerHappening> Happenings) Live(AdventurerState state, AdventurerRules rules, long toDay,
        Func<long, AdventurerDay>? day = null)
    {
        var all = new List<AdventurerHappening>();
        while (state.Day < toDay)
        {
            var (next, happened) = AdventurerEngine.Midnight(state, rules, day?.Invoke(state.Day + 1) ?? At(state.Day + 1, rules));
            state = next;
            all.AddRange(happened);
        }
        return (state, all);
    }

    [Fact]
    public void ASettlement_FillsItsSlots_ToTheTarget_WithTheLaddersFirstPiece()
    {
        var rules = Rules(target: 3);
        var state = Begin(rules);
        Assert.Equal(3, state.Agents.Count);
        Assert.All(state.Agents.Values, a => Assert.Equal(Sword, a.Gear[EquipSlot.MainHand].DefId));
        Assert.Equal(new[] { "npc.adventurer.town.slot_1_g0", "npc.adventurer.town.slot_2_g0", "npc.adventurer.town.slot_3_g0" }, state.Agents.Keys);
        Assert.Throws<ArgumentOutOfRangeException>(() => AdventurerEngine.Midnight(state, rules, At(2, rules)));
    }

    [Fact]
    public void TheOutcomeTable_PicksItsRowByMargin_AndRiskSetsTheWorstAccepted()
    {
        var rules = Rules() with
        {
            Outcomes = ImmutableArray.Create(new OutcomeRow(-99, 20, 30, 10), new OutcomeRow(0, 5, 10, 60), new OutcomeRow(3, 0, 5, 90)),
        };
        Assert.Null(rules.Problem());
        Assert.Equal(20, rules.Row(-5).DeathPercent);
        Assert.Equal(5, rules.Row(0).DeathPercent);
        Assert.Equal(0, rules.Row(7).DeathPercent);
        Assert.Equal(0, rules.WorstMargin(0));
        Assert.Equal(-3, rules.WorstMargin(80));
        Assert.NotNull((rules with { Outcomes = ImmutableArray.Create(new OutcomeRow(0, 60, 60, 0)) }).Problem());
        Assert.NotNull((rules with { LfgLeadMinutes = 10 }).Problem());   // a SettingOut comes at least 20 game minutes ahead
    }

    [Fact]
    public void AHunt_CullsAndGainsXpAndLoot_AndTheLootIsSoldWhereTheMarketKeepsIt()
    {
        var rules = Rules(target: 1);
        var (state, happenings) = Live(Begin(rules), rules, 2);
        var a = state.Agents.Values.Single();
        Assert.Contains(happenings, h => h.Kind == AdventurerEngine.Culled && h.Key == a.Key);
        Assert.True(a.Kills > 0);
        Assert.True(a.Progression.LevelProgressXp > 0 || a.Level > 1);
        // The hides came home, and the market here keeps them: sold at its rate.
        Assert.Contains(happenings, h => h.Kind == AdventurerEngine.Sold && h.Ref == Hide && h.Market == "market.town");
    }

    [Fact]
    public void TheFirstByKey_ClaimsACompetitiveBounty_AndTheOtherHuntsInstead()
    {
        var rules = Rules(target: 2);
        var bounty = new AdventurerBounty("quest.bounty.wolves", "Wolves", "town", Wolf, null, 2, 15, 60);
        var claimed = new HashSet<string>();
        AdventurerDay Day(long d) => At(d, rules, new[] { bounty }, id => !claimed.Contains(id));
        var (planned, _) = AdventurerEngine.Midnight(Begin(rules), rules, Day(1));
        Assert.Equal(AdventurerGoals.Bounty, planned.Agents["npc.adventurer.town.slot_1_g0"].Plan.Goal);
        Assert.Equal(AdventurerGoals.Hunt, planned.Agents["npc.adventurer.town.slot_2_g0"].Plan.Goal);
        var (resolved, happenings) = AdventurerEngine.Midnight(planned, rules, Day(2));
        var claim = Assert.Single(happenings, h => h.Kind == AdventurerEngine.Claimed);
        Assert.Equal("npc.adventurer.town.slot_1_g0", claim.Key);
        Assert.Equal(15, claim.Coin);
        Assert.Equal(1, resolved.Agents[claim.Key].BountiesClaimed);
        // Someone finished it first: the plan comes to nothing, and is lost.
        var (_, lostHappenings) = AdventurerEngine.Midnight(planned, rules, At(2, rules, new[] { bounty }, _ => false));
        Assert.Contains(lostHappenings, h => h.Kind == AdventurerEngine.Lost && h.Ref == bounty.QuestId);
        Assert.DoesNotContain(lostHappenings, h => h.Kind == AdventurerEngine.Claimed);
    }

    [Fact]
    public void ADeliveryBounty_IsWorkedUpTheChain_HuntForIt_ThenDeliverIt()
    {
        var rules = Rules(target: 1);
        var bounty = new AdventurerBounty("quest.bounty.pelts", "Pelts", "town", null, Pelt, 1, 12, 30);
        AdventurerDay Day(long d) => At(d, rules, new[] { bounty });
        var (first, _) = AdventurerEngine.Midnight(Begin(rules), rules, Day(1));
        var a = first.Agents.Values.Single();
        Assert.Equal((AdventurerGoals.Hunt, Hares), (a.Plan.Goal, a.Plan.Ref));   // the ground that yields what the bounty wants
        var (second, _) = AdventurerEngine.Midnight(first, rules, Day(2));
        Assert.Equal((AdventurerGoals.Bounty, bounty.QuestId), (second.Agents[a.Key].Plan.Goal, second.Agents[a.Key].Plan.Ref));
        var (third, happenings) = AdventurerEngine.Midnight(second, rules, Day(3));
        Assert.Contains(happenings, h => h.Kind == AdventurerEngine.Claimed && h.Ref == bounty.QuestId);
        Assert.Equal(0, third.Agents[a.Key].Pack.GetValueOrDefault(Pelt));
    }

    [Fact]
    public void Death_LeavesTheSlot_ForSomeoneNew_ThreeToSevenDaysLater()
    {
        var deadly = Rules(target: 1, row: new OutcomeRow(-99, 100, 0, 0));
        var (state, happenings) = Live(Begin(deadly), deadly, 2);
        Assert.Contains(happenings, h => h.Kind == AdventurerEngine.Died && h.Key == "npc.adventurer.town.slot_1_g0");
        Assert.Empty(state.Agents);
        var vacancy = state.Vacancies["town.slot_1"];
        Assert.InRange(vacancy.ArrivalDay - vacancy.LeftDay, 3, 7);
        var safe = Rules(target: 1);
        var (later, arrived) = Live(state, safe, vacancy.ArrivalDay);
        Assert.Contains(arrived, h => h.Kind == AdventurerEngine.Arrived && h.Key == "npc.adventurer.town.slot_1_g1");
        Assert.Equal(vacancy.ArrivalDay, later.Agents["npc.adventurer.town.slot_1_g1"].ArrivedDay);
    }

    [Fact]
    public void AnInjury_KeepsThemAtTheLodge_UntilItHeals()
    {
        var hurt = Rules(target: 1, row: new OutcomeRow(-99, 0, 100, 0));
        var (state, _) = Live(Begin(hurt), hurt, 2);
        var a = state.Agents.Values.Single();
        Assert.Equal(AdventurerCondition.Injured, a.Condition);
        Assert.Equal(AdventurerGoals.Rest, a.Plan.Goal);
        var (healed, _) = Live(state, Rules(target: 1), a.InjuredUntilDay);
        Assert.Equal(AdventurerCondition.Active, healed.Agents[a.Key].Condition);
        Assert.NotEqual(AdventurerGoals.Rest, healed.Agents[a.Key].Plan.Goal);
    }

    [Fact]
    public void TheNextPieceOfTheLadder_IsBought_OrCommissioned_WhenTheyCanPay()
    {
        var rules = Rules(target: 1);
        var rich = Begin(rules);
        var key = rich.Agents.Keys.Single();
        rich = rich with { Agents = rich.Agents.SetItem(key, rich.Agents[key] with { Coin = 100 }) };
        var (bought, happenings) = AdventurerEngine.Midnight(rich, rules, At(1, rules));
        Assert.Equal(Cap, bought.Agents[key].Gear[EquipSlot.Head].DefId);
        Assert.Contains(happenings, h => h.Kind == AdventurerEngine.Bought && h.Ref == Cap && h.Coin == 12 && h.Market == "market.town");
        Assert.Null(AdventurerEngine.NextGear(bought.Agents[key], rules, id => Items.GetValueOrDefault(id)));

        // Not kept at the market: commissioned from the smith, its recipe's inputs taken from the market's stock.
        var (made, commissions) = AdventurerEngine.Midnight(rich, rules, At(1, rules, keeps: (_, item) => item == Hide,
            recipe: item => item == Cap ? ImmutableArray.Create((Hide, 1)) : null));
        Assert.Equal(Cap, made.Agents[key].Gear[EquipSlot.Head].DefId);
        Assert.Contains(commissions, h => h.Kind == AdventurerEngine.Commissioned && h.Settlement == "merchant.town.smith" && h.Coin == 9);   // 6 x 1.5
        Assert.Contains(commissions, h => h.Kind == AdventurerEngine.Consumed && h.Ref == Hide && h.Amount == 1);
    }

    [Fact]
    public void PastTheirCoin_TheyRetire_AndTheSlotWaitsForSomeoneNew()
    {
        var rules = Rules(target: 1) with { RetireCoin = 50 };
        var state = Begin(rules);
        var key = state.Agents.Keys.Single();
        state = state with { Agents = state.Agents.SetItem(key, state.Agents[key] with { Coin = 60 }) };
        var (retired, happenings) = AdventurerEngine.Midnight(state, rules, At(1, rules));
        Assert.Contains(happenings, h => h.Kind == AdventurerEngine.Retired && h.Key == key && h.Settlement == "town");
        Assert.False(retired.Agents.ContainsKey(key));
        Assert.True(retired.Vacancies.ContainsKey("town.slot_1"));
    }

    [Fact]
    public void ThoseOutAfterTheSameGround_GoTogether()
    {
        var rules = Rules(target: 3);
        var (state, _) = AdventurerEngine.Midnight(Begin(rules), rules, At(1, rules));
        var hunting = state.Agents.Values.Where(a => a.Plan.Goal == AdventurerGoals.Hunt && a.Plan.Ref == Wolves).ToList();
        Assert.True(hunting.Count > 1);
        Assert.All(hunting, a => Assert.Equal(hunting.Select(h => h.Key).OrderBy(k => k, StringComparer.Ordinal), a.Plan.Group));
        Assert.All(hunting, a => Assert.Equal(a.Level + 1 + rules.PowerPerCompanion * (hunting.Count - 1), AdventurerEngine.Power(a, rules)));
    }

    /// <summary>Codex on #23: a group out after one ground shares its creatures - no midnight credits more kills there than it holds.</summary>
    [Fact]
    public void AGroupOnOneGround_SharesItsCreatures_AndNoMidnightCreditsMoreKillsThanItHolds()
    {
        var rules = Rules(target: 6);
        var (_, happenings) = Live(Begin(rules), rules, 6);
        var culls = happenings.Where(h => h.Kind == AdventurerEngine.Culled && h.Ref == Wolves).ToList();
        Assert.NotEmpty(culls);
        var state = Begin(rules);
        for (long d = 1; d <= 6; d++)
        {
            var (next, happened) = AdventurerEngine.Midnight(state, rules, At(d, rules));
            Assert.True(happened.Where(h => h.Kind == AdventurerEngine.Culled && h.Ref == Wolves).Sum(h => h.Amount) <= 3, $"day {d}");
            Assert.Equal(happened.Where(h => h.Kind == AdventurerEngine.Culled).Sum(h => h.Amount), next.Agents.Values.Sum(a => a.Kills) - state.Agents.Values.Where(a => next.Agents.ContainsKey(a.Key)).Sum(a => a.Kills));
            state = next;
        }
    }

    [Fact]
    public void TheSameDays_GiveTheSameLives()
    {
        var rules = Rules(target: 3, row: new OutcomeRow(-99, 10, 20, 50));
        var (a, _) = Live(Begin(rules), rules, 40);
        var (b, _) = Live(Live(Begin(rules), rules, 17).State, rules, 40);
        using var ha = new CanonicalHasher();
        using var hb = new CanonicalHasher();
        a.AddTo(ha);
        b.AddTo(hb);
        Assert.Equal(ha.Finish(), hb.Finish());
    }

    [Fact]
    public void AThousandAdventurers_LiveTheirDays_WithNothingRefusedOrTruncated()
    {
        // No cap (DN5, the owner's 2026-10-17 ruling): the target is only how many slots a settlement keeps filled.
        var rules = Rules(target: 1_000, row: new OutcomeRow(-99, 5, 10, 60));
        var state = Begin(rules);
        Assert.Equal(1_000, state.Agents.Count);
        var (lived, _) = Live(state, rules, 5);
        Assert.Equal(1_000, lived.Agents.Count + lived.Vacancies.Count);
    }
}
