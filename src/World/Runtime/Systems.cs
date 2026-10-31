// UNNAMED World - the Phase-1 runtime systems (SYSTEMS.md S-04, S-08, S-20, S-21, S-22, S-30)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.Domain.Travel;

namespace UNNAMED.World.Runtime;

// Each system declares the state it owns in its header, writes only that through its SliceOwner, and reaches
// another system's state only by dispatching an internal command (ARCHITECTURE.md Â§4.2, Â§5).

/// <summary>A command one system sends another. Presentation cannot construct these.</summary>
internal abstract record InternalCommand;

/// <summary>To <see cref="WorldFlagSystem"/>: set a cell's world flag.</summary>
internal sealed record SetWorldFlag(CellKey Cell, string FlagId, long Value) : InternalCommand
{
    public bool Quiet { get; init; }
}

/// <summary>To <see cref="ProgressionSystem"/>: award level XP.</summary>
internal sealed record AwardExperience(XpAward Award) : InternalCommand;

/// <summary>To <see cref="LoreSystem"/>: a discovery happened, and the pages naming it open.</summary>
internal sealed record UnlockLore(EntityId Actor, Domain.Knowledge.LoreSourceKind Source, string SourceRef) : InternalCommand;

/// <summary>To <see cref="LoreSystem"/>: an entry was read outright, its every page not yet known filed (a book or a note).</summary>
internal sealed record ReadLore(EntityId Actor, string EntryId, Domain.Knowledge.LoreSourceKind Source, string SourceRef) : InternalCommand;

/// <summary>
/// To <see cref="DiscoverySystem"/>: a page said where a place is, so it goes on the map as told. Not a visit - no landmark banner, no
/// objective met, no discovery XP - because the character has been told about the place, not taken there (spec 41;
/// MAPS_CARTOGRAPHY_AND_WORLD_KNOWLEDGE.md Â§6).
/// </summary>
internal sealed record TellOfPlace(string LocationId) : InternalCommand;

/// <summary>To <see cref="LoreSystem"/>: a reply told the character of something, and the pages this conversation tells are filed.</summary>
internal sealed record TellLore(EntityId Actor, string EntryId, string DialogueId) : InternalCommand;

/// <summary>What systems share: the state, the rules, the event bus, and the internal command route.</summary>
internal sealed class SystemContext
{
    private readonly Dictionary<object, SpaceId> _siteSpaces = new();

    public SystemContext(RuntimeState state, SimulationSetup setup, IEventBus events, Func<InternalCommand, string?> dispatch)
    {
        State = state;
        Setup = setup;
        Events = events;
        Dispatch = dispatch;
        PlayerTraits = setup.Character.TraitsOf(state.PeopleId);
        PlayerAbilities = setup.Character.AbilitiesOf(state.PeopleId);
        PlayerMovement = PeopleRules.Movement(setup.Movement, PlayerTraits, PlayerAbilities);
        // Layouts are immutable for this context. Resolve authored sites once, preserving the
        // first matching layout, instead of rescanning every space for every collision probe.
        void Register<T>(Func<RegionLayout, IEnumerable<T>> sites) where T : notnull
        {
            foreach (var (layout, site) in setup.Sites(sites))
                _siteSpaces.TryAdd(site, layout.SpaceId);
        }
        Register(l => l.Doors);
        Register(l => l.Switches);
        Register(l => l.Barriers);
        Register(l => l.Readables);
    }

    public RuntimeState State { get; }
    public SimulationSetup Setup { get; }
    public IEnumerable<(RegionLayout Layout,StationSite Site)> Stations(EntityId player) =>
        Setup.Sites(l=>l.Stations).Concat(Camps.View(this,player) is { } camp
            ? new[] { (Setup.Layout,camp.Station) } : Enumerable.Empty<(RegionLayout,StationSite)>());
    public IEventBus Events { get; }

    /// <summary>The player's people's biology and aptitude (RACE_ABILITIES_PROPOSAL.md): fixed for the run, as the people is.</summary>
    public PeopleTraits PlayerTraits { get; }

    /// <summary>The player's racial abilities, in content order.</summary>
    public ImmutableArray<PeopleAbility> PlayerAbilities { get; }

    /// <summary>The movement rules the player's body moves by: the shared ones, at its people's speed, with its people's leap and wings.</summary>
    public MovementRules PlayerMovement { get; }

    /// <summary>An NPC's people, where the world knows it (a named NPC's <c>people_ref</c>, a role resident's slot, an adventurer); null for one it does not.</summary>
    public string? PeopleOf(string npcId) =>
        Setup.Social.Npcs.GetValueOrDefault(npcId)?.PeopleId ?? Setup.NpcLife.RoutineOf(npcId)?.PeopleId
        ?? State.World.Adventurers?.Agents.GetValueOrDefault(npcId)?.PeopleId;

    /// <summary>The player's derived pools and Resonance: the attributes' values, then the people's biology and the crossing toll's health.</summary>
    public DerivedStats PlayerStats() =>
        PeopleRules.Derive(ProgressionEngine.Derive(State.Progression, Setup.Progression), PlayerTraits, State.Crossing.Tier);

    /// <summary>Route an internal command to its owner; null when accepted, otherwise the reason.</summary>
    public Func<InternalCommand, string?> Dispatch { get; }

    /// <summary>
    /// What the effects on a body deny it (STEALTH Â§14's control effects): nothing, its feet, or its actions. The strongest effect on it
    /// wins, so a sleeper under a root is asleep. Read wherever movement or a blow is about to happen, which is what makes "rooted" and
    /// "paralysed" one rule rather than a special case per system.
    /// </summary>
    public ControlKind ControlOn(EntityId body)
    {
        if (State.Effects.Count == 0)
            return ControlKind.None;
        var kind = ControlKind.None;
        foreach (var active in State.Effects.GetValueOrDefault(body, ImmutableArray<ActiveEffect>.Empty))
        {
            var control = Setup.Combat.Effects.GetValueOrDefault(active.EffectId)?.Control ?? ControlKind.None;
            if (control > kind)
                kind = control;
        }
        return kind;
    }

    /// <summary>
    /// The name of an actor the world knows that is no NPC body - an adventurer out on a ground (WP13) - by instance ID; null for none. A
    /// board names who claimed a bounty with it. Composition sets it once.
    /// </summary>
    public Func<string, string?> OtherActorName { get; set; } = _ => null;

    /// <summary>The character's people, as the creator made it: who may use and trade heritage items (owner, 2026-10-24).</summary>
    public string PlayerPeople { get; init; } = CharacterSpec.DefaultPeople;

    /// <summary>
    /// The space this layout's sites stand in. Every authored site's cell is resolved in it, so a door's flag and
    /// the quest that reads that flag are always the same cell (review B2).
    /// </summary>
    private SpaceId SpaceOf(object site) => _siteSpaces.GetValueOrDefault(site, Setup.Layout.SpaceId);

    public bool IsOpen(DoorSite door) => State.World.GetFlag(Simulation.CellOf(SpaceOf(door), door), door.FlagId) != 0;

    /// <summary>Whether a door's lock still holds: no tier is no lock, and a picked lock's flag in the door's cell is its release.</summary>
    public bool Locked(DoorSite door) => door.LockTier > 0
        && State.World.GetFlag(Simulation.CellOf(SpaceOf(door), door), LockRules.FlagOf(door.Key)) == 0;

    /// <summary>The same for a container, whose open is recorded in its cell the same way.</summary>
    public bool Locked(ContainerSite site) => site.LockTier > 0
        && State.World.GetFlag(CellOfSite(ContainerSpace(site), site.XMm, site.ZMm), LockRules.FlagOf(site.Key)) == 0;

