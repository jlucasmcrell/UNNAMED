using System.Collections.Immutable;
using UNNAMED.Domain.Combat;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// CMB-06: a shield as the one damage pipeline reads it - the front arc it covers, what it turns, what holding it against a blow costs as the
/// blow's weight rises, the weight that drives it aside, and a shot stopped at the front (COMBAT §17; CMB-24's guard is the same path).
/// </summary>
public class ShieldTests
{
    private static readonly ImmutableSortedDictionary<BodyRegion, int> NoArmor = ImmutableSortedDictionary<BodyRegion, int>.Empty;
    private static readonly ImmutableSortedDictionary<string, double> NoResistance = ImmutableSortedDictionary<string, double>.Empty;

    private static CombatConstants Constants() => new()
    {
        ArmorK = 50,
        BlockMitigationPercent = 70,
        BlockStaminaPerHit = 10,
        BlockWeightReference = 10,
        BlockArcMdeg = 120_000,
        RegionWeights = new Dictionary<BodyRegion, int> { [BodyRegion.Torso] = 100 }.ToImmutableSortedDictionary(),
        RegionMultipliers = new Dictionary<BodyRegion, double> { [BodyRegion.Torso] = 1.0 }.ToImmutableSortedDictionary(),
        StaggerThresholdPercent = 14.5,
    };

    private static readonly ShieldProfile Timber = new(140_000, 80, 50, 22);

    /// <summary>A blow that always lands, always in the torso, and never varies: the numbers are what is under test, not the dice.</summary>
    private static Strike Blow(int damage, string type = "physical_slash") =>
        new(new AttackProfile("ability.test", damage, damage, type, 1_400, 8, 3, 6, 5), 1.0, 0, 1.0) { AccuracyPercent = 100 };

    private static DefenseProfile Guarding(ShieldProfile? shield, int maxHealth = 100) => new(maxHealth, NoArmor, NoResistance, true, false) { Shield = shield };

    private static Func<uint, double> Dice(params double[] values)
    {
        int i = 0;
        return _ => i < values.Length ? values[i++] : 0.0;
    }

    [Fact]
    public void ARaisedShield_TurnsMoreOfABlow_ThanAWeaponsGuard()
    {
        var constants = Constants();
        var plain = CombatRules.Resolve(Blow(20), Guarding(null), constants, Dice(0.5, 0.5, 0.99, 0.99));
        var shielded = CombatRules.Resolve(Blow(20), Guarding(Timber), constants, Dice(0.5, 0.5, 0.99, 0.99));
        // A weapon's guard turns 70% of it, a shield 80%: the shield lets less through than the weapon does.
        Assert.True(plain.Blocked && shielded.Blocked);
        Assert.InRange(plain.Final, 6, 7);
        Assert.InRange(shielded.Final, 4, 5);
        Assert.True(shielded.Final < plain.Final);
        // An unguarded body takes the blow whole, so the turn is the guard's and not the armor's (there is none).
        var open = CombatRules.Resolve(Blow(20), new DefenseProfile(100, NoArmor, NoResistance, Blocking: false, Dodging: false), constants,
            Dice(0.5, 0.5, 0.99, 0.99));
        Assert.Equal(20, open.Final);
    }

