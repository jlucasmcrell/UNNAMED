using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Progression;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Mining and gathering swings: the pick goes in for its own ticks, and a node whose content asks for more than one swing gives on the
/// last of them - the third user of the timed activity fishing and lockpicking share. The Iron Seam takes three swings to each of its
/// three workings; a stand, a patch or a snare is worked at a touch, as every node was before it said otherwise.
/// </summary>
public class SwingingTests
{
    private const string Ore = "item.material.iron_ore";
    private const string Mining = "skill.mining";

    private static Arena At(GameSession session, (double X, double Z) place, Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), change ?? Arena.Usual);

    /// <summary>Where a node of that name stands, a step off it: in the hollow's own metres, inside the arm's reach.</summary>
    private static (double X, double Z) SpotOf(GameSession session, string name)
    {
        var hollow = Harness.HollowOf(session.Setup);
        var site = session.Setup.Layout.Nodes.Single(n => n.Name == name);
        return (site.XMm / 1000.0 - hollow.X - 1, site.ZMm / 1000.0 - hollow.Z);
    }

    private static string KeyOf(Arena arena, string name) => arena.Simulation.Nodes.Single(n => n.Name == name).Key;

    private static bool Ready(Arena arena, string name) => arena.Simulation.Nodes.Single(n => n.Name == name).Ready;

    private static int Carried(Arena arena, string defId) => arena.Simulation.Player.Inventory.Where(e => e.DefId == defId).Sum(e => e.Count);

    /// <summary>Swing at a node and let the swings its content asks for run out.</summary>
    private static void Work(Arena arena, string key, int swings)
    {
        Assert.Null(arena.Submit(new SwingAtNodeCommand(arena.Player, key)));
        arena.Tick(SwingingSystem.SwingTicks * swings);
        Assert.Null(arena.Simulation.Activity);
    }

    [Fact]
    public void TheSeam_IsDrawnAtThreeSwings_AndGivesOnTheLastOfThem()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, SpotOf(session, "iron_seam"));
        var started = arena.Record<SwingStarted>();
        var struck = arena.Record<NodeStruck>();
        var gathered = arena.Record<NodeGathered>();
        var practised = arena.Record<SkillPracticed>();
        string seam = KeyOf(arena, "iron_seam");

        // The pick goes in: one swing's worth of ticks, and one timed activity, as a cast or a pick is.
        Assert.Null(arena.Submit(new SwingAtNodeCommand(arena.Player, seam)));
        var swing = Assert.IsType<PlayerActivity>(arena.Simulation.Activity);
        Assert.Equal((ActivityKind.Swing, seam, 1), (swing.Kind, swing.TargetKey, swing.Strike));
        Assert.Equal(arena.Simulation.WorldTick + SwingingSystem.SwingTicks, swing.UntilTick);
        Assert.Equal((seam, "node.ore.iron_seam"), (Assert.Single(started).NodeKey, started[0].NodeDefId));

        // The first swing lands and the seam holds: a strike is spent, nothing is taken, its charges are its own until the last swing.
        arena.Tick(SwingingSystem.SwingTicks);
        Assert.Equal((seam, 2), (Assert.Single(struck).NodeKey, struck[0].StrikesLeft));
        Assert.Empty(gathered);
        Assert.Equal(0, Carried(arena, Ore));
        Assert.True(Ready(arena, "iron_seam"));
        Assert.Single(practised, p => p.SkillId == Mining);
        Assert.Equal(2, Assert.IsType<PlayerActivity>(arena.Simulation.Activity).Strike);   // the pick goes on

        // The second, and then the third, which fills it: the item, the practice and the tell arrive together.
        arena.Tick(SwingingSystem.SwingTicks);
        Assert.Equal(2, struck.Count);
        Assert.Empty(gathered);
        arena.Tick(SwingingSystem.SwingTicks);
        var gave = Assert.Single(gathered);
        Assert.Equal(("node.ore.iron_seam", Ore), (gave.NodeDefId, gave.ItemId));
        Assert.Equal(gave.Count, Carried(arena, Ore));
        Assert.Null(arena.Simulation.Activity);
        // One swing, one practice: the two the node held back and the one the gather resolved with the yield.
        Assert.Equal(3, practised.Count(p => p.SkillId == Mining));
    }

    [Fact]
    public void ANodeThatTakesOneSwing_IsGatheredAtATouch()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // An ashbloom patch takes the default: no swings are authored for it, and one swing is what a node that names none takes.
        var arena = At(session, SpotOf(session, "bog_ashbloom_1"));
        var struck = arena.Record<NodeStruck>();
        var gathered = arena.Record<NodeGathered>();

        Work(arena, KeyOf(arena, "bog_ashbloom_1"), 1);

        Assert.Empty(struck);
        Assert.Equal("node.firstplay.ashbloom", Assert.Single(gathered).NodeDefId);
        Assert.Equal(1, Carried(arena, "item.material.herb_ashbloom"));
        Assert.False(Ready(arena, "bog_ashbloom_1"));   // its one charge, a day
    }

    [Fact]
    public void WalkingOffTheNode_AndCancelling_GrantNothing_AndLeaveItsCharges()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, SpotOf(session, "iron_seam"));
        var gathered = arena.Record<NodeGathered>();
        string seam = KeyOf(arena, "iron_seam");

        // A cancel ends the swing where it stands: no yield, and the seam keeps every charge it had.
        Assert.Null(arena.Submit(new SwingAtNodeCommand(arena.Player, seam)));
        arena.Tick(SwingingSystem.SwingTicks - 1);
        Assert.Null(arena.Submit(new CancelActivityCommand(arena.Player)));
        Assert.Null(arena.Simulation.Activity);
        arena.Tick(SwingingSystem.SwingTicks * 3);
        Assert.Empty(gathered);
        Assert.Equal(0, Carried(arena, Ore));
        Assert.True(Ready(arena, "iron_seam"));

        // Walking off ends one too, on the tick the arm can no longer reach it.
        Assert.Null(arena.Submit(new SwingAtNodeCommand(arena.Player, seam)));
        arena.Tick(2);
        Assert.True(arena.WalkTo(arena.At.X - 8, arena.At.Z), "walked off the seam");
        Assert.Null(arena.Simulation.Activity);
        arena.Tick(SwingingSystem.SwingTicks * 3);
        Assert.Empty(gathered);
        Assert.Equal(0, Carried(arena, Ore));
        Assert.True(Ready(arena, "iron_seam"));
    }

    [Fact]
    public void AWorkedOutSeam_RefusesTheNextSwing_InTheGathersOwnWords()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, SpotOf(session, "iron_seam"));
        string seam = KeyOf(arena, "iron_seam");

        // Three workings of three swings each: the seam's charges.
        for (int working = 0; working < 3; working++)
            Work(arena, seam, 3);

        Assert.False(Ready(arena, "iron_seam"));
        Assert.Equal("it is worked out", arena.Submit(new SwingAtNodeCommand(arena.Player, seam)));
        Assert.Null(arena.Simulation.Activity);
        Assert.Equal("there is no node 'node.nowhere'", arena.Submit(new SwingAtNodeCommand(arena.Player, "node.nowhere")));
    }
}
