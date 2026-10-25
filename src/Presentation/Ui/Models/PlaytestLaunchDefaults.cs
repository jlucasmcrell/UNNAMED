namespace UNNAMED.Presentation.Ui.Models;

/// <summary>A packaged EXE opened directly uses the same local payload as PLAY.bat.</summary>
public static class PlaytestLaunchDefaults
{
    public static string[] Resolve(string[] arguments, string directory, bool exported) =>
        !exported || arguments.Length != 0 ? arguments :
        [
            "--region", "region.starting_frontier",
            "--content-root", Path.Combine(directory, "content"),
            "--bindings-overlay", Path.Combine(directory, "content", "_art_bindings.json"),
            "--asset-root", Path.Combine(directory, "primary"),
            "--asset-fallback", Path.Combine(directory, "fallback"),
            "--scatter-rules", Path.Combine(directory, "content", "_scatter_rules.json"),
            "--profile", Path.Combine(directory, "saves"),
            "--visual", "terrain=terrain3d,sky=sky3d,people=humgen,foldscar=proof,body=none,textures=cache",
        ];
}

