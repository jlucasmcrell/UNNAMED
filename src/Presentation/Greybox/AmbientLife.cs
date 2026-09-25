// UNNAMED Presentation - birds, butterflies and fireflies drawn by their shaders (res://Art/ambient_life.json) (Phase B, B10)
// Godot presentation only (D-11): ambient life is decoration - no creature of the simulation, nothing hit, heard by the game, saved or read

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Ambient life, kept apart from the game's creatures: each kind is one MultiMesh whose instances stand still at their anchors while the
/// shader flies them - a bird circles its flock's centre with the flock, a butterfly or a firefly wanders a small loop - and beats their
/// wings (or blinks), all from each instance's seed and the clock, so there is no node and no script per animal. Anchors are placed from
/// the world seed by the kind's rules (its grounds, its altitude), and a kind appears only in its hours and weathers.
/// </summary>
public static class AmbientLife
{
    public const string ResourcePath = "res://Art/ambient_life.json";

    /// <summary>The run's ambient life under <paramref name="parent"/>; returns how many animals it drew.</summary>
    public static int Build(Node3D parent, GroundField ground, ulong seed)
    {
        if (!Godot.FileAccess.FileExists(ResourcePath))
            return 0;
        using var json = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ResourcePath));
        string weather = VisualOptions.Weather ?? "fair";
        bool day = SkyView.DaylightNow > 0.5f;
        var region = ground.Region;
        int drawn = 0;
        foreach (var kind in json.RootElement.GetProperty("kinds").EnumerateObject())
        {
            var k = kind.Value;
            bool dayOnly = k.TryGetProperty("day_only", out var d) && d.ValueKind == JsonValueKind.True;
            bool nightOnly = k.TryGetProperty("night_only", out var n) && n.ValueKind == JsonValueKind.True;
            var weathers = k.TryGetProperty("weather", out var w) ? w.EnumerateArray().Select(x => x.GetString()!).ToHashSet() : null;
            if (dayOnly && !day || nightOnly && day || (weathers is null ? weather == "storm" : !weathers.Contains(weather)))
                continue;
            var grounds = k.TryGetProperty("grounds", out var g) ? g.EnumerateArray().Select(x => x.GetString()!).ToHashSet() : null;
            string mesh = k.GetProperty("mesh").GetString()!;
            string motion = k.TryGetProperty("motion", out var m) ? m.GetString()! : mesh == "bird" ? "circle" : mesh == "point" ? "glow" : "wander";
            int count = k.GetProperty("count").GetInt32();
            int flocks = k.TryGetProperty("flocks", out var f) ? f.GetInt32() : 0;
            var (altLo, altHi) = Range(k, "altitude");
            var (radLo, radHi) = Range(k, "radius");
            var (spdLo, spdHi) = Range(k, "speed");
            var (sizeLo, sizeHi) = Range(k, "size");
            // A stable hash of the kind's name (string.GetHashCode differs from run to run): the same animals every run.
            ulong hash = 14695981039346656037UL;
            foreach (char c in kind.Name)
                hash = (hash ^ c) * 1099511628211UL;
            var random = new RandomNumberGenerator { Seed = seed ^ hash };

            // Anchors: points of the kind's grounds (tried at random, kept when most of the ground there is one of them).
            Vector3 Anchor()
            {
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    float x = random.RandfRange(region.Position.X + 4, region.End.X - 4), z = random.RandfRange(region.Position.Y + 4, region.End.Y - 4);
                    if (grounds is not null && !OnGround(ground, x, z, grounds))
                        continue;
                    return new Vector3(x, ground.Height(x, z) + random.RandfRange(altLo, altHi), z);
                }
                return Vector3.Inf;
            }
            var placed = new List<(Vector3 At, Color Custom, float Size)>();
            float around = k.TryGetProperty("around_camera", out var ac) ? (float)ac.GetDouble() : 0f;
            if (around > 0)
            {
                // Anchors anywhere in a square of side 2R; the shader wraps each round the camera and stands it on the ground there.
                for (int i = 0; i < count; i++)
                    placed.Add((new Vector3(random.RandfRange(0, 2 * around), random.RandfRange(altLo, altHi), random.RandfRange(0, 2 * around)),
                        new Color(random.Randf(), random.RandfRange(radLo, radHi), random.RandfRange(spdLo, spdHi), random.Randf()), random.RandfRange(sizeLo, sizeHi)));
            }
            else if (flocks > 0)
            {
                for (int flock = 0; flock < flocks; flock++)
                {
                    var centre = Anchor();
                    float radius = random.RandfRange(radLo, radHi), speed = random.RandfRange(spdLo, spdHi), phase = random.Randf();
                    for (int i = 0; i < count / flocks; i++)
                        placed.Add((centre + new Vector3(0, random.RandfRange(-2, 2), 0),
                            new Color(phase + random.RandfRange(-0.04f, 0.04f), radius + random.RandfRange(-2, 2), speed, random.Randf()), random.RandfRange(sizeLo, sizeHi)));
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                    placed.Add((Anchor(), new Color(random.Randf(), random.RandfRange(radLo, radHi), random.RandfRange(spdLo, spdHi), random.Randf()), random.RandfRange(sizeLo, sizeHi)));
            }
            placed.RemoveAll(p => !float.IsFinite(p.At.X));
            if (placed.Count == 0)
                continue;
            GD.Print($"UNNAMED ambient life: {kind.Name} x{placed.Count}, the first at {placed[0].At}");
            // Each animal leaves its anchor by up to its path's radius: its mesh's bounds are grown to cover the path, so each instance is
            // culled by where it can actually be (the MultiMesh's bounds are the instances' bounds).
            var shape = (ArrayMesh)Shape(mesh);
            shape.CustomAabb = around > 0
                ? new Aabb(new Vector3(-1000, -1000, -1000), new Vector3(2000, 2000, 2000))   // drawn wherever the camera is: never culled
                : new Aabb(new Vector3(-radHi - 3, -3, -radHi - 3), new Vector3(2 * radHi + 6, 6, 2 * radHi + 6));
            var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = shape, InstanceCount = placed.Count };
            for (int i = 0; i < placed.Count; i++)
            {
                multi.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.One * placed[i].Size), placed[i].At));
                multi.SetInstanceCustomData(i, placed[i].Custom);
            }
            parent.AddChild(new MultiMeshInstance3D
            {
                Name = "Ambient_" + kind.Name, Multimesh = multi, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = around > 0 ? AroundCamera(Material(motion), ground, grounds, around) : Material(motion),
            });
            drawn += placed.Count;
        }
        GD.Print($"UNNAMED ambient life: {drawn} animals ({(day ? "day" : "night")}, {weather})");
        return drawn;
    }

    /// <summary>
    /// A kind's material that wraps its animals round the camera: the ground's height and the kind's grounds (a mask) baked into two
    /// small textures over the region, which the shader samples where each animal comes to be.
    /// </summary>
    private static Material AroundCamera(Material shared, GroundField ground, HashSet<string>? grounds, float radius)
    {
        var material = (ShaderMaterial)shared.Duplicate();
        const int N = 128;
        var region = ground.Region;
        var height = Image.CreateEmpty(N, N, false, Image.Format.Rf);
        var mask = Image.CreateEmpty(N, N, false, Image.Format.L8);
        for (int j = 0; j < N; j++)
        for (int i = 0; i < N; i++)
        {
            float x = region.Position.X + (i + 0.5f) / N * region.Size.X, z = region.Position.Y + (j + 0.5f) / N * region.Size.Y;
            height.SetPixel(i, j, new Color(ground.Height(x, z), 0, 0));
            float keep = grounds is null || OnGround(ground, x, z, grounds) ? 1f : 0f;
            mask.SetPixel(i, j, new Color(keep, keep, keep));
        }
        material.SetShaderParameter("around_camera", true);
        material.SetShaderParameter("wrap_r", radius);
        material.SetShaderParameter("region", new Vector4(region.Position.X, region.Position.Y, region.Size.X, region.Size.Y));
        material.SetShaderParameter("height_tex", ImageTexture.CreateFromImage(height));
        material.SetShaderParameter("mask_tex", ImageTexture.CreateFromImage(mask));
        return material;
    }

    private static (float, float) Range(JsonElement e, string name) =>
        e.TryGetProperty(name, out var r) && r.GetArrayLength() == 2 ? ((float)r[0].GetDouble(), (float)r[1].GetDouble()) : (1f, 1f);

    private static bool OnGround(GroundField ground, float x, float z, HashSet<string> grounds)
    {
        var w = ground.CellWeights(x, z);
        float share = 0;
        for (int i = 0; i < 4; i++)
        {
            if (ground.Grounds[i] is { } g && grounds.Contains(g))
                share += w[i];
        }
        return share >= 0.6f;
    }

    /// <summary>The animals' shapes: a bird's body and two wings (0.9 m across), a butterfly's four wing panels (8 cm), a firefly's glow card.</summary>
    private static Mesh Shape(string kind)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            tool.AddVertex(a);
            tool.AddVertex(b);
            tool.AddVertex(c);
        }
        switch (kind)
        {
            case "bird":
                // Body along +Z; each wing a swept triangle out to x = +-0.45 (the shader beats it by |x|).
                Tri(new Vector3(0, 0, 0.22f), new Vector3(-0.06f, 0, -0.18f), new Vector3(0.06f, 0, -0.18f));
                Tri(new Vector3(-0.05f, 0, 0.08f), new Vector3(-0.45f, 0, -0.06f), new Vector3(-0.05f, 0, -0.08f));
                Tri(new Vector3(0.05f, 0, 0.08f), new Vector3(0.05f, 0, -0.08f), new Vector3(0.45f, 0, -0.06f));
                break;
            case "butterfly":
            {
                // The bird's body and swept wings at an eighth of its size: a 12 cm span that beats as a moth or a butterfly does.
                const float k = 0.13f;
                Tri(new Vector3(0, 0, 0.22f) * k, new Vector3(-0.06f, 0, -0.18f) * k, new Vector3(0.06f, 0, -0.18f) * k);
                Tri(new Vector3(-0.05f, 0, 0.12f) * k, new Vector3(-0.45f, 0, 0.1f) * k, new Vector3(-0.05f, 0, -0.14f) * k);
                Tri(new Vector3(0.05f, 0, 0.12f) * k, new Vector3(0.05f, 0, -0.14f) * k, new Vector3(0.45f, 0, 0.1f) * k);
                break;
            }
            default:
                Tri(new Vector3(-0.03f, -0.03f, 0), new Vector3(0.03f, -0.03f, 0), new Vector3(0, 0.03f, 0));
                break;
        }
        tool.GenerateNormals();
        return tool.Commit();
    }

    private static readonly Dictionary<string, Material> Materials = new();

    /// <summary>The flyers' material by how they move: circle (a flock), wander (a loop, wings beating), glow (a blinking light).</summary>
    private static Material Material(string motion)
    {
        if (Materials.TryGetValue(motion, out var cached))
            return cached;
        var material = new ShaderMaterial { Shader = new Shader { Code = FlyerShader } };
        material.SetShaderParameter("kind", motion switch { "circle" => 0, "wander" => 1, _ => 2 });
        Materials[motion] = material;
        return material;
    }

    private const string FlyerShader = @"
