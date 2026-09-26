using System.Collections.Immutable;
using UNNAMED.Domain.Social;

namespace UNNAMED.Domain.Tests;

/// <summary>Conversations as data (M4): conditions over world state decide what is offered, and a spent line passes on.</summary>
public class DialogueRulesTests
{
    private const string Talk = "dialogue.test.smith";
    private const string Other = "dialogue.test.guide";

    private sealed class Facts : IDialogueFacts
    {
        public HashSet<string> Heard { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, long> Flags { get; } = new(StringComparer.Ordinal);
        public List<(string ItemId, int Count, int Quality)> Pack { get; } = new();
        public Dictionary<(string, string), int> Regard { get; } = new();
        public Dictionary<string, int> Skills { get; } = new(StringComparer.Ordinal);
        public Dictionary<(string, string?), string> Quests { get; } = new();

        public HashSet<string> HeardElsewhere { get; } = new(StringComparer.Ordinal);

        public bool Visited(string dialogueId, string nodeId) =>
            dialogueId == Talk ? Heard.Contains(nodeId) : dialogueId == Other && HeardElsewhere.Contains(nodeId);
        public long WorldFlag(string flagId, string? locationId) => Flags.GetValueOrDefault(flagId);
        public int Carried(string itemId, int qualityMin) => Pack.Where(p => p.ItemId == itemId && p.Quality >= qualityMin).Sum(p => p.Count);
        public int Relationship(string npcId, string dimension) => Regard.GetValueOrDefault((npcId, dimension));
        public int SkillLevel(string skillId) => Skills.GetValueOrDefault(skillId);
        public int Level { get; set; } = 1;
        public string QuestState(string questId, string? objectiveId) =>
            Quests.TryGetValue((questId, objectiveId), out var state) ? state : objectiveId is null ? "not_started" : "not_reached";
        public Dictionary<string, UNNAMED.Domain.Companions.CompanionOrder> Companions { get; } = new(StringComparer.Ordinal);
        public UNNAMED.Domain.Companions.CompanionOrder? CompanionOrderOf(string npcId) => Companions.TryGetValue(npcId, out var order) ? order : null;
        public Dictionary<string, int> Standing { get; } = new(StringComparer.Ordinal);
        public HashSet<(string, string)> Acts { get; } = new();
        int IDialogueFacts.StandingLevel(string factionId) => Standing.GetValueOrDefault(factionId);
        bool IDialogueFacts.ActDone(string kind, string subject) => Acts.Contains((kind, subject));
    }

    private static DialogueNode Node(string id, bool once = false, string? exhausted = null) =>
        new(id, id, ImmutableArray<DialogueChoice>.Empty, null, once, exhausted);

    private static DialogueDefinition Graph(params DialogueNode[] nodes) =>
        new(Talk, ImmutableArray.Create("npc.test.smith"), nodes[0].Id, nodes.ToImmutableSortedDictionary(n => n.Id, n => n, StringComparer.Ordinal));