    /// <summary>The lock on the door or container a key names, by key: what the prompt reads and the pick is refused by. False for a key that is neither.</summary>
    public bool Locked(string siteKey) =>
        Setup.Sites(l => l.Doors).FirstOrDefault(p => p.Site.Key == siteKey).Site is { } door ? Locked(door)
        : Setup.Sites(l => l.Containers).FirstOrDefault(p => p.Site.Key == siteKey).Site is { } container && Locked(container);

    public bool IsSet(SwitchSite site) => State.World.GetFlag(Simulation.CellOf(SpaceOf(site), site), site.FlagId) != 0;

    internal const string InteriorTicksFlag = "world.space.elapsed_ticks";

    public CellKey ClockCell(SpaceId space)
    {
        var spawn = Setup.LayoutFor(space).Spawn;
        return CellOfSite(space, spawn.XMm, spawn.ZMm);
    }

    public long SpaceTick(SpaceId space, long worldTick) =>
        space.IsExterior ? worldTick : State.World.GetFlag(ClockCell(space), InteriorTicksFlag);

    public bool RequirementMet(SiteFlagRequirement requirement) =>
        Setup.Sites(l => l.Locations).FirstOrDefault(p => p.Site.Id == requirement.LocationId) is { Site: { } place, Layout: var home }
            && State.World.GetFlag(CellOfSite(home.SpaceId, place.XMm, place.ZMm), requirement.FlagId) != 0;

    public bool Excluded(SwitchSite site) => !site.Excludes.IsDefaultOrEmpty
        && site.Excludes.Any(flag => State.World.GetFlag(Simulation.CellOf(SpaceOf(site), site), flag) != 0);

    public bool IsLifted(BarrierSite barrier) => State.World.GetFlag(Simulation.CellOf(SpaceOf(barrier), barrier), barrier.FlagId) != 0;

    /// <summary>
    /// The cell a body stands in, in the space it is in (M8 D10). A cell is where a flag, a delta and an
    /// <see cref="RngChannel"/> seed live, so a body's own coordinates are not enough to name one: an
    /// interior's local metres would otherwise address real exterior ground.
    /// </summary>
    /// <remarks>
    /// This overload is for the player. Entities and authored sites use their own space. Unflagged interiors,
    /// including Barrowworks, hold their state while absent (systems plan Â§2.11).
    /// </remarks>
    public CellKey CellOf(Body body) => CellKey.OfInterior(State.Space, body.XMm / 1000.0, body.ZMm / 1000.0);

    /// <summary>
    /// The cell an authored site's coordinates name, in the space the layout itself stands in (M8 D10). An
    /// interior region's nodes, spawns, containers and named places are authored in its own local metres, so
    /// their cells must be qualified with the layout's space rather than the exterior.
    /// </summary>
    public CellKey CellOfSite(long xMm, long zMm) =>
        CellOfSite(Setup.Layout.SpaceId, xMm, zMm);

    /// <summary>
    /// The same, in the space the caller names. A site's space is its OWN layout's, never the player's: a ring place
    /// and a frontier place authored at the same metres are different cells, and taking the player's space put a
    /// loaded save's exterior NPCs underground and the ring's flags in the frontier (review 3, B8).
    /// </summary>
    public CellKey CellOfSite(SpaceId space, long xMm, long zMm) =>
        CellKey.OfInterior(space, xMm / 1000.0, zMm / 1000.0);

    /// <summary>Closed doors and standing barriers: the footprints whose passability is a world flag.</summary>
    public ImmutableArray<Blocker> ClosedDoors(SpaceId? space = null)
    {
        var layout = Setup.LayoutFor(space ?? State.Space);
        var doors = layout.ClosedDoors(IsOpen, IsLifted);
        return layout.Tide is { } tide
            ? doors.AddRange(tide.Passages.Where(p => !tide.Open(p, State.WorldTick)).Select(p => p.Footprint)) : doors;
    }

    /// <summary>An authored container, or a corpse lying where its creature fell (M3d).</summary>
    public ContainerSite? FindContainer(string key) =>
        Setup.LayoutFor(State.Space).FindContainer(key) ?? CorpseSites().FirstOrDefault(s => s.Key == key) ?? MerchantSites().FirstOrDefault(s => s.Key == key);

    public SpaceId ContainerSpace(ContainerSite site) =>
        Setup.Sites(l => l.Containers).FirstOrDefault(p => p.Site.Key == site.Key).Layout?.SpaceId
        ?? State.Creatures.Values.FirstOrDefault(c => c.CorpseKey == site.Key)?.Site.Space
        ?? Setup.Layout.SpaceId;

    /// <summary>The stashes a crafting station works from: those in its settlement, within the stash pull radius, in the station's own space.</summary>
    public IEnumerable<ContainerSite> StashesFor(StationSite station) =>
        Setup.Sites(l => l.Containers).Where(p => p.Layout.SpaceId == State.Space && p.Site.Stash
                && Math.Sqrt(Math.Pow(p.Site.XMm - station.XMm, 2) + Math.Pow(p.Site.ZMm - station.ZMm, 2)) <= Setup.Items.Inventory.Bags.StashPullRadiusMm)
            .Select(p => p.Site).OrderBy(c => c.Key, StringComparer.Ordinal);

    /// <summary>How many stacks a trader's wares hold (M4).</summary>
    public const int WaresStackSlots = 48;

    /// <summary>Every trader's wares (M4): a container keyed by the merchant, standing at the trader.</summary>
    public IEnumerable<ContainerSite> MerchantSites() =>
        State.Npcs.Values.Where(n => n.Space == State.Space).Select(WaresOf).OfType<ContainerSite>();

    public ContainerSite? WaresOf(NpcState npc) =>
        npc.Definition.MerchantId is { } merchant && Setup.Items.Merchants.ContainsKey(merchant)
            ? new ContainerSite(merchant, string.Empty, npc.Site.XMm, npc.Site.ZMm, WaresStackSlots)
            : null;

    /// <summary>
    /// How much further the character is seen for what they have equipped (WP3a): the equipped items' <c>stealth_penalty</c>, summed and
    /// capped at 1 - a lit torch in the off hand.
    /// </summary>
    public double PlayerStealthPenalty() =>
        Math.Min(1, State.Equipment.Values
            .Select(id => State.Inventory.FirstOrDefault(e => e.ItemId == id))
            .Sum(entry => entry is null ? 0 : Setup.Items.Catalog.Find(entry.DefId)?.StealthPenalty ?? 0));

    /// <summary>How close the body must be to speak or trade with an NPC: a hand's reach, to the NPC's body (M4).</summary>
    public long TalkReachMm => Setup.Items.Inventory.ReachMm + Setup.Movement.BodyRadiusMm;

    public double DistanceToPlayer(Body body) =>
        Math.Sqrt(Math.Pow(body.XMm - State.Body.XMm, 2) + Math.Pow(body.ZMm - State.Body.ZMm, 2));

    /// <summary>A wall, a structure or a closed door lies across the line between two points.</summary>
    public bool Walled(long x0, long z0, long x1, long z1)
    {
        bool Blocks(Blocker b) => (State.Space.IsExterior || b.ClearanceMm < 1800) && b.Crosses(x0, z0, x1, z1);
        foreach (var blocker in Setup.SpaceFor(State.Space).Blockers)
            if (Blocks(blocker)) return true;
        var layout = Setup.LayoutFor(State.Space);
        foreach (var door in layout.Doors)
            if (!IsOpen(door) && Blocks(door.ClosedFootprint)) return true;
        foreach (var barrier in layout.Barriers)
            if (!IsLifted(barrier) && Blocks(barrier.Footprint)) return true;
        return false;
    }

    /// <summary>An NPC the character can speak to: within a hand's reach, and not through a wall (the Phase-1 technical audit, L-09).</summary>
    public bool InTalkReach(NpcState npc) =>
        npc.Space == State.Space && InTalkReach(npc.Body);

    public bool InTalkReach(Body npc) =>
        DistanceToPlayer(npc) <= TalkReachMm && !Walled(State.Body.XMm, State.Body.ZMm, npc.XMm, npc.ZMm);

