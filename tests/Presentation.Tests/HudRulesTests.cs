using UNNAMED.Presentation.Settings;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

/// <summary>The HUD's player options (WP-1E, SYS-56): how an enemy's condition reads, and what a blow's number says.</summary>
public class HudRulesTests
{
    [Theory]
    [InlineData(40, "Unhurt")]
    [InlineData(31, "Grazed")]
    [InlineData(30, "Wounded")]
    [InlineData(17, "Wounded")]
    [InlineData(16, "Badly wounded")]
    [InlineData(7, "Badly wounded")]
    [InlineData(6, "Near death")]
    [InlineData(0, "Down")]
    public void TheConditionWord_FollowsWhatIsLeft(int health, string word) => Assert.Equal(word, HudRules.ConditionWord(health, 40));

    [Fact]
    public void BeforeTheFight_NothingIsShown_WhateverTheChoice()
    {
        foreach (var mode in Enum.GetValues<EnemyCondition>())
            Assert.Equal(new ConditionRead(null, false, null), HudRules.Condition(mode, 40, 40, fightOn: false));
    }

    [Fact]
    public void InTheFight_EachChoiceShowsItsOwnRead()
    {
        Assert.Equal(new ConditionRead("Wounded", false, null), HudRules.Condition(EnemyCondition.Words, 20, 40, true));
        Assert.Equal(new ConditionRead("Wounded", true, null), HudRules.Condition(EnemyCondition.WordsAndBar, 20, 40, true));
        Assert.Equal(new ConditionRead(null, true, "20 / 40"), HudRules.Condition(EnemyCondition.Numbers, 20, 40, true));
    }

    [Fact]
    public void DamageNumbers_AreOffByDefault_AndOnlyForOurSidesBlows()
    {
        var off = new HudPrefs();
        var on = off with { DamageNumbers = true };
        Assert.Null(HudRules.DamageNumber(off, true, 12, false, false, false));
        Assert.Null(HudRules.DamageNumber(on, false, 12, false, false, false));   // a blow on the character is not ours to count
        Assert.Equal("12", HudRules.DamageNumber(on, true, 12, false, false, false));
        Assert.Equal("12!", HudRules.DamageNumber(on, true, 12, true, false, false));
        Assert.Equal("3 (blocked)", HudRules.DamageNumber(on, true, 3, false, false, true));
        Assert.Equal("Dodged", HudRules.DamageNumber(on, true, 0, false, true, false));
    }
}
