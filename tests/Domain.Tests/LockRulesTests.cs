using UNNAMED.Domain;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// The lock's numbers: a hand against a tier, and no hand certain either way. A tier 3 lock is a wall for a beginner and a delay for a
/// master, which is what the two constants and the clamp are for.
/// </summary>
public class LockRulesTests
{
    [Fact]
    public void ABetterHand_OnTheSameLock_IsNeverLiklierToFail()
    {
        for (int tier = 1; tier <= 3; tier++)
            for (int level = 0; level < 60; level++)
                Assert.True(LockRules.Chance(level + 1, tier) >= LockRules.Chance(level, tier), $"level {level + 1} on tier {tier}");
    }

    [Fact]
    public void AHigherTier_OnTheSameHand_IsNeverEasier()
    {
        for (int level = 0; level <= 60; level++)
            for (int tier = 1; tier < 3; tier++)
                Assert.True(LockRules.Chance(level, tier) >= LockRules.Chance(level, tier + 1), $"level {level} on tier {tier}");
    }

    [Fact]
    public void NoHand_IsEverCertainOrHopeless()
    {
        for (int level = 0; level <= 60; level++)
            for (int tier = 1; tier <= 3; tier++)
                Assert.InRange(LockRules.Chance(level, tier), 0.05, 0.95);
        Assert.Equal(0.95, LockRules.Chance(100, 1));   // clamped above
        Assert.Equal(0.05, LockRules.Chance(0, 3));     // clamped below: a wall, but never quite a shut one
    }

    [Fact]
    public void TheNumbersTheDesignNames()
    {
        // 20% at a hand that matches the tier's whole demand, 2 points of skill to a point of chance, and 12 of demand per tier.
        Assert.Equal(0.20, LockRules.Chance(12, 1), 6);
        Assert.Equal(0.20, LockRules.Chance(24, 2), 6);
        Assert.Equal(0.20, LockRules.Chance(36, 3), 6);
        Assert.Equal(0.40, LockRules.Chance(22, 1), 6);
    }

    [Fact]
    public void APickedLock_IsRecordedInAWorldFlagNamedFromItsOwnKey()
    {
        Assert.Equal("world.lock.door.granary", LockRules.FlagOf("door.granary"));
        Assert.Equal("world.lock.container.road.barrow_cache", LockRules.FlagOf("container.road.barrow_cache"));
    }
}
