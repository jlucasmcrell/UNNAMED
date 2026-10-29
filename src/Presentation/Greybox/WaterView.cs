// UNNAMED Presentation - water surfaces in the scenery (Phase B, B0.8 / B6, D18, D20): the river round the hollow on the ravine floors,
// the gorge's own runs, and every authored water body content declares (a creek, a millpond, a spring)
// Godot presentation only (D-11): a surface to look at; no water is simulated, and nothing reads it

using Godot;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// <c>--visual water=flow</c>: a river along the ravine's floor on its three sides (scenery beyond the walkable edge, seen from the edge),
/// flowing round the hollow - up the west side, along the north, down the east - drawn with <see cref="FlowWater"/>: normals carried by the
/// current (two phases of procedural ripples advected along the flow and blended so nothing stretches), colour by depth, foam where it is
/// shallow and streaks of it carried downstream, a little refraction and a sky reflection. <c>classic</c> draws Phase A's stream shader on
/// the same strips; <c>boujie</c> is gone (B0.8: rejected, removed). Water inside the hollow needs terrain below a water level: M7's.
/// <para>
/// The authored bodies (D18/D20: <see cref="WaterSite"/>) are drawn from the same material, over the beds <see cref="WaterShore"/> cuts
/// for them: the surface at its own level, its edge a shoreline with the body's own noise in it, its colour taken from its own depth,
/// and - for the waters small enough to be pools - stones round the rim and reeds at the edge.
/// </para>
/// </summary>
public static class WaterView
{
    public static MeshInstance3D BuildTidal(Node3D parent, UNNAMED.Domain.Spatial.WalkSpace space)
    {
        var material = (ShaderMaterial)FlowWater.Duplicate();
        material.SetShaderParameter("flow_dir", new Vector2(0, -1));
        material.SetShaderParameter("flow_speed", 0.22f);
        material.SetShaderParameter("foam_m", .22f);
        material.SetShaderParameter("vertical_shore", true);
        material.SetShaderParameter("depth_fade_m", 2.4f);
        material.SetShaderParameter("deep_colour", new Vector3(0.012f, 0.021f, 0.024f));
        material.SetShaderParameter("shallow_colour", new Vector3(0.16f, 0.19f, 0.15f));
        var water = new MeshInstance3D { Name = "TidalFlowWater", MaterialOverride = material,
            Mesh = new PlaneMesh { Size = new Vector2((space.MaxXMm-space.MinXMm)/1000f, (space.MaxZMm-space.MinZMm)/1000f) },
            Position = new Vector3((space.MinXMm+space.MaxXMm)/2000f, 0, (space.MinZMm+space.MaxZMm)/2000f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        parent.AddChild(water);
        return water;
    }

    /// <summary>The rivers on the ravine floors; null (and why) when the run draws none.</summary>
    public static Node3D? BuildRavineRiver(Node3D parent, Rect2 region, Func<float, float, float> height, out string? why)
    {
        why = null;
        if (VisualOptions.Water is not ("flow" or "classic"))
        {
            why = VisualOptions.Water == "boujie" ? "Boujie Water was rejected in B0.8 and is not in the project" : null;
            return null;
        }
        var root = new Node3D { Name = "RavineRiver" };
        parent.AddChild(root);
        foreach (var (centre, size, flow) in Strips(region))
            Strip(root, flow.Y > 0 ? "west" : flow.Y < 0 ? "east" : "north", centre, size, flow, height);
        return root;
    }

    /// <summary>
    /// The river's strips on the ravine floor - west, north, east: each its centre line 17 m out (the floor runs 8-26 m out), its size
    /// (15 m wide, running 24 m past the corners) and the way the current runs along it. Shared with the mist over them.
    /// </summary>
    public static IEnumerable<(Vector3 Centre, Vector2 Size, Vector2 Flow)> Strips(Rect2 region)
    {
        float x0 = region.Position.X, x1 = region.End.X, z0 = region.Position.Y, z1 = region.End.Y;
        const float Out = 17f, Width = 15f, Past = 24f;
        yield return (new Vector3(x0 - Out, 0, (z0 + z1) / 2), new Vector2(Width, z1 - z0 + 2 * Past), new Vector2(0, 1));
        if (DenGorge.On(region))
        {
            // The north run stops either side of the den gorge, whose own river comes down into it (DenGorge.Runs): from the west it
            // runs east into the gorge, from the east it runs west.
            const float West = 440f, East = 704f;
            yield return (new Vector3((x0 - Past + West) / 2, 0, z1 + Out), new Vector2(West - x0 + Past, Width), new Vector2(1, 0));
            yield return (new Vector3((East + x1 + Past) / 2, 0, z1 + Out), new Vector2(x1 + Past - East, Width), new Vector2(-1, 0));
        }
        else
            yield return (new Vector3((x0 + x1) / 2, 0, z1 + Out), new Vector2(x1 - x0 + 2 * Past, Width), new Vector2(1, 0));
        yield return (new Vector3(x1 + Out, 0, (z0 + z1) / 2), new Vector2(Width, z1 - z0 + 2 * Past), new Vector2(0, -1));
    }

    /// <summary>
    /// The gorge behind the den (spec 28): its two river runs into the swallow, white where they fall, brighter and faster than the
    /// ravine's slow strips; the fall off the far wall; and the stream that feeds it across the far shelf.
    /// </summary>
    public static void BuildDenGorge(Node3D parent, Func<float, float, float> height)
    {
        var root = new Node3D { Name = "DenGorgeWater" };
        parent.AddChild(root);
        foreach (var (name, flow, samples) in DenGorge.Runs(height))
        {
            var river = (ShaderMaterial)FlowWater.Duplicate();
            river.SetShaderParameter("flow_dir", flow);
            river.SetShaderParameter("flow_speed", 1.7f);
            river.SetShaderParameter("shallow_colour", new Vector3(0.22f, 0.35f, 0.32f));
            river.SetShaderParameter("deep_colour", new Vector3(0.07f, 0.15f, 0.16f));
            river.SetShaderParameter("depth_fade_m", 2.4f);
            river.SetShaderParameter("foam_m", 0.3f);
            river.SetShaderParameter("rapids", 1f);
            river.SetShaderParameter("edge_fade", 1f);
            root.AddChild(DenGorge.RiverMesh(name, flow, samples, river));
        }
        float faceD = DenGorge.FarFootAt(DenGorge.FallX) + 0.75f;
        float top = height(DenGorge.FallX, DenGorge.Edge + faceD + 4f) + 0.35f;
        float pool = height(DenGorge.FallX, DenGorge.Edge + faceD - 6f) + 1.2f;
        root.AddChild(DenGorge.FallMesh(top, pool, faceD));
        // The feeding stream: a narrow strip down its cut over the far shelf to the lip.
        var stream = (ShaderMaterial)FlowWater.Duplicate();
        stream.SetShaderParameter("flow_dir", new Vector2(0, -1));
        stream.SetShaderParameter("flow_speed", 1.4f);
        stream.SetShaderParameter("foam_m", 0.3f);
        stream.SetShaderParameter("shallow_colour", new Vector3(0.22f, 0.35f, 0.32f));
        root.AddChild(new MeshInstance3D
        {
            Name = "gorge_fall_stream", Mesh = new PlaneMesh { Size = new Vector2(4.5f, 70f), SubdivideWidth = 4, SubdivideDepth = 35 },
            MaterialOverride = stream, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(DenGorge.FallX, top - 0.15f, DenGorge.Edge + faceD + 35.5f),
        });
    }

    /// <summary>
    /// An authored water body as the camera gets it: its surface at its own level in the bed <see cref="WaterShore"/> cut for it, its edge
    /// a shoreline rather than a quad's own corner, its colour taken from its own depth, and - where it is small enough to be a pool -
    /// stones round the rim and reeds at the edge.
    /// </summary>
    public static Node3D Body(WaterShore shore, Func<float, float, float> ground, Art.ArtLibrary? art, Art.ArtCoverage coverage)
    {
        var body = new Node3D { Name = shore.Water.Key };
        body.AddChild(new MeshInstance3D
        {
            Name = "surface", Mesh = Surface(shore, ground), MaterialOverride = Material(shore),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        if (shore.Pool)
            Rim(body, shore, ground, art, coverage);
        return body;
    }

    /// <summary>
    /// The gorge's own water (the look the owner named as the reference), at an authored body's own depth and flow: the same shader the
    /// den's river and the ravine's strips are drawn with, with the tint driven by the body's <c>depth_m</c> rather than by the screen.
    /// </summary>
    private static ShaderMaterial Material(WaterShore shore)
    {
        var water = shore.Water;
        bool runs = water.PointsMm.Length > 1 && !shore.Pool;
        var material = (ShaderMaterial)FlowWater.Duplicate();
        material.SetShaderParameter("flow_dir", runs ? (shore.Line[^1] - shore.Line[0]).Normalized() : new Vector2(0, -1));
        material.SetShaderParameter("flow_speed", water.Kind is "creek" or "stream" or "river" ? 1.25f : 0.22f);
        // Shallow water clear and greenish, deeper water darker and bluer: the gradient is the body's own depth, not a guess.
        float strange = Otherworld.Strength;
        material.SetShaderParameter("shallow_colour", new Vector3(0.24f, 0.38f, 0.32f).Lerp(new Vector3(0.16f, 0.34f, 0.36f), strange));
        material.SetShaderParameter("deep_colour", new Vector3(0.05f, 0.12f, 0.16f).Lerp(new Vector3(0.05f, 0.08f, 0.20f), strange));
        material.SetShaderParameter("depth_fade_m", shore.Depth);
        material.SetShaderParameter("authored_depth", 1f);
        // Close enough to the bank to be judged: fine chop rather than one ripple across the body, a soft sheen rather than a mirror,
        // and only a thread of foam at the waterline.
        material.SetShaderParameter("ripple_scale", 1.9f);
        material.SetShaderParameter("ripple_strength", 0.25f);
        material.SetShaderParameter("refraction", 0.008f);
        material.SetShaderParameter("roughness_floor", 0.26f);
        material.SetShaderParameter("specular_floor", 0.22f);
        material.SetShaderParameter("foam_m", 0.08f);
        material.SetShaderParameter("foam_amount", 0.35f);
        material.SetShaderParameter("edge_fade", 1f);
        material.SetShaderParameter("rapids", runs ? 0.22f : 0f);
        return material;
    }

    /// <summary>
    /// The surface: a mesh whose edge follows the body's own wobbled line, and whose vertex colours carry what the shader needs - the
    /// shore blend in green and the water's own depth in blue, both measured off the carved bed under each vertex.
    /// </summary>
    private static ArrayMesh Surface(WaterShore shore, Func<float, float, float> ground)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        var points = new List<Vector3>();
        if (shore.Disc)
            Pool(shore, ground, tool, points);
        else
            Run(shore, ground, tool, points);
        var mesh = new ArrayMesh();
        tool.Commit(mesh);
        return mesh;
    }

    /// <summary>A pond, spring or well: rings out from its middle, each ring as far out as its own wobbled edge.</summary>
    private static void Pool(WaterShore shore, Func<float, float, float> ground, SurfaceTool tool, List<Vector3> points)
    {
        const int Segments = 72, Rings = 12;
        var centre = shore.Centre;
        float rapids = 0f;
        int first = points.Count;
        for (int k = 1; k <= Rings; k++)
        for (int s = 0; s < Segments; s++)
        {
            float t = s / (float)Segments;
            float outer = shore.SurfaceHalf(t), radius = outer * k / Rings;
            Put(shore, ground, tool, points, centre + new Vector2(Mathf.Cos(t * Mathf.Tau), Mathf.Sin(t * Mathf.Tau)) * radius, outer - radius, rapids);
        }
        int middle = points.Count;
        Put(shore, ground, tool, points, centre, shore.Band * 2f, rapids);
        for (int k = 0; k + 1 < Rings; k++)
        for (int s = 0; s < Segments; s++)
        {
            int a = first + k * Segments + s, b = first + k * Segments + (s + 1) % Segments;
            int c = a + Segments, d = b + Segments;
            Face(tool, points, a, c, b);
            Face(tool, points, b, c, d);
        }
        for (int s = 0; s < Segments; s++)
            Face(tool, points, middle, first + s, first + (s + 1) % Segments);
    }

    /// <summary>A creek, river or millrace: cross-sections along its line, its ends narrowing away where content stops naming it.</summary>
    private static void Run(WaterShore shore, Func<float, float, float> ground, SurfaceTool tool, List<Vector3> points)
    {
        const int Across = 10;
        float rapids = shore.Water.Kind is "creek" or "stream" or "river" ? 0.28f : 0f;
        var line = shore.Line;
        var samples = new List<(Vector2 On, Vector2 Normal, float Along)>();
        for (int i = 1; i < line.Count; i++)
        {
            Vector2 a = line[i - 1], b = line[i], span = b - a;
            float length = MathF.Max(1e-3f, span.Length());
            var normal = new Vector2(-span.Y / length, span.X / length);
            int steps = Math.Max(1, (int)MathF.Ceiling(length / 1.2f));
            for (int k = i == 1 ? 0 : 1; k <= steps; k++)
            {
                float at = k / (float)steps;
                samples.Add((a + span * at, normal, (shore.Arc(i - 1) + length * at) / MathF.Max(1e-3f, shore.Length)));
            }
        }
        int first = points.Count;
        foreach (var (on, normal, along) in samples)
        {
            float taper = Math.Clamp(MathF.Min(along, 1f - along) / 0.05f, 0.25f, 1f);
            float half = shore.SurfaceHalf(along) * taper;
            for (int j = 0; j <= Across; j++)
            {
                float across = j / (float)Across * 2f - 1f;
                Put(shore, ground, tool, points, on + normal * (half * across), half * (1f - MathF.Abs(across)), rapids);
            }
        }
        for (int i = 0; i + 1 < samples.Count; i++)
        for (int j = 0; j < Across; j++)
        {
            int a = first + i * (Across + 1) + j, b = a + Across + 1;
            Face(tool, points, a, b, a + 1);
            Face(tool, points, a + 1, b, b + 1);
        }
    }

    /// <summary>
    /// One surface vertex: the depth the carved bed leaves under it is what the water is tinted by and what fades it out at the shore,
    /// and the last half metre inside the surface's own edge fades to nothing so a sheet that outruns its bed is never cut off square.
    /// </summary>
    private static void Put(WaterShore shore, Func<float, float, float> ground, SurfaceTool tool, List<Vector3> points, Vector2 at,
        float inset, float rapids)
    {
        float depth = MathF.Max(0f, shore.Level - ground(at.X, at.Y));
        float shoreMix = GroundField.SmoothStep(0f, shore.BandDepth, depth) * GroundField.SmoothStep(0f, 0.5f, inset);
        tool.SetColor(new Color(rapids, shoreMix, Math.Clamp(depth / shore.Depth, 0f, 1f), 1f));
        tool.SetNormal(Vector3.Up);
        tool.AddVertex(new Vector3(at.X, shore.Level, at.Y));
        points.Add(new Vector3(at.X, 0f, at.Y));
    }

    /// <summary>
    /// One triangle, wound so it faces up whichever way its corners were walked: Godot's front face is the one that runs counter-clockwise
    /// in (x, z) for a surface whose normal is up, which is the rule <see cref="DenGorge.RiverMesh"/> winds by.
    /// </summary>
    private static void Face(SurfaceTool tool, List<Vector3> points, int a, int b, int c)
    {
        Vector3 pa = points[a], pb = points[b], pc = points[c];
        if ((pb.X - pa.X) * (pc.Z - pa.Z) - (pc.X - pa.X) * (pb.Z - pa.Z) < 0f)
            (b, c) = (c, b);
        tool.AddIndex(a);
        tool.AddIndex(b);
        tool.AddIndex(c);
    }

    /// <summary>
    /// A pool's rim, from the art the areas already place - the creek's own cattails and mossy rocks - half sunk into the bank the carve
    /// made. Every angle, size and turn is a sample of the body's own random channel, so a spring is the same in every run and no two
    /// springs are the same.
    /// </summary>
    private static void Rim(Node3D body, WaterShore shore, Func<float, float, float> ground, Art.ArtLibrary? art, Art.ArtCoverage coverage)
    {
        if (art is null)
            return;
        var rim = new Node3D { Name = "rim" };
        body.AddChild(rim);
        string key = shore.Water.Key;
        var stones = new SortedDictionary<string, Node3D?>(StringComparer.Ordinal)
        {
            ["rock_ph_moss_pebble_a"] = art.Model("rock_ph_moss_pebble_a"),
            ["rock_ph_moss_rock_small_a"] = art.Model("rock_ph_moss_rock_small_a"),
            ["rock_ph_moss_boulder_s"] = art.Model("rock_ph_moss_boulder_s"),
        };
        int count = 5 + (int)(shore.Roll(20) * 4f);
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)count + (shore.Roll((uint)(30 + i)) - 0.5f) * 0.12f;
            float pick = shore.Roll((uint)(40 + i));
            string id = pick < 0.5f ? "rock_ph_moss_pebble_a" : pick < 0.85f ? "rock_ph_moss_rock_small_a" : "rock_ph_moss_boulder_s";
            if (stones[id] is not { } stone)
            {
                coverage.Fallback("water", $"{key} rim", art.Why(id) ?? "its rock art did not load: the rim keeps only what it has", id);
                continue;
            }
            float outM = shore.EdgeAt(t) + 0.10f + shore.Roll((uint)(50 + i)) * 0.55f;
            var at = shore.Centre + new Vector2(Mathf.Cos(t * Mathf.Tau), Mathf.Sin(t * Mathf.Tau)) * outM;
            float size = id == "rock_ph_moss_boulder_s" ? 0.45f + shore.Roll((uint)(60 + i)) * 0.25f : 0.75f + shore.Roll((uint)(60 + i)) * 0.6f;
            Place(rim, stone, at, ground, size, 0.06f + shore.Roll((uint)(65 + i)) * 0.08f, shore.Roll((uint)(68 + i)) * 360f);
            coverage.Resolved("water", $"{key} rim", id);
        }
        Node3D? clump = art.Model("flora_cattail_clump");
        if (clump is null)
        {
            coverage.Fallback("water", $"{key} reeds", art.Why("flora_cattail_clump") ?? "its reed art did not load: the pool keeps bare", "flora_cattail_clump");
            return;
        }
        float height = MathF.Max(0.2f, Art.ArtGallery.Bounds(clump).Size.Y);
        int reeds = 3 + (int)(shore.Roll(21) * 4f);
        for (int i = 0; i < reeds; i++)
        {
            float t = (i + 0.5f) / reeds + (shore.Roll((uint)(70 + i)) - 0.5f) * 0.16f;
            float outM = shore.EdgeAt(t) - 0.3f + shore.Roll((uint)(80 + i)) * 0.8f;
            var at = shore.Centre + new Vector2(Mathf.Cos(t * Mathf.Tau), Mathf.Sin(t * Mathf.Tau)) * outM;
            float size = (1.15f + shore.Roll((uint)(90 + i)) * 0.55f) / height;
            Place(rim, clump, at, ground, size, 0.03f, shore.Roll((uint)(95 + i)) * 360f);
            coverage.Resolved("water", $"{key} reeds", "flora_cattail_clump");
        }
    }

    /// <summary>One prop on the carved ground, its own base on the ground less a little so it sits in the bank rather than on it.</summary>
    private static void Place(Node3D parent, Node3D model, Vector2 at, Func<float, float, float> ground, float scale, float sink, float yaw)
    {
        var holder = new Node3D
        {
            Name = $"{model.Name}_{parent.GetChildCount()}",
            Position = new Vector3(at.X, ground(at.X, at.Y) - sink, at.Y),
            Rotation = new Vector3(0, Mathf.DegToRad(yaw), 0),
            Scale = Vector3.One * scale,
        };
        holder.AddChild(model.Duplicate() as Node3D ?? model);
        parent.AddChild(holder);
    }

    private static void Strip(Node3D root, string name, Vector3 centre, Vector2 size, Vector2 flow, Func<float, float, float> height)
    {
        // The surface stands a little over the floor's mean along the strip: most of the floor is under it, its high spots are banks.
        float level = 0;
        for (int i = -8; i <= 8; i++)
        {
            float along = i / 8f * 0.45f;
            var p = centre + new Vector3(flow.X * along * size.X, 0, flow.Y * along * size.Y);
            level += height(p.X, p.Z) / 17f;
        }
        Material material;
        if (VisualOptions.Water == "flow")
        {
            var flowing = (ShaderMaterial)FlowWater.Duplicate();
            flowing.SetShaderParameter("flow_dir", flow);
            // The otherworld concepts: a little more teal in the shallows, a little more violet-blue in the deep (Otherworld.cs).
            float strange = Otherworld.Strength;
            if (strange > 0)
            {
                flowing.SetShaderParameter("shallow_colour", new Vector3(0.16f, 0.27f, 0.24f).Lerp(new Vector3(0.11f, 0.27f, 0.30f), strange));
                flowing.SetShaderParameter("deep_colour", new Vector3(0.03f, 0.07f, 0.08f).Lerp(new Vector3(0.035f, 0.05f, 0.11f), strange));
            }
            material = flowing;
        }
        else
        {
            material = Palette.StreamWater;
        }
        root.AddChild(new MeshInstance3D
        {
            Name = name, Mesh = new PlaneMesh { Size = size, SubdivideWidth = 32, SubdivideDepth = 32 }, MaterialOverride = material,
            Position = new Vector3(centre.X, level + 1.1f, centre.Z), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>The flowing-water surface (B6), shared and duplicated per strip for its current.</summary>
    public static ShaderMaterial FlowWater { get; } = new()
    {
        Shader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode blend_mix, cull_back, depth_draw_opaque, diffuse_burley, specular_schlick_ggx, shadows_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_linear_mipmap;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform vec2 flow_dir = vec2(1.0, 0.0);
uniform float flow_speed = 0.9;
uniform float ripple_scale = 0.55;
uniform float ripple_strength = 0.55;
uniform vec3 shallow_colour : source_color = vec3(0.16, 0.27, 0.24);
uniform vec3 deep_colour : source_color = vec3(0.03, 0.07, 0.08);
uniform float depth_fade_m = 1.6;
uniform float foam_m = 0.12;
uniform bool vertical_shore = false;
uniform float refraction = 0.018;
uniform float rapids = 0.0;    // how much the vertex colour's red (0 still, 1 a rapid) whitens the run: the den gorge's runs only
uniform float edge_fade = 0.0;   // fades the surface to its sides by the vertex colour's green (the den gorge's runs only)
// 1: the mesh carries the water's own depth in the vertex colour's blue, measured off the bed under it, and that is what tints it.
// The authored bodies (a creek's depth_m, a millpond's) cut their own beds, and their surfaces meet a bank at a grazing angle where
// the screen's depth is no help, so the depth comes from the mesh instead.
uniform float authored_depth = 0.0;
// How mirror-smooth still water is. A body seen from the bank at 1.7 m is close enough that a 0.06-roughness mirror splits into
// glints off every ripple crest and reads as broken glass; the gorge, seen from higher up and further off, is drawn with the mirror.
uniform float roughness_floor = 0.06;
uniform float specular_floor = 0.55;
// How much foam a body keeps. The gorge's foam is read from far off, where its noise cells are a texture; the same amount seen from a
// bank at 1.7 m is a patchwork of pale blocks, so an authored body keeps only a thread of it.
uniform float foam_amount = 1.0;

varying vec3 world_pos;

// A hash without the sine. `fract(sin(dot(p, k)) * 43758.5)` is the usual one-liner and it is WRONG at this world's coordinates:
// the ripple field is sampled at world metres times a scale, and the fbm's fourth octave pushes that into the thousands, where an fp32
// ULP is most of a radian and the sine stops varying from cell to cell - every noise cell comes out flat, and the surface reads as
// pale rectangular blocks (the owner's report, 2026-10-29 02:00). Dave Hoskins' hash keeps its resolution there.
float hash(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}
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
vec2 slope(vec2 p) {
    float e = 0.08;
    float h = fbm(p);
    return vec2(fbm(p + vec2(e, 0.0)) - h, fbm(p + vec2(0.0, e)) - h) / e;
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    // Two phases of the ripple field carried downstream, each reset as the other peaks, so the pattern flows without stretching.
    vec2 p = world_pos.xz * ripple_scale;
    float t = TIME * flow_speed * 0.25;
    float ph0 = fract(t), ph1 = fract(t + 0.5);
    float w = abs(1.0 - 2.0 * ph0);
    vec2 s = mix(slope(p - flow_dir * ph0 * 2.0), slope(p - flow_dir * ph1 * 2.0 + vec2(3.7, 1.3)), 1.0 - w);
    s += slope(p * 2.7 - flow_dir * TIME * flow_speed * 0.35) * 0.35;
    vec3 n = normalize(vec3(-s.x * ripple_strength, 1.0, -s.y * ripple_strength));
    NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

    float raw = textureLod(depth_tex, SCREEN_UV, 0.0).r;
    vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw, 1.0);
    float depth = max(-view.z / view.w - (-VERTEX.z), 0.0);
    // Tidal flats need vertical water depth: ray length grows at grazing angles
    // and otherwise makes shoreline foam vanish when looking across the sound.
    if (vertical_shore) depth = max(world_pos.y - (INV_VIEW_MATRIX * vec4(view.xyz / view.w, 1.0)).y, 0.0);
    float deep = clamp(depth / depth_fade_m, 0.0, 1.0);
    // A mesh that knows its own bed says so in blue: its depth is the water it stands over, not what this pixel's ray happens to pass.
    if (authored_depth > 0.5) {
        deep = clamp(COLOR.b, 0.0, 1.0);
        depth = deep * depth_fade_m;
    }

    // What lies under the water, bent a little by the ripples - only where the screen copy holds it (instances fading in or out are
    // not in it: its alpha is 0 there).
    vec2 bent = SCREEN_UV + n.xz * refraction * deep;
    vec4 under = textureLod(screen_tex, bent, 0.0);
    vec3 body = mix(shallow_colour, deep_colour, deep);
    vec3 colour = mix(body, under.rgb * body * 3.0, (1.0 - deep) * 0.6 * under.a);

    // Foam where it is shallow, broken up by noise and drawn out downstream in streaks.
    vec2 streak = world_pos.xz * vec2(0.9, 0.9) - flow_dir * TIME * flow_speed;
    float breakup = vnoise(streak * vec2(1.0 + abs(flow_dir.y) * 3.0, 1.0 + abs(flow_dir.x) * 3.0));
    float foam = (1.0 - smoothstep(0.0, foam_m, depth)) * smoothstep(0.55, 0.85, breakup) * 0.7;
    foam += smoothstep(0.8, 0.95, fbm(streak * 0.6)) * 0.2;
    foam += rapids * COLOR.r * 0.8 * smoothstep(0.35, 0.75, vnoise(streak * vec2(2.6, 2.6) + vec2(11.0, 3.0)) * 0.5 + breakup * 0.5);
    foam *= foam_amount;
    colour = mix(colour, vec3(0.62, 0.66, 0.65), clamp(foam, 0.0, 1.0));

    ALBEDO = colour;
    ALPHA = clamp(mix(0.55, 0.96, deep) * smoothstep(0.0, 0.06, depth) + foam * 0.3, 0.0, 1.0);
    ALPHA *= mix(1.0, smoothstep(0.0, 1.0, COLOR.g), edge_fade);
    ROUGHNESS = mix(roughness_floor, 0.5, clamp(foam, 0.0, 1.0));
    SPECULAR = specular_floor;
}
",
        },
    };
}
