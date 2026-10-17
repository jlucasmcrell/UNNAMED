using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit.Abstractions;

namespace UNNAMED.Soak.Tests;

public sealed class MotionSoakTests(ITestOutputHelper output)
{
    [Fact, Trait("Category", "Smoke")]
    public void CommandMotion_Smoke() => Run(600, 510);

    [Fact, Trait("Category", "Smoke")]
    public void ThirtyMinutesOfCommandMotion() => Run(36_000, 511);

    private void Run(int ticks, int seed)
    {
        using var w = new SoakWorld();
        var setup = w.Session.Setup;
        var spawn = setup.Layout.Spawn;
        var sim = w.New((ulong)seed, change: r => r.WithCompanions(new[] {
            new UNNAMED.World.CompanionRecord("npc.ashen_hollow.tavar_orr", CompanionOrder.Follow,
                CompanionCondition.Up, spawn.XMm + 1000, spawn.ZMm, 0, 100) }));
        var random = new Random(seed);
        var allowed = new Dictionary<string, Body>();
        // Exempt only the named entity, on the exact command/tick publishing the event,
        // and only when the destination matches the event's authoritative body.
        w.Bus(sim).Subscribe<PlayerRespawned>(e => allowed[sim.PlayerId.Value] = e.Body);
        w.Bus(sim).Subscribe<PlayerTravelled>(e => {
            allowed[sim.PlayerId.Value] = e.Arrival;
            foreach (string npcId in e.Party)
            {
                var npc = sim.Npcs.Single(n => n.Id == npcId);
                double dx = npc.Body.XMm - e.Arrival.XMm, dz = npc.Body.ZMm - e.Arrival.ZMm;
                Assert.True(dx * dx + dz * dz <= 2002.0 * 2002.0, "party arrival outside documented 2 m ring");
                allowed[npc.InstanceId.Value] = npc.Body;
            }
        });
        var violations = new List<string>();
        var pushes = new List<string>();
        long observed = 0;
        int rejected = 0;
        w.Bus(sim).Subscribe<CommandRejected>(_ => rejected++);
        int angle = 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            allowed.Clear();
            var before = Positions(sim);
            if (tick % 120 == 0) angle = random.Next(8) * 45_000;
            double radians = angle * Math.PI / 180_000;
            sim.Enqueue(new MoveCommand(sim.PlayerId, new MoveIntent((int)(Math.Sin(radians) * 1000),
                (int)(Math.Cos(radians) * 1000), (Gait)random.Next(3), angle)));
            if (tick % 71 == 0) sim.Enqueue(new AttackCommand(sim.PlayerId));
            // Invalid command actors must never become an alternate position-write route.
            if (tick % 97 == 0) sim.Enqueue(new MoveCommand(EntityId.NewId(EntityKind.Character),
                new MoveIntent(1000, 0, Gait.Sprint, 90_000)));
            sim.DrainCommands(); sim.Step();
            var after = Positions(sim);
            foreach (var (id, old) in before)
            {
                if (!after.TryGetValue(id, out var next)) continue; // removed bodies/new generations are births, not movement
                observed++;
                double dx = next.Body.XMm - old.Body.XMm, dz = next.Body.ZMm - old.Body.ZMm;
                // Root locomotion speed is planar; terrain height and the prescribed jump arc are separate.
                double distance = Math.Sqrt(dx * dx + dz * dz);
                double limit = old.Lives ? LifeLimit(sim, old.Body) : old.LimitMm;
                bool teleport = allowed.TryGetValue(id, out var arrival) && arrival.XMm == next.Body.XMm && arrival.ZMm == next.Body.ZMm;
                if (distance <= limit || teleport) continue;
                // A collision's separation is a push, not motion: Kinematics resolves every sub-step by pushing the body's circle out of
                // what it overlaps, which can carry it past its own speed (W4-MOTION-03: a wolf 2.3 mm over its bound). It is allowed only
                // to a body that ends touching what it was pushed out of, and never further than the two footprints' radii; it is reported.
                string where = $"seed={seed} tick={sim.WorldTick} entity={id}: ({old.Body.XMm},{old.Body.ZMm}) -> ({next.Body.XMm},{next.Body.ZMm}), {distance:F2} mm > {limit:F2} mm";
                if (Push(sim, old.BlockerId, next.Body, old.RadiusMm) is { } push && distance <= limit + push.MaxMm)
                    pushes.Add($"{where}, pushed off {push.Id}");
                else
                    violations.Add(where);
            }
        }
        output.WriteLine($"seed={seed}: {ticks} ticks ({ticks * sim.Setup.TickMilliseconds / 1000.0:F0} simulated seconds), {observed} body intervals, {rejected} rejected commands, {pushes.Count} pushes, {violations.Count} violations");
        foreach (string push in pushes) output.WriteLine("push: " + push);
        Assert.True(observed > ticks && rejected > 0);
        Assert.True(violations.Count == 0, string.Join("\n", violations.Take(50)));
    }

    private sealed record Observed(Body Body, double LimitMm, long RadiusMm, string BlockerId, bool Lives);

    /// <summary>Each body and how far it may move in one tick: its top speed for a tick, plus two millimetres of integer rounding.</summary>
    private static Dictionary<string, Observed> Positions(Simulation sim)
    {
        double tick = sim.Setup.TickMilliseconds / 1000.0;
        double playerMax = Enum.GetValues<Gait>().Max(g => sim.Setup.Movement.SpeedMmPerSecond(g)) * tick + 2;
        long body = sim.Setup.Movement.BodyRadiusMm;
        var result = new Dictionary<string, Observed> { [sim.PlayerId.Value] = new(sim.Player.Body, playerMax, body, "player", false) };
        // Companions are moved by the party, at the character's bound; the others with a life walk WP12's tiers (LifeLimit).
        var lives = sim.Lives.Select(l => l.Key).Except(sim.Companions.Select(c => c.NpcId)).ToHashSet();
        foreach (var npc in sim.Npcs) result[npc.InstanceId.Value] = new(npc.Body, playerMax, body, npc.Id, lives.Contains(npc.Id));
        foreach (var creature in sim.Creatures)
        {
            var def = sim.Setup.Combat.Creatures[creature.DefId];
            double speed = Math.Max(def.MoveSpeedMmPerSecond, def.Charge?.ChargeSpeedMmPerSecond ?? 0);
            result[creature.Id.Value] = new(creature.Body, speed * tick + 2, def.RadiusMm, creature.Key, false);
        }
        foreach (var item in sim.WorldItems) result[item.Id.Value] = new(new Body(item.XMm, 0, item.ZMm, 0), 2, 0, item.Id.Value, false);
        return result;
    }

    /// <summary>
    /// WP12's tier simulations, bounded as NpcLifeWorldTests bounds them, by the tier the tick simulated the body at (the tiers are
    /// updated before anyone moves): tier A walks, held to the tick; tier B glides ten ticks of walking every tenth tick
    /// (NpcSystem.RegionalEveryTicks), with half a metre for stepping out of a wall; tier C and D are out of sight and parked by the hour.
    /// </summary>
    private static double LifeLimit(Simulation sim, Body was)
    {
        double tick = sim.Setup.TickMilliseconds / 1000.0;
        var tier = sim.CellTiers.GetValueOrDefault(CellKey.OfWorld(was.XMm / 1000.0, was.ZMm / 1000.0).ToString(), SimulationTier.D);
        return tier switch
        {
            SimulationTier.A => Enum.GetValues<Gait>().Max(g => sim.Setup.Movement.SpeedMmPerSecond(g)) * tick + 2,
            SimulationTier.B => sim.Setup.Movement.SpeedMmPerSecond(Gait.Walk) * tick * 10 + 500,
            _ => double.PositiveInfinity,
        };
    }

    /// <summary>
    /// What a body of this radius, here, touches besides itself - a structure, a closed door, a creature, a person or the character - and
    /// the furthest a separation from it could have moved the body: the two radii (a structure's taken as the body's own); null for nothing.
    /// </summary>
    private static (string Id, double MaxMm)? Push(Simulation sim, string self, Body at, long radiusMm)
    {
        long body = sim.Setup.Movement.BodyRadiusMm;
        var touched = sim.Setup.Layout.Space.Blockers.Concat(sim.DynamicBlockers)
            .Append(new CircleBlocker("player", sim.Player.Body.XMm, sim.Player.Body.ZMm, body, 0))
            .Concat(sim.Npcs.Where(n => sim.Companions.Any(c => c.NpcId == n.Id))
                .Select(n => (Blocker)new CircleBlocker(n.Id, n.Body.XMm, n.Body.ZMm, body, 0)))
            .Where(b => b.Id != self && b.Separation(at.XMm, at.ZMm, radiusMm + 2) is not null)
            .Select(b => (b.Id, MaxMm: (double)radiusMm + (b is CircleBlocker c ? c.RadiusMm : radiusMm)))
            .OrderByDescending(b => b.MaxMm).ToList();
        return touched.Count > 0 ? touched[0] : null;
    }
}