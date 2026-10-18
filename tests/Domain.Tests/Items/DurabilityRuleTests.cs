using System.Collections.Immutable;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;

namespace UNNAMED.Domain.Tests.Items;

/// <summary>WP3b's pure rules: wear up to a worn-out piece that counts as crude (D5), a repair capped at the made quality, and the laden band (D4).</summary>
public class DurabilityRuleTests
{
    private static readonly ItemDefinition Sword = new("item.weapon.test_sword", "weapon", 1, 3_000, 15, "common", false, false, EquipSlot.MainHand,
        Requirements.None, null, 0) { DurabilityMax = 10 };

    [Fact]
    public void Wear_CountsUp_AndAtItsDurabilityThePieceIsWornOut_AndReadsCrude_NeverLower()
    {
        var (quality, wear) = Durability.Worn(Quality.Fine, null, 10, 4);
        Assert.Equal((Quality.Fine, new ItemWear(4, Quality.Fine)), (quality, wear));
        Assert.False(Durability.IsWornOut(wear, 10));
        Assert.Equal(6, Durability.Remaining(wear, 10));

        (quality, wear) = Durability.Worn(quality, wear, 10, 50);
        Assert.Equal((Quality.Crude, new ItemWear(10, Quality.Fine)), (quality, wear));   // capped at its durability, its make kept
        Assert.True(Durability.IsWornOut(wear, 10));
        Assert.Equal(0, Durability.Remaining(wear, 10));
        Assert.Equal((Quality.Crude, wear), Durability.Worn(quality, wear, 10, 1));     // nothing further: never broken, never gone
    }

    [Fact]
    public void SomethingWithoutDurability_TakesNoWear() =>
        Assert.Equal((Quality.Standard, (ItemWear?)null), Durability.Worn(Quality.Standard, null, 0, 5));

    [Theory]
    [InlineData(Quality.Fine, Quality.Fine)]
    [InlineData(Quality.Masterwork, Quality.Fine)]       // never above what it was made at
    [InlineData(Quality.Standard, Quality.Standard)]
    [InlineData(Quality.Crude, Quality.Crude)]
    public void ARepair_IsCappedAtTheMadeQuality(int rolled, int expected)
    {
        var (quality, wear) = Durability.Repaired(Quality.Crude, new ItemWear(10, Quality.Fine), rolled);
        Assert.Equal(expected, quality);
        Assert.Equal(expected == Quality.Fine ? null : new ItemWear(0, Quality.Fine), wear);   // unworn at its make carries nothing
    }

    [Fact]
    public void OnlyWearOrALostStep_NeedsRepair()
    {
        Assert.False(Durability.NeedsRepair(Quality.Standard, null));
        Assert.True(Durability.NeedsRepair(Quality.Standard, new ItemWear(1, Quality.Standard)));
        Assert.True(Durability.NeedsRepair(Quality.Standard, new ItemWear(0, Quality.Fine)));   // a poor repair can be tried again
    }

    [Fact]
    public void ATradersFee_IsAShareOfTheValue_ScaledByTheWear_AtLeastOneCoin()
    {
        var rules = DurabilityRules.Default with { MerchantFeePercent = 40 };
        Assert.Equal(6L, Durability.MerchantFee(Sword, new ItemWear(10, Quality.Standard), rules));   // 15 x 40%
        Assert.Equal(3L, Durability.MerchantFee(Sword, new ItemWear(5, Quality.Standard), rules));
        Assert.Equal(1L, Durability.MerchantFee(Sword, new ItemWear(1, Quality.Standard), rules));
    }

    [Theory]
    [InlineData(10, 0, 10)]
    [InlineData(10, 5, 7)]    // half worn: a quarter off, rounded down (50% at full wear)
    [InlineData(10, 10, 5)]   // worn out: half
    [InlineData(10, 40, 5)]   // wear never counts past its durability
    [InlineData(1, 10, 1)]    // anything that sells for something sells for at least 1
    [InlineData(0, 10, 0)]
    public void AWornPiece_SellsForLess_ByTheShareATraderWouldChargeToMendIt(long offer, int points, long expected) =>
        Assert.Equal(expected, Durability.SalePrice(offer, Sword, new ItemWear(points, Quality.Standard), DurabilityRules.Default));

    [Fact]
    public void MendingAWornPiece_ToSellIt_NeverPaysForItself()
    {
        var rules = DurabilityRules.Default;
        for (long offer = 1; offer <= Sword.ValueBase; offer++)
            for (int points = 1; points <= Sword.DurabilityMax; points++)
            {
                var wear = new ItemWear(points, Quality.Standard);
                long gain = Durability.SalePrice(offer, Sword, null, rules) - Durability.SalePrice(offer, Sword, wear, rules);
                Assert.True(gain <= Durability.MerchantFee(Sword, wear, rules), $"offer {offer}, wear {points}: mending gains {gain}");
            }
    }

    private static readonly ItemDefinition Dear = Sword with { ValueBase = 3_000 };
    private static readonly DurabilityRules Config = DurabilityRules.Default with { MerchantFeePercent = 40 };

    [Theory]
    [InlineData(3_000, 0, 3_000)]
    [InlineData(3_000, 5, 2_400)]    // half worn: a fifth off
    [InlineData(3_000, 10, 1_800)]   // fully worn: the 40% drop the sell side makes
    public void AWornPiece_OnAShelf_CostsLess_ByTheSameCurve(long ask, int points, long expected) =>
        Assert.Equal(expected, Durability.ShelfPrice(ask, Dear, new ItemWear(points, Quality.Standard), Config));

    [Fact]
    public void BuyingAWornPiece_AndMendingIt_NeverCostsLessThanBuyingItWhole()
    {
        var rules = Config;
        for (long ask = 1; ask <= 2 * Dear.ValueBase; ask += 7)
            for (int points = 1; points <= Dear.DurabilityMax; points++)
            {
                var wear = new ItemWear(points, Quality.Standard);
                long shelf = Durability.ShelfPrice(ask, Dear, wear, rules);
                Assert.InRange(shelf, 1, ask);
                Assert.True(shelf + Durability.MerchantFee(Dear, wear, rules) >= ask, $"ask {ask}, wear {points}: worn {shelf} + mend is cheaper");
            }
    }

    [Fact]
    public void ThePieceWithoutDurability_SellsAtTheOffer() =>
        Assert.Equal(7, Durability.SalePrice(7, Sword with { DurabilityMax = 0 }, new ItemWear(3, Quality.Standard), DurabilityRules.Default));

    [Theory]
    [InlineData(24_000, 30_000, false)]   // exactly 80%: not above the band
    [InlineData(24_001, 30_000, true)]
    [InlineData(30_000, 30_000, true)]
    [InlineData(0, 0, false)]
    public void TheLadenBand_IsAboveEightyPercentOfTheLimit(long carried, long limit, bool laden) =>
        Assert.Equal(laden, LadenRules.Default.IsLaden(carried, limit));

    [Fact]
    public void QualityKeys_ParseBack() =>
        Assert.All(new[] { Quality.Crude, Quality.Standard, Quality.Fine, Quality.Masterwork }, q => Assert.Equal(q, Quality.Parse(Quality.Key(q))));
}
