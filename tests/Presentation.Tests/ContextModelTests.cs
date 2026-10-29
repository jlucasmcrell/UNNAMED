using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Tests;

/// <summary>The context line under the prompt, what interacting with a person does, and the outline rules (W12, the gatherable affordance).</summary>
public class ContextModelTests
{
    private static readonly NpcHireView Hireable = new("routine.hunter", true, null, ImmutableArray.Create("role.hunter"), ImmutableArray.Create(HirelingRole.Guard, HirelingRole.Porter));
    private static readonly NpcHireView Busy = new("routine.hunter", false, "Mira is injured", ImmutableArray.Create("role.hunter"), ImmutableArray.Create(HirelingRole.Guard));
    private static readonly GroupPost Trip = new(EntityId.NewId(EntityKind.Npc), "Bram", "grey wolves", "the Old Den", "the North Gate", "14:30", "in about 1 h", false, null);

    private static NpcFacts Npc(bool talks = false, bool downed = false, bool party = false, NpcHireView? hire = null, bool teaches = false, GroupPost? trip = null) =>
        new("Mira", talks, downed, party, hire, teaches, trip);

    [Fact]
    public void APerson_IsHelpedUpFirst_ThenTalkedTo_ThenSeenInTheParty_ThenAskedAboutWork()
    {
        Assert.Equal(NpcApproachKind.HelpUp, ContextModel.Approach(Npc(talks: true, downed: true, party: true)));
        Assert.Equal(NpcApproachKind.Talk, ContextModel.Approach(Npc(talks: true, hire: Hireable)));
        Assert.Equal(NpcApproachKind.Party, ContextModel.Approach(Npc(party: true, hire: Hireable)));
        Assert.Equal(NpcApproachKind.Hire, ContextModel.Approach(Npc(hire: Hireable)));
        Assert.Equal(NpcApproachKind.Hire, ContextModel.Approach(Npc(hire: Busy)));   // the sheet says why not
        Assert.Equal(NpcApproachKind.Hire, ContextModel.Approach(Npc(trip: Trip)));
        Assert.Equal(NpcApproachKind.Talk, ContextModel.Approach(Npc()));
    }

    [Fact]
    public void ThePrompt_NamesWhatInteractingDoes()
    {
        Assert.Equal("Help Mira up", ContextModel.Prompt(Npc(downed: true)));
        Assert.Equal("Talk to Mira", ContextModel.Prompt(Npc(talks: true)));
        Assert.Equal("Ask Mira about work", ContextModel.Prompt(Npc(hire: Hireable)));
        Assert.Equal("Ask Mira about the trip", ContextModel.Prompt(Npc(trip: Trip)));
        Assert.Equal("Mira: your party", ContextModel.Prompt(Npc(party: true)));
    }

    [Fact]
    public void APersonsContextLine_SaysTheTripHiringAndTeaching_OrNothing()
    {
        Assert.Null(ContextModel.Npc(Npc(talks: true)));
        Assert.Equal("Hires on as guard, porter", ContextModel.Npc(Npc(hire: Hireable)));
        Assert.Equal("Not hiring now: is injured", ContextModel.Npc(Npc(hire: Busy)));
        Assert.Equal("Asking for company: leaves the North Gate at 14:30 · Teaches", ContextModel.Npc(Npc(teaches: true, trip: Trip)));
        Assert.Equal("Expects you at the North Gate by 14:30", ContextModel.Npc(Npc(trip: Trip with { Joined = true })));
        Assert.Equal("With you", ContextModel.Npc(Npc(party: true, hire: Hireable)));
    }

    [Fact]
    public void ANodesContextLine_SaysWhatItGivesAndTheSkill_OrWhetherItGrowsBack()
    {
        var vein = new NodeDefinition("node.iron_vein", "item.material.iron_ore", 1, 2, 3, Respawn.Daily, "skill.mining", 1);
        Assert.Equal("Gives iron ore · mining 2 · grade 1", ContextModel.Node(vein, true, 2, Names.Words));
        Assert.Equal("Gives iron ore · mining (untrained) · grade 1", ContextModel.Node(vein, true, 0, Names.Words));
        Assert.Equal("Grows back in time", ContextModel.Node(vein, false, 2, Names.Words));
        Assert.Equal("Gone for good", ContextModel.Node(vein with { Respawn = Respawn.None }, false, 2, Names.Words));
    }

