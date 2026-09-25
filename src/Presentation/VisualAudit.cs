// UNNAMED Presentation - the visual audit: pictures of chosen assets as the game draws them, and alone on a neutral stage, with what was drawn
// Godot presentation only: it moves a camera and reads the scene; it submits no command and changes nothing the game draws (D-11)

using System.Text.Json;
using Godot;
using UNNAMED.Application;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>--visual-audit dir</c>: the game booted as a player gets it (the same session, the hollow with its art bindings, NPCs, creatures, the
/// game's lighting and camera settings), the HUD hidden, and a camera stood at each shot's eye looking at its target - a 1920x1080 picture
/// and a JSON dump of what is drawn for the target (mesh by mesh: the level shown at that distance, triangles, each surface's material and
/// textures, greybox or not) and of the renderer settings in effect. Then each sample alone on a neutral grey stage in a 1536x1536 view,
/// loaded the way the game loads it, with the same dump. A harness: it may name what it shows.
/// </summary>
public sealed class VisualAudit
{
    private const int WarmupFrames = 150;
    private const int SettleFrames = 75;
    private const float IsoFov = 35f;
    private const float IsoAzimuth = 45f;
    private const float IsoElevation = 35f;

    /// <summary>A picture: the camera at an eye (x, height above the ground, z) looking at a target (the same), and the nodes it is of.</summary>
    internal sealed record Shot(string Name, string Sample, (float X, float H, float Z) Eye, (float X, float H, float Z) Target, string[] Targets, string Says);

    // Gameplay shots at a player's distance: the third-person camera stands 3.5 m behind a body, about 2.65 m up (eye 1.62 m, pitch -0.3).
    /// <summary>The in-world shots (the A/B harness, <see cref="VisualAuditAB"/>, takes the same ones).</summary>
    internal static readonly Shot[] WorldShots =
    {
        new Shot("world_1_longhouse_far", "longhouse", (60.5f, 2.65f, 120f), (44f, 2.2f, 128f), new[] { "longhouse", "door.longhouse_door", "Terrain" },
            "Renn's lodge from the south-east, 18 m from its centre"),
        new Shot("world_1_longhouse_door", "longhouse", (59f, 2.65f, 128f), (52f, 1.6f, 128f), new[] { "longhouse", "door.longhouse_door", "Terrain" },
            "The lodge's east door from 7 m"),
        new Shot("world_2_flora_oak_tree_far", "flora_oak_tree", (98f, 2.65f, 156f), (108f, 6.5f, 158f), new[] { "tree_01", "Terrain" },
            "tree_01 (an oak by the id hash) from 10 m"),
        new Shot("world_2_flora_oak_tree_trunk", "flora_oak_tree", (104.2f, 2.65f, 157f), (108f, 2.2f, 158f), new[] { "tree_01", "Terrain" },
            "tree_01's trunk from 4 m"),
        new Shot("world_3_prop_cart_damaged_merchant", "prop_cart_damaged_merchant", (151.8f, 2.65f, 159f), (157f, 0.7f, 162.2f),
            new[] { "cart_wreck", "Terrain" }, "The merchant cart wreck from 6 m"),
        new Shot("world_4_rock_boulder", "rock_boulder", (15.8f, 2.65f, 85.5f), (10f, 1.0f, 84f), new[] { "rock_rim_01", "Terrain" },
            "rock_rim_01 (bound to rock_boulder) from 6 m"),
        new Shot("world_5_container_chest_iron_banded", "container_chest_iron_banded", (39.2f, 2.3f, 139.5f), (38f, 0.35f, 137f),
            new[] { "container.waystation_chest", "Terrain" }, "The waystation chest from 3 m"),
        new Shot("world_6_npc_veth_magistrate", "npc_veth_magistrate", (49f, 1.75f, 126.2f), (46.5f, 1.45f, 126.2f),
            new[] { "npc.ashen_hollow.renn_vale", "longhouse" }, "Renn Vale inside the lodge from 2.5 m, as the game lights it"),
        new Shot("world_7_establishing_waystation", "establishing", (27.9f, 2.65f, 160.8f), (44f, 1.8f, 134f),
            new[] { "longhouse", "forge", "container.waystation_chest", "rock_waystone", "Player", "Terrain" },
            "The waystation from the spawn stone, 3.5 m behind the body at spawn"),
        new Shot("world_8_establishing_charwood", "establishing", (124f, 2.65f, 158f), (150f, 3f, 164f),
            new[] { "tree_07", "tree_08", "tree_11", "tree_01", "tree_03", "Terrain" }, "Inside the Charwood, looking east"),
    };

    private sealed record Sample(int N, string Id, string Says, Func<Node3D?> Load);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly Node3D _root;
    private readonly GameSession _session;
    private readonly Art.ArtLibrary _art;
    private readonly Art.ArtBindings _bindings;
    private readonly CameraRig _rig;
    private readonly string _raw;
    private readonly List<Shot> _shots = new();
    private readonly List<Sample> _samples = new();
    private readonly List<Dictionary<string, object?>> _log = new();
    private readonly Dictionary<ulong, Dictionary<string, object?>> _textureCache = new();
    private Camera3D? _camera;
    private SubViewport? _stage;
    private Camera3D? _stageCamera;
    private MeshInstance3D? _floor;
    private Node3D? _sample;
    private Figure? _figure;
    private int _frame;
    private int _index;
    private int _wait;

    public VisualAudit(Node3D root, GameSession session, Art.ArtLibrary art, Art.ArtBindings bindings, CameraRig rig, string directory)
    {
        _root = root;
        _session = session;
        _art = art;
        _bindings = bindings;
        _rig = rig;
        Directory = directory;
        _raw = Path.Combine(directory, "raw");
        System.IO.Directory.CreateDirectory(_raw);
        DisplayServer.WindowSetSize(new Vector2I(1920, 1080));

        _shots.AddRange(WorldShots);

        _samples.AddRange(new[]
        {
            new Sample(1, "longhouse", "longhouse (ArtLibrary.ModelWithLods, as Fitting.Building loads it)", () => _art.ModelWithLods("longhouse")),
            new Sample(2, "flora_oak_tree", "flora_oak_tree (ArtLibrary.ModelWithLods, as Fitting.Structure loads it)", () => _art.ModelWithLods("flora_oak_tree")),
            new Sample(3, "prop_cart_damaged_merchant", "prop_cart_damaged_merchant (ModelWithLods)", () => _art.ModelWithLods("prop_cart_damaged_merchant")),
            new Sample(4, "rock_boulder", "rock_boulder (ModelWithLods; the game refuses it at the rim rocks)", () => _art.ModelWithLods("rock_boulder")),
            new Sample(5, "container_chest_iron_banded", "container_chest_iron_banded (ModelWithLods, as Fitting.Site loads it)",
                () => _art.ModelWithLods("container_chest_iron_banded")),
            new Sample(6, "npc_veth_magistrate", "Renn Vale (SkinnedFigure.Create: the rigged model and its idle clip)",
                () => Art.SkinnedFigure.Create(_art, _bindings, "npc.ashen_hollow.renn_vale")),
        });
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
            if (_index < _shots.Count)
            {
                WorldShot(_shots[_index]);
                return null;
            }
            if (_index < _shots.Count + _samples.Count)
            {
                Isolated(_samples[_index - _shots.Count]);
                return null;
            }
            Write("visual_audit_log.json", new Dictionary<string, object?>
            {
                ["pictures"] = _log,
                ["art_problems"] = _art.Problems,
                ["art_used"] = _art.Used.OrderBy(u => u, StringComparer.Ordinal).ToList(),
                ["measurement_control"] = MeasurementControl(),
            });
            GD.Print($"UNNAMED visual audit written to {Directory}");
            return "done";
        }
        catch (Exception e)
        {
            GD.PushError($"UNNAMED visual audit failed: {e}");
            return "failed";
        }
    }

    // ── in the world ────────────────────────────────────────────────────────

    private void WorldShot(Shot shot)
    {
        if (_camera is null)
        {
            // A camera like the player's: its field of view, near and far planes, in the same viewport and world.
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
        if (++_wait < SettleFrames)
            return;
        _wait = 0;
        _index++;
        var image = _root.GetViewport().GetTexture().GetImage();
        image.SavePng(Path.Combine(_raw, shot.Name + ".png"));
        var dump = new Dictionary<string, object?>
        {
            ["shot"] = shot.Name,
            ["sample"] = shot.Sample,
            ["says"] = shot.Says,
            ["image_size"] = new[] { image.GetWidth(), image.GetHeight() },
            ["camera"] = CameraDump(_camera),
            ["target_point"] = V(target),
            ["distance_to_target_point_m"] = Math.Round(eye.DistanceTo(target), 2),
            ["targets"] = shot.Targets.Select(t => TargetDump(t, _camera.GlobalPosition)).ToList(),
            ["renderer"] = RendererDump(_root.GetViewport()),
        };
        Write(shot.Name + ".json", dump);
        _log.Add(new Dictionary<string, object?> { ["png"] = shot.Name + ".png", ["says"] = shot.Says, ["eye"] = V(eye), ["target"] = V(target) });
    }

    private Vector3 Ground((float X, float H, float Z) at)
    {
        var terrain = _session.Setup.Layout.Space.Terrain;
        float ground = terrain.HeightAtMm((long)(at.X * 1000), (long)(at.Z * 1000)) / 1000f;
        return new Vector3(at.X, ground + at.H, at.Z);
    }

    /// <summary>A target by its node name, searched under the hollow, the items, the NPCs and the scene root.</summary>
    private Node3D? FindTarget(string name)
    {
        string alt = name.Replace('.', '_');
        foreach (var parent in new[] { "Hollow", "Items", "Npcs", "Creatures", "Crafting" }.Select(p => _root.GetNodeOrNull(p)).Append(_root))
        {
            if (parent is null)
                continue;
            foreach (var child in parent.GetChildren())
            {
                if (child is Node3D node && (child.Name == name || child.Name == alt))
                    return node;
            }
        }
        return null;
    }

    private Dictionary<string, object?> TargetDump(string name, Vector3 cameraAt)
    {
        var node = FindTarget(name);
        var result = new Dictionary<string, object?> { ["target"] = name, ["found"] = node is not null };
        string? bound = name switch
        {
            "longhouse" => _bindings.Buildings.GetValueOrDefault("longhouse_")?.Model,
            "forge" => _bindings.Buildings.GetValueOrDefault("forge_")?.Model,
            "door.longhouse_door" => _bindings.Doors.GetValueOrDefault("door.longhouse")?.Model,
            _ when name.StartsWith("container.", StringComparison.Ordinal) => _bindings.Containers.GetValueOrDefault(name)?.Model,
            _ when name.StartsWith("npc.", StringComparison.Ordinal) => _bindings.People.GetValueOrDefault(name)?.Model,
            _ => _bindings.Structure(name)?.Model,
        };
        result["bound_model"] = bound;
        result["art_problems"] = _art.Problems.Where(p => (bound is not null && p.Key.StartsWith(bound, StringComparison.Ordinal)) || p.Key.EndsWith(" " + name, StringComparison.Ordinal))
            .ToDictionary(p => p.Key, p => p.Value);
        if (node is null)
            return result;
        result["node_path"] = node.GetPath().ToString();
        result["node_class"] = node.GetClass();
        result["global_position"] = V(node.GlobalPosition);
        result["global_scale"] = V(node.GlobalTransform.Basis.Scale);
        var meshes = MeshesUnder(node, cameraAt);
        result["meshes"] = meshes;
        result["greybox_fallback"] = meshes.Count > 0 && meshes.Where(m => (bool)m["drawn"]!).All(m => (bool)m["greybox"]!);
        result["drawn_triangles"] = meshes.Where(m => (bool)m["drawn"]!).Sum(m => (int)m["triangles"]!);
        result["drawn_lod_levels"] = meshes.Where(m => (bool)m["drawn"]!).Select(m => m["lod_level"]).Distinct().ToList();
        return result;
    }

    /// <summary>Every mesh under a node: whether it is drawn at this camera distance, which level of its chain it is, and its surfaces.</summary>
    private List<Dictionary<string, object?>> MeshesUnder(Node3D node, Vector3 cameraAt)
    {
        var all = (node is MeshInstance3D self ? new[] { self } : Array.Empty<MeshInstance3D>())
            .Concat(node.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>()).ToList();
        var begins = all.Select(m => m.VisibilityRangeBegin).Distinct().OrderBy(b => b).ToList();
        var list = new List<Dictionary<string, object?>>();
        foreach (var mesh in all)
        {
            var aabb = mesh.GlobalTransform * mesh.GetAabb();
            float distance = cameraAt.DistanceTo(aabb.GetCenter());
            bool inRange = (mesh.VisibilityRangeBegin <= 0 || distance >= mesh.VisibilityRangeBegin)
                && (mesh.VisibilityRangeEnd <= 0 || distance < mesh.VisibilityRangeEnd);
            bool drawn = mesh.Mesh is not null && mesh.IsVisibleInTree() && inRange && mesh.CastShadow != GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
            var entry = new Dictionary<string, object?>
            {
                ["path"] = node.GetPathTo(mesh).ToString(),
                ["mesh_class"] = mesh.Mesh?.GetClass(),
                ["mesh_name"] = mesh.Mesh?.ResourceName,
                ["drawn"] = drawn,
                ["visible_in_tree"] = mesh.IsVisibleInTree(),
                ["cast_shadow"] = mesh.CastShadow.ToString(),
                ["distance_to_aabb_centre_m"] = Math.Round(distance, 2),
                ["visibility_range"] = new[] { mesh.VisibilityRangeBegin, mesh.VisibilityRangeEnd },
                ["visibility_range_margins"] = new[] { mesh.VisibilityRangeBeginMargin, mesh.VisibilityRangeEndMargin },
                ["lod_level"] = begins.Count > 1 ? begins.IndexOf(mesh.VisibilityRangeBegin) : 0,
                ["greybox"] = mesh.Mesh is PrimitiveMesh,
                ["global_scale"] = V(mesh.GlobalTransform.Basis.Scale),
                ["aabb_size_m"] = V(aabb.Size),
                ["triangles"] = mesh.Mesh is { } m ? Triangles(m) : 0,
                ["material_override"] = mesh.MaterialOverride?.GetClass(),
                ["surfaces"] = mesh.Mesh is null ? new List<Dictionary<string, object?>>()
                    : Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Select(s => SurfaceDump(mesh, s)).ToList(),
            };
            if (mesh is { Skeleton: { IsEmpty: false } skeleton })
                entry["skeleton"] = skeleton.ToString();
            list.Add(entry);
        }
        return list;
    }

    private static int Triangles(Mesh mesh)
    {
        int total = 0;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            total += SurfaceTriangles(mesh, s);
        return total;
    }

    private static int SurfaceTriangles(Mesh mesh, int s)
    {
        if (mesh is ArrayMesh array)
        {
            int indices = array.SurfaceGetArrayIndexLen(s);
            return (indices > 0 ? indices : array.SurfaceGetArrayLen(s)) / 3;
        }
        var arrays = mesh.SurfaceGetArrays(s);
        var index = arrays[(int)Mesh.ArrayType.Index];
        if (index.VariantType != Variant.Type.Nil && index.AsInt32Array().Length > 0)
            return index.AsInt32Array().Length / 3;
        return arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length / 3;
    }

    private Dictionary<string, object?> SurfaceDump(MeshInstance3D mesh, int s)
    {
        var material = mesh.GetActiveMaterial(s);
        var entry = new Dictionary<string, object?>
        {
            ["surface"] = s,
            ["triangles"] = SurfaceTriangles(mesh.Mesh!, s),
            ["material_class"] = material?.GetClass(),
            ["material_name"] = material?.ResourceName,
        };
        if (material is BaseMaterial3D b)
        {
            entry["shading_mode"] = b.ShadingMode.ToString();
            entry["albedo_color"] = b.AlbedoColor.ToHtml();
            entry["albedo_texture"] = TextureDump(b.AlbedoTexture);
            entry["texture_filter"] = b.TextureFilter.ToString();
            entry["texture_repeat"] = b.TextureRepeat;
            entry["normal_enabled"] = b.NormalEnabled;
            entry["normal_scale"] = b.NormalScale;
            entry["normal_texture"] = TextureDump(b.NormalTexture);
            entry["roughness"] = b.Roughness;
            entry["roughness_texture"] = TextureDump(b.RoughnessTexture);
            entry["metallic"] = b.Metallic;
            entry["metallic_specular"] = b.MetallicSpecular;
            entry["metallic_texture"] = TextureDump(b.MetallicTexture);
            entry["ao_enabled"] = b.Get("ao_enabled").AsBool();
            entry["ao_texture"] = TextureDump(b.Get("ao_texture").As<Texture2D>());
            if (material is OrmMaterial3D orm)
                entry["orm_texture"] = TextureDump(orm.OrmTexture);
            entry["transparency"] = b.Transparency.ToString();
            entry["alpha_scissor_threshold"] = b.AlphaScissorThreshold;
            entry["alpha_antialiasing_mode"] = b.Get("alpha_antialiasing_mode").AsInt32();
            entry["cull_mode"] = b.CullMode.ToString();
            entry["uv1_scale"] = V(b.Uv1Scale);
            entry["uv1_triplanar"] = b.Uv1Triplanar;
            entry["uv1_world_triplanar"] = b.Uv1WorldTriplanar;
            entry["vertex_color_use_as_albedo"] = b.VertexColorUseAsAlbedo;
            entry["specular_mode"] = b.SpecularMode.ToString();
            entry["diffuse_mode"] = b.DiffuseMode.ToString();
            entry["heightmap_enabled"] = b.Get("heightmap_enabled").AsBool();
            entry["detail_enabled"] = b.Get("detail_enabled").AsBool();
        }
        else if (material is ShaderMaterial shader)
        {
            entry["shader"] = shader.Shader?.ResourcePath;
        }
        return entry;
    }

    private Dictionary<string, object?>? TextureDump(Texture2D? texture)
    {
        if (texture is null)
            return null;
        if (_textureCache.TryGetValue(texture.GetInstanceId(), out var cached))
            return cached;
        var image = texture.GetImage();
        var entry = new Dictionary<string, object?>
        {
            ["class"] = texture.GetClass(),
            ["size"] = new[] { texture.GetWidth(), texture.GetHeight() },
            ["format"] = image?.GetFormat().ToString(),
            ["has_mipmaps"] = image?.HasMipmaps(),
            ["mipmap_count"] = image?.GetMipmapCount(),
            ["resource_path"] = texture.ResourcePath,
            ["resource_name"] = texture.ResourceName,
        };
        _textureCache[texture.GetInstanceId()] = entry;
        return entry;
    }

    /// <summary>The texture dump run on two textures of known make, one with mipmaps and one without: the measurement is read the same way.</summary>
    private Dictionary<string, object?> MeasurementControl()
    {
        var with = Image.CreateEmpty(64, 64, false, Image.Format.Rgb8);
        with.GenerateMipmaps();
        var without = Image.CreateEmpty(64, 64, false, Image.Format.Rgb8);
        return new Dictionary<string, object?>
        {
            ["image_texture_made_with_mipmaps"] = TextureDump(ImageTexture.CreateFromImage(with)),
            ["image_texture_made_without_mipmaps"] = TextureDump(ImageTexture.CreateFromImage(without)),
        };
    }

    private static Dictionary<string, object?> CameraDump(Camera3D camera) => new()
    {
        ["position"] = V(camera.GlobalPosition),
        ["fov_deg"] = camera.Fov,
        ["keep_aspect"] = camera.KeepAspect.ToString(),
        ["near"] = camera.Near,
        ["far"] = camera.Far,
        ["attributes"] = camera.Attributes?.GetClass(),
        ["environment"] = camera.Environment?.GetClass(),
    };

    /// <summary>The renderer in effect: the project's rendering settings, the viewport's, the world environment and the sun.</summary>
    private Dictionary<string, object?> RendererDump(Viewport viewport)
    {
        var settings = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in ProjectSettings.Singleton.GetPropertyList())
        {
            string name = property["name"].AsString();
            if (name.StartsWith("rendering/", StringComparison.Ordinal))
                settings[name] = Str(ProjectSettings.GetSetting(name));
        }
        string project = Godot.FileAccess.GetFileAsString("res://project.godot");
        var environment = _root.FindChildren("*", nameof(WorldEnvironment), true, false).Cast<WorldEnvironment>().FirstOrDefault()?.Environment;
        var sun = _root.FindChildren("*", nameof(DirectionalLight3D), true, false).Cast<DirectionalLight3D>().ToList();
        var lights = _root.FindChildren("*", nameof(Light3D), true, false).Cast<Light3D>().ToList();
        return new Dictionary<string, object?>
        {
            ["rendering_method"] = RenderingServer.GetCurrentRenderingMethod(),
            ["rendering_driver"] = RenderingServer.GetCurrentRenderingDriverName(),
            ["adapter"] = RenderingServer.GetVideoAdapterName(),
            ["project_godot_has_rendering_section"] = project.Contains("[rendering]", StringComparison.Ordinal),
            ["viewport"] = new Dictionary<string, object?>
            {
                ["size"] = V2(viewport.GetVisibleRect().Size),
                ["msaa_3d"] = viewport.Msaa3D.ToString(),
                ["screen_space_aa"] = viewport.ScreenSpaceAA.ToString(),
                ["use_taa"] = viewport.UseTaa,
                ["use_debanding"] = viewport.UseDebanding,
                ["scaling_3d_mode"] = viewport.Scaling3DMode.ToString(),
                ["scaling_3d_scale"] = viewport.Scaling3DScale,
                ["texture_mipmap_bias"] = viewport.TextureMipmapBias,
                ["anisotropic_filtering_level"] = Str(viewport.Get("anisotropic_filtering_level")),
                ["mesh_lod_threshold"] = viewport.MeshLodThreshold,
                ["use_occlusion_culling"] = viewport.UseOcclusionCulling,
                ["positional_shadow_atlas_size"] = viewport.PositionalShadowAtlasSize,
            },
            ["environment"] = environment is null ? null : new Dictionary<string, object?>
            {
                ["summary"] = new Dictionary<string, object?>
                {
                    ["background_mode"] = environment.BackgroundMode.ToString(),
                    ["sky_material"] = environment.Sky?.SkyMaterial?.GetClass(),
                    ["ambient_light_source"] = environment.AmbientLightSource.ToString(),
                    ["ambient_light_energy"] = environment.AmbientLightEnergy,
                    ["ambient_light_sky_contribution"] = environment.AmbientLightSkyContribution,
                    ["reflected_light_source"] = environment.ReflectedLightSource.ToString(),
                    ["tonemap_mode"] = environment.TonemapMode.ToString(),
                    ["tonemap_exposure"] = environment.TonemapExposure,
                    ["tonemap_white"] = environment.TonemapWhite,
                    ["ssao_enabled"] = environment.SsaoEnabled,
                    ["ssil_enabled"] = environment.SsilEnabled,
                    ["sdfgi_enabled"] = environment.SdfgiEnabled,
                    ["glow_enabled"] = environment.GlowEnabled,
                    ["fog_enabled"] = environment.FogEnabled,
                    ["fog_density"] = environment.FogDensity,
                    ["volumetric_fog_enabled"] = environment.VolumetricFogEnabled,
                    ["adjustment_enabled"] = environment.AdjustmentEnabled,
                    ["voxel_gi_nodes"] = _root.FindChildren("*", nameof(VoxelGI), true, false).Count,
                    ["lightmap_gi_nodes"] = _root.FindChildren("*", nameof(LightmapGI), true, false).Count,
                    ["reflection_probes"] = _root.FindChildren("*", nameof(ReflectionProbe), true, false).Count,
                },
                ["all_properties"] = Props(environment),
                ["sky_material_properties"] = environment.Sky?.SkyMaterial is { } sky ? Props(sky) : null,
            },
            ["lights"] = lights.Select(l => new Dictionary<string, object?>
            {
                ["path"] = l.GetPath().ToString(),
                ["class"] = l.GetClass(),
                ["energy"] = l.LightEnergy,
                ["shadow_enabled"] = l.ShadowEnabled,
                ["rotation_deg"] = V(l.GlobalRotationDegrees),
                ["directional_shadow_mode"] = l is DirectionalLight3D d ? d.DirectionalShadowMode.ToString() : null,
                ["all_properties"] = Props(l),
            }).ToList(),
            ["directional_lights"] = sun.Count,
            ["project_rendering_settings"] = settings,
        };
    }

    // ── alone on the stage ──────────────────────────────────────────────────

    private void Isolated(Sample sample)
    {
        if (_stage is null)
            BuildStage();
        if (_sample is null)
        {
            if (sample.Load() is not { } model)
            {
                Write($"iso_{sample.N}_{sample.Id}.json", new Dictionary<string, object?> { ["sample"] = sample.Id, ["loaded"] = false, ["art_problems"] = _art.Problems });
                _index++;
                return;
            }
            _sample = new Node3D { Name = sample.Id };
            _sample.AddChild(model);
            _stage!.AddChild(_sample);
            _figure = model as Figure;
            Pose();
            var bounds = Art.ArtGallery.Bounds(_sample);
            // Stood on the floor, centred.
            model.Position = new Vector3(-bounds.GetCenter().X, -bounds.Position.Y, -bounds.GetCenter().Z);
            bounds = Art.ArtGallery.Bounds(_sample);
            float radius = bounds.Size.Length() / 2;
            float az = Mathf.DegToRad(IsoAzimuth), el = Mathf.DegToRad(IsoElevation);
            var direction = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
            var centre = bounds.GetCenter();
            float distance = FitDistance(bounds, direction);
            _stageCamera!.Far = distance * 6;
            _stageCamera.Near = Math.Max(0.02f, distance / 500);
            _stageCamera.LookAtFromPosition(centre + direction * distance, centre, Vector3.Up);
            _floor!.Scale = Vector3.One * Math.Max(40f, radius * 60);
            return;
        }
        Pose();
        if (++_wait < SettleFrames)
            return;
        _wait = 0;
        _index++;
        var image = _stage!.GetTexture().GetImage();
        image.SavePng(Path.Combine(_raw, $"iso_{sample.N}_{sample.Id}.png"));
        var boundsNow = Art.ArtGallery.Bounds(_sample);
        var meshes = MeshesUnder(_sample, _stageCamera!.GlobalPosition);
        Write($"iso_{sample.N}_{sample.Id}.json", new Dictionary<string, object?>
        {
            ["sample"] = sample.Id,
            ["says"] = sample.Says,
            ["loaded"] = true,
            ["image_size"] = new[] { image.GetWidth(), image.GetHeight() },
            ["stage"] = new Dictionary<string, object?>
            {
                ["camera_fov_deg"] = IsoFov, ["azimuth_deg"] = IsoAzimuth, ["elevation_deg"] = IsoElevation,
                ["camera"] = CameraDump(_stageCamera), ["bounds_size_m"] = V(boundsNow.Size), ["bounds_centre"] = V(boundsNow.GetCenter()),
            },
            ["meshes"] = meshes,
            ["drawn_triangles"] = meshes.Where(m => (bool)m["drawn"]!).Sum(m => (int)m["triangles"]!),
            ["all_triangles_by_lod"] = meshes.GroupBy(m => m["lod_level"]).ToDictionary(g => $"lod{g.Key}", g => g.Sum(m => (int)m["triangles"]!)),
            ["renderer_viewport"] = new Dictionary<string, object?>
            {
                ["msaa_3d"] = _stage.Msaa3D.ToString(), ["screen_space_aa"] = _stage.ScreenSpaceAA.ToString(), ["use_taa"] = _stage.UseTaa,
                ["scaling_3d_mode"] = _stage.Scaling3DMode.ToString(), ["scaling_3d_scale"] = _stage.Scaling3DScale,
            },
            ["art_problems"] = _art.Problems.Where(p => p.Key.StartsWith(sample.Id, StringComparison.Ordinal)).ToDictionary(p => p.Key, p => p.Value),
        });
        _log.Add(new Dictionary<string, object?> { ["png"] = $"iso_{sample.N}_{sample.Id}.png", ["says"] = sample.Says });
        _sample.QueueFree();
        _sample = null;
        _figure = null;
    }

    /// <summary>
    /// How far along a direction the camera stands so every corner of the bounds is inside the square view, with 8 % to spare (the neutral
    /// Blender render uses the same rule).
    /// </summary>
    private static float FitDistance(Aabb bounds, Vector3 direction)
    {
        var right = Vector3.Up.Cross(direction).Normalized();
        var up = direction.Cross(right).Normalized();
        float t = Mathf.Tan(Mathf.DegToRad(IsoFov / 2));
        float distance = 0;
        for (int i = 0; i < 8; i++)
        {
            var q = bounds.GetEndpoint(i) - bounds.GetCenter();
            distance = Math.Max(distance, q.Dot(direction) + Math.Max(Math.Abs(q.Dot(right)), Math.Abs(q.Dot(up))) / t);
        }
        return distance * 1.08f;
    }

    private void Pose()
    {
        if (_figure is null)
            return;
        _figure.SetStance(CombatStance.AtRest);
        _figure.SetTalking(false);
        _figure.Pose(_figure.Position, 0f, 0f, 1 / 60.0);
    }

    /// <summary>A neutral stage in a world of its own: a mid-grey floor, a key and a fill light, flat grey ambient, the game's viewport settings.</summary>
    private void BuildStage()
    {
        var main = _root.GetViewport();
        _stage = new SubViewport
        {
            Name = "AuditStage", Size = new Vector2I(1536, 1536), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = main.Msaa3D, ScreenSpaceAA = main.ScreenSpaceAA, UseTaa = main.UseTaa, Scaling3DMode = main.Scaling3DMode, Scaling3DScale = main.Scaling3DScale,
            TransparentBg = false,
        };
        _root.AddChild(_stage);
        _stage.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.5f, 0.5f, 0.5f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.5f, 0.5f, 0.5f), AmbientLightEnergy = 0.6f,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
                TonemapMode = Godot.Environment.ToneMapper.Agx,
            },
        });
        // Key from the camera's left and above; fill from the other side, no shadow.
        _stage.AddChild(new DirectionalLight3D { Name = "Key", RotationDegrees = new Vector3(-50, -20, 0), LightEnergy = 1.6f, ShadowEnabled = true });
        _stage.AddChild(new DirectionalLight3D { Name = "Fill", RotationDegrees = new Vector3(-25, 150, 0), LightEnergy = 0.45f, ShadowEnabled = false });
        _floor = new MeshInstance3D
        {
            Name = "Floor", Mesh = new PlaneMesh { Size = new Vector2(1, 1) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.5f, 0.5f), Roughness = 1f },
        };
        _stage.AddChild(_floor);
        _stageCamera = new Camera3D { Name = "StageCamera", Fov = IsoFov, Current = true };
        _stage.AddChild(_stageCamera);
    }

    // ── writing ─────────────────────────────────────────────────────────────

    private void Write(string name, object value) => File.WriteAllText(Path.Combine(_raw, name), JsonSerializer.Serialize(value, Json));

    private static SortedDictionary<string, string> Props(GodotObject o)
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in o.GetPropertyList())
        {
            long usage = property["usage"].AsInt64();
            string name = property["name"].AsString();
            if ((usage & (long)PropertyUsageFlags.Storage) == 0 || name is "script" or "resource_local_to_scene")
                continue;
            d[name] = Str(o.Get(name));
        }
        return d;
    }

    private static string Str(Variant v) => v.VariantType switch
    {
        Variant.Type.Nil => "null",
        Variant.Type.Object => v.AsGodotObject() is { } g ? g.GetClass() + (g is Resource { ResourcePath.Length: > 0 } r ? " " + r.ResourcePath : "") : "null",
        _ => v.Obj?.ToString() ?? "null",
    };

    private static float[] V(Vector3 v) => new[] { (float)Math.Round(v.X, 3), (float)Math.Round(v.Y, 3), (float)Math.Round(v.Z, 3) };

    private static float[] V2(Vector2 v) => new[] { v.X, v.Y };
}
