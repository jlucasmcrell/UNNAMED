using System.Text.Json;
using UNNAMED.Domain;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// M7's required instrumentation beside N-A10 (M7 design §14.12): T10, the building commands under a random script. T2 lands with the
/// errand (E9).
/// </summary>
public class M7BudgetTests
{
    private static readonly string[] Pieces =
    {
        "piece.pad.timber", "piece.wall.timber", "piece.doorway.timber", "piece.door.timber", "piece.roof.timber", "piece.storage.chest",
        "piece.station.anvil",
    };

    private const string Timber = "item.material.timber";

    /// <summary>A test-local generator (Knuth's MMIX constants): the script depends on the seed and the world, nothing else.</summary>
    private sealed class Lcg(ulong seed)
    {
        private ulong _x = seed;

        public int Next(int bound)
        {
            _x = _x * 6364136223846793005UL + 1442695040888963407UL;
            return (int)((_x >> 33) % (ulong)bound);
        }
    }

    /// <summary>What a run leaves to compare: the replayable dump, every refusal by tick, and the navigation counters at each save.</summary>
    private sealed record Outcome(string Dump, List<(long Tick, string Reason)> Rejected, List<string> Counters, int Saves, Dictionary<string, int> Done);

    // T10 (R-B3, the chest-crash class)
    /// <summary>
    /// From the Crossing Workshop's start, 500 commands from a seeded script - placements of every piece on the area's lattice and one
    /// module outside it, take-downs and repairs of standing and unknown pieces, stores and takes at the chests, doors worked, blows
    /// at pieces until they fall, and walking - with a save through the session and a load into a fresh one every 100th: nothing
    /// throws, every refusal arrives as a <see cref="CommandRejected"/>, each load equals the world saved, and the same script run again
    /// ends with an equal replayable dump, equal refusals and equal navigation counters. The raw digest is not compared: splitting a
    /// stack mints an item ID (G8).
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    public void BuildingCommands_NeverThrow(ulong seed)
    {
        var first = Run(seed);
        var second = Run(seed);
        Assert.Equal(4, first.Saves);
        Assert.Empty(StateDump.Compare(first.Dump, second.Dump, out _));
        Assert.Equal(first.Rejected, second.Rejected);
        Assert.Equal(first.Counters, second.Counters);
        Assert.Equal(first.Done, second.Done);
        // The script reaches what it is for: building, blows to destruction, mending, doors and the chests (seeds 1-5 place 20-54
        // pieces, strike 157-198 times, destroy 1-6 and store 2-9 times).
        foreach (string what in new[] { "placed", "damaged", "destroyed", "repaired", "door", "stored" })
            Assert.True(first.Done.GetValueOrDefault(what) > 0, $"seed {seed}: nothing {what}");
        Assert.True(first.Done["placed"] >= 10 && first.Done["damaged"] >= 50, $"seed {seed}: {first.Done["placed"]} placed, {first.Done["damaged"]} blows");
    }

    private static Outcome Run(ulong seed)
    {
        using var profile = new TempProfile();
        var random = new Lcg(seed);
        var session = BuildingAcceptanceTests.LoadS0(profile);
        var rejected = new List<(long Tick, string Reason)>();
        var counters = new List<string>();
        int saves = 0, since = 0;
        var done = new Dictionary<string, int>();
        void Count(string what) => done[what] = done.GetValueOrDefault(what) + 1;
        void Listen(GameSession s)
        {
            s.Subscribe<CommandRejected>(r => rejected.Add((r.Tick, r.Reason)));
            s.Subscribe<PiecePlaced>(_ => Count("placed"));
            s.Subscribe<PieceRemoved>(_ => Count("dismantled"));
            s.Subscribe<PieceDamaged>(_ => Count("damaged"));
            s.Subscribe<PieceDestroyed>(_ => Count("destroyed"));
            s.Subscribe<PieceRepaired>(_ => Count("repaired"));
            s.Subscribe<DoorToggled>(_ => Count("door"));
            s.Subscribe<ItemMoved>(m => Count(m.To.ContainerKey?.StartsWith("container.pce_", StringComparison.Ordinal) == true ? "stored" : "moved"));
        }
        // Every refusal a session's world logged arrived as an event, and nothing else did.
        void Refusals(Simulation simulation) =>
            Assert.Equal(simulation.CommandLog.Count(e => e.RejectedReason is not null), rejected.Count - since);
        Listen(session);

        for (int step = 1; step <= 500; step++)
        {
            var simulation = session.Simulation!;
            Act(session, simulation, random);
            if (step % 100 == 0 && step < 500)
            {
                // A save through the session, loaded by a fresh one that carries on: the load is the world saved.
                counters.Add(JsonSerializer.Serialize(simulation.Navigation.Counters));
                string saved = StateDump.Render(simulation);
                session.Save(SaveSlots.Quick);
                saves++;
                var next = Harness.Boot(profile);
                Assert.True(next.Load(SaveSlots.Quick).IsComplete);
                Assert.Empty(StateDump.Compare(saved, StateDump.Render(next.Simulation!), out _));
                Assert.Equal(0, session.SubscriberFailures);
                Refusals(simulation);
                since = rejected.Count;
                session = next;
                Listen(session);
            }
        }
        var end = session.Simulation!;
        counters.Add(JsonSerializer.Serialize(end.Navigation.Counters));
        Assert.Equal(0, session.SubscriberFailures);
        Refusals(end);
        return new Outcome(StateDump.Render(end, replayable: true), rejected, counters, saves, done);
    }

