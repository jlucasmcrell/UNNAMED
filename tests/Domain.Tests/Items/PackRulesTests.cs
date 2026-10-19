using System.Collections.Immutable;
using UNNAMED.Domain.Items;

namespace UNNAMED.Domain.Tests.Items;

/// <summary>Where a stack lies in the pack: routing to the narrowest bag that takes its kind, kept slots, sorting and the category tabs.</summary>
public class PackRulesTests
{
    private static ImmutableArray<string> Kinds(params string[] kinds) => kinds.ToImmutableArray();

    // The backpack (4 slots), a satchel for materials and herbs (3), a herb pouch (2) and a general bag (2).
    private static readonly ImmutableArray<PackContainer> Containers = PackRules.Containers(4, new[]
    {
        (1, 3, Kinds(PackKinds.Material, PackKinds.Herb)),
        (2, 2, Kinds(PackKinds.Herb)),
        (3, 2, ImmutableArray<string>.Empty),
    });

    private static PackItem Item(string id, string? kind, PackSlot? at = null, bool locked = false) => new(id, kind, at, locked);

    [Fact]
    public void AKindGoesToTheNarrowestBagThatTakesIt_ThenToTheGeneralOnes_BackpackFirst()
    {
        var placed = PackRules.Place(new[] { Item("a", PackKinds.Herb), Item("b", PackKinds.Herb), Item("c", PackKinds.Herb), Item("d", PackKinds.Material), Item("e", null) }, Containers);
        Assert.Empty(placed.Unplaced);
        Assert.Equal(new PackSlot(2, 0), placed.Placed["a"]);      // the herb pouch before the satchel that also takes herbs
        Assert.Equal(new PackSlot(2, 1), placed.Placed["b"]);
        Assert.Equal(new PackSlot(1, 0), placed.Placed["c"]);      // the pouch is full: on to the next that takes herbs
        Assert.Equal(new PackSlot(1, 1), placed.Placed["d"]);
        Assert.Equal(new PackSlot(0, 0), placed.Placed["e"]);      // what no bag is for goes to the backpack
    }

    [Fact]
    public void ASpecialisedBag_NeverTakesWhatItIsNotFor_AndAFullPackLeavesTheRestUnplaced()
    {
        var six = Enumerable.Range(0, 6).Select(i => Item($"w{i}", null)).ToList();
        var placed = PackRules.Place(six, Containers);
        Assert.Empty(placed.Unplaced);
        Assert.Equal(new[] { 0, 0, 0, 0, 3, 3 }, six.Select(i => placed.Placed[i.Id].Bag).OrderBy(b => b));   // backpack first, then the general bag
        var seven = Enumerable.Range(0, 7).Select(i => Item($"w{i}", null));
        Assert.Equal(new[] { "w6" }, PackRules.Place(seven, Containers).Unplaced);
    }

    [Fact]
    public void AStackKeepsTheSlotItHolds_UnlessItIsOutOfRangeTakenOrNotForThatBag()
    {
        var items = new[]
        {
            Item("a", null, new PackSlot(0, 2)), Item("b", null, new PackSlot(0, 2)), Item("c", null, new PackSlot(9, 0)), Item("d", null, new PackSlot(2, 0)),
            Item("e", PackKinds.Herb, new PackSlot(2, 1)),
        };
        var placed = PackRules.Place(items, Containers).Placed;
        Assert.Equal(new PackSlot(0, 2), placed["a"]);              // the first of two claimants keeps it
        Assert.NotEqual(new PackSlot(0, 2), placed["b"]);
        Assert.Equal(0, placed["c"].Bag);                           // there is no bag 9
        Assert.Equal(0, placed["d"].Bag);                           // the herb pouch does not take it
        Assert.Equal(new PackSlot(2, 1), placed["e"]);
    }

