using Godot;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Presentation.Greybox;

// Presentation of the existing circular barrier; no collision or saved state.
public sealed partial class FoldWard : Node3D
{
    private ShaderMaterial _wall = null!, _ring = null!;
    private StandardMaterial3D _crown = null!;
    private Decal _decal = null!;
    private OmniLight3D _light = null!;
    private GpuParticles3D _motes = null!;
    private StandardMaterial3D _moteMaterial = null!;
    private float _radius;
    private double _clock, _pulseAt = -10;
    private bool _near, _touch;
    public Vector3 PlayerFeet { get; set; }

    public static FoldWard Build(MeshInstance3D wall, CircleBlocker circle, TerrainGrid terrain)
    {
        var ward = new FoldWard { Name = "WardRunes", _wall = (ShaderMaterial)wall.MaterialOverride,
            _radius = circle.RadiusMm / 1000f, PlayerFeet = new Vector3(float.MaxValue, 0, 0) };
        ward._ring = new ShaderMaterial { Shader = new Shader { Code = RingCode } };
        var runes = RuneTexture();
        ward._ring.SetShaderParameter("runes", runes);
        float extent = ward._radius + .6f;
        ward._decal = new Decal { Name="EmissiveRuneDecal", Size=new Vector3(extent*2,1.2f,extent*2),
            Position=Vector3.Down*(circle.HeightMm/2000f-.15f), TextureAlbedo=runes, TextureEmission=runes,
            AlbedoMix=0, EmissionEnergy=5, UpperFade=.1f, LowerFade=.1f };
        ward.AddChild(ward._decal);
        var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        const int segments = 192;
        void Vertex(int i, float width)
        {
            float angle = i * Mathf.Tau / segments, radius = ward._radius + width;
            float x = circle.CenterXMm / 1000f + Mathf.Cos(angle) * radius;
            float z = circle.CenterZMm / 1000f + Mathf.Sin(angle) * radius;
            surface.SetUV(new Vector2(Mathf.Cos(angle)*radius,Mathf.Sin(angle)*radius)/(extent*2)+Vector2.One*.5f);
            surface.SetNormal(Vector3.Up);
            surface.AddVertex(new Vector3(x, terrain.HeightAtMm((long)(x*1000),(long)(z*1000))/1000f+.09f,z)-wall.Position);
        }
        for (int i=0;i<segments;i++)
        {
            Vertex(i,-.6f);Vertex(i+1,-.6f);Vertex(i+1,.6f);
            Vertex(i,-.6f);Vertex(i+1,.6f);Vertex(i,.6f);
        }
        // Terrain3D uses its own terrain shader. This conforming carrier keeps the
        // same decal artwork visible there too, without changing terrain buffers.
        ward.AddChild(new MeshInstance3D { Name="GroundRuneRing", Mesh=surface.Commit(), MaterialOverride=ward._ring,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,GIMode=GeometryInstance3D.GIModeEnum.Disabled });
        ward._crown = new StandardMaterial3D { ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency=BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor=new Color(.6f,1.4f,2.5f,.8f),
            EmissionEnabled=true,Emission=new Color(.25f,.6f,1),EmissionEnergyMultiplier=4 };
        ward.AddChild(new MeshInstance3D { Name="FoldBoundaryCrown",
            Mesh=new TorusMesh { InnerRadius=ward._radius-.045f,OuterRadius=ward._radius+.045f,Rings=96,RingSegments=8 },
            Position=Vector3.Up*(circle.HeightMm/2000f),MaterialOverride=ward._crown,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,GIMode=GeometryInstance3D.GIModeEnum.Disabled });
        ward._light = new OmniLight3D { Name="WardSoftLight", Position=Vector3.Up*.2f,
            LightColor=new Color(.35f,.65f,1),LightEnergy=2,OmniRange=7,OmniAttenuation=1.5f,ShadowEnabled=false };
        ward.AddChild(ward._light);
        var glow=Image.CreateEmpty(32,32,false,Image.Format.Rgba8);
        for(int y=0;y<32;y++) for(int x=0;x<32;x++)
        {
            float r=new Vector2((x-15.5f)/16,(y-15.5f)/16).Length();
            float strength=Mathf.Pow(Mathf.Max(0,1-r),2);
            glow.SetPixel(x,y,new Color(.55f*strength,.8f*strength,strength,strength));
        }
        var moteTexture=ImageTexture.CreateFromImage(glow);
        ward._moteMaterial=new StandardMaterial3D { ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency=BaseMaterial3D.TransparencyEnum.Alpha,BlendMode=BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode=BaseMaterial3D.BillboardModeEnum.Particles,AlbedoTexture=moteTexture,EmissionTexture=moteTexture,
            AlbedoColor=new Color(3,3,3,1),VertexColorUseAsAlbedo=true,EmissionEnabled=true,
            Emission=new Color(.3f,.65f,1),EmissionEnergyMultiplier=3 };
        ward._motes=new GpuParticles3D { Name="DriftingWardMotes", Amount=80,Lifetime=7,Preprocess=7,
            LocalCoords=true,Position=Vector3.Down*(circle.HeightMm/2000f-.15f),
            VisibilityAabb=new Aabb(new Vector3(-4,-1,-4),new Vector3(8,6,8)),
            ProcessMaterial=new ParticleProcessMaterial { EmissionShape=ParticleProcessMaterial.EmissionShapeEnum.Ring,
                EmissionRingAxis=Vector3.Up,EmissionRingRadius=ward._radius,EmissionRingInnerRadius=ward._radius-.4f,
                EmissionRingHeight=.4f,Direction=Vector3.Up,Spread=18,Gravity=Vector3.Zero,
                InitialVelocityMin=.18f,InitialVelocityMax=.42f,ScaleMin=.45f,ScaleMax=1,
                ColorRamp=new GradientTexture1D { Gradient=new Gradient { Colors=new[] {
                    new Color(1,1,1,0),Colors.White,Colors.White,new Color(1,1,1,0) },Offsets=new[]{0f,.15f,.7f,1f} } } },
            DrawPass1=new QuadMesh { Size=new Vector2(.12f,.12f),Material=ward._moteMaterial },
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,GIMode=GeometryInstance3D.GIModeEnum.Disabled };
        ward.AddChild(ward._motes);
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
        float breathing=.88f+.12f*Mathf.Sin((float)_clock*2.5f);
        _decal.EmissionEnergy=(5*breathing+reaction*7)*presence;
        _decal.Modulate=new Color(1,1,1,presence);
        _light.LightEnergy=(2*breathing+reaction*1.5f)*presence;
        _light.Visible=Visible && presence>.001f;
        _motes.Visible=presence>.001f;
        _moteMaterial.AlbedoColor=new Color(3,3,3,presence);
        _crown.AlbedoColor=_crown.AlbedoColor with { A=presence*(.55f+.2f*Mathf.Sin((float)_clock*2.5f)+reaction*.2f) };
    }

