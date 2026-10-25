// UNNAMED Presentation - Otherreach's own sky over Sky3D: the bodies, the world's ring, a neighbouring galaxy and the night glows
// Godot presentation only (D-11): drawn in Sky3D's sky pass (Environment/otherreach_sky.gdshaderinc), never into the radiance the scene
// is lit by, so no light, shadow or GI changes; positions follow the sky clock and carry no names or lore.

using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// <c>--visual celestial=mix|giant|arc|binary|none</c>; <c>mix</c>, the default, is the owner's pick (2026-10-16): <c>arc</c> by day with
/// <c>binary</c>'s galaxy and aurora at night, no companion sun. The three concepts the owner chose between (2026-10-15, "we want to make this look
/// like we are being born into a new world/universe"), each with three moons as the canon's carved stone has them (PROJECT_CHARTER.md:
/// "a stone carved with three moons") - Sky3D's own moon, which keeps its orbit and phases, and two more. Every body is lit from the
/// sun's real direction, so its phase follows the hour; each sits behind the hills and the clouds and is dimmed and reddened by the air
/// toward the horizon. Surfaces are Cassini maps and Hubble imagery recombined (Environment/sky/SOURCES.md).
/// <list type="bullet">
/// <item><c>giant</c>: the ringed gas giant of 2026-10-05, low in the east-south-east where the spawn looks, held still in the sky as a
/// tidally locked world's primary is, waxing and waning through the day; a pale moon high in the south-west, a small bright one in the
/// north-east, a hard white companion star low in the west-north-west, airglow at night and a night sky that is not Earth's.</item>
/// <item><c>arc</c>: the world's own rings, an arc from the eastern to the western horizon through the southern sky, its fine structure
/// lit by day and cut by the world's shadow at night; a dark-site Milky Way with its core riding high.</item>
/// <item><c>binary</c>: an orange companion sun that rises hours after the sun and stays up into the night, a great face-on spiral
/// galaxy across the northern sky, and aurora curtains low in the north.</item>
/// </list>
/// </summary>
public static class CelestialView
{
    private const string Dir = "res://Environment/sky/";
    private const string Sky3DMoon = "res://addons/sky_3d/assets/thirdparty/textures/moon/MoonMap.png";

    private static ShaderMaterial? _material;
    private static GodotObject? _dome;
    private static DirectionalLight3D? _sun;
    private static string _applied = "";

    /// <summary>The run's sky, once Sky3D has built it: the material its sky is drawn with, its dome and its sun.</summary>
    public static void Attach(WorldEnvironment sky)
    {
        _material = sky.Environment?.Sky?.SkyMaterial as ShaderMaterial;
        _dome = sky.Get("sky").AsGodotObject();
        _sun = sky.Get("sun").AsGodotObject() as DirectionalLight3D;
        _applied = "";
    }

    /// <summary>A direction from a compass bearing and an elevation in degrees (+X east, +Z north, the region's convention).</summary>
    private static Vector3 Toward(float bearing, float elevation)
    {
        float b = Mathf.DegToRad(bearing), e = Mathf.DegToRad(elevation);
        return new Vector3(Mathf.Sin(b) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(b) * Mathf.Cos(e));
    }

    private static float SineRadius(float diameterDegrees) => Mathf.Sin(Mathf.DegToRad(diameterDegrees / 2f));

    /// <summary>The world's rotation axis in the region's frame: Sky3D's sun culminates toward +Z, so the pole stands 41 degrees up toward -Z.</summary>
    private static readonly Vector3 Pole = Toward(180f, 41f);

    private static void Set(string name, Variant value) => _material!.SetShaderParameter(name, value);

    private static Texture2D? Load(string file) => ResourceLoader.Exists(Dir + file) ? GD.Load<Texture2D>(Dir + file) : null;

    /// <summary>
    /// How many world days each extra moon takes round its orbit (WLD-34): presentation tuning, not lore. Each day it stands further along,
    /// so it shows another phase of the sun's light and another place in the sky, instead of repeating itself every day.
    /// </summary>
    public const double PaleMoonDays = 9.0, WispMoonDays = 23.0;

    /// <summary>How far along its orbit a moon is after <paramref name="days"/>, in degrees of bearing.</summary>
    public static float Orbit(double days, double period) => (float)(days / period % 1.0 * 360.0);

