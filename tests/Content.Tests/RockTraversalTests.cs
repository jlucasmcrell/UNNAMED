using UNNAMED.Content;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Content.Tests;

/// <summary>Owner finding 50, on the shipped region: Cairnwold's turned rocks collide at their outline and the low ones are jumped onto.</summary>
public class RockTraversalTests
{
    private static readonly Lazy<(RegionLayout Layout, MovementRules Rules)> Region = new(() =>
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        return (WorldContent.BuildLayout(loader, "region.starting_frontier"), WorldContent.BuildMovement(loader));
    });

    [Fact]
    public void TurnedRocks_AreClimbableHulls_AndFencesAreNot()
    {
        var blockers = Region.Value.Layout.Space.Blockers;
        var rocks = blockers.Where(b => b.Id.StartsWith("cw_boulder_", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(rocks);
        Assert.All(rocks, r => Assert.True(r is PolygonBlocker && r.Climbable, r.Id));
        Assert.DoesNotContain(blockers, b => b.Climbable && b.Id.Contains("fence", StringComparison.Ordinal));
    }

    [Fact]
    public void ARunningJump_LandsOnTheLowBoulder()
    {
        var (layout, rules) = Region.Value;
        var rock = (PolygonBlocker)layout.Space.Blockers.Single(b => b.Id == "cw_boulder_a_205");
        long z = rock.CenterZMm, x = rock.Corners.Min(c => c.XMm) - 2500;
        var body = new Body(x, layout.Space.HeightAtMm(x, z), z, 90_000);
        var posture = Posture.Grounded;
        var east = new MoveIntent(1000, 0, Gait.Run, 90_000);
        bool jumped = false;
        for (int tick = 0; tick < 60; tick++)
        {
            if (!jumped && rock.DistanceTo(body.XMm, body.ZMm) < 900)
                (posture, jumped) = (new Posture(Stance.Standing, true, 0), true);
            (body, posture) = Kinematics.Step(body, posture, jumped && !posture.Airborne ? MoveIntent.Idle(90_000) : east, rules, layout.Space,
                Array.Empty<Blocker>(), 50);
        }
        Assert.True(jumped);
        Assert.True(rock.DistanceTo(body.XMm, body.ZMm) == 0, $"ended at {body.XMm},{body.ZMm}, {rock.DistanceTo(body.XMm, body.ZMm):0} mm off the rock");
        Assert.True(body.YMm > layout.Space.HeightAtMm(body.XMm, body.ZMm) + 500, $"feet at {body.YMm} over ground {layout.Space.HeightAtMm(body.XMm, body.ZMm)}");
    }
}
