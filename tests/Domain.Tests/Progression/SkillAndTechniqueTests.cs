using System.Collections.Immutable;
using UNNAMED.Domain.Progression;
using static UNNAMED.Domain.Tests.Progression.TestRules;

namespace UNNAMED.Domain.Tests.Progression;

/// <summary>AX-SKL use under challenge (PROGRESSION.md §4.2) and AX-TEC learning (§4.4).</summary>
public class SkillAndTechniqueTests
{
    private static readonly ProgressionRules Rules = Default();

    private static CharacterProgression WithSkill(string skill, int level) =>
        CharacterProgression.Empty with { Skills = CharacterProgression.Empty.Skills.Add(skill, new SkillState(level, 0)) };

    private static SkillPractice Use(string skill, int difficulty, PracticeOutcome outcome = PracticeOutcome.Success) =>
        new(skill, difficulty, outcome, 0);

    [Theory]
    [InlineData(0, -15, 0)]     // at the gate: trivial, worthless by construction
    [InlineData(0, 0, 10)]      // one margin past the gate
    [InlineData(0, 15, 20)]     // capped at twice that
    [InlineData(0, 90, 20)]
    [InlineData(20, 5, 0)]
    [InlineData(20, 12, 5)]     // (12 - 5) / 15 of a use, rounded
    public void Practice_GrantsXpOnlyPastTheDifficultyGate_ScaledByTheChallenge(int skill, int difficulty, long expected)
    {
        Assert.Equal(expected, ProgressionEngine.Practice(WithSkill(Blade, skill), Use(Blade, difficulty), Rules).XpGained);
    }

    [Fact]
    public void AFailure_StillTeaches_AtTheFailureRate()
    {
        Assert.Equal(5, ProgressionEngine.Practice(CharacterProgression.Empty, Use(Blade, 0, PracticeOutcome.Failure), Rules).XpGained);
    }

    [Fact]
    public void TheNoveltyBonus_ComesOnce_ForAFirstSuccess()
    {
        var first = ProgressionEngine.Practice(CharacterProgression.Empty, Use(Blade, 0) with { NoveltyKey = Salve }, Rules);
        // The first use raised the skill to 1, so one margin past the gate is now difficulty 1.
        var again = ProgressionEngine.Practice(first.Progression, Use(Blade, 1) with { NoveltyKey = Salve }, Rules);
        var failed = ProgressionEngine.Practice(CharacterProgression.Empty, Use(Blade, 0, PracticeOutcome.Failure) with { NoveltyKey = Salve }, Rules);

        Assert.Equal(35, first.XpGained);
        Assert.Equal(10, again.XpGained);
        Assert.Equal(5, failed.XpGained);
        Assert.Empty(failed.Progression.NoveltyFirsts);
    }

    [Fact]
    public void SkillLevels_FollowTheCurve()
    {
        var result = ProgressionEngine.Practice(CharacterProgression.Empty, Use(Blade, 0) with { NoveltyKey = Salve }, Rules);

        Assert.Equal(new SkillState(1, 15), result.Progression.Skills[Blade]);   // 35 XP: 20 to level 1, then 15 of 24
        Assert.Equal(1, result.LevelsGained);
        Assert.Equal(new Advancement(Axis.Skills, Blade, 35, "practice", 0), Assert.Single(result.Advancements));
    }

    [Fact]
    public void ASkill_StopsAtTheCommonCeiling()
    {
        var p = WithSkill(Blade, 59);
        for (int use = 0; use < 40; use++)
            p = ProgressionEngine.Practice(p, Use(Blade, 100), Rules).Progression;

        Assert.Equal(new SkillState(60, 0), p.Skills[Blade]);   // designated masteries (M12) are the only way past 60
    }

    [Fact]
    public void Practice_MovesOnlyItsOwnDiscipline()
    {
        var p = ProgressionEngine.Practice(WithSkill(Athletics, 7), Use(Blade, 15), Rules).Progression;

        Assert.Equal(new SkillState(7, 0), p.Skills[Athletics]);
        Assert.True(p.Skills[Blade].Level > 0);
    }

    [Theory]
    [InlineData("skill")]
    [InlineData("ability.martial.power_strike")]
    [InlineData("Skill.Blade")]
    public void Practice_RefusesAnythingButASkillId(string id)
    {
        Assert.Throws<ArgumentException>(() => ProgressionEngine.Practice(CharacterProgression.Empty, Use(id, 0), Rules));
    }

    [Fact]
    public void Learning_RecordsTheSource_AndLearningAgainChangesNothing()
    {
        var learned = ProgressionEngine.Learn(CharacterProgression.Empty,
            new TechniqueLearning(Bolt, LearningSource.Teacher, 1_200) { SourceRef = "npc_01HF7YAT0T0000000000000001" });
        var again = ProgressionEngine.Learn(learned.Progression, new TechniqueLearning(Bolt, LearningSource.Book, 9_999));

        Assert.True(learned.Learned);
        Assert.Equal(new KnownTechnique(LearningSource.Teacher, "npc_01HF7YAT0T0000000000000001", 1_200), learned.Progression.Known[Bolt]);
        Assert.Equal(new Advancement(Axis.Techniques, Bolt, 1, "teacher", 1_200), Assert.Single(learned.Advancements));
        Assert.False(again.Learned);
        Assert.Same(learned.Progression, again.Progression);
    }

    [Theory]
    [InlineData("skill.one_hand_blade")]
    [InlineData("item.weapon.iron_sword")]
    [InlineData("spell")]
    public void Learning_AcceptsOnlyTechniqueFormulaAndRecipeIds(string id)
    {
        Assert.Throws<ArgumentException>(() =>
            ProgressionEngine.Learn(CharacterProgression.Empty, new TechniqueLearning(id, LearningSource.Study, 0)));
    }

    [Fact]
    public void Learning_ChangesNothingButTheKnowledgeRecord()
    {
        var before = ProgressionEngine.Award(WithSkill(Athletics, 12), new XpAward(XpSource.Discovery, 120, 0), Rules).Progression;

        var after = ProgressionEngine.Learn(before, new TechniqueLearning(Salve, LearningSource.Book, 5)).Progression;

        Assert.Equal(before.Digest, (after with { Known = before.Known }).Digest);
        Assert.True(ProgressionEngine.Knows(after, Salve));
    }

    [Fact]
    public void ANewCharacter_StartsWithTheStartingPackage()
    {
        var package = new StartingPackage(
            ImmutableSortedDictionary.CreateRange(new[] { KeyValuePair.Create(CharacterAttribute.Might, 2) }),
            ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[] { KeyValuePair.Create(Athletics, 15) }),
            ImmutableArray.Create("ability.martial.weapon_focus"));
        var rules = Default(package: package);

        var p = ProgressionEngine.Create(rules);

        Assert.Equal(1, p.Level);
        Assert.Equal(12, ProgressionEngine.AttributeValue(p, CharacterAttribute.Might, rules));
        Assert.Equal(GrantSource.Creation, Assert.Single(p.Grants).Source);
        Assert.Equal(15, ProgressionEngine.SkillLevel(p, Athletics));
        Assert.Equal(LearningSource.StartingPackage, p.Known["ability.martial.weapon_focus"].Source);
        Assert.Equal(CharacterProgression.Empty.Digest, ProgressionEngine.Create(Rules).Digest);   // Phase 1's package is empty
    }
}
