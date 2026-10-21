using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Creatures;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// Spec 25-27's rules, read without a world (STEALTH_DETECTION_AND_THREAT.md §12-§14, §20): stealth as an information skill that never adds
/// damage, the control effects' diminishing return, and the takedown as an armour-and-vital check rather than a multiplier.
/// </summary>
public class StealthTests
{
    [Theory]
    [InlineData(true,true,true,Threat.Even,false,true)]
    [InlineData(true,true,true,Threat.Easy,false,true)]
    [InlineData(true,true,true,Threat.Hard,false,false)]
    [InlineData(true,true,true,Threat.Even,true,false)]
    [InlineData(true,false,true,Threat.Even,false,false)]
    [InlineData(true,true,false,Threat.Even,false,false)]
    [InlineData(false,true,true,Threat.Even,false,false)]
    public void ExecutionRequiresTheAmbushCircumstances(bool dagger,bool unaware,bool behind,Threat threat,bool boss,bool kill) =>
        Assert.Equal(kill,TakedownRules.Executes(dagger,unaware,behind,threat,boss));

    [Fact]
    public void AQualifyingExecution_KillsThroughArmourInTheOneResolver()
    {
        var result=TakedownRules.Resolve(new Strike(Dagger(),1,0,1) { Execution=true },Mailed(health:60),Constants,60,true,_=>.5);
        Assert.True(result.Killed);Assert.True(result.Hit.Final>=60);
    }
    private static readonly CombatConstants Constants = new()
    {
        RegionWeights = new Dictionary<BodyRegion, int> { [BodyRegion.Head] = 10, [BodyRegion.Torso] = 90 }.ToImmutableSortedDictionary(),
        RegionMultipliers = new Dictionary<BodyRegion, double> { [BodyRegion.Head] = 2.0, [BodyRegion.Torso] = 1.0 }.ToImmutableSortedDictionary(),
        PierceArmorIgnored = 0.3,
        CritMultiplier = 1.5,
        BaseCritPercent = 0,
        ArmorK = 50,
        StaggerThresholdPercent = 100,
    };

    private static AttackProfile Dagger(int damage = 6) =>
        new("item.weapon.iron_dagger", damage, damage, "physical_pierce", 900, 1, 1, 1, 10) { SneakMultiplier = 3.0 };

    private static AttackProfile Sword(int damage = 7) =>
        new("item.weapon.rusted_sword", damage, damage, "physical_slash", 1_800, 1, 1, 1, 10);

    private static DefenseProfile Bare(int health) => new(health, ImmutableSortedDictionary<BodyRegion, int>.Empty,
        ImmutableSortedDictionary<string, double>.Empty, Blocking: false, Dodging: false);

    private static DefenseProfile Mailed(int health) => new(health,
        new Dictionary<BodyRegion, int> { [BodyRegion.Head] = 60 }.ToImmutableSortedDictionary(),
        ImmutableSortedDictionary<string, double>.Empty, Blocking: false, Dodging: false);

    [Theory]
    [InlineData(0, 0, 1.0, 1.0)]
    [InlineData(1, 1, 0.85, 0.9)]
    [InlineData(3, 1, 0.85, 0.9)]
    [InlineData(4, 2, 0.7, 0.8)]
    [InlineData(8, 3, 0.55, 0.7)]
    [InlineData(40, 3, 0.55, 0.7)]
    public void StealthRanks_HaveTheSpecsNoiseAndAwarenessFactors(int level, int rank, double noise, double awareness)
    {
        Assert.Equal(rank, StealthRules.Rank(level));
        Assert.Equal(noise, StealthRules.NoiseFactor(level), 3);
        Assert.Equal(awareness, StealthRules.AwarenessFactor(level), 3);
    }

    [Fact]
    public void StealthIsUnawareBelowSuspicion_NotAFlag()
    {
        var rules = new AwarenessRules(Suspicious: 30, SightPerSecondAtRange: 40, SightPerSecondClose: 200, DecayPerSecond: 10,
            HeardNoise: 60, HeardCall: 80, SearchTicks: 120);
        Assert.True(StealthRules.Unaware(0, rules));
        Assert.True(StealthRules.Unaware(29, rules));
        Assert.False(StealthRules.Unaware(30, rules));
    }

    [Fact]
    public void TheDaggerClass_IsWhatATakedownAndASneakBlowRequire()
    {
        Assert.True(StealthRules.IsDagger(Dagger()));
        Assert.False(StealthRules.IsDagger(Sword()));
        Assert.False(StealthRules.IsDagger(Dagger() with { Ranged = true }));
    }

