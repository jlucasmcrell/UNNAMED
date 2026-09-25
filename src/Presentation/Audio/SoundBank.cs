// UNNAMED Presentation - the asset pipeline's Phase-1 sound set, played by semantic ID (the V3 selection)
// Godot presentation only: no gameplay state lives here (D-11)

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Audio;

/// <summary>
/// The sound set as its manifest describes it (<c>manifests/playable_prototype_audio_v3.json</c>, the owner's current selection; V1
/// and V2 stay as history): each stable ID's file, channels and loop flag, read where the asset workspace lies and never copied into
/// this repository. IDs numbered <c>.01</c>, <c>.02</c>... are one family; a family is played by picking a variation at random that is
/// never the one it played last (PHASE1_AUDIO_EVENT_CONTRACT.md). A mono sound plays at a place in the world; a stereo one (the beds,
/// the Strain layers, the forge) and the UI's play flat. Every sound at the level it was delivered at: the set is already levelled
/// relative to itself, so nothing here trims one. Without the workspace every call is silent.
/// </summary>
public partial class SoundBank : Node
{
    private sealed record Sound(string Id, string File, bool Stereo, bool Loop, bool Flat);

    private readonly Dictionary<string, Sound> _sounds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _families = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _last = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AudioStreamWav?> _streams = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _played = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _unknown = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _requested = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _resolved = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, string> _errors = new(StringComparer.Ordinal);
    private readonly Random _pick = new();

    public const string Manifest = "playable_prototype_audio_v3.json";

    /// <summary>The manifest's sound count (0 without the workspace).</summary>
    public int Count => _sounds.Count;

    /// <summary>Every family or ID asked for that the set does not have (reported, never guessed at).</summary>
    public IReadOnlyCollection<string> Unknown => _unknown;

    /// <summary>Every ID that has played.</summary>
    public IReadOnlyCollection<string> Played => _played;

    /// <summary>Every family or ID the mapping asked to play this run, and how often (the audio coverage report).</summary>
    public IReadOnlyDictionary<string, int> Requested => _requested;

    /// <summary>Every family or ID asked for that resolved to a sound whose file loaded.</summary>
    public IReadOnlyCollection<string> Resolved => _resolved;

    /// <summary>Every ID whose file would not load when it was asked for, and why.</summary>
    public IReadOnlyDictionary<string, string> Errors => _errors;

    /// <summary>Every ID in the set and its file (the static check).</summary>
    public IReadOnlyDictionary<string, string> Files => _sounds.ToDictionary(s => s.Key, s => s.Value.File, StringComparer.Ordinal);

    private void Asked(string family) => _requested[family] = _requested.GetValueOrDefault(family) + 1;

