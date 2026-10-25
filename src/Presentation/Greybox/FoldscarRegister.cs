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
    // A hand's breadth at the 2.8 m the stones were first drawn at, and the same share of a stone's height at any size.
    private const float DoubleOffsetPerMetre = 0.1f / 2.8f;
    private const double TurnSeconds = 1.3;
    private const double RegisterSeconds = 0.9;
    // Grand scale (GS-2): the release runs out past the whole ring and through anyone watching from the road.
    private const float PulseSpeed = 40f;
    private const float PulseReach = 300f;
    private const float PulseHeight = 16f;
    private const float GlowStrength = 1.6f;
    // The moment itself (asset overhaul 2026-10-14, owner: "only glow faintly after activation"): a Godot scene per stone and for the
    // heart - flash of light, a ring running out over the ground (a decal, so it follows the terrain), a shell of bent air, a column of
    // light, dust thrown out in a ring, motes - played once ("activate"), or put straight into its resting state ("lit") on a load.
    private const string StoneScene = "res://Art/vfx/foldscar/foldscar_stone_set.tscn";
    private const string HeartScene = "res://Art/vfx/foldscar/foldscar_heart_release.tscn";

    private sealed class Stone
    {
        public required SwitchSite Site { get; init; }
        public required Node3D Model { get; init; }
        public required float Yaw { get; init; }
        public required bool Turns { get; init; }
        public required Node3D Double { get; init; }
        public required List<BaseMaterial3D> DoubleMaterials { get; init; }
        public required Vector3 Along { get; init; }
        public required float Offset { get; init; }
        public List<(Node3D Model, Node3D Double, List<BaseMaterial3D> Materials)> Riders { get; } = new();
        public Node3D? Shell { get; init; }
        public ShaderMaterial? Glow { get; init; }
        public bool Set { get; set; }
        public bool Placed { get; set; }
        public Node3D? Effect { get; set; }
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
    private readonly List<ShaderMaterial> _tear = new();
    private readonly List<(Node3D Piece, Vector3 Axis, float Speed)> _drift = new();
    private bool _still;
    private readonly List<GpuParticles3D> _motes = new();
    private Vector3? _heart;
    private double? _releasedAt;
    private MeshInstance3D? _pulse;
    private Node3D? _heartEffect;
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
            Offset = DoubleOffsetPerMetre * Art.ArtGallery.Bounds(model).Size.Y, Shell = shell, Glow = glow,
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

    /// <summary>The heart's hanging upper half (FoldscarDressing): doubled with the heart and brought into register with it.</summary>
    public void AddHanging(Node3D hanging)
    {
        if (_stones.Values.FirstOrDefault(s => !s.Turns) is not { } heart)
            return;
        var (ghost, materials) = Double(hanging, null);
        AddChild(ghost);
        heart.Riders.Add((hanging, ghost, materials));
        Rest(heart);
    }

    /// <summary>The tear between the heart's halves (its bend and its seam); it goes when the heart is steadied.</summary>
    public void AddTear(params ShaderMaterial[] tear) => _tear.AddRange(tear);

    /// <summary>A piece of the break's debris turning slowly where it hangs, <paramref name="speed"/> degrees a second; still once steadied.</summary>
    public void AddDrift(Node3D piece, Vector3 axis, float speed) => _drift.Add((piece, axis, Mathf.DegToRad(speed)));

    /// <summary>What rises in the basin (dust, the tear's sparks); it stops when the heart is steadied.</summary>
    public void AddMotes(GpuParticles3D motes) => _motes.Add(motes);

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
            if (view.Set && stone.Turns)
                stone.Effect ??= Play(StoneScene, stone.Model.GlobalPosition, turning ? "activate" : "lit");
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
            foreach (var tear in _tear)
                tear.SetShaderParameter("presence", settled ? 0f : 1f);
            foreach (var motes in _motes)
                motes.Emitting = !settled;
            _still = settled;
            if (settled && _heart is { } at)
                _heartEffect ??= Play(HeartScene, at, "lit");
        }
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        foreach (var stone in _stones.Values)
            Animate(stone);
        foreach (var fold in _folds.Values.Where(f => f.FellAt is not null))
            Animate(fold);
        if (!_still)
        {
            foreach (var (piece, axis, speed) in _drift)
                piece.Rotate(axis, speed * (float)delta);
        }
        Pulse();
    }

    // ── the stones ──────────────────────────────────────────────────────────

    /// <summary>A stone put where it belongs without a transition: square and lit if set; off square and doubled if not.</summary>
    private static void Rest(Stone stone)
    {
        stone.Model.Rotation = stone.Model.Rotation with { Y = stone.Yaw - (stone.Turns && !stone.Set ? Mathf.DegToRad(OffSquareDegrees) : 0) };
        stone.Double.Visible = !stone.Set;
        foreach (var rider in stone.Riders)
            rider.Double.Visible = !stone.Set;
        if (stone.Shell is not null)
            stone.Shell.Visible = stone.Set && Held(stone) > 0;
        stone.Glow?.SetShaderParameter("strength", stone.Set ? GlowStrength * Held(stone) : 0f);
    }

    // "The stones hold their light" - the stones, faintly at sixteen metres of carved face; the heart holds none (grand scale, GS-2).
    private static float Held(Stone stone) => stone.Turns ? 0.05f : 0f;

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
                stone.Shell.Visible = Held(stone) > 0;
                float light = Mathf.Clamp((float)((t - 0.4) / 0.6), 0, 1);
                float settle = Mathf.Exp(-(float)Math.Pow((t - 1.0) / 0.3, 2));
                stone.Glow?.SetShaderParameter("strength", Held(stone) * (GlowStrength * light * light * (3 - 2 * light) + 1.2f * settle));
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
        foreach (var rider in stone.Riders)
            rider.Double.Visible = registered < 1;
        if (!stone.Double.Visible)
            return;
        float drift = stone.Offset * (1 + 0.18f * Mathf.Sin((float)_clock * 0.7f + stone.Along.X * 3));
        var offset = stone.Along * drift * (1 - registered) + new Vector3(0, stone.Offset * 0.12f * Mathf.Sin((float)_clock * 0.45f), 0) * (1 - registered);
        stone.Double.GlobalTransform = stone.Model.GlobalTransform with { Origin = stone.Model.GlobalTransform.Origin + offset };
        float alpha = DoubleAlpha * (1 - registered) * (0.85f + 0.15f * Mathf.Sin((float)_clock * 1.9f + stone.Along.Z * 5));
        foreach (var material in stone.DoubleMaterials)
            material.AlbedoColor = material.AlbedoColor with { A = alpha };
        foreach (var (model, ghost, materials) in stone.Riders)
        {
            ghost.GlobalTransform = model.GlobalTransform with { Origin = model.GlobalTransform.Origin + offset };
            foreach (var material in materials)
                material.AlbedoColor = material.AlbedoColor with { A = alpha };
        }
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
                // A model wearing a world material (the heart's dark stone) has no plain material to copy: its double is as dark as it is,
                // or the faint copy reads as a pale coat over it in the sun.
                var ghost = mesh.GetActiveMaterial(s) is BaseMaterial3D own ? (BaseMaterial3D)own.Duplicate() : new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.07f, 0.07f, 0.09f), Roughness = 0.6f,
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

    /// <summary>An activation scene at a stone's or the heart's foot, playing <paramref name="animation"/>; null without the scene.</summary>
    private Node3D? Play(string scene, Vector3 at, string animation)
    {
        if (ResourceLoader.Load<PackedScene>(scene) is not { } packed)
            return null;
        var effect = packed.Instantiate<Node3D>();
        AddChild(effect);
        effect.GlobalPosition = at;
        effect.GetNode<AnimationPlayer>("Anim").Play(animation);
        return effect;
    }

    // ── the heart's release ─────────────────────────────────────────────────

    /// <summary>The heart steadied and the fold fallen, in play: one pulse out across the ground from the heart.</summary>
    private void Release()
    {
        _releasedAt = _clock;
        _still = true;
        if (_heart is not { } heart)
            return;
        _heartEffect ??= Play(HeartScene, heart, "activate");
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
                Name = "ReleasePulse", Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = PulseHeight, CapTop = false, CapBottom = false, RadialSegments = 256, Rings = 1 },
                MaterialOverride = _pulseMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            AddChild(_pulse);
        }
        // From well under the ground (which hides what is below it, whatever its height there) to a soft top well over a person's head.
        _pulse.GlobalPosition = heart + new Vector3(0, PulseHeight / 2 - 4f, 0);
        foreach (var motes in _motes)
            motes.Emitting = false;
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
        // The tear goes out of the gap over two seconds.
        foreach (var tear in _tear)
            tear.SetShaderParameter("presence", 1 - Mathf.Clamp((float)(t / 2.0), 0, 1));
        // What rested on nothing comes down as the pulse reaches it, falling as a dropped thing falls - a pebble at once, a shard from
        // twenty metres for two seconds: the last of the fold's wrongness.
        bool falling = t < 2.0;
        foreach (var (pebble, hover, rest, distance) in _pebbles)
        {
            float drop = Mathf.Max(0.32f, Mathf.Sqrt(2 * Mathf.Max(hover - rest, 0) / 9.81f));
            float fall = Mathf.Clamp((float)((t - (distance - 1.6f) / PulseSpeed) / drop), 0, 1);
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
    height = VERTEX.y + 8.0;
}
void fragment() {
    // A faint wall of violet light rising from wherever the ground cuts it, thinning to nothing at its top...
    float wall = 1.0 - smoothstep(6.0, 16.0, height);
    // ...and a fine line where it meets the ground (how far the ground behind lies beyond it, from the depth buffer).
    float depth = texture(depth_tex, SCREEN_UV).r;
    vec4 behind_view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
    float gap = -behind_view.z / behind_view.w + VERTEX.z;
    float contact = 1.0 - smoothstep(0.0, 0.55, gap);
    vec2 shift = vec2(0.004, 0.0) * wall * fade;
    vec3 behind = textureLod(screen_tex, SCREEN_UV + shift, 0.0).rgb;
    ALBEDO = behind + vec3(0.68, 0.48, 1.0) * (wall * 0.26 + contact * 1.6) * fade;
    ALPHA = clamp(wall * 0.65 + contact, 0.0, 1.0) * fade;
}
";
}
