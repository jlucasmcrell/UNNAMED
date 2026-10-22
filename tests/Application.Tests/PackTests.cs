using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Organising the pack over the game's own content: bags as items equipped into four bag slots (slots, never strength), specialised bags that
/// take only their kind and are routed into, sorting, junk and locks, split and merge, and a stash that a station in its settlement works from.
/// Every move is a command; the same commands always lay the pack out the same way.
/// </summary>
public class PackTests
{
    private const string Satchel = "item.bag.satchel", Reagent = "item.bag.reagent_satchel", Quiver = "item.bag.quiver", Pouch = "item.bag.herb_pouch";
    private const string Arrow = "item.ammo.arrow_rough", Ashbloom = "item.material.herb_ashbloom", Ore = "item.material.iron_ore";
    private const string Wolf = "item.material.wolf_hide", Small = "item.material.small_hide", Cotton = "item.material.bog_cotton", Flask = "item.tool.water_flask";
    private const string Salve = "item.consumable.salve_minor", Sword = "item.weapon.rusted_sword";
    private const string Kera = "npc.ashen_hollow.kera_voss", PouchRecipe = "recipe.outfitting.herb_pouch";

    private static readonly (double X, double Z) Open = (120, 60);
    private static readonly (double X, double Z) AtChest = (38, 138.4);
    private static readonly (double X, double Z) AtStash = (40, 138.4);
    private static readonly (double X, double Z) AtBench = (38.5, 143);
    private static readonly (double X, double Z) AtKera = (60.3, 140.3);
    private const string Stash = "container.stash.waystation";

