using Godot;
using UNNAMED.Presentation.Greybox;

namespace UNNAMED.Presentation.Perf;

// Native pixel comparison against the pre-change shader supplied by the caller.
public partial class WindHueCheck : Node
{
    public override async void _Ready()
    {
        try
        {
            WindField.Register();
            WindField.Set(new Vector3(.8f, 0, .6f), .7f, .8f);
            RenderingServer.GlobalShaderParameterSet("otherworld_mix", new Vector4(.7f, .4f, .1f, .5f));
            string old = System.IO.File.ReadAllText(OS.GetEnvironment("UNNAMED_OLD_FOLIAGE_SHADER"));
            string current = WindField.FoliageShader;
            for (int variant = 0; variant < 4; variant++)
            {
                bool opaque = variant % 2 == 1, mineral = variant >= 2;
                string Code(string code)
                {
                    code = code.Replace("TIME", "12.345");
                    if (opaque) code = "#define OPAQUE_FOLIAGE\n" + code;
                    return code.Replace("shader_type spatial;", "shader_type spatial;\n#define OTHERWORLD" + (mineral ? "\n#define OW_MINERAL" : ""));
                }
                var a = View(Code(old));
                var b = View(Code(current));
                for (int frame = 0; frame < 8; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var before = a.GetTexture().GetImage();
                using var after = b.GetTexture().GetImage();
                var p = before.GetData(); var q = after.GetData();
                int max = p.Zip(q, (x, y) => Math.Abs(x - y)).Max();
                int changed = p.Zip(q, (x, y) => x != y).Count(x => x);
                GD.Print($"WindHueCheck variant={variant}: max channel delta={max}, changed={changed}/{p.Length}");
                if (max > 1) throw new Exception("Moving hue trigonometry changed the rendered field");
                // Positive control: this field must contain plants rather than two blank images.
                if (p.Distinct().Count() < 16) throw new Exception("Blank comparison image");
                string output = OS.GetEnvironment("UNNAMED_WIND_CHECK_OUT");
                if (output.Length > 0)
                {
                    System.IO.Directory.CreateDirectory(output);
                    before.SavePng(System.IO.Path.Combine(output, $"before_{variant}.png"));
                    after.SavePng(System.IO.Path.Combine(output, $"after_{variant}.png"));
                }
                RemoveChild(a); a.QueueFree(); RemoveChild(b); b.QueueFree();
            }
            GD.Print("WindHueCheck PASS"); GetTree().Quit();
        }
        catch (Exception e) { GD.PrintErr(e); GetTree().Quit(1); }
    }

    private SubViewport View(string code)
    {
        var viewport = new SubViewport { Size = new Vector2I(512, 512), OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport);
        viewport.AddChild(new WorldEnvironment { Environment = new Godot.Environment {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.1f, .14f, .2f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White,
            AmbientLightEnergy = .7f, TonemapMode = Godot.Environment.ToneMapper.Linear } });
        var camera = new Camera3D { Position = new Vector3(0, 10, 15), Current = true, Far = 100 };
        viewport.AddChild(camera); camera.LookAt(new Vector3(0, 0, -15));
        viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-45, -25, 0), LightEnergy = .8f });
        using var image = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
        image.Fill(new Color(.3f, .6f, .2f));
        var material = new ShaderMaterial { Shader = new Shader { Code = code } };
        material.SetShaderParameter("albedo_tex", ImageTexture.CreateFromImage(image));
        material.SetShaderParameter("ow_hue", -40f);
        material.SetShaderParameter("thin_from", 20f); material.SetShaderParameter("thin_to", 40f);
        var mesh = new BoxMesh { Size = new Vector3(.18f, 1, .18f), Material = material };
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true, Mesh = mesh, InstanceCount = 1600 };
        for (int i = 0; i < mm.InstanceCount; i++)
        {
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity,
                new Vector3(i % 40 - 19.5f, .5f, -(i / 40))));
            mm.SetInstanceCustomData(i, new Color(.8f, .9f, .7f, .6f));
        }
        viewport.AddChild(new MultiMeshInstance3D { Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return viewport;
    }
}
