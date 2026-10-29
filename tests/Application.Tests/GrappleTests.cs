using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The grapple (D21, <c>HOOK.TRAV.GRAPPLE</c>): a structure the grapple prefixes made an anchor is a target, the command throws a line at the
/// lowest one above the body within the reach band for <see cref="GrappleSystem.ThrowTicks"/> ticks, and the pull puts the body on the surface
/// the anchor stands on - a straight haul to the anchor's base, not a swing, and the lip beyond it. The anchor is the basin's own
/// (grapple_basin_lip, at region (899.5, 647.5) on the west wall of Cairnwold's basin): the body hooks it from the basin floor 4 m below and
/// stands on the lip.
/// </summary>
public class GrappleTests
{
    /// <summary>The basin floor under the wall, in the region's metres: 5.3 m up against a 9.0 m lip.</summary>
    private const double FootX = 906, LipZ = 647.5;

    private const string Anchor = "grapple_basin_lip";

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

    [Fact]
    public void TheAnchorAboveTheBody_IsThrownTo_AndThePullStandsTheBodyOnTheRim_Once()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var space = session.Setup.Layout.Space;
        var foot = Standing(session, FootX, LipZ, facing: 270);   // on the basin floor, facing the cliff
        var thrown = foot.Record<GrappleThrown>();
        var pulled = foot.Record<Grappled>();
        var moved = foot.Record<BodyMoved>();
        long at = foot.Simulation.Player.Body.YMm;

        Assert.Null(foot.Submit(new GrappleCommand(foot.Player)));
        var line = Assert.Single(thrown);
        Assert.Equal(Anchor, line.AnchorId);
        Assert.Equal(ActivityKind.GrappleThrow, foot.Simulation.Activity!.Kind);
        Assert.Equal(Anchor, foot.Simulation.Activity.TargetKey);
        Assert.Empty(pulled);

        // The line is out for 40 ticks: nothing has happened to the body yet but the throw.
        foot.Tick(GrappleSystem.ThrowTicks - 1);
        Assert.Empty(pulled);
        Assert.Equal(at, foot.Simulation.Player.Body.YMm);
        foot.Tick(1);

        // Once, and the body is on the lip: above where it stood, on the ground there, on its feet.
        var haul = Assert.Single(pulled);
        Assert.Equal(Anchor, haul.AnchorId);
        Assert.Null(foot.Simulation.Activity);
        var stood = foot.Simulation.Player.Body;
        Assert.True(stood.YMm > 8_000, $"the pull put it on the lip: {stood.YMm} mm");
        Assert.Equal(space.HeightAtMm(stood.XMm, stood.ZMm), stood.YMm);
        Assert.InRange(stood.XMm, 898_200, 899_000);   // a body's radius past the anchor, away from the drop
        Assert.InRange(stood.ZMm, 647_000, 648_000);
        Assert.False(foot.Simulation.Posture.Airborne);
        Assert.False(foot.Simulation.Swimming);
        Assert.False(foot.Simulation.IsClimbing);
        Assert.Contains(moved, m => m.To == stood);

