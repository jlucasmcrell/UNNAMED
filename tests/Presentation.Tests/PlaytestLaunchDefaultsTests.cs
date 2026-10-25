using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Tests;

public class PlaytestLaunchDefaultsTests
{
    [Fact]
    public void DirectExportUsesItsOwnPayloadRegardlessOfWorkingDirectory()
    {
        string root = Path.GetFullPath("owner build");
        string[] args = PlaytestLaunchDefaults.Resolve([], root, true);
        var options = Enumerable.Range(0, args.Length / 2).ToDictionary(i => args[2 * i], i => args[2 * i + 1]);
        Assert.Equal(Path.Combine(root, "primary"), options["--asset-root"]);
        Assert.Equal(Path.Combine(root, "fallback"), options["--asset-fallback"]);
        Assert.Equal(Path.Combine(root, "content"), options["--content-root"]);
        Assert.Equal(Path.Combine(root, "content", "_scatter_rules.json"), options["--scatter-rules"]);
        Assert.Equal(Path.Combine(root, "content", "_art_bindings.json"), options["--bindings-overlay"]);
        Assert.Equal(Path.Combine(root, "saves"), options["--profile"]);
        Assert.Equal("region.starting_frontier", options["--region"]);
        Assert.DoesNotContain("tier=", options["--visual"]); // Preserve the saved tier; VisualOptions supplies the owner default.
        Assert.Equal("terrain=terrain3d,sky=sky3d,people=humgen,foldscar=proof,body=none,textures=cache", options["--visual"]);
    }

    [Fact]
    public void HumGenPackageUsesItsOwnPayload()
    {
        var args = PlaytestLaunchDefaults.Resolve([], "comparison", true);
        Assert.Contains("terrain=terrain3d,sky=sky3d,people=humgen,foldscar=proof,body=none,textures=cache", args);
        Assert.Contains(Path.Combine("comparison", "primary"), args);
    }

    [Fact]
    public void ExplicitArgumentsAndEditorBootArePreserved()
    {
        string[] explicitArgs = ["--smoke", "--content-root", "custom content"];
        Assert.Same(explicitArgs, PlaytestLaunchDefaults.Resolve(explicitArgs, "ignored", true));
        Assert.Empty(PlaytestLaunchDefaults.Resolve([], "ignored", false));
    }
}

