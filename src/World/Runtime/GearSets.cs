// UNNAMED World - gear sets at run time: save the outfit worn, and switch to one as a timed action (action bars and gear sets)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;

namespace UNNAMED.World.Runtime;

/// <summary>Keep what is worn now as gear set <see cref="Index"/> (1 to 10), under <see cref="Name"/>; a set already there is replaced.</summary>
public sealed record SaveGearSetCommand(EntityId Actor, int Index, string Name) : GameCommand(Actor);

public sealed record DeleteGearSetCommand(EntityId Actor, int Index) : GameCommand(Actor);

/// <summary>Begin switching to gear set <see cref="Index"/>: a timed action (<see cref="GearSetRules"/>), carried out when its time is up.</summary>
public sealed record SwapGearSetCommand(EntityId Actor, int Index) : GameCommand(Actor);

/// <summary>Give up a switch that is under way.</summary>
public sealed record CancelGearSwapCommand(EntityId Actor) : GameCommand(Actor);

public sealed record GearSetSaved(int Index, string Name, long Tick);

public sealed record GearSetDeleted(int Index, long Tick);

/// <summary>A switch began and will be done at <see cref="DueTick"/>.</summary>
public sealed record GearSwapStarted(int Index, long DueTick, bool InCombat, long Tick);

/// <summary>A piece the equipment's own rules refused, and why.</summary>
public sealed record GearSwapRefusal(GearSetItem Item, string Why);

/// <summary>
/// A switch was carried out. <see cref="Missing"/> are the set's pieces that were not carried and <see cref="Refused"/> those the equipment's
/// own rules refused (a requirement not met, the hands busy), each with its reason; their slots were left as they were.
/// </summary>
/// <see cref="ArmourKept"/>: it was a fight, so only the weapon and the off hand were changed and the armour pieces were skipped, not queued.
public sealed record GearSwapped(int Index, ImmutableArray<EquipSlot> Changed, ImmutableArray<GearSetItem> Missing, ImmutableArray<GearSwapRefusal> Refused, long Tick,
    bool ArmourKept = false);

public sealed record GearSwapCancelled(int Index, string Why, long Tick);

/// <summary>A switch under way, as a view shows it.</summary>
public sealed record GearSwapView(int Index, long StartTick, long DueTick, bool InCombat);

/// <summary>
/// Owns: <see cref="StateSlice.GearSets"/>. A set records the very items worn (instances), so a switch is only ever to what is carried: a
/// piece that is not in the pack is reported missing and never made. The switch is a command that takes time - longer in a fight - and
/// changes the equipment only by the equipment system's own commands when the time is up, so every requirement, and the rule that the
/// hands are not changed mid-swing, still apply. A switch under way is transient: a load starts none.
/// </summary>
internal sealed class GearSetSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;
    private readonly Func<EquipCommand, long, string?> _equip;
    private readonly Func<UnequipCommand, long, string?> _unequip;
    private readonly Func<bool> _fighting;
    private GearSwapView? _swap;

    public GearSetSystem(SystemContext context, SliceOwner owner, EntityId player, Func<EquipCommand, long, string?> equip,
        Func<UnequipCommand, long, string?> unequip, Func<bool> fighting)
    {
        _context = context;
        _owner = owner;
        _player = player;
        _equip = equip;
        _unequip = unequip;
        _fighting = fighting;
    }

    private RuntimeState State => _context.State;

    public GearSwapView? Swap => _swap;

    private string? DefOf(EntityId item) => State.Inventory.FirstOrDefault(e => e.ItemId == item)?.DefId;

    public string? Handle(SaveGearSetCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (command.Index is < 1 or > GearSetRules.MaxSets)
            return $"there is no gear set {command.Index}";
        if (GearSetRules.CleanName(command.Name) is not { } name)
            return $"a gear set's name is 1 to {GearSetRules.MaxNameLength} characters";
        if (State.Equipment.Count == 0)
            return "nothing is worn to keep";
        var set = GearSetRules.Capture(command.Index, name, State.Equipment, DefOf);
        State.SetGearSets(_owner, State.GearSets.Where(g => g.Index != command.Index).Append(set).OrderBy(g => g.Index).ToImmutableArray());
        _context.Events.Publish(new GearSetSaved(command.Index, name, tick));
        return null;
    }

    public string? Handle(DeleteGearSetCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.GearSets.All(g => g.Index != command.Index))
            return $"there is no gear set {command.Index}";
        if (_swap?.Index == command.Index)
            return "that set is being put on";
        State.SetGearSets(_owner, State.GearSets.Where(g => g.Index != command.Index).ToImmutableArray());
        _context.Events.Publish(new GearSetDeleted(command.Index, tick));
        return null;
    }

    public string? Handle(SwapGearSetCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.GearSets.FirstOrDefault(g => g.Index == command.Index) is not { } set)
            return $"there is no gear set {command.Index}";
        if (_swap is not null)
            return "already changing gear";
        if (State.PlayerCombat.Defeated)
            return "cannot change gear now";
        bool inCombat = _fighting();
        var plan = GearSetRules.Plan(set, State.Equipment, item => DefOf(item) is not null, inCombat);
        if (!plan.Changes)
            return plan.ArmourKept ? $"{GearSetRules.ArmourStays}: {set.Name} changes nothing in the hands"
                : plan.Missing.IsEmpty ? $"{set.Name} is already worn" : $"{set.Name}: nothing it wears is carried";
        long due = tick + GearSetRules.SwapTicks(inCombat, _context.Setup.TickMilliseconds);
        _swap = new GearSwapView(set.Index, tick, due, inCombat);
        _context.Events.Publish(new GearSwapStarted(set.Index, due, inCombat, tick));
        return null;
    }

    public string? Handle(CancelGearSwapCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (_swap is null)
            return "no change of gear is under way";
        Cancel("stopped", tick);
        return null;
    }

    private void Cancel(string why, long tick)
    {
        if (_swap is not { } swap)
            return;
        _swap = null;
        _context.Events.Publish(new GearSwapCancelled(swap.Index, why, tick));
    }

    /// <summary>Carry out a switch whose time is up. A set deleted meanwhile, or a character no longer on their feet, cancels it.</summary>
    public void Tick(long tick)
    {
        if (_swap is not { } swap || tick < swap.DueTick)
            return;
        if (State.GearSets.FirstOrDefault(g => g.Index == swap.Index) is not { } set)
        {
            Cancel("the set is gone", tick);
            return;
        }
        if (State.PlayerCombat.Defeated)
        {
            Cancel("downed", tick);
            return;
        }
        _swap = null;
        var plan = GearSetRules.Plan(set, State.Equipment, item => DefOf(item) is not null, swap.InCombat);
        var changed = ImmutableArray.CreateBuilder<EquipSlot>();
        var refused = ImmutableArray.CreateBuilder<GearSwapRefusal>();
        foreach (var slot in plan.Unequip)
        {
            if (_unequip(new UnequipCommand(_player, slot), tick) is null)
                changed.Add(slot);
        }
        foreach (var item in plan.Equip)
        {
            if (_equip(new EquipCommand(_player, item.Item), tick) is { } why)
                refused.Add(new GearSwapRefusal(item, why));
            else
                changed.Add(item.Slot);
        }
        _context.Events.Publish(new GearSwapped(set.Index, changed.ToImmutable(), plan.Missing, refused.ToImmutable(), tick, plan.ArmourKept));
    }
}
