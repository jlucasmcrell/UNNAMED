using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Items;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// GEN-09's accessory slots in the game: a belt, a shoulder piece and a ring are worn in slots of their own, what they give counts on the
/// body region they guard, and a save keeps them (a character wearing a belt is the same character after a load, digest and all).
/// </summary>
public class AccessoryGearTests
{
    private const string Tavar = "npc.ashen_hollow.tavar_orr";
    private const string Belt = "item.armor.leather_belt";
    private const string Shoulders = "item.armor.hide_shoulder_guards";
    private const string Ring = "item.trinket.iron_band_ring";
    private static readonly (double X, double Z) AtStand = (181.5, 137);

    private static InventoryEntry Stack(string defId, int count = 1) => new(EntityId.NewId(EntityKind.Item), defId, count);

    private static PlayerRecord Carrying(PlayerRecord r, params InventoryEntry[] extra) =>
        new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(extra), r.Progression, r.FacingMdeg, r.Discoveries, r.Equipment, 0,
            r.Effects, r.Relationships, r.Conversations, r.Quests, r.Companions) { PartySheets = r.PartySheets };

    private static CompanionRecord Joined(double x, double z) =>
        new(Tavar, CompanionOrder.Follow, CompanionCondition.Up, Harness.InRegion(x, z).XMm, Harness.InRegion(x, z).ZMm, 90_000, 100);

    private static Arena Open(GameSession session, Func<PlayerRecord, PlayerRecord> change) =>
        Arena.OpenCreatures(session, session.Setup, AtStand, 0, Array.Empty<(string, double, double, string)>(), change);

    private static CompanionView Member(Arena arena) => arena.Simulation.Companions.Single(c => c.NpcId == Tavar);

    [Fact]
    public void TheThreeAccessories_GoIntoTheirOwnSlots_AndAreStillWornAfterALoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (belt, shoulders, ring) = (Stack(Belt), Stack(Shoulders), Stack(Ring));
        var arena = Open(session, r => Carrying(r, belt, shoulders, ring));

        Assert.Null(arena.Submit(new EquipCommand(arena.Player, belt.ItemId)));
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, shoulders.ItemId)));
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, ring.ItemId)));
        var worn = arena.Simulation.Player.Equipment;
        Assert.Equal(belt.ItemId, worn[EquipSlot.Belt]);
        Assert.Equal(shoulders.ItemId, worn[EquipSlot.Shoulders]);
        Assert.Equal(ring.ItemId, worn[EquipSlot.Ring]);
        var digest = arena.Simulation.StateDigest();

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("accessories"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        var loaded = store.Load(SaveSlots.Manual("accessories"), new LoadContext(session.Generator, session.Content, new Registry()));
        Assert.True(loaded.IsComplete);
        var resumed = Simulation.Start(arena.Simulation.Setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, new EventBus());

        Assert.Equal(belt.ItemId, resumed.Player.Equipment[EquipSlot.Belt]);
        Assert.Equal(shoulders.ItemId, resumed.Player.Equipment[EquipSlot.Shoulders]);
        Assert.Equal(ring.ItemId, resumed.Player.Equipment[EquipSlot.Ring]);
        Assert.Equal(digest, resumed.StateDigest());
    }

    /// <summary>A slot takes the item whose slot it is: a belt cannot be worn on the hand, and a ring cannot be worn round the waist.</summary>
    [Fact]
    public void AnAccessory_IsWornOnlyInItsOwnSlot()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var belt = Stack(Belt);
        var arena = Open(session, r => Carrying(r, belt));

        Assert.DoesNotContain(EquipSlot.Ring, arena.Simulation.Player.Equipment.Keys);
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, belt.ItemId)));
        Assert.Single(arena.Simulation.Player.Equipment, worn => worn.Value == belt.ItemId);
        Assert.Equal(EquipSlot.Belt, arena.Simulation.Player.Equipment.Single(worn => worn.Value == belt.ItemId).Key);
    }

    /// <summary>
    /// What a hireling wears in the new slots counts on the region each guards, read the way the character's is: the belt and the shoulder
    /// guards on the torso, the ring on the limbs.
    /// </summary>
    [Fact]
    public void AHirelingsAccessories_AddTheirArmour_OnTheRegionEachSlotGuards()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (belt, shoulders, ring) = (Stack(Belt), Stack(Shoulders), Stack(Ring));
        var arena = Open(session, r => Carrying(r, belt, shoulders, ring).WithCompanions(new[] { Joined(181.5, 138) }));
        var before = Member(arena).Armor;

        foreach (var item in new[] { belt, shoulders })
            Assert.Null(arena.Submit(new GiveGearCommand(arena.Player, Tavar, item.ItemId, Loan: true)));
        var wore = Member(arena).Armor;
        Assert.Equal(before[BodyRegion.Torso] + 1 + 2, wore[BodyRegion.Torso]);
        Assert.Equal(before.GetValueOrDefault(BodyRegion.Limbs), wore.GetValueOrDefault(BodyRegion.Limbs));

        Assert.Null(arena.Submit(new GiveGearCommand(arena.Player, Tavar, ring.ItemId, Loan: true)));
        var later = Member(arena).Armor;
        Assert.Equal(wore[BodyRegion.Torso], later[BodyRegion.Torso]);
        Assert.Equal(wore.GetValueOrDefault(BodyRegion.Limbs) + 1, later[BodyRegion.Limbs]);
    }
}
