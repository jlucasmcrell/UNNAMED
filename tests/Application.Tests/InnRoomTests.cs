using UNNAMED.Domain.Social;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Spec 15 (owner finding 15): a room at the inn - the keeper's own town's inn, five coin for the night, and the sleep is the world's own
/// advance to the hour the character asks for, so the adventurers, the markets and the boards come out of it as they do from an absence.
/// </summary>
public class InnRoomTests
{
    private const long Day = 57_600;
    private const string Renn = "npc.ashen_hollow.renn_vale";

    [Fact]
    public void ARoom_IsFiveCoin_AndTheSleepIsTheWorldsOwnAdvanceToTheHourAskedFor()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AdventurerWorldTests.AtTheInn(session, coin: 20);
        var sim = arena.Simulation;
        var slept = arena.Record<RoomRented>();
        long before = sim.WorldTick;
        long coin = sim.Player.Currency;
        // The world starts at 07:30; the sixth hour of the day is tomorrow morning.
        Assert.Null(sim.Submit(new RentRoom(Renn, 6)));
        var room = Assert.Single(slept);
        Assert.Equal((Renn, "ashen_hollow", 6, 5L), (room.KeeperId, room.Settlement, room.Hour, room.Coin));
        Assert.Equal(coin - 5, sim.Player.Currency);
        // Up at the sixth hour, and the world got there by its own advance, not by a tick.
        Assert.Equal(6, session.Setup.Clock.MinuteOfDay(sim.WorldTick) / 60);
        Assert.Equal(room.WakeTick, sim.WorldTick);
        Assert.True(sim.WorldTick > before + 20 * 2_400);   // 07:30 to the sixth hour is most of a day
        // A second room asked for at the same hour is a whole day's wait: the hour has to be ahead.
        Assert.Null(sim.Submit(new RentRoom(Renn, 6)));
        Assert.Equal(room.WakeTick + Day, slept[1].WakeTick);
    }

    [Fact]
    public void SomeoneWithNoInn_AndAPurseTooLight_LetsNoRoom()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AdventurerWorldTests.AtTheInn(session, coin: 3);
        var sim = arena.Simulation;
        var slept = arena.Record<RoomRented>();
        Assert.Equal("not enough coin: the room is 5, 3 carried", sim.Submit(new RentRoom(Renn, 6)));
        Assert.Equal("no one keeps a room here", sim.Submit(new RentRoom("npc.ashen_hollow.kera_voss", 6)));
        Assert.Equal("a room is let until an hour of the day", sim.Submit(new RentRoom(Renn, 24)));
        Assert.Empty(slept);
        Assert.Equal(3, sim.Player.Currency);
    }
}
