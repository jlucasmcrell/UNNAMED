using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

public class BarrowworksFrontierTests
{
    internal static Arena AtThreshold(GameSession session, bool creatures = false) =>
        Arena.OpenCreatures(session, session.Setup, (437, 663), 90,
            Array.Empty<(string, double, double, string)>(), keepSpawns: creatures, origin: (0, 0));

    // Paths are found on the actual walk space and traversed with ordinary movement commands. No relocation to props.
    internal static void Walk(Arena arena, Func<long, long, bool> goal)
    {
        var simulation = arena.Simulation;
        var layout = simulation.ActiveLayout;
        var closed = layout.ClosedDoors(d => simulation.Doors.Single(v => v.Site == d).Open,
            b => !simulation.Barriers.Single(v => v.Site == b).Standing).ToArray();
        var body = simulation.Player.Body;
        var start = ((int)Math.Round(body.XMm / 1000.0), (int)Math.Round(body.ZMm / 1000.0));
        var parents = new Dictionary<(int X, int Z), (int X, int Z)?> { [start] = null };
        var queue = new Queue<(int X, int Z)>();
        queue.Enqueue(start);
        (int X, int Z)? end = null;
        while (queue.TryDequeue(out var point))
        {
            if (goal(point.X * 1000L, point.Z * 1000L)) { end = point; break; }
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var next = (point.X + dx, point.Z + dz);
                if (parents.ContainsKey(next) || !Kinematics.IsClear(next.Item1 * 1000L, next.Item2 * 1000L,
                        simulation.Setup.Movement.BodyRadiusMm, layout.Space, closed))
                    continue;
                parents[next] = point;
                queue.Enqueue(next);
            }
        }
        Assert.True(end is not null, $"No walkable route from {start} in {layout.Id}");
        var path = new List<(int X, int Z)>();
        for (var at = end; at is { } step; at = parents[step]) path.Add(step);
        path.Reverse();
        foreach (var point in path)
            Assert.True(arena.WalkTo(point.X - arena.Origin.X, point.Z - arena.Origin.Z, 50), $"blocked at {point} in {layout.Id}");
    }

    internal static void Work(Arena arena, string key)
    {
        var site = arena.Simulation.ActiveLayout.Switches.Single(s => s.Key == key);
        Walk(arena, (x, z) => site.Body.DistanceTo(x, z) <= arena.Simulation.Setup.Movement.InteractReachMm - 200);
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, key)));
        arena.Tick();
    }

    [Fact]
    public void FrontierEntry_ThenWalkingToTheReadings_UsesInteriorCollisionAndFlags()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, barrowworks: true);
        var arena = AtThreshold(session);
        Assert.Null(arena.Submit(new EnterSpaceCommand(arena.Player, "portal.barrowworks.threshold")));
        arena.Tick();
        Assert.Equal("region.barrowworks", arena.Simulation.ActiveLayout.Id);
        foreach (string key in new[] { "notice", "fitting_note", "diagram_rack" })
        {
            Work(arena, "switch.barrowworks." + key);
            var view = arena.Simulation.Switches.Single(s => s.Site.Key == "switch.barrowworks." + key);
            Assert.True(view.Set);
            var (x, z) = Footprints.Center(view.Site.Body);
            var cell = CellKey.OfInterior(arena.Simulation.ActiveLayout.SpaceId, x / 1000.0, z / 1000.0);
            Assert.Equal(1, arena.Simulation.World.GetFlag(cell, view.Site.FlagId));
            Assert.Equal(0, arena.Simulation.World.GetFlag(CellKey.OfWorld(x / 1000.0, z / 1000.0), view.Site.FlagId));
            Assert.Contains("already set", arena.Submit(new InteractCommand(arena.Player, view.Site.Key)));
        }
    }

    [Fact]
    public void SaveInside_PreservesTheExteriorPeoplesOwnGround()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, barrowworks: true);
        var arena = AtThreshold(session);
        Assert.Null(arena.Submit(new EnterSpaceCommand(arena.Player, "portal.barrowworks.threshold")));
        var simulation = arena.Simulation;
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("inside"), SaveDocuments.Capture(simulation.World, simulation.CaptureRecord(), session.Content, simulation.WorldTick, 0));
        var loaded = store.Load(SaveSlots.Manual("inside"), new LoadContext(session.Generator, session.Content, new Registry()));
        var resumed = Arena.Resume(session.Setup, loaded).Simulation;
        Assert.False(resumed.PlayerInExterior);
        foreach (var npc in resumed.Npcs)
        {
            var home = session.Setup.LayoutFor(session.Setup.SpaceOfNpc(npc.Id));
            Assert.Equal(home.Space.Terrain.HeightAtMm(npc.Body.XMm, npc.Body.ZMm), npc.Body.YMm);
        }
    }
}
