// UNNAMED Application tests - the ring's quest GRAPH, not just its flags (review B3)
// The satisfiability suite proves each step has a writer and the chain is connected. It does not prove the graph
// means what it says: the choice did not exclude its alternative, the close demanded BOTH endings, the optional
// report was the only terminal, and the recipe the craft step needs was taught by nobody. Every one of those left
// the quest uncompletable with the whole suite green.

using System.Collections.Immutable;
using UNNAMED.Application;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using Xunit;

namespace UNNAMED.Application.Tests;

public class BarrowworksQuestGraphTests
{
    private const string Quest = "quest.barrowworks.unfinished_measure";
    private const string Region = "region.barrowworks";
    private const string Kera = "dialogue.ashen_hollow.kera_voss";
    private const string Wedge = "recipe.smithing.fitting_wedge";
    private const string Preserved = "world.barrowworks.headworks_preserved";
    private const string Dismantled = "world.barrowworks.headworks_dismantled";

    private static GameSession Boot(TempProfile profile) =>
        GameSession.Boot(new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root)
        {
            LockProfile = true,
            RegionId = Region,
            Barrowworks = true,
        });

    /// <summary>An objective's flag, when it has one, so the two endings can be named by their conditions.</summary>
    private static string? FlagOf(ObjectiveDefinition objective) =>
        objective.Condition is WorldStateObjective w ? w.FlagId : null;

    /// <summary>
    /// The choice is a choice. Without <c>branch: first</c> neither alternative closes the other, so a character
    /// could answer both ways and the join below would be waiting on a pair of contradictory endings.
    /// </summary>
    [Fact]
    public void TheChoice_ExcludesItsAlternative()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var quest = session.Setup.Quests.Quests[Quest];

        var point = quest.Order.Single(o => o.Next.Contains("o_preserve") && o.Next.Contains("o_dismantle"));
        Assert.True(point.FirstBranch, $"{point.Id} offers both endings but does not exclude one");
    }

    /// <summary>
    /// No step may require both endings. This is the shape the authored graph had: <c>o_done</c> joined
    /// <c>o_preserve</c> and <c>o_dismantle</c> with <c>all_of</c>, so the quest completed only if the player both
    /// left the measure standing and broke it up. And with <c>branch: first</c> the join could never hold at all,
    /// because a losing branch is CLOSED rather than Satisfied (<c>Quests.cs</c> L521).
    /// </summary>
    [Fact]
    public void NoObjective_RequiresBothEndings()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var quest = session.Setup.Quests.Quests[Quest];

        var endings = quest.Order.Where(o => FlagOf(o) is Preserved or Dismantled).Select(o => o.Id).ToImmutableHashSet();
        Assert.Equal(2, endings.Count);

        foreach (var objective in quest.Order)
        {
            int required = objective.AllOf.Count(endings.Contains);
            Assert.True(required <= 1,
                $"{objective.Id} waits on {required} endings at once ({string.Join(", ", objective.AllOf)}), so answering the choice one way can never satisfy it");
        }
    }

    /// <summary>
    /// Telling Kera ends the quest. The close used to hand off to the blind-store report, which made a step the
    /// yaml's own comment calls optional the only way to finish.
    /// </summary>
    [Fact]
    public void TellingKera_EndsTheQuest()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var quest = session.Setup.Quests.Quests[Quest];

        var close = quest.Objectives["o_done"];
        Assert.True(close.Terminal, "o_done does not end the quest, so something else is required after it");
    }

    /// <summary>
    /// And nothing terminal waits on the report, so a player who never finds the Blind Store is never blocked by
    /// it - which is what the yaml says it is for.
    /// </summary>
    [Fact]
    public void TheBlindStoreReport_GatesNothing()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var quest = session.Setup.Quests.Quests[Quest];

        Assert.DoesNotContain(quest.Objectives.Values, o => o.AllOf.Contains("o_report"));
        Assert.DoesNotContain(quest.Objectives.Values, o => o.Next.Contains("o_report") && o.Terminal);
        Assert.Contains("o_report", quest.Objectives.Keys);
    }

    /// <summary>
    /// The craft step is performable: crafting refuses a recipe the character does not know, so something has to
    /// teach this one or <c>o_wedge</c> is a step nobody can pass.
    /// </summary>
    [Fact]
    public void TheFittingWedgeRecipe_IsTaught()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);

        var taught = session.Setup.Social.Dialogues.Values
            .SelectMany(d => d.Nodes.Values)
            .SelectMany(n => n.Choices)
            .SelectMany(c => c.Consequences)
            .OfType<GiveRecipeConsequence>()
            .Any(c => c.RecipeId == Wedge);

        Assert.True(taught, $"{Wedge} is asked for by o_wedge and taught by no reply");
        Assert.True(session.Setup.Social.Dialogues.ContainsKey(Kera));
    }
}