    public void Load(string? assetRoot)
    {
        if (assetRoot is null || !File.Exists(Path.Combine(assetRoot, "manifests", Manifest)))
            return;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(assetRoot, "manifests", Manifest)));
            HashSet<string>? open = null;
            foreach (var sound in json.RootElement.GetProperty("sounds").EnumerateArray())
            {
                // An entry the manifest got wrong is left out, and said; the rest of the set still plays (the Phase-1 technical audit, H-02).
                if (Text(sound, "audio_id") is not { } id || Text(sound, "delivered") is not { } delivered
                    || !sound.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Number
                    || !sound.TryGetProperty("loop", out var loop) || loop.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    _problems.Add(Text(sound, "audio_id") ?? sound.ToString());
                    continue;
                }
                bool stereo = channels.TryGetInt32(out int count) && count == 2;
                // Phase B's audition (--visual audio=proof): a proof file of the same ID, format and loudness plays in V3's place; V3 is untouched.
                string file = Path.Combine(assetRoot, delivered);
                string audio = VisualOptions.All.GetValueOrDefault("audio") ?? "v3";
                if ((audio == "proof" || audio == "proof_open" && (open ??= AudioAudition.OpenProofs(assetRoot)).Contains(id))
                    && Path.Combine(assetRoot, "audio_proof", id + ".wav") is var proof && File.Exists(proof))
                    file = proof;
                _sounds[id] = new Sound(id, file, stereo, loop.GetBoolean(), stereo || Text(sound, "group") == "ui");
                string family = id.LastIndexOf('.') is var dot and > 0 && id[(dot + 1)..].All(char.IsDigit) ? id[..dot] : id;
                if (!_families.TryGetValue(family, out var members))
                    _families[family] = members = new List<string>();
                members.Add(id);
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            GD.PushWarning($"UNNAMED audio: the sound manifest could not be read ({e.Message}); silent");
            _sounds.Clear();
            _families.Clear();
        }
    }

    /// <summary>Manifest entries left out because they are malformed, by ID where they have one.</summary>
    public IReadOnlyList<string> Problems => _problems;

    private readonly List<string> _problems = new();

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text ? text : null;

    /// <summary>Whether the set has a family (or a single ID) by this name.</summary>
    public bool Has(string family) => _families.ContainsKey(family) || _sounds.ContainsKey(family);

    /// <summary>
    /// One sound of a family, a variation other than the last, at a place in the world - or flat, when the sound is stereo or the UI's, or
    /// no place is given. <paramref name="delay"/> holds it back (a swing heard at its commit, not its windup). Returns whether it played.
    /// </summary>
    public bool Play(string family, Vector3? at = null, double delay = 0)
    {
        if (_sounds.Count > 0)
            Asked(family);
        if (Pick(family) is not { } sound || Stream(sound) is not { } stream)
            return false;
        _resolved.Add(family);
        Node player;
        if (sound.Flat || at is null)
        {
            player = new AudioStreamPlayer { Stream = stream };
        }
        else
        {
            // A mono sound is placed: heard fully within a few metres and gone by forty.
            player = new AudioStreamPlayer3D { Stream = stream, Position = at.Value, UnitSize = 4f, MaxDistance = 45f };
        }
        AddChild(player);
        void Start()
        {
            if (!IsInstanceValid(player))
                return;
            if (player is AudioStreamPlayer flat)
            {
                flat.Finished += flat.QueueFree;
                flat.Play();
            }
            else if (player is AudioStreamPlayer3D placed)
            {
                placed.Finished += placed.QueueFree;
                placed.Play();
            }
        }
        if (delay > 0.001)
            GetTree().CreateTimer(delay).Timeout += Start;
        else
            Start();
        _played.Add(sound.Id);
        return true;
    }

    /// <summary>The V3 source of an ID - its file (as the manifest delivers it), whether it plays flat, whether it loops - or null.</summary>
    public (string File, bool Flat, bool Loop)? Source(string id) => _sounds.TryGetValue(id, out var sound) ? (sound.File, sound.Flat, sound.Loop) : null;

    /// <summary>
    /// A file played as the game plays a sound (Phase B's audition): placed, or flat; a loop stopped after <paramref name="stopAfter"/>
    /// seconds. Returns how long it sounds.
    /// </summary>
    public double PlayFile(string file, bool flat, bool loop, Vector3? at, double? stopAfter)
    {
        var options = new Godot.Collections.Dictionary();
        if (loop)
        {
            options["edit/loop_mode"] = 2;
            options["edit/loop_begin"] = 0;
            options["edit/loop_end"] = -1;
        }
        if (AudioStreamWav.LoadFromFile(file, options) is not { } stream)
            return 0;
        Node player = flat || at is null
            ? new AudioStreamPlayer { Stream = stream }
            : new AudioStreamPlayer3D { Stream = stream, Position = at.Value, UnitSize = 4f, MaxDistance = 45f };
        AddChild(player);
        double length = stopAfter ?? stream.GetLength();
        if (player is AudioStreamPlayer f)
            f.Play();
        else
            ((AudioStreamPlayer3D)player).Play();
        GetTree().CreateTimer(length).Timeout += () =>
        {
            if (IsInstanceValid(player))
                player.QueueFree();
        };
        return length;
    }

    /// <summary>A looping sound on a player of its own, silent until its volume is set (a bed, a Strain layer, the forge).</summary>
    public AudioStreamPlayer? Loop(string id)
    {
        if (_sounds.Count > 0)
            Asked(id);
        if (!_sounds.TryGetValue(id, out var sound))
        {
            _unknown.Add(id);
            return null;
        }
        if (Stream(sound) is not { } stream)
            return null;
        _resolved.Add(id);
        var player = new AudioStreamPlayer { Stream = stream, VolumeDb = -80f };
        AddChild(player);
        player.Play();
        _played.Add(id);
        return player;
    }

    /// <summary>At shutdown: stop what still plays (the loops) and let go of the loaded streams, so none outlives the engine.</summary>
    public override void _ExitTree()
    {
        foreach (var child in GetChildren())
        {
            if (child is AudioStreamPlayer flat)
                flat.Stop();
            else if (child is AudioStreamPlayer3D placed)
                placed.Stop();
        }
        foreach (var stream in _streams.Values)
            stream?.Dispose();
        _streams.Clear();
    }

    private Sound? Pick(string family)
    {
        if (_sounds.TryGetValue(family, out var single))
            return single;
        if (!_families.TryGetValue(family, out var members) || members.Count == 0)
        {
            if (_sounds.Count > 0)
                _unknown.Add(family);
            return null;
        }
        string last = _last.GetValueOrDefault(family) ?? string.Empty;
        var choices = members.Count > 1 ? members.Where(m => m != last).ToList() : members;
        string id = choices[_pick.Next(choices.Count)];
        _last[family] = id;
        return _sounds[id];
    }

    private AudioStreamWav? Stream(Sound sound)
    {
        if (_streams.TryGetValue(sound.Id, out var cached))
            return cached;
        AudioStreamWav? stream = null;
        if (File.Exists(sound.File))
        {
            // A loop wraps over its whole length: the pipeline cross-faded each bed at its own seam.
            var options = new Godot.Collections.Dictionary();
            if (sound.Loop)
            {
                options["edit/loop_mode"] = 2;
                options["edit/loop_begin"] = 0;
                options["edit/loop_end"] = -1;
            }
            stream = AudioStreamWav.LoadFromFile(sound.File, options);
        }
        if (stream is null)
        {
            GD.PushWarning($"UNNAMED audio: {sound.Id} would not load from {sound.File}");
            _errors[sound.Id] = File.Exists(sound.File) ? "the file would not load as WAV" : $"no file at {sound.File}";
        }
        _streams[sound.Id] = stream;
        return stream;
    }
}
