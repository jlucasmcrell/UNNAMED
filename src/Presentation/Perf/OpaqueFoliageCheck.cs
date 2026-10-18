using Godot;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Greybox;

namespace UNNAMED.Presentation.Perf;

public partial class OpaqueFoliageCheck : Node3D
{
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public override void _Ready()
    {
        try
        {
            var kind = ScatterRules.Parse("""{"kinds":{"grass":{"mesh":"model:veg_gen_grass_test","max_per_m2":1,"stiffness":2}}}""").Kinds.Single();
            using var pixels = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++) pixels.SetPixel(x, y, new Color(.2f + x * .02f, .3f + y * .02f, .1f, 1));
            var source = new StandardMaterial3D { AlbedoTexture = ImageTexture.CreateFromImage(pixels) };
            var cache = new Dictionary<string, Texture2D>();
            var view = new ScatterView();
            ShaderMaterial Make(BaseMaterial3D? material, ScatterKind? rules = null) => (ShaderMaterial)view.Foliage(material, rules ?? kind, 2, true, cache);
            var solid = Make(source);
            Require(solid.Shader.Code.Contains("#define OPAQUE_FOLIAGE"), "Opaque texture kept the cutout shader");
            Require(solid.GetShaderParameter("plant_height").AsSingle() == 2 && solid.GetShaderParameter("stiffness").AsSingle() == 2
                && solid.GetShaderParameter("thin_to").AsSingle() > 0, "Wind or thinning parameters changed");
            var texture = (Texture2D)solid.GetShaderParameter("albedo_tex");
            using var actual = texture.GetImage();
            using var expected = ImageMips.Coverage(pixels, .4f);
            Require(actual.GetData().SequenceEqual(expected.GetData()), "Coverage RGB/mip bytes changed");
            Require(actual.GetData().Where((_, i) => i % 4 == 3).All(a => a / 255f >= .18f), "Opaque coverage mip fails the original cutoff");
            using var cutPixels = (Image)pixels.Duplicate();
            cutPixels.SetPixel(0, 0, new Color(0, 0, 0, 0));
            var cut = Make(new StandardMaterial3D { AlbedoTexture = ImageTexture.CreateFromImage(cutPixels) });
            Require(!cut.Shader.Code.Contains("#define OPAQUE_FOLIAGE"), "A transparent texel lost its alpha test");
            source.SetMeta("otherreach_source_transparency", (int)BaseMaterial3D.TransparencyEnum.AlphaScissor);
            Require(!Make(source).Shader.Code.Contains("#define OPAQUE_FOLIAGE"), "Authored cutout intent was ignored");
            Require(!Make(null).Shader.Code.Contains("#define OPAQUE_FOLIAGE"), "Unknown texture opacity was guessed");
            source.RemoveMeta("otherreach_source_transparency");
            var other = new ScatterView();
            var independent = (ShaderMaterial)other.Foliage(source, kind, 2, true, new Dictionary<string, Texture2D>());
            Require(independent.Shader.GetInstanceId() != solid.Shader.GetInstanceId(), "Worlds share mutable shader cache");
            var mineral = Make(source, kind with { Otherflora = new Otherflora(0, null, 0, 0, 0, null, false, new[] { .1f, .2f, .3f }, 0, null) });
            Require(mineral.Shader.Code.Contains("#define OPAQUE_FOLIAGE") && mineral.Shader.Code.Contains("#define OW_MINERAL"), "Mineral shader variant lost");
            view.Free(); other.Free();
            GD.Print("OPAQUE FOLIAGE CHECK PASS: opacity, coverage bytes, wind/thinning, cutouts, unknown textures, mineral variant and world isolation");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
}
