using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>M3b: carrying, containers, the ground and equipment, over the game's own content (PROTOTYPE.md §6.2's inventory rows).</summary>
public class ItemTests
{
    private const string Den = "container.den_cache";

    private static InventoryEntry Carried(Simulation simulation, string defId) => simulation.Player.Inventory.Single(e => e.DefId == defId);

    private static string? Rejection(GameSession session, GameCommand command)
    {
        var rejected = Harness.Record<CommandRejected>(session);
        session.Submit(command);
        Harness.Ticks(session, 1);
        return rejected.SingleOrDefault()?.Reason;
    }

    /// <summary>A world with changed item rules, stepped directly: the game's layout and content otherwise.</summary>
    private static (Simulation Simulation, EventBus Bus) Custom(GameSession session, Func<ItemSetup, ItemSetup> change)
    {
        var bus = new EventBus();
        var setup = session.Setup with { Items = change(session.Setup.Items) };
        var player = Simulation.NewCharacter(setup, EntityId.NewId(EntityKind.Character), "Wanderer", 7);
        return (Simulation.Start(setup, player, new WorldDelta(session.Generator, 42, new Registry()), 0, bus), bus);
    }

    private static void Walk(Simulation simulation, params (double X, double Z)[] path)
    {
        foreach (var (x, z) in path)
        {
            for (int i = 0; i < 3000; i++)
            {
                var body = simulation.Player.Body;
                double dx = x * 1000 - body.XMm, dz = z * 1000 - body.ZMm;
                if (Math.Sqrt(dx * dx + dz * dz) < 300)
                    break;
                simulation.Enqueue(new MoveCommand(simulation.PlayerId, Harness.Toward(dx, dz)));
                simulation.DrainCommands();
                simulation.Step();
            }
        }
        simulation.Enqueue(new MoveCommand(simulation.PlayerId, UNNAMED.Domain.Spatial.MoveIntent.Idle(0)));
        simulation.DrainCommands();
        simulation.Step();
    }

    // From the waystone east past the smithy to Charwood's north-west corner, and north into the den's mouth (M6's layout).
    private static readonly (double X, double Z)[] ToTheDenCache = { (60, 156), (90, 165), (104, 170), (112, 174), (112, 182), (112, 188.2) };

    /// <summary>The bow is found in the world, not carried from the start (content bible §11): a kit with one, for the tests that need it.</summary>
    private static ItemSetup WithBow(ItemSetup items) =>
        items with { StartingKit = items.StartingKit.Add(new StartingItem("item.weapon.hunting_bow", 1, false)) };

    [Fact]
    public void ANewCharacter_CarriesTheStartingKit_WithTheSwordEquipped()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var player = session.NewGame("Wanderer", seed: 42).Player;

