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
        output.WriteLine($"space blockers={space.Blockers.Length}; clearance={fence.ClearanceMm}; climbable={fence.Climbable}; blocks at lift 0 = {Kinematics.Blocks(fence, 0, Moving.HeightMm(Stance.Standing))}");
        output.WriteLine($"separation from 9_000 = {fence.Separation(9_000, 5_000, Moving.BodyRadiusMm)}");
        output.WriteLine($"separation from 9_250 = {fence.Separation(9_300, 5_000, Moving.BodyRadiusMm)}");
        output.WriteLine($"sheer={Moving.SheerGradeMmPerM} scramble={Moving.ScrambleGradeMmPerM} scramble%={Moving.ScramblePercent} stepUp={Moving.StepUpMm} height={Moving.HeightMm(Stance.Standing)}");
        // One step from two hundred millimetres short of the face, so the failure is a single tick and not forty.
        var near = new Body(9_400, 0, 5_000, 90_000);
        var one = Kinematics.Step(near, Posture.Grounded, new MoveIntent(MoveIntent.FullDeflection, 0, Gait.Run, 90_000), Moving, space, Array.Empty<Blocker>(), 50);
        output.WriteLine($"one step from 9_400: x={one.Body.XMm} y={one.Body.YMm}");
        var two = Kinematics.Step(one.Body, Posture.Grounded, new MoveIntent(MoveIntent.FullDeflection, 0, Gait.Run, 90_000), Moving, space, Array.Empty<Blocker>(), 50);
        output.WriteLine($"second step: x={two.Body.XMm} y={two.Body.YMm}");
        var start = new Body(8_000, 0, 5_000, 90_000);
        var (end, peak) = Press(space, start);
        output.WriteLine($"start x={start.XMm} y={start.YMm}; end x={end.XMm} y={end.YMm}; peak={peak}; radius={Moving.BodyRadiusMm} reach={Moving.MantleMm}");
        output.WriteLine($"fence top={fence.HeightMm}; probe from {end.XMm} is at {end.XMm + Moving.BodyRadiusMm}");
        output.WriteLine($"blocker distance at probe={fence.DistanceTo(end.XMm + Moving.BodyRadiusMm, end.ZMm)}");
        Assert.True(end.YMm >= fence.HeightMm - 50, $"the body did not vault the fence: y={end.YMm}");
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
