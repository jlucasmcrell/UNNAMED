using System.Text;
using System.Text.Json;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Spatial;
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
                WriteRoute(json, companion.Route);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartObject("posture");
            json.WriteString("stance", StanceKeys.Key(player.Posture.Stance));
            json.WriteBoolean("airborne", player.Posture.Airborne);
            json.WriteNumber("air_ms", player.Posture.AirMs);
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
                json.WriteString("cell_key", act.CellKey);
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
            json.WriteStartArray("standing");
            foreach (var row in player.Factions.Standing)
            {
                json.WriteStartObject();
                json.WriteString("faction_id", row.FactionId);
                json.WriteNumber("points", row.Points);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteStartObject("vitals");
            json.WriteNumber("last_combat_tick", player.Vitals.LastCombatTick);
            json.WriteNumber("last_exertion_tick", player.Vitals.LastExertionTick);
            json.WriteNumber("last_cast_tick", player.Vitals.LastCastTick);
            json.WriteNumber("health_milli", player.Vitals.HealthMilli);
            json.WriteNumber("stamina_milli", player.Vitals.StaminaMilli);
            json.WriteNumber("focus_milli", player.Vitals.FocusMilli);
            json.WriteNumber("strain_milli", player.Vitals.StrainMilli);
            json.WriteNumber("sprint_milli", player.Vitals.SprintMilli);
            json.WriteEndObject();
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

            json.WriteNumber("structure_seq", snapshot.StructureSequence);
            json.WriteStartArray("pieces");
            foreach (var piece in snapshot.Pieces)
            {
                json.WriteStartObject();
                json.WriteString("instance_id", piece.InstanceId.Value);
                json.WriteString("def_id", piece.DefId);
                json.WriteString("host_cell", piece.HostCell);
                json.WriteString("baseline_hash", piece.BaselineHash);
                json.WriteNumber("x_mm", piece.XMm);
                json.WriteNumber("z_mm", piece.ZMm);
                json.WriteNumber("rotation", piece.Rotation);
                json.WriteString("owner", piece.Owner.Value);
                json.WriteNumber("health", piece.HealthCurrent);
                json.WriteBoolean("door_open", piece.DoorOpen);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("npc_errands");
            foreach (var errand in snapshot.NpcErrands)
            {
                json.WriteStartObject();
                json.WriteString("npc_id", errand.NpcId);
                json.WriteString("host_cell", errand.HostCell);
                json.WriteString("baseline_hash", errand.BaselineHash);
                json.WriteString("phase", NpcErrandPhases.Key(errand.Phase));
                if (errand.PieceId is { } piece)
                    json.WriteString("piece_id", piece.Value);
                else
                    json.WriteNull("piece_id");
                if (errand.WorkOwner is { } owner)
                    json.WriteString("work_owner", owner.Value);
                else
                    json.WriteNull("work_owner");
                json.WriteNumber("x_mm", errand.XMm);
                json.WriteNumber("z_mm", errand.ZMm);
                json.WriteNumber("facing_mdeg", errand.FacingMdeg);
                WriteRoute(json, errand.Route);
                json.WriteNumber("stuck_ticks", errand.StuckTicks);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n") + "\n";
    }

    /// <summary>A mover's route (schema 15), every field: its status by key, the stamp as the seed is written.</summary>
    private static void WriteRoute(Utf8JsonWriter json, NavRoute route)
    {
        json.WriteStartObject("route");
        json.WriteString("status", NavRoute.StatusKey(route.Status));
        json.WriteStartArray("goal_mm");
        json.WriteNumberValue(route.GoalXMm);
        json.WriteNumberValue(route.GoalZMm);
        json.WriteEndArray();
        json.WriteStartArray("corners_mm");
        foreach (var corner in route.Corners)
        {
            json.WriteNumberValue(corner.XMm);
            json.WriteNumberValue(corner.ZMm);
        }
        json.WriteEndArray();
        json.WriteNumber("planned_tick", route.PlannedTick);
        json.WriteString("stamp", World.WorldSeed.Format(route.Stamp));
        json.WriteStartArray("watch_mm");
        json.WriteNumberValue(route.Watch.MinXMm);
        json.WriteNumberValue(route.Watch.MinZMm);
        json.WriteNumberValue(route.Watch.MaxXMm);
        json.WriteNumberValue(route.Watch.MaxZMm);
        json.WriteEndArray();
        json.WriteBoolean("partial", route.Partial);
        json.WriteEndObject();
    }

    /// <summary>The progression record (schema 4), every field, in canonical order.</summary>
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
