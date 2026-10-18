using Godot;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Greybox;
namespace UNNAMED.Presentation.Perf;
public partial class AtmosphereShelterCheck : Node3D
{
    public override async void _Ready()
    {
        try
        {
            WindField.Register();
            string root = ProjectSettings.GlobalizePath("res://");
            using(var white=Image.CreateEmpty(2,2,false,Image.Format.Rgba8)) { white.Fill(Colors.White); white.SavePng(Path.Combine(root,"white.png")); }
            var recipes = new ParticleRecipes(root,"""{"recipes":{"rain":{"texture":"white.png","mode":"stream","amount":8,"lifetime":0.3,"size":[0.1,0.1]}}}""");
            var camera=new Camera3D { Current=true, Position=new Vector3(0,1.7f,0) }; AddChild(camera);
            var roof=new StaticBody3D { Position=new Vector3(0,3,0), CollisionLayer=HollowView.CameraCollisionLayer, CollisionMask=0 };
            roof.AddChild(new CollisionShape3D { Shape=new BoxShape3D { Size=new Vector3(8,.3f,8) } }); AddChild(roof);
            var air=new AtmosphereView(); AddChild(air);
            if(air.Build(recipes,null!,(_,_)=>0)!=1) throw new Exception("Synthetic precipitation recipe did not build");
            async Task Settle() { for(int i=0;i<4;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame); await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
            await Settle();
            if(!air.PrecipitationSheltered || air.PrecipitationActive) throw new Exception("Camera under roof still requests precipitation");
            camera.Position=new Vector3(10,1.7f,0); await Settle();
            if(air.PrecipitationSheltered || !air.PrecipitationActive) throw new Exception("Outdoor precipitation did not resume");
            air.Visible=false; await Settle();
            if(air.PrecipitationActive) throw new Exception("Hidden exterior still requests precipitation");
            air.Visible=true; camera.Position=new Vector3(0,1.7f,0); roof.CollisionLayer=0; await Settle();
            if(air.PrecipitationSheltered || !air.PrecipitationActive) throw new Exception("Nonblocking roof layer suppressed precipitation");
            GD.Print("ATMOSPHERE SHELTER CHECK PASS: roof, outdoors, hidden exterior, collision-layer control; native physics with synthetic always-on precipitation");
            GetTree().Quit();
        }
        catch(Exception e) { GD.PrintErr(e); GetTree().Quit(1); }
    }
}
