// UNNAMED Presentation - the renderer's quality tiers, Low to Ultra (Phase B, B1)
// Godot presentation only (D-11): how expensively the same world is drawn

using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// <c>--visual tier=low|medium|high|ultra</c> (see <see cref="VisualOptions"/> for the systems each tier draws with): the environment's
/// and the shadows' cost by tier. The historical RAZER baseline was High at 1920 x 1080 and 60 FPS; the current Ultra art target is native 4K. High draws what Phase A drew with
/// TAA alone (MSAA 4x cost half a millisecond and 170 MB on the 5090 for little on alpha-tested foliage). Medium drops the screen-space
/// indirect light and shortens SDFGI; Low drops SDFGI and the volumetric fog and halves the shadow atlas; Ultra adds screen-space
/// reflections and finer SDFGI rays. <c>phase_a</c> (the default) changes nothing.
/// </summary>
public static class RenderTiers
{
    /// <summary>How far the prepared ground cover reaches, as a multiple of each kind's <c>fade_m</c>. The plants thin out one by one
    /// over the outer part of it (ScatterView), so a longer reach costs a thinned far band, not a full one; it is paid for by the
    /// grass no longer casting into the sun's cascades (2026-10-15).</summary>
    public static float GroundReach => VisualOptions.ReachScale * VisualOptions.Tier switch
    {
        "high" => 1.5f,
        "ultra" => 1.8f,
        _ => 1.0f,
    };

    /// <summary>How far the library models (trees, rocks, buildings) hold each level before the next lighter one, as a multiple of
    /// ArtLibrary's ladder: Ultra keeps a tree's crown off its crossed-plane impostor until 225 m rather than 150.</summary>
    public static float LodReach => VisualOptions.ReachScale * (VisualOptions.Tier == "ultra" ? 1.5f : 1.0f);

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
                // No percentage-closer soft shadows here either (the sun's angular size stays 0, as HollowView builds it).
                // PCSS softened a fencepost's shadow as it stretched away, but its per-frame sample pattern is the shadow
                // sparkle on swaying grass, and the 2026-10-02 walk measured 0.5 degrees no better than 1.0 for it. The
                // cost it carried goes to the reach instead (GroundReach and LodReach above).
                environment.SsrEnabled = true;
                environment.SsrMaxSteps = 64;
                // Six cascades, not eight. SDFGI's reach is cell size x 64 x 2^cascades, so at the 0.2 m cell the
                // environment carries, eight cascades computes to a 3,276 m range - well past the far plane, and
                // bought with about 49 MiB per cascade plus the CPU to keep it fed. Six is 819 m, which covers the
                // whole basin and its cliffs, and the budget goes to ray count instead.
                environment.SdfgiCascades = 6;
                // 350, not 120. The tier could only ever take back what HollowView asked for, and HollowView asked
                // for 90 - so in a 2 km world the whole village beyond the near houses, the hillside and the tree
                // line carried NO directional shadow at all and were lit by ambient sky plus SDFGI alone. The
                // splits at this distance put split 0's boundary around 17 m, which is the subject and the wall
                // beside him, and the last boundary near 150 m, so the near hills are still shadowed.
                sun.DirectionalShadowMaxDistance = 350f;
                // Soft Ultra widens the filter to 4/3 of Soft High's, so 1.5 here is High's softness (2.0) from more samples.
                sun.ShadowBlur = 1.5f;
                break;
        }
    }
}
