using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The climb (D20, the ladder's half of <c>HOOK.TRAV.LADDER</c>): a body against a climbable face - a ladder, or rock the prefixes made
/// climbable - takes hold of it with its own key and goes up at the climb's pace, vertical and facing into the wall. At the top the mantle
/// puts it on the surface beyond the lip, once. The water is no wall to climb, a dead body has no hands for it, and a wall the prefixes did
/// not make climbable is never climbed. Everything is worked out from where the body is, so nothing is added to what a save holds: the
/// posture stays the only saved movement state.
/// </summary>
public class ClimbTests
{
    /// <summary>The frontier ladder (ladder_outpost_lodge): against the lodge's north wall, a 0.8 x 0.6 m landing 3.2 m up.</summary>
    private const double LadderX = 444, LadderZ = 731.35;

    /// <summary>The same wall with no ladder on it (longhouse_north, (436, 731.6)-(452, 732)).</summary>
    private const double WallX = 440, WallZ = 731.26;

    /// <summary>The frontier's millpond is in Ashen Hollow, at the hollow's own metres; the pond here is the D20 swim's own stand-in.</summary>
    private const string Ladder = "ladder_outpost_lodge";

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

    /// <summary>The ladder's own top in the world's millimetres: the ground its footprint is on, plus the content's 3.2 m.</summary>
    private static long LadderTop(GameSession session) => session.Setup.Layout.Space.HeightAtMm((long)(LadderX * 1000), (long)(LadderZ * 1000)) + 3_200;

    [Fact]
    public void TheLadderInFrontOfTheBodyIsTaken_AndTheFeetGoUpIt_VerticallyAndFacingTheWall()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var ladder = Standing(session, LadderX, LadderZ - 0.65);
        var began = ladder.Record<ClimbBegan>();

        Assert.Null(ladder.Submit(new ClimbCommand(ladder.Player)));
        var taken = Assert.Single(began);
        Assert.Equal(Ladder, taken.StructureId);
        Assert.True(ladder.Simulation.IsClimbing);
        Assert.Equal(Ladder, ladder.Simulation.Climbing!.Value.Id);
        var start = ladder.Simulation.Player.Body;
        Assert.Equal(0, start.FacingMdeg);   // facing the wall it is about to climb

        // 30 % of a 3.2 m/s base is 0.96 m/s: 48 mm on a 50 ms tick, straight up, no drift across the wall and no gravity.
        ladder.Tick(1);
        var rose = ladder.Simulation.Player.Body;
        Assert.Equal(start.YMm + 48, rose.YMm);
        Assert.Equal(start.XMm, rose.XMm);
        Assert.Equal(start.ZMm, rose.ZMm);
        Assert.Equal(0, rose.FacingMdeg);
        Assert.False(ladder.Simulation.Posture.Airborne);

        ladder.Tick(9);
        Assert.Equal(start.YMm + 480, ladder.Simulation.Player.Body.YMm);
        Assert.True(ladder.Simulation.IsClimbing);
    }

    [Fact]
    public void TheFeetComingOverTheTop_StandTheBodyBeyondTheLip_Once()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var ladder = Standing(session, LadderX, LadderZ - 0.65);
        var mantled = ladder.Record<Mantled>();
        long top = LadderTop(session);

        Assert.Null(ladder.Submit(new ClimbCommand(ladder.Player)));
        for (int tick = 0; tick < 400 && ladder.Simulation.IsClimbing; tick++)
            ladder.Tick();

        // The mantle fired once: the body stands on the ladder's own landing at the top, a body's radius past the lip, and the climb is over.
        Assert.Single(mantled);
        Assert.False(ladder.Simulation.IsClimbing);
        var stood = ladder.Simulation.Player.Body;
        Assert.Equal(top, stood.YMm);
        Assert.InRange(stood.ZMm, 731_000, 731_600);   // on the landing the ladder's head makes, past the lip the feet came over
        Assert.False(ladder.Simulation.Posture.Airborne);

        // And it stays there: the landing is under its feet, so the next ticks neither drop it nor climb it.
        ladder.Tick(4);
        Assert.Equal(top, ladder.Simulation.Player.Body.YMm);
        Assert.False(ladder.Simulation.IsClimbing);
    }

    [Fact]
    public void TheClimbKeyLetsGo_WhereTheBodyHangs()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var ladder = Standing(session, LadderX, LadderZ - 0.65);
        long ground = session.Setup.Layout.Space.HeightAtMm((long)(LadderX * 1000), (long)(LadderZ * 1000));

        Assert.Null(ladder.Submit(new ClimbCommand(ladder.Player)));
        ladder.Tick(10);
        Assert.Equal(ground + 480, ladder.Simulation.Player.Body.YMm);

        Assert.Null(ladder.Submit(new ClimbCommand(ladder.Player)));
        Assert.False(ladder.Simulation.IsClimbing);
        Assert.Empty(ladder.Record<Mantled>());
        // Let go half a metre up, the body has no top under it to stand on: it settles to the ground, where the climb began.
        ladder.Tick(1);
        Assert.Equal(ground, ladder.Simulation.Player.Body.YMm);
    }

    [Fact]
    public void AStructureThePrefixesDidNotMakeClimbable_IsNeverClimbed()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // Against the lodge's north wall, the body's front a hand inside it: a wall, and walls are not climbable.
        var wall = Standing(session, WallX, WallZ);

        Assert.Equal("nothing to climb", wall.Submit(new ClimbCommand(wall.Player)));
        Assert.False(wall.Simulation.IsClimbing);
        wall.Tick(4);
        Assert.False(wall.Simulation.IsClimbing);
    }

    [Fact]
    public void TheWaterIsNoWallToClimb()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var pond = Arena.OpenCreatures(session, session.Setup, (558, 100), 90, Array.Empty<(string, double, double, string)>(), Arena.Usual);
        pond.Tick(1);
        Assert.True(pond.Simulation.Swimming);

        Assert.Equal("the water is no wall to climb", pond.Submit(new ClimbCommand(pond.Player)));
        Assert.False(pond.Simulation.IsClimbing);
    }

    [Fact]
    public void ADeadBodyHasNoHandsToClimbWith()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A wolf on the body and health enough for one bite (the arena's own kill). The refusal comes before the face is looked for, so this is
        // the hollow's open ground and not the ladder: a dead body has no hands to climb with wherever it fell.
        var arena = Arena.Open(session, (120, 60), 0, new[] { (120.0, 61.9) },
            r => r.WithProgression(r.Progression with { Pools = r.Progression.Pools with { Health = 3 } }));
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));   // the swing that makes the wolf answer
        for (int tick = 0; tick < 400 && !arena.Simulation.Combat.Defeated; tick++)
            arena.Tick();
        Assert.True(arena.Simulation.Combat.Defeated, "the wolf killed the body");

        Assert.Equal("dead", arena.Submit(new ClimbCommand(arena.Player)));
        Assert.False(arena.Simulation.IsClimbing);
    }

    /// <summary>
    /// The pace against the clip: ANIM.CLIMB_LOOP (anim.player.mh.climb_loop) is one 0.8 s cycle of two rungs - the source's feet alternate
    /// 0.33 m - so one cycle of the loop is about 0.66 m of climb, and 0.8 s of climbing must cover that and no more. A body that covered a
    /// cycle's worth of height in a fraction of the clip would skate up the ladder.
    /// </summary>
    [Fact]
    public void OneCycleOfTheClimbLoop_IsOnePairOfRungs()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var ladder = Standing(session, LadderX, LadderZ - 0.65);
        Assert.Null(ladder.Submit(new ClimbCommand(ladder.Player)));
        long from = ladder.Simulation.Player.Body.YMm;

        ladder.Tick(16);   // 16 x 50 ms = 0.80 s, the loop's own length
        long climbed = ladder.Simulation.Player.Body.YMm - from;
        Assert.InRange(climbed, 550, 850);
        Assert.InRange(climbed / 660.0, 0.8, 1.3);   // one loop's two rungs, either side of it
    }

    /// <summary>
    /// The same rules without a world: a face is taken only when it is in front of the body and higher than a step, the climb is vertical, and
    /// the mantle is a body's radius past the lip, on the lip's own surface.
    /// </summary>
    [Fact]
    public void TheFaceIsTakenFromWhereTheBodyStands_TheClimbIsVertical_AndTheMantleIsAPaceBeyondTheLip()
    {
        var rules = new MovementRules(3_200, 50, 160, 350, 1_600) { StepUpMm = 400, MantleMm = 1_600 };
        var heights = new long[2 * 11];
        var space = new WalkSpace(-5_000, -5_000, 5_000, 5_000, new TerrainGrid(-5_000, -5_000, 1_000, 11, 2, heights),
            ImmutableArray.Create<Blocker>(
                new BoxBlocker("ladder_test", -300, 1_000, 300, 1_600, 2_500) { Climbable = true },
                new BoxBlocker("stone_test", -300, -600, 300, -300, 300) { Climbable = true },
                new BoxBlocker("wall_test", -300, 3_000, 300, 3_400, 3_000)));

        // In front of the ladder, facing it: taken. The same body facing away from it: nothing.
        var atLadder = new Body(0, 0, 700, 0);
        Assert.Equal("ladder_test", Kinematics.ClimbFaceAt(atLadder, rules, space)!.Value.Id);
        Assert.Null(Kinematics.ClimbFaceAt(atLadder with { FacingMdeg = 180_000 }, rules, space));

        // Facing the 3 m wall: not climbable, so not a face. Facing the 0.3 m stone: within a step, so a step up, not a climb.
        Assert.Null(Kinematics.ClimbFaceAt(new Body(0, 0, 2_700, 0), rules, space));
        Assert.Null(Kinematics.ClimbFaceAt(new Body(0, 0, -700, 0), rules, space));

        // One tick of the climb: 48 mm straight up in the same place, facing the wall.
        var face = Kinematics.ClimbFaceAt(atLadder, rules, space)!.Value;
        var climbed = Kinematics.Climb(atLadder, face, rules, 50);
        Assert.Equal(48, climbed.YMm);
        Assert.Equal(atLadder.XMm, climbed.XMm);
        Assert.Equal(atLadder.ZMm, climbed.ZMm);
        Assert.False(Kinematics.AtTop(climbed, face, rules));

        // At the top: the last tick stops at the lip itself, and the mantle puts the body a pace beyond it, on the ladder's own landing.
        var top = Kinematics.Climb(new Body(0, 2_480, 700, 0), face, rules, 50);
        Assert.Equal(2_500, top.YMm);
        Assert.True(Kinematics.AtTop(top, face, rules));
        var stood = Kinematics.Mantled(top, face, rules, space);
        Assert.NotNull(stood);
        Assert.Equal(2_500, stood!.YMm);
        Assert.InRange(stood.ZMm, 1_000, 1_600);
        Assert.Equal(0, stood.FacingMdeg);

        // And a lip with nothing at its own height behind it has no mantle: the body lets go where it hangs.
        var rail = new ClimbFace("rail_test", 2_500, 0);
        Assert.Null(Kinematics.Mantled(new Body(0, 2_500, 700, 0), rail, rules, space with { Blockers = ImmutableArray<Blocker>.Empty }));
    }
}
