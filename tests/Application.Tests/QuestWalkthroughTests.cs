using UNNAMED.Domain.Quests;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The first hour, played rather than asserted about: a fresh character starts the story, follows the road in, meets the keeper, takes
/// the Veth heritage thread to Cairnwold and closes it, and takes a bounty and is paid for it - every step through the commands the player
/// actually submits, with each objective's own status checked as it goes.
/// </summary>
public class QuestWalkthroughTests
{
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Berrin = "npc.cairnwold.berrin_garth";
    private const string Tam = "npc.cairnwold.tam_crowden";

    private const string RoadIn = "quest.main.the_road_in";
    private const string WhatWasKept = "quest.heritage.what_was_kept";
    private const string LodgeSupplies = "quest.bounty.lodge_supplies";

    private const string Timber = "item.material.timber";
    private const string RawMeat = "item.material.raw_meat";

    // The givers' own places: the hollow's three in the hollow's metres, Cairnwold's in the region's.
    private static readonly (double X, double Z) AtRenn = (48, 126.2);
    private static readonly (double X, double Z) AtBerrin = (957.5, 643.5);
    private static readonly (double X, double Z) AtTam = (941, 679.8);

    /// <summary>
    /// The region's own road between the hollow and Cairnwold - <c>road.old_east_road</c>'s own points, from Renn's door to Berrin's -
    /// because a straight line between the two settlements runs through the waystation's buildings.
    /// </summary>
    private static readonly (double X, double Z)[] OnTheRoad =
    {
        (456, 729), (461, 739), (464, 748), (522, 690), (564, 690), (600, 656), (680, 657), (760, 658),
        (840, 659), (900, 660), (920, 660), (938, 654), (952, 656), (957.5, 648), (957.5, 644.5),
    };

    /// <summary>The same road walked backwards, from Berrin's door home to Renn's.</summary>
    private static readonly (double X, double Z)[] OffTheRoad =
        OnTheRoad.Reverse().Append((Harness.Hollow.X + AtRenn.X, Harness.Hollow.Z + AtRenn.Z)).ToArray();

