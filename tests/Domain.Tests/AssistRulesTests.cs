// UNNAMED Domain tests - GEN-10's rules: how wide a generous window is, and what a telegraph palette may never re-tint.

using UNNAMED.Domain.Combat;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// The two assists that are rules rather than pictures: a generous timing window, and a telegraph palette that must stay readable for a
/// red-green colour-blind player without taking violet away from the Other.
/// </summary>
public class AssistRulesTests
{
    [Theory]
    [InlineData(1, 2)]      // half again as long is one and a half ticks: a window that cannot grow is not a wider one
    [InlineData(5, 8)]      // the dodge: 5 x 1.5 = 7.5, rounded up
    [InlineData(8, 12)]     // the parry: exactly half again as long
    [InlineData(7, 11)]     // 10.5 rounds up to 11
    [InlineData(12, 18)]
    public void AGenerousWindowIsHalfAgainAsLong_AndNeverNarrower(int normal, int generous)
    {
        Assert.Equal(normal, AssistRules.WindowTicks(normal, TimingWindows.Normal));
        Assert.Equal(generous, AssistRules.WindowTicks(normal, TimingWindows.Generous));
        Assert.True(AssistRules.WindowTicks(normal, TimingWindows.Generous) > normal);
    }

    [Fact]
    public void AWindowOfNothingStaysNothing()
    {
        Assert.Equal(0, AssistRules.WindowTicks(0, TimingWindows.Generous));
        Assert.Equal(-3, AssistRules.WindowTicks(-3, TimingWindows.Generous));
    }

    [Fact]
    public void OnlyTheDodgeWidensInTheConstants_TheRestOfTheFightIsUntouched()
    {
        var normal = new CombatConstants();

        Assert.Same(normal, AssistRules.Windowed(normal, TimingWindows.Normal));

        var generous = AssistRules.Windowed(normal, TimingWindows.Generous);
        Assert.Equal(AssistRules.WindowTicks(normal.DodgeTicks, TimingWindows.Generous), generous.DodgeTicks);
        // Everything else is the fight as it was tuned: the assist widens the answer, never the blow.
        Assert.Equal(normal.DodgeRecoveryTicks, generous.DodgeRecoveryTicks);
        Assert.Equal(normal.DodgeStaminaCost, generous.DodgeStaminaCost);
        Assert.Equal(normal.DodgeDistanceMm, generous.DodgeDistanceMm);
    }

    [Theory]
    [InlineData(TelegraphPalette.Amber)]
    [InlineData(TelegraphPalette.Orange)]
    public void TheAlternativePalettes_MoveBothTelegraphs_OffTheRedOrangeWarning(TelegraphPalette palette)
    {
        var drawn = Telegraphs.Of(TelegraphPalette.Default, TelegraphRole.EnemyBlow);
        var mine = Telegraphs.Of(TelegraphPalette.Default, TelegraphRole.YourWindow);
        var enemy = Telegraphs.Of(palette, TelegraphRole.EnemyBlow);
        var window = Telegraphs.Of(palette, TelegraphRole.YourWindow);

        Assert.False(enemy.Is(drawn), $"{palette}'s warning is still the drawn red-orange");
        Assert.False(window.Is(mine), $"{palette}'s own window is still the drawn green");
        // The two must never be one colour: a dichromat reads them as the same ink otherwise.
        Assert.False(enemy.Is(window), $"{palette} draws the blow and the window the same");
        Assert.False(enemy.Is(Telegraphs.Other), $"{palette} draws a blow in the Other's violet");
    }

    [Theory]
    [InlineData(TelegraphPalette.Default)]
    [InlineData(TelegraphPalette.Amber)]
    [InlineData(TelegraphPalette.Orange)]
    public void VioletIsTheOthers_InEveryPalette(TelegraphPalette palette)
    {
        var other = Telegraphs.Of(palette, TelegraphRole.Other);
        Assert.True(other.Is(Telegraphs.Other));
        // and it is not one of the two telegraphs either, so the register never doubles as a warning
        Assert.False(other.Is(Telegraphs.Of(palette, TelegraphRole.EnemyBlow)));
        Assert.False(other.Is(Telegraphs.Of(palette, TelegraphRole.YourWindow)));
    }

    [Fact]
    public void TheAuthoredPalette_KeepsAWarmWarningAndAGreenWindow()
    {
        Assert.True(Telegraphs.IsAuthored(TelegraphPalette.Default));
        Assert.False(Telegraphs.IsAuthored(TelegraphPalette.Amber));

        var warning = Telegraphs.Of(TelegraphPalette.Default, TelegraphRole.EnemyBlow);
        Assert.True(warning.R > warning.G && warning.G >= warning.B, "the drawn warning is warm");
    }

    [Fact]
    public void AnUnknownRoleIsRefused_NotDrawnInSomeOtherColour()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Telegraphs.Of(TelegraphPalette.Default, (TelegraphRole)99));
    }
}
