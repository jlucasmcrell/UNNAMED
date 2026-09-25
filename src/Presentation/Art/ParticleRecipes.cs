// UNNAMED Presentation - particle effects built from data (res://Art/vfx_recipes.json): one factory for magic, sparks, smoke, embers and dust (Phase B, B0.7)
// Godot presentation only (D-11): what an effect looks like; when it plays is the caller's (a clip event, a hit, a working)

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// A particle effect as data: a burst or a stream of camera-facing quads showing a texture (a flipbook played at its rate when it has a
/// grid), thrown in a direction with a spread, pulled by gravity, slowed by damping, coloured along a ramp and scaled along a curve over
/// each particle's life, added or blended. <see cref="Build"/> makes the <see cref="GpuParticles3D"/> for one; a burst is fired with
/// <see cref="Fire"/>. Textures load from the asset workspace; without it (or the texture) the effect is not drawn and the caller's
/// greybox stands.
/// </summary>
public sealed class ParticleRecipes
{
    public const string ResourcePath = "res://Art/vfx_recipes.json";

    private readonly Dictionary<string, JsonElement> _recipes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _effects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.Ordinal);
    private readonly string? _root;

    public IReadOnlyCollection<string> Names => _recipes.Keys;

    public ParticleRecipes(string? assetRoot, string json)
    {
        _root = assetRoot;
        using var document = JsonDocument.Parse(json);
        foreach (var recipe in document.RootElement.GetProperty("recipes").EnumerateObject())
            _recipes[recipe.Name] = recipe.Value.Clone();
        if (document.RootElement.TryGetProperty("effects", out var effects))
        {
            foreach (var effect in effects.EnumerateObject().Where(e => e.Value.ValueKind == JsonValueKind.Array))
                _effects[effect.Name] = effect.Value.EnumerateArray().Select(n => n.GetString()!).ToArray();
        }
    }

    /// <summary>The recipes that stand in for a flipbook effect (or a greybox moment's key) in the game; empty when none do.</summary>
    public IReadOnlyList<string> For(string effect) => _effects.GetValueOrDefault(effect) ?? Array.Empty<string>();

    /// <summary>The longest life of the recipes named, in seconds (how long a burst's node must live).</summary>
    public float Lifetime(IEnumerable<string> names) =>
        names.Select(n => _recipes.TryGetValue(n, out var r) ? Number(r, "lifetime", 1f) : 0f).DefaultIfEmpty(0f).Max();

    public static ParticleRecipes Load(string? assetRoot) =>
        new(assetRoot, Godot.FileAccess.FileExists(ResourcePath) ? Godot.FileAccess.GetFileAsString(ResourcePath) : """{ "recipes": {} }""");

    /// <summary>The effect <paramref name="name"/> as a particle node (a stream starts emitting; a burst waits for <see cref="Fire"/>), or null.</summary>
    public GpuParticles3D? Build(string name)
    {
        if (!_recipes.TryGetValue(name, out var r) || Texture(Text(r, "texture")) is not { } texture)
            return null;
        bool burst = Text(r, "mode") == "burst";
        var grid = Numbers(r, "grid") is { Length: 2 } g ? ((int)g[0], (int)g[1]) : (1, 1);
        int frames = (int)Number(r, "frames", grid.Item1 * grid.Item2);
        float lifetime = Number(r, "lifetime", 1f);
        var size = Numbers(r, "size") is { Length: 2 } s ? (s[0], s[1]) : (0.2f, 0.2f);
        var velocity = Numbers(r, "velocity") is { Length: 2 } v ? (v[0], v[1]) : (0f, 0f);
        var damping = Numbers(r, "damping") is { Length: 2 } d ? (d[0], d[1]) : (0f, 0f);
        // How far each quad is turned (degrees): any way for a puff or a spark, nearly upright for a flame.
        var angle = Numbers(r, "angle") is { Length: 2 } a ? (a[0], a[1]) : (-180f, 180f);
        var process = new ParticleProcessMaterial
        {
            Direction = Vector(r, "direction", Vector3.Up), Spread = Number(r, "spread", 0), Gravity = Vector(r, "gravity", Vector3.Zero),
            InitialVelocityMin = velocity.Item1, InitialVelocityMax = velocity.Item2, DampingMin = damping.Item1, DampingMax = damping.Item2,
            // The quad's size is the recipe's largest; each particle takes a share of it.
            ScaleMin = size.Item1 / size.Item2, ScaleMax = 1f, AngleMin = angle.Item1, AngleMax = angle.Item2,
            ColorRamp = Ramp(r), ScaleCurve = Curve(Numbers(r, "size_curve") ?? new[] { 1f, 1f }),
        };
        // Emitted through a box (motes and rain round the camera, mist along a river) rather than from a point.
        if (Numbers(r, "box") is { Length: 3 } box)
        {
            process.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
            process.EmissionBoxExtents = new Vector3(box[0], box[1], box[2]) / 2;
        }
        if (frames > 1)
        {
            // One pass through the book at its own rate across a particle's life, each particle starting somewhere in it.
            float speed = Number(r, "fps", 30) * lifetime / frames;
            process.AnimSpeedMin = speed;
            process.AnimSpeedMax = speed;
            process.AnimOffsetMin = 0;
            process.AnimOffsetMax = 1;
        }
        var material = new StandardMaterial3D
        {
            // Light-emitting effects are unshaded; smoke, mist, ash and rain take the scene's light (they must not glow at night).
            ShadingMode = Flag(r, "lit") ? BaseMaterial3D.ShadingModeEnum.PerPixel : BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = Text(r, "billboard") == "fixed_y" ? BaseMaterial3D.BillboardModeEnum.FixedY : BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = Text(r, "blend") == "add" ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
            AlbedoTexture = texture, VertexColorUseAsAlbedo = true, AlbedoColor = Colors.White * Number(r, "energy", 1f),
            ParticlesAnimHFrames = grid.Item1, ParticlesAnimVFrames = grid.Item2, ParticlesAnimLoop = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        };
        return new GpuParticles3D
        {
            Name = name, Amount = (int)Number(r, "amount", 16), Lifetime = lifetime, OneShot = burst, Explosiveness = burst ? 1f : 0f,
            Emitting = !burst, ProcessMaterial = process,
            // A quad of the recipe's own proportions (a rain streak is thin and tall), at its largest size.
            DrawPass1 = new QuadMesh
            {
                Size = Numbers(r, "quad") is { Length: 2 } q ? new Vector2(q[0], q[1]) * size.Item2 : new Vector2(size.Item2, size.Item2), Material = material,
            },
            LocalCoords = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // A stream starts as if it had been running a whole life (a chimney is already smoking when the camera arrives).
            Preprocess = burst ? 0 : lifetime,
            VisibilityAabb = Numbers(r, "box") is { Length: 3 } b
                ? new Aabb(new Vector3(-b[0], -b[1] - 20, -b[2]), new Vector3(2 * b[0], 2 * b[1] + 40, 2 * b[2]))
                : new Aabb(new Vector3(-8, -8, -8), new Vector3(16, 24, 16)),
        };
    }

    /// <summary>Fire a burst (again).</summary>
    public static void Fire(GpuParticles3D particles)
    {
        particles.Restart();
        particles.Emitting = true;
    }

    private Texture2D? Texture(string? relative)
    {
        if (relative is null || _root is null)
            return null;
        if (_textures.TryGetValue(relative, out var cached))
            return cached;
        Texture2D? texture = null;
        string path = Path.Combine(_root, relative);
        if (File.Exists(path) && Image.LoadFromFile(path) is { } image && !image.IsEmpty())
        {
            image.Convert(Image.Format.Rgba8);
            image.GenerateMipmaps();
            texture = ImageTexture.CreateFromImage(image);
        }
        _textures[relative] = texture;
        return texture;
    }

    private static GradientTexture1D Ramp(JsonElement r)
    {
        var colors = r.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray().Select(x => new Color(x.GetString()!)).ToArray() : new[] { Colors.White, Colors.White };
        var gradient = new Gradient();
        gradient.Offsets = Enumerable.Range(0, colors.Length).Select(i => colors.Length == 1 ? 0f : (float)i / (colors.Length - 1)).ToArray();
        gradient.Colors = colors;
        return new GradientTexture1D { Gradient = gradient };
    }

    private static CurveTexture Curve(float[] points)
    {
        var curve = new Curve { MaxValue = Math.Max(1f, points.Max()) };
        for (int i = 0; i < points.Length; i++)
            curve.AddPoint(new Vector2(points.Length == 1 ? 0 : (float)i / (points.Length - 1), points[i]));
        return new CurveTexture { Curve = curve };
    }

    private static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static float Number(JsonElement e, string name, float fallback) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : fallback;

    private static float[]? Numbers(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => (float)x.GetDouble()).ToArray() : null;

    private static Vector3 Vector(JsonElement e, string name, Vector3 fallback) => Numbers(e, name) is { Length: 3 } n ? new Vector3(n[0], n[1], n[2]) : fallback;
}
