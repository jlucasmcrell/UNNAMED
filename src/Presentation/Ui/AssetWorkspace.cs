namespace UNNAMED.Presentation.Ui;

/// <summary>Packaged art lives beside content in primary/, independent of the launcher's arguments.</summary>
public static class AssetWorkspace
{
    public static string? Find(string? explicitRoot, string? environmentRoot, string home) =>
        new[] { explicitRoot, Path.Combine(home, "primary"), environmentRoot, Path.Combine(home, "assets") }
            .FirstOrDefault(root => !string.IsNullOrWhiteSpace(root)
                && File.Exists(Path.Combine(root, "manifests", "ui_icons.json")));
}
