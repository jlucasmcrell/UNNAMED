using UNNAMED.Domain.Items;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// PROTOTYPE.md C18: "see every enemy, node, and item definition reachable in the world without a debug command" - read off the
/// game's own content as a fresh session builds it: every creature stands in the region, every node is placed, and every item comes
/// from somewhere a fresh character can get to - the starting kit, a container or a corpse's loot, a trader's wares, a node, a recipe
/// whose inputs are themselves reachable, a conversation, or a quest's reward. And every quest step points at something the region
/// has: a place in it, a person standing in it and a line of theirs a reply leads to, a creature that spawns there, an item that can be got.
/// </summary>
public class ReachabilityTests
{
    /// <remarks>
    /// Both regions a run can stand in, for the same reason <see cref="EveryQuestStep_PointsAtSomethingInTheRegion"/>
    /// checks both: the Ring Custodian is region.barrowworks' and the start area's run correctly does not carry it,
    /// so checking it here alone would report a creature no player can fail to meet.
    /// </remarks>
    [Fact]
    public void EveryCreatureAndNode_StandsInTheRegion()
    {
        using var profile = new TempProfile();
        var setup = Harness.Boot(profile).Setup;
        using var ringProfile = new TempProfile();
        var ring = Harness.Boot(ringProfile, "region.barrowworks", barrowworks: true).Setup;

        // The CATALOGS are world-wide - BuildCreatures and BuildCrafting take no region - and only the SPAWNS and the
        // placed nodes are region-scoped. So this asks "is every catalogued creature spawned somewhere a player can
        // reach", which is a question about the world, and the union is the scope it needs. It is NOT a weakening to
        // be tightened later: asking one region to spawn every catalogued creature would demand the Ashen Hollow
        // spawn the Ring Custodian, which the design does not.
        var spawned = setup.Combat.Spawns.SelectMany(s => s.Members).Select(m => m.CreatureId).ToHashSet(StringComparer.Ordinal);
        // A creature an item brings is met by buying the item, not by walking into a spawn: the hound pup is bought at Ashen Hollow and
        // grows into the wolf (spec 43), so no spawn places one and none should (WP-pets).
        foreach (var item in setup.Items.Catalog.Definitions.Values.Where(i => i.PetCreature is { Length: > 0 }))
            spawned.Add(item.PetCreature!);
        spawned.UnionWith(ring.Combat.Spawns.SelectMany(s => s.Members).Select(m => m.CreatureId));
        Assert.Empty(setup.Combat.Creatures.Keys.Where(c => !spawned.Contains(c)));
        var placed = setup.Layout.Nodes.Select(n => n.NodeDefId).ToHashSet(StringComparer.Ordinal);
        placed.UnionWith(ring.Layout.Nodes.Select(n => n.NodeDefId));
        Assert.Empty(setup.Crafting.Nodes.Keys.Where(n => !placed.Contains(n)));
    }

    [Fact]
    public void EveryItem_CanBeGotInAFreshSession()
    {
        using var profile = new TempProfile();
        var setup = Harness.Boot(profile, barrowworks: true).Setup;

        var reachable = Reachable(setup);
        // The heritage of a people nobody can play yet (the Ondrek's Record Stone) waits for them to be playable (owner, 2026-10-23).
        var held = setup.Character.Peoples.Values.Where(p => !p.Playable).SelectMany(p => p.Heritage).Select(k => k.ItemId).ToHashSet(StringComparer.Ordinal);
        Assert.Empty(setup.Items.Catalog.Definitions.Keys.Where(id => !reachable.Contains(id) && !held.Contains(id)));
    }

