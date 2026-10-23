// UNNAMED Presentation - the pets review fixture: five beats on one quarter of flat grass, every camera placed by hand
// Godot presentation only: it submits the commands a player would and takes stills; it never writes state (D-11)

using Godot;
using UNNAMED.Domain;
using UNNAMED.Domain.Pets;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    private const string PetReviewOption = "--pet-review";

    /// <summary>Where the fixture's character stands: flat open grass east of Ashen Hollow, clear of every building.</summary>
    private const long ReviewPlayerXMm = 503_000;
    private const long ReviewPlayerZMm = 741_000;
    private const string ReviewWolf = "creature.beast.wolf_grey";

    private string? _reviewDirectory;
    private int _reviewStep;
    private ulong _reviewNext;
    private bool _reviewBusy;
    private Camera3D? _reviewCamera;
    private readonly List<string> _reviewLog = new();

    /// <summary>
    /// The world a review runs in (capture only, never in normal play): the character on flat grass, one wolf eight metres ahead to fight, one
    /// unaware wolf twelve metres off to tame, and no other spawner anywhere.
    /// </summary>
    private void InstallPetReview()
    {
        if (_options.GetValueOrDefault(PetReviewOption) is not { Length: > 0 } directory)
            return;
        _reviewDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_reviewDirectory);
        var pup = (ReviewPlayerXMm + 1_500, ReviewPlayerZMm);
        _session.OpenReview(DefaultCharacter, (ReviewPlayerXMm, ReviewPlayerZMm),
        [
            (ReviewWolf, ReviewPlayerXMm + 8_000, ReviewPlayerZMm),
            (ReviewWolf, ReviewPlayerXMm - 1_000, ReviewPlayerZMm + 12_000),
        ]);
        _session.Subscribe<PetAdopted>(e => _reviewLog.Add($"adopted {e.Name}"));
        _session.Subscribe<PetSkillUsed>(e => _reviewLog.Add($"{e.Skill} at {e.Creature}"));
        _session.Subscribe<PetStruck>(e => _reviewLog.Add($"struck for {e.Damage}, {e.Health} left"));
        _session.Subscribe<PetWoundedHome>(e => _reviewLog.Add($"wounded home until {e.UntilTick}"));
        _session.Subscribe<PetRefused>(e => _reviewLog.Add($"refused {e.Order}"));
        _reviewNext = Time.GetTicksMsec() + 4000;
        GD.Print($"PET_REVIEW installed at {_reviewDirectory}; character at {ReviewPlayerXMm},{ReviewPlayerZMm}, pup to be adopted at {pup}");
    }

    /// <summary>Driven from the main loop: one beat at a time, each waiting for the last.</summary>
    private void TickPetReview()
    {
        if (_reviewDirectory is null || _reviewBusy || Time.GetTicksMsec() < _reviewNext)
            return;
        _reviewBusy = true;
        _ = RunReviewBeat();
    }

    private async Task RunReviewBeat()
    {
        try
        {
            await OneReviewBeat();
        }
        catch (Exception e)
        {
            GD.Print($"PET_REVIEW failed at step {_reviewStep}: {e}");
            File.WriteAllText(Path.Combine(_reviewDirectory!, "failed.txt"), e.ToString());
            CaptureQuit.At(this, 3);
        }
        finally
        {
            _reviewBusy = false;
        }
    }

    private async Task OneReviewBeat()
    {
        int beat = _reviewStep++;
        var simulation = _session.Simulation!;
        GD.Print($"PET_REVIEW beat {beat}: {simulation.Creatures.Length} creature(s), {simulation.Pets.Length} animal(s), " +
            $"{simulation.Player.Inventory.Where(i => i.DefId == "item.material.raw_meat").Sum(i => i.Count)} meat");
        switch (beat)
        {
            case 0:
            {
                // The pup is taken from the pack where it was granted, so it is a real kept animal and not a fabricated one.
                await Rcd3Wait(1.0);
                if (simulation.Player.Inventory.FirstOrDefault(i => i.DefId == "item.misc.hound_pup") is { } lead)
                    _session.Submit(new AdoptPetCommand(simulation.PlayerId, lead.ItemId));
                await Rcd3Wait(1.5);
                await ReviewStill("review0_pup_spawned", simulation.Pets.FirstOrDefault()?.Body, along: 4.0, height: 1.5, look: 0.35);
                break;
            }
            case 1:
            {
                // The collar, side on, at a metre and a half: the mark the party's animal wears.
                var pup = simulation.Pets.FirstOrDefault()?.Body;
                await ReviewStill("review1_collar", pup, side: 2.6, along: 1.6, height: 0.70, look: 0.40);
                break;
            }
            case 2:
            {
                // Wait here, on its ring, with the character ten metres off and both in the frame.
                for (int fed = 0; fed < 20 && simulation.Pets.FirstOrDefault() is { } hungry && hungry.Bond < 60
                                   && simulation.Player.Inventory.FirstOrDefault(i => i.DefId == "item.material.raw_meat") is { } piece; fed++)
                {
                    _session.Submit(new FeedPetCommand(simulation.PlayerId, hungry.Id, piece.ItemId));
                    await Rcd3Wait(0.4);
                }
                var pup = simulation.Pets.FirstOrDefault();
                if (pup is not null)
                {
                    for (int ask = 0; ask < 6 && simulation.Pets.FirstOrDefault()?.Order != PetOrder.WaitHere; ask++)
                    {
                        _session.Submit(new OrderPetCommand(simulation.PlayerId, pup.Id, PetOrder.WaitHere, pup.Body.XMm, pup.Body.ZMm));
                        await Rcd3Wait(0.8);
                    }
                    await Rcd3Wait(2.0);
                    await WalkOff(10);
                    await Rcd3Wait(1.0);
                    await ReviewStill("review2_ring", simulation.Pets.FirstOrDefault()?.Body, side: 5.5, along: 5.0, height: 2.4, look: 0.9);
                }
                break;
            }
            case 3:
            {
                // The fight: the animal goes for the wolf, and its bite lands.
                if (simulation.Pets.FirstOrDefault() is { } pup && NearestReviewWolf(simulation) is { } wolf)
                {
                    _session.Submit(new OrderPetCommand(simulation.PlayerId, pup.Id, PetOrder.AttackTarget, wolf.Body.XMm, wolf.Body.ZMm));
                    for (int round = 0; round < 40 && !simulation.Pets.Any(p => p.Condition == PetCondition.Wounded); round++)
                    {
                        if (round % 6 == 0 && simulation.Pets.FirstOrDefault() is { } again && NearestReviewWolf(simulation) is { } target)
                            _session.Submit(new OrderPetCommand(simulation.PlayerId, again.Id, PetOrder.AttackTarget, target.Body.XMm, target.Body.ZMm));
                        if (_reviewLog.Any(l => l.StartsWith("Lure") == false && l.Contains("Bite")))
                        {
                            await ReviewStill("review3_pup_bites", NearestReviewWolf(simulation)?.Body,
                                side: 2.4, along: 2.2, height: 0.55, look: 0.45);
                            break;
                        }
                        await Rcd3Wait(0.7);
                    }
                }
                break;
            }
            case 4:
            {
                // Struck until it goes home: the record stays, the animal is at home mending, and the HUD says so.
                for (int round = 0; round < 60 && !simulation.Pets.Any(p => p.Condition == PetCondition.Wounded); round++)
                {
                    if (simulation.Pets.FirstOrDefault() is { } pup && NearestReviewWolf(simulation) is { } wolf)
                        _session.Submit(new OrderPetCommand(simulation.PlayerId, pup.Id, PetOrder.AttackTarget, wolf.Body.XMm, wolf.Body.ZMm));
                    await Rcd3Wait(1.0);
                }
                await ReviewStill("review4_wounded_home", null, side: 1.5, along: 4.0, height: 1.7, look: 0.9);
                File.WriteAllLines(Path.Combine(_reviewDirectory!, "review.log"), _reviewLog);
                break;
            }
            default:
                GD.Print("PET_REVIEW complete: " + string.Join(" | ", _reviewLog));
                CaptureQuit.At(this, 0);
                break;
        }
        _reviewNext = Time.GetTicksMsec() + 400;
    }

    /// <summary>The nearest wolf of the fixture, which is the one placed for the beat.</summary>
    private static CreatureView? NearestReviewWolf(Simulation simulation) => simulation.Creatures
        .Where(c => c.Alive && c.DefId == ReviewWolf)
        .OrderBy(c => Distance(c.Body, simulation.Player.Body)).FirstOrDefault();

    /// <summary>Walk straight back from where the character stands, so the animal it left is behind it.</summary>
    private async Task WalkOff(double metres)
    {
        var simulation = _session.Simulation!;
        var body = simulation.Player.Body;
        double heading = body.FacingMdeg / 1000.0 * Math.PI / 180;
        double x = body.XMm - Math.Sin(heading) * metres * 1000;
        double z = body.ZMm - Math.Cos(heading) * metres * 1000;
        for (int step = 0; step < 60; step++)
        {
            var now = simulation.Player.Body;
            double dx = x - now.XMm, dz = z - now.ZMm;
            if (Math.Sqrt(dx * dx + dz * dz) < 800)
                break;
            _controller.SteerWorld(new Vector3((float)dx, 0, (float)dz).Normalized(), Gait.Walk, _camera, faceCamera: true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
    }

    /// <summary>
    /// A still with the camera placed by hand: on the side of the subject, [side] metres off, at the subject's height, looking at it - and where a
    /// second body matters (the animal on its ring with the character beyond it, the pup at the wolf) the camera stands so both are in the frame.
    /// </summary>
    private async Task ReviewStill(string name, Body? subject = null, double side = 0, double height = 1.6, double along = 3.0, double look = 0.35)
    {
        var simulation = _session.Simulation;
        if (simulation is null || _reviewDirectory is null)
            return;
        var target = subject is { } one
            ? Greybox.HollowView.ToGodot(one.XMm, one.YMm, one.ZMm)
            : Greybox.HollowView.ToGodot(simulation.Player.Body.XMm, simulation.Player.Body.YMm, simulation.Player.Body.ZMm);
        _reviewCamera ??= new Camera3D { Name = "ReviewCamera", Fov = 55 };
        if (_reviewCamera.GetParent() is null)
            AddChild(_reviewCamera);
        // A constant transform per beat: so many metres along X and Z from the subject, so high, looking at that height on it. Deriving it from
        // facings and body vectors put the camera inside a wolf's muzzle and across fifteen metres of grass (measured over five runs).
        _reviewCamera.GlobalPosition = target + new Vector3((float)side, (float)height, (float)along);
        _reviewCamera.LookAt(target + new Vector3(0, (float)look, 0), Vector3.Up);
        var play = GetViewport().GetCamera3D();
        _reviewCamera.MakeCurrent();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_reviewDirectory, name + ".png"));
        if (play is not null)
            play.MakeCurrent();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
