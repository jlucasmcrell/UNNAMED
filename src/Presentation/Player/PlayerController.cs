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
    private Body _body = new(0, 0, 0, 0);
    private (long X, long Z) _lastStep;
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

    public void OnBodyMoved(BodyMoved moved)
    {
        _lastStep = (moved.To.XMm - moved.From.XMm, moved.To.ZMm - moved.From.ZMm);
        _body = moved.To;
    }

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
            : direction.LengthSquared() > 0.0001f ? FacingOf(direction) : _intent.FacingMdeg;
        var wish = new MoveIntent(
            (int)Mathf.Round(direction.X * MoveIntent.FullDeflection), (int)Mathf.Round(direction.Z * MoveIntent.FullDeflection), gait, facing);
        bool turned = Math.Abs(Mathf.Wrap(wish.FacingMdeg - _intent.FacingMdeg, -180_000, 180_000)) > 500;
        if (wish.DirXPermille == _intent.DirXPermille && wish.DirZPermille == _intent.DirZPermille && wish.Gait == _intent.Gait && !turned)
            return;
        _intent = wish;
        _session.Submit(new MoveCommand(_session.Simulation!.PlayerId, wish));
    }

    /// <summary>
    /// Where to draw the body this frame: the last tick advanced by the frame's fraction of the next one, moving the way
    /// the simulation will - a dodge carries on as it went, a stagger holds still, an attack or a guard walks, and an
    /// empty stamina pool runs rather than sprints.
    /// </summary>
    public Body Predict(double alpha)
    {
        var setup = _session.Setup;
        var simulation = _session.Simulation!;
        var combat = simulation.Combat;
        var intent = _intent;
        switch (combat.Phase)
        {
            case CombatPhase.Dodge:
                return _body with { XMm = _body.XMm + (long)(_lastStep.X * alpha), ZMm = _body.ZMm + (long)(_lastStep.Z * alpha) };
            case CombatPhase.Staggered:
            case CombatPhase.Recovery when combat.AttackSource is null:
                return _body;
            case CombatPhase.Windup or CombatPhase.Active or CombatPhase.Recovery:
                intent = intent with { Gait = Gait.Walk };
                break;
            default:
                if (combat.Blocking)
                    intent = intent with { Gait = Gait.Walk };
                else if (intent.Gait == Gait.Sprint && combat.Stamina == 0)
                    intent = intent with { Gait = Gait.Run };
                break;
        }
        return Kinematics.Step(_body, intent, setup.Movement, setup.Layout.Space, simulation.DynamicBlockers, (int)Math.Round(alpha * setup.TickMilliseconds));
    }

    public void Attack() => _session.Submit(new AttackCommand(_session.Simulation!.PlayerId));

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
    public void Dodge(Vector3 direction) =>
        _session.Submit(new DodgeCommand(_session.Simulation!.PlayerId, (int)Mathf.Round(direction.X * 1000), (int)Mathf.Round(direction.Z * 1000)));

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
                door.ClosedFootprint.DistanceTo(_body.XMm, _body.ZMm) - doorReach));
        }
        foreach (var site in simulation.Containers.Select(c => c.Site))
        {
            // A corpse is searched like a chest, and named for the creature it was.
            string defId = simulation.Creatures.FirstOrDefault(c => c.CorpseKey == site.Key)?.DefId ?? site.Key;
            candidates.Add((new Focus(FocusKind.Container, site.Key, defId, site.XMm, site.ZMm), Distance(site.XMm, site.ZMm) - itemReach));
        }
        foreach (var item in simulation.WorldItems)
            candidates.Add((new Focus(FocusKind.Item, item.Id.Value, item.DefId, item.XMm, item.ZMm, item.Quality), Distance(item.XMm, item.ZMm) - itemReach));
        // Gathering and crafting (M3f) are measured like picking up: from the body, at the same reach.
        foreach (var node in simulation.Nodes)
            candidates.Add((new Focus(FocusKind.Node, node.Key, node.NodeDefId, node.XMm, node.ZMm), Distance(node.XMm, node.ZMm) - itemReach));
        foreach (var station in _session.Setup.Layout.Stations)
            candidates.Add((new Focus(FocusKind.Station, station.Key, station.Kind, station.XMm, station.ZMm), Distance(station.XMm, station.ZMm) - itemReach));
        // An NPC is spoken to within a hand's reach of their body (M4), the rule the simulation applies.
        long talkReach = itemReach + _session.Setup.Movement.BodyRadiusMm;
        foreach (var npc in simulation.Npcs)
            candidates.Add((new Focus(FocusKind.Npc, npc.Id, npc.Id, npc.Body.XMm, npc.Body.ZMm), Distance(npc.Body.XMm, npc.Body.ZMm) - talkReach));
        // A switch is worked like a door, from the body to its edge (M6); once set it has nothing more to offer.
        foreach (var view in simulation.Switches.Where(s => !s.Set))
        {
            var (x, z) = Footprints.Center(view.Site.Body);
            candidates.Add((new Focus(FocusKind.Switch, view.Site.Key, view.Site.FlagId, x, z), view.Site.Body.DistanceTo(_body.XMm, _body.ZMm) - doorReach));
        }
        // A standing barrier cannot be used, but whoever stands at it is told what it is.
        foreach (var view in simulation.Barriers.Where(b => b.Standing))
        {
            var (x, z) = Footprints.Center(view.Site.Footprint);
            candidates.Add((new Focus(FocusKind.Barrier, view.Site.Key, view.Site.FlagId, x, z), view.Site.Footprint.DistanceTo(_body.XMm, _body.ZMm) - doorReach));
        }

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

    /// <summary>Speak to an NPC within reach (M4).</summary>
    public void Talk(string npcId) => _session.Submit(new TalkCommand(_session.Simulation!.PlayerId, npcId));

    /// <summary>Harvest a node within reach (M3f).</summary>
    public void Gather(string nodeKey) => _session.Submit(new GatherCommand(_session.Simulation!.PlayerId, nodeKey));

    /// <summary>Work a recipe at the station in reach (M3f).</summary>
    public void Craft(string recipeId) => _session.Submit(new CraftCommand(_session.Simulation!.PlayerId, recipeId));

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
