using Godot;
using UNNAMED.Presentation.Greybox;

namespace UNNAMED.Presentation.Perf;

// Native pixel comparison against the pre-change shader supplied by the caller.
public partial class FoliageSpecializationCheck : Node
{
    public override async void _Ready()
    {
        try
        {
            WindField.Register();
            WindField.Set(new Vector3(.8f, 0, .6f), .7f, .8f);
            RenderingServer.GlobalShaderParameterSet("otherworld_mix", new Vector4(.7f, .4f, .1f, .5f));
            bool tree = OS.GetEnvironment("UNNAMED_TREE_FIELD_CHECK") == "1";
            string current = tree ? WindField.TreeShader : WindField.FoliageShader;
            string old = tree ? current.Replace("    if (otherworld_mix.x == 0.0) return 0.0;", "")
                : System.IO.File.ReadAllText(OS.GetEnvironment("UNNAMED_OLD_FOLIAGE_SHADER"));
            if (tree && old == current) throw new Exception("Tree baseline did not remove the field early return");
            RenderingServer.GlobalShaderParameterSet("otherworld_calm", new Vector4(-19, -20, 18, -21));
            RenderingServer.GlobalShaderParameterSet("otherworld_fold", new Vector4(0, -15, 5, 25));
            bool differences = false;
            for (int variant = 0; variant < (tree ? 16 : 64); variant++)
            {
                bool opaque = (variant & 1) != 0, noWind = (variant & 2) != 0, noOtherworld = (variant & 4) != 0, noThin = (variant & 8) != 0;
                bool mineral = !tree && !noOtherworld && noWind;
                bool normalMap = (variant & 16) != 0, armMap = (variant & 32) != 0;
                RenderingServer.GlobalShaderParameterSet("otherworld_mix", new Vector4(tree && noOtherworld ? 0 : .7f, .4f, .1f, .5f));
                string Code(string code, bool specialized)
                {
                    code = code.Replace("TIME", "12.345");
                    if (opaque) code = "#define OPAQUE_FOLIAGE\n" + code;
                    if (tree || !noOtherworld || !specialized) code = code.Replace("shader_type spatial;", "shader_type spatial;\n#define OTHERWORLD" + (mineral ? "\n#define OW_MINERAL" : ""));
                    if (specialized && noOtherworld && noWind) code = "#define STATIC_FOLIAGE\n" + code;
                    if (specialized && noOtherworld && noThin) code = "#define NO_FOLIAGE_THINNING\n" + code;
                    if (!tree && specialized && noOtherworld && !normalMap) code = "#define NO_FOLIAGE_NORMAL_MAP\n" + code;
                    if (!tree && specialized && noOtherworld && !armMap) code = "#define NO_FOLIAGE_ARM_MAP\n" + code;
                    return code;
                }
                var a = View(Code(old, false), noWind, noOtherworld, noThin, mineral, tree, opaque, normalMap, armMap);
                var b = View(Code(current, true), noWind, noOtherworld, noThin, mineral, tree, opaque, normalMap, armMap);
                for (int frame = 0; frame < 16; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var before = a.GetTexture().GetImage();
                using var after = b.GetTexture().GetImage();
                var p = before.GetData(); var q = after.GetData();
                int max = p.Zip(q, (x, y) => Math.Abs(x - y)).Max();
                int changed = p.Zip(q, (x, y) => x != y).Count(x => x);
                GD.Print($"FoliageSpecializationCheck variant={variant}: max channel delta={max}, changed={changed}/{p.Length}");
                if (max > 1) differences = true;
                string output = OS.GetEnvironment("UNNAMED_WIND_CHECK_OUT");
                if (output.Length > 0)
                {
                    System.IO.Directory.CreateDirectory(output);
                    before.SavePng(System.IO.Path.Combine(output, $"before_{variant}.png"));
                    after.SavePng(System.IO.Path.Combine(output, $"after_{variant}.png"));
                }
                // Save failed controls as well: equal empty readbacks must never pass.
                if (p.Distinct().Count() < 16) throw new Exception($"Blank comparison image, variant {variant}, distinct bytes={p.Distinct().Count()}");
                RemoveChild(a); a.QueueFree(); RemoveChild(b); b.QueueFree();
            }
            if (differences) throw new Exception("Shader specialization changed the rendered field; all failed images retained");
            GD.Print("FoliageSpecializationCheck PASS"); CaptureQuit.At(this);
        }
        catch (Exception e) { GD.PrintErr(e); CaptureQuit.At(this, 1); }
    }

    private SubViewport View(string code, bool noWind, bool noOtherworld, bool noThin, bool mineral, bool tree, bool opaque, bool normalMap, bool armMap)
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
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            image.SetPixel(x, y, new Color(.3f + x * .05f, .6f - y * .04f, .2f + ((x + y) % 2) * .04f));
        var material = new ShaderMaterial { Shader = new Shader { Code = code } };
        material.SetShaderParameter("albedo_tex", ImageTexture.CreateFromImage(image));
        if (normalMap)
        {
            using var normal = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
            normal.Fill(new Color(.55f, .65f, .97f));
            material.SetShaderParameter("normal_tex", ImageTexture.CreateFromImage(normal));
            material.SetShaderParameter("has_normal", true);
        }
        if (armMap)
        {
            material.SetShaderParameter("arm_tex", ImageTexture.CreateFromImage(image));
            material.SetShaderParameter("has_arm", true);
        }
        if (tree) { material.SetShaderParameter("has_albedo", true); material.SetShaderParameter("foliage", !opaque); }
        material.SetShaderParameter("ow_hue", noOtherworld ? 0f : -40f);
        if (noWind) { material.SetShaderParameter("plant_height", 0f); material.SetShaderParameter("flutter", 0f); }
        if (mineral) {
            material.SetShaderParameter("ow_vein_on", 1f); material.SetShaderParameter("ow_vein", new Vector3(.1f,.2f,.3f));
            material.SetShaderParameter("ow_lichen_on", 1f); material.SetShaderParameter("ow_lichen", new Vector3(.2f,.4f,.3f));
            material.SetShaderParameter("ow_iridescence", .5f);
        }
        material.SetShaderParameter("thin_from", 20f); material.SetShaderParameter("thin_to", noThin ? 0f : 40f);
        PrimitiveMesh mesh = tree ? new BoxMesh { Size = new Vector3(.18f, 1, .18f) }
            : new SphereMesh { Radius = .18f, Height = 1, RadialSegments = 8, Rings = 4 };
        mesh.Material = material;
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
