using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Gear sets and the further action bars over the game's own content: a set keeps the very items worn; a switch is a timed command (longer
/// in a fight), puts on only what is carried and says what is not, and leaves the equipment's own rules in force.
/// </summary>
public class GearSetTests
{
    private const string Bow = "item.weapon.hunting_bow", Salve = "item.consumable.salve_minor";
    private static readonly (double X, double Z) Ground = (120, 60);

    private static Arena Open(GameSession session, params InventoryEntry[] extra) =>
        Arena.OpenCreatures(session, session.Setup, Ground, 0, Array.Empty<(string, double, double, string)>(),
            r => new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(extra), r.Progression, r.FacingMdeg,
                r.Discoveries, r.Equipment, r.Currency, r.Effects));

    private static EntityId Id(Arena arena, string def) => arena.Simulation.Player.Inventory.First(e => e.DefId == def).ItemId;

    [Fact]
    public void ASetKeepsWhatIsWorn_AndSwitchingTakesTime_ThenPutsItOn()
    {
        using var profile = new TempProfile();
        var arena = Open(Harness.Boot(profile), Arena.Stack(Bow, 1));
        var worn = arena.Simulation.Player.Equipment;
        Assert.Null(arena.Submit(new SaveGearSetCommand(arena.Player, 1, "  Blade   and board ")));
        var set = Assert.Single(arena.Simulation.GearSets);
        Assert.Equal(("Blade and board", worn.Count), (set.Name, set.Items.Length));

        Assert.Null(arena.Submit(new EquipCommand(arena.Player, Id(arena, Bow))));
        Assert.Null(arena.Submit(new SwapGearSetCommand(arena.Player, 1)));
        var swap = arena.Simulation.GearSwap!;
        Assert.False(swap.InCombat);
        Assert.Equal(GearSetRules.SwapTicks(false, arena.Simulation.Setup.TickMilliseconds), swap.DueTick - swap.StartTick);
        // Not yet: the bow is still in the hands until the time is up.
        arena.Tick((int)(swap.DueTick - swap.StartTick) - 2);
        Assert.Equal(Id(arena, Bow), arena.Simulation.Player.Equipment[EquipSlot.MainHand]);
        var done = arena.Record<GearSwapped>();
        arena.Tick(4);
        Assert.Null(arena.Simulation.GearSwap);
        Assert.Single(done);
        Assert.Equal(worn, arena.Simulation.Player.Equipment);
    }

    [Fact]
    public void ASwitchInAFight_IsSlower()
    {
        Assert.True(GearSetRules.SwapTicks(true, 50) > GearSetRules.SwapTicks(false, 50));
        Assert.Equal(40, GearSetRules.SwapTicks(false, 50));
        Assert.Equal(80, GearSetRules.SwapTicks(true, 50));
    }

    [Fact]
    public void APieceNoLongerCarried_IsNeverConjured()
    {
        using var profile = new TempProfile();
        var arena = Open(Harness.Boot(profile), Arena.Stack(Bow, 1));
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, Id(arena, Bow))));
        Assert.Null(arena.Submit(new SaveGearSetCommand(arena.Player, 2, "Archer")));
        var bow = Id(arena, Bow);
        Assert.Null(arena.Submit(new UnequipCommand(arena.Player, EquipSlot.MainHand)));
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, bow.Value, ItemPlace.Carried, ItemPlace.Ground, 1)));
        // The set also wore other pieces, so it still changes the equipment, and what it cannot find stays missing.
        arena.Submit(new SwapGearSetCommand(arena.Player, 2));
        arena.Tick(100);
        Assert.DoesNotContain(arena.Simulation.Player.Inventory, e => e.DefId == Bow);
        Assert.DoesNotContain(EquipSlot.MainHand, arena.Simulation.Player.Equipment.Where(kv => kv.Value == bow).Select(kv => kv.Key));
    }

    [Fact]
    public void ASwitch_RefusesWhatIsNotThere_ADoubleSwitch_AndADeleteMidSwitch()
    {
        using var profile = new TempProfile();
        var arena = Open(Harness.Boot(profile), Arena.Stack(Bow, 1));
        Assert.Equal("there is no gear set 3", arena.Submit(new SwapGearSetCommand(arena.Player, 3)));
        Assert.Equal("there is no gear set 11", arena.Submit(new SaveGearSetCommand(arena.Player, 11, "x")));
        Assert.Equal("a gear set's name is 1 to 24 characters", arena.Submit(new SaveGearSetCommand(arena.Player, 1, "   ")));
        Assert.Null(arena.Submit(new SaveGearSetCommand(arena.Player, 1, "Plain")));
        Assert.Equal("Plain is already worn", arena.Submit(new SwapGearSetCommand(arena.Player, 1)));
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, Id(arena, Bow))));
        Assert.Null(arena.Submit(new SwapGearSetCommand(arena.Player, 1)));
        Assert.Equal("already changing gear", arena.Submit(new SwapGearSetCommand(arena.Player, 1)));
        Assert.Equal("that set is being put on", arena.Submit(new DeleteGearSetCommand(arena.Player, 1)));
        Assert.Null(arena.Submit(new CancelGearSwapCommand(arena.Player)));
        Assert.Null(arena.Simulation.GearSwap);
        Assert.Null(arena.Submit(new DeleteGearSetCommand(arena.Player, 1)));
        Assert.Empty(arena.Simulation.GearSets);
    }

    [Fact]
    public void AGearSetSlot_NeedsASavedSet_AndTheFurtherBarsAreTheirOwn()
    {
        using var profile = new TempProfile();
        var arena = Open(Harness.Boot(profile));
        arena.Tick();
        Assert.Equal("there is no gear set 1", arena.Submit(new AssignQuickSlotCommand(arena.Player, 8, QuickBar.GearSetId(1))));
        Assert.Null(arena.Submit(new SaveGearSetCommand(arena.Player, 1, "Plain")));
        Assert.Null(arena.Submit(new AssignQuickSlotCommand(arena.Player, 8, QuickBar.GearSetId(1))));
        Assert.Null(arena.Submit(new AssignQuickSlotCommand(arena.Player, 11, Salve, Bar: 2)));
        Assert.Equal(QuickBar.GearSetId(1), arena.Simulation.QuickBar[8]);
        Assert.Equal(Salve, arena.Simulation.ActionBars[2][11]);
        Assert.Equal("there is no action bar 7", arena.Submit(new AssignQuickSlotCommand(arena.Player, 0, Salve, Bar: 6)));
        Assert.Null(arena.Submit(new AssignQuickSlotCommand(arena.Player, 11, null, Bar: 2)));
        Assert.All(arena.Simulation.ActionBars[2], s => Assert.Null(s));
    }
}
