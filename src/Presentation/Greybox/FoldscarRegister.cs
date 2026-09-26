// UNNAMED Presentation - the Foldscar coming back into register: each Quiet Stone turned into line, the heart's release, the ground at rest (Phase B VFX lane)
// Godot presentation only (D-11): keyed to the switches' and the barrier's own state as the simulation publishes it; nothing here is read by the game

using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// The Quiet Stones and the heart as the content bible's Foldscar asks (restrained weirdness: slight image doubling, delayed shadow,
/// displaced audio): while a stone is out of line its image is doubled a hand's breadth along the ring - a faint copy that drifts - and
/// it sits a few degrees off square. Turned, it grinds round into square (the switch's done text: "a hand's width round... square to the
/// ring") while its double slides back into it and is gone, and then the pale light of a set stone comes up on it. The heart - the switch
/// whose flag lifts the fold - does not turn: steadied, its double registers the same way and one faint pulse runs out across the ground
/// from it; the fold's doubled view closes into register as the pulse crosses it and the fold thins away; the stones that rested on
/// nothing round the heart come down as the pulse passes them. Loaded already set, everything is simply at rest. Presentation only:
/// the stones' collision is the domain's circle, untouched.
/// </summary>
public sealed partial class FoldscarRegister : Node3D
{
    // A hand's width round at the edge of a stone some 0.6 m across: about fourteen degrees.
    private const float OffSquareDegrees = 14f;
    private const float DoubleAlpha = 0.3f;
    private const float DoubleOffset = 0.1f;
    private const double TurnSeconds = 1.3;
    private const double RegisterSeconds = 0.9;
    private const float PulseSpeed = 7.5f;
    private const float PulseReach = 17f;
    private const float GlowStrength = 0.8f;

    private sealed class Stone
    {
        public required SwitchSite Site { get; init; }
        public required Node3D Model { get; init; }
        public required float Yaw { get; init; }
        public required bool Turns { get; init; }
        public required Node3D Double { get; init; }
        public required List<BaseMaterial3D> DoubleMaterials { get; init; }
        public required Vector3 Along { get; init; }
        public Node3D? Shell { get; init; }
        public ShaderMaterial? Glow { get; init; }
        public bool Set { get; set; }
        public bool Placed { get; set; }
        public double? SetAt { get; set; }
    }

    private sealed class Fold
    {
        public required BarrierSite Site { get; init; }
        public required MeshInstance3D Node { get; init; }
        public required ShaderMaterial Material { get; init; }
        public bool Standing { get; set; } = true;
        public double? FellAt { get; set; }
    }

    private readonly Dictionary<string, Stone> _stones = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Fold> _folds = new(StringComparer.Ordinal);
    private readonly List<(Node3D Pebble, float Hover, float Settled, float Distance)> _pebbles = new();
    private Vector3? _heart;
    private double? _releasedAt;
    private MeshInstance3D? _pulse;
    private ShaderMaterial? _pulseMaterial;
    private double _clock;

    /// <summary>Whether a switch's set mark is drawn here (the owner leaves its visibility to this).</summary>
    public bool Owns(string switchKey) => _stones.ContainsKey(switchKey);

    /// <summary>Whether a barrier's visibility is drawn here.</summary>
    public bool OwnsFold(string barrierKey) => _folds.ContainsKey(barrierKey);

    /// <summary>A switch on a structure the library draws: its model, and the glow shell that shows it set (the owner built it).</summary>
    public void AddStone(SwitchSite site, Node3D model, Node3D? shell, bool liftsABarrier, Vector3 ringCentre)
    {
        // The set light, the stone's own copy so it can come up on its own.
        ShaderMaterial? glow = null;
        if (shell is not null)
        {
            glow = (ShaderMaterial)Palette.AlignedGlow.Duplicate();
            glow.SetShaderParameter("strength", 0f);
            foreach (var mesh in shell.GetChildren().OfType<MeshInstance3D>())
                mesh.MaterialOverride = glow;
        }
        var outward = model.GlobalPosition - ringCentre;
        outward.Y = 0;
        // Out of line along the ring, not towards or away from its middle; the heart, at the middle, is off to one side.
        var along = outward.LengthSquared() > 0.01f ? outward.Normalized().Cross(Vector3.Up) : Vector3.Right;
        var (ghost, materials) = Double(model, shell);
        AddChild(ghost);
        _stones[site.Key] = new Stone
        {
            Site = site, Model = model, Yaw = model.Rotation.Y, Turns = !liftsABarrier, Double = ghost, DoubleMaterials = materials, Along = along,
            Shell = shell, Glow = glow,
        };
        if (liftsABarrier)
            _heart = model.GlobalPosition;
    }

