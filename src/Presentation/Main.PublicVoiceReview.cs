using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Opt-in review only: one fresh real conversation, recorded through the production Master bus.
    private async Task RunPublicVoiceReview(string directory, Camera3D camera, string npc)
    {
        if (npc is not ("renn_vale" or "kera_voss" or "sel_arien" or "tavar_orr"))
            throw new InvalidDataException("Unapproved public voice review NPC");
        File.WriteAllText(Path.Combine(directory,"record_ready.json"),"{}");
        ulong deadline=Time.GetTicksMsec()+120000;
        while(!File.Exists(Path.Combine(directory,"recording.started")))
        {
            if(Time.GetTicksMsec()>deadline)throw new TimeoutException("Voice WGC recorder not ready");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
        int bus=AudioServer.GetBusIndex("Master"),index=AudioServer.GetBusEffectCount(bus);
        using var recording=new AudioEffectRecord();AudioServer.AddBusEffect(bus,recording);
        recording.SetRecordingActive(true);
        var sim=_session.Simulation ?? throw new InvalidDataException("Voice gate has no world");
        async Task WalkTo(double x,double z)
        {
            var goal=new Body((long)Math.Round(x*1000),0,(long)Math.Round(z*1000),0);
            ulong walkingDeadline=Time.GetTicksMsec()+90000;
            foreach(var corner in Playthrough.WayTo(sim,_controller.Authoritative,goal,0))
                while(true)
                {
                    var body=_controller.Authoritative;
                    var to=new Vector3((float)(corner.X-body.XMm/1000.0),0,(float)(corner.Z-body.ZMm/1000.0));
                    if(to.Length()<.25f)break;
                    if(Time.GetTicksMsec()>walkingDeadline)throw new TimeoutException("Voice walk blocked at "+body);
                    _camera.Yaw=Mathf.Atan2(-to.X,-to.Z);_controller.SteerWorld(to.Normalized(),Gait.Run,_camera);
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);await Rcd3Wait(.3);
        }
        try
        {
            if(npc=="renn_vale" && !_controller.IsOpen("door.longhouse"))
            {
                await WalkTo(453.3,728);_controller.Interact("door.longhouse");await Rcd3Wait(1);
                if(!_controller.IsOpen("door.longhouse"))throw new InvalidDataException("Normal longhouse door interaction refused");
                await WalkTo(450.5,728);
            }
            if (npc == "tavar_orr")
            {
                foreach(var site in new[]{("switch.stone_north",547.2,678.0),("switch.stone_southwest",524.8,638.0),
                    ("switch.stone_southeast",583.8,631.0),("switch.foldscar_heart",553.0,652.3)})
                {
                    await WalkTo(site.Item2,site.Item3);
                    _controller.Interact(site.Item1);await Rcd3Wait(1);
                    if(!sim.Switches.Single(x=>x.Site.Key==site.Item1).Set)
                        throw new InvalidDataException("Normal Foldscar interaction refused: "+site.Item1);
                    CloseScreens();
                }
                await Rcd3Wait(2);
            }
            await RunNormalVoicedGreeting(Path.Combine(directory,npc),camera,"npc.ashen_hollow."+npc);
            if(npc=="renn_vale" && OS.GetEnvironment("OTHERREACH_RACE_LOOKS_PLAN") is {Length:>0})
            {
                File.WriteAllText(Path.Combine(directory,"optional_preview_started.json"),JsonSerializer.Serialize(new {uptime_ms=Time.GetTicksMsec(),utc=DateTimeOffset.UtcNow}));
                await RunRaceLooksReview(directory,camera);
            }
            File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
                npc,status="FULL_GREETING_CAPTURED_AUDIO_VISUAL_REVIEW_PENDING",speaking=_voice.Speaking}));
        }
        finally
        {
            recording.SetRecordingActive(false);recording.GetRecording()?.SaveToWav(Path.Combine(directory,"game_master.wav"));
            AudioServer.RemoveBusEffect(bus,index);File.WriteAllText(Path.Combine(directory,"recording.finished"),"done");
        }
        ulong stopped=Time.GetTicksMsec()+15000;
        while(!File.Exists(Path.Combine(directory,"recording.stopped")) && Time.GetTicksMsec()<stopped)
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    }

    private async Task RunNormalVoicedGreeting(string directory, Camera3D gameplay, string npcId)
    {
        Directory.CreateDirectory(directory);
        _rcd3Driving=true;
        double reach=(_session.Setup.Items.Inventory.ReachMm+_session.Setup.Movement.BodyRadiusMm)/1000.0;
        if(!await Approach(npcId,Math.Max(.8,reach-.25),90))throw new InvalidDataException("Normal greeting approach blocked");
        var figure=_npcs.FigureOf(npcId) as Art.SkinnedFigure ?? throw new InvalidDataException("Normal greeting requires native HumGen");
        var face=figure.FindChildren("*","",true,false).OfType<Art.FaceDriver>().Single();
        string expected="dialogue.ashen_hollow."+npcId.Split('.').Last()+"/greet";
        _voice.Preload(expected);
        _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);
        await Rcd3Wait(.3);
        _controller.Talk(npcId);
        ulong deadline=Time.GetTicksMsec()+5000;
        while(_session.Simulation!.Conversation?.NpcId!=npcId)
        {
            if(Time.GetTicksMsec()>deadline)throw new InvalidDataException("Normal dialogue never opened");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        var talk=_session.Simulation.Conversation!;
        if(talk.LineId!=expected || !_dialogue.Visible || GetViewport().CanvasCullMask==0)
            throw new InvalidDataException("Greeting panel/line must be visible before voice");
        ulong start=Time.GetTicksMsec();
        var rows=new List<object>();
        Camera3D? close=null;
        float peak=0;
        try
        {
            while(_voice.Speaking || Time.GetTicksMsec()-start<150)
            {
                if(Time.GetTicksMsec()-start>45000)throw new TimeoutException("Full greeting did not finish");
                // First prove the ordinary talk-camera/UI. Then inspect the SAME uninterrupted line close-up.
                if(close is null && Time.GetTicksMsec()-start>3000)
                {
                    close=new Camera3D {Fov=32,Near=gameplay.Near,Far=gameplay.Far,CullMask=gameplay.CullMask,
                        Attributes=gameplay.Attributes,Environment=gameplay.Environment};
                    AddChild(close);_innFreeCamera=true;
                    var aim=(figure.Head ?? throw new InvalidDataException("No native head"))-Vector3.Up*.07f;
                    close.GlobalPosition=aim+figure.GlobalBasis.Orthonormalized().Z*.85f+Vector3.Up*.02f;
                    close.LookAt(aim);close.MakeCurrent();
                }
                var speech=_voice.Speech(talk.LineId);
                if(speech is null || _session.Simulation.Conversation?.LineId != expected)
                    throw new InvalidDataException("Greeting was replaced or interrupted before completion");
                var current=GetViewport().GetCamera3D();
                peak=Math.Max(peak,face.JawWeight);
                rows.Add(new {utc=DateTimeOffset.UtcNow,elapsed_ms=Time.GetTicksMsec()-start,
                    playback=speech?.Position,duration=speech?.Duration,voice_jaw=speech?.Jaw,jaw_weight=face.JawWeight,
                    native_jaw_angle_rad=figure.FindChildren("*","",true,false).OfType<Art.HumGenJaw>().Single().AppliedAngle,
                    dialogue_visible=_dialogue.Visible,canvas=GetViewport().CanvasCullMask,
                    camera=current?.Name.ToString(),camera_position=current?.GlobalPosition.ToString(),
                    framing=close is null ? "NORMAL_TALK_CAMERA":"REVIEW_CLOSEUP_SAME_LINE_UI_VISIBLE"});
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            if(peak<.05f)throw new InvalidDataException("Recorded voice produced no native speaking jaw");
            var ended=_voice.Speech(expected);
            if(ended is null || ended.Value.Position < ended.Value.Duration-.15)
                throw new InvalidDataException("Greeting ended before its full recorded duration");
            rows.Add(new {full_utterance_finished=true,utc=DateTimeOffset.UtcNow,peak_jaw=peak});
            File.WriteAllText(Path.Combine(directory,"evidence.json"),JsonSerializer.Serialize(new {
                status="FULL_NORMAL_GREETING_THEN_SAME_LINE_CLOSEUP_REQUIRES_REVIEW",npc=npcId,talk.LineId,frames=rows}));
            // Readback only AFTER speech, so the screenshot cannot consume its visible performance.
            await Rcd3Shot(directory,"after_greeting");
        }
        finally
        {
            _dialogue.Leave();_innFreeCamera=false;gameplay.MakeCurrent();close?.QueueFree();
        }
    }
}
