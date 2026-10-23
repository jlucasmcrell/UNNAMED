using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>Surface veins and a small pool of rising motes; animation runs on shader TIME.</summary>
public partial class WaystoneGlow : Node3D
{
    private readonly ShaderMaterial _veins = new() { Shader = new Shader { Code = """
        shader_type spatial;
        render_mode unshaded, blend_add, depth_draw_never, cull_back;
        uniform float awake = 0.0;
        uniform float travel_pulse = 0.0;
        varying vec3 p;
        vec3 cell_hash(vec3 n) {
            return fract(sin(vec3(dot(n,vec3(127.1,311.7,74.7)),dot(n,vec3(269.5,183.3,246.1)),dot(n,vec3(113.5,271.9,124.6))))*43758.5453);
        }
        float seam(vec3 at) {
            vec3 cell=floor(at), f=fract(at);
            float first=100.0, second=100.0;
            for(int z=-1;z<=1;z++) for(int y=-1;y<=1;y++) for(int x=-1;x<=1;x++) {
                vec3 offset=vec3(float(x),float(y),float(z));
                vec3 d=offset+cell_hash(cell+offset)-f;
                float distance_=dot(d,d);
                if(distance_<first) { second=first;first=distance_; }
                else { second=min(second,distance_); }
            }
            return sqrt(second)-sqrt(first);
        }
        void vertex() { p = (MODEL_MATRIX*vec4(VERTEX,1.0)).xyz; VERTEX += NORMAL * 0.002; }
        void fragment() {
            // Continuous mineral-like seams, rather than opaque white dashed paint.
            float edge=seam(p*2.0+vec3(sin(p.y*2.1)*0.15,0.0,cos(p.x*1.8)*0.15));
            float core=exp(-pow(edge/0.018,2.0));
            float fringe=exp(-edge*28.0)*0.12;
            float pulse=0.82+0.18*sin(TIME*1.25663706);
            ALBEDO=vec3(0.0);
            // HDR emission crosses the world's existing bloom threshold.
            EMISSION=vec3(0.08,0.58,0.72)*(4.5+travel_pulse*8.0)*pulse;
            ALPHA=(core+fringe)*awake;
        }
        """ } };
    private readonly OmniLight3D _light = new() { OmniRange = 4, LightEnergy = 0,
        LightColor = new Color(.3f,.78f,.86f), LightIndirectEnergy=0, ShadowEnabled = false };
    private CpuParticles3D _motes = null!;
    private Tween? _fade;
    private bool? _awake;
    private float _amount,_phase;

    public override void _Process(double delta)
    {
        _phase+=(float)delta;
        _light.LightEnergy=.7f*_amount*(.85f+.15f*Mathf.Sin(_phase*Mathf.Tau/5));
    }

    public void Build(Node3D model)
    {
        foreach(var mesh in model.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>()
            .Where(m=>m.Mesh is not null && m.VisibilityRangeBegin<=0))
            AddChild(new MeshInstance3D { Mesh=mesh.Mesh,Transform=ArtLibrary.Relative(model,mesh),
                MaterialOverride=_veins,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode=GeometryInstance3D.GIModeEnum.Disabled });
        AddChild(_light); _light.Position=Vector3.Up;
        var material=new StandardMaterial3D { ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor=new Color(.12f,.65f,.8f),EmissionEnabled=true,Emission=new Color(.12f,.65f,.8f),
            EmissionEnergyMultiplier=3,Transparency=BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo=true };
        var ramp=new Gradient(); ramp.SetColor(0,new Color(1,1,1,0));
        ramp.AddPoint(.2f,Colors.White);ramp.SetColor(1,new Color(1,1,1,0));
        _motes=new CpuParticles3D { Name="WaystoneMotes",Amount=20,Lifetime=6,
            EmissionShape=CpuParticles3D.EmissionShapeEnum.Sphere,EmissionSphereRadius=.65f,
            Position=Vector3.Up*.6f,Direction=Vector3.Up,Spread=12,InitialVelocityMin=.08f,
            InitialVelocityMax=.16f,Gravity=Vector3.Zero,ScaleAmountMin=.012f,ScaleAmountMax=.025f,
            Mesh=new SphereMesh { Radius=1,Height=2 },MaterialOverride=material,ColorRamp=ramp,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,GIMode=GeometryInstance3D.GIModeEnum.Disabled,Emitting=false };
        AddChild(_motes);
    }

    public void TravelPulse(float intensity)
    {
        var tween=CreateTween();
        tween.TweenMethod(Callable.From<float>(value=>_veins.SetShaderParameter("travel_pulse",value)),intensity,0f,1.4);
    }

    public void SetAwake(bool awake, bool animate)
    {
        if(_awake==awake)return;
        _awake=awake;_fade?.Kill();_motes.Emitting=awake;
        void Apply(float value) { _amount=value;_veins.SetShaderParameter("awake",value); }
        if(animate) { _fade=CreateTween();_fade.TweenMethod(Callable.From<float>(Apply),awake?0f:1f,awake?1f:0f,1.6); }
        else Apply(awake?1:0);
    }
}
