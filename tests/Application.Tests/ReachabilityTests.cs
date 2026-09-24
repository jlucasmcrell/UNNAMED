using UNNAMED.Domain.Items;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;

namespace UNNAMED.Application.Tests;

/// <summary>
/// PROTOTYPE.md C18: "see every enemy, node, and item definition reachable in the world without a debug command" - read off the
/// game's own content as a fresh session builds it: every creature stands in the region, every node is placed, and every item comes
/// from somewhere a fresh character can get to - the starting kit, a container or a corpse's loot, a trader's wares, a node, a recipe
/// whose inputs are themselves reachable, a conversation, or a quest's reward.
/// </summary>
public class ReachabilityTests
{
    [Fact]
    public void EveryCreatureAndNode_StandsInTheRegion()
    {
        using var profile = new TempProfile();
        var setup = Harness.Boot(profile).Setup;

        var spawned = setup.Combat.Spawns.SelectMany(s => s.Members).Select(m => m.CreatureId).ToHashSet(StringComparer.Ordinal);
        Assert.Empty(setup.Combat.Creatures.Keys.Where(c => !spawned.Contains(c)));
        var placed = setup.Layout.Nodes.Select(n => n.NodeDefId).ToHashSet(StringComparer.Ordinal);
        Assert.Empty(setup.Crafting.Nodes.Keys.Where(n => !placed.Contains(n)));
    }

    [Fact]
    public void EveryItem_CanBeGotInAFreshSession()
    {
        using var profile = new TempProfile();
        var setup = Harness.Boot(profile).Setup;
        var items = setup.Items;

        // Loot a fresh character can reach: the region's containers, and the corpses of the creatures that spawn.
        var tables = new Queue<string>(setup.Layout.Containers.Select(c => c.LootTableId)
            .Concat(setup.Combat.Spawns.SelectMany(s => s.Members).Select(m => setup.Combat.Creatures[m.CreatureId].LootTableId).OfType<string>()));
        var seenTables = new HashSet<string>(StringComparer.Ordinal);
        var reachable = new HashSet<string>(items.StartingKit.Select(k => k.ItemId), StringComparer.Ordinal);
        while (tables.TryDequeue(out string? id))
        {
            if (!seenTables.Add(id) || !items.LootTables.TryGetValue(id, out var table))
                continue;
            foreach (var entry in table.Weighted.Concat(table.Independent).Concat(table.Guaranteed))
            {
                if (entry.ItemId is { } item)
                    reachable.Add(item);
                if (entry.TableId is { } nested)
                    tables.Enqueue(nested);
            }
        }
        // Traders who stand in the region, and what their conversations hand over; nodes that are placed; quest rewards.
        var traders = setup.Layout.Npcs.Select(n => setup.Social.Npcs[n.NpcId]).Where(n => n.MerchantId is not null);
        reachable.UnionWith(traders.SelectMany(n => items.Merchants[n.MerchantId!].Stock).Select(s => s.ItemId));
        reachable.UnionWith(setup.Social.Dialogues.Values.SelectMany(d => d.Nodes.Values).SelectMany(n => n.Choices)
            .SelectMany(c => c.Consequences).OfType<TransferItemConsequence>().Where(t => t.ToPlayer).Select(t => t.ItemId));
        reachable.UnionWith(setup.Layout.Nodes.Select(n => setup.Crafting.Nodes[n.NodeDefId].ItemId));
        reachable.UnionWith(setup.Quests.Quests.Values.SelectMany(q => q.Rewards).OfType<ItemReward>().Select(r => r.ItemId));
        // Recipes whose every input is reachable, until nothing more is.
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var recipe in setup.Crafting.Recipes.Values.Where(r => r.Inputs.All(i => reachable.Contains(i.ItemId))))
                grew |= reachable.Add(recipe.OutputItemId);
        }

        Assert.Empty(items.Catalog.Definitions.Keys.Where(id => !reachable.Contains(id)));
    }
}
