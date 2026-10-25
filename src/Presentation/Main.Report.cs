// UNNAMED Presentation - Main's problem reports, screenshots and build id: what a tester keeps and sends, written locally (public playtest gate E2, F3)
// Godot presentation only: this file renders state and submits commands; it never writes state. Nothing here sends anything anywhere.

using Godot;
using UNNAMED.Presentation.Diagnostics;
using UNNAMED.Presentation.Settings;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation;

public partial class Main
{
    private ReportPanel _report = null!;
    private byte[]? _reportShot;

    /// <summary>Where reports are written: beside the game in an exported build (the folder the README names), else <c>user://reports</c>.</summary>
    private static string ReportsFolder => OS.HasFeature("template")
        ? Path.Combine(Home(), "reports")
        : ProjectSettings.GlobalizePath("user://reports");

    /// <summary>
    /// The build, from the package's <c>VERSION.txt</c> (<c>build &lt;label&gt;</c> and <c>commit &lt;sha&gt;</c> lines, written by
    /// build_playtest.ps1): "d9 · 6131fc64". A run from the repository says "development".
    /// </summary>
    private static string BuildId()
    {
        string file = Path.Combine(Home(), "VERSION.txt");
        if (!OS.HasFeature("template") || !File.Exists(file))
            return "development";
        var lines = File.ReadAllLines(file);
        string label = lines.FirstOrDefault(l => l.StartsWith("build ", StringComparison.Ordinal))?[6..].Trim() ?? string.Empty;
        string commit = lines.FirstOrDefault(l => l.StartsWith("commit ", StringComparison.Ordinal))?[7..].Trim() ?? string.Empty;
        return string.Join(" · ", new[] { label, commit.Length > 8 ? commit[..8] : commit }.Where(p => p.Length > 0));
    }

    /// <summary>Where screenshots go: beside the game in an exported build, else <c>user://screenshots</c>.</summary>
    private static string ScreenshotsFolder => OS.HasFeature("template")
        ? Path.Combine(Home(), "screenshots")
        : ProjectSettings.GlobalizePath("user://screenshots");

    /// <summary>The last frame, at the window's full size, as <c>otherreach_NNNN.png</c>; the toast comes after, so it is never in the picture.</summary>
    private void TakeScreenshot()
    {
        Directory.CreateDirectory(ScreenshotsFolder);
        int next = Directory.EnumerateFiles(ScreenshotsFolder, "otherreach_*.png")
            .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f)["otherreach_".Length..], out int n) ? n : 0)
            .DefaultIfEmpty(0).Max() + 1;
        string file = Path.Combine(ScreenshotsFolder, $"otherreach_{next:D4}.png");
        var error = GetViewport().GetTexture().GetImage().SavePng(file);
        _hud.Toast(error == Error.Ok ? $"Screenshot saved: {Path.GetFileName(file)}" : $"The screenshot could not be saved ({error})", 2);
    }

    /// <summary>The interface hidden, for a clear view or a picture: the HUD and the hint line, until the key is pressed again.</summary>
    private void ToggleInterface()
    {
        _hud.Visible = !_hud.Visible;
        if (_hintLine is not null)
            _hintLine.Visible = _hud.Visible;
    }

    private void BuildReportPanel()
    {
        _report = new ReportPanel { Name = "Report", WriteReport = WriteReport };
        AddChild(_report);
    }

    /// <summary>The screen as it was before the panel drew over it, then the panel.</summary>
    private void OpenReport()
    {
        _reportShot = DisplayServer.GetName() == "headless" ? null : GetViewport().GetTexture().GetImage()?.SavePngToBuffer();
        _report.Open();
    }

    private string WriteReport(string note)
    {
        var files = new List<string> { LogPath, ProjectSettings.GlobalizePath(SettingsStore.Path) };
        if (Directory.Exists(CrashFolder))
            files.AddRange(Directory.EnumerateFiles(CrashFolder, "crash_*.txt").OrderByDescending(File.GetLastWriteTimeUtc).Take(1));
        // The save in play: every file of the slot written last (the document and its integrity files), not its backups.
        if (_session?.SaveFolder is { } saves && Directory.Exists(saves)
            && Directory.EnumerateDirectories(saves).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault() is { } slot)
            files.AddRange(Directory.EnumerateFiles(slot));
        var memory = OS.GetMemoryInfo();
        string machine = string.Join("\n",
            $"Graphics: {RenderingServer.GetVideoAdapterName()} ({RenderingServer.GetVideoAdapterVendor()}, {RenderingServer.GetVideoAdapterApiVersion()})",
            $"Graphics memory in use: {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1048576} MB",
            $"Processor: {OS.GetProcessorName()} ({OS.GetProcessorCount()} threads)",
            $"Memory: {memory["physical"].AsInt64() / 1073741824} GB",
            $"System: {OS.GetName()} {OS.GetVersion()}",
            $"Window: {DisplayServer.WindowGetSize()} {DisplayServer.WindowGetMode()}, quality {VisualOptions.Tier}",
            $"Engine: {Engine.GetVersionInfo()["string"]}");
        return ProblemReport.Write(ReportsFolder, new ProblemReportParts(note, BuildId(), machine, files, _reportShot));
    }
}
