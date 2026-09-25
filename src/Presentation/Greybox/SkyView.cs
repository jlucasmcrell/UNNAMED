// UNNAMED Presentation - the sky, sun and moon when Phase B's sky systems draw them (B0.3): Sky3D (MIT, TokisanGames, v2.1.0) or an HDRI
// Godot presentation only (D-11): the hour is an input, never kept here; the simulation holds no clock yet, so the sky holds a fixed hour
// unless a harness sets one (--visual hour=H)

using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// <c>--visual sky=sky3d</c>: Sky3D's physically placed sun and moon, stars, sky dome and its own time of day, over the game's own
/// environment (its tonemap, AO, GI, glow and fog carry over; Sky3D replaces the sky and drives the lights). Sky3D's clock never runs:
/// its game and editor time are off and the hour is set, one way. <c>--visual sky=hdri</c>: the staged Poly Haven <c>quarry_cloudy</c>
/// panorama as the sky and its light, for comparison.
/// </summary>
public static class SkyView
{
    public const float DefaultHour = 10.5f;
    private const string Sky3DScript = "res://addons/sky_3d/src/Sky3D.gd";

    /// <summary>The sky's hour: the harness's override, or the fixed presentation default (the simulation has no clock to follow).</summary>
    public static float Hour => VisualOptions.Hour ?? DefaultHour;

    /// <summary>Sky3D over <paramref name="environment"/>, its sun given the classic sun's shadow tuning; null (and why) if it will not load.</summary>
    public static Node? BuildSky3D(Node parent, Godot.Environment environment, DirectionalLight3D classicSun, out string? why)
    {
        why = null;
        if (!ResourceLoader.Exists(Sky3DScript) || GD.Load<Script>(Sky3DScript) is not { } script)
        {
            why = "the Sky3D addon is not in the project";
            return null;
        }
        // Sky3D installs its own sky (dome, stars, moon, clouds) only over an environment without one: take the Phase-A sky off.
        environment.Sky = null;
        var sky = new WorldEnvironment { Name = "Sky3D", Environment = environment };
        sky.SetScript(script);
        // Sky3D makes its sun, moon, dome and clock when it enters the tree - which, built with the scene, is later than here: set it up then.
        sky.Ready += () => Configure(sky, classicSun);
        parent.AddChild(sky);
        return sky;
    }

    private static readonly Color FogDay = new(0.66f, 0.68f, 0.70f);

    /// <summary>How much daylight at a sun altitude (degrees): full from 10 degrees up, through civil twilight to none at -6.</summary>
    public static float Daylight(float sunAltitudeDegrees) => Mathf.Clamp((sunAltitudeDegrees + 6f) / 16f, 0f, 1f);

    private static void Configure(WorldEnvironment sky, DirectionalLight3D classicSun)
    {
        sky.Set("game_time_enabled", false);
        sky.Set("editor_time_enabled", false);
        sky.Set("current_time", Hour);
        // TimeOfDay moves the sun and moon when its time changes, once its dome is linked: recompute them next frame to be sure.
        if (sky.Get("tod").AsGodotObject() is { } tod)
            tod.CallDeferred("_update_celestial_coords");
        if (sky.Get("sun").AsGodotObject() is DirectionalLight3D sun)
        {
            sun.ShadowEnabled = true;
            sun.ShadowBlur = classicSun.ShadowBlur;
            sun.ShadowBias = classicSun.ShadowBias;
            sun.ShadowNormalBias = classicSun.ShadowNormalBias;
            sun.DirectionalShadowMaxDistance = classicSun.DirectionalShadowMaxDistance;
            sun.DirectionalShadowBlendSplits = true;
            sun.LightAngularDistance = classicSun.LightAngularDistance;
        }
        // The weather (B5): Sky3D's clouds and light, the fog's density and the wind, by the run's state.
        WeatherStates.Apply(sky, VisualOptions.Weather ?? "fair");
        // The environment's depth fog glows with a fixed light colour: scale it with the daylight, or night stays a grey noon.
        // Sky3D's SkyDome.sun_altitude is radians from the zenith, negated (its TimeOfDay, read): elevation = 90 - |it| degrees.
        float zenith = Mathf.Abs(sky.Get("sky").AsGodotObject()?.Get("sun_altitude").AsSingle() ?? 0.8f);
        float altitude = 90f - Mathf.RadToDeg(zenith);
        float daylight = Daylight(altitude);
        if (sky.Environment is { } environment)
        {
            environment.FogLightColor = FogDay * Mathf.Max(daylight, 0.035f);
            environment.VolumetricFogAmbientInject = 0.35f * Mathf.Max(daylight, 0.1f);
        }
        // Night you can see by (B5): Sky3D's night ambient boost does nothing under SDFGI (the GI replaces the ambient), and the moon
        // may be down; a dim, cool key light from high in the sky stands in for moonlight while the sun is under the horizon.
        if (daylight < 0.3f)
        {
            float night = 1f - daylight / 0.3f;
            sky.AddChild(new DirectionalLight3D
            {
                Name = "NightFill", LightColor = new Color(0.55f, 0.65f, 0.9f), LightEnergy = 0.16f * night, ShadowEnabled = true,
                ShadowBlur = 2f, RotationDegrees = new Vector3(-52f, 140f, 0f), DirectionalShadowMaxDistance = 60f,
            });
        }
        if (VisualOptions.Clouds == "sunshine" && sky.Get("sun").AsGodotObject() is DirectionalLight3D cloudSun)
        {
            if (CloudsView.Attach(sky, cloudSun, out string? cloudsWhy))
                sky.Set("clouds_enabled", false);   // Sky3D's flat 2D cloud layer gives way to the volumetric one
            else
                GD.PushWarning("UNNAMED clouds: " + cloudsWhy);
        }
        GD.Print($"UNNAMED sky: Sky3D {sky.Get("version")} at {Hour:0.00} h (its clock stopped; is_day {sky.Call("is_day")}, sun altitude "
                 + $"{altitude:0.0}, daylight {daylight:0.00})");
    }

    /// <summary>The HDRI panorama as the environment's sky; false (and why) when the file is missing.</summary>
    public static bool ApplyHdri(Godot.Environment environment, string? assetRoot, out string? why)
    {
        why = null;
        string? path = assetRoot is null ? null : Path.Combine(assetRoot, "materials", "sky_ph_quarry_cloudy", "quarry_cloudy_4k.hdr");
        if (path is null || !File.Exists(path))
        {
            why = "the quarry_cloudy HDRI is not in the asset workspace";
            return false;
        }
        var image = Image.LoadFromFile(path);
        environment.Sky = new Sky { SkyMaterial = new PanoramaSkyMaterial { Panorama = ImageTexture.CreateFromImage(image) },
            RadianceSize = Sky.RadianceSizeEnum.Size256, ProcessMode = Sky.ProcessModeEnum.Automatic };
        return true;
    }
}
