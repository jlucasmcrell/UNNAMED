using UNNAMED.Domain.Combat;

namespace UNNAMED.Domain.Tests.Combat;

/// <summary>
/// CMB-15: a placed strike against an unaware body kills it outright with any class that can reach a vital, not only the
/// dagger. COMBAT §19 (an appropriate weapon into an unprotected critical structure can be lethal regardless of level) and
/// §32 (level grants no categorical immunity); the x3 on a vital belongs to a body that has noticed you, and is a wound.
/// </summary>
public class PlacedStrikeTests
{
    private static AttackProfile Attack(long reachMm = 900, bool ranged = false) =>
        new("test.blade", 8, 12, "physical_slash", reachMm, 4, 2, 6, 10) { Ranged = ranged };

    [Fact]
    public void AnArrowIntoAnUnawareHead_Kills_WhateverTheClass()
    {
        Assert.True(PlacedStrikeRules.Lethal(Attack(reachMm: 20_000, ranged: true), unaware: true, BodyRegion.Head, armouredRegion: false, boss: false));
    }

    [Fact]
    public void ASpearIntoAnUnawareHead_Kills_AtMeleeRange()
    {
        Assert.True(PlacedStrikeRules.Lethal(Attack(reachMm: 2_400), unaware: true, BodyRegion.Head, armouredRegion: false, boss: false));
    }

    [Fact]
    public void AFistNeverReaches_TheVital()
    {
        Assert.False(PlacedStrikeRules.Lethal(Attack(reachMm: 0), unaware: true, BodyRegion.Head, armouredRegion: false, boss: false));
        Assert.False(PlacedStrikeRules.Lethal(Attack(reachMm: 0), unaware: true, BodyRegion.Torso, armouredRegion: false, boss: false));
    }

    [Fact]
    public void ACoveredVital_IsNotLethal_TheArmourTakesIt()
    {
        Assert.False(PlacedStrikeRules.Lethal(Attack(reachMm: 2_400), unaware: true, BodyRegion.Head, armouredRegion: true, boss: false));
    }

    [Fact]
    public void AKeeper_IsNeverTakenBySurprise()
    {
        Assert.False(PlacedStrikeRules.Lethal(Attack(reachMm: 20_000, ranged: true), unaware: true, BodyRegion.Head, armouredRegion: false, boss: true));
    }

    [Fact]
    public void AnAwareVital_IsAThreefoldWound_NotAKill()
    {
        Assert.Equal(3.0, PlacedStrikeRules.Multiplier(unaware: false, BodyRegion.Head));
        Assert.Equal(3.0, PlacedStrikeRules.Multiplier(unaware: false, BodyRegion.Torso));
        Assert.Equal(1.0, PlacedStrikeRules.Multiplier(unaware: false, BodyRegion.Limbs));
        Assert.Equal(1.0, PlacedStrikeRules.Multiplier(unaware: true, BodyRegion.Head));
    }
}
