using Godot;
using UNNAMED.Presentation.Greybox;

namespace UNNAMED.Presentation.Perf;

public partial class ScatterBatchCheck : Node3D
{
    public override async void _Ready()
    {
        try
        {
            if (DisplayServer.GetName() == "headless") throw new Exception("Requires the real renderer: dummy MultiMesh getters return empty buffers");
            WindField.Register();
            var root = new Node3D(); AddChild(root);
            var shader = new Shader { Code = Otherworld.Variant(WindField.FoliageShader) };
            var material = new ShaderMaterial { Shader = shader };
            var mesh = new BoxMesh { Size = new Vector3(.2f, 1, .2f), Material = material };
            // Use an ArrayMesh exactly as ModelMeshes does, with one material/surface.
            var array = new ArrayMesh(); using (var arrays = mesh.GetMeshArrays()) array.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            array.SurfaceSetMaterial(0, material);
            var before = new List<(Transform3D T, Color Tint)>();
            var centres = new List<Vector3>();
            MultiMeshInstance3D Chunk(int x, int z, int level, bool shadow)
            {
                var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = array, InstanceCount = 8 };
                for (int i = 0; i < 8; i++)
                {
                    var t = new Transform3D(Basis.Identity, new Vector3(x * 6 + i * .31f, .7f, z * 6 + i * .27f));
                    var tint = new Color(.123456f + i * .001f, .765432f, .345678f, i / 7f);
                    multi.SetInstanceTransform(i, t); multi.SetInstanceCustomData(i, tint);
                    if (level == 2 && !shadow) before.Add((t, tint));
                }
                var n = new MultiMeshInstance3D { Multimesh = multi,
                    CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                    VisibilityRangeBegin = 24, VisibilityRangeEnd = 48,
                    CustomAabb = new Aabb(new Vector3(x * 6 - 1, -1, z * 6 - 1), new Vector3(8, 3, 8)) };
                n.SetMeta(ScatterBatch.GridKey, new Vector3I(x, z, level)); root.AddChild(n);
                if (level == 2 && !shadow) centres.Add(n.CustomAabb.GetCenter());
                return n;
            }
            for (int z = 0; z < 2; z++) for (int x = 0; x < 2; x++) Chunk(x, z, 2, false);
            var near = Chunk(0, 0, 0, false); var caster = Chunk(0, 0, 2, true);
            if (ScatterBatch.Build(root) != 3) throw new Exception("Expected four far chunks to become one");
            var nodes = root.GetChildren().OfType<MultiMeshInstance3D>().ToArray();
            if (nodes.Length != 3 || !nodes.Contains(near) || !nodes.Contains(caster)) throw new Exception("Near geometry or caster changed");
            var batch = nodes.Single(n => n != near && n != caster);
            if (batch.Multimesh.InstanceCount != before.Count || batch.Multimesh.Mesh != array) throw new Exception("Geometry/density changed");
            var settings = (ShaderMaterial)batch.MaterialOverride;
            if (settings.GetShaderParameter("scatter_band").AsVector2() != new Vector2(24, 48)) throw new Exception("Band changed");
            var meta = settings.GetShaderParameter("scatter_centres").AsVector3Array();
            for (int c = 0; c < centres.Count; c++)
            {
                if (meta[c] != centres[c]) throw new Exception("Chunk centre changed");
                for (int i = 0; i < 8; i++)
                {
                    int at = c * 8 + i;
                    var tint = batch.Multimesh.GetInstanceCustomData(at);
                    if (batch.Multimesh.GetInstanceTransform(at) != before[at].T
                        || tint.R != before[at].Tint.R || tint.G != before[at].Tint.G || tint.B != before[at].Tint.B
                        || (int)Math.Floor(tint.A) != c || Math.Abs((tint.A - c) * 2 - before[at].Tint.A) > .000001f)
                        throw new Exception($"Placement, RGB or field share changed c={c} i={i}: transform={batch.Multimesh.GetInstanceTransform(at)} expected={before[at].T}; tint={tint} expected={before[at].Tint}");
                }
                // Triangle inequality: coarse node ranges must never reject an eligible original chunk.
                for (int angle = 0; angle < 360; angle += 5) foreach (float distance in new[] { 24.01f, 35f, 47.99f })
                {
                    var eye = centres[c] + new Vector3(MathF.Cos(Mathf.DegToRad(angle)), 0, MathF.Sin(Mathf.DegToRad(angle))) * distance;
                    float d = eye.DistanceTo(batch.CustomAabb.GetCenter());
                    if (d < batch.VisibilityRangeBegin || d >= batch.VisibilityRangeEnd) throw new Exception("Batch range loses a visible chunk");
                }
            }
            AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(.04f, .06f, .1f), AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White, AmbientLightEnergy = 1 } });
            var camera = new Camera3D { Current = true, Position = new Vector3(0, 2, 30) }; AddChild(camera);
            camera.LookAt(new Vector3(4, 1, 4));
            for (int frame = 0; frame < 6; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var picture = GetViewport().GetTexture().GetImage();
            int green = 0;
            for (int y = 0; y < picture.GetHeight(); y += 4) for (int x = 0; x < picture.GetWidth(); x += 4)
            { var pixel = picture.GetPixel(x, y); if (pixel.G > pixel.R + .1f && pixel.G > pixel.B + .05f) green++; }
            if (green < 10) throw new Exception("Batch render has no expected green geometry");
            string output = OS.GetEnvironment("UNNAMED_BATCH_CHECK_OUT");
            if (output.Length > 0) picture.SavePng(output);
            GD.Print("ScatterBatchCheck PASS: exact transforms/RGB/counts, bounded field-share precision, original chunk bands, conservative coarse ranges, near/casters untouched, rendered scene");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PrintErr(e); GetTree().Quit(1); }
    }
}