    /// <summary>The fold: its node, drawn with its own copy of the fold's material so it can close into register on its own.</summary>
    public void AddFold(BarrierSite site, MeshInstance3D node)
    {
        var material = (ShaderMaterial)((ShaderMaterial)node.MaterialOverride).Duplicate();
        node.MaterialOverride = material;
        _folds[site.Key] = new Fold { Site = site, Node = node, Material = material };
    }

    /// <summary>The stones that rest on nothing round the heart (FoldscarDressing), with the height each comes to rest at.</summary>
    public void AddPebbles(IEnumerable<(Node3D Pebble, float Settled)> pebbles)
    {
        foreach (var (pebble, rest) in pebbles)
            _pebbles.Add((pebble, pebble.Position.Y, rest, 0));
    }

    /// <summary>
    /// The switches and barriers as they now stand. <paramref name="animate"/>: the change happened in play (a stone just turned, the
    /// heart just steadied); otherwise (a load, a new world) everything is put straight where it belongs.
    /// </summary>
    public void Apply(IEnumerable<SwitchView> switches, IEnumerable<BarrierView> barriers, bool animate)
    {
        foreach (var view in switches)
        {
            if (!_stones.TryGetValue(view.Site.Key, out var stone) || (stone.Placed && stone.Set == view.Set))
                continue;
            bool turning = animate && stone.Placed && view.Set && !stone.Set;
            stone.Placed = true;
            stone.Set = view.Set;
            stone.SetAt = turning ? _clock : null;
            if (!turning)
                Rest(stone);
        }
        foreach (var view in barriers)
        {
            if (!_folds.TryGetValue(view.Site.Key, out var fold) || fold.Standing == view.Standing)
                continue;
            bool falling = animate && !view.Standing;
            fold.Standing = view.Standing;
            fold.FellAt = falling ? _clock : null;
            if (falling)
                Release();
            else
                Rest(fold);
        }
        if (!animate)
        {
            bool settled = _folds.Values.Any(f => !f.Standing);
            _releasedAt = null;
            foreach (var (pebble, hover, rest, _) in _pebbles)
                pebble.Position = pebble.Position with { Y = settled ? rest : hover };
        }
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        foreach (var stone in _stones.Values)
            Animate(stone);
        foreach (var fold in _folds.Values.Where(f => f.FellAt is not null))
            Animate(fold);
        Pulse();
    }

    // ── the stones ──────────────────────────────────────────────────────────

    /// <summary>A stone put where it belongs without a transition: square and lit if set; off square and doubled if not.</summary>
    private static void Rest(Stone stone)
    {
        stone.Model.Rotation = stone.Model.Rotation with { Y = stone.Yaw - (stone.Turns && !stone.Set ? Mathf.DegToRad(OffSquareDegrees) : 0) };
        stone.Double.Visible = !stone.Set;
        if (stone.Shell is not null)
            stone.Shell.Visible = stone.Set;
        stone.Glow?.SetShaderParameter("strength", stone.Set ? GlowStrength : 0f);
    }

