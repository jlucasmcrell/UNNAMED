using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit.Abstractions;

namespace UNNAMED.Soak.Tests;

public sealed class OfflineSoakTests(ITestOutputHelper output)
{
    [Fact, Trait("Category", "Smoke")]
    public void Reckoning_IsByteIdentical_WholeDailyAndMixed() => Run(183, 77);

    [SoakFact("Long"), Trait("Category", "Long")]
    public void ThreeReckonings_AreByteIdentical() => Run(549, 301);

    private void Run(int days, int seed)
    {
        using var w = new SoakWorld();
        var setup = SoakWorld.WithHireling(w.Session.Setup);
        long day = setup.Clock.TicksPerDay;
        var firstNode = setup.Layout.Nodes.First();
        setup = setup with {
            Combat = setup.Combat with { Spawns = setup.Combat.Spawns.Add(new SpawnSite("spawn.soak.wolf", firstNode.XMm, firstNode.ZMm + 1900, 0,
                ImmutableArray.Create(new SpawnMember("creature.beast.wolf_grey", "pack_hunter"))) { RespawnTicks = 2 * day }) },
        };
        // The hireling is a resident who hires on (WP12b): vendors and quest givers never do.
        var site = setup.NpcLife.Routines[SoakWorld.Hireling].Anchors.Values.First();
        var role = HirelingRole.Porter;
        // Deplete a node, expire an effect and a contract, and let the real markets settle.
        var node = setup.Layout.Nodes.First();
        var initial = w.New((ulong)seed, setup, r => SoakWorld.Place(SoakWorld.Fund(r, 20_000), setup, node.XMm, node.ZMm)
            .WithEffects(new[] { new ActiveEffect("effect.weakened", 1, 5 * day, 5 * day) })
            .WithCompanions(new[] { new CompanionRecord(SoakWorld.Hireling, CompanionOrder.Wait, CompanionCondition.Up,
                site.XMm, site.ZMm, 0, 60) { Membership = PartyMembership.Hireling,
                Contract = new HirelingContract(role, 7, 8, PartyRules.WageOnly) { Purse = 8 } } }));
        var visible = initial.Nodes.First(n => n.Name == node.Name);
        Assert.Null(w.Command(initial, new GatherCommand(initial.PlayerId, visible.Key)));
        for (int i = 0; i < 1_000 && initial.Creatures.Single(c => c.Key.Contains("spawn.soak.wolf", StringComparison.Ordinal)).Alive; i++)
        {
            var target = initial.Creatures.Single(c => c.Key.Contains("spawn.soak.wolf", StringComparison.Ordinal));
            var body = initial.Player.Body;
            int facing = CombatRules.FacingTowards(body.XMm, body.ZMm, target.Body.XMm, target.Body.ZMm);
            initial.Enqueue(new MoveCommand(initial.PlayerId, MoveIntent.Idle(facing)));
            if (initial.Combat.Phase == CombatPhase.Idle)
                initial.Enqueue(new AttackCommand(initial.PlayerId));
            initial.DrainCommands(); initial.Step();
        }
        var killed = initial.Creatures.Single(c => c.Key.Contains("spawn.soak.wolf", StringComparison.Ordinal));
        Assert.False(killed.Alive, $"wolf={killed.Body}, health={killed.Health}, player={initial.Player.Body}, health={initial.Combat.Health}");
        var whole = w.RoundTrip(initial);
        var daily = w.Load(setup);
        var mixed = w.Load(setup);
        // A new IAbstractAdvance implementation missing from the dispatch list must fail this gate.
        var implementations = typeof(Simulation).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IAbstractAdvance).IsAssignableFrom(t)).OrderBy(t => t.FullName).ToArray();
        var registered = ((IEnumerable<IAbstractAdvance>)typeof(Simulation)
            .GetField("_advances", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(whole)!)
            .Select(a => a.GetType()).OrderBy(t => t.FullName).ToArray();
        Assert.Equal(implementations, registered);
        output.WriteLine("Abstract coverage: " + string.Join(", ", registered.Select(t => t.Name)));
        var timer = Stopwatch.StartNew(); // elapsed benchmark only; never feeds the simulation
        whole.Advance(days * day);
        timer.Stop();
        Assert.True(whole.Creatures.Single(c => c.Key == killed.Key).Generation > killed.Generation, "respawn did not execute");
        Assert.True(whole.Nodes.Single(n => n.Key == visible.Key).Ready, "harvested node did not renew");
        Assert.Empty(whole.CaptureRecord().Companions); // the seven-day contract actually expired
        Assert.Empty(whole.Combat.Effects);
        output.WriteLine($"{days}-day single advance: {timer.Elapsed.TotalMilliseconds:F1} ms" +
            (timer.Elapsed.TotalSeconds > 2 ? " WARNING: exceeds 2 s catch-up budget" : " (within 2 s budget)"));
        for (int d = 0; d < days; d++) daily.Advance(day);
        var random = new Random(seed);
        for (long left = days * day; left > 0;)
        {
            long chunk = Math.Min(left, random.NextInt64(1, 11 * day));
            mixed.Advance(chunk);
            left -= chunk;
        }
        SoakWorld.Equal(whole, daily, $"{days} days, daily");
        SoakWorld.Equal(whole, mixed, $"{days} days, seed {seed}");
        Assert.Equal(whole.Calendar, mixed.Calendar);
        Assert.Equal(whole.Weather, mixed.Weather);
        Assert.Equal(whole.WeatherCursor, mixed.WeatherCursor);
        // The World Event Director (WP 1H): the same events, the same history, at every chunking.
        Assert.NotEmpty(whole.WorldHistory);
        Assert.Equal(whole.WorldHistory.ToArray(), daily.WorldHistory.ToArray());
        Assert.Equal(whole.WorldHistory.ToArray(), mixed.WorldHistory.ToArray());
        Assert.Equal(whole.WorldEvents.ToArray(), mixed.WorldEvents.ToArray());
        for (int i = 0; i < 40; i++) { whole.Step(); daily.Step(); mixed.Step(); }
        SoakWorld.Equal(whole, daily, "post-catch-up daily continuation");
        SoakWorld.Equal(whole, mixed, "post-catch-up mixed continuation");
    }
}
