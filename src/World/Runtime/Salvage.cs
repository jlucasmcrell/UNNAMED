using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

public sealed record SalvageCommand(EntityId Actor, EntityId Item) : GameCommand(Actor);
public sealed record SalvageView(string? Refusal, int Skill, int RecoveryPercent, ImmutableArray<RecipeInput> Materials)
{
    /// <summary>
    /// What becomes of each enhancement the piece carries (owner ruling 2026-10-21), in slot order: recovered whole (its own item), only its
    /// essence (an essence item), or nothing (null).
    /// </summary>
    public ImmutableArray<(string Enhancement, string? Returns)> Enhancements { get; init; } = ImmutableArray<(string, string?)>.Empty;
}
public sealed record ItemSalvaged(string ItemId, ImmutableArray<RecipeInput> Materials, long Tick);

internal sealed partial class CraftingSystem
{
    public SalvageView Salvage(EntityId item, long tick)
    {
        int skill = ProgressionEngine.SkillLevel(State.Progression, SalvageRules.SkillId);
        SalvageView Refuse(string reason) => new(reason, skill, 0, ImmutableArray<RecipeInput>.Empty);
        if (State.PlayerCombat.Defeated) return Refuse("You cannot salvage while defeated.");
        if (State.PlayerCombat.Action.PhaseAt(tick, _context.Setup.Combat.Constants).Phase != CombatPhase.Idle) return Refuse("Finish your current action first.");
        var entry = State.Inventory.FirstOrDefault(e => e.ItemId == item);
        if (entry is null) return Refuse("That item is no longer carried.");
        if (entry.Locked) return Refuse("Unlock this item first.");
        if (State.Equipment.ContainsValue(item) || State.Bags.Contains(item)) return Refuse("Take this item off first.");
        var definition = _context.Setup.Items.Catalog.Find(entry.DefId);
        if (definition?.Heritage is not null)
            return Refuse("Heritage is never broken down.");
        if (definition?.Class is UNNAMED.Domain.Items.ItemClass.Notable or UNNAMED.Domain.Items.ItemClass.Artefact
            || entry.Notable?.Provenance == UNNAMED.Domain.Items.ProvenanceKind.Given)
            return Refuse("Keepsakes, named rewards and artefacts cannot be salvaged.");
        var plan = definition is null ? null : SalvageRules.Plan(definition, Setup.Recipes.Values, _context.Setup.Items.Catalog);
        if (plan is null) return Refuse("This item has no recoverable construction materials.");
        int percent = SalvageRules.RecoveryPercent(skill, definition!, entry.Wear, Setup.Constants);
        var channel = SalvageChannel(entry);
        var materials = SalvageRules.Recover(plan, percent, i => channel.UInt64((uint)i) / SampleScale);
        // An enhancement comes back whole above a skill set by its tier, else it is destroyed and leaves at most its essence; the better of the
        // salvaging and enchanting hands does the work (owner ruling 2026-10-21).
        var rules = _context.Setup.Items.Inventory.Enhancement;
        int hand = Math.Max(skill, ProgressionEngine.SkillLevel(State.Progression, rules.EnchantingSkill));
        var enhancements = (entry.Enhancements?.Filled ?? ImmutableArray<string>.Empty).Select((id, i) =>
            (id, _context.Setup.Items.Catalog.Find(id)?.Enhancement is { } spec
                ? UNNAMED.Domain.Items.EnhancementRulesEngine.Salvaged(id, hand, spec, rules, channel.UInt64((uint)(200 + i)) / SampleScale)
                : null)).ToImmutableArray();
        return new(null, skill, percent, materials) { Enhancements = enhancements };
    }

    private RngChannel SalvageChannel(InventoryEntry entry) => RngChannel.Open(State.World.WorldSeed,
        _context.CellOfSite(SpaceId.Exterior, 0, 0), "salvage", $"{entry.ItemId}/{entry.Count}");

    public string? Handle(SalvageCommand command, long tick)
    {
        if (command.Actor != _player) return "Unknown actor.";
        var preview = Salvage(command.Item, tick);
        if (preview.Refusal is { } refusal) return refusal;
        var entry = State.Inventory.Single(e => e.ItemId == command.Item);
        var plan = SalvageRules.Plan(_context.Setup.Items.Catalog.Get(entry.DefId), Setup.Recipes.Values, _context.Setup.Items.Catalog)!;
        var gives = preview.Materials.Select(m => new StackGive(m.ItemId, m.Count))
            .Concat(preview.Enhancements.Where(e => e.Returns is not null).Select(e => new StackGive(e.Returns!, 1))).ToImmutableArray();
        if (_context.Dispatch(new ExchangeItemBatch(ImmutableArray.Create(new StackTake(entry.ItemId, 1)), gives)) is { } full) return full;
        _context.Dispatch(new PracticeSkill(new SkillPractice(SalvageRules.SkillId, plan.Difficulty,
            gives.IsEmpty ? PracticeOutcome.Failure : PracticeOutcome.Success, tick) { NoveltyKey = entry.DefId }));
        if (plan.RecipeId is { } recipe && !ProgressionEngine.Knows(State.Progression, recipe)
            && SalvageChannel(entry).UInt64(100) / SampleScale < Setup.Constants.SalvageHintPercent / 100.0)
        {
            var finding = new Finding(FindingKind.Hint, entry.DefId, "salvage", recipe, plan.Materials.Select(m => m.ItemId).ToImmutableArray(), tick);
            if (!State.Findings.Any(f => f.Key == finding.Key))
            {
                State.SetFindings(_owner, State.Findings.Add(finding));
                _context.Events.Publish(new FindingRecorded(finding));
            }
        }
        _context.Events.Publish(new ItemSalvaged(entry.DefId, preview.Materials, tick));
        return null;
    }
}
