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
    private static CreatureView View(CombatPhase phase = CombatPhase.Windup) => new(default, "boss", "creature.test", "", new(0, 0, 0, 45000),
        150, 150, phase, 11, CreatureCondition.Alive, CreatureMind.Engaged, 100, false)
        { AttackSource = Sweep.Source, PhaseDurationTicks = 22 };

    [Fact]
    public void TheFootprintReadsTheActualAttackAndCombatArc()
    {
        var tell = BossTelegraphModel.Of(View(), Boss(Sweep), new CombatConstants { MeleeArcMdeg = 110000 }, 350, 0, 50)!;
        Assert.Equal(2.95f, tell.ReachM);
        Assert.Equal(110, tell.ArcDegrees);
        Assert.Equal(.5f, tell.Progress);
        Assert.False(tell.Active);
        Assert.False(tell.Lane);
        Assert.Equal(45000, tell.FacingMdeg);
    }

    [Fact]
    public void EachMoveUsesItsOwnReachAndPattern()
    {
        var fall = Sweep with { Source = "ability.creature.measure_fall", ReachMm = 1700 };
        var grasp = Sweep with { Source = "ability.creature.index_grasp", ReachMm = 1200 };
        var boss = Boss(Sweep, fall, grasp);
        var a = BossTelegraphModel.Of(View() with { AttackSource = fall.Source }, boss, new(), 350, 0, 50)!;
        var b = BossTelegraphModel.Of(View() with { AttackSource = grasp.Source }, boss, new(), 350, 0, 50)!;
        Assert.Equal((BossTellPattern.Fall, 2.05f), (a.Pattern, a.ReachM));
        Assert.Equal((BossTellPattern.Grasp, 1.55f), (b.Pattern, b.ReachM));
    }

    [Fact]
    public void ChargeShowsTheContactCapsuleAndOnlyItsRemainingCommittedTravel()
    {
        var charge = Sweep with { Source = "ability.creature.matriarch_lunge", ChargeSpeedMmPerSecond = 6000, ReachMm = 7000 };
        var view = View() with { AttackTick = 100, ChargeFacingMdeg = 90000, AttackSource = charge.Source };
        var windup = BossTelegraphModel.Of(view, Boss(charge), new(), 350, 111, 50)!;
        Assert.True(windup.Lane);
        Assert.Equal(1.25f, windup.HalfWidthM); // Same300mm contact allowance as the real strike.
        Assert.Equal(8.15f, windup.ReachM); // 23 whole300mm steps, then the contact disk.
        Assert.Equal(90000, windup.FacingMdeg); // Travel direction, not the body facing.
        var active = BossTelegraphModel.Of(view with { Phase = CombatPhase.Active, PhaseTicksLeft = 0 }, Boss(charge), new(), 350, 132, 50)!;
        Assert.Equal(5.15f, active.ReachM); // Ten steps have already happened; never paint a fresh full lane.
        Assert.Equal(1, active.Progress);
        var end = BossTelegraphModel.Of(view with { Phase = CombatPhase.Active }, Boss(charge), new(), 350, 145, 50)!;
        Assert.Equal(1.25f, end.ReachM); // No travel left; contact around the current body remains.
        var otherClock = BossTelegraphModel.Of(view with { Phase = CombatPhase.Active }, Boss(charge), new(), 350, 132, 100)!;
        Assert.Equal(1.85f, otherClock.ReachM); // Reads the configured tick size, not a baked50ms clock.
    }

    [Fact]
    public void AMovingMeleeTellIncludesOnlyTheUnspentLunge()
    {
        var lunge = Sweep with { LungeMm = 1800 };
        var windup = BossTelegraphModel.Of(View(), Boss(lunge), new(), 350, 0, 50)!;
        Assert.Equal(4.75f, windup.ReachM);
        var active = BossTelegraphModel.Of(View(CombatPhase.Active) with { PhaseTicksLeft = 5, PhaseDurationTicks = 10 }, Boss(lunge), new(), 350, 0, 50)!;
        Assert.Equal(3.85f, active.ReachM);
        Assert.False(active.Lane);
    }

    [Fact]
    public void ThereIsNoStaleOrInventedTellOutsideAnAliveBossAttack()
    {
        Assert.Null(BossTelegraphModel.Of(View(CombatPhase.Recovery), Boss(Sweep), new(), 350, 0, 50));
        Assert.Null(BossTelegraphModel.Of(View() with { AttackSource = "missing" }, Boss(Sweep), new(), 350, 0, 50));
        Assert.Null(BossTelegraphModel.Of(View() with { Health = 0, Condition = CreatureCondition.Corpse }, Boss(Sweep), new(), 350, 0, 50));
        Assert.Null(BossTelegraphModel.Of(View(), Boss(Sweep) with { Tags = ImmutableSortedSet<string>.Empty }, new(), 350, 0, 50));
    }
}
