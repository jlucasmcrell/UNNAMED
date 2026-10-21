using Godot;
using System.Text.Json;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Opt-in evidence for spec 15 (owner finding 15): the inn and the adventurers' day, photographed at four hours from three viewpoints.
    // The clock is advanced through the world's own offline-advance step - the same step an absence takes - so nothing here writes simulation
    // state by hand, and the bodies standing in the frames are the ones the adventurers' own schedule placed.
    private const string InnShotsOption = "--inn-shots";

    private static readonly int[] InnShotsHours = { 8, 13, 19, 23 };

    /// <summary>The three viewpoints: the square (inn and forge from the road side), the inn's common room, and the gate at the road out.</summary>
    private static readonly (string Name, Vector3 Eye, Vector3 Look)[] InnShotsViews =
    {
        ("square", new Vector3(444, 9, 761), new Vector3(452, 2, 747)),
        ("inn", new Vector3(449.4f, 2.4f, 727.0f), new Vector3(438, 1.3f, 727.5f)),
        ("gate", new Vector3(457, 3.2f, 741.5f), new Vector3(466, 1.4f, 748.5f)),
    };

    private string? _innShotsDirectory;
    private ulong _innNext;
    private int _innStep = -1;
    private double _innTicksPerHour;
    private double _innHour;

    private void InstallInnShots()
    {
        if (_options.GetValueOrDefault(InnShotsOption) is not { Length: > 0 } directory)
            return;
        _innShotsDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_innShotsDirectory);
        var clock = _session.Setup.Clock;
        _innTicksPerHour = (double)clock.TicksPerDay / clock.HoursPerDay;
        _innNext = Time.GetTicksMsec() + 2500;
        _rcd3Driving = true;
        GD.Print("INN_SHOTS installed " + _innShotsDirectory);
    }

    /// <summary>Driven from the main loop: one hour is advanced and photographed per step, so each step stays a frame's work.</summary>
    private void TickInnShots()
    {
        if (_innShotsDirectory is null || Time.GetTicksMsec() < _innNext)
            return;
        _innNext = Time.GetTicksMsec() + 400;
        _ = StepInnShots();
    }

    private async Task StepInnShots()
    {
        _innStep++;
        if (_innStep >= InnShotsHours.Length)
        {
            File.WriteAllText(Path.Combine(_innShotsDirectory!, "complete.json"),
                JsonSerializer.Serialize(new { hours = InnShotsHours, views = InnShotsViews.Select(v => v.Name).ToArray() },
                    new JsonSerializerOptions { WriteIndented = true }));
            GetTree().Quit();
            return;
        }
        var camera = _camera.Camera;
        SetProcess(false);                       // nothing moves the camera out from under the shot
        var clock = _session.Setup.Clock;
        _innHour = clock.Hour(_session.Simulation!.WorldTick) % clock.HoursPerDay;
        double target = InnShotsHours[_innStep];
        double ahead = target - _innHour;
        if (ahead <= 0)
            ahead += clock.HoursPerDay;
        // A jump is the world's own advance (no re-entrancy risk: the harness holds no command), so midnight-crossing work - the adventurers'
        // day, their outcomes, the markets - happens exactly as it does while the character is away.
        _session.Simulation.Advance((long)Math.Max(1, Math.Round(ahead * _innTicksPerHour)));
        // Let the views and the light catch up with the new hour, then hold the camera where the shot wants it.
        SetProcess(true);
        await Rcd3Wait(0.8);
        SetProcess(false);
        foreach (var (name, eye, look) in InnShotsViews)
        {
            camera.GlobalPosition = eye;
            camera.LookAt(look);
            await Rcd3Wait(0.3);
            await Rcd3Shot(_innShotsDirectory!, $"h{InnShotsHours[_innStep]:00}_{name}");
            GD.Print($"INN_SHOTS h{InnShotsHours[_innStep]:00}_{name}");
        }
        File.AppendAllText(Path.Combine(_innShotsDirectory!, "inn_shots.jsonl"),
            JsonSerializer.Serialize(new { hour = InnShotsHours[_innStep], world_hour = clock.Hour(_session.Simulation!.WorldTick) % clock.HoursPerDay })
            + System.Environment.NewLine);
        SetProcess(true);
    }
}
