using UNNAMED.Domain.Social;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// GEN-06 in the world: at a midnight two of a settlement's free adventurers leave for a ground it really works, are away for days, and come
/// home with coin, goods and hurts the world takes through its own doors - purses, the market's own trade, the injury rule, and news for the
/// away sheet. The fixture keeps the region's own spawners (a party needs a ground to work) and reads the arena's bus, because a simulation
/// publishes to the bus it was started with rather than the session's.
/// </summary>
public class ExpeditionTests
{
    private const long Day = 57_600;

    /// <summary>The world with the region's own spawners in place, from the adventurer suite's own lookout at the spawn.</summary>
    internal static Arena World(GameSession session) =>
        Arena.OpenCreatures(session, session.Setup, (30, 158), 90, Array.Empty<(string, double, double, string)>(),
            keepSpawns: true, origin: Harness.Region);

    private static long Stock(Simulation sim, string marketId, string itemId) =>
        sim.Markets.First(m => m.MarketId == marketId).Goods.First(g => g.ItemId == itemId).Stock;

    [Fact]
    public void APartyLeavesForAGroundItsSettlementWorks_AndComesHomeWithItsSpoils()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = World(session);
        var sim = arena.Simulation;
        var left = arena.Record<ExpeditionLeft>();
        var returned = arena.Record<ExpeditionReturned>();
        var purses = sim.Adventurers.ToDictionary(a => a.Key, a => a.Coin);

        // The doors the trip has to go through, read where it passes them: the market as it stood the midnight before, and as it stands when
        // the party walks in, before the town's own day has gone over the stock.
        sim.Advance(4 * Day);
        var marketId = "market.cairnwold";
        var stocksBefore = sim.Markets.First(m => m.MarketId == marketId).Goods.ToDictionary(g => g.ItemId, g => g.Stock);
        var atTheDoor = new Dictionary<string, long>(StringComparer.Ordinal);
        arena.Bus.Subscribe<ExpeditionReturned>(e =>
        {
            foreach (string item in e.Loot)
                atTheDoor[item] = Stock(sim, marketId, item);
        });

        sim.Advance(1 * Day);

        // A party went out, from a settlement with a ground, and it was two of them.
        Assert.Single(left);
        var trip = left[0];
        Assert.Contains(trip.Settlement, session.Setup.Adventurers!.Settlements.Keys);
        Assert.Contains(trip.AreaKey, session.Setup.Adventurers.Settlements[trip.Settlement].Grounds);
        Assert.Equal(2, trip.Party.Length);
        Assert.True(trip.ReturnDay > trip.LeftDay, "an expedition is away days, not hours");

        // They came home, on the day they were due, with coin and with goods.
        Assert.Single(returned);
        var home = returned[0];
        Assert.Equal(trip.Settlement, home.Settlement);
        Assert.Equal(trip.AreaKey, home.AreaKey);
        Assert.Equal(trip.Party.ToArray(), home.Party.ToArray());
        Assert.True(home.Coin > 0);
        Assert.NotEmpty(home.Loot);
        Assert.Contains("came back from", home.News);

        // The coin was paid to each of them: an equal share apiece, less the few coin a day out costs them (a salve, a piece of the ladder).
        long share = home.Coin / home.Party.Length;
        foreach (string key in home.Party)
        {
            long gain = sim.Adventurers.First(a => a.Key == key).Coin - purses[key];
            Assert.InRange(gain, share - 100, share + 100);
        }

