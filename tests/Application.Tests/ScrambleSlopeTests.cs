using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The scramble (D21, <c>HOOK.TRAV.SCRAMBLE_SLOPE</c>): ground steeper than <see cref="MovementRules.WalkableGradePercent"/> is climbed at
/// <see cref="MovementRules.ScramblePercent"/> of the body's own pace and crested, never refused. The basin's scree run - the west wall of
/// Cairnwold's basin east of the Rim, cut to a clean 98 % bank over z 641-646 - is the frontier's own steep slope: the body goes up it at the
/// scramble's pace and stands on the lip. A wall is not a slope and is not scrambled; the walkable grade itself is untouched.
/// </summary>
public class ScrambleSlopeTests
{
    /// <summary>The scree run's foot, in the region's metres: the basin floor under the wall, at 5.9 m.</summary>
    private const double FootX = 906, RunZ = 643;

    /// <summary>The longhouse's north wall, walked into from the yard beside the well ((436, 731.6)-(452, 732)).</summary>
    private const double WallX = 438, WallZ = 736;

    /// <summary>The basin floor below the run: ground at about 4 m and nearly level, clear of the wall and of the trees.</summary>
    private const double FlatX = 912, FlatZ = 660;

    /// <summary>A place in the region's metres, turned into the arena's (the arena measures from the space's own corner).</summary>
    private static (double X, double Z) At(GameSession session, double regionX, double regionZ)
    {
        var (ox, oz) = Harness.HollowOf(session.Setup);
        return (regionX - ox, regionZ - oz);
    }

    private static Arena Standing(GameSession session, double regionX, double regionZ, int facing = 0,
        Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, session.Setup, At(session, regionX, regionZ), facing,
            Array.Empty<(string, double, double, string)>(), change ?? Arena.Usual);

    /// <summary>Ticks of one full-deflection intent, and how far it carried the body, in millimetres.</summary>
    private static (long X, long Z) Driven(Arena arena, int dirX, int dirZ, Gait gait, int ticks)
    {
        long fromX = arena.Simulation.Player.Body.XMm, fromZ = arena.Simulation.Player.Body.ZMm;
        arena.Submit(new MoveCommand(arena.Player, new MoveIntent(dirX, dirZ, gait, Harness.FacingOf(dirX, dirZ))));
        arena.Tick(ticks);
        return (arena.Simulation.Player.Body.XMm - fromX, arena.Simulation.Player.Body.ZMm - fromZ);
    }

    [Fact]
    public void ASteepBank_IsScrambledAtAReducedPace_AndIsCrested()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var rules = session.Setup.Movement;
        var space = session.Setup.Layout.Space;
        var run = Standing(session, FootX, RunZ, facing: 270);   // at the foot, facing up the bank (west)
        long foot = run.Simulation.Player.Body.YMm;

        // The run's own 160 mm a tick is the same key on the walkable grade; here it is the scramble's share of it - 57 % of it on a 98 %
        // bank - and the body gains 98 % of the ground it covers.
        var (west, _) = Driven(run, -MoveIntent.FullDeflection, 0, Gait.Run, 20);
        Assert.InRange(west, -1_840, -1_810);
        var climbed = run.Simulation.Player.Body;
        Assert.InRange(climbed.YMm - foot, 1_750, 1_830);

        // And on up: it crests the bank and stands on the lip, on the ground, walking at its own pace again.
        for (int tick = 0; tick < 200 && run.Simulation.Player.Body.XMm > 900_000; tick++)
            run.Tick();
        climbed = run.Simulation.Player.Body;
        Assert.True(climbed.XMm <= 900_000, $"the body crested the bank (x {climbed.XMm})");
        Assert.Equal(space.HeightAtMm(climbed.XMm, climbed.ZMm), climbed.YMm);
        Assert.True(climbed.YMm > 11_000, $"it stands on the lip, {climbed.YMm} mm up");
        var (onTheRim, _) = Driven(run, -MoveIntent.FullDeflection, 0, Gait.Run, 20);
        Assert.Equal(-3_200, onTheRim);   // off the bank the pace is the run's own again
        Assert.Equal(45, rules.ScramblePercent);
        Assert.Equal(55, rules.WalkableGradePercent);
    }

    [Fact]
    public void AWallIsNoSlope_ItStopsTheBody()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var radius = session.Setup.Movement.BodyRadiusMm;
        // The longhouse's north wall, walked into from the yard: a wall is a barrier, not a grade, and no pace passes it.
        var yard = Standing(session, WallX, WallZ, facing: 180);
        var (_, south) = Driven(yard, 0, -MoveIntent.FullDeflection, Gait.Run, 40);

        // 40 ticks of a run is 6.4 m and the wall's face is 4 m off: the body stops a body's radius short of it and goes no further.
        Assert.True(south > -4_000, $"the wall stopped the body: {south} mm south, of the 6.4 m the keys asked for");
        Assert.InRange(yard.Simulation.Player.Body.ZMm, 732_300, 732_400);
        Assert.Equal(radius, yard.Simulation.Player.Body.ZMm - 732_000);
        Assert.False(yard.Simulation.IsClimbing);
    }

    [Fact]
    public void TheWalkableGrade_IsUnchanged()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The basin floor, which is nearly level: the body keeps every millimetre of its pace.
        var flat = Standing(session, FlatX, FlatZ, facing: 90);
        long from = flat.Simulation.Player.Body.YMm;
        var (east, _) = Driven(flat, MoveIntent.FullDeflection, 0, Gait.Run, 20);
        var (eastWalking, _) = Driven(flat, MoveIntent.FullDeflection, 0, Gait.Walk, 10);

        Assert.Equal(3_200, east);        // 3.2 m/s at a run, exactly as before the scramble
        Assert.Equal(800, eastWalking);   // and half of it at a walk
        Assert.InRange(flat.Simulation.Player.Body.YMm - from, -200, 200);   // and it climbed or fell almost nothing doing it
    }

    /// <summary>
    /// The curve itself, without a world: the pace is the body's own up to the walkable grade, falls with the grade to the scramble's share at
    /// twice it, holds there however steep the face - and climbing is what costs, not descending.
    /// </summary>
    [Fact]
    public void TheGradeCurve_CostsClimbing_AndNothingElse()
    {
        var rules = new MovementRules(3_200, 50, 160, 350, 1_600);
        Assert.Equal(1, rules.GradePace(0, 350));            // flat
        Assert.Equal(1, rules.GradePace(192, 350));          // 55 %: the walkable grade itself
        Assert.Equal(1, rules.GradePace(-300, 350));         // downhill costs nothing
        Assert.Equal(1, rules.GradePace(385, 0));            // a stride of nothing has no grade
        Assert.InRange(rules.GradePace(300, 350), 0.68, 0.70);   // 86 %: part way down the curve
        Assert.Equal(0.45, rules.GradePace(385, 350));       // 110 %: the scramble's share
        Assert.Equal(0.45, rules.GradePace(1_400, 350));     // and no steeper face is refused: 45 % all the way up
    }
}
