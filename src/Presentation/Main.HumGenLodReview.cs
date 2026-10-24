using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Opt-in evidence only: normal game figures, material/motion loader and world.
    private async Task RunHumGenLodReview(string directory, Camera3D camera)
    {
        _hud.Visible = false;
        _avatar.Visible = false;
        _npcs.Visible = false;
        using var cast = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://Art/humgen_cast.json"));
        var rows = new List<object>();
        var feet = _avatar.GlobalPosition + Vector3.Up * 2.5f;
        var platform = new MeshInstance3D
        {
            Position = feet - Vector3.Up * .04f,
            Mesh = new BoxMesh { Size = new Vector3(3, .08f, 3) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.18f, .2f, .2f), Roughness = 1 },
        };
        AddChild(platform);
        camera.Fov = 40;
        foreach (var body in cast.RootElement.GetProperty("models").EnumerateArray())
        {
            string id = body.GetProperty("id").GetString()!;
            string person = _bindings.People.FirstOrDefault(p => p.Value.Model == id).Key ?? "player";
            var figure = SkinnedFigure.Create(_art, _bindings, person, id)
                ?? throw new InvalidDataException("LOD gate missing native figure: " + id);
            AddChild(figure);
            figure.SetStance(CombatStance.AtRest);
            figure.Pose(feet, 0, 0, 0);
            foreach (float distance in new[] { 2f, 10f, 40f, 10f })
            {
                camera.GlobalPosition = feet + new Vector3(0, 1.2f, MathF.Sqrt(distance * distance - 1.44f));
                camera.LookAt(feet + Vector3.Up);
                await Rcd3Wait(.35);
                var meshes = figure.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
                    .Where(m => m.Name.ToString().Contains("_LOD", StringComparison.Ordinal)).ToArray();
                foreach (var part in meshes.GroupBy(m => m.Name.ToString()[..m.Name.ToString().LastIndexOf("_LOD", StringComparison.Ordinal)]))
                    if (part.Count(m => m.Visible) != 1)
                        throw new InvalidDataException("LOD overlap or hole: " + id + "/" + part.Key);
                rows.Add(new { id, distance, camera = camera.GlobalPosition.ToString(), feet = feet.ToString(),
                    shown = meshes.Where(m => m.Visible).Select(m => m.Name.ToString()).ToArray(),
                    total_meshes = meshes.Length, engine_max_fps = Engine.MaxFps });
                await Rcd3Shot(directory, "lod_" + id + "_" + distance + "m");
            }
            figure.Free();
        }
        platform.Free();
        // Same world/camera/settings and normal simulation pacing. Do not impose a
        // simulation-tick frame cap or infer whole-game speed from the isolated rig test.
        _avatar.Visible = true;
        _npcs.Visible = true;
        camera.GlobalTransform = _camera.Camera.GlobalTransform;
        var people = _npcs.GetChildren().OfType<SkinnedFigure>().ToList();
        if (_avatar is SkinnedFigure player) people.Add(player);
        var performance = new List<object>();
        try
        {
            foreach (bool all in new[] { true, false, true, false })
            {
                foreach (var person in people) person.ReviewAllLods(all);
                await Rcd3Wait(1);
                var milliseconds = new List<double>();
                ulong previous = Time.GetTicksUsec();
                for (int frame = 0; frame < 180; frame++)
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    ulong now = Time.GetTicksUsec();
                    milliseconds.Add((now - previous) / 1000.0);
                    previous = now;
                }
                double[] sorted = milliseconds.Order().ToArray();
                performance.Add(new { arm = all ? "all_four_diagnostic" : "exclusive", characters = people.Count,
                    frame_ms = milliseconds, mean_ms = milliseconds.Average(), median_ms = sorted[90], p95_ms = sorted[171],
                    engine_max_fps = Engine.MaxFps, viewport = GetViewport().GetVisibleRect().Size.ToString(),
                    camera = camera.GlobalTransform.ToString(), visual = VisualOptions.All,
                    draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
                    primitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) });
                await Rcd3Shot(directory, all ? "lod_world_all_four_before" : "lod_world_exclusive_after");
            }
        }
        finally { foreach (var person in people) person.ReviewAllLods(false); }
        File.WriteAllText(Path.Combine(directory, "humgen_lod_review.json"), JsonSerializer.Serialize(new
        {
            status = "PACKAGED_EXCLUSIVITY_PASS_FRAMES_REQUIRE_CLAUDE_REVIEW",
            staging = "Normal game figures on raised inspection platform; production camera/materials/motion",
            rows,
            performance,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
