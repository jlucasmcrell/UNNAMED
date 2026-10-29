using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Progression;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The grade on a resource, the hand it asks for, and the read that answers it (D18 SYSTEMS, needed-2; skill.prospecting). Blackvein Cut
/// ships the second and third iron grades: a hand of 4 is refused the seam graded 2 in words that say how short it is and spends none of
/// its charges, a hand of 10 works it and takes its better yield - and a hand of 0, which can work neither, can still read the deep seam's
/// ground, learn prospecting from it, and be given that read once.
/// </summary>
public class ResourceTierTests
{
    private const string Ore = "item.material.iron_ore";
    private const string Mining = "skill.mining";
    private const string Prospecting = "skill.prospecting";
    private const string Blackvein = "blackvein_iron_seam";
    private const string Deep = "blackvein_deep_seam";

    private static Arena At(GameSession session, string node, Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, session.Setup, StepOff(session, node), 0, Array.Empty<(string, double, double, string)>(),
            change ?? Arena.Usual, origin: Harness.Region);

    /// <summary>Where a node of that name stands, a step off it, in the region's own metres, inside the arm's reach.</summary>
    private static (double X, double Z) StepOff(GameSession session, string name)
    {
        var site = session.Setup.Layout.Nodes.Single(n => n.Name == name);
        return (site.XMm / 1000.0 - 1, site.ZMm / 1000.0);
    }

    private static PlayerRecord Skilled(PlayerRecord r, string skill, int level) =>
        r.WithProgression(r.Progression with { Skills = r.Progression.Skills.SetItem(skill, new SkillState(level, 0)) });

    private static string KeyOf(Arena arena, string name) => arena.Simulation.Nodes.Single(n => n.Name == name).Key;

    private static bool Ready(Arena arena, string name) => arena.Simulation.Nodes.Single(n => n.Name == name).Ready;

    /// <summary>The refusal a hand of that level gets at a seam graded that: the node's own name, and how far short it is.</summary>
    private static string ShortOf(string node, int level, int demand) =>
        $"the {node} is beyond your reading of it: {Mining} {level}, and it wants {demand}";

    [Fact]
    public void AHandBelowTheGrade_IsRefusedInThoseWords_AndSpendsNothing()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, Blackvein, r => Skilled(Arena.Usual(r), Mining, 4));
        var struck = arena.Record<NodeStruck>();
        var gathered = arena.Record<NodeGathered>();
        string key = KeyOf(arena, Blackvein);

        Assert.Equal(ShortOf(Blackvein, 4, 10), arena.Submit(new SwingAtNodeCommand(arena.Player, key)));
        Assert.Null(arena.Simulation.Activity);
        arena.Tick(SwingingSystem.SwingTicks * 3);
        Assert.Empty(struck);
        Assert.Empty(gathered);
        Assert.True(Ready(arena, Blackvein));   // refused, so not one of its three charges is spent
        Assert.DoesNotContain(arena.Simulation.Player.Inventory, e => e.DefId == Ore);
        // The gather asks the same question of the same node, so it refuses in the same words rather than taking what the swing would not.
        Assert.Equal(ShortOf(Blackvein, 4, 10), arena.Submit(new GatherCommand(arena.Player, key)));
    }

    [Fact]
    public void AHandAtTheGrade_WorksIt_AndTakesTheBetterYield()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, Blackvein, r => Skilled(Arena.Usual(r), Mining, CraftingRules.MinSkill(2)));
        var gathered = arena.Record<NodeGathered>();
        string key = KeyOf(arena, Blackvein);

        Assert.Null(arena.Submit(new SwingAtNodeCommand(arena.Player, key)));
        arena.Tick(SwingingSystem.SwingTicks * 3);
        Assert.Null(arena.Simulation.Activity);
        var gave = Assert.Single(gathered);
        Assert.Equal(("node.ore.blackvein_iron", Ore), (gave.NodeDefId, gave.ItemId));
        // The second grade's own yield is two to three a working, plus the one more that mining's own passive gives from level 3.
        Assert.InRange(gave.Count, 3, 4);
    }

    /// <summary>
    /// The read is for the hand that cannot work the ground: at the deep seam a hand of 0 is refused the swing and given the look, which
    /// says the material, the grade and what is left, practises prospecting, and is not offered a second time because the ground is known.
    /// </summary>
    [Fact]
    public void AHandBelowTheGrade_CanStillReadTheGround_AndLearnsProspecting()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, Deep, r => Skilled(Arena.Usual(r), Mining, 0));
        var read = arena.Record<NodeProspected>();
        var practised = arena.Record<SkillPracticed>();
        string key = KeyOf(arena, Deep);

        Assert.Equal(ShortOf(Deep, 0, 20), arena.Submit(new SwingAtNodeCommand(arena.Player, key)));

        Assert.Null(arena.Submit(new ProspectCommand(arena.Player, key)));
        var activity = Assert.IsType<PlayerActivity>(arena.Simulation.Activity);
        Assert.Equal((ActivityKind.Prospect, key), (activity.Kind, activity.TargetKey));
        Assert.Equal(arena.Simulation.WorldTick + ProspectingSystem.ProspectTicks, activity.UntilTick);
        Assert.Empty(read);

        arena.Tick(ProspectingSystem.ProspectTicks);
        var facts = Assert.Single(read);
        Assert.Equal(("node.ore.deep_seam", "resource.ore.deep_iron", 3, 2), (facts.NodeDefId, facts.ResourceId, facts.Tier, facts.ChargesLeft));
        Assert.Contains(practised, p => p.SkillId == Prospecting);
        Assert.Null(arena.Simulation.Activity);

        // Read once, and known: the second look is refused rather than given again.
        Assert.Equal("you have read this ground already", arena.Submit(new ProspectCommand(arena.Player, key)));
    }
}
