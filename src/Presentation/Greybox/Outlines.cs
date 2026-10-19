// UNNAMED Presentation - the outline on what the character can work with: the thing the prompt is about, and nearby nodes with something to
// gather (W12; the owner's second play: "no indication when something can be gathered from")
// Godot presentation only: it draws over what the views built and changes nothing in the world (D-11)

using Godot;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Outlines drawn as a shell: every mesh under an outlined thing gets an overlay pass that pushes its back faces out along the normals by
/// a pixel or two on the screen - the same width at any distance - and paints them one flat colour: a thin rim, never a fill. Ember for
/// the thing the prompt is about; a fainter brass for a node near the character with something to take. High contrast makes them wider
/// and solid. What was outlined and is no longer is given back its own overlay (none).
/// </summary>
public sealed class Outlines
{
    private const string Code = @"
shader_type spatial;
render_mode unshaded, cull_front, depth_draw_never, shadows_disabled, fog_disabled;
uniform vec4 tone : source_color = vec4(0.93, 0.66, 0.33, 0.8);
uniform float pixels = 1.5;
void vertex() {
    vec4 clip = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
    vec2 along = normalize((PROJECTION_MATRIX * vec4(mat3(MODELVIEW_MATRIX) * NORMAL, 0.0)).xy + vec2(0.00001));
    clip.xy += along * clip.w * 2.0 * pixels / VIEWPORT_SIZE;
    POSITION = clip;
}
void fragment() {
    ALBEDO = tone.rgb;
    ALPHA = tone.a;
}";

    private readonly Shader _shader = new() { Code = Code };
    private readonly Dictionary<OutlineTone, ShaderMaterial> _materials = new();
    private readonly Dictionary<GeometryInstance3D, OutlineTone> _drawn = new();
    private bool _contrast;

    /// <summary>Outline these things, each in its tone, and nothing else; a null root (nothing drawn for it) is passed over.</summary>
    public void Show(IEnumerable<(Node3D? Root, OutlineTone Tone)> wanted, bool highContrast)
    {
        if (highContrast != _contrast)
        {
            _contrast = highContrast;
            _materials.Clear();
            _drawn.Clear();   // every overlay is put on again in the new look
        }
        var now = new Dictionary<GeometryInstance3D, OutlineTone>();
        foreach (var (root, tone) in wanted)
        {
            if (root is null || tone == OutlineTone.None || !GodotObject.IsInstanceValid(root))
                continue;
            foreach (var mesh in Meshes(root))
            {
                // The stronger tone wins where two outlines meet: the focus over the soft one.
                if (!now.TryGetValue(mesh, out var had) || tone == OutlineTone.Focus && had != OutlineTone.Focus)
                    now[mesh] = tone;
            }
        }
        foreach (var (mesh, _) in _drawn)
        {
            if (!now.ContainsKey(mesh) && GodotObject.IsInstanceValid(mesh))
                mesh.MaterialOverlay = null;
        }
        foreach (var (mesh, tone) in now)
        {
            if (!_drawn.TryGetValue(mesh, out var had) || had != tone || mesh.MaterialOverlay is null)
                mesh.MaterialOverlay = Material(tone);
        }
        _drawn.Clear();
        foreach (var (mesh, tone) in now)
            _drawn[mesh] = tone;
    }

    private ShaderMaterial Material(OutlineTone tone)
    {
        if (_materials.TryGetValue(tone, out var made))
            return made;
        var colour = tone == OutlineTone.Focus ? HudStyle.Ember : HudStyle.Brass;
        float alpha = _contrast ? 1f : tone == OutlineTone.Focus ? 0.8f : 0.4f;
        float pixels = (tone == OutlineTone.Focus ? 1.5f : 1f) * (_contrast ? 1.6f : 1f);
        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("tone", new Color(colour, alpha));
        material.SetShaderParameter("pixels", pixels);
        return _materials[tone] = material;
    }

    private static IEnumerable<GeometryInstance3D> Meshes(Node root)
    {
        if (root is GeometryInstance3D geometry and (MeshInstance3D or CsgShape3D) && geometry.IsVisibleInTree())
            yield return geometry;
        foreach (var child in root.GetChildren())
        {
            foreach (var mesh in Meshes(child))
                yield return mesh;
        }
    }
}
