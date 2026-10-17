using System.Collections.Immutable;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Tests;

/// <summary>The Learn column (WP9, C10): each lesson's fee and its requirements as met and unmet chips, in the Skills page's reason grammar.</summary>
public class LearnModelTests
{
    private static string Name(string id) => id switch
    {
        "skill.smithing" => "Smithing",
        "ability.blade_feint" => "Feint",
        "recipe.iron_nails" => "Iron nails",
        "recipe.hinge" => "Hinge",
        "quest.lost_hammer" => "The Lost Hammer",
        _ => Names.Words(id),
    };

    private static RequirementCheck Skill(bool met, long have) => new(new SkillBandRequirement("skill.smithing", "Journeyman", 4), met, have);

    private static LessonView Lesson(string id, long fee, bool known = false, bool byDialogue = false, params RequirementCheck[] requirements) =>
        new(id.StartsWith("ability.") ? TeachingKind.Technique : TeachingKind.Recipe, id, fee, known, byDialogue, requirements.ToImmutableArray());

    [Fact]
    public void ALessonWithEveryRequirementMet_AndTheFeeCarried_IsReady()
    {
        var row = LearnModel.Row(Lesson("recipe.iron_nails", 12, requirements: Skill(true, 5)), null, "Kera Voss", 20, Name);
        Assert.Equal(LearnState.Ready, row.State);
        Assert.True(row.CanLearn);
        Assert.Equal("12 coin", row.Fee);
        Assert.Equal("Recipe", row.Kind);
        Assert.Null(row.Why);
        Assert.Equal(new LearnChip("Smithing: journeyman · you have 5", true), Assert.Single(row.Chips));
    }

    [Fact]
    public void AnUnmetRequirement_Blocks_AndIsTheReasonGiven()
    {
        var row = LearnModel.Row(Lesson("ability.blade_feint", 0, requirements: Skill(false, 2)), null, "Kera Voss", 20, Name);
        Assert.Equal(LearnState.Blocked, row.State);
        Assert.False(row.CanLearn);
        Assert.Equal("Not yet: smithing: journeyman · you have 2.", row.Why);
        Assert.Equal("No fee", row.Fee);
        Assert.Equal("Technique", row.Kind);
        Assert.False(Assert.Single(row.Chips).Met);
    }

    [Fact]
    public void AFeeNotCarried_Blocks_WithWhatIsAskedAndWhatIsHeld()
    {
        var row = LearnModel.Row(Lesson("recipe.iron_nails", 12), null, "Kera Voss", 5, Name);
        Assert.Equal(LearnState.Blocked, row.State);
        Assert.Equal("Not yet: the fee is 12 coin and you have 5.", row.Why);
    }

    [Fact]
    public void ATeacherOutOfHours_TeachesNothing_ButEveryLessonIsListed()
    {
        var view = new TeachingView("npc.kera", "Kera Voss teaches at their work, 08:00-18:00",
            ImmutableArray.Create(Lesson("recipe.iron_nails", 0), Lesson("recipe.hinge", 0, known: true)));
        var column = LearnModel.Column(view, "Kera Voss", 0, Name);
        Assert.False(column.Teaching);
        Assert.Equal("Kera Voss teaches at their work, 08:00-18:00.", column.Unavailable);
        Assert.Equal(new[] { LearnState.Blocked, LearnState.Known }, column.Rows.Select(r => r.State));
        Assert.Equal("Kera Voss teaches at their work, 08:00-18:00.", column.Rows[0].Why);
    }

    [Fact]
    public void ALessonWaitingOnTheWorld_IsNotListed_UntilTheWorldIsSo()
    {
        var flag = new FlagRequirement(new WorldStateCondition("world.ring_accessible", 1, long.MaxValue));
        var waiting = new TeachingView("npc.kera", null, ImmutableArray.Create(Lesson("recipe.iron_nails", 0),
            Lesson("recipe.hinge", 0, byDialogue: true, requirements: new RequirementCheck(flag, false, 0))));
        Assert.Equal(new[] { "recipe.iron_nails" }, LearnModel.Column(waiting, "Kera Voss", 0, Name).Rows.Select(r => r.DefinitionId));
        var so = waiting with { Lessons = waiting.Lessons.SetItem(1, waiting.Lessons[1] with { Requirements = ImmutableArray.Create(new RequirementCheck(flag, true, 1)) }) };
        Assert.Equal(2, LearnModel.Column(so, "Kera Voss", 0, Name).Rows.Length);
    }

    [Fact]
    public void TheColumn_ListsReadyFirst_ThenWhatTheConversationGives_ThenBlocked_ThenKnown()
    {
        var view = new TeachingView("npc.kera", null, ImmutableArray.Create(
            Lesson("recipe.hinge", 0, known: true),
            Lesson("recipe.a_blocked", 50),
            Lesson("recipe.c_by_talk", 0, byDialogue: true),
            Lesson("recipe.iron_nails", 0)));
        var column = LearnModel.Column(view, "Kera Voss", 10, Name);
        Assert.Equal(new[] { LearnState.Ready, LearnState.InConversation, LearnState.Blocked, LearnState.Known }, column.Rows.Select(r => r.State));
        Assert.Equal("Ask Kera Voss for it in conversation.", column.Rows[1].Why);
    }

    [Fact]
    public void Trust_IsWordsNeverANumber_AndOtherRequirementsAreSaidPlainly()
    {
        Assert.Equal("Kera Voss trusts you enough", LearnModel.Chip(new RequirementCheck(new TrustRequirement(20), true, 25), "Kera Voss", Name));
        Assert.Equal("Kera Voss must trust you more", LearnModel.Chip(new RequirementCheck(new TrustRequirement(20), false, 5), "Kera Voss", Name));
        Assert.Equal("Learn Iron nails first", LearnModel.Chip(new RequirementCheck(new PrerequisiteRequirement("recipe.iron_nails"), false, 0), "Kera", Name));
        Assert.Equal("Knows Iron nails", LearnModel.Chip(new RequirementCheck(new PrerequisiteRequirement("recipe.iron_nails"), true, 1), "Kera", Name));
        Assert.Equal("Kill grey wolf 3 times · you have 1",
            LearnModel.Chip(new RequirementCheck(new DeedRequirement(new DeedCondition(DeedKind.Killed, "creature.beast.grey_wolf", 3, false)), false, 1), "Kera", Name));
        Assert.Equal("The Lost Hammer: done",
            LearnModel.Chip(new RequirementCheck(new QuestRequirement(new QuestStateCondition("quest.lost_hammer", null, "completed", false)), true, 1), "Kera", Name));
    }

    [Fact]
    public void AKnownLesson_StaysKnown_WhateverTheRequirementsNow() =>
        Assert.Equal(LearnState.Known, LearnModel.Row(Lesson("recipe.hinge", 99, known: true, requirements: Skill(false, 0)), "away", "Kera", 0, Name).State);
}
