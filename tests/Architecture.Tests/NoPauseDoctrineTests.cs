using System.Text.RegularExpressions;

namespace UNNAMED.Architecture.Tests;

/// <summary>
/// The owner's ruling (2026-10-02, restated 2026-10-17 under "normal MMO rules"): nothing pauses the world - no menu, no panel, no
/// conversation, no option. The presentation may never pause the scene tree, process only while paused, or change the engine's time
/// scale; the one exception is the visual audit's capture harness, which freezes a frame to photograph it and never runs in play. A node
/// switched off because it is out of view is culling, not a pause, and is allowed only where it is listed here with its reason. The
/// running half of the doctrine is the smoke's no-pause step, which holds every screen open and sees the world tick under each.
/// </summary>
public class NoPauseDoctrineTests
{
    private static readonly (string Pattern, string What)[] Forbidden =
    {
        (@"\bPaused\s*=[^=]", "pauses the scene tree"),
        (@"\bpaused\s*=[^=]", "pauses the scene tree (GDScript)"),
        (@"ProcessModeEnum\.WhenPaused", "runs only while the tree is paused"),
        (@"PROCESS_MODE_WHEN_PAUSED", "runs only while the tree is paused (GDScript)"),
        (@"ProcessModeEnum\.Disabled", "switches a node's processing off"),
        (@"PROCESS_MODE_DISABLED", "switches a node's processing off (GDScript)"),
        (@"\bTimeScale\s*=[^=]", "changes the engine's time scale"),
        (@"\btime_scale\s*=[^=]", "changes the engine's time scale (GDScript)"),
    };

    /// <summary>Files allowed one of the patterns, and why it is not a pause.</summary>
    private static readonly Dictionary<string, (string Pattern, string Why)> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [Path.Combine("Greybox", "CreaturesView.cs")] = ("ProcessModeEnum.Disabled", "a creature's figure out of view stops animating; the creature runs on in the simulation"),
        [Path.Combine("Greybox", "PerchedRavens.cs")] = ("ProcessModeEnum.Disabled", "a perched raven not shown stops its idle; ravens are scenery"),
        ["VisualAudit.cs"] = ("TimeScale", "the visual audit's capture harness freezes a frame to photograph it; it never runs in play"),
        [Path.Combine("Ui", "CreatorPreview.cs")] = ("ProcessModeEnum.Disabled", "a hidden creator preview stops rendering and animating; the world simulation keeps running"),
    };

    [Fact]
    public void ThePresentation_NeverPausesTheWorld()
    {
        string presentation = Path.Combine(RepositoryRoot(), "src", "Presentation");
        string sep = Path.DirectorySeparatorChar.ToString();
        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(presentation, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".gd", StringComparison.Ordinal))
                     .Where(f => !f.Contains($"{sep}.godot{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}addons{sep}", StringComparison.Ordinal)))
        {
            string relative = Path.GetRelativePath(presentation, file);
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].Split("//")[0];
                if (code.TrimStart().StartsWith('#') || code.TrimStart().StartsWith("///", StringComparison.Ordinal))
                    continue;
                foreach (var (pattern, what) in Forbidden)
                {
                    if (!Regex.IsMatch(code, pattern))
                        continue;
                    if (Allowed.TryGetValue(relative, out var allowed) && pattern.Contains(allowed.Pattern.Replace(".", @"\."), StringComparison.Ordinal))
                        continue;
                    offenders.Add($"{relative}:{i + 1}: {what}: {lines[i].Trim()}");
                }
            }
        }
        Assert.True(offenders.Count == 0, "Something in src/Presentation would stop the world (the no-pause ruling):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void EveryAllowance_IsStillNeeded()
    {
        string presentation = Path.Combine(RepositoryRoot(), "src", "Presentation");
        foreach (var (file, (pattern, why)) in Allowed)
            Assert.True(File.ReadAllText(Path.Combine(presentation, file)).Contains(pattern, StringComparison.Ordinal),
                $"{file} no longer uses {pattern} ({why}): take it off the no-pause allowance list");
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
