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

    private sealed record Follower(GpuParticles3D Particles, Vector3 Offset, HashSet<string>? Grounds,
        HashSet<string>? Weathers, bool DayOnly, bool NightOnly, float WindResponse, bool RequiresSky)
    {
        public float Amount = -1;
        public bool Active;
        public readonly ParticleProcessMaterial? WindMaterial = Particles.ProcessMaterial as ParticleProcessMaterial;
        public readonly Vector3 Gravity = (Particles.ProcessMaterial as ParticleProcessMaterial)?.Gravity ?? Vector3.Zero;
        public Vector3 AppliedGravity;
    }

    private readonly List<Follower> _followers = new();
    private GroundField _ground = null!;
    private RayCast3D? _shelter;
    public bool PrecipitationSheltered => _shelter?.IsColliding() == true;
    public bool PrecipitationActive => _followers.Any(f => f.RequiresSky && f.Active);

    /// <summary>Build the run's emitters (none when the file is missing or no recipe builds); returns how many.</summary>
    public int Build(ParticleRecipes recipes, GroundField ground, Func<float, float, float> scenery)
    {
        _ground = ground;
        if (!Godot.FileAccess.FileExists(ResourcePath))
            return 0;
        using var json = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ResourcePath));
        int built = 0;
        foreach (var e in json.RootElement.GetProperty("emitters").EnumerateArray())
        {
            string recipe = e.GetProperty("recipe").GetString()!;
            var weathers = e.TryGetProperty("weather", out var w)
                ? w.EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal) : null;
            bool dayOnly = e.TryGetProperty("day_only", out var day) && day.ValueKind == JsonValueKind.True;
            bool nightOnly = e.TryGetProperty("night_only", out var night) && night.ValueKind == JsonValueKind.True;
            // The otherworld concepts' air (Greybox/Otherworld.cs): drawn only when a concept is on.
            if (e.TryGetProperty("otherworld", out var other) && other.ValueKind == JsonValueKind.True && Otherworld.Strength <= 0)
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
            float windResponse = e.TryGetProperty("wind_response", out var response) ? response.GetSingle() : 0;
            bool requiresSky = e.TryGetProperty("requires_sky", out var exposed) && exposed.GetBoolean();
            _followers.Add(new Follower(particles, offset, grounds, weathers, dayOnly, nightOnly, windResponse, requiresSky));
            built++;
        }
        if (_shelter is null && _followers.Any(f => f.RequiresSky))
        {
            _shelter = new RayCast3D { Name = "WeatherShelter", TargetPosition = Vector3.Up * 32,
                CollisionMask = HollowView.CameraCollisionLayer, HitFromInside = true, Enabled = true };
            AddChild(_shelter);
        }
        GD.Print($"UNNAMED atmosphere: {built} emitters, gated by live WP7 weather and daylight");
        return built;
    }

    public override void _Process(double delta)
    {
        if (_followers.Count == 0 || GetViewport().GetCamera3D() is not { } camera)
            return;
        var eye = camera.GlobalPosition;
        var weather = SkyView.Weather;
        bool outside = IsVisibleInTree();
        if (_shelter is not null) { _shelter.GlobalPosition = eye; _shelter.Enabled = outside; }
        foreach (var follower in _followers)
        {
            follower.Particles.GlobalPosition = eye + follower.Offset;
            if (follower.WindResponse > 0 && follower.WindMaterial is { } material)
            {
                var gravity = follower.Gravity + WindField.Direction * (WindField.Strength * follower.WindResponse);
                if (gravity != follower.AppliedGravity) { follower.AppliedGravity = gravity; material.Gravity = gravity; }
            }
            float amount = follower.Weathers is null ? 1 : 0;
            if (follower.Weathers is { } allowed)
                foreach (string state in allowed) amount += weather.Amount(state);
            amount *= follower.DayOnly ? SkyView.DaylightLevel : follower.NightOnly ? 1 - SkyView.DaylightLevel : 1;
            bool active = outside && amount > .001f && (!follower.RequiresSky || !PrecipitationSheltered)
                && (follower.Grounds is null || OnGround(eye, follower.Grounds));
            if (amount != follower.Amount)
            {
                follower.Amount = amount;
                follower.Particles.AmountRatio = Mathf.Clamp(amount, 0, 1);
            }
            if (active != follower.Active)
            {
                follower.Active = active;
                follower.Particles.Emitting = active;
                // Existing world-space drops would otherwise fall through the roof until their lifetime ends.
                if (follower.RequiresSky) follower.Particles.Visible = active;
            }
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

