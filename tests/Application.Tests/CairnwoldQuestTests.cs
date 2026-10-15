using UNNAMED.Domain.Quests;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Cairnwold's two quests end to end in region.starting_frontier, by the playthrough's own routes: Durn's spear from the moor's own
/// seam and ash, and Mara's three ashbloom from the Sinking Moss given back to her at the well.
/// </summary>
public class CairnwoldQuestTests
{
    private const string Durn = "npc.cairnwold.durn_skell";
    private const string Mara = "npc.cairnwold.mara_ling";
    private const string IronFromTheMoor = "quest.cairnwold.iron_from_the_moor";
    private const string WhatTheBogKept = "quest.cairnwold.what_the_bog_kept";
    private const string Ashbloom = "item.material.herb_ashbloom";

    private static readonly (double X, double Z) AtDurn = (980.7, 674.4);
    private static readonly (double X, double Z) AtMara = (975.8, 666);
    private static readonly (double X, double Z)[] ToTheSeam =
        { (980.8, 657), (979, 655.2), (977.2, 648.2), (972.2, 640.2), (971, 639), (970.5, 633.2), (956.8, 609.2), (957.8, 605.5) };
    private static readonly (double X, double Z)[] ToTheSmithyDoor = { (956.8, 609.5), (971, 637), (971.8, 644.2), (978.2, 651.5), (980.2, 661), (980.8, 661.5), (982, 677.5) };
    private static readonly (double X, double Z)[] ToTheHearth = { (982.2, 679.8), (983.5, 683.5) };
    private static readonly (double X, double Z)[] ToTheStand = { (982, 680.5), (982, 677.5), (987.2, 678.2), (988, 679), (984.5, 692.5), (975, 713.2) };
    private static readonly (double X, double Z)[] ToTheAnvil = { (986.5, 685.2), (988, 679), (987.2, 678.2), (982, 677.5), (982, 679), (981.2, 681.2) };
    private static readonly (double X, double Z)[] OutToDurn = { (982, 679), (982, 677.5), (982, 675.8) };
    private static readonly (double X, double Z)[] ToTheMoss =
        { (985, 677.2), (987.5, 678.5), (1004.2, 703.5), (1004.5, 706.2), (1036.8, 748.5), (1037, 750.5), (1042.8, 751.8), (1044, 755.5) };
    private static readonly (double X, double Z)[] BackToMara =
        { (1042.5, 751.5), (1037, 750.5), (1032.8, 743.2), (988, 684.2), (987.8, 678.8), (985.2, 677), (984.5, 676.8), (976.2, 666.2) };

    private static Arena At(GameSession session, (double X, double Z) place) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), origin: Harness.Region);

    private static void Walk(Arena arena, params (double X, double Z)[] route)
    {
        foreach (var (x, z) in route)
            Assert.True(arena.WalkTo(x, z), $"never reached ({x}, {z}): stopped at {arena.Simulation.Player.Body}");
    }

    private static void Say(Arena arena, string npcId, params string[] replies)
    {
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, npcId)));
        foreach (string reply in replies)
            Assert.Null(arena.Submit(new ChooseCommand(arena.Player, reply)));
        if (arena.Simulation.Conversation is not null)
            Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
        arena.Tick();
    }

    private static void Gather(Arena arena, string node)
    {
        var site = arena.Simulation.Nodes.Single(n => n.Name == node);
        string? refused = arena.Submit(new GatherCommand(arena.Player, site.Key));
        Assert.True(refused is null, $"{node} at ({site.XMm}, {site.ZMm}): {refused}, from {arena.Simulation.Player.Body}");
        arena.Tick();
    }

    private static int Carried(Arena arena, string defId) => arena.Simulation.Player.Inventory.Where(e => e.DefId == defId).Sum(e => e.Count);

    private static QuestStatus Status(Arena arena, string quest) => arena.Simulation.Quests.Single(q => q.Id == quest).Status;

    [Fact]
    public void IronFromTheMoor_PlaysEndToEnd_FromCairnwoldsOwnSeamAndAsh()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), AtDurn);

        Say(arena, Durn, "teach", "thanks");
        Walk(arena, ToTheSeam);
        Gather(arena, "cairnwold_iron_seam");
        Assert.True(Carried(arena, "item.material.iron_ore") > 0, "the seam gave no ore");
        Walk(arena, ToTheSmithyDoor);
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "door.smithy")));
        arena.Tick();
        Walk(arena, ToTheHearth);
        Assert.Null(arena.Submit(new CraftCommand(arena.Player, "recipe.smithing.iron_billet")));
        arena.Tick();
        Walk(arena, ToTheStand);
        Gather(arena, "cairnwold_ash_stand");
        Walk(arena, ToTheAnvil);
        Assert.Null(arena.Submit(new CraftCommand(arena.Player, "recipe.smithing.march_spear")));
        arena.Tick();
        Walk(arena, OutToDurn);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Durn)));
        string show = arena.Simulation.Conversation!.Replies.Select(r => r.Id).First(r => r.StartsWith("show", StringComparison.Ordinal));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, show)));
        arena.Tick();

        Assert.Equal(QuestStatus.Completed, Status(arena, IronFromTheMoor));
    }

    [Fact]
    public void WhatTheBogKept_PlaysEndToEnd_WithAshbloomFromTheMoss()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), AtMara);

        Say(arena, Mara, "ask", "offer", "back");
        Assert.Equal(QuestStatus.Active, Status(arena, WhatTheBogKept));
        Walk(arena, ToTheMoss);
        Gather(arena, "bog_ashbloom_1");
        Walk(arena, (1048, 780.2));
        Gather(arena, "bog_ashbloom_3");
        Walk(arena, (1059.5, 775.5));
        Gather(arena, "bog_ashbloom_2");
        Assert.Equal(3, Carried(arena, Ashbloom));
        Walk(arena, BackToMara);
        Walk(arena, AtMara);
        Say(arena, Mara, "bog_return", "back");

        Assert.Equal(QuestStatus.Completed, Status(arena, WhatTheBogKept));
        Assert.Equal(0, Carried(arena, Ashbloom));   // given to her
    }
}