    /// <summary>Every corpse that can be searched: its body is still there, and its creature has a loot table.</summary>
    public IEnumerable<ContainerSite> CorpseSites() =>
        State.Creatures.Values
            .Where(c => c.Site.Space == State.Space && c.Condition == CreatureCondition.Corpse && c.Definition.LootTableId is not null)
            .Select(c => new ContainerSite(c.CorpseKey, c.Definition.LootTableId!, c.Body.XMm, c.Body.ZMm, Setup.Combat.CorpseStackSlots)
            {
                DropLevel = c.Definition.Level,
            });

    /// <summary>
    /// What the player's body cannot pass besides the static blockers: closed doors, standing barriers, living creatures and people - but
    /// not a companion (M6), who keeps out of the way instead, so a narrow door is never held shut by a friend.
    /// </summary>
    public ImmutableArray<Blocker> Obstacles() =>
        Phased() ? ClosedDoors() : ClosedDoors().AddRange(Bodies());

    /// <summary>Living creatures and people the player's body meets - not a companion, who keeps out of the way.</summary>
    public IEnumerable<Blocker> Bodies() =>
        State.Creatures.Values.Where(c => c.Alive && c.Site.Space == State.Space)
            .Select(c => (Blocker)new CircleBlocker(c.Key, c.Body.XMm, c.Body.ZMm, c.Definition.RadiusMm, 0))
            .Concat(State.Npcs.Values.Where(n => !State.Companions.ContainsKey(n.Definition.Id) && n.Space == State.Space)
                .Select(n => (Blocker)new CircleBlocker(n.Definition.Id, n.Body.XMm, n.Body.ZMm, Setup.Movement.BodyRadiusMm, 0)));

    /// <summary>The player is half out of this reality (Orenth: wraith-step): blows of the body pass through it, and it through bodies.</summary>
    public bool Phased() =>
        State.Effects.Any(kv => kv.Key.Kind == EntityKind.Character && EffectRules.Phased(kv.Value, Setup.Combat.Effects));
}

/// <summary>
/// Owns: <see cref="StateSlice.Clock"/>. Advances <c>world_tick</c>, the only clock (S-04). Its abstract advance is closed-form: the tick
/// jumps, and the calendar's day is a function of it (<see cref="WorldClock"/>).
/// </summary>
internal sealed class ClockSystem : IAbstractAdvance
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;

    public ClockSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }

    public void Tick()
    {
        _context.State.AdvanceClock(_owner);
        var space = _context.State.Space;
        if (!space.IsExterior)
            _context.Dispatch(new SetWorldFlag(_context.ClockCell(space), SystemContext.InteriorTicksFlag,
                _context.SpaceTick(space, _context.State.WorldTick) + 1) { Quiet = true });
    }

    /// <summary>The world going on while the character was away (WP11): the first system of the advance, so the others see the new tick.</summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
        if (_context.State.WorldTick != fromTick)
            throw new InvalidOperationException($"The clock reads {_context.State.WorldTick}, not the advance's start {fromTick}");
        _context.State.AdvanceClock(_owner, toTick - fromTick);
        long days = _context.Setup.Clock.DaysCrossed(fromTick, toTick);
        log.Add(new AdvanceLine(toTick, AdvanceKinds.TimePassed, string.Empty, toTick - fromTick));
        if (days > 0)
            log.Add(new AdvanceLine(toTick, AdvanceKinds.DaysPassed, string.Empty, days));
        _context.Events.Publish(new WorldTimeAdvanced(fromTick, toTick, days));
    }
}

/// <summary>
/// Owns: <see cref="StateSlice.PlayerBody"/> - the body and its posture. Integrates the player's movement intent every tick with
/// <see cref="Kinematics.Step"/>, jumps and crouches (the owner's M6 playtest), and a winged body's leap, glide and flare (Kal). The intent
/// itself is transient input state and is never saved; the posture is. The wings' cooldowns are transient, as cooldowns are. A body in deep
/// water floats rather than stands (<see cref="RuntimeState.Swimming"/>), and a swimmer whose wind runs out comes ashore.
/// </summary>
internal sealed class MovementSystem
{
    /// <summary>What going under costs in health: a small lesson, and never the last of it, as a portal's fall is not (D20).</summary>
    private const int DrownHarm = 5;

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;
    private readonly Dictionary<PeopleAction, long> _readyAt = new();
    private int _glideMilli;

    public MovementSystem(SystemContext context, SliceOwner owner, EntityId player)
    {
        _context = context;
        _owner = owner;
        _player = player;
        Intent = MoveIntent.Idle(context.State.Body.FacingMdeg);
    }

    public MoveIntent Intent { get; private set; }

    /// <summary>
    /// The intent as the body carries it out: a crouched body goes at a walk, whatever the gait asked (its footfalls, its stamina), and a
    /// body in the water keeps that walk's pace with the sprint refused.
    /// </summary>
    public MoveIntent Effective => _context.State.Posture.Stance == Stance.Crouched || _context.State.Swimming ? Intent with { Gait = Gait.Walk } : Intent;

    /// <summary>Leave the ground: not in the air already, not mid-action or guarding, and from a crouch only where there is room to stand.</summary>
    public string? Handle(JumpCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        var posture = _context.State.Posture;
        if (_context.State.Swimming)
            return "the water is not a place to leap from";
        if (posture.Airborne)
            return "already in the air";
        var combat = _context.State.PlayerCombat;
        var (phase, _) = combat.Action.PhaseAt(tick, _context.Setup.Combat.Constants);
        if (combat.Defeated || phase != CombatPhase.Idle || combat.Blocking)
            return "busy";
        if (posture.Stance == Stance.Crouched && !Kinematics.CanStand(_context.State.Body, _context.Setup.Movement, _context.Setup.SpaceFor(_context.State.Space)))
            return "no room to stand";
        // Sprint and jump: a winged body throws its wings open for a leap, if it has the stamina and the wings are ready (Kal: Gap Leap).
        var wing = Intent.Gait == Gait.Sprint && Intent.IsMoving && posture.Stance == Stance.Standing && Spend(PeopleAction.Leap, tick)
            ? WingState.Leap
            : WingState.Folded;
        _context.State.SetPosture(_owner, new Posture(Stance.Standing, Airborne: true, AirMs: 0) { Wing = wing });
        _context.Events.Publish(new Jumped(_player, tick));
        if (wing == WingState.Leap)
            _context.Events.Publish(new WingsChanged(_player, wing, tick));
        if (posture.Stance == Stance.Crouched)
            _context.Events.Publish(new StanceChanged(_player, Stance.Standing, tick));
        return null;
    }

    /// <summary>
    /// Crouch, or stand: never in the air, never in a dodge's dash (a dash begun standing ends standing - the Phase-1 technical audit,
    /// L-11), and standing only where no overhang is lower than a standing head.
    /// </summary>
    public string? Handle(CrouchCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        var posture = _context.State.Posture;
        if (posture.Aloft)
            return command.Crouched ? Flare(posture, tick) : posture.Wing == WingState.Flare ? Wings(posture with { Wing = WingState.Glide }, tick) : null;
        var stance = command.Crouched ? Stance.Crouched : Stance.Standing;
        if (posture.Stance == stance)
            return null;
        if (posture.Airborne)
            return "in the air";
        if (_context.State.PlayerCombat.Action.PhaseAt(tick, _context.Setup.Combat.Constants).Phase == CombatPhase.Dodge)
            return "mid-dodge";
        if (stance == Stance.Standing && !Kinematics.CanStand(_context.State.Body, _context.Setup.Movement, _context.Setup.SpaceFor(_context.State.Space)))
            return "no room to stand";
        _context.State.SetPosture(_owner, posture with { Stance = stance });
        _context.Events.Publish(new StanceChanged(_player, stance, tick));
        return null;
    }

