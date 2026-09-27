// UNNAMED World - gathering and crafting at run time: harvesting a node, working a recipe at a station
// (SYSTEMS.md S-17, S-19; PROTOTYPE.md C12-C14; the content bible's §12; M3f)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>A skill threshold that adds to a harvest's yield: survival's +1 from level 3 (PROTOTYPE.md §4.1).</summary>
public sealed record YieldBonus(string SkillId, int FromLevel, int Amount);

/// <summary>The gathering and crafting rules a simulation runs under, built from content at boot.</summary>
public sealed record CraftingSetup(
    CraftingConstants Constants,
    ImmutableSortedDictionary<string, NodeDefinition> Nodes,
    ImmutableSortedDictionary<string, RecipeDefinition> Recipes)
{
    public ImmutableArray<YieldBonus> YieldBonuses { get; init; } = ImmutableArray<YieldBonus>.Empty;

    public static CraftingSetup Empty { get; } = new(new CraftingConstants(),
        ImmutableSortedDictionary.Create<string, NodeDefinition>(StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<string, RecipeDefinition>(StringComparer.Ordinal));
}

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>Harvest a node within reach: what it yields goes to the pack, and the node remembers.</summary>
public sealed record GatherCommand(EntityId Actor, string NodeKey) : GameCommand(Actor);

/// <summary>Work a known recipe at a station of its kind within reach, from carried materials.</summary>
public sealed record CraftCommand(EntityId Actor, string RecipeId) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

/// <summary>A node was harvested. <see cref="Spent"/>: it cannot be harvested again until it refills, if it ever does.</summary>
public sealed record NodeGathered(string NodeKey, string NodeDefId, string ItemId, int Count, bool Spent, long Tick);

/// <summary>A recipe was worked: its inputs spent, its output made at this quality.</summary>
public sealed record ItemCrafted(string RecipeId, string ItemId, int Count, int Quality, long Tick);

// ── views ───────────────────────────────────────────────────────────────────

/// <summary>A node as presentation draws it: where, what it gives, and whether it can be harvested now.</summary>
public sealed record NodeView(string Key, string Name, string NodeDefId, string ItemId, long XMm, long ZMm, bool Ready);

// ── systems ─────────────────────────────────────────────────────────────────

/// <summary>
/// Owns: <see cref="StateSlice.Nodes"/> - the harvest records of the region's resource nodes (PERSISTENCE.md §5.5). A node
/// is the world generator's, keyed by name; its record holds its last harvest tick and how many harvests it has had. A
/// finite seam holds its charges and no more; a stand refills at each world-day boundary (PROTOTYPE.md C12).
/// </summary>
internal sealed class GatheringSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;
    private readonly ImmutableArray<(string Key, CellKey Cell, NodeSite Site)> _nodes;

    public GatheringSystem(SystemContext context, SliceOwner owner, EntityId player)
    {
        _context = context;
        _owner = owner;
        _player = player;
        _nodes = context.Setup.Layout.Nodes.Select(site =>
        {
            var cell = CellKey.OfWorld(site.XMm / 1000.0, site.ZMm / 1000.0);
            return (Keys.NodeKey(cell, site.Name, 0), cell, site);
        }).ToImmutableArray();
    }

    private CraftingSetup Setup => _context.Setup.Crafting;
    private RuntimeState State => _context.State;

    public string? Handle(GatherCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        var node = _nodes.FirstOrDefault(n => n.Key == command.NodeKey);
        if (node.Key is null || !Setup.Nodes.TryGetValue(node.Site.NodeDefId, out var definition))
            return $"there is no node '{command.NodeKey}'";
        var body = State.Body;
        if (Math.Sqrt(Math.Pow(node.Site.XMm - body.XMm, 2) + Math.Pow(node.Site.ZMm - body.ZMm, 2)) > _context.Setup.Items.Inventory.ReachMm)
            return "that is out of reach";
        var record = State.World.NodeRecord(node.Cell, node.Key);
        int harvests = record?.HarvestSeq ?? 0;
        if (!Ready(definition, record, tick))
            return definition.Respawn == Respawn.None ? "it is worked out" : "there is nothing to take until it grows back";

        // The yield is rolled from the node and its harvest, and a skilled hand takes more.
        var channel = RngChannel.Open(State.World.WorldSeed, node.Cell, "gather", $"{node.Key}@{harvests + 1}");
        int count = channel.Int(0, definition.YieldMin, definition.YieldMax + 1) + Bonus(definition.SkillId);
        if (_context.Dispatch(new ExchangeItems(ImmutableArray<StackTake>.Empty, definition.ItemId, count, Quality.Standard)) is { } refused)
            return refused;
        State.HarvestNode(_owner, node.Cell, node.Key, tick);
        _context.Dispatch(new PracticeSkill(new SkillPractice(definition.SkillId, definition.Difficulty, PracticeOutcome.Success, tick)
        {
            NoveltyKey = definition.Id,
        }));
        _context.Events.Publish(new NodeGathered(node.Key, definition.Id, definition.ItemId, count,
            !Ready(definition, State.World.NodeRecord(node.Cell, node.Key), tick), tick));
        if (definition.ResourceId is { } resource)
            _context.Dispatch(new RecordDeed(new Deed(DeedKind.Harvested, resource, count, Quality.Standard, null, tick)));
        return null;
    }

    private bool Ready(NodeDefinition definition, NodeHarvest? record, long tick) =>
        CraftingRules.Ready(definition, record?.LastHarvestTick, record?.HarvestSeq ?? 0, tick, _context.Setup.Progression.TicksPerWorldDay);

    private int Bonus(string skillId)
    {
        int level = ProgressionEngine.SkillLevel(State.Progression, skillId);
        return Setup.YieldBonuses.Where(b => b.SkillId == skillId && level >= b.FromLevel).Sum(b => b.Amount);
    }

    public ImmutableArray<NodeView> Views() =>
        _nodes.Where(n => Setup.Nodes.ContainsKey(n.Site.NodeDefId)).Select(n =>
        {
            var definition = Setup.Nodes[n.Site.NodeDefId];
            return new NodeView(n.Key, n.Site.Name, definition.Id, definition.ItemId, n.Site.XMm, n.Site.ZMm,
                Ready(definition, State.World.NodeRecord(n.Cell, n.Key), State.WorldTick));
        }).ToImmutableArray();
}

