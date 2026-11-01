using System.Text.Json.Serialization;

namespace UNNAMED.Presentation.Art;

/// <summary>A portable cache proof, not an art approval. Every cached byte remains tied to its source and engine policy.</summary>
public sealed record NativeSceneProof(
    [property: JsonPropertyName("stamp")] string Stamp,
    [property: JsonPropertyName("files")] IReadOnlyDictionary<string, string> Files)
{
    public void Verify(string root, string scene, string expectedStamp, Func<string, string> digest)
    {
        if (Stamp != expectedStamp) throw new InvalidDataException("Stale native scene source, engine or texture policy: " + scene);
        root = Path.GetFullPath(root);
        if (!Files.ContainsKey(Path.GetRelativePath(root, scene).Replace('\\', '/')))
            throw new InvalidDataException("Native scene itself is not pinned: " + scene);
        foreach (var (relative, expected) in Files)
        {
            string file = Path.GetFullPath(Path.Combine(root, relative));
            if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(file) || digest(file) != expected)
                throw new InvalidDataException("Missing, external or changed native scene dependency: " + relative);
        }
    }
}
