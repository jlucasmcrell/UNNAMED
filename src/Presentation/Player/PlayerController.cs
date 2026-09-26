// UNNAMED Presentation - input to commands, and prediction for feel (D-11, CAMERA_PERSPECTIVE_AND_PRESENTATION.md §6)
// Godot presentation only: no gameplay state lives here

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Player;

public enum FocusKind
{
    Door,
    Container,
    Item,
    Node,
    Station,
    Npc,
    Switch,
    Barrier,
}

/// <summary>
/// What the player means to use: a door, a container, an item lying in the world (and its quality), a resource node (its
/// definition), a crafting station (its kind), a named NPC (M4), a switch not yet set, or a barrier standing in the way (M6).
/// </summary>
public sealed record Focus(FocusKind Kind, string Key, string DefId, long XMm, long ZMm, int Quality = 0);

/// <summary>
/// Turns the player's wishes into the same commands at every camera distance - a movement intent, an interaction, and
/// (M3c) an attack, a guard, a dodge or a use - and draws the body a fraction of a tick ahead of the simulation with the
/// simulation's own movement function. It keeps a copy of what it was told (events and the load snapshot), never the
/// truth, and writes nothing.
/// </summary>
public sealed class PlayerController
{
    private readonly GameSession _session;
    private readonly Dictionary<string, bool> _open = new(StringComparer.Ordinal);
    private readonly PlayerMotion _motion;

    public PlayerController(GameSession session)
    {
        _session = session;
        _motion = new PlayerMotion(session);
    }

    /// <summary>The body as of the last simulated tick.</summary>
    public Body Authoritative => _motion.Body;

    public MoveIntent Intent => _motion.Sent;

    /// <summary>Take a fresh copy after a new game or a load, the only times presentation reads the whole state.</summary>
    public void Resync()
    {
        var simulation = _session.Simulation!;
        _motion.Resync();
        _open.Clear();
        foreach (var door in simulation.Doors)
            _open[door.Site.Key] = door.Open;
        foreach (var piece in simulation.Pieces.Where(p => p.Family == PieceFamily.Door))
            _open[piece.Id.Value] = piece.DoorOpen;
    }

    public void OnBodyMoved(BodyMoved moved) => _motion.OnBodyMoved(moved);

    /// <summary>Back at the Waystone, standing (M-05, L-14): a key still held is sent again, and the jump there is not a step.</summary>
    public void OnRespawned(PlayerRespawned respawned) => _motion.OnRespawned(respawned);

    public void OnDoorToggled(DoorToggled toggled) => _open[toggled.DoorKey] = toggled.Open;

    public bool IsOpen(string doorKey) => _open.GetValueOrDefault(doorKey);

    /// <summary>
    /// Submit a movement intent if the wish changed. <paramref name="stick"/> is camera-relative (x right, y forward).
    /// In first person the body faces where the camera looks; in third person it faces where it walks, except while it
    /// fights (<paramref name="faceCamera"/>: a swing, a raised guard, a drawn bow go where the camera looks).
    /// </summary>
    public void Steer(CameraRig camera, Vector2 stick, Gait gait, bool faceCamera = false)
    {
        if (stick.LengthSquared() > 1)
            stick = stick.Normalized();
        SteerWorld(camera.GroundForward * stick.Y + camera.GroundRight * stick.X, gait, camera, faceCamera);
    }

    /// <summary>Submit a world-space movement wish (length at most 1) if it changed. Scripted runs steer this way.</summary>
    public void SteerWorld(Vector3 direction, Gait gait, CameraRig camera, bool faceCamera = false)
    {
        int facing = camera.IsFirstPerson || faceCamera
            ? FacingOf(camera.GroundForward)
            : direction.LengthSquared() > 0.0001f ? FacingOf(direction) : _motion.Sent.FacingMdeg;
        var wish = new MoveIntent(
            (int)Mathf.Round(direction.X * MoveIntent.FullDeflection), (int)Mathf.Round(direction.Z * MoveIntent.FullDeflection), gait, facing);
        if (_motion.Send(wish, precise: faceCamera))
            _session.Submit(new MoveCommand(_session.Simulation!.PlayerId, wish));
    }

    /// <summary>Where to draw the body this frame: the last tick advanced by the frame's share of the next (<see cref="PlayerMotion.Predict"/>).</summary>
    public Body Predict(double alpha) => _motion.Predict(alpha);

    public void Attack() => _session.Submit(new AttackCommand(_session.Simulation!.PlayerId));

    /// <summary>Jump (the owner's M6 playtest): the simulation decides whether there is room, and the arc.</summary>
    public void Jump() => _session.Submit(new JumpCommand(_session.Simulation!.PlayerId));

