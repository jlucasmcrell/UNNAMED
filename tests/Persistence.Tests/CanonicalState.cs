using System.Text;
using System.Text.Json;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Travel;
using UNNAMED.World;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// A loaded save's authoritative state as canonical JSON: what a historical fixture must load to
/// (<c>Fixtures/vN/expected.json</c>). Identity metadata that a migration legitimately rewrites -
/// content version and hash, build time, fingerprint - is left out; everything a player owns is in.
/// </summary>
internal static class CanonicalState
{
    public static string Render(LoadResult result)
    {
        var snapshot = result.World.TakeSnapshot();
        var player = result.Player;
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("world_seed", result.Manifest.WorldSeed);
            json.WriteNumber("world_tick", result.Manifest.WorldTick);
            json.WriteNumber("world_time_advanced_ticks", result.Manifest.WorldTimeAdvancedTicks);
            json.WriteNumber("playtime_seconds", result.Manifest.PlaytimeSeconds);
            json.WriteString("saved_utc", result.Manifest.SavedUtc);
            json.WriteStartArray("away_log");
            foreach (var line in result.Manifest.AwayLog)
            {
                json.WriteStartObject();
                json.WriteNumber("tick", line.Tick);
                json.WriteString("kind", line.Kind);
                json.WriteString("ref", line.Ref);
                json.WriteNumber("amount", line.Amount);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartObject("player");
            json.WriteString("id", player.Id.Value);
            json.WriteString("name", player.Name);
            json.WriteNumber("x_mm", player.XMm);
            json.WriteNumber("y_mm", player.YMm);
            json.WriteNumber("z_mm", player.ZMm);
            json.WriteNumber("facing_mdeg", player.FacingMdeg);
            json.WriteString("appearance_seed", World.WorldSeed.Format(player.AppearanceSeed));
            json.WriteStartArray("inventory");
            foreach (var entry in player.Inventory)
            {
                json.WriteStartObject();
                json.WriteString("item_id", entry.ItemId.Value);
                json.WriteString("def_id", entry.DefId);
                json.WriteNumber("count", entry.Count);
                json.WriteNumber("quality", entry.Quality);
                WriteNotable(json, entry.Notable);
                WriteWear(json, entry.Wear);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            WriteProgression(json, player.Progression);
            json.WriteStartArray("discoveries");
            foreach (var discovery in player.Discoveries)
            {
                json.WriteStartObject();
                json.WriteString("location_id", discovery.LocationId);
                json.WriteString("method", World.DiscoveryMethods.Key(discovery.Method));
                json.WriteNumber("tick", discovery.Tick);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartObject("equipment");
            foreach (var (slot, item) in player.Equipment)
                json.WriteString(Domain.Items.EquipSlots.Key(slot), item.Value);
            json.WriteEndObject();
            json.WriteNumber("currency", player.Currency);
            json.WriteStartArray("effects");
            foreach (var effect in player.Effects)
            {
                json.WriteStartObject();
                json.WriteString("effect_id", effect.EffectId);
                json.WriteNumber("stacks", effect.Stacks);
                json.WriteNumber("expires_tick", effect.ExpiresTick);
                json.WriteNumber("next_tick_at", effect.NextTickAt);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("relationships");
            foreach (var value in player.Relationships)
            {
                json.WriteStartObject();
                json.WriteString("npc_id", value.NpcId);
                json.WriteString("dimension", value.Dimension);
                json.WriteNumber("value", value.Value);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("conversations");
            foreach (var memory in player.Conversations)
            {
                json.WriteStartObject();
                json.WriteString("dialogue_id", memory.DialogueId);
                json.WriteStartArray("heard");
                foreach (string node in memory.Heard)
                    json.WriteStringValue(node);
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("quests");
            foreach (var quest in player.Quests)
            {
                json.WriteStartObject();
                json.WriteString("quest_id", quest.QuestId);
                json.WriteString("status", QuestKeys.Key(quest.Status));
                json.WriteNumber("started_tick", quest.StartedTick);
                if (quest.EndedTick is { } ended)
                    json.WriteNumber("ended_tick", ended);
                if (quest.EndedBy is { } by)
                    json.WriteString("ended_by", by);
                json.WriteStartArray("objectives");
                foreach (var objective in quest.Objectives)
                {
                    json.WriteStartObject();
                    json.WriteString("id", objective.Id);
                    json.WriteString("status", QuestKeys.Key(objective.Status));
                    json.WriteNumber("activated_tick", objective.ActivatedTick);
                    if (objective.EndedTick is { } over)
                        json.WriteNumber("ended_tick", over);
                    json.WriteNumber("progress", objective.Progress);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("companions");
            foreach (var companion in player.Companions)
            {
                json.WriteStartObject();
                json.WriteString("npc_id", companion.NpcId);
                json.WriteString("order", CompanionKeys.Key(companion.Order));
                json.WriteString("condition", CompanionKeys.Key(companion.Condition));
                json.WriteNumber("x_mm", companion.XMm);
                json.WriteNumber("z_mm", companion.ZMm);
                json.WriteNumber("facing_mdeg", companion.FacingMdeg);
                json.WriteNumber("health", companion.Health);
                json.WriteNumber("downed_tick", companion.DownedTick);
                json.WriteNumber("stuck_ticks", companion.StuckTicks);
                json.WriteNumber("last_combat_tick", companion.LastCombatTick);
                json.WriteStartArray("trail_mm");
                foreach (var mark in companion.Trail)
                {
                    json.WriteNumberValue(mark.XMm);
                    json.WriteNumberValue(mark.ZMm);
                }
                json.WriteEndArray();
                // Schema 30: the space they were left in, when not their own ground's.
                if (companion.Space is { } space)
                    json.WriteString("space", space);
                else
                    json.WriteNull("space");
                // Schema 21 (WP15a): why they travel with the character, how they fight, and a hireling's contract.
                json.WriteString("membership", PartyKeys.Key(companion.Membership));
                json.WriteString("stance", PartyKeys.Key(companion.Stance));
                json.WriteStartObject("tactics");
                json.WriteBoolean("keep_to_leash", companion.Tactics.KeepToLeash);
                json.WriteBoolean("protect_injured", companion.Tactics.ProtectInjured);
                json.WriteBoolean("hold_healing", companion.Tactics.HoldHealing);
                json.WriteBoolean("focus_target", companion.Tactics.FocusTarget);   // schema 34
                json.WriteEndObject();
                if (companion.Contract is { } contract)
                {
                    json.WriteStartObject("contract");
                    json.WriteString("role", PartyKeys.Key(contract.Role));
                    json.WriteNumber("days", contract.Days);
                    json.WriteNumber("daily_wage", contract.DailyWage);
                    json.WriteString("loot_rule", contract.LootRule);
                    json.WriteNumber("days_served", contract.DaysServed);
                    json.WriteNumber("unpaid_days", contract.UnpaidDays);
                    json.WriteNumber("purse", contract.Purse);
                    if (contract.WorkSite is { } site) json.WriteString("work_site", site); else json.WriteNull("work_site");
                    if (contract.RecipeId is { } recipe) json.WriteString("recipe_id", recipe); else json.WriteNull("recipe_id");
                    json.WriteStartObject("supply");
                    foreach (var (item, count) in contract.Supply)
                        json.WriteNumber(item, count);
                    json.WriteEndObject();
                    json.WriteEndObject();
                }
                else
                    json.WriteNull("contract");
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartObject("posture");
            json.WriteString("stance", StanceKeys.Key(player.Posture.Stance));
            json.WriteBoolean("airborne", player.Posture.Airborne);
            json.WriteNumber("air_ms", player.Posture.AirMs);
            json.WriteEndObject();
            json.WriteStartArray("waystones");
            foreach (var stone in player.Waystones)
            {
                json.WriteStartObject();
                json.WriteString("stone", stone.StoneKey);
                json.WriteString("region", stone.RegionId);
                json.WriteString("method", WaystoneMethods.Key(stone.Method));
                json.WriteNumber("tick", stone.Tick);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            if (player.ReturnPoint is { } returnPoint)
                json.WriteString("return_point", returnPoint);
            else
                json.WriteNull("return_point");
            json.WriteStartArray("roads_walked");
            foreach (string stretch in player.RoadsWalked)
                json.WriteStringValue(stretch);
            json.WriteEndArray();
            json.WriteStartArray("deeds");
            foreach (var deed in player.Deeds)
            {
                json.WriteStartObject();
                json.WriteString("deed", DeedKeys.Key(deed.Kind));
                json.WriteString("subject", deed.SubjectId);
                if (deed.NpcId is { } npc)
                    json.WriteString("npc", npc);
                else
                    json.WriteNull("npc");
                json.WriteNumber("count", deed.Count);
                json.WriteNumber("last_tick", deed.LastTick);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartObject("identity");
            json.WriteString("pronouns", PronounKeys.Key(player.Identity.Pronouns));
            json.WriteString("people", player.Identity.PeopleId);
            json.WriteString("body_preset", player.Identity.BodyPresetId);
            if (player.Identity.CallId is { } call)
                json.WriteString("call", call);
            else
                json.WriteNull("call");
            json.WriteEndObject();
            json.WriteStartObject("factions");
            json.WriteNumber("next_act_seq", player.Factions.NextActSeq);
            json.WriteStartArray("acts");
            foreach (var act in player.Factions.Acts)
            {
                json.WriteStartObject();
                json.WriteNumber("seq", act.Seq);
                json.WriteString("kind", act.Kind);
                json.WriteString("subject", act.Subject);
                json.WriteString("cell", act.CellKey);
                json.WriteNumber("x_mm", act.XMm);
                json.WriteNumber("z_mm", act.ZMm);
                json.WriteNumber("tick", act.Tick);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("knowledge");
            foreach (var row in player.Factions.Knowledge)
            {
                json.WriteStartObject();
                json.WriteString("knower", row.Knower);
                json.WriteNumber("act", row.Act);
                json.WriteString("identity", row.Identity);
                json.WriteString("source", row.Source);
                if (row.Via is { } via)
                    json.WriteString("via", via);
                else
                    json.WriteNull("via");
                json.WriteNumber("tick", row.Tick);
                json.WriteNumber("delta", row.Delta);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartObject("standing");
            foreach (var row in player.Factions.Standing)
                json.WriteNumber(row.FactionId, row.Points);
            json.WriteEndObject();
            json.WriteEndObject();
            // Schema 21 (WP15a): the party's ledger - who is recovering at home, and who has fallen.
            json.WriteStartObject("party");
            json.WriteStartArray("recovering");
            foreach (var recovery in player.Party.Recovering)
            {
                json.WriteStartObject();
                json.WriteString("npc_id", recovery.NpcId);
                json.WriteNumber("until_tick", recovery.UntilTick);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("fallen");
            foreach (string fallen in player.Party.Fallen)
                json.WriteStringValue(fallen);
            json.WriteEndArray();
            json.WriteEndObject();
            // Schema 23 (WP9): the quick bar's slots, and what was cleared off it on purpose.
            json.WriteStartObject("quick_bar");
            json.WriteStartArray("slots");
            foreach (string? slot in player.QuickBar)
            {
                if (slot is null)
                    json.WriteNullValue();
                else
                    json.WriteStringValue(slot);
            }
            json.WriteEndArray();
            json.WriteStartArray("cleared");
            foreach (string cleared in player.QuickBarCleared)
                json.WriteStringValue(cleared);
            json.WriteEndArray();
            json.WriteEndObject();
            // Schema 25 (WP15b): the party's sheets - each member's progression, the gear they wear, given or loaned, and their own things.
            json.WriteStartArray("party_sheets");
            foreach (var sheet in player.PartySheets)
            {
                json.WriteStartObject();
                json.WriteString("npc_id", sheet.NpcId);
                WriteProgression(json, sheet.Progression);
                json.WriteStartArray("gear");
                foreach (var (slot, gear) in sheet.Gear)
                {
                    json.WriteStartObject();
                    json.WriteString("slot", Domain.Items.EquipSlots.Key(slot));
                    json.WriteBoolean("loaned", gear.Loaned);
                    WriteStack(json, gear.Item);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteStartArray("belongings");
                foreach (var item in sheet.Belongings)
                {
                    json.WriteStartObject();
                    WriteStack(json, item);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            // Schema 27: the attack under way - what makes the blow, when it began, what it has struck - or null.
            if (player.Attack is { } attack)
            {
                json.WriteStartObject("attack");
                json.WriteString("source", attack.Source);
                json.WriteNumber("start_tick", attack.StartTick);
                json.WriteStartArray("struck");
                foreach (var struck in attack.Struck)
                    json.WriteStringValue(struck.Value);
                json.WriteEndArray();
                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("attack");
            }
            // Schema 30 (the Barrowworks): the space the body stands in - empty, the exterior - and the portal it came in by, or null.
            json.WriteString("space_id", player.SpaceId);
            if (player.SpaceTraversal is { } traversal)
                json.WriteString("space_traversal", traversal);
            else
                json.WriteNull("space_traversal");
            // Schema 32 (action bars and gear sets): the five further bars, and the character's gear sets by place.
            json.WriteStartArray("extra_bars");
            foreach (var extra in player.ExtraBars)
            {
                json.WriteStartArray();
                foreach (string? slot in extra)
                {
                    if (slot is null)
                        json.WriteNullValue();
                    else
                        json.WriteStringValue(slot);
                }
                json.WriteEndArray();
            }
            json.WriteEndArray();
            json.WriteStartArray("gear_sets");
            foreach (var set in player.GearSets)
            {
                json.WriteStartObject();
                json.WriteNumber("index", set.Index);
                json.WriteString("name", set.Name);
                json.WriteStartArray("items");
                foreach (var item in set.Items)
                {
                    json.WriteStartObject();
                    json.WriteString("slot", UNNAMED.Domain.Items.EquipSlots.Key(item.Slot));
                    json.WriteString("item", item.Item.Value);
                    json.WriteString("def", item.DefId);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            // Schema 34 (before testers): the weapon family whose layout the main bar shows, and each family's layout.
            json.WriteString("held_family", player.HeldFamily);
            json.WriteStartArray("family_bars");
            foreach (var (family, layout) in player.FamilyBars)
            {
                json.WriteStartObject();
                json.WriteString("family", family);
                json.WriteStartArray("slots");
                foreach (string? slot in layout)
                {
                    if (slot is null)
                        json.WriteNullValue();
                    else
                        json.WriteStringValue(slot);
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            // Schema 33 (inventory organisation): the four bag slots, and where each carried stack lies and whether it is locked or junk.
            json.WriteStartObject("pack");
            json.WriteStartArray("bags");
            foreach (var bag in player.Bags)
            {
                if (bag is { } worn)
                    json.WriteStringValue(worn.Value);
                else
                    json.WriteNullValue();
            }
            json.WriteEndArray();
            json.WriteStartArray("stacks");
            foreach (var entry in player.Inventory)
            {
                json.WriteStartObject();
                json.WriteString("item_id", entry.ItemId.Value);
                json.WriteNumber("bag", entry.Bag);
                json.WriteNumber("slot", entry.Slot);
                json.WriteBoolean("locked", entry.Locked);
                json.WriteBoolean("junk", entry.Junk);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
            // Schema 31 (UI progression F): what each experiment at a station taught, oldest first.
            json.WriteStartArray("findings");
            foreach (var finding in player.Findings)
            {
                json.WriteStartObject();
                json.WriteString("kind", UNNAMED.Domain.Crafting.FindingKeys.Key(finding.Kind));
                json.WriteString("station", finding.StationKey);
                json.WriteString("station_kind", finding.StationKind);
                if (finding.RecipeId is { } recipe)
                    json.WriteString("recipe", recipe);
                else
                    json.WriteNull("recipe");
                json.WriteStartArray("materials");
                foreach (string material in finding.Materials)
                    json.WriteStringValue(material);
                json.WriteEndArray();
                json.WriteNumber("tick", finding.Tick);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();

            json.WriteStartArray("cells");
            foreach (var cell in snapshot.Cells)
            {
                json.WriteStartObject();
                json.WriteString("cell_key", cell.CellKey);
                json.WriteString("baseline_hash", cell.BaselineHash);
                json.WriteStartObject("flags");
                foreach (var (flag, value) in cell.Flags)
                    json.WriteNumber(flag, value);
                json.WriteEndObject();
                json.WriteStartArray("harvested_nodes");
                foreach (var node in cell.HarvestedNodes)
                {
                    json.WriteStartObject();
                    json.WriteString("node_key", node.NodeKey);
                    json.WriteNumber("last_harvest_tick", node.LastHarvestTick);
                    json.WriteNumber("harvest_seq", node.HarvestSeq);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteStartObject("population_alive");
                foreach (var (population, alive) in cell.PopulationAlive)
                    json.WriteNumber(population, alive);
                json.WriteEndObject();
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("entities");
            foreach (var entity in snapshot.Entities)
            {
                json.WriteStartObject();
                json.WriteString("instance_id", entity.InstanceId.Value);
                json.WriteString("slot_key", entity.SlotKey);
                json.WriteString("def_id", entity.DefId);
                if (entity.Alive is bool alive) json.WriteBoolean("alive", alive); else json.WriteNull("alive");
                if (entity.XCm is int x) json.WriteNumber("x_cm", x); else json.WriteNull("x_cm");
                if (entity.ZCm is int z) json.WriteNumber("z_cm", z); else json.WriteNull("z_cm");
                json.WriteString("baseline_hash", entity.BaselineHash);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("created");
            foreach (var created in snapshot.Created)
            {
                json.WriteStartObject();
                json.WriteString("instance_id", created.InstanceId.Value);
                json.WriteString("def_id", created.DefId);
                json.WriteString("host_cell", created.HostCell);
                json.WriteNumber("x_cm", created.XCm);
                json.WriteNumber("z_cm", created.ZCm);
                json.WriteNumber("count", created.Count);
                json.WriteNumber("quality", created.Quality);
                WriteNotable(json, created.Notable);
                WriteWear(json, created.Wear);
                json.WriteString("baseline_hash", created.BaselineHash);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("containers");
            foreach (var container in snapshot.Containers)
            {
                json.WriteStartObject();
                json.WriteString("key", container.Key);
                json.WriteString("instance_id", container.InstanceId.Value);
                json.WriteString("host_cell", container.HostCell);
                json.WriteString("baseline_hash", container.BaselineHash);
                json.WriteStartArray("items");
                foreach (var item in container.Items)
                {
                    json.WriteStartObject();
                    json.WriteString("item_id", item.ItemId.Value);
                    json.WriteString("def_id", item.DefId);
                    json.WriteNumber("count", item.Count);
                    json.WriteNumber("quality", item.Quality);
                    WriteNotable(json, item.Notable);
                    WriteWear(json, item.Wear);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("creatures");
            foreach (var creature in snapshot.Creatures)
            {
                json.WriteStartObject();
                json.WriteString("key", creature.Key);
                json.WriteString("def_id", creature.DefId);
                json.WriteString("instance_id", creature.InstanceId.Value);
                json.WriteString("host_cell", creature.HostCell);
                json.WriteString("baseline_hash", creature.BaselineHash);
                json.WriteNumber("generation", creature.Generation);
                json.WriteString("condition", World.CreatureConditions.Key(creature.Condition));
                json.WriteNumber("x_mm", creature.XMm);
                json.WriteNumber("z_mm", creature.ZMm);
                json.WriteNumber("facing_mdeg", creature.FacingMdeg);
                json.WriteNumber("health", creature.Health);
                json.WriteNumber("died_tick", creature.DiedTick);
                json.WriteNumber("respawn_tick", creature.RespawnTick);
                json.WriteString("mind", World.CreatureMinds.Key(creature.Mind));
                json.WriteNumber("awareness", creature.Awareness);
                json.WriteBoolean("knows", creature.Knows);
                json.WriteNumber("known_x_mm", creature.KnownXMm);
                json.WriteNumber("known_z_mm", creature.KnownZMm);
                json.WriteNumber("last_seen_tick", creature.LastSeenTick);
                json.WriteNumber("search_until", creature.SearchUntil);
                json.WriteBoolean("has_called", creature.HasCalled);
                json.WriteNumber("next_charge_tick", creature.NextChargeTick);
                json.WriteNumber("stagger_immune_until", creature.StaggerImmuneUntil);
                if (creature.StaggeredTick is { } staggered)
                    json.WriteNumber("staggered_tick", staggered);
                else
                    json.WriteNull("staggered_tick");
                json.WriteNumber("stagger_lasts_ticks", creature.StaggerLastsTicks);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("noises");
            foreach (var noise in snapshot.Noises)
            {
                json.WriteStartObject();
                json.WriteNumber("x_mm", noise.XMm);
                json.WriteNumber("z_mm", noise.ZMm);
                json.WriteNumber("radius_mm", noise.RadiusMm);
                json.WriteBoolean("call", noise.Call);
                if (noise.CallerKind is { } kind)
                    json.WriteString("caller_kind", kind);
                else
                    json.WriteNull("caller_kind");
                json.WriteEndObject();
            }
            json.WriteEndArray();

            // The economy (schema 22).
            if (snapshot.Economy is { } economy)
            {
                json.WriteStartObject("economy");
                json.WriteNumber("day", economy.Day);
                json.WriteStartArray("markets");
                foreach (var market in economy.Markets.Values)
                {
                    json.WriteStartObject();
                    json.WriteString("market_id", market.MarketId);
                    json.WriteStartObject("stock");
                    foreach (var (item, count) in market.Stock)
                        json.WriteNumber(item, count);
                    json.WriteEndObject();
                    json.WriteStartObject("sold_by_character");
                    foreach (var (item, count) in market.SoldByCharacter)
                        json.WriteNumber(item, count);
                    json.WriteEndObject();
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteStartArray("shipments");
                foreach (var shipment in economy.Shipments)
                {
                    json.WriteStartObject();
                    json.WriteString("link_id", shipment.LinkId);
                    json.WriteString("from_market", shipment.FromMarket);
                    json.WriteString("to_market", shipment.ToMarket);
                    json.WriteString("item_id", shipment.ItemId);
                    json.WriteNumber("count", shipment.Count);
                    json.WriteNumber("arrives_day", shipment.ArrivesDay);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("economy");
            }
            json.WriteStartArray("merchants");
            foreach (var merchant in snapshot.Merchants)
            {
                json.WriteStartObject();
                json.WriteString("merchant_id", merchant.MerchantId);
                json.WriteNumber("purse", merchant.Purse);
                json.WriteNumber("restocked_day", merchant.RestockedDay);
                json.WriteNumber("shelf_day", merchant.ShelfDay);
                json.WriteBoolean("traded", merchant.Traded);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            // NPC life (schema 26).
            if (snapshot.NpcLife is { } life)
            {
                json.WriteStartObject("npc_life");
                json.WriteNumber("hour", life.Hour);
                json.WriteStartArray("npcs");
                foreach (var npc in life.Npcs.Values)
                {
                    json.WriteStartObject();
                    json.WriteString("key", npc.Key);
                    json.WriteString("routine_id", npc.RoutineId);
                    json.WriteString("cell", npc.Cell);
                    json.WriteString("name", npc.Name);
                    json.WriteNumber("generation", npc.Generation);
                    json.WriteNumber("arrived_day", npc.ArrivedDay);
                    json.WriteString("activity", npc.Activity);
                    json.WriteString("condition", npc.Condition.ToString());
                    json.WriteNumber("condition_until", npc.ConditionUntil);
                    json.WriteString("pose", $"{npc.XMm},{npc.ZMm},{npc.FacingMdeg}");
                    json.WriteString("route", $"{npc.Route.Status} to {npc.Route.GoalXMm},{npc.Route.GoalZMm} via {string.Join(" ", npc.Route.Corners.Select(c => $"{c.XMm},{c.ZMm}"))} "
                        + $"at {npc.Route.PlannedTick} stamp {npc.Route.Stamp:X16} watch {npc.Route.Watch.MinXMm},{npc.Route.Watch.MinZMm},{npc.Route.Watch.MaxXMm},{npc.Route.Watch.MaxZMm}{(npc.Route.Partial ? " partial" : "")}");
                    json.WriteNumber("stuck_ticks", npc.StuckTicks);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteStartArray("vacancies");
                foreach (var vacancy in life.Vacancies.Values)
                    json.WriteStringValue($"{vacancy.SlotId} left {vacancy.LeftDay} arrives {vacancy.ArrivalDay} as {vacancy.NextGeneration}"
                        + (vacancy.ArrivingName is { } arriving ? $" ({arriving})" : ""));
                json.WriteEndArray();
                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("npc_life");
            }

            // Adventurers (schema 29).
            if (snapshot.Adventurers is { } adventurers)
            {
                json.WriteStartObject("adventurers");
                json.WriteNumber("day", adventurers.Day);
                json.WriteStartArray("agents");
                foreach (var a in adventurers.Agents.Values)
                {
                    json.WriteStartObject();
                    json.WriteString("key", a.Key);
                    json.WriteString("slot", a.Slot);
                    json.WriteNumber("generation", a.Generation);
                    json.WriteString("name", a.Name);
                    json.WriteString("people_id", a.PeopleId);
                    json.WriteString("settlement", a.Settlement);
                    json.WriteString("at", a.At);
                    WriteProgression(json, a.Progression);
                    json.WriteNumber("coin", a.Coin);
                    json.WriteNumber("risk_percent", a.RiskPercent);
                    json.WriteNumber("arrived_day", a.ArrivedDay);
                    json.WriteStartArray("gear");
                    foreach (var (slot, gear) in a.Gear)
                        json.WriteStringValue($"{UNNAMED.Domain.Items.EquipSlots.Key(slot)}: {gear.DefId} {gear.ItemId} quality {gear.Quality}");
                    json.WriteEndArray();
                    json.WriteStartObject("pack");
                    foreach (var (item, count) in a.Pack)
                        json.WriteNumber(item, count);
                    json.WriteEndObject();
                    json.WriteString("condition", a.Condition.ToString());
                    json.WriteNumber("injured_until_day", a.InjuredUntilDay);
                    json.WriteString("plan", $"day {a.Plan.Day} {a.Plan.Goal} {a.Plan.Ref ?? "-"} with {string.Join(" ", a.Plan.Group)} post {a.Plan.PostTick} depart {a.Plan.DepartTick}");
                    json.WriteString("party", a.Party);
                    json.WriteString("counts", $"acquired {a.Acquired} kills {a.Kills} bounties {a.BountiesClaimed}");
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteStartArray("vacancies");
                foreach (var v in adventurers.Vacancies.Values)
                    json.WriteStringValue($"{v.Slot} of {v.Settlement} left {v.LeftDay} arrives {v.ArrivalDay} as {v.NextGeneration}");
                json.WriteEndArray();
                json.WriteStartArray("bodies");
                foreach (var (key, body) in adventurers.Bodies)
                    json.WriteStringValue($"{key} {body.Name} at {body.XMm},{body.ZMm} facing {body.FacingMdeg}");
                json.WriteEndArray();
                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("adventurers");
            }
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n") + "\n";
    }

    /// <summary>The progression record (schema 4), every field, in canonical order.</summary>
    /// <summary>A stack's wear (schema 24): null for an unworn stack.</summary>
    private static void WriteWear(Utf8JsonWriter json, UNNAMED.Domain.Items.ItemWear? wear)
    {
        if (wear is null)
        {
            json.WriteNull("wear");
            return;
        }
        json.WriteStartObject("wear");
        json.WriteNumber("points", wear.Points);
        json.WriteNumber("made_quality", wear.MadeQuality);
        json.WriteEndObject();
    }

    /// <summary>A stack's notable record (schema 19): null for an ordinary stack.</summary>
    private static void WriteNotable(Utf8JsonWriter json, UNNAMED.Domain.Items.NotableRecord? notable)
    {
        if (notable is null)
        {
            json.WriteNull("notable");
            return;
        }
        json.WriteStartObject("notable");
        json.WriteString("name", notable.Name);
        json.WriteString("provenance", UNNAMED.Domain.Items.ProvenanceKinds.Key(notable.Provenance));
        json.WriteString("source_ref", notable.SourceRef);
        json.WriteNumber("tick", notable.Tick);
        if (notable.Maker is { } maker) json.WriteString("maker", maker); else json.WriteNull("maker");
        json.WriteNumber("familiarity", notable.Familiarity);
        json.WriteNumber("uses", notable.Uses);
        json.WriteEndObject();
    }

    private static void WriteStack(Utf8JsonWriter json, InventoryEntry entry)
    {
        json.WriteString("item_id", entry.ItemId.Value);
        json.WriteString("def_id", entry.DefId);
        json.WriteNumber("count", entry.Count);
        json.WriteNumber("quality", entry.Quality);
        WriteNotable(json, entry.Notable);
        WriteWear(json, entry.Wear);
    }

    private static void WriteProgression(Utf8JsonWriter json, CharacterProgression p)
    {
        json.WriteStartObject("progression");
        json.WriteNumber("level", p.Level);
        json.WriteNumber("level_progress_xp", p.LevelProgressXp);
        json.WriteNumber("xp_debt", p.XpDebt);
        json.WriteStartObject("lifetime_xp");
        foreach (var (source, xp) in p.LifetimeXp)
            json.WriteNumber(ProgressionKeys.Key(source), xp);
        json.WriteEndObject();
        json.WriteStartObject("attribute_allocation");
        foreach (var (attribute, points) in p.Allocation)
            json.WriteNumber(ProgressionKeys.Key(attribute), points);
        json.WriteEndObject();
        json.WriteNumber("unspent_attribute_points", p.UnspentAttributePoints);
        json.WriteStartArray("attribute_grants");
        foreach (var g in p.Grants)
        {
            json.WriteStartObject();
            json.WriteString("attribute", ProgressionKeys.Key(g.Attribute));
            json.WriteNumber("amount", g.Amount);
            json.WriteString("source", ProgressionKeys.Key(g.Source));
            json.WriteString("source_ref", g.SourceRef);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteStartObject("skills");
        foreach (var (id, skill) in p.Skills)
        {
            json.WriteStartObject(id);
            json.WriteNumber("level", skill.Level);
            json.WriteNumber("progress_xp", skill.ProgressXp);
            json.WriteEndObject();
        }
        json.WriteEndObject();
        json.WriteStartObject("known");
        foreach (var (id, technique) in p.Known)
        {
            json.WriteStartObject(id);
            json.WriteString("source", ProgressionKeys.Key(technique.Source));
            if (technique.SourceRef is { } sourceRef) json.WriteString("source_ref", sourceRef); else json.WriteNull("source_ref");
            json.WriteNumber("tick", technique.Tick);
            json.WriteEndObject();
        }
        json.WriteEndObject();
        json.WriteStartArray("production_firsts");
        foreach (var id in p.ProductionFirsts)
            json.WriteStringValue(id);
        json.WriteEndArray();
        json.WriteStartArray("novelty_firsts");
        foreach (var id in p.NoveltyFirsts)
            json.WriteStringValue(id);
        json.WriteEndArray();
        json.WriteStartObject("pools");
        if (p.Pools.Health is int health) json.WriteNumber("health", health); else json.WriteNull("health");
        if (p.Pools.Stamina is int stamina) json.WriteNumber("stamina", stamina); else json.WriteNull("stamina");
        if (p.Pools.Focus is int focus) json.WriteNumber("focus", focus); else json.WriteNull("focus");
        json.WriteNumber("strain", p.Pools.Strain);
        json.WriteEndObject();
        json.WriteStartObject("guards");
        json.WriteStartObject("species_today");
        foreach (var (species, day) in p.Guards.SpeciesToday)
        {
            json.WriteStartObject(species);
            json.WriteNumber("day", day.Day);
            json.WriteNumber("kills", day.Kills);
            json.WriteEndObject();
        }
        json.WriteEndObject();
        json.WriteStartArray("species_ever_killed");
        foreach (var species in p.Guards.SpeciesEverKilled)
            json.WriteStringValue(species);
        json.WriteEndArray();
        json.WriteStartObject("cluster_kills");
        foreach (var (cluster, ticks) in p.Guards.ClusterKills)
        {
            json.WriteStartArray(cluster);
            foreach (long tick in ticks)
                json.WriteNumberValue(tick);
            json.WriteEndArray();
        }
        json.WriteEndObject();
        json.WriteEndObject();
        json.WriteEndObject();
    }
}
