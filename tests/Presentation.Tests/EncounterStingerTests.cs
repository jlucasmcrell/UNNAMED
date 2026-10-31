using UNNAMED.Presentation.Audio;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// CMB-42's audio half: which owned sound marks the start of an encounter, each phase it crosses into, and its end, and how far the score ducks
/// under it. Deterministic: the same moment always names the same cue.
/// </summary>
public class EncounterStingerTests
{
    [Fact]
    public void TheStartIsMarkedByTheOwnedStinger_WithTheArchetypesOwnVoiceBehindIt()
    {
        var cues = EncounterStinger.Cues(EncounterMoment.Start, "construct", phaseIndex: 0, phaseCount: 3);

        Assert.Equal(EncounterStinger.OwnedStinger, cues[0]);
        Assert.Equal("sfx.creature.animated_armour.alert.01", cues[1]);
        // Nothing is invented: every id is a family the owned set carries.
        Assert.All(cues, id => Assert.True(id.StartsWith("mus.stinger.") || id.StartsWith("sfx.creature."), id));
    }

    [Fact]
    public void APhaseChangeIsMarkedByTheCreature_AndTheLastPhaseGetsTheStinger()
    {
        var middle = EncounterStinger.Cues(EncounterMoment.Phase, "undead", phaseIndex: 1, phaseCount: 3);
        var last = EncounterStinger.Cues(EncounterMoment.Phase, "undead", phaseIndex: 2, phaseCount: 3);

        Assert.Equal("sfx.creature.bone_walker_husk.alert.01", middle[0]);
        // The stinger is still in the chain, behind the creature's own voice, so a set without it is not a silent phase.
        Assert.Contains(EncounterStinger.OwnedStinger, middle);
        // The last phase puts it first: that is the crossing that changes the fight.
        Assert.Equal(EncounterStinger.OwnedStinger, last[0]);
    }

    [Fact]
    public void TheEndIsMarkedByTheStinger_WithTheCreaturesDeathBehindIt()
    {
        var cues = EncounterStinger.Cues(EncounterMoment.End, "spider", phaseIndex: 2, phaseCount: 3);
        Assert.Equal(EncounterStinger.OwnedStinger, cues[0]);
        Assert.Equal("sfx.creature.cave_hunting_spider.death.01", cues[1]);
    }

    [Theory]
    [InlineData("construct", "animated_armour")]
    [InlineData("beast", "bristleback_boar")]
    [InlineData("undead", "bone_walker_husk")]
    [InlineData("animated_armour", "animated_armour")]
    [InlineData("something unattested", "animated_armour")]
    public void ACreatureFamilyIsNamedTheWayTheAudioContractNamesIt(string archetype, string expected) =>
        Assert.Equal(expected, EncounterStinger.Creature(archetype));

    [Fact]
    public void APhaseIsOnlyWorthACue_WhenThereWasAPhaseToCrossInto()
    {
        // A one-phase definition never crosses anything: the start and end are the whole encounter.
        Assert.False(EncounterStinger.Wants(EncounterMoment.Phase, phaseIndex: 0, phaseCount: 1));
        Assert.False(EncounterStinger.Wants(EncounterMoment.Phase, phaseIndex: 0, phaseCount: 3));
        Assert.True(EncounterStinger.Wants(EncounterMoment.Phase, phaseIndex: 1, phaseCount: 3));

        // Start and end are always worth it.
        Assert.True(EncounterStinger.Wants(EncounterMoment.Start, 0, 3));
        Assert.True(EncounterStinger.Wants(EncounterMoment.End, 2, 3));
    }

    [Fact]
    public void TheDuckGoesDownFast_HoldsUnderTheStinger_AndComesBackSlowly()
    {
        // At once: nothing ducked yet.
        Assert.Equal(1f, EncounterStinger.DuckGainAt(0f, stingerSeconds: 3));
        // Halfway through the attack: part way down.
        Assert.InRange(EncounterStinger.DuckGainAt(EncounterStinger.DuckAttackSeconds / 2f, 3), EncounterStinger.DuckGain, 1f);
        // Under the stinger: fully ducked, and it holds.
        Assert.Equal(EncounterStinger.DuckGain, EncounterStinger.DuckGainAt(1f, 3));
        Assert.Equal(EncounterStinger.DuckGain, EncounterStinger.DuckGainAt(3f, 3));
        // After it: back to full, and never past it.
        Assert.Equal(1f, EncounterStinger.DuckGainAt(4.5f, 3));
        Assert.Equal(1f, EncounterStinger.DuckGainAt(30f, 3));
    }

    /// <summary>The duck is a curve with no jumps in it: one second of frames never moves the music bus by more than a step it could not hear.</summary>
    [Fact]
    public void TheDuckCurve_IsContinuous()
    {
        float previous = EncounterStinger.DuckGainAt(0f, 2);
        for (int i = 1; i <= 300; i++)
        {
            float gain = EncounterStinger.DuckGainAt(i / 60f, 2);
            Assert.InRange(Math.Abs(gain - previous), 0f, 0.2f);
            previous = gain;
        }
    }
}
