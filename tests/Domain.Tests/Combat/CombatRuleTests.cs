using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Tests.Combat;

/// <summary>The one damage pipeline (PROTOTYPE.md §6.2 "combat resolution": determinism, armor, crit, resistances, stagger, reach).</summary>
public class CombatRuleTests
{
    private static readonly CombatConstants Constants = new()
    {
        RegionWeights = ImmutableSortedDictionary.CreateRange(new[]
        {
            KeyValuePair.Create(BodyRegion.Head, 10), KeyValuePair.Create(BodyRegion.Torso, 60), KeyValuePair.Create(BodyRegion.Limbs, 30),
        }),
        RegionMultipliers = ImmutableSortedDictionary.CreateRange(new[]
        {
            KeyValuePair.Create(BodyRegion.Head, 1.5), KeyValuePair.Create(BodyRegion.Torso, 1.0), KeyValuePair.Create(BodyRegion.Limbs, 0.75),
        }),
    };

    private static readonly AttackProfile Sword = new("item.weapon.rusted_sword", 7, 7, "physical_slash", 1_800, 6, 3, 5, 8);
    private static readonly AttackProfile Arrow = Sword with { Source = "item.weapon.hunting_bow", DamageMin = 9, DamageMax = 9, DamageType = "physical_pierce" };

    private static ImmutableSortedDictionary<BodyRegion, int> Armor(int head, int torso, int limbs) => ImmutableSortedDictionary.CreateRange(new[]
    {
        KeyValuePair.Create(BodyRegion.Head, head), KeyValuePair.Create(BodyRegion.Torso, torso), KeyValuePair.Create(BodyRegion.Limbs, limbs),
    });

    private static DefenseProfile Body(int maxHealth = 50, int torsoArmor = 0, bool blocking = false, bool dodging = false,
        ImmutableSortedDictionary<string, double>? resistances = null) =>
        new(maxHealth, Armor(0, torsoArmor, 0), resistances ?? ImmutableSortedDictionary.Create<string, double>(StringComparer.Ordinal), blocking, dodging);

    /// <summary>Region, damage, crit, effect and rounding rolls: a torso blow, no crit, the effect lands, fractions round down.</summary>
    private static double TorsoNoCrit(uint sample) => sample switch { 0 => 0.5, 2 => 0.99, 4 => 0.999, _ => 0.0 };

    private static Strike Plain(AttackProfile attack) => new(attack, 1.0, 5, 1.0);

