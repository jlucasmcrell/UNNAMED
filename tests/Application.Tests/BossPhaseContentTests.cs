using System.Collections.Immutable;
using UNNAMED.Domain.Combat;

namespace UNNAMED.Application.Tests;

/// <summary>
/// CMB-38 and CMB-40, wired: the content names its own phases and the runtime reads them back.
/// <para>
/// The domain tests pin the machine down; these ask the world that actually boots whether the 35 bosses carry it - every boss
/// has at least two named phases, every non-final phase says what ends it, every phase's moves are moves the boss really has,
/// and a timed weak point knows which phase it opens in.
/// </para>
/// </summary>
public class BossPhaseContentTests
{
    private static IReadOnlyDictionary<string, CreatureDefinition> Bosses()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.starting_frontier");
        return session.Setup.Combat.Creatures
            .Where(pair => pair.Key.StartsWith("creature.boss.", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    [Fact]
    public void EveryBossIsANamedPhaseMachine()
    {
        var bosses = Bosses();
        Assert.True(bosses.Count >= 35, $"expected the boss roster, found {bosses.Count}");
        foreach (var (id, boss) in bosses)
        {
            Assert.False(boss.Phases.IsDefaultOrEmpty, $"{id} has no phases");
            Assert.True(boss.Phases.Length >= 2, $"{id} is a one-phase fight: {boss.Phases.Length} phase(s)");
            var names = boss.Phases.Select(phase => phase.Name).ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
            Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name), $"{id} has an unnamed phase"));
        }
    }

    [Fact]
    public void EveryPhaseButTheLast_SaysWhatEndsIt()
    {
        foreach (var (id, boss) in Bosses())
        {
            for (int index = 0; index < boss.Phases.Length - 1; index++)
            {
                var phase = boss.Phases[index];
                Assert.True(phase.HealthBelow is not null || phase.AfterTicks is not null,
                    $"{id}'s phase '{phase.Name}' never ends: it names neither health_below nor after_ticks");
                if (phase.HealthBelow is { } fraction)
                    Assert.InRange(fraction, 0.01, 1.0);
                if (phase.AfterTicks is { } ticks)
                    Assert.True(ticks > 0, $"{id}'s phase '{phase.Name}' ends after {ticks} ticks");
            }
            var last = boss.Phases[^1];
            Assert.Null(last.HealthBelow);
            Assert.Null(last.AfterTicks);
        }
    }

    [Fact]
    public void EveryPhasesMovesAreMovesTheBossHas()
    {
        foreach (var (id, boss) in Bosses())
        {
            var own = boss.Moves.Select(move => move.Source).ToHashSet(StringComparer.Ordinal);
            foreach (var phase in boss.Phases)
            {
                if (phase.Moves.IsDefaultOrEmpty)
                    continue;
                Assert.All(phase.Moves,
                    move => Assert.Contains(move, own));
                _ = id;
            }
        }
    }

    [Fact]
    public void ATimedWeakPointOpensOnlyInThePhaseItNames()
    {
        var bosses = Bosses();
        var timed = bosses.Where(pair => pair.Value.WeakPoint is { Windows.IsDefaultOrEmpty: false }).ToList();
        Assert.NotEmpty(timed);
        foreach (var (id, boss) in timed)
        {
            var window = boss.WeakPoint!.Windows[0];
            var phaseIndex = boss.Phases.IndexOf(boss.Phases.FirstOrDefault(phase => phase.Name == window.Phase) ?? BossPhase.Named(""));
            Assert.True(phaseIndex >= 0, $"{id}'s weak point opens in '{window.Phase}', which is not one of its phases");
            // guarded while it is early in the fight
            Assert.False(BossEncounter.WeakPointOpen(boss, boss.MaxHealth, 0));
            // and open once the fight has reached that phase, at the top of the window
            Assert.True(window.OpenAt(0));
        }
    }

    [Fact]
    public void TheCustodiansGaugeIsGuardedThenOpen()
    {
        var bosses = Bosses();
        var custodian = bosses.SingleOrDefault(pair => pair.Key == "creature.boss.barrowworks.ring_custodian.deep");
        Assert.NotNull(custodian.Value);
        var boss = custodian.Value!;
        Assert.Equal("closed", BossEncounter.PhaseAt(boss, boss.MaxHealth, 0).Name);
        Assert.Equal("open", BossEncounter.PhaseAt(boss, 1, 0).Name);
        Assert.False(BossEncounter.WeakPointOpen(boss, boss.MaxHealth, 0));
        Assert.True(BossEncounter.WeakPointOpen(boss, 1, 0));
    }
}
