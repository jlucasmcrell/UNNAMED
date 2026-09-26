// UNNAMED Presentation - authored set dressing: the props that make a place lived in, placed from data (Art/dressing.json)
// Godot presentation only (D-11): no collider, nothing the simulation sees; each prop grounded on the domain's own terrain

using System.Text.Json;
using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Phase B demo: the settlement's, quarry's, wood's and ruin's props - barrels and sacks against the lodge, the smithy's coal and
/// wheelbarrow, rails and an ore cart in the quarry, stumps and fallen trunks in the Charwood, the Foldscar's broken statuary - each a
/// prepared model at an authored spot, turned, its lowest point on the ground (or <c>y</c> metres above it, for what hangs from a wall).
/// Dressing is scenery: it has no collider and is placed against walls and off the paths, where nobody needs to walk.
/// </summary>
public static class Dressing
{
    public const string ResourcePath = "res://Art/dressing.json";

    /// <summary>Every placement that loads, under <paramref name="parent"/>; returns how many were placed and how many were skipped.</summary>
    public static (int Placed, int Skipped) Place(Node3D parent, ArtLibrary art, Func<float, float, float> ground)
    {
        if (!Godot.FileAccess.FileExists(ResourcePath))
            return (0, 0);
        int placed = 0, skipped = 0;
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ResourcePath));
        var root = new Node3D { Name = "Dressing" };
        parent.AddChild(root);
        foreach (var entry in document.RootElement.GetProperty("places").EnumerateArray())
        {
            string? id = entry.TryGetProperty("model", out var m) ? m.GetString() : null;
            if (id is null || !entry.TryGetProperty("at", out var at) || at.GetArrayLength() != 2 || art.ModelWithLods(id) is not { } model)
            {
                skipped++;
                if (id is not null)
                    art.Coverage.Fallback("dressing", id, "the prop did not load: the spot stays bare", id);
                continue;
            }
            float x = (float)at[0].GetDouble(), z = (float)at[1].GetDouble();
            float yaw = entry.TryGetProperty("yaw", out var y) ? (float)y.GetDouble() : 0;
            float above = entry.TryGetProperty("y", out var h) ? (float)h.GetDouble() : 0;
            float sink = entry.TryGetProperty("sink", out var s) ? (float)s.GetDouble() : 0.02f;
            var bounds = ArtGallery.Bounds(model);
            // Centred over its spot, its lowest point on the ground (less a little, so it sits in the ground rather than on it).
            model.Position = new Vector3(-bounds.GetCenter().X, -bounds.Position.Y, -bounds.GetCenter().Z);
            var holder = new Node3D { Name = $"{id}_{placed}", Position = new Vector3(x, ground(x, z) + above - sink, z), Rotation = new Vector3(0, Mathf.DegToRad(yaw), 0) };
            // "align": laid on the slope (rails, a cart) - tilted to the ground's plane under its own footprint, not level on its lowest point.
            if (entry.TryGetProperty("align", out var align) && align.ValueKind == JsonValueKind.True)
            {
                float hx = MathF.Max(bounds.Size.X, 0.5f) / 2, hz = MathF.Max(bounds.Size.Z, 0.5f) / 2;
                var basis = new Basis(Vector3.Up, Mathf.DegToRad(yaw));
                Vector3 Along(Vector3 local) { var w = basis * local; return new Vector3(w.X, (ground(x + w.X, z + w.Z) - ground(x - w.X, z - w.Z)) / 2, w.Z); }
                var ax = Along(new Vector3(hx, 0, 0)).Normalized();
                var az = Along(new Vector3(0, 0, hz)).Normalized();
                var up = az.Cross(ax).Normalized();
                holder.Basis = new Basis(ax, up, up.Cross(ax).Normalized()).Orthonormalized();
            }
            holder.AddChild(model);
            root.AddChild(holder);
            art.Coverage.Resolved("dressing", $"{id}@{x:0.#},{z:0.#}", id);
            placed++;
        }
        return (placed, skipped);
    }
}
