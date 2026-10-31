// UNNAMED Domain tests - the rules that make a rival race fair, deterministic and worth remembering (R12)
using System.Collections.Immutable;
using UNNAMED.Domain.Social;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// R12's rules on their own: when a rival finishes a posting, who won, and what they say about it. No world, no random: the same
/// seed and the same posting always produce the same race, which is what makes a save reproducible.
/// </summary>
public class RivalRulesTests
{
    private static RivalProfile Rival(string npc = "npc.road.jessa_vane", int pace = 1000, int bounties = 0) => new(
        npc, People: "veth", Call: "call.wayfarer", PacePermille: pace, SkillPermille: 500,
        Bounties: bounties == 0 ? ImmutableArray<string>.Empty : ImmutableArray.Create("quest.bounty.road_wolves"),
        Taunts: new RivalTaunts(
            ImmutableArray.Create("met one", "met two"),
            ImmutableArray.Create("won one", "won two"),
            ImmutableArray.Create("lost one", "lost two")));

    [Fact]
    public void ASlowerRivalTakesLongerToFinishTheSamePosting()
    {
        var fast = RivalRules.AttemptTick(postedTick: 1_000, workTicks: 2_000, Rival(pace: 1_250), seed: 0);
        var slow = RivalRules.AttemptTick(postedTick: 1_000, workTicks: 2_000, Rival(pace: 800), seed: 0);
        Assert.True(slow > fast, $"800 per mille is slower than 1250: {slow} against {fast}");
    }

    [Fact]
    public void TheSameSeedAndPostingAlwaysProduceTheSameRace()
    {
        var a = RivalRules.AttemptTick(500, 1_000, Rival(), seed: 42);
        var b = RivalRules.AttemptTick(500, 1_000, Rival(), seed: 42);
        Assert.Equal(a, b);
    }

    [Fact]
    public void NoRaceIsShorterThanTheFloor()
    {
        var attempt = RivalRules.AttemptTick(postedTick: 100, workTicks: 1, Rival(pace: 2_000), seed: 0);
        Assert.True(attempt >= 100 + RivalRules.ShortestRaceTicks, $"{attempt} respects the floor");
    }

    [Fact]
    public void FinishingOnTheSameTickAsTheRivalIsAWinForTheCharacter()
    {
        Assert.Equal(RivalOutcome.PlayerFirst, RivalRules.Decide(attemptTick: 900, playerFinishedTick: 900));
        Assert.Equal(RivalOutcome.PlayerFirst, RivalRules.Decide(attemptTick: 900, playerFinishedTick: 899));
        Assert.Equal(RivalOutcome.RivalFirst, RivalRules.Decide(attemptTick: 900, playerFinishedTick: 901));
        Assert.Equal(RivalOutcome.RivalFirst, RivalRules.Decide(attemptTick: 900, playerFinishedTick: null));
    }

    [Fact]
    public void BothOutcomesAreRemembered_AndTheTallyMovesTheRegard()
    {
        var memory = RivalMemory.None;
        Assert.Equal(RivalRegard.Stranger, memory.Regard);

        memory = RivalRules.After(memory, RivalOutcome.RivalFirst, "quest.a", 10);
        Assert.Equal(1, memory.Wins);
        Assert.Equal(RivalRegard.Wary, memory.Regard);

        memory = RivalRules.After(memory, RivalOutcome.PlayerFirst, "quest.b", 20);
        Assert.Equal(1, memory.Losses);
        Assert.Equal(2, memory.Contests);

        memory = RivalRules.After(memory, RivalOutcome.PlayerFirst, "quest.c", 30);
        Assert.Equal(RivalRegard.Respectful, memory.Regard);
        Assert.Equal("quest.c", memory.LastQuestId);
        Assert.Equal(30, memory.LastTick);
    }

    [Fact]
    public void ATwiceBeatenRivalRespectsTheCharacter_AndATwiceWinningOneDoesNot()
    {
        var respectful = new RivalMemory(Wins: 0, Losses: 2, 0, null);
        var contemptuous = new RivalMemory(Wins: 2, Losses: 0, 0, null);
        Assert.Equal(RivalRegard.Respectful, respectful.Regard);
        Assert.Equal(RivalRegard.Contemptuous, contemptuous.Regard);
    }

    [Fact]
    public void TheTauntRotatesWithTheContests_SoNobodyRepeatsThemselvesTwiceRunning()
    {
        var rival = Rival();
        string? first = RivalRules.TauntFor(rival, RivalMoment.Beaten, RivalMemory.None);
        string? second = RivalRules.TauntFor(rival, RivalMoment.Beaten, new RivalMemory(Wins: 1, Losses: 0, 0, null));
        string? third = RivalRules.TauntFor(rival, RivalMoment.Beaten, new RivalMemory(Wins: 2, Losses: 0, 0, null));
        Assert.Equal("won one", first);
        Assert.Equal("won two", second);
        Assert.Equal("won one", third);
    }

    [Fact]
    public void ARivalWithNothingToSayStaysQuiet()
    {
        var silent = Rival() with { Taunts = RivalTaunts.Empty };
        Assert.Null(RivalRules.TauntFor(silent, RivalMoment.Bested, RivalMemory.None));
    }

    [Fact]
    public void ARivalsListOfBountiesDecidesWhatTheyWork_AndAnEmptyListIsAnyOfThem()
    {
        Assert.True(Rival(bounties: 0).Wants("quest.bounty.anything"));
        var picky = Rival(bounties: 1);
        Assert.True(picky.Wants("quest.bounty.road_wolves"));
        Assert.False(picky.Wants("quest.bounty.crossing_timber"));
    }
}
