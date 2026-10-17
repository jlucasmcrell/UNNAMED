// UNNAMED World - the quick bar at run time: what is on each of the eight slots, assigned by the player (PJ09; C26; WP9)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Progression;

namespace UNNAMED.World.Runtime;

/// <summary>Put a usable item, a known formula or a known technique on a quick slot (0-7), or clear the slot (null).</summary>
public sealed record AssignQuickSlotCommand(EntityId Actor, int Slot, string? DefinitionId) : GameCommand(Actor);

/// <summary>A quick slot now holds <see cref="DefinitionId"/>, or nothing.</summary>
public sealed record QuickSlotAssigned(int Slot, string? DefinitionId, long Tick);

/// <summary>
/// Owns: <see cref="StateSlice.QuickBar"/>. The player assigns the slots. What comes to hand is offered a free slot as it does: a usable
/// item the character starts to carry (keys 1 to 3 first) and a formula or technique they come to know (keys 4 to 8 first). What is
/// already on the bar stays where it is, even while none of it is carried. What the player cleared off the bar is kept off it while it is
/// carried or known, across logins too, until they put it back.
/// </summary>
internal sealed class QuickBarSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;

    // What was offered from the pack and the knowledge record, and the arrays it was read from: transient, so a load offers again what
    // is carried or known and not on the bar.
    private readonly HashSet<string> _offered = new(StringComparer.Ordinal);
    private ImmutableArray<InventoryEntry> _inventory;
    private ImmutableSortedDictionary<string, KnownTechnique>? _known;

    public QuickBarSystem(SystemContext context, SliceOwner owner, EntityId player)
    {
        _context = context;
        _owner = owner;
        _player = player;
    }

    private RuntimeState State => _context.State;

    public string? Handle(AssignQuickSlotCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (command.Slot is < 0 or >= QuickBar.Slots)
            return $"there is no quick slot {command.Slot + 1}";
        if (command.DefinitionId is { } id && Refusal(id) is { } refused)
            return refused;
        var bar = QuickBar.Assign(State.QuickBar, command.Slot, command.DefinitionId);
        if (bar.SequenceEqual(State.QuickBar))
            return null;
        var was = State.QuickBar;
        State.SetQuickBar(_owner, bar);
        // What left the bar was cleared on purpose (a swap keeps both on it); what is on it now is not.
        State.SetQuickBarCleared(_owner, State.QuickBarCleared.Union(was.OfType<string>()).Except(bar.OfType<string>()));
        for (int slot = 0; slot < QuickBar.Slots; slot++)
        {
            if (bar[slot] != was[slot])
                _context.Events.Publish(new QuickSlotAssigned(slot, bar[slot], tick));
        }
        return null;
    }

    /// <summary>Why <paramref name="id"/> cannot go on the bar, or null: an item needs a use, a formula or technique must be known.</summary>
    private string? Refusal(string id)
    {
        if (!QuickBar.CanHold(id))
            return $"{id} cannot go on the quick bar";
        if (id.StartsWith("item.", StringComparison.Ordinal))
            return _context.Setup.Combat.UseEffects.ContainsKey(id) ? null : $"{id} has no use";
        return ProgressionEngine.Knows(State.Progression, id) ? null : $"{id} is not known";
    }

    /// <summary>Offer a free slot to whatever came to hand since the last tick; reads the pack and the knowledge record, never writes them.</summary>
    public void Tick(long tick)
    {
        if (State.Inventory == _inventory && State.Progression.Known == _known)
            return;
        _inventory = State.Inventory;
        _known = State.Progression.Known;
        var uses = _context.Setup.Combat.UseEffects;
        var carried = State.Inventory.Select(e => e.DefId).Where(uses.ContainsKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var magic = _context.Setup.Magic;
        // In the order they were learned, and those learned from one book in the book's own order.
        var workings = State.Progression.Known.Where(k => magic.Formulas.ContainsKey(k.Key) || k.Key.StartsWith("ability.", StringComparison.Ordinal))
            .OrderBy(k => k.Value.Tick)
            .ThenBy(k => k.Value.SourceRef is { } book && magic.Teaches.TryGetValue(book, out var taught) && taught.Contains(k.Key) ? taught.IndexOf(k.Key) : int.MaxValue)
            .ThenBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key).ToList();
        // An item no longer carried may be offered again when it is next picked up, even one cleared off the bar; nothing is unlearned.
        _offered.RemoveWhere(id => id.StartsWith("item.", StringComparison.Ordinal) && !carried.Contains(id));
        if (State.QuickBarCleared.Where(id => id.StartsWith("item.", StringComparison.Ordinal) && !carried.Contains(id)).ToList() is { Count: > 0 } dropped)
            State.SetQuickBarCleared(_owner, State.QuickBarCleared.Except(dropped));
        foreach (var (id, order) in carried.Select(id => (id, QuickBar.ItemOrder)).Concat(workings.Select(id => (id, QuickBar.WorkingOrder))))
        {
            if (!_offered.Add(id) || State.QuickBar.Contains(id) || State.QuickBarCleared.Contains(id))
                continue;
            int slot = QuickBar.FreeSlot(State.QuickBar, order);
            if (slot < 0)
                continue;
            State.SetQuickBar(_owner, State.QuickBar.SetItem(slot, id));
            _context.Events.Publish(new QuickSlotAssigned(slot, id, tick));
        }
    }
}
