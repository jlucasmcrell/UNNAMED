using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

public sealed record SalvageCommand(EntityId Actor, EntityId Item) : GameCommand(Actor);
public sealed record SalvageView(string? Refusal, int Skill, int RecoveryPercent, ImmutableArray<RecipeInput> Materials);
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
        if (definition?.Class is UNNAMED.Domain.Items.ItemClass.Notable or UNNAMED.Domain.Items.ItemClass.Artefact
            || entry.Notable?.Provenance == UNNAMED.Domain.Items.ProvenanceKind.Given)
            return Refuse("Keepsakes, named rewards and artefacts cannot be salvaged.");
        if (definition?.Heritage is { } people)
            return Refuse($"{UNNAMED.Domain.Items.HeritageRules.PeopleWord(people)} heritage is never salvaged.");
        var plan = definition is null ? null : SalvageRules.Plan(definition, Setup.Recipes.Values, _context.Setup.Items.Catalog);
        if (plan is null) return Refuse("This item has no recoverable construction materials.");
        int percent = SalvageRules.RecoveryPercent(skill, definition!, entry.Wear, Setup.Constants);
        var channel = SalvageChannel(entry);
        return new(null, skill, percent, SalvageRules.Recover(plan, percent, i => channel.UInt64((uint)i) / SampleScale));
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
        var gives = preview.Materials.Select(m => new StackGive(m.ItemId, m.Count)).ToImmutableArray();
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
