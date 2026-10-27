// UNNAMED Presentation - input to commands, and prediction for feel (D-11, CAMERA_PERSPECTIVE_AND_PRESENTATION.md §6)
// Godot presentation only: no gameplay state lives here

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain;
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
    Waystone,
    Portal,
    Readable,
    Water,
}

/// <summary>
/// What the player means to use: a door, a container, an item lying in the world (and its quality), a resource node (its
/// definition), a crafting station (its kind), a named NPC (M4), a switch not yet set, a barrier standing in the way (M6), or a Waystone (S-38).
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
    }

    public void OnBodyMoved(BodyMoved moved) => _motion.OnBodyMoved(moved);

    /// <summary>Back at the Waystone, standing (M-05, L-14): a key still held is sent again, and the jump there is not a step.</summary>
    public void OnRespawned(PlayerRespawned respawned) => _motion.OnRespawned(respawned);

    /// <summary>Set down at the far stone (S-38), as at a respawn.</summary>
    public void OnTravelled(PlayerTravelled travelled) => _motion.OnTravelled(travelled);

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
        if (_session.Simulation!.Combat.Defeated)
            direction = Vector3.Zero;   // dead: input is locked until the respawn
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

    /// <summary>A knife into a creature that has not noticed the character (STEALTH §12): the simulation decides reach and whether it lives.</summary>
    public void Takedown() => _session.Submit(new TakedownCommand(_session.Simulation!.PlayerId));

    /// <summary>Jump (the owner's M6 playtest): the simulation decides whether there is room, and the arc.</summary>
    public void Jump() => _session.Submit(new JumpCommand(_session.Simulation!.PlayerId));

    /// <summary>Crouch, or stand where there is room to; gliding, flare the wings, or spread them again.</summary>
    public void Crouch(bool crouched) => _session.Submit(new CrouchCommand(_session.Simulation!.PlayerId, crouched));

    /// <summary>Spread the wings in the air, or fold them (Kal): the simulation decides whether there are wings, and stamina to hold them.</summary>
    public void Glide(bool spread) => _session.Submit(new GlideCommand(_session.Simulation!.PlayerId, spread));

    /// <summary>Use a racial ability (an Orenth's blink, a Siann's ring): the simulation decides whether it is ready and what it costs.</summary>
    public void PeopleAbility(string abilityId) => _session.Submit(new PeopleAbilityCommand(_session.Simulation!.PlayerId, abilityId));

    /// <summary>Begin a working (M3e): its tell, then its release.</summary>
    public void Cast(string formulaId) => _session.Submit(new CastCommand(_session.Simulation!.PlayerId, formulaId));

    /// <summary>Begin a technique (WP9, D11): the weapon in hand's own blow, changed.</summary>
    public void Technique(string techniqueId) => _session.Submit(new TechniqueCommand(_session.Simulation!.PlayerId, techniqueId));

    /// <summary>
    /// The formulas the character knows (the harnesses work them by ID; a player works them from the quick bar), in the order they were learned - and formulas learned from
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

    /// <summary>Use the first carried stack of an item (a quick slot's salve, WP9); false when none is carried.</summary>
    public bool UseItem(string defId)
    {
        var simulation = _session.Simulation!;
        if (simulation.Player.Inventory.FirstOrDefault(e => e.DefId == defId) is not { } item)
            return false;
        _session.Submit(new UseItemCommand(simulation.PlayerId, item.ItemId));
        return true;
    }

    /// <summary>Use a carried healing item (the salve) if there is one, else the first carried item that has a use. False when there is none.</summary>
    public bool UseConsumable()
    {
        var simulation = _session.Simulation!;
        var combat = _session.Setup.Combat;
        bool Heals(string defId) => combat.UseEffects.TryGetValue(defId, out var effect) && combat.Effects.TryGetValue(effect, out var definition) && definition.HealPerTick > 0;
        var carried = simulation.Player.Inventory.Where(e => combat.UseEffects.ContainsKey(e.DefId))
            .OrderByDescending(e=>combat.Effects[combat.UseEffects[e.DefId]].HealPerTick).ToList();
        if ((carried.FirstOrDefault(e => Heals(e.DefId)) ?? carried.FirstOrDefault()) is not { } item)
            return false;
        _session.Submit(new UseItemCommand(simulation.PlayerId, item.ItemId));
        return true;
    }

    /// <summary>
    /// What the player means to use: within reach of the body (the rule the simulation applies), and the one the camera
    /// faces most directly. In first person it must be roughly under the crosshair.
    /// </summary>
    public static bool IsPickup(ContainerSite site) => site.StackSlots == 1 && site.DropLevel is null;

    public Focus? FocusOn(CameraRig camera)
    {
        var simulation = _session.Simulation!;
        long doorReach = _session.Setup.Movement.InteractReachMm, itemReach = _session.Setup.Items.Inventory.ReachMm;
        var candidates = new List<(Focus Focus, double Distance)>();
        foreach (var door in simulation.ActiveLayout.Doors)
        {
            candidates.Add((new Focus(FocusKind.Door, door.Key, door.FlagId, door.ClosedFootprint.CenterXMm, door.ClosedFootprint.CenterZMm),
                door.ClosedFootprint.DistanceTo(Authoritative.XMm, Authoritative.ZMm) - doorReach));
        }
        foreach (var site in simulation.Containers.Where(c => !(IsPickup(c.Site) && c.Items.IsEmpty)).Select(c => c.Site).Where(s =>
                     simulation.ActiveLayout.Containers.Contains(s) || simulation.Creatures.Any(c => c.CorpseKey == s.Key)))
        {
            // A corpse is searched like a chest, and named for the creature it was.
            string defId = simulation.Creatures.FirstOrDefault(c => c.CorpseKey == site.Key)?.DefId ?? site.Key;
            candidates.Add((new Focus(FocusKind.Container, site.Key, defId, site.XMm, site.ZMm), Distance(site.XMm, site.ZMm) - itemReach));
        }
        foreach (var item in simulation.WorldItems)
            candidates.Add((new Focus(FocusKind.Item, item.Id.Value, item.DefId, item.XMm, item.ZMm, item.Quality), Distance(item.XMm, item.ZMm) - itemReach));
        // Gathering and crafting (M3f) are measured like picking up: from the body, at the same reach.
        foreach (var node in simulation.Nodes.Where(n => n.Space == simulation.ActiveLayout.SpaceId))
            candidates.Add((new Focus(FocusKind.Node, node.Key, node.NodeDefId, node.XMm, node.ZMm), Distance(node.XMm, node.ZMm) - itemReach));
        foreach (var station in simulation.Stations)
            candidates.Add((new Focus(FocusKind.Station, station.Key, station.Kind, station.XMm, station.ZMm), Distance(station.XMm, station.ZMm) - itemReach));
        if(simulation.CanRefillFlask && simulation.Player.Inventory.Any(i=>i.DefId=="item.tool.water_flask_empty"))
            candidates.Add((new Focus(FocusKind.Water,"water","water",Authoritative.XMm,Authoritative.ZMm),-itemReach));
        // An NPC is spoken to within a hand's reach of their body (M4), and not through a wall: the rule the simulation applies.
        long talkReach = itemReach + _session.Setup.Movement.BodyRadiusMm;
        foreach (var npc in simulation.Npcs.Where(n => n.Space == simulation.ActiveLayout.SpaceId && !simulation.Walled(Authoritative.XMm, Authoritative.ZMm, n.Body.XMm, n.Body.ZMm)))
            candidates.Add((new Focus(FocusKind.Npc, npc.Id, npc.Id, npc.Body.XMm, npc.Body.ZMm), Distance(npc.Body.XMm, npc.Body.ZMm) - talkReach));
        // A switch is worked like a door, from the body to its edge (M6); once set it has nothing more to offer.
        foreach (var view in simulation.Switches.Where(s => !s.Set && simulation.ActiveLayout.Switches.Contains(s.Site)))
        {
            var (x, z) = Footprints.Center(view.Site.Body);
            candidates.Add((new Focus(FocusKind.Switch, view.Site.Key, view.Site.FlagId, x, z), view.Site.Body.DistanceTo(Authoritative.XMm, Authoritative.ZMm) - doorReach));
        }
        // A Waystone is touched like a switch is worked, from the body to its edge (S-38); a known one opens its panel.
        foreach (var view in simulation.Waystones)
        {
            var (x, z) = Footprints.Center(view.Site.Body);
            candidates.Add((new Focus(FocusKind.Waystone, view.Site.Key, view.Site.Key, x, z), view.Site.Body.DistanceTo(Authoritative.XMm, Authoritative.ZMm) - doorReach));
        }
        // Something written - a carved panel, a book - is read from where a switch is worked (spec 41).
        foreach (var readable in simulation.ActiveLayout.Readables)
        {
            var (x, z) = Footprints.Center(readable.Body);
            candidates.Add((new Focus(FocusKind.Readable, readable.Key, readable.EntryId, x, z), readable.Body.DistanceTo(Authoritative.XMm, Authoritative.ZMm) - doorReach));
        }
        // A portal: the door out of the exterior, or out of the space the body is in. It stands at its own anchor -
        // the exterior's while the body is outside, the interior's while it is in - so the same gesture works both
        // ways and the body is never offered a door it cannot reach (B1).
        bool outside = simulation.PlayerInExterior;
        foreach (var portal in simulation.Setup.Layout.Portals.Portals)
        {
            // Only the doors of the space the body is in, and only those still there to be used.
            if (!outside && portal.Space != simulation.ActiveLayout.SpaceId || simulation.PortalGone(portal) || outside && !simulation.PortalOpen(portal))
                continue;
            long px = outside ? portal.ExteriorXMm : portal.InteriorXMm;
            long pz = outside ? portal.ExteriorZMm : portal.InteriorZMm;
            var foot = new CircleBlocker(portal.Key, px, pz, 1_500, 2_200);
            candidates.Add((new Focus(FocusKind.Portal, portal.Key, portal.Key, px, pz),
                foot.DistanceTo(Authoritative.XMm, Authoritative.ZMm) - doorReach));
        }

        // A standing barrier cannot be used, but whoever stands at it is told what it is.
        foreach (var view in simulation.Barriers.Where(b => b.Standing && simulation.ActiveLayout.Barriers.Contains(b.Site)))
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
        var simulation = _session.Simulation!;
        // A portal is not an interaction: it moves the body between spaces, and the simulation decides by where it
        // stands whether that is going in or coming out. The key is the whole of what the player names.
        if (doorKey.StartsWith("portal.", StringComparison.Ordinal))
        {
            if (simulation.PlayerInExterior)
                _session.Submit(new EnterSpaceCommand(simulation.PlayerId, doorKey));
            else
                _session.Submit(new LeaveSpaceCommand(simulation.PlayerId));
            Reached?.Invoke();
            return;
        }
        _session.Submit(new InteractCommand(simulation.PlayerId, doorKey));
        Reached?.Invoke();
    }

    /// <summary>Speak to an NPC within reach (M4).</summary>
    public void Talk(string npcId) => _session.Submit(new TalkCommand(_session.Simulation!.PlayerId, npcId));

    /// <summary>
    /// Whether an NPC is near enough to talk to, by the simulation's measure (a hand's reach plus the body), a step inside it. The harnesses
    /// read it: someone who walks their day (WP12) may come to stand nearer than the spot a harness was making for.
    /// </summary>
    public bool InTalkReach(NpcView npc)
    {
        var body = Authoritative;
        long dx = body.XMm - npc.Body.XMm, dz = body.ZMm - npc.Body.ZMm, reach = _session.Setup.Items.Inventory.ReachMm + _session.Setup.Movement.BodyRadiusMm - 100;
        return dx * dx + dz * dz <= reach * reach;
    }

    /// <summary>Tell a companion to follow or to wait (M6).</summary>
    public void Order(string npcId, Domain.Companions.CompanionOrder order) =>
        _session.Submit(new OrderCompanionCommand(_session.Simulation!.PlayerId, npcId, order));

    /// <summary>A hand on a Waystone (S-38): it is known from now on, or - dormant, its rite heard - woken.</summary>
    public void TouchStone(string stoneKey)
    {
        _session.Submit(new TouchStoneCommand(_session.Simulation!.PlayerId, stoneKey));
        Reached?.Invoke();
    }

    /// <summary>Make the stone the character stands at their return point: its own act, never travel's.</summary>
    public void BindStone(string stoneKey) => _session.Submit(new BindStoneCommand(_session.Simulation!.PlayerId, stoneKey));

    /// <summary>Go by the stones, from the one the character stands at to another they know.</summary>
    public void TravelTo(string fromKey, string toKey) => _session.Submit(new TravelCommand(_session.Simulation!.PlayerId, fromKey, toKey));

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
