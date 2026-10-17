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
