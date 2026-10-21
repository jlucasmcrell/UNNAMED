// UNNAMED Presentation - the action bars' view model: what each slot of a bar shows (its icon ID, name, cost, count, cooldown and availability)
// Godot-free: Presentation.Tests links this file and tests the mapping on its own (D-11). The availability is the Skills page's one evaluator.

using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>What a slot holds.</summary>
public enum ActionSlotKind { Empty, Item, Formula, Technique, GearSet }

/// <summary>
/// A slot as the bar draws it. <see cref="Note"/> is the small line under the icon: a formula's Focus, a technique's Stamina, an item's
/// count, a gear set's place. <see cref="Word"/> and <see cref="Reason"/> are the availability's, shown on focus or hover; a slot that is
/// not <see cref="Ready"/> is dimmed and also carries <see cref="Shape"/>, so its state never rests on colour alone.
/// </summary>
public sealed record ActionSlotView(int Bar, int Slot, string Id, ActionSlotKind Kind, string Name, string Key, string Note, bool Ready, string Word,
    string? Reason, int CooldownSeconds, bool Active)
{
    public bool Empty => Kind == ActionSlotKind.Empty;
    public string? CoatingLabel => Id switch
    {
        "item.consumable.poison_flask" => "VEN",
        "item.consumable.sleep_coating" => "SLP",
        "item.consumable.paralyse_coating" => "PAR",
        "item.consumable.snarebind_resin" => "ROOT",
        _ => null,
    };

    /// <summary>A mark for the availability that is not a colour: none when ready, a dot for a wait, a bar for a want.</summary>
    public string Shape => Ready ? "" : CooldownSeconds > 0 ? "…" : "–";
}

/// <summary>
/// A gear set as the bars and the character sheet list it: its name, how many pieces it wears, which of them are not carried (named, never
/// conjured) and whether it is what is worn now.
/// </summary>
public sealed record GearSetRow(int Index, string Name, int Pieces, ImmutableArray<string> Missing, bool Worn, bool Swapping)
{
    public bool Complete => Missing.IsEmpty;
}

public static class ActionBarModel
{
    /// <summary>
    /// The availability context for what the character is and holds now; the Skills page and the bars read the same one, so a technique reads
    /// the same on both.
    /// </summary>
    public static AvailabilityContext Context(Simulation simulation, Func<string, string> name, string? stationKind)
    {
        var view = simulation.Player;
        var progression = view.Progression;
        string? held = null, heldName = null;
        if (view.Equipment.TryGetValue(EquipSlot.MainHand, out var hand) && view.Inventory.FirstOrDefault(e => e.ItemId == hand) is { } entry
            && simulation.Setup.Items.Catalog.Find(entry.DefId) is { Weapon.SkillId: { } skill })
        {
            held = skill;
            heldName = name(entry.DefId);
        }
        return new AvailabilityContext(id => UNNAMED.Domain.Progression.ProgressionEngine.SkillLevel(progression, id),
            progression.Pools.Stamina ?? (int)view.Stats.StaminaMax, progression.Pools.Focus ?? (int)view.Stats.FocusMax, progression.Pools.Strain,
            (int)view.Stats.StrainTolerance, progression.Pools.Health ?? (int)view.Stats.HealthMax, held, heldName,
            id => view.Inventory.Where(e => e.DefId == id).Sum(e => e.Count), stationKind,
            id => (simulation.TechniqueCooldown(id) + 19) / 20, name);
    }

