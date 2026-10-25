using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Diagnostics;

public partial class TreeTierLodGate : Node
{
    public override void _Ready()
    {
        string? before=System.Environment.GetEnvironmentVariable("UNNAMED_TREE_VIEW_LOD");
        string? shadow=System.Environment.GetEnvironmentVariable("UNNAMED_TREE_SHADOW_LOD");
        try
        {
            System.Environment.SetEnvironmentVariable("UNNAMED_TREE_SHADOW_LOD","0");
            int cases=0;
            foreach(bool candidate in new[]{false,true})
            foreach(string tier in new[]{"low","medium","high","ultra"})
            {
                System.Environment.SetEnvironmentVariable("UNNAMED_TREE_VIEW_LOD",candidate?"1":"0");
                VisualOptions.Parse("tier="+tier);
                var root=new Node3D();AddChild(root);
                var geometry=new BoxMesh();var material=new StandardMaterial3D();
                var tree=new MeshInstance3D {Mesh=geometry};root.AddChild(tree);
                tree.SetSurfaceOverrideMaterial(0,material);
                var multi=new MultiMesh {Mesh=geometry,TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,InstanceCount=1};
                multi.SetInstanceTransform(0,new Transform3D(Basis.Identity,new Vector3(2,0,3)));
                var scatter=new MultiMeshInstance3D {Multimesh=multi};root.AddChild(scatter);
                var buffer=multi.Buffer;
                ArtLibrary.Whole(root);
                float expected=candidate && tier=="low"?.75f:candidate && tier=="medium"?1.5f:ArtLibrary.FullDetailLodBias;
                foreach(var instance in new GeometryInstance3D[]{tree,scatter})
                    if(instance.LodBias!=expected || instance.VisibilityRangeBegin!=0 || instance.VisibilityRangeEnd!=0
                        || !instance.IgnoreOcclusionCulling || instance.CastShadow!=GeometryInstance3D.ShadowCastingSetting.On)
                        throw new InvalidDataException("Tree quality/range/casting policy changed unexpectedly: "+tier+"/"+candidate);
                if(tree.Mesh!=geometry || tree.GetSurfaceOverrideMaterial(0)!=material || scatter.Multimesh!=multi || !multi.Buffer.SequenceEqual(buffer))
                    throw new InvalidDataException("Tree candidate changed geometry, materials or native instance buffers");
                ArtLibrary.Whole(root);
                if(root.GetChildCount()!=2)throw new InvalidDataException("Tree quality setup created duplicate draws");
                root.Free();cases++;
            }
            GD.Print($"TREE_TIER_LOD_GATE_PASS {cases}: defaults and High/Ultra unchanged; buffers/materials/ranges/casting preserved");
            GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
        finally
        {
            System.Environment.SetEnvironmentVariable("UNNAMED_TREE_VIEW_LOD",before);
            System.Environment.SetEnvironmentVariable("UNNAMED_TREE_SHADOW_LOD",shadow);
        }
    }
}