    [Fact]
    public void Level_IsNotAnInput_TheSameBlowOnTheSameBodyWoundsTheSame()
    {
        // Resolve takes the blow, the body and the dice - nothing names a level (COMBAT §32). A level-50 character is
        // harder to hurt only through armor, resistances, guard and footwork.
        var parameters = typeof(CombatRules).GetMethod(nameof(CombatRules.Resolve))!.GetParameters();
        Assert.DoesNotContain(parameters, p => p.Name!.Contains("level", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(7, CombatRules.Resolve(Plain(Sword), Body(maxHealth: 50), Constants, TorsoNoCrit).Final);
        Assert.Equal(7, CombatRules.Resolve(Plain(Sword), Body(maxHealth: 5_000), Constants, TorsoNoCrit).Final);
    }

    [Fact]
    public void ArmorOnTheRegionStruck_Mitigates_AndPiercingFindsTheGaps()
    {
        // 50 armor with k = 50 halves a slash; a pierce ignores 30% of it: 35 / 85 mitigated.
        Assert.Equal(3, CombatRules.Resolve(Plain(Sword), Body(torsoArmor: 50), Constants, TorsoNoCrit).Final);        // 7 x 0.5 = 3.5
        Assert.Equal(5, CombatRules.Resolve(Plain(Arrow), Body(torsoArmor: 50), Constants, TorsoNoCrit).Final);        // 9 x 50/85 = 5.3
        // Armor is coverage: the same vest does nothing for a blow that lands on the head.
        double head(uint s) => s == 0 ? 0.0 : TorsoNoCrit(s);
        Assert.Equal(BodyRegion.Head, CombatRules.Resolve(Plain(Sword), Body(torsoArmor: 50), Constants, head).Region);
        Assert.Equal(10, CombatRules.Resolve(Plain(Sword), Body(torsoArmor: 50), Constants, head).Final);            // 7 x 1.5 = 10.5
    }

    [Fact]
    public void AFractionOfAPoint_IsKeptAsAChance_SoSmallArmorMattersOnAverage()
    {
        // A 5-point bite into 6 points of hide (a pierce sees 4.2): 5 x 50/54.2 = 4.61 on average, never a flat 5.
        var bite = Arrow with { DamageMin = 5, DamageMax = 5 };
        var random = new Random(11);
        double total = 0;
        for (int i = 0; i < 20_000; i++)
        {
            double rounding = random.NextDouble();
            total += CombatRules.Resolve(Plain(bite), Body(torsoArmor: 6), Constants, s => s == 4 ? rounding : TorsoNoCrit(s)).Final;
        }
        Assert.InRange(total / 20_000, 4.58, 4.65);
    }

    [Fact]
    public void ABlowThatLands_AlwaysWounds_HoweverHeavyTheArmor() =>
        Assert.Equal(1, CombatRules.Resolve(Plain(Sword), Body(torsoArmor: 100_000), Constants, TorsoNoCrit).Final);

    [Fact]
    public void ACriticalBlow_Multiplies_AndResistanceReducesItsType()
    {
        double crit(uint s) => s == 2 ? 0.0 : TorsoNoCrit(s);
        var hit = CombatRules.Resolve(Plain(Sword), Body(), Constants, crit);
        Assert.True(hit.Critical);
        Assert.Equal(10, hit.Final);   // 10.5, the fraction rolled down

        var fire = Sword with { DamageType = "fire" };
        var resistant = Body(resistances: ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[] { KeyValuePair.Create("fire", 0.5) }));
        Assert.Equal(3, CombatRules.Resolve(Plain(fire), resistant, Constants, TorsoNoCrit).Final);   // 3.5
        Assert.Equal(7, CombatRules.Resolve(Plain(Sword), resistant, Constants, TorsoNoCrit).Final);  // slashing is not fire
    }

    [Fact]
    public void ADodge_AvoidsEverything_AndAGuard_TakesMostOfAPhysicalBlow_AndAllItsStagger()
    {
        var dodged = CombatRules.Resolve(Plain(Sword), Body(dodging: true), Constants, TorsoNoCrit);
        Assert.Equal((0, true, false), (dodged.Final, dodged.Dodged, dodged.EffectApplied));

        var heavy = Sword with { DamageMin = 20, DamageMax = 20 };
        var open = CombatRules.Resolve(Plain(heavy), Body(), Constants, TorsoNoCrit);
        var guarded = CombatRules.Resolve(Plain(heavy), Body(blocking: true), Constants, TorsoNoCrit);
        Assert.True(open.Staggered);
        Assert.Equal((6, true, false), (guarded.Final, guarded.Blocked, guarded.Staggered));   // 20 x 0.3
    }

    [Fact]
    public void Stagger_ComesFromImpact_AndTheBladesPassiveTipsATorsoBlowOverTheThreshold()
    {
        // A wolf (50 health) staggers at 14.5% impact: 7.25. The rusted sword's torso blow is 7 - short, until the
        // one-hand blade's level-3 passive (x1.05) or a point of Might (x1.04) adds to it. Armor does not absorb impact.
        Assert.False(CombatRules.Resolve(Plain(Sword), Body(torsoArmor: 2), Constants, TorsoNoCrit).Staggered);
        Assert.True(CombatRules.Resolve(Plain(Sword) with { StaggerPower = 1.05 }, Body(torsoArmor: 2), Constants, TorsoNoCrit).Staggered);
        Assert.True(CombatRules.Resolve(Plain(Sword) with { DamageMultiplier = 1.04 }, Body(torsoArmor: 2), Constants, TorsoNoCrit).Staggered);
    }

    [Fact]
    public void Regions_FallByTheirWeights()
    {
        var counts = new Dictionary<BodyRegion, int>();
        var random = new Random(7);
        for (int i = 0; i < 100_000; i++)
        {
            double region = random.NextDouble();
            var hit = CombatRules.Resolve(Plain(Sword), Body(), Constants, s => s == 0 ? region : 0.5);
            counts[hit.Region] = counts.GetValueOrDefault(hit.Region) + 1;
        }
        Assert.InRange(counts[BodyRegion.Head], 9_500, 10_500);
        Assert.InRange(counts[BodyRegion.Torso], 59_300, 60_700);
        Assert.InRange(counts[BodyRegion.Limbs], 29_400, 30_600);
    }

    [Fact]
    public void Reach_AndTheFrontArc_Gate_ABlow()
    {
        // Facing +Z (0): a point 1.5 m ahead is inside a 1.8 m reach and a 90-degree arc; beside or behind is not.
        Assert.True(CombatRules.InFront(0, 0, 0, 0, 1_500, 1_800, 90_000));
        Assert.False(CombatRules.InFront(0, 0, 0, 0, 2_000, 1_800, 90_000));
        Assert.False(CombatRules.InFront(0, 0, 0, 1_500, 0, 1_800, 90_000));
        Assert.False(CombatRules.InFront(0, 0, 0, 0, -1_500, 1_800, 90_000));
        Assert.True(CombatRules.InFront(0, 0, 90_000, 1_500, 0, 1_800, 90_000));   // facing +X
        Assert.Equal(90_000, CombatRules.FacingTowards(0, 0, 1_000, 0));
        Assert.Equal(180_000, CombatRules.FacingTowards(0, 0, 0, -1_000));
    }

    [Fact]
    public void MightMultiplies_PhysicalBlows_AboveAndBelowTheBase()
    {
        Assert.Equal(1.08, CombatRules.MightMultiplier(12, 10, Constants), 6);
        Assert.Equal(0.96, CombatRules.MightMultiplier(9, 10, Constants), 6);
    }
}

public class EffectRuleTests
{
    private static readonly EffectDefinition Bleeding = new("effect.bleeding", "dot", StackPolicy.StackIntensity, 3, 120, 20)
    {
        DamagePerTick = 1,
        StaminaRegenMultiplier = 0.8,
    };

