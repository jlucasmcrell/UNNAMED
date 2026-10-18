using System.Reflection;
using Godot;
using UNNAMED.Presentation.Greybox;

namespace UNNAMED.Presentation.Perf;

public partial class ScatterDrawBatchesCheck : Node
{
    public override async void _Ready()
    {
        try
        {
            var add = typeof(ScatterView).GetMethod("AddDrawBatchBands", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var update = typeof(ScatterView).GetMethod("UpdateDrawBatches", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var shader = new Shader { Code = "shader_type spatial; render_mode unshaded; varying vec3 tint; void vertex(){tint=INSTANCE_CUSTOM.rgb;} void fragment(){ALBEDO=tint;}" };
            var meshes = new Mesh[] { new BoxMesh { Size = new Vector3(.5f, 2, .5f), Material = new ShaderMaterial { Shader = shader } },
                new BoxMesh { Size = new Vector3(.8f, 1.5f, .8f), Material = new ShaderMaterial { Shader = shader } } };
            (SubViewport V, ScatterView S, Camera3D C) View(bool batched)
            {
                var v = new SubViewport { Size = new Vector2I(640, 360), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                AddChild(v);
                var camera = new Camera3D { Current = true, Far = 100 }; v.AddChild(camera);
                var scatter = new ScatterView(); v.AddChild(scatter);
                for (int z = -4; z < 4; z++) for (int x = -4; x < 4; x++)
                {
                    var placed = new List<(Transform3D T, Color C, int Form)>();
                    for (int i = 0; i < 9; i++) placed.Add((new Transform3D(Basis.FromEuler(new Vector3(.03f * i, .3f * i, 0)).Scaled(Vector3.One * (.7f + .05f * i)),
                        new Vector3(x * 4 + i % 3, 1, z * 4 + i / 3)), new Color(.2f + .08f * i, .6f, .8f - .06f * i, 1), 0));
                    var bands = new List<MultiMeshInstance3D>();
                    var bounds = new Aabb(new Vector3(x * 4 - 1, -1, z * 4 - 1), new Vector3(5, 5, 5));
                    for (int level = 0; level < 2; level++)
                    {
                        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = meshes[level], InstanceCount = placed.Count };
                        for (int i = 0; i < placed.Count; i++) { mm.SetInstanceTransform(i, placed[i].T); mm.SetInstanceCustomData(i, placed[i].C); }
                        var band = new MultiMeshInstance3D { Multimesh = mm, CustomAabb = bounds, VisibilityRangeBegin = level == 0 ? 0 : 15,
                            VisibilityRangeEnd = level == 0 ? MathF.BitDecrement(15) : 35, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                        scatter.AddChild(band); bands.Add(band);
                    }
                    if (batched) add.Invoke(scatter, new object[] { bands, placed });
                }
                return (v, scatter, camera);
            }
            var a = View(false); var b = View(true);
            var eyes = new[] { new Vector3(0, 7, 18), new Vector3(16, 7, 18), new Vector3(-16, 7, 4), new Vector3(0, 7, 38) };
            for (int n = 0; n < eyes.Length; n++)
            {
                foreach (var view in new[] { a, b }) { view.C.Position = eyes[n]; view.C.LookAt(Vector3.Zero); }
                update.Invoke(b.S, new object[] { eyes[n] });
                for (int frame = 0; frame < 5; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var before = a.V.GetTexture().GetImage(); using var after = b.V.GetTexture().GetImage();
                var p = before.GetData(); var q = after.GetData();
                int max = p.Zip(q, (x, y) => Math.Abs(x - y)).Max();
                GD.Print($"SCATTER DRAW BATCHES CHECK view{n}: max channel difference={max}");
                if (p.Distinct().Count() < 16 || max > 1) throw new Exception("Selected LOD batching changed the rendered geometry/tint or produced a blank control");
                string output = OS.GetEnvironment("UNNAMED_BATCH_CHECK_OUT");
                if (output.Length > 0) { Directory.CreateDirectory(output); before.SavePng(Path.Combine(output, $"before_{n}.png")); after.SavePng(Path.Combine(output, $"after_{n}.png")); }
            }
            GD.Print("SCATTER DRAW BATCHES CHECK PASS: native distance bands, moved camera, transforms, custom tint, no blank-image control");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PrintErr(e); GetTree().Quit(1); }
    }
}