    /// <summary>A new character at a place, carrying more, and taught the herb pouch as Durn teaches it.</summary>
    private static Arena Start(GameSession session, (double X, double Z) at, params InventoryEntry[] extra) =>
        Arena.OpenCreatures(session, session.Setup, at, 0, Array.Empty<(string, double, double, string)>(),
            r => new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(extra), r.Progression, r.FacingMdeg, r.Discoveries,
                r.Equipment, 0, r.Effects, r.Relationships, r.Conversations)
                .WithProgression(r.Progression with
                {
                    Known = r.Progression.Known.SetItem(PouchRecipe, new KnownTechnique(LearningSource.Teacher, "npc.cairnwold.durn_skell", 0)),
                }));

    private static InventoryEntry Stack(string def, int count, int quality = Quality.Standard) =>
        new(EntityId.NewId(EntityKind.Item), def, count) { Quality = quality };

    private static EntityId Id(Arena arena, string def) => arena.Simulation.Player.Inventory.First(e => e.DefId == def).ItemId;

    private static InventoryEntry Entry(Arena arena, string def) => arena.Simulation.Player.Inventory.First(e => e.DefId == def);

    private static PackContainerView Bag(Arena arena, int bag) => arena.Simulation.Pack.Containers.Single(c => c.Bag == bag);

    private static int Count(Arena arena, string def) => arena.Simulation.Player.Inventory.Where(e => e.DefId == def).Sum(e => e.Count);

    private static string[] Order(Arena arena, int bag) => Bag(arena, bag).Stacks.Select(s => s.DefId).ToArray();

    private static ItemDefinition Def(Arena arena, string def) => arena.Simulation.Setup.Items.Catalog.Get(def);

    [Fact]
    public void WornItems_LeavePackSlotsButKeepTheirWeight_AndFullPackUnequipIsRefused()
    {
        using var profile=new TempProfile();
        var arena=Start(Harness.Boot(profile),AtChest,Enumerable.Range(0,20).Select(_=>Stack(Flask,1)).Append(Stack("item.armor.hide_vest",1)).ToArray());
        var vest=Id(arena,"item.armor.hide_vest");var before=arena.Simulation.Pack.Used;long grams=arena.Simulation.Player.CarriedGrams;
        Assert.Equal(24,before);
        Assert.Null(arena.Submit(new EquipCommand(arena.Player,vest)));
        Assert.Equal(before-1,arena.Simulation.Pack.Used);
        Assert.Equal(grams,arena.Simulation.Player.CarriedGrams);
        Assert.Null(arena.Simulation.Pack.Holding(vest));
        var entry=arena.Simulation.Player.Inventory.Single(e=>e.ItemId==vest);
        Assert.Equal((-1,-1),(entry.Bag,entry.Slot));
        const string chest="container.waystation_chest";
        var arrows=arena.Simulation.Containers.Single(c=>c.Site.Key==chest).Items.Single(i=>i.DefId==Arrow);
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player,arrows.Ref,ItemPlace.In(chest),ItemPlace.Carried,1)));
        Assert.Equal(24,arena.Simulation.Pack.Used);
        Assert.StartsWith("no room",arena.Submit(new UnequipCommand(arena.Player,EquipSlot.Chest)));
        Assert.Equal(vest,arena.Simulation.Player.Equipment[EquipSlot.Chest]);
        var carriedArrow=Id(arena,Arrow);
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player,carriedArrow.Value,ItemPlace.Carried,ItemPlace.In(chest),1)));
        Assert.Null(arena.Submit(new UnequipCommand(arena.Player,EquipSlot.Chest)));
        Assert.NotNull(arena.Simulation.Pack.Holding(vest));
        Assert.Equal(24,arena.Simulation.Pack.Used);
        Assert.Single(arena.Simulation.Player.Inventory.Where(e=>e.ItemId==vest));
    }

    [Fact]
    public void ABag_AddsSlots_NotCarryingCapacity_AndLiesInNoSlotOfTheBackpack()
    {
        using var profile = new TempProfile();
        var arena = Start(Harness.Boot(profile), Open, Stack(Satchel, 1), Stack(Satchel, 1));
        var before = arena.Simulation.Player;
        int stacks = arena.Simulation.Pack.Used;
        Assert.Equal(24, arena.Simulation.Pack.Capacity);
        var first = arena.Simulation.Player.Inventory.Where(e => e.DefId == Satchel).Select(e => e.ItemId).OrderBy(i => i.Value, StringComparer.Ordinal).ToList();

        Assert.Null(arena.Submit(new EquipBagCommand(arena.Player, first[0], 2)));
        var after = arena.Simulation.Player;
        var pack = arena.Simulation.Pack;
        Assert.Equal(24 + 6, pack.Capacity);
        // Weight and the laden band are untouched: the bag weighs what it weighs whether it is worn or in the pack.
        Assert.Equal((before.CarriedGrams, before.CarryLimitGrams), (after.CarriedGrams, after.CarryLimitGrams));
        Assert.Equal(first[0], pack.BagSlots[1]);
        Assert.Equal(stacks - 1, pack.Used);
        var worn = after.Inventory.Single(e => e.ItemId == first[0]);
        Assert.Equal((-1, -1), (worn.Bag, worn.Slot));
        Assert.Equal(new[] { 0, 2 }, pack.Containers.Select(c => c.Bag));

        Assert.Equal("item.bag.satchel is already in a bag slot", arena.Submit(new EquipBagCommand(arena.Player, first[0], 3)));
        Assert.Equal("bag slot 2 holds item.bag.satchel: take it out first", arena.Submit(new EquipBagCommand(arena.Player, first[1], 2)));
        Assert.Equal("there is no bag slot 5", arena.Submit(new EquipBagCommand(arena.Player, first[1], 5)));
        Assert.Equal("item.weapon.rusted_sword is not a bag", arena.Submit(new EquipBagCommand(arena.Player, Id(arena, Sword), 1)));
        // A bag in a bag slot is neither dropped nor sold nor stashed: take it out first.
        Assert.Equal("unequip item.bag.satchel first", arena.Submit(new MoveItemCommand(arena.Player, first[0].Value, ItemPlace.Carried, ItemPlace.Ground, 1)));

        Assert.Null(arena.Submit(new UnequipBagCommand(arena.Player, 2)));
        Assert.Equal(24, arena.Simulation.Pack.Capacity);
        Assert.Equal(stacks, arena.Simulation.Pack.Used);
        Assert.Equal("bag slot 2 is empty", arena.Submit(new UnequipBagCommand(arena.Player, 2)));
    }

    [Fact]
    public void ABagsQuality_MovesItsSlots_AStepEach()
    {
        using var profile = new TempProfile();
        var arena = Start(Harness.Boot(profile), Open, Stack(Satchel, 1, Quality.Fine), Stack(Pouch, 1, Quality.Crude));
        Assert.Null(arena.Submit(new EquipBagCommand(arena.Player, Id(arena, Satchel), 1)));
        Assert.Null(arena.Submit(new EquipBagCommand(arena.Player, Id(arena, Pouch), 2)));
        Assert.Equal((7, 7), (Bag(arena, 1).Capacity, Bag(arena, 2).Capacity));   // 6 and a fine step; 8 and a crude one
    }

    [Fact]
    public void ASpecialisedBag_TakesOnlyItsKind_AndItemsAreRoutedIntoTheNarrowestOne()
    {
        using var profile = new TempProfile();
        var arena = Start(Harness.Boot(profile), Open, Stack(Reagent, 1), Stack(Quiver, 1), Stack(Pouch, 1), Stack(Arrow, 10), Stack(Ashbloom, 5), Stack(Ore, 3), Stack(Wolf, 2));
        Assert.Null(arena.Submit(new EquipBagCommand(arena.Player, Id(arena, Reagent), 1)));
        Assert.Null(arena.Submit(new EquipBagCommand(arena.Player, Id(arena, Quiver), 2)));
        Assert.Null(arena.Submit(new EquipBagCommand(arena.Player, Id(arena, Pouch), 3)));

        Assert.Equal(new[] { Ashbloom }, Order(arena, 3));                     // the herb pouch is narrower than the satchel that also takes herbs
        Assert.Equal(new[] { Arrow }, Order(arena, 2));
        Assert.Equal(new[] { Ore, Wolf }, Order(arena, 1).OrderBy(d => d, StringComparer.Ordinal));
        Assert.DoesNotContain(Arrow, Order(arena, 0));
        Assert.DoesNotContain(Ore, Order(arena, 0));

        // A stack may not be put where it does not belong.
        Assert.Equal("bag 2 takes only ammo", arena.Submit(new PlaceStackCommand(arena.Player, Id(arena, Ore), 2, 3)));
        Assert.Equal("bag 3 takes only herb", arena.Submit(new PlaceStackCommand(arena.Player, Id(arena, Ore), 3, 0)));
        Assert.Equal("bag 2 takes only ammo", arena.Submit(new PlaceStackCommand(arena.Player, Id(arena, Sword), 2, 0)));
        // ...and a herb may go in the satchel, which takes herbs too.
        Assert.Null(arena.Submit(new PlaceStackCommand(arena.Player, Id(arena, Ashbloom), 1, 5)));
        Assert.Equal((1, 5), (Entry(arena, Ashbloom).Bag, Entry(arena, Ashbloom).Slot));
    }

    [Fact]
    public void ThingsTakenFromAChest_RouteIntoTheirBagsAsTheyArrive()
    {
        using var profile = new TempProfile();
        var arena = Start(Harness.Boot(profile), AtChest, Stack(Quiver, 1), Stack(Pouch, 1));
        Assert.Null(arena.Submit(new EquipBagCommand(arena.Player, Id(arena, Quiver), 1)));
        Assert.Null(arena.Submit(new EquipBagCommand(arena.Player, Id(arena, Pouch), 2)));
        Assert.Empty(Bag(arena, 1).Stacks);

        Assert.Null(arena.Submit(new TakeAllCommand(arena.Player, "container.waystation_chest")));
        Assert.Equal(new[] { Arrow }, Order(arena, 1));
        Assert.Equal(new[] { Ashbloom }, Order(arena, 2));
        Assert.DoesNotContain(Arrow, Order(arena, 0));
    }

    [Fact]
    public void AFullPack_RefusesWhatDoesNotFit_UntilABagIsEquipped()
    {
        using var profile = new TempProfile();
        // The three starting packed stacks (the sword is worn), twenty flasks and a satchel fill the backpack's 24.
        var arena = Start(Harness.Boot(profile), AtChest, Enumerable.Range(0, 20).Select(_ => Stack(Flask, 1)).Append(Stack(Satchel, 1)).ToArray());
        Assert.Null(arena.Submit(new TakeAllCommand(arena.Player, "container.waystation_chest")));   // the salve joins its stack; the rest has no room
        Assert.Equal(24, arena.Simulation.Pack.Used);
        Assert.Equal(0, Count(arena, Arrow));
        var arrows = arena.Simulation.Containers.Single(c => c.Site.Key == "container.waystation_chest").Items.Single(i => i.DefId == Arrow);
        var again = arena.Submit(new MoveItemCommand(arena.Player, arrows.Ref, ItemPlace.In("container.waystation_chest"), ItemPlace.Carried, 1));
        Assert.StartsWith("no room", again ?? "");

        Assert.Null(arena.Submit(new EquipBagCommand(arena.Player, Id(arena, Satchel), 1)));
        Assert.Null(arena.Submit(new TakeAllCommand(arena.Player, "container.waystation_chest")));
        Assert.Equal(6, Count(arena, Arrow));
        Assert.Equal(2, Count(arena, Ashbloom));
    }

    [Fact]
    public void ABagIsRefusedItsWayOut_WhenWhatItHoldsHasNowhereElseToGo()
    {
        using var profile = new TempProfile();
        // 24 stacks fill the backpack; a quiver takes the arrows so the backpack stays full, then there is no room for the arrows' stacks and the quiver.
        var arena = Start(Harness.Boot(profile), Open, Enumerable.Range(0, 21).Select(_ => Stack(Flask, 1))
            .Concat(new[] { Stack(Quiver, 1), Stack(Arrow, 20), Stack(Arrow, 20), Stack(Arrow, 20) }).ToArray());
        Assert.Null(arena.Submit(new EquipBagCommand(arena.Player, Id(arena, Quiver), 1)));
        Assert.Equal(3, Bag(arena, 1).Stacks.Length);
        Assert.Equal(24, Bag(arena, 0).Stacks.Length);
        var refused = arena.Submit(new UnequipBagCommand(arena.Player, 1));
        Assert.Equal("item.bag.quiver holds more than the rest of the pack has room for: 4 stack(s) would not fit", refused);
        Assert.Equal(3, Bag(arena, 1).Stacks.Length);                                  // nothing changed
        Assert.Equal(Id(arena, Quiver), arena.Simulation.Pack.BagSlots[0]);
    }

    [Fact]
    public void SortingLaysTheUnlockedOut_ByName_Value_Weight_OrType_AndALockedStackKeepsItsSlot()
    {
        using var profile = new TempProfile();
        var arena = Start(Harness.Boot(profile), Open, Stack(Ore, 4), Stack(Wolf, 2), Stack(Small, 3), Stack(Salve, 2));
        var locked = Id(arena, Ore);
        Assert.Null(arena.Submit(new LockStackCommand(arena.Player, locked, true)));
        int lockedSlot = Entry(arena, Ore).Slot;
        Assert.True(lockedSlot >= 0);
        IEnumerable<InventoryEntry> Rest() => Bag(arena, 0).Stacks.Where(s => s.ItemId != locked);
        string Name(InventoryEntry e) => Def(arena, e.DefId).Name ?? e.DefId;

        Assert.Null(arena.Submit(new SortPackCommand(arena.Player, PackSort.Name)));
        Assert.Equal(Rest().Select(Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase), Rest().Select(Name));
        Assert.Equal(lockedSlot, Entry(arena, Ore).Slot);                       // the lock held its slot through the sort

        Assert.Null(arena.Submit(new SortPackCommand(arena.Player, PackSort.Value)));
        var values = Rest().Select(e => Def(arena, e.DefId).ValueBase * e.Count).ToList();
        Assert.Equal(values.OrderByDescending(v => v), values);

        Assert.Null(arena.Submit(new SortPackCommand(arena.Player, PackSort.Weight)));
        var weights = Rest().Select(e => Def(arena, e.DefId).WeightGrams * e.Count).ToList();
        Assert.Equal(weights.OrderByDescending(v => v), weights);

        Assert.Null(arena.Submit(new SortPackCommand(arena.Player, PackSort.Type)));
        var types = Rest().Select(e => Def(arena, e.DefId).Category).ToList();
        Assert.True(types.IndexOf("weapon") < types.IndexOf("material"));
        Assert.Equal(lockedSlot, Entry(arena, Ore).Slot);
    }

    [Fact]
    public void ASortIsTheSameEveryTime_AndMergesPartialStacksOfAKind()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        string Run()
        {
            var arena = Start(session, Open, Stack(Wolf, 3), Stack(Wolf, 4), Stack(Wolf, 5), Stack(Small, 2));
            Assert.Null(arena.Submit(new SortPackCommand(arena.Player, PackSort.Name)));
            return string.Join(",", Bag(arena, 0).Stacks.Select(s => $"{s.Slot}:{s.DefId}x{s.Count}"));
        }
        string first = Run();
        Assert.Equal(first, Run());
        Assert.Contains($"{Wolf}x10", first);                                  // 3 + 4 + 5 = 12: a full stack of 10 and one of 2
        Assert.Contains($"{Wolf}x2", first);
        Assert.DoesNotContain($"{Wolf}x3", first);
    }

    [Fact]
    public void ASplitKeepsItsMarks_AndAMergeNeedsStacksAlike()
    {
        using var profile = new TempProfile();
        var arena = Start(Harness.Boot(profile), Open, Stack(Wolf, 6));
        var id = Id(arena, Wolf);
        Assert.Null(arena.Submit(new MarkJunkCommand(arena.Player, id, true)));
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, id.Value, ItemPlace.Carried, ItemPlace.Carried, 2)));
        var stacks = arena.Simulation.Player.Inventory.Where(e => e.DefId == Wolf).OrderBy(e => e.Count).ToList();
        Assert.Equal(new[] { 2, 4 }, stacks.Select(e => e.Count));
        Assert.All(stacks, e => Assert.True(e.Junk));
        Assert.All(stacks, e => Assert.True(e.Slot >= 0));
        Assert.NotEqual(stacks[0].Slot, stacks[1].Slot);

        // Unmarked, the two are no longer alike: they will not merge.
        Assert.Null(arena.Submit(new MarkJunkCommand(arena.Player, stacks[0].ItemId, false)));
        Assert.Equal("there is no other stack to merge into", arena.Submit(new MoveItemCommand(arena.Player, stacks[0].ItemId.Value, ItemPlace.Carried, ItemPlace.Carried, 2)));
        Assert.Null(arena.Submit(new MarkJunkCommand(arena.Player, stacks[0].ItemId, true)));
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, stacks[0].ItemId.Value, ItemPlace.Carried, ItemPlace.Carried, 2)));
        Assert.Equal(6, Entry(arena, Wolf).Count);
    }

    [Fact]
    public void SellJunk_SellsWhatIsMarked_AndNeverWhatIsLockedEquippedOrAQuestItem()
    {
        using var profile = new TempProfile();
        var arena = Start(Harness.Boot(profile), AtKera, Stack(Wolf, 2), Stack(Small, 3), Stack(Ore, 2), Stack("item.quest.halda_token", 1));
        long coin = arena.Simulation.Player.Currency;
        Assert.Null(arena.Submit(new MarkJunkCommand(arena.Player, Id(arena, Wolf), true)));
        Assert.Null(arena.Submit(new MarkJunkCommand(arena.Player, Id(arena, Small), true)));
        Assert.Null(arena.Submit(new LockStackCommand(arena.Player, Id(arena, Small), true)));      // locked beats junk
        Assert.StartsWith("item.quest.halda_token cannot be marked", arena.Submit(new MarkJunkCommand(arena.Player, Id(arena, "item.quest.halda_token"), true)) ?? "");
        Assert.StartsWith("unequip", arena.Submit(new MarkJunkCommand(arena.Player, Id(arena, Sword), true)) ?? "");
        Assert.Contains("locked", arena.Submit(new SellCommand(arena.Player, Kera, Id(arena, Small), 1)) ?? "");

        var sold = arena.Record<JunkSold>();
        Assert.Null(arena.Submit(new SellJunkCommand(arena.Player, Kera)));
        Assert.Equal(0, Count(arena, Wolf));
        Assert.Equal(3, Count(arena, Small));
        Assert.Equal(2, Count(arena, Ore));
        Assert.True(arena.Simulation.Player.Currency > coin);
        Assert.Equal(1, Assert.Single(sold).Stacks);
        // Nothing left to sell: refused, and nothing changes.
        Assert.NotNull(arena.Submit(new SellJunkCommand(arena.Player, Kera)));
        Assert.Equal(3, Count(arena, Small));
    }

    [Fact]
    public void AStationWorksFromTheStashInItsSettlement()
    {
        using var profile = new TempProfile();
        var arena = Start(Harness.Boot(profile), AtStash, Stack(Small, 1), Stack(Cotton, 2));
        // The pelt goes in the stash; the cotton stays on the person.
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, Id(arena, Small).Value, ItemPlace.Carried, ItemPlace.In(Stash), 1)));
        Assert.Equal(0, Count(arena, Small));
        Assert.True(arena.WalkTo(AtBench.X, AtBench.Z));
        // The bench is not within reach of the stash, yet it works from it.
        var station = arena.Simulation.ActiveLayout.Stations.Single(s => s.Key == "station.hollow.workbench");
        Assert.Equal(Stash, Assert.Single(arena.Simulation.StashesAt(station)).Site.Key);

        var crafted = arena.Record<ItemCrafted>();
        Assert.Null(arena.Submit(new CraftCommand(arena.Player, PouchRecipe)));
        arena.Tick();
        Assert.Equal(PouchRecipe, Assert.Single(crafted).RecipeId);
        Assert.Equal(1, Count(arena, Pouch));
        Assert.Equal(1, Count(arena, Cotton));
        Assert.Empty(arena.Simulation.Containers.Single(c => c.Site.Key == Stash).Items);   // the pelt was spent out of the stash
        Assert.Equal("needs 1 item.material.small_hide", arena.Submit(new CraftCommand(arena.Player, PouchRecipe)));
    }

    [Fact]
    public void AStationFarFromAStash_DoesNotWorkFromIt()
    {
        using var profile = new TempProfile();
        var arena = Start(Harness.Boot(profile), AtStash, Stack(Small, 1), Stack(Cotton, 2));
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, Id(arena, Small).Value, ItemPlace.Carried, ItemPlace.In(Stash), 1)));
        // Each bench works from the stash of its own settlement: Cairnwold's from Cairnwold's, the hollow's from the hollow's.
        var cairnwold = arena.Simulation.Setup.Layout.Stations.Single(s => s.Key == "station.cairnwold.workbench");
        Assert.Equal("container.stash.cairnwold", Assert.Single(arena.Simulation.StashesAt(cairnwold)).Site.Key);
        var hollow = arena.Simulation.Setup.Layout.Stations.Single(s => s.Key == "station.hollow.workbench");
        Assert.Equal(Stash, Assert.Single(arena.Simulation.StashesAt(hollow)).Site.Key);
        // The road's cookfire is a long way from both.
        var road = arena.Simulation.Setup.Layout.Stations.Single(s => s.Key == "station.road.rim_cookfire");
        Assert.Empty(arena.Simulation.StashesAt(road));
    }

    [Fact]
    public void ThePackCommands_AreDeterministic_ReplayingThemGivesTheSamePack()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        string Run()
        {
            var arena = Start(session, Open, Stack(Reagent, 1), Stack(Satchel, 1), Stack(Ore, 3), Stack(Wolf, 2), Stack(Small, 1), Stack(Ashbloom, 4));
            arena.Submit(new EquipBagCommand(arena.Player, Id(arena, Reagent), 3));
            arena.Submit(new EquipBagCommand(arena.Player, Id(arena, Satchel), 1));
            arena.Submit(new SortPackCommand(arena.Player, PackSort.Value));
            arena.Submit(new MarkJunkCommand(arena.Player, Id(arena, Small), true));
            arena.Submit(new SortPackCommand(arena.Player, PackSort.Name));
            return string.Join(";", arena.Simulation.Pack.Containers.Select(c => $"{c.Bag}[{string.Join(",", c.Stacks.Select(s => $"{s.Slot}={s.DefId}x{s.Count}"))}]"));
        }
        Assert.Equal(Run(), Run());
    }
}