    /// <summary>
    /// One command of the script, and the frames it takes. The script aims at the world: it builds on the lattice round the player
    /// (pads, then walls and doorways on their edges, doors in doorways, roofs, chests and benches on pads), walks up to a piece before
    /// striking, storing, taking or working a door, and mends what is damaged; asks someone to work at a bench or lets them go (E9) - and,
    /// one time in six, places anything anywhere on the lattice, inside the area or a module beyond it, and takes down or mends an unknown
    /// piece, for the refusals.
    /// </summary>
    private static void Act(GameSession session, Simulation simulation, Lcg random)
    {
        var player = simulation.PlayerId;
        var pieces = simulation.Pieces;
        void Do(GameCommand command, int frames = 1)
        {
            session.Submit(command);
            for (int f = 0; f < frames; f++)
                session.Frame(session.TickSeconds);
        }
        void Idle() => Do(new MoveCommand(player, MoveIntent.Idle(simulation.Player.Body.FacingMdeg)));
        // Run towards a point until within reach of it, or for a while, then stand.
        void Approach(long x, long z, long withinMm)
        {
            for (int t = 0; t < 120; t++)
            {
                var body = simulation.Player.Body;
                double dx = x - body.XMm, dz = z - body.ZMm;
                if (Math.Sqrt(dx * dx + dz * dz) <= withinMm)
                    break;
                Do(new MoveCommand(player, Harness.Toward(dx, dz)));
            }
            Idle();
        }
        PieceView? Some(Func<PieceView, bool> which)
        {
            var some = pieces.Where(which).ToList();
            return some.Count == 0 ? null : some[random.Next(some.Count)];
        }
        EntityId Any() => pieces.IsEmpty || random.Next(6) == 0 ? EntityId.NewId(EntityKind.Piece) : pieces[random.Next(pieces.Length)].Id;
        (long X, long Z) Centre(PieceView p) => ((p.MinXMm + p.MaxXMm) / 2, (p.MinZMm + p.MaxZMm) / 2);

        int roll = random.Next(100);
        switch (roll)
        {
            case < 35:
            {
                string def;
                long x, z;
                int r = random.Next(4);
                var here = simulation.Player.Body;
                var pad = Some(p => p.Family == PieceFamily.Pad && Math.Abs(p.XMm - here.XMm) < 4_500 && Math.Abs(p.ZMm - here.ZMm) < 4_500);
                int kind = random.Next(6);
                if (kind == 0 || pad is null)
                {
                    // A pad on a square near the player - or, now and then, anything anywhere on the lattice. With no pad in reach, a pad.
                    var body = simulation.Player.Body;
                    int i = (int)Math.Round((body.XMm - 88_500) / 3_000.0) + random.Next(5) - 2;
                    int j = (int)Math.Round((body.ZMm - 88_500) / 3_000.0) + random.Next(5) - 2;
                    def = random.Next(6) == 0 ? Pieces[random.Next(Pieces.Length)] : "piece.pad.timber";
                    if (random.Next(6) == 0)
                        (i, j) = (random.Next(11) - 1, random.Next(11) - 1);
                    (x, z) = (88_500 + 3_000L * i, 88_500 + 3_000L * j);
                    if (def is "piece.wall.timber" or "piece.doorway.timber" or "piece.door.timber")
                        (x, z) = r % 2 == 0 ? (x, z - 1_500) : (x - 1_500, z);
                }
                else if (kind <= 2)
                {
                    // A wall or doorway on one of a pad's edges.
                    def = random.Next(3) == 0 ? "piece.doorway.timber" : "piece.wall.timber";
                    int edge = random.Next(4);
                    (x, z, r) = edge switch
                    {
                        0 => (pad.XMm, pad.ZMm - 1_500, 0),
                        1 => (pad.XMm, pad.ZMm + 1_500, 2),
                        2 => (pad.XMm - 1_500, pad.ZMm, 1),
                        _ => (pad.XMm + 1_500, pad.ZMm, 3),
                    };
                }
                else if (kind == 3 && Some(p => p.Family == PieceFamily.Doorway) is { } doorway)
                    (def, x, z, r) = ("piece.door.timber", doorway.XMm, doorway.ZMm, doorway.Rotation);
                else if (kind == 4)
                    (def, x, z) = ("piece.roof.timber", pad.XMm, pad.ZMm);
                else
                    (def, x, z) = (random.Next(3) == 0 ? "piece.station.anvil" : "piece.storage.chest", pad.XMm, pad.ZMm);
                Do(new PlacePieceCommand(player, def, x, z, r));
                break;
            }
            case < 38:
            {
                // Timber from the crossing's stack, when the pack runs low.
                var stack = simulation.Containers.Single(c => c.Site.Key == "container.timber_stack");
                Approach(stack.Site.XMm, stack.Site.ZMm, 1_200);
                if (stack.Items.FirstOrDefault(i => i.DefId == Timber) is { } wood)
                    Do(new MoveItemCommand(player, wood.Ref, ItemPlace.In(stack.Site.Key), ItemPlace.Carried, Math.Min(10, wood.Count)));
                break;
            }
            case < 42:
                Do(new DismantlePieceCommand(player, Any()));
                break;
            case < 47:
                Do(new RepairPieceCommand(player, Some(p => p.HealthCurrent < p.HealthMax)?.Id ?? Any()));
                break;
            case < 55:
            {
                // At a chest: store a little timber, or take everything back.
                if (Some(p => p.ContainerKey is not null) is not { } chest)
                {
                    Idle();
                    break;
                }
                var site = simulation.Containers.Single(c => c.Site.Key == chest.ContainerKey).Site;
                Approach(site.XMm, site.ZMm, 1_200);
                var timber = simulation.Player.Inventory.Where(e => e.DefId == Timber).OrderBy(e => e.Count).FirstOrDefault();
                if (random.Next(3) == 0 || timber is null)
                    Do(new TakeAllCommand(player, chest.ContainerKey!));
                else
                    Do(new MoveItemCommand(player, timber.ItemId.Value, ItemPlace.Carried, ItemPlace.In(chest.ContainerKey!), 1 + random.Next(2)));
                break;
            }
            case < 61:
            {
                if (Some(p => p.Family == PieceFamily.Door) is not { } door)
                {
                    Idle();
                    break;
                }
                var (x, z) = Centre(door);
                Approach(x, z, 1_200);
                Do(new InteractCommand(player, door.Id.Value));
                break;
            }
            case < 78:
            {
                // Up to a piece with parts, facing its middle, and one to four blows, each waited out: pieces fall.
                if (Some(p => !p.Parts.IsEmpty) is not { } target)
                {
                    Do(new AttackCommand(player), simulation.Combat.Weapon.TotalTicks + 1);
                    break;
                }
                var (x, z) = Centre(target);
                Approach(x, z, 1_300);
                var body = simulation.Player.Body;
                Do(new MoveCommand(player, MoveIntent.Idle(CombatRules.FacingTowards(body.XMm, body.ZMm, x, z))));
                for (int blow = 1 + random.Next(4); blow > 0; blow--)
                    Do(new AttackCommand(player), simulation.Combat.Weapon.TotalTicks + 1);
                break;
            }
            case < 80:
            {
                // E9: ask someone to work at a piece, or let someone go - mostly refused (out of reach, no station, not working), never thrown.
                string[] npcs = { "npc.ashen_hollow.kera_voss", "npc.ashen_hollow.renn_vale", "npc.ashen_hollow.tavar_orr", "npc.ashen_hollow.nobody" };
                string npc = npcs[random.Next(npcs.Length)];
                Do(random.Next(2) == 0 ? new AssignWorkerCommand(player, npc, Some(p => p.Family == PieceFamily.Station)?.Id ?? Any()) : new ReleaseWorkerCommand(player, npc));
                break;
            }
            default:
            {
                // A walk towards a point of the area and a little round it, then a stop.
                long tx = 86_000 + random.Next(29_000), tz = 86_000 + random.Next(29_000);
                int ticks = 5 + random.Next(20);
                for (int t = 0; t < ticks; t++)
                {
                    var body = simulation.Player.Body;
                    double dx = tx - body.XMm, dz = tz - body.ZMm;
                    if (Math.Sqrt(dx * dx + dz * dz) < 300)
                        break;
                    Do(new MoveCommand(player, Harness.Toward(dx, dz)));
                }
                Idle();
                break;
            }
        }
    }
}
