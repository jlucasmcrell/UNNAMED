// UNNAMED Domain - gear sets: named outfits of the equipment the character wears, switched as a timed action (action bars and gear sets)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Domain.Items;

/// <summary>One piece of a gear set: the slot it is worn in, the very item (an instance, never a kind of item) and its definition, kept so a piece that is no longer carried can still be named.</summary>
public sealed record GearSetItem(EquipSlot Slot, EntityId Item, string DefId);

/// <summary>A saved outfit: its place (1 to 10), its name and the pieces worn. A slot it does not list is worn empty.</summary>
public sealed record GearSet(int Index, string Name, ImmutableArray<GearSetItem> Items);

/// <summary>What switching to a gear set does from the equipment as it stands.</summary>
/// <param name="Unequip">Slots worn now that the set wears empty, hands first.</param>
/// <param name="Equip">Pieces of the set that are carried and not worn in their slot, main hand first.</param>
/// <param name="Missing">Pieces of the set that are not carried: shown to the player, never conjured, and their slots left as they are.</param>
/// <param name="ArmourKept">The switch was made in a fight: only the hands change, and the set's armour pieces were left as they are.</param>
public sealed record GearSwapPlan(ImmutableArray<EquipSlot> Unequip, ImmutableArray<GearSetItem> Equip, ImmutableArray<GearSetItem> Missing, bool ArmourKept = false)
{
    /// <summary>The switch would change what is worn.</summary>
    public bool Changes => !Unequip.IsEmpty || !Equip.IsEmpty;
}

/// <summary>The gear sets' rules: how many, what a name may be, what a switch does, and how long it takes (the owner's request: a short, sensible time, longer in combat).</summary>
public static class GearSetRules
{
    public const int MaxSets = QuickBar.GearSets;
    public const int MaxNameLength = 24;

    /// <summary>How long a switch takes out of a fight, and in one, in seconds.</summary>
    public const double CalmSeconds = 2.0;

    public const double CombatSeconds = 4.0;

    /// <summary>What a switch in a fight changes (the owner, 2026-10-18): the hands only. Armour stays on; there is no ammunition slot - arrows are carried.</summary>
    public static bool ChangesInAFight(EquipSlot slot) => slot is EquipSlot.MainHand or EquipSlot.OffHand;

    /// <summary>The short line said when a switch in a fight left the armour alone.</summary>
    public const string ArmourStays = "Armour stays on in a fight";

    /// <summary>A name as kept: trimmed and single-spaced, 1 to 24 characters, no control characters; null when it is not one.</summary>
    public static string? CleanName(string? name)
    {
        if (name is null || name.Any(char.IsControl))
            return null;
        string clean = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return clean.Length is >= 1 and <= MaxNameLength ? clean : null;
    }

    /// <summary>Sets by place, each place once, named, wearing one piece and one slot at most once.</summary>
    public static bool IsValid(ImmutableArray<GearSet> sets)
    {
        if (sets.IsDefault || sets.Length > MaxSets)
            return false;
        int last = 0;
        foreach (var set in sets)
        {
            if (set.Index <= last || set.Index > MaxSets || CleanName(set.Name) != set.Name || set.Items.IsDefault)
                return false;
            last = set.Index;
            if (set.Items.Select(i => i.Slot).Distinct().Count() != set.Items.Length || set.Items.Select(i => i.Item).Distinct().Count() != set.Items.Length
                || set.Items.Any(i => !DefinitionId.IsValid(i.DefId) || !Enum.IsDefined(i.Slot)))
                return false;
        }
        return true;
    }

    /// <summary>The set that is now worn, kept under <paramref name="index"/>; <paramref name="defOf"/> names what a worn item is.</summary>
    public static GearSet Capture(int index, string name, IReadOnlyDictionary<EquipSlot, EntityId> equipment, Func<EntityId, string?> defOf) =>
        new(index, name, equipment.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value, Def: defOf(kv.Value))).Where(w => w.Def is not null)
            .Select(w => new GearSetItem(w.Key, w.Value, w.Def!)).ToImmutableArray());

    /// <summary>
    /// What switching to <paramref name="set"/> does: what is worn and not in the set comes off; what the set wears and is carried goes on;
    /// what the set wears and is not carried is missing, and what is worn in its slot stays.
    /// </summary>
    public static GearSwapPlan Plan(GearSet set, IReadOnlyDictionary<EquipSlot, EntityId> worn, Func<EntityId, bool> carried, bool inAFight = false)
    {
        var wanted = set.Items.ToDictionary(i => i.Slot);
        var unequip = worn.Keys.Where(slot => !wanted.ContainsKey(slot) && (!inAFight || ChangesInAFight(slot))).OrderBy(SlotOrder).ToImmutableArray();
        var equip = ImmutableArray.CreateBuilder<GearSetItem>();
        var missing = ImmutableArray.CreateBuilder<GearSetItem>();
        bool kept = inAFight && worn.Keys.Any(slot => !wanted.ContainsKey(slot) && !ChangesInAFight(slot));
        foreach (var item in set.Items.OrderBy(i => SlotOrder(i.Slot)))
        {
            if (worn.TryGetValue(item.Slot, out var current) && current == item.Item)
                continue;
            if (inAFight && !ChangesInAFight(item.Slot))
            {
                kept = true;   // skipped, not queued
                continue;
            }
            if (carried(item.Item))
                equip.Add(item);
            else
                missing.Add(item);
        }
        return new GearSwapPlan(unequip, equip.ToImmutable(), missing.ToImmutable(), kept);
    }

    /// <summary>Hands before the rest, main hand first: a two-hander named in the main hand clears the off hand as it goes on.</summary>
    private static int SlotOrder(EquipSlot slot) => (int)slot;

    /// <summary>How many ticks a switch takes, at <paramref name="tickMilliseconds"/> a tick.</summary>
    public static int SwapTicks(bool inCombat, int tickMilliseconds) =>
        Math.Max(1, (int)Math.Round((inCombat ? CombatSeconds : CalmSeconds) * 1000.0 / Math.Max(1, tickMilliseconds)));
}
