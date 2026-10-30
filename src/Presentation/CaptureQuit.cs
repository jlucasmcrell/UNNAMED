// UNNAMED Presentation - every quit of the game goes through here, so a capture run's log names the line that ended it
// Godot presentation only: it prints and asks the engine to stop; it writes no state.

using System.Runtime.CompilerServices;
using Godot;

namespace UNNAMED.Presentation;

/// <summary>
/// <see cref="SceneTree.Quit(int)"/> only sets a flag the engine reads at the end of the frame, so when the tree is torn down the caller
/// has left the stack and a capture run cannot say who ended it - a harness finishing its scenes, a boot failing, or the window closing
/// look the same in the log. A quit made through here prints the file and line that asked for it and the stack that led there first, so
/// the last of those lines in a run's log is the answer.
/// </summary>
internal static class CaptureQuit
{
    public static void At(Node from, int code = 0, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        GD.Print($"CAPTURE quit {code} at frame {Engine.GetFramesDrawn()} ({DateTimeOffset.Now:O}) from " +
            $"{Path.GetFileName(file)}:{line}\n{System.Environment.StackTrace}");
        if (code == 0)
        {
            from.GetTree().Quit(code);
            return;
        }
        // A run that ends because it failed leaves the process here instead of unwinding it. Godot's own teardown frees a
        // block twice when the run ended after a failed start or a failed route (B-001: ntdll 0xC0000374 at
        // Otherreach+0x3fa8a33, "BlockNotBusy_DOUBLE_FREE", no managed frame in the chain), so the process died of a crash
        // report instead of the failure the log and the exit code already name. Nothing is lost: the log is written, the
        // alert has been shown, and a save's commit is atomic even if it is cut off (P-01). A zero-code quit still shuts
        // down through the engine, so an orderly run keeps its exit tree and its leaks report.
        GD.Print("CAPTURE failed run: leaving the process without an engine teardown");
        System.Environment.Exit(code);
    }

    /// <summary>
    /// The end of every orderly quit of a packaged build, called last from <c>Main._ExitTree</c> once the save is committed and the
    /// logs are written. Once a full world is built, a zero-code quit hits the same double free in Godot's own teardown as B-001
    /// (B-017: 0xC0000374 at ntdll+0x118095 from Otherreach+0x3fa8a33, no managed frame; a clean frontier boot on the full art root
    /// crashed this way every time), so the process leaves here with 0 instead. Every non-zero quit has already left through
    /// <see cref="At"/>, so 0 is the code the run asked for. What the engine teardown would still have done is free memory the
    /// OS takes back anyway; an editor run keeps it, with its leaks report.
    /// </summary>
    public static void Finished()
    {
        if (!OS.HasFeature("template"))
            return;
        GD.Print("CAPTURE finished: leaving the process without an engine teardown");
        Console.Out.Flush();
        System.Environment.Exit(0);
    }
}
