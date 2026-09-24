using System.Collections.Immutable;
using UNNAMED.Domain.Progression;
using static UNNAMED.Domain.Tests.Progression.TestRules;

namespace UNNAMED.Domain.Tests.Progression;

/// <summary>AX-ATTR (PROGRESSION.md §4.1, §11.3): allocation, bounded grants, and the derived values.</summary>
public class AttributeTests
{
    private static readonly ProgressionRules Rules = Default();

    private static CharacterProgression WithUnspent(int points) => CharacterProgression.Empty with { UnspentAttributePoints = points };

    [Fact]
    public void Allocation_SpendsUnspentPoints()
    {
        var p = ProgressionEngine.Allocate(WithUnspent(2), new AttributeAllocation(CharacterAttribute.Might, 1));

        Assert.Equal(1, p.Allocation[CharacterAttribute.Might]);
        Assert.Equal(1, p.UnspentAttributePoints);
        Assert.Equal(11, ProgressionEngine.AttributeValue(p, CharacterAttribute.Might, Rules));
        Assert.Equal(10, ProgressionEngine.AttributeValue(p, CharacterAttribute.Will, Rules));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    public void Allocation_CannotSpendPointsItDoesNotHave(int points)
    {
        Assert.Throws<ArgumentException>(() => ProgressionEngine.Allocate(WithUnspent(2), new AttributeAllocation(CharacterAttribute.Might, points)));
    }

    [Fact]
    public void PostCreationGrants_ShareTheBoundedBudget()
    {
        // §11.3: at most 8% of the 49 points levels 2..50 give, so 3.
        Assert.Equal(3, Rules.AttributeGrantBudget);
        var p = ProgressionEngine.Grant(CharacterProgression.Empty, new AttributeGrant(CharacterAttribute.Will, 2, GrantSource.Trainer, "npc.trainer.a"), Rules);
        p = ProgressionEngine.Grant(p, new AttributeGrant(CharacterAttribute.Might, 1, GrantSource.Quest, "quest.a"), Rules);

        Assert.Throws<InvalidOperationException>(() =>
            ProgressionEngine.Grant(p, new AttributeGrant(CharacterAttribute.Agility, 1, GrantSource.Item, "item.tonic.a"), Rules));
        Assert.Equal(12, ProgressionEngine.AttributeValue(p, CharacterAttribute.Will, Rules));
    }

    [Fact]
    public void CreationGrants_AreOutsideTheBudget_ButEverySourceGrantsOnce()
    {
        var p = ProgressionEngine.Grant(CharacterProgression.Empty, new AttributeGrant(CharacterAttribute.Might, 4, GrantSource.Creation, "archetype.warrior"), Rules);
        p = ProgressionEngine.Grant(p, new AttributeGrant(CharacterAttribute.Endurance, 2, GrantSource.Race, "species.kal"), Rules);

        Assert.Throws<InvalidOperationException>(() =>
            ProgressionEngine.Grant(p, new AttributeGrant(CharacterAttribute.Will, 1, GrantSource.Race, "species.kal"), Rules));
        Assert.Equal(14, ProgressionEngine.AttributeValue(p, CharacterAttribute.Might, Rules));
    }

    [Fact]
    public void DerivedValues_ComeFromTheAttributes_AndThereIsNoMana()
    {
        var baseline = ProgressionEngine.Derive(CharacterProgression.Empty, Rules);
        Assert.Equal(new DerivedStats(HealthMax: 120, StaminaMax: 100, FocusMax: 80, Resonance: 20, StrainTolerance: 40), baseline);

        var sturdier = ProgressionEngine.Allocate(WithUnspent(2), new AttributeAllocation(CharacterAttribute.Endurance, 2));
        var derived = ProgressionEngine.Derive(sturdier, Rules);
        Assert.Equal(130, derived.HealthMax);
        Assert.Equal(110, derived.StaminaMax);
        Assert.Equal(42, derived.StrainTolerance);

        // PROGRESSION.md §4.1: Health/Stamina/Focus plus Resonance/Strain, never a mana pool.
        var names = typeof(DerivedStats).GetProperties().Select(p => p.Name)
            .Concat(typeof(DerivedFormulas).GetProperties().Select(p => p.Name))
            .Concat(typeof(PoolState).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain(names, n => n.Contains("Mana", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AttributesNeverAdvanceByUse()
    {
        var p = CharacterProgression.Empty;
        for (int use = 0; use < 50; use++)
            p = ProgressionEngine.Practice(p, new SkillPractice(Blade, 40, PracticeOutcome.Success, use), Rules).Progression;

        Assert.All(ProgressionKeys.Attributes, a => Assert.Equal(Rules.AttributeBase, ProgressionEngine.AttributeValue(p, a, Rules)));
        Assert.Equal(ImmutableSortedDictionary<CharacterAttribute, int>.Empty, p.Allocation);
    }
}