    [Fact]
    public void Fits_CountsTheStacksStillToCome()
    {
        var full = Enumerable.Range(0, 4).Select(i => Item($"w{i}", null)).Concat(Enumerable.Range(0, 2).Select(i => Item($"g{i}", null))).ToList();
        // Backpack and the general bag are full; five herbs still have the pouch and the satchel (2 + 3 slots).
        Assert.True(PackRules.Fits(full, new string?[] { PackKinds.Herb, PackKinds.Herb, PackKinds.Herb, PackKinds.Herb, PackKinds.Herb }, Containers));
        Assert.False(PackRules.Fits(full, new string?[] { null }, Containers));
    }

    [Fact]
    public void ACapacity_MovesAStepPerQuality_AndNeverBelowOne()
    {
        var rules = BagRules.Default;
        var spec = new BagSpec(6, ImmutableArray<string>.Empty);
        Assert.Equal(new[] { 5, 6, 7, 8 }, new[] { -1, 0, 1, 2 }.Select(q => rules.Capacity(spec, q)));
        Assert.Equal(1, rules.Capacity(new BagSpec(1, ImmutableArray<string>.Empty), -1));
    }

    [Fact]
    public void SortOrders_AreByTypeNameValueOrWeight_WithTiesBrokenTheSameWayEveryTime()
    {
        var rows = new[]
        {
            new SortRow("3", "material", "Hide", 12, 1000, 0), new SortRow("1", "weapon", "Sword", 15, 3000, 0), new SortRow("2", "consumable", "Salve", 18, 200, 0),
            new SortRow("4", "material", "Hide", 12, 1000, 1),
        };
        Assert.Equal(new[] { "1", "2", "4", "3" }, PackSorting.Order(rows, PackSort.Type));       // finer first among equals
        Assert.Equal(new[] { "4", "3", "2", "1" }, PackSorting.Order(rows, PackSort.Name));
        Assert.Equal(new[] { "2", "1", "4", "3" }, PackSorting.Order(rows, PackSort.Value));
        Assert.Equal(new[] { "1", "4", "3", "2" }, PackSorting.Order(rows, PackSort.Weight));
        Assert.Equal(PackSorting.Order(rows, PackSort.Value), PackSorting.Order(rows.Reverse(), PackSort.Value));
    }

    [Fact]
    public void TheTabs_SortItemsIntoGearConsumablesMaterialsAndQuest_AndTheSearchFindsWords()
    {
        ItemDefinition Make(string category, string? kind = null) => new("item.x", category, 1, 0, 0, "common", false, false, null, Requirements.None, null, 0) { PackKind = kind };
        Assert.Equal(PackTab.Gear, PackTabs.Of(Make("weapon")));
        Assert.Equal(PackTab.Gear, PackTabs.Of(Make("misc", PackKinds.Ammo)));
        Assert.Equal(PackTab.Consumables, PackTabs.Of(Make("consumable")));
        Assert.Equal(PackTab.Consumables, PackTabs.Of(Make("book")));
        Assert.Equal(PackTab.Materials, PackTabs.Of(Make("material")));
        Assert.Equal(PackTab.Materials, PackTabs.Of(Make("material", PackKinds.Herb)));
        Assert.Equal(PackTab.Quest, PackTabs.Of(Make("quest_item")));
        Assert.True(PackTabs.Shows(PackTab.All, Make("quest_item")));
        Assert.False(PackTabs.Shows(PackTab.Gear, Make("material")));

        var hide = Make("material");
        Assert.True(PackTabs.Matches("wolf", "Wolf Hide", hide));
        Assert.True(PackTabs.Matches("HIDE wolf", "Wolf Hide", hide));       // every word, any case
        Assert.True(PackTabs.Matches("material", "Wolf Hide", hide));        // by category as well
        Assert.False(PackTabs.Matches("wolf sword", "Wolf Hide", hide));
        Assert.True(PackTabs.Matches("   ", "Wolf Hide", hide));
    }
}
