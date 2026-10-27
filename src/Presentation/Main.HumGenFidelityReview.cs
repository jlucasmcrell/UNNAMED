using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Art;
namespace UNNAMED.Presentation;

public partial class Main
{
    // One opt-in gate: actual ArtLibrary import/material/cache path, neutral native pose, no people recolour or motion.
    private async Task RunHumGenFidelityReview(string directory, Camera3D camera)
    {
        _hud.Visible = false; _avatar.Visible = false; _npcs.Visible = false;
        camera.GetNode<OmniLight3D>("CharacterFill").Visible = false;
        var feet = _avatar.GlobalPosition;
        var evidence = new List<object>();
        var studio = new SubViewport { Size = new Vector2I(1080,1080), OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, UseTaa = true };
        AddChild(studio);
        var studioRoot = new Node3D(); studio.AddChild(studioRoot);
        studioRoot.AddChild(new WorldEnvironment { Environment = new Godot.Environment {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black,
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White,
            AmbientLightEnergy = .06f, ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Filmic, TonemapExposure = 1f } });
        var studioCamera = new Camera3D { Fov=22, Near=.01f, Current=true }; studioRoot.AddChild(studioCamera);
        void Light(Vector3 position,float energy,float size)
        {
            var light=new OmniLight3D { Position=position,LightEnergy=energy,OmniRange=5,LightSize=size,ShadowEnabled=true };
            studioRoot.AddChild(light);
        }
        foreach (string sex in new[]{"female","male"})
        {
            string id="humgen_fidelity_"+sex+"_caucasian2";
            var model=_art.Rigged(id) ?? throw new InvalidDataException("Native preset failed: "+id);
            HumGenIdentityWeights.Apply(model,_art,id);
            int upgraded=CharacterMaterials.Upgrade(model,out _,new Dictionary<string,HumanEye.Maps>(),nativeHumGen:true);
            HumGenMaterials.Apply(model,_art,id);
            HumGenMouth.Apply(model,_art,id);
            var lods=new HumGenLods(model); lods.Update(0);
            studioRoot.AddChild(model);
            var meshes=model.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>().ToArray();
            var body=meshes.Single(m=>m.Name.ToString().EndsWith("_Body_LOD0",StringComparison.Ordinal));
            var box=body.Transform*body.Mesh.GetAabb();
            var aim=new Vector3(0,box.End.Y-.16f,0);
            Light(new Vector3(-.75f,aim.Y+.55f,1.1f),2.3f,.6f);
            Light(new Vector3(.8f,aim.Y+.1f,.8f),.65f,.8f);
            Light(new Vector3(.8f,aim.Y+.45f,-.15f),1.2f,.3f);
            studioCamera.Position=aim+new Vector3(0,0,.85f);studioCamera.LookAt(aim);
            await Rcd3Wait(2);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            studio.GetTexture().GetImage().SavePng(Path.Combine(directory,sex+"_studio.png"));
            foreach(var light in studioRoot.GetChildren().OfType<Light3D>())light.QueueFree();
            model.Reparent(this,false);model.GlobalPosition=feet;
            camera.Fov=22;camera.Near=.01f;camera.GlobalPosition=feet+aim+new Vector3(0,0,.85f);camera.LookAt(feet+aim);
            await Rcd3Wait(2);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory,sex+"_noon.png"));
            evidence.Add(new { id,upgraded,lod=lods.Level,neutral=true,peopleOverrides=false,
                meshes=meshes.Where(m=>m.Visible).Select(m=>new {name=m.Name.ToString(),
                    materials=Enumerable.Range(0,m.Mesh.GetSurfaceCount()).Select(s=>
                    {
                        var material=m.GetSurfaceOverrideMaterial(s)??m.Mesh.SurfaceGetMaterial(s);
                        object? Map(string key) => material is ShaderMaterial shader && shader.GetShaderParameter(key).AsGodotObject() is Texture2D t
                            ? new {width=t.GetWidth(),height=t.GetHeight()} : null;
                        return new {name=material?.ResourceName,type=material?.GetType().Name,
                            albedo=Map("texture_albedo")??Map("color_map"),normal=Map("texture_normal")??Map("normal_map"),
                            nativeSpecular=Map("native_rough_spec_cavity"),scatter=Map("scatter_map")};
                    }).ToArray()}).ToArray(),
                cacheBytes=_art.Cache.CompressedBytes,
                renderVramBytes=RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed),
                textureVramBytes=RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed) });
            model.QueueFree();await Rcd3Wait(.2);
        }
        File.WriteAllText(Path.Combine(directory,"fidelity.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(directory,"complete.json"),"{\"gate\":\"HumGen A\"}");
    }
}