        // And it stays there: nothing further is pulled, and the ground under it is the rim's.
        foot.Tick(8);
        Assert.Single(pulled);
        Assert.Equal(stood.YMm, foot.Simulation.Player.Body.YMm);
    }

    [Fact]
    public void ThereIsNoAnchorInReach_OverTheHollow()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var yard = Standing(session, 430, 758, facing: 90);   // the waystone's own ground, nothing to hook

        Assert.Equal("no anchor in reach", yard.Submit(new GrappleCommand(yard.Player)));
        Assert.Null(yard.Simulation.Activity);
        Assert.Empty(yard.Record<GrappleThrown>());
        Assert.Empty(yard.Record<Grappled>());
    }

    [Fact]
    public void TheWaterIsNoPlaceToThrowALine_AndNeitherIsTheAir()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // In the millpond, which floats the body: no footing to throw from.
        var pond = Arena.OpenCreatures(session, session.Setup, (558, 100), 270,
            Array.Empty<(string, double, double, string)>(), Arena.Usual);
        pond.Tick(1);
        Assert.True(pond.Simulation.Swimming);
        Assert.Equal("the water is no place to throw a line", pond.Submit(new GrappleCommand(pond.Player)));
        Assert.Null(pond.Simulation.Activity);

        // And off the ground, there is nothing to throw from either: the refusal comes before any anchor is looked for.
        var foot = Standing(session, FootX, LipZ, facing: 270);
        Assert.Null(foot.Submit(new JumpCommand(foot.Player)));
        Assert.True(foot.Simulation.Posture.Airborne);
        Assert.Equal("in the air", foot.Submit(new GrappleCommand(foot.Player)));
        Assert.Null(foot.Simulation.Activity);
    }

    [Fact]
    public void AThrowInFlight_TakesNoSecondOne_AndACancelPullsNothing()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var foot = Standing(session, FootX, LipZ, facing: 270);
        var thrown = foot.Record<GrappleThrown>();
        var pulled = foot.Record<Grappled>();
        long at = foot.Simulation.Player.Body.YMm;

        Assert.Null(foot.Submit(new GrappleCommand(foot.Player)));
        foot.Tick(4);
        Assert.Equal("You are already doing something", foot.Submit(new GrappleCommand(foot.Player)));
        Assert.Single(thrown);

        // A cancel brings the line in cleanly: no pull, no event, the body where it stood.
        Assert.Null(foot.Submit(new CancelActivityCommand(foot.Player)));
        Assert.Null(foot.Simulation.Activity);
        foot.Tick(GrappleSystem.ThrowTicks + 4);
        Assert.Empty(pulled);
        Assert.Equal(at, foot.Simulation.Player.Body.YMm);
    }

    [Fact]
    public void ADeadBodyThrowsNothing()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A wolf on the body and health enough for one bite (the arena's own kill): the refusal comes before the anchor is looked for, so this
        // is the hollow's open ground and not the Rim.
        var arena = Arena.Open(session, (120, 60), 0, new[] { (120.0, 61.9) },
            r => r.WithProgression(r.Progression with { Pools = r.Progression.Pools with { Health = 3 } }));
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        for (int tick = 0; tick < 400 && !arena.Simulation.Combat.Defeated; tick++)
            arena.Tick();
        Assert.True(arena.Simulation.Combat.Defeated, "the wolf killed the body");

        Assert.Equal("dead", arena.Submit(new GrappleCommand(arena.Player)));
        Assert.Null(arena.Simulation.Activity);
        Assert.Empty(arena.Record<GrappleThrown>());
    }

    /// <summary>
    /// The rules without a world: a hook above the feet by more than a mantle and inside the line's own reach is a target, and the pull is its
    /// base; a hook too low, one out of reach, and one whose base is no higher than the body are not, and the lowest hook wins.
    /// </summary>
    [Fact]
    public void TheHookIsTakenFromWhereTheBodyStands_AndThePullIsTheAnchorsOwnLedge()
    {
        var rules = new MovementRules(3_200, 50, 160, 350, 1_600) { StepUpMm = 400, MantleMm = 1_600 };
        // A terrace: flat at 0 west of x = 0, a 600 % ramp up to 6 m, and flat at 6 m east of it.
        var heights = new long[3 * 41];
        for (int column = 0; column <= 40; column++)
        {
            long x = -20_000 + column * 1_000;
            long h = x <= 0 ? 0 : x >= 1_000 ? 6_000 : x * 6;
            for (int row = 0; row < 3; row++)
                heights[row * 41 + column] = h;
        }
        var space = new WalkSpace(-20_000, -1_000, 20_000, 1_000, new TerrainGrid(-20_000, -1_000, 1_000, 41, 3, heights),
            ImmutableArray.Create<Blocker>(
                new CircleBlocker("grapple_lip", 1_600, 0, 350, 500) { Grapple = true },
                new CircleBlocker("grapple_higher", 1_600, 700, 350, 1_900) { Grapple = true },
                new CircleBlocker("grapple_low", -3_000, 0, 350, 500) { Grapple = true },
                new CircleBlocker("grapple_far", 15_000, 0, 350, 500) { Grapple = true }));
        var at = new Body(-2_000, 0, 0, 90_000);

        // The lowest hook above the feet within the line's reach, and the pull is a body's radius past it on the terrace.
        var target = Kinematics.GrappleAt(at, rules, space)!.Value;
        Assert.Equal("grapple_lip", target.Id);
        Assert.Equal(6_500, target.HookMm);
        Assert.Equal(6_000, target.Landing.YMm);
        Assert.InRange(target.Landing.XMm, 2_250, 2_350);
        Assert.Equal(space.HeightAtMm(target.Landing.XMm, target.Landing.ZMm), target.Landing.YMm);

        // A hook half a metre above the feet is a step and not a throw, and a hook twenty metres off is out of the line's reach.
        Assert.Null(Kinematics.GrappleAt(at, rules, space with { Blockers = ImmutableArray.Create(space.Blockers[2]) }));
        Assert.Null(Kinematics.GrappleAt(new Body(-19_000, 0, 0, 90_000), rules, space));

        // A hook whose own base is level with the body has nothing to haul it onto, however high the hook itself.
        var podium = new CircleBlocker("grapple_podium", 0, 0, 350, 5_000) { Grapple = true };
        Assert.Null(Kinematics.Pulled(at, podium, rules, space));
        Assert.Null(Kinematics.GrappleAt(at, rules, space with { Blockers = ImmutableArray.Create<Blocker>(podium) }));

        // And a structure the grapple prefixes did not make an anchor is not one, whatever its height.
        Assert.Null(Kinematics.GrappleAt(at, rules, space with
        {
            Blockers = ImmutableArray.Create<Blocker>(new CircleBlocker("wall_test", 1_600, 0, 350, 5_000)),
        }));
    }
}
