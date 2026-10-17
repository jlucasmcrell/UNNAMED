// UNNAMED Presentation - Main's crash hooks: an exception nothing caught goes to a local file, and the next launch says so (WP10, R2)
// Godot presentation only: this file renders state and submits commands; it never writes state. Nothing here sends anything anywhere.

using Godot;
using UNNAMED.Presentation.Diagnostics;

namespace UNNAMED.Presentation;

public partial class Main
{
    private CrashLog? _crashLog;
    private CrashNotice? _crashNotice;

    /// <summary>The folder the crash files go to: Godot's own log folder (<c>user://logs</c>), beside <c>godot.log</c>.</summary>
    public static string CrashFolder => ProjectSettings.GlobalizePath("user://logs");

    /// <summary>
    /// Hooks the unhandled and unobserved exceptions to the crash log, and takes the notice the last run left - only for a run with a
    /// window, so a headless harness never eats a player's notice.
    /// </summary>
    private void InstallCrashLog()
    {
        _crashLog = new CrashLog(CrashFolder) { Build = $"{(OS.IsDebugBuild() ? "debug" : "release")} {Engine.GetVersionInfo()["string"]}" };
        _crashLog.Install();
        if (DisplayServer.GetName() != "headless")
            _crashNotice = CrashLog.TakeNotice(CrashFolder);
        if (_crashNotice is not null)
            GD.PushWarning($"UNNAMED: the last run closed unexpectedly; its crash log is {_crashNotice.LogFile}");
    }

    /// <summary>The log folder in the system's file browser: a local path, opened on this machine.</summary>
    private static void OpenCrashFolder() => OS.ShellShowInFileManager(CrashFolder);
}
