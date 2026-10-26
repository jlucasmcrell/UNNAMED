using Godot;
using System.Text.Json;
namespace UNNAMED.Presentation.Art;

/// <summary>Restore the six new face assets' authored defaults, which runtime glTF import omits.</summary>
internal static class HumGenIdentityWeights
{
    public static void Apply(Node model, ArtLibrary art, string id)
    {
        if (art.RiggedFile(id, "identity_weights.json") is not { } path) return;
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        string body = art.RiggedFile(id, id + "_rigged.glb") ?? throw new InvalidDataException(id);
        if (json.RootElement.GetProperty("target_body_sha256").GetString() != Godot.FileAccess.GetSha256(body))
            throw new InvalidDataException("Identity defaults target another body: " + id);
        var meshes = model.FindChildren("*", nameof(MeshInstance3D), true, false)
            .OfType<MeshInstance3D>().ToDictionary(m => m.Name.ToString());
        foreach (var part in json.RootElement.GetProperty("meshes").EnumerateObject())
        {
            if (!meshes.TryGetValue(part.Name, out var mesh) || mesh.Mesh is not ArrayMesh array
                || array.GetBlendShapeCount() != part.Value.GetArrayLength())
                throw new InvalidDataException("Identity mesh contract changed: " + part.Name);
            int index = 0;
            foreach (var shape in part.Value.EnumerateArray())
            {
                if (array.GetBlendShapeName(index).ToString() != shape.GetProperty("name").GetString())
                    throw new InvalidDataException("Identity shape order changed: " + part.Name);
                mesh.SetBlendShapeValue(index++, shape.GetProperty("value").GetSingle());
            }
        }
    }
}
