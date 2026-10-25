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
        try
        {
            if (npc == "tavar_orr")
            {
                var sim=_session.Simulation ?? throw new InvalidDataException("Voice gate has no world");
                foreach(var site in new[]{("switch.stone_north",547.2,678.0),("switch.stone_southwest",524.8,638.0),
                    ("switch.stone_southeast",583.8,631.0),("switch.foldscar_heart",553.0,652.3)})
                {
                    var goal=new Body((long)Math.Round(site.Item2*1000),0,(long)Math.Round(site.Item3*1000),0);
                    ulong walkingDeadline=Time.GetTicksMsec()+90000;
                    foreach(var corner in Playthrough.WayTo(sim,_controller.Authoritative,goal,0))
                        while(true)
                        {
                            var body=_controller.Authoritative;
                            var to=new Vector3((float)(corner.X-body.XMm/1000.0),0,(float)(corner.Z-body.ZMm/1000.0));
                            if(to.Length()<.25f)break;
                            if(Time.GetTicksMsec()>walkingDeadline)throw new TimeoutException("Cannot walk to "+site.Item1);
                            _camera.Yaw=Mathf.Atan2(-to.X,-to.Z);
                            _controller.SteerWorld(to.Normalized(),Gait.Run,_camera);
                            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                        }
                    _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);await Rcd3Wait(.3);
                    _controller.Interact(site.Item1);await Rcd3Wait(1);
                    if(!sim.Switches.Single(x=>x.Site.Key==site.Item1).Set)
                        throw new InvalidDataException("Normal Foldscar interaction refused: "+site.Item1);
                    CloseScreens();
                }
                await Rcd3Wait(2);
            }
            await RunTalkingThroatGate(Path.Combine(directory,npc),camera,"npc.ashen_hollow."+npc,true);
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
}