shader_type spatial;
render_mode cull_disabled, specular_disabled;
uniform int kind = 0;
uniform bool around_camera = false;
uniform float wrap_r = 30.0;
uniform vec4 region = vec4(0.0, 0.0, 200.0, 200.0);
uniform sampler2D height_tex : filter_linear, repeat_disable;
uniform sampler2D mask_tex : filter_linear, repeat_disable;
varying vec4 seed;
varying float kept;
varying vec2 card;

void vertex() {
    seed = INSTANCE_CUSTOM;
    card = VERTEX.xy / 0.03 + vec2(0.0, 0.33);
    float t = TIME * seed.z * 6.2832 + seed.x * 6.2832;
    vec3 offset;
    vec3 fwd;
    if (kind == 0) {
        // A bird circles its flock's centre, rising and falling a little, banking into the turn.
        offset = vec3(cos(t), 0.12 * sin(t * 2.3 + seed.w * 6.0), sin(t)) * seed.y;
        fwd = normalize(vec3(-sin(t), 0.0, cos(t)));
    } else {
        // A butterfly or a firefly wanders a small loop, never the same twice.
        offset = vec3(sin(t * 1.3 + seed.w * 5.0), 0.35 * sin(t * 2.1 + 1.0), cos(t * 0.9 + seed.w * 3.0)) * seed.y;
        fwd = normalize(vec3(1.3 * cos(t * 1.3 + seed.w * 5.0), 0.0, -0.9 * sin(t * 0.9 + seed.w * 3.0)) + vec3(1e-4));
    }
    vec3 v = VERTEX;
    if (kind < 2) {
        // Wingbeat: each wing turns about the body by an angle growing to its tip; a bird glides between bursts, a butterfly never stops.
        float rate = kind == 0 ? 9.0 : 22.0;
        float beat = sin(TIME * rate + seed.x * 40.0);
        float glide = kind == 0 ? smoothstep(-0.2, 0.6, sin(TIME * 0.7 + seed.w * 9.0)) : 1.0;
        float angle = beat * (kind == 0 ? 0.55 : 1.1) * glide;
        v = vec3(v.x * cos(angle), abs(v.x) * sin(angle), v.z);
    }
    vec3 right = normalize(cross(vec3(0.0, 1.0, 0.0), fwd));
    vec3 up = cross(fwd, right);
    if (kind == 2) {
        // A firefly's card faces the camera.
        right = normalize(INV_VIEW_MATRIX[0].xyz);
        up = normalize(INV_VIEW_MATRIX[1].xyz);
        fwd = cross(right, up);
    }
    float scale = max(length(MODEL_MATRIX[0].xyz), 0.001);
    vec3 anchor = MODEL_MATRIX[3].xyz;
    kept = 1.0;
    if (around_camera) {
        // The anchor wrapped into the square round the camera, stood on the ground there, and kept only over its grounds (and the region).
        vec3 cam = INV_VIEW_MATRIX[3].xyz;
        vec2 p = cam.xz + mod(anchor.xz - cam.xz + wrap_r, 2.0 * wrap_r) - wrap_r;
        vec2 uv = (p - region.xy) / region.zw;
        float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
        kept = inside * step(0.5, texture(mask_tex, uv).r);
        vec3 world = vec3(p.x, texture(height_tex, uv).r + anchor.y, p.y);
        // Fade in and out at the edge of the square, where it wraps.
        float edge = max(abs(p.x - cam.x), abs(p.y - cam.z)) / wrap_r;
        kept *= 1.0 - smoothstep(0.8, 1.0, edge);
        VERTEX = (right * v.x + up * v.y + fwd * v.z) * kept + (world - anchor + offset) / scale;
    } else {
        VERTEX = right * v.x + up * v.y + fwd * v.z + offset / scale;
    }
}

void fragment() {
    if (kind == 0) {
        ALBEDO = vec3(0.05, 0.05, 0.055);
        ROUGHNESS = 0.9;
    } else if (kind == 1) {
        // Whites, yellows and browns, as the meadow's own would be.
        vec3 a = vec3(0.92, 0.9, 0.82), b = vec3(0.95, 0.78, 0.25), c = vec3(0.55, 0.32, 0.12);
        ALBEDO = seed.w < 0.4 ? a : (seed.w < 0.75 ? b : c);
        ROUGHNESS = 0.8;
        BACKLIGHT = ALBEDO * 0.5;
    } else {
        // A firefly: a soft glow that blinks on and off in its own time.
        vec2 p = card;
        float blink = smoothstep(0.25, 0.8, sin(TIME * (0.9 + seed.w) + seed.x * 30.0));
        float d = 1.0 - clamp(length(p), 0.0, 1.0);
        ALBEDO = vec3(0.0);
        EMISSION = vec3(0.75, 1.0, 0.35) * 6.0 * d * d * blink;
        ALPHA = d * blink;
    }
}";
}
