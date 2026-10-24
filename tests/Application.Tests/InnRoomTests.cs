using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>Paid inn rest restores the character while the shared world clock keeps its own hours (DN8, 2026-10-24).</summary>
public class InnRoomTests
{
    private const string Renn = "npc.ashen_hollow.renn_vale";

    [Fact]
    public void ARoomCostsFiveCoin_RestoresHealthAndStamina_AndSkipsNoWorldTime()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AdventurerWorldTests.AtTheInn(session, coin: 20);
        var sim = arena.Simulation;
        Assert.Null(sim.Submit(new ChangePools(-25, -15)));
        Assert.True(sim.Combat.Health < sim.Combat.MaxHealth);
        Assert.True(sim.Combat.Stamina < sim.Combat.MaxStamina);
        var slept = arena.Record<RoomRented>();
        long before = sim.WorldTick;
        string world = AdventurerWorldTests.World(sim);

        Assert.Null(sim.Submit(new RentRoom(Renn)));
        var room = Assert.Single(slept);
        Assert.Equal((Renn, "ashen_hollow", 5L, before), (room.KeeperId, room.Settlement, room.Coin, room.Tick));
        Assert.Equal(15, sim.Player.Currency);
        Assert.Equal(sim.Combat.MaxHealth, sim.Combat.Health);
        Assert.Equal(sim.Combat.MaxStamina, sim.Combat.Stamina);
        Assert.Equal(before, sim.WorldTick);
        Assert.Equal(world, AdventurerWorldTests.World(sim));
        Assert.Null(sim.Submit(new RentRoom(Renn)));
        Assert.Equal(before, slept[1].Tick);
        Assert.Equal(before, sim.WorldTick);
        Assert.Equal(10, sim.Player.Currency);
    }

    [Fact]
    public void SomeoneWithNoInn_AndAPurseTooLight_LetsNoRoom()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AdventurerWorldTests.AtTheInn(session, coin: 3);
        var sim = arena.Simulation;
        var slept = arena.Record<RoomRented>();
        Assert.Equal("not enough coin: the room is 5, 3 carried", sim.Submit(new RentRoom(Renn)));
        Assert.Equal("no one keeps a room here", sim.Submit(new RentRoom("npc.ashen_hollow.kera_voss")));
        Assert.Empty(slept);
        Assert.Equal(3, sim.Player.Currency);
    }

    [Fact]
    public void FightingCannotUseAnInnAsInstantCombatHealing()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AdventurerWorldTests.AtTheInn(session, coin: 20);
        Assert.Null(arena.Submit(new BlockCommand(arena.Player, true)));
        Assert.Equal("you cannot rest while fighting or downed", arena.Simulation.Submit(new RentRoom(Renn)));
        Assert.Equal(20, arena.Simulation.Player.Currency);
    }
}

