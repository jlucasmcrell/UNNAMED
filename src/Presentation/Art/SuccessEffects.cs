using Godot;
using UNNAMED.Presentation.Settings;

namespace UNNAMED.Presentation.Art;

/// <summary>Short, presentation-only responses. No collision, shadows or persistent lights.</summary>
public partial class SuccessEffects : Node3D
{
    private Application.GameSession? _session;
    private readonly Dictionary<string,WaystoneGlow> _stones=new();
    private double _stoneRefresh;
    public void BindWaystones(Application.GameSession session, Greybox.HollowView hollow)
    {
        _session=session;
        foreach(var site in session.Setup.Layout.Waystones)
        {
            if(hollow.PlacedModel(site.Body.Id) is not { } model)continue;
            var glow=new WaystoneGlow { Name="WaystoneAmbient" };glow.Build(model);model.AddChild(glow);
            _stones[site.Key]=glow;
        }
        RefreshStones(false);
    }
    public override void _Process(double delta)
    {
        _stoneRefresh-=delta;
        if(_stoneRefresh<=0) { _stoneRefresh=.25;RefreshStones(true); }
    }
    private void RefreshStones(bool animate)
    {
        if(_session?.Simulation is not { } simulation)return;
        foreach(var view in simulation.Waystones)
            if(_stones.TryGetValue(view.Site.Key,out var glow))glow.SetAwake(view.Awake,animate);
    }

    public void Waystone(Vector3 stone, Vector3 player, bool repeat)
    {
        float seconds = repeat ? .8f : 1.6f;
        var root = new Node3D { Name = "WaystoneResponse" }; AddChild(root); root.GlobalPosition = stone;
        var tone = new Color(.5f, .78f, .85f);
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.Tau / 6;
            var rune = new MeshInstance3D {
                Mesh = new PrismMesh { Size = new Vector3(.045f, .16f, .045f) },
                Position = new Vector3(Mathf.Sin(angle) * .55f, .3f, Mathf.Cos(angle) * .55f),
                Rotation = new Vector3(0, angle, .2f), MaterialOverride = Glow(tone),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                Visible = false };
            root.AddChild(rune);
            root.CreateTween().TweenCallback(Callable.From(() => rune.Visible = true)).SetDelay(i * seconds / 12);
        }
        var light = new OmniLight3D { OmniRange = 2.5f, LightColor = tone, LightEnergy = 0, ShadowEnabled = false };
        root.AddChild(light); light.Position = Vector3.Up;
        var pulse = root.CreateTween();
        pulse.TweenProperty(light, "light_energy", FlashScale * .55f, seconds * .3);
        pulse.TweenProperty(light, "light_energy", 0f, seconds * .7);
        Burst(player + Vector3.Up * .15f, tone, false, repeat ? 12 : 24, seconds);
        GetTree().CreateTimer(seconds).Timeout += root.QueueFree;
    }

    public void Travel(string key,Vector3 at,bool arriving)
    {
        if(_stones.TryGetValue(key,out var glow))glow.TravelPulse(FlashScale);
        var swirl=new Node3D { Name=arriving?"WaystoneArrival":"WaystoneDeparture" };
        AddChild(swirl);swirl.GlobalPosition=at;
        var material=new StandardMaterial3D {
            AlbedoColor=new Color(.08f,.6f,.85f),EmissionEnabled=true,Emission=new Color(.08f,.6f,.85f),
            EmissionEnergyMultiplier=6*FlashScale,Transparency=BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode=BaseMaterial3D.BlendModeEnum.Add,VertexColorUseAsAlbedo=true };
        var ramp=new Gradient();ramp.SetColor(0,new Color(1,1,1,0));ramp.AddPoint(.15f,Colors.White);ramp.SetColor(1,new Color(1,1,1,0));
        for(int arm=0;arm<2;arm++)
        {
            var rotor=new Node3D { Rotation=new Vector3(0,arm*Mathf.Pi,0) };swirl.AddChild(rotor);
            var particles=new CpuParticles3D { Amount=72,Lifetime=1.4,OneShot=true,Explosiveness=.05f,
                Position=new Vector3(.75f,arriving?1.8f:.15f,0),LocalCoords=false,
                EmissionShape=CpuParticles3D.EmissionShapeEnum.Sphere,EmissionSphereRadius=.045f,
                Direction=arriving?Vector3.Down:Vector3.Up,Spread=7,Gravity=Vector3.Zero,
                InitialVelocityMin=1.2f,InitialVelocityMax=1.8f,ScaleAmountMin=.025f,ScaleAmountMax=.045f,
                Mesh=new SphereMesh { Radius=1,Height=2 },MaterialOverride=material,ColorRamp=ramp,
                CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,GIMode=GeometryInstance3D.GIModeEnum.Disabled,Emitting=false };
            rotor.AddChild(particles);particles.Emitting=true;
            rotor.CreateTween().TweenProperty(rotor,"rotation:y",arm*Mathf.Pi+(arriving?-1:1)*Mathf.Tau*1.7f,1.4);
            if(arriving)particles.CreateTween().TweenProperty(particles,"position:x",.1f,1.4);
        }
        var light=new OmniLight3D { Position=Vector3.Up,OmniRange=4,LightColor=new Color(.25f,.75f,1),
            LightEnergy=0,LightIndirectEnergy=0,ShadowEnabled=false };
        swirl.AddChild(light);
        var pulse=swirl.CreateTween();pulse.TweenProperty(light,"light_energy",2.5f*FlashScale,.4);
        pulse.TweenProperty(light,"light_energy",0f,1.4);
        GetTree().CreateTimer(3).Timeout+=swirl.QueueFree;
    }

    public void Craft(Vector3 at, bool metal, int quality) => Burst(at, metal ? new Color(1,.62f,.23f) : new Color(.72f,.65f,.49f),
        metal, 12 + Mathf.Clamp(quality, 0, 2) * 8, 1.2f + Mathf.Clamp(quality, 0, 2) * .2f);

    private static float FlashScale => SettingsStore.Current.Profile.Camera.ReduceFlashes ? .3f : 1;
    private static StandardMaterial3D Glow(Color tone) => new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = tone * FlashScale, EmissionEnabled = true, Emission = tone, EmissionEnergyMultiplier = FlashScale * .4f,
        VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };

    private void Burst(Vector3 at, Color tone, bool sparks, int amount, float seconds)
    {
        var dust = new CpuParticles3D { Name = "SuccessMotes", Amount = amount, Lifetime = seconds, OneShot = true,
            Explosiveness = 1, Direction = Vector3.Up, Spread = 40, InitialVelocityMin = sparks ? .8f : .18f,
            InitialVelocityMax = sparks ? 1.5f : .45f, Gravity = Vector3.Down * (sparks ? 2 : -.05f),
            ScaleAmountMin = sparks ? .007f : .009f, ScaleAmountMax = sparks ? .015f : .018f,
            Mesh = new SphereMesh { Radius = 1, Height = 2 }, MaterialOverride = Glow(tone),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Emitting = false };
        var ramp = new Gradient(); ramp.SetColor(0, Colors.White); ramp.SetColor(1, new Color(1,1,1,0));
        dust.ColorRamp = ramp;
        AddChild(dust); dust.GlobalPosition = at; dust.Emitting = true;
        GetTree().CreateTimer(seconds + .1).Timeout += dust.QueueFree;
    }
}
