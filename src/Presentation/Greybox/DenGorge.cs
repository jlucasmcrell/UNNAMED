// UNNAMED Presentation - the gorge behind the wolf den (owner finding 28, spec 28): its landform, water, mist and rock
// Godot presentation only (D-11): scenery beyond the walkable edge, placed by a seeded rule on the landform's own height function

using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// The north ravine is a 25 m ditch with a far side 30 m away along most of its length; behind the wolf den it is a gorge. The lip drops
/// nine metres to a ledge, then to a river 31 m down; the far wall stands 45-55 m off and rises 30 m to a wooded shelf, and the land
/// climbs behind it to ridges 100 m and more over the hollow 450 m out. A waterfall comes off the far wall opposite the den, the river
/// runs east as whitewater and is swallowed at the gorge's east end, where it goes under the ground. Everything is measured from the
/// region's north edge: x along it, d out from it.
/// </summary>
public static partial class DenGorge
{
    /// <summary>The north edge the gorge hangs off (the region's end in z).</summary>
    public const float Edge = 800f;

    /// <summary>Where Halda's rope is anchored on the lip, east of the den's east rock and clear of the cave mouth (x 534-544).</summary>
    public const float RopeX = 547.5f;

    /// <summary>The waterfall, opposite the den, and the swallow at the east end where the river goes under.</summary>
    public const float FallX = 497f, SwallowX = 640f;

    private const float Floor = -24f, RiverOut = 27f, RiverHalf = 9.5f, FarFoot = 44f, ShelfDrop = 9.2f;

    /// <summary>Whether a region has the gorge: the frontier, whose north edge runs past the den. Its own towns and interiors do not.</summary>
    public static bool On(Rect2 region) => region.End.Y == Edge && region.Position.X < 425f && region.End.X > 700f;

    /// <summary>How much of the gorge's profile stands at <paramref name="x"/> on the north side: 1 from 465 to 655, easing out to the plain ravine.</summary>
    public static float Weight(float x) => GroundField.SmoothStep(425f, 465f, x) * (1f - GroundField.SmoothStep(655f, 700f, x));

    private static float Ledge(float x) => GroundField.SmoothStep(522f, 532f, x) * (1f - GroundField.SmoothStep(556f, 566f, x));

    private static float Notch(float x) => GroundField.SmoothStep(483f, 491f, x) * (1f - GroundField.SmoothStep(503f, 511f, x));

    /// <summary>How far out the ledge reaches at x: a metre of shelf along the gorge, six at the rope.</summary>
    public static float ShelfEnd(float x) => 3.6f + 5f * Ledge(x);

    /// <summary>The ledge's height under the lip's own height <paramref name="edge"/>.</summary>
    public static float Shelf(float edge) => edge - ShelfDrop;

    /// <summary>The far wall's foot: pushed back round the waterfall's plunge pool.</summary>
    public static float FarFootAt(float x) => FarFoot + 8f * Notch(x);

    /// <summary>The cave mouth at the back of the ledge, where the terrain is cut away for the passage's own rock.</summary>
    public static bool InMouth(float x, float d) => x > 534.5f && x < 543.5f && d > 0.55f && d < 1.45f;

