// UNNAMED Presentation - the visual audit's A/B: the audit's in-world shots under cumulative look layers (renderer, textured ground, ground scatter)
// Godot presentation only, a harness: every layer is built here, for this run, and nothing it does reaches the game's own look or state (D-11)

using System.Text.Json;
using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>--visual-audit-ab dir</c>: the game booted as <see cref="VisualAudit"/> boots it, and the same in-world shots (the same eyes and
/// targets) taken under four cumulative variants: V0 the game as it draws today; V1 the renderer (mipmaps on every loaded texture, MSAA
/// with a screen-space or temporal AA, 16x anisotropy, SSAO, SSIL, SDFGI, a light glow, an overcast sky, AgX, volumetric fog, soft
/// shadows, a warm light in the lodge); V2 V1 plus the staged seamless world materials on the terrain cells and the ravine; V3 V2 plus a
/// procedural ground scatter (grass cards, stones, leaf litter) and a trodden path from the lodge door. Each picture is a 1920x1080 PNG
/// with the median frame time of 60 frames. Tuning switches (harness only): <c>--ab-variants 0,1,2,3</c>, <c>--ab-shots name,name</c>
/// (substring match), <c>--ab-aa taa|fxaa|smaa|none</c>.
/// </summary>
public sealed class VisualAuditAB
{
    private const int WarmupFrames = 150;
    private const int SettleFrames = 90;
    private const int SettleFramesLit = 300;   // SDFGI cascades, volumetric fog and TAA converge over frames after the camera jumps
    private const int MeasureFrames = 60;
    private const float CellSplit = 100f;      // the four 100 m cells meet at (100, 100)

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly string[] Names = { "V0", "V1", "V2", "V3" };

    private readonly Node3D _root;
    private readonly GameSession _session;
    private readonly Art.ArtBindings _bindings;
    private readonly CameraRig _rig;
    private readonly string _raw;
    private readonly string _staging;
    private readonly List<VisualAudit.Shot> _shots;
    private readonly List<int> _variants;
    private readonly string _aa;
    private readonly Rid _viewport;
    private readonly List<Dictionary<string, object?>> _results = new();
    private readonly Dictionary<string, object?> _settings = new();
    private readonly List<double> _gpu = new(), _cpu = new(), _wall = new();
    private Camera3D? _camera;
    private int _applied = 0;
    private int _frame, _vi, _si, _wait;
    private bool _measuring;
    private ulong _last;
    private Image? _macro;
    private Image? _path;

    public VisualAuditAB(Node3D root, GameSession session, Art.ArtLibrary art, Art.ArtBindings bindings, CameraRig rig, string directory)
    {
        _root = root;
        _session = session;
        _bindings = bindings;
        _rig = rig;
        Directory = directory;
        _raw = Path.Combine(directory, "raw");
        System.IO.Directory.CreateDirectory(_raw);
        _staging = Path.Combine(art.Root ?? string.Empty, "_staging", "materials");
        var args = OS.GetCmdlineUserArgs();
        string? Arg(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        _variants = (Arg("--ab-variants") ?? "0,1,2,3").Split(',').Select(int.Parse).OrderBy(v => v).ToList();
        var only = Arg("--ab-shots")?.Split(',');
        _shots = VisualAudit.WorldShots.Where(s => only is null || only.Any(o => s.Name.Contains(o, StringComparison.Ordinal))).ToList();
        _aa = Arg("--ab-aa") ?? "taa";
        DisplayServer.WindowSetSize(new Vector2I(1920, 1080));
        // Measured, not paced: no vsync, no cap.
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        _viewport = root.GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(_viewport, true);
    }

    public string Directory { get; }

    /// <summary>Advance one frame: null while working, then "done" or "failed".</summary>
    public string? Update()
    {
        _frame++;
        try
        {
            if (_frame < WarmupFrames)
                return null;
            if (_vi >= _variants.Count)
            {
                Write("ab_summary.json", new Dictionary<string, object?>
                {
                    ["gpu"] = RenderingServer.GetVideoAdapterName(),
                    ["driver"] = RenderingServer.GetCurrentRenderingDriverName(),
                    ["method"] = RenderingServer.GetCurrentRenderingMethod(),
                    ["window"] = new[] { DisplayServer.WindowGetSize().X, DisplayServer.WindowGetSize().Y },
                    ["aa"] = _aa,
                    ["layers"] = _settings,
                    ["results"] = _results,
                });
                GD.Print($"UNNAMED visual audit A/B written to {Directory}");
                return "done";
            }
            int variant = _variants[_vi];
            while (_applied < variant)
                Apply(++_applied);
            Step(variant, _shots[_si]);
            return null;
        }
        catch (Exception e)
        {
            GD.PushError($"UNNAMED visual audit A/B failed: {e}");
            return "failed";
        }
    }

    private void Step(int variant, VisualAudit.Shot shot)
    {
        if (_camera is null)
        {
            _camera = new Camera3D { Name = "AuditCamera", Fov = _rig.Camera.Fov, Near = _rig.Camera.Near, Far = _rig.Camera.Far,
                Attributes = _rig.Camera.Attributes, Environment = _rig.Camera.Environment };
            _root.AddChild(_camera);
        }
        foreach (var layer in _root.FindChildren("*", nameof(CanvasLayer), true, false).Cast<CanvasLayer>())
            layer.Visible = false;
        var eye = Ground(shot.Eye);
        var target = Ground(shot.Target);
        _camera.Current = true;
        _camera.LookAtFromPosition(eye, target, Vector3.Up);
        ulong now = Time.GetTicksUsec();
        if (!_measuring)
        {
            if (++_wait < (variant == 0 ? SettleFrames : SettleFramesLit))
                return;
            var image = _root.GetViewport().GetTexture().GetImage();
            image.SavePng(Path.Combine(_raw, $"{shot.Name}__{Names[variant]}.png"));
            _measuring = true;
            _wait = 0;
            _gpu.Clear();
            _cpu.Clear();
            _wall.Clear();
            _last = now;
            return;
        }
        // The frame after the read-back stalls on it: skipped.
        if (++_wait > 2)
        {
            _gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewport));
            _cpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewport));
            _wall.Add((now - _last) / 1000.0);
        }
        _last = now;
        if (_gpu.Count < MeasureFrames)
            return;
        _results.Add(new Dictionary<string, object?>
        {
            ["shot"] = shot.Name,
            ["variant"] = Names[variant],
            ["png"] = $"{shot.Name}__{Names[variant]}.png",
            ["frames"] = _gpu.Count,
            ["gpu_ms_median"] = Math.Round(Median(_gpu), 3),
            ["cpu_render_ms_median"] = Math.Round(Median(_cpu), 3),
            ["frame_ms_median"] = Math.Round(Median(_wall), 3),
            ["frame_ms_p90"] = Math.Round(_wall.OrderBy(w => w).ElementAt((int)(_wall.Count * 0.9)), 3),
        });
        GD.Print($"A/B {Names[variant]} {shot.Name}: gpu {Median(_gpu):0.00} ms, frame {Median(_wall):0.00} ms");
        _measuring = false;
        _wait = 0;
        if (++_si >= _shots.Count)
        {
            _si = 0;
            _vi++;
        }
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }

    private Vector3 Ground((float X, float H, float Z) at) => new(at.X, Height(at.X, at.Z) + at.H, at.Z);

    private float Height(float x, float z) =>
        _session.Setup.Layout.Space.Terrain.HeightAtMm((long)(Math.Clamp(x, 0, 200) * 1000), (long)(Math.Clamp(z, 0, 200) * 1000)) / 1000f;

    private void Apply(int layer)
    {
        switch (layer)
        {
            case 1:
                _settings["V1"] = Renderer();
                break;
            case 2:
                _settings["V2"] = TexturedGround();
                break;
            case 3:
                _settings["V3"] = Scatter();
                break;
        }
    }

    // ── V1: the renderer ────────────────────────────────────────────────────

    private Dictionary<string, object?> Renderer()
    {
        var s = new Dictionary<string, object?>();
        s["mipmaps"] = MipmapEverything();

        var viewport = _root.GetViewport();
        viewport.Msaa3D = Viewport.Msaa.Msaa4X;
        viewport.UseTaa = _aa == "taa";
        viewport.ScreenSpaceAA = _aa switch { "fxaa" => Viewport.ScreenSpaceAAEnum.Fxaa, "smaa" => Viewport.ScreenSpaceAAEnum.Smaa, _ => Viewport.ScreenSpaceAAEnum.Disabled };
        viewport.AnisotropicFilteringLevel = Viewport.AnisotropicFiltering.Anisotropy16X;
        viewport.UseDebanding = true;
        RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftHigh);
        RenderingServer.PositionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftHigh);
        RenderingServer.DirectionalShadowAtlasSetSize(4096, true);
        s["viewport"] = new Dictionary<string, object?>
        {
            ["msaa_3d"] = "4x", ["use_taa"] = viewport.UseTaa, ["screen_space_aa"] = viewport.ScreenSpaceAA.ToString(), ["anisotropic"] = "16x",
            ["debanding"] = true, ["soft_shadow_quality"] = "SoftHigh (directional and positional)", ["directional_shadow_atlas"] = "4096, 16-bit",
        };

        var sun = _root.FindChildren("*", nameof(DirectionalLight3D), true, false).Cast<DirectionalLight3D>().First();
        sun.LightEnergy = 1.35f;
        sun.LightColor = new Color(1.0f, 0.95f, 0.86f);
        sun.LightAngularDistance = 1.0f;
        sun.ShadowBlur = 1.0f;
        sun.ShadowBias = 0.03f;
        sun.ShadowNormalBias = 1.0f;
        sun.DirectionalShadowMaxDistance = 90f;
        sun.DirectionalShadowBlendSplits = true;
        s["sun"] = new Dictionary<string, object?>
        {
            ["rotation_deg"] = "unchanged (-48, 35, 0)", ["energy"] = sun.LightEnergy, ["color"] = sun.LightColor.ToHtml(false), ["angular_distance_deg"] = sun.LightAngularDistance,
            ["shadow_blur"] = sun.ShadowBlur, ["shadow_max_distance_m"] = sun.DirectionalShadowMaxDistance, ["blend_splits"] = true,
        };

        var world = _root.FindChildren("*", nameof(WorldEnvironment), true, false).Cast<WorldEnvironment>().First();
        var sky = new ShaderMaterial { Shader = new Shader { Code = SkyShader } };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky, RadianceSize = Sky.RadianceSizeEnum.Size256, ProcessMode = Sky.ProcessModeEnum.Automatic },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 1f,
            AmbientLightEnergy = 1f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 1.15f,
            TonemapAgxContrast = 1.15f,
            SsaoEnabled = true, SsaoRadius = 1.4f, SsaoIntensity = 2.2f, SsaoPower = 1.6f, SsaoDetail = 0.6f, SsaoHorizon = 0.06f, SsaoSharpness = 0.98f,
            SsaoLightAffect = 0.15f, SsaoAOChannelAffect = 0.5f,
            SsilEnabled = true, SsilRadius = 4f, SsilIntensity = 0.9f, SsilSharpness = 0.98f, SsilNormalRejection = 1f,
            SdfgiEnabled = true, SdfgiCascades = 6, SdfgiMinCellSize = 0.2f, SdfgiUseOcclusion = true, SdfgiReadSkyLight = true, SdfgiBounceFeedback = 0.5f,
            SdfgiEnergy = 1.0f, SdfgiNormalBias = 1.1f, SdfgiProbeBias = 1.1f, SdfgiYScale = Godot.Environment.SdfgiyScale.Scale75Percent,
            GlowEnabled = true, GlowIntensity = 0.35f, GlowStrength = 1f, GlowBloom = 0.02f, GlowHdrThreshold = 1.1f, GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
            FogEnabled = true, FogMode = Godot.Environment.FogModeEnum.Exponential, FogDensity = 0.0016f, FogAerialPerspective = 0.75f, FogSkyAffect = 0f,
            FogLightColor = new Color(0.66f, 0.68f, 0.70f), FogSunScatter = 0.05f,
            VolumetricFogEnabled = true, VolumetricFogDensity = 0.0025f, VolumetricFogAlbedo = new Color(0.86f, 0.87f, 0.9f), VolumetricFogAnisotropy = 0.35f,
            VolumetricFogLength = 96f, VolumetricFogDetailSpread = 2f, VolumetricFogGIInject = 0.6f, VolumetricFogAmbientInject = 0.35f, VolumetricFogSkyAffect = 0f,
            VolumetricFogTemporalReprojectionEnabled = true,
        };
        world.Environment = env;
        if (_camera is not null)
            _camera.Environment = null;
        s["environment"] = new Dictionary<string, object?>
        {
            ["sky"] = "sky shader: zenith-to-horizon gradient, an FBM cloud deck projected on a plane (overcast, 86 % cover, lit/dark cloud colours, sun-side brightening), horizon haze band",
            ["ambient"] = "sky, energy 1", ["reflections"] = "sky",
            ["tonemap"] = $"AgX, exposure {env.TonemapExposure}, contrast {env.TonemapAgxContrast}",
            ["ssao"] = $"radius {env.SsaoRadius}, intensity {env.SsaoIntensity}, power {env.SsaoPower}, detail {env.SsaoDetail}, light affect {env.SsaoLightAffect}",
            ["ssil"] = $"radius {env.SsilRadius}, intensity {env.SsilIntensity}",
            ["sdfgi"] = $"{env.SdfgiCascades} cascades, min cell {env.SdfgiMinCellSize} m, occlusion on, sky light, bounce feedback {env.SdfgiBounceFeedback}, energy {env.SdfgiEnergy}, y scale 75 %",
            ["glow"] = $"softlight, intensity {env.GlowIntensity}, bloom {env.GlowBloom}, HDR threshold {env.GlowHdrThreshold}",
            ["fog"] = $"exponential depth fog {env.FogDensity} with aerial perspective {env.FogAerialPerspective}",
            ["volumetric_fog"] = $"density {env.VolumetricFogDensity}, anisotropy {env.VolumetricFogAnisotropy}, length {env.VolumetricFogLength} m, GI inject {env.VolumetricFogGIInject}",
        };

        // The lodge lit from within: one warm lamp under the ridge, as a lived-in hall would have (its hearth or a hanging lantern).
        float floor = Height(44f, 128f);
        var lamp = new OmniLight3D
        {
            Name = "AB_LodgeLamp", Position = new Vector3(47.4f, floor + 2.6f, 127.2f), LightColor = new Color(1f, 0.80f, 0.60f), LightEnergy = 1.8f,
            OmniRange = 12f, OmniAttenuation = 1.2f, ShadowEnabled = true, LightSize = 0.25f, LightVolumetricFogEnergy = 0.4f,
        };
        _root.AddChild(lamp);
        s["lodge_light"] = new Dictionary<string, object?>
        {
            ["type"] = "OmniLight3D", ["position"] = new[] { lamp.Position.X, lamp.Position.Y, lamp.Position.Z }, ["color"] = lamp.LightColor.ToHtml(false),
            ["energy"] = lamp.LightEnergy, ["range_m"] = lamp.OmniRange, ["shadows"] = "on, light size 0.25 m",
        };
        return s;
    }

    /// <summary>
    /// Every texture of every material drawn in the scene: mipmaps generated where it has none (the runtime glTF path makes none), and
    /// each material's filter set to anisotropic. Also what the textures cost now, with mipmaps, and with VRAM block compression.
    /// </summary>
    private Dictionary<string, object?> MipmapEverything()
    {
        var textures = new Dictionary<ulong, ImageTexture>();
        var materials = new HashSet<ulong>();
        int materialCount = 0;
        foreach (var geometry in _root.FindChildren("*", nameof(GeometryInstance3D), true, false).Cast<GeometryInstance3D>())
        {
            foreach (var material in MaterialsOf(geometry).OfType<BaseMaterial3D>())
            {
                if (!materials.Add(material.GetInstanceId()))
                    continue;
                materialCount++;
                material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
                for (int p = 0; p < (int)BaseMaterial3D.TextureParam.Max; p++)
                {
                    if (material.GetTexture((BaseMaterial3D.TextureParam)p) is ImageTexture texture)
                        textures.TryAdd(texture.GetInstanceId(), texture);
                }
            }
        }
        long before = 0, withMips = 0, compressed = 0;
        int made = 0;
        ulong started = Time.GetTicksMsec();
        foreach (var texture in textures.Values)
        {
            var image = texture.GetImage();
            if (image is null || image.IsEmpty())
                continue;
            long pixels = (long)image.GetWidth() * image.GetHeight();
            // A 3-channel image is uploaded as RGBA8 (4 bytes a pixel) on Vulkan.
            before += pixels * 4 * (image.HasMipmaps() ? 4 : 3) / 3;
            if (!image.HasMipmaps())
            {
                image.GenerateMipmaps();
                texture.SetImage(image);
                made++;
            }
            withMips += pixels * 4 * 4 / 3;
            // BC1 (half a byte a pixel) for opaque colour, BC7/BC5 (a byte a pixel) for colour with alpha and for normal and ORM data.
            bool alpha = image.DetectAlpha() != Image.AlphaMode.None;
            bool colour = !texture.ResourceName.Contains("normal", StringComparison.OrdinalIgnoreCase) && !texture.ResourceName.Contains("orm", StringComparison.OrdinalIgnoreCase);
            compressed += pixels * (colour && !alpha ? 1 : 2) * 4 / 3 / 2;
        }
        return new Dictionary<string, object?>
        {
            ["materials"] = materialCount,
            ["textures"] = textures.Count,
            ["mipmaps_generated"] = made,
            ["generation_ms"] = Time.GetTicksMsec() - started,
            ["vram_mb_as_loaded"] = Math.Round(before / 1048576.0, 1),
            ["vram_mb_with_mipmaps_rgba8"] = Math.Round(withMips / 1048576.0, 1),
            ["vram_mb_with_mipmaps_bc1_bc7"] = Math.Round(compressed / 1048576.0, 1),
            ["note"] = "not compressed in this run: block compression is an import-time step (or a slow Image.Compress at load); it would cut texture memory "
                + "about 4-8x and sample faster, with a small quality cost on BC1 colour maps",
        };
    }

    private static IEnumerable<Material> MaterialsOf(GeometryInstance3D geometry)
    {
        if (geometry.MaterialOverride is { } over)
            yield return over;
        if (geometry.MaterialOverlay is { } overlay)
            yield return overlay;
        if (geometry is MeshInstance3D { Mesh: { } mesh } instance)
        {
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                if (instance.GetSurfaceOverrideMaterial(s) is { } surface)
                    yield return surface;
                if (mesh.SurfaceGetMaterial(s) is { } own)
                    yield return own;
            }
        }
        if (geometry is MultiMeshInstance3D { Multimesh.Mesh: { } multi })
        {
            for (int s = 0; s < multi.GetSurfaceCount(); s++)
            {
                if (multi.SurfaceGetMaterial(s) is { } own)
                    yield return own;
            }
        }
    }

    private const string SkyShader = @"
