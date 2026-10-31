// UNNAMED Content - D23 S6: a quest that pays for a stealth crossing

using UNNAMED.Content;
using UNNAMED.Domain.Quests;
using Xunit;

namespace UNNAMED.Content.Tests;

/// <summary>
/// S6 of D23 (canon/STEALTH.md 9.3): the non-lethal way is rewarded with a mechanic, not a line of dialogue. The Quiet Crossing asks for a ghost
/// crossing by the same objective machinery every other quest uses, which means the reward is paid when the world records the deed.
/// </summary>
public class StealthQuestTests
{
    private static ContentLoader Content()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        return loader;
    }

    [Fact]
    public void TheQuietCrossing_AsksForAGhostCrossing_AndPaysForIt()
    {
        var quests = QuestContent.BuildQuests(Content());

        var quest = quests["quest.ashen_hollow.quiet_crossing"];
        var objective = Assert.IsType<DeedObjective>(quest.Objectives.Values.Single().Condition);
        Assert.Equal(DeedKind.Ghosted, objective.Kind);
        Assert.Equal(1, objective.Count);
        Assert.True(quest.OnNewGame, "a way to travel has to be in the journal from the start");
        Assert.NotEmpty(quest.Rewards);
    }

    [Fact]
    public void ADeedObjective_IsABuiltType_SoAContentAuthorCanNameIt()
    {
        // The objective type is in the closed set DATA_MODEL.md 4.11 is checked against, or content cannot use it at all.
        Assert.Contains("deed", ObjectiveTypes.Built);
        Assert.DoesNotContain("deed", ObjectiveTypes.NotBuilt.Keys);
    }
}
