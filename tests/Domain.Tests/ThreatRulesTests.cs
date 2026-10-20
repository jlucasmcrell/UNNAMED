using UNNAMED.Domain.Creatures;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// What the nameplate reads off a creature (owner finding 24): how the fight would go from the level gap, and what the creature will do when
/// it notices the character. The content it was tuned against is levels 1 to 5 - a chicken at 1, the grey wolf at 2, the Barrowback at 5.
/// </summary>
public class ThreatRulesTests
{
    [Theory]
    [InlineData(1, 1, Threat.Even)]        // a level-1 hen beside a level-1 character
    [InlineData(2, 1, Threat.Even)]        // the grey wolf: the fight the player takes first
    [InlineData(3, 2, Threat.Even)]
    [InlineData(4, 1, Threat.Hard)]        // the bristleback boar
    [InlineData(5, 1, Threat.Deadly)]      // the Barrowback: finding 24's "can or should I fight it" is answered no
    [InlineData(1, 5, Threat.Trivial)]     // and the same animal read from a character five levels up
    [InlineData(3, 5, Threat.Easy)]
    [InlineData(9, 1, Threat.FarAbove)]
    public void TheGapBetweenLevels_ReadsAsTheBand(int creatureLevel, int playerLevel, Threat expected) =>
        Assert.Equal(expected, ThreatRules.Of(creatureLevel, playerLevel));

    [Fact]
    public void TheBandsAreOrdered_FromTrivialToFarAbove()
    {
        Threat[] ladder = { Threat.Trivial, Threat.Easy, Threat.Even, Threat.Hard, Threat.Deadly, Threat.FarAbove };
        var words = ladder.Select(ThreatRules.Word).ToArray();
        Assert.Equal(ladder.Length, words.Distinct().Count());
        Assert.Equal("Even", ThreatRules.Word(Threat.Even));
        Assert.Equal("Far above", ThreatRules.Word(Threat.FarAbove));
    }

    [Fact]
    public void AHarderCreature_NeverReadsEasierThanALowerOne_AtTheSameCharacterLevel()
    {
        // Monotonic in the creature's level, which is what makes the colour trustworthy at a glance.
        for (int level = 1; level <= 12; level++)
            Assert.True(ThreatRules.Of(level, 1) >= ThreatRules.Of(level - 1, 1), $"level {level} read easier than {level - 1}");
    }
}

/// <summary>What a creature does when it notices the character: the words the plate shows beside the name.</summary>
public class TemperamentRulesTests
{
    [Theory]
    [InlineData(true, false, false, Temperament.Passive)]   // the hares and hens: it never starts a fight
    [InlineData(true, true, false, Temperament.Neutral)]    // the herd: it leaves you alone until struck, then the herd answers
    [InlineData(false, true, false, Temperament.Hostile)]   // the pack hunter: it will start it
    [InlineData(false, false, true, Temperament.Hostile)]   // whatever it is, once it is fighting you it is hostile
    [InlineData(true, true, true, Temperament.Hostile)]     // and a provoked herd reads hostile while it fights
    public void WhatItDoesWhenItNoticesYou(bool peaceful, bool answersCalls, bool engaged, Temperament expected) =>
        Assert.Equal(expected, TemperamentRules.Of(peaceful, answersCalls, engaged));

    [Fact]
    public void EveryTemperamentHasItsOwnWord() =>
        Assert.Equal(new[] { "Passive", "Neutral", "Hostile" },
            new[] { Temperament.Passive, Temperament.Neutral, Temperament.Hostile }.Select(TemperamentRules.Word));
}