    [Fact]
    public void EachConditionKind_ReadsTheWorld()
    {
        var facts = new Facts { Level = 3 };
        facts.Heard.Add("lesson");
        facts.Flags["world.test.gate"] = 2;
        facts.Pack.Add(("item.weapon.spear", 1, -1));
        facts.Pack.Add(("item.weapon.spear", 1, 1));
        facts.Regard[("npc.test.smith", "trust")] = 10;
        facts.Skills["skill.smithing"] = 4;
        bool Holds(DialogueCondition c) => DialogueRules.Holds(c, Talk, facts);

        Assert.True(Holds(new VisitedCondition("lesson", false)));
        Assert.False(Holds(new VisitedCondition("lesson", true)));
        Assert.True(Holds(new VisitedCondition("praise", true)));
        Assert.True(Holds(new WorldStateCondition("world.test.gate", 1, long.MaxValue)));
        Assert.False(Holds(new WorldStateCondition("world.test.gate", 3, long.MaxValue)));
        Assert.False(Holds(new WorldStateCondition("world.test.other", 1, long.MaxValue)));   // an unset flag is 0
        Assert.True(Holds(new HasItemCondition("item.weapon.spear", 2, -1, false)));
        Assert.True(Holds(new HasItemCondition("item.weapon.spear", 1, 1, false)));            // one fine one
        Assert.False(Holds(new HasItemCondition("item.weapon.spear", 2, 1, false)));
        Assert.True(Holds(new HasItemCondition("item.weapon.spear", 2, 1, true)));             // not two fine ones
        Assert.True(Holds(new RelationshipCondition("npc.test.smith", "trust", 5, 100)));
        Assert.False(Holds(new RelationshipCondition("npc.test.smith", "trust", -100, 9)));
        Assert.True(Holds(new RelationshipCondition("npc.test.smith", "fear", -100, 0)));      // never moved is 0
        Assert.True(Holds(new SkillCondition("skill.smithing", 4)));
        Assert.False(Holds(new SkillCondition("skill.smithing", 5)));
        Assert.True(Holds(new LevelCondition(3)));
        Assert.False(Holds(new LevelCondition(4)));

        facts.Quests[("quest.test.iron", null)] = "active";
        facts.Quests[("quest.test.iron", "o_ore")] = "satisfied";
        Assert.True(Holds(new QuestStateCondition("quest.test.iron", null, "active", false)));
        Assert.False(Holds(new QuestStateCondition("quest.test.iron", null, "completed", false)));
        Assert.True(Holds(new QuestStateCondition("quest.test.iron", null, "completed", true)));
        Assert.True(Holds(new QuestStateCondition("quest.test.iron", "o_ore", "satisfied", false)));
        Assert.True(Holds(new QuestStateCondition("quest.test.iron", "o_show", "not_reached", false)));
        Assert.True(Holds(new QuestStateCondition("quest.test.other", null, "not_started", false)));

        // A line of another conversation (M6): what was said to someone else, asked of this speaker.
        facts.HeardElsewhere.Add("greet");
        Assert.True(Holds(new VisitedCondition("greet", false) { DialogueId = Other }));
        Assert.False(Holds(new VisitedCondition("greet", false)));   // not a line of this conversation
        Assert.True(Holds(new VisitedCondition("lesson", true) { DialogueId = Other }));
    }

    [Fact]
    public void AReply_IsOfferedOnlyWhileAllItsConditionsHold()
    {
        var facts = new Facts();
        var show = new DialogueChoice("show", "Look.", ImmutableArray.Create<DialogueCondition>(
                new HasItemCondition("item.weapon.spear", 1, -1, false), new VisitedCondition("judged", true)),
            ImmutableArray<DialogueConsequence>.Empty, "judged");

        Assert.False(DialogueRules.Offered(show, Talk, facts));
        facts.Pack.Add(("item.weapon.spear", 1, 0));
        Assert.True(DialogueRules.Offered(show, Talk, facts));
        facts.Heard.Add("judged");
        Assert.False(DialogueRules.Offered(show, Talk, facts));
    }

    [Fact]
    public void ASpentLine_PassesOnToWhereItLeads_OrEndsTheConversation()
    {
        var graph = Graph(Node("greet", once: true, exhausted: "again"), Node("again"), Node("farewell", once: true));
        var heard = new HashSet<string>(StringComparer.Ordinal);

        Assert.Equal("greet", DialogueRules.Arrive(graph, "greet", heard.Contains)!.Id);
        heard.Add("greet");
        Assert.Equal("again", DialogueRules.Arrive(graph, "greet", heard.Contains)!.Id);
        Assert.Equal("farewell", DialogueRules.Arrive(graph, "farewell", heard.Contains)!.Id);
        heard.Add("farewell");
        Assert.Null(DialogueRules.Arrive(graph, "farewell", heard.Contains));   // spent, and nowhere to pass on to
    }

    [Fact]
    public void ARelationship_IsHeldBetweenMinusAndPlusAHundred()
    {
        Assert.Equal(100, Relationships.Clamp(95 + 10));
        Assert.Equal(-100, Relationships.Clamp(-250));
        Assert.Equal(7, Relationships.Clamp(7));
        Assert.True(Relationships.IsDimension("trust"));
        Assert.False(Relationships.IsDimension("love"));   // no single meter: named dimensions only
    }
}
