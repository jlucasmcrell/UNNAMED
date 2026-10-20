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
        Assert.Equal("tier=high,terrain=terrain3d,sky=sky3d,people=production,foldscar=proof,body=none", options["--visual"]);
    }

    [Fact]
    public void ExplicitArgumentsAndEditorBootArePreserved()
    {
        string[] explicitArgs = ["--smoke", "--content-root", "custom content"];
        Assert.Same(explicitArgs, PlaytestLaunchDefaults.Resolve(explicitArgs, "ignored", true));
        Assert.Empty(PlaytestLaunchDefaults.Resolve([], "ignored", false));
    }
}