    private static Texture2D RuneTexture()
    {
        const int size=512;
        var image=Image.CreateEmpty(size,size,true,Image.Format.Rgba8);
        for(int y=0;y<size;y++) for(int x=0;x<size;x++)
        {
            var p=new Vector2((x+.5f)/size-.5f,(y+.5f)/size-.5f)*7.2f;
            float radius=p.Length(),angle=Mathf.PosMod(Mathf.Atan2(p.Y,p.X),Mathf.Tau);
            float lines=1-Mathf.SmoothStep(.025f,.065f,Mathf.Min(Mathf.Abs(radius-2.55f),Mathf.Abs(radius-3.45f)));
            var q=new Vector2(Mathf.PosMod(angle*24/Mathf.Tau,1), (radius-2.55f)/.9f);
            float stem=(1-Mathf.SmoothStep(.035f,.08f,Mathf.Abs(q.X-.5f)))*(q.Y>.2f&&q.Y<.8f?1:0);
            float slash=(1-Mathf.SmoothStep(.035f,.08f,Mathf.Abs(q.Y-(q.X*.7f+.18f))))*(q.X>.2f&&q.X<.8f?1:0);
            float rune=Mathf.Max(stem,slash)*(q.Y>.12f&&q.Y<.88f?1:0);
            float alpha=Mathf.Max(lines,rune);
            // Decal emission samples RGB independently of albedo alpha.
            image.SetPixel(x,y,new Color(.3f*alpha,.7f*alpha,alpha,alpha));
        }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private const string RingCode = @"
shader_type spatial;
render_mode unshaded, cull_disabled, blend_add, depth_draw_never, shadows_disabled;
uniform float presence=1.0;
uniform float reaction=0.0;
uniform sampler2D runes : source_color, filter_linear_mipmap;
void fragment() {
    vec4 rune=texture(runes,UV);
    float pulse=.88+.12*sin(TIME*2.5);
    ALBEDO=vec3(0.0);
    EMISSION=rune.rgb*(5.0+reaction*7.0);
    ALPHA=rune.a*pulse*presence;
}
";
}
