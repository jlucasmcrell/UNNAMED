using Godot;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Presentation.Greybox;

// Presentation of the existing circular barrier; no collision or saved state.
public sealed partial class FoldWard : Node3D
{
    private ShaderMaterial _wall = null!, _ring = null!;
    private StandardMaterial3D _crown = null!;
    private float _radius;
    private double _clock, _pulseAt = -10;
    private bool _near, _touch;
    public Vector3 PlayerFeet { get; set; }

    public static FoldWard Build(MeshInstance3D wall, CircleBlocker circle, TerrainGrid terrain)
    {
        var ward = new FoldWard { Name = "WardRunes", _wall = (ShaderMaterial)wall.MaterialOverride,
            _radius = circle.RadiusMm / 1000f, PlayerFeet = new Vector3(float.MaxValue, 0, 0) };
        ward._ring = new ShaderMaterial { Shader = new Shader { Code = RingCode } };
        var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        const int segments = 192;
        void Vertex(int i, float width)
        {
            float angle = i * Mathf.Tau / segments, radius = ward._radius + width;
            float x = circle.CenterXMm / 1000f + Mathf.Cos(angle) * radius;
            float z = circle.CenterZMm / 1000f + Mathf.Sin(angle) * radius;
            surface.SetUV(new Vector2(i / (float)segments, (width + .22f) / .44f));
            surface.SetNormal(Vector3.Up);
            surface.AddVertex(new Vector3(x, terrain.HeightAtMm((long)(x*1000),(long)(z*1000))/1000f+.065f,z)-wall.Position);
        }
        for (int i=0;i<segments;i++)
        {
            Vertex(i,-.22f);Vertex(i+1,-.22f);Vertex(i+1,.22f);
            Vertex(i,-.22f);Vertex(i+1,.22f);Vertex(i,.22f);
        }
        ward.AddChild(new MeshInstance3D { Name="GroundRuneRing", Mesh=surface.Commit(), MaterialOverride=ward._ring,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,GIMode=GeometryInstance3D.GIModeEnum.Disabled });
        ward._crown = new StandardMaterial3D { ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency=BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor=new Color(.3f,.55f,1,.65f),
            EmissionEnabled=true,Emission=new Color(.3f,.55f,1),EmissionEnergyMultiplier=1.8f };
        ward.AddChild(new MeshInstance3D { Name="FoldBoundaryCrown",
            Mesh=new TorusMesh { InnerRadius=ward._radius-.025f,OuterRadius=ward._radius+.025f,Rings=96,RingSegments=8 },
            Position=Vector3.Up*(circle.HeightMm/2000f),MaterialOverride=ward._crown,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,GIMode=GeometryInstance3D.GIModeEnum.Disabled });
        return ward;
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        float distance = new Vector2(PlayerFeet.X-GlobalPosition.X, PlayerFeet.Z-GlobalPosition.Z).Length();
        bool near = distance < _radius+4, touch = distance < _radius+.7f;
        if ((near && !_near) || (touch && !_touch)) _pulseAt=_clock;
        _near=near;_touch=touch;
        float reaction=Mathf.Exp(-(float)(_clock-_pulseAt)*1.7f);
        float proximity=Mathf.Clamp(1-(distance-_radius)/8,0,1);
        _wall.SetShaderParameter("reaction",reaction);
        _wall.SetShaderParameter("proximity",proximity);
        _ring.SetShaderParameter("presence",_wall.GetShaderParameter("presence"));
        _ring.SetShaderParameter("reaction",reaction);
        float presence=_wall.GetShaderParameter("presence").AsSingle();
        _crown.AlbedoColor=_crown.AlbedoColor with { A=presence*(.55f+.2f*Mathf.Sin((float)_clock*2.5f)+reaction*.2f) };
    }

    private const string RingCode = @"
shader_type spatial;
render_mode unshaded, cull_disabled, blend_add, depth_draw_never, shadows_disabled;
uniform float presence=1.0;
uniform float reaction=0.0;
void fragment() {
    float radial=UV.y;
    float lines=1.0-smoothstep(.025,.07,min(abs(radial-.12),abs(radial-.88)));
    vec2 p=vec2(fract(UV.x*32.0),radial);
    float stem=(1.0-smoothstep(.035,.075,abs(p.x-.5)))*step(.25,p.y)*step(p.y,.75);
    float slash=(1.0-smoothstep(.035,.075,abs(p.y-(p.x*.7+.18))))*step(.24,p.x)*step(p.x,.76);
    float rune=max(stem,slash)*smoothstep(.15,.25,radial)*(1.0-smoothstep(.75,.85,radial));
    float pulse=.78+.22*sin(TIME*2.5-UV.x*6.283);
    ALBEDO=vec3(.42,.66,1.0)*(1.5+reaction*2.2);
    ALPHA=max(lines,rune)*pulse*presence*.85;
}
";
}
