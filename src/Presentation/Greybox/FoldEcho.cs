// UNNAMED Presentation - Tavar held by the Foldscar's fold: the restrained doubling and the delayed shadow (Phase B remediation, M03)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// The person the fold holds, drawn as the content bible's Foldscar asks (PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md, the
/// Foldscar: "slight image doubling; delayed shadow"): while the fold's barrier stands, a faint double of the body a hand's width
/// off it and a shadow cast by a second copy that moves a beat behind; when the heart is steadied and the barrier falls, the double
/// thins away over two seconds and the shadow rejoins the body. Keyed only to the barrier's own state - the quest is untouched.
/// </summary>
public sealed partial class FoldEcho : Node3D
{
    /// <summary>The fold's barrier and the person it holds (content/regions/ashen_hollow.yaml: Tavar placed inside the fold).</summary>
    public const string Barrier = "barrier.foldscar_fold";
    public const string Held = "npc.ashen_hollow.tavar_orr";

    private const double ShadowLag = 0.45;
    private const float DoubleAlpha = 0.11f;

    private Figure? _double;
    private Figure? _shadow;
    private StandardMaterial3D? _ghost;
    private readonly Queue<(double T, Vector3 Feet, float Facing, float Speed)> _trail = new();
    private double _clock;
    private float _fade = 1f;
    private bool _released;

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
        if (!foldStanding)
            _released = true;
        _fade = _released ? Math.Max(0, _fade - (float)delta / 2f) : 1f;
        bool on = _fade > 0;
        _double.Visible = on;
        _shadow.Visible = on;
        SetBodyShadow(body, !on);
        if (!on)
            return;
        // The double: a few centimetres to the body's side, drifting slowly, a quarter-beat behind; the shadow: a full beat behind.
        var side = new Vector3(Mathf.Cos(facing), 0, -Mathf.Sin(facing));
        float drift = 0.13f + 0.04f * Mathf.Sin((float)_clock * 0.9f);
        _double.Pose(feet + side * drift, facing + 0.02f * Mathf.Sin((float)_clock * 0.6f), speed, delta);
        _shadow.Pose(late.Feet, late.Facing, late.Speed, delta);
        _ghost.AlbedoColor = _ghost.AlbedoColor with { A = DoubleAlpha * _fade * (0.85f + 0.15f * Mathf.Sin((float)_clock * 2.3f)) };
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
