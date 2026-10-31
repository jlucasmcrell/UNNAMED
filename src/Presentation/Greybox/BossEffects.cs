using Godot;
using UNNAMED.Domain.Combat;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Ui.Models;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

/// <summary>Read-only tells from the live move, phase and weak-point window. No combat state is changed.</summary>
public partial class BossEffects : Node3D
{
    private readonly MeshInstance3D _floor = Carrier("AttackFootprint");
    private readonly MeshInstance3D _wave = Carrier("PhaseGroundWave");
    private readonly ShaderMaterial _tell = new() { Shader = new Shader { Code = TellShader } };
    private readonly ShaderMaterial _pulse = new() { Shader = new Shader { Code = PulseShader } };
    private MeshInstance3D _weak = null!;
    private GpuParticles3D? _dust;
    private CreatureDefinition _definition = null!;
    private CreatureBody _figure = null!;
    private Vector3 _weakAt;
    private Skeleton3D? _weakRig;
    private int _weakBone;
    private Vector3 _weakOffset;
    private string? _phase;
    private bool _fighting;
    private float _pulseAge = 3;
    private long _tick = -1;

    public static BossEffects Create(CreatureDefinition definition, CreatureBody figure, ParticleRecipes recipes, CreatureArt? look = null)
    {
        var effect = new BossEffects { Name = "BossTells:" + definition.Id, _definition = definition, _figure = figure };
        effect._floor.MaterialOverride = effect._tell;
        effect._wave.MaterialOverride = effect._pulse;
        effect.AddChild(effect._floor);
        effect.AddChild(effect._wave);
        // Bounds are read from the actual body. The tell lies on the named weak region,
        // on the back when content says from_behind, rather than glowing the whole creature.
        var box = ArtGallery.Bounds(figure);
        float height = Math.Max(.5f, box.Size.Y);
        float y = definition.WeakPoint?.Region == BodyRegion.Head ? .86f : .55f;
        bool behind = definition.WeakPoint?.FromBehind == true;
        effect._weakAt = new Vector3(box.GetCenter().X, box.Position.Y + height * y,
            behind ? box.Position.Z - .05f : box.End.Z + .05f);
        float radius = Mathf.Clamp(height * .065f, .13f, .4f);
        if (look?.WeakPointBone is { } bone)
        {
            effect._weakRig = figure.FindChildren("*", nameof(Skeleton3D), true, false).OfType<Skeleton3D>()
                .FirstOrDefault(rig => rig.FindBone(bone) >= 0)
                ?? throw new InvalidDataException($"{definition.Id}: native weak-point bone '{bone}' is missing");
            if (!float.IsFinite(look.WeakPointRadius) || look.WeakPointRadius <= 0 || !look.WeakPointOffset.IsFinite())
                throw new InvalidDataException($"{definition.Id}: invalid native weak-point cue dimensions");
            effect._weakBone = effect._weakRig.FindBone(bone);
            effect._weakOffset = look.WeakPointOffset;
            radius = look.WeakPointRadius;
        }
        effect._weak = Carrier("OpenWeakPoint");
        effect._weak.Mesh = new TorusMesh { InnerRadius = radius * .76f, OuterRadius = radius, Rings = 32, RingSegments = 8 };
        effect._weak.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, .83f, .42f), EmissionEnabled = true,
            Emission = new Color(1f, .66f, .2f), EmissionEnergyMultiplier = 1.1f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        effect.AddChild(effect._weak);
        // Existing full-quality recipe, not a second particle implementation. These
        // three fights are physical: violet remains reserved for the Other.
        effect._dust = recipes.Build("dust_kick");
        if (effect._dust is { } dust)
        {
            dust.Emitting = false;
            dust.Scale = Vector3.One * Mathf.Clamp(definition.RadiusMm / 1000f, .8f, 2.5f);
            effect.AddChild(dust);
        }
        return effect;
    }

    public void Draw(CreatureView view, CombatConstants constants, long targetRadiusMm,
        long tick, double delta, int tickMilliseconds, Func<float, float, float> ground)
    {
        Visible = view.Alive && _figure.Visible;
        if (!Visible) return;
        _pulseAge += (float)delta;
        bool fighting = view.Hostile || view.Health < view.MaxHealth;
        bool phaseChanged = _phase is not null && _phase != view.BossPhase;
        if (fighting && (!_fighting || phaseChanged))
        {
            _pulseAge = 0;
            if (_dust is { } dust) { dust.GlobalPosition = _figure.GlobalPosition; ParticleRecipes.Fire(dust); }
        }
        _fighting = fighting;
        _phase = view.BossPhase;
        var tell = BossTelegraphModel.Of(view, _definition, constants, targetRadiusMm, tick, tickMilliseconds);
        _floor.Visible = tell is not null;
        _wave.Visible = _pulseAge < 2.2f;
        _weak.Visible = fighting && view.WeakPointOpen && _definition.WeakPoint is not null;
        if (_weakRig is { } rig)
        {
            var bone = rig.GlobalTransform * rig.GetBoneGlobalPose(_weakBone);
            _weak.GlobalTransform = new Transform3D(bone.Basis * new Basis(Vector3.Right, Mathf.Pi / 2), bone * _weakOffset);
        }
        else
        {
            _weak.GlobalPosition = _figure.ToGlobal(_weakAt);
            _weak.Rotation = new Vector3(Mathf.Pi / 2, _figure.Rotation.Y, 0);
        }
        if (tell is not null)
        {
            _tell.SetShaderParameter("progress", tell.Progress);
            _tell.SetShaderParameter("pattern", (int)tell.Pattern);
            _tell.SetShaderParameter("lane", tell.Lane);
            _tell.SetShaderParameter("ink", tell.Active ? new Vector3(1f, .24f, .08f) : new Vector3(1f, .65f, .16f));
        }
        _pulse.SetShaderParameter("age", _pulseAge / 2.2f);
        // Conform to the same terrain field as the fight; do not project through a
        // terrace or paint a flat quad across the cave floor. The simulation tick
        // bounds rebuilding, while the shader continues smoothly between ticks.
        if (_tick == tick) return;
        _tick = tick;
        Vector3 feet = _figure.GlobalPosition;
        float yaw = (tell?.FacingMdeg ?? view.Body.FacingMdeg) / 1000f * Mathf.Pi / 180;
        if (tell is not null)
            _floor.Mesh = Ground(feet, yaw, tell.ReachM, tell.ArcDegrees, tell.Lane ? tell.HalfWidthM : 0, .12f, ground);
        if (_wave.Visible)
            _wave.Mesh = Ground(feet, 0, Math.Max(2, _definition.RadiusMm / 1000f * 2.8f), 360, 0, .15f, ground);
    }

    private static MeshInstance3D Carrier(string name) => new()
    {
        Name = name, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        GIMode = GeometryInstance3D.GIModeEnum.Disabled,
    };

    internal static ArrayMesh Ground(Vector3 feet, float yaw, float reach, float arc, float laneWidth,
        float lift, Func<float, float, float> ground)
    {
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        int along = laneWidth > 0 ? 64 : 24;
        const int across = 4;
        void Vertex(int x, int y)
        {
            float u = x / (float)along, v = y / (float)across;
            float angle = yaw + (u - .5f) * Mathf.DegToRad(arc);
            Vector3 offset;
            Vector2 uv = new(u, v);
            if (laneWidth > 0)
            {
                // Sweep the actual contact disk along only the remaining travel.
                // Rounded ends include bodies beside/behind the charger, rather
                // than drawing a fresh full-length rectangle ahead every tick.
                float travel = Math.Max(0, reach - laneWidth);
                float theta = u * Mathf.Tau;
                var middle = new Vector3(0, 0, travel / 2);
                var edge = new Vector3(Mathf.Sin(theta) * laneWidth, 0,
                    Mathf.Cos(theta) * laneWidth + (Mathf.Cos(theta) >= 0 ? travel : 0));
                var local = middle.Lerp(edge, v);
                uv = new Vector2((local.X / laneWidth + 1) / 2, (local.Z + laneWidth) / (travel + 2 * laneWidth));
                offset = new Basis(Vector3.Up, yaw) * local;
            }
            else offset = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * v * reach;
            Vector3 p = feet + offset;
            p.Y = ground(p.X, p.Z) + lift;
            surface.SetNormal(Vector3.Up);
            surface.SetUV(uv);
            surface.SetUV2(new Vector2(u, v));
            surface.AddVertex(p);
        }
        for (int x = 0; x < along; x++)
        for (int y = 0; y < across; y++)
        {
            Vertex(x, y); Vertex(x + 1, y); Vertex(x + 1, y + 1);
            Vertex(x, y); Vertex(x + 1, y + 1); Vertex(x, y + 1);
        }
        return surface.Commit();
    }

    private const string TellShader = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never;
        uniform vec3 ink = vec3(1.0,.65,.16);
        uniform float progress = 0.0;
        uniform int pattern = 0;
        uniform bool lane = false;
        void fragment() {
            float border = smoothstep(.93,.98,UV2.y);
            if (!lane) border = max(border,1.0-smoothstep(.018,.04,min(UV2.x,1.0-UV2.x)));
            float mark = 0.0;
            if (pattern == 0) mark = step(.82,fract(UV.x*12.0))*step(.78,UV.y);
            else if (pattern == 1) mark = step(.91,fract(UV.y*4.0))*step(.3,UV.x)*step(UV.x,.7);
            else if (pattern == 2) mark = step(.85,fract(UV.x*3.0))*step(.45,UV.y);
            else if (pattern == 3) mark = step(.88,fract(UV.x*2.0))*step(.5,UV.y);
            else mark = (1.0-smoothstep(.025,.055,abs(UV.x-.5)))*step(.25,fract(UV.y*5.0));
            float fill = (.09+.12*step(UV.y,progress))*smoothstep(.03,.16,UV.y);
            ALBEDO = ink;
            EMISSION = ink*(.5+.6*progress);
            ALPHA = max(fill,max(border,mark)*(.55+.3*progress));
        }
        """;

    private const string PulseShader = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never;
        uniform float age = 1.0;
        void fragment() {
            float ring = 1.0-smoothstep(.018,.045,abs(UV.y-age));
            float ticks = step(.88,fract(UV.x*24.0))*step(.82,UV.y);
            vec3 chalk = vec3(.92,.78,.48);
            ALBEDO = chalk;
            EMISSION = chalk*.8;
            ALPHA = max(ring,ticks*.45)*(1.0-age)*.8;
        }
        """;
}
