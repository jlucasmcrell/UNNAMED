// UNNAMED Presentation - Tavar held by the Foldscar's fold: the restrained doubling and the delayed shadow (Phase B remediation, M03)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// The person the fold holds, drawn as the content bible's Foldscar asks (PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md, the
/// Foldscar: "slight image doubling; delayed shadow"; RACES.md, the Orenth: "a shadow lags behind by an instant"): while the fold's
/// barrier stands, a faint double of the body a hand's width off it, and a shadow cast by a second copy that moves a beat behind and
/// stands a little aside - across the light, so even standing still his shadow does not quite meet his feet. When the heart is steadied
/// and the barrier falls (the switch's done text: "across it Tavar's shadow catches up with him"), the double slides back into him and
/// thins away and the shadow slides home under him; then his own shadow takes over. Keyed only to the barrier's own state - the quest is
/// untouched.
/// </summary>
public sealed partial class FoldEcho : Node3D
{
    /// <summary>The fold's barrier and the person it holds (content/regions/ashen_hollow.yaml: Tavar placed inside the fold).</summary>
    public const string Barrier = "barrier.foldscar_fold";
    public const string Held = "npc.ashen_hollow.tavar_orr";

    private const double ShadowLag = 0.45;
    private const float DoubleAlpha = 0.11f;
    private const float ShadowAside = 0.3f;
    private const double RegisterSeconds = 1.1;
    private const double CatchUpSeconds = 1.3;

    private Figure? _double;
    private Figure? _shadow;
    private StandardMaterial3D? _ghost;
    private DirectionalLight3D? _sun;
    private bool _sunSought;
    private readonly Queue<(double T, Vector3 Feet, float Facing, float Speed)> _trail = new();
    private double _clock;
    private double? _releasedAt;
    private bool _done;

    /// <summary>Build the copies (the same person's skinned body, twice); false when the person has no skinned body.</summary>
    public bool Build(Art.ArtLibrary art, Art.ArtBindings bindings)
    {
        if (Art.SkinnedFigure.Create(art, bindings, Held) is not { } copy || Art.SkinnedFigure.Create(art, bindings, Held) is not { } shade)
            return false;
        _ghost = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.62f, 0.55f, 0.82f, DoubleAlpha),
            CullMode = BaseMaterial3D.CullModeEnum.Back,
            NoDepthTest = false,
        };
        _double = copy;
        _shadow = shade;
        AddChild(copy);
        AddChild(shade);
        foreach (var mesh in Meshes(copy))
        {
            mesh.MaterialOverride = _ghost;
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        foreach (var mesh in Meshes(shade))
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
        return true;
    }

    /// <summary>
    /// A frame: whether the fold still stands, and the held body's pose as drawn. The real body's own shadow is turned off while the
    /// delayed one stands in for it.
    /// </summary>
    public void Draw(bool foldStanding, Figure body, Vector3 feet, float facing, float speed, double delta)
    {
        if (_double is null || _shadow is null || _ghost is null)
            return;
        _clock += delta;
        _trail.Enqueue((_clock, feet, facing, speed));
        while (_trail.Count > 1 && _trail.Peek().T < _clock - ShadowLag)
            _trail.Dequeue();
        var late = _trail.Peek();
        // Found already fallen (a load): at rest at once; fallen in play: the release.
        if (!foldStanding && _releasedAt is null)
            _releasedAt = _clock <= delta ? _clock - 10 : _clock;
        // Standing: out of register. Released: the double slides back in as it thins, and the shadow comes home.
        double since = _releasedAt is { } at ? _clock - at : 0;
        float register = _releasedAt is null ? 0 : Smooth(since / RegisterSeconds);
        float fade = _releasedAt is null ? 1 : 1 - Smooth((since - 0.25) / (RegisterSeconds + 0.3));
        float home = _releasedAt is null ? 0 : Smooth(since / CatchUpSeconds);
        bool on = !_done && (fade > 0 || home < 1);
        _done |= !on;
        _double.Visible = on;
        _shadow.Visible = on;
        SetBodyShadow(body, !on);
        if (!on)
            return;
        // The double: a few centimetres to the body's side, drifting slowly, a quarter-beat behind.
        var side = new Vector3(Mathf.Cos(facing), 0, -Mathf.Sin(facing));
        float drift = (0.13f + 0.04f * Mathf.Sin((float)_clock * 0.9f)) * (1 - register);
        _double.Pose(feet + side * drift, facing + 0.02f * Mathf.Sin((float)_clock * 0.6f) * (1 - register), speed, delta);
        // The shadow: a full beat behind, and aside across the light by a hand and a half.
        var shadowFeet = late.Feet.Lerp(feet, home) + Aside(side) * ShadowAside * (1 - home);
        _shadow.Pose(shadowFeet, Mathf.LerpAngle(late.Facing, facing, home), late.Speed, delta);
        // Let go, the double is seen for a moment as it snaps back into him.
        float snap = _releasedAt is null ? 0 : 1.6f * Mathf.Exp(-(float)Math.Pow((since - 0.35) / 0.25, 2));
        _ghost.AlbedoColor = _ghost.AlbedoColor with { A = DoubleAlpha * (fade + snap) * (0.85f + 0.15f * Mathf.Sin((float)_clock * 2.3f)) };
    }

    /// <summary>Across the sun's light on the ground (so the shadow is seen to stand off the feet, not merely lengthen); else the body's side.</summary>
    private Vector3 Aside(Vector3 fallback)
    {
        if (!_sunSought)
        {
            _sunSought = true;
            _sun = GetTree().Root.FindChildren("*", nameof(DirectionalLight3D), true, false).OfType<DirectionalLight3D>()
                .Where(l => l.Visible).OrderByDescending(l => l.LightEnergy).FirstOrDefault();
        }
        if (_sun is null || !IsInstanceValid(_sun))
            return fallback;
        var light = -_sun.GlobalTransform.Basis.Z;
        var across = new Vector3(light.X, 0, light.Z).Cross(Vector3.Up);
        return across.LengthSquared() > 0.0001f ? across.Normalized() : fallback;
    }

    private static float Smooth(double x)
    {
        float t = Mathf.Clamp((float)x, 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    private static void SetBodyShadow(Figure body, bool casts)
    {
        foreach (var mesh in Meshes(body))
            mesh.CastShadow = casts ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
    }

    private static IEnumerable<MeshInstance3D> Meshes(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is MeshInstance3D mesh)
                yield return mesh;
            foreach (var inner in Meshes(child))
                yield return inner;
        }
    }
}
