using UNNAMED.Domain.Character;
using UNNAMED.Domain.Quests;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP2's local part, the opening (OD-01 "The Road In", PJ06 §5, §8): a character from the creator starts on the road below the north-west
/// crest or, the veteran's skip, at the Ashen Waystone; the world is the same either way, and the journal's first entry (WP4's
/// <c>on_new_game</c>) is there before the first tick.
/// </summary>
public class OpeningTests
{
    private const string Opening = "quest.main.the_road_in";

    private static CharacterSpec Spec(string? call, StartPoint start) =>
        new CharacterSpec("Wenna Lind", Pronouns.She, CharacterSpec.DefaultPeople, "body.veth_f.lean", call) { Start = start };

    [Theory]
    [MemberData(nameof(CharacterCreationTests.Calls), MemberType = typeof(CharacterCreationTests))]
    public void ANewGame_WithEachCall_TakingTheRoad_StartsAtTheOpening_AndTheWorldRuns(string call)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var opening = session.Setup.Layout.Opening;
        Assert.NotNull(opening);

        var simulation = session.NewGame(Spec(call, StartPoint.Road), seed: 42);

        Assert.Equal(opening, simulation.Player.Body);
        Assert.Equal(call, simulation.Player.Identity.CallId);
        Assert.Equal(QuestStatus.Active, simulation.Quests.Single(q => q.Id == Opening).Status);
        Harness.Ticks(session, 20);
        Assert.Equal(20, simulation.WorldTick);
    }

    [Fact]
    public void TheWaystoneStart_IsTheSpawn_AndTheOpeningsArrivalIsAlreadyMade()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame(Spec("call.wayfarer", StartPoint.Waystone), seed: 42);

        Assert.Equal(session.Setup.Layout.Spawn, simulation.Player.Body);
        Harness.Ticks(session, 2);
        var road = simulation.Quests.Single(q => q.Id == Opening);
        Assert.Equal(ObjectiveStatus.Satisfied, road.Objectives.Single(o => o.Id == "o_arrive").Status);
        Assert.Equal(ObjectiveStatus.Active, road.Objectives.Single(o => o.Id == "o_keeper").Status);
    }

    [Fact]
    public void TheRoadIn_WalksStraightDownToTheWaystone_AndArrivingCountsForTheOpening()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame(Spec(null, StartPoint.Road), seed: 42);
        var road = () => simulation.Quests.Single(q => q.Id == Opening).Objectives.Single(o => o.Id == "o_arrive").Status;
        Assert.Equal(ObjectiveStatus.Active, road());

        var spawn = session.Setup.Layout.Spawn;
        Assert.True(Harness.WalkTo(session, spawn.XMm, spawn.ZMm, maxTicks: 1200), "the road from the crest to the Waystone is open");
        Harness.Ticks(session, 2);

        Assert.Equal(ObjectiveStatus.Satisfied, road());
    }

    [Fact]
    public void ASpecWithAStartThatDoesNotExist_IsRefused()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        Assert.Throws<ArgumentException>(() => session.NewGame(Spec(null, (StartPoint)7)));
        Assert.Null(session.Simulation);
    }
}
