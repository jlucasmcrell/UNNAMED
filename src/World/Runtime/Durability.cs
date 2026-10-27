// UNNAMED World - wear and repair: durability from use, and repair at a station (WP3b; SYSTEMS_COMPLETENESS_PLAN §2.4 I10, D5)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>
/// Repair a carried piece at a station of its repair's kind within reach (C13:226): its skill at least the repair's least level, one of its
/// material carried and spent. The quality is re-rolled from the skill and the material, and capped at the quality it was made at.
/// </summary>
public sealed record RepairCommand(EntityId Actor, EntityId Item) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

/// <summary>A piece wore out (D5): it counts as crude until it is repaired, and is never lost.</summary>
public sealed record ItemWornOut(EntityId Item, string DefId, long Tick);

/// <summary>
/// A piece was repaired: at a station (<see cref="By"/> its key, <see cref="Coin"/> 0) or by a trader (<see cref="By"/> their NPC ID, for
/// <see cref="Coin"/>), to this quality.
/// </summary>
public sealed record ItemRepaired(EntityId Item, string DefId, int Quality, string By, long Coin, long Tick);

/// <summary>To <see cref="InventorySystem"/>: what is equipped in these slots took this much wear - each piece once, however many slots it fills.</summary>
internal sealed record WearEquipped(ImmutableArray<EquipSlot> Slots, int Points) : InternalCommand;

/// <summary>To <see cref="InventorySystem"/>: a carried piece repaired - no wear, and <see cref="Rolled"/> capped at its made quality.</summary>
internal sealed record RepairItem(EntityId Item, int Rolled) : InternalCommand;

// ── systems ─────────────────────────────────────────────────────────────────

/// <summary>
/// Owns no state. Wear (D5) comes from what the character does, read from events and never from combat's or gathering's insides: a blow the
/// character lands with the main-hand weapon wears it; a blow they take wears what is worn where it landed - or, blocked, the shield, else the weapon
/// that took it; a harvest wears a tool in the main hand. A piece wears only while it is equipped, and only the character's: while they are
/// away they do nothing, so nothing of theirs wears (§2.4). Repair at a station is this system's too; a trader's repair is the trade's.
/// </summary>
internal sealed class DurabilitySystem : IAbstractAdvance
{
    private const double SampleScale = 18446744073709551616.0;

    private readonly SystemContext _context;
    private readonly EntityId _player;

    public DurabilitySystem(SystemContext context, EntityId player)
    {
        _context = context;
        _player = player;
    }

    private RuntimeState State => _context.State;
    private DurabilityRules Rules => _context.Setup.Items.Inventory.Durability;

    public void Listen(SimulationEvents events)
    {
        events.Listen<HitResolved>(Hit);
        events.Listen<NodeGathered>(_ => Gathered());
    }

