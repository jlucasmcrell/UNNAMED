using UNNAMED.Domain.Combat;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// D23 S4's takedown reach (STEALTH.md section 5.3): a blade reaches a step and its own length on the level, further from above because the drop
/// carries the character down onto the target, and never up.
/// </summary>
public class TakedownReachTests
{
    private const long Reach = 2_400;

    [Fact]
    public void OnTheLevel_ItIsAStepAndTheBlade()
    {
        Assert.True(TakedownRules.InReach(Reach, 0, 0, 2_000));
        Assert.False(TakedownRules.InReach(Reach, 0, 0, 2_600));
    }

    [Fact]
    public void FromALedge_TheDropAddsReach_UpToALimit()
    {
        // A body two metres below and three metres out: the drop carries the character onto it.
        Assert.True(TakedownRules.InReach(Reach, 2_000, 0, 4_000));
        // A drop smaller than MinDrop adds nothing.
        Assert.False(TakedownRules.InReach(Reach, 900, 0, 3_000));
        // And the drop can add no more than MaxDropReach, however deep the ledge.
        Assert.False(TakedownRules.InReach(Reach, 20_000, 0, Reach + TakedownRules.MaxDropReachMm + 100));
    }

    [Fact]
    public void ATargetAboveCannotBeTaken_UnlessItIsWithinAnArm()
    {
        Assert.True(TakedownRules.InReach(Reach, 0, 400, 1_000));
        Assert.False(TakedownRules.InReach(Reach, 0, 1_500, 1_000));
    }
}