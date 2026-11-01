using Godot;

namespace UNNAMED.Presentation.Diagnostics;

internal static class CpuDiagnostic
{
    internal const string Prefix = "--cpu-diagnostic=";
    internal static string[] Arguments()
    {
        string[] args = OS.GetCmdlineUserArgs();
        return args.Length > 0 && args[0].StartsWith(Prefix, StringComparison.Ordinal) ? args[1..] : args;
    }
}
