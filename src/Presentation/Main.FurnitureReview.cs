using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

public partial class Main
{
    private async Task RunFurnitureReview(string directory,Camera3D camera)
    {
        _rcd3Driving=true;_innFreeCamera=true;
        while(GetViewport().Disable3D)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        DisplayServer.WindowSetTitle("Otherreach - "+Path.GetFileName(directory));
        var hud=_hud.Visible;_hud.Visible=false;_avatar.Visible=false;
        var layer=new CanvasLayer{Layer=100};AddChild(layer);
        var label=new Label{Position=new Vector2(24,24),Text="Furniture motion review"};layer.AddChild(label);
        var flash=new ColorRect{Position=new Vector2(4,4),Size=new Vector2(48,48),Color=Colors.Magenta,Visible=false};layer.AddChild(flash);
        var records=new List<object>();
        try
        {
            AdvanceTo(6*60+30);
            File.WriteAllText(Path.Combine(directory,"record_ready.json"),JsonSerializer.Serialize(new{cast=VisualOptions.People,max_fps=Engine.MaxFps,kind="Real-time normal world/NPC animation; offline clock advance between labelled phases only; daylight held for pose visibility"}));
            ulong timeout=Time.GetTicksMsec()+300000;
            while(!File.Exists(Path.Combine(directory,"recording.started")))
            {
                if(Time.GetTicksMsec()>timeout)throw new TimeoutException("Furniture recorder not ready");
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            foreach(var phase in new[]{("seated",6*60+30,new Vector3(447,1.65f,728.8f),new Vector3(443,1.0f,726.3f)),
                ("sleeping",23*60,new Vector3(442.9f,1.9f,729.5f),new Vector3(437.2f,.85f,727.8f)),
                ("keeper",23*60,new Vector3(443.5f,1.7f,729.0f),new Vector3(440.95f,.85f,730.93f))})
            {
                label.Text="ADVANCING CLOCK - excluded from motion judgement";
                AdvanceTo(phase.Item2);
                var eye=phase.Item3+Vector3.Up*_ground.Height(phase.Item3.X,phase.Item3.Z);
                var aim=phase.Item4+Vector3.Up*_ground.Height(phase.Item4.X,phase.Item4.Z);
                void HoldCamera(){camera.GlobalPosition=eye;camera.LookAt(aim);camera.MakeCurrent();_avatar.Visible=false;}
                GetTree().ProcessFrame+=HoldCamera;
                try
                {
                    HoldCamera();await Rcd3Wait(1.5);
                    label.Text=$"{VisualOptions.People} | {phase.Item1} | world {phase.Item2/60:00}:{phase.Item2%60:00} | real time";
                    HoldCamera();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory,phase.Item1+".png"));
                    flash.Visible=true;await Rcd3Wait(.15);flash.Visible=false;
                    ulong start=Time.GetTicksUsec(),end=Time.GetTicksMsec()+12000;
                    int frames=0;
                    while(Time.GetTicksMsec()<end)
                    {
                        HoldCamera();
                        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                        frames++;
                    }
                    var actors=_npcs.GetChildren().OfType<Figure>().Where(f=>f.IsVisibleInTree() && f.GlobalPosition.DistanceTo(aim)<12)
                        .Select(f=>new{id=f.Name.ToString(),clip=f.ClipState,position=f.GlobalPosition.ToString(),head=f.Head?.ToString(),left=f.Foot("L")?.ToString(),right=f.Foot("R")?.ToString(),support=f.RestContact?.ToString()}).ToArray();
                    records.Add(new{phase=phase.Item1,frames,start_usec=start,end_usec=Time.GetTicksUsec(),hour=phase.Item2/60,actors});
                    File.WriteAllText(Path.Combine(directory,"furniture.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
                    if(!actors.Any(a=>a.clip==(phase.Item1=="seated"?"sit":"lie_to_idle")))throw new InvalidOperationException("No furniture actor in "+phase.Item1);
                    if(phase.Item1=="keeper" && (_npcs.FigureOf("npc.ashen_hollow.renn_vale") is not Figure keeper || keeper.ClipState!="lie_to_idle"))throw new InvalidOperationException("Renn is not lying in his own cot");
                }
                finally{GetTree().ProcessFrame-=HoldCamera;}
            }
            File.WriteAllText(Path.Combine(directory,"furniture.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
            File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new{status="FURNITURE_REQUIRES_VIDEO_REVIEW",cast=VisualOptions.People,max_fps=Engine.MaxFps}));
        }
        finally
        {
            _hud.Visible=hud;layer.QueueFree();_innFreeCamera=false;
            File.WriteAllText(Path.Combine(directory,"recording.finished"),DateTimeOffset.Now.ToString("o"));
            ulong stop=Time.GetTicksMsec()+10000;
            while(!File.Exists(Path.Combine(directory,"recording.stopped")) && Time.GetTicksMsec()<stop)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
    }
}