    /// <summary>
    /// Spread the wings, in the air and with stamina to hold them (Kal: Wing Glide): the body keeps the height it has and glides from there.
    /// Folding them aloft lets the body drop; on the ground, or wingless, there is nothing to spread.
    /// </summary>
    public string? Handle(GlideCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        var posture = _context.State.Posture;
        if (!command.Spread)
            return posture.Aloft && posture.Wing != WingState.Fall ? Wings(posture with { Wing = WingState.Fall }, tick) : null;
        if (!_context.PlayerMovement.Wings.CanGlide)
            return "no wings to spread";
        if (_context.State.Swimming)
            return "the water holds the wings";
        if (!posture.Airborne)
            return "on the ground";
        if (posture.Wing is WingState.Glide or WingState.Flare)
            return null;
        if (_context.State.PlayerCombat.Defeated || _context.ControlOn(_player) != ControlKind.None)
            return "cannot spread the wings now";
        if (Stamina() <= 0)
            return "too tired to hold the wings";
        _glideMilli = 0;
        return Wings(posture with { Wing = WingState.Glide, FeetMm = _context.State.Body.YMm, AirMs = 0 }, tick);
    }

    /// <summary>Gliding, flare the wings to brake: forward to nothing, and down slowly onto the spot below (Kal: Wing Brake).</summary>
    private string? Flare(Posture posture, long tick)
    {
        if (posture.Wing != WingState.Glide)
            return "not gliding";
        if (!Spend(PeopleAction.Flare, tick))
            return "cannot flare yet";
        return Wings(posture with { Wing = WingState.Flare }, tick);
    }

    private string? Wings(Posture posture, long tick)
    {
        _context.State.SetPosture(_owner, posture);
        _context.Events.Publish(new WingsChanged(_player, posture.Wing, tick));
        return null;
    }

    /// <summary>Pay for a wing ability the body has: its stamina, once its cooldown is out. False, and nothing paid, otherwise.</summary>
    private bool Spend(PeopleAction action, long tick)
    {
        if (_context.PlayerAbilities.FirstOrDefault(a => a.Action == action) is not { } ability
            || _readyAt.TryGetValue(action, out long ready) && tick < ready || Stamina() < ability.StaminaCost)
            return false;
        if (ability.StaminaCost > 0)
            _context.Dispatch(new ChangePools(0, -ability.StaminaCost));
        _readyAt[action] = tick + ability.CooldownTicks;
        return true;
    }

    private int Stamina() => _context.State.Progression.Pools.Stamina ?? (int)_context.PlayerStats().StaminaMax;

    /// <summary>Ticks until a wing ability is ready again; 0 when it is.</summary>
    public int Cooldown(PeopleAction action, long tick) => _readyAt.TryGetValue(action, out long ready) && ready > tick ? (int)(ready - tick) : 0;

    public string? Handle(MoveCommand command)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (command.Intent.Problem() is { } problem)
            return problem;
        Intent = command.Intent;
        return null;
    }

    /// <summary>
    /// One tick of movement. What the body is doing in combat shapes it: a dodge dashes along its own direction, a stagger
    /// or a dodge's recovery roots the feet, an attack or a raised guard slows them to a walk (and a landed blow holds
    /// its facing), and an empty stamina pool turns a sprint into a run.
    /// </summary>
    public void Tick(long tick)
    {
        var from = _context.State.Body;
        // Out of wind in deep water: the body comes ashore at the nearest bank, on its feet and wading, a little the worse for it.
        if (_context.State.Swimming && Stamina() <= 0)
        {
            Ashore(from, tick);
            return;
        }
        var combat = _context.State.PlayerCombat;
        var constants = _context.Setup.Combat.Constants;
        var rules = _context.PlayerMovement;
        var intent = Intent;
        var (phase, _) = combat.Action.PhaseAt(tick, constants);
        if (combat.Defeated || phase == CombatPhase.Staggered || (phase == CombatPhase.Recovery && combat.Action.Kind == ActionKind.Dodge)
            || _context.ControlOn(_player) != ControlKind.None)
        {
            intent = MoveIntent.Idle(from.FacingMdeg);
        }
        else if (phase == CombatPhase.Dodge)
        {
            intent = new MoveIntent(combat.Action.DirXPermille, combat.Action.DirZPermille, Gait.Run, from.FacingMdeg);
            rules = rules with
            {
                BaseSpeedMmPerSecond = constants.DodgeDistanceMm * 1000 / Math.Max(1, constants.DodgeTicks * _context.Setup.TickMilliseconds),
            };
        }
        else if (phase is CombatPhase.Windup or CombatPhase.Active or CombatPhase.Recovery)
        {
            intent = intent with { Gait = Gait.Walk, FacingMdeg = phase == CombatPhase.Windup ? intent.FacingMdeg : from.FacingMdeg };
        }
        else if (combat.Blocking)
        {
            intent = intent with { Gait = Gait.Walk };
        }
        else if (intent.Gait == Gait.Sprint && _context.State.Progression.Pools.Stamina == 0)
        {
            intent = intent with { Gait = Gait.Run };
        }

        var before = _context.State.Posture;
        var posture = before.Aloft && (combat.Defeated || before.Wing == WingState.Glide && !HoldWings(tick)) ? before with { Wing = WingState.Fall } : before;
        int staminaBefore = (int)(_context.State.Progression.Pools.Stamina ?? (int)_context.PlayerStats().StaminaMax);
        var space = _context.Setup.SpaceFor(_context.State.Space);
        var (to, next) = Kinematics.Step(from, posture, intent, rules, space, _context.Obstacles(), _context.Setup.TickMilliseconds);
        // In deep water or out of it, as the point it has come to says: the body's own slice, and the tick's own body, so the HUD and the
        // stamina both read the water the body is in now (D20).
        bool swimming = Kinematics.SwimWater(to.XMm, to.ZMm, space) is not null;
        if (swimming != _context.State.Swimming)
            _context.State.SetSwimming(_owner, swimming);
        // A climb or a vault (BRIEF D6): the feet come up more than a step in one tick. It costs stamina by the metre, and a body with none
        // left stays on the ground - which is what makes a ladder a decision rather than a free lift.
        long rose = to.YMm - from.YMm;
        if (!posture.Airborne && rose > rules.StepUpMm)
        {
            if (staminaBefore <= 0)
            {
                to = from;
                next = posture;
            }
            else
            {
                int cost = (int)Math.Max(1, rose * rules.ClimbStaminaPerMetre / 1_000);
                _context.Dispatch(new ChangePools(0, -Math.Min(cost, staminaBefore)));
                _context.Events.Publish(new Climbed(_player, from, to, cost, tick));
            }
        }        if (next != before)
        {
            _context.State.SetPosture(_owner, next);
            if (next.Wing != before.Wing)
                _context.Events.Publish(new WingsChanged(_player, next.Wing, tick));
        }
        if (to == from)
            return;
        _context.State.SetBody(_owner, to);
        _context.Events.Publish(new BodyMoved(_player, from, to, tick));
    }

    /// <summary>A tick of gliding drains the wings' stamina a thousandth at a time; spent, they fold and the body drops.</summary>
    private bool HoldWings(long tick)
    {
        var glide = _context.PlayerAbilities.FirstOrDefault(a => a.Action == PeopleAction.Glide);
        if (glide is null)
            return false;
        _glideMilli += glide.StaminaPerSecond * _context.Setup.TickMilliseconds;
        int whole = _glideMilli / 1000;
        _glideMilli %= 1000;
        if (whole > 0)
            _context.Dispatch(new ChangePools(0, -whole));
        return Stamina() > 0;
    }

    /// <summary>
    /// Out of strength in the water (D20): the body is put on the nearest bank of the water it is in, on its feet and wading - out along
    /// the line from the nearest point of the water's own line to it, a body's radius past the edge - and loses a little health for having
    /// gone under. The intent stops with it, as a body put somewhere by a respawn does.
    /// </summary>
    private void Ashore(Body from,long tick)
    {
        var space = _context.Setup.SpaceFor(_context.State.Space);
        if (space.WaterUnder(from.XMm,from.ZMm) is not { } water)
        {
            _context.State.SetPosture(_owner,Posture.Grounded);
            _context.State.SetSwimming(_owner,false);
            return;
        }
        var (x,z) = water.ShoreFrom(from.XMm,from.ZMm,_context.PlayerMovement.BodyRadiusMm);
        var to = new Body(x,space.HeightAtMm(x,z),z,from.FacingMdeg);
        _context.State.SetBody(_owner,to);
        _context.State.SetPosture(_owner,Posture.Grounded);
        _context.State.SetSwimming(_owner,false);
        Intent = MoveIntent.Idle(to.FacingMdeg);
        _context.Events.Publish(new BodyMoved(_player,from,to,tick));
        _context.Events.Publish(new WentUnder(water.Key,tick));
        // Never the last of it: going under leaves the character ashore, not dead (the portal's fall costs health the same way).
        int harm = DrownHarm;
        if (_context.State.Progression.Pools.Health is { } health)
            harm = Math.Min(harm,health-1);
        _context.Dispatch(new Harm(_player,"drowning",harm));
    }

    public string? Handle(Relocate command, long tick)
    {
        var from = _context.State.Body;
        _context.State.SetBody(_owner, command.Body);
        _context.State.SetPosture(_owner, Posture.Grounded);
        _context.State.SetSwimming(_owner, false);   // put somewhere, on whatever ground is there: the next step decides the water again
        Intent = MoveIntent.Idle(command.Body.FacingMdeg);
        _context.Events.Publish(new BodyMoved(_player, from, command.Body, tick));
        return null;
    }
}

