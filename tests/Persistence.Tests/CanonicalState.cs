using System.Text;
using System.Text.Json;
using UNNAMED.Domain.Progression;

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
            json.WriteString("appearance_seed", World.WorldSeed.Format(player.AppearanceSeed));
            json.WriteStartArray("inventory");
            foreach (var entry in player.Inventory)
            {
                json.WriteStartObject();
                json.WriteString("item_id", entry.ItemId.Value);
                json.WriteString("def_id", entry.DefId);
                json.WriteNumber("count", entry.Count);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            WriteProgression(json, player.Progression);
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
                json.WriteString("baseline_hash", created.BaselineHash);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n") + "\n";
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