    /// <summary>The character does nothing while away, so nothing they carry wears (§2.4): closed-form, and nothing to do.</summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
    }

    private void Hit(HitResolved e)
    {
        if (e.Dodged)
            return;
        if (e.Attacker == _player && e.Target != _player)
        {
            // A blow of the weapon itself: a working cast or a bare-handed blow wears nothing.
            if (Equipped(EquipSlot.MainHand) is { } weapon && e.Source == weapon.DefId)
                Wear(Rules.WearPerHit, EquipSlot.MainHand);
        }
        else if (e.Target == _player)
        {
            if (e.Blocked)
                Wear(Rules.WearPerHitTaken, Wears(EquipSlot.OffHand) ? EquipSlot.OffHand : EquipSlot.MainHand);
            else
                Wear(Rules.WearPerHitTaken, e.Region switch
                {
                    BodyRegion.Head => new[] { EquipSlot.Head },
                    BodyRegion.Torso => new[] { EquipSlot.Chest, EquipSlot.Cloak },
                    _ => new[] { EquipSlot.Hands, EquipSlot.Legs, EquipSlot.Feet },
                });
        }
    }

    /// <summary>A harvest wears the tool in the main hand; a weapon there is not worked against the node.</summary>
    private void Gathered()
    {
        if (Equipped(EquipSlot.MainHand) is { } entry && _context.Setup.Items.Catalog.Find(entry.DefId) is { Category: "tool" })
            Wear(Rules.WearPerGather, EquipSlot.MainHand);
    }

    private void Wear(int points, params EquipSlot[] slots)
    {
        if (points > 0)
            _context.Dispatch(new WearEquipped(slots.ToImmutableArray(), points));
    }

    private bool Wears(EquipSlot slot) => Equipped(slot) is { } entry && _context.Setup.Items.Catalog.Find(entry.DefId) is { DurabilityMax: > 0 };

    private InventoryEntry? Equipped(EquipSlot slot) =>
        State.Equipment.TryGetValue(slot, out var id) ? State.Inventory.FirstOrDefault(e => e.ItemId == id) : null;

    public string? Handle(RepairCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        if (State.PlayerCombat.Action.PhaseAt(tick, _context.Setup.Combat.Constants).Phase != CombatPhase.Idle)
            return "busy";
        if (State.Inventory.FirstOrDefault(e => e.ItemId == command.Item) is not { } entry)
            return $"{command.Item} is not carried";
        if (_context.Setup.Items.Catalog.Find(entry.DefId) is not { Repair: { } repair } definition)
            return $"{entry.DefId} is not something a station repairs";
        if (!Durability.NeedsRepair(entry.Quality, entry.Wear))
            return $"{definition.Id} needs no repair";
        bool field=repair.MaterialId is "item.material.iron_ingot" or "item.material.bog_iron_ingot" or "item.material.grey_iron_ingot";
        if (_context.Stations(_player).Where(p=>p.Layout.SpaceId==State.Space).Select(p=>p.Site)
                .Where(s => (s.Kind == repair.StationKind || field && s.Kind=="cookfire") && InReach(s))
                .OrderBy(s=>s.Kind=="cookfire").ThenBy(s => s.Key, StringComparer.Ordinal).FirstOrDefault() is not { } station)
            return $"no {repair.StationKind} in reach";
        int skill = ProgressionEngine.SkillLevel(State.Progression, repair.SkillId);
        if(station.Kind=="cookfire" && (entry.Wear?.Points??0)==0 && entry.Quality>=Quality.Standard)
            return "This field repair is already sound; a proper station can restore its original make";
        if (skill < repair.SkillMin)
            return $"repairing {definition.Id} needs {repair.SkillId} {repair.SkillMin}; you have {skill}";
        // The best of the material carried: like a recipe's inputs, it caps the work.
        if (State.Inventory.Where(e => e.DefId == repair.MaterialId && !State.Equipment.ContainsValue(e.ItemId))
                .OrderByDescending(e => e.Quality).ThenBy(e => e.ItemId.Value, StringComparer.Ordinal).FirstOrDefault() is not { } material)
            return $"needs 1 {repair.MaterialId}";

        var body = State.Body;
        var channel = RngChannel.Open(State.World.WorldSeed, CellKey.OfWorld(body.XMm / 1000.0, body.ZMm / 1000.0), "repair", $"{entry.ItemId}@{tick}");
        int rolled = CraftingRules.Roll(material.Quality, skill, repair.SkillMin, _context.Setup.Crafting.Constants, channel.UInt64(0) / SampleScale);
        if(station.Kind=="cookfire") rolled=Quality.Standard;
        if (_context.Dispatch(new ExchangeItems(ImmutableArray.Create(new StackTake(material.ItemId, 1)), null, 0, Quality.Standard)) is { } refused)
            return refused;
        if (_context.Dispatch(new RepairItem(entry.ItemId, rolled)) is { } failed)
            return failed;
        _context.Dispatch(new PracticeSkill(new SkillPractice(repair.SkillId, repair.SkillMin, PracticeOutcome.Success, tick)));
        int quality = State.Inventory.Single(e => e.ItemId == entry.ItemId).Quality;
        _context.Events.Publish(new ItemRepaired(entry.ItemId, definition.Id, quality, station.Key, 0, tick));
        return null;
    }

    private bool InReach(StationSite station)
    {
        var body = State.Body;
        return Math.Sqrt(Math.Pow(station.XMm - body.XMm, 2) + Math.Pow(station.ZMm - body.ZMm, 2)) <= _context.Setup.Items.Inventory.ReachMm;
    }
}
