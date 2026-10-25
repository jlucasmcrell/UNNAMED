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

    public void Load(string? assetRoot) => _assetRoot = assetRoot;

    /// <summary>Whether anyone is speaking now.</summary>
    public bool Speaking => _line.Playing || _barks.Any(b => b.Playing);

    public override void _Ready()
    {
        AudioMix.EnsureBuses();
        AddChild(_line);
        _duck = MusicDuck();
    }

    /// <summary>Say a conversation's line: the line before it stops. Whether it was voiced.</summary>
    public bool Say(string lineId)
    {
        _line.Stop();
        if (Stream(lineId) is not { } stream)
            return false;
        _line.Stream = stream;
        _line.Play();
        return true;
    }

    /// <summary>Read a line's recording ahead of saying it, so its first showing decodes nothing (the opening's lines).</summary>
    public void Preload(string lineId) => Stream(lineId);

    /// <summary>Stop the conversation's line (the conversation ended).</summary>
    public void Hush() => _line.Stop();

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
