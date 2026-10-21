using Godot;

namespace UNNAMED.Presentation.Greybox;

// A visible grip around planted feet; driven only by CreatureView.Rooted.
public static class RootVines
{
    public static Node3D Create(float radius)
    {
        var root=new Node3D { Name="RootedVines" };
        var bark=new StandardMaterial3D { AlbedoColor=new Color(.42f,.235f,.09f),Roughness=.95f,
            EmissionEnabled=true,Emission=new Color(.23f,.10f,.025f),EmissionEnergyMultiplier=.6f };
        var leaf=new StandardMaterial3D { AlbedoColor=new Color(.18f,.34f,.04f),Roughness=.9f,
            EmissionEnabled=true,Emission=new Color(.07f,.16f,.015f),EmissionEnergyMultiplier=.6f };
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        for(int strand=0;strand<8;strand++)
        {
            Vector3 Point(float t)
            {
                float angle=strand*Mathf.Tau/8+t*2.2f;
                float r=radius*(1.65f-.7f*t);
                return new Vector3(Mathf.Cos(angle)*r,.03f+t*.85f,Mathf.Sin(angle)*r);
            }
            Vector3 Ring(float t,int side)
            {
                var tangent=(Point(t+.005f)-Point(t-.005f)).Normalized();
                var x=tangent.Cross(Vector3.Up).Normalized();var y=tangent.Cross(x);
                float angle=side*Mathf.Tau/8;
                return Point(t)+(x*Mathf.Cos(angle)+y*Mathf.Sin(angle))*(.055f*(1-t)+.006f);
            }
            for(int segment=0;segment<12;segment++)for(int side=0;side<8;side++)
            {
                var a=Ring(segment/12f,side);var b=Ring(segment/12f,side+1);
                var c=Ring((segment+1)/12f,side);var d=Ring((segment+1)/12f,side+1);
                foreach(var point in new[]{a,c,b,b,c,d})surface.AddVertex(point);
            }
            root.AddChild(new MeshInstance3D { Mesh=new SphereMesh { Radius=.1f,Height=.08f,RadialSegments=8,Rings=4 },
                MaterialOverride=leaf,Position=Point(.8f),Scale=new Vector3(1,1,1.8f),
                CastShadow=GeometryInstance3D.ShadowCastingSetting.Off });
        }
        surface.GenerateNormals();
        root.AddChild(new MeshInstance3D { Mesh=surface.Commit(),MaterialOverride=bark,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off });
        return root;
    }
}