    [Fact]
    public void ASneakMultiplier_OnlyAppliesToATargetThatHasNotNoticed()
    {
        Assert.Equal(3.0, StealthRules.SneakMultiplier(Dagger(), unaware: true));
        Assert.Equal(1.0, StealthRules.SneakMultiplier(Dagger(), unaware: false));
        Assert.Equal(1.0, StealthRules.SneakMultiplier(Sword(), unaware: true));
    }

    [Fact]
    public void ControlEffects_ShrinkWithEachReapplication()
    {
        var sleep = new EffectDefinition("effect.sleep", "debuff", StackPolicy.StackIntensity, MaxStacks: 3, DurationTicks: 1_000, TickIntervalTicks: 0)
        {
            Control = ControlKind.Incapacitates,
        };
        var once = EffectRules.Apply(ImmutableArray<ActiveEffect>.Empty, sleep, now: 100);
        var twice = EffectRules.Apply(once, sleep, now: 120);
        var thrice = EffectRules.Apply(twice, sleep, now: 140);
        Assert.Equal(1_100, once.Single().ExpiresTick);
        // Half the time on the second application, a quarter on the third: the diminishing return, held in the effect itself.
        Assert.Equal(620, twice.Single().ExpiresTick);
        Assert.Equal(390, thrice.Single().ExpiresTick);
        Assert.Equal(3, thrice.Single().Stacks);
    }

    [Fact]
    public void AnOrdinaryEffect_KeepsItsFullDurationOnReapplication()
    {
        var venom = new EffectDefinition("effect.venom", "dot", StackPolicy.Refresh, MaxStacks: 1, DurationTicks: 500, TickIntervalTicks: 30);
        var once = EffectRules.Apply(ImmutableArray<ActiveEffect>.Empty, venom, now: 0);
        var twice = EffectRules.Apply(once, venom, now: 100);
        Assert.Equal(600, twice.Single().ExpiresTick);
    }

    [Fact]
    public void ATakedownOnAnUnarmouredHead_Kills()
    {
        // A dagger's 6 through spread on an unarmoured head, at the weapon's sneak multiplier: 6 x 3 x 2.
        var result = TakedownRules.Resolve(new Strike(Dagger(), 3.0, 0, 1.0), Bare(health: 20), Constants, health: 20, fromBehind: true, _ => 0.5);
        Assert.Equal(BodyRegion.Head, result.Region);
        Assert.True(result.Killed);
        Assert.Equal(36, result.Hit.Final);
    }

    [Fact]
    public void MailAndABigBody_LeaveTheKnifeReachingNothingThatMatters()
    {
        // A 60-point body behind 60 of mail on the head: the same blow wounds it and does not put it down (§14: armour and anatomy resist, no flag).
        var result = TakedownRules.Resolve(new Strike(Dagger(), 3.0, 0, 1.0), Mailed(health: 60), Constants, health: 60, fromBehind: true, _ => 0.5);
        Assert.False(result.Killed);
        Assert.InRange(result.Hit.Final, 15, 25);   // it is wounded, not immune: it turns round
    }

    [Fact]
    public void TheSameBlowOnAnUnarmouredBodyOfThatSize_ReachesTheVital()
    {
        // Armour is the whole difference: the blow is identical, and 36 through the head is what mail takes down to about twenty (§13).
        var bare = TakedownRules.Resolve(new Strike(Dagger(), 3.0, 0, 1.0), Bare(health: 60), Constants, health: 60, fromBehind: true, _ => 0.5);
        var mailed = TakedownRules.Resolve(new Strike(Dagger(), 3.0, 0, 1.0), Mailed(health: 60), Constants, health: 60, fromBehind: true, _ => 0.5);
        Assert.False(bare.Killed);                     // a big enough body survives one knife even unarmoured (§14's redundant anatomy)
        Assert.Equal(36, bare.Hit.Final);
        Assert.True(bare.Hit.Final > mailed.Hit.Final * 1.5);
    }

    [Fact]
    public void ATakedownFromInsideReach_AimsAtTheTorso()
    {
        var result = TakedownRules.Resolve(new Strike(Dagger(), 1.0, 0, 1.0), Bare(health: 20), Constants,
            health: 20, fromBehind: false, _ => 0.5);
        Assert.Equal(BodyRegion.Torso, result.Region);
    }
}
