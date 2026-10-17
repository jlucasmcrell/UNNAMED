using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>Schema 24 (WP3b): a piece's wear is kept by a save - carried and equipped, and lying on the ground - and the world plays on the same.</summary>
public class DurabilitySaveTests
{
    private static readonly (double X, double Z) Open = (120, 60);

    [Fact]
    public void Wear_IsKeptByASave_CarriedAndOnTheGround()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var sword = new InventoryEntry(EntityId.NewId(EntityKind.Item), "item.weapon.rusted_sword", 1) { Quality = Quality.Crude, Wear = new ItemWear(200, Quality.Standard) };
        var spear = new InventoryEntry(EntityId.NewId(EntityKind.Item), "item.weapon.march_spear", 1) { Quality = Quality.Fine, Wear = new ItemWear(37, Quality.Fine) };
        var arena = Arena.OpenCreatures(session, session.Setup, Open, 0, Array.Empty<(string, double, double, string)>(), r =>
            new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Where(e => e.DefId != sword.DefId).Append(sword).Append(spear),
                r.Progression, r.FacingMdeg, r.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, sword.ItemId) }, r.Currency, r.Effects));
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, spear.ItemId.Value, ItemPlace.Carried, ItemPlace.Ground, 1)));

        var store = new SaveStore(profile.Root);
        long tick = arena.Simulation.WorldTick;
        store.Save(SaveSlots.Manual("worn"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content, tick, 0));
        var loaded = store.Load(SaveSlots.Manual("worn"), new LoadContext(session.Generator, session.Content, new Registry()));
        Assert.True(loaded.IsComplete);
        var resumed = Simulation.Start(arena.Simulation.Setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, new EventBus());

        Assert.Equal(arena.Simulation.StateDigest(), resumed.StateDigest());
        var carried = resumed.Player.Inventory.Single(e => e.ItemId == sword.ItemId);
        Assert.Equal((Quality.Crude, new ItemWear(200, Quality.Standard)), (carried.Quality, carried.Wear));
        Assert.Equal(5, resumed.Combat.Weapon.DamageMin);   // still worn out: still crude
        var lying = resumed.WorldItems.Single(i => i.Id == spear.ItemId);
        Assert.Equal((Quality.Fine, new ItemWear(37, Quality.Fine)), (lying.Quality, lying.Wear));
    }
}
