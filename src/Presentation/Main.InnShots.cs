using Godot;
using System.Text.Json;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    // C2 proof uses the existing inn capture entry point and the world's own offline
    // advance. A recorder-ready handshake keeps boot/loading out of the day tour.
    private const string InnShotsOption = "--inn-shots";
    private static readonly int[] InnShotsHours = [8, 13, 21, 23];
    private const int InnShotsDay = 1;
    private string? _innShotsDirectory;
    private ulong _innNext;
    private int _innStep;
    private bool _innBusy;
    private Label? _innCaption;
    private Camera3D _innCamera = null!;
    private readonly record struct InnView(string Name, float X, float Z, float LookX, float LookZ, float EyeRaise=0, float LookRaise=0);

    private static InnView[] InnViews(int hour) => hour switch
    {
        8 or 13 => [new("Waystation common room: Renn and the cook",449,730,442,726),
            new("Smithy: Kera",460,739,461.6f,739.6f),
            new("Survey table: Sel",474,725,472,722),
            new("Road watch: guard",467,750,464,748),
            new("Road deliveries: porter",478,734,476,731),
            new("Tavar remains captive until the Foldscar is steadied",548,645,545,642)],
        21 => [new("Supper in the common room",449,730.5f,441.5f,726.3f),
            new("The keeper, workers and travellers eat and drink",437.8f,729,443,725.8f),
            new("Renn's bar and the common room",446,726.5f,440.5f,724.8f),
            new("Tavar remains captive until the Foldscar is steadied",548,645,545,642)],
        _ => [new("Upstairs: Renn's room",439,716.1f,437.3f,715.5f),
            new("Upstairs: Sel's room",444,716.1f,442.4f,715.5f),
            new("Upstairs: Kera's room",449,716.1f,447.4f,715.5f),
            new("Upstairs: the workers' bunk room",438.9f,719.05f,438.9f,720.6f),
            new("Upstairs: scout and travelling guest",444,719.05f,444,720.6f),
            new("Upstairs: the player's private cot, five coin through Renn",449.6f,719.05f,449.6f,720.6f),
            new("Tavar remains captive until the Foldscar is steadied",548,645,545,642)]
    };

    private void InstallInnShots()
    {
        if (_options.GetValueOrDefault(InnShotsOption) is not { Length: > 0 } directory) return;
        DisplayServer.WindowSetTitle("Otherreach_C2_Inn_Proof");
        _innShotsDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_innShotsDirectory);
        _innNext = Time.GetTicksMsec() + 2500;
        _rcd3Driving = _innFreeCamera = true;
        // SpringArm3D writes its child camera during physics even when Follow is
        // paused. Use the existing review-camera pattern outside that rig.
        var source = _camera.Camera;
        _innCamera = new Camera3D { Name = "InnReviewCamera", Fov = 80, Near = source.Near,
            Far = source.Far, CullMask = source.CullMask, Attributes = source.Attributes,
            Environment = source.Environment, Position = new Vector3(449, 8.85f, 730) };
        AddChild(_innCamera);
        if (source.GetNodeOrNull<OmniLight3D>("CharacterFill") is { } fill) fill.Reparent(_innCamera, false);
        _innCamera.LookAt(new Vector3(442, 8.2f, 726));
        _innCamera.MakeCurrent();
        var layer = new CanvasLayer { Layer = 100 };
        _innCaption = new Label { Position = new Vector2(22,22) };
        _innCaption.AddThemeFontSizeOverride("font_size",24);
        _innCaption.AddThemeConstantOverride("outline_size",8);
        layer.AddChild(_innCaption); AddChild(layer);
        _ = InnFrameReady();
    }

    private async Task InnFrameReady()
    {
        // Cold HumGen loading can span several frames. Do not signal a recorder
        // while the first resident draw is still importing bodies and exact-target clips.
        do { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        while (_session.Simulation!.Npcs.Any(n => n.Space == _session.Simulation.ActiveLayout.SpaceId && _npcs.FigureOf(n.Id) is null));
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        File.WriteAllText(Path.Combine(_innShotsDirectory!, "ready.json"), JsonSerializer.Serialize(new {
            gameplay_frame_rendered = true, game_pid = OS.GetProcessId(),
            window_handle = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle),
            frames_drawn = Engine.GetFramesDrawn(), width = GetViewport().GetVisibleRect().Size.X,
            height = GetViewport().GetVisibleRect().Size.Y }));
        GD.Print("INN_SHOTS gameplay frame rendered; waiting for recording.ready");
    }

    private void TickInnShots()
    {
        if (_innShotsDirectory is null || _innBusy || Time.GetTicksMsec() < _innNext
            || !File.Exists(Path.Combine(_innShotsDirectory,"recording.ready"))) return;
        _innBusy = true;
        _ = StepInnShots();
    }

    private async Task StepInnShots()
    {
        try
        {
            if (_innStep >= InnShotsHours.Length)
            {
                File.WriteAllText(Path.Combine(_innShotsDirectory!,"complete.json"),
                    JsonSerializer.Serialize(new { day=InnShotsDay, hours=InnShotsHours }));
                CaptureQuit.At(this); return;
            }
            int hour = InnShotsHours[_innStep++];
            AdvanceTo(hour * 60);
            var simulation = _session.Simulation!;
            var residents = simulation.Lives.Where(l=>l.Settlement=="ashen_hollow").Select(l=> new {
                l.Key,l.Name,l.Role,l.Activity, body=simulation.Npcs.FirstOrDefault(n=>n.Id==l.Key)?.Body }).ToArray();
            File.AppendAllText(Path.Combine(_innShotsDirectory!,"occupants.jsonl"),
                JsonSerializer.Serialize(new { hour,residents,travellers=simulation.Adventurers.Where(a=>a.Settlement=="ashen_hollow") })+System.Environment.NewLine);
            _hud.Visible = false;
            foreach (var view in InnViews(hour)) await InnShot(hour,view);
        }
        catch(Exception error)
        {
            GD.PushError("INN_SHOTS failed: "+error);
            File.WriteAllText(Path.Combine(_innShotsDirectory!,"failed.txt"),error.ToString());
            CaptureQuit.At(this,3);
        }
        finally { _innBusy=false; _innNext=Time.GetTicksMsec()+200; }
    }

    private void AdvanceTo(int minuteOfDay)
    {
        var simulation=_session.Simulation!;
        var clock=_session.Setup.Clock;
        long target=InnShotsDay*clock.TicksPerDay-clock.StartTicks+minuteOfDay*(clock.TicksPerDay/(clock.HoursPerDay*60L));
        if(target>simulation.WorldTick) simulation.Advance(target-simulation.WorldTick);
    }

    private async Task InnShot(int hour,InnView view)
    {
        float Floor(float x,float z)=>_session.Simulation!.ActiveLayout.Space.HeightAtMm((long)(x*1000),(long)(z*1000))/1000f;
        var eye=new Vector3(view.X,Floor(view.X,view.Z)+1.65f+view.EyeRaise,view.Z);
        var look=new Vector3(view.LookX,Floor(view.LookX,view.LookZ)+1+view.LookRaise,view.LookZ);
        _innCaption!.Text=$"Day 2 · {hour:00}:00\n{view.Name}";
        for(int frame=0;frame<48;frame++)
        {
            _avatar.Visible=false;
            _innCamera.GlobalPosition=eye; _innCamera.LookAt(look); _innCamera.MakeCurrent();
            await Rcd3Wait(.05);
        }
        if(hour==21 && view==InnViews(21)[0]) await Rcd3Shot(_innShotsDirectory!,"ground_floor");
        if(hour==23 && view==InnViews(23)[5]) await Rcd3Shot(_innShotsDirectory!,"upper_floor");
        if(hour==21 && view==InnViews(21)[1]) await Rcd3Shot(_innShotsDirectory!,"common_room");
        if(hour==21 && view==InnViews(21)[2]) await Rcd3Shot(_innShotsDirectory!,"bar_counter");
        if(hour==23 && view==InnViews(23)[3]) await Rcd3Shot(_innShotsDirectory!,"bunk_room");
        if(hour==13 && view==InnViews(13)[0])
            await InnShot(13,new InnView("The raised guest storey and covered stair",465,736,447,720,2,2));
        if(view.Name=="The raised guest storey and covered stair") await Rcd3Shot(_innShotsDirectory!,"exterior_storey");
    }
}
