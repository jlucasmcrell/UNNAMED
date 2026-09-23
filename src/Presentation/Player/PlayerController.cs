// UNNAMED Presentation - input to commands, and prediction for feel (D-11, CAMERA_PERSPECTIVE_AND_PRESENTATION.md §6)
// Godot presentation only: no gameplay state lives here

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Player;

public enum FocusKind
{
    Door,
    Container,
    Item,
}

/// <summary>What the player means to use: a door, a container, or an item lying in the world.</summary>
public sealed record Focus(FocusKind Kind, string Key, string DefId, long XMm, long ZMm);

/// <summary>
/// Turns the player's wishes into the same two commands at every camera distance - a movement intent and an
/// interaction - and draws the body a fraction of a tick ahead of the simulation with the simulation's own movement
/// function. It keeps a copy of what it was told (events and the load snapshot), never the truth, and writes nothing.
/// </summary>
public sealed class PlayerController
{
    private readonly GameSession _session;
    private readonly Dictionary<string, bool> _open = new(StringComparer.Ordinal);
    private Body _body = new(0, 0, 0, 0);
    private MoveIntent _intent;

    public PlayerController(GameSession session) => _session = session;

    /// <summary>The body as of the last simulated tick.</summary>
    public Body Authoritative => _body;

    public MoveIntent Intent => _intent;

    /// <summary>Take a fresh copy after a new game or a load, the only times presentation reads the whole state.</summary>
    public void Resync()
    {
        var simulation = _session.Simulation!;
        _body = simulation.Player.Body;
        _intent = simulation.Player.Intent;
        _open.Clear();
        foreach (var door in simulation.Doors)
            _open[door.Site.Key] = door.Open;
    }

    public void OnBodyMoved(BodyMoved moved) => _body = moved.To;

    public void OnDoorToggled(DoorToggled toggled) => _open[toggled.DoorKey] = toggled.Open;

    public bool IsOpen(string doorKey) => _open.GetValueOrDefault(doorKey);

    /// <summary>
    /// Submit a movement intent if the wish changed. <paramref name="stick"/> is camera-relative (x right, y forward).
    /// In first person the body faces where the camera looks; in third person it faces where it walks.
    /// </summary>
    public void Steer(CameraRig camera, Vector2 stick, Gait gait)
    {
        if (stick.LengthSquared() > 1)
            stick = stick.Normalized();
        SteerWorld(camera.GroundForward * stick.Y + camera.GroundRight * stick.X, gait, camera);
    }

    /// <summary>Submit a world-space movement wish (length at most 1) if it changed. Scripted runs steer this way.</summary>
    public void SteerWorld(Vector3 direction, Gait gait, CameraRig camera)
    {
        int facing = camera.IsFirstPerson
            ? FacingOf(camera.GroundForward)
            : direction.LengthSquared() > 0.0001f ? FacingOf(direction) : _intent.FacingMdeg;
        var wish = new MoveIntent(
            (int)Mathf.Round(direction.X * MoveIntent.FullDeflection), (int)Mathf.Round(direction.Z * MoveIntent.FullDeflection), gait, facing);
        bool turned = Math.Abs(Mathf.Wrap(wish.FacingMdeg - _intent.FacingMdeg, -180_000, 180_000)) > 500;
        if (wish.DirXPermille == _intent.DirXPermille && wish.DirZPermille == _intent.DirZPermille && wish.Gait == _intent.Gait && !turned)
            return;
        _intent = wish;
        _session.Submit(new MoveCommand(_session.Simulation!.PlayerId, wish));
    }

    /// <summary>Where to draw the body this frame: the last tick advanced by the frame's fraction of the next one.</summary>
    public Body Predict(double alpha)
    {
        var setup = _session.Setup;
        var closed = setup.Layout.ClosedDoors(d => IsOpen(d.Key));
        return Kinematics.Step(_body, _intent, setup.Movement, setup.Layout.Space, closed, (int)Math.Round(alpha * setup.TickMilliseconds));
    }

    /// <summary>
    /// What the player means to use: within reach of the body (the rule the simulation applies), and the one the camera
    /// faces most directly. In first person it must be roughly under the crosshair.
    /// </summary>
    public Focus? FocusOn(CameraRig camera)
    {
        var simulation = _session.Simulation!;
        long doorReach = _session.Setup.Movement.InteractReachMm, itemReach = _session.Setup.Items.Inventory.ReachMm;
        var candidates = new List<(Focus Focus, double Distance)>();
        foreach (var door in _session.Setup.Layout.Doors)
        {
            candidates.Add((new Focus(FocusKind.Door, door.Key, door.FlagId, door.ClosedFootprint.CenterXMm, door.ClosedFootprint.CenterZMm),
                door.ClosedFootprint.DistanceTo(_body.XMm, _body.ZMm) - doorReach));
        }
        foreach (var site in _session.Setup.Layout.Containers)
            candidates.Add((new Focus(FocusKind.Container, site.Key, site.Key, site.XMm, site.ZMm), Distance(site.XMm, site.ZMm) - itemReach));
        foreach (var item in simulation.WorldItems)
            candidates.Add((new Focus(FocusKind.Item, item.Id.Value, item.DefId, item.XMm, item.ZMm), Distance(item.XMm, item.ZMm) - itemReach));

        Focus? best = null;
        float bestAlignment = float.MinValue;
        foreach (var (focus, beyondReach) in candidates)
        {
            if (beyondReach > 0)
                continue;
            var to = new Vector3(focus.XMm - _body.XMm, 0, focus.ZMm - _body.ZMm);
            float alignment = to.LengthSquared() < 1 ? 1 : to.Normalized().Dot(camera.GroundForward);
            if (camera.IsFirstPerson && alignment < 0.5f)
                continue;
            if (alignment > bestAlignment)
            {
                bestAlignment = alignment;
                best = focus;
            }
        }
        return best;
    }

    public void Interact(string doorKey) => _session.Submit(new InteractCommand(_session.Simulation!.PlayerId, doorKey));

    /// <summary>Pick up everything in a stack lying within reach.</summary>
    public void PickUp(string itemId)
    {
        var simulation = _session.Simulation!;
        if (simulation.WorldItems.FirstOrDefault(i => i.Id.Value == itemId) is { } item)
            _session.Submit(new MoveItemCommand(simulation.PlayerId, itemId, ItemPlace.Ground, ItemPlace.Carried, item.Count));
    }

    private double Distance(long xMm, long zMm)
    {
        double dx = _body.XMm - xMm, dz = _body.ZMm - zMm;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Millidegrees from +Z towards +X, the simulation's facing convention.</summary>
    public static int FacingOf(Vector3 direction)
    {
        int mdeg = (int)Mathf.Round(Mathf.RadToDeg(Mathf.Atan2(direction.X, direction.Z)) * 1000) % MoveIntent.FullTurnMdeg;
        return mdeg < 0 ? mdeg + MoveIntent.FullTurnMdeg : mdeg;
    }

    public static float FacingRadians(int facingMdeg) => Mathf.DegToRad(facingMdeg / 1000f);
}
