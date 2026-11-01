using Godot;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UNNAMED.Presentation.Art;

public sealed partial class ArtLibrary
{
    // Change when the raw import/material policy changes. Runtime appearance is never cached here.
    private const string NativeRiggedVersion = "v2";
    internal int NativeRiggedHits { get; private set; }
    private readonly Dictionary<string, Dictionary<string, string>> _nativeTextureSignatures = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? _nativeStaticSources;

    private bool NativeStaticSelected(string path)
    {
        if (!Path.GetRelativePath(WorkspaceOf(path), path).Replace('\\','/').StartsWith("ready/", StringComparison.Ordinal)) return false;
        if (_nativeStaticSources is null)
        {
            _nativeStaticSources = new(StringComparer.OrdinalIgnoreCase);
            string manifest = Path.Combine(Root ?? "", "manifests", "native_static_sources.json");
            if (File.Exists(manifest))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(manifest));
                if (json.RootElement.GetProperty("schema").GetInt32() != 1) throw new InvalidDataException("Invalid native static source manifest");
                string baseRoot = Path.GetDirectoryName(Path.GetFullPath(Root!))!;
                foreach (var entry in json.RootElement.GetProperty("paths").EnumerateArray())
                {
                    string relative = entry.GetString()!.Replace('\\','/');
                    if (!relative.StartsWith("primary/ready/", StringComparison.Ordinal) && !relative.StartsWith("fallback/ready/", StringComparison.Ordinal))
                        throw new InvalidDataException("Native static source must identify a packaged ready model");
                    string file = Path.GetFullPath(Path.Combine(baseRoot, relative));
                    if (!Workspaces.Any(root => file.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        || !file.Contains(Path.DirectorySeparatorChar + "ready" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        || !File.Exists(file)) throw new InvalidDataException("Missing/external native static source: " + file);
                    _nativeStaticSources.Add(file);
                }
            }
        }
        return _nativeStaticSources.Contains(Path.GetFullPath(path));
    }

    private bool NativeRiggedEligible(string path) => VisualOptions.Tier == "ultra"
        && (Path.GetRelativePath(WorkspaceOf(path), path).Replace('\\', '/').StartsWith("rigged/", StringComparison.Ordinal) || NativeStaticSelected(path));

