using Godot;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation.Diagnostics;

public partial class CameraBoomGate : Node3D
{
    public override async void _Ready()
    {
        try
        {
            void Wall(Vector3 position,Vector3 size)
            {
                var body=new StaticBody3D {Position=position,CollisionLayer=1,CollisionMask=0};
                body.AddChild(new CollisionShape3D {Shape=new BoxShape3D {Size=size}});AddChild(body);
            }
            Wall(new Vector3(0,1.5f,0),new Vector3(.1f,3,12));
            Wall(new Vector3(0,1.5f,0),new Vector3(12,3,.1f));
            Wall(new Vector3(0,2.3f,0),new Vector3(12,.1f,12));
            var camera=new CameraRig();AddChild(camera);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            camera.Pitch=0;camera.Yaw=0;camera.Follow(new Vector3(-5,0,-5),1);
            if(camera.EffectiveDistance<3.45f || camera.Camera.GlobalPosition.DistanceTo(camera.GlobalPosition)<3.45f)
                throw new InvalidDataException("Clear camera did not extend in the same rendered frame");
            int cases=1;
            using var sphere=new SphereShape3D {Radius=.18f};
            foreach(var shoulder in new[]{Shoulder.Right,Shoulder.Left,Shoulder.Center})
            foreach(float pitch in new[]{-.8f,-.3f,.2f})
            for(int angle=0;angle<360;angle+=30)
            {
                camera.Side=shoulder;camera.Pitch=pitch;camera.Yaw=Mathf.DegToRad(angle);
                camera.Follow(new Vector3(-.35f,0,-.35f),1);
                var query=new PhysicsShapeQueryParameters3D {Shape=sphere,CollisionMask=1,Transform=new Transform3D(Basis.Identity,camera.Camera.GlobalPosition)};
                if(GetWorld3D().DirectSpaceState.IntersectShape(query).Count>0)
                    throw new InvalidDataException($"Camera intersects wall/roof: {shoulder}/{pitch}/{angle} at {camera.Camera.GlobalPosition}");
                var sight=PhysicsRayQueryParameters3D.Create(camera.GlobalPosition,camera.Camera.GlobalPosition,1);
                if(GetWorld3D().DirectSpaceState.IntersectRay(sight).Count>0)
                    throw new InvalidDataException($"Camera passed through a wall/roof: {shoulder}/{pitch}/{angle}");
                var expected=new Basis(Vector3.Up,camera.Yaw)*new Basis(Vector3.Right,pitch);
                if(camera.Camera.GlobalBasis.Z.Normalized().Dot(expected.Z.Normalized())<.9999f)
                    throw new InvalidDataException("Shoulder sweep changed the viewing direction");
                cases++;
            }
            GD.Print($"CAMERA_BOOM_GATE_PASS {cases} immediate render-frame clear/wall/corner/roof sweeps; no physics waits; view direction unchanged");
            GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
}
