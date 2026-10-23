using Godot;
using UNNAMED.Presentation.Settings;

namespace UNNAMED.Presentation;

public partial class Main
{
    private bool _waystoneTravelBusy, _waystoneArrived;

    private void TravelStonePulse(string key,bool arriving)
    {
        if(_session.Setup.Layout.FindWaystone(key) is not { } site)return;
        var at=arriving?new Vector3(site.ArrivalXMm/1000f,
            _session.Setup.Layout.Space.Terrain.HeightAtMm(site.ArrivalXMm,site.ArrivalZMm)/1000f,site.ArrivalZMm/1000f):_avatar.GlobalPosition;
        _successEffects.Travel(key,at,arriving);
    }

    private async void TravelWithEffects(string from, string to)
    {
        if(_waystoneTravelBusy)return;
        _waystoneTravelBusy=true;_waystoneArrived=false;
        bool gentle=SettingsStore.Current.Profile.Camera.ReduceFlashes;
        var layer=new CanvasLayer { Layer=90 };
        var material=new ShaderMaterial { Shader=new Shader { Code="""
            shader_type canvas_item;
            uniform sampler2D scene : hint_screen_texture, filter_linear;
            uniform float strength = 0.0;
            uniform bool gentle = false;
            uniform bool arriving = false;
            void fragment() {
                vec2 d=SCREEN_UV-vec2(0.5);
                float r=length(d);
                vec2 uv=SCREEN_UV + (gentle ? vec2(0.0) : d*sin(r*28.0-TIME*3.0)*strength*0.006);
                vec3 world=texture(scene,uv).rgb;
                float wash=strength*(gentle?0.9:0.85);
                if(arriving && !gentle)wash*=smoothstep(strength*0.75-0.12,strength*0.75+0.16,r);
                vec3 tone=gentle?vec3(0.025,0.035,0.05):vec3(0.63,0.79,0.9);
                vec2 stars=floor(SCREEN_UV*vec2(150.,85.)+vec2(TIME*0.4,-TIME*0.8));
                float seed=fract(sin(dot(stars,vec2(12.9898,78.233)))*43758.5453);
                float motes=step(0.9985,seed)*(0.5+0.5*sin(TIME*1.8+seed*19.));
                COLOR=vec4(mix(world,tone,wash)+vec3(0.3,0.4,0.5)*motes*strength,1.);
            }
            """ } };
        material.SetShaderParameter("gentle",gentle);
        var screen=new ColorRect { Material=material,MouseFilter=Control.MouseFilterEnum.Stop };
        AddChild(layer);layer.AddChild(screen);screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        float fov=_camera.Camera.Fov;
        async Task Fade(float start,float end,double seconds)
        {
            var tween=CreateTween();
            tween.TweenMethod(Callable.From<float>(p=> {
                material.SetShaderParameter("strength",p);
                _camera.Camera.Fov=fov-(gentle?0:2.5f*p);
            }),start,end,seconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            await ToSignal(tween,Tween.SignalName.Finished);
        }
        try
        {
            _controller.SteerWorld(Vector3.Zero,Domain.Spatial.Gait.Walk,_camera);
            _soundEvents.WaystoneUse(from);WaystoneResponse(from,true);TravelStonePulse(from,false);
            await Fade(0,1,1.2);
            _controller.TravelTo(from,to);
            ulong deadline=Time.GetTicksMsec()+5000;
            // The command and world streaming run normally underneath the wash.
            while(!_waystoneArrived && Time.GetTicksMsec()<deadline)
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(_waystoneArrived)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            material.SetShaderParameter("arriving",true);
            await Fade(1,0,1.2);
        }
        catch(Exception e) { GD.PushError("Waystone presentation: "+e); }
        finally { _camera.Camera.Fov=fov;layer.QueueFree();_waystoneTravelBusy=false; }
    }
}