    /// <summary>The gorge's height at <paramref name="d"/> metres out, x along; <paramref name="edge"/> is the walkable height at the lip.</summary>
    public static float Height(float edge, float x, float d, float n, float n2)
    {
        float shelf = Shelf(edge), shelfEnd = ShelfEnd(x), farFoot = FarFootAt(x), notch = Notch(x);
        float floor = Floor - (x - 465f) * 0.012f + n2 * 0.8f;
        float across = (d - RiverOut) / RiverHalf;
        // The channel cut into the floor, with banks that stand clear of the water so the river's sides meet stone, not air.
        float bed = floor - 2.8f * MathF.Max(0f, 1f - across * across) + 0.9f * GroundField.SmoothStep(0.85f, 1.25f, MathF.Abs(across));
        // The plunge pool under the fall and the swallow's throat, where the river leaves.
        bed -= 3f * notch * GroundField.SmoothStep(farFoot - 12f, farFoot - 2f, d);
        float swallow = MathF.Sqrt((x - SwallowX) * (x - SwallowX) + (d - RiverOut) * (d - RiverOut));
        bed -= 11f * (1f - GroundField.SmoothStep(4f, 13f, swallow));
        if (d < 0.5f)
            return edge - d * 0.35f;
        // The faces are near-sheer so the rock standing on them (DenGorgeDressing) is in front of the ground, not buried in it.
        if (d < 1.3f)
            return Mathf.Lerp(edge - 0.175f, shelf, GroundField.SmoothStep(0.5f, 1.3f, d));
        if (d < shelfEnd)
            return shelf - MathF.Max(0f, d - 2.6f) * 0.08f + n2 * 0.12f;
        float shelfLow = shelf - (shelfEnd - 2.6f) * 0.08f;
        if (d < shelfEnd + 2.5f)
            return Mathf.Lerp(shelfLow, bed, GroundField.SmoothStep(shelfEnd, shelfEnd + 2.5f, d));
        if (d < farFoot)
            return bed;
        float top = 30f + n * 4f;
        // The far wall in three sheer steps with ledges between them, so even when the sun rakes along the gorge its ledges are lit and the
        // wall reads in layers; round the fall it is one sheer face (the notch) for the water to come down.
        float terraced = 1f - notch, rise = Mathf.Lerp(1.5f, 22f, terraced);
        // The stream that feeds the fall runs in a shallow cut over the far shelf.
        float channel = 1.4f * (1f - GroundField.SmoothStep(2.5f, 6f, MathF.Abs(x - FallX))) * GroundField.SmoothStep(farFoot + rise - 1f, farFoot + rise + 2f, d);
        if (d < farFoot + rise)
        {
            float t = (d - farFoot) / rise, step = top - bed;
            // Three faces over 0-0.2, about 0.4-0.6 and 0.8-1 of the run, ledges between; the middle face wanders along the gorge and in
            // places closes up with the lower one, so no ledge runs dead straight.
            float wander = 0.09f * MathF.Sin(x * 0.11f) + 0.06f * MathF.Sin(x * 0.29f + 1.3f);
            float a1 = 0.2f + 0.08f * MathF.Max(0f, MathF.Sin(x * 0.07f + 2f)), a2 = 0.4f + wander;
            float h1 = 0.33f + 0.06f * MathF.Sin(x * 0.17f), h2 = 0.67f + 0.05f * MathF.Sin(x * 0.13f + 0.7f);
            float terraces = h1 * GroundField.SmoothStep(0f, a1, t) + (h2 - h1) * GroundField.SmoothStep(MathF.Max(a1, a2), MathF.Max(a1, a2) + 0.2f, t)
                + (1f - h2) * GroundField.SmoothStep(0.8f, 1f, t);
            return bed + step * Mathf.Lerp(GroundField.SmoothStep(0f, 1f, t), terraces, terraced) + (t is > 0.2f and < 0.4f or > 0.6f and < 0.8f ? n2 * 0.3f : 0f);
        }
        if (d < 110f)
            return Mathf.Lerp(top, 44f + n * 6f, GroundField.SmoothStep(farFoot + rise, 110f, d)) - channel;
        return 44f + n * 6f + GroundField.SmoothStep(110f, 470f, d) * (70f + n * 35f) - channel;
    }

    /// <summary>
    /// The river's two runs, each sampled every two metres along x with its centre, surface and how hard it runs there (0 still, 1 a
    /// rapid): from the west end down past the fall to the swallow, and from the east end back west into it.
    /// </summary>
    public static IEnumerable<(string Name, Vector2 Flow, (float X, float Level, float Rapid)[] Samples)> Runs(Func<float, float, float> height)
    {
        yield return ("gorge_river_west", new Vector2(1, 0), Run(height, 436f, SwallowX - 3f, 2f));
        yield return ("gorge_river_east", new Vector2(-1, 0), Run(height, 704f, SwallowX + 3f, -2f));
    }

    private static (float X, float Level, float Rapid)[] Run(Func<float, float, float> height, float from, float to, float step)
    {
        var samples = new List<(float X, float Level, float Rapid)>();
        float level = float.MaxValue;
        for (float x = from; step > 0 ? x <= to : x >= to; x += step)
        {
            // The surface over the bed's deepest line, never rising downstream: where the bed climbs the water pools, where it falls it runs.
            float bed = MathF.Min(height(x, Edge + RiverOut), MathF.Min(height(x, Edge + RiverOut - 3f), height(x, Edge + RiverOut + 3f)));
            float next = MathF.Min(level, bed + 1.2f);
            float drop = level == float.MaxValue ? 0f : level - next;
            samples.Add((x, next, Math.Clamp(drop / MathF.Abs(step) * 6f, 0f, 1f)));
            level = next;
        }
        return samples.ToArray();
    }

