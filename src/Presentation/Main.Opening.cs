// UNNAMED Presentation - Main's opening at launch: the cinematic framed on the world's own landmarks, its score, and the hand-off to the title
// Godot presentation only (D-11): it reads the built world and the player's preferences; nothing here is game state

using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Settings;

namespace UNNAMED.Presentation;

public partial class Main
{
    /// <summary>The stone the opening wakes, and the landmarks it flies past (the region's structures by their layout ids).</summary>
    private const string OpeningStone = "waystone.ashen";
    private const string OpeningHeart = "rock_foldscar_heart", OpeningOldLate = "old_late", OpeningNeedle = "fs_needle";

    /// <summary>The start screen's establishing shot (Phase B), drifting from its first frame.</summary>
    private StartCamera StartView(string region) => new()
    {
        Name = "StartCamera", Ground = _ground.Height,
        Offset = region == "region.starting_frontier" ? new Vector2(400, 600) : Vector2.Zero,
    };

    /// <summary>
    /// Where the opening flies, from the models the world placed: each landmark's feet and height from what is drawn of it. Null when the
    /// run has no such world (another region, a greybox build), and the start screen opens as it always has.
    /// </summary>
    private OpeningCinematic.Places? OpeningPlaces(string region)
    {
        if (region != "region.starting_frontier" || VisualOptions.Tier == "phase_a" || DisplayServer.GetName() == "headless"
            || _session.Setup.Layout.FindWaystone(OpeningStone) is not { } stone
            || _hollow.PlacedModel(OpeningHeart) is not { } heart || _hollow.PlacedModel(OpeningOldLate) is not { } oldLate
            || _hollow.PlacedModel(OpeningNeedle) is not { } needle || _hollow.PlacedModel(stone.Body.Id) is not { } waystone)
            return null;
        (Vector3 Feet, float Height) Drawn(Node3D model)
        {
            var box = model.FindChildren("*", nameof(VisualInstance3D), true, false).OfType<VisualInstance3D>()
                .Select(v => v.GlobalTransform * v.GetAabb()).Aggregate((a, b) => a.Merge(b));
            var middle = box.GetCenter();
            float floor = _ground.Height(middle.X, middle.Z);
            return (new Vector3(middle.X, floor, middle.Z), box.End.Y - floor);
        }
        var late = Drawn(oldLate);
        var spire = Drawn(needle);
        var stoneDrawn = Drawn(waystone);
        return new OpeningCinematic.Places(_ground.Height, Drawn(heart).Feet, late.Feet, late.Height, spire.Feet, spire.Height, stoneDrawn.Feet, stoneDrawn.Height);
    }

    /// <summary>
    /// The opening, before the start screen: the world's view goes to the start screen's own camera on the hit, and the title comes in
    /// under the name. The first viewing plays through; it counts as seen once the name has come in.
    /// </summary>
    private OpeningCinematic? _opening;
    private int _openingTitleLayer;

    private void StopOpening()
    {
        if (_opening is null) return;
        _opening.Dismiss();
        _opening = null;
        _loading?.End();
        _title.Layer = _openingTitleLayer;
        _voice.Hush();
        _music.Opening(false);
    }

    private void BeginOpening(OpeningCinematic.Places places, string region)
    {
        string? capture = System.Environment.GetEnvironmentVariable("OTHERREACH_OPENING_VIDEO");
        string filmPath=Path.Combine(_assets.Root??Home(),"films","opening_v2.ogv");
        bool filmed=File.Exists(filmPath);
        int titleLayer = _openingTitleLayer = _title.Layer;
        var opening = new OpeningCinematic(places)
        {
            Name = "Opening", FilmFile=filmed?filmPath:null, Skippable = filmed||SettingsStore.Current.Profile.OpeningSeen, Holding = capture is not null,
            // The title built and drawn once under the loading screen (a layer beneath its canvas), and the narration read, so neither
            // costs a frame when it comes.
            Warming = () =>
            {
                _title.Layer = -1;
                _title.Open();
                foreach (var line in filmed?Ui.OpeningFilmWords.Lines:Ui.OpeningWords.Lines)
                    _voice.Preload(line.Voice);
            },
            Started = () =>
            {
                _title.Close();
                _title.Layer = titleLayer;
                _music.Opening(!filmed);
                if(filmed)_music.Title(false);
            },
            Say = id => _voice.Say(id),
            Pulse = () => _successEffects.Answer(OpeningStone),
            Named = () =>
            {
                _startCamera = StartView(region);
                AddChild(_startCamera);
                _music.Opening(false);
                if(!filmed)_music.Title(true);
                // Seen once the name is: written under the hit's flash, not as the title's entries come in.
                if (!SettingsStore.Current.Profile.OpeningSeen)
                    SettingsStore.Set(SettingsStore.Current with { Profile = SettingsStore.Current.Profile with { OpeningSeen = true } });
            },
        };
        _opening = opening;
        opening.Done = skipped =>
        {
            _opening = null;
            _voice.Hush();
            _music.Title(true);
            _title.Layer = titleLayer;
            GD.Print($"UNNAMED opening: {(skipped ? "skipped" : "played through")}");
            OpenFrontEnd(reveal: true);
        };
        opening.Warmed = () =>
        {
            _loading?.End();
            if (capture is not null)
                RecordOpening(opening, capture);
        };
        AddChild(opening);
    }