    private void Animate(Stone stone)
    {
        float registered = 0, turned = 0;
        if (stone.SetAt is { } at)
        {
            double t = _clock - at;
            // Ground round, with the catch of stone on stone in it.
            float u = Mathf.Clamp((float)(t / TurnSeconds), 0, 1);
            turned = u * u * (3 - 2 * u) + 0.035f * Mathf.Sin(u * Mathf.Pi * 5) * (1 - u);
            registered = Mathf.Clamp((float)(t / RegisterSeconds), 0, 1);
            registered = registered * registered * (3 - 2 * registered);
            if (stone.Turns)
                stone.Model.Rotation = stone.Model.Rotation with { Y = stone.Yaw - Mathf.DegToRad(OffSquareDegrees) * (1 - turned) };
            if (stone.Shell is not null)
            {
                // The set light comes up as the double lands - a brief brighter moment as it settles into line - then holds.
                stone.Shell.Visible = true;
                float light = Mathf.Clamp((float)((t - 0.4) / 0.6), 0, 1);
                float settle = Mathf.Exp(-(float)Math.Pow((t - 1.0) / 0.3, 2));
                stone.Glow?.SetShaderParameter("strength", GlowStrength * light * light * (3 - 2 * light) + 1.2f * settle);
            }
            if (t > Math.Max(TurnSeconds, 1.9))
            {
                stone.SetAt = null;
                Rest(stone);
                return;
            }
        }
        else if (stone.Set)
        {
            return;
        }
        // The double: a hand's breadth along the ring, drifting a little; turned, it slides back in and is gone.
        stone.Double.Visible = registered < 1;
        if (!stone.Double.Visible)
            return;
        float drift = DoubleOffset + 0.018f * Mathf.Sin((float)_clock * 0.7f + stone.Along.X * 3);
        var offset = stone.Along * drift * (1 - registered) + new Vector3(0, 0.012f * Mathf.Sin((float)_clock * 0.45f), 0) * (1 - registered);
        stone.Double.GlobalTransform = stone.Model.GlobalTransform with { Origin = stone.Model.GlobalTransform.Origin + offset };
        float alpha = DoubleAlpha * (1 - registered) * (0.85f + 0.15f * Mathf.Sin((float)_clock * 1.9f + stone.Along.Z * 5));
        foreach (var material in stone.DoubleMaterials)
            material.AlbedoColor = material.AlbedoColor with { A = alpha };
    }