    /// <summary>A strip of water over a run: its surface follows the samples, white where it runs hard.</summary>
    public static MeshInstance3D RiverMesh(string name, Vector2 flow, (float X, float Level, float Rapid)[] samples, Material material)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int Across = 8;
        float width = RiverHalf * 2f * 0.95f;
        for (int i = 0; i < samples.Length; i++)
        {
            var (x, level, rapid) = samples[i];
            // A rapid's foam carries on a little downstream of where the water fell.
            float carried = Math.Max(rapid, i > 0 ? samples[i - 1].Rapid * 0.7f : 0f);
            for (int j = 0; j <= Across; j++)
            {
                float t = j / (float)Across;
                st.SetColor(new Color(carried, Math.Clamp(MathF.Min(t, 1f - t) * 5f, 0f, 1f), 0));
                st.SetUV(new Vector2(t, x));
                st.SetNormal(Vector3.Up);
                st.AddVertex(new Vector3(x, level, Edge + RiverOut + (t - 0.5f) * width));
            }
        }
        for (int i = 0; i + 1 < samples.Length; i++)
        {
            for (int j = 0; j < Across; j++)
            {
                int a = i * (Across + 1) + j, b = a + Across + 1;
                // Wound so the surface faces up whichever way the run was sampled.
                if (flow.X > 0) { st.AddIndex(a); st.AddIndex(b); st.AddIndex(a + 1); st.AddIndex(a + 1); st.AddIndex(b); st.AddIndex(b + 1); }
                else { st.AddIndex(a); st.AddIndex(a + 1); st.AddIndex(b); st.AddIndex(a + 1); st.AddIndex(b + 1); st.AddIndex(b); }
            }
        }
        return new MeshInstance3D { Name = name, Mesh = st.Commit(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    /// <summary>
    /// The fall: three curtains off the far shelf's lip into the plunge pool - a wide thin veil behind, the main sheet, and a narrow heavy
    /// core in front - each swaying a little as it drops and spreading towards the foot. <paramref name="top"/> is the stream's surface at
    /// the lip and <paramref name="pool"/> the river's under it. The curtains share one material; each has its own seed and weight.
    /// </summary>
    public static Node3D FallMesh(float top, float pool, float faceD)
    {
        var fall = new Node3D { Name = "gorge_fall" };
        FallWater.SetShaderParameter("drop_m", top - pool);
        foreach (var (name, width, forward, seed, body) in new[] { ("veil", 11f, 0f, 0f, 0.45f), ("sheet", 8.5f, 0.35f, 1f, 0.8f), ("core", 4.2f, 0.7f, 2f, 0.9f) })
        {
            var curtain = new MeshInstance3D
            {
                Name = name, Mesh = Curtain(top, pool, faceD - forward, width, seed), MaterialOverride = FallWater,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            curtain.SetInstanceShaderParameter("seed", seed);
            curtain.SetInstanceShaderParameter("body", body);
            curtain.SetInstanceShaderParameter("width_m", width);
            fall.AddChild(curtain);
        }
        return fall;
    }

    private static ArrayMesh Curtain(float top, float pool, float faceD, float width, float seed)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int Across = 12, Down = 60;
        float drop = top - pool;
        for (int i = 0; i <= Down; i++)
        {
            float v = i / (float)Down;
            float y = top - drop * v;
            // The water leaves the lip moving outward and falls clear of the face: a little parabola, about 5 m out at the foot (the notch
            // face is sheer only to the terrain's metre: hung closer, the curtain cuts into it). It sways as it falls.
            float out_ = 2.2f + 2.6f * MathF.Sqrt(v);
            float sway = 0.7f * v * MathF.Sin(v * 3.3f + seed * 1.7f);
            for (int j = 0; j <= Across; j++)
            {
                float u = j / (float)Across;
                float spread = 1f + 0.3f * v;
                st.SetUV(new Vector2(u, v));
                st.SetNormal(new Vector3(0, 0, -1));
                st.AddVertex(new Vector3(FallX + sway + (u - 0.5f) * width * spread, y, Edge + faceD - out_ - 0.25f * MathF.Sin(u * 9f + seed)));
            }
        }
        for (int i = 0; i < Down; i++)
            for (int j = 0; j < Across; j++)
            {
                int a = i * (Across + 1) + j, b = a + Across + 1;
                st.AddIndex(a); st.AddIndex(a + 1); st.AddIndex(b); st.AddIndex(a + 1); st.AddIndex(b + 1); st.AddIndex(b);
            }
        return st.Commit();
    }

    /// <summary>
    /// Falling water: thin streaks and clumps rushing down at the speed of a long drop, edges eaten away irregularly so the silhouette is
    /// never a ribbon, gaps opening towards the foot, and foam where it pours over the lip and where it hits the pool.
    /// </summary>
    public static ShaderMaterial FallWater { get; } = new()
    {
        Shader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_never, diffuse_burley, specular_schlick_ggx, shadows_disabled;

uniform float speed = 1.0;
uniform float drop_m = 54.0;
uniform float sky_light = 0.0;   // the skylight its spray scatters, set with the daylight (DenGorgeAir)
instance uniform float seed = 0.0;
instance uniform float body = 0.8;
instance uniform float width_m = 9.0;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
float fbm(vec2 p) {
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++) { v += a * vnoise(p); p = p * 2.03 + vec2(17.0, 9.0); a *= 0.5; }
    return v;
}

void fragment() {
    vec2 m = vec2(UV.x * width_m, UV.y * drop_m);
    float fall = TIME * speed;
    // Streaks: thin and long, rushing down; clumps: heavier water falling faster still.
    float s1 = fbm(vec2(m.x * 2.4 + seed * 13.0, m.y * 0.10 - fall * 1.5));
    float s2 = fbm(vec2(m.x * 6.0 + seed * 7.0, m.y * 0.22 - fall * 2.8));
    float clump = smoothstep(0.58, 0.86, fbm(vec2(m.x * 0.9 + seed * 3.0, m.y * 0.28 - fall * 3.2)));
    float streak = s1 * 0.55 + s2 * 0.45;
    // The silhouette: each side eaten by its own noise running down with the water, so the edge is ragged and moving.
    float left = 0.06 + 0.16 * fbm(vec2(UV.y * drop_m * 0.12 - fall * 1.4, seed * 5.0));
    float right = 0.94 - 0.16 * fbm(vec2(UV.y * drop_m * 0.12 - fall * 1.4, seed * 5.0 + 31.0));
    float side = smoothstep(left - 0.05, left + 0.05, UV.x) * (1.0 - smoothstep(right - 0.05, right + 0.05, UV.x));
    // Gaps open as the sheet breaks up towards the foot.
    float whole = smoothstep(0.18, 0.5, streak + 0.42 - 0.38 * UV.y);
    float lip = 1.0 - smoothstep(0.0, 0.05, UV.y);
    float foot = smoothstep(0.82, 1.0, UV.y);
    float white = clamp(smoothstep(0.45, 0.72, streak) * 0.65 + clump * 0.7 + lip * 0.8 + foot * 0.85, 0.0, 1.0);
    ALBEDO = mix(vec3(0.28, 0.36, 0.36), vec3(0.94, 0.96, 0.96), white);
    ALPHA = clamp(side * max(body * whole, white * 0.9) * (1.0 - 0.8 * smoothstep(0.93, 1.0, UV.y)), 0.0, 0.97);
    EMISSION = ALBEDO * sky_light * (0.6 + 0.4 * white);
    // Wet and glossy: it gives back the sky, so it stays the brightest thing in the gorge when the light goes.
    ROUGHNESS = mix(0.08, 0.4, white);
    SPECULAR = 0.7;
    NORMAL = normalize(NORMAL + vec3((s2 - 0.5) * 0.9, (s1 - 0.5) * 0.3, 0.0));
}
",
        },
    };

