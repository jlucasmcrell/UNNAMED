using System.Collections.Immutable;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Quests;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// Quests as objective graphs (M5, D-07): predicates over world state from a closed set, evaluated by rules that know no quest -
/// ordering, cascades, branches, joins, timers and failure all come from the definition.
/// </summary>
public class QuestRulesTests
{
    private const string Ore = "item.material.iron_ore";
    private const string Spear = "item.weapon.march_spear";

    private sealed class Facts : IQuestFacts
    {
        public HashSet<string> Lines { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Places { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, long> Distances { get; } = new(StringComparer.Ordinal);
        public List<(string ItemId, int Count, int Quality)> Pack { get; } = new();
        public Dictionary<(string, string), long> Flags { get; } = new();
        public Dictionary<(string, string), int> Regard { get; } = new();

        public bool Heard(string dialogueId, string nodeId) => Lines.Contains(dialogueId + "/" + nodeId);
        public bool Discovered(string locationId) => Places.Contains(locationId);
        public long DistanceMm(string locationId) => Distances.GetValueOrDefault(locationId, long.MaxValue);
        public int Carried(string itemId, int qualityMin) => Pack.Where(p => p.ItemId == itemId && p.Quality >= qualityMin).Sum(p => p.Count);
        public long WorldFlag(string flagId, string locationId) => Flags.GetValueOrDefault((flagId, locationId));
        public int Relationship(string npcId, string dimension) => Regard.GetValueOrDefault((npcId, dimension));
    }

    private static ObjectiveDefinition O(string id, ObjectiveCondition condition, params string[] next) =>
        new(id, id, condition, next.ToImmutableArray());

    private static QuestDefinition Quest(params ObjectiveDefinition[] objectives) =>
        new("quest.test.iron", "Iron", "Bring iron.", null, objectives[0].Id, objectives.ToImmutableArray(),
            ImmutableArray<ObjectiveCondition>.Empty, ImmutableArray<QuestReward>.Empty);

    private static readonly ObjectiveCondition Visit = new VisitLocation("location.shelf");
    private static readonly ObjectiveCondition HoldOre = new AcquireItem(Ore, 1, Quality.Crude);
    private static readonly ObjectiveCondition MakeSpear = new CraftItem(Spear, 1, Quality.Crude);

    private static ObjectiveStatus? StatusOf(QuestState state, string id) => state.Objective(id)?.Status;

    [Fact]
    public void AQuest_StartsAtItsEntry_AndWaitsWhileNothingHolds()
    {
        var quest = Quest(O("o_shelf", Visit, "o_ore"), O("o_ore", HoldOre));
        var state = QuestRules.Start(quest, 100);

        Assert.Equal((QuestStatus.Active, 100L), (state.Status, state.StartedTick));
        Assert.Equal(ObjectiveStatus.Active, StatusOf(state, "o_shelf"));
        Assert.Null(state.Objective("o_ore"));

        var step = QuestRules.Advance(quest, state, new Facts(), 101);
        Assert.Empty(step.Transitions);
        Assert.Equal(QuestStatus.Active, step.State.Status);
        Assert.Equal(state.Objectives.ToList(), step.State.Objectives.ToList());
        var term = Assert.Single(Assert.Single(step.Evaluations).Evaluation.Terms);
        Assert.Equal(("location.shelf discovered", "no", "yes", false), (term.Label, term.Value, term.Wanted, term.Holds));
    }

    [Fact]
    public void WhatTheWorldAlreadySatisfies_CompletesInOneTick_InOrder()
    {
        var quest = Quest(O("o_shelf", Visit, "o_ore"), O("o_ore", HoldOre, "o_home"), O("o_home", new ExploreLocation("location.home", 20_000)));
        var facts = new Facts();
        facts.Places.Add("location.shelf");
        facts.Pack.Add((Ore, 2, Quality.Standard));
        facts.Distances["location.home"] = 5_000;

        var step = QuestRules.Advance(quest, QuestRules.Start(quest, 0), facts, 7);

        Assert.Equal((QuestStatus.Completed, 7L, "o_home"), (step.State.Status, step.State.EndedTick!.Value, step.State.EndedBy));
        Assert.Equal(new[]
        {
            (TransitionKind.Satisfied, "o_shelf"), (TransitionKind.Activated, "o_ore"), (TransitionKind.Satisfied, "o_ore"),
            (TransitionKind.Activated, "o_home"), (TransitionKind.Satisfied, "o_home"), (TransitionKind.Completed, "o_home"),
        }, step.Transitions.Select(t => (t.Kind, t.ObjectiveId)));
        Assert.All(step.State.Objectives, o => Assert.Equal(ObjectiveStatus.Satisfied, o.Status));
    }

    [Fact]
    public void ASatisfiedObjective_StaysSatisfied_AndAFinishedQuestIsNeverEvaluatedAgain()
    {
        var quest = Quest(O("o_ore", HoldOre, "o_spear"), O("o_spear", MakeSpear));
        var facts = new Facts();
        facts.Pack.Add((Ore, 1, Quality.Standard));
        var state = QuestRules.Advance(quest, QuestRules.Start(quest, 0), facts, 1).State;
        Assert.Equal(ObjectiveStatus.Satisfied, StatusOf(state, "o_ore"));

        facts.Pack.Clear();   // the ore went into a billet: what was acquired stays acquired
        var step = QuestRules.Advance(quest, state, facts, 2);
        Assert.Empty(step.Transitions);
        Assert.Equal(ObjectiveStatus.Satisfied, StatusOf(step.State, "o_ore"));

        var done = QuestRules.Advance(quest, QuestRules.Record(quest, step.State, new Deed(DeedKind.Crafted, Spear, 1, 0, null, 3)), facts, 3).State;
        Assert.Equal(QuestStatus.Completed, done.Status);
        var again = QuestRules.Advance(quest, done, facts, 4);
        Assert.Empty(again.Transitions);
        Assert.Empty(again.Evaluations);
        Assert.Same(done, again.State);
    }

    [Fact]
    public void Deeds_CountOnlyWhileTheirObjectiveIsActive_AndOnlyTheDeedItWaitsFor()
    {
        var quest = Quest(O("o_ore", HoldOre, "o_spear"), O("o_spear", new CraftItem(Spear, 2, Quality.Standard)));
        var facts = new Facts();
        var state = QuestRules.Start(quest, 0);

        state = QuestRules.Record(quest, state, new Deed(DeedKind.Crafted, Spear, 1, Quality.Fine, null, 1));   // before o_spear is reached
        facts.Pack.Add((Ore, 1, Quality.Standard));
        state = QuestRules.Advance(quest, state, facts, 2).State;
        Assert.Equal(0, state.Objective("o_spear")!.Progress);

        state = QuestRules.Record(quest, state, new Deed(DeedKind.Crafted, Spear, 1, Quality.Crude, null, 3));        // not good enough
        state = QuestRules.Record(quest, state, new Deed(DeedKind.Crafted, "item.material.iron_ingot", 1, 0, null, 3));  // not a spear
        state = QuestRules.Record(quest, state, new Deed(DeedKind.Harvested, Spear, 1, 0, null, 3));                  // not made
        state = QuestRules.Record(quest, state, new Deed(DeedKind.Crafted, Spear, 1, Quality.Standard, null, 4));
        Assert.Equal(1, state.Objective("o_spear")!.Progress);
        Assert.Equal(QuestStatus.Active, QuestRules.Advance(quest, state, facts, 5).State.Status);

        state = QuestRules.Record(quest, state, new Deed(DeedKind.Crafted, Spear, 1, Quality.Fine, null, 6));
        Assert.Equal(QuestStatus.Completed, QuestRules.Advance(quest, state, facts, 7).State.Status);
    }

    [Fact]
    public void ABranch_TakesTheFirstAlternativeSatisfied_AndClosesTheRest()
    {
        var point = O("o_show", Visit, "o_fine", "o_plain") with { FirstBranch = true };
        var quest = Quest(point,
            O("o_fine", new AcquireItem(Spear, 1, Quality.Fine)),
            O("o_plain", new AcquireItem(Spear, 1, Quality.Crude)));
        var facts = new Facts();
        facts.Places.Add("location.shelf");
        facts.Pack.Add((Spear, 1, Quality.Fine));   // both alternatives hold: authored order decides

        var step = QuestRules.Advance(quest, QuestRules.Start(quest, 0), facts, 1);

        Assert.Equal((QuestStatus.Completed, "o_fine"), (step.State.Status, step.State.EndedBy));
        Assert.Equal(ObjectiveStatus.Closed, StatusOf(step.State, "o_plain"));
        Assert.Contains(step.Transitions, t => t.Kind == TransitionKind.BranchTaken && t.ObjectiveId == "o_show" && t.Detail == "o_fine");
        Assert.DoesNotContain(step.Transitions, t => t.Kind == TransitionKind.Satisfied && t.ObjectiveId == "o_plain");
    }

    [Fact]
    public void AJoin_WaitsForAllItsObjectives_InAnyOrder()
    {
        var quest = Quest(
            O("o_reach", Visit, "o_north", "o_south"),
            O("o_north", new WorldStateObjective("world.stone.north", "location.north", 1, 1), "o_all"),
            O("o_south", new WorldStateObjective("world.stone.south", "location.south", 1, 1), "o_all"),
            O("o_all", new WaitUntil(0)) with { AllOf = ImmutableArray.Create("o_north", "o_south") });
        var facts = new Facts();
        facts.Places.Add("location.shelf");
        facts.Flags[("world.stone.south", "location.south")] = 1;   // the second stone first

        var state = QuestRules.Advance(quest, QuestRules.Start(quest, 0), facts, 1).State;
        Assert.Equal(ObjectiveStatus.Satisfied, StatusOf(state, "o_south"));
        Assert.Null(state.Objective("o_all"));   // waits for the north stone

        facts.Flags[("world.stone.north", "location.north")] = 1;
        var step = QuestRules.Advance(quest, state, facts, 2);
        Assert.Equal((QuestStatus.Completed, "o_all"), (step.State.Status, step.State.EndedBy));
    }

    [Fact]
    public void ATimedObjective_FailsAtItsDeadline_UnlessItHolds_AndFailureGoesWhereItSays()
    {
        var chase = O("o_chase", new ExploreLocation("location.den", 5_000), "o_done") with { TimeLimitTicks = 40, OnFail = "o_retreat" };
        var quest = Quest(chase, O("o_retreat", new WaitUntil(10), "o_done"), O("o_done", Visit));
        var facts = new Facts();
        facts.Distances["location.den"] = 50_000;
        var state = QuestRules.Start(quest, 100);

        Assert.Empty(QuestRules.Advance(quest, state, facts, 139).Transitions);
        var step = QuestRules.Advance(quest, state, facts, 140);
        Assert.Equal(new[] { (TransitionKind.Failed, "o_chase"), (TransitionKind.Activated, "o_retreat") },
            step.Transitions.Select(t => (t.Kind, t.ObjectiveId)));
        Assert.Equal(QuestStatus.Active, step.State.Status);   // a setback, not an end

        // Holding on the deadline tick counts.
        facts.Distances["location.den"] = 4_000;
        var held = QuestRules.Advance(quest, state, facts, 140);
        Assert.Equal(ObjectiveStatus.Satisfied, StatusOf(held.State, "o_chase"));

        var fatal = Quest(chase with { OnFail = QuestRules.FailQuest, Next = ImmutableArray<string>.Empty });
        facts.Distances["location.den"] = 50_000;
        var failed = QuestRules.Advance(fatal, QuestRules.Start(fatal, 100), facts, 140).State;
        Assert.Equal((QuestStatus.Failed, "o_chase", 140L), (failed.Status, failed.EndedBy, failed.EndedTick!.Value));
        Assert.Equal(ObjectiveStatus.Failed, StatusOf(failed, "o_chase"));
    }

    [Fact]
    public void FailIf_EndsTheQuest_WhenItsPredicateHolds_AndClosesWhatWasActive()
    {
        var quest = Quest(O("o_ore", HoldOre)) with
        {
            FailIf = ImmutableArray.Create<ObjectiveCondition>(new RelationshipValueObjective("npc.test.smith", "trust", int.MinValue, -50)),
        };
        var facts = new Facts();
        var state = QuestRules.Start(quest, 0);
        Assert.Equal(QuestStatus.Active, QuestRules.Advance(quest, state, facts, 1).State.Status);

        facts.Regard[("npc.test.smith", "trust")] = -60;
        var failed = QuestRules.Advance(quest, state, facts, 2).State;
        Assert.Equal((QuestStatus.Failed, "fail_if[0]"), (failed.Status, failed.EndedBy));
        Assert.Equal(ObjectiveStatus.Closed, StatusOf(failed, "o_ore"));
    }

    [Fact]
    public void EveryTerm_ReportsItsValueAndWhatItWants()
    {
        var facts = new Facts();
        facts.Lines.Add("dialogue.test.smith/judged");
        facts.Pack.Add((Spear, 1, Quality.Standard));
        facts.Distances["location.home"] = 12_345;
        facts.Flags[("world.test.gate", "location.home")] = 2;
        facts.Regard[("npc.test.smith", "respect")] = 15;
        Term Only(ObjectiveCondition c, int progress = 0) => Assert.Single(QuestRules.Evaluate(c, facts, progress, 100, 130).Terms);

        var talk = QuestRules.Evaluate(new TalkTo("npc.test.smith", "dialogue.test.smith", ImmutableArray.Create("praised", "judged")), facts, 0, 0, 0);
        Assert.True(talk.Holds);
        Assert.Equal(new[] { ("heard 'praised' (dialogue.test.smith)", false), ("heard 'judged' (dialogue.test.smith)", true) },
            talk.Terms.Select(t => (t.Label, t.Holds)));
        Assert.Equal(("12.3 m", "<= 20.0 m", true), (Only(new ExploreLocation("location.home", 20_000)).Value,
            Only(new ExploreLocation("location.home", 20_000)).Wanted, Only(new ExploreLocation("location.home", 20_000)).Holds));
        Assert.Equal(("0", ">= 1", false), (Only(new AcquireItem(Spear, 1, Quality.Fine)).Value, Only(new AcquireItem(Spear, 1, Quality.Fine)).Wanted,
            Only(new AcquireItem(Spear, 1, Quality.Fine)).Holds));
        Assert.Equal("item.weapon.march_spear carried (quality >= 1)", Only(new AcquireItem(Spear, 1, Quality.Fine)).Label);
        Assert.Equal(("2", ">= 3", false), (Only(new KillCreature("creature.beast.wolf_grey", 3), 2).Value,
            Only(new KillCreature("creature.beast.wolf_grey", 3), 2).Wanted, Only(new KillCreature("creature.beast.wolf_grey", 3), 2).Holds));
        Assert.Equal(("2", "in [1, 3]", true), (Only(new WorldStateObjective("world.test.gate", "location.home", 1, 3)).Value,
            Only(new WorldStateObjective("world.test.gate", "location.home", 1, 3)).Wanted, Only(new WorldStateObjective("world.test.gate", "location.home", 1, 3)).Holds));
        Assert.Equal(("15", ">= 10", true), (Only(new RelationshipValueObjective("npc.test.smith", "respect", 10, int.MaxValue)).Value,
            Only(new RelationshipValueObjective("npc.test.smith", "respect", 10, int.MaxValue)).Wanted,
            Only(new RelationshipValueObjective("npc.test.smith", "respect", 10, int.MaxValue)).Holds));
        Assert.Equal(("30", ">= 40", false), (Only(new WaitUntil(40)).Value, Only(new WaitUntil(40)).Wanted, Only(new WaitUntil(40)).Holds));
    }
}
