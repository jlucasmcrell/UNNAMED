using Godot;
using System.Reflection;
using System.Text.Json;
using UNNAMED.Presentation;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Greybox;

// Private evidence: ordinary Main input/controller/authority and existing CameraRig, no production edits.
public partial class OutfitProof : Node
{
 static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(o)!;
 static float[] V(Vector3 p)=>new[]{p.X,p.Y,p.Z};
 public override async void _Ready() {
  try {
   string output=OS.GetEnvironment("LOCOMOTION_OUT"),arm=OS.GetEnvironment("LOCOMOTION_ARM");if(Directory.Exists(output))throw new IOException("Fresh output required");Directory.CreateDirectory(output);
   var viewport=new SubViewport {Size=new Vector2I(1920,1080),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always};AddChild(viewport);
   var main=new Main();viewport.AddChild(main);main.SetProcess(false);typeof(Main).GetField("_connectedCheck",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(main,null);
   var camera=Field<CameraRig>(main,"_camera");camera.TargetDistance=3.5f;camera.Pitch=-.3f;camera.Yaw=0;Input.MouseMode=Input.MouseModeEnum.Captured;
   var controller=Field<PlayerController>(main,"_controller");var avatar=Field<Figure>(main,"_avatar");if(avatar is not SkinnedFigure figure)throw new Exception("Actual player fell back to greybox");
   var model=Field<SkinnedModel>(figure,"_model");var player=Field<AnimationPlayer>(model,"_player");var skeleton=model.Skeleton;
   var ground=Field<GroundField>(main,"_ground");var bindings=Field<ArtBindings>(main,"_bindings");var art=Field<ArtLibrary>(main,"_art");
   var suns=main.FindChildren("*",nameof(DirectionalLight3D),true,false).OfType<DirectionalLight3D>().ToArray();var envs=main.FindChildren("*",nameof(WorldEnvironment),true,false).OfType<WorldEnvironment>().ToArray();
   object Lights()=>suns.Select(s=>new{path=s.GetPath().ToString(),s.ShadowEnabled,s.LightAngularDistance,s.ShadowBias,s.ShadowNormalBias,s.ShadowBlur,s.DirectionalShadowMaxDistance,s.DirectionalShadowSplit1,s.DirectionalShadowSplit2,s.DirectionalShadowSplit3,s.DirectionalShadowBlendSplits}).ToArray();
   string? At(string relative)=>(string?)typeof(ArtLibrary).GetMethod("At",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(art,new object[]{relative});
   var person=bindings.People["player"];var rest=SkeletonRest.Of(skeleton);
   File.WriteAllText(Path.Combine(output,"resolved.json"),JsonSerializer.Serialize(new{scope="Actual native live DLL in private Main scene; normal-speed scripted Input, side view; no rendering changes; not owner playtest",options=VisualOptions.All,model=person.Model,model_path=At(Path.Combine("rigged",person.Model,person.Model+"_rigged.glb")),person.Clips,person.Paces,clip_paths=person.Clips.ToDictionary(k=>k.Key,k=>At(Path.Combine("animation","ready",k.Value.Split('.')[0],"anim."+k.Value+".glb"))),skeleton_joints=skeleton.GetBoneCount(),hip_rest_height=rest.Height,npcs=bindings.People.Where(x=>x.Key.Contains("tavar")||x.Key.Contains("tam_crowden")).ToDictionary(x=>x.Key,x=>new{x.Value.Model,x.Value.Clips,x.Value.Paces}),lights=Lights(),taa=viewport.UseTaa,msaa=viewport.Msaa3D.ToString(),screenAA=viewport.ScreenSpaceAA.ToString(),wind=WindField.Strength,exposure=envs.Select(n=>n.Environment?.TonemapExposure).ToArray()}));
   for(int i=0;i<30;i++){main._Process(1.0/30);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
   var rows=new List<object>();
   for(int frame=0;frame<300;frame++) {
    // 1 s idle, 7 s sideways walking at authoritative speed, 2 s settle/idle, same route both arms.
    foreach(string action in new[]{"move_forward","move_right","move_left","move_back","walk"})Input.ActionRelease(action);
    string phase=frame<30?"idle":frame<240?"walk":"stop_idle";
    if(phase=="walk"){Input.ActionPress("move_right");Input.ActionPress("walk");}
    main._Process(1.0/30);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
    Vector3 Point(string bone){int i=skeleton.FindBone(bone);return skeleton.GlobalTransform*skeleton.GetBoneGlobalPose(i).Origin;}
    var body=controller.Authoritative;
    rows.Add(new{frame,phase,player= new[]{body.XMm,body.YMm,body.ZMm},avatar=V(avatar.GlobalPosition),camera=V(camera.Camera.GlobalPosition),fov=camera.Camera.Fov,arm=camera.TargetDistance,state=model.Current,animation_time=player.CurrentAnimationPosition,animation_rate=player.SpeedScale,drawn_speed=Field<float>(main,"_drawnSpeed"),hips=V(Point("hips")),root=V(Point("root")),foot_l=V(Point("foot.L")),foot_r=V(Point("foot.R")),toe_l=V(Point("toe.L")),toe_r=V(Point("toe.R")),ground_l=ground.Height(Point("toe.L").X,Point("toe.L").Z),ground_r=ground.Height(Point("toe.R").X,Point("toe.R").Z),lights=Lights(),wind=WindField.Strength,exposure=envs.Select(n=>n.Environment?.TonemapExposure).ToArray()});
    viewport.GetTexture().GetImage().SavePng(Path.Combine(output,$"view_{frame:000}.png"));if(frame%30==0)GD.Print($"LOCOMOTION_CAPTURE {arm} {frame}/300 body={body.XMm},{body.ZMm} state={model.Current} rate={player.SpeedScale}");
   }
   File.WriteAllText(Path.Combine(output,"proof.json"),JsonSerializer.Serialize(new{arm,rows}));GetTree().Quit(0);
  }catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(2);}
 }
}
