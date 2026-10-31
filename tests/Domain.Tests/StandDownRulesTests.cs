// UNNAMED Domain tests - standing a duty construct down rather than breaking it (CMB-43)
using System.Collections.Immutable;
using UNNAMED.Domain.Combat;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// CMB-43's rule: a doorkeeper may be talked down, shown its own token, or walked through its ritual in order, and a construct that
/// answers none of them must be fought. Deterministic: the same attempt always gets the same verdict.
/// </summary>
public class StandDownRulesTests
{
    private static DutyTerms Court(ImmutableArray<StandDownRoute> routes) => new(
        routes,
        Rites: ImmutableArray.Create(new DutyRite("name_the_court", "Name the court."), new DutyRite("stand_the_measure", "Stand where the measure stands.")),
        TokenId: "item.quest.court_token",
        Words: ImmutableArray.Create("the measure is kept"),
        Voice: new DutyVoice("It turns.", "It stands down.", "It does not answer."));

    private static readonly ImmutableArray<StandDownRoute> All =
        ImmutableArray.Create(StandDownRoute.Word, StandDownRoute.Token, StandDownRoute.Rite);

    [Fact]
    public void TheRightWord_StandsItDown()
    {
        var result = StandDownRules.Answers(Court(All), new StandDownAttempt(StandDownRoute.Word, "the measure is kept", 0, false));
        Assert.True(result.Accepted);
        Assert.Equal("It stands down.", result.Said);
    }

    [Fact]
    public void TheWrongWord_DoesNot_AndTheRefusalIsInItsOwnVoice()
    {
        var result = StandDownRules.Answers(Court(All), new StandDownAttempt(StandDownRoute.Word, "let me past", 0, false));
        Assert.False(result.Accepted);
        Assert.Equal("It does not answer.", result.Said);
    }

    [Fact]
    public void ShowingTheToken_StandsItDown_ButOnlyIfItIsCarried()
    {
        Assert.True(StandDownRules.Answers(Court(All), new StandDownAttempt(StandDownRoute.Token, null, 0, true)).Accepted);
        Assert.False(StandDownRules.Answers(Court(All), new StandDownAttempt(StandDownRoute.Token, null, 0, false)).Accepted);
    }

    [Fact]
    public void ARouteTheCourtDoesNotKeep_IsRefusedEvenWithTheRightArgument()
    {
        var wordOnly = Court(ImmutableArray.Create(StandDownRoute.Word));
        Assert.True(StandDownRules.Answers(wordOnly, new StandDownAttempt(StandDownRoute.Word, "the measure is kept", 0, false)).Accepted);
        Assert.False(StandDownRules.Answers(wordOnly, new StandDownAttempt(StandDownRoute.Token, null, 0, true)).Accepted);
    }

    [Fact]
    public void TheRitualIsAnsweredStepByStep_InItsOwnOrder()
    {
        var court = Court(All);

        // First step asked, not answered yet: the construct prompts, and does not stand down.
        var first = StandDownRules.Answers(court, new StandDownAttempt(StandDownRoute.Rite, null, 0, false));
        Assert.False(first.Accepted);
        Assert.Equal("Name the court.", first.Said);

        // Out of order: the second step's id offered at the first step.
        Assert.False(StandDownRules.Answers(court, new StandDownAttempt(StandDownRoute.Rite, "stand_the_measure", 0, false)).Accepted);

        // In order: each step at its own place, and the last one completes it.
        Assert.False(StandDownRules.Answers(court, new StandDownAttempt(StandDownRoute.Rite, "name_the_court", 1, false)).Accepted);
        Assert.True(StandDownRules.Answers(court, new StandDownAttempt(StandDownRoute.Rite, "stand_the_measure", 2, false)).Accepted);
    }

    [Fact]
    public void AKillOnlyConstruct_IsNeverTalkedDown()
    {
        var killOnly = DutyTerms.None;
        Assert.False(killOnly.AnswersAny);
        Assert.False(StandDownRules.Answers(killOnly, new StandDownAttempt(StandDownRoute.Word, "the measure is kept", 0, true)).Accepted);
    }

    [Fact]
    public void BothSolvingsAreLegal_AndTheyPayDifferently()
    {
        var stood = StandDownRules.Reward(StandDownVerdict.Accepted, xpValue: 170);
        var killed = StandDownRules.Reward(StandDownVerdict.Refused, xpValue: 170);
        Assert.Equal(85, stood.Xp);
        Assert.False(stood.Loot);
        Assert.True(stood.Knowledge);
        Assert.Equal(170, killed.Xp);
        Assert.True(killed.Loot);
        Assert.False(killed.Knowledge);
    }
}
