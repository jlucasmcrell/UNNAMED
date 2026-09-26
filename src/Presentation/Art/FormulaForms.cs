// UNNAMED Presentation - the formulas as shapes in the world: the ward's lattice, the mending's threads, the cast's glyph, the bolt (Phase B VFX lane)
// Godot presentation only (D-11): when a form shows and ends is its caller's (a working on the body, a cast, a shot); nothing here is read by the game

using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// A formula working drawn as geometry, not a flipbook: the content bible's magic is formula working - precise, geometric, costly - so
/// each form is lines, facets and inscribed circles built in code and drawn by its own shader, depth-tested against the world (a shell
/// round a body is hidden behind it and drawn in front of it), with no camera-facing picture to cut off. The particle recipes' effects map
/// (<c>res://Art/vfx_recipes.json</c>) names a form as <c>form:&lt;name&gt;</c> beside the particles that stand in for a flipbook, so which
/// effect draws which form is data. A form animates itself from its age; its owner calls <see cref="End"/> when the working ends and frees
/// it after the seconds that returns.
/// </summary>
public abstract partial class FormulaForm : Node3D
{
    public const string Prefix = "form:";

    /// <summary>The form a recipes entry names (<c>form:ward_lattice</c>, ...), or null for a name that is not a form.</summary>
    public static FormulaForm? Create(string name) => name switch
    {
        "form:ward_lattice" => new WardLattice(),
        "form:mending_weave" => new MendingWeave(),
        "form:cast_glyph" => new CastGlyph(),
        "form:bolt_core" => new BoltCore(),
        "form:bolt_shock" => new BoltShock(),
        _ => null,
    };

    /// <summary>Seconds since the form was made.</summary>
    protected double Age { get; private set; }

    /// <summary>Seconds since <see cref="End"/>; null while the working holds.</summary>
    protected double? Ending { get; private set; }

    /// <summary>Seconds the working has left, when the owner knows it (a body effect's expiry); null otherwise.</summary>
    public double? SecondsLeft { get; set; }

    public override void _Process(double delta)
    {
        Age += delta;
        if (Ending is { } ending)
            Ending = ending + delta;
        Animate();
    }

    /// <summary>This frame's look, from <see cref="Age"/>, <see cref="Ending"/> and <see cref="SecondsLeft"/>.</summary>
    protected abstract void Animate();

    /// <summary>How long the form's ending takes, in seconds.</summary>
    protected abstract double EndSeconds { get; }

    /// <summary>The working has ended: the form plays its ending. Returns the seconds after which it can be freed.</summary>
    public double End()
    {
        Ending ??= 0;
        return EndSeconds;
    }

    /// <summary>A blow landed on the body this form is on, from a point in the world.</summary>
    public virtual void Struck(Vector3 from)
    {
    }

    /// <summary>A shader material from code, its parameters set.</summary>
    protected static ShaderMaterial Material(Shader shader, params (string Name, Variant Value)[] parameters)
    {
        var material = new ShaderMaterial { Shader = shader };
        foreach (var (name, value) in parameters)
            material.SetShaderParameter(name, value);
        return material;
    }

    protected static MeshInstance3D Drawn(string name, Mesh mesh, Material material) => new()
    {
        Name = name, Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        GIMode = GeometryInstance3D.GIModeEnum.Disabled,
    };