    /// <summary>
    /// Once a frame from the sky clock: the look's fixed set-up when the option changes, then what moves with the hour and, for the two
    /// extra moons, with the day (<paramref name="days"/> since the world's first tick).
    /// </summary>
    public static void Advance(float hour, float daylight, Godot.Environment environment, double days = 0)
    {
        float pale = Orbit(days, PaleMoonDays), wisp = Orbit(days, WispMoonDays);
        if (_material is null || !GodotObject.IsInstanceValid(_material))
            return;
        string look = VisualOptions.Celestial;
        if (look != _applied)
            Configure(look);
        var toSun = _sun is not null && GodotObject.IsInstanceValid(_sun) ? _sun.GlobalBasis.Z.Normalized() : Vector3.Up;
        Set("or_sun", toSun);
        Set("or_daylight", daylight);
        // The sky's fog lays (fog_sky_affect) of the fog colour over the whole sky; the bodies are added before it, so they are raised
        // by its inverse to reach the screen at the brightness the air leaves them.
        Set("or_fog_comp", environment.FogEnabled ? 1f / Mathf.Max(1f - environment.FogSkyAffect, 0.2f) : 1f);
        switch (look)
        {
            case "giant":
                // The giant stays put (the world turns once per orbit, facing it); the moons cross slowly with the clock.
                Set("or_giant_spin", hour * Mathf.Tau / 10.4f);
                Set("or_moon1_dir", Toward(236f + pale + (hour - 12f) * 0.9f, 38f));
                Set("or_moon2_dir", Toward(52f + wisp + (hour - 12f) * 1.6f, 31f));
                break;
            case "arc":
            case "mix":
                Set("or_moon1_dir", Toward(70f + pale + (hour - 12f) * 0.9f, 24f));
                Set("or_moon2_dir", Toward(205f + wisp + (hour - 12f) * 1.4f, 47f));
                if (look == "mix")
                    Galaxy(toSun, 70f);
                break;
            case "binary":
                // A distant companion of the sun: it rides the sun's own daily circle about the pole, seven hours behind it.
                Set("or_star_dir", new Quaternion(Pole, Mathf.DegToRad(-105f)) * toSun);
                Galaxy(toSun, 25f);
                Set("or_moon1_dir", Toward(130f + pale + (hour - 12f) * 0.9f, 33f));
                Set("or_moon2_dir", Toward(300f + wisp + (hour - 12f) * 1.3f, 22f));
                break;
        }
    }

    /// <summary>
    /// The galaxy turns with the stars: opposite the sun on its daily circle (so it is up all night and never by day), moved
    /// <paramref name="towardPole"/> degrees toward the pole (70 with the ring-arc: it circles high in the pole-side sky, clear of the ring),
    /// 36 degrees across and tilted 35 degrees from face-on.
    /// </summary>
    private static void Galaxy(Vector3 toSun, float towardPole)
    {
        var at = new Quaternion(Pole, Mathf.Pi) * toSun;
        at = new Quaternion(at.Cross(Pole).Normalized(), Mathf.DegToRad(towardPole)) * at;
        var e1 = Pole.Cross(at).Normalized();
        var e2 = at.Cross(e1).Normalized();
        float half = Mathf.Tan(Mathf.DegToRad(18f));
        float pa = Mathf.DegToRad(28f);
        var u = e1 * Mathf.Cos(pa) + e2 * Mathf.Sin(pa);
        var v = -e1 * Mathf.Sin(pa) + e2 * Mathf.Cos(pa);
        Set("or_galaxy_dir", at);
        Set("or_galaxy_u", u / half);
        Set("or_galaxy_v", v / (half * Mathf.Cos(Mathf.DegToRad(35f))));
    }