/// <summary>
/// Owns no state. Validates an interaction against the actor's authoritative body - never the camera - and asks
/// the owner of what changes to change it. In Phase 1 the interactables are doors and (M6) switches.
/// </summary>
internal sealed class InteractionSystem
{
    private readonly SystemContext _context;
    private readonly EntityId _player;

    public InteractionSystem(SystemContext context, EntityId player)
    {
        _context = context;
        _player = player;
    }

    public string? Handle(InteractCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        // The site's own layout, and only usable from inside its space: local metres overlap between spaces, so a
        // proximity test without a space test would reach across the hill.
        if (_context.Setup.Sites(l => l.Switches).FirstOrDefault(p => p.Site.Key == command.TargetKey) is { Site: { } switchSite, Layout: var switchHome })
            return Work(switchHome, switchSite, tick);
        if (_context.Setup.Sites(l => l.Readables).FirstOrDefault(p => p.Site.Key == command.TargetKey) is { Site: { } readable, Layout: var readableHome })
            return Read(readableHome, readable, tick);
        var found = _context.Setup.Sites(l => l.Doors).FirstOrDefault(p => p.Site.Key == command.TargetKey);
        if (found.Site is not { } door)
            return $"nothing to interact with called '{command.TargetKey}'";
        if (found.Layout.SpaceId != _context.State.Space)
            return $"{door.Key} is not in this place";
        var body = _context.State.Body;
        var rules = _context.Setup.Movement;
        double distance = door.ClosedFootprint.DistanceTo(body.XMm, body.ZMm);
        if (distance > rules.InteractReachMm)
            return $"{door.Key} is {distance / 1000:0.00} m away; reach is {rules.InteractReachMm / 1000.0:0.00} m";

        bool open = _context.IsOpen(door);
        if (open && door.StaysOpen)
            return null;
        // A tiered door is the pick's, not the handle's: it says why it will not open and leaves the lock to the picks.
        if (!open && _context.Locked(door))
            return door.LockedText ?? "The door is locked.";
        if (!open)
        {
            bool atLatch = door.LatchSide switch
            {
                "east" => body.XMm > door.ClosedFootprint.CenterXMm,
                "west" => body.XMm < door.ClosedFootprint.CenterXMm,
                "north" => body.ZMm > door.ClosedFootprint.CenterZMm,
                "south" => body.ZMm < door.ClosedFootprint.CenterZMm,
                _ => true,
            };
            if (!atLatch || (door.Requirement is { } requirement && !_context.RequirementMet(requirement)))
                return door.LockedText ?? "The door is locked.";
        }
        // Nor on anyone else standing in it - a creature or an NPC - who would be shut inside the wall (the Phase-1 technical audit, L-16).
        bool blocked = door.ClosedFootprint.Separation(body.XMm, body.ZMm, rules.BodyRadiusMm) is not null
                       || _context.State.Creatures.Values.Any(c => c.Alive && door.ClosedFootprint.Separation(c.Body.XMm, c.Body.ZMm, c.Definition.RadiusMm) is not null)
                       || _context.State.Npcs.Values.Any(n => door.ClosedFootprint.Separation(n.Body.XMm, n.Body.ZMm, rules.BodyRadiusMm) is not null);
        if (open && blocked)
            return $"{door.Key} cannot close: something is in the doorway";
        if (!open && door.Fitted is { } fitted && _context.Dispatch(new ConsumeItem(fitted.ItemId, fitted.Count)) is not null)
            return door.LockedText;
        if (_context.Dispatch(new SetWorldFlag(Simulation.CellOf(found.Layout.SpaceId, door), door.FlagId, open ? 0 : 1)) is { } refused)
            return refused;
        _context.Events.Publish(new DoorToggled(_player, door.Key, !open, tick));
        return null;
    }

    /// <summary>
    /// Something written is read from where a switch is worked (spec 41). A page names itself; a book (page 0) opens every page of its entry
    /// the character has not read. Either way presentation is told, and opens the page on its panel.
    /// </summary>
    private string? Read(RegionLayout home, ReadableSite site, long tick)
    {
        if (home.SpaceId != _context.State.Space)
            return $"the {site.Name} is not in this place";
        var body = _context.State.Body;
        long reach = _context.Setup.Movement.InteractReachMm;
        double distance = site.Body.DistanceTo(body.XMm, body.ZMm);
        if (distance > reach)
            return $"the {site.Name} is {distance / 1000:0.00} m away; reach is {reach / 1000.0:0.00} m";
        if (site.Page > 0)
        {
            if (!_context.Setup.Lore.TryGetValue(site.EntryId, out var entry) || entry.PageAt(site.Page) is not { } page)
                return $"{site.Key} names page {site.Page} of {site.EntryId}, which nobody wrote";
            if (_context.State.Lore.Any(r => r.EntryId == entry.Id && r.Page == page.Page))
                return $"{site.Key} is already in your journal";
            if (_context.Dispatch(new UnlockLore(_player, Domain.Knowledge.LoreSourceKind.Inspected, site.Key)) is { } refused)
                return refused;
        }
        else if (_context.Dispatch(new ReadLore(_player, site.EntryId, Domain.Knowledge.LoreSourceKind.Inspected, site.Key)) is { } refused)
        {
            return refused;
        }
        _context.Events.Publish(new LoreReadableRead(site.Key, site.EntryId, tick));
        return null;
    }

