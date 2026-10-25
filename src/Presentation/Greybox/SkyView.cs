// UNNAMED Presentation - the sky, sun and moon when Phase B's sky systems draw them (B0.3): Sky3D (MIT, TokisanGames, v2.1.0) or an HDRI
// Godot presentation only (D-11): the hour is an input, never kept here; the simulation holds no clock yet, so the sky holds a fixed hour
// unless a harness sets one (--visual hour=H)

using System.Text.Json.Nodes;
using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// <c>--visual sky=sky3d</c>: Sky3D's physically placed sun and moon, stars, sky dome and its own time of day, over the game's own
/// environment (its tonemap, AO, GI, glow and fog carry over; Sky3D replaces the sky and drives the lights). Sky3D's clock never runs:
/// its game and editor time are off and the hour is set, one way. <c>--visual sky=hdri</c>: the staged Poly Haven <c>quarry_cloudy</c>
/// panorama as the sky and its light, for comparison.
/// </summary>
public static class SkyView
{
    /// <summary>
    /// What the world clock reads at tick 0, in hours: config.time's clock_at_world_start_h, set by the frame loop from the booted
    /// session, so the sky and the creatures' hours (CombatContent) run from the one reading. 07:30 by the owner's ruling (2026-10-06,
    /// confirmed 2026-10-16): spawn looks east into a low morning sun under the ring. The value here only serves a run with no session.
    /// </summary>
    public static float ClockAtWorldStart { get; set; } = 7.5f;
    private const string Sky3DScript = "res://addons/sky_3d/src/Sky3D.gd";

