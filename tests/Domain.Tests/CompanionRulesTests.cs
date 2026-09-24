using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Tests;

/// <summary>A companion's pure rules (M6): how fast to keep up, when to catch up, and how they are in words.</summary>
public class CompanionRulesTests
{
    private static readonly CompanionTuning Tuning = new(2_500, 4_000, 8_000, 30_000, 80, 1_000, 48, 10_000, 16_000, 4_000, 12, 1_200, 40, 200, 2);

    [Fact]
    public void Following_StandsClose_WalksNear_RunsFarther_SprintsFar()
    {
        Assert.Null(CompanionRules.FollowGait(2_500, Tuning));
        Assert.Equal(Gait.Walk, CompanionRules.FollowGait(2_501, Tuning));
        Assert.Equal(Gait.Walk, CompanionRules.FollowGait(4_000, Tuning));
        Assert.Equal(Gait.Run, CompanionRules.FollowGait(4_001, Tuning));
        Assert.Equal(Gait.Sprint, CompanionRules.FollowGait(8_001, Tuning));
    }

    [Fact]
    public void TooFarBehind_OrSnaggedTooLong_TheyCatchUp()
    {
        Assert.Null(CompanionRules.CatchUp(30_000, 79, Tuning));
        Assert.Equal("distance", CompanionRules.CatchUp(30_001, 0, Tuning));
        Assert.Equal("snag", CompanionRules.CatchUp(5_000, 80, Tuning));
        Assert.Equal("distance", CompanionRules.CatchUp(40_000, 80, Tuning));   // distance first
    }

    [Fact]
    public void HowTheyAre_IsWords_NotNumbers()
    {
        Assert.Equal("healthy", CompanionRules.Condition(CompanionCondition.Up, 75, 100));
        Assert.Equal("wounded", CompanionRules.Condition(CompanionCondition.Up, 74, 100));
        Assert.Equal("wounded", CompanionRules.Condition(CompanionCondition.Up, 30, 100));
        Assert.Equal("critical", CompanionRules.Condition(CompanionCondition.Up, 29, 100));
        Assert.Equal("downed", CompanionRules.Condition(CompanionCondition.Downed, 0, 100));
    }

    [Fact]
    public void TheTuning_MustNest_AndBeSensible()
    {
        Assert.Null(Tuning.Problem());
        Assert.Contains("nest", Tuning with { RunBeyondMm = 2_000 } is var bad ? bad.Problem() : null);
        Assert.Contains("revive share", (Tuning with { RevivePercent = 0 }).Problem());
        Assert.Contains("leash", (Tuning with { LeashMm = 5_000 }).Problem());
        Assert.Contains("trail", (Tuning with { TrailLength = 1 }).Problem());
    }

    [Fact]
    public void OrdersAndConditions_AreSavedByName()
    {
        foreach (var order in Enum.GetValues<CompanionOrder>())
            Assert.Equal(order, CompanionKeys.ParseOrder(CompanionKeys.Key(order)));
        foreach (var condition in Enum.GetValues<CompanionCondition>())
            Assert.Equal(condition, CompanionKeys.ParseCondition(CompanionKeys.Key(condition)));
        Assert.Null(CompanionKeys.ParseOrder("guard"));
        Assert.Null(CompanionKeys.ParseCondition("dead"));
    }
}