    /// <summary>A switch sets its flag once, and only when every flag it requires is set in its cell.</summary>
    private string? Work(RegionLayout home, SwitchSite site, long tick)
    {
        if (home.SpaceId != _context.State.Space)
            return $"the {site.Name} is not in this place";
        var body = _context.State.Body;
        long reach = _context.Setup.Movement.InteractReachMm;
        double distance = site.Body.DistanceTo(body.XMm, body.ZMm);
        if (distance > reach)
            return $"the {site.Name} is {distance / 1000:0.00} m away; reach is {reach / 1000.0:0.00} m";
        if (_context.IsSet(site))
            return $"the {site.Name} is already set";
        if (_context.Excluded(site))
            return "That choice has already been made.";
        var cell = Simulation.CellOf(home.SpaceId, site);
        if (site.Requires.Any(flag => _context.State.World.GetFlag(cell, flag) == 0))
            return site.LockedText;
        if (!site.Requirements.IsDefaultOrEmpty && site.Requirements.Any(r => !_context.RequirementMet(r)))
            return site.LockedText;
        if (site.Consume is { } cost && _context.Dispatch(new ConsumeItem(cost.ItemId, cost.Count)) is { } missing)
            return missing;
        if (_context.Dispatch(new SetWorldFlag(cell, site.FlagId, 1)) is { } refused)
            return refused;
        _context.Dispatch(new RecordAct(Domain.Factions.ActKinds.SwitchSet, site.FlagId, body.XMm, body.ZMm));
        if (site.Learn is { } recipe)
            _context.Dispatch(new LearnTechnique(new TechniqueLearning(recipe, LearningSource.Discovery, tick) { SourceRef = site.Key }));
        if (site.Reward is { } reward)
            _context.Dispatch(new GrantItem(reward.ItemId, reward.Count, Domain.Crafting.Quality.Standard) { SourceRef = site.Key });
        _context.Events.Publish(new SwitchSet(_player, site.Key, tick));
        return null;
    }
}

/// <summary>Owns: <see cref="StateSlice.WorldFlags"/>. The cell-level flags of the sparse world delta (S-20).</summary>
internal sealed class WorldFlagSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;

    public WorldFlagSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }

    /// <summary>
    /// Writes a flag as a FACT OF THE WORLD at creation rather than as something the character did, through the same
    /// slice this system owns. The ring's withdrawal notice is the case: it is in a hall the character can reach, so
    /// it is readable before they go anywhere.
    /// </summary>
    public void Seed(CellKey cell, string flagId, long value) => _context.State.SetFlag(_owner, cell, flagId, value);

    public string? Handle(SetWorldFlag command, long tick)
    {
        long from = _context.State.World.GetFlag(command.Cell, command.FlagId);
        if (from == command.Value)
            return null;
        _context.State.SetFlag(_owner, command.Cell, command.FlagId, command.Value);
        if (!command.Quiet)
            _context.Events.Publish(new WorldFlagChanged(command.Cell.ToString(), command.FlagId, from, command.Value, tick));
        return null;
    }
}

/// <summary>Spend an unspent attribute point on an attribute in play (the owner's M6 playtest: the character sheet; PROTOTYPE.md C11).</summary>
public sealed record SpendAttributeCommand(EntityId Actor, CharacterAttribute Attribute) : GameCommand(Actor);

/// <summary>An attribute point was spent: the attribute's new value, and the points left.</summary>
public sealed record AttributeSpent(CharacterAttribute Attribute, int Value, int Unspent, long Tick);

/// <summary>Owns: <see cref="StateSlice.PlayerProgression"/>. Applies the pure progression rules (S-08, M2c).</summary>
internal sealed class ProgressionSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;

    public ProgressionSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }

    public string? Handle(AwardExperience command)
    {
        var result = ProgressionEngine.Award(_context.State.Progression, command.Award, _context.Setup.Progression);
        _context.State.SetProgression(_owner, result.Progression);
        // An award that paid nothing - a first-time award made again - is not news (the Phase-1 technical audit, L-12).
        if (result.Awarded > 0)
            _context.Events.Publish(new ExperienceGained(command.Award.Source, result.Awarded, result.Repaid, result.LevelsGained,
                result.Progression.Level, command.Award.Tick));
        return null;
    }

    /// <summary>
    /// The current pools move, clamped to their derived maxima; a full pool is stored as full (null), never as a number.
    /// Strain moves between zero and the character's tolerance.
    /// </summary>
    public string? Handle(ChangePools command)
    {
        var progression = _context.State.Progression;
        var stats = _context.PlayerStats();
        int maxHealth = (int)stats.HealthMax, maxStamina = (int)stats.StaminaMax, maxFocus = (int)stats.FocusMax;
        int health = Math.Clamp((progression.Pools.Health ?? maxHealth) + command.Health, 0, maxHealth);
        int stamina = Math.Clamp((progression.Pools.Stamina ?? maxStamina) + command.Stamina, 0, maxStamina);
        int focus = Math.Clamp((progression.Pools.Focus ?? maxFocus) + command.Focus, 0, maxFocus);
        int strain = Math.Clamp(progression.Pools.Strain + command.Strain, 0, (int)Math.Max(0, stats.StrainTolerance));
        var pools = progression.Pools with
        {
            Health = health >= maxHealth ? null : health,
            Stamina = stamina >= maxStamina ? null : stamina,
            Focus = focus >= maxFocus ? null : focus,
            Strain = strain,
        };
        if (pools != progression.Pools)
            _context.State.SetProgression(_owner, progression with { Pools = pools });
        return null;
    }

    /// <summary>One point from level-ups onto an attribute some derived value reads; one nothing reads yet would be a point wasted.</summary>
    public string? Handle(SpendAttributeCommand command, long tick)
    {
        var progression = _context.State.Progression;
        var rules = _context.Setup.Progression;
        if (progression.UnspentAttributePoints <= 0)
            return "no attribute points to spend";
        if (!rules.Derived.Live.Contains(command.Attribute))
            return $"{ProgressionKeys.Key(command.Attribute)} is not in play yet";
        var spent = ProgressionEngine.Allocate(progression, new AttributeAllocation(command.Attribute, 1));
        _context.State.SetProgression(_owner, spent);
        _context.Events.Publish(new AttributeSpent(command.Attribute, ProgressionEngine.AttributeValue(spent, command.Attribute, rules),
            spent.UnspentAttributePoints, tick));
        return null;
    }

    /// <summary>A learning event (PROGRESSION.md Â§4.4): the only way a formula becomes known.</summary>
    public string? Handle(LearnTechnique command)
    {
        // PRG-29: a technique's own requirements are checked here, where every path arrives - a teacher, a book, a quest or a discovery - so
        // none of them can hand out what the character cannot hold, and the refusal says which requirement and in what words.
        if (_context.Setup.Techniques.TryGetValue(command.Learning.DefinitionId, out var technique)
            && TechniqueRequirements.FirstUnmet(technique.Requirements, TechniqueCandidates.Of(_context.State, _context.Setup)) is { } unmet)
            return $"{command.Learning.DefinitionId} is not learnt yet: {unmet.Why}";
        var result = ProgressionEngine.Learn(_context.State.Progression, command.Learning);
        if (!result.Learned)
            return $"{command.Learning.DefinitionId} is already known";
        _context.State.SetProgression(_owner, result.Progression);
        _context.Events.Publish(new TechniqueLearned(command.Learning.DefinitionId, ProgressionKeys.Key(command.Learning.Source), command.Learning.Tick));
        return null;
    }

    public string? Handle(PracticeSkill command)
    {
        var result = ProgressionEngine.Practice(_context.State.Progression, command.Practice, _context.Setup.Progression, _context.PlayerTraits.Learning);
        _context.State.SetProgression(_owner, result.Progression);
        _context.Events.Publish(new SkillPracticed(command.Practice.SkillId, result.XpGained,
            ProgressionEngine.SkillLevel(result.Progression, command.Practice.SkillId), result.LevelsGained, command.Practice.Tick));
        return null;
    }

    /// <summary>AG-8: a death owes XP debt and takes nothing earned; the body comes back whole.</summary>
    public string? Handle(RecordDeath command)
    {
        var result = ProgressionEngine.Die(_context.State.Progression, _context.Setup.Progression);
        _context.State.SetProgression(_owner, result.Progression with { Pools = PoolState.Full });
        return null;
    }
}

