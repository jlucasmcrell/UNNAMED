// UNNAMED Content tests - B-036: a dialogue line may not be offered at a quest stage it contradicts.

using System.Collections.Immutable;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using Xunit;

namespace UNNAMED.Content.Tests;

/// <summary>
/// B-036: a line's conditions and consequences live in the dialogue graph, so the pack lint could only prove that a `quest.stage` a line names
/// exists - never that the line can be said at it. This walks every reply in the game with a quest condition and proves three things: the stage
/// it asks for is reachable and not one the quest has already left behind, a reply never starts a quest it also says is already past, and a reply
/// never hands over a thing its own conditions say the character must already hold. It is the check `tools/first_hour_audit.py` pointed at but
/// could not make: that tool resolves references, this one walks the stage graph.
///
/// The rules are in `Problems`/`Reachability`, taking the quests and dialogues as arguments, so a planted contradiction proves the guard has teeth
/// rather than passing because the pack happens to be clean.
/// </summary>
public class DialogueStageTests
{
    private static ContentLoader Game()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        return loader;
    }

    /// <summary>Every objective a quest can actually reach: the entry, then whatever its objectives' <c>next</c> names.</summary>
    private static ImmutableHashSet<string> Reachable(QuestDefinition quest)
    {
        var reached = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var frontier = new Stack<string>();
        frontier.Push(quest.Entry);
        while (frontier.Count > 0)
        {
            string id = frontier.Pop();
            if (!reached.Add(id) || !quest.Objectives.TryGetValue(id, out var objective))
                continue;
            foreach (string next in objective.Next)
                frontier.Push(next);
        }
        return reached.ToImmutable();
    }

    private static IEnumerable<(string Where, DialogueChoice Choice)> Replies(DialogueDefinition dialogue)
    {
        foreach (var (nodeId, node) in dialogue.Nodes)
            foreach (var choice in node.Choices)
                yield return ($"{dialogue.Id}:{nodeId}:{choice.Id}", choice);
    }

    private static IEnumerable<QuestStateCondition> Quests(DialogueChoice choice) =>
        choice.Conditions.OfType<QuestStateCondition>();

    /// <summary>A named stage that could never be reached, so the line carrying it could never be offered.</summary>
    private static List<string> Reachability(
        ImmutableSortedDictionary<string, QuestDefinition> quests,
        ImmutableSortedDictionary<string, DialogueDefinition> dialogues)
    {
        var problems = new List<string>();
        var reachable = quests.ToImmutableDictionary(q => q.Key, q => Reachable(q.Value), StringComparer.Ordinal);
        foreach (var dialogue in dialogues.Values)
        {
            foreach (var (where, choice) in Replies(dialogue))
            {
                foreach (var condition in Quests(choice))
                {
                    if (!quests.TryGetValue(condition.QuestId, out var quest))
                    {
                        problems.Add($"{where}: names {condition.QuestId}, which is not a quest");
                        continue;
                    }
                    if (condition.ObjectiveId is not { } objective)
                        continue;
                    if (!quest.Objectives.ContainsKey(objective))
                    {
                        problems.Add($"{where}: names {condition.QuestId}/{objective}, which that quest does not have");
                        continue;
                    }
                    // A negated condition asks for the stage to be absent, so reachability says nothing about it.
                    if (!condition.Negated && condition.State is "active" or "satisfied"
                        && !reachable[condition.QuestId].Contains(objective))
                    {
                        problems.Add(
                            $"{where}: asks for {condition.QuestId}/{objective} {condition.State}, but no chain from " +
                            $"{quest.Entry} reaches that objective - the line can never be offered");
                    }
                }
            }
        }
        return problems;
    }

    /// <summary>A line that starts a quest it says is already past, or hands over what it says is already held.</summary>
    private static List<string> Problems(ImmutableSortedDictionary<string, DialogueDefinition> dialogues)
    {
        var problems = new List<string>();
        foreach (var dialogue in dialogues.Values)
        {
            foreach (var (where, choice) in Replies(dialogue))
            {
                foreach (var start in choice.Consequences.OfType<StartQuestConsequence>())
                {
                    foreach (var condition in Quests(choice).Where(c => c.QuestId == start.QuestId && !c.Negated))
                    {
                        if (condition.State is "active" or "satisfied" or "completed" or "failed")
                            problems.Add($"{where}: starts {start.QuestId} while requiring it to be '{condition.State}' already");
                    }
                }
                foreach (var giving in choice.Consequences.OfType<TransferItemConsequence>().Where(c => c.ToPlayer))
                {
                    foreach (var held in choice.Conditions.OfType<HasItemCondition>()
                                 .Where(c => c.ItemId == giving.ItemId && !c.Negated))
                    {
                        // "I have lost my tool" is right; the same line said by someone who must be carrying it is B-036's own example.
                        problems.Add($"{where}: gives {giving.ItemId} while requiring the character to be carrying it");
                    }
                }
            }
        }
        return problems;
    }

    [Fact]
    public void EveryStageAChoiceAsksFor_IsReachable_InTheShippedPack()
    {
        var loader = Game();
        var quests = QuestContent.BuildQuests(loader);
        var dialogues = SocialContent.BuildDialogues(loader);

        int walked = dialogues.Values
            .SelectMany(Replies)
            .SelectMany(r => r.Choice.Conditions)
            .OfType<QuestStateCondition>()
            .Count();
        Assert.True(walked > 50, $"only {walked} quest conditions were walked");

        var problems = Reachability(quests, dialogues);
        Assert.True(problems.Count == 0, "dialogue lines name a stage they cannot be said at:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void NoShippedLine_StartsAQuestItSaysIsPast_OrHandsOverWhatItSaysIsHeld()
    {
        var loader = Game();
        var problems = Problems(SocialContent.BuildDialogues(loader));
        Assert.True(problems.Count == 0, "a line contradicts its own conditions:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void TheCheckHasTeeth_APlantedContradictionIsCaught()
    {
        var condition = new QuestStateCondition("quest.test.plot", "o_late", "active", Negated: false);
        var awkward = new DialogueChoice("awkward", "Say the impossible thing",
            ImmutableArray.Create<DialogueCondition>(condition), ImmutableArray<DialogueConsequence>.Empty, null);

        var held = new HasItemCondition("item.heritage.finishing_tool", 1, 0, Negated: false);
        var givesAgain = new DialogueChoice("again", "Here it is again",
            ImmutableArray.Create<DialogueCondition>(held),
            ImmutableArray.Create<DialogueConsequence>(
                new TransferItemConsequence("item.heritage.finishing_tool", 1, ToPlayer: true)), null);

        var startsPast = new DialogueChoice("starts", "Begin what has begun",
            ImmutableArray.Create<DialogueCondition>(
                new QuestStateCondition("quest.test.plot", null, "completed", Negated: false)),
            ImmutableArray.Create<DialogueConsequence>(new StartQuestConsequence("quest.test.plot")), null);

        var node = new DialogueNode("greet", "A line.", ImmutableArray.Create(awkward, givesAgain, startsPast), null, false, null);
        var dialogue = new DialogueDefinition("dialogue.test.plot",
            ImmutableArray.Create("npc.test.plot"), "greet",
            ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[] { new KeyValuePair<string, DialogueNode>("greet", node) }));

        var objective = new ObjectiveDefinition("o_early", "Do the first thing.", Condition(), ImmutableArray.Create("o_last"));
        var last = new ObjectiveDefinition("o_last", "Do the last thing.", Condition(), ImmutableArray<string>.Empty);
        var quest = new QuestDefinition("quest.test.plot", "Plot", "Summary", "npc.test.plot", "o_early",
            ImmutableArray.Create(objective, last), ImmutableArray<ObjectiveCondition>.Empty, ImmutableArray<QuestReward>.Empty);

        var quests = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,
            new[] { new KeyValuePair<string, QuestDefinition>("quest.test.plot", quest) });
        var dialogues = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,
            new[] { new KeyValuePair<string, DialogueDefinition>("dialogue.test.plot", dialogue) });

        // the impossible stage, the item handed over while held, and the quest started after it is done: all three are caught
        Assert.Single(Reachability(quests, dialogues));
        Assert.Equal(2, Problems(dialogues).Count);

        static ObjectiveCondition Condition() => new VisitLocation("location.test.plot");
    }
}
