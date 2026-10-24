using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>HumGen exports ordinary coincident meshes, not a glTF LOD extension.
/// Explicit visibility keeps skin, garments and hair exclusive even with animated bounds.</summary>
public sealed class HumGenLods
{
    private sealed record Part(MeshInstance3D Mesh, string PartName, int Level);
    private readonly Part[] _parts;
    private readonly HashSet<MeshInstance3D> _hiddenOutfits = new();
    private int _level;
    public bool CloseUp { get; set; }
    public int Level => _level;

    public HumGenLods(Node model)
    {
        _parts = model.FindChildren("*", nameof(MeshInstance3D), true, false)
            .OfType<MeshInstance3D>().Select(mesh =>
            {
                string name = mesh.Name.ToString();
                int at = name.LastIndexOf("_LOD", StringComparison.Ordinal);
                return at >= 0 && int.TryParse(name.AsSpan(at + 4), out int level)
                    ? new Part(mesh, name[..at], level) : null;
            }).OfType<Part>().ToArray();
        foreach (var part in _parts)
        {
            part.Mesh.VisibilityRangeBegin = 0;
            part.Mesh.VisibilityRangeEnd = 0;
        }
        Apply(); // Correct before the first rendered frame, including the creator.
    }

    public void OutfitVisible(MeshInstance3D mesh, bool shown)
    {
        if (!_parts.Any(p => p.Mesh == mesh)) { mesh.Visible = shown; return; }
        if (shown) _hiddenOutfits.Remove(mesh); else _hiddenOutfits.Add(mesh);
        Apply();
    }

    public void Update(float distance)
    {
        int next = CharacterLodBand.Select(distance, _level, VisualOptions.Tier == "ultra", CloseUp);
        if (next == _level) return;
        _level = next;
        Apply();
    }

    private void Apply()
    {
        foreach (var group in _parts.GroupBy(p => p.PartName))
        {
            // Some hair parts omit a distant level: retain their nearest available level.
            int selected = group.OrderBy(p => Math.Abs(p.Level - _level)).ThenBy(p => p.Level).First().Level;
            foreach (var part in group)
                part.Mesh.Visible = part.Level == selected && !_hiddenOutfits.Contains(part.Mesh);
        }
    }
}
