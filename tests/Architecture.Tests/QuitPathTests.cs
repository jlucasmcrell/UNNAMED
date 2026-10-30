using System.Text.RegularExpressions;

namespace UNNAMED.Architecture.Tests;

/// <summary>
/// A quit the game takes because it FAILED goes through <c>CaptureQuit</c> (B-001). It is not bookkeeping: a run that ends
/// because it failed must leave the process rather than let the engine unwind a world that never finished building, because
/// Godot's own teardown frees one block twice on that path (Windows reports 0xC0000374 at <c>Otherreach+0x3fa8a33</c>,
/// "BlockNotBusy_DOUBLE_FREE", with no managed frame in the chain). The packaged abort path once called <c>GetTree().Quit(1)</c>
/// directly and was the one failure the fix could not reach, so the rule is asserted here: a non-zero quit may not ask the scene
/// tree to stop, and only <c>CaptureQuit</c> leaves the process. A zero-code quit still goes to the engine, which is the right
/// end for a run that finished - an orderly run keeps its exit tree, its leaks report and its final save flush.
/// </summary>
public class QuitPathTests
{
    [Fact]
    public void AFailedRun_DoesNotStopTheSceneTree()
    {
        string presentation = Path.Combine(RepositoryRoot(), "src", "Presentation");
        string sep = Path.DirectorySeparatorChar.ToString();
        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(presentation, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{sep}.godot{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}addons{sep}", StringComparison.Ordinal)))
        {
            string relative = Path.GetRelativePath(presentation, file);
            if (relative == "CaptureQuit.cs")
                continue;
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].Split("//")[0];
                if (Regex.IsMatch(code, @"\bEnvironment\.Exit\s*\("))
                    offenders.Add($"{relative}:{i + 1}: leaves the process outside CaptureQuit: {lines[i].Trim()}");
                var quit = Regex.Match(code, @"\.Quit\s*\(([^)]*)\)");
                if (!quit.Success)
                    continue;
                string argument = quit.Groups[1].Value.Trim();
                if (argument.Length > 0 && argument != "0")
                    offenders.Add($"{relative}:{i + 1}: a failed run stops the scene tree: {lines[i].Trim()}");
            }
        }
        Assert.True(offenders.Count == 0,
            "A failed run would unwind the half-built world and die of Godot's double free (B-001); route it through CaptureQuit:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void CaptureQuit_LeavesTheProcessOnAFailedRun_AndStopsTheTreeOnACleanOne()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Presentation", "CaptureQuit.cs"));
        Assert.Contains("Environment.Exit", source, StringComparison.Ordinal);
        Assert.Contains("code == 0", source, StringComparison.Ordinal);
        Assert.Contains("from.GetTree().Quit(code)", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "UNNAMED.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found");
    }
}
