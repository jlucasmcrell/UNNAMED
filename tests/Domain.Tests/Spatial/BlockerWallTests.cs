using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Tests.Spatial;

/// <summary>
/// A footprint keeps two bodies from each other only if it stands taller than a step: the prepared ritual circles are drawn 12 cm up and had
/// been stopping arrows, blows, charges and every creature's sight of a character standing on one (the boar-charge family of reds).
/// </summary>
public class BlockerWallTests
{
    /// <summary>The Foldscar's watch circle as content authors it: a 3.4 m ring standing 12 cm.</summary>
    private static readonly CircleBlocker Ring = new("ritual_circle_foldscar_watch", 551_000, 643_000, 3_400, 120)
    {
        ClearanceMm = 50,
    };

    /// <summary>The Scar Stone: the same shape, 1.6 m tall.</summary>
    private static readonly CircleBlocker Stone = new("foldscar_waystone", 553_000, 642_000, 900, 1_600);

    [Fact]
    public void AGroundRing_IsNotAWall_BetweenTwoBodies()
    {
        // The line crosses the ring's footprint either way - that is geometry, and it still holds.
        Assert.True(Ring.Crosses(550_000, 640_000, 550_000, 646_000));
        Assert.False(Ring.StandsBetween);
    }

    [Fact]
    public void AStandingStone_IsAWall()
    {
        Assert.True(Stone.Crosses(553_000, 633_600, 553_000, 643_400));
        Assert.True(Stone.StandsBetween);
    }

    [Fact]
    public void TheStepIsTheLine_AndAnUnknownHeightIsStillAWall()
    {
        var kerb = new CircleBlocker("kerb", 0, 0, 200, Blocker.StepOverHeightMm);
        var post = new CircleBlocker("post", 0, 0, 200, Blocker.StepOverHeightMm + 1);
        Assert.False(kerb.StandsBetween);
        Assert.True(post.StandsBetween);
        // Nothing this codebase knows the height of is a wall; a body whose height is unknown is, everywhere it walks (Kinematics.Blocks).
        Assert.True(new CircleBlocker("body", 0, 0, 350, 0).StandsBetween);
    }
}