    private static readonly EffectDefinition Weakened = new("effect.weakened", "debuff", StackPolicy.Refresh, 1, 1_200, 0)
    {
        DamageDealtMultiplier = 0.75,
    };

    [Fact]
    public void StackingIntensity_AddsStacksToItsMaximum_AndEveryApplicationRestartsTheDuration()
    {
        var effects = ImmutableArray<ActiveEffect>.Empty;
        effects = EffectRules.Apply(effects, Bleeding, 100);
        Assert.Equal(new ActiveEffect("effect.bleeding", 1, 220, 120), Assert.Single(effects));
        effects = EffectRules.Apply(effects, Bleeding, 110);
        effects = EffectRules.Apply(effects, Bleeding, 130);
        effects = EffectRules.Apply(effects, Bleeding, 140);
        // Three stacks at most; the duration restarts; the tick schedule keeps its phase.
        Assert.Equal(new ActiveEffect("effect.bleeding", 3, 260, 120), Assert.Single(effects));
    }

    [Fact]
    public void Refresh_KeepsOneStack_AndModifiersCompoundByStack()
    {
        var effects = EffectRules.Apply(EffectRules.Apply(ImmutableArray<ActiveEffect>.Empty, Weakened, 0), Weakened, 50);
        Assert.Equal(new ActiveEffect("effect.weakened", 1, 1_250, 1), Assert.Single(effects));
        var definitions = new Dictionary<string, EffectDefinition> { [Bleeding.Id] = Bleeding, [Weakened.Id] = Weakened };
        Assert.Equal(0.75, EffectRules.DamageDealtMultiplier(effects, definitions), 6);
        effects = EffectRules.Apply(EffectRules.Apply(effects, Bleeding, 60), Bleeding, 61);
        Assert.Equal(0.64, EffectRules.StaminaRegenMultiplier(effects, definitions), 6);   // 0.8 x 0.8
        Assert.Equal(new[] { "effect.bleeding", "effect.weakened" }, effects.Select(e => e.EffectId));
    }
}

public class LineOfSightTests
{
    [Fact]
    public void ALineThroughAWall_IsStopped_AndOneBesideIt_IsNot()
    {
        var wall = new BoxBlocker("wall", 0, 1_000, 10_000, 1_400, 3_000);
        Assert.True(wall.Crosses(5_000, 0, 5_000, 3_000));
        Assert.False(wall.Crosses(12_000, 0, 12_000, 3_000));
        Assert.False(wall.Crosses(5_000, 0, 5_000, 900));   // stops short
        Assert.True(wall.Crosses(-1_000, 0, 11_000, 2_000));   // diagonal through it

        var trunk = new CircleBlocker("tree", 0, 5_000, 400, 7_000);
        Assert.True(trunk.Crosses(0, 0, 0, 10_000));
        Assert.False(trunk.Crosses(500, 0, 500, 10_000));
        Assert.True(trunk.Crosses(390, 0, 390, 10_000));
    }
}