shader_type sky;
uniform vec3 zenith : source_color = vec3(0.46, 0.49, 0.53);
uniform vec3 horizon : source_color = vec3(0.66, 0.68, 0.70);
uniform vec3 ground : source_color = vec3(0.33, 0.33, 0.32);
uniform vec3 cloud_lit : source_color = vec3(0.74, 0.74, 0.75);
uniform vec3 cloud_dark : source_color = vec3(0.34, 0.36, 0.40);
uniform float coverage = 0.86;
uniform float cloud_scale = 0.9;
uniform float energy = 1.0;

float hash(vec2 p) { vec3 q = fract(vec3(p.xyx) * 0.1031); q += dot(q, q.yzx + 33.33); return fract((q.x + q.y) * q.z); }
float noise(vec2 p) {
    vec2 i = floor(p); vec2 f = fract(p); vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
float fbm(vec2 p) {
    float v = 0.0; float a = 0.5; mat2 r = mat2(vec2(0.8, -0.6), vec2(0.6, 0.8));
    for (int i = 0; i < 6; i++) { v += a * noise(p); p = r * p * 2.03 + vec2(1.7, 9.2); a *= 0.5; }
    return v;
}
void sky() {
    vec3 d = EYEDIR;
    float h = d.y;
    vec3 col = mix(horizon, zenith, pow(clamp(h, 0.0, 1.0), 0.45));
    if (h > 0.0) {
        vec2 uv = d.xz / (h + 0.12) * cloud_scale;
        float n = fbm(uv + vec2(3.1, 7.7));
        float cov = smoothstep(1.0 - coverage - 0.15, 1.0 - coverage + 0.35, n);
        float thick = smoothstep(0.35, 0.85, fbm(uv * 1.7 + vec2(11.0, 2.0)));
        vec3 cc = mix(cloud_lit, cloud_dark, thick * 0.85);
        if (LIGHT0_ENABLED) {
            float sun = pow(max(dot(d, LIGHT0_DIRECTION), 0.0), 6.0);
            cc += LIGHT0_COLOR * sun * 0.2 * (1.0 - thick);
        }
        col = mix(col, cc, cov * smoothstep(0.0, 0.25, h));
    } else {
        col = mix(horizon, ground, smoothstep(0.0, 0.2, -h));
    }
    col = mix(col, horizon, exp(-abs(h) * 14.0) * 0.6);
    COLOR = col * energy;
}
";

    // ── V2: textured ground ─────────────────────────────────────────────────

    private Dictionary<string, object?> TexturedGround()
    {
        var s = new Dictionary<string, object?>();
        var terrain = _root.GetNode<MeshInstance3D>("Hollow/Terrain");
        // The four cells in the shader's slots: (west, south), (west, north), (east, south), (east, north).
        string[] cells = { UNNAMED.World.CellKey.OfWorld(50, 50).ToString(), UNNAMED.World.CellKey.OfWorld(50, 150).ToString(),
            UNNAMED.World.CellKey.OfWorld(150, 50).ToString(), UNNAMED.World.CellKey.OfWorld(150, 150).ToString() };
        var shader = new ShaderMaterial { Shader = new Shader { Code = TerrainShader } };
        var tiles = new float[4];
        var assigned = new Dictionary<string, object?>();
        for (int i = 0; i < 4; i++)
        {
            string id = _bindings.Terrain[cells[i]];
            var (albedo, normal, orm, tile, strength) = Staged(id);
            shader.SetShaderParameter($"alb{i}", albedo);
            shader.SetShaderParameter($"nrm{i}", normal);
            shader.SetShaderParameter($"orm{i}", orm);
            tiles[i] = tile;
            assigned[cells[i]] = $"{id} (tile {tile} m, normal strength {strength})";
        }
        shader.SetShaderParameter("tiles", new Vector4(tiles[0], tiles[1], tiles[2], tiles[3]));
        shader.SetShaderParameter("split", new Vector2(CellSplit, CellSplit));
        shader.SetShaderParameter("macro_noise", ImageTexture.CreateFromImage(Macro()));
        shader.SetShaderParameter("path_mask", ImageTexture.CreateFromImage(PathMask()));
        shader.SetShaderParameter("region", new Vector4(0, 0, 200, 200));
        shader.SetShaderParameter("path_strength", 0f);
        for (int i = 0; i < terrain.Mesh.GetSurfaceCount(); i++)
            terrain.SetSurfaceOverrideMaterial(i, shader);
        _terrainMaterial = shader;
        s["terrain"] = new Dictionary<string, object?>
        {
            ["cells"] = assigned,
            ["shader"] = "one splat shader on every cell surface: world-space top projection at each record's tile size, a second sample at 0.37x scale "
                + "rotated 37 degrees mixed in by a macro noise (breaks the tiling), height-weighted blending across the cell borders over ~12 m with a "
                + "noise-perturbed edge, +/-12 % macro brightness, ORM occlusion and roughness, tangent-space normals at the record's strength",
            ["source"] = "assets/_staging/materials/<id>/ (the staged seamless bakes), loaded for this run only",
        };

        var (calb, cnrm, corm, ctile, _) = Staged("material_rubble_stone_wall");
        var cliff = new OrmMaterial3D
        {
            AlbedoTexture = calb, NormalEnabled = true, NormalTexture = cnrm, NormalScale = 1f, OrmTexture = corm,
            Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1TriplanarSharpness = 4f, Uv1Scale = Vector3.One / (ctile * 2f),
            AlbedoColor = new Color(0.82f, 0.80f, 0.78f), TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        foreach (var wall in _root.GetNode("Hollow/Ravine").GetChildren().OfType<MeshInstance3D>())
            wall.MaterialOverride = cliff;
        // The wolves' den is a notch in the same rock: its three greybox faces take the cliff's stone too.
        foreach (var den in _root.GetNode("Hollow").GetChildren().OfType<MeshInstance3D>().Where(m => m.Name.ToString().StartsWith("den_rock", StringComparison.Ordinal)))
            den.MaterialOverride = cliff;
        s["cliffs_and_ravine"] = $"material_rubble_stone_wall, world triplanar (sharpness 4) at {ctile * 2f} m a tile (twice the record's {ctile} m: a cliff face, not a wall), albedo x0.8; also on the three den_rock greybox faces (the den is a notch in the same rock)";
        return s;
    }

    private ShaderMaterial? _terrainMaterial;

    private (ImageTexture Albedo, ImageTexture Normal, ImageTexture Orm, float Tile, float Strength) Staged(string id)
    {
        string folder = Path.Combine(_staging, id);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, id + "_material.json")));
        var root = json.RootElement;
        var maps = root.GetProperty("maps");
        ImageTexture Load(string key)
        {
            var image = Image.LoadFromFile(Path.Combine(folder, maps.GetProperty(key).GetString()!));
            image.GenerateMipmaps();
            return ImageTexture.CreateFromImage(image);
        }
        float strength = root.TryGetProperty("pbr", out var pbr) && pbr.TryGetProperty("normal_strength", out var n) ? (float)n.GetDouble() : 1f;
        return (Load("basecolor"), Load("normal"), Load("orm"), (float)root.GetProperty("tile_size_m").GetDouble(), strength);
    }

    /// <summary>A seamless two-channel macro noise (512 px, sampled at tens of metres a tile): cell-border wobble, tiling breakup, brightness.</summary>
    private Image Macro()
    {
        if (_macro is not null)
            return _macro;
        Image Channel(int seed, float frequency)
        {
            var noise = new FastNoiseLite { Seed = seed, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = frequency,
                FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 4 };
            return noise.GetSeamlessImage(512, 512);
        }
        var a = Channel(11, 0.008f);
        var b = Channel(23, 0.012f);
        var c = Channel(37, 0.02f);
        var image = Image.CreateEmpty(512, 512, false, Image.Format.Rgb8);
        for (int y = 0; y < 512; y++)
        for (int x = 0; x < 512; x++)
            image.SetPixel(x, y, new Color(a.GetPixel(x, y).R, b.GetPixel(x, y).R, c.GetPixel(x, y).R));
        image.GenerateMipmaps();
        _macro = image;
        return image;
    }

    private float MacroAt(float u, float v, int channel)
    {
        var image = Macro();
        float x = (u - MathF.Floor(u)) * 512f - 0.5f, y = (v - MathF.Floor(v)) * 512f - 0.5f;
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        float P(int px, int py)
        {
            var c = image.GetPixel(((px % 512) + 512) % 512, ((py % 512) + 512) % 512);
            return channel == 0 ? c.R : channel == 1 ? c.G : c.B;
        }
        return Mathf.Lerp(Mathf.Lerp(P(x0, y0), P(x0 + 1, y0), fx), Mathf.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), fx), fy);
    }

    /// <summary>The cells' weights at a point, as the terrain shader blends them (same noise, same border).</summary>
    private Vector4 CellWeights(float x, float z)
    {
        float edge = (MacroAt(x / 37f, z / 37f, 0) - 0.5f) * 14f + (MacroAt(x / 9f, z / 9f, 1) - 0.5f) * 4f;
        float edge2 = (MacroAt(x / 41f + 0.3f, z / 41f + 0.6f, 0) - 0.5f) * 14f + (MacroAt(x / 11f + 0.5f, z / 11f, 1) - 0.5f) * 4f;
        float wx = SmoothStep(-6f, 6f, x - CellSplit + edge), wz = SmoothStep(-6f, 6f, z - CellSplit + edge2);
        return new Vector4((1 - wx) * (1 - wz), (1 - wx) * wz, wx * (1 - wz), wx * wz);
    }

    private static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
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
uniform vec4 tiles = vec4(2.0, 4.0, 3.0, 3.0);
uniform vec2 split = vec2(100.0, 100.0);
uniform vec4 region = vec4(0.0, 0.0, 200.0, 200.0);
uniform float path_strength = 0.0;

