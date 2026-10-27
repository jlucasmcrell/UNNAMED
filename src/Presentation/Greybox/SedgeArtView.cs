// Sedge's authored art. Presentation only; the accepted domain tide/route remains authoritative.
using System.Text.Json;
using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Greybox;

public partial class SedgeArtView : Node3D
{
    private ShaderMaterial? _ground;
    private Node3D? _crest;
    private readonly List<OmniLight3D> _lamps = new();
    private readonly List<ParticleProcessMaterial> _rain = new();
    private readonly List<(FogVolume Bank, Vector3 Origin)> _mist = new();
    private readonly List<SpotLight3D> _shafts = new();
    private readonly List<FogVolume> _shaftHaze = new();
    private float _elapsed;
    private bool _airProbeLogged;

    public static SedgeArtView? Build(Node3D parent, Node3D terrain, ArtLibrary art, string region)
    {
        if (region != "region.sedge_levels" || art.Root is null || !File.Exists(Path.Combine(art.Root, "sedge_art_gate3.json")))
            return null;
        var view = new SedgeArtView { Name = "SedgeArt" };
        parent.AddChild(view);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(art.Root, "sedge_art_gate3.json")));
        view.Ground(terrain, art);
        view.Masonry(parent, art);
        view.Reeds(art);
        view.Mist(json.RootElement.GetProperty("mist_banks"));
        view.Lamps(json.RootElement.GetProperty("lamps"));
        view.Crest();
        // Openings in the overcast: real light scattered through the native
        // water mist. Positions are art placements, never simulation state.
        foreach (var at in new[] { new Vector3(4860,135,1100), new Vector3(4910,165,600), new Vector3(5110,145,1700) })
        {
            var shaft = new SpotLight3D { Name = "CloudBreakShaft", Position = at,
                // Godot low-density fog needs a separate high volumetric-energy multiplier.
                LightColor = new Color(.87f,.93f,1), LightEnergy = 22f,
                LightVolumetricFogEnergy = 25000f, SpotRange = 260, SpotAngle = 13,
                SpotAttenuation = .5f, ShadowEnabled = true,
                RotationDegrees = new Vector3(-72, -20, 0) };
            view.AddChild(shaft);view._shafts.Add(shaft);
            var haze = new FogVolume { Name = "CloudBreakAerosols", Shape = RenderingServer.FogVolumeShape.Ellipsoid,
                Position = at + shaft.Basis * Vector3.Forward * 70,
                Size = new Vector3(32,145,32), RotationDegrees = new Vector3(18,-20,0),
                Material = new FogMaterial { Density = .0014f, Albedo = new Color(.88f,.93f,1), EdgeFade = .75f } };
            view.AddChild(haze);view._shaftHaze.Add(haze);
        }
        if (parent.GetNodeOrNull<Node3D>("Atmosphere") is { } atmosphere)
            foreach (var particles in atmosphere.GetChildren().OfType<GpuParticles3D>().Where(p => p.Name.ToString().StartsWith("rain", StringComparison.Ordinal)))
                if (particles.ProcessMaterial is ParticleProcessMaterial process)
                {
                    var own = (ParticleProcessMaterial)process.Duplicate();particles.ProcessMaterial = own;view._rain.Add(own);
                }
        GD.Print($"SEDGE_ART Gate3: masonry, wet ground, layered mist, native rain, {view._lamps.Count} lamps; native kit reed LODs");
        return view;
    }

    public void SetTideLevel(float level)
    {
        _ground?.SetShaderParameter("tide_level", level);
        if (_crest is not null) _crest.Visible = level >= .70f;
    }

    private void Ground(Node3D terrain, ArtLibrary art)
    {
        _ground = new ShaderMaterial { Shader = GD.Load<Shader>("res://Greybox/SedgeGround.gdshader") };
        foreach (var (id, slot) in new[] { ("sedge_wet_peat", "peat"), ("sedge_salt_crust", "salt") })
        {
            var maps = art.WorldMaps(id) ?? throw new InvalidDataException($"Sedge requires {id}");
            _ground.SetShaderParameter(slot + "_albedo", maps.Albedo);
            _ground.SetShaderParameter(slot + "_normal", maps.Normal ?? throw new InvalidDataException($"Missing {id} normal map"));
        }
        foreach (var mesh in Meshes(terrain)) mesh.MaterialOverride = _ground;
    }

    private void Masonry(Node3D parent, ArtLibrary art)
    {
        if (parent.GetNodeOrNull<Node3D>("Dressing") is not { } dressing) return;
        var shader = GD.Load<Shader>("res://Greybox/SedgeMasonry.gdshader");
        foreach (var holder in dressing.GetChildren().OfType<Node3D>().Where(n =>
                     n.Name.ToString().StartsWith("sedge_arch_", StringComparison.Ordinal) ||
                     n.Name.ToString().StartsWith("sedge_belfry_", StringComparison.Ordinal)))
            foreach (var mesh in Meshes(holder))
            {
                // Keep every source surface's carved UV/albedo/normal. Weather
                // the original material, rather than replacing it with bricks.
                if (mesh.Mesh is not ArrayMesh original) continue;
                var copy = (ArrayMesh)original.Duplicate();
                for (int surface = 0; surface < copy.GetSurfaceCount(); surface++)
                {
                    if (mesh.GetActiveMaterial(surface) is not BaseMaterial3D source) continue;
                    var material = new ShaderMaterial { Shader = shader };
                    material.SetShaderParameter("source_colour", source.AlbedoColor);
                    material.SetShaderParameter("source_roughness", source.Roughness);
                    material.SetShaderParameter("source_metallic", source.Metallic);
                    material.SetShaderParameter("use_vertex_colour", source.VertexColorUseAsAlbedo);
                    material.SetShaderParameter("has_roughness", source.RoughnessTexture is not null);
                    material.SetShaderParameter("has_metallic", source.MetallicTexture is not null);
                    material.SetShaderParameter("roughness_channel", (int)source.RoughnessTextureChannel);
                    material.SetShaderParameter("metallic_channel", (int)source.MetallicTextureChannel);
                    if (source.RoughnessTexture is { } rough) material.SetShaderParameter("source_roughness_tex", rough);
                    if (source.MetallicTexture is { } metal) material.SetShaderParameter("source_metallic_tex", metal);
                    material.SetShaderParameter("has_albedo", source.AlbedoTexture is not null);
                    material.SetShaderParameter("has_normal", source.NormalEnabled && source.NormalTexture is not null);
                    material.SetShaderParameter("normal_strength", source.NormalScale);
                    if (source.AlbedoTexture is { } albedo) material.SetShaderParameter("source_albedo", albedo);
                    if (source.NormalTexture is { } normal) material.SetShaderParameter("source_normal", normal);
                    copy.SurfaceSetMaterial(surface, material);
                }
                mesh.Mesh = copy;
            }
    }

    private void Reeds(ArtLibrary art)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(art.Root!, "sedge_reeds.json")));
        var groups = json.RootElement.GetProperty("instances").EnumerateArray().GroupBy(e =>
        {
            var at = e.GetProperty("at");
            return ((int)MathF.Floor(at[0].GetSingle() / 64), (int)MathF.Floor(at[2].GetSingle() / 64), e.GetProperty("band").GetInt32());
        }).ToArray();
        var ranges = new[] { 0f, 90f, 220f, 650f };
        var tints = new[] { new Color(1.06f, 1.17f, .87f), new Color(1.24f, 1.16f, .90f), new Color(1.32f, 1.21f, .95f) };
        var windShader = new Shader { Code = WindField.TreeShader.Replace("render_mode cull_back", "render_mode cull_disabled")
            .Replace("BACKLIGHT = vec3(0.88, 0.81, 0.63);", "BACKLIGHT = ALBEDO * 0.22;") };
        for (int lod = 0; lod < 3; lod++)
        {
            var source = art.ModelWithLods($"sedge_reeds_lod{lod}") ?? throw new InvalidDataException("Missing native reed LOD");
            foreach (var mesh in Meshes(source))
            {
                // Source kits are static; bake their native node transform into the instance, preserve all source surfaces.
                var meshTransform = mesh.Transform;
                for (var ancestor = mesh.GetParent() as Node3D; ancestor is not null && ancestor != source; ancestor = ancestor.GetParent() as Node3D)
                    meshTransform = ancestor.Transform * meshTransform;
                var bandMeshes = new ArrayMesh[3];
                for (int band = 0; band < 3; band++)
                {
                    var copy = (ArrayMesh)mesh.Mesh.Duplicate();
                    for (int s = 0; s < copy.GetSurfaceCount(); s++)
                    {
                        if (mesh.GetActiveMaterial(s) is not BaseMaterial3D original) continue;
                        var mat = new ShaderMaterial { Shader = windShader };
                        if (original.AlbedoTexture is { } albedo) mat.SetShaderParameter("albedo_tex", albedo);
                        mat.SetShaderParameter("has_albedo", original.AlbedoTexture is not null);
                        mat.SetShaderParameter("albedo_colour", original.AlbedoColor * tints[band]);
                        mat.SetShaderParameter("foliage", true);mat.SetShaderParameter("roughness_value", .76f);
                        if (original.NormalTexture is { } normal) mat.SetShaderParameter("normal_tex", normal);
                        mat.SetShaderParameter("has_normal", original.NormalEnabled);
                        copy.SurfaceSetMaterial(s, mat);
                    }
                    bandMeshes[band] = copy;
                }
                foreach (var group in groups)
                {
                    var entries = group.ToArray();
                    var origin = new Vector3((group.Key.Item1 + .5f) * 64, 0, (group.Key.Item2 + .5f) * 64);
                    var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = bandMeshes[group.Key.Item3], InstanceCount = entries.Length };
                    for (int i = 0; i < entries.Length; i++)
                    {
                        var e = entries[i];var at = e.GetProperty("at");var scale = e.GetProperty("scale");
                        var basis = new Basis(Vector3.Up, Mathf.DegToRad(e.GetProperty("yaw").GetSingle())).Scaled(new Vector3(scale[0].GetSingle(), scale[1].GetSingle(), scale[2].GetSingle()));
                        var p = new Vector3(at[0].GetSingle(), at[1].GetSingle(), at[2].GetSingle());
                        multi.SetInstanceTransform(i, new Transform3D(basis, p - origin) * meshTransform);
                    }
                    var node = new MultiMeshInstance3D { Name = $"Reeds_L{lod}_{group.Key}", Multimesh = multi, Position = origin,
                        VisibilityRangeBegin = ranges[lod], VisibilityRangeEnd = ranges[lod + 1], VisibilityRangeBeginMargin = 12, VisibilityRangeEndMargin = 20,
                        VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
                        CastShadow = lod == 0 ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off };
                    node.SetInstanceShaderParameter("tree_height", 2f);node.SetInstanceShaderParameter("stiffness", .12f);
                    AddChild(node);
                }
            }
            source.Free();
        }
        GD.Print($"SEDGE_REEDS {json.RootElement.GetProperty("instances").GetArrayLength()} bed-rooted clumps; 3 native geometry LODs, 64m culling groups");
    }

    private void Mist(JsonElement banks)
    {
        var noise = new NoiseTexture3D { Width = 48, Height = 32, Depth = 48, Seamless = true,
            Noise = new FastNoiseLite { Seed = 27103, Frequency = .055f, FractalOctaves = 3 } };
        foreach (var b in banks.EnumerateArray())
        {
            var bank = new FogVolume { Name = "SoundMistBank", Shape = RenderingServer.FogVolumeShape.Ellipsoid,
                Position = new Vector3(b[0].GetSingle(), b[1].GetSingle(), b[2].GetSingle()), Size = new Vector3(b[3].GetSingle(), b[4].GetSingle(), b[5].GetSingle()),
                Material = new FogMaterial { Density = .12f, Albedo = new Color(.79f, .85f, .84f), Emission = new Color(.025f, .035f, .037f), EdgeFade = .55f, HeightFalloff = 0f, DensityTexture = noise } };
            AddChild(bank);_mist.Add((bank, bank.Position));
        }
    }

    private void Lamps(JsonElement lamps)
    {
        var glass = new StandardMaterial3D { AlbedoColor = new Color(.80f, .35f, .08f), EmissionEnabled = true, Emission = new Color(1, .47f, .12f), EmissionEnergyMultiplier = 4f, Roughness = .25f };
        foreach (var e in lamps.EnumerateArray())
        {
            var p = e.GetProperty("at");var at = new Vector3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle());
            AddChild(new MeshInstance3D { Name = "LanternGlass", Position = at, Mesh = new SphereMesh { Radius = .085f, Height = .24f, RadialSegments = 12, Rings = 6 }, MaterialOverride = glass });
            var light = new OmniLight3D { Name = "TollpilesLamp", Position = at, LightColor = new Color(1, .60f, .26f), LightEnergy = 1.8f, OmniRange = e.GetProperty("range_m").GetSingle(), OmniAttenuation = 1.1f,
                ShadowEnabled = _lamps.Count % 3 == 0, DistanceFadeEnabled = true, DistanceFadeBegin = 90, DistanceFadeLength = 30 };
            AddChild(light);_lamps.Add(light);
        }
    }

    private void Crest()
    {
        _crest = new Node3D { Name = "OtherAtCrest", Position = new Vector3(5000, 2.45f, 58), Visible = false };AddChild(_crest);
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Greybox/SedgeOther.gdshader") };
        // An open threshold with drifting light columns. Nothing closes the entire aperture with a coloured polygon.
        float[] columns = { -47, -40, -31, -23, -18, -7, 3, 16, 28, 37, 48 };
        for (int i = 0; i < columns.Length; i++)
        {
            float x = columns[i], bottom = (i % 4) * 4.2f;
            float height = MathF.Min(102, 21 * (4.9f - MathF.Max(0, MathF.Abs(x) / 24.3f - .95f) * 1.72f)) - bottom;
            _crest.AddChild(new MeshInstance3D { Name = $"VioletShaft_{i}", Position = new Vector3(x, bottom + height * .5f, (i % 4) * 3.1f),
                Mesh = Ribbon(1.4f + (i % 3) * 1.3f, height, i), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        _crest.AddChild(new FogVolume { Name = "VioletThresholdBreath", Position = new Vector3(0, 5, 0), Size = new Vector3(92, 12, 18),
            Shape = RenderingServer.FogVolumeShape.Ellipsoid,
            Material = new FogMaterial { Density = .008f, Albedo = new Color(.38f, .22f, .58f), Emission = new Color(.11f, .015f, .28f), EdgeFade = .25f } });
        foreach (float x in new[] { -48f, 0f, 48f })
            _crest.AddChild(new OmniLight3D { Position = new Vector3(x, 12, 0), LightColor = new Color(.43f, .12f, 1), LightEnergy = 2f, OmniRange = 22f, LightVolumetricFogEnergy = 1.8f });
    }

    private static ArrayMesh Ribbon(float width, float height, int phase)
    {
        var tool = new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(float u, float v) => new((u - .5f) * width + MathF.Sin(v * 8 + phase) * 1.8f + MathF.Sin(v * 17 + phase * 1.7f) * .35f, (v - .5f) * height, MathF.Sin(v * 5 + phase) * .8f);
        for (int row = 0; row < 64; row++)
            foreach (var uv in new[] { new Vector2(0, row / 64f), new Vector2(1, row / 64f), new Vector2(1, (row + 1) / 64f),
                new Vector2(0, row / 64f), new Vector2(1, (row + 1) / 64f), new Vector2(0, (row + 1) / 64f) })
            { tool.SetUV(uv);tool.SetNormal(Vector3.Back);tool.AddVertex(Point(uv.X, uv.Y)); }
        return tool.Commit();
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        float energy = Mathf.Lerp(2.3f, .25f, SkyView.DaylightLevel);
        foreach (var lamp in _lamps) lamp.LightEnergy = energy;
        foreach (var shaft in _shafts) shaft.LightEnergy = 22f * SkyView.DaylightLevel * Mathf.SmoothStep(.35f, .65f, SkyView.DaylightLevel);
        foreach (var haze in _shaftHaze) haze.Visible = SkyView.DaylightLevel > .35f;
        if (!_airProbeLogged && _elapsed > .5f)
        {
            _airProbeLogged = true;
            var air = GetWorld3D().Environment;
            GD.Print($"SEDGE_AIR volumetric={air?.VolumetricFogEnabled} density={air?.VolumetricFogDensity} length={air?.VolumetricFogLength} daylight={SkyView.DaylightLevel} shaft_energy={_shafts[0].LightEnergy} fog_energy={_shafts[0].LightVolumetricFogEnergy}");
        }
        foreach (var rain in _rain)
        {
            rain.Direction = (Vector3.Down + WindField.Direction * (.25f + WindField.Strength * .55f)).Normalized();
            rain.Gravity = Vector3.Down * 9.8f + WindField.Direction * WindField.Strength * 5f;
        }
        foreach (var (bank, origin) in _mist)
            bank.Position = origin + WindField.Direction * MathF.Sin(_elapsed * .018f + origin.Z * .002f) * 8;
    }

    private static IEnumerable<MeshInstance3D> Meshes(Node node)
    {
        if (node is MeshInstance3D mesh) yield return mesh;
        foreach (var child in node.GetChildren()) foreach (var nested in Meshes(child)) yield return nested;
    }
}
