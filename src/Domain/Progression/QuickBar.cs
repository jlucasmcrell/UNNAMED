// UNNAMED Domain - an action bar: twelve slots for what the character uses from the keys (PJ09; C26; WP9; action bars)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Progression;

/// <summary>
/// An action bar's shape and rules (the first of six is the quick bar of schema 23, widened from eight slots to twelve at schema 32). A slot
/// is empty (null) or holds what it uses, by definition ID: a usable item (<c>item.*</c>, the first carried stack of it), a known formula
/// (<c>spell.*</c>), a known technique (<c>ability.*</c>) or a gear-set switch (<c>gearset.set_N</c>, N = 1 to 10). A thing is on one bar at
/// most once.
/// </summary>
public static class QuickBar
{
    public const int Slots = 12;

    /// <summary>The bars the character has: the first is the main bar, the others are paged to it or shown beside it.</summary>
    public const int Bars = 6;

    /// <summary>The gear sets a character may keep.</summary>
    public const int GearSets = 10;

    private const string GearSetPrefix = "gearset.set_";

    /// <summary>The slot content that switches to gear set <paramref name="index"/> (1 to 10).</summary>
    public static string GearSetId(int index) => $"{GearSetPrefix}{index}";

    /// <summary>The gear set a slot content switches to (1 to 10), or false when it is no gear-set switch.</summary>
    public static bool TryGearSet(string id, out int index)
    {
        index = 0;
        return id.StartsWith(GearSetPrefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(GearSetPrefix.Length), out index)
               && index is >= 1 and <= GearSets && GearSetId(index) == id;
    }

    /// <summary>Where a newly carried item goes first (keys 1 to 3, as the quick slots were) before any other free slot.</summary>
    public static readonly ImmutableArray<int> ItemOrder = ImmutableArray.Create(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);

    /// <summary>Where a newly known formula or technique goes first (keys 4 to 8, as the formulas were) before any other free slot.</summary>
    public static readonly ImmutableArray<int> WorkingOrder = ImmutableArray.Create(3, 4, 5, 6, 7, 0, 1, 2, 8, 9, 10, 11);

    public static ImmutableArray<string?> Empty { get; } = ImmutableArray.Create(new string?[Slots]);

    /// <summary>What a slot may hold: an item, formula or technique definition, or a gear-set switch.</summary>
    public static bool CanHold(string id) =>
        TryGearSet(id, out _) || DefinitionId.IsValid(id) && (id.StartsWith("item.", StringComparison.Ordinal) || id.StartsWith("spell.", StringComparison.Ordinal)
                                     || id.StartsWith("ability.", StringComparison.Ordinal));

    /// <summary>Twelve slots, each empty or holding something a slot may hold, nothing twice.</summary>
    public static bool IsValid(ImmutableArray<string?> bar) =>
        !bar.IsDefault && bar.Length == Slots && bar.All(s => s is null || CanHold(s))
        && bar.OfType<string>().Distinct(StringComparer.Ordinal).Count() == bar.Count(s => s is not null);

    /// <summary>
    /// <paramref name="id"/> put on <paramref name="slot"/>: from another slot it swaps with what was there; null clears the slot.
    /// </summary>
    public static ImmutableArray<string?> Assign(ImmutableArray<string?> bar, int slot, string? id)
    {
        var slots = bar.ToBuilder();
        if (id is not null && bar.IndexOf(id) is var from and >= 0)
            slots[from] = bar[slot];
        slots[slot] = id;
        return slots.ToImmutable();
    }

    /// <summary>The first free slot in <paramref name="order"/>, or -1 when the bar is full.</summary>
    public static int FreeSlot(ImmutableArray<string?> bar, ImmutableArray<int> order)
    {
        foreach (int slot in order)
        {
            if (bar[slot] is null)
                return slot;
        }
        return -1;
    }
}
