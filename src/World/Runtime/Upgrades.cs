// UNNAMED World - reforging a piece up a tier, and cutting a working into it at an enchanting bench (queue #5; canon 13 §6,
// crafting_webs/enchanting_runecraft, SKILLS_AND_TECHNIQUES §Enchanting)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>
/// Reforge a carried weapon or piece of armour a tier up (canon 13 §6.2) at a station of its repair's kind within reach: sound (repaired
/// first), below the top tier, its repair skill at <see cref="UpgradeRules.SkillFor"/> and <see cref="UpgradeRules.MaterialFor"/> of its repair
/// material carried and spent. A working on it loses a point of stability.
/// </summary>
public sealed record UpgradeCommand(EntityId Actor, EntityId Item) : GameCommand(Actor);

/// <summary>
/// Cut a working into a carried piece of its kind at an enchanting bench within reach: enchanting at the working's least, its anchor material
/// carried and spent. A working already on the piece is cut away for it.
/// </summary>
public sealed record InscribeCommand(EntityId Actor, EntityId Item, string WorkingId) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

public sealed record ItemUpgraded(EntityId Item, string DefId, int Tier, string Station, long Tick);

/// <summary>A working was cut into a piece, with this much stability; <see cref="Replaced"/> names the working it cut away, if any.</summary>
public sealed record ItemInscribed(EntityId Item, string DefId, string WorkingId, int Stability, string? Replaced, string Station, long Tick);

/// <summary>A working ran out of stability (from use, or a reforge) and faded; the piece is plain again and otherwise unharmed.</summary>
public sealed record InscriptionFaded(EntityId Item, string DefId, string WorkingId, long Tick);

/// <summary>To <see cref="InventorySystem"/>: a carried piece's work is now this.</summary>
internal sealed record SetWork(EntityId Item, ItemWork? Work) : InternalCommand;

// ── system ──────────────────────────────────────────────────────────────────

/// <summary>Owns no state: it checks the station, the hand and the materials, then has the inventory spend and set the piece's work.</summary>
internal sealed class UpgradeSystem
{
    private readonly SystemContext _context;
    private readonly EntityId _player;

    public UpgradeSystem(SystemContext context, EntityId player)
    {
        _context = context;
        _player = player;
    }

    private RuntimeState State => _context.State;
    private UpgradeRules Rules => _context.Setup.Crafting.Upgrades;

    public string? Handle(UpgradeCommand command, long tick)
    {
        if (Ready(command.Actor, command.Item, tick) is { } why)
            return why;
        var entry = State.Inventory.Single(e => e.ItemId == command.Item);
        if (_context.Setup.Items.Catalog.Find(entry.DefId) is not { Repair: { } repair } definition || (definition.Weapon is null && definition.ArmorValue <= 0))
            return $"{entry.DefId} is not a weapon or armour a smith can reforge";
        int tier = entry.Work?.Tier ?? 0;
        if (tier >= Rules.MaxTier)
            return $"{definition.Id} is already reforged as far as it goes (tier {Rules.MaxTier})";
        if (Durability.NeedsRepair(entry.Quality, entry.Wear))
            return $"{definition.Id} must be repaired before it is reforged";
        if (Station(repair.StationKind) is not { } station)
            return $"no {repair.StationKind} in reach";
        int skill = ProgressionEngine.SkillLevel(State.Progression, repair.SkillId);
        int needs = Rules.SkillFor(repair, tier);
        if (skill < needs)
            return $"reforging {definition.Id} to tier {tier + 1} needs {repair.SkillId} {needs}; you have {skill}";
        if (Spend(repair.MaterialId, Rules.MaterialFor(tier)) is { } missing)
            return missing;
        var work = UpgradeRules.Reforged(entry.Work, out var faded);
        if (_context.Dispatch(new SetWork(entry.ItemId, work)) is { } failed)
            return failed;
        _context.Dispatch(new PracticeSkill(new SkillPractice(repair.SkillId, needs, PracticeOutcome.Success, tick)));
        _context.Events.Publish(new ItemUpgraded(entry.ItemId, definition.Id, tier + 1, station.Key, tick));
        if (faded is not null)
            _context.Events.Publish(new InscriptionFaded(entry.ItemId, definition.Id, faded.WorkingId, tick));
        return null;
    }

