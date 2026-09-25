// UNNAMED Presentation - the ground as the terrain shader and the scatter both read it: which ground where, the worn paths, the macro noise
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Art;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// The region's ground, shared by the terrain's splat shader and the scatter so they agree to the centimetre: the four cells' grounds (the
/// bindings' terrain map) in the shader's four slots round the point where the cells meet, their weights at a point - the borders blended
/// over about twelve metres along a noise-wobbled line - the worn paths (the scatter rules' polylines for this region) as a mask, and a
/// seamless macro noise that breaks up tiling and patches the scatter. Built from the layout and the rules alone: the same for every world.
/// </summary>
public sealed class GroundField
{
    private const int NoiseSize = 512;
    private const int PathSize = 1024;

    private readonly TerrainGrid _terrain;
    private readonly float[] _noise = new float[NoiseSize * NoiseSize * 3];
    private readonly float[] _path = new float[PathSize * PathSize];

    public GroundField(RegionLayout layout, ArtBindings bindings, ScatterRules rules)
    {
        _terrain = layout.Space.Terrain;
        Region = new Rect2(layout.Space.MinXMm / 1000f, layout.Space.MinZMm / 1000f, (layout.Space.MaxXMm - layout.Space.MinXMm) / 1000f,
            (layout.Space.MaxZMm - layout.Space.MinZMm) / 1000f);
        // Where the cells meet: the cell corner nearest the region's middle (a 2 x 2 block of cells; a larger region would need more slots).
        var middle = Region.GetCenter();
        float size = WorldMath.CellSizeMeters;
        Split = new Vector2(MathF.Round(middle.X / size) * size, MathF.Round(middle.Y / size) * size);
        // Slots: (west, south), (west, north), (east, south), (east, north).
        Cells = new[]
        {
            CellKey.OfWorld(Split.X - size / 2, Split.Y - size / 2).ToString(), CellKey.OfWorld(Split.X - size / 2, Split.Y + size / 2).ToString(),
            CellKey.OfWorld(Split.X + size / 2, Split.Y - size / 2).ToString(), CellKey.OfWorld(Split.X + size / 2, Split.Y + size / 2).ToString(),
        };
        Grounds = Cells.Select(c => bindings.Terrain.GetValueOrDefault(c)).ToArray();
        BuildNoise();
        Paths = rules.Paths.GetValueOrDefault(layout.Id) ?? Array.Empty<ScatterPath>();
        BuildPaths(rules.PathWidthM);
    }

    /// <summary>The region in metres (x, z, width, depth).</summary>
    public Rect2 Region { get; }

    /// <summary>Where the four cells meet.</summary>
    public Vector2 Split { get; }

    /// <summary>The four slots' cell keys: (west, south), (west, north), (east, south), (east, north).</summary>
    public string[] Cells { get; }

    /// <summary>Each slot's ground: its world material's ID, or null when its cell has none.</summary>
    public string?[] Grounds { get; }

    public IReadOnlyList<ScatterPath> Paths { get; }

    /// <summary>The macro noise as an RGB texture (three channels at three frequencies), for the shader.</summary>
    public ImageTexture MacroTexture { get; private set; } = null!;

    /// <summary>The worn paths over the region (R8, about 0.2 m a texel), for the shader.</summary>
    public ImageTexture PathTexture { get; private set; } = null!;

    public float Height(float x, float z) =>
        _terrain.HeightAtMm((long)(Math.Clamp(x, Region.Position.X, Region.End.X) * 1000), (long)(Math.Clamp(z, Region.Position.Y, Region.End.Y) * 1000)) / 1000f;

