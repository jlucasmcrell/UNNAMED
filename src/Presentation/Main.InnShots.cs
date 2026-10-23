using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Opt-in evidence for spec 15 (owner finding 15): the inn and the adventurers' day, photographed at four hours from three viewpoints,
    // plus the departure at the gate and the notice board open. The clock is advanced through the world's own offline-advance step - the same
    // step an absence takes - so nothing here writes simulation state by hand, and the bodies standing in the frames are the ones the
    // adventurers' own schedule placed. The captures are of the SECOND day: the world opens at 07:30 on day one, half an hour before the
    // departure hour, and a fresh world plans no day before its first midnight - a day of bodies standing about in the inn is exactly what the
    // owner complained of, and not what the routine is.
    private const string InnShotsOption = "--inn-shots";

    private static readonly int[] InnShotsHours = { 8, 13, 19, 23 };

    /// <summary>The day the frames are taken on (the world's first midnight has been crossed, so the day is planned).</summary>
    private const int InnShotsDay = 1;

    /// <summary>Five minutes before the departure hour: the party is at the gate, walking out.</summary>
    private const int InnShotsDepartureMinute = 7 * 60 + 50;

    /// <summary>The steps: the departure, the four hours, and the board. Each hour step shoots every view.</summary>
    private const int InnShotsSteps = 1 + 4 + 1;

    /// <summary>
    /// The three viewpoints, out where the things are, in metres on the region's own ground: the square from the open side of the longhouse
    /// looking back at its east door, the common room from just inside that door, and the gate from the road outside it looking back into the
    /// hamlet. Heights are ABOVE THE GROUND the camera stands on - the region's ground is some fifteen metres up, so a camera placed at a
    /// fixed world height is under the terrain, which is what the first attempts photographed.
    /// </summary>
    private static readonly (string Name, float EyeX, float EyeZ, float EyeUp, float LookX, float LookZ, float LookUp)[] InnShotsViews =
    {
        ("square", 461.0f, 734.0f, 1.75f, 449.0f, 728.5f, 1.35f),
        ("inn", 444.5f, 726.6f, 1.65f, 437.5f, 726.2f, 1.0f),
        ("gate", 467.5f, 751.5f, 1.65f, 463.9f, 748.7f, 1.2f),
    };

    private string? _innShotsDirectory;
    private ulong _innNext;
    private int _innStep;
    private bool _innBusy;

    private void InstallInnShots()
    {
        if (_options.GetValueOrDefault(InnShotsOption) is not { Length: > 0 } directory)
            return;
        _innShotsDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_innShotsDirectory);
        _innNext = Time.GetTicksMsec() + 2500;
        _rcd3Driving = true;
        _innFreeCamera = true;   // the rig keeps its hands off the camera while the frames are being taken
        GD.Print("INN_SHOTS installed " + _innShotsDirectory);
    }

    /// <summary>Driven from the main loop: one step is advanced and photographed at a time, and the loop waits for it.</summary>
    private void TickInnShots()
    {
        if (_innShotsDirectory is null || _innBusy || Time.GetTicksMsec() < _innNext)
            return;
        _innNext = Time.GetTicksMsec() + 400;
        _innBusy = true;
        _ = StepInnShots();
    }

    private async Task StepInnShots()
    {
        try
        {
            await OneStep();
        }
        catch (Exception error)
        {
            GD.PushError("INN_SHOTS failed: " + error);
            File.WriteAllText(Path.Combine(_innShotsDirectory!, "failed.txt"), error.ToString());
            CaptureQuit.At(this, 3);
        }
        finally
        {
            _innBusy = false;
        }
    }

    private async Task OneStep()
    {
        int step = _innStep++;
        if (step >= InnShotsSteps)
        {
            File.WriteAllText(Path.Combine(_innShotsDirectory!, "complete.json"),
                JsonSerializer.Serialize(new { day = InnShotsDay, hours = InnShotsHours, departure_minute = InnShotsDepartureMinute,
                    views = InnShotsViews.Select(v => v.Name).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
            CaptureQuit.At(this);
            return;
        }
        var simulation = _session.Simulation!;
        bool hud = _hud.Visible;
        // The world keeps drawing while a shot is taken - the NPC figures only catch up with their bodies in the frame pass, so stopping it
        // left them standing where they had been an hour before ("nothing at the gate" was this, not the schedule). The rig keeps its hands
        // off the camera for the same frames (_innFreeCamera).
        try
        {
            _hud.Visible = false;
            if (step == 0)
            {
                AdvanceTo(InnShotsDepartureMinute);
                await Shot("h0755_departure", InnShotsViews.First(v => v.Name == "gate"));
                await Occupants("07:55");
                AdventurerBodies("07:50");
                return;
            }
            if (step <= InnShotsHours.Length)
            {
                int hour = InnShotsHours[step - 1];
                AdvanceTo(hour * 60);
                foreach (var view in InnShotsViews)
                    await Shot($"h{hour:00}", view);
                await Occupants($"{hour:00}:00");
                return;
            }
            // The board, open, over the common room it hangs in.
            AdvanceTo(8 * 60);
            _noticeBoard.Open("station.ah_notice_board");
            await Rcd3Wait(0.5);
            await Shot("board", InnShotsViews.First(v => v.Name == "inn"));
            _noticeBoard.Close();
            await Occupants("08:00");
        }
        finally
        {
            _hud.Visible = hud;
        }
    }

    /// <summary>The first tick of a minute of the capture day, by the world's own advance - the same catch-up an absence takes.</summary>
    private void AdvanceTo(int minuteOfDay)
    {
        var simulation = _session.Simulation!;
        var clock = _session.Setup.Clock;
        long ticksPerMinute = clock.TicksPerDay / (clock.HoursPerDay * 60L);
        long target = InnShotsDay * clock.TicksPerDay - clock.StartTicks + minuteOfDay * ticksPerMinute;
        if (target > simulation.WorldTick)
            simulation.Advance(target - simulation.WorldTick);
    }

    private async Task Shot(string prefix, (string Name, float EyeX, float EyeZ, float EyeUp, float LookX, float LookZ, float LookUp) view)
    {
        // Eye and target stand on the ground under them: the region's ground is several metres up, so a fixed world height is under the terrain.
        var eye = new Vector3(view.EyeX, _ground.Height(view.EyeX, view.EyeZ) + view.EyeUp, view.EyeZ);
        var look = new Vector3(view.LookX, _ground.Height(view.LookX, view.LookZ) + view.LookUp, view.LookZ);
        // The rig puts the camera back on the character every frame it draws, and the character's own body would otherwise stand in the
        // middle of the shot: the placement is held - not set once - across the frames the shot is taken over, and the avatar kept out of them.
        for (int frame = 0; frame < 8; frame++)
        {
            _avatar.Visible = false;
            _camera.Camera.GlobalPosition = eye;
            _camera.Camera.LookAt(look);
            _camera.Camera.MakeCurrent();
            await Rcd3Wait(0.05);
        }
        _avatar.Visible = false;
        _camera.Camera.GlobalPosition = eye;
        _camera.Camera.LookAt(look);
        _camera.Camera.MakeCurrent();
        var drawn = GetViewport().GetCamera3D();
        GD.Print($"INN_SHOTS {prefix}_{view.Name} from {eye} looking at {look}; drawn from {drawn?.Name.ToString() ?? "none"} at " +
                 $"{drawn?.GlobalPosition.ToString() ?? "-"}");
        await Rcd3Shot(_innShotsDirectory!, $"{prefix}_{view.Name}");
    }

    /// <summary>
    /// Where each adventurer of the town actually is when the shutter opens: the simulation's body, the head the world draws for it (or none),
    /// and whether that figure is in the tree and visible. A body the simulation holds and no view draws is invisible to the player as well -
    /// which is exactly what an empty gate at the departure hour is.
    /// </summary>
    private void AdventurerBodies(string when)
    {
        var simulation = _session.Simulation!;
        var figures = _npcs.GetChildren().OfType<Figure>().ToList();
        foreach (var adventurer in simulation.Adventurers.Where(a => a.Key.Contains("ashen_hollow", StringComparison.Ordinal)))
        {
            var view = simulation.Npcs.FirstOrDefault(n => n.Id == adventurer.Key);
            var head = _npcs.HeadOf(adventurer.Key);
            var figure = figures.FirstOrDefault(f => f.Name.ToString().Contains(adventurer.Key, StringComparison.Ordinal));
            GD.Print($"INN_BODIES {when} {adventurer.Key} inTown={adventurer.InTown} sim={(view is null ? "none" : $"{view.Body.XMm},{view.Body.ZMm}")} space={(view is null ? "-" : view.Space.ToString())} active={simulation.ActiveLayout.SpaceId} " +
                     $"head={(head is { } h ? $"{h.X:0.0},{h.Y:0.0},{h.Z:0.0}" : "none")} " +
                     $"figure={(figure is null ? "none" : $"{figure.GlobalPosition.X:0.0},{figure.GlobalPosition.Z:0.0} visible={figure.Visible} inTree={figure.IsVisibleInTree()}")} " +
                     $"figuresInTree={figures.Count(f => f.IsVisibleInTree())}");
        }
    }

    /// <summary>
    /// Who is in the inn at this hour, as a line beside the frames: every body within twenty metres of the door, its role, and whether it
    /// stands at a bed or in the common room. The room's population is the owner's complaint, so the frames say who the crowd is.
    /// </summary>
    private async Task Occupants(string when)
    {
        var simulation = _session.Simulation!;
        var inn = _session.Setup.Adventurers!.Settlements["ashen_hollow"].Inn!;
        long Distance(long ax, long az, long bx, long bz)
        {
            long dx = ax - bx, dz = az - bz;
            return (long)Math.Sqrt(dx * dx + dz * dz);
        }
        var near = simulation.Npcs
            .Where(n => Distance(n.Body.XMm, n.Body.ZMm, inn.Door.XMm, inn.Door.ZMm) < 20_000)
            .Select(n => new
            {
                id = n.Id,
                role = n.Role,
                at_bed = inn.Beds.Any(b => Distance(n.Body.XMm, n.Body.ZMm, b.XMm, b.ZMm) < 1_500),
                in_room = inn.Seats.Append(inn.Board).Append(inn.Hearth).Any(p => Distance(n.Body.XMm, n.Body.ZMm, p.XMm, p.ZMm) < 1_500),
            })
            .ToList();
        File.AppendAllText(Path.Combine(_innShotsDirectory!, "occupants.jsonl"),
            JsonSerializer.Serialize(new { when, day = _session.Setup.Clock.Day(simulation.WorldTick), count = near.Count, bodies = near })
            + System.Environment.NewLine);
        await Rcd3Wait(0.05);
    }
}
