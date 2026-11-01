using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Art;
using UNNAMED.Content;

namespace UNNAMED.Presentation.Diagnostics;

/// <summary>CPU character preflight using effective Ultra bindings and actual packaged NPC definitions.</summary>
public partial class PackageDependenciesGate : Node
{
    public override void _Ready()
    {
        var errors = new List<string>();
        try
        {
            if (DisplayServer.GetName() != "headless") throw new InvalidOperationException("Package character preflight requires --headless");
            string[] args = CpuDiagnostic.Arguments();
            string package = Path.GetFullPath(args[0]);
            string[] roots = [Path.Combine(package, "primary"), Path.Combine(package, "fallback")];
            if (roots.Any(root => !Directory.Exists(root))) throw new InvalidDataException("A packaged art root is missing");
            VisualOptions.Parse("tier=ultra,terrain=terrain3d,sky=sky3d,people=humgen,foldscar=proof,body=none,textures=cache");
            OS.SetEnvironment("OTHERREACH_CHARACTER_ROOT", Path.Combine(package, "humgen_characters"));
            string bindingFile = Path.Combine(package, "content", "_art_bindings.json");
            var bindingFiles = Directory.GetFiles(Path.Combine(package, "content"), "_art_bindings*.json");
            var alternatives = bindingFiles.Select(file =>
            {
                using var parsed = JsonDocument.Parse(File.ReadAllText(file));
                return ArtBindings.LoadWithOverlay(ArtBindings.ResourcePath, file);
            }).Append(ArtBindings.Load(ArtBindings.ResourcePath)).ToArray();
            if (alternatives.Any(b => b.People.Count == 0 || b.Bodies.Types.Count == 0))
                throw new InvalidDataException("Effective package bindings are empty/incomplete");
            var loader = new ContentLoader();
            if (!loader.LoadAll(Path.Combine(package, "content"))) throw new InvalidDataException("Packaged NPC content did not load");
            var npcs = SocialContent.BuildNpcs(loader);
            var people = alternatives.SelectMany(b => b.People.Values).ToList();
            // Nonhuman fauna can have procedural or embedded-animation renderers.
            // Their package checks remain separate from the native HumGen contract.
            var creatures = alternatives.SelectMany(b => b.Creatures.Values)
                .Where(c => c.Model?.StartsWith("humgen_", StringComparison.Ordinal) == true).ToArray();
            foreach (var bindings in alternatives)
            {
                foreach (var pair in bindings.People.Where(p => string.IsNullOrWhiteSpace(p.Value.Model)))
                    errors.Add("Person binding has no model: " + pair.Key);
                foreach (var type in bindings.Bodies.Types.Where(t => t.Models.Any(string.IsNullOrWhiteSpace)))
                    errors.Add("Player body choice has no model: " + type.Type);
                foreach (var npc in npcs.Values)
                {
                    var look = bindings.Person(npc.Id, npc.PeopleId, npc.Id, npc.Sex);
                    if (look is null) { errors.Add("Packaged NPC has no effective appearance: " + npc.Id); continue; }
                    // This is NpcsView's authored-appearance body selection.
                    people.Add(npc.Appearance is not null ? look with { Model = npc.Sex == "f" ? "humgen_creator_female" : "humgen_creator_male" } : look);
                }
            }
            foreach (string model in people.Select(p => p.Model).Concat(alternatives.SelectMany(b => b.Bodies.Types.SelectMany(t => t.Models))))
                if (model is null || !model.StartsWith("humgen_", StringComparison.Ordinal)) errors.Add("Player/NPC would use a nonnative fallback body: " + model);
            string? Find(string relative) => roots.Select(root => Path.GetFullPath(Path.Combine(root, relative)))
                .FirstOrDefault(path => roots.Any(root => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) && File.Exists(path));
            foreach (string file in HumGenMouth.RequiredMaps)
                if (Find(Path.Combine("rigged", "humgen_mouth", file)) is null) errors.Add("Shared native mouth input missing: " + file);
            var rigged = creatures.Select(c => c.Model)
                .Concat(people.Select(p => p.Model))
                .Concat(people.SelectMany(p => p.Outfit.Concat(p.EquipmentOutfits.Values.SelectMany(a => a))))
                .Concat(alternatives.SelectMany(b => b.Bodies.Types.SelectMany(t => t.Models))).OfType<string>().Distinct().ToArray();
            var models = rigged.Select(id => (Id: id, Relative: Path.Combine("rigged", id, id + "_rigged.glb"))).ToArray();
            foreach (var (id, relative) in models)
            {
                string? file = Find(relative);
                if (file is null) errors.Add("Bound model missing from both package roots: " + id);
                else CheckGlb(file, roots, errors, false);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(args[1])!);
            File.WriteAllText(args[1], JsonSerializer.Serialize(new { status = errors.Count == 0 ? "PASS" : "FAIL", package,
                executable = OS.GetExecutablePath(), executable_sha256 = Godot.FileAccess.GetSha256(OS.GetExecutablePath()),
                packaged = OS.HasFeature("template"), binding_sha256 = Godot.FileAccess.GetSha256(bindingFile),
                binding_files = bindingFiles.ToDictionary(file => Path.GetRelativePath(package, file).Replace('\\','/'), Godot.FileAccess.GetSha256),
                checked_scope = "native_player_npc_inputs", npc_definitions = npcs.Count,
                models = models.Length, boundModels = models.Select(m => new { id = m.Id, relative = m.Relative }).ToArray(), failures = errors }));
            foreach (string error in errors) GD.PushError(error);
            CaptureQuit.At(this, errors.Count == 0 ? 0 : 1);
        }
        catch (Exception error) { GD.PushError(error.ToString()); CaptureQuit.At(this, 1); }
    }

    private static void CheckGlb(string file, string[] roots, List<string> errors, bool animation)
    {
        using var stream = File.OpenRead(file);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x46546c67 || reader.ReadUInt32() != 2 || reader.ReadUInt32() != stream.Length)
        { errors.Add("Invalid bound GLB: " + file); return; }
        int length = checked((int)reader.ReadUInt32());
        if (reader.ReadUInt32() != 0x4e4f534a) { errors.Add("Invalid bound GLB JSON chunk: " + file); return; }
        using var document = JsonDocument.Parse(reader.ReadBytes(length));
        string payload = animation ? "animations" : "meshes";
        if (!document.RootElement.TryGetProperty(payload, out var content) || content.GetArrayLength() == 0)
            errors.Add("Empty bound " + payload + ": " + file);
        foreach (string collection in new[] { "images", "buffers" })
            if (document.RootElement.TryGetProperty(collection, out var entries))
                foreach (var entry in entries.EnumerateArray())
                    if (entry.TryGetProperty("uri", out var uri) && uri.GetString() is { } path && !path.StartsWith("data:", StringComparison.Ordinal))
                    {
                        string root = Path.GetDirectoryName(file)!;
                        string target = Path.GetFullPath(Path.Combine(root, Uri.UnescapeDataString(path)));
                        if (!roots.Any(allowed => target.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) || !File.Exists(target))
                            errors.Add("Missing/external bound GLB dependency: " + file + " -> " + path);
                    }
    }
}
