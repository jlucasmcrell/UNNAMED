using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Diagnostics;

public partial class TreeShadowLodGate : Node
{
    public override void _Ready()
    {
        string? prior = System.Environment.GetEnvironmentVariable("UNNAMED_TREE_SHADOW_LOD");
        string? viewPrior = System.Environment.GetEnvironmentVariable("UNNAMED_TREE_VIEW_LOD");
        try
        {
            System.Environment.SetEnvironmentVariable("UNNAMED_TREE_VIEW_LOD", "0");
            int cases = 0;
            foreach (string tier in new[] { "low", "medium", "high", "ultra" })
            foreach (string? mode in new string?[] { null, "0", "1" })
            {
                VisualOptions.Parse("tier=" + tier);
                bool enabled = mode != "0";
                System.Environment.SetEnvironmentVariable("UNNAMED_TREE_SHADOW_LOD", mode);
                var root = new Node3D(); AddChild(root);
                var material = new StandardMaterial3D { Roughness = .8f };
                var tree = new MeshInstance3D { Mesh = new BoxMesh(), Position = new Vector3(2, 0, 1) };
                tree.SetSurfaceOverrideMaterial(0, material); root.AddChild(tree);
                ArtLibrary.Whole(root);
                var proxy = tree.GetNodeOrNull<MeshInstance3D>("TreeShadowLod");
                if (tree.LodBias != ArtLibrary.FullDetailLodBias || tree.VisibilityRangeEnd != 0 || !tree.IgnoreOcclusionCulling)
                    throw new InvalidDataException("Main-view tree quality changed");
                if (!enabled)
                {
                    if (proxy is not null || tree.CastShadow != GeometryInstance3D.ShadowCastingSetting.On)
                        throw new InvalidDataException("Explicit baseline control changed");
                }
                else
                {
                    if (proxy is null || proxy.Mesh != tree.Mesh || proxy.Transform != Transform3D.Identity
                        || proxy.GetSurfaceOverrideMaterial(0) != material || proxy.LodBias != 1
                        || proxy.CastShadow != GeometryInstance3D.ShadowCastingSetting.ShadowsOnly
                        || tree.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off)
                        throw new InvalidDataException("Shadow proxy did not share original geometry/materials");
                    ArtLibrary.Whole(root);
                    if (tree.GetChildren().Count != 1) throw new InvalidDataException("Repeated Whole duplicated proxies");
                }
                var band = new MultiMeshInstance3D { Multimesh = new MultiMesh { Mesh = tree.Mesh, InstanceCount = 1 } };
                root.AddChild(band); ArtLibrary.Whole(band);
                var bandProxy = band.GetNodeOrNull<MultiMeshInstance3D>("TreeShadowLod");
                if (enabled && (bandProxy is null || bandProxy.Multimesh != band.Multimesh))
                    throw new InvalidDataException("Scatter shadow proxy copied native instance buffers");
                root.Free();
                cases++;
            }
            GD.Print($"TREE_SHADOW_LOD_GATE_PASS {cases}: accepted default ON all tiers; explicit baseline OFF; visible mesh/buffers/materials shared; proxy idempotent");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally
        {
            System.Environment.SetEnvironmentVariable("UNNAMED_TREE_SHADOW_LOD", prior);
            System.Environment.SetEnvironmentVariable("UNNAMED_TREE_VIEW_LOD", viewPrior);
        }
    }
}