    /// <summary>The twelve slots of action bar <paramref name="bar"/> (0 to 5). <paramref name="keyOf"/> names the key that uses a slot, or empty when none does.</summary>
    public static ImmutableArray<ActionSlotView> Slots(Simulation simulation, int bar, Func<string, string> name, Func<int, int, string> keyOf, string? stationKind,
        string? casting)
    {
        var contents = simulation.ActionBars[bar];
        var context = Context(simulation, name, stationKind);
        var setup = simulation.Setup;
        var builder = ImmutableArray.CreateBuilder<ActionSlotView>(contents.Length);
        for (int slot = 0; slot < contents.Length; slot++)
        {
            string key = keyOf(bar, slot);
            string? id = contents[slot];
            builder.Add(id switch
            {
                null => new ActionSlotView(bar, slot, "", ActionSlotKind.Empty, "", key, "", true, "", null, 0, false),
                _ when QuickBar.TryGearSet(id, out int set) => GearSlot(simulation, bar, slot, key, set),
                _ when setup.Magic.Formulas.TryGetValue(id, out var formula) =>
                    Of(AvailabilityRules.Formula(formula, setup.Magic.Constants, context), bar, slot, id, ActionSlotKind.Formula, name(id), key, $"{formula.FocusCost}F", id == casting),
                _ when setup.Techniques.TryGetValue(id, out var technique) =>
                    Of(AvailabilityRules.Technique(technique, context), bar, slot, id, ActionSlotKind.Technique, name(id), key, $"{technique.StaminaCost}S", false),
                _ when id.StartsWith("item.", StringComparison.Ordinal) => ItemSlot(simulation, bar, slot, id, name(id), key),
                _ => new ActionSlotView(bar, slot, id, ActionSlotKind.Item, name(id), key, "", true, "", null, 0, false),
            });
        }
        return builder.MoveToImmutable();
    }

    private static ActionSlotView Of(Availability availability, int bar, int slot, string id, ActionSlotKind kind, string name, string key, string note, bool active) =>
        new(bar, slot, id, kind, name, key, note, availability.Ready, availability.Word, availability.Reason,
            availability.Kind == AvailabilityKind.Recovering ? SecondsOf(availability.Reason) : 0, active);

    /// <summary>The seconds in "Recovering: ready again in N s", or 1 when the words say no number.</summary>
    private static int SecondsOf(string? reason)
    {
        if (reason is null)
            return 1;
        var digits = new string(reason.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out int seconds) ? Math.Max(1, seconds) : 1;
    }

    private static ActionSlotView ItemSlot(Simulation simulation, int bar, int slot, string id, string name, string key)
    {
        int count = simulation.Player.Inventory.Where(e => e.DefId == id).Sum(e => e.Count);
        return new ActionSlotView(bar, slot, id, ActionSlotKind.Item, name, key, $"×{count}", count > 0, count > 0 ? ReasonText.Ready : "None left",
            count > 0 ? null : $"None left: you carry no {name}", 0, false);
    }

    private static ActionSlotView GearSlot(Simulation simulation, int bar, int slot, string key, int index)
    {
        var rows = GearSets(simulation, _ => "");
        var row = rows.FirstOrDefault(r => r.Index == index);
        string id = QuickBar.GearSetId(index);
        if (row is null)
            return new ActionSlotView(bar, slot, id, ActionSlotKind.GearSet, $"Gear set {index}", key, $"Set {index}", false, "Empty set", "No gear set is kept here", 0, false);
        if (row.Swapping)
            return new ActionSlotView(bar, slot, id, ActionSlotKind.GearSet, row.Name, key, $"Set {index}", false, "Changing", "Changing gear", 0, true);
        string reason = row.Worn ? $"{row.Name} is worn" : row.Complete ? "Ready" : $"Not carried: {string.Join(", ", row.Missing)}";
        return new ActionSlotView(bar, slot, id, ActionSlotKind.GearSet, row.Name, key, $"Set {index}", row.Worn || row.Complete, row.Worn ? "Worn" : row.Complete ? ReasonText.Ready : "Missing pieces",
            row.Worn || row.Complete ? null : reason, 0, row.Worn);
    }

    /// <summary>The gear sets as listed: each with the pieces that are not carried named by <paramref name="name"/>, and whether it is what is worn.</summary>
    public static ImmutableArray<GearSetRow> GearSets(Simulation simulation, Func<string, string> name)
    {
        var carried = simulation.Player.Inventory.Select(e => e.ItemId).ToHashSet();
        var worn = simulation.Player.Equipment;
        var swapping = simulation.GearSwap?.Index;
        return simulation.GearSets.Select(set => new GearSetRow(set.Index, set.Name, set.Items.Length,
            set.Items.Where(i => !carried.Contains(i.Item)).Select(i => name(i.DefId)).ToImmutableArray(),
            set.Items.Length == worn.Count && set.Items.All(i => worn.TryGetValue(i.Slot, out var current) && current == i.Item), swapping == set.Index)).ToImmutableArray();
    }

    /// <summary>The page after <paramref name="page"/> in steps of <paramref name="by"/>, wrapping within the six bars.</summary>
    public static int Page(int page, int by) => ((page + by) % QuickBar.Bars + QuickBar.Bars) % QuickBar.Bars;
}
