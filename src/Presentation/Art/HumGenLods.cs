using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>HumGen exports ordinary coincident meshes, not a glTF LOD extension.
/// Explicit visibility keeps skin, garments and hair exclusive even with animated bounds.</summary>
public sealed class HumGenLods
{
    private sealed record Part(MeshInstance3D Mesh, string PartName, int Level);
    private readonly List<Part> _parts;
    private readonly HashSet<MeshInstance3D> _hiddenOutfits = new();
    private int _level;
    private bool _reviewAllLevels;
    public bool CloseUp { get; set; }
    public int Level => _level;

    // Called only by the opt-in native capture gate, to measure the reported defect.
    internal void ReviewAllLevels(bool all) { _reviewAllLevels = all; Apply(); }

    public HumGenLods(Node model)
    {
        _parts = Parts(model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>());
        Apply(); // Correct before the first rendered frame, including the creator.
    }

    /// <summary>Meshes taken off the body and put on it after it was built (the creator's hairstyles), the band chosen again.</summary>
    public void Swap(IReadOnlyCollection<MeshInstance3D> removed, IEnumerable<MeshInstance3D> added)
    {
        _parts.RemoveAll(p => removed.Contains(p.Mesh));
        _hiddenOutfits.ExceptWith(removed);
        _parts.AddRange(Parts(added));
        Apply();
    }

    private static List<Part> Parts(IEnumerable<MeshInstance3D> meshes) => meshes.Select(mesh =>
    {
        string name = mesh.Name.ToString();
        // Native garment linings follow the shell's level: _LOD0_lining.
        // Keep them as a separate piece so one shell and one lining draw together.
        bool lining = name.EndsWith("_lining", StringComparison.Ordinal);
        if (lining) name = name[..^7];
        int at = name.LastIndexOf("_LOD", StringComparison.Ordinal);
        if (at < 0 || !int.TryParse(name.AsSpan(at + 4), out int level))
            return null;
        mesh.VisibilityRangeBegin = 0;
        mesh.VisibilityRangeEnd = 0;
        return new Part(mesh, name[..at] + (lining ? "_lining" : ""), level);
    }).OfType<Part>().ToList();

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
                part.Mesh.Visible = (_reviewAllLevels || part.Level == selected) && !_hiddenOutfits.Contains(part.Mesh);
        }
    }
}