varying vec3 wpos;
varying vec3 wnrm;

void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

vec2 rot(vec2 p) { return mat2(vec2(0.7986, 0.6018), vec2(-0.6018, 0.7986)) * p; }

void layer(sampler2D a, sampler2D n, sampler2D o, vec2 p, float tile, float mixb, out vec3 alb, out vec3 nt, out vec3 orm) {
    vec2 uv1 = p / tile;
    vec2 uv2 = rot(p) / (tile * 2.7) + vec2(0.37, 0.71);
    alb = mix(texture(a, uv1).rgb, texture(a, uv2).rgb, mixb);
    nt = mix(texture(n, uv1).rgb, texture(n, uv2).rgb, mixb) * 2.0 - 1.0;
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
    vec3 a0; vec3 n0; vec3 o0; layer(alb0, nrm0, orm0, p, tiles.x, mixb, a0, n0, o0);
    vec3 a1; vec3 n1; vec3 o1; layer(alb1, nrm1, orm1, p, tiles.y, mixb, a1, n1, o1);
    vec3 a2; vec3 n2; vec3 o2; layer(alb2, nrm2, orm2, p, tiles.z, mixb, a2, n2, o2);
    vec3 a3; vec3 n3; vec3 o3; layer(alb3, nrm3, orm3, p, tiles.w, mixb, a3, n3, o3);

    // Height-weighted blend: where two grounds meet, the higher (brighter, less occluded) texel of each wins, not a cross-fade.
    vec4 h = vec4(dot(a0, vec3(0.333)) * o0.r, dot(a1, vec3(0.333)) * o1.r, dot(a2, vec3(0.333)) * o2.r, dot(a3, vec3(0.333)) * o3.r);
    vec4 hw = w + h * 0.6 * step(0.001, w);
    float top = max(max(hw.x, hw.y), max(hw.z, hw.w));
    vec4 b = max(hw - top + 0.18, 0.0) * step(0.001, w);
    b /= max(b.x + b.y + b.z + b.w, 1e-4);

    vec3 alb = a0 * b.x + a1 * b.y + a2 * b.z + a3 * b.w;
    vec3 nt = n0 * b.x + n1 * b.y + n2 * b.z + n3 * b.w;
    vec3 orm = o0 * b.x + o1 * b.y + o2 * b.z + o3 * b.w;

    // A trodden path: the dirt darkened and packed smooth, a little of the mud's wet in it.
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

    // ── the path from the lodge door ────────────────────────────────────────

    /// <summary>Where feet have worn the ground: from the lodge's east door round to the smithy's door, past the well to the waystone, and a fork to Sel's table.</summary>
    private static readonly Vector2[][] Paths =
    {
        new Vector2[] { new(52.3f, 128f), new(54.3f, 128.3f), new(55.2f, 130.6f), new(52.6f, 134.2f), new(51.2f, 138.2f), new(51.6f, 142f), new(52.8f, 142f) },
        new Vector2[] { new(51.6f, 142f), new(49.6f, 145.6f), new(46.4f, 149.4f), new(41.5f, 153.4f), new(35f, 156.2f), new(29.2f, 157.6f) },
        new Vector2[] { new(54.3f, 128.3f), new(59f, 127.4f), new(64.5f, 125.6f), new(70.6f, 123.4f) },
    };

    private const int PathSize = 1024;

    private Image PathMask()
    {
        if (_path is not null)
            return _path;
        var image = Image.CreateEmpty(PathSize, PathSize, false, Image.Format.R8);
        float metresPerPixel = 200f / PathSize;
        foreach (var line in Paths)
        {
            float minX = line.Min(p => p.X) - 4, maxX = line.Max(p => p.X) + 4, minZ = line.Min(p => p.Y) - 4, maxZ = line.Max(p => p.Y) + 4;
            for (int py = (int)(minZ / metresPerPixel); py <= (int)(maxZ / metresPerPixel); py++)
            for (int px = (int)(minX / metresPerPixel); px <= (int)(maxX / metresPerPixel); px++)
            {
                var at = new Vector2((px + 0.5f) * metresPerPixel, (py + 0.5f) * metresPerPixel);
                float d = float.MaxValue;
                for (int i = 0; i + 1 < line.Length; i++)
                    d = Math.Min(d, SegmentDistance(at, line[i], line[i + 1]));
                // About 1.5 m wide, its edge wandering and frayed.
                float wobble = (MacroAt(at.X / 13f, at.Y / 13f, 2) - 0.5f) * 1.2f + (MacroAt(at.X / 3f, at.Y / 3f, 1) - 0.5f) * 0.6f;
                float v = 1f - SmoothStep(0.45f, 1.25f, d + wobble);
                float old = image.GetPixel(px, py).R;
                image.SetPixel(px, py, new Color(Math.Max(old, v), 0, 0));
            }
        }
        _path = image;
        image.SavePng(Path.Combine(_raw, "ab_path_mask.png"));
        return image;
    }

    private float PathAt(float x, float z)
    {
        var image = PathMask();
        int px = Math.Clamp((int)(x / 200f * PathSize), 0, PathSize - 1), py = Math.Clamp((int)(z / 200f * PathSize), 0, PathSize - 1);
        return image.GetPixel(px, py).R;
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Math.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
        return p.DistanceTo(a + ab * t);
    }

    // ── V3: ground scatter ──────────────────────────────────────────────────

    private const float Chunk = 16f;

    private sealed record Kind(string Name, Mesh[] Meshes, float MaxDensity, float RangeEnd, bool Shadows, Func<float, float, Vector4, float> Density,
        Func<Random, float, float, Vector4, (Basis Basis, float Sink, Color Tint)> Place);

    private Dictionary<string, object?> Scatter()
    {
        var s = new Dictionary<string, object?>();
        _terrainMaterial?.SetShaderParameter("path_strength", 1f);
        var layout = _session.Setup.Layout;
        var boxes = layout.Space.Blockers.OfType<BoxBlocker>().ToList();
        var circles = layout.Space.Blockers.OfType<CircleBlocker>().ToList();
        var trees = circles.Where(c => c.Id.StartsWith("tree_", StringComparison.Ordinal)).Select(c => new Vector2(c.CenterXMm / 1000f, c.CenterZMm / 1000f)).ToList();
        // Each building as a whole (its walls' extent), not wall by wall: nothing grows inside.
        var buildings = new[] { "longhouse_", "forge_" }.Select(prefix => boxes.Where(b => b.Id.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            .Where(w => w.Count > 0).Select(w => new Rect2(w.Min(b => b.MinXMm) / 1000f, w.Min(b => b.MinZMm) / 1000f,
                (w.Max(b => b.MaxXMm) - w.Min(b => b.MinXMm)) / 1000f, (w.Max(b => b.MaxZMm) - w.Min(b => b.MinZMm)) / 1000f)).ToList();
        var spots = layout.Containers.Select(c => new Vector2(c.XMm / 1000f, c.ZMm / 1000f))
            .Concat(layout.Stations.Select(c => new Vector2(c.XMm / 1000f, c.ZMm / 1000f)))
            .Concat(layout.Npcs.Select(c => new Vector2(c.XMm / 1000f, c.ZMm / 1000f))).ToList();

        // Clear of things: 0 inside a footprint, thinning over a metre or two outside it.
        float Clear(float x, float z, float margin, bool offPath = true)
        {
            var p = new Vector2(x, z);
            float k = 1f;
            foreach (var r in buildings)
                k = Math.Min(k, SmoothStep(0.2f, 0.2f + 2.2f * margin, RectDistance(p, r)));
            foreach (var b in boxes)
                k = Math.Min(k, SmoothStep(0.05f, 0.05f + 0.9f * margin, RectDistance(p, new Rect2(b.MinXMm / 1000f, b.MinZMm / 1000f, (b.MaxXMm - b.MinXMm) / 1000f, (b.MaxZMm - b.MinZMm) / 1000f))));
            foreach (var c in circles)
                k = Math.Min(k, SmoothStep(0.02f, 0.02f + 0.6f * margin, p.DistanceTo(new Vector2(c.CenterXMm / 1000f, c.CenterZMm / 1000f)) - c.RadiusMm / 1000f));
            foreach (var spot in spots)
                k = Math.Min(k, SmoothStep(0.6f, 1.2f, p.DistanceTo(spot)));
            if (offPath)
                k *= 1f - SmoothStep(0.1f, 0.55f, PathAt(x, z));
            if (Height(x, z) < 0.2f || Slope(x, z) > 0.75f)
                return 0;
            return k;
        }

        float NearTree(float x, float z) => trees.Count == 0 ? 99 : trees.Min(t => t.DistanceTo(new Vector2(x, z)));
        float NearBuilding(float x, float z) => buildings.Count == 0 ? 99 : buildings.Min(r => RectDistance(new Vector2(x, z), r));

        var grassTexture = GrassAtlas();
        var grassMaterial = new StandardMaterial3D
        {
            AlbedoTexture = grassTexture, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, AlphaScissorThreshold = 0.4f,
            AlphaAntialiasingMode = BaseMaterial3D.AlphaAntiAliasing.AlphaToCoverage, CullMode = BaseMaterial3D.CullModeEnum.Back, Roughness = 0.85f,
            MetallicSpecular = 0.25f, BacklightEnabled = true, Backlight = new Color(0.12f, 0.14f, 0.05f),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        var grassMeshes = new Mesh[] { GrassClump(grassMaterial, 0), GrassClump(grassMaterial, 1) };
        var leafMaterial = new StandardMaterial3D
        {
            AlbedoTexture = LeafAtlas(), VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, AlphaScissorThreshold = 0.5f,
            AlphaAntialiasingMode = BaseMaterial3D.AlphaAntiAliasing.AlphaToCoverage, CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 0.9f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        var leafMesh = new Mesh[] { FlatCard(leafMaterial) };
        var (salb, snrm, sorm, _, _) = Staged("material_limestone_ashlar");
        var stoneMaterial = new OrmMaterial3D
        {
            AlbedoTexture = salb, NormalEnabled = true, NormalTexture = snrm, OrmTexture = sorm, Uv1Triplanar = true, Uv1Scale = Vector3.One * 0.6f,
            AlbedoColor = new Color(0.72f, 0.70f, 0.66f), TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        var stoneMeshes = Enumerable.Range(0, 4).Select(i => Stone(stoneMaterial, 100 + i)).ToArray();

        // Clumps a square metre, by cell: gravel quarry, waystation dirt, mud flats, Charwood turf.
        var grassDensity = new Vector4(0.6f, 4.0f, 2.4f, 4.6f);
        var stoneDensity = new Vector4(0.55f, 0.12f, 0.1f, 0.06f);
        var leafDensity = new Vector4(0.005f, 0.01f, 0.02f, 0.12f);
        var kinds = new[]
        {
            new Kind("grass", grassMeshes, 4.6f, 60f, false,
                (x, z, w) =>
                {
                    float d = w.Dot(grassDensity);
                    // Patches at two scales everywhere; a trodden yard round the buildings; thinner under the canopies.
                    float clump = SmoothStep(0.38f, 0.58f, MacroAt(x / 17f, z / 17f, 1));
                    float fine = SmoothStep(0.25f, 0.75f, MacroAt(x / 5f, z / 5f, 2));
                    d *= 0.3f + 0.7f * clump * (0.5f + 0.5f * fine);
                    d *= Mathf.Lerp(1f, SmoothStep(2f, 8f, NearBuilding(x, z)), w.Y);
                    d *= 0.35f + 0.65f * SmoothStep(1.2f, 6f, NearTree(x, z));
                    return d * Clear(x, z, 1f);
                },
                (rng, x, z, w) =>
                {
                    float yaw = (float)rng.NextDouble() * Mathf.Tau;
                    float lean = ((float)rng.NextDouble() - 0.5f) * 0.25f;
                    float size = 0.75f + (float)rng.NextDouble() * 0.55f;
                    float tall = size * (0.8f + (float)rng.NextDouble() * 0.5f) * (0.75f + 0.35f * w.W + 0.15f * w.Z);
                    var basis = Basis.FromEuler(new Vector3(lean, yaw, ((float)rng.NextDouble() - 0.5f) * 0.25f)) * Basis.FromScale(new Vector3(size, tall, size));
                    // Greener in the wood and the wet, straw-coloured on the worked dirt and the quarry, with a little per-clump drift.
                    var lush = new Color(0.78f, 0.88f, 0.70f);
                    var dry = new Color(0.92f, 0.86f, 0.62f);
                    var tint = dry.Lerp(lush, Math.Clamp(w.W + w.Z * 0.7f, 0, 1));
                    float drift = 0.88f + (float)rng.NextDouble() * 0.24f;
                    return (basis, 0.03f, new Color(tint.R * drift, tint.G * drift, tint.B * drift));
                }),
            new Kind("leaves", leafMesh, 0.5f, 40f, false,
                (x, z, w) =>
                {
                    float near = NearTree(x, z);
                    float d = w.Dot(leafDensity) + 0.35f * (1f - SmoothStep(2f, 7f, near));
                    return d * Clear(x, z, 0.5f);
                },
                (rng, x, z, w) =>
                {
                    float size = 0.6f + (float)rng.NextDouble() * 0.5f;
                    var basis = Basis.FromEuler(new Vector3(0, (float)rng.NextDouble() * Mathf.Tau, 0)) * Basis.FromScale(new Vector3(size, 1, size));
                    float v = 0.85f + (float)rng.NextDouble() * 0.25f;
                    return (basis, -0.012f - (float)rng.NextDouble() * 0.01f, new Color(v, v, v));
                }),
            new Kind("stones", stoneMeshes, 0.6f, 80f, true,
                (x, z, w) =>
                {
                    float d = w.Dot(stoneDensity);
                    float path = PathAt(x, z);
                    // Kicked to the path's shoulders.
                    d += 0.25f * SmoothStep(0.05f, 0.3f, path) * (1f - SmoothStep(0.4f, 0.7f, path));
                    return d * Clear(x, z, 0.4f, offPath: false) * (1f - SmoothStep(0.45f, 0.7f, path));
                },
                (rng, x, z, w) =>
                {
                    // Mostly pebbles, a few fist- and head-sized, sunk into the ground by a third.
                    float r = (float)rng.NextDouble();
                    float size = 0.05f + r * r * r * 0.32f;
                    var basis = Basis.FromEuler(new Vector3(((float)rng.NextDouble() - 0.5f) * 0.6f, (float)rng.NextDouble() * Mathf.Tau, ((float)rng.NextDouble() - 0.5f) * 0.6f))
                        * Basis.FromScale(new Vector3(size * (0.8f + (float)rng.NextDouble() * 0.5f), size * (0.45f + (float)rng.NextDouble() * 0.3f), size));
                    float v = 0.8f + (float)rng.NextDouble() * 0.35f;
                    return (basis, size * 0.3f, new Color(v, v * 0.98f, v * 0.95f));
                }),
        };

        var node = new Node3D { Name = "AB_Scatter" };
        _root.AddChild(node);
        var counts = new Dictionary<string, object?>();
        ulong started = Time.GetTicksMsec();
        var rng = new Random(20260925);
        foreach (var kind in kinds)
        {
            int total = 0;
            for (float cz = 0; cz < 200f; cz += Chunk)
            for (float cx = 0; cx < 200f; cx += Chunk)
            {
                var per = kind.Meshes.Select(_ => new List<(Transform3D, Color)>()).ToArray();
                // A jittered grid at the kind's greatest density; each point kept with the probability the density gives there.
                float step = 1f / MathF.Sqrt(kind.MaxDensity);
                for (float z = cz; z < cz + Chunk; z += step)
                for (float x = cx; x < cx + Chunk; x += step)
                {
                    float px = x + (float)rng.NextDouble() * step, pz = z + (float)rng.NextDouble() * step;
                    if (px >= 200f || pz >= 200f)
                        continue;
                    var w = CellWeights(px, pz);
                    float keep = kind.Density(px, pz, w) / kind.MaxDensity;
                    if (rng.NextDouble() >= keep)
                        continue;
                    var (basis, sink, tint) = kind.Place(rng, px, pz, w);
                    int m = rng.Next(kind.Meshes.Length);
                    per[m].Add((new Transform3D(basis, new Vector3(px, Height(px, pz) - sink, pz)), tint));
                }
                for (int m = 0; m < kind.Meshes.Length; m++)
                {
                    if (per[m].Count == 0)
                        continue;
                    var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = kind.Meshes[m], InstanceCount = per[m].Count };
                    for (int i = 0; i < per[m].Count; i++)
                    {
                        multi.SetInstanceTransform(i, per[m][i].Item1);
                        multi.SetInstanceColor(i, per[m][i].Item2);
                    }
                    node.AddChild(new MultiMeshInstance3D
                    {
                        Name = $"{kind.Name}_{cx}_{cz}_{m}", Multimesh = multi, CastShadow = kind.Shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                        GIMode = GeometryInstance3D.GIModeEnum.Disabled, VisibilityRangeEnd = kind.RangeEnd, VisibilityRangeEndMargin = 10f,
                        VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
                    });
                    total += per[m].Count;
                }
            }
            counts[kind.Name] = total;
        }
        s["instances"] = counts;
        s["build_ms"] = Time.GetTicksMsec() - started;
        s["chunks"] = $"{Chunk} m square MultiMeshInstance3D chunks (frustum-culled), faded out by distance (grass 60 m, leaves 40 m, stones 80 m); no GI contribution; stones cast shadows";
        s["grass"] = "procedural alpha cards: a 512 px atlas of two painted clumps (~70 tapered, curved blades each), mipmaps rescaled to keep alpha-test coverage; "
            + "three crossed cards per clump, normals bent up for soft shading, base darkened in the vertex colour; alpha scissor 0.4 with alpha-to-coverage; "
            + $"density by cell (quarry, dirt, mud, turf) {grassDensity} clumps/m2, patchy at two scales (17 m and 5 m noise), a bare trodden yard within ~2-8 m of the buildings, thinner under canopies, per-cell tint (straw on dirt and gravel, green in the wood)";
        s["stones"] = $"four noise-displaced icospheres (320 tris) in the limestone staged texture, object triplanar; {stoneDensity}/m2 by cell plus the path's shoulders; mostly pebbles, cubed size distribution, sunk a third";
        s["leaves"] = "flat cards carrying a painted patch of ~24 leaves (a 512 px atlas), 0.6-1.1 m, densest within 7 m of a trunk";
        s["thinning"] = "zero inside buildings and footprints; fading out within ~2 m of buildings, ~1 m of walls and rocks, 1.2 m of containers, stations and NPCs; none on the path, under water or on slopes over ~37 degrees";
        s["path"] = "a worn strip ~1.5 m wide with a frayed edge (splat mask, 0.2 m texels): lodge door, round to the smithy door, past the well to the waystone; a fork east to Sel's table";
        return s;
    }

    private float Slope(float x, float z)
    {
        float dx = Height(x + 0.5f, z) - Height(x - 0.5f, z), dz = Height(x, z + 0.5f) - Height(x, z - 0.5f);
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static float RectDistance(Vector2 p, Rect2 r)
    {
        float dx = Math.Max(r.Position.X - p.X, p.X - r.End.X), dz = Math.Max(r.Position.Y - p.Y, p.Y - r.End.Y);
        if (dx <= 0 && dz <= 0)
            return Math.Max(dx, dz);
        return MathF.Sqrt(MathF.Max(dx, 0) * MathF.Max(dx, 0) + MathF.Max(dz, 0) * MathF.Max(dz, 0));
    }

    // ── procedural meshes and atlases ───────────────────────────────────────

    /// <summary>Three cards crossed at 60 degrees, 0.6 m wide and 0.5 m tall, from half <paramref name="half"/> of the grass atlas.</summary>
    private static Mesh GrassClump(Material material, int half)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colours = new List<Color>();
        var indices = new List<int>();
        const float w = 0.4f, h = 0.5f;
        for (int c = 0; c < 3; c++)
        {
            float a = c * Mathf.Pi / 3;
            var along = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var face = new Vector3(-along.Z, 0, along.X);
            int b = vertices.Count;
            // Two rows of vertices so the top can splay outwards a little.
            for (int row = 0; row < 2; row++)
            for (int col = 0; col < 2; col++)
            {
                float sx = col == 0 ? -1 : 1;
                vertices.Add(along * w * sx * (row == 0 ? 0.75f : 1.1f) + Vector3.Up * h * row);
                normals.Add((Vector3.Up * 0.8f + along * sx * 0.25f + face * 0.15f).Normalized());
                uvs.Add(new Vector2(half * 0.5f + (col == 0 ? 0.002f : 0.498f), row == 0 ? 0.998f : 0.002f));
                float shade = row == 0 ? 0.62f : 1f;
                colours.Add(new Color(shade, shade, shade));
            }
            // Both windings over the same vertices: each side keeps the bent-up normal (a two-sided material would flip it on the back, darkening half the cards).
            indices.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3, b, b + 1, b + 2, b + 1, b + 3, b + 2 });
        }
        return Build(vertices, normals, uvs, colours, indices, material);
    }

    private static Mesh FlatCard(Material material)
    {
        var vertices = new List<Vector3> { new(-0.5f, 0, -0.5f), new(0.5f, 0, -0.5f), new(-0.5f, 0, 0.5f), new(0.5f, 0, 0.5f) };
        var normals = Enumerable.Repeat(Vector3.Up, 4).ToList();
        var uvs = new List<Vector2> { new(0, 0), new(1, 0), new(0, 1), new(1, 1) };
        var colours = Enumerable.Repeat(Colors.White, 4).ToList();
        return Build(vertices, normals, uvs, colours, new List<int> { 0, 1, 2, 1, 3, 2 }, material);
    }

    private static Mesh Build(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<Color> colours, List<int> indices, Material material)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    /// <summary>A stone: an icosphere twice subdivided, pushed about by noise and flattened underneath.</summary>
    private static Mesh Stone(Material material, int seed)
    {
        float t = (1 + MathF.Sqrt(5)) / 2;
        var v = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        var f = new List<(int, int, int)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
        };
        for (int i = 0; i < v.Count; i++)
            v[i] = v[i].Normalized();
        for (int level = 0; level < 2; level++)
        {
            var mid = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (!mid.TryGetValue(key, out int m))
                {
                    m = v.Count;
                    v.Add(((v[a] + v[b]) / 2).Normalized());
                    mid[key] = m;
                }
                return m;
            }
            var next = new List<(int, int, int)>();
            foreach (var (a, b, c) in f)
            {
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.AddRange(new[] { (a, ab, ca), (b, bc, ab), (c, ca, bc), (ab, bc, ca) });
            }
            f = next;
        }
        var noise = new FastNoiseLite { Seed = seed, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.9f, FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 3 };
        for (int i = 0; i < v.Count; i++)
        {
            var p = v[i] * (1f + noise.GetNoise3Dv(v[i] * 1.3f) * 0.35f);
            if (p.Y < -0.35f)
                p.Y = -0.35f + (p.Y + 0.35f) * 0.3f;
            v[i] = p;
        }
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var p in v)
            tool.AddVertex(p);
        // The icosphere's winding is counter-clockwise from outside; Godot's front face is clockwise.
        foreach (var (a, b, c) in f)
        {
            tool.AddIndex(a);
            tool.AddIndex(c);
            tool.AddIndex(b);
        }
        tool.GenerateNormals();
        tool.SetMaterial(material);
        return tool.Commit();
    }

    /// <summary>Two grass clumps side by side (512 x 256 each): tapered, curved blades, dark at the root and paler at the tip, some dry.</summary>
    private static ImageTexture GrassAtlas()
    {
        const int W = 1024, H = 512;
        var buffer = new float[W * H * 4];
        // Behind every blade, the grass's own mean colour at zero alpha, so filtering never bleeds a fringe.
        for (int i = 0; i < W * H; i++)
        {
            buffer[i * 4] = 0.28f;
            buffer[i * 4 + 1] = 0.32f;
            buffer[i * 4 + 2] = 0.12f;
        }
        var rng = new Random(7);
        for (int half = 0; half < 2; half++)
        {
            int blades = half == 0 ? 72 : 60;
            for (int n = 0; n < blades; n++)
            {
                float cx = half * 512 + 256 + (float)(Gauss(rng) * 95);
                float height = 240 + (float)rng.NextDouble() * 260;
                float lean = (float)(Gauss(rng) * 0.35) + (cx - (half * 512 + 256)) / 400f;
                float bend = lean * (0.6f + (float)rng.NextDouble() * 0.8f);
                float width = 7 + (float)rng.NextDouble() * 8;
                bool dry = rng.NextDouble() < (half == 0 ? 0.12 : 0.3);
                var root = dry ? new Color(0.24f, 0.22f, 0.10f) : new Color(0.12f, 0.16f, 0.06f);
                var tip = dry ? new Color(0.52f, 0.46f, 0.27f) : new Color(0.34f, 0.39f, 0.15f);
                float jitter = 0.85f + (float)rng.NextDouble() * 0.3f;
                for (float s = 0; s <= 1f; s += 0.5f / height)
                {
                    float y = H - 1 - s * height;
                    float x = cx + lean * s * height * 0.35f + bend * s * s * height * 0.4f;
                    float hw = width * 0.5f * MathF.Pow(1 - s, 0.9f) + 0.4f;
                    var colour = root.Lerp(tip, MathF.Pow(s, 0.7f));
                    for (int px = (int)(x - hw - 1); px <= (int)(x + hw + 1); px++)
                    {
                        if (px < half * 512 || px >= half * 512 + 512 || y < 0)
                            continue;
                        float d = MathF.Abs(px + 0.5f - x);
                        float a = Math.Clamp(hw - d + 0.5f, 0, 1);
                        if (a <= 0)
                            continue;
                        // The midrib a little lighter.
                        float rib = 1f + 0.12f * (1f - Math.Clamp(d / hw, 0, 1));
                        int i = ((int)y * W + px) * 4;
                        buffer[i] = Mathf.Lerp(buffer[i], colour.R * jitter * rib, a);
                        buffer[i + 1] = Mathf.Lerp(buffer[i + 1], colour.G * jitter * rib, a);
                        buffer[i + 2] = Mathf.Lerp(buffer[i + 2], colour.B * jitter * rib, a);
                        buffer[i + 3] = Math.Max(buffer[i + 3], a);
                    }
                }
            }
        }
        return ImageTexture.CreateFromImage(CoverageMips(ToImage(buffer, W, H), 0.4f));
    }

    /// <summary>A patch of fallen leaves (512 px): lobed oak-ish leaves in browns, ochres and a dull red, each with a darker rim and a vein.</summary>
    private static ImageTexture LeafAtlas()
    {
        const int S = 512;
        var buffer = new float[S * S * 4];
        for (int i = 0; i < S * S; i++)
        {
            buffer[i * 4] = 0.33f;
            buffer[i * 4 + 1] = 0.24f;
            buffer[i * 4 + 2] = 0.12f;
        }
        var palette = new[]
        {
            new Color(0.36f, 0.25f, 0.12f), new Color(0.46f, 0.33f, 0.14f), new Color(0.30f, 0.20f, 0.11f), new Color(0.50f, 0.40f, 0.20f),
            new Color(0.38f, 0.19f, 0.10f), new Color(0.26f, 0.22f, 0.12f), new Color(0.42f, 0.36f, 0.20f),
        };
        var rng = new Random(11);
        for (int n = 0; n < 24; n++)
        {
            float cx = 60 + (float)rng.NextDouble() * (S - 120), cy = 60 + (float)rng.NextDouble() * (S - 120);
            float length = 38 + (float)rng.NextDouble() * 34, width = length * (0.32f + (float)rng.NextDouble() * 0.12f);
            float angle = (float)rng.NextDouble() * Mathf.Tau;
            var colour = palette[rng.Next(palette.Length)];
            float lobes = 2 + rng.Next(3);
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var across = new Vector2(-dir.Y, dir.X);
            int r = (int)length + 2;
            for (int py = (int)cy - r; py <= (int)cy + r; py++)
            for (int px = (int)cx - r; px <= (int)cx + r; px++)
            {
                var q = new Vector2(px + 0.5f - cx, py + 0.5f - cy);
                float u = q.Dot(dir) / length, v = q.Dot(across);
                if (u < -1 || u > 1)
                {
                    // The stalk.
                    if (u > 1 && u < 1.25f && MathF.Abs(v) < 1.2f)
                        Paint(buffer, S, px, py, colour * 0.6f, 1);
                    continue;
                }
                float edge = width * MathF.Pow(1 - u * u, 0.65f) * (1f + 0.16f * MathF.Sin((u + 1) * Mathf.Pi * lobes));
                float a = Math.Clamp(edge - MathF.Abs(v) + 0.5f, 0, 1);
                if (a <= 0)
                    continue;
                float rim = Math.Clamp(MathF.Abs(v) / Math.Max(edge, 0.01f), 0, 1);
                float shade = 1.05f - 0.3f * rim * rim - (MathF.Abs(v) < 0.9f ? 0.22f : 0f);
                Paint(buffer, S, px, py, colour * shade, a);
            }
        }
        return ImageTexture.CreateFromImage(CoverageMips(ToImage(buffer, S, S), 0.5f));
    }

    private static void Paint(float[] buffer, int width, int x, int y, Color colour, float a)
    {
        if (x < 0 || y < 0 || x >= width || y >= buffer.Length / 4 / width)
            return;
        int i = (y * width + x) * 4;
        buffer[i] = Mathf.Lerp(buffer[i], colour.R, a);
        buffer[i + 1] = Mathf.Lerp(buffer[i + 1], colour.G, a);
        buffer[i + 2] = Mathf.Lerp(buffer[i + 2], colour.B, a);
        buffer[i + 3] = Math.Max(buffer[i + 3], a);
    }

    private static double Gauss(Random rng) => Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());

    private static Image ToImage(float[] buffer, int w, int h)
    {
        var bytes = new byte[w * h * 4];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)Math.Clamp(buffer[i] * 255f + 0.5f, 0, 255);
        return Image.CreateFromData(w, h, false, Image.Format.Rgba8, bytes);
    }

    /// <summary>
    /// A mip chain whose alpha keeps the base level's alpha-tested coverage: plain averaging thins cut-out foliage to nothing with
    /// distance; each level's alpha is scaled until as much of it passes <paramref name="cutoff"/> as of the base.
    /// </summary>
    private static Image CoverageMips(Image source, float cutoff)
    {
        int w = source.GetWidth(), h = source.GetHeight();
        float target = Coverage(source.GetData(), cutoff, 1f);
        var data = new List<byte>(source.GetData());
        int lw = w, lh = h;
        while (lw > 1 || lh > 1)
        {
            lw = Math.Max(1, lw / 2);
            lh = Math.Max(1, lh / 2);
            var level = (Image)source.Duplicate();
            level.Resize(lw, lh, Image.Interpolation.Lanczos);
            var bytes = level.GetData();
            float lo = 0.5f, hi = 4f;
            for (int i = 0; i < 16; i++)
            {
                float mid = (lo + hi) / 2;
                if (Coverage(bytes, cutoff, mid) < target)
                    lo = mid;
                else
                    hi = mid;
            }
            float scale = (lo + hi) / 2;
            for (int i = 3; i < bytes.Length; i += 4)
                bytes[i] = (byte)Math.Clamp(bytes[i] * scale, 0, 255);
            data.AddRange(bytes);
        }
        return Image.CreateFromData(w, h, true, Image.Format.Rgba8, data.ToArray());
    }

    private static float Coverage(byte[] rgba, float cutoff, float scale)
    {
        int n = 0, pass = 0;
        for (int i = 3; i < rgba.Length; i += 4, n++)
        {
            if (rgba[i] / 255f * scale >= cutoff)
                pass++;
        }
        return n == 0 ? 0 : (float)pass / n;
    }

    // ── writing ─────────────────────────────────────────────────────────────

    private void Write(string name, object value) => File.WriteAllText(Path.Combine(_raw, name), JsonSerializer.Serialize(value, Json));
}
