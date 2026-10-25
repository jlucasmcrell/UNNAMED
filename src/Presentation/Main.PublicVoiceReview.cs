using Godot;
using System.Text.Json;

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
