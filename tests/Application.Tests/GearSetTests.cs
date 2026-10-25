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
    private const string Cap = "item.armor.hide_cap", Bow = "item.weapon.hunting_bow", Salve = "item.consumable.salve_minor";
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

    private static EntityId Worn(Arena arena, EquipSlot slot) => arena.Simulation.Player.Equipment[slot];

    [Fact]
    public void InAFight_ASwitchChangesTheHandsOnly_ArmourStaysAndIsNotQueued_AndTheTimeIsTheCombatOne()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Ground, 0, new[] { (Arena.Wolf, Ground.X + 1.3, Ground.Z, "pack_hunter") },
            r => new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(new[] { Arena.Stack(Bow, 1), Arena.Stack(Cap, 1) }), r.Progression, r.FacingMdeg,
                r.Discoveries, r.Equipment, r.Currency, r.Effects));
        var sword = Worn(arena, EquipSlot.MainHand);
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, Id(arena, Cap))));
        arena.Tick();
        var head = Worn(arena, EquipSlot.Head);
        Assert.Null(arena.Submit(new SaveGearSetCommand(arena.Player, 1, "Blade and cap")));
        Assert.Null(arena.Submit(new UnequipCommand(arena.Player, EquipSlot.Head)));
        var done = arena.Record<GearSwapped>();
        arena.Tick();
        arena.Face(arena.Creature());
        arena.Tick();
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        for (int i = 0; i < 300 && !arena.Simulation.Creatures.Any(c => c.Hostile); i++)
            arena.Tick();
        Assert.Contains(arena.Simulation.Creatures, c => c.Hostile);
        arena.Tick(30);
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, Id(arena, Bow))));
        Assert.Equal("cannot change armour in combat", arena.Submit(new EquipCommand(arena.Player, head)));
        Assert.Contains(arena.Simulation.Creatures, c => c.Hostile);

        Assert.Null(arena.Submit(new SwapGearSetCommand(arena.Player, 1)));
        var swap = arena.Simulation.GearSwap!;
        Assert.True(swap.InCombat);
        Assert.Equal(GearSetRules.SwapTicks(true, arena.Simulation.Setup.TickMilliseconds), swap.DueTick - swap.StartTick);
        arena.Tick((int)(swap.DueTick - swap.StartTick) + 2);

        var swapped = Assert.Single(done);
        Assert.True(swapped.ArmourKept);
        Assert.Equal(sword, Worn(arena, EquipSlot.MainHand));
        Assert.False(arena.Simulation.Player.Equipment.ContainsKey(EquipSlot.Head), "the cap was put on in a fight");
        Assert.DoesNotContain(EquipSlot.Head, swapped.Changed);
        // Not queued: nothing puts it on afterwards either.
        arena.Tick(80);
        Assert.False(arena.Simulation.Player.Equipment.ContainsKey(EquipSlot.Head));
        Assert.Contains(arena.Simulation.Player.Inventory, e => e.ItemId == head);
    }

    [Fact]
    public void OutOfAFight_TheSameSwitchPutsTheArmourOnToo()
    {
        using var profile = new TempProfile();
        var arena = Open(Harness.Boot(profile), Arena.Stack(Bow, 1), Arena.Stack(Cap, 1));
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, Id(arena, Cap))));
        arena.Tick();
        var head = Worn(arena, EquipSlot.Head);
        Assert.Null(arena.Submit(new SaveGearSetCommand(arena.Player, 1, "Blade and cap")));
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, Id(arena, Bow))));
        Assert.Null(arena.Submit(new UnequipCommand(arena.Player, EquipSlot.Head)));
        var done = arena.Record<GearSwapped>();
        Assert.Null(arena.Submit(new SwapGearSetCommand(arena.Player, 1)));
        Assert.False(arena.Simulation.GearSwap!.InCombat);
        arena.Tick(60);
        Assert.False(Assert.Single(done).ArmourKept);
        Assert.Equal(head, Worn(arena, EquipSlot.Head));
    }

    [Fact]
    public void TheMainBar_FollowsTheWeaponInHand_EachFamilyKeepsItsOwnLayout_AndAFirstTimeFamilyCopiesTheBar()
    {
        using var profile = new TempProfile();
        var arena = Open(Harness.Boot(profile), Arena.Stack(Bow, 1));
        arena.Tick();
        string bladeFamily = arena.Simulation.HeldFamily;
        Assert.True(WeaponFamilies.IsValid(bladeFamily), bladeFamily);
        Assert.Equal(arena.Simulation.QuickBar, arena.Simulation.FamilyBars[bladeFamily]);

        Assert.Null(arena.Submit(new AssignQuickSlotCommand(arena.Player, 10, Salve)));
        var blade = arena.Simulation.QuickBar;
        Assert.Equal(Salve, blade[10]);

        // A bow in hand: the archery layout, a copy of what showed - until it is edited.
        var sword = Worn(arena, EquipSlot.MainHand);
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, Id(arena, Bow))));
        arena.Tick();
        Assert.NotEqual(bladeFamily, arena.Simulation.HeldFamily);
        Assert.Equal(blade, arena.Simulation.QuickBar);
        Assert.Null(arena.Submit(new AssignQuickSlotCommand(arena.Player, 10, null)));
        Assert.Null(arena.Submit(new AssignQuickSlotCommand(arena.Player, 11, Salve)));
        var bow = arena.Simulation.QuickBar;
        Assert.Equal(Salve, bow[11]);
        Assert.Equal(bow, arena.Simulation.FamilyBars[arena.Simulation.HeldFamily]);

        // Bare hands: another first time, another copy.
        Assert.Null(arena.Submit(new UnequipCommand(arena.Player, EquipSlot.MainHand)));
        arena.Tick();
        Assert.Equal(WeaponFamilies.Unarmed, arena.Simulation.HeldFamily);
        Assert.Equal(bow, arena.Simulation.QuickBar);

        // Back to the sword: its own layout, untouched by the bow's edits; and the bow's is kept for next time.
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, sword)));
        arena.Tick();
        Assert.Equal(bladeFamily, arena.Simulation.HeldFamily);
        Assert.Equal(blade, arena.Simulation.QuickBar);
        Assert.Equal(bow, arena.Simulation.FamilyBars.Single(f => f.Key != bladeFamily && f.Key != WeaponFamilies.Unarmed).Value);
        Assert.Equal(3, arena.Simulation.FamilyBars.Count);
    }}