    /// <summary>Where the mist hangs (river mist laid along x, spray at the plunge and the swallow), as recipe and place.</summary>
    public static IEnumerable<(string Recipe, Vector3 At, float Yaw)> Mist(Func<float, float, float> height)
    {
        for (float x = 455f; x <= 690f; x += 30f)
            yield return ("river_mist", new Vector3(x, height(x, Edge + RiverOut) + 3.2f, Edge + RiverOut), 90f);
        float pool = height(FallX, Edge + FarFootAt(FallX) - 6f);
        foreach (float dx in new[] { -5f, -2.5f, 0f, 2.5f, 5f })
            yield return ("waterfall_spray", new Vector3(FallX + dx, pool + 1.4f, Edge + FarFootAt(FallX) - 5.5f), 0f);
        foreach (float dx in new[] { -3f, 0f, 3f })
            yield return ("water_splash", new Vector3(FallX + dx, pool + 0.6f, Edge + FarFootAt(FallX) - 5f), 0f);
        float lip = height(FallX, Edge + FarFootAt(FallX) + 4f);
        yield return ("waterfall_spray", new Vector3(FallX, lip - 1.5f, Edge + FarFootAt(FallX) - 1.5f), 0f);
        yield return ("river_mist", new Vector3(FallX, pool + 4f, Edge + FarFootAt(FallX) - 8f), 0f);
        foreach (float dx in new[] { -4f, 4f })
            yield return ("waterfall_spray", new Vector3(SwallowX + dx, height(SwallowX, Edge + RiverOut) + 6f, Edge + RiverOut), 90f);
    }
}
