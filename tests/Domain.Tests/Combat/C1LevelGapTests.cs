using UNNAMED.Domain.Combat;
namespace UNNAMED.Domain.Tests.Combat;
public class C1LevelGapTests
{
    private static readonly Strike Blow = new(new AttackProfile("test",10,10,"physical_blunt",1000,5,2,5,0),1,0,1);
    [Fact] public void PeerAndOneLevelKeepAuthoredNumbers()
    { Assert.Equal(Blow, CombatRules.LevelGap(Blow,2,2)); Assert.Equal(Blow, CombatRules.LevelGap(Blow,2,3)); }
    [Fact] public void ALevelGapMissCannotStaggerOrApplyAnEffect()
    {
        var blow=CombatRules.LevelGap(Blow with { Attack=Blow.Attack with { ForcesStagger=true,OnHitEffect="venom",OnHitEffectPercent=100 } },1,4);
        var defense=new DefenseProfile(100,System.Collections.Immutable.ImmutableSortedDictionary<BodyRegion,int>.Empty,
            System.Collections.Immutable.ImmutableSortedDictionary<string,double>.Empty,false,false);
        var hit=CombatRules.Resolve(blow,defense,new CombatConstants(),sample=>sample==5?.999:0);
        Assert.Equal(0,hit.Final);Assert.False(hit.Staggered);Assert.False(hit.EffectApplied);
    }
    [Fact] public void TwoLevelsMakeTradingWorseInBothDirections()
    { var weak=CombatRules.LevelGap(Blow,2,4);var strong=CombatRules.LevelGap(Blow,4,2);
      Assert.True(weak.DamageMultiplier<1); Assert.True(weak.AccuracyPercent<100); Assert.True(strong.DamageMultiplier>1); }
}
