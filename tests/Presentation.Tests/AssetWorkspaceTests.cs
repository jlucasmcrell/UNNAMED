using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

public sealed class AssetWorkspaceTests
{
    [Fact]
    public void PlainLaunchUsesPackagedPrimaryEvenWithLegacyEnvironmentAndAssets()
    {
        string home = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            string primary = Workspace("primary"), old = Workspace("assets"), environment = Workspace("old-environment");
            Assert.Equal(primary, AssetWorkspace.Find(null, environment, home));
            string fallback = Directory.CreateDirectory(Path.Combine(home, "fallback")).FullName;
            Assert.Equal(fallback, AssetWorkspace.PackagedFallback(primary));
            Assert.Null(AssetWorkspace.PackagedFallback(environment));
            Assert.Equal(environment, AssetWorkspace.Find(environment, null, home));
            Directory.Delete(primary, true);
            Assert.Equal(environment, AssetWorkspace.Find(null, environment, home));
            Assert.Equal(old, AssetWorkspace.Find(null, null, home));
            Directory.Delete(old, true);
            Assert.Null(AssetWorkspace.Find(null, null, home));

            string Workspace(string name)
            {
                string path = Path.Combine(home, name);
                Directory.CreateDirectory(Path.Combine(path, "manifests"));
                File.WriteAllText(Path.Combine(path, "manifests", "ui_icons.json"), "{\"icons\":{}}");
                return path;
            }
        }
        finally { Directory.Delete(home, true); }
    }
}
