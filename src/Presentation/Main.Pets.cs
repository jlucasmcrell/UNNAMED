// UNNAMED Presentation - the animals' orders (spec 43 and its owner rulings, 4: the tactical commands are the design goal)
// Godot presentation only: it names a place on the ground, shows it, and submits the order the player's key chose (D-11)

using Godot;
using UNNAMED.Domain.Pets;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

public partial class Main
{
    /// <summary>
    /// The six orders the key offers, in the order the number keys give them: the mark's meaning is the number. Three name a place -
    /// wait here, guard here, lure to here - and the ground marker is where the one chosen would put it.
    /// </summary>
    private static readonly (PetOrder Order, string Label, string What)[] PetCommands =
    {
        (PetOrder.WaitHere, "Wait here", "it holds this spot"),
        (PetOrder.LureToHere, "Lure to here", "it draws one enemy to this spot"),
        (PetOrder.Pull, "Pull one", "it takes one enemy and brings it back"),
        (PetOrder.Guard, "Guard here", "it holds this spot and meets what comes"),
        (PetOrder.Follow, "Follow me", "it comes back to you"),
        (PetOrder.AttackTarget, "Attack my target", "it takes what you struck"),
    };

    /// <summary>How far ahead of the character the ground marker is laid: a place to walk to, not one on top of the character.</summary>
    private const long PetMarkerReachMm = 9_000;

    private bool _petOrdersOpen;
    private Node3D? _petMarker;
    private MeshInstance3D? _petMarkerDisc;

    /// <summary>The marker, made once: a flat ring on the ground where the order would land. The list itself is the HUD's, not the world's.</summary>
    private void InstallPetOrders()
    {
        var disc = new MeshInstance3D
        {
            Name = "PetOrderMarker",
            Mesh = new CylinderMesh { TopRadius = 0.9f, BottomRadius = 0.9f, Height = 0.04f, RadialSegments = 40 },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.62f, 0.86f, 0.42f, 0.5f),
                NoDepthTest = false,
            },
            Visible = false,
        };
        AddChild(disc);
        _petMarkerDisc = disc;

        _petMarker = disc;
    }

    /// <summary>
    /// Each frame the marker follows the point the camera's own heading would name, on the ground, while the list is up - the same
    /// projection the bow's reticle uses, so the place marked is where the character can see it.
    /// </summary>
    private void TickPetOrders()
    {
        if (_petMarkerDisc is null)
            return;
        if (!_petOrdersOpen || _session.Simulation is not { } simulation)
        {
            _petMarkerDisc.Visible = false;
            return;
        }
        int facing = PlayerController.FacingOf(_camera.GroundForward);
        var (x, z, _) = simulation.Aim(facing, PetMarkerReachMm);
        var terrain = _session.Setup.Layout.Space.Terrain;
        float y = terrain.HeightAtMm(x, z) / 1000f;
        _petMarkerDisc.GlobalPosition = new Vector3(x / 1000f, y + 0.03f, z / 1000f);
        _petMarkerDisc.Visible = true;
    }

    /// <summary>The list comes up, and the marker with it: nothing is ordered until a number is given.</summary>
    private void OpenPetOrders()
    {
        var simulation = _session.Simulation;
        if (simulation is null)
            return;
        if (!simulation.Pets.Any(p => p.Condition == PetCondition.Up))
        {
            _hud.Toast("No animal is with you", 2);
            return;
        }
        _petOrdersOpen = true;
        _hud.Toast("Command the animal: 1 wait here, 2 lure to here, 3 pull one, 4 guard here, 5 follow, 6 attack my target", 5);
    }

    private void ClosePetOrders() => _petOrdersOpen = false;

    /// <summary>A number while the list is up: the order that number named, to the first animal on its feet.</summary>
    private void ReadPetOrderKeys()
    {
        if (Input.IsActionJustPressed("ui_cancel") || Input.IsActionJustPressed("pet_orders"))
        {
            ClosePetOrders();
            return;
        }
        for (int i = 0; i < PetCommands.Length; i++)
        {
            if (!Input.IsActionJustPressed($"reply_{i + 1}"))
                continue;
            OrderPet(PetCommands[i].Order);
            ClosePetOrders();
            return;
        }
    }

    /// <summary>
    /// Give the order to the nearest animal on its feet, naming the ground point for the three that use one. A refusal is the
    /// simulation's own words, said as it said them.
    /// </summary>
    private void OrderPet(PetOrder order)
    {
        var simulation = _session.Simulation;
        if (simulation is null)
            return;
        var pet = simulation.Pets.Where(p => p.Condition == PetCondition.Up)
            .OrderBy(p => Distance(p.Body, simulation.Player.Body)).FirstOrDefault();
        if (pet is null)
        {
            _hud.Toast("No animal is with you", 2);
            return;
        }
        var (x, z) = PointMarked(order) ?? (0, 0);
        _session.Submit(new UNNAMED.World.Runtime.OrderPetCommand(simulation.PlayerId, pet.Id, order, x, z));
        _hud.Toast($"{pet.Name}: {PetCommands.First(c => c.Order == order).What}", 2);
    }

    /// <summary>Where the marker stands for an order, or null for the ones that name no place.</summary>
    private (long XMm, long ZMm)? PointMarked(PetOrder order)
    {
        if (_petMarkerDisc is null || _session.Simulation is not { } simulation)
            return null;
        if (!_petOrdersOpen)
            return null;
        // The three that name a place use the point the marker was showing when the number was given.
        if (order is not (PetOrder.WaitHere or PetOrder.LureToHere or PetOrder.Guard))
            return null;
        int facing = PlayerController.FacingOf(_camera.GroundForward);
        var (x, z, _) = simulation.Aim(facing, PetMarkerReachMm);
        return (x, z);
    }

    /// <summary>"1.5 days" - how long a pet is home badly wounded, for the HUD.</summary>
    private string DaysAway(long untilTick)
    {
        var simulation = _session.Simulation!;
        double perDay = Math.Max(1, _session.Setup.Clock.TicksPerDay);
        double days = Math.Max(0, untilTick - simulation.WorldTick) / perDay;
        if (days >= 1)
            return $"{days:0.#} d";
        double hours = days * 24;
        return $"{hours:0} h";
    }

    private static double Distance(Body a, Body b) => Math.Sqrt(Math.Pow(a.XMm - b.XMm, 2) + Math.Pow(a.ZMm - b.ZMm, 2));
}