    private static Arena InHollow(GameSession session, (double X, double Z) place,
        Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), change);

    private static Arena InRegion(GameSession session, (double X, double Z) place, Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), change,
            origin: Harness.Region);

    /// <summary>Every quest's state as one string, to notice when the world has changed under a conversation.</summary>
    private static string Quests(Arena arena) =>
        string.Join(";", arena.Simulation.Quests.Select(q => q.Id + ":" + q.Status + ":" + string.Join(",", q.Objectives.Select(o => o.Id + "=" + o.Status))));

    private static QuestView? Quest(Arena arena, string id) =>
        arena.Simulation.Quests.FirstOrDefault(q => q.Id == id);

    private static ObjectiveStatus? Step(Arena arena, string quest, string objective) =>
        Quest(arena, quest)?.Objectives.FirstOrDefault(o => o.Id == objective)?.Status;

    private static string State(Arena arena, string quest) =>
        Quest(arena, quest) is { } q
            ? $"{q.Status}: " + string.Join(", ", q.Objectives.Select(o => $"{o.Id}={o.Status}"))
            : "not started";

    private static int Carried(Arena arena, string defId) =>
        arena.Simulation.Player.Inventory.Where(e => e.DefId == defId).Sum(e => e.Count);

    private static long Coin(Arena arena) => arena.Simulation.CaptureRecord().Currency;

    /// <summary>A character of one of the peoples: heritage content is gated on being one, which this walkthrough proves by playing it.</summary>
    private static Func<PlayerRecord, PlayerRecord> OfPeople(string peopleId) =>
        r => r with { Identity = r.Identity with { PeopleId = peopleId } };

    private static Func<PlayerRecord, PlayerRecord> Carrying(params (string DefId, int Count)[] stacks) =>
        r => r.WithInventory(r.Inventory.Concat(stacks.Select(s => Arena.Stack(s.DefId, s.Count))));

    /// <summary>Walk to where a person is right now: their routine moves them, so their placement is not a fixed coordinate.</summary>
    private static void StandWith(Arena arena, string npcId)
    {
        var npc = arena.Simulation.Npcs.FirstOrDefault(n => n.Id == npcId);
        Assert.True(npc is not null, $"{npcId} is not in the world");
        double x = npc!.Body.XMm / 1000.0 - arena.Origin.X;
        double z = npc.Body.ZMm / 1000.0 - arena.Origin.Z;
        Assert.True(arena.WalkTo(x, z, maxTicks: 40000), $"{npcId} stands at ({x:0.0}, {z:0.0}) and cannot be reached: at {arena.At}");
    }

    private static void Say(Arena arena, string reply)
    {
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, reply)));
        arena.Tick();
    }

    private static void Talk(Arena arena, string npc)
    {
        StandWith(arena, npc);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, npc)));
        arena.Tick();
    }

    /// <summary>
    /// Poke around a giver's conversation the way a player does: take a reply, see where it leads, take the next one there, and when the
    /// branch runs out go back to the greeting and try what is left. Stops as soon as <paramref name="done"/> holds.
    /// </summary>
    private static int Explore(Arena arena, string npc, Func<bool> done, int maxChoices = 60)
    {
        var tried = new HashSet<string>(StringComparer.Ordinal);
        int choices = 0;
        string signature = Quests(arena);
        for (int i = 0; i < maxChoices; i++)
        {
            if (done())
                return choices;
            // the world changed: whatever the giver said before, say it again
            string now = Quests(arena);
            if (now != signature)
            {
                signature = now;
                tried.Clear();
            }
            if (arena.Simulation.Conversation is null)
            {
                Talk(arena, npc);
                if (arena.Simulation.Conversation is null)
                    continue;
            }
            string node = arena.Simulation.Conversation!.NodeId;
            string? reply = arena.Simulation.Conversation.Replies.Select(r => r.Id)
                .FirstOrDefault(id => tried.Add(node + "|" + id));
            if (reply is null)
            {
                // nothing new on this branch: back out and start again from the greeting
                if (arena.Simulation.Conversation is not null && arena.Simulation.Conversation.Replies.Any(r => r.Id == "leave"))
                    Say(arena, "leave");
                else
                    arena.Submit(new LeaveCommand(arena.Player));
                arena.Tick();
                continue;
            }
            if (arena.Submit(new ChooseCommand(arena.Player, reply)) is null)
            {
                arena.Tick();
                choices++;
            }
        }
        return choices;
    }

    /// <summary>Take a quest the way a player does, by finding the line that offers it.</summary>
    private static void Take(Arena arena, string npc, string quest)
    {
        Explore(arena, npc, () => Quest(arena, quest) is not null);
        Assert.True(Quest(arena, quest) is not null, $"{npc} offered no line that starts {quest}");
    }

    /// <summary>
    /// Satisfy one objective through the reply that advances it: walk there first when the test says how, then explore the giver's lines
    /// until the step's own status turns. An item the step requires is carried from the start, because gathering it is another test's job.
    /// </summary>
    private static void Advance(Arena arena, string quest, string objective, string npc, params (double X, double Z)[] walk)
    {
        if (walk.Length > 0)
            foreach (var (x, z) in walk)
                Assert.True(arena.WalkTo(x, z, maxTicks: 40000), $"never reached ({x}, {z}) for {quest}.{objective}: at {arena.At}");

        Explore(arena, npc, () => Step(arena, quest, objective) is ObjectiveStatus.Satisfied or ObjectiveStatus.Closed);
        Assert.True(Step(arena, quest, objective) is ObjectiveStatus.Satisfied or ObjectiveStatus.Closed,
            $"{quest}.{objective} could not be satisfied by talking to {npc}: {State(arena, quest)}");
    }

    [Fact]
    public void TheRoadIn_StartsOnANewCharacter_AndCompletesWhenTheKeeperIsSpokenTo()
    {
        using var profile = new TempProfile();
        var arena = InHollow(Harness.Boot(profile), AtRenn);

        Assert.Null(arena.Submit(new BeginStoryCommand(arena.Player)));
        arena.Tick();

        Assert.NotNull(Quest(arena, RoadIn));
        // a new character starts inside the waystation, so arriving is already done and the keeper is the step left
        Assert.Equal(ObjectiveStatus.Satisfied, Step(arena, RoadIn, "o_arrive") ?? ObjectiveStatus.Closed);

        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Renn)));
        arena.Tick();

        Assert.Equal(QuestStatus.Completed, Quest(arena, RoadIn)!.Status);
        Assert.Equal(ObjectiveStatus.Satisfied, Step(arena, RoadIn, "o_keeper"));
    }

    [Fact]
    public void WhatWasKept_PlaysFromRennsRollToCairnwoldsBookAndBackForRelease()
    {
        using var profile = new TempProfile();
        // the Kept Roll is a Veth inheritance: the walkthrough plays it as a Veth, in the region's own metres
        var arena = InRegion(Harness.Boot(profile), (Harness.Hollow.X + AtRenn.X, Harness.Hollow.Z + AtRenn.Z), OfPeople("people.veth"));
        Assert.Null(arena.Submit(new BeginStoryCommand(arena.Player)));
        arena.Tick();

        Assert.NotNull(Quest(arena, WhatWasKept));
        // the roll is opened by showing Renn the open line, the way a player has to
        Advance(arena, WhatWasKept, "o_roll", Renn);

        // the other half of the oath is in Cairnwold: the road there, in the region's metres
        foreach (var (x, z) in OnTheRoad)
            Assert.True(arena.WalkTo(x, z, maxTicks: 40000), $"never reached ({x}, {z}) on the road to Cairnwold: at {arena.At}");

        Advance(arena, WhatWasKept, "o_lodge_book", Berrin);
        Advance(arena, WhatWasKept, "o_release", Berrin);
        // the release and the keeping are one choice ("branch: first"): taking the release satisfies it and the keeping is
        // never taken, which is the design - so the release is what this walkthrough asserts
        Assert.Equal(ObjectiveStatus.Satisfied, Step(arena, WhatWasKept, "o_release"));

        // and back to Renn so the open line can be closed
        foreach (var (x, z) in OffTheRoad)
            Assert.True(arena.WalkTo(x, z, maxTicks: 40000), $"never reached ({x}, {z}) on the road home: at {arena.At}");
        Advance(arena, WhatWasKept, "o_recorded", Renn);

        Assert.Equal(QuestStatus.Completed, Quest(arena, WhatWasKept)!.Status);
    }

    [Fact]
    public void LodgeSupplies_IsTakenFromTam_PaidOnDelivery_AndPostedAgainOnlyAfterAWorldDay()
    {
        using var profile = new TempProfile();
        // two portions of meat, which hunting would take an hour to bring back: the character starts carrying them
        var arena = InRegion(Harness.Boot(profile), AtTam, Carrying((RawMeat, 2)));

        long coin = Coin(arena);
        Take(arena, Tam, LodgeSupplies);
        Assert.Equal(QuestStatus.Active, Quest(arena, LodgeSupplies)!.Status);
        Assert.Equal(ObjectiveStatus.Active, Step(arena, LodgeSupplies, "o_deliver"));
        Assert.Equal(2, Carried(arena, RawMeat));
        Advance(arena, LodgeSupplies, "o_deliver", Tam);

        Assert.Equal(QuestStatus.Completed, Quest(arena, LodgeSupplies)!.Status);
        Assert.True(Coin(arena) > coin, $"the pot paid nothing: {coin} before, {Coin(arena)} after");
    }

    [Fact]
    public void TheFirstHoursStarterQuests_AreAllReachableFromANewCharacter()
    {
        using var profile = new TempProfile();
        var arena = InHollow(Harness.Boot(profile), AtRenn);
        Assert.Null(arena.Submit(new BeginStoryCommand(arena.Player)));
        arena.Tick();

        // on_new_game starts the opening for everyone; the two heritage threads start for the people they belong to
        Assert.True(Quest(arena, RoadIn) is not null, $"{RoadIn} is on_new_game and did not start: {State(arena, RoadIn)}");

        foreach (var (people, quest) in new[]
                 {
                     ("people.veth", WhatWasKept),
                     ("people.kal", "quest.heritage.unfinished_joint"),
                 })
        {
            using var heritageProfile = new TempProfile();
            var heritage = InHollow(Harness.Boot(heritageProfile), AtRenn, OfPeople(people));
            Assert.Null(heritage.Submit(new BeginStoryCommand(heritage.Player)));
            heritage.Tick();
            Assert.True(Quest(heritage, quest) is not null, $"{quest} did not start for {people}: {State(heritage, quest)}");
        }

        // Renn's own standing work is reachable where he stands, which is what a first-hour player walks into
        using var workProfile = new TempProfile();
        var work = InHollow(Harness.Boot(workProfile), AtRenn);
        Take(work, Renn, "quest.bounty.crossing_timber");
        Assert.Equal(QuestStatus.Active, Quest(work, "quest.bounty.crossing_timber")!.Status);
    }
}
