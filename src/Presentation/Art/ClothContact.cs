using Godot;
using System.Text.Json;

namespace UNNAMED.Presentation.Art;

/// <summary>Offline native garment correctives follow the same seek/crossfade clock as their recorded pose.</summary>
internal static class ClothContact
{
    public static void Bind(SkinnedModel model, ArtLibrary art, string body, AnimationLibrary library)
    {
        string? outfit = body switch
        {
            "humgen_player_ada_f" => "humgen_frontier_ada_f",
            "humgen_player_mateo_m" => "humgen_frontier_mateo_m",
            _ => null,
        };
        if (outfit is null || art.RiggedFile(outfit, outfit + "_rigged.glb") is not { } source)
            return;
        string receipt = System.IO.Path.ChangeExtension(source, ".contact.json");
        if (!System.IO.File.Exists(receipt)) return;
        using var document = JsonDocument.Parse(System.IO.File.ReadAllText(receipt));
        if (document.RootElement.GetProperty("output_sha256").GetString() != Godot.FileAccess.GetSha256(source))
            throw new InvalidDataException("Garment contact receipt differs from its native mesh: " + outfit);
        var reset = library.HasAnimation("RESET") ? library.GetAnimation("RESET") : new Animation();
        if (!library.HasAnimation("RESET")) library.AddAnimation("RESET", reset);
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is not ArrayMesh array) continue;
            var names = Enumerable.Range(0, array.GetBlendShapeCount())
                .Select(i => array.GetBlendShapeName(i).ToString()).ToHashSet(StringComparer.Ordinal);
            foreach (var clip in document.RootElement.GetProperty("clips").EnumerateObject())
            {
                if (!library.HasAnimation(clip.Name)) continue;
                var samples = clip.Value.EnumerateArray().ToArray();
                var animation = library.GetAnimation(clip.Name);
                for (int selected = 0; selected < samples.Length; selected++)
                {
                    string target = samples[selected].GetProperty("target").GetString()!;
                    if (!names.Contains(target)) continue;
                    var path = new NodePath($"{model.GetPathTo(mesh)}:blend_shapes/{target}");
                    int track = animation.AddTrack(Animation.TrackType.Value);
                    animation.TrackSetPath(track, path);
                    for (int sample = 0; sample < samples.Length; sample++)
                        animation.TrackInsertKey(track, samples[sample].GetProperty("seconds").GetDouble(), sample == selected ? 1f : 0f);
                    int atRest = reset.AddTrack(Animation.TrackType.Value);
                    reset.TrackSetPath(atRest, path);
                    reset.TrackInsertKey(atRest, 0, 0f);
                }
            }
        }
    }
}