    private string NativeRiggedFile(string path) => Path.Combine(WorkspaceOf(path), "character_native",
        Engine.GetVersionInfo()["string"].AsString(), NativeRiggedVersion,
        (NativeStaticSelected(path) ? "static_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetRelativePath(WorkspaceOf(path), path).Replace('\\','/'))))[..16] + "_" : "") + Path.GetFileName(path) + ".scn");

    private string NativeRiggedStamp(string path)
    {
        string workspace = WorkspaceOf(path), relative = Path.GetRelativePath(workspace, path).Replace('\\', '/');
        if (!_nativeTextureSignatures.TryGetValue(workspace, out var signatures))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, TextureCache.Folder, "manifest.json")));
            signatures = json.RootElement.GetProperty("entries").EnumerateObject().Where(p => p.Name.Contains('#'))
                .GroupBy(p => p.Name.Split('#')[0], StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
                    group.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => p.Name + "|" + p.Value.GetProperty("dds").GetString()
                        + "|" + p.Value.GetProperty("size").GetInt64() + "|" + p.Value.GetProperty("sig").GetString()
                        + "|" + p.Value.GetProperty("role").GetString()))))), StringComparer.Ordinal);
            _nativeTextureSignatures[workspace] = signatures;
        }
        string stamp = "native-rigged-" + NativeRiggedVersion + (NativeStaticSelected(path) ? " static-template-v1" : "") + " " + Engine.GetVersionInfo()["string"].AsString()
            + " " + SourceSha256(path) + " " + signatures.GetValueOrDefault(relative, "untextured");
        if (OS.GetEnvironment("UNNAMED_AO_ALL_OFF") == "1") stamp += " ao-off";
        if (OS.GetEnvironment("UNNAMED_BUILDING_OCCLUSION") == "0") stamp += " no-occluders";
        if (OS.GetEnvironment("UNNAMED_RUNTIME_MESH_LODS") == "0") stamp += " no-runtime-lods";
        return stamp;
    }

    private PackedScene? TryNativeRigged(string path)
    {
        string native = NativeRiggedFile(path), receipt = native + ".proof.json";
        if (!File.Exists(native) && !File.Exists(receipt)) return null;
        try
        {
            var proof = JsonSerializer.Deserialize<NativeSceneProof>(File.ReadAllText(receipt))
                ?? throw new InvalidDataException("Invalid native rigged scene proof: " + receipt);
            proof.Verify(WorkspaceOf(path), native, NativeRiggedStamp(path), SourceSha256);
            if (ResourceLoader.GetDependencies(native).Length != 0)
                throw new InvalidDataException("Native rigged scene is not self-contained: " + native);
            using var saved = ResourceLoader.Load<PackedScene>(native, cacheMode: ResourceLoader.CacheMode.Ignore)
                ?? throw new InvalidDataException("Cannot load native rigged scene: " + native);
            var root = saved.Instantiate<Node3D>();
            foreach (var material in NativeRiggedMaterials(root))
                for (int slot = 0; slot < (int)BaseMaterial3D.TextureParam.Max; slot++)
                {
                    var parameter = (BaseMaterial3D.TextureParam)slot;
                    if (material.GetTexture(parameter) is { } texture)
                        material.SetTexture(parameter, CacheFor(path).ShareNative(texture));
                }
            var shared = new PackedScene();
            var result = shared.Pack(root);
            root.Free();
            if (result != Error.Ok) throw new InvalidDataException("Cannot pack shared native rigged resources: " + result);
            NativeRiggedHits++;
            return shared;
        }
        catch (Exception error) when (_buildSceneCache && error is IOException or JsonException or InvalidDataException)
        {
            GD.Print("UNNAMED rebuilding native rigged scene: " + error.Message);
            return null;
        }
    }

    internal Node3D? NativeStaticSource(string relative, bool requireSelected = true)
    {
        relative = relative.Replace('\\', '/');
        string? expected = null;
        if (relative.StartsWith("primary/", StringComparison.Ordinal) || relative.StartsWith("fallback/", StringComparison.Ordinal))
        {
            expected = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Root!))!, relative));
            relative = relative[(relative.IndexOf('/') + 1)..];
        }
        if (!relative.StartsWith("ready/", StringComparison.Ordinal) || relative.Contains("../", StringComparison.Ordinal))
            throw new InvalidDataException("Native static source must be an explicit ready path");
        string actual = At(relative) ?? throw new InvalidDataException("Native static source missing: " + relative);
        if (expected is not null && !string.Equals(expected, Path.GetFullPath(actual), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native static source is shadowed: " + expected);
        if (requireSelected && !NativeStaticSelected(actual)) throw new InvalidDataException("Native static source is not selected: " + relative);
        return Instance(Path.GetFileName(Path.GetDirectoryName(relative))!, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static IEnumerable<BaseMaterial3D> NativeRiggedMaterials(Node root) => root
        .FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().Where(mesh => mesh.Mesh is not null)
        .SelectMany(mesh => Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Select(surface => mesh.Mesh.SurfaceGetMaterial(surface)))
        .OfType<BaseMaterial3D>().DistinctBy(material => material.GetInstanceId());

    private void SaveNativeRigged(PackedScene scene, string path)
    {
        string workspace = WorkspaceOf(path), native = NativeRiggedFile(path);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path, native };
        var root = scene.Instantiate<Node3D>();
        foreach (var material in NativeRiggedMaterials(root))
            for (int slot = 0; slot < (int)BaseMaterial3D.TextureParam.Max; slot++)
                if (material.GetTexture((BaseMaterial3D.TextureParam)slot) is { } texture)
                {
                    string dds = TextureCache.NativeDds(texture);
                    if (dds.Length == 0 || !TextureCache.KnownCompressed(texture))
                        throw new InvalidDataException("Native rigged scene requires signed compressed textures: " + path);
                    files.Add(Path.Combine(workspace, TextureCache.Folder, dds));
                }
        root.Free();
        Directory.CreateDirectory(Path.GetDirectoryName(native)!);
        string temporary = native + ".building.scn";
        var result = ResourceSaver.Save(scene, temporary, ResourceSaver.SaverFlags.Compress | ResourceSaver.SaverFlags.BundleResources);
        if (result != Error.Ok || ResourceLoader.GetDependencies(temporary).Length != 0)
            throw new IOException("Cannot write portable native rigged scene: " + native + " / " + result);
        File.Move(temporary, native, overwrite: true);
        _sourceHashes.Remove(Path.GetFullPath(native));
        var proof = new NativeSceneProof(NativeRiggedStamp(path), files.ToDictionary(
            file => Path.GetRelativePath(workspace, file).Replace('\\', '/'), file => Godot.FileAccess.GetSha256(file)));
        proof.Verify(workspace, native, proof.Stamp, SourceSha256);
        File.WriteAllText(native + ".proof.json", JsonSerializer.Serialize(proof));
        GD.Print("UNNAMED native rigged scene built: " + native);
    }
}
