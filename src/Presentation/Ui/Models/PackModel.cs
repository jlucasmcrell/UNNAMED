// UNNAMED Presentation - the pack's view model: the sections of the inventory (backpack and bags) with the tab and search applied, and where a
// recipe's inputs come from (carried, in a bag, in the stash)
// Godot-free: Presentation.Tests links this file and tests the mapping on its own (D-11).

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// One container of the pack as the inventory lists it. <see cref="Rows"/> are the stacks the tab and the search leave, by slot;
/// <see cref="Used"/> and <see cref="Capacity"/> are the container's own and never move with the filter.
/// </summary>
public sealed record PackSectionModel(int Bag, string Title, string Detail, int Used, int Capacity, ImmutableArray<InventoryEntry> Rows)
{
    public string Fill => $"{Used} / {Capacity}";

    public bool Full => Used >= Capacity;
}

/// <summary>Where a recipe's input is to be had.</summary>
public enum SupplySource { Short, Carried, Stash, Both }

/// <summary>
/// What a station could work an input from: the count carried (not worn), the bags that carried count lies in, and the count in the
/// stashes of the station's settlement.
/// </summary>
public sealed record InputSupply(string ItemId, int Needed, int Carried, int Stashed, ImmutableArray<string> CarriedIn)
{
    public int Total => Carried + Stashed;

    public bool Enough => Total >= Needed;

    public SupplySource Source => !Enough ? SupplySource.Short : Stashed == 0 ? SupplySource.Carried : Carried == 0 ? SupplySource.Stash : SupplySource.Both;

    /// <summary>"3 carried (Reagent Satchel)", "2 in the stash", "1 carried + 2 in the stash", or "have 1 of 3".</summary>
    public string Label
    {
        get
        {
            string where = CarriedIn.IsDefaultOrEmpty ? "" : $" ({string.Join(", ", CarriedIn)})";
            return Source switch
            {
                SupplySource.Carried => $"{Carried} carried{where}",
                SupplySource.Stash => $"{Stashed} in the stash",
                SupplySource.Both => $"{Carried} carried{where} + {Stashed} in the stash",
                _ => $"have {Total} of {Needed}" + (Total == 0 ? "" : Stashed > 0 ? $" ({Carried} carried, {Stashed} in the stash)" : ""),
            };
        }
    }
}

public static class PackModel
{
    /// <summary>What a specialised bag takes, in words: "crafting materials and herbs only".</summary>
    public static string AcceptsWords(ImmutableArray<string> accepts) => accepts.IsDefaultOrEmpty
        ? "anything"
        : string.Join(" and ", accepts.Select(k => k switch { PackKinds.Material => "crafting materials", PackKinds.Herb => "herbs", PackKinds.Ammo => "arrows", _ => k })) + " only";

    /// <summary>
    /// The pack's containers in order (backpack, then the bags in their slots), each with the stacks the tab and the search leave. A stack is
    /// shown when its name matches every word of the search; the tab narrows by <see cref="PackTabs.Of"/>.
    /// </summary>
    public static ImmutableArray<PackSectionModel> Sections(PackView pack, ItemCatalog catalog, Func<InventoryEntry, string> name, Func<string, string> bagName,
        PackTab tab, string search)
    {
        bool Shown(InventoryEntry e) => catalog.Find(e.DefId) is { } definition && PackTabs.Shows(tab, definition)
            && (string.IsNullOrWhiteSpace(search) || PackTabs.Matches(search, name(e), definition));
        return pack.Containers.Select(c => new PackSectionModel(c.Bag,
            c.IsBackpack ? "Backpack" : bagName(c.BagDefId!),
            c.IsBackpack ? "" : $"Bag {c.Bag} · {(c.Accepts.IsDefaultOrEmpty ? "takes anything" : "takes " + AcceptsWords(c.Accepts))}",
            c.Stacks.Length, c.Capacity, c.Stacks.Where(Shown).ToImmutableArray())).ToImmutableArray();
    }

    /// <summary>The line for a bag slot in the bag bar: the bag and how full it is, or that the slot is empty.</summary>
    public static string SlotLine(PackView pack, int slot, Func<string, string> bagName) =>
        pack.Containers.FirstOrDefault(c => c.Bag == slot) is { BagDefId: { } def } c ? $"{bagName(def)} {c.Stacks.Length}/{c.Capacity}" : "empty";

    /// <summary>The first empty bag slot (1-based), or 0 when all are taken.</summary>
    public static int FirstEmptySlot(PackView pack) => pack.BagSlots.Select((b, i) => (b, i)).FirstOrDefault(x => x.b is null, (null, -1)).Item2 + 1;

    /// <summary>
    /// What a station could work this input from: what is carried (not worn, not a bag in a bag slot), the bags it lies in, and what the
    /// stashes in the station's settlement hold.
    /// </summary>
    public static InputSupply Supply(string itemId, int needed, PackView pack, IEnumerable<ItemView> stash, IReadOnlyCollection<EntityId> worn, Func<PackContainerView, string> bagTitle)
    {
        var lying = pack.Containers.SelectMany(c => c.Stacks.Where(e => e.DefId == itemId && !worn.Contains(e.ItemId)).Select(e => (Container: c, e.Count))).ToList();
        int stashed = stash.Where(i => i.DefId == itemId && !i.Ref.Contains('#')).Sum(i => i.Count);
        var bags = lying.Select(l => l.Container).Where(c => !c.IsBackpack).Select(bagTitle).Distinct().ToImmutableArray();
        return new InputSupply(itemId, needed, lying.Sum(l => l.Count), stashed, bags);
    }
}