    // Hour is defined below, beside the clock that drives it. The note that used to sit here - "the simulation has
    // no clock to follow" - was simply wrong: content/config/time.yaml has given the world a clock all along, and
    // content/config/time.yaml's own numbers put a full day and night at 48 real minutes.

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
        sky.Ready += () =>
        {
            Configure(sky, classicSun);
            // Never in the tree (Sky3D's sun replaces it): left alone it leaks its light at exit, where a release build dies of
            // heap corruption (0xC0000374).
            if (GodotObject.IsInstanceValid(classicSun))
                classicSun.Free();
        };
        parent.AddChild(sky);
        _sky = sky;   // the clock Advance() drives each frame; null-checked there, so an HDRI run is unaffected
        return sky;
    }

    private static readonly Color FogDay = new(0.66f, 0.68f, 0.70f);

    /// <summary>
    /// The colour the fog's light takes at a low sun, for <c>--visual air=hue</c>. Chosen to carry very nearly the same
    /// LUMINANCE as <see cref="FogDay"/> - (0.85, 0.62, 0.42) reads 0.654 against its 0.677 - because the whole point
    /// of that arm is to move the air's HUE while leaving its brightness, and therefore the shadow floor that passes 8
    /// and 9 were spent buying, exactly where it is.
    /// </summary>
    private static readonly Color GoldenAir = new(0.85f, 0.62f, 0.42f);
    private static readonly Color OtherAir = new(0.64f, 0.67f, 0.80f);

    private const string NightFillName = "NightFill";

    // HOW OFTEN THE AIR IS RECOMPUTED, and why it is not every frame. The clock moves 1/2400 of a game hour a tick
    // and the sun crosses about 10 degrees a game hour, so 0.01 of a game hour is 0.1 of a degree of sun - a step in
    // the fog's daylight term of 0.006, well under one code value of the colour it produces. At time.yaml's rate
    // (1 game hour = 120 real seconds) that is a recompute every 1.2 real seconds, about 0.8 a second. Between
    // recomputes a frame pays one subtraction and one comparison; a recompute reads the dome's sun altitude once,
    // writes two environment properties and looks up one child node.
    private const float AirStepHours = 0.01f;

    private static float _airHour = float.NaN;

    /// <summary>The sun's elevation in degrees, as Sky3D's own dome has it: 90 less its angle from the zenith.</summary>
    private static float SunAltitude(WorldEnvironment sky)
    {
        // Sky3D's SkyDome.sun_altitude is radians from the zenith, negated (its TimeOfDay, read): elevation = 90 - |it| degrees.
        // Setting TimeOfDay.current_time recomputes it in the same call (TimeOfDay.gd:182 -> 386/401), so this is never a frame stale.
        float zenith = Mathf.Abs(sky.Get("sky").AsGodotObject()?.Get("sun_altitude").AsSingle() ?? 0.8f);
        return 90f - Mathf.RadToDeg(zenith);
    }

    private static float _daylightLevel = 1f;

    /// <summary>
    /// How much daylight the run is in right now: 0 at night, 1 in full day, civil twilight between - the same
    /// number, from the same function, that sets the fog's light colour and the night fill. Cached rather than
    /// computed per call, because the callers are lights and there is one per lantern: <see cref="ApplyAir"/> is
    /// the only writer and it runs on the air's own throttle, so this costs a field read per frame per light.
    /// Before the sky is built it falls back to the hour, which is what the ambient audio and life gates use.
    /// </summary>
    public static float DaylightLevel => _sky is WorldEnvironment ? _daylightLevel : DaylightNow;

    /// <summary>
    /// The air, by the sun's elevation: the depth fog's light colour, the light the volumetric pass takes from the
    /// sky, and the fill light that stands in for the moon. A pure function of the hour and the whole of what this
    /// file writes on the environment - which is why it is called both when the sky is built and as the clock runs.
    /// </summary>
    private static void ApplyAir(WorldEnvironment sky, float altitude)
    {
        float daylight = Daylight(altitude);
        _daylightLevel = daylight;
        Otherworld.Night(1f - daylight);
        if (sky.Environment is { } environment)
        {
            // The environment's depth fog glows with a fixed light colour: scale it with the daylight, or night stays a grey noon.
            //
            // WHICH SCALE, AND WHY THERE ARE TWO. `daylight` is clamped to 0-1 and reaches 1.00 by 08:00, so both
            // products below are CONSTANT for as long as it is pinned. Measured over fifteen hourly samples
            // (docs/phase_b/air_day): the fog light colour and the volumetric ambient read BYTE-IDENTICAL at
            // 08:00, 10:00, 12:00, 14:00 and 16:00 - five samples spanning eight hours - while the sun's altitude
            // moved 21.3 -> 42.4 -> 51.0 -> 40.0 -> 17.9 across those same samples. So the air has three states a
            // day and one of them covers the whole of a play session, which is what "the lighting is static" means
            // as a measurement rather than as an impression.
            //
            // `sun` reaches THE SAME TWO ENDPOINTS through the altitude, which keeps moving all day - so the night
            // and noon values are exactly what shipped and only the plateau is filled in. That is what makes this a
            // fair A/B: it changes precisely the thing under test and nothing else. Selected by `--visual air=sun`;
            // the default `flat` branch below is the shipped expression, untouched.
            //
            // AND `sun` WAS MEASURED AND REJECTED, WHICH IS WHY THERE IS A THIRD ARM. At hour 08 it took the fog
            // light from (0.6600, 0.6800, 0.7000) to (0.3326, 0.3427, 0.3527) and the ambient from 0.3500 to
            // 0.1881, and the picture it produced bought a 1.6 per cent wider p05..p95 range with THREE TIMES the
            // crushed shadow (2.05 -> 6.09 per cent of the frame under 0.05). This project has refused that trade
            // three times by its own recorded standard - pass 8's win was "crushed black 18.02 -> 8.12", pass 9 took
            // deep shade "39.73 -> 18.60", and contrast 1.25 was turned down for "buying crispness by crushing
            // nearly a third of the picture". So `sun` is kept as the arm it was built to be and is NOT the fix.
            //
            // `hue` IS THE FIX THE MEASUREMENT ASKS FOR: VARY THE COLOUR, HOLD THE INTENSITY. Both scaling terms
            // below are the SHIPPED ones, untouched, so the ambient inject stays exactly where passes 8 and 9 tuned
            // it and the shadow floor cannot move. Only the fog's HUE follows the sun - no tint above 45 degrees of
            // altitude, a warm cast as it drops - and the tint is chosen to carry very nearly the SAME LUMINANCE as
            // FogDay (0.677 against 0.654), so what changes is the colour of the air and not its brightness.
            float light, ambient;
            Color fog = FogDay;
            if (VisualOptions.AirFollowsSun)
            {
                // 0 at 4 degrees below the horizon, 1 at 48 above. This region's sun runs -82.7 to +51.0, so noon
                // reaches the top of the ramp and the two transitions occupy the ends of the day.
                float lift = Mathf.Clamp((altitude + 4f) / 52f, 0f, 1f);
                light = Mathf.Lerp(0.035f, 1f, lift);
                ambient = Mathf.Lerp(0.035f, 0.35f, lift);
            }
            else
            {
                light = Mathf.Max(daylight, 0.035f);
                ambient = 0.35f * Mathf.Max(daylight, 0.1f);
                if (VisualOptions.AirFollowsSunHue)
                {
                    // 0 at 45 degrees and above, 1 at 5 and below. Across the measured plateau the sun runs from
                    // about 12 degrees at 07:30 to 51 at noon and back to 12 at 17:00, so this spends the whole
                    // middle of the day at zero and moves at both ends - which is the shape the fifteen hourly
                    // samples asked for.
                    float warmth = 1f - Mathf.Clamp((altitude - 5f) / 40f, 0f, 1f);
                    fog = FogDay.Lerp(GoldenAir, warmth * 0.55f);
                }
            }
            // The otherworld concepts lean the air toward the ring-sky's cool lavender, at the same luminance as FogDay (0.677).
            fog = fog.Lerp(OtherAir, 0.6f * Otherworld.Strength);
            environment.FogLightColor = fog * light;
            environment.VolumetricFogAmbientInject = ambient;
        }
        // Night you can see by (B5): Sky3D's night ambient boost does nothing under SDFGI (the GI replaces the ambient), and the moon
        // may be down; a dim, cool key light from high in the sky stands in for moonlight while the sun is under the horizon.
        if (daylight < 0.3f)
        {
            if (sky.GetNodeOrNull<DirectionalLight3D>(NightFillName) is not { } fill)
            {
                fill = new DirectionalLight3D
                {
                    Name = NightFillName, LightColor = new Color(0.55f, 0.65f, 0.9f), ShadowEnabled = true,
                    ShadowBlur = 2f, RotationDegrees = new Vector3(-52f, 140f, 0f), DirectionalShadowMaxDistance = 60f,
                };
                sky.AddChild(fill);
            }
            fill.LightEnergy = 0.16f * (1f - daylight / 0.3f);
        }
        else if (sky.GetNodeOrNull<DirectionalLight3D>(NightFillName) is { } spent)
        {
            // Morning. Taken off rather than left at zero energy, or a 48-minute day would leave one more of them
            // behind on every pass through the night and the run would accumulate them for as long as it lived.
            spent.QueueFree();
        }
    }

    // THE CLOCK THE SKY WAS BUILT WITH NEVER RAN, WHICH IS THE OWNER'S "LIGHTING IN GENERAL IS PRETTY STATIC".
    // Sky3D has a full day and night cycle - its own sun, moon, stars and dome - and this file set its
    // `current_time` ONCE, from a fixed --visual hour defaulting to 10.5, and never touched it again. The boot
    // line even said so out loud: "Sky3D ... at 10.50 h (its clock stopped)". So the sun never moved, the light
    // colour never changed and the shadows never lengthened, for the whole life of the process.
    //
    // The world clock was already there and already correct: content/config/time.yaml gives one game day as
    // 1440 game minutes at 2 real seconds each - 2880 real seconds, or 48 real minutes for a full day and night.
    // The sun crosses 7.5 degrees a minute at that rate, which is plainly visible while walking.
    private static Node? _sky;

    /// <summary>The Sky3D node the weather is drawn on, once built.</summary>
    public static WorldEnvironment? Sky3DNode => _sky as WorldEnvironment is { } sky && GodotObject.IsInstanceValid(sky) ? sky : null;

    /// <summary>The hour the sky is showing: the world clock when a run has one, else the fixed --visual hour.</summary>
    public static float Hour => IsFinite(_clockHour) ? _clockHour : VisualOptions.Hour ?? ClockAtWorldStart;

    private static float _clockHour = float.NaN;

    private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    /// <summary>
    /// Advance the sky to the world clock. Called once a frame by the frame loop with the simulation's tick;
    /// does nothing when the run's sky is not Sky3D (an HDRI sky has no clock to move) or before it is built.
    /// </summary>
    public static void Advance(double worldTick, double ticksPerDay)
    {
        if (_sky is not WorldEnvironment sky || !GodotObject.IsInstanceValid(sky) || ticksPerDay <= 0)
            return;
        double day = worldTick / ticksPerDay;
        // THE DAY STARTS AT THE WORLD CLOCK'S READING AT TICK 0 (ClockAtWorldStart, from config.time), not at
        // midnight, which would open every run in darkness. The clock advances from there, and 48 real minutes
        // later it has been through a night - the same hours the creatures rest by.
        //
        // `--visual hour=N` PINS IT, AND UNTIL NOW IT DID NOT. The option has existed all along ("the harness's
        // time-of-day override, 0-24") and Hour fell back to it - but only while _clockHour was not yet finite,
        // so the first Advance overwrote it and every run followed the world clock regardless. That made the
        // option look implemented and behave as dead.
        //
        // WHY IT MATTERS MORE THAN IT LOOKS: two runs reach the same station at different world ticks, so they
        // photograph the same place at different sun positions - a before/after pair was measured at sky clock
        // 13.50 h against 15.50 h, two hours apart, and a blind judge scored the LIGHTING (vegetation, lighting,
        // tree silhouette and terrain integration all 9 against 6) rather than the thing that had changed. Any
        // A/B whose arms come from separate runs needs this pinned on BOTH of them.
        _clockHour = VisualOptions.Hour is { } pinned
            ? pinned
            : (float)((ClockAtWorldStart + (day - Math.Floor(day)) * 24.0) % 24.0);
        // Sky3D recomputes its sun and moon from this when it is set; TimeOfDay is its own name for the hour.
        sky.Set("current_time", _clockHour);
        // AND THE AIR FOLLOWS THE SAME CLOCK. The line above is where the sun is; this is what the air is made of, and
        // until now only the first half of the atmosphere ran per frame. Configure wrote the fog light colour, the
        // volumetric fog's ambient and the night fill ONCE, from sky.Ready, so in a live run they were pinned to the
        // hour the world booted at - 10.5, daylight 1.00 - for the whole life of the process. Measured on the build
        // before this change, over one run: at 17.00 h the sun stood at 5.2 degrees and this file's own daylight term
        // read 0.70, and fog_light_color was still exactly (0.6600, 0.6800, 0.7000) with volumetric_fog_ambient_inject
        // still 0.3500 and NightFill never created at all; at 18.00 h the sun was 8 degrees UNDER the horizon and every
        // one of those numbers was unchanged. Half the atmosphere was on the world clock and half was on the boot.
        if (float.IsNaN(_airHour) || Math.Abs(_clockHour - _airHour) >= AirStepHours)
        {
            _airHour = _clockHour;
            ApplyAir(sky, SunAltitude(sky));
        }
        if (sky.Environment is { } air)
            CelestialView.Advance(_clockHour, _daylightLevel, air);
        // Logged a quarter of a game hour apart, which is every 30 real seconds at time.yaml's rate: the only way
        // to tell a running clock from a stopped one from outside the process, and the thing that was wrong.
        if (Math.Abs(_clockHour - _loggedHour) >= 0.25f || float.IsNaN(_loggedHour))
        {
            _loggedHour = _clockHour;
            GD.Print($"UNNAMED sky clock: {_clockHour:0.00} h at world tick {worldTick:0} of {ticksPerDay:0} a day");
            AirProbe();
        }
    }

    /// <summary>
    /// INSTRUMENT ONLY - it writes nothing and changes nothing; it reports. The sky clock line above says
    /// where the sun is; it does not say what the AIR was set to, and the air is the half of the atmosphere
    /// that this file configures itself. One line per quarter game hour, in the same gate as the clock, so a
    /// run's log carries the fog light colour and the night fill as a function of the hour - the only way to
    /// see from outside the process whether the air follows the clock or is pinned to the moment of boot.
    /// </summary>
    private static void AirProbe()
    {
        var environment = (_sky as WorldEnvironment)?.Environment;
        var fill = _sky?.GetNodeOrNull<DirectionalLight3D>(NightFillName);
        float altitude = _sky as WorldEnvironment is { } world ? SunAltitude(world) : 0f;
        GD.Print($"UNNAMED air probe: {_clockHour:0.00} h fog_light_color "
                 + $"({environment?.FogLightColor.R:0.0000}, {environment?.FogLightColor.G:0.0000}, {environment?.FogLightColor.B:0.0000}) "
                 + $"volumetric_fog_ambient_inject {environment?.VolumetricFogAmbientInject:0.0000} sun altitude {altitude:0.0} "
                 + $"daylight {Daylight(altitude):0.00} NightFill "
                 + (fill is null ? "ABSENT" : $"present energy {fill.LightEnergy:0.0000}"));
    }

    private static float _loggedHour = float.NaN;

    /// <summary>Whether the run's hour is day (the sun well up): for what only runs by day (the atmosphere's pollen).</summary>
    public static float DaylightNow => Hour is > 7f and < 18f ? 1f : 0f;

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
        // The air at the hour the sky was built at. Advance() applies the same thing as the clock runs, from the same
        // function, so a locked run and a running one cannot drift apart - and this call is here as well as there
        // because a run whose world never ticks (a sky with no simulation behind it) still has to have air.
        float altitude = SunAltitude(sky);
        ApplyAir(sky, altitude);
        float daylight = Daylight(altitude);
        CelestialView.Attach(sky);
        if (sky.Environment is { } air)
            CelestialView.Advance(Hour, daylight, air);
        if (VisualOptions.Clouds == "sunshine" && sky.Get("sun").AsGodotObject() is DirectionalLight3D cloudSun)
        {
            if (CloudsView.Attach(sky, cloudSun, out string? cloudsWhy))
                sky.Set("clouds_enabled", false);   // Sky3D's flat 2D cloud layer gives way to the volumetric one
            else
                GD.PushWarning("UNNAMED clouds: " + cloudsWhy);
        }
        // The weather (B5) last, over the clouds too: the run's state, which the world's own weather then moves on from (Main).
        WeatherStates.Apply(sky, VisualOptions.Weather ?? "fair");
        GD.Print($"UNNAMED sky: Sky3D {sky.Get("version")} at {Hour:0.00} h, its clock DRIVEN FROM THE WORLD CLOCK " +
                 $"(is_day {sky.Call("is_day")}, sun altitude "
                 + $"{altitude:0.0}, daylight {daylight:0.00})");
    }

    /// <summary>The HDRI panorama as the environment's sky; false (and why) when the file is missing.</summary>
    public static bool ApplyHdri(Godot.Environment environment, string? assetRoot, out string? why)
    {
        why = null;
        // A region may name its own panorama in <asset-root>/sky.json ({"hdri": "<path under the root>"}).
        // Without one the original path stands, so the existing region is untouched - and the same rule as the
        // terrain layers applies: the file is content, but a file that cannot be read is refused with a reason
        // rather than falling back silently onto a sky nobody chose.
        string relative = Path.Combine("materials", "sky_ph_quarry_cloudy", "quarry_cloudy_4k.hdr");
        if (assetRoot is not null && File.Exists(Path.Combine(assetRoot, "sky.json")))
        {
            try
            {
                relative = JsonNode.Parse(File.ReadAllText(Path.Combine(assetRoot, "sky.json")))!["hdri"]!.GetValue<string>();
            }
            catch (Exception e)
            {
                why = $"{Path.Combine(assetRoot, "sky.json")}: {e.Message}";
                return false;
            }
        }
        string? path = assetRoot is null ? null : Path.Combine(assetRoot, relative);
        if (path is null || !File.Exists(path))
        {
            why = $"the sky panorama {relative} is not in the asset workspace";
            return false;
        }
        var cache = new TextureCache(assetRoot, ArtLibrary.BuildTextureCache);
        var panorama = cache.For(TextureCache.Key(relative), path, () => Image.LoadFromFile(path), "hdr");
        if (panorama is null)
        {
            using var image = Image.LoadFromFile(path);
            panorama = ImageTexture.CreateFromImage(image);
            TextureCache.RequireCompressed(panorama, path);
        }
        // The radiance cubemap the sky lights the scene with. At 256 it is a sixteenth of the detail the panorama
        // actually carries in each axis, and it is what every reflective surface and the whole ambient term are
        // sampled from. 1024 is about 32 MiB of cubemap and is the ceiling worth paying for.
        var radiance = VisualOptions.Tier is "high" or "ultra" ? Sky.RadianceSizeEnum.Size1024 : Sky.RadianceSizeEnum.Size256;
        environment.Sky = new Sky { SkyMaterial = new PanoramaSkyMaterial { Panorama = panorama },
            RadianceSize = radiance, ProcessMode = Sky.ProcessModeEnum.Automatic };
        return true;
    }

    /// <summary>
    /// The region's own air, from an optional <c>&lt;asset-root&gt;/atmosphere.json</c>: how thick the fog stands,
    /// what colour it scatters, how far the volumetric pass reaches, and how much light the sky hands down. A
    /// region states these because they are the difference between a close, hedged valley and an open moor, and
    /// one set of numbers cannot serve both - a density tuned for a sheltered hollow turns an open basin into milk.
    /// A region with no file keeps the built-in numbers exactly as they were.
    /// </summary>
    public static string? ApplyAtmosphere(Godot.Environment environment, string? assetRoot)
    {
        if (assetRoot is null)
            return null;
        string path = Path.Combine(assetRoot, "atmosphere.json");
        if (!File.Exists(path))
            return null;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path))!;
            float? Number(string key) => root[key] is { } n ? n.GetValue<float>() : null;
            Color? Tint(string key) => root[key] is { } c ? Color.FromHtml(c.GetValue<string>()) : null;
            var applied = new List<string>();

            if (Number("fog_density") is { } fog) { environment.FogDensity = fog; applied.Add($"fog {fog}"); }
            if (Number("fog_aerial_perspective") is { } aerial) { environment.FogAerialPerspective = aerial; applied.Add($"aerial {aerial}"); }
            if (Tint("fog_light_colour") is { } fogColour) environment.FogLightColor = fogColour;
            if (Number("fog_sun_scatter") is { } scatter) environment.FogSunScatter = scatter;
            if (Number("volumetric_fog_density") is { } volumetric) { environment.VolumetricFogDensity = volumetric; applied.Add($"volumetric {volumetric}"); }
            if (Tint("volumetric_fog_albedo") is { } albedo) environment.VolumetricFogAlbedo = albedo;
            if (Number("volumetric_fog_anisotropy") is { } anisotropy) environment.VolumetricFogAnisotropy = anisotropy;
            if (Number("volumetric_fog_length") is { } length) { environment.VolumetricFogLength = length; applied.Add($"length {length} m"); }
            if (Number("volumetric_fog_gi_inject") is { } inject) environment.VolumetricFogGIInject = inject;
            if (Number("volumetric_fog_ambient_inject") is { } ambientInject) environment.VolumetricFogAmbientInject = ambientInject;
            if (Number("ambient_light_energy") is { } energy) environment.AmbientLightEnergy = energy;
            if (Number("ambient_light_sky_contribution") is { } sky) environment.AmbientLightSkyContribution = sky;
            // SDFGI's own energy is the lever that actually matters outdoors. Measured: cutting ambient_light_energy
            // from 0.32 to 0.11 changed 85% of pixels but moved mean luma by only +4 and contrast by +6%, because
            // with sdfgi_read_sky_light on it is SDFGI, not the ambient term, that fills the shadows from the sky.
            if (Number("sdfgi_energy") is { } sdfgi) environment.SdfgiEnergy = sdfgi;
            if (Number("tonemap_exposure") is { } exposure) environment.TonemapExposure = exposure;
            if (Number("tonemap_contrast") is { } contrast) environment.TonemapAgxContrast = contrast;
            if (Number("glow_intensity") is { } glow) environment.GlowIntensity = glow;
            return $"atmosphere from {path}: {string.Join(", ", applied)}";
        }
        catch (Exception e)
        {
            GD.PushWarning($"UNNAMED atmosphere: {path} could not be read ({e.Message}); the built-in air stands");
            return null;
        }
    }
}
