using System.Collections.Immutable;
using UNNAMED.Domain.Factions;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// The Reputation sheet's words (WP6): standing is report-only (PJ07), so the sheet tells what the character has been told - a regard in
/// words and what each faction has heard - and never a number.
/// </summary>
public class StandingWordsTests
{
    private const string Survey = "faction.ashen_hollow.long_survey", Wayhold = "faction.ashen_hollow.wayhold";

    private static readonly Dictionary<string, string> Content = new()
    {
        ["npc.ashen_hollow.sel_arien"] = "Sel Arien",
        ["creature.ridge_wolf"] = "Ridge wolf",
        ["world.foldscar.steadied"] = "foldscar steadied",
    };

    private static string Name(string id) => Content.GetValueOrDefault(id, id);
    private static string Place(string id) => "the outpost";

    private static FactionView Faction(string id, string name, int points, string tier) =>
        new(id, name, points, tier, StandingLadder.LevelOf(tier), "location.outpost",
            id == Survey ? ImmutableArray.Create("npc.ashen_hollow.sel_arien") : ImmutableArray<string>.Empty,
            ImmutableArray.Create(new RelationView(id == Survey ? Wayhold : Survey, "cordial")));

    private static ActView Act(long seq, string kind, string subject, params KnowledgeView[] known) =>
        new(seq, kind, subject, "0:0:0", 0, 0, 100, known.ToImmutableArray());

    [Fact]
    public void AFactionThatHeardNothing_SaysSo_WithNoNumber()
    {
        var factions = new[] { Faction(Survey, "the Long Survey", 0, "neutral"), Faction(Wayhold, "the Wayhold", 0, "neutral") };
        var report = StandingWords.Of(factions, Array.Empty<ActView>(), Name, Place)[0];
        Assert.False(report.Known);
        Assert.Equal("The Long Survey", report.Name);
        Assert.Equal("They have heard nothing of you.", report.Regard);
        Assert.Equal("Not heard", report.Word);
        Assert.Equal(0, report.Level);
        Assert.Empty(report.Heard);
        Assert.Equal("At the outpost · Sel Arien · cordial to the Wayhold", report.About);
    }

    [Fact]
    public void WhatAMemberWasTold_IsWhatTheSheetTells()
    {
        var factions = new[] { Faction(Survey, "the Long Survey", 100, "accepted"), Faction(Wayhold, "the Wayhold", 0, "neutral") };
        var acts = new[]
        {
            Act(1, ActKinds.SwitchSet, "world.foldscar.steadied",
                new KnowledgeView(Survey, Identities.Identified, KnowledgeSources.Reported, "npc.ashen_hollow.sel_arien", 120, 100)),
        };
        var reports = StandingWords.Of(factions, acts, Name, Place);
        Assert.True(reports[0].Known);
        Assert.Equal("They accept you.", reports[0].Regard);
        Assert.Equal("Accepted", reports[0].Word);
        Assert.Equal(1, reports[0].Level);
        Assert.Equal(new[] { "You told Sel Arien that you saw to foldscar steadied." }, reports[0].Heard);
        // The other faction was told nothing, so knows nothing.
        Assert.False(reports[1].Known);
        Assert.Empty(reports[1].Heard);
    }

    [Fact]
    public void ARegardMovedByAQuest_IsKnown_EvenWithNothingHeard()
    {
        var report = StandingWords.Of(new[] { Faction(Wayhold, "the Wayhold", 250, "trusted") }, Array.Empty<ActView>(), Name, Place)[0];
        Assert.True(report.Known);
        Assert.Equal("They trust you.", report.Regard);
    }

    [Fact]
    public void HeardAboutSomeone_IsNotHeardAboutYou()
    {
        var act = Act(2, ActKinds.CreatureKilled, "creature.ridge_wolf",
            new KnowledgeView(Survey, Identities.Unidentified, KnowledgeSources.Witnessed, null, 120, 0));
        Assert.Equal("They have heard that someone killed a ridge wolf, not knowing it was you.", StandingWords.Heard(act, act.Known[0], Name));
        var told = act with { Known = ImmutableArray.Create(new KnowledgeView(Survey, Identities.Identified, KnowledgeSources.Witnessed, null, 120, 5)) };
        Assert.Equal("They know that you killed a ridge wolf.", StandingWords.Heard(told, told.Known[0], Name));
    }

    [Fact]
    public void EveryTier_HasWords_AndNoneIsANumber()
    {
        foreach (var (key, _) in StandingLadder.Keys)
        {
            string regard = StandingWords.Regard(key);
            Assert.False(string.IsNullOrWhiteSpace(regard));
            Assert.DoesNotContain(regard, char.IsDigit);
        }
    }
}
