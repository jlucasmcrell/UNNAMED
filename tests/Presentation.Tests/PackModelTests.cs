using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Presentation.Ui;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Tests;

/// <summary>The inventory sections (backpack and bags, with the tab and the search applied) and where a recipe input is to be had.</summary>
public class PackModelTests
{
    private static ItemDefinition Def(string id, string category, string? kind = null) =>
        new(id, category, 20, 100, 1, "common", false, false, null, Requirements.None, null, 0) { PackKind = kind };

    private static readonly ItemCatalog Catalog = new(new[]
    {
        Def("item.material.wolf_hide", "material"), Def("item.material.herb_ashbloom", "material", PackKinds.Herb), Def("item.weapon.rusted_sword", "weapon"),
        Def("item.consumable.salve_minor", "consumable"), Def("item.bag.herb_pouch", "bag"),
    });

    private static InventoryEntry Entry(string def, int count, int bag, int slot) => new(EntityId.NewId(EntityKind.Item), def, count) { Bag = bag, Slot = slot };

    private static PackView Pack(params InventoryEntry[] stacks)
    {
        var pouch = new InventoryEntry(EntityId.NewId(EntityKind.Item), "item.bag.herb_pouch", 1) { Bag = -1 };
        var containers = ImmutableArray.Create(
            new PackContainerView(0, null, null, 0, 24, ImmutableArray<string>.Empty, stacks.Where(s => s.Bag == 0).OrderBy(s => s.Slot).ToImmutableArray()),
            new PackContainerView(1, pouch.ItemId, pouch.DefId, 0, 8, ImmutableArray.Create(PackKinds.Herb), stacks.Where(s => s.Bag == 1).OrderBy(s => s.Slot).ToImmutableArray()));
        return new PackView(containers, ImmutableArray.Create<EntityId?>(pouch.ItemId, null, null, null), ImmutableArray<InventoryEntry>.Empty);
    }

    private static string Name(InventoryEntry e) => e.DefId[(e.DefId.LastIndexOf('.') + 1)..].Replace('_', ' ');

    [Fact]
    public void TheSections_FollowTheBags_AndTheTabAndSearchOnlyNarrowTheRows()
    {
        var pack = Pack(Entry("item.weapon.rusted_sword", 1, 0, 0), Entry("item.material.wolf_hide", 3, 0, 1), Entry("item.material.herb_ashbloom", 5, 1, 0));
        var all = PackModel.Sections(pack, Catalog, Name, _ => "Herb Pouch", PackTab.All, "");
        Assert.Equal(new[] { "Backpack", "Herb Pouch" }, all.Select(s => s.Title));
        Assert.Equal(new[] { "2 / 24", "1 / 8" }, all.Select(s => s.Fill));
        Assert.Equal("Bag 1 · takes herbs only", all[1].Detail);

        var materials = PackModel.Sections(pack, Catalog, Name, _ => "Herb Pouch", PackTab.Materials, "");
        Assert.Equal(new[] { "wolf hide" }, materials[0].Rows.Select(Name));
        Assert.Equal("2 / 24", materials[0].Fill);                          // the fill is the bag's own, never the filter's
        Assert.Single(materials[1].Rows);

        var found = PackModel.Sections(pack, Catalog, Name, _ => "Herb Pouch", PackTab.All, "ash");
        Assert.Empty(found[0].Rows);
        Assert.Single(found[1].Rows);
    }

    [Fact]
    public void ABagSlotReadsItsBagAndHowFull_OrEmpty_AndTheFirstEmptyOneIsFound()
    {
        var pack = Pack(Entry("item.material.herb_ashbloom", 5, 1, 0));
        Assert.Equal("Herb Pouch 1/8", PackModel.SlotLine(pack, 1, _ => "Herb Pouch"));
        Assert.Equal("empty", PackModel.SlotLine(pack, 2, _ => "Herb Pouch"));
        Assert.Equal(2, PackModel.FirstEmptySlot(pack));
        var taken = Enumerable.Range(0, 4).Select(_ => (EntityId?)EntityId.NewId(EntityKind.Item)).ToImmutableArray();
        Assert.Equal(0, PackModel.FirstEmptySlot(pack with { BagSlots = taken }));
    }

    [Fact]
    public void ARecipeInputsSource_IsCarried_Stashed_Both_OrShort_AndNamesTheBagItLiesIn()
    {
        var pack = Pack(Entry("item.material.wolf_hide", 3, 0, 1), Entry("item.material.herb_ashbloom", 2, 1, 0));
        var stash = new[] { new ItemView("x1", "item.material.wolf_hide", 4), new ItemView("x2", "item.material.iron_ore", 9) };
        string Title(PackContainerView c) => "Herb Pouch";
        var none = Array.Empty<EntityId>();

        var carried = PackModel.Supply("item.material.wolf_hide", 2, pack, Array.Empty<ItemView>(), none, Title);
        Assert.Equal((SupplySource.Carried, "3 carried"), (carried.Source, carried.Label));
        Assert.Equal("2 carried (Herb Pouch)", PackModel.Supply("item.material.herb_ashbloom", 2, pack, stash, none, Title).Label);
        var both = PackModel.Supply("item.material.wolf_hide", 6, pack, stash, none, Title);
        Assert.Equal((SupplySource.Both, "3 carried + 4 in the stash"), (both.Source, both.Label));
        var stashed = PackModel.Supply("item.material.iron_ore", 5, pack, stash, none, Title);
        Assert.Equal((SupplySource.Stash, "9 in the stash"), (stashed.Source, stashed.Label));
        var short_ = PackModel.Supply("item.material.iron_ore", 10, pack, stash, none, Title);
        Assert.Equal((SupplySource.Short, false, "have 9 of 10 (0 carried, 9 in the stash)"), (short_.Source, short_.Enough, short_.Label));
        Assert.Equal("have 0 of 2", PackModel.Supply("item.material.feathers", 2, pack, stash, none, Title).Label);
        // A worn piece is not something a station works from.
        var worn = pack.Containers[0].Stacks[0].ItemId;
        Assert.Equal(0, PackModel.Supply("item.material.wolf_hide", 1, pack, Array.Empty<ItemView>(), new[] { worn }, Title).Carried);
    }
}