    /// <summary>Crouch, or stand where there is room to.</summary>
    public void Crouch(bool crouched) => _session.Submit(new CrouchCommand(_session.Simulation!.PlayerId, crouched));

    /// <summary>Begin a working (M3e): its tell, then its release.</summary>
    public void Cast(string formulaId) => _session.Submit(new CastCommand(_session.Simulation!.PlayerId, formulaId));

    /// <summary>
    /// The formulas on keys 4 to 6: those the character knows, in the order they were learned - and formulas learned from
    /// one book in the book's own order. No formula is named here; the knowledge record and the content decide.
    /// </summary>
    public string[] Formulas()
    {
        var magic = _session.Setup.Magic;
        var known = _session.Simulation!.Player.Progression.Known;
        return known.Where(k => magic.Formulas.ContainsKey(k.Key))
            .OrderBy(k => k.Value.Tick)
            .ThenBy(k => k.Value.SourceRef is { } book && magic.Teaches.TryGetValue(book, out var taught) && taught.Contains(k.Key)
                ? taught.IndexOf(k.Key)
                : int.MaxValue)
            .ThenBy(k => k.Key, StringComparer.Ordinal)
            .Select(k => k.Key)
            .ToArray();
    }

    public void Guard(bool raised) => _session.Submit(new BlockCommand(_session.Simulation!.PlayerId, raised));

    /// <summary>Dodge along a world-space direction; no direction dodges backwards.</summary>
    public void Dodge(Vector3 direction)
    {
        _motion.OnDodgeAsked();
        _session.Submit(new DodgeCommand(_session.Simulation!.PlayerId, (int)Mathf.Round(direction.X * 1000), (int)Mathf.Round(direction.Z * 1000)));
    }

