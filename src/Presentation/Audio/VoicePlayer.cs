// UNNAMED Presentation - voice-over: a conversation's lines and barks said aloud when their recording exists (WP5, E-5, E-10)
// Godot presentation only (D-11): nothing here is game state or part of a save

using Godot;

namespace UNNAMED.Presentation.Audio;

/// <summary>
/// Plays a line's voice-over by its line ID (<c>vo/&lt;line-id&gt;.ogg</c> under the asset workspace, read where it lies and never copied
/// into this repository) on the <see cref="AudioMix.Voice"/> bus, and ducks the music while anyone speaks. A conversation's line plays
/// flat, one at a time; a bark plays where its speaker stands. A line without a recording is silent and says so by returning nothing:
/// text comes first, voice follows (E-13).
/// </summary>
public partial class VoicePlayer : Node
{
    private readonly Dictionary<string, AudioStream?> _streams = new(StringComparer.Ordinal);
    private readonly AudioStreamPlayer _line = new() { Bus = AudioMix.Voice };
    private readonly List<AudioStreamPlayer3D> _barks = new();
    private string? _assetRoot;
    private AudioEffectAmplify? _duck;
    private string? _lineId;
    private int _request;
    private bool _pending;
    private readonly Dictionary<AudioStream, (double Step, float[] Jaw)> _envelopes = new();

    private float Jaw(AudioStream stream, double position, float peakDb)
    {
        if (!_envelopes.TryGetValue(stream, out var envelope)) return VoiceRules.SpeechJaw(peakDb);
        int at = (int)Math.Max(0, position / envelope.Step);
        return at < envelope.Jaw.Length ? envelope.Jaw[at] : 0;
    }

    public void Load(string? assetRoot) => _assetRoot = assetRoot;

    /// <summary>Whether anyone is speaking now.</summary>
    public bool Speaking => _pending || _line.Playing || _barks.Any(b => b.Playing);

    public (double Position, double Duration, float Jaw)? Speech(string lineId)
    {
        if (_lineId != lineId || _line.Stream is not { } stream) return null;
        int bus = AudioServer.GetBusIndex(AudioMix.Voice);
        float peak = Math.Max(AudioServer.GetBusPeakVolumeLeftDb(bus, 0), AudioServer.GetBusPeakVolumeRightDb(bus, 0));
        return (_line.Playing ? _line.GetPlaybackPosition() : _pending ? 0 : stream.GetLength(),
            stream.GetLength(), _line.Playing ? Jaw(stream, _line.GetPlaybackPosition(), peak) : 0);
    }

    /// <summary>A world speaker uses its own playback clock, including concurrent barks.</summary>
    public (double Position, double Duration, float Jaw)? Speech(AudioStreamPlayer3D? speaker, AudioStream? expected)
    {
        if (speaker is null || expected is null || !GodotObject.IsInstanceValid(speaker) || speaker.Stream != expected) return null;
        int bus = AudioServer.GetBusIndex(AudioMix.Voice);
        float peak = Math.Max(AudioServer.GetBusPeakVolumeLeftDb(bus, 0), AudioServer.GetBusPeakVolumeRightDb(bus, 0));
        return (speaker.Playing ? speaker.GetPlaybackPosition() : expected.GetLength(), expected.GetLength(),
            speaker.Playing ? Jaw(expected, speaker.GetPlaybackPosition(), peak) : 0);
    }

    public override void _Ready()
    {
        AudioMix.EnsureBuses();
        AddChild(_line);
        _duck = MusicDuck();
    }

    /// <summary>Say a conversation's line: the line before it stops. Whether it was voiced.</summary>
    public bool Say(string lineId)
    {
        Hush();
        if (Stream(lineId) is not { } stream)
            return false;
        _lineId = lineId;
        _line.Stream = stream;
        _pending = true;
        PlayAfterPicture(_request);
        return true;
    }

    private async void PlayAfterPicture(int request)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (request != _request || !IsInsideTree()) return;
        _pending = false;
        _line.Play();
    }

    /// <summary>Read a line's recording ahead of saying it, so its first showing decodes nothing (the opening's lines).</summary>
    public void Preload(string lineId) => Stream(lineId);

    /// <summary>Stop the conversation's line (the conversation ended).</summary>
    public void Hush()
    {
        _request++;
        _pending = false;
        _lineId = null;
        _line.Stop();
    }

    /// <summary>
    /// Say a bark where its speaker's head is: the voice saying it, which the caller keeps at the speaker's head while it plays, or null
    /// when it has no recording.
    /// </summary>
    public AudioStreamPlayer3D? Bark(string lineId, Vector3 at)
    {
        if (Stream(lineId) is not { } stream)
            return null;
        var player = _barks.FirstOrDefault(b => !b.Playing);
        if (player is null)
        {
            player = new AudioStreamPlayer3D { Bus = AudioMix.Voice, UnitSize = 4f, MaxDistance = VoiceRules.BarkGoneM * 2 };
            AddChild(player);
            _barks.Add(player);
        }
        player.Stream = stream;
        player.GlobalPosition = at;
        player.Play();
        return player;
    }

    public override void _Process(double delta)
    {
        if (_duck is not null)
            _duck.VolumeDb = VoiceRules.Duck(_duck.VolumeDb, Speaking, delta);
    }

    private AudioStream? Stream(string lineId)
    {
        if (_streams.TryGetValue(lineId, out var known))
            return known;
        AudioStream? stream = null;
        if (VoiceRules.VoicePath(_assetRoot, lineId) is { } path && File.Exists(path))
        {
            stream = AudioStreamOggVorbis.LoadFromFile(path);
            if (stream is not null && File.Exists(path + ".envelope.json"))
            {
                using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path + ".envelope.json"));
                double step = json.RootElement.GetProperty("step_seconds").GetDouble();
                float[] jaw = json.RootElement.GetProperty("jaw").EnumerateArray().Select(value => value.GetSingle()).ToArray();
                if (!double.IsFinite(step) || step <= 0 || jaw.Any(value => !float.IsFinite(value) || value < 0 || value > 1))
                    throw new InvalidDataException("Invalid voice envelope: " + path);
                _envelopes[stream] = (step, jaw);
            }
            if (stream is null)
                GD.PushWarning($"UNNAMED voice: {path} would not load");
        }
        _streams[lineId] = stream;
        return stream;
    }

    /// <summary>The music bus's duck: an amplifier kept at 0 dB until a voice speaks, added once per engine run.</summary>
    private static AudioEffectAmplify? MusicDuck()
    {
        int bus = AudioServer.GetBusIndex(AudioMix.Music);
        if (bus < 0)
            return null;
        for (int i = 0; i < AudioServer.GetBusEffectCount(bus); i++)
        {
            if (AudioServer.GetBusEffect(bus, i) is AudioEffectAmplify { ResourceName: DuckName } existing)
                return existing;
        }
        var duck = new AudioEffectAmplify { ResourceName = DuckName, VolumeDb = 0 };
        AudioServer.AddBusEffect(bus, duck);
        return duck;
    }

    private const string DuckName = "VoiceDuck";
}
