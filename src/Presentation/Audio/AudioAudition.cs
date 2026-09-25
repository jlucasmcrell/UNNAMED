// UNNAMED Presentation - the external-audio proof batch heard in the game against V3, blind (Phase B, the audio addition's in-game audition)
// Godot presentation only, a harness: nothing here is game state (D-11)

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Audio;

/// <summary>
/// <c>--audio-audition dir</c>: in the running world, each proof sound (<c>audio_proof/proof_batch.json</c>) and the V3 sound of the same
/// ID are played one after the other through the game's own players - placed a few metres ahead of the listener as the game places a mono
/// sound, flat when V3 plays it flat (stereo, the UI), a bed for a few seconds - in an order a seeded coin decides, under an on-screen
/// "pair N: A" / "B" caption and nothing else. Which of A and B was the proof goes only to <c>audition_key.json</c>, so a reviewer of the
/// recording (<c>--write-movie</c>) judges blind. Quits when the last pair has played.
/// </summary>
public partial class AudioAudition : Node
{
    private const double Gap = 0.9, Between = 2.4, BedSeconds = 5.0, Lead = 3.0;

    private sealed record Pair(int N, string Id, string First, string Second, bool ProofFirst, bool Flat, bool Loop);

    private readonly SoundBank _bank;
    private readonly string _directory;
    private readonly List<Pair> _pairs = new();
    private readonly List<Dictionary<string, object>> _key = new();
    private readonly Label _caption = new() { Position = new Vector2(60, 50) };
    private double _clock, _next = Lead;
    private int _index = -1, _half;

    /// <summary>
    /// The proofs every source of which sets no AI/ML restriction (its record says "none" or "none stated"): the only ones a recording may
    /// carry to an AI reviewer. Sonniss's GDC bundle licence prohibits AI use outright; an unclear licence is treated the same way.
    /// </summary>
    public static HashSet<string> OpenProofs(string assetRoot)
    {
        var open = new HashSet<string>(StringComparer.Ordinal);
        string batch = Path.Combine(assetRoot, "audio_proof", "proof_batch.json");
        if (!File.Exists(batch))
            return open;
        using var json = JsonDocument.Parse(File.ReadAllText(batch));
        foreach (var proof in json.RootElement.GetProperty("proofs").EnumerateArray())
        {
            if (proof.GetProperty("sources").EnumerateArray().All(s => s.TryGetProperty("ai_ml_restriction", out var r) && r.GetString() is "none" or "none stated"))
                open.Add(proof.GetProperty("audio_id").GetString()!);
        }
        return open;
    }

    public AudioAudition(SoundBank bank, string directory, string assetRoot, bool openOnly)
    {
        var open = openOnly ? OpenProofs(assetRoot) : null;
        _bank = bank;
        _directory = directory;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(assetRoot, "audio_proof", "proof_batch.json")));
        var coin = new Random(20260925);
        int n = 0;
        foreach (var proof in json.RootElement.GetProperty("proofs").EnumerateArray())
        {
            string id = proof.GetProperty("audio_id").GetString()!;
            if (open is not null && !open.Contains(id))
                continue;
            string proofFile = Path.Combine(assetRoot, "audio_proof", id + ".wav");
            if (bank.Source(id) is not { } v3 || !File.Exists(proofFile))
            {
                GD.PushWarning($"UNNAMED audition: {id} has no V3 sound or no proof file; left out");
                continue;
            }
            bool proofFirst = coin.Next(2) == 0;
            _pairs.Add(new Pair(++n, id, proofFirst ? proofFile : v3.File, proofFirst ? v3.File : proofFile, proofFirst, v3.Flat, v3.Loop));
        }
    }

    public override void _Ready()
    {
        Directory.CreateDirectory(_directory);
        var layer = new CanvasLayer();
        _caption.AddThemeFontSizeOverride("font_size", 44);
        _caption.AddThemeConstantOverride("outline_size", 10);
        _caption.AddThemeColorOverride("font_outline_color", Colors.Black);
        layer.AddChild(_caption);
        AddChild(layer);
        GD.Print($"UNNAMED audition: {_pairs.Count} pairs");
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_clock < _next)
            return;
        if (_index >= 0 && _half == 0)
        {
            // The second of the pair, after a short gap.
            _half = 1;
            _next = _clock + Play(_pairs[_index], second: true) + Between;
            return;
        }
        if (++_index >= _pairs.Count)
        {
            File.WriteAllText(Path.Combine(_directory, "audition_key.json"), JsonSerializer.Serialize(_key, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"UNNAMED audition: done, key written to {_directory}");
            GetTree().Quit(0);
            return;
        }
        _half = 0;
        _next = _clock + Play(_pairs[_index], second: false) + Gap;
    }

    /// <summary>Play one half of a pair; returns how long it sounds.</summary>
    private double Play(Pair pair, bool second)
    {
        string label = second ? "B" : "A";
        _caption.Text = $"pair {pair.N}: {label}";
        // The listener is the current camera: a placed sound stands four metres ahead of it, a little to the right, at about waist height.
        var listener = GetViewport().GetCamera3D();
        Vector3? at = pair.Flat || listener is null ? null
            : listener.GlobalPosition + listener.GlobalTransform.Basis.Z * -4f + listener.GlobalTransform.Basis.X * 1.2f + Vector3.Down * 1.2f;
        double length = _bank.PlayFile(second ? pair.Second : pair.First, pair.Flat, pair.Loop, at, pair.Loop ? BedSeconds : null);
        _key.Add(new Dictionary<string, object>
        {
            ["pair"] = pair.N, ["half"] = label, ["audio_id"] = pair.Id,
            ["which"] = (second ? !pair.ProofFirst : pair.ProofFirst) ? "proof" : "v3",
            ["starts_s"] = Math.Round(_clock, 2), ["seconds"] = Math.Round(length, 2),
        });
        return length;
    }
}