    /// <summary>
    /// A quest whose step names a place another region holds, or a line no reply leads to, can never finish (the Oct 15 playthrough:
    /// Iron Under Ash stalled at Blackvein Cut in a region with no places in it, and Mara's <c>told</c> had no way in).
    /// </summary>
    /// <remarks>
    /// Checked against the places, people, spawns and items of <b>both</b> regions a run can stand in, because
    /// The Unfinished Measure spans them: Kera gives it in the Ashen Hollow, and its five places are the ring's,
    /// which is its own region and its own load (the gate opens the portals, it does not merge the layouts).
    /// With the option off the quest is not offered either, so checking it against a run that cannot reach it
    /// would report a failure no player can experience.
    /// </remarks>
    [Fact]
    public void EveryQuestStep_PointsAtSomethingInTheRegion()
    {
        using var profile = new TempProfile();
        var setup = Harness.Boot(profile, barrowworks: true).Setup;

        // Every layout this run can stand in. Each interior is its own, and a quest step that names one of its places
        // is satisfied by it - not by the exterior, where that place does not exist.
        var layouts = setup.AllLayouts.ToList();

        var places = layouts.SelectMany(l => l.Locations).Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        var people = layouts.SelectMany(l => l.Npcs).Select(n => n.NpcId).ToHashSet(StringComparer.Ordinal);
        var spawned = setup.Combat.Spawns.SelectMany(s => s.Members).Select(m => m.CreatureId).ToHashSet(StringComparer.Ordinal);
        // A creature an item brings is met by buying the item, not by walking into a spawn: the hound pup is bought at Ashen Hollow and
        // grows into the wolf (spec 43), so no spawn places one and none should (WP-pets).
        foreach (var item in setup.Items.Catalog.Definitions.Values.Where(i => i.PetCreature is { Length: > 0 }))
            spawned.Add(item.PetCreature!);
        var reachable = Reachable(setup);

        var missing = new List<string>();
        foreach (var quest in setup.Quests.Quests.Values)
        {
            foreach (var step in quest.Order)
            {
                string at = $"{quest.Id} {step.Id}";
                switch (step.Condition)
                {
                    case TalkTo talk:
                        if (!people.Contains(talk.NpcId))
                            missing.Add($"{at}: {talk.NpcId} does not stand in the region");
                        var dialogue = setup.Social.Dialogues[talk.DialogueId];
                        var ledTo = dialogue.Nodes.Values.SelectMany(n => n.Choices.Select(c => c.Next).Append(n.Next).Append(n.NextIfExhausted))
                            .Append(dialogue.Root).OfType<string>().ToHashSet(StringComparer.Ordinal);
                        missing.AddRange(talk.Nodes.Where(n => !ledTo.Contains(n)).Select(n => $"{at}: no reply leads to {talk.DialogueId} {n}"));
                        break;
                    case VisitLocation visit when !places.Contains(visit.LocationId):
                        missing.Add($"{at}: {visit.LocationId} is not a place in the region");
                        break;
                    case ExploreLocation explore when !places.Contains(explore.LocationId):
                        missing.Add($"{at}: {explore.LocationId} is not a place in the region");
                        break;
                    case WorldStateObjective flag when flag.LocationId is { } place && !places.Contains(place):
                        missing.Add($"{at}: {place} is not a place in the region");
                        break;
                    case KillCreature kill when !spawned.Contains(kill.CreatureId):
                        missing.Add($"{at}: {kill.CreatureId} does not spawn in the region");
                        break;
                    case DeliverItem deliver when !people.Contains(deliver.NpcId) || !reachable.Contains(deliver.ItemId):
                        missing.Add($"{at}: {deliver.ItemId} to {deliver.NpcId} cannot be delivered here");
                        break;
                    case AcquireItem acquire when !reachable.Contains(acquire.ItemId):
                        missing.Add($"{at}: {acquire.ItemId} cannot be got");
                        break;
                }
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>Every item a fresh character can get, by the routes the class summary lists.</summary>
    private static HashSet<string> Reachable(SimulationSetup setup)
    {
        var items = setup.Items;
        // Loot a fresh character can reach: the region's containers, and the corpses of the creatures that spawn.
        var tables = new Queue<string>(setup.AllLayouts.SelectMany(l => l.Containers).Select(c => c.LootTableId)
            .Concat(setup.Combat.Spawns.SelectMany(s => s.Members).Select(m => setup.Combat.Creatures[m.CreatureId].LootTableId).OfType<string>()));
        var seenTables = new HashSet<string>(StringComparer.Ordinal);
        var reachable = new HashSet<string>(items.StartingKit.Select(k => k.ItemId), StringComparer.Ordinal);
        reachable.UnionWith(setup.Character.PlayablePeoples.SelectMany(p => p.Heritage).Select(k => k.ItemId));
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
            // Real item-use transitions, also exercised by CampTests: kit placement and drinking/boiling.
            if (reachable.Contains("item.tool.camp_kit"))
            {
                grew |= reachable.Add("item.tool.camp_fire");
                grew |= reachable.Add("item.tool.camp_bedroll");
            }
            if (reachable.Contains("item.tool.water_flask"))
                grew |= reachable.Add("item.tool.water_flask_empty");
            foreach (var recipe in setup.Crafting.Recipes.Values.Where(r => r.Inputs.All(i => reachable.Contains(i.ItemId))))
                grew |= reachable.Add(recipe.OutputItemId);
        }
        return reachable;
    }
}
