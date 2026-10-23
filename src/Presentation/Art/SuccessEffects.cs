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
        var particles=new CpuParticles3D { Name=arriving?"WaystoneArrival":"WaystoneDeparture",
            Amount=20,Lifetime=.65,OneShot=true,Explosiveness=1,
            EmissionShape=CpuParticles3D.EmissionShapeEnum.Sphere,EmissionSphereRadius=arriving?.12f:.8f,
            Gravity=Vector3.Zero,InitialVelocityMin=0,InitialVelocityMax=0,
            RadialAccelMin=arriving?2.5f:-3f,RadialAccelMax=arriving?2.5f:-3f,
            ScaleAmountMin=.008f,ScaleAmountMax=.016f,Mesh=new SphereMesh { Radius=1,Height=2 },
            MaterialOverride=Glow(new Color(.6f,.8f,1)),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode=GeometryInstance3D.GIModeEnum.Disabled,Emitting=false };
        AddChild(particles);particles.GlobalPosition=at+Vector3.Up;particles.Emitting=true;
        GetTree().CreateTimer(.8).Timeout+=particles.QueueFree;
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
