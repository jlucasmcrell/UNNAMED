// UNNAMED Presentation - the pets' capture pass (spec 43 and its owner rulings): the wain's meat, the hound bought at Ashen Hollow's
// stall, a wolf tamed out of the wild, the orders with their ground marker, and a fight that sends an animal home badly wounded.
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

    /// <summary>Kera Voss keeps Ashen Hollow's stall: her shelf is where the pup is bought (spec 43's reliable route).</summary>
    private const string Kera = "npc.ashen_hollow.kera_voss";

    private const string PupItem = "item.misc.hound_pup";
    private const string RawMeat = "item.material.raw_meat";
    private const string RoastMeat = "item.consumable.roast_meat";

    /// <summary>How near the character must be to a wild animal before it offers the food: a step closer than the rule's own reach.</summary>
    private const double OfferWithinMetres = 3.2;

    private string? _petShotsDirectory;
    private string? _petRefusal;
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
        // A command the world refuses is answered nowhere the log can see it (the panels toast it), and a refused trade looks exactly like one
        // that has not landed yet: the reason is printed here, and kept, so the walking in above knows whether the world took its command.
        _session.Subscribe<CommandRejected>(rejected =>
        {
            _petRefusal = rejected.Reason;
            GD.Print($"PET_SHOTS refused {rejected.Command.GetType().Name}: {rejected.Reason}");
        });
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
            // Nothing after a failed scene: the pass stops here rather than driving a run that is already being torn down.
            _petShotsDirectory = null;
            CaptureQuit.At(this, 3);
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
        GD.Print($"PET_SHOTS step {step} begins at frame {_petFrame}, tick {simulation.WorldTick}, {PetWhere(simulation)}, " +
            $"{simulation.Pets.Length} animal(s), {Meat(simulation)} meat, purse {simulation.Player.Currency}");
        switch (step)
        {
            case 0:
            {
                // The meat: the overturned wain the road left, searched with the character's own hands. The world is loaded and has settled
                // by now, so this is also where the recorder outside is told to start: the boot is not part of the pass.
                await Rcd3Wait(1.0);
                File.WriteAllText(Path.Combine(_petShotsDirectory!, "loaded.txt"), DateTimeOffset.Now.ToString("O"));
                await Rcd3Wait(3.0);
                PetEvidence("start", $"the character stands {PetWhere(simulation)}; queued {_petFrame} frames; purse {simulation.Player.Currency}");
                await Rcd3Shot(_petShotsDirectory!, "pets_start");
                var wain = simulation.Containers.FirstOrDefault(c => c.Site.Key == "container.road.wain_load");
                if (wain is null)
                    throw new InvalidOperationException("there is no wain to search here");
                _session.Submit(new TakeAllCommand(simulation.PlayerId, wain.Site.Key));
                await Rcd3Wait(1.2);
                await Rcd3Shot(_petShotsDirectory!, "pets_meat");
                PetEvidence("meat", $"the wain searched: {Carried(simulation)}");
                if (Meat(simulation) == 0)
                    throw new InvalidOperationException($"the wain gave no meat; the character stands {PetWhere(simulation)}");
                break;
            }
            case 1:
            {
                // The stall, and the hound on the shelf: the reliable route of spec 43. The walk is over the region's own navigation - the
                // road between the wain and the hollow bends, and a straight line to it walks into the Foldscar.
                var kera = simulation.Npcs.First(n => n.Id == Kera);
                // Kera works her anvil inside the forge shed, and its door is shut at the start of a world: the navigation cannot route through a
                // shut door, and the shed's other three sides are wall. The way in is the door itself - waited at from outside, opened, and only
                // then is she a walk away (Playthrough.ToTheForgeDoor brings a character along the road to this same spot).
                if (simulation.Doors.FirstOrDefault(d => d.Site.Key == "door.forge_shed") is { Open: false })
                {
                    await GoTo(new Body(451_800, kera.Body.YMm, 741_200, 0), 1.4, 180);
                    _controller.Interact("door.forge_shed");
                    await Rcd3Wait(1.0);
                    GD.Print($"PET_SHOTS forge door: open {simulation.Doors.First(d => d.Site.Key == "door.forge_shed").Open}, " +
                        $"{PetWhere(simulation)}");
                }
                await GoTo(kera.Body, 1.0, 300);
                await Rcd3Shot(_petShotsDirectory!, "pets_hollow");
                // Her counter is close work, and the trade's reach is the simulation's 1.6 m, not the conversation prompt's. What stands between
                // the road and her (the anvil she works at) is solid, so the last metres are walked round her: a ring of standpoints, each tried
                // until the world itself takes a command.
                for (int side = 0; side < 8; side++)
                {
                    double round = side * Math.PI / 4;
                    await ApproachPoint((kera.Body.XMm + Math.Sin(round) * 1_100) / 1000.0,
                        (kera.Body.ZMm + Math.Cos(round) * 1_100) / 1000.0, 0.4, 8);
                    _petRefusal = null;
                    _controller.Talk(Kera);
                    await Rcd3Wait(0.5);
                    if (_petRefusal is null)
                        break;
                    GD.Print($"PET_SHOTS not at her counter yet ({_petRefusal}); {PetWhere(simulation)}");
                }
                GD.Print($"PET_SHOTS at her counter: {_petRefusal ?? "the world took the command"}, purse {simulation.Player.Currency}");
                Trade(simulation);
                await Rcd3Wait(1.2);
                await Rcd3Shot(_petShotsDirectory!, "pets_stall");
                PetEvidence("stall", $"at Kera's shelf with {simulation.Player.Currency} coin: {Carried(simulation)}");
                // What the road gave, sold for the pup: the load's hides, then the character's own salve - a broke traveller's kit. The timber is
                // worth nothing to her (two coin at the forty percent a merchant pays) and the flask is a tool she does not buy at all.
                foreach (string selling in new[] { "item.material.small_hide", "item.consumable.salve_minor" })
                    await SellAll(simulation, selling);
                await Rcd3Shot(_petShotsDirectory!, "pets_sold");
                var bought = await BuyPup(simulation);
                await Rcd3Shot(_petShotsDirectory!, "pets_pup_bought");
                PetEvidence("buy a pup", $"{bought ?? "not bought"}; purse {simulation.Player.Currency}");
                // A run that could not buy it still has the wolf to tame and the fight to show: the pass says so and carries on rather than
                // stopping with half the beats unrecorded.
                if (bought is null)
                    GD.Print($"PET_SHOTS: no pup this run - {PupPrice(simulation)} asked, {simulation.Player.Currency} carried");
                // In the pack, and then taken in: the animal that is already the character's.
                if (simulation.Player.Inventory.FirstOrDefault(e => e.DefId == PupItem) is { } lead)
                {
                    _session.Submit(new AdoptPetCommand(simulation.PlayerId, lead.ItemId));
                    await Rcd3Wait(1.0);
                    await Rcd3Shot(_petShotsDirectory!, "pets_pup");
                }
                // The food for the offer, bought while the shelf is open and the coin is still in hand.
                await BuyMeat(simulation);
                _inventory.Close();
                PetEvidence("the party", $"{simulation.Pets.Length} animal(s): " +
                    string.Join("; ", simulation.Pets.Select(p => $"{p.Name} {p.Order}, {p.Standing}, {p.Health}/{p.MaxHealth}, bond {p.Bond}")) +
                    $"; carrying {Carried(simulation)}, purse {simulation.Player.Currency}");
                break;
            }
            case 2:
            {
                // The wild wolf: creep up on one crouched, offer the meat, and hold the channel. The walker goes for the animal itself rather
                // than a coordinate - a moving pack is what a fixed approach point fails at - and the offer, the channel, the odds and the
                // roll are the pet system's own. The pup is in the party from here, so a pack that walks off still leaves the pass its beats.
                _controller.Crouch(true);
                await Rcd3Wait(0.6);
                await Rcd3Shot(_petShotsDirectory!, "pets_creeping");
                bool tamed = await Tame(180);
                _controller.Crouch(false);
                PetEvidence("tame", $"{(tamed ? "the offer took" : "no animal was taken")}: " +
                    string.Join("; ", simulation.Pets.Select(p => $"{p.Name} {p.Order}, {p.Standing}, {p.Health}/{p.MaxHealth}, bond {p.Bond}")));
                await Rcd3Wait(1.0);
                await Rcd3Shot(_petShotsDirectory!, "pets_tamed");
                break;
            }
            case 3:
            {
                // "Wait here", with the ground marker: the list comes up, the ring is laid out ahead, and the animal holds the spot while the
                // character walks away from it. With no animal in the party the pass says so and moves on rather than standing about.
                if (!simulation.Pets.Any(p => p.Condition == PetCondition.Up))
                {
                    PetEvidence("wait here", "no animal is with the party: nothing to order");
                    break;
                }
                OpenPetOrders();
                await Rcd3Wait(1.2);
                await Rcd3Shot(_petShotsDirectory!, "pets_marker");
                await Obey(PetOrder.WaitHere, 6);
                await Rcd3Wait(0.8);
                await RunAwayFrom(simulation.Pets.FirstOrDefault()?.Body, 22);
                await Rcd3Wait(1.5);
                await Rcd3Shot(_petShotsDirectory!, "pets_waiting");
                PetEvidence("wait here", string.Join("; ", simulation.Pets.Select(p => $"{p.Name} {p.Order} at {Metres(p.Body)}")));
                break;
            }
            case 4:
            {
                // A fight to work on: the wolves are roused by the character's own blade - the party engages one, which is what a lure and a
                // pull both act on (an animal draws off an enemy already fighting the party, it does not start the fight).
                for (int round = 0; round < 30 && !simulation.Creatures.Any(c => c.Hostile); round++)
                {
                    if (NearestWolf(simulation) is { } wolf)
                        await GoTo(wolf.Body, 4.0, 30);
                    _controller.Attack();
                    await Rcd3Wait(0.8);
                }
                await Rcd3Shot(_petShotsDirectory!, "pets_roused");
                PetEvidence("roused", $"{simulation.Creatures.Count(c => c.Hostile)} creature(s) fighting the party");
                // The lure: the animal is sent to a place by the pack and draws one creature out of the fight to it - the control skill doing
                // the work a fight would otherwise cost.
                OpenPetOrders();
                await Rcd3Wait(1.0);
                await Obey(PetOrder.LureToHere, 6);
                await Rcd3Wait(7.0);
                await Rcd3Shot(_petShotsDirectory!, "pets_lure");
                PetEvidence("lure", string.Join("; ", simulation.Pets.Select(p => $"{p.Name} {p.Doing}, {p.Standing}")) +
                    $" | fighting: {simulation.Creatures.Count(c => c.Hostile)}");
                break;
            }
            case 5:
            {
                // One pulled out of the pack: the animal takes one creature and brings it back to the character.
                OpenPetOrders();
                await Rcd3Wait(1.0);
                await Obey(PetOrder.Pull, 6);
                await Rcd3Wait(8.0);
                await Rcd3Shot(_petShotsDirectory!, "pets_pull");
                PetEvidence("pull one", string.Join("; ", simulation.Pets.Select(p => $"{p.Name} {p.Doing}, {p.Standing}, {p.Health}")) +
                    $" | fighting: {simulation.Creatures.Count(c => c.Hostile)}");
                break;
            }
            case 6:
            {
                // The fight: the animal goes for what the character strikes and is bitten back for it, until one of the animals is down. It has
                // to be IN the fight to be hurt - an animal that was only pulled and then walks at heel takes no more blows. A pet is never
                // killed: at no health it goes home badly wounded (owner ruling 1) and the HUD says for how long.
                for (int round = 0; round < 90 && simulation.Pets.All(p => p.Condition == PetCondition.Up); round++)
                {
                    bool withAnimal = simulation.Pets.Any(p => p.Condition == PetCondition.Up);
                    if (withAnimal && (round % 12 == 0 || simulation.Pets.All(p => p.Order != PetOrder.AttackTarget)))
                    {
                        OpenPetOrders();
                        await Rcd3Wait(0.6);
                        await Obey(PetOrder.AttackTarget, 3);
                    }
                    if (NearestHostile(simulation) is { } foe)
                        await GoTo(foe.Body, 2.5, 6);
                    _controller.Attack();
                    await Rcd3Wait(0.7);
                }
                await Rcd3Wait(1.5);
                await Rcd3Shot(_petShotsDirectory!, "pets_fight");
                PetEvidence("fight", string.Join("; ", simulation.Pets.Select(p => $"{p.Name} {p.Doing}, {p.Standing} {p.Health}/{p.MaxHealth}")));
                break;
            }
            case 7:
            {
                // Home badly wounded: the record stays and the days are counted, not a death (owner ruling 1). The character is told where it
                // is and when it can be collected.
                var home = simulation.Pets.FirstOrDefault(p => p.Condition == PetCondition.Wounded);
                PetEvidence("wounded home", home is null
                    ? "no animal was wounded in this pass"
                    : $"{home.Name}: {home.Standing}, home until tick {home.HomeUntilTick}, strength {simulation.PartyStrength}");
                await Rcd3Wait(2.0);
                await Rcd3Shot(_petShotsDirectory!, "pets_after_the_fight");
                break;
            }
            default:
                File.WriteAllText(Path.Combine(_petShotsDirectory!, "complete.json"),
                    JsonSerializer.Serialize(new
                    {
                        frames = _petFrame, steps = step, animals = simulation.Pets.Length,
                        purse = simulation.Player.Currency, wounded = simulation.Pets.Where(p => p.Condition == PetCondition.Wounded).Select(p => p.Name).ToArray(),
                    }));
                GD.Print($"PET_SHOTS complete: {step} steps, {_petFrame} frames, {simulation.Pets.Length} animals");
                CaptureQuit.At(this, 0);
                break;
        }
    }

    // ── the player's own hands: walking, trading, ordering ───────────────────

    /// <summary>
    /// Walk to a body over the region's navigation, steering each frame, and finish at the body itself: a trade needs the character at the
    /// counter (interact reach is 1.6 m), not on the road outside it. The corners on the way only have to be passed; each gets its own slice of
    /// the time so one awkward corner cannot eat the walk. False when it is not reached in time.
    /// </summary>
    private async Task<bool> GoTo(Body target, double withinMetres, double maxSeconds)
    {
        var simulation = _session.Simulation!;
        double deadline = Time.GetTicksMsec() + maxSeconds * 1000;
        var route = Playthrough.WayTo(simulation, simulation.Player.Body, target, withinMetres);
        for (int i = 0; i < route.Length; i++)
        {
            var (x, z) = route[i];
            double left = deadline - Time.GetTicksMsec();
            if (left <= 200)
                return false;
            bool last = i == route.Length - 1;
            if (!await ApproachPoint(x, z, last ? 1.1 : 1.5, (last ? left : Math.Min(left, 45_000)) / 1000))
                return false;
        }
        return true;
    }

    /// <summary>Her shelf, opened as the player opens it: her conversation's own trade reply, and the panel behind it.</summary>
    private void Trade(Simulation simulation)
    {
        _controller.Talk(Kera);
        if (_dialogue.Visible && _dialogue.Replies.FirstOrDefault(r => r.StartsWith("trade", StringComparison.Ordinal)) is { } reply)
            _dialogue.Answer(reply);
        if (_inventory.OpenTrader is null)
            _inventory.OpenTrade(Kera);
        GD.Print($"PET_SHOTS stall: talk reach {_controller.InTalkReach(simulation.Npcs.First(n => n.Id == Kera))}, " +
            $"wares {simulation.Wares(Kera)?.Wares.Length ?? 0}, panel {_inventory.OpenTrader ?? "shut"}");
    }

    /// <summary>What Kera asks for the pup, or null when her shelf has none.</summary>
    private long? PupPrice(Simulation simulation) =>
        simulation.Wares(Kera)?.Wares.FirstOrDefault(w => w.ItemId == PupItem)?.Price;

    /// <summary>Sell every one of an item the character carries: the game's own SellCommand, one call per stack.</summary>
    private async Task SellAll(Simulation simulation, string defId)
    {
        long before = simulation.Player.Currency;
        foreach (var entry in simulation.Player.Inventory.Where(e => e.DefId == defId).ToArray())
            _session.Submit(new SellCommand(simulation.PlayerId, Kera, entry.ItemId, entry.Count));
        await Rcd3Wait(1.0);
        GD.Print($"PET_SHOTS sold {defId}: purse {before} -> {simulation.Player.Currency}");
    }

    /// <summary>Buy the pup off her shelf. A refusal is only ever visible as the thing not arriving, so the pack is read back.</summary>
    private async Task<string?> BuyPup(Simulation simulation)
    {
        if (simulation.Wares(Kera)?.Wares.FirstOrDefault(w => w.ItemId == PupItem) is not { } pup)
        {
            GD.Print($"PET_SHOTS buy {PupItem}: her shelf has none");
            return null;
        }
        long before = simulation.Player.Currency;
        _session.Submit(new BuyCommand(simulation.PlayerId, Kera, pup.Ref, 1));
        await Rcd3Wait(1.0);
        bool taken = simulation.Player.Inventory.Any(e => e.DefId == PupItem);
        GD.Print($"PET_SHOTS buy {PupItem}: {pup.Price} asked, purse {before} -> {simulation.Player.Currency}, " +
            $"pup {(taken ? "in the pack" : "NOT bought")}");
        return taken ? $"the pup bought for {pup.Price}" : null;
    }

    /// <summary>Raw meat for the offers, as much as the purse still covers.</summary>
    private async Task BuyMeat(Simulation simulation)
    {
        for (int i = 0; i < 4; i++)
        {
            if (simulation.Wares(Kera)?.Wares.FirstOrDefault(w => w.ItemId == RawMeat) is not { } meat || meat.Price > simulation.Player.Currency)
                break;
            int had = simulation.Player.Inventory.Where(e => e.DefId == RawMeat).Sum(e => e.Count);
            _session.Submit(new BuyCommand(simulation.PlayerId, Kera, meat.Ref, 1));
            await Rcd3Wait(0.8);
            if (simulation.Player.Inventory.Where(e => e.DefId == RawMeat).Sum(e => e.Count) == had)
            {
                GD.Print($"PET_SHOTS buy {RawMeat}: refused at {meat.Price} against a purse of {simulation.Player.Currency}");
                break;
            }
        }
    }

    /// <summary>Give an order until the animal takes it: a wary one can refuse a hard one, and the player asks again (owner ruling 1).</summary>
    private async Task<bool> Obey(PetOrder order, int tries)
    {
        for (int attempt = 0; attempt < tries; attempt++)
        {
            if (!_session.Simulation!.Pets.Any(p => p.Condition == PetCondition.Up))
                return false;
            OrderPet(order);
            await Rcd3Wait(0.9);
            if (_session.Simulation.Pets.Any(p => p.Condition == PetCondition.Up && p.Order == order))
                return true;
            OpenPetOrders();
            await Rcd3Wait(0.5);
        }
        return false;
    }

    // ── what the pass reads off the world ───────────────────────────────────

    private static int Meat(Simulation simulation) =>
        simulation.Player.Inventory.Where(e => e.DefId == RawMeat || e.DefId == RoastMeat).Sum(e => e.Count);

    private static string Carried(Simulation simulation) =>
        string.Join(", ", simulation.Player.Inventory.Select(e => $"{e.DefId} x{e.Count}"));

    /// <summary>The nearest grey wolf still standing, alive or roused.</summary>
    private static UNNAMED.World.Runtime.CreatureView? NearestWolf(Simulation simulation) => simulation.Creatures
        .Where(c => c.Alive && c.DefId == "creature.beast.wolf_grey")
        .OrderBy(c => Distance(c.Body, simulation.Player.Body)).FirstOrDefault();

    /// <summary>The creature of the pack that is fighting the party and nearest the character.</summary>
    private static UNNAMED.World.Runtime.CreatureView? NearestHostile(Simulation simulation) => simulation.Creatures
        .Where(c => c.Alive && c.Hostile)
        .OrderBy(c => Distance(c.Body, simulation.Player.Body)).FirstOrDefault();

    /// <summary>Creep to within the offer's reach of the nearest animal that can still be offered food, and offer it, until the food is out.</summary>
    private async Task<bool> Tame(double maxSeconds)
    {
        var simulation = _session.Simulation!;
        double deadline = Time.GetTicksMsec() + maxSeconds * 1000;
        int animals = simulation.Pets.Length;
        bool Taken() => simulation.Pets.Skip(animals).Any(p => p.CreatureId == "creature.beast.wolf_grey");
        while (Time.GetTicksMsec() < deadline && Meat(simulation) > 0)
        {
            var prey = FindTameable(simulation);
            if (prey is null)
            {
                await Rcd3Wait(4.0);
                continue;
            }
            string food = simulation.Player.Inventory.FirstOrDefault(e => e.DefId == RawMeat) is { } raw ? raw.DefId : RoastMeat;
            var body = simulation.Player.Body;
            double distance = Distance(prey.Body, body);
            if (distance <= OfferWithinMetres * 1000)
            {
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                GD.Print($"PET_SHOTS offering {food} to {prey.Id} at {distance / 1000:0.0} m, frame {_petFrame}, tick {simulation.WorldTick}");
                _session.Submit(new TameCommand(simulation.PlayerId, prey.Id, food));
                // The channel is content's; hold on until it is taken or broken.
                await Rcd3Wait((simulation.PetRules.Tame.ChannelTicks + 6) * _session.TickSeconds);
                if (Taken())
                    return true;
                if (Meat(simulation) == 0)
                    return false;
                // It has seen the character now, or the roll was against: step back, let it settle, and try another of the pack.
                await Rcd3Wait(6.0);
                continue;
            }
            var to = new Vector3((prey.Body.XMm - body.XMm) / 1000f, 0, (prey.Body.ZMm - body.ZMm) / 1000f);
            _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
            _controller.SteerWorld(to.Normalized(), Gait.Walk, _camera, faceCamera: true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        return Taken();
    }

    /// <summary>The nearest grey wolf that can be reached for: alive, not yet roused, and near enough to walk to.</summary>
    private static UNNAMED.World.Runtime.CreatureView? FindTameable(Simulation simulation) => simulation.Creatures
        .Where(c => c.Alive && c.DefId == "creature.beast.wolf_grey" && c.Mind != CreatureMind.Engaged
                    && Distance(c.Body, simulation.Player.Body) < 80_000)
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
                wall = DateTimeOffset.Now.ToString("O"),
                purse = simulation.Player.Currency,
                animals = simulation.Pets.Select(p => $"{p.Name}:{p.Order}:{p.Standing}:{p.Health}/{p.MaxHealth}:bond{p.Bond}").ToArray(),
                strength = simulation.PartyStrength,
                encounterPercent = simulation.PetRules.Encounter.PercentPerPoint > 0
                    ? UNNAMED.Domain.Pets.Encounters.Of(simulation.PartyStrength, simulation.PetRules.Encounter).Percent : 100,
            }) + System.Environment.NewLine);
    }
}
