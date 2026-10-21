using Godot;
using System.Text.Json;
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
    private const int InnShotsDepartureMinute = 7 * 60 + 55;

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
        ("square", 457.0f, 733.5f, 1.65f, 449.5f, 727.5f, 1.4f),
        ("inn", 449.9f, 727.4f, 1.65f, 440.0f, 728.0f, 1.2f),
        ("gate", 470.0f, 753.0f, 1.65f, 458.0f, 741.0f, 1.3f),
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
            GetTree().Quit(3);
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
            GetTree().Quit();
            return;
        }
        var simulation = _session.Simulation!;
        bool hud = _hud.Visible;
        SetProcess(false);                       // nothing moves the camera out from under the shot
        try
        {
            _hud.Visible = false;
            if (step == 0)
            {
                AdvanceTo(InnShotsDepartureMinute);
                await Shot("h0755_departure", InnShotsViews.First(v => v.Name == "gate"));
                await Occupants("07:55");
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
            SetProcess(true);
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
        // The rig puts the camera back on the character every frame it draws, so the placement is held - not set once - for the frames the
        // shot is taken over, and applied again on the frame the picture is taken from.
        for (int frame = 0; frame < 8; frame++)
        {
            _camera.Camera.GlobalPosition = eye;
            _camera.Camera.LookAt(look);
            _camera.Camera.MakeCurrent();
            await Rcd3Wait(0.05);
        }
        _camera.Camera.GlobalPosition = eye;
        _camera.Camera.LookAt(look);
        _camera.Camera.MakeCurrent();
        var drawn = GetViewport().GetCamera3D();
        GD.Print($"INN_SHOTS {prefix}_{view.Name} from {eye} looking at {look}; drawn from {drawn?.Name.ToString() ?? "none"} at " +
                 $"{drawn?.GlobalPosition.ToString() ?? "-"}");
        await Rcd3Shot(_innShotsDirectory!, $"{prefix}_{view.Name}");
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
