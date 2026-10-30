using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Equipping the bow through the command a click submits (STEALTH.md section 7.1): a two-hander that needs Might takes both hands, and it has to be in
/// the main hand afterwards and be the weapon the shot uses. Three proof captures showed the sheet reading UNARMED and nothing in the logs said why,
/// so the same two steps the route performs - the item in the pack, an EquipCommand, a tick - are done here where a failure can be read.
/// </summary>
public class EquipFromThePackTests
{
    private const string Bow = "item.weapon.hunting_bow";
    private const string Dagger = "item.weapon.iron_dagger";

    /// <summary>A character strong enough for the bow, holding a bow and a dagger in the pack and nothing in their hands.</summary>
    private static Func<PlayerRecord, PlayerRecord> Carrying => r =>
    {
        var inventory = new List<InventoryEntry> { Arena.Stack(Bow, 1), Arena.Stack(Dagger, 1) };
        var progression = r.Progression with
        {
            Level = 10,
            Allocation = System.Collections.Immutable.ImmutableSortedDictionary.CreateRange(
                new[] { KeyValuePair.Create(CharacterAttribute.Might, 9) }),
        };
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, progression, r.FacingMdeg,
            r.Discoveries, Array.Empty<KeyValuePair<EquipSlot, EntityId>>(), r.Currency, r.Effects);
    };

    /// <summary>
    /// The dagger, then the bow: each takes the main hand in turn, and the bow is the weapon afterwards - which is what the route needs and what the
    /// stills said was not happening.
    /// </summary>
    [Fact]
    public void TheDagger_ThenTheBow_LeavesTheBowInHand()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (470, 745), 0, Array.Empty<(string, double, double, string)>(),
            Carrying, startTick: 12_000, origin: (0, 0));
        arena.Tick(2);

        var player = arena.Simulation.Player;
        var dagger = player.Inventory.Single(e => e.DefId == Dagger).ItemId;
        var bow = player.Inventory.Single(e => e.DefId == Bow).ItemId;

        arena.Submit(new EquipCommand(arena.Player, dagger));
        arena.Tick(2);
        Assert.Equal(dagger, arena.Simulation.Player.Equipment[EquipSlot.MainHand]);

        arena.Submit(new EquipCommand(arena.Player, bow));
        arena.Tick(2);
        Assert.Equal(bow, arena.Simulation.Player.Equipment[EquipSlot.MainHand]);
        Assert.Equal(Bow, arena.Simulation.Player.Inventory.Single(e => e.ItemId == arena.Simulation.Player.Equipment[EquipSlot.MainHand]).DefId);
    }
}