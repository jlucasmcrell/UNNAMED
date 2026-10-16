using System.Collections.Immutable;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;

namespace UNNAMED.Domain.Tests.Items;

/// <summary>
/// WP3a's itemization rules: a class with its rarity derived, masterwork as craft-only quality, armour moved by quality, drop quality by the
/// creature's level, a seeded experiment set, and a notable instance's familiarity.
/// </summary>
public class ItemizationRuleTests
{
    private static readonly CraftingConstants Rules = new();

    private static ItemDefinition Armour(int armor, ItemClass itemClass = ItemClass.Ordinary) =>
        new("item.armor.test", "armor", 1, 1000, 10, ItemClasses.Rarity(itemClass), false, false, EquipSlot.Chest, Requirements.None, null, armor)
        {
            Class = itemClass,
        };

    [Fact]
    public void EveryClass_HasOneRarity_AndItsKeyRoundTrips()
    {
        Assert.Equal(new[] { "common", "uncommon", "rare", "artifact" }, Enum.GetValues<ItemClass>().Select(ItemClasses.Rarity));
        foreach (var value in Enum.GetValues<ItemClass>())
            Assert.Equal(value, ItemClasses.Parse(ItemClasses.Key(value)));
        Assert.Throws<FormatException>(() => ItemClasses.Parse("legendary"));
    }

    [Fact]
    public void AnOrdinaryMake_AtMasterworkQuality_IsAMasterwork_AndKeepsARecord()
    {
        Assert.Equal(ItemClass.Ordinary, ItemClasses.Of(Armour(3), Quality.Fine));
        Assert.False(ItemClasses.KeepsRecord(Armour(3), Quality.Fine));
        Assert.Equal(ItemClass.Masterwork, ItemClasses.Of(Armour(3), Quality.Masterwork));
        Assert.True(ItemClasses.KeepsRecord(Armour(3), Quality.Masterwork));
        Assert.Equal(ItemClass.Notable, ItemClasses.Of(Armour(3, ItemClass.Notable), Quality.Standard));
        Assert.True(ItemClasses.KeepsRecord(Armour(3, ItemClass.Notable), Quality.Crude));
    }

    [Fact]
    public void ArmourQuality_MovesArmourAStepAStep_NeverBelowNothing()
    {
        Assert.Equal(new[] { 2, 3, 4, 5 }, new[] { Quality.Crude, Quality.Standard, Quality.Fine, Quality.Masterwork }
            .Select(q => EquipmentRules.ArmorOf(Armour(3), q, Rules.ArmorPerStep)));
        Assert.Equal(0, EquipmentRules.ArmorOf(Armour(0), Quality.Masterwork, Rules.ArmorPerStep));   // no armour, nothing to move
        Assert.Equal(0, EquipmentRules.ArmorOf(Armour(1), Quality.Crude, 5));
    }

    [Fact]
    public void Masterwork_OnlyFromAFineRoll_OfAPossibleRecipe_AtAnUpgradedStation()
    {
        var always = Rules with { MasterworkPercentOfFine = 100 };
        Assert.Equal(Quality.Masterwork, CraftingRules.Masterwork(Quality.Fine, true, 1, always, 0.99));
        Assert.Equal(Quality.Fine, CraftingRules.Masterwork(Quality.Fine, false, 1, always, 0.0));    // the recipe does not allow it
        Assert.Equal(Quality.Fine, CraftingRules.Masterwork(Quality.Fine, true, 0, always, 0.0));     // a common station
        Assert.Equal(Quality.Standard, CraftingRules.Masterwork(Quality.Standard, true, 1, always, 0.0));
        Assert.Equal(Quality.Masterwork, CraftingRules.Masterwork(Quality.Fine, true, 1, Rules, 0.24));
        Assert.Equal(Quality.Fine, CraftingRules.Masterwork(Quality.Fine, true, 1, Rules, 0.25));      // a quarter of fine pieces
    }