/// <summary>
/// Owns no state. Works a recipe (S-17): the recipe known, a station of its kind in reach, the materials carried - the
/// best of them spent, since the weakest caps the work - then the output, its quality rolled from the smith's skill
/// against the recipe's complexity. The work trains its skill through the difficulty gate; the first of each output also
/// earns level XP, once (AG-7). No rank gates anything: a novice may try the hardest recipe and make something crude.
/// </summary>
internal sealed class CraftingSystem
{
    private const double SampleScale = 18446744073709551616.0;

    private readonly SystemContext _context;
    private readonly EntityId _player;

    public CraftingSystem(SystemContext context, EntityId player)
    {
        _context = context;
        _player = player;
    }

    private CraftingSetup Setup => _context.Setup.Crafting;
    private RuntimeState State => _context.State;

    public string? Handle(CraftCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        if (State.PlayerCombat.Action.PhaseAt(tick, _context.Setup.Combat.Constants).Phase != CombatPhase.Idle)
            return "busy";
        if (!Setup.Recipes.TryGetValue(command.RecipeId, out var recipe))
            return $"{command.RecipeId} is not a recipe this build knows";
        if (!ProgressionEngine.Knows(State.Progression, recipe.Id))
            return $"{recipe.Id} is not known";
        var body = State.Body;
        long reach = _context.Setup.Items.Inventory.ReachMm;
        if (!_context.Stations().Any(s => s.Kind == recipe.StationKind
                && Math.Sqrt(Math.Pow(s.XMm - body.XMm, 2) + Math.Pow(s.ZMm - body.ZMm, 2)) <= reach))
            return $"no {recipe.StationKind} in reach";

        var takes = ImmutableArray.CreateBuilder<StackTake>();
        int weakest = recipe.Inputs.IsEmpty ? Quality.Standard : Quality.Fine;
        foreach (var input in recipe.Inputs)
        {
            int left = input.Count;
            foreach (var stack in State.Inventory.Where(e => e.DefId == input.ItemId && !State.Equipment.ContainsValue(e.ItemId))
                         .OrderByDescending(e => e.Quality).ThenBy(e => e.ItemId.Value, StringComparer.Ordinal))
            {
                int take = Math.Min(left, stack.Count);
                takes.Add(new StackTake(stack.ItemId, take));
                weakest = Math.Min(weakest, stack.Quality);
                left -= take;
                if (left == 0)
                    break;
            }
            if (left > 0)
                return $"needs {input.Count} {input.ItemId}";
        }

        int skill = ProgressionEngine.SkillLevel(State.Progression, recipe.SkillId);
        var channel = RngChannel.Open(State.World.WorldSeed, CellKey.OfWorld(body.XMm / 1000.0, body.ZMm / 1000.0), "craft", $"{recipe.Id}@{tick}");
        int quality = recipe.QualityRoll
            ? CraftingRules.Roll(weakest, skill, recipe.Complexity, Setup.Constants, channel.UInt64(0) / SampleScale)
            : weakest;
        if (_context.Dispatch(new ExchangeItems(takes.ToImmutable(), recipe.OutputItemId, recipe.OutputCount, quality)) is { } refused)
            return refused;
        _context.Dispatch(new PracticeSkill(new SkillPractice(recipe.SkillId, recipe.Complexity, PracticeOutcome.Success, tick)
        {
            NoveltyKey = recipe.OutputItemId,
        }));
        if (recipe.FirstTimeXp > 0)
            _context.Dispatch(new AwardExperience(new XpAward(XpSource.Production, recipe.FirstTimeXp, tick) { FirstKey = recipe.OutputItemId }));
        _context.Events.Publish(new ItemCrafted(recipe.Id, recipe.OutputItemId, recipe.OutputCount, quality, tick));
        _context.Dispatch(new RecordDeed(new Deed(DeedKind.Crafted, recipe.OutputItemId, recipe.OutputCount, quality, null, tick)));
        return null;
    }
}
