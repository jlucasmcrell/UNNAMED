// UNNAMED Presentation - ravens perched about the settlements and the road, that take off when someone comes near
// Godot presentation only (D-11): decoration like the rest of ambient life - nothing hit, heard by the game, saved or read

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Ravens perched at the spots res://Art/ambient_life.json's <c>perched</c> names (walls, fences, stones), drawn with the asset library's rigged raven.
/// Each stands and pecks until the camera comes within <c>flee_m</c>, then takes off and flies away from it, climbing, and is gone; it is
/// back at its spot once the camera has been well away for <c>return_s</c>. By day only; out of sight (past 70 m) a raven is neither drawn
/// nor animated.
/// </summary>
public sealed partial class PerchedRavens : Node3D
{
    private const float DrawM = 70f;
    private const float FlightS = 7f;

    private sealed class Raven
    {
        public required Art.SkinnedModel Body;
        public Vector3 Spot;
        public float Yaw;
        public double FlownAt = -1;
        public Vector3 Away;
    }

    private readonly List<Raven> _ravens = new();
    private float _fleeM = 8f;
    private double _returnS = 120;
    private double _clock;

    /// <summary>The ravens of the <c>perched</c> entry under <paramref name="parent"/>; how many were drawn (0 without the model).</summary>
    public static int Build(Node3D parent, Art.ArtLibrary art, GroundField ground)
    {
        if (!Godot.FileAccess.FileExists(AmbientLife.ResourcePath))
            return 0;
        using var json = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(AmbientLife.ResourcePath));
        if (!json.RootElement.TryGetProperty("perched", out var perched))
            return 0;
        string model = perched.GetProperty("model").GetString()!;
        string clips = perched.GetProperty("clips").GetString()!;
        var states = perched.GetProperty("states").EnumerateObject().ToDictionary(s => s.Name, s => $"{clips}.{s.Value.GetString()}");
        var flock = new PerchedRavens { Name = "PerchedRavens" };
        if (perched.TryGetProperty("flee_m", out var flee))
            flock._fleeM = (float)flee.GetDouble();
        if (perched.TryGetProperty("return_s", out var back))
            flock._returnS = back.GetDouble();
        int i = 0;
        foreach (var spot in perched.GetProperty("spots").EnumerateArray())
        {
            if (Art.SkinnedModel.Create(art, model, states, "ambient.raven") is not { } body)
            {
                GD.Print($"UNNAMED ambient life: no perched ravens ({art.Why(model) ?? "the raven model is not in the library"})");
                flock.QueueFree();
                return 0;
            }
            // On what it perches (a wall, a fence, a stone): the third number is its height above the ground.
            float x = (float)spot[0].GetDouble(), z = (float)spot[1].GetDouble(), up = spot.GetArrayLength() > 2 ? (float)spot[2].GetDouble() : 0f;
            var raven = new Raven { Body = body, Spot = new Vector3(x, ground.Height(x, z) + up, z), Yaw = i++ * 2.39996f };
            body.Position = raven.Spot;
            body.Rotation = new Vector3(0, raven.Yaw, 0);
            body.Play("idle", 0f, 0.8f + 0.1f * (i % 4));
            flock.AddChild(body);
            flock._ravens.Add(raven);
        }
        parent.AddChild(flock);
        GD.Print($"UNNAMED ambient life: {flock._ravens.Count} perched ravens");
        return flock._ravens.Count;
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        var eye = GetViewport()?.GetCamera3D()?.GlobalPosition;
        bool day = SkyView.DaylightNow > 0.5f;
        foreach (var raven in _ravens)
        {
            var body = raven.Body;
            float near = eye is { } at ? new Vector2(at.X - raven.Spot.X, at.Z - raven.Spot.Z).Length() : 0f;
            if (raven.FlownAt < 0)
            {
                bool shown = day && near < DrawM;
                body.Visible = shown;
                body.ProcessMode = shown ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
                if (!shown || near > _fleeM || eye is not { } from)
                    continue;
                // Startled: up and away from whoever came, a little off the straight line.
                raven.FlownAt = _clock;
                var away = new Vector3(raven.Spot.X - from.X, 0, raven.Spot.Z - from.Z).Normalized();
                raven.Away = away.Rotated(Vector3.Up, (raven.Yaw % 1f - 0.5f) * 0.8f);
                body.Rotation = new Vector3(0, Mathf.Atan2(raven.Away.X, raven.Away.Z), 0);
                body.Play("takeoff", 0.05f, 1f, restart: true);
                continue;
            }
            double t = _clock - raven.FlownAt;
            if (t < FlightS)
            {
                // A heavy take-off, then a climbing, flapping glide.
                float s = (float)Math.Max(0, t - 0.35);
                body.Position = raven.Spot + raven.Away * (2.2f * s + 0.9f * s * s) + Vector3.Up * (1.1f * s + 0.35f * s * s);
                if (t > 0.35)
                    body.Play(t < 4 ? "flap" : "soar", 0.2f);
                continue;
            }
            body.Visible = false;
            body.ProcessMode = ProcessModeEnum.Disabled;
            // Back at its spot once nobody has been near for a while.
            if (t > FlightS + _returnS && near > 3 * _fleeM)
            {
                raven.FlownAt = -1;
                body.Position = raven.Spot;
                body.Rotation = new Vector3(0, raven.Yaw, 0);
                body.Play("idle", 0f);
            }
        }
    }
}