/// <summary>
/// Owns: <see cref="StateSlice.Discoveries"/>. The Phase-1 subset of S-30: entering a named place's radius
/// discovers it, once, and earns its discovery XP (PROGRESSION.md Â§3.3 <c>discovery</c>). Only places in tier-A
/// cells are checked; a place in a cell the simulation is not running cannot be walked into.
/// </summary>
internal sealed class DiscoverySystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;

    public DiscoverySystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }

    /// <summary>A page said where a place is: it joins the map as told, once, and nothing else about the world changes.</summary>
    public string? Handle(TellOfPlace command, long tick)
    {
        var state = _context.State;
        if (state.Discoveries.ContainsKey(command.LocationId))
            return null;
        if (_context.Setup.Sites(l => l.Locations).All(site => site.Site.Id != command.LocationId))
            return $"{command.LocationId} is not a place any loaded region lays out";
        state.AddDiscovery(_owner, new DiscoveryRecord(command.LocationId, DiscoveryMethod.Told, tick));
        return null;
    }

    public void Tick(long tick)
    {
        var state = _context.State;
        foreach (var (layout, place) in _context.Setup.Sites(l => l.Locations))
        {
            if (state.Discoveries.ContainsKey(place.Id))
                continue;
            // The place's own space: its discovery tier, and its distance, are its layout's.
            string cell = _context.CellOfSite(layout.SpaceId, place.XMm, place.ZMm).ToString();
            if (state.Tiers.GetValueOrDefault(cell, SimulationTier.D) != SimulationTier.A)
                continue;
            double dx = state.Body.XMm - place.XMm, dz = state.Body.ZMm - place.ZMm;
            if (dx * dx + dz * dz > (double)place.DiscoveryRadiusMm * place.DiscoveryRadiusMm)
                continue;
            state.AddDiscovery(_owner, new DiscoveryRecord(place.Id, DiscoveryMethod.Visited, tick));
            _context.Events.Publish(new LocationDiscovered(place.Id, DiscoveryMethod.Visited, tick));
            if (place.DiscoveryXp > 0)
                _context.Dispatch(new AwardExperience(new XpAward(XpSource.Discovery, place.DiscoveryXp, tick)));
        }
        // The roads the body walks along, stretch by stretch: what the map draws of them (schema 15).
        foreach (string stretch in Roads.StretchesNear(_context.Setup.Layout.Roads, state.Body.XMm, state.Body.ZMm, _context.Setup.Travel.RoadWalkedWithinMm))
        {
            if (!state.RoadsWalked.Contains(stretch))
                state.WalkRoad(_owner, stretch);
        }
    }
}

/// <summary>Someone the world is simulated around (WP12b): a key, and where they stand. In a single-player world, the character.</summary>
public sealed record TierObserver(string Key, long XMm, long ZMm);

/// <summary>
/// Who the world is simulated around at a tick (WP12b; multiplayer-ready): every cell's tier is its distance to the nearest of them, and
/// the NPC budgets admit those nearest any of them first. The character is always one, at <paramref name="player"/>; a server would add
/// every connected player. A pure read: the same world and tick give the same observers.
/// </summary>
public interface ITierObservers
{
    IReadOnlyList<TierObserver> At(long tick, Body player);
}

public static class TierObservers
{
    public const string PlayerKey = "player";

    /// <summary>The character alone: a single-player world.</summary>
    public static ITierObservers PlayerOnly { get; } = new Fixed(ImmutableArray<TierObserver>.Empty);

    /// <summary>The character, and others standing where they stand: for tests, and a stand-in for a server's other players.</summary>
    public static ITierObservers With(IEnumerable<TierObserver> others) => new Fixed(others.ToImmutableArray());

    private sealed class Fixed : ITierObservers
    {
        private readonly ImmutableArray<TierObserver> _others;

        public Fixed(ImmutableArray<TierObserver> others) => _others = others;

        public IReadOnlyList<TierObserver> At(long tick, Body player) =>
            _others.Insert(0, new TierObserver(PlayerKey, player.XMm, player.ZMm));
    }
}

/// <summary>
/// Owns: <see cref="StateSlice.CellTiers"/>. Assigns every cell of the region a simulation tier from its distance
/// to the nearest observer (S-21, D-06; WP12b: a set of observers, the character among them), one step per tick with hysteresis. Tier A cells are fully simulated and tier D
/// cells are stored state only; tiers B and C run their <see cref="ITierSimulation"/>s (WP12: the NPCs').
/// </summary>
internal sealed class TierSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly ImmutableArray<CellKey> _cells;

    private readonly ITierObservers _observers;

    public TierSystem(SystemContext context, SliceOwner owner, ImmutableArray<CellKey> cells, ITierObservers observers)
    {
        _context = context;
        _owner = owner;
        _cells = cells;
        _observers = observers;
    }

    /// <summary>Who the world is simulated around at a tick: the character, and whoever else the observers name.</summary>
    public IReadOnlyList<TierObserver> Observers(long tick) => _observers.At(tick, _context.State.Body);

    /// <summary>A hard boundary (a load, a new game): each cell settles directly, with no events (WORLD_ARCHITECTURE.md Â§5.1).</summary>
    public void Settle()
    {
        var observers = Observers(_context.State.WorldTick);
        foreach (var cell in _cells)
        {
            var tier = SimulationTier.D;
            for (int step = 0; step < 3; step++)
                tier = _context.Setup.Tiers.Next(tier, DistanceTo(cell, observers));
            _context.State.SetTier(_owner, cell.ToString(), tier);
        }
    }

    public void Tick(long tick)
    {
        var observers = Observers(tick);
        foreach (var cell in _cells)
        {
            string key = cell.ToString();
            var from = _context.State.Tiers.GetValueOrDefault(key, SimulationTier.D);
            var to = _context.Setup.Tiers.Next(from, DistanceTo(cell, observers));
            if (to == from)
                continue;
            _context.State.SetTier(_owner, key, to);
            _context.Events.Publish(new CellTierChanged(key, from, to, tick));
        }
    }

    /// <summary>A cell's distance to the nearest observer: to the nearest point of the cell, 0 inside it. Far beyond everything with none.</summary>
    public long DistanceTo(CellKey cell, IReadOnlyList<TierObserver> observers)
    {
        // A cell in another space is not near the body, whatever the local metres say. Without this the ring's cells
        // would be promoted by a body standing at the same metres in the frontier, and the Custodian's tier would be
        // decided by someone in a different room entirely (review 3, B8).
        if (cell.Space != _context.State.Space)
            return long.MaxValue / 4;
        long minX = ((long)cell.Region.Rx * WorldMath.RegionSizeMeters + (long)cell.Cx * WorldMath.CellSizeMeters) * 1000;
        long minZ = ((long)cell.Region.Rz * WorldMath.RegionSizeMeters + (long)cell.Cz * WorldMath.CellSizeMeters) * 1000;
        long size = WorldMath.CellSizeMeters * 1000L;
        long nearest = long.MaxValue;
        foreach (var o in observers)
        {
            long dx = o.XMm < minX ? minX - o.XMm : o.XMm > minX + size ? o.XMm - (minX + size) : 0;
            long dz = o.ZMm < minZ ? minZ - o.ZMm : o.ZMm > minZ + size ? o.ZMm - (minZ + size) : 0;
            nearest = Math.Min(nearest, (long)Math.Sqrt((double)dx * dx + (double)dz * dz));
        }
        return nearest;
    }
}

