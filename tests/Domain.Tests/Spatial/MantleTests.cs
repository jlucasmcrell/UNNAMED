using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;
using Xunit.Abstractions;

namespace UNNAMED.Domain.Tests.Spatial;

/// <summary>
/// D6: a climbable structure in a body's way is climbed when its top is within reach - a fence vaulted, a ladder climbed at any height, a wall
/// mantled onto. These are the domain's own conditions, run without a world: a body pressed into a box, tick after tick, must end up standing on
/// its top rather than stopped against its face. The report is printed because a failure here is always about which condition gave way.
/// </summary>
public class MantleTests(ITestOutputHelper output)
{
    private static readonly MovementRules Moving = new(3_600, 50, 160, 350, 1_600) { StepUpMm = 500, MantleMm = 3_000, ClimbPrefixes = ImmutableArray.Create("ladder_") };

    private static WalkSpace Flat(params Blocker[] blockers) =>
        new(0, 0, 40_000, 40_000, new TerrainGrid(0, 0, 40_000, 2, 2, new long[] { 0, 0, 0, 0 }), blockers.ToImmutableArray());

    /// <summary>Runs a body east for a while and reports how high it ever stood: a mantle is a peak, not a final position.</summary>
    private static (Body End, long Peak) Press(WalkSpace space, Body from, int ticks = 40)
    {
        var intent = new MoveIntent(MoveIntent.FullDeflection, 0, Gait.Run, 90_000);
        var body = from;
        long peak = from.YMm;
        for (int tick = 0; tick < ticks; tick++)
        {
            (body, _) = Kinematics.Step(body, Posture.Grounded, intent, Moving, space, Array.Empty<Blocker>(), 50);
            peak = Math.Max(peak, body.YMm - space.HeightAtMm(body.XMm, body.ZMm));
        }
        return (body, peak);
    }

    [Fact]
    public void ABodyPressedIntoAFence_VaultsOntoIt()
    {
        var fence = new BoxBlocker("fence_smithy", 9_600, 4_700, 10_000, 5_300, 1_200) { Climbable = true };
        var space = Flat(fence);
        var start = new Body(8_000, 0, 5_000, 90_000);
        var (_, peak) = Press(space, start);
        output.WriteLine($"fence top={fence.HeightMm}; peak={peak}; reach={Moving.MantleMm}");
        Assert.True(peak >= fence.HeightMm - 50, $"the body never stood on the fence: peak={peak}");
    }

    [Fact]
    public void ABodyStandingOnADeck_VaultsTheFenceOnIt()
    {
        // The waystation's own case: a 1.5 m deck with a 1.2 m fence standing on it. Measured from the field below, the fence's top is under the
        // body's feet and nothing climbs; measured from the surface under the feet, the body stands on the fence at deck + fence.
        // The plateau is terrain, which is what the waystation's deck actually is in play (the press report: body y = ground, raised = 0). The
        // fence stands on it and the body stands on it, so the climb is measured from the surface under the feet, 1,500 up.
        var raised = new WalkSpace(0, 0, 40_000, 40_000, new TerrainGrid(0, 0, 40_000, 2, 2, new long[] { 1_500, 1_500, 1_500, 1_500 }),
            ImmutableArray.Create<Blocker>(new BoxBlocker("fence_smithy", 9_600, 4_700, 10_000, 5_300, 1_200) { Climbable = true }));
        var (_, peak) = Press(raised, new Body(8_000, 1_500, 5_000, 90_000), 50);
        // Press measures the peak as height above the ground under the body, so on a plateau the fence is still 1,200 up.
        output.WriteLine($"plateau 1_500 + fence 1_200 => peak above the plateau={peak}");
        Assert.True(peak >= 1_200 - 50, $"the body never stood on the fence on the plateau: peak={peak}");
    }

    [Fact]
    public void ALadderStandingOnRaisedGround_IsClimbedToo()
    {
        // The overflow that kept the ladder unclimbable in play: an unlimited reach added to a standing surface above world zero went negative,
        // so the ladder was refused as beyond reach. On flat ground the sum could not overflow, which is why the tests were green.
        var plateau = new WalkSpace(0, 0, 40_000, 40_000, new TerrainGrid(0, 0, 40_000, 2, 2, new long[] { 7_000, 7_000, 7_000, 7_000 }),
            ImmutableArray.Create<Blocker>(new BoxBlocker("ladder_waystation", 9_600, 4_800, 10_000, 5_200, 2_800) { Climbable = true }));
        var (_, peak) = Press(plateau, new Body(8_000, 7_000, 5_000, 90_000), 60);
        output.WriteLine($"ladder 2_800 on 7_000 ground => peak above the plateau={peak}");
        Assert.True(peak >= 2_800 - 50, $"the body never stood on the ladder top: peak={peak}");
    }

    [Fact]
    public void ALadderLeaningOnATallerWall_IsStillClimbed()
    {
        // The case that kept the ladder unclimbed in play: the ladder stands against a 3.0 m wall, so the probe point one radius into the ladder
        // fell inside the WALL's footprint and the old "highest top under the probe" test saw the wall's top instead of the ladder's.
        // Press runs EAST, so the ladder stands across the path and the wall stands immediately behind it.
        var ladder = new BoxBlocker("ladder_waystation", 9_600, 4_800, 10_000, 5_800, 1_330) { Climbable = true };
        var wall = new BoxBlocker("forge_north", 10_000, 4_600, 10_200, 6_000, 3_000) { Climbable = true };
        var space = Flat(ladder, wall);
        var (_, peak) = Press(space, new Body(9_000, 0, 5_300, 90_000), 40);
        output.WriteLine($"ladder 1_330 against wall 3_000 => peak={peak}");
        Assert.True(peak >= 1_330 - 50, $"the body never stood on the ladder: peak={peak}");
    }

    [Fact]
    public void ABodyPressedIntoALadder_ClimbsItAtAnyHeight()
    {
        // Three metres: past the mantle reach, so only the ladder_ prefix can make this climbable.
        var ladder = new BoxBlocker("ladder_waystation", 9_600, 4_800, 10_000, 5_200, 3_200) { Climbable = true };
        var space = Flat(ladder);
        var start = new Body(8_000, 0, 5_000, 90_000);
        var (end, peak) = Press(space, start, 80);
        output.WriteLine($"end x={end.XMm} y={end.YMm}; peak={peak}; ladder top={ladder.HeightMm}; prefixes={string.Join(',', Moving.ClimbPrefixes)}");
        Assert.True(peak >= ladder.HeightMm - 50, $"the body never stood on the ladder top: peak={peak}");
    }
}
