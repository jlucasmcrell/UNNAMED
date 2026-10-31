// UNNAMED Domain tests - CMB-16: shove, trip to prone and disarm, and that prone is not downed

using UNNAMED.Domain.Combat;
using Xunit;

namespace UNNAMED.Domain.Tests;

public sealed class ShoveTripDisarmTests
{
    private static ControlProfile Actor(int stamina = 100, int reach = 1, double weight = 80) => new(stamina, reach, weight);

    [Fact]
    public void AShoveCostsStaminaWhetherOrNotItLands()
    {
        var hit = ControlRules.Shove(Actor(), Footing.Standing, targetGuarding: false, distanceM: 1.0);
        Assert.True(hit.Landed);
        Assert.Equal(ControlRules.ShoveStamina, hit.StaminaSpent);

        var guarded = ControlRules.Shove(Actor(), Footing.Standing, targetGuarding: true, distanceM: 1.0);
        Assert.True(guarded.Landed);
        Assert.Equal(ControlRules.ShoveStamina, guarded.StaminaSpent);
    }

    [Fact]
    public void AShoveKnocksDownOnlyAnUnguardedTargetThatCanBeKnockedDown()
    {
        var unguarded = ControlRules.Shove(Actor(), Footing.Standing, targetGuarding: false, distanceM: 1.0);
        Assert.True(unguarded.Target.Prone);

        var guarded = ControlRules.Shove(Actor(), Footing.Standing, targetGuarding: true, distanceM: 1.0);
        Assert.False(guarded.Target.Prone);
    }

    [Fact]
    public void AShoveOutOfReachCostsStaminaAndDoesNothing()
    {
        var far = ControlRules.Shove(Actor(), Footing.Standing, targetGuarding: false, distanceM: 4.0);
        Assert.False(far.Landed);
        Assert.False(far.Target.Prone);
        Assert.Equal(ControlRules.ShoveStamina, far.StaminaSpent);
    }

    [Fact]
    public void ShoveAndTripAreRefusedWithoutTheStamina()
    {
        var tired = Actor(stamina: 5);
        Assert.False(ControlRules.Shove(tired, Footing.Standing, false, 1.0).Landed);
        Assert.Equal(0, ControlRules.Shove(tired, Footing.Standing, false, 1.0).StaminaSpent);
        Assert.False(ControlRules.Trip(tired, Footing.Standing, false, 80).Landed);
        Assert.False(ControlRules.Disarm(tired, 1, true).Landed);
    }

    [Fact]
    public void ProneRisesInOneSecondAndIsImmuneForThree()
    {
        var trip = ControlRules.Trip(Actor(), Footing.Standing, targetGuarding: false, targetWeightKg: 80);
        Assert.True(trip.Landed);
        Assert.True(trip.Target.Prone);

        // 20 ticks at 20 a second is the second CMB-16 names.
        var footing = trip.Target;
        for (int i = 0; i < ControlRules.ProneRiseTicks; i++)
            footing = footing.Tick();
        Assert.False(footing.Prone);

        // Three seconds of immunity: 60 ticks before another knock-down is possible.
        Assert.True(footing.ImmunityTicksRemaining > 0);
        Assert.False(footing.CanBeKnockedDown);
        for (int i = 0; i < ControlRules.ProneImmunityTicks; i++)
            footing = footing.Tick();
        Assert.True(footing.CanBeKnockedDown);
    }

    [Fact]
    public void AGuardedOrRecentlyRisenTargetCannotBeTrippedAgain()
    {
        Assert.False(ControlRules.Trip(Actor(), Footing.Standing, targetGuarding: true, targetWeightKg: 70).Landed);

        var newlyRisen = new Footing(false, 0, ControlRules.ProneImmunityTicks);
        var again = ControlRules.Trip(Actor(), newlyRisen, targetGuarding: false, targetWeightKg: 70);
        Assert.False(again.Landed);
        Assert.False(again.Target.Prone);
    }

    [Fact]
    public void WeightResistsATrip()
    {
        Assert.True(ControlRules.Trip(Actor(weight: 80), Footing.Standing, false, 100).Landed);
        Assert.False(ControlRules.Trip(Actor(weight: 60), Footing.Standing, false, 120).Landed);
    }

    [Fact]
    public void ReachIsTheCounterToDisarm()
    {
        Assert.True(ControlRules.Disarm(Actor(reach: 2), targetReachM: 2, targetHoldingWeapon: true).Disarmed);
        var longWeapon = ControlRules.Disarm(Actor(reach: 1), targetReachM: 4, targetHoldingWeapon: true);
        Assert.False(longWeapon.Disarmed);
        Assert.Equal(ControlRules.DisarmStamina, longWeapon.StaminaSpent);
        Assert.False(ControlRules.Disarm(Actor(), 1, targetHoldingWeapon: false).Disarmed);
    }

    [Fact]
    public void ProneIsNotDowned()
    {
        // The two states must never share a timer: prone is 1 s of footing, downed is CombatConstants.DownedTicks of defeat.
        var constants = new CombatConstants();
        Assert.NotEqual(constants.DownedTicks, ControlRules.ProneRiseTicks);
        Assert.True(ControlRules.ProneRiseTicks < constants.DownedTicks / 10);
        var prone = new Footing(true, ControlRules.ProneRiseTicks, ControlRules.ProneImmunityTicks);
        Assert.True(prone.Prone);
        Assert.True(prone.CanBeKnockedDown == false);
    }

    [Fact]
    public void ACreatureKnowsWhatWasDoneToIt()
    {
        var disarmed = ControlRules.Disarm(Actor(), 1, true);
        Assert.Contains("weapon", ControlRules.AwarenessFor(disarmed));

        var tripped = ControlRules.Trip(Actor(), Footing.Standing, false, 70);
        Assert.Contains("tripped", ControlRules.AwarenessFor(tripped));

        var blocked = ControlRules.Trip(Actor(), Footing.Standing, targetGuarding: true, targetWeightKg: 70);
        Assert.Equal("unaffected", ControlRules.AwarenessFor(blocked));
    }
}
