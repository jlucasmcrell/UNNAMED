using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using static UNNAMED.Application.Tests.BuildingTests;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The replay test (N-A8 for WP-1G): a building session played through the command path - the workshop's house raised across the
/// four-cell corner, a door hung and worked, a refusal, blows, a mend, a take-down, a chest placed and filled - and then its command log
/// replayed into a second world from the same start, at the logged ticks. Building mints nothing (piece and chest identities are derived),
/// so the second world is the first: the same raw digest, the same pieces and IDs, the same refusals at the same ticks, the same grid.
/// </summary>
public class BuildingReplayTests
{
    [Fact]
    public void ABuildingSession_ReplaysFromItsLog_ToTheSameWorld()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        PlayerRecord? start = null;
        var played = OpenAt(session, session.Setup, (102.0, 102.0), 0, Array.Empty<(string, double, double, string)>(), r =>
            start = r.WithInventory(r.Inventory.Concat(new[] { Arena.Stack(Timber, 20), Arena.Stack(Timber, 20), Arena.Stack(Timber, 20) })));

        // The house, its door hung shut.
        WorkshopStepOne(played);
        Assert.Null(Place(played, Door, 100_500, 99_000, 0));
        var door = PieceAt(played, Door, 100_500, 99_000);
        played.Tick(20);
        // A refusal is logged as one, and replays as one.
        Assert.Equal("that is 12.73 m away; building reach is 6.00 m", Place(played, Pad, 112_500, 112_500, 0));
        // The door, worked from inside.
        Assert.True(played.WalkTo(100.5, 100.3));
        Assert.Null(played.Submit(new InteractCommand(played.Player, door.Value)));
        played.Tick(10);
        Assert.Null(played.Submit(new InteractCommand(played.Player, door.Value)));
        // Three blows on the west wall, and the mend.
        var west = PieceAt(played, Wall, 99_000, 100_500);
        Assert.True(played.WalkTo(99.9, 101.5));
        played.TurnTo(270);
        played.Tick();
        Blows(played, 3);
        Assert.Equal(170, Pieces(played).Single(p => p.Id == west).HealthCurrent);
        Assert.Null(played.Submit(new RepairPieceCommand(played.Player, west)));
        // A roof taken down, and a chest on the square it covered, filled with a whole stack.
        Assert.Null(Dismantle(played, PieceAt(played, Roof, 103_500, 103_500)));
        Assert.True(played.WalkTo(103.5, 102.6));
        Assert.Null(Place(played, Chest, 103_500, 103_500, 0));
        string key = Pieces(played).Single(p => p.DefId == Chest).ContainerKey!;
        Assert.True(played.WalkTo(103.5, 103.5));   // within reach of its site
        var stack = played.Simulation.Player.Inventory.Where(e => e.DefId == Timber).OrderBy(e => e.Count).First();
        Assert.Null(played.Submit(new MoveItemCommand(played.Player, stack.ItemId.Value, ItemPlace.Carried, ItemPlace.In(key), stack.Count)));
        played.Tick(20);
        long end = played.Simulation.WorldTick;
        var log = played.Simulation.CommandLog.ToList();
        Assert.Contains(log, e => e.RejectedReason is not null);

        // The second world: the same start, the same rules, the log at its ticks.
        var rules = session.Setup with { Combat = session.Setup.Combat with { Spawns = ImmutableArray<SpawnSite>.Empty } };
        var replay = Simulation.Start(rules, start!, new WorldDelta(session.Generator, 42, new Registry()), 0, new EventBus());
        void StepTo(long tick)
        {
            while (replay.WorldTick < tick)
            {
                replay.DrainCommands();
                replay.Step();
            }
        }
        foreach (var entry in log)
        {
            StepTo(entry.Tick);
            replay.Enqueue(entry.Command);
            replay.DrainCommands();
        }
        StepTo(end);

        Assert.Equal(played.Simulation.StateDigest(), replay.StateDigest());
        static IEnumerable<object> Rows(Simulation s) => s.World.Pieces.Select(p =>
            (object)(p.InstanceId, p.DefId, p.HostCell, p.XMm, p.ZMm, p.Rotation, p.Owner, p.HealthCurrent, p.DoorOpen));
        Assert.Equal(Rows(played.Simulation), Rows(replay));
        Assert.Equal(played.Simulation.StructureRevision, replay.StructureRevision);
        Assert.Equal(log.Select(e => (e.Tick, e.RejectedReason)), replay.CommandLog.Select(e => (e.Tick, e.RejectedReason)));
        Assert.Equal(played.Simulation.Navigation.Grid.Digest(), replay.Navigation.Grid.Digest());
        static (EntityId, string) Stored(Simulation s, string key) =>
            (s.World.Container(key)!.InstanceId, string.Join(",", s.World.Container(key)!.Items.Select(i => $"{i.ItemId}:{i.Count}")));
        Assert.Equal(Stored(played.Simulation, key), Stored(replay, key));
        Assert.Empty(StateDump.Compare(StateDump.Render(played.Simulation, replayable: true), StateDump.Render(replay, replayable: true), out _));
    }
}
