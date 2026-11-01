using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Diagnostics;

/// <summary>Headless development controls for redundant readbacks. Synthetic images are never art evidence.</summary>
public partial class TextureMemoizationControls : Node
{
    public override void _Ready()
    {
        try
        {
            if (DisplayServer.GetName() != "headless") throw new InvalidOperationException("Controls require --headless");
            VerifyRootIsolation();
            OS.SetEnvironment("OTHERREACH_TEXTURE_AUDIT", "1");
            using var compressed = Image.CreateFromData(2048, 2048, false, Image.Format.BptcRgba, new byte[2048 * 2048]);
            var signed = new ReadbackCountingTexture { Data = compressed };
            TextureCache.RequireCompressed(signed, "synthetic compressed control");
            for (int index = 0; index < 10; index++) TextureCache.RequireCompressed(signed, "synthetic compressed control");
            if (signed.Reads != 1) throw new InvalidDataException("Compression audit repeated its readback: " + signed.Reads);
            using var uncompressed = Image.CreateEmpty(2048, 2048, true, Image.Format.Rgba8);
            uncompressed.Fill(new Color(.2f, .4f, .6f));
            uncompressed.GenerateMipmaps();
            var raw = new ReadbackCountingTexture { Data = uncompressed };
            bool refused = false;
            try { TextureCache.RequireCompressed(raw, "synthetic unsafe control"); }
            catch (InvalidDataException) { refused = true; }
            if (!refused || TextureCache.KnownCompressed(raw)) throw new InvalidDataException("Uncompressed source passed or was memoized");
            var material = new StandardMaterial3D { AlbedoTexture = raw, ResourceName = "synthetic_shirt" };
            var garment = new MeshInstance3D { Mesh = new BoxMesh { Material = material } };
            var figure = new Node3D();
            figure.AddChild(garment);
            int before = raw.Reads;
            PeopleAppearance.ApplyLook(figure, "people.veth", null);
            var first = (BaseMaterial3D)garment.GetSurfaceOverrideMaterial(0);
            PeopleAppearance.ApplyLook(figure, "people.veth", null);
            var second = (BaseMaterial3D)garment.GetSurfaceOverrideMaterial(0);
            if (raw.Reads - before != 1 || first.AlbedoColor != second.AlbedoColor)
                throw new InvalidDataException("Dye mean repeated its readback or changed output");
            string file = Path.GetTempFileName();
            try
            {
                File.WriteAllText(file, "first source");
                var world = new ArtLibrary(null);
                string original = world.SourceSha256(file);
                File.WriteAllText(file, "next world source");
                if (world.SourceSha256(file) != original || new ArtLibrary(null).SourceSha256(file) == original)
                    throw new InvalidDataException("Body hash cache escaped its ArtLibrary lifetime");
            }
            finally { File.Delete(file); }
            File.WriteAllText(CpuDiagnostic.Arguments()[0], JsonSerializer.Serialize(new { status = "PASS", development_only = true,
                compressed_audit_reads = signed.Reads, dye_mean_reads = raw.Reads - before, uncompressed_rejected = refused,
                same_dye_output = first.AlbedoColor == second.AlbedoColor, source_hash_scope = "per ArtLibrary", root_isolation = true }));
            GD.Print("TEXTURE_MEMOIZATION_CONTROLS_PASS");
            figure.Free();
            CaptureQuit.At(this);
        }
        catch (Exception error) { GD.PushError(error.ToString()); CaptureQuit.At(this, 1); }
    }

    private static void VerifyRootIsolation()
    {
        string fixture = Path.Combine(Path.GetTempPath(), "otherreach-root-control-" + Guid.NewGuid().ToString("N"));
        string? original = ArtLibrary.FallbackRoot;
        try
        {
            string FileAt(string scope)
            {
                string path = Path.Combine(fixture, scope, "fallback", "rigged", "synthetic", "marker.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, scope);
                return path;
            }
            string expectedA = FileAt("a"), expectedB = FileAt("b");
            ArtLibrary.FallbackRoot = Path.Combine(fixture, "a", "fallback");
            var left = new ArtLibrary(Path.Combine(fixture, "a", "primary"));
            ArtLibrary.FallbackRoot = Path.Combine(fixture, "b", "fallback");
            var right = new ArtLibrary(Path.Combine(fixture, "b", "primary"));
            if (left.RiggedFile("synthetic", "marker.txt") != expectedA || right.RiggedFile("synthetic", "marker.txt") != expectedB)
                throw new InvalidDataException("Changing a new world's fallback redirected an existing ArtLibrary");
        }
        finally
        {
            ArtLibrary.FallbackRoot = original;
            string absolute = Path.GetFullPath(fixture);
            if (!absolute.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(absolute).StartsWith("otherreach-root-control-", StringComparison.Ordinal))
                throw new InvalidDataException("Unexpected synthetic fixture cleanup path");
            Directory.Delete(absolute, recursive: true);
        }
    }
}

public partial class ReadbackCountingTexture : Texture2D
{
    public Image Data { get; set; } = null!;
    public int Reads { get; private set; }
    public override int _GetWidth() => Data.GetWidth();
    public override int _GetHeight() => Data.GetHeight();
    public override Image _GetImage() { Reads++; return (Image)Data.Duplicate(); }
}
