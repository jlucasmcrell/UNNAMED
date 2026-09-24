using UNNAMED.Domain.Progression;
using static UNNAMED.Domain.Tests.Progression.TestRules;

namespace UNNAMED.Domain.Tests.Progression;

/// <summary>
/// ROADMAP M2c exit (d), PROGRESSION.md §2: every retained neighboring pair of axes can be driven apart by
/// legitimate play. Each test reaches all four cells of the pair's 2x2 through the sanctioned advance vectors
/// only, and shows that no vector moves the other axis as a side effect.
/// </summary>
public class IndependenceTests
{
    private static readonly ProgressionRules Rules = Default();

    // The sanctioned vectors, one per axis.
    private static CharacterProgression Explore(CharacterProgression p) =>                  // AX-LVL: discovery and quests
        ProgressionEngine.Award(
            ProgressionEngine.Award(p, new XpAward(XpSource.Discovery, 1_500, 0), Rules).Progression,
            new XpAward(XpSource.QuestObjective, 1_025, 0), Rules).Progression;

    private static CharacterProgression Train(CharacterProgression p, string skill) {        // AX-SKL: challenging practice
        for (int session = 0; session < 20; session++)
            p = ProgressionEngine.Practice(p, new SkillPractice(skill, ProgressionEngine.SkillLevel(p, skill) + 15, PracticeOutcome.Success, session), Rules).Progression;
        return p;
    }

    private static CharacterProgression Teach(CharacterProgression p, string technique) =>  // AX-TEC: a teacher
        ProgressionEngine.Learn(p, new TechniqueLearning(technique, LearningSource.Teacher, 0)).Progression;

    private static CharacterProgression Shape(CharacterProgression p) =>                    // AX-ATTR: spend what levels gave
        ProgressionEngine.Allocate(p, new AttributeAllocation(CharacterAttribute.Might, p.UnspentAttributePoints));

    private const int HighLevel = 5, HighSkill = 10;

    [Fact]
    public void LevelAndSkill_AreIndependent()
    {
        var none = CharacterProgression.Empty;
        var levelOnly = Explore(none);
        var skillOnly = Train(none, Blade);
        var both = Train(Explore(none), Blade);

        Assert.Equal((HighLevel, 0), (levelOnly.Level, ProgressionEngine.SkillLevel(levelOnly, Blade)));
        Assert.Equal(1, skillOnly.Level);
        Assert.Equal(0, skillOnly.LevelProgressXp);
        Assert.Empty(skillOnly.LifetimeXp);                       // practice awards no level XP at all
        Assert.True(ProgressionEngine.SkillLevel(skillOnly, Blade) >= HighSkill);
        Assert.Equal(HighLevel, both.Level);
        Assert.Equal(ProgressionEngine.SkillLevel(skillOnly, Blade), ProgressionEngine.SkillLevel(both, Blade));
        Assert.Equal(1, none.Level);
    }

    [Fact]
    public void SkillAndTechnique_AreIndependent()
    {
        var none = CharacterProgression.Empty;
        var skillOnly = Train(none, Blade);
        var techniqueOnly = Teach(none, "ability.martial.power_strike");
        var both = Teach(Train(none, Blade), "ability.martial.power_strike");

        Assert.Empty(skillOnly.Known);                             // practice never grants a technique
        Assert.True(ProgressionEngine.SkillLevel(skillOnly, Blade) >= HighSkill);
        Assert.Equal(0, ProgressionEngine.SkillLevel(techniqueOnly, Blade));   // knowing never grants skill
        Assert.True(ProgressionEngine.Knows(techniqueOnly, "ability.martial.power_strike"));
        Assert.True(ProgressionEngine.Knows(both, "ability.martial.power_strike"));
        Assert.Equal(ProgressionEngine.SkillLevel(skillOnly, Blade), ProgressionEngine.SkillLevel(both, Blade));
    }

    [Fact]
    public void LevelAndTechnique_AreIndependent()
    {
        var none = CharacterProgression.Empty;
        var levelOnly = Explore(none);
        var techniqueOnly = Teach(none, Bolt);
        var both = Teach(Explore(none), Bolt);

        Assert.Empty(levelOnly.Known);                             // levels never grant techniques
        Assert.Equal(HighLevel, levelOnly.Level);
        Assert.Equal((1, 0L), (techniqueOnly.Level, techniqueOnly.LevelProgressXp));
        Assert.True(ProgressionEngine.Knows(both, Bolt));
        Assert.Equal(HighLevel, both.Level);
    }

    [Fact]
    public void AttributesAndSkill_AreIndependent()
    {
        var none = CharacterProgression.Empty;
        var shapedOnly = Shape(Explore(none));
        var skillOnly = Train(none, Blade);
        var both = Train(Shape(Explore(none)), Blade);

        Assert.Equal(14, ProgressionEngine.AttributeValue(shapedOnly, CharacterAttribute.Might, Rules));
        Assert.Equal(0, ProgressionEngine.SkillLevel(shapedOnly, Blade));   // strength is not swordsmanship
        Assert.All(ProgressionKeys.Attributes, a => Assert.Equal(10, ProgressionEngine.AttributeValue(skillOnly, a, Rules)));
        Assert.True(ProgressionEngine.SkillLevel(skillOnly, Blade) >= HighSkill);   // a weak character can be a fine fencer
        Assert.Equal(14, ProgressionEngine.AttributeValue(both, CharacterAttribute.Might, Rules));
        Assert.Equal(ProgressionEngine.SkillLevel(skillOnly, Blade), ProgressionEngine.SkillLevel(both, Blade));
    }

    [Fact]
    public void Disciplines_AreIndependentOfEachOther()
    {
        var bladeOnly = Train(CharacterProgression.Empty, Blade);
        var athleticsOnly = Train(CharacterProgression.Empty, Athletics);
        var both = Train(Train(CharacterProgression.Empty, Blade), Athletics);

        Assert.Equal(0, ProgressionEngine.SkillLevel(bladeOnly, Athletics));
        Assert.Equal(0, ProgressionEngine.SkillLevel(athleticsOnly, Blade));
        Assert.Equal(ProgressionEngine.SkillLevel(bladeOnly, Blade), ProgressionEngine.SkillLevel(both, Blade));
        Assert.Equal(ProgressionEngine.SkillLevel(athleticsOnly, Athletics), ProgressionEngine.SkillLevel(both, Athletics));
    }
}
