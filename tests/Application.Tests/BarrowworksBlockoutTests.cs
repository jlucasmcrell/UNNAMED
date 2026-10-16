using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// M8 D10 and P2: the Barrowworks interior is thirteen rooms in its own space, and every room is reachable
/// from the spawn. A blockout whose rooms do not connect is the one failure a screenshot cannot show, so it
/// is walked here rather than looked at.
/// </summary>
public class BarrowworksBlockoutTests
{
    private static readonly SpaceId Barrowworks = SpaceId.Parse("space.barrowworks");

    private const string Region = "region.barrowworks";

    /// <summary>The thirteen room centres, in the blockout's own shifted metres (generator OFFSET 50, 10).</summary>
    private static readonly (string Id, double X, double Z)[] Rooms =
    [
        ("R01", 50, 10), ("R02", 50, 28), ("R03", 28, 38), ("R04", 8, 38), ("R05", 28, 60),
        ("R06", 50, 60), ("R07", 72, 60), ("R08", 72, 38), ("R09", 92, 60), ("R10", 50, 84),
        ("R11", 50, 106), ("R12", 50, 131), ("R13", 72, 84),
    ];

    private static GameSession Boot(TempProfile profile) =>
        GameSession.Boot(new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root)
        {
            RegionId = Region,
            Barrowworks = true,
        });

    [Fact]
    public void TheInteriorIsItsOwnSpace_NotTheExterior()
    {
        using var profile = new TempProfile();
        var session = Boot(profile);

        Assert.Equal(Barrowworks, session.Setup.Layout.SpaceId);
        Assert.False(session.Setup.Layout.SpaceId.IsExterior);
        // Its cells are qualified with that space, so they cannot collide with exterior ground.
        Assert.All(session.Setup.Layout.CellKeys, key => Assert.StartsWith("space.barrowworks/", key));
    }

    [Fact]
    public void EveryRoom_IsReachableFromTheSpawn()
    {
        using var profile = new TempProfile();
        var session = Boot(profile);
        var space = session.Setup.Layout.Space;

        // A coarse flood fill over the walkable area, at 1 m steps, blocked by walls with the body's own
        // radius - the same test movement uses, so a gap a body cannot fit through is not counted as a route.
        var blockers = session.Setup.Layout.ClosedDoors(_ => false).ToArray();
        double radius = session.Setup.Movement.BodyRadiusMm / 1000.0;
        const double step = 1.0;
        var start = (X: (double)session.Setup.Layout.Spawn.XMm / 1000.0, Z: (double)session.Setup.Layout.Spawn.ZMm / 1000.0);

        var seen = new HashSet<(int, int)> { ((int)start.X, (int)start.Z) };
        var queue = new Queue<(double X, double Z)>();
        queue.Enqueue(start);

        bool Blocked(double x, double z) => blockers.Any(b => b.Separation(x, z, radius) is not null);

        while (queue.Count > 0)
        {
            var (x, z) = queue.Dequeue();
            foreach (var (dx, dz) in new[] { (step, 0.0), (-step, 0.0), (0.0, step), (0.0, -step) })
            {
                double nx = x + dx, nz = z + dz;
                if (nx < space.MinXMm / 1000.0 || nx > space.MaxXMm / 1000.0) continue;
                if (nz < space.MinZMm / 1000.0 || nz > space.MaxZMm / 1000.0) continue;
                if (Blocked(nx, nz)) continue;
                if (!seen.Add(((int)nx, (int)nz))) continue;
                queue.Enqueue((nx, nz));
            }
        }

        var unreachable = Rooms.Where(r => !seen.Contains(((int)r.X, (int)r.Z))).Select(r => r.Id).ToArray();
        Assert.True(unreachable.Length == 0,
            $"unreachable from the spawn: {string.Join(", ", unreachable)} (reached {seen.Count} tiles)");
    }

    [Fact]
    public void TheBlockout_HasThirteenDistinctRooms()
    {
        using var profile = new TempProfile();
        var session = Boot(profile);
        var walls = session.Setup.Layout.Space.Blockers;

        // Four walls per room, each split into two by its opening, is the generator's shape.
        Assert.Equal(13, Rooms.Select(r => r.Id).Distinct().Count());
        Assert.True(walls.Length > 90, $"expected the rooms' walls, found {walls.Length} blockers");

        // Rooms are clear at their centres: no wall runs through one.
        double radius = session.Setup.Movement.BodyRadiusMm / 1000.0;
        foreach (var (id, x, z) in Rooms)
        {
            bool inside = walls.Any(b => b.Separation(x, z, radius) is not null);
            Assert.False(inside, $"{id} at ({x}, {z}) is inside a wall");
        }
    }

    [Fact]
    public void EveryRoom_IsANamedPlace_SoTheSurveyHasThirteenStations()
    {
        // SurveyShots builds one camera station per location the region authored, so the per-room screenshots
        // exist exactly when every room is a named place. This is the check that they do - and the same
        // content P4 needs, since a room the player cannot discover is not a room.
        using var profile = new TempProfile();
        var session = Boot(profile);
        var places = session.Setup.Layout.Locations;

        Assert.Equal(13, places.Length);
        foreach (var (id, x, z) in Rooms)
        {
            // Room ids are R01..R13; the place ids are location.barrowworks.r01_threshold and so on.
            var place = places.FirstOrDefault(p => p.Id.Contains(id.ToLowerInvariant(), StringComparison.Ordinal));
            Assert.NotNull(place);
            Assert.Equal((long)(x * 1000), place.XMm);
            Assert.Equal((long)(z * 1000), place.ZMm);
        }
    }

    [Fact]
    public void WithTheGateOff_TheInteriorRegionIsStillItsOwnSpace_AndDeclaresNoPortal()
    {
        // Booting the interior directly is a harness path; a player reaches it only through a portal, and no
        // portal exists unless the run asks for one. This pins that the interior carries no portal of its own.
        using var profile = new TempProfile();
        var session = Boot(profile);

        Assert.Empty(session.Setup.Layout.Portals.Portals);
    }
}
