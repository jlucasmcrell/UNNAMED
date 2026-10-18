using Godot;

namespace UNNAMED.Presentation.Perf;

// One fixed camera, same session, temporary component exclusions. Never an acceptance capture.
public sealed class FrontierBreakdown
{
    private readonly (Node3D Node, bool Visible)[] _scatter, _trees, _terrain;
    private readonly (Light3D Light, bool Shadows)[] _lights;
    private readonly (CompositorEffect Effect, bool Enabled)[] _effects;
    private static readonly string[] Names = ["baseline", "no_scatter", "no_trees", "no_terrain", "no_shadows", "no_clouds", "no_vegetation", "baseline_again"];
    public string Name(int index) => Names[index];

    public FrontierBreakdown(Node root)
    {
        _scatter = [(root.GetNode<Node3D>("Scatter"), root.GetNode<Node3D>("Scatter").Visible)];
        _trees = root.GetNode<Node3D>("Hollow").FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
            .Where(m => m.Mesh is not null && Enumerable.Range(0, m.Mesh.GetSurfaceCount())
                .Any(s => m.GetActiveMaterial(s) is ShaderMaterial { Shader: { } shader }
                    && shader.Code.Contains("instance uniform float tree_height", StringComparison.Ordinal)))
            .Select(m => ((Node3D)m, m.Visible)).ToArray();
        _terrain = root.FindChildren("Terrain3D", "Node3D", true, false).OfType<Node3D>().Select(n => (n, n.Visible)).ToArray();
        _lights = root.FindChildren("*", "Light3D", true, false).OfType<Light3D>().Select(l => (l, l.ShadowEnabled)).ToArray();
        _effects = root.FindChildren("*", "WorldEnvironment", true, false).OfType<WorldEnvironment>()
            .Where(w => w.Compositor is not null).SelectMany(w => w.Compositor!.CompositorEffects)
            .Distinct().Select(e => (e, e.Enabled)).ToArray();
        if (_trees.Length == 0 || _terrain.Length == 0 || _effects.Length == 0)
            throw new InvalidOperationException("Incomplete diagnostic component inventory");
        GD.Print($"FRONTIER DIAGNOSTIC sweep: {_trees.Length} tree meshes, {_terrain.Length} native terrains, {_lights.Length} lights, {_effects.Length} compositor effects");
    }

    public void Apply(int index)
    {
        foreach (var (node, visible) in _scatter.Concat(_trees).Concat(_terrain)) node.Visible = visible;
        foreach (var (light, shadows) in _lights) if (GodotObject.IsInstanceValid(light)) light.ShadowEnabled = shadows;
        foreach (var (effect, enabled) in _effects) effect.Enabled = enabled;
        if (index is 1 or 6) foreach (var (node, _) in _scatter) node.Visible = false;
        if (index is 2 or 6) foreach (var (node, _) in _trees) node.Visible = false;
        if (index == 3) foreach (var (node, _) in _terrain) node.Visible = false;
        if (index == 4) foreach (var (light, _) in _lights) if (GodotObject.IsInstanceValid(light)) light.ShadowEnabled = false;
        if (index == 5) foreach (var (effect, _) in _effects) effect.Enabled = false;
        GD.Print($"FRONTIER DIAGNOSTIC sweep stage: {Name(index)}");
    }
}
