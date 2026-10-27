using System.Collections.Immutable;
using UNNAMED.Domain.Guidance;

namespace UNNAMED.Domain.Tests;

/// <summary>WP10's guidance (§2.10 O1): which hint a moment shows - each once, Full shows every one, Light the first of each category, Off none.</summary>
public class HintRulesTests
{
    private static readonly HintDefinition[] Hints =
    {
        new("hint.fighting", "combat", HintTriggers.CreatureEngaged, "{key:attack} strikes.", 0),
        new("hint.guarding", "combat", HintTriggers.CreatureEngaged, "Hold {key:guard} to guard.", 1),
        new("hint.dodging", "combat", HintTriggers.HitTaken, "{key:dodge} dashes clear.", 0),
        new("hint.journal", "quests", HintTriggers.QuestStarted, "{key:journal} opens the journal.", 0),
        new("hint.kal_wings", "movement", HintTriggers.NewGame, "Sprint and jump to throw your wings open.", 0) { People = new[] { "people.kal" }.ToImmutableArray() },
    };

    private static HintDefinition? Pick(string trigger, HintDepth depth, params string[] seen) =>
        HintRules.Pick(Hints, trigger, seen.ToHashSet(StringComparer.Ordinal), depth);

    private static HintDefinition? PickFor(string people, string trigger, HintDepth depth, params string[] seen) =>
        HintRules.Pick(Hints, trigger, seen.ToHashSet(StringComparer.Ordinal), depth, people);

    [Fact]
    public void APeoplesHint_ShowsOnlyToThatPeople_AndEveryonesShowsToAll()
    {
        // The Kal wing hint (note 3) is theirs alone; a Veth is told nothing about wings it has not got.
        Assert.Equal("hint.kal_wings", PickFor("people.kal", HintTriggers.NewGame, HintDepth.Every)?.Id);
        Assert.Null(PickFor("people.veth", HintTriggers.NewGame, HintDepth.Every));
        Assert.Null(PickFor(null, HintTriggers.NewGame, HintDepth.Every));
        // And a hint with no people listed is everyone's, however the character is made.
        Assert.Equal("hint.fighting", PickFor("people.veth", HintTriggers.CreatureEngaged, HintDepth.Every)?.Id);
        Assert.Equal("hint.fighting", PickFor(null, HintTriggers.CreatureEngaged, HintDepth.Every)?.Id);
    }

    [Fact]
    public void Full_ShowsEachHintOnce_InOrder()
    {
        Assert.Equal("hint.fighting", Pick(HintTriggers.CreatureEngaged, HintDepth.Every)?.Id);
        Assert.Equal("hint.guarding", Pick(HintTriggers.CreatureEngaged, HintDepth.Every, "hint.fighting")?.Id);
        Assert.Null(Pick(HintTriggers.CreatureEngaged, HintDepth.Every, "hint.fighting", "hint.guarding"));
        Assert.Equal("hint.dodging", Pick(HintTriggers.HitTaken, HintDepth.Every, "hint.fighting", "hint.guarding")?.Id);
        Assert.Null(Pick(HintTriggers.AwaySheet, HintDepth.Every));
    }

    [Fact]
    public void Light_ShowsOnlyTheFirstOfEachCategory()
    {
        Assert.Equal("hint.fighting", Pick(HintTriggers.CreatureEngaged, HintDepth.FirstOfEachCategory)?.Id);
        Assert.Null(Pick(HintTriggers.CreatureEngaged, HintDepth.FirstOfEachCategory, "hint.fighting"));
        Assert.Null(Pick(HintTriggers.HitTaken, HintDepth.FirstOfEachCategory, "hint.fighting"));
        Assert.Equal("hint.journal", Pick(HintTriggers.QuestStarted, HintDepth.FirstOfEachCategory, "hint.fighting")?.Id);
    }

    [Fact]
    public void Off_ShowsNone()
    {
        Assert.All(HintTriggers.All, t => Assert.Null(Pick(t, HintDepth.None)));
    }

    [Fact]
    public void KeyTokens_AreNamed_AndReplacedByTheirBinding()
    {
        const string text = "{key:cast_1} {key:cast_2} work formulas; {key:interact} acts.";
        Assert.Equal(new[] { "cast_1", "cast_2", "interact" }, HintRules.Actions(text));
        Assert.Equal("4 5 work formulas; E acts.", HintRules.Words(text, a => a switch { "cast_1" => "4", "cast_2" => "5", _ => "E" }));
        Assert.Null(HintRules.Problem(text));
        Assert.NotNull(HintRules.Problem("{wage} is paid"));
        Assert.NotNull(HintRules.Problem(""));
        Assert.NotNull(HintRules.Problem(new string('a', HintRules.MaxLength + 1)));
    }
}
