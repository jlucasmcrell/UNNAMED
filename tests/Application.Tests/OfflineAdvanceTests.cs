using System.Collections.Immutable;
using System.Diagnostics;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit.Abstractions;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP11: the world goes on while the character is away (SYSTEMS_COMPLETENESS_PLAN §2.12). Every system brings itself to the new tick
/// through <see cref="IAbstractAdvance"/>, and one long absence leaves the world that the same absence in pieces does (canon T-22).
/// </summary>
[Collection("Simulation timing budgets")]
public class OfflineAdvanceTests
{
    private const long Day = 57_600;
    private const long Reckoning = 183 * Day;

    private readonly ITestOutputHelper _output;

    public OfflineAdvanceTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A world with something due in it: a wolf killed (back two days later), its packmate roused, the character bleeding for longer than
    /// a Reckoning and weakened for five days - saved, so every copy loaded from it is the same world.
    /// </summary>
    private static (GameSession Session, Func<Simulation> Load) Lived(TempProfile profile)
    {
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (120, 60), 0,
            new[] { (Arena.Wolf, 120.0, 62.0, "pack_hunter"), (Arena.Wolf, 135.0, 75.0, "pack_hunter") }, respawnTicks: 2 * Day);
        arena.Fight(arena.Creature(0), 800);
        Assert.False(arena.Creature(0).Alive);
        arena.Tick(5);
        long now = arena.Simulation.WorldTick;
        var record = arena.Simulation.CaptureRecord().WithEffects(new[]
        {
            new ActiveEffect("effect.bleeding", 1, now + Reckoning + 3 * Day, now + 7),
            new ActiveEffect("effect.weakened", 1, now + 5 * Day, now + 5 * Day),
        });
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("away"), SaveDocuments.Capture(arena.Simulation.World, record, session.Content, now, 0));
        return (session, () =>
        {
            var loaded = store.Load(SaveSlots.Manual("away"), new LoadContext(session.Generator, session.Content, new Registry()));
            Assert.True(loaded.IsComplete);
            return Simulation.Start(arena.Simulation.Setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, new EventBus());
        });
    }

    /// <summary>Everything a player could see of the world, beyond the persistent digest: creatures, effects, nodes, the clock and the sky.</summary>
    private static object Seen(Simulation simulation) => (
        (simulation.WorldTick, simulation.Calendar, simulation.Weather, simulation.WeatherCursor),
        simulation.StateDigest(),
        string.Join(";", simulation.Creatures.OrderBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => $"{c.Key}:{c.Condition}:{c.Generation}:{c.Health}:{c.Body}:{c.Mind}:{c.Awareness}")),
        string.Join(";", simulation.Combat.Effects.Select(e => $"{e.EffectId}:{e.Stacks}:{e.ExpiresTick}")),
        string.Join(";", simulation.Nodes.Select(n => $"{n.Key}:{n.Ready}")),
        string.Join(";", simulation.Containers.Select(c => c.Site.Key)));

    [Fact]
    public void T22_OneReckoningAtOnce_IsTheSameWorldAs183OneDayAdvances()
    {
        using var profile = new TempProfile();
        var (_, load) = Lived(profile);
        var before = load();
        var once = load();
        var daily = load();
        Assert.Equal(Seen(before), Seen(daily));

        var whole = once.Advance(Reckoning);
        for (int day = 0; day < 183; day++)
            daily.Advance(Day);

        Assert.Equal(Seen(once), Seen(daily));
        Assert.Equal(before.WorldTick + Reckoning, once.WorldTick);
        Assert.NotEqual(before.StateDigest(), once.StateDigest());
        // And they go on alike: the next minute of play is the same in both.
        for (int i = 0; i < 1_200; i++)
        {
            once.Step();
            daily.Step();
        }
        Assert.Equal(Seen(once), Seen(daily));

        // Advance splits at creature respawns/pet recoveries; the clock logs each segment.
        Assert.Equal(Reckoning, whole.Lines.Where(l => l.Kind == AdvanceKinds.TimePassed).Sum(l=>l.Amount));
        Assert.InRange(whole.Lines.Where(l => l.Kind == AdvanceKinds.DaysPassed).Sum(l=>l.Amount), 182, 183);
        Assert.Equal("effect.weakened", whole.Lines.Single(l => l.Kind == AdvanceKinds.EffectExpired).Ref);
    }

    [Fact]
    public void AnyChunking_OfAnAbsence_EndsInTheSameWorld()
    {
        using var profile = new TempProfile();
        var (_, load) = Lived(profile);
        var once = load();
        once.Advance(3 * Day + 1_234);
        foreach (var pieces in new[] { new long[] { 1, 3 * Day + 1_233 }, new long[] { Day + 17, 0, 2 * Day + 1_217 }, new long[] { 1_234, Day, Day, Day } })
        {
            var chunked = load();
            foreach (long piece in pieces)
                chunked.Advance(piece);
            Assert.Equal(Seen(once), Seen(chunked));
        }
    }

    [Fact]
    public void AnAbsence_BringsTheDeadBack_SendsTheLivingHome_AndRunsOutTheCharactersEffects()
    {
        using var profile = new TempProfile();
        var (_, load) = Lived(profile);
        var simulation = load();
        long start = simulation.WorldTick;
        var bleeding = simulation.Combat.Effects.Single(e => e.EffectId == "effect.bleeding");

        simulation.Advance(Reckoning);

        Assert.All(simulation.Creatures, c =>
        {
            Assert.Equal(CreatureCondition.Alive, c.Condition);
            Assert.Equal(CreatureMind.Unaware, c.Mind);
            Assert.Equal(0, c.Awareness);
        });
        Assert.Equal(1, simulation.Creatures.OrderBy(c => c.Key, StringComparer.Ordinal).First().Generation);
        Assert.DoesNotContain(simulation.Containers, c => c.Site.Key.StartsWith("corpse.", StringComparison.Ordinal));
        Assert.Empty(simulation.World.Noises);
        // The weakness ran out in the absence; the bleed did not, and nothing of it ticked through it.
        var effect = Assert.Single(simulation.Combat.Effects);
        Assert.Equal(("effect.bleeding", bleeding.ExpiresTick), (effect.EffectId, effect.ExpiresTick));
        Assert.Equal(start + Reckoning, simulation.WorldTick);
    }

    [Fact]
    public void NoAbsence_ChangesNothing_AndTheWorldNeverGoesBack()
    {
        using var profile = new TempProfile();
        var (_, load) = Lived(profile);
        var simulation = load();
        var before = Seen(simulation);
        var log = simulation.Advance(0);
        Assert.Empty(log.Lines);
        Assert.Equal(before, Seen(simulation));
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Advance(-1));
    }

    [Fact]
    public void AStepAlignedOnAbsoluteTicks_VisitsTheSameBoundaries_HoweverTheSpanIsSplit()
    {
        long hour = Day / 24;
        var whole = AbstractAdvance.Steps(1_000, 1_000 + 3 * Day, hour).ToList();
        var split = AbstractAdvance.Steps(1_000, 50_000, hour).Concat(AbstractAdvance.Steps(50_000, 1_000 + 3 * Day, hour)).ToList();
        Assert.Equal(whole, split);
        Assert.Equal(72, whole.Count);
        Assert.All(whole, b => Assert.Equal(0, b % hour));
        Assert.Empty(AbstractAdvance.Steps(5, 5, hour));
    }

    [Fact]
    public void TheLoginGrace_KeepsANearbyWolfFromNoticing_UntilItEnds()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        long grace = session.Setup.Offline.LoginGraceTicks;
        Assert.Equal(200, grace);   // config.time: 10 s at 20 Hz

        Arena Close() => Arena.OpenCreatures(session, session.Setup, (120, 60), 0, new[] { (Arena.Wolf, 120.0, 64.0, "pack_hunter") });
        int Until(Arena arena, int limit)
        {
            for (int i = 1; i <= limit; i++)
            {
                // Sprinting to and fro beside it: loud, and never far.
                arena.Simulation.Enqueue(new MoveCommand(arena.Player, Harness.Toward(i / 20 % 2 == 0 ? 1 : -1, 0, Gait.Sprint)));
                arena.Tick();
                if (arena.Creature().Mind != CreatureMind.Unaware)
                    return i;
            }
            return -1;
        }

        int unguarded = Until(Close(), 400);
        Assert.InRange(unguarded, 1, (int)grace - 1);

        var guarded = Close();
        guarded.Simulation.BeginLoginGrace();
        Assert.Equal(guarded.Simulation.WorldTick + grace, guarded.Simulation.LoginGraceUntil);
        int noticed = Until(guarded, 1_000);
        Assert.True(noticed > grace, $"the wolf noticed the character {noticed} ticks into a {grace}-tick grace");
    }

    /// <summary>A stand-in for WP12's population: ~40 agents, each resolving an hourly band from a seeded table - the load the cap must bear.</summary>
    private sealed class HourlyPopulation : IAbstractAdvance
    {
        private const long Hour = Day / 24;
        private readonly long[] _state = new long[40];

        public long Digest => _state.Aggregate(17L, (h, v) => unchecked(h * 31 + v));

        public void Advance(long fromTick, long toTick, AdvanceLog log)
        {
            foreach (long boundary in AbstractAdvance.Steps(fromTick, toTick, Hour))
            {
                for (int agent = 0; agent < _state.Length; agent++)
                {
                    ulong x = unchecked((ulong)boundary * 0x9E3779B97F4A7C15UL ^ (ulong)agent * 0xBF58476D1CE4E5B9UL ^ (ulong)_state[agent]);
                    x = unchecked((x ^ (x >> 31)) * 0x94D049BB133111EBUL);
                    _state[agent] = (long)(x % 1_000);
                }
            }
        }
    }

    [Fact]
    public void TheCap_CatchesUpInTwoSecondsOrLess_WithAStubbedPopulation()
    {
        using var profile = new TempProfile();
        var (session, load) = Lived(profile);
        Assert.Equal(Reckoning, session.Setup.Offline.CapTicks);
        var simulation = load();
        var population = new HourlyPopulation();

        var clock = Stopwatch.StartNew();
        long from = simulation.WorldTick;
        var log = simulation.Advance(session.Setup.Offline.CapTicks);
        AbstractAdvance.Run(from, simulation.WorldTick, new[] { population }, log);
        clock.Stop();
        _output.WriteLine($"one Reckoning caught up in {clock.Elapsed.TotalMilliseconds:0.0} ms");
        Assert.True(clock.Elapsed <= TimeSpan.FromSeconds(2), $"the cap took {clock.Elapsed.TotalSeconds:0.00} s");

        var daily = new HourlyPopulation();
        for (long day = 0; day < 183; day++)
            daily.Advance(from + day * Day, from + (day + 1) * Day, new AdvanceLog(from + day * Day, from + (day + 1) * Day));
        Assert.Equal(population.Digest, daily.Digest);
    }
}