    [Fact]
    public void TheShieldsPrice_RisesWithTheBlowsWeight_WhereAPlainGuardsIsFlat()
    {
        var constants = Constants();
        var light = CombatRules.Resolve(Blow(6), Guarding(Timber), constants, Dice(0.5, 0.5, 0.99, 0.99));
        var heavy = CombatRules.Resolve(Blow(18), Guarding(Timber), constants, Dice(0.5, 0.5, 0.99, 0.99));
        Assert.True(light.Blocked && heavy.Blocked);
        Assert.True(light.BlockStamina > 0);
        Assert.True(heavy.BlockStamina > light.BlockStamina, $"a heavier blow costs more: {light.BlockStamina} then {heavy.BlockStamina}");
        // Half the guard's price (stamina_percent 50), scaled by the weight against the table's reference.
        Assert.Equal(3, light.BlockStamina);
        Assert.Equal(9, heavy.BlockStamina);
        // A weapon's guard costs its flat price whatever the blow weighs.
        Assert.Equal(10, CombatRules.Resolve(Blow(6), Guarding(null), constants, Dice(0.5, 0.5, 0.99, 0.99)).BlockStamina);
        Assert.Equal(10, CombatRules.Resolve(Blow(18), Guarding(null), constants, Dice(0.5, 0.5, 0.99, 0.99)).BlockStamina);
    }

    [Fact]
    public void ABlowAtOrAboveTheBreakingWeight_DrivesTheShieldAside_AndTheBlowGoesIn()
    {
        var constants = Constants();
        var heavy = CombatRules.Resolve(Blow(22), Guarding(Timber), constants, Dice(0.5, 0.5, 0.99, 0.99));
        Assert.True(heavy.GuardBroken);
        Assert.False(heavy.Blocked);          // nothing turned: the shield is gone
        Assert.Equal(22, heavy.Final);        // and the blow is whole
        Assert.True(heavy.Staggered);         // the impact staggers the body behind it
        Assert.Equal(10, heavy.BlockStamina); // what it cost to hold a guard that was driven aside
        // One point under, the same blow is turned.
        var under = CombatRules.Resolve(Blow(21), Guarding(Timber), constants, Dice(0.5, 0.5, 0.99, 0.99));
        Assert.False(under.GuardBroken);
        Assert.True(under.Blocked);
    }

    [Fact]
    public void AnArrowAtTheFront_IsStopped_ByARaisedShield()
    {
        var constants = Constants();
        var arrow = Blow(14, "physical_pierce");
        var stopped = CombatRules.Resolve(arrow, Guarding(Timber), constants, Dice(0.5, 0.5, 0.99, 0.99));
        Assert.True(stopped.Blocked);
        Assert.True(stopped.BlockStamina > 0);
        // A guard lowered, or a blow from behind one, is no guard at all: the arrow goes in whole.
        var unguarded = CombatRules.Resolve(arrow, new DefenseProfile(100, NoArmor, NoResistance, Blocking: false, Dodging: false), constants,
            Dice(0.5, 0.5, 0.99, 0.99));
        Assert.False(unguarded.Blocked);
        Assert.Equal(14, unguarded.Final);
    }

    [Fact]
    public void AShieldsGuard_CoversMoreOfTheFrontThanAWeapons()
    {
        // A body facing +Z (0 degrees) with a blow coming in 65 degrees off its facing: inside a shield's 140-degree arc, outside a weapon's 120.
        const long x = 0, z = 0;
        const int facing = 0;
        double radians = 65 * Math.PI / 180;
        long ax = (long)(Math.Sin(radians) * 2_000), az = (long)(Math.Cos(radians) * 2_000);
        Assert.False(CombatRules.InFront(x, z, facing, ax, az, long.MaxValue / 4, 120_000));
        Assert.True(CombatRules.InFront(x, z, facing, ax, az, long.MaxValue / 4, 140_000));
        Assert.Equal(140_000, Timber.ArcMdeg);
    }

    [Fact]
    public void AShieldProfile_RefusesNumbersItCannotHave()
    {
        Assert.Null(Timber.Problem());
        Assert.Contains("arc_deg", new ShieldProfile(0, 80, 50, 22).Problem()!);
        Assert.Contains("mitigation_percent", new ShieldProfile(140_000, 101, 50, 22).Problem()!);
        Assert.Contains("break_power", new ShieldProfile(140_000, 80, 50, 0).Problem()!);
        Assert.Contains("stamina_percent", new ShieldProfile(140_000, 80, 900, 22).Problem()!);
    }
}
