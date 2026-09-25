// UNNAMED Presentation - volumetric clouds by SunshineClouds2 (MIT, David House; pinned at a73a80b0, 2026-09-06) (Phase B, B0.3)
// Godot presentation only (D-11): a compositor effect over the world environment; it tracks the sun it is given and owns nothing else

using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// <c>--visual clouds=sunshine</c>: SunshineClouds2's ray-marched cloud layer as a compositor effect on the world environment, lit by the
/// sun it tracks and ambient-sampled from the environment. Configured from the addon's own example resource (its noise textures and
/// shaders), duplicated per run. The addon has no tagged release; <c>addons/SunshineClouds2/PINNED_COMMIT.txt</c> names the commit.
/// </summary>
public static class CloudsView
{
    private const string Resource = "res://Environment/otherreach_clouds.tres";   // ours: the addon example's set-up, its mask copied into the addon
    private const string Driver = "res://addons/SunshineClouds2/SunshineCloudsDriver.gd";

    /// <summary>The clouds on <paramref name="world"/>, tracking <paramref name="sun"/>; false (and why) when the addon will not load.</summary>
    public static bool Attach(WorldEnvironment world, DirectionalLight3D sun, out string? why)
    {
        why = null;
        if (!ResourceLoader.Exists(Resource) || !ResourceLoader.Exists(Driver))
        {
            why = "the SunshineClouds2 addon is not in the project";
            return false;
        }
        if (GD.Load<Resource>(Resource)?.Duplicate(true) is not CompositorEffect effect || GD.Load<Script>(Driver) is not { } driverScript)
        {
            why = "the SunshineClouds2 example resource did not load as a compositor effect";
            return false;
        }
        var effects = world.Compositor?.CompositorEffects ?? new Godot.Collections.Array<CompositorEffect>();
        effects.Add(effect);
        world.Compositor = new Compositor { CompositorEffects = effects };

        var driver = new Node { Name = "SunshineCloudsDriver" };
        driver.SetScript(driverScript);
        world.AddChild(driver);
        driver.Set("clouds_resource", effect);
        driver.Set("ambience_sample_environment", world.Environment);
        driver.Set("tracked_directional_lights", new Godot.Collections.Array<DirectionalLight3D> { sun });
        driver.Set("tracked_directional_light_shadow_steps", new Godot.Collections.Array<int> { 32 });
        driver.Set("update_continuously", true);
        GD.Print("UNNAMED clouds: SunshineClouds2 @ a73a80b0 on the world environment, tracking " + sun.Name);
        return true;
    }
}