    /// <summary>Use the first carried item that has a use (the salve). False when there is none.</summary>
    public bool UseConsumable()
    {
        var simulation = _session.Simulation!;
        var uses = _session.Setup.Combat.UseEffects;
        if (simulation.Player.Inventory.FirstOrDefault(e => uses.ContainsKey(e.DefId)) is not { } item)
            return false;
        _session.Submit(new UseItemCommand(simulation.PlayerId, item.ItemId));
        return true;
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
                door.ClosedFootprint.DistanceTo(Authoritative.XMm, Authoritative.ZMm) - doorReach));
        }
        // A placed door (M7) is worked the same way, from the body to its shut leaf.
        foreach (var piece in simulation.Pieces.Where(p => p.Family == PieceFamily.Door))
        {
            var leaf = new BoxBlocker(piece.Id.Value, piece.MinXMm, piece.MinZMm, piece.MaxXMm, piece.MaxZMm, 0);
            candidates.Add((new Focus(FocusKind.Door, piece.Id.Value, piece.DefId, leaf.CenterXMm, leaf.CenterZMm),
                leaf.DistanceTo(Authoritative.XMm, Authoritative.ZMm) - doorReach));
        }
        foreach (var site in simulation.Containers.Select(c => c.Site))
        {
            // A corpse is searched like a chest, and named for the creature it was; a placed chest (M7) for its piece.
            string defId = simulation.Creatures.FirstOrDefault(c => c.CorpseKey == site.Key)?.DefId
                ?? simulation.Pieces.FirstOrDefault(p => p.ContainerKey == site.Key)?.DefId ?? site.Key;
            candidates.Add((new Focus(FocusKind.Container, site.Key, defId, site.XMm, site.ZMm), Distance(site.XMm, site.ZMm) - itemReach));
        }
        foreach (var item in simulation.WorldItems)
            candidates.Add((new Focus(FocusKind.Item, item.Id.Value, item.DefId, item.XMm, item.ZMm, item.Quality), Distance(item.XMm, item.ZMm) - itemReach));
        // Gathering and crafting (M3f) are measured like picking up: from the body, at the same reach.
        foreach (var node in simulation.Nodes)
            candidates.Add((new Focus(FocusKind.Node, node.Key, node.NodeDefId, node.XMm, node.ZMm), Distance(node.XMm, node.ZMm) - itemReach));
        foreach (var station in simulation.Stations)   // the authored ones, then each placed bench (M7)
            candidates.Add((new Focus(FocusKind.Station, station.Key, station.Kind, station.XMm, station.ZMm), Distance(station.XMm, station.ZMm) - itemReach));
        // An NPC is spoken to within a hand's reach of their body (M4), and not through a wall: the rule the simulation applies.
        long talkReach = itemReach + _session.Setup.Movement.BodyRadiusMm;
        foreach (var npc in simulation.Npcs.Where(n => !simulation.Walled(Authoritative.XMm, Authoritative.ZMm, n.Body.XMm, n.Body.ZMm)))
            candidates.Add((new Focus(FocusKind.Npc, npc.Id, npc.Id, npc.Body.XMm, npc.Body.ZMm), Distance(npc.Body.XMm, npc.Body.ZMm) - talkReach));
        // A switch is worked like a door, from the body to its edge (M6); once set it has nothing more to offer.
        foreach (var view in simulation.Switches.Where(s => !s.Set))
        {
            var (x, z) = Footprints.Center(view.Site.Body);
            candidates.Add((new Focus(FocusKind.Switch, view.Site.Key, view.Site.FlagId, x, z), view.Site.Body.DistanceTo(Authoritative.XMm, Authoritative.ZMm) - doorReach));
        }
        // A standing barrier cannot be used, but whoever stands at it is told what it is.
        foreach (var view in simulation.Barriers.Where(b => b.Standing))
        {
            var (x, z) = Footprints.Center(view.Site.Footprint);
            candidates.Add((new Focus(FocusKind.Barrier, view.Site.Key, view.Site.FlagId, x, z), view.Site.Footprint.DistanceTo(Authoritative.XMm, Authoritative.ZMm) - doorReach));
        }

        Focus? best = null;
        float bestAlignment = float.MinValue;
        foreach (var (focus, beyondReach) in candidates)
        {
            if (beyondReach > 0)
                continue;
            var to = new Vector3(focus.XMm - Authoritative.XMm, 0, focus.ZMm - Authoritative.ZMm);
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

    /// <summary>The body was asked to reach for something (a door or switch, a node, an item, a companion to help up): its figure shows it.</summary>
    public event Action? Reached;

    public void Interact(string doorKey)
    {
        _session.Submit(new InteractCommand(_session.Simulation!.PlayerId, doorKey));
        Reached?.Invoke();
    }

    /// <summary>Speak to an NPC within reach (M4).</summary>
    public void Talk(string npcId) => _session.Submit(new TalkCommand(_session.Simulation!.PlayerId, npcId));

    /// <summary>Tell a companion to follow or to wait (M6).</summary>
    public void Order(string npcId, Domain.Companions.CompanionOrder order) =>
        _session.Submit(new OrderCompanionCommand(_session.Simulation!.PlayerId, npcId, order));

    /// <summary>Help a downed companion up (M6).</summary>
    public void Revive(string npcId)
    {
        _session.Submit(new ReviveCommand(_session.Simulation!.PlayerId, npcId));
        Reached?.Invoke();
    }

    /// <summary>Harvest a node within reach (M3f).</summary>
    public void Gather(string nodeKey)
    {
        _session.Submit(new GatherCommand(_session.Simulation!.PlayerId, nodeKey));
        Reached?.Invoke();
    }

    /// <summary>Work a recipe at the station in reach (M3f).</summary>
    public void Craft(string recipeId) => _session.Submit(new CraftCommand(_session.Simulation!.PlayerId, recipeId));

    /// <summary>Place a piece at a pose on the building lattice (M7): the authority judges it, whatever the ghost showed.</summary>
    public void Place(string pieceDefId, long xMm, long zMm, int rotation) =>
        _session.Submit(new PlacePieceCommand(_session.Simulation!.PlayerId, pieceDefId, xMm, zMm, rotation));

    /// <summary>Take a piece down (M7).</summary>
    public void Dismantle(Domain.EntityId pieceId) => _session.Submit(new DismantlePieceCommand(_session.Simulation!.PlayerId, pieceId));

    public void Repair(Domain.EntityId pieceId) => _session.Submit(new RepairPieceCommand(_session.Simulation!.PlayerId, pieceId));

    /// <summary>The recipes the character knows that are worked at a station of this kind. None is named here.</summary>
    public IReadOnlyList<Domain.Crafting.RecipeDefinition> Recipes(string stationKind)
    {
        var known = _session.Simulation!.Player.Progression.Known;
        return _session.Setup.Crafting.Recipes.Values.Where(r => r.StationKind == stationKind && known.ContainsKey(r.Id)).ToList();
    }

    /// <summary>Pick up everything in a stack lying within reach.</summary>
    public void PickUp(string itemId)
    {
        var simulation = _session.Simulation!;
        if (simulation.WorldItems.FirstOrDefault(i => i.Id.Value == itemId) is { } item)
        {
            _session.Submit(new MoveItemCommand(simulation.PlayerId, itemId, ItemPlace.Ground, ItemPlace.Carried, item.Count));
            Reached?.Invoke();
        }
    }

    private double Distance(long xMm, long zMm)
    {
        double dx = Authoritative.XMm - xMm, dz = Authoritative.ZMm - zMm;
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
