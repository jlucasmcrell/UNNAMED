using System.Collections.Immutable;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Tests.Progression;

namespace UNNAMED.Domain.Tests.Items;

public class LootRollerTests
{
    private static readonly LootTable Wolf = new("loot.wolf_grey", 0, ImmutableArray<LootEntry>.Empty,
        ImmutableArray.Create(
            new LootEntry("item.material.raw_meat", null, 0, 0.70, 1, 1),
            new LootEntry("item.material.wolf_hide", null, 0, 0.45, 1, 1),
            new LootEntry("item.trinket.wolf_fang", null, 0, 0.15, 1, 1)),
        ImmutableArray<LootEntry>.Empty);

    private static readonly LootTable Cache = new("loot.den_cache", 0, ImmutableArray<LootEntry>.Empty, ImmutableArray<LootEntry>.Empty,
        ImmutableArray.Create(
            new LootEntry("item.material.iron_ingot", null, 0, 0, 3, 3),
            new LootEntry("item.ammo.arrow_rough", null, 0, 0, 12, 12),
            new LootEntry("item.consumable.salve_minor", null, 0, 0, 2, 2)));

    private static readonly Dictionary<string, LootTable> Tables = new() { [Wolf.Id] = Wolf, [Cache.Id] = Cache };

    /// <summary>A seeded source keyed by sample index, like RngChannel: the same seed and index give the same value.</summary>
    private static Func<uint, double> Seeded(int seed) => sample => new Random(HashCode.Combine(seed, sample)).NextDouble();

    [Fact]
    public void AStaticTable_GivesExactlyItsContents() =>
        Assert.Equal(
            new[] { new LootDrop("item.ammo.arrow_rough", 12), new LootDrop("item.consumable.salve_minor", 2), new LootDrop("item.material.iron_ingot", 3) },
            LootRoller.Roll(Cache, Tables, Seeded(1)));

    [Fact]
    public void TheSameSource_RollsTheSameResult() =>
        Assert.Equal(LootRoller.Roll(Wolf, Tables, Seeded(42)).AsEnumerable(), LootRoller.Roll(Wolf, Tables, Seeded(42)).AsEnumerable());

    [Fact]
    public void AHundredThousandRolls_StayInsideTheDesignedBounds()
    {
        const int rolls = 100_000;
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < rolls; i++)
        {
            foreach (var drop in LootRoller.Roll(Wolf, Tables, Seeded(i)))
                counts[drop.ItemId] = counts.GetValueOrDefault(drop.ItemId) + drop.Count;
        }
        // Four standard deviations of a binomial at 100 000 draws is about 0.6 percentage points.
        Assert.InRange(counts["item.material.raw_meat"] / (double)rolls, 0.694, 0.706);
        Assert.InRange(counts["item.material.wolf_hide"] / (double)rolls, 0.444, 0.456);
        Assert.InRange(counts["item.trinket.wolf_fang"] / (double)rolls, 0.1445, 0.1555);
    }

    [Fact]
    public void WeightedRolls_AndNestedTables_Resolve()
    {
        var inner = new LootTable("loot.inner", 1, ImmutableArray.Create(new LootEntry("item.a", null, 1, 0, 2, 2)),
            ImmutableArray<LootEntry>.Empty, ImmutableArray<LootEntry>.Empty);
        var outer = new LootTable("loot.outer", 3, ImmutableArray.Create(new LootEntry(null, "loot.inner", 1, 0, 1, 1)),
            ImmutableArray<LootEntry>.Empty, ImmutableArray<LootEntry>.Empty);
        var tables = new Dictionary<string, LootTable> { [inner.Id] = inner, [outer.Id] = outer };

        Assert.Equal(new[] { new LootDrop("item.a", 6) }, LootRoller.Roll(outer, tables, Seeded(3)));
    }
}

public class EquipmentRuleTests
{
    private static ItemDefinition Bow(int might) => new("item.weapon.hunting_bow", "weapon", 1, 1_500, 45, "common", false, false,
        EquipSlot.MainHand, new Requirements(ImmutableSortedDictionary.CreateRange(new[] { KeyValuePair.Create(CharacterAttribute.Might, might) }),
            ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal)),
        new WeaponStats(9, 9, "physical_pierce", 0, true, null, "item.ammo.arrow_rough"), 0);

    [Fact]
    public void AnAttributeMinimum_GatesEquipping_NotALevel()
    {
        var rules = TestRules.Default();
        var fresh = ProgressionEngine.Create(rules);

        Assert.Null(EquipmentRules.Refusal(Bow(10), fresh, rules));
        Assert.Contains("needs might 11; you have 10", EquipmentRules.Refusal(Bow(11), fresh, rules));
    }

    [Fact]
    public void ATwoHander_OccupiesBothHands() =>
        Assert.Equal(new[] { EquipSlot.MainHand, EquipSlot.OffHand }, EquipmentRules.Occupies(Bow(0)));

    [Fact]
    public void ANonEquippableItem_IsRefused()
    {
        var herb = new ItemDefinition("item.material.herb_ashbloom", "material", 20, 50, 3, "common", false, false, null, Requirements.None, null, 0);
        Assert.Contains("cannot be equipped", EquipmentRules.Refusal(herb, CharacterProgression.Empty, TestRules.Default()));
    }
}

public class PricingTests
{
    private static readonly Merchant Smith = new("merchant.smith_orren",
        ImmutableArray.Create(new MerchantStock("item.ammo.arrow_rough", 60, 1.0)), ImmutableArray.Create("material", "misc"));

    private static ItemDefinition Item(string category, long value, bool noSell = false) =>
        new("item.x.y", category, 1, 100, value, "common", false, noSell, null, Requirements.None, null, 0);

    [Fact]
    public void AMerchantBuysAtTheSellRatio_SoResellingLosesMoney()
    {
        var pricing = new Pricing(0.4);
        var ore = Item("material", 25);
        Assert.Equal(10, pricing.SellPrice(ore, Smith));
        Assert.Equal(25, pricing.BuyPrice(ore, new MerchantStock(ore.Id, 1, 1.0)));
    }

    [Fact]
    public void AMerchantRefuses_NoSellItems_AndCategoriesItDoesNotBuy()
    {
        var pricing = new Pricing(0.4);
        Assert.Null(pricing.SellPrice(Item("quest_item", 0, noSell: true), Smith));
        Assert.Null(pricing.SellPrice(Item("book", 120), Smith));
    }
}
