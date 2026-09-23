using System.Collections.Immutable;
using UNNAMED.Domain.Progression;
using static UNNAMED.Domain.Tests.Progression.TestRules;

namespace UNNAMED.Domain.Tests.Progression;

/// <summary>The persisted record itself: its digest, its validation, and the definition-ID pass over it.</summary>
public class ProgressionRecordTests
{
    private static readonly ProgressionRules Rules = Default();

    private static CharacterProgression Sample()
    {
        var p = ProgressionEngine.Award(CharacterProgression.Empty, new XpAward(XpSource.Discovery, 500, 0), Rules).Progression;
        p = ProgressionEngine.Practice(p, new SkillPractice(Blade, 10, PracticeOutcome.Success, 1) { NoveltyKey = Salve }, Rules).Progression;
        p = ProgressionEngine.Learn(p, new TechniqueLearning(Bolt, LearningSource.Teacher, 2)).Progression;
        return ProgressionEngine.Award(p, Kill(Wolf, 2, "pop.r_0_0.c_00_02.wolves", 3, amount: 40), Rules).Progression;
    }

    [Fact]
    public void TheDigest_IsEqualForEqualRecords_AndChangesWithAnyField()
    {
        Assert.Equal(Sample().Digest, Sample().Digest);
        var changes = new[]
        {
            Sample() with { XpDebt = 1 },
            Sample() with { UnspentAttributePoints = 9 },
            Sample() with { Pools = new PoolState(10, null, null, 0) },
            Sample() with { Pools = PoolState.Full with { Strain = 1 } },
            ProgressionEngine.Learn(Sample(), new TechniqueLearning(Salve, LearningSource.Book, 4)).Progression,
            ProgressionEngine.Practice(Sample(), new SkillPractice(Athletics, 0, PracticeOutcome.Success, 4), Rules).Progression,
        };
        Assert.All(changes, changed => Assert.NotEqual(Sample().Digest, changed.Digest));
        Assert.Equal(changes.Length, changes.Select(c => c.Digest).Distinct().Count());
    }

    [Fact]
    public void Validation_RejectsOutOfRangeFields()
    {
        var bad = new[]
        {
            CharacterProgression.Empty with { Level = 0 },
            CharacterProgression.Empty with { XpDebt = -1 },
            CharacterProgression.Empty with { Skills = CharacterProgression.Empty.Skills.Add("item.weapon.sword", new SkillState(1, 0)) },
            CharacterProgression.Empty with { Skills = CharacterProgression.Empty.Skills.Add(Blade, new SkillState(101, 0)) },
            CharacterProgression.Empty with { Known = CharacterProgression.Empty.Known.Add("skill.athletics", new KnownTechnique(LearningSource.Book, null, 0)) },
            CharacterProgression.Empty with
            {
                Grants = ImmutableArray.Create(
                    new AttributeGrant(CharacterAttribute.Might, 1, GrantSource.Quest, "quest.a"),
                    new AttributeGrant(CharacterAttribute.Will, 1, GrantSource.Quest, "quest.a")),
            },
            CharacterProgression.Empty with { Pools = new PoolState(-1, null, null, 0) },
        };
        Assert.All(bad, p => Assert.Throws<FormatException>(() => p.Validate()));
        var valid = Sample();
        Assert.Same(valid, valid.Validate());
    }

    [Fact]
    public void TheDefinitionPass_RenamesAndDropsEveryStoredId()
    {
        var renames = new Dictionary<string, string?>
        {
            [Blade] = "skill.sword",
            [Bolt] = "spell.ember.flare",
            [Salve] = null,                      // removed with no replacement
            [Wolf] = "creature.beast.grey_wolf",
        };
        var roles = new List<string>();

        var rewritten = Sample().RewriteDefinitionIds((id, role) =>
        {
            roles.Add(role);
            return renames.TryGetValue(id, out var current) ? current : id;
        });

        Assert.Equal(new[] { "skill.sword" }, rewritten.Skills.Keys);
        Assert.Equal(new[] { "spell.ember.flare" }, rewritten.Known.Keys);
        Assert.Empty(rewritten.NoveltyFirsts);
        Assert.Equal(new[] { "creature.beast.grey_wolf" }, rewritten.Guards.SpeciesToday.Keys);
        Assert.Equal(new[] { "creature.beast.grey_wolf" }, rewritten.Guards.SpeciesEverKilled);
        Assert.Equal(Sample().Guards.ClusterKills.Keys, rewritten.Guards.ClusterKills.Keys);   // world keys, not definitions
        Assert.Contains("known technique", roles);
        Assert.Contains("kill record", roles);
    }

    [Fact]
    public void TheDefinitionPass_MergesTwoIdsThatBecomeOne()
    {
        var p = CharacterProgression.Empty with
        {
            Skills = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[]
            {
                KeyValuePair.Create("skill.axe", new SkillState(12, 3)),
                KeyValuePair.Create("skill.hatchet", new SkillState(20, 1)),
            }),
            Known = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[]
            {
                KeyValuePair.Create("ability.cleave", new KnownTechnique(LearningSource.Book, null, 50)),
                KeyValuePair.Create("ability.hew", new KnownTechnique(LearningSource.Teacher, "npc_x", 10)),
            }),
        };

        var merged = p.RewriteDefinitionIds((id, _) => id switch
        {
            "skill.hatchet" => "skill.axe",
            "ability.hew" => "ability.cleave",
            _ => id,
        });

        Assert.Equal(new SkillState(20, 1), Assert.Single(merged.Skills).Value);             // the higher competence
        Assert.Equal(new KnownTechnique(LearningSource.Teacher, "npc_x", 10), Assert.Single(merged.Known).Value);   // the earliest learning
    }
}