    protected static float Smooth(double x)
    {
        float t = Mathf.Clamp((float)x, 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// A flat inscribed circle, the formulas' common mark: a ring at <c>radius</c> (metres), a dashed inner ring, fine ticks every 10
    /// degrees and long ticks every 30, drawn round from its start as <c>drawn</c> goes 0 to 1. On a quad of <c>half_size</c> x 2.
    /// </summary>
    protected static readonly Shader CircleShader = new()
    {
        Code = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
uniform vec3 ink : source_color = vec3(0.6, 0.8, 1.0);
uniform float energy = 1.0;
uniform float radius = 0.78;
uniform float half_size = 0.9;
uniform float drawn = 1.0;
uniform float line_m = 0.007;
void fragment() {
    vec2 p = (UV - 0.5) * 2.0 * half_size;
    float r = length(p);
    float a = atan(p.y, p.x) / 6.2831853 + 0.5;
    float px = max(fwidth(r), 1e-4);
    float w = max(line_m, px * 0.9);
    float ring = 1.0 - smoothstep(w, w + px * 1.5, abs(r - radius));
    float inner = (1.0 - smoothstep(w * 0.7, w * 0.7 + px * 1.5, abs(r - radius * 0.88))) * step(0.45, fract(a * 90.0));
    float fine_d = abs(fract(a * 36.0 + 0.5) - 0.5) / 36.0 * 6.2831853 * r;
    float fine = (1.0 - smoothstep(w * 0.8, w * 0.8 + px * 1.5, fine_d)) * step(radius * 0.9, r) * step(r, radius);
    float long_d = abs(fract(a * 12.0 + 0.5) - 0.5) / 12.0 * 6.2831853 * r;
    float lng = (1.0 - smoothstep(w, w + px * 1.5, long_d)) * step(radius * 0.8, r) * step(r, radius * 1.06);
    float mask = step(a, drawn);
    ALBEDO = ink * (ring + inner * 0.55 + fine * 0.7 + lng) * energy * mask;
}
",
    };
}

/// <summary>
/// The Brace Ward: a faceted lattice shell round the body - an icosahedron split to 80 facets, stretched to the body's height - drawn as
/// fine edges with bright nodes and the faintest facet fill, strongest at its silhouette so the body stays readable inside it; a circle
/// inscribed on the ground at its foot. It assembles from the feet up as the ward takes hold, turns slowly while it holds, a light
/// climbing its edges; a blow on it spreads a ring across the facets from where it landed; in its last 1.5 s it wavers; when the ward
/// ends its facets break apart outward and fade.
/// </summary>
public sealed partial class WardLattice : FormulaForm
{
    private static readonly Color Ink = new(0.58f, 0.78f, 1.0f);
    private static ArrayMesh? _mesh;
    private static Shader? _shader;
    private readonly ShaderMaterial _material;
    private readonly ShaderMaterial _circle;
    private readonly Node3D _shell;
    private readonly Node3D _foot;
    private double _struckAt = -10;

    public WardLattice()
    {
        Name = "WardLattice";
        _material = Material(_shader ??= new Shader { Code = LatticeCode }, ("ink", Ink), ("assemble", 0f), ("shatter", 0f));
        _shell = Drawn("Shell", _mesh ??= Lattice(), _material);
        _shell.Position = new Vector3(0, 0.95f, 0);
        AddChild(_shell);
        _circle = Material(CircleShader, ("ink", Ink), ("radius", 0.78f), ("half_size", 0.9f), ("drawn", 0f));
        _foot = Drawn("Circle", new QuadMesh { Size = new Vector2(1.8f, 1.8f), Orientation = PlaneMesh.OrientationEnum.Y }, _circle);
        _foot.Position = new Vector3(0, 0.04f, 0);
        AddChild(_foot);
    }

    protected override double EndSeconds => 0.6;

    public override void Struck(Vector3 from)
    {
        var local = _shell.ToLocal(from);
        local.Y = Math.Clamp(local.Y, -0.3f, 0.5f);
        if (local.LengthSquared() > 0.0001f)
            _material.SetShaderParameter("hit_dir", local.Normalized());
        _struckAt = Age;
    }

    protected override void Animate()
    {
        float assemble = Smooth(Age / 0.45);
        // A brighter moment as it closes, settling to its holding light.
        float energy = 1f + 0.9f * Mathf.Exp(-(float)Math.Pow((Age - 0.45) / 0.18, 2));
        float waver = SecondsLeft is { } left && left < 1.5 ? (float)(1 - left / 1.5) : 0f;
        float shatter = 0f;
        if (Ending is { } ending)
        {
            shatter = Smooth(ending / EndSeconds);
            energy = 1f + 1.2f * Mathf.Exp(-(float)ending * 9f);
            waver = 0;
        }
        _shell.Rotation = new Vector3(0, (float)Age * 0.22f, 0);
        _foot.Rotation = new Vector3(0, -(float)Age * 0.35f, 0);
        _material.SetShaderParameter("assemble", assemble);
        _material.SetShaderParameter("shatter", shatter);
        _material.SetShaderParameter("energy", energy);
        _material.SetShaderParameter("waver", waver);
        _material.SetShaderParameter("hit_age", (float)(Age - _struckAt));
        _circle.SetShaderParameter("drawn", Smooth(Age / 0.35));
        _circle.SetShaderParameter("energy", 0.5f * energy * (1 - shatter));
        _foot.Scale = Vector3.One * (1 - 0.35f * shatter);
    }

    /// <summary>The lattice: 80 facets, each with its own corners (so each can move alone), its corners' barycentrics in the colour and
    /// its centre and a random number in CUSTOM0; one of the icosahedron's corners straight up, so it turns true.</summary>
    private static ArrayMesh Lattice()
    {
        float t = (1 + MathF.Sqrt(5)) / 2;
        var up = new Basis(Vector3.Back, Mathf.Atan2(1, t));
        var v = new[] { new Vector3(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1) }.Select(p => (up * p).Normalized()).ToArray();
        int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetCustomFormat(0, SurfaceTool.CustomFormat.RgbaFloat);
        var random = new RandomNumberGenerator { Seed = 0x3A7D };
        void Facet(Vector3 a, Vector3 b, Vector3 c)
        {
            var centre = (a + b + c) / 3;
            float r = random.Randf();
            foreach (var (p, bary) in new[] { (a, new Color(1, 0, 0)), (b, new Color(0, 1, 0)), (c, new Color(0, 0, 1)) })
            {
                st.SetColor(bary);
                st.SetCustom(0, new Color(centre.X, centre.Y, centre.Z, r));
                st.SetNormal(p);
                st.AddVertex(p);
            }
        }
        for (int i = 0; i < f.Length; i += 3)
        {
            Vector3 a = v[f[i]], b = v[f[i + 1]], c = v[f[i + 2]];
            Vector3 ab = ((a + b) / 2).Normalized(), bc = ((b + c) / 2).Normalized(), ca = ((c + a) / 2).Normalized();
            Facet(a, ab, ca);
            Facet(b, bc, ab);
            Facet(c, ca, bc);
            Facet(ab, bc, ca);
        }
        return st.Commit();
    }

    private const string LatticeCode = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
uniform vec3 radii = vec3(0.62, 1.02, 0.62);
uniform vec3 ink : source_color = vec3(0.58, 0.78, 1.0);
uniform float assemble = 1.0;
uniform float shatter = 0.0;
uniform float energy = 1.0;
uniform float waver = 0.0;
uniform vec3 hit_dir = vec3(0.0, 0.0, 1.0);
uniform float hit_age = 10.0;
varying vec3 bary;
varying vec3 unit;
varying vec3 outward;
varying float grown;
varying float face_rand;

void vertex() {
    vec3 c = CUSTOM0.xyz;
    float r = CUSTOM0.w;
    bary = COLOR.rgb;
    face_rand = r;
    unit = VERTEX;
    float h = c.y * 0.5 + 0.5;
    // Each facet grows from its centre as the sweep from the feet passes it; breaking, each shrinks and drifts outward.
    float arrive = clamp((assemble * 1.45 - h - r * 0.25) / 0.2, 0.0, 1.0);
    float s = clamp(shatter * 1.5 - r * 0.5, 0.0, 1.0);
    grown = arrive * (1.0 - s);
    vec3 p = mix(c, VERTEX, grown);
    p += normalize(c) * s * 0.5 + vec3(0.0, -0.25 * s * s, 0.0);
    VERTEX = p * radii;
    NORMAL = normalize(p / radii);
    outward = (MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz;
}

void fragment() {
    float e = min(min(bary.x, bary.y), bary.z);
    float aa = max(fwidth(e), 1e-4);
    float w = max(0.011, aa * 0.75);
    float line = 1.0 - smoothstep(w, w + aa * 1.5, e);
    float node = smoothstep(0.955, 0.99, max(max(bary.x, bary.y), bary.z));
    float facing = abs(dot(NORMAL, VIEW));
    float rim = pow(1.0 - facing, 2.5);
    // The far side of the shell, seen through it, is fainter: glass, not a cage.
    float near_side = mix(0.45, 1.0, smoothstep(-0.1, 0.1, outward.z));
    // A light climbing the edges while the ward holds.
    float band = exp(-pow((unit.y - (fract(TIME * 0.3) * 2.8 - 1.4)) * 3.0, 2.0));
    float blow = 0.0;
    if (hit_age < 1.6) {
        float angle = acos(clamp(dot(normalize(unit), normalize(hit_dir)), -1.0, 1.0));
        blow = exp(-pow((angle - hit_age * 4.5) * 4.0, 2.0)) * exp(-hit_age * 2.2) + exp(-angle * 3.0) * exp(-hit_age * 7.0) * 1.6;
    }
    float flicker = 1.0 - waver * step(0.55, fract(TIME * 4.0 + face_rand * 0.7)) * 0.75;
    float glow = line * (0.2 + 1.0 * rim + 0.45 * band) + node * 0.75 + rim * 0.07 + 0.01 + blow * (0.6 + line * 3.0);
    // Seen from inside (first person, or a camera pulled in against a wall) the facets nearest the eye fade out.
    float near = smoothstep(0.25, 0.8, -VERTEX.z);
    ALBEDO = ink * glow * energy * flicker * near_side * near * step(0.02, grown);
}
";
}

/// <summary>
/// The Mending Thread: seven fine threads of warm light drawing in to the body - each spirals in from about a metre out, its bright end
/// running along it and a fading length behind - and stitching: the last of each thread runs round the chest in a zigzag, like thread
/// pulled through, lit as the end passes. The threads are ribbons of geometry turned edge-on to the eye along their own length (never a
/// picture that a frame edge can cut), with a soft warm light on the body while the mending works. It fades when the mending ends.
/// </summary>
public sealed partial class MendingWeave : FormulaForm
{
    private const int Threads = 7;
    private const int Samples = 120;
    private static ArrayMesh? _mesh;
    private static Shader? _shader;
    private readonly ShaderMaterial _material;
    private readonly OmniLight3D _warmth;

    public MendingWeave()
    {
        Name = "MendingWeave";
        _material = Material(_shader ??= new Shader { Code = WeaveCode }, ("age", 0f), ("fade", 0f));
        var weave = Drawn("Threads", _mesh ??= Weave(), _material);
        // The threads never leave a 1.4 m radius round the body, 2.2 m high.
        weave.CustomAabb = new Aabb(new Vector3(-1.5f, -0.2f, -1.5f), new Vector3(3, 2.6f, 3));
        AddChild(weave);
        _warmth = new OmniLight3D
        {
            Name = "Warmth", LightColor = new Color(1f, 0.82f, 0.58f), LightEnergy = 0f, OmniRange = 2.6f, Position = new Vector3(0, 1.2f, 0.3f),
            ShadowEnabled = false,
        };
        AddChild(_warmth);
    }

    protected override double EndSeconds => 0.8;

    protected override void Animate()
    {
        float fade = Smooth(Age / 0.35) * (Ending is { } ending ? 1 - Smooth(ending / EndSeconds) : 1f);
        _material.SetShaderParameter("age", (float)Age);
        _material.SetShaderParameter("fade", fade);
        _warmth.LightEnergy = 0.55f * fade * (0.85f + 0.15f * Mathf.Sin((float)Age * 3.1f));
        Rotation = new Vector3(0, (float)Age * 0.15f, 0);
    }

    /// <summary>
    /// Seven threads as ribbons: each vertex on the thread's centre line, UV (along, side), CUSTOM0 the line's direction there and the
    /// thread's own phase. Along 0-0.72 the thread spirals in from about a metre out to the chest; 0.72-1 it runs round the chest in a
    /// zigzag, the stitch.
    /// </summary>
    private static ArrayMesh Weave()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetCustomFormat(0, SurfaceTool.CustomFormat.RgbaFloat);
        var random = new RandomNumberGenerator { Seed = 0x7E4D };
        for (int k = 0; k < Threads; k++)
        {
            float start = Mathf.Tau * k / Threads + random.RandfRange(-0.3f, 0.3f);
            float from = random.RandfRange(0.95f, 1.25f), fromHeight = random.RandfRange(0.25f, 1.85f);
            float chest = random.RandfRange(0.98f, 1.36f), turns = random.RandfRange(0.7f, 1.0f);
            float phase = (float)k / Threads;
            Vector3 At(float s)
            {
                if (s <= 0.72f)
                {
                    float u = s / 0.72f, eased = 1 - (1 - u) * (1 - u);
                    float angle = start + u * turns * Mathf.Tau;
                    float radius = Mathf.Lerp(from, 0.26f, eased);
                    return new Vector3(Mathf.Cos(angle) * radius, Mathf.Lerp(fromHeight, chest, eased), Mathf.Sin(angle) * radius);
                }
                float w = (s - 0.72f) / 0.28f;
                float round = start + turns * Mathf.Tau + w * 0.55f * Mathf.Tau;
                // The stitch: seven passes up and down across a hand's width as it goes round (the shader shows the rising halves).
                float pass = w * 7 - Mathf.Floor(w * 7);
                float stitch = (pass < 0.5f ? -1 + 4 * pass : 3 - 4 * pass) * 0.04f;
                return new Vector3(Mathf.Cos(round) * 0.26f, chest + stitch, Mathf.Sin(round) * 0.26f);
            }
            var points = Enumerable.Range(0, Samples).Select(i => At((float)i / (Samples - 1))).ToArray();
            for (int i = 0; i < Samples - 1; i++)
            {
                float s0 = (float)i / (Samples - 1), s1 = (float)(i + 1) / (Samples - 1);
                var t0 = (points[Math.Min(i + 1, Samples - 1)] - points[Math.Max(i - 1, 0)]).Normalized();
                var t1 = (points[Math.Min(i + 2, Samples - 1)] - points[i]).Normalized();
                void Corner(Vector3 p, Vector3 tangent, float s, float side)
                {
                    st.SetUV(new Vector2(s, side));
                    st.SetCustom(0, new Color(tangent.X, tangent.Y, tangent.Z, phase));
                    st.AddVertex(p);
                }
                Corner(points[i], t0, s0, -1);
                Corner(points[i + 1], t1, s1, -1);
                Corner(points[i + 1], t1, s1, 1);
                Corner(points[i], t0, s0, -1);
                Corner(points[i + 1], t1, s1, 1);
                Corner(points[i], t0, s0, 1);
            }
        }
        return st.Commit();
    }

    private const string WeaveCode = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled, skip_vertex_transform;
uniform vec3 head_ink : source_color = vec3(1.0, 0.8, 0.46);
uniform vec3 tail_ink : source_color = vec3(0.5, 0.86, 0.6);
uniform float age = 0.0;
uniform float fade = 1.0;
uniform float energy = 1.25;
uniform float width_m = 0.02;
uniform float cycle_s = 1.5;
varying float along;
varying float side;
varying float behind;

void vertex() {
    along = UV.x;
    side = UV.y;
    float phase = CUSTOM0.w;
    // The bright end runs the thread's length in 1.1 s, a pass every cycle, each thread starting a share of a cycle after the last.
    float t = age - phase * cycle_s;
    float head = t < 0.0 ? -1.0 : fract(t / cycle_s) * cycle_s / 1.1;
    behind = head - along;
    vec3 centre = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
    vec3 tangent = normalize((MODELVIEW_MATRIX * vec4(CUSTOM0.xyz, 0.0)).xyz);
    vec3 across = normalize(cross(tangent, normalize(-centre)));
    // A little wider at the bright end, and never under about three pixels wide far off (the core is a third of it).
    float near_head = exp(-pow(behind / 0.05, 2.0));
    float w = max(width_m * (1.0 + 0.8 * near_head), -centre.z * 0.0022);
    VERTEX = centre + across * side * w;
    NORMAL = normalize(-centre);
}

void fragment() {
    float tail = 0.42;
    float lit = behind >= 0.0 ? clamp(1.0 - behind / tail, 0.0, 1.0) : 0.0;
    lit = lit * lit;
    float head_glow = exp(-pow(behind / 0.025, 2.0));
    // The stitch keeps a faint light once sewn: the part of the thread the end has passed in this pass, round the chest.
    float stitching = step(0.72, along);
    float sewn = stitching * step(0.0, behind) * 0.3;
    // Round the chest only the rising half of each pass shows: a row of short slanted stitches, not a zigzag.
    float pass = fract((along - 0.72) / 0.28 * 7.0);
    float dashes = mix(1.0, smoothstep(0.02, 0.08, pass) * (1.0 - smoothstep(0.42, 0.5, pass)), stitching);
    // A fine bright core in a soft warm halo.
    float core = exp(-side * side * 14.0) + 0.22 * exp(-side * side * 2.5);
    float ends = smoothstep(0.0, 0.1, along);
    // A camera pulled in against a wall, or in first person, sits inside the threads' outer turns: those fade out near the eye.
    float near = smoothstep(0.6, 1.3, -VERTEX.z);
    float glow = (lit * 0.85 + head_glow * 1.8 + sewn) * core * ends * dashes * near;
    vec3 colour = mix(tail_ink, head_ink, clamp(1.0 - behind / tail, 0.0, 1.0));
    ALBEDO = colour * glow * energy * fade;
}
";
}

/// <summary>
/// The cast glyph: while a formula gathers, a small inscribed circle stands before the casting hand - an outer ring drawing itself round,
/// ticks, and a polygon turning inside it (three sides for Force, four for Warding, six for Vital: <see cref="Sides"/>) - and on the
/// release it flashes and widens once and is gone. Upright, across the body's facing: a thing in the world, seen edge-on from beside.
/// </summary>
public sealed partial class CastGlyph : FormulaForm
{
    private static Shader? _shader;
    private readonly ShaderMaterial _material;

    public CastGlyph()
    {
        Name = "CastGlyph";
        _material = Material(_shader ??= new Shader { Code = GlyphCode }, ("drawn", 0f), ("flash", 0f));
        AddChild(Drawn("Glyph", new QuadMesh { Size = new Vector2(0.38f, 0.38f) }, _material));
    }

    /// <summary>The polygon's sides.</summary>
    public int Sides { set => _material.SetShaderParameter("sides", (float)value); }

    public Color Ink { set => _material.SetShaderParameter("ink", value); }

    protected override double EndSeconds => 0.24;

    protected override void Animate()
    {
        _material.SetShaderParameter("drawn", Smooth(Age / 0.4));
        _material.SetShaderParameter("spin", (float)Age * 1.6f);
        float flash = Ending is { } ending ? Mathf.Clamp((float)(ending / EndSeconds), 0f, 1f) : 0f;
        _material.SetShaderParameter("flash", flash);
        Scale = Vector3.One * (1 + 0.5f * flash);
    }

    private const string GlyphCode = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
uniform vec3 ink : source_color = vec3(0.8, 0.9, 1.0);
uniform float drawn = 1.0;
uniform float spin = 0.0;
uniform float sides = 3.0;
uniform float flash = 0.0;
void fragment() {
    vec2 p = (UV - 0.5) * 0.46;  // drawn at 0.46 m and shown on a 0.38 m quad: the figure is 0.83 of its authored size
    float r = length(p);
    float a = atan(p.y, p.x);
    float a01 = a / 6.2831853 + 0.5;
    float px = max(fwidth(r), 1e-4);
    float w = max(0.003, px * 0.6);
    float outer = (1.0 - smoothstep(w, w + px * 1.5, abs(r - 0.19))) * step(a01, drawn);
    float inner = 1.0 - smoothstep(w * 0.7, w * 0.7 + px * 1.5, abs(r - 0.155));
    float tick_d = abs(fract(a01 * 24.0 + 0.5) - 0.5) / 24.0 * 6.2831853 * r;
    float ticks = (1.0 - smoothstep(w * 0.7, w * 0.7 + px * 1.5, tick_d)) * step(0.158, r) * step(r, 0.186) * step(0.25, drawn);
    // The polygon: its edge at the apothem over the cosine of the angle from its nearest edge's middle.
    float sector = 6.2831853 / sides;
    float local_a = mod(a + spin, sector) - sector * 0.5;
    float edge_r = 0.13 * cos(3.14159265 / sides) / cos(local_a);
    float poly = (1.0 - smoothstep(w, w + px * 1.5, abs(r - edge_r))) * step(0.5, drawn);
    float dot_c = 1.0 - smoothstep(0.012, 0.012 + px * 1.5, r);
    float glow = outer + inner * 0.5 + ticks * 0.8 + poly * 0.9 + dot_c * 0.8;
    glow *= (1.0 + flash * 1.5) * (1.0 - flash);
    ALBEDO = ink * glow * 0.6;
}
";
}

/// <summary>
/// The Impulse Bolt in flight: a hard, cold core stretched along its path, and round it a ring of displaced air - the world behind it bent
/// outward, with a fine bright edge - square to the path and turning. (Its trail is the view's streak and the recipe's particles.)
/// </summary>
public sealed partial class BoltCore : FormulaForm
{
    private static Shader? _core;
    private static Shader? _lens;
    private readonly Node3D _ring;

    public BoltCore()
    {
        Name = "BoltCore";
        var core = Drawn("Core", new SphereMesh { Radius = 0.075f, Height = 0.15f, RadialSegments = 12, Rings = 6 },
            Material(_core ??= new Shader { Code = CoreCode }));
        core.Scale = new Vector3(1, 1, 4.2f);
        AddChild(core);
        _ring = Drawn("Lens", new QuadMesh { Size = new Vector2(0.62f, 0.62f) }, Material(_lens ??= new Shader { Code = LensCode }));
        AddChild(_ring);
    }

    protected override double EndSeconds => 0;

    protected override void Animate() => _ring.Rotation = new Vector3(0, 0, (float)Age * 7f);

    private const string CoreCode = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_back, depth_draw_never, shadows_disabled, fog_disabled;
void fragment() {
    float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
    ALBEDO = mix(vec3(0.45, 0.62, 1.0), vec3(1.0), facing) * (0.6 + 2.2 * pow(facing, 1.5));
}
";

    private const string LensCode = @"
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
void fragment() {
    vec2 p = (UV - 0.5) * 2.0;
    float r = length(p);
    // The air bent outward in a ring round the core, strongest at two thirds of the way out, gone at the edge.
    float band = exp(-pow((r - 0.62) / 0.16, 2.0));
    vec2 push = (r > 0.001 ? p / r : vec2(0.0)) * band * 0.012;
    vec3 behind = textureLod(screen_tex, SCREEN_UV - push, 0.0).rgb;
    float px = max(fwidth(r), 1e-4);
    float edge = 1.0 - smoothstep(0.012, 0.012 + px * 1.5, abs(r - 0.8));
    float ticks = step(0.8, fract(atan(p.y, p.x) / 6.2831853 * 8.0)) * edge;
    ALBEDO = behind + vec3(0.75, 0.87, 1.0) * (edge * 0.25 + ticks * 0.55);
    ALPHA = clamp(band * 1.4 + edge, 0.0, 1.0) * (1.0 - smoothstep(0.85, 1.0, r));
}
";
}

/// <summary>
/// Where the Impulse Bolt stops: a pressure front - a shell of displaced air that swells to about a metre and thins as it goes, bright only
/// at its silhouette - and, square to the bolt's path, an inscribed circle thrown outward with it: the formula's mark, gone in a third of a
/// second. <see cref="Along"/> is the bolt's direction.
/// </summary>
public sealed partial class BoltShock : FormulaForm
{
    private static readonly Color Ink = new(0.75f, 0.87f, 1.0f);
    private static Shader? _shell;
    private readonly ShaderMaterial _shellMaterial;
    private readonly ShaderMaterial _circle;
    private readonly MeshInstance3D _front;
    private readonly MeshInstance3D _mark;

    public BoltShock()
    {
        Name = "BoltShock";
        _shellMaterial = Material(_shell ??= new Shader { Code = ShellCode }, ("fade", 1f));
        _front = Drawn("Front", new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 24, Rings = 12 }, _shellMaterial);
        AddChild(_front);
        _circle = Material(CircleShader, ("ink", Ink), ("radius", 0.8f), ("half_size", 1f), ("drawn", 1f));
        _mark = Drawn("Mark", new QuadMesh { Size = new Vector2(2, 2) }, _circle);
        AddChild(_mark);
        _flashMaterial = Material(_flash ??= new Shader { Code = FlashCode }, ("energy", 3f));
        _core = Drawn("Flash", new SphereMesh { Radius = 0.18f, Height = 0.36f, RadialSegments = 16, Rings = 8 }, _flashMaterial);
        AddChild(_core);
        Animate();
    }

    private static Shader? _flash;
    private readonly ShaderMaterial _flashMaterial;
    private readonly MeshInstance3D _core;

    /// <summary>The bolt's direction, world space: the mark stands square to it.</summary>
    public Vector3 Along
    {
        set
        {
            if (value.LengthSquared() > 0.0001f)
                _mark.Basis = Basis.LookingAt(value.Normalized(), Mathf.Abs(value.Normalized().Dot(Vector3.Up)) > 0.98f ? Vector3.Forward : Vector3.Up);
        }
    }

    public const double Seconds = 0.38;

    protected override double EndSeconds => 0;

    protected override void Animate()
    {
        float t = Mathf.Clamp((float)(Age / Seconds), 0, 1);
        float grow = 1 - (1 - t) * (1 - t);
        _front.Scale = Vector3.One * (0.15f + 1.0f * grow);
        _shellMaterial.SetShaderParameter("fade", 1 - t);
        _mark.Scale = Vector3.One * (0.25f + 0.85f * grow);
        _circle.SetShaderParameter("energy", 2.4f * (1 - t) * (1 - t));
        _mark.Visible = t < 1;
        _front.Visible = t < 1;
        // The blow itself: a hard white-blue point that is gone in a tenth of a second.
        // Short, so it never hides a creature's tell for more than a few frames.
        float flash = Mathf.Clamp(1 - (float)(Age / 0.11), 0, 1);
        _core.Visible = flash > 0;
        _core.Scale = Vector3.One * (1 + 1.2f * (1 - flash));
        _flashMaterial.SetShaderParameter("energy", 3f * flash * flash);
    }

    private const string FlashCode = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_back, depth_draw_never, shadows_disabled, fog_disabled;
uniform float energy = 1.0;
void fragment() {
    float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
    ALBEDO = mix(vec3(0.5, 0.68, 1.0), vec3(1.0), facing) * pow(facing, 1.2) * energy;
}
";

    private const string ShellCode = @"
shader_type spatial;
render_mode unshaded, blend_mix, cull_back, depth_draw_never, shadows_disabled, fog_disabled;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float fade = 1.0;
void fragment() {
    float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
    float rim = pow(1.0 - facing, 2.5);
    vec2 push = NORMAL.xy * rim * 0.02 * fade;
    vec3 behind = textureLod(screen_tex, SCREEN_UV - push, 0.0).rgb;
    ALBEDO = behind + vec3(0.72, 0.85, 1.0) * rim * 1.5 * fade;
    ALPHA = clamp(rim * 1.8, 0.0, 1.0) * fade;
}
";
}
