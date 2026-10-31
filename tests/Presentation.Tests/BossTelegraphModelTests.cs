using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Presentation.Ui.Models;
using UNNAMED.World.Runtime;
using UNNAMED.World;

namespace UNNAMED.Presentation.Tests;

public class BossTelegraphModelTests
{
    private static readonly AttackProfile Sweep = new("ability.creature.gauge_sweep", 24, 30, "physical_blunt", 2600, 22, 5, 26, 0);
    private static CreatureDefinition Boss(params AttackProfile[] attacks) => new("creature.test", "construct", 5, 150,
        ImmutableSortedDictionary<BodyRegion, int>.Empty, ImmutableSortedDictionary<string, double>.Empty,
        attacks[0], 2000, 600, 150) { Moves = attacks.ToImmutableArray(), Tags = ImmutableSortedSet.Create("boss") };
    private static CreatureView View(CombatPhase phase = CombatPhase.Windup) => new(default, "boss", "creature.test", "", default,
        150, 150, phase, 11, CreatureCondition.Alive, CreatureMind.Engaged, 100, false)
        { AttackSource = Sweep.Source, PhaseDurationTicks = 22 };

    [Fact]
    public void TheFootprintReadsTheActualAttackAndCombatArc()
    {
        var tell = BossTelegraphModel.Of(View(), Boss(Sweep), new CombatConstants { MeleeArcMdeg = 110000 }, 350)!;
        Assert.Equal(2.95f, tell.ReachM);
        Assert.Equal(110, tell.ArcDegrees);
        Assert.Equal(.5f, tell.Progress);
        Assert.False(tell.Active);
        Assert.False(tell.Lane);
    }

    [Fact]
    public void EachMoveUsesItsOwnReachAndPattern()
    {
        var fall = Sweep with { Source = "ability.creature.measure_fall", ReachMm = 1700 };
        var grasp = Sweep with { Source = "ability.creature.index_grasp", ReachMm = 1200 };
        var boss = Boss(Sweep, fall, grasp);
        var a = BossTelegraphModel.Of(View() with { AttackSource = fall.Source }, boss, new(), 350)!;
        var b = BossTelegraphModel.Of(View() with { AttackSource = grasp.Source }, boss, new(), 350)!;
        Assert.Equal((BossTellPattern.Fall, 2.05f), (a.Pattern, a.ReachM));
        Assert.Equal((BossTellPattern.Grasp, 1.55f), (b.Pattern, b.ReachM));
    }

    [Fact]
    public void ChargeShowsTheCommittedLaneAndActiveTellIsComplete()
    {
        var charge = Sweep with { Source = "ability.creature.matriarch_lunge", ChargeSpeedMmPerSecond = 6000, ReachMm = 7000 };
        var tell = BossTelegraphModel.Of(View(CombatPhase.Active) with { AttackSource = charge.Source }, Boss(charge), new(), 350)!;
        Assert.True(tell.Lane);
        Assert.Equal(.95f, tell.HalfWidthM);
        Assert.Equal(7.35f, tell.ReachM);
        Assert.Equal(1, tell.Progress);
    }

    [Fact]
    public void ThereIsNoStaleOrInventedTellOutsideAnAliveBossAttack()
    {
        Assert.Null(BossTelegraphModel.Of(View(CombatPhase.Recovery), Boss(Sweep), new(), 350));
        Assert.Null(BossTelegraphModel.Of(View() with { AttackSource = "missing" }, Boss(Sweep), new(), 350));
        Assert.Null(BossTelegraphModel.Of(View() with { Health = 0, Condition = CreatureCondition.Corpse }, Boss(Sweep), new(), 350));
        Assert.Null(BossTelegraphModel.Of(View(), Boss(Sweep) with { Tags = ImmutableSortedSet<string>.Empty }, new(), 350));
    }
}
