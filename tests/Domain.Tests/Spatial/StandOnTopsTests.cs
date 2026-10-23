using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Tests.Spatial;

/// <summary>Owner finding 50: small rocks are stepped onto, low boulders jumped onto and stood on, and walked off again.</summary>
public class StandOnTopsTests
{
    private static readonly MovementRules Rules = new(3200, 50, 160, 350, 1600) { StepUpMm = 400 };

    private static WalkSpace Space(params Blocker[] blockers) =>
        new(0, 0, 20000, 20000, new TerrainGrid(0, 0, 20000, 2, 2, new long[] { 0, 0, 0, 0 }), blockers.ToImmutableArray());

    private static readonly MoveIntent East = new(1000, 0, Gait.Run, 90_000);

    private static (Body Body, Posture Posture) Walk(Body body, Posture posture, MoveIntent intent, WalkSpace space, int ticks, MovementRules? rules = null)
    {
        for (int i = 0; i < ticks; i++)
            (body, posture) = Kinematics.Step(body, posture, intent, rules ?? Rules, space, Array.Empty<Blocker>(), 50);
        return (body, posture);
    }

    [Fact]
    public void ASmallRock_IsWalkedUpOnto_AndOffAgain()
    {
        var space = Space(new BoxBlocker("rock_small", 5000, 4000, 6000, 6000, 300) { Climbable = true });
        var (onTop, _) = Walk(new Body(3000, 0, 5000, 90_000), Posture.Grounded, East, space, 16);
        Assert.InRange(onTop.XMm, 5000, 6000);
        Assert.Equal(300, onTop.YMm);
        var (beyond, _) = Walk(onTop, Posture.Grounded, East, space, 20);
        Assert.True(beyond.XMm > 6000);
        Assert.Equal(0, beyond.YMm);
    }

    [Fact]
    public void ALowBoulder_StopsAWalk_ButIsJumpedOntoAndStoodOn()
    {
        var space = Space(new BoxBlocker("boulder", 5000, 4000, 7000, 6000, 800) { Climbable = true });
        var (stopped, _) = Walk(new Body(3000, 0, 5000, 90_000), Posture.Grounded, East, space, 20);
        Assert.True(stopped.XMm <= 5000 - 350 + 1, $"walked into the boulder to {stopped.XMm}");
        Assert.Equal(0, stopped.YMm);

        var (landed, posture) = (stopped, new Posture(Stance.Standing, true, 0));
        for (int i = 0; i < 40 && posture.Airborne; i++)
            (landed, posture) = Kinematics.Step(landed, posture, East, Rules, space, Array.Empty<Blocker>(), 50);
        Assert.False(posture.Airborne);
        Assert.InRange(landed.XMm, 5000, 7000);
        Assert.Equal(800, landed.YMm);
        var (still, _) = Walk(landed, Posture.Grounded, MoveIntent.Idle(90_000), space, 10);
        Assert.Equal(landed, still);
    }

    private static (Body Body, Posture Posture) Jump(Body body, MovementRules rules, WalkSpace space)
    {
        var posture = new Posture(Stance.Standing, true, 0);
        for (int i = 0; i < 40 && posture.Airborne; i++)
            (body, posture) = Kinematics.Step(body, posture, East, rules, space, Array.Empty<Blocker>(), 50);
        return (body, posture);
    }

    [Fact]
    public void AChestHighLedge_IsMantledOnlyWithinReach()
    {
        var ledge = new BoxBlocker("ledge", 5000, 4000, 8000, 6000, 1500) { Climbable = true };
        var start = new Body(4600, 0, 5000, 90_000);
        var (up, _) = Jump(start, Rules with { MantleMm = 1600 }, Space(ledge));
        Assert.Equal(1500, up.YMm);
        Assert.InRange(up.XMm, 5000, 8000);

        var (below, _) = Jump(start, Rules, Space(ledge));
        Assert.Equal(0, below.YMm);
        var tooHigh = ledge with { HeightMm = 1800 };
        var (still, _) = Jump(start, Rules with { MantleMm = 1600 }, Space(tooHigh));
        Assert.Equal(0, still.YMm);
    }

    [Fact]
    public void WithoutAStep_NothingIsStoodOn()
    {
        var planar = Rules with { StepUpMm = 0 };
        var space = Space(new BoxBlocker("rock_small", 5000, 4000, 6000, 6000, 300) { Climbable = true });
        var (body, _) = Walk(new Body(3000, 0, 5000, 90_000), Posture.Grounded, East, space, 12, planar);
        Assert.True(body.XMm <= 5000 - 350 + 1);
        Assert.Equal(0, body.YMm);
    }

    [Fact]
    public void AFence_IsNeitherSteppedOntoNorMantled()
    {
        var fence = new BoxBlocker("cw_fence", 5000, 4000, 5400, 6000, 300);
        var (walked, _) = Walk(new Body(3000, 0, 5000, 90_000), Posture.Grounded, East, Space(fence), 20);
        Assert.True(walked.XMm <= 5000 - 350 + 1);
        var (jumped, _) = Jump(new Body(4600, 0, 5000, 90_000), Rules with { MantleMm = 1600 }, Space(fence with { HeightMm = 1500 }));
        Assert.Equal(0, jumped.YMm);
    }

    [Fact]
    public void AnOverhang_IsNeverStoodOn()
    {
        var beam = new BoxBlocker("beam", 5000, 4000, 6000, 6000, 1900) { ClearanceMm = 1500 };
        var (body, _) = Walk(new Body(3000, 0, 5000, 90_000), new Posture(Stance.Crouched, false, 0), East, Space(beam), 40);
        Assert.True(body.XMm > 6000);
        Assert.Equal(0, body.YMm);
    }
}
