// UNNAMED Presentation - the pets' capture pass (spec 43 and its owner rulings): taming an animal out of the wild, the orders with
// their ground marker, and a fight - recorded in real time by Godot's Movie Maker (--write-movie before --), one scene at a time
// Godot presentation only: every step is the player's own command path, and nothing here writes simulation state (D-11)

using Godot;
using System.Text.Json;
using UNNAMED.Domain.Pets;
using UNNAMED.World;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    private const string PetShotsOption = "--pet-shots";

    /// <summary>How near the character must be to a wild animal before it offers the food: a step closer than the rule's own reach.</summary>
    private const double OfferWithinMetres = 3.2;

    private string? _petShotsDirectory;
    private ulong _petNext;
    private ulong _petBeat;
    private int _petStep;
    private bool _petBusy;
    private int _petFrame;

    private void InstallPetShots()
    {
        if (_options.GetValueOrDefault(PetShotsOption) is not { Length: > 0 } directory)
            return;
        _petShotsDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_petShotsDirectory);
        _petNext = Time.GetTicksMsec() + 2600;
        // The harness has the controller: ReadInput leaves the body alone while this stands, so the steering below moves it.
        _rcd3Driving = true;
        GD.Print("PET_SHOTS installed " + _petShotsDirectory);
    }

    /// <summary>Driven from the main loop: one scene at a time, each waiting for the last.</summary>
    private void TickPetShots()
    {
        if (_petShotsDirectory is null)
            return;
        _petFrame++;
        // A heartbeat every ten seconds: a run that stops without one having come first was killed from outside, and the last line dates
        // it next to whatever else the machine was doing.
        if (Time.GetTicksMsec() >= _petBeat)
        {
            _petBeat = Time.GetTicksMsec() + 10_000;
            GD.Print($"PET_SHOTS heartbeat frame={_petFrame} step={_petStep} tick={_session?.Simulation?.WorldTick} " +
                $"fps={Engine.GetFramesPerSecond():0.0} wall={DateTimeOffset.Now:O}");
        }
        if (_petBusy || Time.GetTicksMsec() < _petNext)
            return;
        _petNext = Time.GetTicksMsec() + 400;
        _petBusy = true;
        _ = StepPetShots();
    }

    private async Task StepPetShots()
    {
        try
        {
            await OnePetStep();
        }
        catch (Exception error)
        {
            GD.PushError("PET_SHOTS failed: " + error);
            File.WriteAllText(Path.Combine(_petShotsDirectory!, "failed.txt"), error.ToString());
            GetTree().Quit(3);
        }
        finally
        {
            _petBusy = false;
        }
    }

    private async Task OnePetStep()
    {
        int step = _petStep++;
        var simulation = _session.Simulation!;
        switch (step)
        {
            case 0:
            {
                // The hound comes from the wild with meat and a steady hand, and the meat is the character's own: a hare on the open ground
                // between the yard and the den, run down, killed with the sword and searched. Nothing is granted.
                await Rcd3Wait(1.5);
                PetEvidence("start", $"the character stands {PetWhere(simulation)}; queued {_petFrame} frames; packets {simulation.Pets.Length} animals");
                await Rcd3Shot(_petShotsDirectory!, "pets_start");
                if (!await Hunt(120))
                    throw new InvalidOperationException($"no hare was run down for its meat; the character stands {PetWhere(simulation)}");
                await Rcd3Shot(_petShotsDirectory!, "pets_meat");
                PetEvidence("meat", $"a hare killed and searched: {Carried(simulation)}");
                break;
            }
            case 1:
            {
                // The den's sleeper: creep up on it crouched, offer the meat, and hold. Nothing in the world is written by this - the offer,
                // the channel, the odds and the roll are the pet system's own.
                if (!await ApproachPoint(520, 730, 7.0, 120))
                    throw new InvalidOperationException($"could not reach the strays; the character stands {PetWhere(simulation)}");
                _controller.Crouch(true);
                await Rcd3Wait(0.6);
                await Rcd3Shot(_petShotsDirectory!, "pets_creeping");
                if (!await Tame(90))
                    GD.Print("PET_SHOTS: the offer did not take; carrying on with what the run has");
                await Rcd3Wait(1.5);
                await Rcd3Shot(_petShotsDirectory!, "pets_tamed");
                PetEvidence("tame", $"{simulation.Pets.Length} animal(s): " +
                    string.Join("; ", simulation.Pets.Select(p => $"{p.Name} {p.Order}, {p.Standing}, {p.Health}/{p.MaxHealth}, bond {p.Bond}")));
                break;
            }
            case 2:
            {
                // "Wait here", with the ground marker: the list comes up, the ring is laid ten metres out, and the animal holds the spot
                // while the character walks away from it.
                _controller.Crouch(false);
                OpenPetOrders();
                await Rcd3Wait(1.2);
                await Rcd3Shot(_petShotsDirectory!, "pets_marker");
                OrderPet(PetOrder.WaitHere);
                await Rcd3Wait(0.6);
                await RunAwayFrom(simulation.Pets.FirstOrDefault()?.Body, 25);
                await Rcd3Wait(1.5);
                await Rcd3Shot(_petShotsDirectory!, "pets_waiting");
                PetEvidence("wait here", string.Join("; ", simulation.Pets.Select(p => $"{p.Name} {p.Order} at {Metres(p.Body)}")));
                break;
            }
            case 3:
            {
                // Back to the character, then one of them pulled out of the pack: the pet takes one creature and brings it.
                OrderPet(PetOrder.Follow);
                await Rcd3Wait(4.0);
                OpenPetOrders();
                await Rcd3Wait(0.8);
                OrderPet(PetOrder.Pull);
                await Rcd3Wait(12.0);
                await Rcd3Shot(_petShotsDirectory!, "pets_pull");
                PetEvidence("pull one", $"{simulation.Pets.Length} animal(s): " +
                    string.Join("; ", simulation.Pets.Select(p => $"{p.Name} {p.Doing}, {p.Standing}, {p.Health}")) +
                    $" | creatures engaged: {simulation.Creatures.Count(c => c.Hostile)}");
                break;
            }
            case 4:
            {
                // The fight it pulled: the animal bites what is fighting the party, and is bitten back for it. A pet is never killed - at no
                // health it goes home badly wounded (owner ruling 1), and the HUD says for how long.
                for (int swing = 0; swing < 40 && simulation.Creatures.Any(c => c.Hostile); swing++)
                {
                    _controller.Attack();
                    await Rcd3Wait(0.7);
                }
                await Rcd3Wait(2.0);
                await Rcd3Shot(_petShotsDirectory!, "pets_fight");
                PetEvidence("fight", string.Join("; ", simulation.Pets.Select(p => $"{p.Name} {p.Doing}, {p.Standing} {p.Health}/{p.MaxHealth}")));
                break;
            }
            default:
                File.WriteAllText(Path.Combine(_petShotsDirectory!, "complete.json"),
                    JsonSerializer.Serialize(new { frames = _petFrame, steps = step, animals = simulation.Pets.Length }));
                GD.Print($"PET_SHOTS complete: {step} steps, {_petFrame} frames, {simulation.Pets.Length} animals");
                GetTree().Quit(0);
                break;
        }
    }

    /// <summary>
    /// Run down the nearest hare and search it: steer at it, swing when it is in reach, and keep at it until the character has meat.
    /// A hare's own run is slower than a sprint, so the chase is a chase and not a wish.
    /// </summary>
    private async Task<bool> Hunt(double maxSeconds)
    {
        var simulation = _session.Simulation!;
        double deadline = Time.GetTicksMsec() + maxSeconds * 1000;
        while (Time.GetTicksMsec() < deadline)
        {
            if (Meat(simulation) > 0)
                return true;
            var hare = simulation.Creatures.Where(c => c.Alive && c.DefId == "creature.beast.rabbit")
                .OrderBy(c => Distance(c.Body, simulation.Player.Body)).FirstOrDefault();
            if (hare is null)
                break;
            var body = simulation.Player.Body;
            if (Distance(hare.Body, body) <= 2_400)
            {
                _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
                _controller.Attack();
                await Rcd3Wait(0.45);
                if (simulation.Creatures.All(c => !c.Alive || c.Key != hare.Key))
                    await SearchNearby();
                continue;
            }
            var to = new Vector3((hare.Body.XMm - body.XMm) / 1000f, 0, (hare.Body.ZMm - body.ZMm) / 1000f);
            _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
            _controller.SteerWorld(to.Normalized(), Gait.Sprint, _camera, faceCamera: true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await SearchNearby();
        return Meat(simulation) > 0;
    }

    /// <summary>Search every corpse within reach: what the hunt leaves is the character's.</summary>
    private async Task SearchNearby()
    {
        var simulation = _session.Simulation!;
        foreach (var corpse in simulation.Creatures.Where(c => !c.Alive && c.CorpseKey.Length > 0).ToList())
            _session.Submit(new TakeAllCommand(simulation.PlayerId, corpse.CorpseKey));
        await Rcd3Wait(0.8);
    }
    private static int Meat(Simulation simulation) =>
        simulation.Player.Inventory.Where(e => e.DefId == "item.material.raw_meat" || e.DefId == "item.consumable.roast_meat").Sum(e => e.Count);

    private static string Carried(Simulation simulation) =>
        string.Join(", ", simulation.Player.Inventory.Where(e => e.DefId.Contains("meat")).Select(e => $"{e.DefId} x{e.Count}"));

    /// <summary>Creep to within the offer's reach of the nearest animal that can still be offered food, and offer it.</summary>
    private async Task<bool> Tame(double maxSeconds)
    {
        var simulation = _session.Simulation!;
        double deadline = Time.GetTicksMsec() + maxSeconds * 1000;
        string food = simulation.Player.Inventory.FirstOrDefault(e => e.DefId == "item.material.raw_meat") is { } raw
            ? raw.DefId
            : simulation.Player.Inventory.FirstOrDefault(e => e.DefId == "item.consumable.roast_meat").DefId;
        while (Time.GetTicksMsec() < deadline)
        {
            if (simulation.Pets.Length > 0 && simulation.TameOf(FindTameable(simulation)?.Id ?? default) is null)
                return true;
            var prey = FindTameable(simulation);
            if (prey is null)
                return simulation.Pets.Length > 0;
            var body = simulation.Player.Body;
            double distance = Distance(prey.Body, body);
            if (distance <= OfferWithinMetres * 1000)
            {
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                _session.Submit(new TameCommand(simulation.PlayerId, prey.Id, food));
                // The channel is content's; hold on until it is taken or broken.
                await Rcd3Wait((simulation.PetRules.Tame.ChannelTicks + 6) * _session.TickSeconds);
                if (simulation.Pets.Length > 0)
                    return true;
                // It has seen the character now, or the roll was against: step back and try once more later.
                await Rcd3Wait(6.0);
                continue;
            }
            var to = new Vector3((prey.Body.XMm - body.XMm) / 1000f, 0, (prey.Body.ZMm - body.ZMm) / 1000f);
            _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
            _controller.SteerWorld(to.Normalized(), Gait.Walk, _camera, faceCamera: true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        return simulation.Pets.Length > 0;
    }

    /// <summary>The nearest grey wolf that can be reached for: alive, not yet roused, inside sight of the character.</summary>
    private static UNNAMED.World.Runtime.CreatureView? FindTameable(Simulation simulation) => simulation.Creatures
        .Where(c => c.Alive && c.DefId == "creature.beast.wolf_grey" && c.Mind != CreatureMind.Engaged
                    && Distance(c.Body, simulation.Player.Body) < 22_000)
        .OrderBy(c => Distance(c.Body, simulation.Player.Body)).FirstOrDefault();

    /// <summary>Walk to a place in region metres, steering each frame; false when it is not reached in time.</summary>
    private async Task<bool> ApproachPoint(double x, double z, double withinMetres, double maxSeconds)
    {
        var simulation = _session.Simulation!;
        var (tx, tz) = (x * 1000, z * 1000);
        double deadline = Time.GetTicksMsec() + maxSeconds * 1000;
        while (Time.GetTicksMsec() < deadline)
        {
            var body = simulation.Player.Body;
            double distance = Math.Sqrt(Math.Pow(tx - body.XMm, 2) + Math.Pow(tz - body.ZMm, 2));
            if (distance <= withinMetres * 1000)
            {
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                await Rcd3Wait(0.3);
                return true;
            }
            var to = new Vector3((float)((tx - body.XMm) / 1000.0), 0, (float)((tz - body.ZMm) / 1000.0));
            _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
            _controller.SteerWorld(to.Normalized(), Gait.Run, _camera, faceCamera: true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
        return false;
    }

    /// <summary>Put distance between the character and the animals, so a waiting one is seen to hold its post.</summary>
    private async Task RunAwayFrom(Body? from, double metres)
    {
        var simulation = _session.Simulation!;
        if (from is null)
        {
            await Rcd3Wait(1.0);
            return;
        }
        double deadline = Time.GetTicksMsec() + 20_000;
        while (Time.GetTicksMsec() < deadline && Distance(from, simulation.Player.Body) < metres * 1000)
        {
            var body = simulation.Player.Body;
            var away = new Vector3((body.XMm - from.XMm) / 1000f, 0, (body.ZMm - from.ZMm) / 1000f);
            _camera.Yaw = Mathf.Atan2(-away.X, -away.Z);
            _controller.SteerWorld(away.Normalized(), Gait.Run, _camera, faceCamera: true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
    }

    private static string PetWhere(Simulation simulation) => Metres(simulation.Player.Body);

    private static string Metres(Body body) => $"({body.XMm / 1000.0:0.0}, {body.ZMm / 1000.0:0.0})";

    /// <summary>One line per scene beside the video: what happened, in the systems' own numbers, and the frame it began at.</summary>
    private void PetEvidence(string scene, string what)
    {
        var simulation = _session.Simulation!;
        File.AppendAllText(Path.Combine(_petShotsDirectory!, "shots.jsonl"),
            JsonSerializer.Serialize(new
            {
                scene,
                what,
                frame = _petFrame,
                tick = simulation.WorldTick,
                animals = simulation.Pets.Select(p => $"{p.Name}:{p.Order}:{p.Standing}:{p.Health}/{p.MaxHealth}:bond{p.Bond}").ToArray(),
                strength = simulation.PartyStrength,
                encounterPercent = simulation.PetRules.Encounter.PercentPerPoint > 0
                    ? UNNAMED.Domain.Pets.Encounters.Of(simulation.PartyStrength, simulation.PetRules.Encounter).Percent : 100,
            }) + System.Environment.NewLine);
    }
}