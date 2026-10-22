using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Opt-in real-time normal-controller evidence. Master bus only; no microphone.
    private async Task RunFootstepReview(string directory)
    {
        _rcd3Driving=true;
        while(GetViewport().Disable3D)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        ulong warm=Time.GetTicksMsec()+2000;
        while(Time.GetTicksMsec()<warm)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        if(OS.GetEnvironment("OTHERREACH_FOOTSTEP_MATERIAL_AUDIT")=="1")
        {
            string item=_session.Simulation!.Combat.Weapon.Source;
            var weapon=_avatar.FindChildren("*",nameof(BoneAttachment3D),true,false).OfType<BoneAttachment3D>()
                .SelectMany(a=>a.GetChildren().OfType<Node3D>()).FirstOrDefault(n=>n.FindChild(Art.HeldWeapon.GripSocket,true,false) is not null);
            if(weapon is null)throw new InvalidDataException("Normal-play held weapon missing: "+item);
            var materials=new List<object>();int imageIndex=0;
            foreach(var mesh in weapon.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>())
                for(int surface=0;surface<mesh.Mesh.GetSurfaceCount();surface++)
                {
                    var active=mesh.GetActiveMaterial(surface);var material=active as BaseMaterial3D;
                    if(material?.AlbedoTexture is { } texture)
                    {
                        using var image=texture.GetImage();
                        if(image.IsCompressed())image.Decompress();
                        image.SavePng(Path.Combine(directory,"held_albedo_"+(imageIndex++)+".png"));
                    }
                    materials.Add(new{mesh=mesh.Name.ToString(),surface,active=active?.GetClass(),name=active?.ResourceName,
                        shader=(active as ShaderMaterial)?.Shader?.ResourcePath,material_override=mesh.MaterialOverride?.GetClass(),overlay=mesh.MaterialOverlay?.GetClass(),next_pass=active?.NextPass?.GetClass(),
                        albedo=material?.AlbedoColor.ToString(),vertex_colour=material?.VertexColorUseAsAlbedo,roughness=material?.Roughness,metallic=material?.Metallic,
                        metallic_channel=material?.MetallicTextureChannel.ToString(),roughness_channel=material?.RoughnessTextureChannel.ToString(),emissive=material?.EmissionEnabled,layers=mesh.Layers});
                }
            File.WriteAllText(Path.Combine(directory,"normal_held_materials.json"),JsonSerializer.Serialize(new{item,model=_bindings.Weapons.GetValueOrDefault(item)?.Model,materials},new JsonSerializerOptions{WriteIndented=true}));
            _innFreeCamera=true;
            var auditCamera=new Camera3D { Fov=42,Near=.01f };AddChild(auditCamera);
            var fillLight=_camera.Camera.GetNode<OmniLight3D>("CharacterFill");fillLight.Reparent(auditCamera,false);
            auditCamera.MakeCurrent();
            try
            {
                var socket=(Node3D)weapon.FindChild(Art.HeldWeapon.GripSocket,true,false);
                foreach(var view in new[]{("hand",Vector3.Zero),("blade",Vector3.Right*.45f)})
                {
                    var aim=socket.GlobalTransform*view.Item2;
                    var meshes=weapon.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>().ToArray();
                    var layers=meshes.Select(m=>m.Layers).ToArray();
                    try
                    {
                        foreach(bool fill in new[]{false,true})
                        {
                            for(int i=0;i<meshes.Length;i++)meshes[i].Layers=(layers[i]&~MHCharacters.MetaHumanFigure.FillLayer)|(fill?MHCharacters.MetaHumanFigure.FillLayer:0u);
                            for(int frame=0;frame<6;frame++){auditCamera.GlobalPosition=aim+new Vector3(-.32f,.02f,.32f);auditCamera.LookAt(aim);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
                            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory,"normal_held_"+view.Item1+(fill?"_after":"_before")+".png"));
                        }
                    }
                    finally{for(int i=0;i<meshes.Length;i++)meshes[i].Layers=layers[i];}
                }
            }
            finally{fillLight.Reparent(_camera.Camera,false);_camera.Camera.MakeCurrent();auditCamera.QueueFree();_innFreeCamera=false;}
        }
        bool baseline=OS.GetEnvironment("OTHERREACH_FOOTSTEP_REVIEW_BASELINE")=="1";
        DisplayServer.WindowSetTitle("Otherreach - "+Path.GetFileName(directory));
        if(!baseline && !_avatar.HasFootPlantEvents)throw new InvalidDataException("Footstep gate loaded no baked contact events");
        var events=new List<object>();string phase="sync";
        void Contact(string clip,string side,double seconds)=>events.Add(new{phase,clip,side,contact_seconds=seconds,ticks_usec=Time.GetTicksUsec()});
        _avatar.FootPlanted+=Contact;
        _camera.Yaw=-Mathf.Pi/2;_camera.TargetDistance=3.8f;_camera.Pitch=-.35f;
        bool hudVisible=_hud.Visible;_hud.Visible=false;
        // This isolated foot-contact gate exposes ankles; normal gameplay keeps its scatter.
        foreach(var plant in _scatter.FindChildren("*",nameof(MultiMeshInstance3D),true,false).OfType<MultiMeshInstance3D>())
            if(plant.CastShadow==GeometryInstance3D.ShadowCastingSetting.Off)plant.Visible=false;
        int bus=AudioServer.GetBusIndex("Master"),effectIndex=AudioServer.GetBusEffectCount(bus);
        using var recording=new AudioEffectRecord();AudioServer.AddBusEffect(bus,recording);
        var syncLayer=new CanvasLayer { Layer=100 };
        var flash=new ColorRect { Color=Colors.Magenta,Position=new Vector2(4,4),Size=new Vector2(48,48),Visible=false,MouseFilter=Control.MouseFilterEnum.Ignore };
        AddChild(syncLayer);syncLayer.AddChild(flash);
        var toneData=new byte[4800*2];
        for(int i=0;i<4800;i++)BitConverter.GetBytes((short)(800*Math.Sin(2*Math.PI*2000*i/48000))).CopyTo(toneData,i*2);
        using var tone=new AudioStreamWav { Format=AudioStreamWav.FormatEnum.Format16Bits,MixRate=48000,Stereo=false,Data=toneData };
        var beep=new AudioStreamPlayer { Stream=tone,Bus="Master" };AddChild(beep);
        using var frames=new StreamWriter(Path.Combine(directory,"foot_frames.csv"));
        frames.WriteLine("ticks_usec,phase,player_x,player_y,player_z,left_x,left_y,left_z,right_x,right_y,right_z,clip");
        try
        {
            File.WriteAllText(Path.Combine(directory,"record_ready.json"),JsonSerializer.Serialize(new{baseline,cast=VisualOptions.People,contacts=_avatar.HasFootPlantEvents,max_fps=Engine.MaxFps,shadowless_scatter_hidden_for_ankle_visibility=true,audio_output_latency_seconds=AudioServer.GetOutputLatency(),sync="quiet 2kHz tone and magenta corner square; align recorded Master bus to desktop video"}));
            ulong deadline=Time.GetTicksMsec()+300000;
            while(!File.Exists(Path.Combine(directory,"recording.started")))
            {
                if(Time.GetTicksMsec()>deadline)throw new TimeoutException("Recorder not ready");
                // WGC may wait for window damage. Grey is readiness only; magenta is sync.
                flash.Visible=true;
                flash.Color=Time.GetTicksMsec()%500<250 ? new Color(.1f,.1f,.1f) : new Color(.2f,.2f,.2f);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            recording.SetRecordingActive(true);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            flash.Color=Colors.Magenta;flash.Visible=true;beep.Play();
            ulong syncEnd=Time.GetTicksMsec()+150;
            while(Time.GetTicksMsec()<syncEnd)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            flash.Visible=false;
            ulong settle=Time.GetTicksMsec()+1000;
            while(Time.GetTicksMsec()<settle)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            foreach(var step in new[]{("walk",Vector3.Right,Gait.Walk,4), ("back",Vector3.Left,Gait.Walk,4),
                ("strafe_left",Vector3.Forward,Gait.Walk,3),("strafe_right",Vector3.Back,Gait.Walk,3),
                ("run",Vector3.Right,Gait.Run,4),("back_return",Vector3.Left,Gait.Walk,6)})
            {
                phase=step.Item1;ulong through=Time.GetTicksMsec()+(ulong)step.Item4*1000;
                var from=_avatar.GlobalPosition;
                while(Time.GetTicksMsec()<through)
                {
                    _controller.SteerWorld(step.Item2,step.Item3,_camera,faceCamera:true);
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    var p=_avatar.GlobalPosition;var l=_avatar.Foot("L")??p;var r=_avatar.Foot("R")??p;
                    frames.WriteLine(FormattableString.Invariant($"{Time.GetTicksUsec()},{phase},{p.X},{p.Y},{p.Z},{l.X},{l.Y},{l.Z},{r.X},{r.Y},{r.Z},{_avatar.ClipState}"));
                }
                if(_avatar.GlobalPosition.DistanceTo(from)<.5f)throw new InvalidOperationException("Footstep route blocked: "+phase);
            }
            if(OS.GetEnvironment("OTHERREACH_CALM_MOTION_REVIEW")=="1")
            {
                _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);
                bool attachmentReview=OS.GetEnvironment("OTHERREACH_SWORD_ATTACHMENT_REVIEW")=="1";
                var attachmentRows=new List<object>();
                void AuditSword()
                {
                    if(!attachmentReview)return;
                    var right=_avatar.FindChildren("*",nameof(BoneAttachment3D),true,false).OfType<BoneAttachment3D>()
                        .Single(a=>a.BoneName=="hand_r");
                    var socket=right.FindChild(Art.HeldWeapon.GripSocket,true,false) as Node3D;
                    var meshes=right.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>().ToArray();
                    bool drawable=meshes.Any(m=>m.IsVisibleInTree() && m.CastShadow!=GeometryInstance3D.ShadowCastingSetting.ShadowsOnly);
                    if(socket is null || !drawable || right.GlobalPosition.DistanceTo(socket.GlobalPosition)>.20f)
                        throw new InvalidDataException("Sword attachment lost during "+phase+"/"+_avatar.ClipState);
                    attachmentRows.Add(new{phase,clip=_avatar.ClipState,ticks_usec=Time.GetTicksUsec(),
                        drawable,hand=right.GlobalPosition.ToString(),socket=socket.GlobalPosition.ToString(),
                        socket_from_hand_m=right.GlobalPosition.DistanceTo(socket.GlobalPosition),
                        weapon=_session.Simulation!.Combat.Weapon.Source});
                }
                var attacks=new HashSet<string>();
                for(int strike=1;strike<=3;strike++)
                {
                    phase="attack_"+strike;_controller.Attack();
                    ulong through=Time.GetTicksMsec()+3000;
                    while(Time.GetTicksMsec()<through)
                    {
                        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                        attacks.Add(_avatar.ClipState);AuditSword();
                        var p=_avatar.GlobalPosition;var l=_avatar.Foot("L")??p;var r=_avatar.Foot("R")??p;
                        frames.WriteLine(FormattableString.Invariant($"{Time.GetTicksUsec()},{phase},{p.X},{p.Y},{p.Z},{l.X},{l.Y},{l.Z},{r.X},{r.Y},{r.Z},{_avatar.ClipState}"));
                    }
                }
                // Required motion-review angles use a separate render camera. The real
                // controller's facing camera stays fixed, so backing remains backing.
                phase="clear_ground_approach";
                foreach(var approach in new[]{Vector3.Right,Vector3.Forward})
                {
                    ulong through=Time.GetTicksMsec()+4000;
                    while(Time.GetTicksMsec()<through)
                    {
                        _controller.SteerWorld(approach,Gait.Run,_camera,faceCamera:true);
                        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    }
                }
                _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);
                var angleCamera=new Camera3D { Fov=50,Near=.05f };AddChild(angleCamera);
                var fill=_camera.Camera.GetNode<OmniLight3D>("CharacterFill");fill.Reparent(angleCamera,false);
                var angleLabel=new Label { Position=new Vector2(64,8) };syncLayer.AddChild(angleLabel);
                try
                {
                    foreach(var angle in attachmentReview ? new[]{("sword_side",-Mathf.Pi/2),("sword_threequarter",-Mathf.Pi/4)}
                        : new[]{("side",Mathf.Pi/2),("threequarter",Mathf.Pi/4)})
                    {
                        void FrameAngle()
                        {
                            var aim=_avatar.GlobalPosition+Vector3.Up*.95f;
                            angleCamera.GlobalPosition=aim+_avatar.GlobalBasis.Z.Rotated(Vector3.Up,angle.Item2)*3.5f+Vector3.Up*.2f;
                            angleCamera.LookAt(aim);angleCamera.MakeCurrent();
                        }
                        foreach(var motion in new[]{("walk_back",Vector3.Left,Gait.Walk,3),("run_back",Vector3.Left,Gait.Run,3),
                            ("strafe_left",Vector3.Forward,Gait.Walk,2),("strafe_right",Vector3.Back,Gait.Walk,2)})
                        {
                            phase=angle.Item1+"_"+motion.Item1;angleLabel.Text=phase+" | real time | 3.5 m";
                            ulong through=Time.GetTicksMsec()+(ulong)motion.Item4*1000;
                            while(Time.GetTicksMsec()<through)
                            {
                                _controller.SteerWorld(motion.Item2,motion.Item3,_camera,faceCamera:true);FrameAngle();
                                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                                var p=_avatar.GlobalPosition;var l=_avatar.Foot("L")??p;var r=_avatar.Foot("R")??p;
                                frames.WriteLine(FormattableString.Invariant($"{Time.GetTicksUsec()},{phase},{p.X},{p.Y},{p.Z},{l.X},{l.Y},{l.Z},{r.X},{r.Y},{r.Z},{_avatar.ClipState}"));
                            }
                        }
                        _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);
                        for(int strike=1;strike<=3;strike++)
                        {
                            phase=angle.Item1+"_attack_"+strike;angleLabel.Text=phase+" | real AttackCommand";
                            _controller.Attack();ulong through=Time.GetTicksMsec()+3000;
                            while(Time.GetTicksMsec()<through)
                            {
                                FrameAngle();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                                attacks.Add(_avatar.ClipState);AuditSword();
                                var p=_avatar.GlobalPosition;var l=_avatar.Foot("L")??p;var r=_avatar.Foot("R")??p;
                                frames.WriteLine(FormattableString.Invariant($"{Time.GetTicksUsec()},{phase},{p.X},{p.Y},{p.Z},{l.X},{l.Y},{l.Z},{r.X},{r.Y},{r.Z},{_avatar.ClipState}"));
                            }
                        }
                    }
                }
                finally{fill.Reparent(_camera.Camera,false);_camera.Camera.MakeCurrent();angleCamera.QueueFree();angleLabel.QueueFree();}
                File.WriteAllText(Path.Combine(directory,"attack_states.json"),JsonSerializer.Serialize(attacks));
                if(attachmentReview)File.WriteAllText(Path.Combine(directory,"sword_attachment.json"),JsonSerializer.Serialize(attachmentRows));
                foreach(string required in new[]{"sword_attack_1","sword_attack_2","sword_attack_3"})
                    if(!attacks.Contains(required))throw new InvalidOperationException("Normal controller did not show "+required);
            }
            File.WriteAllText(Path.Combine(directory,"foot_events.json"),JsonSerializer.Serialize(events));
            if(!baseline && events.Count<12)throw new InvalidOperationException("Too few audible contact candidates");
            File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new{status="FOOTSTEP_REQUIRES_AUDIO_VISUAL_REVIEW",baseline,cast=VisualOptions.People,events=events.Count,max_fps=Engine.MaxFps}));
        }
        finally
        {
            _hud.Visible=hudVisible;
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);_rcd3Driving=false;
            recording.SetRecordingActive(false);
            using var audio=recording.GetRecording();
            if(audio is not null && audio.Data.Length>0)audio.SaveToWav(Path.Combine(directory,"game_audio.wav"));
            AudioServer.RemoveBusEffect(bus,effectIndex);beep.QueueFree();
            _avatar.FootPlanted-=Contact;frames.Flush();
            // Keep a changing tail frame until all motion timestamps reached WGC.
            ulong tail=Time.GetTicksMsec()+750;
            while(Time.GetTicksMsec()<tail)
            {
                flash.Color=Time.GetTicksMsec()%250<125 ? new Color(.1f,.1f,.1f) : new Color(.2f,.2f,.2f);
                flash.Visible=true;
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            }
            syncLayer.QueueFree();
            File.WriteAllText(Path.Combine(directory,"recording.finished"),DateTimeOffset.Now.ToString("o"));
            ulong stop=Time.GetTicksMsec()+30000;
            while(!File.Exists(Path.Combine(directory,"recording.stopped")) && Time.GetTicksMsec()<stop)
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
    }
}
