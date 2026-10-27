using System.Text.Json;
using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Private --stand/--start-items fixture. Everything shown after boot uses the
    // same public item, station and crafting commands as the player's buttons.
    private void InstallCampReview()
    {
        if(_options.GetValueOrDefault("--camp-review") is not { Length:>0 } directory) return;
        _rcd3Driving=_innFreeCamera=true;
        _=CampReview(Path.GetFullPath(directory));
    }

    private async Task CampReview(string directory)
    {
        Directory.CreateDirectory(directory);
        DisplayServer.WindowSetTitle("Otherreach_C3_Camp_Proof");
        var rejected=new List<string>();
        _session.Subscribe<CommandRejected>(e=>rejected.Add(e.Reason));
        void Mark(string phase)=>File.AppendAllText(Path.Combine(directory,"phases.jsonl"),JsonSerializer.Serialize(new {
            phase,frame=Engine.GetFramesDrawn(),tick=_session.Simulation!.WorldTick,camp=_session.Simulation.Camp,
            inventory=_session.Simulation.Player.Inventory.Select(i=>new {i.DefId,i.Count}) })+System.Environment.NewLine);
        try
        {
            var source=_camera.Camera;
            var camera=new Camera3D { Fov=75,Near=source.Near,Far=source.Far,CullMask=source.CullMask,
                Attributes=source.Attributes,Environment=source.Environment };
            AddChild(camera);
            source.GetNodeOrNull<OmniLight3D>("CharacterFill")?.Reparent(camera,false);
            var layer=new CanvasLayer {Layer=100};
            var caption=new Label {Position=new Vector2(22,22)};
            caption.AddThemeFontSizeOverride("font_size",24);caption.AddThemeConstantOverride("outline_size",8);
            layer.AddChild(caption);AddChild(layer);
            var sim=_session.Simulation!;
            float floor=sim.ActiveLayout.Space.Terrain.HeightAtMm(600_000,750_000)/1000f;
            camera.GlobalPosition=new Vector3(604,floor+2.2f,754);
            camera.LookAt(new Vector3(600.8f,floor+.4f,750));camera.MakeCurrent();
            do { await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
            while(sim.Npcs.Any(n=>n.Space==sim.ActiveLayout.SpaceId && _npcs.FigureOf(n.Id) is null));
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            File.WriteAllText(Path.Combine(directory,"ready.json"),JsonSerializer.Serialize(new {gameplay_frame_rendered=true,game_pid=OS.GetProcessId()}));
            while(!File.Exists(Path.Combine(directory,"recording.ready"))) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            long startFrame=(long)Engine.GetFramesDrawn();
            AdvanceTo(13*60);
            _session.Submit(new MoveCommand(sim.PlayerId,MoveIntent.Idle(0)));
            caption.Text="Day 2 · 13:00\nPrivate review kit · set camp from the pack";
            await Rcd3Wait(1);
            Mark("before placement");
            OpenInventory(null);await Rcd3Wait(1.5);
            var kit=sim.Player.Inventory.First(i=>i.DefId=="item.tool.camp_kit");
            _session.Submit(new UseItemCommand(sim.PlayerId,kit.ItemId));
            await Rcd3Wait(.6);CloseInventory();_avatar.Interact();
            await Rcd3Wait(2);
            if(sim.Camp is null || rejected.Count>0) throw new InvalidOperationException("Camp placement failed: "+string.Join("; ",rejected));
            caption.Text="Day 2 · 13:00\nA bedroll and a working cookfire · the world keeps moving";
            Mark("camp placed");await Rcd3Wait(2);
            await Rcd3Shot(directory,"camp_day");
            ulong walkUntil=Time.GetTicksMsec()+10_000;
            while(Time.GetTicksMsec()<walkUntil)
            {
                var body=_controller.Authoritative;
                var toward=new Vector3((sim.Camp!.XMm-body.XMm)/1000f,0,(sim.Camp.ZMm-body.ZMm)/1000f);
                if(toward.Length()<=1.3f) break;
                _controller.SteerWorld(toward.Normalized(),Gait.Walk,_camera);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);await Rcd3Wait(.3);
            OpenStation(CampView.StationKey);await Rcd3Wait(1);
            caption.Text="The campfire · the normal crafting panel\nTorches, cooking, boiled water, tea and salves";
            await Rcd3Wait(1);await Rcd3Shot(directory,"camp_crafting");
            _session.Submit(new CraftCommand(sim.PlayerId,"recipe.cooking.roast_meat"));
            await Rcd3Wait(1.5);
            if(!sim.Player.Inventory.Any(i=>i.DefId=="item.consumable.roast_meat") || rejected.Count>0)
                throw new InvalidOperationException("Cooking failed: "+string.Join("; ",rejected));
            caption.Text="Roast meat made · one raw meat spent\nThe same command as Make at the fire";
            Mark("roast cooked");await Rcd3Wait(2);CloseInventory();
            _session.Submit(new RestAtCampCommand(sim.PlayerId,true));await Rcd3Wait(.4);
            long restingTick=sim.WorldTick;
            caption.Text="Resting by the fire\nFaster recovery outside a fight · time and weather keep running";
            await Rcd3Wait(2);
            if(!sim.RestingAtCamp || sim.WorldTick<=restingTick) throw new InvalidOperationException("Live camp rest was not established");
            Mark("rest while world ticks");
            _session.Submit(new RestAtCampCommand(sim.PlayerId,false));await Rcd3Wait(.3);
            // This is a review-only clock jump, not a camp or rest mechanic.
            AdvanceTo(23*60);
            caption.Text="Day 2 · 23:00\nNight comparison · review clock advanced for this still";
            await Rcd3Wait(3);await Rcd3Shot(directory,"camp_night");Mark("night comparison");
            File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
                source="Private supplied kit; real UseItem, VisitStation, Craft and RestAtCamp commands",
                startFrame,endFrame=Engine.GetFramesDrawn(),framesPerSecond=20,rejected,
                stills=new[] {"camp_day.png","camp_crafting.png","camp_night.png"} }));
            CaptureQuit.At(this);
        }
        catch(Exception error)
        {
            File.WriteAllText(Path.Combine(directory,"failed.txt"),error.ToString());
            GD.PushError("CAMP_REVIEW failed: "+error);CaptureQuit.At(this,3);
        }
    }
}
