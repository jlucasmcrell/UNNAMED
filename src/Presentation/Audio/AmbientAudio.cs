// UNNAMED Presentation - environmental audio hooks for the Phase-B world (res://Art/audio_hooks.json)
// Godot presentation only (D-11): sounds of the weather, the water and the wildlife; nothing here is simulated or saved

using System.Text.Json;
using Godot;
using UNNAMED.Presentation.Greybox;

namespace UNNAMED.Presentation.Audio;

/// <summary>
/// The Phase-B world's sounds, hooked where they belong: wind by the world wind's strength, rain and thunder in a storm, the river near the
/// ravine, insects by day and night, gusts in the Charwood's leaves, the smithy's fire, the fold's hum. A hook plays only an ID the sound set
/// has - V3 is kept as it is - and any hook still waiting for its audio is listed (<see cref="Awaiting"/>) and stays silent, never
/// requested from the bank (so the audio coverage gate is unchanged).
/// </summary>
public partial class AmbientAudio : Node
{
    public const string ResourcePath = "res://Art/audio_hooks.json";

    private sealed class Hook
    {
        public string Name = "";
        public string Id = "";
        public bool Loop;
        public float Gain = 1;
        public bool ByWind;
        public HashSet<string>? Weathers, Grounds;
        public bool DayOnly, NightOnly;
        public float NearRavine;
        public (float Lo, float Hi) Every;
        public double Next;
        public AudioStreamPlayer? Player;
    }

    private readonly List<Hook> _hooks = new();
    private readonly SoundBank _bank;
    private readonly GroundField _ground;
    private readonly RandomNumberGenerator _random = new() { Seed = 0x50A1D };
    private double _clock;

    /// <summary>The hooks whose audio is not in the sound set yet (hook: audio ID).</summary>
    public IReadOnlyList<string> Awaiting { get; }

    public AmbientAudio(SoundBank bank, GroundField ground)
    {
        _bank = bank;
        _ground = ground;
        var awaiting = new List<string>();
        if (Godot.FileAccess.FileExists(ResourcePath))
        {
            using var json = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ResourcePath));
            foreach (var h in json.RootElement.GetProperty("hooks").EnumerateArray())
            {
                var hook = new Hook
                {
                    Name = h.GetProperty("hook").GetString()!,
                    Loop = h.TryGetProperty("loop", out _),
                    Gain = h.TryGetProperty("gain", out var g) ? (float)g.GetDouble() : 1f,
                    ByWind = h.TryGetProperty("gain_by", out var by) && by.GetString() == "wind",
                    Weathers = Set(h, "weather"), Grounds = Set(h, "grounds"),
                    DayOnly = h.TryGetProperty("day_only", out var d) && d.ValueKind == JsonValueKind.True,
                    NightOnly = h.TryGetProperty("night_only", out var n) && n.ValueKind == JsonValueKind.True,
                    NearRavine = h.TryGetProperty("near_ravine_m", out var r) ? (float)r.GetDouble() : 0f,
                    Every = h.TryGetProperty("every_s", out var e) ? ((float)e[0].GetDouble(), (float)e[1].GetDouble()) : (10f, 30f),
                };
                hook.Id = (hook.Loop ? h.GetProperty("loop") : h.GetProperty("one_shot")).GetString()!;
                if (!_bank.Has(hook.Id))
                {
                    awaiting.Add($"{hook.Name}: {hook.Id}");
                    continue;
                }
                hook.Next = _random.RandfRange(hook.Every.Lo, hook.Every.Hi);
                _hooks.Add(hook);
            }
        }
        Awaiting = awaiting;
        GD.Print($"UNNAMED audio hooks: {_hooks.Count} playing, {awaiting.Count} awaiting audio ({string.Join(", ", awaiting)})");
    }

    private static HashSet<string>? Set(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal) : null;

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_hooks.Count == 0 || GetViewport().GetCamera3D() is not { } camera)
            return;
        var at = camera.GlobalPosition;
        string weather = VisualOptions.Weather ?? "fair";
        bool day = SkyView.DaylightNow > 0.5f;
        foreach (var hook in _hooks)
        {
            bool on = (hook.Weathers is null || hook.Weathers.Contains(weather)) && !(hook.DayOnly && !day) && !(hook.NightOnly && day)
                      && (hook.Grounds is null || OnGround(at, hook.Grounds)) && (hook.NearRavine <= 0 || RavineDistance(at) <= hook.NearRavine);
            float gain = hook.Gain * (hook.ByWind ? Math.Clamp(WindField.Strength * 1.6f, 0f, 1f) : 1f);
            if (hook.Loop)
            {
                hook.Player ??= _bank.Loop(hook.Id);
                if (hook.Player is null)
                    continue;
                float current = Mathf.DbToLinear(hook.Player.VolumeDb);
                hook.Player.VolumeDb = Mathf.LinearToDb(Math.Max(0.0001f, Mathf.MoveToward(current, on ? gain : 0f, (float)delta * 0.4f)));
            }
            else if (on && _clock >= hook.Next)
            {
                _bank.Play(hook.Id);
                hook.Next = _clock + _random.RandfRange(hook.Every.Lo, hook.Every.Hi);
            }
        }
    }

    private bool OnGround(Vector3 at, HashSet<string> grounds)
    {
        if (!_ground.Region.HasPoint(new Vector2(at.X, at.Z)))
            return false;
        var w = _ground.CellWeights(at.X, at.Z);
        float share = 0;
        for (int i = 0; i < 4; i++)
        {
            if (_ground.Grounds[i] is { } g && grounds.Contains(g))
                share += w[i];
        }
        return share >= 0.5f;
    }

    /// <summary>How far the listener is from the ravine's edge (the west, north and east edges of the region).</summary>
    private float RavineDistance(Vector3 at)
    {
        var r = _ground.Region;
        return Math.Max(0, Math.Min(Math.Min(at.X - r.Position.X, r.End.X - at.X), r.End.Y - at.Z));
    }
}
