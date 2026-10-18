using Godot;

namespace UNNAMED.Presentation.Perf;

// One fixed camera, same session, temporary component exclusions. Never an acceptance capture.
public sealed class FrontierBreakdown
{
    private readonly (Node3D Node, bool Visible)[] _scatter, _trees, _terrain;
    private readonly (Light3D Light, bool Shadows)[] _lights;
    private readonly (CompositorEffect Effect, bool Enabled)[] _effects;
    private static readonly string[] Names = ["baseline", "no_scatter", "no_trees", "no_terrain", "no_shadows", "no_clouds", "no_vegetation", "baseline_again"];
    private static readonly string[] ShadowNames = ["baseline_original", "atlas4096_ultra", "atlas4096_high", "atlas4096_medium", "atlas8192_high", "atlas2048_high", "no_shadows_verified", "baseline_again"];
    private readonly bool _shadowSweep = System.Environment.GetEnvironmentVariable("UNNAMED_FRONTIER_SWEEP") == "shadows";
    private readonly int _atlasSize;
    private readonly RenderingServer.ShadowQuality _filter;
    private readonly RenderingServer.ShadowQuality _positionalFilter;
    private readonly bool _depth16;
    private bool _hideShadows, _shadowControlLogged;
    public string Name(int index) => (_shadowSweep ? ShadowNames : Names)[index];

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
        _atlasSize = ProjectSettings.GetSetting("rendering/lights_and_shadows/directional_shadow/size").AsInt32();
        _filter = (RenderingServer.ShadowQuality)ProjectSettings.GetSetting("rendering/lights_and_shadows/directional_shadow/soft_shadow_filter_quality").AsInt32();
        _positionalFilter = (RenderingServer.ShadowQuality)ProjectSettings.GetSetting("rendering/lights_and_shadows/positional_shadow/soft_shadow_filter_quality").AsInt32();
        _depth16 = ProjectSettings.GetSetting("rendering/lights_and_shadows/directional_shadow/16_bits").AsBool();
        // Sky3D can re-enable shadows when its light energy changes. Enforce the diagnostic
        // immediately before rendering and positively verify the native flags.
        RenderingServer.FramePreDraw += EnforceShadowControl;
        root.TreeExiting += () => RenderingServer.FramePreDraw -= EnforceShadowControl;
        GD.Print($"FRONTIER DIAGNOSTIC sweep: {_trees.Length} tree meshes, {_terrain.Length} native terrains, {_lights.Length} lights, {_effects.Length} compositor effects");
    }

    public void Apply(int index)
    {
        foreach (var (node, visible) in _scatter.Concat(_trees).Concat(_terrain)) node.Visible = visible;
        foreach (var (light, shadows) in _lights) if (GodotObject.IsInstanceValid(light)) light.ShadowEnabled = shadows;
        foreach (var (effect, enabled) in _effects) effect.Enabled = enabled;
        _hideShadows = _shadowSweep ? index == 6 : index == 4;
        _shadowControlLogged = false;
        if (_shadowSweep)
        {
            var (size, filter) = index switch
            {
                1 => (4096, RenderingServer.ShadowQuality.SoftUltra),
                2 => (4096, RenderingServer.ShadowQuality.SoftHigh),
                3 => (4096, RenderingServer.ShadowQuality.SoftMedium),
                4 => (8192, RenderingServer.ShadowQuality.SoftHigh),
                5 => (2048, RenderingServer.ShadowQuality.SoftHigh),
                _ => (_atlasSize, _filter),
            };
            RenderingServer.DirectionalShadowAtlasSetSize(size, _depth16);
            RenderingServer.DirectionalSoftShadowFilterSetQuality(filter);
            var positional = index is 2 or 3 or 4 or 5 ? filter : _positionalFilter;
            RenderingServer.PositionalSoftShadowFilterSetQuality(positional);
            GD.Print($"FRONTIER DIAGNOSTIC shadow settings: {Name(index)} atlas={size} directional={filter} positional={positional} depth16={_depth16}");
        }
        else
        {
            if (index is 1 or 6) foreach (var (node, _) in _scatter) node.Visible = false;
            if (index is 2 or 6) foreach (var (node, _) in _trees) node.Visible = false;
            if (index == 3) foreach (var (node, _) in _terrain) node.Visible = false;
            if (index == 4) foreach (var (light, _) in _lights) if (GodotObject.IsInstanceValid(light)) light.ShadowEnabled = false;
            if (index == 5) foreach (var (effect, _) in _effects) effect.Enabled = false;
        }
        GD.Print($"FRONTIER DIAGNOSTIC sweep stage: {Name(index)}");
    }

    private void EnforceShadowControl()
    {
        if (!_hideShadows) return;
        foreach (var (light, _) in _lights)
            if (GodotObject.IsInstanceValid(light)) light.ShadowEnabled = false;
        if (_shadowControlLogged) return;
        int active = _lights.Count(l => GodotObject.IsInstanceValid(l.Light) && l.Light.ShadowEnabled);
        if (active != 0) throw new InvalidOperationException("No-shadow diagnostic failed to disable native shadow flags");
        GD.Print($"FRONTIER DIAGNOSTIC no-shadow native control: {active} enabled lights immediately before draw");
        _shadowControlLogged = true;
    }
}