    /// <summary>Rise per metre across a metre.</summary>
    public float Slope(float x, float z)
    {
        float dx = Height(x + 0.5f, z) - Height(x - 0.5f, z), dz = Height(x, z + 0.5f) - Height(x, z - 0.5f);
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>The macro noise at (u, v) in tiles (it repeats every whole unit), one of its three channels, bilinear.</summary>
    public float MacroAt(float u, float v, int channel)
    {
        float x = (u - MathF.Floor(u)) * NoiseSize - 0.5f, y = (v - MathF.Floor(v)) * NoiseSize - 0.5f;
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        float P(int px, int py) => _noise[((((py % NoiseSize) + NoiseSize) % NoiseSize) * NoiseSize + ((px % NoiseSize) + NoiseSize) % NoiseSize) * 3 + channel];
        return Mathf.Lerp(Mathf.Lerp(P(x0, y0), P(x0 + 1, y0), fx), Mathf.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), fx), fy);
    }

    /// <summary>The four slots' weights at a point, as the terrain shader blends them (the same noise, the same border).</summary>
    public Vector4 CellWeights(float x, float z)
    {
        float edge = (MacroAt(x / 37f, z / 37f, 0) - 0.5f) * 14f + (MacroAt(x / 9f, z / 9f, 1) - 0.5f) * 4f;
        float edge2 = (MacroAt(x / 41f + 0.3f, z / 41f + 0.6f, 0) - 0.5f) * 14f + (MacroAt(x / 11f + 0.5f, z / 11f, 1) - 0.5f) * 4f;
        float wx = SmoothStep(-6f, 6f, x - Split.X + edge), wz = SmoothStep(-6f, 6f, z - Split.Y + edge2);
        return new Vector4((1 - wx) * (1 - wz), (1 - wx) * wz, wx * (1 - wz), wx * wz);
    }

    /// <summary>How worn the ground is at a point: 1 on a path, fraying to 0 at its edge.</summary>
    public float PathAt(float x, float z)
    {
        int px = Math.Clamp((int)((x - Region.Position.X) / Region.Size.X * PathSize), 0, PathSize - 1);
        int py = Math.Clamp((int)((z - Region.Position.Y) / Region.Size.Y * PathSize), 0, PathSize - 1);
        return _path[py * PathSize + px];
    }

    public static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    private void BuildNoise()
    {
        var channels = new[] { (Seed: 11, Frequency: 0.008f), (Seed: 23, Frequency: 0.012f), (Seed: 37, Frequency: 0.02f) };
        var bytes = new byte[NoiseSize * NoiseSize * 3];
        for (int c = 0; c < 3; c++)
        {
            var noise = new FastNoiseLite
            {
                Seed = channels[c].Seed, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = channels[c].Frequency,
                FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 4,
            };
            var image = noise.GetSeamlessImage(NoiseSize, NoiseSize);
            if (image.GetFormat() != Image.Format.L8)
                image.Convert(Image.Format.L8);
            byte[] data = image.GetData();
            for (int i = 0; i < NoiseSize * NoiseSize; i++)
            {
                bytes[i * 3 + c] = data[i];
                _noise[i * 3 + c] = data[i] / 255f;
            }
        }
        var macro = Image.CreateFromData(NoiseSize, NoiseSize, false, Image.Format.Rgb8, bytes);
        macro.GenerateMipmaps();
        MacroTexture = ImageTexture.CreateFromImage(macro);
    }

    /// <summary>About <paramref name="width"/> metres wide, its edge wandering and frayed by the macro noise.</summary>
    private void BuildPaths(float width)
    {
        float metresPerPixel = Region.Size.X / PathSize;
        float half = width * 0.3f;
        foreach (var path in Paths)
        {
            var line = path.Points.Select(p => new Vector2(p.X, p.Z)).ToArray();
            float minX = line.Min(p => p.X) - 4, maxX = line.Max(p => p.X) + 4, minZ = line.Min(p => p.Y) - 4, maxZ = line.Max(p => p.Y) + 4;
            int fromY = Math.Max(0, (int)((minZ - Region.Position.Y) / metresPerPixel)), toY = Math.Min(PathSize - 1, (int)((maxZ - Region.Position.Y) / metresPerPixel));
            int fromX = Math.Max(0, (int)((minX - Region.Position.X) / metresPerPixel)), toX = Math.Min(PathSize - 1, (int)((maxX - Region.Position.X) / metresPerPixel));
            for (int py = fromY; py <= toY; py++)
            for (int px = fromX; px <= toX; px++)
            {
                var at = new Vector2(Region.Position.X + (px + 0.5f) * metresPerPixel, Region.Position.Y + (py + 0.5f) * metresPerPixel);
                float d = float.MaxValue;
                for (int i = 0; i + 1 < line.Length; i++)
                    d = Math.Min(d, SegmentDistance(at, line[i], line[i + 1]));
                float wobble = (MacroAt(at.X / 13f, at.Y / 13f, 2) - 0.5f) * 1.2f + (MacroAt(at.X / 3f, at.Y / 3f, 1) - 0.5f) * 0.6f;
                float v = 1f - SmoothStep(half, half + 0.8f, d + wobble);
                int i2 = py * PathSize + px;
                _path[i2] = Math.Max(_path[i2], v);
            }
        }
        var bytes = new byte[PathSize * PathSize];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)Math.Clamp(_path[i] * 255f + 0.5f, 0, 255);
        PathTexture = ImageTexture.CreateFromImage(Image.CreateFromData(PathSize, PathSize, false, Image.Format.R8, bytes));
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Math.Clamp((p - a).Dot(ab) / Math.Max(ab.LengthSquared(), 1e-6f), 0f, 1f);
        return p.DistanceTo(a + ab * t);
    }

    /// <summary>
    /// The terrain's splat material: the four grounds' maps (through the library's world-material path) laid in world space at each record's
    /// tile size, a second sample at another scale and angle mixed in by the macro noise so the tiling does not show, the cells' borders
    /// blended with the height of each ground's texels, a macro brightness drift, and the worn paths. Null when any of the four grounds
    /// cannot be drawn (the terrain then wears each cell's own material, or greybox).
    /// </summary>
    public ShaderMaterial? TerrainMaterial(ArtLibrary art)
    {
        var maps = Grounds.Select(g => g is null ? null : art.WorldMaps(g)).ToArray();
        if (maps.Any(m => m is null))
            return null;
        var shader = new ShaderMaterial { Shader = new Shader { Code = TerrainShader } };
        for (int i = 0; i < 4; i++)
        {
            shader.SetShaderParameter($"alb{i}", maps[i]!.Albedo);
            if (maps[i]!.Normal is { } normal)
                shader.SetShaderParameter($"nrm{i}", normal);
            if (maps[i]!.Orm is { } orm)
                shader.SetShaderParameter($"orm{i}", orm);
        }
        shader.SetShaderParameter("tiles", new Vector4(maps[0]!.TileSizeM, maps[1]!.TileSizeM, maps[2]!.TileSizeM, maps[3]!.TileSizeM));
        shader.SetShaderParameter("strengths", new Vector4(maps[0]!.NormalStrength, maps[1]!.NormalStrength, maps[2]!.NormalStrength, maps[3]!.NormalStrength));
        shader.SetShaderParameter("split", Split);
        shader.SetShaderParameter("macro_noise", MacroTexture);
        shader.SetShaderParameter("path_mask", PathTexture);
        shader.SetShaderParameter("region", new Vector4(Region.Position.X, Region.Position.Y, Region.Size.X, Region.Size.Y));
        shader.SetShaderParameter("path_strength", 1f);
        return shader;
    }

    /// <summary>
    /// A world material laid triplanar in world space for rock faces (the ravine and cliffs): at twice its record's tile size - a cliff, not a
    /// wall - blended sharply between the faces, a little darker. Null when the material cannot be drawn.
    /// </summary>
    public static Material? Cliff(ArtLibrary art, string? id)
    {
        if (id is null || art.WorldMaps(id) is not { } maps)
            return null;
        return new OrmMaterial3D
        {
            AlbedoTexture = maps.Albedo, NormalEnabled = maps.Normal is not null, NormalTexture = maps.Normal, NormalScale = maps.NormalStrength,
            OrmTexture = maps.Orm, Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1TriplanarSharpness = 4f, Uv1Scale = Vector3.One / (maps.TileSizeM * 2f),
            AlbedoColor = new Color(0.82f, 0.80f, 0.78f), TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
    }

    private const string TerrainShader = @"
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D alb0 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D alb1 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D alb2 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D alb3 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D nrm0 : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D nrm1 : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D nrm2 : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D nrm3 : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D orm0 : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D orm1 : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D orm2 : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D orm3 : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D macro_noise : filter_linear_mipmap, repeat_enable;
uniform sampler2D path_mask : filter_linear, repeat_disable;
uniform vec4 tiles = vec4(4.0, 4.0, 4.0, 4.0);
uniform vec4 strengths = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec2 split = vec2(100.0, 100.0);
uniform vec4 region = vec4(0.0, 0.0, 200.0, 200.0);
uniform float path_strength = 1.0;

varying vec3 wpos;
varying vec3 wnrm;

void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

vec2 rot(vec2 p) { return mat2(vec2(0.7986, 0.6018), vec2(-0.6018, 0.7986)) * p; }

void layer(sampler2D a, sampler2D n, sampler2D o, vec2 p, float tile, float strength, float mixb, out vec3 alb, out vec3 nt, out vec3 orm) {
    vec2 uv1 = p / tile;
    vec2 uv2 = rot(p) / (tile * 2.7) + vec2(0.37, 0.71);
    alb = mix(texture(a, uv1).rgb, texture(a, uv2).rgb, mixb);
    nt = mix(texture(n, uv1).rgb, texture(n, uv2).rgb, mixb) * 2.0 - 1.0;
    nt.xy *= strength;
    orm = mix(texture(o, uv1).rgb, texture(o, uv2).rgb, mixb);
}

void fragment() {
    vec2 p = wpos.xz;
    vec3 m1 = texture(macro_noise, p / 37.0).rgb;
    vec3 m2 = texture(macro_noise, p / 9.0).rgb;
    vec3 m3 = texture(macro_noise, p / 41.0 + vec2(0.3, 0.6)).rgb;
    vec3 m4 = texture(macro_noise, p / 11.0 + vec2(0.5, 0.0)).rgb;
    float ex = (m1.r - 0.5) * 14.0 + (m2.g - 0.5) * 4.0;
    float ez = (m3.r - 0.5) * 14.0 + (m4.g - 0.5) * 4.0;
    float wx = smoothstep(-6.0, 6.0, p.x - split.x + ex);
    float wz = smoothstep(-6.0, 6.0, p.y - split.y + ez);
    vec4 w = vec4((1.0 - wx) * (1.0 - wz), (1.0 - wx) * wz, wx * (1.0 - wz), wx * wz);

    float mixb = smoothstep(0.42, 0.58, texture(macro_noise, p / 23.0).b);
    vec3 a0; vec3 n0; vec3 o0; layer(alb0, nrm0, orm0, p, tiles.x, strengths.x, mixb, a0, n0, o0);
    vec3 a1; vec3 n1; vec3 o1; layer(alb1, nrm1, orm1, p, tiles.y, strengths.y, mixb, a1, n1, o1);
    vec3 a2; vec3 n2; vec3 o2; layer(alb2, nrm2, orm2, p, tiles.z, strengths.z, mixb, a2, n2, o2);
    vec3 a3; vec3 n3; vec3 o3; layer(alb3, nrm3, orm3, p, tiles.w, strengths.w, mixb, a3, n3, o3);

    // Height-weighted blend: where two grounds meet, the higher (brighter, less occluded) texel of each wins, not a cross-fade.
    vec4 h = vec4(dot(a0, vec3(0.333)) * o0.r, dot(a1, vec3(0.333)) * o1.r, dot(a2, vec3(0.333)) * o2.r, dot(a3, vec3(0.333)) * o3.r);
    vec4 hw = w + h * 0.6 * step(0.001, w);
    float top = max(max(hw.x, hw.y), max(hw.z, hw.w));
    vec4 b = max(hw - top + 0.18, 0.0) * step(0.001, w);
    b /= max(b.x + b.y + b.z + b.w, 1e-4);

    vec3 alb = a0 * b.x + a1 * b.y + a2 * b.z + a3 * b.w;
    vec3 nt = n0 * b.x + n1 * b.y + n2 * b.z + n3 * b.w;
    vec3 orm = o0 * b.x + o1 * b.y + o2 * b.z + o3 * b.w;

    // A worn path: the dirt darkened and packed smooth, a little of the mud's wet in it.
    float pm = texture(path_mask, (p - region.xy) / region.zw).r * path_strength;
    vec3 path_alb = mix(a1, a2, 0.45) * 0.66;
    alb = mix(alb, path_alb, pm);
    nt = mix(nt, mix(n1, vec3(0.0, 0.0, 1.0), 0.6), pm);
    orm = mix(orm, vec3(o1.r, o1.g * 0.75, 0.0), pm);

    alb *= mix(0.88, 1.12, texture(macro_noise, p / 53.0 + vec2(0.13, 0.29)).g);

    vec3 N = normalize(wnrm);
    vec3 T = normalize(vec3(1.0, 0.0, 0.0) - N * N.x);
    vec3 B = cross(N, T);
    vec3 nw = normalize(T * nt.x + B * nt.y + N * max(nt.z, 0.05));
    ALBEDO = alb;
    NORMAL = normalize((VIEW_MATRIX * vec4(nw, 0.0)).xyz);
    ROUGHNESS = orm.g;
    METALLIC = 0.0;
    AO = orm.r;
    AO_LIGHT_AFFECT = 0.25;
}
";
}
