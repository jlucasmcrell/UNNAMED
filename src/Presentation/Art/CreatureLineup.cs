using Godot;
using System.Text.Json;
using UNNAMED.Content;
using UNNAMED.Presentation.Greybox;

namespace UNNAMED.Presentation.Art;

/// <summary>Opt-in review of current bindings, not candidate creature replacements.</summary>
public partial class CreatureLineup(ArtLibrary art, ArtBindings bindings, string content, string directory) : Node3D
{
    public override async void _Ready()
    {
        Directory.CreateDirectory(directory);
        var rows = new List<object>();
        try
        {
            DisplayServer.WindowSetSize(new Vector2I(1280, 960));
            var loader = new ContentLoader();
            if (!loader.LoadAll(content)) throw new InvalidDataException("Lineup content did not load");
            var definitions = CombatContent.BuildCreatures(loader, 50);
            using var labels = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "labels.json")));
            art.Withhold(bindings.Withheld);
            AddChild(new WorldEnvironment { Environment = new Godot.Environment {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.48f, .54f, .59f),
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
                ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(.65f, .68f, .74f), AmbientLightEnergy = .5f,
                TonemapMode = Godot.Environment.ToneMapper.Agx, SsaoEnabled = true,
            } });
            AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-51, 30, 0), ShadowEnabled = true, LightEnergy = 1.2f });
            AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(60, 60) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.32f, .31f, .28f), Roughness = .95f } });
            var camera = new Camera3D { Current = true, Near = .03f, Far = 100 };
            AddChild(camera);
            foreach (var definition in definitions.Values.OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                if (OS.GetEnvironment("OTHERREACH_LINEUP_ONLY") is { Length: > 0 } only && !only.Split(',').Contains(definition.Id)) continue;
                if (File.Exists(@"C:\Otherreach_Recovery\OWNER_PLAYING.flag")) throw new OperationCanceledException("Owner playing");
                var look = bindings.Creatures.GetValueOrDefault(definition.Id);
                Node3D figure;
                bool fallback = look is null;
                if (fallback)
                {
                    var missing = new CreatureFigure(); missing.Build(definition); figure = missing;
                }
                else figure = SkinnedCreature.Create(art, bindings, definition, definition.Id)
                    ?? throw new InvalidDataException("Bound creature failed to draw: " + definition.Id);
                AddChild(figure);
                var models = figure.FindChildren("*", "", true, false).OfType<SkinnedModel>().ToArray();
                var box = ArtGallery.Bounds(figure);
                figure.Position -= Vector3.Up * box.Position.Y;
                var aim = figure.Position + box.GetCenter();
                var direction = new Vector3(1, .35f, 1).Normalized();
                camera.Position = aim + direction * 3;
                camera.LookAt(aim);
                // Keep distance exactly3m; widen only when the current authored size needs it.
                float radius = box.Size.Length() * .5f;
                camera.Fov = Mathf.Clamp(Mathf.RadToDeg(2 * Mathf.Atan(radius * 1.12f / 3)), 36, 115);
                for (int frame = 0; frame < 12; frame++)
                {
                    foreach (var model in models) model.Hold("idle", .25f, 0);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                // Large subjects need an honest surface stand-off; skinning can move vertices beyond the imported rest AABB.
                if (OS.GetEnvironment("OTHERREACH_LINEUP_ONLY").Length > 0)
                {
                    Aabb? posed = null;
                    foreach (var mesh in figure.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
                    {
                        if (mesh.Mesh is null || !mesh.IsVisibleInTree()) continue;
                        using var baked = mesh.BakeMeshFromCurrentSkeletonPose();
                        var b = mesh.GlobalTransform * baked.GetAabb();
                        posed = posed is { } prior ? prior.Merge(b) : b;
                    }
                    box = posed ?? box;
                    aim = box.GetCenter();
                    radius = box.Size.Length() * .5f;
                    camera.Position = aim + direction * (3 + radius);
                    camera.LookAt(aim);
                    camera.Fov = Mathf.Clamp(Mathf.RadToDeg(2 * Mathf.Atan(radius * 1.25f / (3 + radius))), 36, 100);
                    for (int frame = 0; frame < 12; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                string file = definition.Id.Replace('.', '_');
                Main.SaveScreenshot(GetViewport(), directory, file);
                var materials = figure.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
                    .Where(m => m.Mesh is not null).SelectMany(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).Select(s => {
                        var material = m.GetActiveMaterial(s);
                        var texture = material is BaseMaterial3D pbr ? pbr.AlbedoTexture
                            : material is ShaderMaterial shader ? shader.GetShaderParameter(
                                shader.ResourceName.EndsWith("_otherfauna", StringComparison.Ordinal) ? "albedo_tex" : "texture_albedo").AsGodotObject() as Texture2D : null;
                        return new { mesh = m.Name.ToString(), surface = s, material = material?.ResourceName,
                            albedo_width = texture?.GetWidth(), albedo_height = texture?.GetHeight() };
                    })).ToArray();
                rows.Add(new { id = definition.Id, name = labels.RootElement.GetProperty(definition.Id).GetString(),
                    model = look?.Model, fallback, png = file + ".png", camera_distance_m = camera.Position.DistanceTo(aim),
                    camera_fov = camera.Fov, angle_degrees = 45, sun_altitude_degrees = 51,
                    framing = OS.GetEnvironment("OTHERREACH_LINEUP_ONLY").Length > 0 ? "3m stand-off beyond posed bounding sphere" : "3m from rest bounds centre",
                    bounds = box.Size.ToString(), materials });
                figure.Free();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            File.WriteAllText(Path.Combine(directory, "lineup.json"), JsonSerializer.Serialize(new {
                scope = "Current Sedge plus accepted C1 bindings; native idle25percent, unchanged authored size; neutral noon review stage. No candidate replacements.",
                rows, art_used = art.Used, problems = art.Problems }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"UNNAMED art: {art.Used.Count} assets drawn from the library; {art.Problems.Count} withheld or unavailable");
            if (art.Problems.Count != 0) throw new InvalidDataException("Real-art lineup has unavailable assets");
            File.WriteAllText(Path.Combine(directory, "gate.exit"), "0");
            CaptureQuit.At(this, 0);
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(directory, "failure.txt"), e.ToString());
            File.WriteAllText(Path.Combine(directory, "gate.exit"), "1");
            GD.PushError(e.ToString()); CaptureQuit.At(this, 3);
        }
    }
}
