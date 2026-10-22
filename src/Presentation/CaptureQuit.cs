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
        from.GetTree().Quit(code);
    }
}
