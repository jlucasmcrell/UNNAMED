using System.Collections.Immutable;
using UNNAMED.Domain.Combat;

namespace UNNAMED.Application.Tests;

/// <summary>
/// CMB-38 and CMB-40: an encounter is a named machine of phases, and a weak point can be timed.
/// <para>
/// These ask the domain for the answers the world asks it for - which phase the fight is in, what it swings in that phase, and
/// whether a guarded weak point is open this instant - because that is where the rule lives: a phase machine that a test can
/// only reach through a whole simulated fight is a machine nobody can pin down.
/// </para>
/// </summary>
public class BossEncounterTests
{
    private static AttackProfile Blow(string source, int reachMm = 2_000) =>
        new(source, 10, 14, "physical_slash", reachMm, 20, 6, 30, 4);

    /// <summary>Two phases on health, a third that only the clock reaches.</summary>
    private static CreatureDefinition Reading(WeakPoint? weak = null) =>
        new("creature.boss.test.reading", "construct", 5, 300, ImmutableSortedDictionary<BodyRegion, int>.Empty,
            ImmutableSortedDictionary<string, double>.Empty, Blow("ability.test.sweep"), 2_000, 1_600, 150)
        {
            Moves = ImmutableArray.Create(Blow("ability.test.sweep"), Blow("ability.test.fall"), Blow("ability.test.grasp")),
            WeakPoint = weak,
            Phases = ImmutableArray.Create(
                new BossPhase("reading", 0.66, null, ImmutableArray.Create("ability.test.sweep")),
                new BossPhase("measuring", 0.33, null, ImmutableArray.Create("ability.test.fall")),
                new BossPhase("verdict", null, null, ImmutableArray.Create("ability.test.grasp"))),
        };

    [Fact]
    public void AFullHealthBossIsInItsFirstPhase_AndTheLastPhaseHasNoTrigger()
    {
        var boss = Reading();
        var (index, name, _) = BossEncounter.PhaseAt(boss, boss.MaxHealth, tick: 0);
        Assert.Equal(0, index);
        Assert.Equal("reading", name);
        Assert.Null(boss.Phases[^1].HealthBelow);
        Assert.Null(boss.Phases[^1].AfterTicks);
    }

    [Fact]
    public void HealthCarriesTheFightThroughItsPhases()
    {
        var boss = Reading();
        Assert.Equal("reading", BossEncounter.PhaseAt(boss, boss.MaxHealth, 0).Name);
        Assert.Equal("measuring", BossEncounter.PhaseAt(boss, (int)(boss.MaxHealth * 0.5), 0).Name);
        Assert.Equal("verdict", BossEncounter.PhaseAt(boss, 1, 0).Name);
    }

    [Fact]
    public void TicksCarryTheFightThroughAPhase_OnItsOwn()
    {
        var boss = Reading() with
        {
            Phases = ImmutableArray.Create(
                new BossPhase("waiting", null, 120, ImmutableArray<string>.Empty),
                new BossPhase("striking", 0.5, null, ImmutableArray<string>.Empty)),
        };
        Assert.Equal("waiting", BossEncounter.PhaseAt(boss, boss.MaxHealth, tick: 119).Name);
        Assert.Equal("striking", BossEncounter.PhaseAt(boss, boss.MaxHealth, tick: 120).Name);
    }

    [Fact]
    public void EachPhaseOffersItsOwnMoves_AndAPhaseWithNoneOffersTheWholeSet()
    {
        var boss = Reading();
        var full = BossEncounter.MovesAt(boss, boss.MaxHealth, 0);
        Assert.Single(full);
        Assert.Equal("ability.test.sweep", full[0].Source);

        var last = BossEncounter.MovesAt(boss, 1, 0);
        Assert.Single(last);
        Assert.Equal("ability.test.grasp", last[0].Source);

        var plain = BossEncounter.MovesAt(boss with { Phases = ImmutableArray<BossPhase>.Empty }, boss.MaxHealth, 0);
        Assert.Equal(3, plain.Length);
    }

    [Fact]
    public void AnUntimedWeakPointIsOpen_AndATimedOneIsGuardedUntilItsPhase()
    {
        var always = Reading(new WeakPoint(BodyRegion.Torso, FromBehind: true));
        Assert.True(BossEncounter.WeakPointOpen(always, always.MaxHealth, 0));

        var timed = Reading(new WeakPoint(BodyRegion.Torso, FromBehind: true)
        {
            Windows = ImmutableArray.Create(new WeakPointWindow("verdict", EveryTicks: 0, OpenTicks: 0)),
        });
        Assert.False(BossEncounter.WeakPointOpen(timed, timed.MaxHealth, 0));
        Assert.False(BossEncounter.WeakPointOpen(timed, (int)(timed.MaxHealth * 0.5), 0));
        Assert.True(BossEncounter.WeakPointOpen(timed, 1, 0));
    }

    [Fact]
    public void APeriodicWindowOpensAndClosesInsideItsPhase()
    {
        var window = new WeakPointWindow("verdict", EveryTicks: 300, OpenTicks: 90);
        Assert.True(window.OpenAt(0));
        Assert.True(window.OpenAt(89));
        Assert.False(window.OpenAt(90));
        Assert.False(window.OpenAt(299));
        Assert.True(window.OpenAt(300));
        Assert.False(window.OpenAt(390));
    }

    [Fact]
    public void AStandingWindowNeverCloses()
    {
        var window = new WeakPointWindow("verdict", EveryTicks: 0, OpenTicks: 0);
        Assert.True(window.Standing);
        Assert.True(window.OpenAt(0));
        Assert.True(window.OpenAt(100_000));
    }

    [Fact]
    public void APhaselessCreatureIsAOnePhaseFight()
    {
        var plain = Reading() with { Phases = ImmutableArray<BossPhase>.Empty };
        var (index, name, ticks) = BossEncounter.PhaseAt(plain, plain.MaxHealth / 2, tick: 5_000);
        Assert.Equal(0, index);
        Assert.Equal("", name);
        Assert.Equal(0, ticks);
        Assert.True(BossEncounter.WeakPointOpen(plain with { WeakPoint = new WeakPoint(BodyRegion.Head, false) }, 1, 0));
    }
}
