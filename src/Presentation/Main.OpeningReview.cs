using Godot;
using System.Text.Json;

namespace UNNAMED.Presentation;

public partial class Main
{
    private readonly List<(int Bus, int Index, AudioEffectRecord Effect, string Name)> _openingRecordings = new();
    private ulong _openingAudioStart;

    // Capture only: records the actual mixed game buses, never a replacement soundtrack.
    private async void StartOpeningReview(string directory)
    {
        _mhReviewBusy = true;
        DisplayServer.WindowSetTitle("Otherreach - RC d9 Opening Review");
        File.WriteAllText(Path.Combine(directory, "opening_ready.json"), "{}");
        ulong deadline = Time.GetTicksMsec() + 60000;
        while (!File.Exists(Path.Combine(directory, "opening_recording.started")))
        {
            // Godot may apply its project title after _Ready; keep the window discoverable until WGC attaches.
            DisplayServer.WindowSetTitle("Otherreach - RC d9 Opening Review");
            if (Time.GetTicksMsec() > deadline)
            {
                File.WriteAllText(Path.Combine(directory, "failed.txt"), "Opening recorder did not start");
                CaptureQuit.At(this, 3);
                return;
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        foreach (string name in new[] { "Master", Audio.AudioMix.Music })
        {
            int bus = AudioServer.GetBusIndex(name), index = AudioServer.GetBusEffectCount(bus);
            var effect = new AudioEffectRecord();
            AudioServer.AddBusEffect(bus, effect);
            effect.SetRecordingActive(true);
            _openingRecordings.Add((bus, index, effect, name));
        }
        _openingAudioStart = Time.GetTicksUsec();
        File.WriteAllText(Path.Combine(directory, "opening_audio_started.json"), JsonSerializer.Serialize(new {
            ticks_usec = _openingAudioStart, cue = _music.Cue,
            timing = "Audio starts after WGC first frame; original video timestamps retained. No microphone or external soundtrack." }));
        _mhReviewNext = Time.GetTicksMsec() + 12000;
        _mhReviewBusy = false;
    }

    private void FinishOpeningReview()
    {
        foreach (var item in _openingRecordings)
        {
            item.Effect.SetRecordingActive(false);
            using var audio = item.Effect.GetRecording();
            if (audio is not null && audio.Data.Length > 0)
                audio.SaveToWav(Path.Combine(_mhReviewDirectory!, "opening_" + item.Name.ToLowerInvariant() + ".wav"));
            AudioServer.RemoveBusEffect(item.Bus, item.Index);
            item.Effect.Dispose();
        }
        if (_openingRecordings.Count > 0)
            File.WriteAllText(Path.Combine(_mhReviewDirectory!, "opening_audio_complete.json"), JsonSerializer.Serialize(new {
                start_usec = _openingAudioStart, end_usec = Time.GetTicksUsec(), cue = _music.Cue }));
        _openingRecordings.Clear();
    }
}