    /// <summary>
    /// The grade is on the line, and a grade the hand is short of says so where it stands: the read is offered by the prompt beside it
    /// (skill.prospecting), so a hand that cannot work the ground is told what it is looking at before it swings.
    /// </summary>
    [Fact]
    public void ANodesGrade_IsOnItsLine_AndSaysWhenItIsBeyondTheHand()
    {
        var blackvein = new NodeDefinition("node.ore.blackvein_iron", "item.material.iron_ore", 2, 3, 3, Respawn.None, "skill.mining", 12) { Tier = 2 };
        Assert.Equal("Gives iron ore · mining 10 · grade 2", ContextModel.Node(blackvein, true, CraftingRules.MinSkill(2), Names.Words));
        Assert.Equal("Gives iron ore · mining 4 · grade 2, beyond you", ContextModel.Node(blackvein, true, 4, Names.Words));
        // The first grade asks nothing of the hand, so a hand is never short of it.
        Assert.Equal("Gives iron ore · mining (untrained) · grade 1", ContextModel.Node(blackvein with { Tier = 1 }, true, 0, Names.Words));
    }

    [Fact]
    public void ContainersItemsAndStations_HaveTheirLine()
    {
        Assert.Equal("Empty", ContextModel.Container(0));
        Assert.Equal("Holds one thing", ContextModel.Container(1));
        Assert.Equal("Holds 4 things", ContextModel.Container(4));
        var rules = LadenRules.Default;
        Assert.Equal("0.4 kg", ContextModel.Item(400, 10_000, 50_000, rules));
        Assert.Equal("2 kg · you would be laden", ContextModel.Item(2_000, 39_000, 50_000, rules));
        Assert.Equal("2 kg · too heavy to take: you are carrying all you can", ContextModel.Item(2_000, 49_000, 50_000, rules));
        Assert.Equal("You know 2 of the 5 things made here", ContextModel.Station(2, 5));
        Assert.Equal("You know none of the 5 things made here", ContextModel.Station(0, 5));
        Assert.Equal("Nothing is made here yet", ContextModel.Station(0, 0));
    }

    [Fact]
    public void TheFocus_IsOutlined_UnlessItIsANodeWithNothingToGather()
    {
        Assert.Equal(OutlineTone.Focus, ContextModel.Focus(isNode: false, nodeReady: false));
        Assert.Equal(OutlineTone.Focus, ContextModel.Focus(isNode: true, nodeReady: true));
        Assert.Equal(OutlineTone.None, ContextModel.Focus(isNode: true, nodeReady: false));
    }

    [Fact]
    public void GatherableNodes_AreTheReadyOnesNearby_NearestFirst_WithoutTheFocus()
    {
        var nodes = new[]
        {
            new NodeView("n.far", "far", "node.iron_vein", "item.material.iron_ore", 30_000, 0, true),
            new NodeView("n.near", "near", "node.iron_vein", "item.material.iron_ore", 2_000, 0, true),
            new NodeView("n.mid", "mid", "node.oak", "item.material.timber", 0, 5_000, true),
            new NodeView("n.spent", "spent", "node.oak", "item.material.timber", 1_000, 0, false),
            new NodeView("n.focus", "focus", "node.oak", "item.material.timber", 500, 0, true),
        };
        Assert.Equal(new[] { "n.near", "n.mid" }, ContextModel.Gatherable(nodes, 0, 0, 12_000, "n.focus", 8));
        Assert.Equal(new[] { "n.near" }, ContextModel.Gatherable(nodes, 0, 0, 12_000, "n.focus", 1));
    }
}
