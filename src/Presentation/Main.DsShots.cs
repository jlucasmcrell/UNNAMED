using Godot;
using System.Text.Json;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Opt-in evidence for the owner's findings 24 (the nameplate), 25 (the torch as a real light) and 29 (the alpha wolf), taken through the
    // running game's normal simulation, camera and HUD. The only things submitted are the same commands the player's own input sends.
    private const string DsShotsOption = "--ds-shots";
    // Find the chest torch through a normal loot command, then walk to the den. No starting grant.
    private static readonly (long X, long Z) DsShotsStart = (438_000, 738_500);

    private string? _dsShotsDirectory;
    private int _dsStep = -2;
    private ulong _dsNext;
    private ulong _dsLast;
    private double _dsDelta;
    private ulong _dsStatusAt;
    private readonly Queue<(double X,double Z)> _dsRoute = new();
    private bool _dsTorchBeforeDen;
    private double _dsSettle;

    private void InstallDsShots()
    {
        if (_options.GetValueOrDefault(DsShotsOption) is not { Length: > 0 } directory)
            return;
        _dsShotsDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_dsShotsDirectory);
        _dsNext = Time.GetTicksMsec() + 3000;
        _dsLast = _dsNext;
        _rcd3Driving = true;
        GD.Print("DS_SHOTS installed " + _dsShotsDirectory);
    }

    private void TickDsShots()
    {
        if (_dsShotsDirectory is null || Time.GetTicksMsec() < _dsNext)
            return;
        ulong now=Time.GetTicksMsec();_dsDelta=(now-_dsLast)/1000.0;_dsLast=now;
        try { AdvanceDsShots(); }
        catch (Exception error)
        {
            GD.PushError("DS_SHOTS failed: " + error);
            if (_dsShotsDirectory is { } directory) File.WriteAllText(Path.Combine(directory, "failed.txt"), error.ToString());
            _dsShotsDirectory = null;
            GetTree().Quit(3);
        }
    }

    private void AdvanceDsShots()
    {
        var simulation = _session.Simulation!;
        var creatures = simulation.Creatures;
        CreatureView? Alpha() => creatures.FirstOrDefault(c => c.Alive && c.DefId == "creature.beast.wolf_grey_alpha");
        var player = simulation.Player.Body;
        if(Time.GetTicksMsec()>_dsStatusAt)
        {
            _dsStatusAt=Time.GetTicksMsec()+2000;
            File.WriteAllText(Path.Combine(_dsShotsDirectory!,"status.json"),JsonSerializer.Serialize(new {
                stage=_dsStep,player,route=_dsRoute.Count,alpha=Alpha()?.Body,
                torch=simulation.Player.Inventory.Any(i=>i.DefId=="item.tool.torch"),tick=simulation.WorldTick }));
            GD.Print($"DS_SHOTS stage={_dsStep} feet={player.XMm},{player.ZMm} route={_dsRoute.Count} alpha={Alpha()?.DefId ?? "absent"}");
        }
        double Away(CreatureView c) => Math.Sqrt(Math.Pow(c.Body.XMm - player.XMm, 2) + Math.Pow(c.Body.ZMm - player.ZMm, 2));

        switch (_dsStep)
        {
            case -2:
            {
                var box=simulation.Containers.Single(c=>c.Site.Key=="container.waystation_chest");
                var torch=box.Items.FirstOrDefault(i=>i.DefId=="item.tool.torch")
                    ?? throw new InvalidOperationException("Chest torch missing");
                _session.Submit(new MoveItemCommand(simulation.PlayerId,torch.Ref,ItemPlace.In(box.Site.Key),ItemPlace.Carried,1));
                _dsStep=-1;_dsNext=Time.GetTicksMsec()+200;return;
            }
            case -1:
            {
                if(!simulation.Player.Inventory.Any(i=>i.DefId=="item.tool.torch"))
                    throw new InvalidOperationException("Normal chest take was refused");
                if(_dsRoute.Count==0)
                {
                    var from=player;
                    foreach(var anchor in new[] {(430000L,738500L),(430000L,755000L),(455000L,755000L),
                        (480000L,755000L),(505000L,765000L)})
                    {
                        var goal=new Body(anchor.Item1,simulation.ActiveLayout.Space.Terrain.HeightAtMm(anchor.Item1,anchor.Item2),anchor.Item2,0);
                        foreach(var point in Playthrough.WayTo(simulation,from,goal,standOff:0)) _dsRoute.Enqueue(point);
                        from=goal;
                    }
                }
                var next=_dsRoute.Peek();
                var toward=new Vector3((float)(next.X-player.XMm/1000.0),0,(float)(next.Z-player.ZMm/1000.0));
                if(toward.Length()<.4f)
                {
                    _dsRoute.Dequeue();
                    if(!_dsTorchBeforeDen && next.X==480 && next.Z==755)
                    {
                        _dsTorchBeforeDen=true;_dsStep=2;_dsSettle=0;
                        _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);
                        _camera.Yaw=-1.1f;_camera.Pitch=-.35f;_camera.TargetDistance=4;
                    }
                    else if(_dsRoute.Count==0)_dsStep=0;
                }
                else _controller.SteerWorld(toward.Normalized(),Gait.Run,_camera);
                _dsNext=Time.GetTicksMsec()+16;return;
            }
            // Walk at the den until the alpha is close enough to read, then hold still: the plate is drawn by the normal HUD.
            case 0:
            {
                var alpha = Alpha();
                if (alpha is null)
                {
                    _dsNext = Time.GetTicksMsec() + 500;
                    return;
                }
                if (Away(alpha) > 17_000)
                {
                    var toward = new Vector3((float)((alpha.Body.XMm - player.XMm) / 1000.0), 0, (float)((alpha.Body.ZMm - player.ZMm) / 1000.0));
                    _controller.SteerWorld(toward.Normalized(), Gait.Run, _camera);
                    _camera.Yaw = Mathf.Atan2(-toward.X, -toward.Z);
                    _dsNext = Time.GetTicksMsec() + 16;
                    return;
                }
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                _dsSettle += _dsDelta;
                if (_dsSettle < 0.6)
                    return;
                _dsSettle = 0;
                Shot("den_approach_plate", Target(simulation), "Actual automatic target plate on approaching the den; subject is the HUD's selected creature");
                _dsStep = 1;
                _dsNext = Time.GetTicksMsec() + 100;
                return;
            }
            // Let the pack come, and shoot the plate once one of them is fighting: the bar appears only then.
            case 1:
            {
                var engaged = creatures.Where(c => c.Alive && c.Hostile).OrderBy(Away).FirstOrDefault();
                if (engaged is null)
                {
                    if(Alpha() is { } alpha && Away(alpha)>7000)
                    {
                        var toward=new Vector3((alpha.Body.XMm-player.XMm)/1000f,0,(alpha.Body.ZMm-player.ZMm)/1000f);
                        _controller.SteerWorld(toward.Normalized(),Gait.Walk,_camera);
                    }
                    _dsNext = Time.GetTicksMsec() + 100;
                    return;
                }
                _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);
                _dsSettle += _dsDelta;
                if (_dsSettle < 0.6)
                    return;
                _dsSettle = 0;
                Shot("den_engaged_plate", Target(simulation), "Actual automatic target plate during engagement");
                File.WriteAllText(Path.Combine(_dsShotsDirectory!,"complete.json"),JsonSerializer.Serialize(new {
                    status="DS_SHOTS_CAPTURED_REQUIRES_CLAUDE_REVIEW", people=VisualOptions.People,
                    hour=_session.ClockAtWorldStartHours, shots=new[]{"torch_off","torch_on","den_approach_plate","den_engaged_plate"} }));
                GD.Print("DS_SHOTS complete "+_dsShotsDirectory);_dsShotsDirectory=null;GetTree().Quit();
                return;
            }
            // The torch, in the off hand and out of it, from the same framing: the light is the difference.
            case 2:
            {
                _dsSettle += _dsDelta;
                if (_dsSettle < 0.6)
                    return;
                _dsSettle = 0;
                Shot("torch_off", Target(simulation), "Open-road comparison before combat; off hand empty");
                Equip();
                _dsStep = 3;
                _dsNext = Time.GetTicksMsec() + 100;
                return;
            }
            case 3:
            {
                _dsSettle += _dsDelta;
                if (_dsSettle < 1.2)
                    return;
                _dsSettle = 0;
                Shot("torch_on", Target(simulation), "Same open-road camera before combat; found torch equipped");
                var light = _avatar.FindChild("OffHandLight", true, false) as OmniLight3D;
                File.WriteAllText(Path.Combine(_dsShotsDirectory!, "torch_light.json"), JsonSerializer.Serialize(new
                {
                    found = light is not null,
                    path = light?.GetPath().ToString(),
                    range_m = light?.OmniRange,
                    energy = light?.LightEnergy,
                    shadows = light?.ShadowEnabled,
                    light_world = light?.GlobalPosition.ToString(), player_feet = _avatar.GlobalPosition.ToString(),
                    item = "item.tool.torch",
                    light_radius_in_content_m = 8,
                    note = light is null ? "NO LIGHT NODE: the torch is equipped but nothing was attached to the off hand" : "attached to the off hand's bone attachment",
                }, new JsonSerializerOptions { WriteIndented = true }));
                if(light is null || light.OmniRange!=8) throw new InvalidOperationException("Found torch did not create its 8m light");
                _dsStep=-1;_dsNext=Time.GetTicksMsec()+100;
                return;
            }
        }
    }

    /// <summary>Equip the torch the way the inventory screen does, so the figure and the light see it.</summary>
    private void Equip()
    {
        var simulation = _session.Simulation!;
        var entry = simulation.Player.Inventory.FirstOrDefault(e => e.DefId == "item.tool.torch");
        if (entry is null)
        {
            GD.PushWarning("DS_SHOTS: no torch in the inventory to equip");
            return;
        }
        _session.Submit(new EquipCommand(simulation.PlayerId, entry.ItemId));
    }

    private void Shot(string name, CreatureView? subject, string note)
    {
        SaveScreenshot(_dsShotsDirectory!, name);
        File.WriteAllText(Path.Combine(_dsShotsDirectory!, name + ".json"), JsonSerializer.Serialize(new
        {
            note,
            camera=_camera.Camera.GlobalPosition.ToString(), player=_session.Simulation!.Player.Body,
            subject = subject is null ? null : new
            {
                def = subject.DefId, level = subject.Level, threat = subject.Threat.ToString(), threatWord = UNNAMED.Domain.Creatures.ThreatRules.Word(subject.Threat),
                temperament = subject.Temperament.ToString(), temperamentWord = UNNAMED.Domain.Creatures.TemperamentRules.Word(subject.Temperament),
                engaged = subject.Hostile, huntable = subject.Huntable, health = $"{subject.Health}/{subject.MaxHealth}",
            },
            plate = subject is null ? null : $"Level {subject.Level} · {UNNAMED.Domain.Creatures.ThreatRules.Word(subject.Threat)} · {UNNAMED.Domain.Creatures.TemperamentRules.Word(subject.Temperament)} · {(subject.Huntable ? "can be hunted" : "nothing to take from it")}",
            playerLevel = _session.Simulation!.Player.Progression.Level,
            options = string.Join(",", VisualOptions.All.Select(kv => $"{kv.Key}={kv.Value}")),
        }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("DS_SHOTS frame " + name);
    }
}
