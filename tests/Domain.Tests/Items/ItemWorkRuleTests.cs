using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;

namespace UNNAMED.Domain.Tests.Items;

/// <summary>
/// Queue #5's pure rules: what each reforged tier asks and gives, a working's stability as it is cut, and how reforging and use drift it until
/// it fades - never harming the piece (canon 12 §5.4, 13 §6.2).
/// </summary>
public class ItemWorkRuleTests
{
    private static readonly UpgradeRules Rules = UpgradeRules.Default;
    private static readonly RepairSpec AtTheAnvil = new("anvil", "skill.smithing", 2, "item.material.iron_ingot");
    private static readonly WorkingDefinition Keen = new("working.keen_edge", "Keen Edge", WorkingTarget.Weapon, 0, "item.trinket.wolf_fang", 1, 3) { Damage = 1 };
    private static readonly WorkingDefinition Ward = new("working.anchored_ward", "Anchored Ward", WorkingTarget.Armor, 15, "item.material.spider_silk", 4, 4) { Armor = 2 };

    [Fact]
    public void EachTier_AsksMoreOfTheHandAndMoreMaterial_ThanTheLast()
    {
        Assert.Equal(new[] { 12, 22, 32 }, Enumerable.Range(0, Rules.MaxTier).Select(t => Rules.SkillFor(AtTheAnvil, t)));
        Assert.Equal(new[] { 2, 4, 6 }, Enumerable.Range(0, Rules.MaxTier).Select(Rules.MaterialFor));
    }

    [Fact]
    public void APieceWithNothingOnIt_CarriesNoWork_AndOneWithTiersOrAWorkingDoes()
    {
        Assert.Null(ItemWork.Of(0, null));
        Assert.Equal(new ItemWork(2, null), ItemWork.Of(2, null));
        Assert.False(new ItemWork(0, null).IsValid);
        Assert.False(new ItemWork(1, new Inscription("working.keen_edge", 0)).IsValid);
        Assert.True(new ItemWork(0, new Inscription("working.keen_edge", 1)).IsValid);
    }

    [Fact]
    public void Stability_IsTheWorkingsOwn_PlusAStepOfQuality_PlusTheHandPastItsLeast()
    {
        Assert.Equal(3, Rules.StabilityOf(Keen, Quality.Standard, 0));
        Assert.Equal(4, Rules.StabilityOf(Keen, Quality.Fine, 14));
        Assert.Equal(6, Rules.StabilityOf(Keen, Quality.Masterwork, 15));
        Assert.Equal(2, Rules.StabilityOf(Keen, Quality.Crude, 0));
        Assert.Equal(5, Rules.StabilityOf(Ward, Quality.Standard, 30));
        Assert.Equal(1, Rules.StabilityOf(Keen with { Stability = 1 }, Quality.Crude, 0));   // never below one
    }

    [Fact]
    public void Reforging_RaisesTheTier_AndCostsTheWorkingAPoint_UntilItFades()
    {
        var work = UpgradeRules.Reforged(null, out var faded);
        Assert.Equal((new ItemWork(1, null), (Inscription?)null), (work, faded));

        work = UpgradeRules.Reforged(new ItemWork(1, new Inscription(Keen.Id, 2)), out faded);
        Assert.Equal(new ItemWork(2, new Inscription(Keen.Id, 1)), work);
        Assert.Null(faded);

        work = UpgradeRules.Reforged(work, out faded);
        Assert.Equal(new ItemWork(3, null), work);
        Assert.Equal(new Inscription(Keen.Id, 1), faded);
    }

    [Fact]
    public void Use_DriftsTheWorking_APointForEveryStretchOfWear_AndAtNoneItFades()
    {
        var work = new ItemWork(0, new Inscription(Keen.Id, 2));
        Assert.Same(work, Rules.Worn(work, 0, Rules.WearPerStability - 1, out var faded));
        Assert.Null(faded);

        var drifted = Rules.Worn(work, Rules.WearPerStability - 1, Rules.WearPerStability, out faded);
        Assert.Equal(new ItemWork(0, new Inscription(Keen.Id, 1)), drifted);
        Assert.Null(faded);

        Assert.Null(Rules.Worn(drifted, Rules.WearPerStability, 3 * Rules.WearPerStability, out faded));   // the piece is plain again
        Assert.Equal(Keen.Id, faded?.WorkingId);

        var tiered = new ItemWork(2, new Inscription(Keen.Id, 1));
        Assert.Equal(new ItemWork(2, null), Rules.Worn(tiered, 0, Rules.WearPerStability, out _));   // the tiers stay
        Assert.Equal(new ItemWork(2, null), Rules.Worn(new ItemWork(2, null), 0, 100, out _));
        Assert.Same(work, Rules.Worn(work, 15, 0, out _));   // a repair takes wear away; nothing drifts back
    }

    [Fact]
    public void TheTiersAndAWorking_AddToBlowsOrArmour_EachOnlyWhereItBelongs()
    {
        var keen = new ItemWork(2, new Inscription(Keen.Id, 3));
        Assert.Equal(2 * Rules.DamagePerTier + 1, Rules.DamageBonus(keen, Keen));
        Assert.Equal(2 * Rules.ArmorPerTier, Rules.ArmorBonus(keen, Keen));
        var ward = new ItemWork(1, new Inscription(Ward.Id, 3));
        Assert.Equal(Rules.ArmorPerTier + 2, Rules.ArmorBonus(ward, Ward));
        Assert.Equal(Rules.DamagePerTier, Rules.DamageBonus(ward, Ward));
        Assert.Equal(0, Rules.DamageBonus(null, null));
        // A working whose definition left the content does nothing; the tiers still count.
        Assert.Equal(Rules.DamagePerTier, Rules.DamageBonus(new ItemWork(1, new Inscription("working.gone", 2)), null));
    }
}
