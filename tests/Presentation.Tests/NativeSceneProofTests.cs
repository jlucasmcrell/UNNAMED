using System.Security.Cryptography;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Tests;

public class NativeSceneProofTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("version")]
    [InlineData("source")]
    [InlineData("texture_missing")]
    [InlineData("texture_changed")]
    [InlineData("scene_changed")]
    [InlineData("external")]
    [InlineData("scene_unpinned")]
    public void OnlyExactSelfContainedCacheInputsPass(string defect)
    {
        string root = Path.Combine(Path.GetTempPath(), "otherreach-native-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string scene = Path.Combine(root, "model.scn"), source = Path.Combine(root, "source.glb"), texture = Path.Combine(root, "map.dds");
            File.WriteAllText(scene, "synthetic cached scene");
            File.WriteAllText(source, "synthetic full native source");
            File.WriteAllText(texture, "synthetic compressed map");
            string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
            var files = new Dictionary<string, string> { ["model.scn"] = Hash(scene), ["source.glb"] = Hash(source), ["map.dds"] = Hash(texture) };
            if (defect == "source") File.AppendAllText(source, "changed");
            if (defect == "texture_missing") File.Delete(texture);
            if (defect == "texture_changed") File.AppendAllText(texture, "changed");
            if (defect == "scene_changed") File.AppendAllText(scene, "changed");
            if (defect == "external") files["../outside.dds"] = "wrong";
            if (defect == "scene_unpinned") files.Remove("model.scn");
            var proof = new NativeSceneProof("engine-policy-source-textures", files);
            void Verify() => proof.Verify(root, scene, defect == "version" ? "other-engine-or-policy" : proof.Stamp, Hash);
            if (defect == "valid") Verify();
            else Assert.Throws<InvalidDataException>(Verify);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