    /// <summary>A copy of a model's meshes for its double: its own surfaces made faint (lit, so it sits in the scene's light), no shadow.</summary>
    private static (Node3D Double, List<BaseMaterial3D> Materials) Double(Node3D model, Node3D? shell)
    {
        var root = new Node3D { Name = model.Name + "_double" };
        var materials = new List<BaseMaterial3D>();
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>()
                     .Where(m => m.Mesh is not null && m.VisibilityRangeBegin <= 0 && shell?.IsAncestorOf(m) != true))
        {
            var copy = new MeshInstance3D
            {
                Mesh = mesh.Mesh, Transform = Art.ArtLibrary.Relative(model, mesh), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            for (int s = 0; s < mesh.Mesh!.GetSurfaceCount(); s++)
            {
                var ghost = mesh.GetActiveMaterial(s) is BaseMaterial3D own ? (BaseMaterial3D)own.Duplicate() : new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.24f, 0.25f, 0.29f), Roughness = 0.4f,
                };
                ghost.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                // The faint cool cast of the fold's own doubling (Tavar's double is the same violet-grey).
                ghost.AlbedoColor = new Color(ghost.AlbedoColor.R * 0.88f, ghost.AlbedoColor.G * 0.86f, ghost.AlbedoColor.B, DoubleAlpha);
                ghost.CullMode = BaseMaterial3D.CullModeEnum.Back;
                copy.SetSurfaceOverrideMaterial(s, ghost);
                materials.Add(ghost);
            }
            root.AddChild(copy);
        }
        return (root, materials);
    }

    // ── the heart's release ─────────────────────────────────────────────────

    /// <summary>The heart steadied and the fold fallen, in play: one pulse out across the ground from the heart.</summary>
    private void Release()
    {
        _releasedAt = _clock;
        if (_heart is not { } heart)
            return;
        for (int i = 0; i < _pebbles.Count; i++)
        {
            var (pebble, hover, rest, _) = _pebbles[i];
            _pebbles[i] = (pebble, hover, rest, new Vector2(pebble.GlobalPosition.X - heart.X, pebble.GlobalPosition.Z - heart.Z).Length());
        }
        if (_pulse is null)
        {
            _pulseMaterial = new ShaderMaterial { Shader = new Shader { Code = PulseCode } };
            _pulse = new MeshInstance3D
            {
                Name = "ReleasePulse", Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 5.8f, CapTop = false, CapBottom = false, RadialSegments = 96, Rings = 1 },
                MaterialOverride = _pulseMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            AddChild(_pulse);
        }
        // From well under the ground (which hides what is below it, whatever its height there) to a soft top 1.8 m over the heart's foot.
        _pulse.GlobalPosition = heart + new Vector3(0, -1.1f, 0);
    }

    private void Pulse()
    {
        if (_releasedAt is not { } at)
            return;
        double t = _clock - at;
        float radius = 1.6f + PulseSpeed * (float)t;
        if (_pulse is not null && _pulseMaterial is not null)
        {
            _pulse.Visible = radius < PulseReach;
            _pulse.Scale = new Vector3(radius, 1, radius);
            float fade = Mathf.Clamp(1 - (radius - 1.6f) / (PulseReach - 1.6f), 0, 1);
            _pulseMaterial.SetShaderParameter("fade", fade * fade);
        }
        // The stones that rested on nothing come down as the pulse reaches them: a short fall, the last of the fold's wrongness.
        bool falling = false;
        foreach (var (pebble, hover, rest, distance) in _pebbles)
        {
            float fall = Mathf.Clamp((float)((t - (distance - 1.6f) / PulseSpeed) / 0.32), 0, 1);
            pebble.Position = pebble.Position with { Y = Mathf.Lerp(hover, rest, fall * fall) };
            falling |= fall < 1;
        }
        if (!falling && radius >= PulseReach)
            _releasedAt = null;
    }

    // ── the fold ────────────────────────────────────────────────────────────

    private static void Rest(Fold fold)
    {
        fold.Node.Visible = fold.Standing;
        fold.Material.SetShaderParameter("in_register", fold.Standing ? 0f : 1f);
        fold.Material.SetShaderParameter("presence", fold.Standing ? 1f : 0f);
        fold.Material.SetShaderParameter("doubling", 0.010f);
    }

    /// <summary>The fold letting go: its doubled view closes into register first, then the rest of it thins away.</summary>
    private void Animate(Fold fold)
    {
        double t = _clock - fold.FellAt!.Value;
        // A last jolt - the doubled view thrown wide for a fifth of a second - then it closes into register and thins away.
        float jolt = Mathf.Exp(-(float)Math.Pow((t - 0.12) / 0.09, 2));
        float register = Mathf.Clamp((float)((t - 0.2) / 0.9), 0, 1);
        float thin = Mathf.Clamp((float)((t - 0.6) / 1.8), 0, 1);
        fold.Material.SetShaderParameter("doubling", 0.010f * (1 + 3.5f * jolt));
        fold.Material.SetShaderParameter("in_register", register * register * (3 - 2 * register));
        fold.Material.SetShaderParameter("presence", 1 - thin * thin * (3 - 2 * thin));
        fold.Node.Visible = thin < 1;
        if (thin >= 1)
            fold.FellAt = null;
    }

    /// <summary>
    /// The heart's pulse: an open cylinder growing out from the heart, faint - a band of pale light at the ground and the world behind it
    /// shifted a hair, fading as it goes; depth-tested, so the ground and whatever stands in its way hide it truly.
    /// </summary>
    private const string PulseCode = @"
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform float fade = 1.0;
varying float height;
void vertex() {
    height = VERTEX.y + 2.9;
}
void fragment() {
    // A faint wall of light rising from wherever the ground cuts it, thinning to nothing at its top...
    float wall = 1.0 - smoothstep(3.2, 5.8, height);
    // ...and a fine line where it meets the ground (how far the ground behind lies beyond it, from the depth buffer).
    float depth = texture(depth_tex, SCREEN_UV).r;
    vec4 behind_view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
    float gap = -behind_view.z / behind_view.w + VERTEX.z;
    float contact = 1.0 - smoothstep(0.0, 0.55, gap);
    vec2 shift = vec2(0.004, 0.0) * wall * fade;
    vec3 behind = textureLod(screen_tex, SCREEN_UV + shift, 0.0).rgb;
    ALBEDO = behind + vec3(0.82, 0.87, 1.0) * (wall * 0.14 + contact * 1.3) * fade;
    ALPHA = clamp(wall * 0.5 + contact, 0.0, 1.0) * fade;
}
";
}