/// <summary>
/// Owns: <see cref="StateSlice.PlayerSpace"/>. Moves the player between the exterior and an interior space
/// through a declared portal (M8 D10), and remembers which portal they came in by so leaving returns them
/// to the anchor they actually used.
/// </summary>
/// <remarks>
/// It owns only which space the body is in and how it got there; it writes the body itself by dispatching
/// <see cref="Relocate"/>, because the body is the movement system's. Refusals are returned as a reason and
/// never as an exception: a command that cannot be honoured leaves the character exactly where they were,
/// which is the recoverable coherent snapshot the design asks for when a transition cannot complete.
/// </remarks>
internal sealed class SpaceSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;

    public SpaceSystem(SystemContext context, SliceOwner owner, EntityId player)
    {
        _context = context;
        _owner = owner;
        _player = player;
    }

    /// <summary>What a fall costs, a metre: the rope's 3.5 m is a fifth of a starting character's health, never the last of it.</summary>
    private const long FallHarmPerMetre = 6;

    /// <summary>How close the body must stand to a portal's anchor to use it: an arm's reach, as for a door.</summary>
    private long ReachMm => _context.Setup.Items.Inventory.ReachMm + _context.Setup.Movement.BodyRadiusMm;

    /// <summary>
    /// Put the body into the space its save recorded (the interior-space save extension), without moving it and without a portal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A load is the one way a body arrives in a space it did not walk into: the save holds the position and the
    /// space, and the two are one coordinate between them.
    /// </para>
    /// <para>
    /// An empty name is the exterior's, and the exterior is what a body that never entered the ring is in - so an
    /// empty name is NOT adopted. A region that is itself an interior (region.barrowworks, opened directly by a
    /// capture or a survey) starts its bodies in its own space, and overwriting that with the empty exterior name
    /// would put every such body outside the room it is standing in. Only a save that names a space moves one.
    /// </para>
    /// </remarks>
    public void Adopt(string spaceId, string? traversal)
    {
        if (string.IsNullOrEmpty(spaceId))
            return;
        _context.State.SetSpace(_owner, new SpaceId(spaceId), traversal is { } key ? new SpaceTraversal(key) : SpaceTraversal.None);
    }

    /// <summary>Whether a way that gives under its first crossing has already given.</summary>
    private bool Gone(SpacePortal portal) => portal.GivesWay is { } gives && _context.RequirementMet(gives.Flag);

    /// <summary>Enter the space a portal leads to, placing the body at its interior anchor.</summary>
    public string? Enter(EnterSpaceCommand command, long tick)
    {
        if (command.Actor != _player)
            return "Only the player moves between spaces";

        var portals = _context.Setup.Layout.Portals;
        var portal = portals.Find(command.PortalKey);
        if (portal is null)
            return $"No portal '{command.PortalKey}'";

        if (!_context.State.Space.IsExterior)
            return $"Already inside {_context.State.Space}";

        var body = _context.State.Body;
        double away = Math.Sqrt(Math.Pow(portal.ExteriorXMm - body.XMm, 2) + Math.Pow(portal.ExteriorZMm - body.ZMm, 2));
        if (away > ReachMm)
            return $"Too far from '{command.PortalKey}' to use it";
        if (portal.OpensWith is { } opens && !_context.RequirementMet(opens))
            return "There is no way down here";
        if (Gone(portal))
            return "That way is gone";

        var (x, z, traversal) = portals.Enter(command.PortalKey);
        _context.State.SetSpace(_owner, portal.Space, traversal);
        var arrival = new Body(x, _context.Setup.SpaceFor(portal.Space).Terrain.HeightAtMm(x, z), z, body.FacingMdeg);
        _context.Dispatch(new Relocate(arrival));
        // The party follows through the door: a follower left behind would be in a space the player has
        // left, and every rule about who can see and reach whom now depends on sharing it.
        _context.Dispatch(new ArriveWithParty(arrival));
        // A way that gives under its first crossing: the body still arrives, the fall costs health but never the last of it,
        // and the way is gone for good (consequence, not a failure screen).
        if (portal.GivesWay is { } gives)
        {
            var place = _context.Setup.Sites(l => l.Locations).First(p => p.Site.Id == gives.Flag.LocationId);
            _context.Dispatch(new SetWorldFlag(_context.CellOfSite(place.Layout.SpaceId, place.Site.XMm, place.Site.ZMm), gives.Flag.FlagId, 1));
            int fall = (int)(gives.DropMm * FallHarmPerMetre / 1000);
            if (_context.State.Progression.Pools.Health is { } health)
                fall = Math.Min(fall, health - 1);
            _context.Dispatch(new Harm(_player, "fall", fall));
            _context.Events.Publish(new PortalGaveWay(portal.Key, gives.DropMm, tick));
        }
        return null;
    }

    /// <summary>
    /// Leave the space by the portal the body is standing at, arriving at THAT portal's exterior anchor.
    /// </summary>
    /// <remarks>
    /// The door out is the one underfoot, not the one that was walked in through. The design is explicit that a
    /// character who came in by the drain mouth must be able to leave by the threshold if they walked to it, and
    /// the stored traversal is a record of the entry rather than the destination (review B5). Nothing is not a
    /// crash and not a silent fallback: a body in the middle of a room is simply refused, and the stored portal is
    /// never looked up - which is what makes a renamed or gated-off portal a refusal rather than an exception.
    /// </remarks>
    public string? Leave(LeaveSpaceCommand command)
    {
        if (command.Actor != _player)
            return "Only the player moves between spaces";

        if (_context.State.Space.IsExterior)
            return "Already outside";

        // Not through a door with something at your back: an Engaged creature in this space that can still come for
        // you. One in the other space (a wolf outside fighting someone else) or one its territory or leash holds back
        // from where you stand is not at your back, and must not be able to camp the way out.
        var body = _context.State.Body;
        long leash = _context.Setup.Combat.Constants.LeashMm;
        bool Pursues(CreatureState c) => Math.Sqrt(Math.Pow(body.XMm - c.HomeXMm, 2) + Math.Pow(body.ZMm - c.HomeZMm, 2))
            <= (c.Role.TerritoryMm > 0 ? c.Role.TerritoryMm : leash);
        if (_context.State.Creatures.Values.Any(c => c.Condition == CreatureCondition.Alive && c.Mind == CreatureMind.Engaged
                && c.Site.Space == _context.State.Space && Pursues(c)))
            return "Not while something is fighting you";

        if (_context.Setup.Layout.Portals.AtInterior(body.XMm, body.ZMm, ReachMm, _context.State.Space) is not { } portal)
            return "There is no way out from here";
        if (Gone(portal))
            return "That way is gone";

        _context.State.SetSpace(_owner, SpaceId.Exterior, SpaceTraversal.None);
        var out_ = new Body(portal.ExteriorXMm,
            _context.Setup.SpaceFor(SpaceId.Exterior).Terrain.HeightAtMm(portal.ExteriorXMm, portal.ExteriorZMm),
            portal.ExteriorZMm, body.FacingMdeg);
        _context.Dispatch(new Relocate(out_));
        _context.Dispatch(new ArriveWithParty(out_));
        return null;
    }

    /// <summary>
    /// Put the player outside whatever space they are in, without needing a portal to leave by.
    /// </summary>
    /// <remarks>
    /// Death is the caller. A return point is a place in the region's own space (S-38), so a character who dies
    /// inside an interior has to come OUT of it. Leaving them in the interior while moving the body to the
    /// stone's metres would stand them at the wrong coordinates in the wrong space, and every cell they then
    /// touched would be the wrong cell. This is deliberately not <see cref="Leave"/>: there is no portal here to
    /// anchor to and none is wanted - dying is not a door.
    /// </remarks>
    public void LeaveForDeath()
    {
        if (_context.State.Space.IsExterior)
            return;
        _context.State.SetSpace(_owner, SpaceId.Exterior, SpaceTraversal.None);
    }
}

/// <summary>
/// What a tier runs for the entities in its cells (D-06): the plug-in point for the coarser tiers. Tier A's work is the systems above; B
/// and C are the NPCs' regional and abstract simulations (<see cref="NpcTierSimulation"/>, WP12); D runs nothing per tick - stored state,
/// advanced an hour at a time with every life and by the offline catch-up (<see cref="IAbstractAdvance"/>).
/// </summary>
internal interface ITierSimulation
{
    SimulationTier Tier { get; }

    void Tick(long tick, IReadOnlyList<CellKey> cells);
}
