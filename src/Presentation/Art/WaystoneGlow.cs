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
        void vertex() { p = VERTEX; VERTEX += NORMAL * 0.002; }
        void fragment() {
            float vein = abs(sin(p.y * 19.0 + sin(p.x * 17.0 + p.z * 13.0) * 2.1));
            float notches = step(0.78, sin(p.y * 37.0)) * step(0.75, cos(p.x * 21.0 + p.z * 23.0));
            float line = max(1.0-smoothstep(0.015, 0.06, vein), notches * 0.55);
            float pulse = 0.65 + 0.35 * sin(TIME * 1.04719755);
            ALBEDO = vec3(0.55, 0.8, 1.0); EMISSION = ALBEDO * pulse * awake * (0.65 + travel_pulse * 2.0);
            ALPHA = line * awake * pulse * min(1.0, 0.6 + travel_pulse);
        }
        """ } };
    private readonly OmniLight3D _light = new() { OmniRange = 4, LightEnergy = 0,
        LightColor = new Color(.68f,.82f,1), ShadowEnabled = false };
    private CpuParticles3D _motes = null!;
    private Tween? _fade;
    private bool? _awake;

    public void Build(Node3D model)
    {
        foreach(var mesh in model.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>()
            .Where(m=>m.Mesh is not null && m.VisibilityRangeBegin<=0))
            AddChild(new MeshInstance3D { Mesh=mesh.Mesh,Transform=ArtLibrary.Relative(model,mesh),
                MaterialOverride=_veins,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode=GeometryInstance3D.GIModeEnum.Disabled });
        AddChild(_light); _light.Position=Vector3.Up;
        var material=new StandardMaterial3D { ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor=new Color(.65f,.8f,1),EmissionEnabled=true,Emission=new Color(.5f,.7f,1),
            EmissionEnergyMultiplier=.4f,Transparency=BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo=true };
        var ramp=new Gradient(); ramp.SetColor(0,new Color(1,1,1,0));
        ramp.AddPoint(.2f,Colors.White);ramp.SetColor(1,new Color(1,1,1,0));
        _motes=new CpuParticles3D { Name="WaystoneMotes",Amount=20,Lifetime=6,
            EmissionShape=CpuParticles3D.EmissionShapeEnum.Sphere,EmissionSphereRadius=.65f,
            Position=Vector3.Up*.6f,Direction=Vector3.Up,Spread=12,InitialVelocityMin=.08f,
            InitialVelocityMax=.16f,Gravity=Vector3.Zero,ScaleAmountMin=.008f,ScaleAmountMax=.016f,
            Mesh=new SphereMesh { Radius=1,Height=2 },MaterialOverride=material,ColorRamp=ramp,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,GIMode=GeometryInstance3D.GIModeEnum.Disabled,Emitting=false };
        AddChild(_motes);
    }

    public void TravelPulse(float intensity)
    {
        var tween=CreateTween();
        tween.TweenMethod(Callable.From<float>(value=>_veins.SetShaderParameter("travel_pulse",value)),intensity,0f,.8);
    }

    public void SetAwake(bool awake, bool animate)
    {
        if(_awake==awake)return;
        _awake=awake;_fade?.Kill();_motes.Emitting=awake;
        void Apply(float value) { _veins.SetShaderParameter("awake",value);_light.LightEnergy=.17f*value; }
        if(animate) { _fade=CreateTween();_fade.TweenMethod(Callable.From<float>(Apply),awake?0f:1f,awake?1f:0f,1.6); }
        else Apply(awake?1:0);
    }
}
