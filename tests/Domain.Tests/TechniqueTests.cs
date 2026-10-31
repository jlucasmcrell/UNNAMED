using UNNAMED.Domain;
using UNNAMED.Domain.Combat;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// CMB-24 (CAP05 §4-5, §8; SKILLS_AND_TECHNIQUES §3.1): the slice's seven techniques as rules - what each does to a blow, what a live point
/// adds, what each kind may not be, and the armour-gap reader the gap thrust reads until CMB-04 owns it.
/// </summary>
public class TechniqueTests
{
    private static TechniqueDefinition Technique(TechniqueKind kind, double damage = 1.0) =>
        new("ability.catalogue.test", "skill.one_hand_blade", damage, 1.0, 0, 5, 40)
        {
            Kind = kind,
            ParryTicks = kind == TechniqueKind.Parry ? 8 : 0,
            OpeningTicks = kind == TechniqueKind.Parry ? 10 : 0,
            GuardBreakTicks = kind == TechniqueKind.GuardBeat ? 60 : 0,
            PointLiveTicks = kind == TechniqueKind.GivingGround ? 60 : 0,
            PointLiveReachMm = kind == TechniqueKind.GivingGround ? 400 : 0,
            PointLiveDamageMultiplier = kind == TechniqueKind.GivingGround ? 1.25 : 1.0,
            BraceTicks = kind == TechniqueKind.SetAgainstCharge ? 60 : 0,
            ChargeStopMultiplier = kind == TechniqueKind.SetAgainstCharge ? 1.8 : 1.0,
            Gap = kind == TechniqueKind.GapThrust ? ArmourGap.Throat : null,
            Silent = kind == TechniqueKind.QuietBlade,
            SteadyTicks = kind == TechniqueKind.SteadyDraw ? 60 : 0,
            SteadyCritPercent = kind == TechniqueKind.SteadyDraw ? 10 : 0,
        };

    private static readonly AttackProfile Sword = new("item.weapon.rusted_sword", 8, 12, "physical_slash", 1_400, 8, 3, 6, 5)
    {
        SkillId = "skill.one_hand_blade",
    };

    [Fact]
    public void EachKind_IsWhatItSaysOnTheProfile_SoTheOnePipelineCanReadIt()
    {
        Assert.False(TechniqueRules.Profile(Sword, Technique(TechniqueKind.Blow, 1.6)).BreaksGuard);
        Assert.True(TechniqueRules.Profile(Sword, Technique(TechniqueKind.GuardBeat, 0.6)).BreaksGuard);
        Assert.True(TechniqueRules.Profile(Sword, Technique(TechniqueKind.QuietBlade, 1.1)).Silent);
        Assert.Equal(ArmourGap.Throat, TechniqueRules.Profile(Sword, Technique(TechniqueKind.GapThrust, 0.8)).Gap);
        Assert.True(TechniqueRules.Profile(Sword, Technique(TechniqueKind.SteadyDraw, 1.15)).Steady);
        // A blow keeps the weapon's damage scaled, its reach and its timings; the kinds that are not a blow never make one.
        var heavy = TechniqueRules.Profile(Sword, Technique(TechniqueKind.Blow, 1.6));
        Assert.Equal((13, 19), (heavy.DamageMin, heavy.DamageMax));
        Assert.True(TechniqueKinds.IsBlow(TechniqueKind.GuardBeat));
        Assert.False(TechniqueKinds.IsBlow(TechniqueKind.Parry));
        Assert.False(TechniqueKinds.IsBlow(TechniqueKind.GivingGround));
        Assert.False(TechniqueKinds.IsBlow(TechniqueKind.SetAgainstCharge));
        Assert.Equal("set_against_charge", TechniqueKinds.Key(TechniqueKind.SetAgainstCharge));
        Assert.Equal(TechniqueKind.SetAgainstCharge, TechniqueKinds.Parse("set_against_charge"));
        Assert.Null(TechniqueKinds.Parse("warding"));
    }

    [Fact]
    public void APointKeptLive_MakesTheNextBlowLongerAndHarder_AndNeverWeaker()
    {
        var givingGround = Technique(TechniqueKind.GivingGround);
        var point = TechniqueRules.PointLive(Sword, givingGround);
        Assert.Equal((10, 15), (point.DamageMin, point.DamageMax));
        Assert.Equal(1_800, point.ReachMm);
        // A point that adds nothing changes nothing, and a bow's reach is its range, not its arm.
        Assert.Equal(Sword.DamageMax, TechniqueRules.PointLive(Sword, givingGround with { PointLiveDamageMultiplier = 1.0, PointLiveReachMm = 0 }).DamageMax);
        var bow = Sword with { Ranged = true, ReachMm = 18_000 };
        Assert.Equal(18_000, TechniqueRules.PointLive(bow, givingGround).ReachMm);
    }

    [Fact]
    public void EachKind_RefusesTheShapeItCannotBe()
    {
        Assert.Null(Technique(TechniqueKind.Parry).Problem());
        Assert.Contains("catches a blow for at least a tick", (Technique(TechniqueKind.Parry) with { ParryTicks = 0 }).Problem()!);
        Assert.Contains("keeps the guard down", (Technique(TechniqueKind.GuardBeat) with { GuardBreakTicks = 0 }).Problem()!);
        Assert.Contains("holds the point live", (Technique(TechniqueKind.GivingGround) with { PointLiveTicks = 0 }).Problem()!);
        Assert.Contains("plants for at least a tick", (Technique(TechniqueKind.SetAgainstCharge) with { BraceTicks = 0 }).Problem()!);
        Assert.Contains("names the gap", (Technique(TechniqueKind.GapThrust) with { Gap = null }).Problem()!);
        Assert.Contains("makes no swing noise", (Technique(TechniqueKind.QuietBlade) with { Silent = false }).Problem()!);
        Assert.Contains("holds an aim", (Technique(TechniqueKind.SteadyDraw) with { SteadyTicks = 0 }).Problem()!);
        // The shared rules still hold for every kind.
        Assert.Contains("multiplier_of_weapon is positive", (Technique(TechniqueKind.Blow) with { DamageMultiplier = 0 }).Problem()!);
        Assert.Contains("windup_multiplier", (Technique(TechniqueKind.Blow) with { WindupMultiplier = 0.5 }).Problem()!);
    }

    [Fact]
    public void TheStubGaps_AreOpenWhereCanonLeavesThem_AndTheNoGapReaderClosesEveryGap()
    {
        var target = EntityId.NewId(EntityKind.Character);
        var stub = StubArmourGaps.Instance;
        Assert.True(stub.OpenPermille(target, ArmourGap.Throat) > stub.OpenPermille(target, ArmourGap.Waist));
        Assert.All(Enum.GetValues<ArmourGap>(), gap => Assert.InRange(stub.OpenPermille(target, gap), 0, 1_000));
        Assert.All(Enum.GetValues<ArmourGap>(), gap => Assert.Equal(0, NoArmourGaps.Instance.OpenPermille(target, gap)));
        // The interface is the seam CMB-04 replaces: a reader that knows the body answers per body.
        IArmourGapReader perBody = new OneBodiesGaps();
        Assert.Equal(900, perBody.OpenPermille(target, ArmourGap.Face));
    }

    /// <summary>A stand-in for the reader CMB-04 will ship: it knows this body's face is open and nothing else is.</summary>
    private sealed class OneBodiesGaps : IArmourGapReader
    {
        public int OpenPermille(EntityId target, ArmourGap gap) => gap == ArmourGap.Face ? 900 : 0;
    }
}