        // The goods went onto the settlement's own market, by its own trade: every piece is on it when they walk in.
        foreach (string item in home.Loot)
            Assert.Equal(stocksBefore.GetValueOrDefault(item) + 1, atTheDoor[item]);
    }

    [Fact]
    public void TheHurtComeBackHurt_AndAreMendedByTheSameRuleAHuntsHurtIs()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = World(session);
        var sim = arena.Simulation;
        var returned = arena.Record<ExpeditionReturned>();
        var injured = arena.Record<AdventurerInjured>();

        sim.Advance(5 * Day);

        var home = Assert.Single(returned);
        Assert.NotEmpty(home.Injured);   // the fixture's trip hurts one of the party
        var rules = session.Setup.Adventurers!.Rules;
        long cameBack = session.Setup.Clock.Day(home.Tick);
        foreach (string key in home.Injured)
        {
            var told = injured.Single(i => i.Key == key && i.Tick == home.Tick);
            var agent = sim.World.Adventurers!.Agents[key];
            Assert.Equal(AdventurerCondition.Injured, agent.Condition);
            Assert.Equal(told.UntilDay, agent.InjuredUntilDay);
            // A hunt's hurt lasts what the rules say (injury_days), not a number written into the return.
            Assert.InRange(agent.InjuredUntilDay - cameBack, rules.InjuryMinDays, rules.InjuryMaxDays);
            Assert.True(agent.InjuredUntilDay > cameBack, "a hurt that lasts past the day they walked in");
        }
    }

    [Fact]
    public void APartyOnTheRoadDoesNothingInTown_AndTakesItsOwnDaysUpAgainWhenItIsHome()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = World(session);
        var sim = arena.Simulation;
        var left = arena.Record<ExpeditionLeft>();
        var home = arena.Record<ExpeditionReturned>();

        // The first midnight: the party sets out. What it had made of itself by then is the road's starting line.
        sim.Advance(Day);
        var trip = Assert.Single(left);
        Assert.True(trip.ReturnDay > trip.LeftDay + 1, "a trip worth tracing covers more than a day");
        var setOut = trip.Party.Select(k => sim.World.Adventurers!.Agents[k]).ToList();
        var stayed = sim.World.Adventurers!.Agents.Values.Where(a => a.Settlement == trip.Settlement && !trip.Party.Contains(a.Key)).ToList();
        Assert.Equal(2, stayed.Count);   // the two of the cairnwold this trip did not take

        for (long d = trip.LeftDay + 1; d < trip.ReturnDay; d++)
        {
            sim.Advance(Day);
            Assert.Equal(d, session.Setup.Clock.Day(sim.WorldTick));
            foreach (var was in setOut)
            {
                var now = sim.World.Adventurers!.Agents[was.Key];
                // On the road there is no day at the ground, no bounty off the board and nothing bought: the engine has them resting.
                Assert.Equal(AdventurerGoals.Rest, now.Plan.Goal);
                Assert.Equal(was.Kills, now.Kills);
                Assert.Equal(was.BountiesClaimed, now.BountiesClaimed);
                Assert.Equal(was.Coin, now.Coin);
                Assert.Equal(was.Gear.Count, now.Gear.Count);
                Assert.Equal(was.Level, now.Level);
            }
            // ...while the town they left goes on without them, so what is frozen is the party and not the world.
            Assert.Contains(stayed, was => sim.World.Adventurers!.Agents.TryGetValue(was.Key, out var now) && now.Kills > was.Kills);
        }

        // The midnight they are due home is not one of the trip's days: the engine plans them a day of their own again.
        sim.Advance(Day);
        var back = Assert.Single(home);
        Assert.Equal(trip.ReturnDay, session.Setup.Clock.Day(sim.WorldTick));
        Assert.Equal(trip.Party.ToArray(), back.Party.ToArray());
        foreach (var was in setOut)
        {
            var now = sim.World.Adventurers!.Agents[was.Key];
            Assert.Equal(trip.ReturnDay, now.Plan.Day);
            Assert.NotEqual(AdventurerGoals.Rest, now.Plan.Goal);
        }
    }

    [Fact]
    public void ANewPairGoesOutEachTime_SoEveryAdventurerSharesTheRoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = World(session);
        var sim = arena.Simulation;
        var left = arena.Record<ExpeditionLeft>();
        var everybody = sim.Adventurers.Where(a => a.Settlement == "cairnwold").Select(a => a.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();

        // Eight days hold two trips: the first leaves on the midnight of day 1 and is home on day 5, the next leaves on the 6th.
        sim.Advance(8 * Day);

        Assert.Equal(2, left.Count);
        // The first pair are the two who have never been out; the second are the two who have been home, not the pair just back.
        Assert.Equal(everybody.Take(2).ToArray(), left[0].Party.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(everybody.Skip(2).ToArray(), left[1].Party.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        // Which is to say all four have been sent.
        Assert.Equal(everybody.ToArray(), left.SelectMany(e => e.Party).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ATripSavedOnTheRoad_ComesHomeTheSameAfterALoad()
    {
        // A world that watched every midnight of the trip, as the yardstick.
        using var watched = new TempProfile();
        var straight = Harness.Boot(watched);
        var arena = World(straight);
        var straightHome = arena.Record<ExpeditionReturned>();
        arena.Simulation.Advance(5 * Day);
        var trip = Assert.Single(straightHome);

        // And the same world saved halfway, when the party is out and due back on the day 5 midnight.
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var onTheRoad = World(session);
        var sim = onTheRoad.Simulation;
        sim.Advance(3 * Day);
        var carried = sim.World.Adventurers!.Expeditions["cairnwold"];

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("road"), SaveDocuments.Capture(sim.World, sim.CaptureRecord(), session.Content, sim.WorldTick, 0));
        var loaded = store.Load(SaveSlots.Manual("road"), new LoadContext(session.Generator, session.Content, new Registry()));
        Assert.True(loaded.IsComplete);
        // The save carries the trip and what it is bringing: who is out, where, the day they are due, and the coin and goods they carry.
        var resumedTrip = Assert.Single(loaded.World.Adventurers!.Expeditions).Value;
        Assert.Equal("cairnwold", Assert.Single(loaded.World.Adventurers.Expeditions).Key);
        Assert.Equal(carried.Plan.AreaKey, resumedTrip.Plan.AreaKey);
        Assert.Equal(carried.Plan.Party.ToArray(), resumedTrip.Plan.Party.ToArray());
        Assert.Equal(carried.Plan.ReturnDay, resumedTrip.Plan.ReturnDay);
        Assert.Equal(carried.Outcome.Coin, resumedTrip.Outcome.Coin);
        Assert.Equal(carried.Outcome.Loot.ToArray(), resumedTrip.Outcome.Loot.ToArray());

        var bus = new EventBus();
        var home = new List<ExpeditionReturned>();
        bus.Subscribe<ExpeditionReturned>(home.Add);
        var resumed = Simulation.Start(sim.Setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, bus);
        resumed.Advance(2 * Day);

        var back = Assert.Single(home);
        Assert.Equal(trip.Coin, back.Coin);
        Assert.Equal(trip.Loot.ToArray(), back.Loot.ToArray());
        Assert.Equal(trip.Injured.ToArray(), back.Injured.ToArray());
        Assert.Equal(trip.Party.ToArray(), back.Party.ToArray());
        Assert.Equal(trip.News, back.News);
    }

    [Fact]
    public void TheSameSaveLivesTheSameExpeditionsAgain()
    {
        static string Run(TempProfile profile, out List<ExpeditionLeft> left, out List<ExpeditionReturned> home)
        {
            var session = Harness.Boot(profile);
            var arena = World(session);
            left = arena.Record<ExpeditionLeft>();
            home = arena.Record<ExpeditionReturned>();
            arena.Simulation.Advance(6 * Day);
            return AdventurerWorldTests.World(arena.Simulation);
        }

        using var one = new TempProfile();
        using var other = new TempProfile();
        string first = Run(one, out var leftOne, out var homeOne);
        string again = Run(other, out var leftTwo, out var homeTwo);

        Assert.Equal(first, again);
        Assert.NotEmpty(leftOne);
        Assert.Equal(leftOne.Count, leftTwo.Count);
        for (int i = 0; i < leftOne.Count; i++)
        {
            Assert.Equal(leftOne[i].Settlement, leftTwo[i].Settlement);
            Assert.Equal(leftOne[i].AreaKey, leftTwo[i].AreaKey);
            Assert.Equal(leftOne[i].LeftDay, leftTwo[i].LeftDay);
            Assert.Equal(leftOne[i].ReturnDay, leftTwo[i].ReturnDay);
            Assert.Equal(leftOne[i].Party.ToArray(), leftTwo[i].Party.ToArray());
        }
        Assert.Equal(homeOne.Count, homeTwo.Count);
        for (int i = 0; i < homeOne.Count; i++)
        {
            Assert.Equal(homeOne[i].Coin, homeTwo[i].Coin);
            Assert.Equal(homeOne[i].Loot.ToArray(), homeTwo[i].Loot.ToArray());
            Assert.Equal(homeOne[i].Injured.ToArray(), homeTwo[i].Injured.ToArray());
            Assert.Equal(homeOne[i].News, homeTwo[i].News);
        }
    }
}
