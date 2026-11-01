using Godot;
using UNNAMED.Presentation.Diagnostics;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Release templates disable positional scene overrides. Enter these existing
    // CPU gates through the ordinary entry point, before any gameplay boot.
    private bool TryStartCpuDiagnostic()
    {
        string[] args = OS.GetCmdlineUserArgs();
        if (args.Length == 0 || !args[0].StartsWith(CpuDiagnostic.Prefix, StringComparison.Ordinal)) return false;
        SetProcess(false);
        if (DisplayServer.GetName() != "headless")
        {
            GD.PushError("CPU diagnostics require --headless");
            CaptureQuit.At(this, 1);
            return true;
        }
        Node? diagnostic = args[0][CpuDiagnostic.Prefix.Length..] switch
        {
            "native-cache" => new NativeSceneCacheGate(),
            "native-raw-profile" => new NativeRawSceneProfile(),
            "native-static-controls" => new NativeStaticPolicyControls(),
            "texture-controls" => new TextureMemoizationControls(),
            "character-profile" => new CharacterCreationProfile(),
            _ => null,
        };
        if (diagnostic is null)
        {
            GD.PushError("Unknown CPU diagnostic: " + args[0]);
            CaptureQuit.At(this, 1);
        }
        else AddChild(diagnostic);
        return true;
    }
}
