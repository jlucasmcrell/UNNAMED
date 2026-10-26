// UNNAMED World - the quick bar at run time: what is on each of the eight slots, assigned by the player (PJ09; C26; WP9)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;

namespace UNNAMED.World.Runtime;

/// <summary>
/// Put a usable item, a known formula, a known technique or a saved gear set's switch on a slot (0-11) of an action bar (<see cref="Bar"/>, 0-5;
/// 0 is the quick bar), or clear the slot (null).
/// </summary>
public sealed record AssignQuickSlotCommand(EntityId Actor, int Slot, string? DefinitionId, int Bar = 0) : GameCommand(Actor);

/// <summary>A slot of an action bar (the quick bar is 0) now holds <see cref="DefinitionId"/>, or nothing.</summary>
public sealed record QuickSlotAssigned(int Slot, string? DefinitionId, long Tick, int Bar = 0);

/// <summary>The held weapon's family changed, and the main bar now shows <see cref="Family"/>'s layout.</summary>
public sealed record MainBarFollowedWeapon(string Family, long Tick);

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
        if (command.Bar is < 0 or >= QuickBar.Bars)
            return $"there is no action bar {command.Bar + 1}";
        if (command.Slot is < 0 or >= QuickBar.Slots)
            return $"there is no slot {command.Slot + 1} on action bar {command.Bar + 1}";
        if (command.DefinitionId is { } id && Refusal(id) is { } refused)
            return refused;
        var was = State.ActionBars[command.Bar];
        var bar = QuickBar.Assign(was, command.Slot, command.DefinitionId);
        if (bar.SequenceEqual(was))
            return null;
        if (command.Bar == 0)
            SetMain(bar);
        else
            State.SetActionBar(_owner, command.Bar, bar);
        // What left every bar was cleared on purpose (a swap keeps both on it); what is on one now is not.
        var onBars = State.ActionBars.SelectMany(b => b).OfType<string>().ToHashSet(StringComparer.Ordinal);
        State.SetQuickBarCleared(_owner, State.QuickBarCleared.Union(was.OfType<string>()).Where(id => !onBars.Contains(id) && !QuickBar.TryGearSet(id, out _))
            .ToImmutableSortedSet(StringComparer.Ordinal));
        for (int slot = 0; slot < QuickBar.Slots; slot++)
        {
            if (bar[slot] != was[slot])
                _context.Events.Publish(new QuickSlotAssigned(slot, bar[slot], tick, command.Bar));
        }
        return null;
    }

    /// <summary>The main bar set to <paramref name="bar"/>: it is the held family's layout, so that layout is kept with it.</summary>
    private void SetMain(ImmutableArray<string?> bar) =>
        State.SetMainBar(_owner, State.HeldFamily, bar, State.HeldFamily.Length > 0 ? State.FamilyBars.SetItem(State.HeldFamily, bar) : State.FamilyBars);

    /// <summary>The family of what is in the main hand: the weapon's skill, or bare hands.</summary>
    private string CurrentFamily()
    {
        if (!State.Equipment.TryGetValue(EquipSlot.MainHand, out var held))
            return WeaponFamilies.Unarmed;
        string? def = State.Inventory.FirstOrDefault(e => e.ItemId == held)?.DefId;
        var weapon = def is null ? null : _context.Setup.Items.Catalog.Find(def)?.Weapon;
        return WeaponFamilies.Of(weapon is not null, weapon?.SkillId);
    }

    /// <summary>
    /// The main bar follows the weapon in hand (canon 26 §8; the owner, 2026-10-18): a change of held family keeps the layout that was showing
    /// under the old family and shows the new family's - which, the first time it is held, is a copy of the layout showing. The first tick of a
    /// character saved without this adopts the family held, so their bar becomes that family's layout.
    /// </summary>
    private void FollowTheWeapon(long tick)
    {
        string family = CurrentFamily();
        if (family == State.HeldFamily)
            return;
        var families = State.HeldFamily.Length > 0 ? State.FamilyBars.SetItem(State.HeldFamily, State.QuickBar) : State.FamilyBars;
        var was = State.QuickBar;
        var bar = families.TryGetValue(family, out var kept) ? kept : was;
        State.SetMainBar(_owner, family, bar, families.SetItem(family, bar));
        for (int slot = 0; slot < QuickBar.Slots; slot++)
        {
            if (bar[slot] != was[slot])
                _context.Events.Publish(new QuickSlotAssigned(slot, bar[slot], tick));
        }
        _context.Events.Publish(new MainBarFollowedWeapon(family, tick));
    }

    /// <summary>Why <paramref name="id"/> cannot go on the bar, or null: an item needs a use, a formula or technique must be known.</summary>
    private string? Refusal(string id)
    {
        if (!QuickBar.CanHold(id))
            return $"{id} cannot go on an action bar";
        if (QuickBar.TryGearSet(id, out int set))
            return State.GearSets.Any(g => g.Index == set) ? null : $"there is no gear set {set}";
        if (id.StartsWith("item.", StringComparison.Ordinal))
            return _context.Setup.Combat.UseEffects.ContainsKey(id) ? null : $"{id} has no use";
        return ProgressionEngine.Knows(State.Progression, id) || Racial().Contains(id) ? null : $"{id} is not known";
    }

    /// <summary>The character's racial abilities that are used from a key: innate, never learned, so first on the bar (Orenth blink, Siann ring).</summary>
    private IEnumerable<string> Racial() =>
        _context.PlayerAbilities.Where(a => !a.Passive && a.Action is not (PeopleAction.Glide or PeopleAction.Flare or PeopleAction.Leap)).Select(a => a.Id);

    /// <summary>Offer a free slot to whatever came to hand since the last tick; reads the pack and the knowledge record, never writes them.</summary>
    public void Tick(long tick)
    {
        FollowTheWeapon(tick);
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
        workings.InsertRange(0, Racial());
        // An item no longer carried may be offered again when it is next picked up, even one cleared off the bar; nothing is unlearned.
        _offered.RemoveWhere(id => id.StartsWith("item.", StringComparison.Ordinal) && !carried.Contains(id));
        if (State.QuickBarCleared.Where(id => id.StartsWith("item.", StringComparison.Ordinal) && !carried.Contains(id)).ToList() is { Count: > 0 } dropped)
            State.SetQuickBarCleared(_owner, State.QuickBarCleared.Except(dropped));
        foreach (var (id, order) in carried.Select(id => (id, QuickBar.ItemOrder)).Concat(workings.Select(id => (id, QuickBar.WorkingOrder))))
        {
            if (!_offered.Add(id) || State.ActionBars.Any(b => b.Contains(id)) || State.FamilyBars.Values.Any(b => b.Contains(id)) || State.QuickBarCleared.Contains(id))
                continue;
            int slot = QuickBar.FreeSlot(State.QuickBar, order);
            if (slot < 0)
                continue;
            SetMain(State.QuickBar.SetItem(slot, id));
            _context.Events.Publish(new QuickSlotAssigned(slot, id, tick));
        }
    }
}
