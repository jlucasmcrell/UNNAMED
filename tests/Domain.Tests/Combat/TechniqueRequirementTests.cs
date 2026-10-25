using System.Collections.Immutable;
using UNNAMED.Domain.Combat;

namespace UNNAMED.Domain.Tests.Combat;

public class TechniqueRequirementTests
{
    private static TechniqueDefinition Technique(params TechniqueRequirement[] requirements) =>
        new("ability.test.hybrid", "skill.one_hand_blade", 1, 1, 0, 10, 0) { Requirements = requirements.ToImmutableArray() };

    [Theory]
    [InlineData(TechniqueRequirementKind.Skill, "skill.one_hand_blade")]
    [InlineData(TechniqueRequirementKind.Attribute, "precision")]
    [InlineData(TechniqueRequirementKind.Technique, "ability.blade.parry")]
    [InlineData(TechniqueRequirementKind.Equipment, "item.weapon.iron_dagger")]
    [InlineData(TechniqueRequirementKind.Body, "grounded")]
    [InlineData(TechniqueRequirementKind.Knowledge, "lore.grey_wolf")]
    [InlineData(TechniqueRequirementKind.Relationship, "npc.ashen_hollow.kera_voss")]
    [InlineData(TechniqueRequirementKind.Standing, "faction.test")]
    public void EveryRequirement_UsesItsActualThreshold(TechniqueRequirementKind kind, string reference)
    {
        var technique = Technique(new TechniqueRequirement(kind, reference, 15));
        Assert.NotNull(TechniqueRequirements.Refusal(technique, _ => 14));
        Assert.Null(TechniqueRequirements.Refusal(technique, _ => 15));
        Assert.Null(TechniqueRequirements.Refusal(technique, _ => 16));
    }

    [Fact]
    public void Hybrid_NeedsBothSkills()
    {
        var technique = Technique(new(TechniqueRequirementKind.Skill, "skill.one_hand_blade", 30),
            new(TechniqueRequirementKind.Skill, "skill.stealth", 15));
        Assert.NotNull(TechniqueRequirements.Refusal(technique, r => r.Ref == "skill.one_hand_blade" ? 30 : 14));
        Assert.Null(TechniqueRequirements.Refusal(technique, r => r.Minimum));
        Assert.Equal(2, technique.Requirements.Length);
    }

    [Theory]
    [InlineData(0)] [InlineData(10)] [InlineData(20)] [InlineData(60)]
    public void EqualAttributes_PreserveTheOriginalDamageBudget(int attribute)
    {
        foreach (var scaling in new[] { PhysicalScaling.Heavy, PhysicalScaling.Balanced, PhysicalScaling.Dagger, PhysicalScaling.Bow, PhysicalScaling.Polearm })
        {
            Assert.Null(scaling.Problem());
            Assert.Equal(CombatRules.MightMultiplier(attribute, 10, new CombatConstants()), scaling.Multiplier(attribute, attribute, attribute, 10, 4), 10);
        }
    }

    [Fact]
    public void Dagger_UsesAgilityAndPrecision_AndBowKeepsItsOwnSplit()
    {
        Assert.Equal(1, PhysicalScaling.Dagger.Multiplier(60, 10, 10, 10, 4));
        Assert.Equal(2, PhysicalScaling.Dagger.Multiplier(10, 60, 10, 10, 4));
        Assert.Equal(2.2, PhysicalScaling.Bow.Multiplier(10, 10, 60, 10, 4), 10);
        Assert.NotNull(new PhysicalScaling(.5, .5, .5).Problem());
        Assert.NotNull(new PhysicalScaling(double.NaN, 0, 0).Problem());
        Assert.NotNull(new PhysicalScaling(-1, 1, 1).Problem());
    }

    [Fact]
    public void GapArmour_RequiresAPlacedGapAttack_AndMissingGapsNeverEraseArmour()
    {
        var attack = new AttackProfile("dagger", 20, 20, "physical_pierce", 900, 4, 2, 2, 8);
        var defense = new DefenseProfile(100, ImmutableSortedDictionary<BodyRegion, int>.Empty.Add(BodyRegion.Torso, 50),
            ImmutableSortedDictionary<string, double>.Empty, false, false)
        { GapArmor = ImmutableSortedDictionary<BodyRegion, int>.Empty.Add(BodyRegion.Torso, 0) };
        int Hit(AttackProfile profile, DefenseProfile target) => CombatRules.Resolve(new(profile, 1, 0, 1), target, new CombatConstants(), _ => .5).Final;
        Assert.True(Hit(attack with { UsesGap = true, PlacedRegion = BodyRegion.Torso }, defense) > Hit(attack, defense));
        Assert.Equal(Hit(attack, defense), Hit(attack with { UsesGap = true }, defense));
        Assert.Equal(Hit(attack, defense), Hit(attack with { UsesGap = true, PlacedRegion = BodyRegion.Torso }, defense with { GapArmor = ImmutableSortedDictionary<BodyRegion, int>.Empty }));
    }
}