    [Fact]
    public void DropQuality_FollowsTheCreaturesLevelBand_AndIsNeverMasterwork()
    {
        var bands = new[] { new DropQualityBand(1, 30, 0), new DropQualityBand(5, 10, 20) };
        Assert.Equal(Quality.Standard, CraftingRules.DropQuality(0, bands, 0.0));   // below every band
        Assert.Equal(Quality.Crude, CraftingRules.DropQuality(3, bands, 0.29));
        Assert.Equal(Quality.Standard, CraftingRules.DropQuality(3, bands, 0.999)); // level 3 never drops fine
        Assert.Equal(Quality.Fine, CraftingRules.DropQuality(7, bands, 0.85));
        Assert.Equal(Quality.Crude, CraftingRules.DropQuality(7, bands, 0.05));
        var rolled = Enumerable.Range(0, 1000).Select(i => CraftingRules.DropQuality(9, bands, i / 1000.0)).ToList();
        Assert.Equal((100, 700, 200), (rolled.Count(q => q == Quality.Crude), rolled.Count(q => q == Quality.Standard), rolled.Count(q => q == Quality.Fine)));
        Assert.DoesNotContain(Quality.Masterwork, rolled);
    }

    [Fact]
    public void TheExperimentSet_IsASeededSubset_TheSameForTheSameSeed()
    {
        string[] candidates = { "recipe.c", "recipe.a", "recipe.d", "recipe.b" };
        Func<uint, double> Seeded(int seed) => sample => new Random(HashCode.Combine(seed, sample)).NextDouble();

        var set = CraftingRules.ExperimentSet(candidates, 2, Seeded(7));
        Assert.Equal(2, set.Length);
        Assert.All(set, id => Assert.Contains(id, candidates));
        Assert.Equal(set.OrderBy(i => i, StringComparer.Ordinal), set);
        Assert.Equal(set.AsEnumerable(), CraftingRules.ExperimentSet(candidates.Reverse(), 2, Seeded(7)).AsEnumerable());   // input order does not matter
        Assert.Equal(candidates.Order(StringComparer.Ordinal), CraftingRules.ExperimentSet(candidates, 9, Seeded(7)));      // never more than there are
        Assert.True(Enumerable.Range(0, 50).Select(s => string.Join(",", CraftingRules.ExperimentSet(candidates, 2, Seeded(s)))).Distinct().Count() > 1);
    }

    [Fact]
    public void Familiarity_RisesAStepAtEachStepsCount_AndStopsAtFive()
    {
        var steps = ImmutableArray.Create(1, 2, 2, 2, 2);
        var record = new NotableRecord("item.weapon.haldas_bow", ProvenanceKind.Given, "quest.test", 10);
        Assert.True(record.IsValid);
        record = record.Used(steps);
        Assert.Equal((1, 0), (record.Familiarity, record.Uses));
        record = record.Used(steps);
        Assert.Equal((1, 1), (record.Familiarity, record.Uses));
        for (int i = 0; i < 20; i++)
            record = record.Used(steps);
        Assert.Equal((NotableRecord.MaxFamiliarity, 0), (record.Familiarity, record.Uses));
        Assert.True(record.IsValid);
    }

    [Fact]
    public void ACraftedRecord_CarriesItsMakersMark()
    {
        Assert.False(new NotableRecord("item.armor.test", ProvenanceKind.Crafted, "recipe.test", 0).IsValid);
        Assert.True(new NotableRecord("item.armor.test", ProvenanceKind.Crafted, "recipe.test", 0) { Maker = "Aelin" }.IsValid);
        Assert.False(new NotableRecord("item.armor.test", ProvenanceKind.Found, "chest", 0) { Maker = "Aelin" }.IsValid);
        Assert.False(new NotableRecord("item.armor.test", ProvenanceKind.Found, "chest", 0) { Familiarity = 6 }.IsValid);
    }
}