    private static void Configure(string look)
    {
        _applied = look;
        foreach (var flag in new[] { "or_giant_on", "or_moon1_on", "or_moon2_on", "or_star_on", "or_arc_on", "or_galaxy_on" })
            Set(flag, false);
        Set("or_space_boost", 0f);
        Set("or_airglow", 0f);
        Set("or_aurora", 0f);
        Set("or_star_frame", Basis.Identity);
        _dome?.Set("moon_texture", GD.Load<Texture2D>(Sky3DMoon));
        if (look == "none")
            return;

        var ring = Load("ring_profile.exr");
        var pale = Load("moon_pale.jpg");
        var wisp = Load("moon_wisp.jpg");
        if (Load("moon_first.jpg") is { } first)
            _dome?.Set("moon_texture", first);
        Set("or_ring_profile", ring!);
        Set("or_moon1_map", pale!);
        Set("or_moon2_map", wisp!);
        Set("or_moon1_on", true);
        Set("or_moon2_on", true);
        // Not Earth's night: the star map mirrored and turned, so no constellation reads as one we know.
        var mirrored = new Basis(new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1));
        switch (look)
        {
            case "giant":
            {
                var at = Toward(104f, 17f);
                Set("or_giant_on", true);
                Set("or_giant_dir", at);
                Set("or_giant_r", SineRadius(24f));
                Set("or_giant_map", Load("giant_bands.jpg")!);
                Set("or_giant_tint", new Color(1f, 1f, 1f));
                // The ring opened 20 degrees to us and rolled 22 degrees off the horizon.
                var right = Vector3.Up.Cross(at).Normalized();
                var up = at.Cross(right).Normalized();
                var roll = (up * Mathf.Cos(0.38f) + right * Mathf.Sin(0.38f)).Normalized();
                var axis = (roll * Mathf.Cos(Mathf.DegToRad(20f)) - at * Mathf.Sin(Mathf.DegToRad(20f))).Normalized();
                Set("or_giant_axis", axis);
                Set("or_giant_ref", -at);
                Set("or_ring_span", new Vector2(1.24f, 2.30f));
                Set("or_moon1_r", SineRadius(4.2f));
                Set("or_moon1_tint", new Color(0.86f, 0.9f, 0.98f));
                Set("or_moon2_r", SineRadius(1.7f));
                Set("or_moon2_tint", new Color(0.98f, 0.93f, 0.85f));
                Set("or_star_on", true);
                Set("or_star_dir", Toward(292f, 9f));
                Set("or_star_color", new Color(0.82f, 0.9f, 1f));
                Set("or_star_size", 0.0016f);
                Set("or_star_power", 6f);
                Set("or_airglow", 0.035f);
                Set("or_star_frame", mirrored * new Basis(Vector3.Right, Mathf.DegToRad(35f)));
                break;
            }
            case "arc":
            case "mix":
                Set("or_arc_on", true);
                // Inclined 15 degrees to the equator (toward the sun's side), so the sun lights the ring's far face at this season and it glows
                // backlit, and the arc stands from about 16 to 53 degrees up instead of lying along the horizon.
                Set("or_arc_axis", Toward(180f, 26f));
                Set("or_arc_world", 1f);
                Set("or_arc_span", new Vector2(1.3f, 3.2f));
                Set("or_moon1_r", SineRadius(3.2f));
                Set("or_moon1_tint", new Color(0.9f, 0.92f, 0.98f));
                Set("or_moon2_r", SineRadius(1.5f));
                Set("or_moon2_tint", new Color(1f, 0.9f, 0.8f));
                Set("or_space_boost", 0.6f);
                Set("or_airglow", 0.025f);
                Set("or_star_frame", mirrored * new Basis(Vector3.Forward, Mathf.DegToRad(-60f)));
                if (look == "mix")
                {
                    // The owner's pick (2026-10-16): the arc by day, the galaxy and the aurora by night. No companion sun: it rides the
                    // sun's daily path, which is the side of the sky the arc spans, so it would cross the ring all day and evening.
                    Set("or_galaxy_on", true);
                    Set("or_galaxy_map", Load("galaxy.jpg")!);
                    Set("or_galaxy_power", 1.6f);
                    Set("or_aurora", 0.3f);
                    Set("or_aurora_dir", Toward(180f, 0f));
                }
                break;
            case "binary":
            {
                Set("or_star_on", true);
                Set("or_star_color", new Color(1f, 0.58f, 0.3f));
                Set("or_star_size", 0.0035f);
                Set("or_star_power", 9f);
                Set("or_moon1_r", SineRadius(2.6f));
                Set("or_moon1_tint", new Color(0.88f, 0.9f, 0.96f));
                Set("or_moon2_r", SineRadius(1.3f));
                Set("or_moon2_tint", new Color(0.95f, 0.9f, 0.86f));
                Set("or_galaxy_on", true);
                Set("or_galaxy_map", Load("galaxy.jpg")!);
                Set("or_galaxy_power", 1.6f);
                Set("or_aurora", 0.3f);
                Set("or_aurora_dir", Toward(180f, 0f));
                Set("or_airglow", 0.02f);
                Set("or_star_frame", mirrored * new Basis(Vector3.Up, Mathf.DegToRad(140f)));
                break;
            }
        }
        var sun = _sun is not null ? _sun.GlobalBasis.Z : Vector3.Up;
        GD.Print($"UNNAMED celestial: '{look}' over Sky3D (sun bearing {Mathf.RadToDeg(Mathf.Atan2(sun.X, sun.Z)):0} deg, elevation {Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(sun.Y, -1f, 1f))):0} deg)");
    }
}
