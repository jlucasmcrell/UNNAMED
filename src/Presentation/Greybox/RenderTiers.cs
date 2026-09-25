// UNNAMED Presentation - the renderer's quality tiers, Low to Ultra (Phase B, B1)
// Godot presentation only (D-11): how expensively the same world is drawn

using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// <c>--visual tier=low|medium|high|ultra</c> (see <see cref="VisualOptions"/> for the systems each tier draws with): the environment's
/// and the shadows' cost by tier. High is the target - RAZER's RTX 4070 Ti at 1920 x 1080 and 60 FPS - and draws what Phase A drew with
/// TAA alone (MSAA 4x cost half a millisecond and 170 MB on the 5090 for little on alpha-tested foliage). Medium drops the screen-space
/// indirect light and shortens SDFGI; Low drops SDFGI and the volumetric fog and halves the shadow atlas; Ultra adds screen-space
/// reflections and finer SDFGI rays. <c>phase_a</c> (the default) changes nothing.
/// </summary>
public static class RenderTiers
{
    /// <summary>The server-wide settings (shadow atlas and filter, SSAO/SSIL quality) for the run's tier.</summary>
    public static void ApplyGlobal()
    {
        switch (VisualOptions.Tier)
        {
            case "low":
                RenderingServer.DirectionalShadowAtlasSetSize(2048, true);
                RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftLow);
                RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.Low, true, 0.5f, 2, 50, 300);
                break;
            case "medium":
                RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftMedium);
                RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.Medium, true, 0.5f, 2, 50, 300);
                break;
            case "ultra":
                RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftUltra);
                RenderingServer.EnvironmentSetSdfgiRayCount(RenderingServer.EnvironmentSdfgiRayCount.Count64);
                break;
        }
    }

    /// <summary>The environment's and the sun's settings for the run's tier, over what the lighting built (Phase A's High look).</summary>
    public static void Apply(Godot.Environment environment, DirectionalLight3D sun)
    {
        switch (VisualOptions.Tier)
        {
            case "low":
                environment.SdfgiEnabled = false;
                environment.SsilEnabled = false;
                environment.VolumetricFogEnabled = false;
                sun.DirectionalShadowMaxDistance = 60f;
                break;
            case "medium":
                environment.SsilEnabled = false;
                environment.SdfgiCascades = 4;
                sun.DirectionalShadowMaxDistance = 75f;
                break;
            case "ultra":
                environment.SsrEnabled = true;
                environment.SsrMaxSteps = 64;
                environment.SdfgiCascades = 8;
                sun.DirectionalShadowMaxDistance = 120f;
                break;
        }
    }
}