    public string? Handle(InscribeCommand command, long tick)
    {
        if (Ready(command.Actor, command.Item, tick) is { } why)
            return why;
        var entry = State.Inventory.Single(e => e.ItemId == command.Item);
        if (!_context.Setup.Crafting.Workings.TryGetValue(command.WorkingId, out var working))
            return $"{command.WorkingId} is not a working";
        var definition = _context.Setup.Items.Catalog.Find(entry.DefId);
        bool fits = working.Target == WorkingTarget.Weapon ? definition?.Weapon is not null : definition is { ArmorValue: > 0 };
        if (!fits)
            return $"{working.Name} is cut into {(working.Target == WorkingTarget.Weapon ? "a weapon" : "armour")}, not {entry.DefId}";
        if (Station(Rules.EnchantingStation) is not { } station)
            return $"no {Rules.EnchantingStation} in reach";
        int skill = ProgressionEngine.SkillLevel(State.Progression, Rules.EnchantingSkill);
        if (skill < working.SkillMin)
            return $"{working.Name} needs {Rules.EnchantingSkill} {working.SkillMin}; you have {skill}";
        if (Spend(working.MaterialId, working.MaterialCount) is { } missing)
            return missing;
        int stability = Rules.StabilityOf(working, entry.Quality, skill);
        var work = ItemWork.Of(entry.Work?.Tier ?? 0, new Inscription(working.Id, stability));
        if (_context.Dispatch(new SetWork(entry.ItemId, work)) is { } failed)
            return failed;
        _context.Dispatch(new PracticeSkill(new SkillPractice(Rules.EnchantingSkill, working.SkillMin + 5, PracticeOutcome.Success, tick)));
        _context.Events.Publish(new ItemInscribed(entry.ItemId, entry.DefId, working.Id, stability, entry.Work?.Inscription?.WorkingId, station.Key, tick));
        return null;
    }

    private string? Ready(EntityId actor, EntityId item, long tick)
    {
        if (actor != _player)
            return $"unknown actor {actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        if (State.PlayerCombat.Action.PhaseAt(tick, _context.Setup.Combat.Constants).Phase != CombatPhase.Idle)
            return "busy";
        return State.Inventory.Any(e => e.ItemId == item) ? null : $"{item} is not carried";
    }

    /// <summary>The nearest-keyed station of this kind in reach in the space the character is in, or null.</summary>
    private StationSite? Station(string kind)
    {
        var body = State.Body;
        long reach = _context.Setup.Items.Inventory.ReachMm;
        return _context.Stations(_player).Where(p => p.Layout.SpaceId == State.Space).Select(p => p.Site)
            .Where(s => s.Kind == kind && Math.Sqrt(Math.Pow(s.XMm - body.XMm, 2) + Math.Pow(s.ZMm - body.ZMm, 2)) <= reach)
            .OrderBy(s => s.Key, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>Spend <paramref name="count"/> of a material from the pack (nothing equipped), or say how short it is.</summary>
    private string? Spend(string materialId, int count)
    {
        var stacks = State.Inventory.Where(e => e.DefId == materialId && !State.Equipment.ContainsValue(e.ItemId))
            .OrderBy(e => e.Quality).ThenBy(e => e.ItemId.Value, StringComparer.Ordinal).ToList();
        if (stacks.Sum(e => e.Count) < count)
            return $"needs {count} {materialId}";
        var takes = ImmutableArray.CreateBuilder<StackTake>();
        int left = count;
        foreach (var stack in stacks)
        {
            if (left == 0)
                break;
            int take = Math.Min(left, stack.Count);
            takes.Add(new StackTake(stack.ItemId, take));
            left -= take;
        }
        return _context.Dispatch(new ExchangeItems(takes.ToImmutable(), null, 0, Domain.Crafting.Quality.Standard));
    }
}
