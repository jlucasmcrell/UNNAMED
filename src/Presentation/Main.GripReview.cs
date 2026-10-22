using Godot;
using UNNAMED.Presentation.Player;
using System.Text.Json;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.MHCharacters;

namespace UNNAMED.Presentation;

public partial class Main
{
    private async Task RunRcd6GripReview(string directory, Camera3D camera)
    {
        SetProcess(false);_scatter.Visible=false;_npcs.Visible=false;_avatar.Visible=false;
        bool scan=System.Environment.GetEnvironmentVariable("OTHERREACH_RCD6_GRIP_SCAN")=="1";
        bool thumbScan=System.Environment.GetEnvironmentVariable("OTHERREACH_RCD6_GRIP_SCAN_THUMB")=="1";
        string? poseFile=System.Environment.GetEnvironmentVariable("OTHERREACH_RCD6_GRIP_POSES");
        using var poseManifest=string.IsNullOrEmpty(poseFile)?null:JsonDocument.Parse(File.ReadAllText(poseFile));
        var people=poseManifest is null?new[]{"ada","mateo"}:poseManifest.RootElement.EnumerateObject().Select(p=>p.Name).ToArray();
        int expectedFrames=0;
        var rows=new List<object>();var stage=_avatar.GlobalPosition;camera.Fov=42;
        foreach(string person in people)
        {
            Figure figure=VisualOptions.People=="mpfb"
                ? SkinnedFigure.Create(_art,_bindings,"player",person=="ada"?"mpfb_fem_lt":"mpfb_male_lt")!
                : MetaHumanFigure.Create(_art,_bindings,"player","mh:"+person)!;
            if(figure is null)throw new InvalidOperationException("Grip body missing: "+person);
            figure.Name="Grip_"+person;AddChild(figure);figure.Pose(stage,0,0,0);figure.SetFirstPerson(false);
            figure.Hold("item.weapon.rusted_sword");
            var weaponMaterials=figure.FindChildren("*",nameof(BoneAttachment3D),true,false).OfType<BoneAttachment3D>()
                .Where(a=>a.BoneName=="hand_r").SelectMany(a=>a.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>())
                .SelectMany(m=>Enumerable.Range(0,m.Mesh?.GetSurfaceCount()??0).Select(i=>m.GetActiveMaterial(i)))
                .OfType<BaseMaterial3D>().Select(m=>new {name=m.ResourceName,albedo=m.AlbedoColor.ToString(),texture=m.AlbedoTexture?.ResourcePath,
                    texture_width=m.AlbedoTexture?.GetWidth(),texture_height=m.AlbedoTexture?.GetHeight(),roughness=m.Roughness,metallic=m.Metallic,
                    roughness_channel=m.RoughnessTextureChannel.ToString(),metallic_channel=m.MetallicTextureChannel.ToString(),emissive=m.EmissionEnabled,shading=m.ShadingMode.ToString()}).ToArray();
            File.WriteAllText(Path.Combine(directory,person+"_weapon_materials.json"),JsonSerializer.Serialize(weaponMaterials,new JsonSerializerOptions{WriteIndented=true}));
            if(weaponMaterials.Length==0 || weaponMaterials.Any(m=>m.texture_width is null or <=0 || m.texture_height is null or <=0 || m.emissive))
                throw new InvalidDataException("Held sword has missing albedo or unexpected emissive material");
            Skeleton3D skeleton;Node? native=null;AnimationPlayer? animator=null;
            if(figure is MetaHumanFigure mh){native=mh.GetNode<Node>("Native");skeleton=native.Call("body_skeleton").As<Skeleton3D>();}
            else
            {
                var model=figure.GetChildren().OfType<SkinnedModel>().Single();model.SetProcess(false);
                skeleton=((SkinnedFigure)figure).Skeleton;animator=model.GetNode<AnimationPlayer>("Clips");
                animator.CallbackModeProcess=AnimationMixer.AnimationCallbackModeProcess.Manual;
            }
            skeleton.ModifierCallbackModeProcess=Skeleton3D.ModifierCallbackModeProcessEnum.Manual;
            var expectedGrip=native is null
                ? figure.GetChildren().OfType<SkinnedModel>().Single().WeaponGrip!
                : native.Call("weapon_grip_rotations").AsGodotDictionary().ToDictionary(p=>p.Key.AsInt32(),p=>p.Value.AsQuaternion());
            var authoredGrip=expectedGrip;
            var restBefore=Enumerable.Range(0,skeleton.GetBoneCount()).Select(skeleton.GetBoneRest).ToArray();
            var modifiedDigits=new Dictionary<string,float[]>();
            // Skeleton restores its unmodified pose after uploading skin matrices. Keep advancing
            // manual modifiers every render frame; a one-off Advance freezes no rendered grip.
            void TickGrip()=>skeleton.Advance(1.0/60);
            void SyncFace()
            {
                native?.Call("sync_modified_pose");
                // Read inside SkeletonUpdated: after this signal Godot restores its
                // unmodified animation pose, so later getters cannot prove the grip.
                foreach(int b in expectedGrip.Keys)
                    {
                        string name=skeleton.GetBoneName(b);
                        var q=skeleton.GetBonePoseRotation(b);
                        modifiedDigits[name]=new[]{q.X,q.Y,q.Z,q.W};
                    }
            }
            GetTree().ProcessFrame+=TickGrip;skeleton.SkeletonUpdated+=SyncFace;
            try
            {
            await Rcd3Wait(.3);
            var samples=scan?(thumbScan?new[]{("t40","sword_ready",.5f),("t55","sword_ready",.5f),("t70","sword_ready",.5f),("t85","sword_ready",.5f),("t100","sword_ready",.5f)}
                :new[]{("a55","sword_ready",.5f),("a65","sword_ready",.5f),("a75","sword_ready",.5f),("a85","sword_ready",.5f),("a100","sword_ready",.5f)})
                :new[]{("idle","sword_ready",.5f),("walk","walk",.5f),("run","run",.5f),("attack","sword_attack_1",.45f)};
            var poseCases=poseManifest is null?Array.Empty<JsonElement>():poseManifest.RootElement.GetProperty(person).EnumerateArray().ToArray();
            if(poseCases.Length>0)samples=poseCases.Select(p=>(p.GetProperty("id").GetString()!,p.TryGetProperty("clip",out var clipName)?clipName.GetString()!:"sword_ready",p.TryGetProperty("phase",out var clipPhase)?clipPhase.GetSingle():.5f)).ToArray();
            expectedFrames+=samples.Length*5;
            foreach(var (state,clip,phase) in samples)
            {
                var targetGripFrame=HeldWeapon.ClosedGrip(skeleton,"hand_r",expectedGrip);
                if(scan || poseCases.Length>0)
                {
                    if(poseCases.Length>0)
                    {
                        if(native is null)throw new InvalidOperationException("Native pose manifest requires native cast");
                        using var data=JsonDocument.Parse(File.ReadAllText(poseCases.Single(p=>p.GetProperty("id").GetString()==state).GetProperty("path").GetString()!));
                        var configuration=native.Get("_config").AsGodotDictionary();
                        if(data.RootElement.GetProperty("native_body_sha256").GetString()!=Godot.FileAccess.GetSha256(Path.Combine(MetaHumanFigure.AssetRoot,configuration["body"].AsString())))throw new InvalidDataException("Grip pose belongs to another native body");
                        var rotations=data.RootElement.GetProperty("rotations");
                        if(rotations.EnumerateObject().Count()!=15)throw new InvalidDataException("Grip must contain fifteen right digits");
                        expectedGrip=authoredGrip.Keys.ToDictionary(b=>b,b=>
                        {
                            var q=rotations.GetProperty(skeleton.GetBoneName(b).ToString()).EnumerateArray().Select(v=>v.GetSingle()).ToArray();
                            return new Quaternion(q[0],q[1],q[2],q[3]).Normalized();
                        });
                    }
                    else
                    {
                        float alpha=int.Parse(state[1..])/100f;
                        expectedGrip=authoredGrip.ToDictionary(p=>p.Key,p=>skeleton.GetBoneRest(p.Key).Basis.GetRotationQuaternion()
                            .Slerp(p.Value,thumbScan && !skeleton.GetBoneName(p.Key).ToString().StartsWith("thumb",StringComparison.Ordinal)?1:alpha));
                    }
                    if(native is not null)
                    {
                        var modifier=native.Get("_weapon_grip").As<SkeletonModifier3D>();
                        modifier.SetProcess(false);modifier.Influence=1;
                        var rotations=new Godot.Collections.Dictionary();foreach(var (b,q) in expectedGrip)rotations[b]=q;
                        modifier.Set("rotations",rotations);
                    }
                    else
                    {
                        var modifier=skeleton.GetChildren().OfType<HandGrip>().Single(m=>m.AuthoredRotations is not null);
                        modifier.AuthoredRotations=expectedGrip;modifier.UseAuthored=true;modifier.Closed=1;
                    }
                    var held=skeleton.GetChildren().OfType<BoneAttachment3D>().Where(n=>n.BoneName=="hand_r")
                        .SelectMany(n=>n.GetChildren().OfType<Node3D>()).Single(n=>n.FindChild(HeldWeapon.GripSocket,true,false) is not null);
                    var socket=(Node3D)held.FindChild(HeldWeapon.GripSocket,true,false);
                    var gripFrame=HeldWeapon.ClosedGrip(skeleton,"hand_r",expectedGrip);
                    if(poseCases.Length>0 && poseCases.Single(p=>p.GetProperty("id").GetString()==state).TryGetProperty("socket_correction",out var matrix))
                    {
                        var m=matrix.EnumerateArray().Select(r=>r.EnumerateArray().Select(v=>v.GetSingle()).ToArray()).ToArray();
                        if(m.Length!=4 || m.Any(r=>r.Length!=4))throw new InvalidDataException("Rigid grip matrix must be 4x4");
                        var correction=new Transform3D(new Basis(new Vector3(m[0][0],m[1][0],m[2][0]),new Vector3(m[0][1],m[1][1],m[2][1]),new Vector3(m[0][2],m[1][2],m[2][2])),new Vector3(m[0][3],m[1][3],m[2][3]));
                        if(correction.Origin.Length()>.035f || Math.Abs(correction.Basis.Determinant()-1)>.001f)throw new InvalidDataException("Grip correction exceeds diagnostic rigid bounds");
                        gripFrame*=correction;
                    }
                    targetGripFrame=gripFrame;
                    float weaponScale=figure is SkinnedFigure scaledFigure?1/scaledFigure.StatureScale:1;
                    held.Transform=gripFrame*new Transform3D(Basis.FromScale(Vector3.One*weaponScale),Vector3.Zero)*ArtLibrary.Relative(held,socket).AffineInverse();
                }
                if(native is not null)
                {
                    double length=native.Call("clip_length",clip).AsDouble();
                    if(!native.Call("sample_clip",clip,length*phase).AsBool())throw new InvalidOperationException("Missing grip clip "+clip);
                }
                else
                {
                    animator!.Play(clip,0);animator.Seek(animator.GetAnimation(clip).Length*phase,true);animator.Advance(0);
                }
                float maxGripError=float.MaxValue;
                // Native grip influence eases in _process, independently of manual
                // Skeleton.Advance. A wall-clock wait can span only one slow shader
                // frame, so wait for observed convergence instead of assuming it.
                for(int attempt=0;attempt<120;attempt++)
                {
                    var updated=ToSignal(skeleton,Skeleton3D.SignalName.SkeletonUpdated);skeleton.Advance(.25);await updated;
                    maxGripError=0;
                    foreach(var (name,values) in modifiedDigits)
                    {
                        var actual=new Quaternion(values[0],values[1],values[2],values[3]);
                        maxGripError=Math.Max(maxGripError,Mathf.RadToDeg(actual.AngleTo(expectedGrip[skeleton.FindBone(name)])));
                    }
                    if(modifiedDigits.Count==expectedGrip.Count && maxGripError<=.1f)break;
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
                var fingerPoses=modifiedDigits.ToDictionary(p=>p.Key,p=>p.Value.ToArray());
                if(fingerPoses.Count!=expectedGrip.Count || maxGripError>.1f)throw new InvalidOperationException("Rendered grip differs from authored target: "+maxGripError);
                native?.Call("sync_modified_pose");
                int hand=skeleton.FindBone("hand_r");var handPose=skeleton.GlobalTransform*skeleton.GetBoneGlobalPose(hand);
                var aim=handPose.Origin;
                foreach(var (view,offset) in new[]{("front",new Vector3(0,.01f,.45f)),("side",new Vector3(-.45f,.01f,0)),("threequarter",new Vector3(-.32f,.02f,.32f)),("below",new Vector3(-.1f,-.32f,.25f)),("full",new Vector3(2.4f,.15f,2.8f))})
                {
                    var target=view=="full"?figure.GlobalPosition+Vector3.Up*.95f:aim;
                    camera.GlobalPosition=target+offset;camera.LookAt(target);await Rcd3Wait(.08);
                    string file=$"{person}_{state}_{view}";await Rcd3Shot(directory,file);
                    var attachments=skeleton.GetChildren().OfType<BoneAttachment3D>().Where(n=>n.BoneName=="hand_r");
                    var weapon=attachments.SelectMany(n=>n.GetChildren().OfType<Node3D>()).FirstOrDefault(n=>n.FindChild(HeldWeapon.GripSocket,true,false) is not null);
                    if(weapon is null)throw new InvalidOperationException("Held sword missing");
                    var socketNode=(Node3D)weapon.FindChild(HeldWeapon.GripSocket,true,false);
                    var relativeSocket=ArtLibrary.Relative(weapon,socketNode);
                    float anchorError=(handPose*targetGripFrame.Origin).DistanceTo(weapon.GlobalTransform*relativeSocket.Origin)*1000;
                    float oldScaleError=0;
                    if(figure is SkinnedFigure scaled)
                    {
                        var old=targetGripFrame*relativeSocket.AffineInverse();old.Basis=old.Basis.Scaled(Vector3.One/scaled.StatureScale);
                        oldScaleError=(handPose*targetGripFrame.Origin).DistanceTo(handPose*(old*relativeSocket.Origin))*1000;
                    }
                    if(anchorError>.1f)throw new InvalidOperationException("Held socket displaced from grip (mm): "+anchorError);
                    rows.Add(new{person,state,clip,phase,view,fingerPoses,max_grip_error_degrees=maxGripError,socket_anchor_error_mm=anchorError,old_post_mount_scale_error_mm=oldScaleError,hand=handPose.ToString(),weapon=weapon.GlobalTransform.ToString(),camera=camera.GlobalPosition.ToString(),file=file+".png"});
                    if((state=="idle" || scan || poseCases.Length>0) && view=="below")GripMeshSnapshot(figure,skeleton,handPose,directory,scan||poseCases.Length>0?person+"_"+state:person);
                }
            }
            if(!Enumerable.Range(0,skeleton.GetBoneCount()).All(b=>restBefore[b]==skeleton.GetBoneRest(b)))
                throw new InvalidOperationException("Grip gate changed a native rest");
            }
            finally{GetTree().ProcessFrame-=TickGrip;skeleton.SkeletonUpdated-=SyncFace;}
            figure.Visible=false;
        }
        File.WriteAllText(Path.Combine(directory,"grip.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        if(rows.Count!=expectedFrames)throw new InvalidOperationException("Grip gate frame count incomplete");
        File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new{status="GRIP_CANDIDATE_REQUIRES_REVIEW",cast=VisualOptions.People,frames=rows.Count,native_rests_unchanged=true,digit_read_phase="SkeletonUpdated before pose restore"}));
    }

    private static void GripMeshSnapshot(Figure figure,Skeleton3D skeleton,Transform3D handPose,string directory,string person)
    {
        var vertices=new List<float[]>();var faces=new List<int[]>();
        foreach(var mesh in figure.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>())
        {
            if(mesh.Skin is null || mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton)!=skeleton)continue;
            var original=mesh.Mesh;
            // Documented GPU readback into a NEW diagnostic mesh. Never assign it to
            // the character or change any original surface/rest/bind/buffer.
            using var snapshot=mesh.BakeMeshFromCurrentSkeletonPose();
            var toHand=handPose.AffineInverse()*mesh.GlobalTransform;
            for(int surface=0;surface<snapshot.GetSurfaceCount();surface++)
            {
                var arrays=snapshot.SurfaceGetArrays(surface);
                var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Select(v=>toHand*v).ToArray();
                var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                if(indices.Length==0)indices=Enumerable.Range(0,points.Length).ToArray();
                for(int i=0;i<indices.Length;i+=3)
                {
                    if(!indices.Skip(i).Take(3).Any(v=>points[v].Length()<.18f))continue;
                    int start=vertices.Count;
                    for(int corner=0;corner<3;corner++){var p=points[indices[i+corner]];vertices.Add(new[]{p.X,p.Y,p.Z});}
                    faces.Add(new[]{start,start+1,start+2});
                }
            }
            if(mesh.Mesh!=original)throw new InvalidOperationException("Diagnostic replaced native mesh");
        }
        File.WriteAllText(Path.Combine(directory,person+"_posed_hand.json"),JsonSerializer.Serialize(new{vertices,faces,space="hand_r local",diagnostic_only=true,native_meshes_unchanged=true}));
    }
}