        // Content bible §14: the sword in hand and a simple restorative; the bow is found at the cart, the spear is made.
        Assert.Equal(new[] { "item.consumable.salve_minor", "item.tool.water_flask", "item.weapon.rusted_sword" }, player.Inventory.Select(e => e.DefId).Order());
        Assert.Equal(Carried(session.Simulation!, "item.weapon.rusted_sword").ItemId, player.Equipment[EquipSlot.MainHand]);
        Assert.Equal(3_700, player.CarriedGrams);
        Assert.Equal(50_000, player.CarryLimitGrams);   // 30 kg + 2 kg per point of Might (10)
    }

    [Fact]
    public void EquippingTheBow_TakesBothHands_AndTheSwordStaysCarried()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (simulation, bus) = Custom(session, WithBow);
        var bow = Carried(simulation, "item.weapon.hunting_bow").ItemId;
        var sword = Carried(simulation, "item.weapon.rusted_sword").ItemId;
        var unequipped = new List<ItemUnequipped>();
        var rejected = new List<CommandRejected>();
        bus.Subscribe<ItemUnequipped>(unequipped.Add);
        bus.Subscribe<CommandRejected>(rejected.Add);

        simulation.Enqueue(new EquipCommand(simulation.PlayerId, bow));
        simulation.DrainCommands();
        Assert.Empty(rejected);

        Assert.Equal(bow, simulation.Player.Equipment[EquipSlot.MainHand]);
        Assert.False(simulation.Player.Equipment.ContainsKey(EquipSlot.OffHand));
        Assert.Equal(sword, Assert.Single(unequipped).Item);
        Assert.Contains(simulation.Player.Inventory, e => e.ItemId == sword);
    }

    [Fact]
    public void AnUnmetAttributeMinimum_IsRefused_AndTheSlotStaysAsItWas()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (simulation, bus) = Custom(session, items =>
        {
            items = WithBow(items);
            var bow = items.Catalog.Get("item.weapon.hunting_bow");
            var heavy = bow with { Requirements = bow.Requirements with { Attributes = bow.Requirements.Attributes.SetItem(CharacterAttribute.Might, 12) } };
            return items with { Catalog = new ItemCatalog(items.Catalog.Definitions.Values.Where(d => d.Id != bow.Id).Append(heavy)) };
        });
        var rejected = new List<CommandRejected>();
        bus.Subscribe<CommandRejected>(rejected.Add);
        var before = simulation.Player.Equipment;

        simulation.Enqueue(new EquipCommand(simulation.PlayerId, Carried(simulation, "item.weapon.hunting_bow").ItemId));
        simulation.DrainCommands();

        Assert.Contains("needs might 12; you have 10", Assert.Single(rejected).Reason);
        Assert.Equal(before, simulation.Player.Equipment);
    }

    [Fact]
    public void Unequipping_EmptiesTheSlot_AndAnEmptySlotIsRefused()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);

        Assert.Null(Rejection(session, new UnequipCommand(simulation.PlayerId, EquipSlot.MainHand)));
        Assert.Empty(simulation.Player.Equipment);
        Assert.Contains("nothing is equipped", Rejection(session, new UnequipCommand(simulation.PlayerId, EquipSlot.MainHand)));
    }

    [Fact]
    public void DroppingAndPickingUp_KeepsTheItemsIdentity_AndItLiesWhereTheBodyStood()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        var flask = Carried(simulation, "item.tool.water_flask").ItemId;
        var body = simulation.Player.Body;

        Assert.Null(Rejection(session, new MoveItemCommand(simulation.PlayerId, flask.Value, ItemPlace.Carried, ItemPlace.Ground, 1)));
        var lying = Assert.Single(simulation.WorldItems);
        Assert.Equal((flask, 1), (lying.Id, lying.Count));
        Assert.InRange(Math.Abs(lying.XMm - body.XMm), 0, 10);
        Assert.InRange(Math.Abs(lying.ZMm - body.ZMm), 0, 10);
        Assert.DoesNotContain(simulation.Player.Inventory, e => e.ItemId == flask);

        Assert.Null(Rejection(session, new MoveItemCommand(simulation.PlayerId, flask.Value, ItemPlace.Ground, ItemPlace.Carried, 1)));
        Assert.Empty(simulation.WorldItems);
        Assert.Contains(simulation.Player.Inventory, e => e.ItemId == flask);
    }

    [Fact]
    public void AnEquippedItem_CannotBeDropped_AndTheRefusalChangesNothing()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        string before = simulation.StateDigest();
        var sword = Carried(simulation, "item.weapon.rusted_sword").ItemId;

        Assert.Contains("unequip", Rejection(session, new MoveItemCommand(simulation.PlayerId, sword.Value, ItemPlace.Carried, ItemPlace.Ground, 1)));
        Assert.Empty(simulation.WorldItems);
        Assert.Contains(simulation.Player.Inventory, e => e.ItemId == sword);
        Assert.NotEqual(before, simulation.StateDigest());   // one tick passed ...
        Assert.Equal(sword, simulation.Player.Equipment[EquipSlot.MainHand]);   // ... and nothing else changed
    }

    [Fact]
    public void AQuestItem_CannotBeDropped()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (simulation, bus) = Custom(session, items => items with { StartingKit = items.StartingKit.Add(new StartingItem("item.quest.halda_token", 1, false)) });
        var rejected = new List<CommandRejected>();
        bus.Subscribe<CommandRejected>(rejected.Add);

        simulation.Enqueue(new MoveItemCommand(simulation.PlayerId, Carried(simulation, "item.quest.halda_token").ItemId.Value, ItemPlace.Carried, ItemPlace.Ground, 1));
        simulation.DrainCommands();

        Assert.Contains("cannot be dropped", Assert.Single(rejected).Reason);
    }

    [Fact]
    public void PickingUpFromAfar_IsRefused()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        var flask = Carried(simulation, "item.tool.water_flask").ItemId;
        session.Submit(new MoveItemCommand(simulation.PlayerId, flask.Value, ItemPlace.Carried, ItemPlace.Ground, 1));
        Assert.True(Harness.WalkPath(session, (30, 140)));

        Assert.Contains("out of reach", Rejection(session, new MoveItemCommand(simulation.PlayerId, flask.Value, ItemPlace.Ground, ItemPlace.Carried, 1)));
    }

    [Fact]
    public void TheDenCache_HoldsItsLootTable_UntilTouched_ThenEveryStackInItHasAnIdentity()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        Assert.True(Harness.WalkPath(session, ToTheDenCache));

        var untouched = simulation.Containers.Single(c => c.Site.Key == Den);
        Assert.Null(untouched.Id);
        Assert.Equal(new[] { ("item.ammo.arrow_rough", 12), ("item.consumable.salve_minor", 2), ("item.material.iron_ingot", 3), ("item.quest.halda_token", 1) },
            untouched.Items.Select(i => (i.DefId, i.Count)));
        Assert.Null(simulation.World.Container(Den));

        // Take five arrows: the cache changes for the first time, so it and everything in it get identities.
        Assert.Null(Rejection(session, new MoveItemCommand(simulation.PlayerId, untouched.Items[0].Ref, ItemPlace.In(Den), ItemPlace.Carried, 5)));
        var touched = simulation.Containers.Single(c => c.Site.Key == Den);
        Assert.NotNull(touched.Id);
        Assert.Equal(EntityKind.Container, touched.Id!.Kind);
        Assert.Equal(new[] { ("item.ammo.arrow_rough", 7), ("item.consumable.salve_minor", 2), ("item.material.iron_ingot", 3), ("item.quest.halda_token", 1) },
            touched.Items.Select(i => (i.DefId, i.Count)));
        Assert.Equal(5, Carried(simulation, "item.ammo.arrow_rough").Count);

        // A whole stack keeps its identity when it moves.
        var ingots = touched.Items.Single(i => i.DefId == "item.material.iron_ingot");
        Assert.Null(Rejection(session, new MoveItemCommand(simulation.PlayerId, ingots.Ref, ItemPlace.In(Den), ItemPlace.Carried, 3)));
        Assert.Equal(ingots.Ref, Carried(simulation, "item.material.iron_ingot").ItemId.Value);

        // Two arrows put back merge into the cache's stack.
        Assert.Null(Rejection(session, new MoveItemCommand(simulation.PlayerId, Carried(simulation, "item.ammo.arrow_rough").ItemId.Value,
            ItemPlace.Carried, ItemPlace.In(Den), 2)));
        Assert.Equal(9, simulation.Containers.Single(c => c.Site.Key == Den).Items.Single(i => i.DefId == "item.ammo.arrow_rough").Count);
        Assert.Equal(3, Carried(simulation, "item.ammo.arrow_rough").Count);
    }

    [Fact]
    public void TheCachesContents_AreTheSameInEveryWorld_UntilTouched()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var first = session.NewGame("A", seed: 1).Containers.Single(c => c.Site.Key == Den).Items;
        var second = session.NewGame("B", seed: 2).Containers.Single(c => c.Site.Key == Den).Items;
        Assert.Equal(first.AsEnumerable(), second.AsEnumerable());   // a static table: no randomness to vary
    }

    [Fact]
    public void TooHeavy_IsRefused_AndTheRefusalTouchesNothing()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A limit of exactly what the starting kit weighs: anything more is too heavy.
        var (simulation, bus) = Custom(session, items => items with { Inventory = items.Inventory with { CarryBaseGrams = 3_700, CarryGramsPerMight = 0 } });
        var rejected = new List<CommandRejected>();
        bus.Subscribe<CommandRejected>(rejected.Add);
        Walk(simulation, ToTheDenCache);
        var ingots = simulation.Containers.Single(c => c.Site.Key == Den).Items.Single(i => i.DefId == "item.material.iron_ingot");

        simulation.Enqueue(new MoveItemCommand(simulation.PlayerId, ingots.Ref, ItemPlace.In(Den), ItemPlace.Carried, 1));
        simulation.DrainCommands();

        Assert.Contains("too heavy", Assert.Single(rejected).Reason);
        Assert.Equal(3, simulation.Player.Inventory.Length);
        Assert.Null(simulation.World.Container(Den));   // the refusal did not even give the cache its identities
    }

    [Fact]
    public void NoFreeStackSlot_IsRefused()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (simulation, bus) = Custom(session, items => items with { Inventory = items.Inventory with { StackSlots = 3 } });
        var rejected = new List<CommandRejected>();
        bus.Subscribe<CommandRejected>(rejected.Add);
        Walk(simulation, ToTheDenCache);

        simulation.Enqueue(new MoveItemCommand(simulation.PlayerId, $"{Den}#00", ItemPlace.In(Den), ItemPlace.Carried, 1));
        simulation.DrainCommands();

        Assert.Contains("no room", Assert.Single(rejected).Reason);
    }

    [Fact]
    public void SplittingAndMerging_WithinTheInventory()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        Assert.True(Harness.WalkPath(session, ToTheDenCache));
        session.Submit(new MoveItemCommand(simulation.PlayerId, $"{Den}#00", ItemPlace.In(Den), ItemPlace.Carried, 12));
        Harness.Ticks(session, 1);
        var arrows = Carried(simulation, "item.ammo.arrow_rough");

        Assert.Null(Rejection(session, new MoveItemCommand(simulation.PlayerId, arrows.ItemId.Value, ItemPlace.Carried, ItemPlace.Carried, 4)));
        var stacks = simulation.Player.Inventory.Where(e => e.DefId == "item.ammo.arrow_rough").ToList();
        Assert.Equal(new[] { 4, 8 }, stacks.Select(s => s.Count).Order());
        Assert.Contains(stacks, s => s.ItemId == arrows.ItemId && s.Count == 8);

        var split = stacks.Single(s => s.ItemId != arrows.ItemId);
        Assert.Null(Rejection(session, new MoveItemCommand(simulation.PlayerId, split.ItemId.Value, ItemPlace.Carried, ItemPlace.Carried, 4)));
        Assert.Equal(12, Carried(simulation, "item.ammo.arrow_rough").Count);
        Assert.Equal(arrows.ItemId, Carried(simulation, "item.ammo.arrow_rough").ItemId);
    }

    [Fact]
    public void ItemsEquipmentAndTheCache_SurviveSaveAndLoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        session.Submit(new UnequipCommand(simulation.PlayerId, EquipSlot.MainHand));   // equipment changed from the kit's
        session.Submit(new MoveItemCommand(simulation.PlayerId, Carried(simulation, "item.tool.water_flask").ItemId.Value, ItemPlace.Carried, ItemPlace.Ground, 1));
        Assert.True(Harness.WalkPath(session, ToTheDenCache));
        session.Submit(new MoveItemCommand(simulation.PlayerId, $"{Den}#02", ItemPlace.In(Den), ItemPlace.Carried, 3));
        Harness.Ticks(session, 1);
        session.Save(SaveSlots.Quick);

        var reloaded = Harness.Boot(profile);
        var result = reloaded.Load(SaveSlots.Quick);

        Assert.True(result.IsComplete);
        Assert.Equal(simulation.StateDigest(), reloaded.Simulation!.StateDigest());
        Assert.Equal(simulation.Player.Equipment, reloaded.Simulation.Player.Equipment);
        Assert.Equal(simulation.WorldItems.AsEnumerable(), reloaded.Simulation.WorldItems.AsEnumerable());
        Assert.Equal(simulation.Containers.Single(c => c.Site.Key == Den).Items.AsEnumerable(),
            reloaded.Simulation.Containers.Single(c => c.Site.Key == Den).Items.AsEnumerable());
        Assert.Equal(simulation.Containers.Single(c => c.Site.Key == Den).Id, reloaded.Simulation.Containers.Single(c => c.Site.Key == Den).Id);
    }
}
