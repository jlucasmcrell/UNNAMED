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
    // Where a capture stands the character: forty metres south-east of the den mouth (spawn.hollow.den_pack at 512, 784 m), outside the
    // pack's sight so the first frame is of a den that has not noticed him.
    private static readonly (long X, long Z) DsShotsStart = (536_000, 752_000);

    private string? _dsShotsDirectory;
    private int _dsStep;
    private ulong _dsNext;
    private double _dsSettle;
    private string _dsFrame = "approach";

    private void InstallDsShots()
    {
        if (_options.GetValueOrDefault(DsShotsOption) is not { Length: > 0 } directory)
            return;
        _dsShotsDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_dsShotsDirectory);
        _dsNext = Time.GetTicksMsec() + 3000;
        GD.Print("DS_SHOTS installed " + _dsShotsDirectory);
    }

    private void TickDsShots()
    {
        if (_dsShotsDirectory is null || Time.GetTicksMsec() < _dsNext)
            return;
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
        double Away(CreatureView c) => Math.Sqrt(Math.Pow(c.Body.XMm - player.XMm, 2) + Math.Pow(c.Body.ZMm - player.ZMm, 2));

        switch (_dsStep)
        {
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
                _dsSettle += GetProcessDeltaTime();
                if (_dsSettle < 0.6)
                    return;
                _dsSettle = 0;
                Shot($"den_alpha_plate", alpha, "the plate over a den that has not noticed the character: name, level, threat, disposition, no bar");
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
                    _dsNext = Time.GetTicksMsec() + 100;
                    return;
                }
                _dsSettle += GetProcessDeltaTime();
                if (_dsSettle < 0.6)
                    return;
                _dsSettle = 0;
                Shot("den_engaged_plate", engaged, "the plate while the pack fights: the health bar is shown only once it is engaged");
                _dsStep = 2;
                _dsNext = Time.GetTicksMsec() + 100;
                return;
            }
            // The torch, in the off hand and out of it, from the same framing: the light is the difference.
            case 2:
            {
                _dsSettle += GetProcessDeltaTime();
                if (_dsSettle < 0.6)
                    return;
                _dsSettle = 0;
                Shot("torch_off", Alpha(), "the off hand empty: no light of the character's own");
                Equip();
                _dsStep = 3;
                _dsNext = Time.GetTicksMsec() + 100;
                return;
            }
            case 3:
            {
                _dsSettle += GetProcessDeltaTime();
                if (_dsSettle < 1.2)
                    return;
                _dsSettle = 0;
                Shot("torch_on", Alpha(), "the torch equipped in the off hand: an OmniLight at its content's light_radius");
                var light = _avatar.FindChild("OffHandLight", true, false) as OmniLight3D;
                File.WriteAllText(Path.Combine(_dsShotsDirectory!, "torch_light.json"), JsonSerializer.Serialize(new
                {
                    found = light is not null,
                    path = light?.GetPath().ToString(),
                    range_m = light?.OmniRange,
                    energy = light?.LightEnergy,
                    shadows = light?.ShadowEnabled,
                    item = "item.tool.torch",
                    light_radius_in_content_m = 8,
                    note = light is null ? "NO LIGHT NODE: the torch is equipped but nothing was attached to the off hand" : "attached to the off hand's bone attachment",
                }, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText(Path.Combine(_dsShotsDirectory!, "complete.json"), JsonSerializer.Serialize(new
                {
                    status = "DS_SHOTS_CAPTURED_REQUIRES_CLAUDE_REVIEW",
                    people = VisualOptions.People,
                    hour = _session.ClockAtWorldStartHours,
                    shots = new[] { "den_alpha_plate", "den_engaged_plate", "torch_off", "torch_on" },
                }, new JsonSerializerOptions { WriteIndented = true }));
                GD.Print("DS_SHOTS complete " + _dsShotsDirectory);
                _dsShotsDirectory = null;
                GetTree().Quit();
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
