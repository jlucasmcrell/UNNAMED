// UNNAMED Presentation - the air itself: ash, pollen, rain and mist by the rules in res://Art/atmosphere.json (Phase B, B11)
// Godot presentation only (D-11): particles to look at; nothing here is simulated, saved or read by the game

using System.Text.Json;
using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Particle recipes placed by rule: an emitter following the camera runs while the ground under it is one of its grounds (ash in the
/// Charwood, pollen over the waystation's meadow), in its weathers, by day if it says so; an emitter at the ravine rivers stands over
/// each strip of water. Emitters that follow leave their particles in the world as the camera moves on.
/// </summary>
public partial class AtmosphereView : Node3D
{
    public const string ResourcePath = "res://Art/atmosphere.json";

    private sealed record Follower(GpuParticles3D Particles, Vector3 Offset, HashSet<string>? Grounds);

    private readonly List<Follower> _followers = new();
    private GroundField _ground = null!;

    /// <summary>Build the run's emitters (none when the file is missing or no recipe builds); returns how many.</summary>
    public int Build(ParticleRecipes recipes, GroundField ground, Func<float, float, float> scenery)
    {
        _ground = ground;
        if (!Godot.FileAccess.FileExists(ResourcePath))
            return 0;
        using var json = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ResourcePath));
        string weather = VisualOptions.Weather ?? "fair";
        int built = 0;
        foreach (var e in json.RootElement.GetProperty("emitters").EnumerateArray())
        {
            string recipe = e.GetProperty("recipe").GetString()!;
            if (e.TryGetProperty("weather", out var w) && !w.EnumerateArray().Any(x => x.GetString() == weather))
                continue;
            if (e.TryGetProperty("day_only", out var day) && day.ValueKind == JsonValueKind.True && SkyView.DaylightNow < 0.5f)
                continue;
            if (e.TryGetProperty("at", out var at) && at.GetString() == "ravine_rivers")
            {
                if (VisualOptions.Water == "none")
                    continue;
                foreach (var (centre, size, _) in WaterView.Strips(ground.Region))
                {
                    if (recipes.Build(recipe) is not { } mist)
                        continue;
                    // Along the strip: the recipe's box is laid lengthwise (its long axis is z; a strip running along x is turned).
                    mist.Position = new Vector3(centre.X, scenery(centre.X, centre.Z) + 2.5f, centre.Z);
                    if (size.X > size.Y)
                        mist.RotationDegrees = new Vector3(0, 90, 0);
                    AddChild(mist);
                    built++;
                }
                continue;
            }
            if (recipes.Build(recipe) is not { } particles)
                continue;
            var offset = e.TryGetProperty("offset", out var o) ? new Vector3((float)o[0].GetDouble(), (float)o[1].GetDouble(), (float)o[2].GetDouble()) : Vector3.Zero;
            var grounds = e.TryGetProperty("grounds", out var g) ? g.EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal) : null;
            particles.Emitting = false;
            AddChild(particles);
            _followers.Add(new Follower(particles, offset, grounds));
            built++;
        }
        GD.Print($"UNNAMED atmosphere: {built} emitters under {weather} weather");
        return built;
    }

    public override void _Process(double delta)
    {
        if (_followers.Count == 0 || GetViewport().GetCamera3D() is not { } camera)
            return;
        var eye = camera.GlobalPosition;
        foreach (var follower in _followers)
        {
            follower.Particles.GlobalPosition = eye + follower.Offset;
            follower.Particles.Emitting = follower.Grounds is null || OnGround(eye, follower.Grounds);
        }
    }

    /// <summary>Whether most of the ground under a point is one of <paramref name="grounds"/> (the cells' blended weights).</summary>
    private bool OnGround(Vector3 at, HashSet<string> grounds)
    {
        if (!_ground.Region.HasPoint(new Vector2(at.X, at.Z)))
            return false;
        var w = _ground.CellWeights(at.X, at.Z);
        float share = 0;
        for (int i = 0; i < 4; i++)
        {
            if (_ground.Grounds[i] is { } ground && grounds.Contains(ground))
                share += w[i];
        }
        return share >= 0.5f;
    }
}