    // OTHERREACH_OPENING_VIDEO=<dir>: the real-time gate. The opening waits on its first frame for the window recorder (record_ready.json,
    // then recording.started), records the game's own mix from the Master bus and every frame's time, and quits ten seconds into the title.
    private async void RecordOpening(OpeningCinematic opening, string directory)
    {
        string label = Path.GetFileName(directory);
        DisplayServer.WindowSetTitle("Otherreach - " + label);
        File.WriteAllText(Path.Combine(directory, "record_ready.json"), "{}");
        ulong deadline = Time.GetTicksMsec() + 60000;
        while (!File.Exists(Path.Combine(directory, "recording.started")))
        {
            DisplayServer.WindowSetTitle("Otherreach - " + label);
            if (Time.GetTicksMsec() > deadline)
            {
                File.WriteAllText(Path.Combine(directory, "failed.txt"), "The window recorder did not start");
                CaptureQuit.At(this, 3);
                return;
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        int bus = AudioServer.GetBusIndex("Master"), slot = AudioServer.GetBusEffectCount(bus);
        var record = new AudioEffectRecord();
        AudioServer.AddBusEffect(bus, record);
        record.SetRecordingActive(true);
        var audioStart = DateTimeOffset.UtcNow;
        bool seenBefore=SettingsStore.Current.Profile.OpeningSeen,skipSent=false;
        double.TryParse(System.Environment.GetEnvironmentVariable("OTHERREACH_OPENING_SKIP_SECONDS"),out double skipAfter);
        ulong captureStart=Time.GetTicksMsec();
        opening.Holding = false;
        var frames = new List<double>();
        bool done = false;
        opening.TreeExiting += () => done = true;
        while (!done)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            frames.Add(GetProcessDeltaTime() * 1000);
            if(skipAfter>0&&!skipSent&&Time.GetTicksMsec()-captureStart>=skipAfter*1000)
            {
                skipSent=true;
                // Opt-in native gate: Godot's real input dispatch, never desktop/keyboard control.
                Input.ParseInputEvent(new InputEventKey { Keycode=Key.Space,Pressed=true });
                Input.ParseInputEvent(new InputEventKey { Keycode=Key.Space,Pressed=false });
                File.WriteAllText(Path.Combine(directory,"skip_key.json"),JsonSerializer.Serialize(new {
                    seenBefore,utc=DateTimeOffset.UtcNow,seconds=(Time.GetTicksMsec()-captureStart)/1000.0,
                    route="Godot Input.ParseInputEvent Space -> OpeningCinematic._Input; no OS input"}));
            }
        }
        var title = DateTimeOffset.UtcNow;
        for (ulong until = Time.GetTicksMsec() + 10000; Time.GetTicksMsec() < until;)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            frames.Add(GetProcessDeltaTime() * 1000);
        }
        record.SetRecordingActive(false);
        using (var audio = record.GetRecording())
            audio?.SaveToWav(Path.Combine(directory, "game_audio.wav"));
        AudioServer.RemoveBusEffect(bus, slot);
        var sorted = frames.OrderBy(f => f).ToArray();
        File.WriteAllText(Path.Combine(directory, "complete.json"), JsonSerializer.Serialize(new
        {
            status = "REAL_GAME_OPENING_VIDEO_REQUIRES_CLAUDE_REVIEW", tier = VisualOptions.Tier, audio_start_utc = audioStart, title_utc = title,
            hit_seconds = opening.HitSeconds, end_seconds = opening.DurationSeconds, frames = frames.Count,
            frame_ms_p50 = sorted[sorted.Length / 2], frame_ms_p99 = sorted[(int)(sorted.Length * 0.99)], frame_ms_max = sorted[^1],
            frames_over_50ms = frames.Count(f => f > 50), cue = _music.Cue,
            route = "The normal launch's opening and start screen, the player's prefs in a run-local user folder; no state set",
        }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(directory, "frames_ms.json"), JsonSerializer.Serialize(frames));
        File.WriteAllText(Path.Combine(directory, "recording.finished"), "done");
        for (ulong until = Time.GetTicksMsec() + 15000; !File.Exists(Path.Combine(directory, "recording.stopped")) && Time.GetTicksMsec() < until;)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        CaptureQuit.At(this, 0);
    }
}
