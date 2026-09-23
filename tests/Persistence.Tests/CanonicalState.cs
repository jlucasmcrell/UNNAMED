using System.Text;
using System.Text.Json;

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
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n") + "\n";
    }
}
