using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>Paid native teeth/tongue atlases on their original UVs, shared by all HumGen bodies.</summary>
public static class HumGenMouth
{
    public static void Apply(Node model, ArtLibrary art, string assetId)
    {
        if (!assetId.StartsWith("humgen_", StringComparison.Ordinal)) return;
        Texture2D Map(string file, string role) => art.RiggedFile("humgen_mouth", file) is { } path
            && art.Texture(path, role) is { } map ? map
            : throw new InvalidDataException("Missing native HumGen mouth map: " + file);
        var material = new StandardMaterial3D
        {
            ResourceName = "HumGen_Native_TeethTongue",
            AlbedoTexture = Map("TeethTongueSet_C_2K.png", "color"),
            NormalEnabled = true, NormalTexture = Map("TeethTongueSet_N_2K.png", "normal"),
            Roughness = 1, RoughnessTexture = Map("TeethTongueSet_ROUGH_2K.png", "data"),
            RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red,
            Metallic = 0, MetallicSpecular = .35f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
            if (mesh.Name.ToString().Contains("_Teeth", StringComparison.Ordinal) && mesh.Mesh is not null)
                for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
                    mesh.SetSurfaceOverrideMaterial(s, material);
    }
}
