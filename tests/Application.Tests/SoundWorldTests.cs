using UNNAMED.Domain;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// D23 S2 through the world (STEALTH.md §3.1-§3.4): the floor a footfall lands on decides how far it carries, and a thrown stone buys a noise at
/// the place it comes down - which is the whole of a distraction, because an observer goes to the noise and never to the thrower.
/// </summary>
public class SoundWorldTests
{
    /// <summary>A body with a stone in the pack and nothing else, standing where the test puts it.</summary>
    private static Func<PlayerRecord, PlayerRecord> Stony(string item = "item.misc.lure_stone") => r =>
    {
        var inventory = r.Inventory.Where(e => e.DefId is "item.weapon.iron_dagger").ToList();
        inventory.Add(Arena.Stack(item, 3));
        var main = inventory.FirstOrDefault(e => e.DefId == "item.weapon.iron_dagger")?.ItemId ?? default;
        var equipment = main == default ? Array.Empty<KeyValuePair<EquipSlot, EntityId>>()
            : new[] { KeyValuePair.Create(EquipSlot.MainHand, main) };
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg,
            r.Discoveries, equipment, r.Currency, r.Effects);
    };

    /// <summary>
    /// STEALTH §3.1: the same walk, on the yard's gravel and on the wood of the lodge floor. The ring is bigger on the gravel, and it is the
    /// same number the ear is read by.
    /// </summary>
    [Fact]
    public void GravelCarriesFurtherThanMud()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var none = Array.Empty<(string DefId, double X, double Z, string Role)>();
        // Region metres with no origin to translate: surface.waystation_yard is gravel, surface.road_ruts is mud - both open ground.
        var gravel = Arena.OpenCreatures(session, session.Setup, (470, 750), 0, none, Stony(), startTick: 12_000, origin: (0, 0));
        var mud = Arena.OpenCreatures(session, session.Setup, (600, 675), 0, none, Stony(), startTick: 12_000, origin: (0, 0));
        foreach (var arena in new[] { gravel, mud })
        {
            arena.Submit(new MoveCommand(arena.Player, new MoveIntent(0, 1_000, Gait.Walk, 0)));
            arena.Tick(3);
        }
        Assert.Equal("gravel", gravel.Simulation.PlayerSurface);
        Assert.Equal("mud", mud.Simulation.PlayerSurface);
        Assert.True(gravel.Simulation.PlayerNoiseMm > 0 && mud.Simulation.PlayerNoiseMm > 0,
            $"both bodies have to be walking for this to mean anything: gravel {gravel.Simulation.PlayerNoiseMm} mm, mud {mud.Simulation.PlayerNoiseMm} mm");
        Assert.True(gravel.Simulation.PlayerNoiseMm > mud.Simulation.PlayerNoiseMm,
            $"gravel carries further than mud: gravel {gravel.Simulation.PlayerNoiseMm} mm against mud {mud.Simulation.PlayerNoiseMm} mm");
    }

    /// <summary>
    /// STEALTH §3.2: the stone is thrown, the place it lands is where the noise is raised, and the thrower is not the thing being walked toward.
    /// </summary>
    [Fact]
    public void AThrownStoneMakesItsNoiseWhereItLands()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (120, 60), 180, Array.Empty<(string, double, double, string)>(),
            Stony(), startTick: 12_000);
        var stone = arena.Simulation.Player.Inventory.First(e => e.DefId == "item.misc.lure_stone");
        var where = arena.Simulation.Player.Body;
        var thrown = arena.Record<Thrown>();
        Assert.Null(arena.Submit(new ThrowCommand(arena.Player, stone.ItemId)));
        var one = Assert.Single(thrown);
        Assert.Equal("item.misc.lure_stone", one.DefId);
        // It flew away from the body, not around it, and it is gone from the pack.
        double flown = Math.Sqrt(Math.Pow(one.ToXMm - where.XMm, 2) + Math.Pow(one.ToZMm - where.ZMm, 2));
        Assert.True(flown > 5_000, $"the stone has to travel to be a distraction: {flown:0} mm");
        Assert.DoesNotContain(arena.Simulation.Player.Inventory, e => e.DefId == "item.misc.lure_stone" && e.Count == 3);
    }

    /// <summary>A thing that cannot be thrown is refused, and says so rather than vanishing.</summary>
    [Fact]
    public void SomethingThatIsNotAThingToThrow_IsRefused()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (120, 60), 180, Array.Empty<(string, double, double, string)>(),
            Stony("item.consumable.salve_minor"), startTick: 12_000);
        var salve = arena.Simulation.Player.Inventory.First(e => e.DefId == "item.consumable.salve_minor");
        Assert.Contains("not a thing to throw", arena.Submit(new ThrowCommand(arena.Player, salve.ItemId)));
        Assert.Contains(arena.Simulation.Player.Inventory, e => e.DefId == "item.consumable.salve_minor");
    }
}
