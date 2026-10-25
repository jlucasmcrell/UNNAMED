using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Art;
using UNNAMED.Domain.Spatial;
namespace UNNAMED.Presentation;
public partial class Main
{
    private async Task RunCharacterQualityReview(string directory,Camera3D gameplay,SkinnedFigure player,FaceDriver face,string bodyHash)
    {
        var model=player.FindChildren("*","",true,false).OfType<SkinnedModel>().Single();
        _rcd3Driving=true;_innFreeCamera=true;_controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);player.ForceLod0=true;
        var close=new Camera3D{Fov=42,Near=gameplay.Near,Far=gameplay.Far,CullMask=gameplay.CullMask,Attributes=gameplay.Attributes,Environment=gameplay.Environment};
        AddChild(close);gameplay.GetNode<OmniLight3D>("CharacterFill").Reparent(close,false);
        var rig=player.Skeleton;var oldMode=rig.ProcessMode;var before=new List<object>();
        async Task Settle(int frames) { for(int i=0;i<frames;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
        object[] Maps(SkinnedFigure figure)=>figure.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>()
            .Where(m=>m.Name.ToString().EndsWith("_Body_LOD0",StringComparison.Ordinal)||m.Name.ToString().EndsWith("_Eyes_LOD0",StringComparison.Ordinal))
            .SelectMany(m=>Enumerable.Range(0,m.Mesh.GetSurfaceCount()).Select(i=>
            {
                var mat=m.GetSurfaceOverrideMaterial(i)??m.Mesh.SurfaceGetMaterial(i);
                Texture2D? colour=mat is ShaderMaterial skin?skin.GetShaderParameter("texture_albedo").As<Texture2D>(): (mat as BaseMaterial3D)?.AlbedoTexture;
                Texture2D? normal=mat is ShaderMaterial shader?shader.GetShaderParameter("texture_normal").As<Texture2D>(): (mat as BaseMaterial3D)?.NormalTexture;
                return (object)new {mesh=m.Name.ToString(),colour_width=colour?.GetWidth(),normal_width=normal?.GetWidth()};
            })).ToArray();
        try
        {
            await Settle(20);ulong deadline=Time.GetTicksMsec()+3000;
            while(face.BlinkWeight>.25f && Time.GetTicksMsec()<deadline)await Settle(1);
            face.SetProcess(false);face.ReviewJawOpen=0;
            foreach(var clip in player.FindChildren("*",nameof(AnimationPlayer),true,false).OfType<AnimationPlayer>()){clip.Pause();clip.ProcessMode=Node.ProcessModeEnum.Disabled;}
            foreach(var tree in player.FindChildren("*",nameof(AnimationTree),true,false).OfType<AnimationTree>()){tree.Active=false;tree.ProcessMode=Node.ProcessModeEnum.Disabled;}
            rig.ProcessMode=Node.ProcessModeEnum.Disabled;
            int l=rig.FindBone("LeftEye"),r=rig.FindBone("RightEye"),h=rig.FindBone("Head");
            var eyes=rig.GlobalTransform*((rig.GetBoneGlobalPose(l).Origin+rig.GetBoneGlobalPose(r).Origin)*.5f);
            var head=rig.GlobalTransform*rig.GetBoneGlobalPose(h).Origin;var ahead=eyes-head;ahead.Y=0;
            var aim=eyes-Vector3.Up*.045f;close.GlobalPosition=aim+ahead.Normalized()*1.0f+Vector3.Up*.02f;close.LookAt(aim);close.MakeCurrent();
            var pose=rig.GetBoneGlobalPose(h);var camera=close.GlobalTransform;var origin=player.GlobalTransform;
            foreach(bool high in new[]{false,true})
            {
                model.ReviewCloseTextures(high);await Settle(24);
                var bodyMesh=player.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>().Single(m=>m.Name.ToString().EndsWith("_Body_LOD0",StringComparison.Ordinal));
                var bodyMat=(ShaderMaterial)bodyMesh.GetSurfaceOverrideMaterial(0);
                if(bodyMat.GetShaderParameter("texture_albedo").As<Texture2D>().GetWidth()!=(high?4096:2048))throw new InvalidDataException("Player skin resolution did not switch");
                var eyeMesh=player.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>().Single(m=>m.Name.ToString().EndsWith("_Eyes_LOD0",StringComparison.Ordinal));
                if(((BaseMaterial3D)eyeMesh.GetSurfaceOverrideMaterial(0)).AlbedoTexture.GetWidth()!=(high?1024:512))throw new InvalidDataException("Player eye resolution did not switch");
                await Rcd3Shot(directory,high?"skin_4k":"skin_2k");
                if(!camera.IsEqualApprox(close.GlobalTransform)||!origin.IsEqualApprox(player.GlobalTransform)||!pose.IsEqualApprox(rig.GetBoneGlobalPose(h)))
                    throw new InvalidDataException("Quality A/B camera or pose moved");
                var row=new {high,model=player.ModelId,body_sha256=bodyHash,camera=camera.ToString(),pose=pose.ToString(),position=player.GlobalPosition.ToString(),hour=VisualOptions.Hour,
                    maps=Maps(player),blink=face.BlinkWeight,skin="accepted matte + pore0.35 + lid rest",render_texture_mib=Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed)/1048576.0};
                before.Add(row);
            }
            var npcs=GetTree().CurrentScene.FindChildren("*","",true,false).OfType<SkinnedFigure>().Where(f=>f!=player).Select(f=>new {model=f.ModelId,maps=Maps(f)}).ToArray();
            File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {status="SAME_POSE_2K_4K_REQUIRES_VISUAL_AND_VRAM_REVIEW",source="PRIVATE_CHARACTER_QUALITY",body_sha256=bodyHash,near_player_only=true,npcs,before},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            model.ReviewCloseTextures(null);face.SetProcess(true);face.ReviewJawOpen=null;rig.ProcessMode=oldMode;player.ForceLod0=false;
            close.GetNode<OmniLight3D>("CharacterFill").Reparent(gameplay,false);close.QueueFree();gameplay.MakeCurrent();_innFreeCamera=false;
        }
    }
}
