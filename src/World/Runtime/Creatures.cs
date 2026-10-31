// UNNAMED World - creatures at run time: spawners, perception, behaviour roles, corpses and respawn
// (SYSTEMS.md S-23, S-31; STEALTH_DETECTION_AND_THREAT.md §1-§7, §16; PROTOTYPE.md §4.1, §7.4; M3d)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Pets;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>One creature a spawner places: what it is, and the role it plays.</summary>
public sealed record SpawnMember(string CreatureId, string RoleId);

/// <summary>
/// A spawner (DATA_MODEL.md §4.17): where its creatures stand when a world starts, and - when it has a timer - how long
/// after a death each comes back. A roaming member walks <see cref="Route"/>.
/// </summary>
public sealed record SpawnSite(string Key, long XMm, long ZMm, long RadiusMm, ImmutableArray<SpawnMember> Members)
{
    public EncounterRecovery? Recovery { get; init; }
    /// <summary>
    /// The space this spawner stands in (M8 D10). Its creatures are in that space and in no other: a view is offered
    /// to a player standing there, a height comes from that space's ground, and a blow can only reach something that
    /// shares the attacker's space.
    /// </summary>
    public SpaceId Space { get; init; } = SpaceId.Exterior;

    /// <summary>Ticks from a death to the return, or 0 when the dead stay dead.</summary>
    public long RespawnTicks { get; init; }

    public ImmutableArray<(long XMm, long ZMm)> Route { get; init; } = ImmutableArray<(long XMm, long ZMm)>.Empty;
}

public sealed record EncounterRecovery(long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, long SafeXMm, long SafeZMm);
public sealed record EncounterRestarted(string SpawnKey, bool Loaded, long Tick);

public sealed record CreatureView(EntityId Id, string Key, string DefId, string RoleId, Body Body, int Health, int MaxHealth,
    CombatPhase Phase, int PhaseTicksLeft, CreatureCondition Condition, CreatureMind Mind, int Awareness, bool Asleep)
{
    public bool Rooted { get; init; }
    public bool Paralysed { get; init; }
    public bool Poisoned { get; init; }
    // Presentation's sampled clip, part of the deliberately transient blow (as Phase/PhaseTicksLeft are in StateDump).
    [System.Text.Json.Serialization.JsonIgnore]
    public string? AttackSource { get; init; }
    public long AttackTick { get; init; }
    public bool Enraged { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int PhaseDurationTicks { get; init; }
    public int AttackVariant { get; init; }
    public bool Guarding { get; init; }
    public bool Alive => Condition == CreatureCondition.Alive;

    public bool Hostile => Mind == CreatureMind.Engaged;

    /// <summary>It is fighting an animal of the party rather than the character: what bit it, while the grudge lasts.</summary>
    public bool OnAnAnimal { get; init; }

    /// <summary>What a <see cref="MoveItemCommand"/> names the body by while it lies there.</summary>
    public string CorpseKey => CreatureSystem.CorpseKeyOf(Key, Generation);

    public int Generation { get; init; }

    /// <summary>Its role never starts a fight (wildlife, the grazers).</summary>
    public bool Peaceful { get; init; }

    /// <summary>Its level (<c>level_band</c> in its content; one level per creature in Phase 1).</summary>
    public int Level { get; init; }

    /// <summary>It holds a loot table, so there is something in killing it (finding 24's "can be hunted").</summary>
    public bool Huntable { get; init; }

    /// <summary>What it does when it notices the character: passive, neutral until struck, or hostile.</summary>
    public Temperament Temperament { get; init; }

    /// <summary>How it reads against the character's own level (finding 24): the nameplate's colour and word.</summary>
    public Threat Threat { get; init; }
}

public sealed record CreatureNoticed(EntityId Creature, string Key, CreatureMind Mind, long Tick);

/// <summary>A creature called for help (a howl): packmates within earshot who answer calls come to it.</summary>
public sealed record CreatureCalled(EntityId Creature, string Key, long XMm, long ZMm, long Tick);

public sealed record CreatureRespawned(EntityId Creature, string Key, int Generation, long Tick);

/// <summary>A spawn cluster is saturated (AG-3): its respawns take twice as long until the window empties.</summary>
public sealed record SpawnerSaturated(string SpawnKey, long Tick);

public sealed record CorpseGone(string CorpseKey, long Tick);

/// <summary>A charge ran into something solid, and the charger is stunned by it.</summary>
public sealed record CreatureStunned(EntityId Creature, string Key, long Tick);

internal sealed record CreatureState(
    EntityId Id,
    string Key,
    SpawnSite Site,
    CreatureDefinition Definition,
    CreatureRole Role,
    long HomeXMm,
    long HomeZMm,
    int HomeFacingMdeg,
    int Generation,
    CreatureCondition Condition,
    Body Body,
    int Health,
    ActionState Action,
    long StaggerImmuneUntil)
{
    public CreatureMind Mind { get; init; }
    public int Awareness { get; init; }
    public bool Knows { get; init; }
    public long KnownXMm { get; init; }
    public long KnownZMm { get; init; }
    public long LastSeenTick { get; init; }
    public long SearchUntil { get; init; }
    public bool HasCalled { get; init; }
    public long NextChargeTick { get; init; }
    // Like a swing in progress, these reactions are transient. A loaded body
    // has to perceive the next windup anew; no unseen attack can provoke one.
    public long GuardUntil { get; init; }
    public int ComboStep { get; init; }
    public long ComboUntil { get; init; }
    public long ReactedToAttackTick { get; init; } = -1;
    public bool Dodges(long tick, CombatConstants constants) => Action.Kind == ActionKind.Dodge
        && Action.PhaseAt(tick, constants).Phase == CombatPhase.Dodge;
    public bool Blocks(Body attacker, AttackProfile attack, long tick, CombatConstants constants) =>
        GuardUntil > tick && DamageTypes.IsPhysical(attack.DamageType)
        && CombatRules.InFront(Body.XMm, Body.ZMm, Body.FacingMdeg, attacker.XMm, attacker.ZMm,
            long.MaxValue / 4, constants.BlockArcMdeg);
    public long DiedTick { get; init; }
    public long RespawnTick { get; init; }

    /// <summary>
    /// A lure has its attention (spec 43's control skill): until this tick it follows the decoy and does not notice the character. Runtime
    /// only, like a blow in progress - a lure is a working of the moment, not a fact about the animal.
    /// </summary>
    public long DrawnUntil { get; init; }

    /// <summary>
    /// It has been led off by a lure once, and does not go again: an enemy that fell for the trick has learned it (spec 43 ruling 5). It
    /// belongs to the animal, not to the decoy, and it is cleared by the reset - a respawned creature, or a load, begins knowing nothing.
    /// </summary>
    public bool LureLearned { get; init; }

    /// <summary>
    /// The animal that savaged it, and until when (spec 43 ruling 5, "enemies learn"): a creature struck by a pet turns on the pet for a few
    /// seconds rather than on the archer behind it - so a hound that bites a wolf is a hound the wolf fights. Transient, like a blow in
    /// progress: a load starts with no grudge, and the creature's own wounds are what it remembers.
    /// </summary>
    public EntityId? ProvokedBy { get; init; }

    public long ProvokedUntil { get; init; }

    /// <summary>
    /// How much of an encounter this body is (spec 43 ruling 5): 100 is exactly as authored, and a party's presence scales what a
    /// spawner places - its health, and the force of its blows. Derived when the world places it, so a save's wounded creature keeps
    /// the body it was wounded at.
    /// </summary>
    public int ScalePercent { get; init; } = 100;

    /// <summary>The levels the scale buys: what the nameplate shows and the reward honours (the fight is harder, not only longer).</summary>
    public int LevelBonus { get; init; }

    public double ScaleDamage => ScalePercent / 100.0;

    /// <summary>The health this body was placed with: what the spawner authored, scaled by the party it was placed against.</summary>
    public int ScaledMaxHealth => Math.Max(1, (int)Math.Round(Definition.MaxHealth * ScalePercent / 100.0));

    public bool Alive => Condition == CreatureCondition.Alive;
    public string CorpseKey => CreatureSystem.CorpseKeyOf(Key, Generation);
}

/// <summary>
/// To <see cref="CreatureSystem"/>: a resolved blow lands on a creature. Its owner applies it. The character struck it unless an
/// <see cref="Attacker"/> is named - a companion (M6), standing at <see cref="FromXMm"/>, <see cref="FromZMm"/>.
/// </summary>
internal sealed record WoundCreature(EntityId Target, HitResult Hit, string Source) : InternalCommand
{
    public EntityId? Attacker { get; init; }
    public string? AttackerDefId { get; init; }
    public long FromXMm { get; init; }
    public long FromZMm { get; init; }
}

/// <summary>To <see cref="CreatureSystem"/>: harm that is not a blow (an effect's tick).</summary>
internal sealed record HarmCreature(EntityId Target, string Source, int Amount) : InternalCommand;

internal sealed record HealCreature(EntityId Target, string Source, int Amount) : InternalCommand;

/// <summary>To <see cref="CombatSystem"/>: a creature's attack - its bite or its charge - reached the player; resolve it.</summary>
internal sealed record CreatureStrike(EntityId Attacker, AttackProfile Attack) : InternalCommand;

/// <summary>To <see cref="CreatureSystem"/>: the player died and woke elsewhere; every hunt for them ends.</summary>
internal sealed record ForgetPlayer : InternalCommand;

/// <summary>To <see cref="CreatureSystem"/>: a corpse was looted empty, so the body is gone.</summary>
internal sealed record CorpseEmptied(string CorpseKey) : InternalCommand;

/// <summary>To <see cref="InventorySystem"/>: a container stops existing (a corpse decays or its creature returns).</summary>
internal sealed record DiscardContainer(string Key) : InternalCommand;

/// <summary>
/// To <see cref="CreatureSystem"/>: a pet drew this creature off to a place (spec 43's control skill, the lure). It goes to look
/// there and loses the character for a while - a lure is information pulled out of a fight, never damage.
/// </summary>
internal sealed record DrawCreature(EntityId Target, long XMm, long ZMm, long Ticks) : InternalCommand;

/// <summary>To <see cref="CreatureSystem"/>: the animal has left the wild as the character's pet, and its slot stays empty.</summary>
internal sealed record TakeCreature(EntityId Target) : InternalCommand;

/// <summary>
/// To <see cref="CreatureSystem"/>: something was reached for and refused - the creature has seen the character, and the next hand
/// offered to it starts from further away.
/// </summary>
internal sealed record RouseCreature(EntityId Target) : InternalCommand;

/// <summary>
/// Owns: <see cref="StateSlice.Creatures"/> and the creature records of the world delta (S-23 and S-31). It places each
/// spawner's creatures, lets them perceive - sight in a cone past no wall, and sound, each carrying only so far - and act
/// on what they perceive, infer or are told through a call, in the manner of their role. There is no shared awareness:
/// a packmate out of earshot of a howl knows nothing (STEALTH §1, §5, §6). Creatures act only in tier-A cells. A dead
/// creature leaves a corpse its loot table fills; a spawner with a timer brings it back. Everything about a creature's
/// future is in its record - body, health, life, mind, the charge it must wait out, the stagger it is in (a charger's stun)
/// and the one it is immune to - the sounds made on a tick wait in the world delta for the next, and its idle wandering and
/// patrolling are functions of the world tick, so a loaded world goes on exactly as the saved one would have. Only a blow
/// in progress is not kept (schema 14; the Phase-1 technical audit, L-06). While the character is away (WP11) a corpse decays and a
/// spawner brings its creature back by their deadlines, and the living give up whatever they were doing and are at home.
/// </summary>
internal sealed class CreatureSystem : IAbstractAdvance
{
    private const double SampleScale = 18446744073709551616.0;

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;
    private readonly Func<MoveIntent> _intent;
    private long _graceUntil;

    // The creature the character struck last and when: transient (a load starts with none), read by a party's "focus my target".
    private string? _focusKey;
    private long _focusTick;

    /// <summary>The creature the character struck within the focus window before <paramref name="tick"/>, still alive, or null.</summary>
    public string? PlayerFocus(long tick) =>
        _focusKey is { } key && tick - _focusTick <= PartyTactics.FocusWindowTicks && State.Creatures.TryGetValue(key, out var struck) && struck.Alive ? key : null;

    /// <summary>
    /// How much of an encounter a party meets (spec 43 ruling 5): null in a pack with no pets system, which leaves every spawner at the
    /// numbers it authored. Asked each time the world places a creature, so an encounter is scaled to the party at that moment.
    /// </summary>
    public Func<EncounterScale>? Scale { get; set; }

    /// <summary>Whether a spawn slot's animal has been taken as a pet, so it does not stand in the wild again (spec 43's taming).</summary>
    public Func<string, bool>? TamedAway { get; set; }

    /// <summary>The animals on their feet, as bodies a creature can turn on, with the health a role that reads a fight needs (spec 43 ruling 5).</summary>
    public Func<ImmutableArray<(Body Body, EntityId Id, int Health, int MaxHealth)>>? PetsOnFeet { get; set; }

    /// <summary>The Orenth afterimages lingering now (owner ruling 2026-10-25): a creature after one of them may strike the ghost instead.</summary>
    public Func<IEnumerable<Afterimage>>? Afterimages { get; set; }

    private EncounterScale ScaleOf() => Scale?.Invoke() ?? EncounterScale.Authored;

    /// <summary>An attack as a scaled body throws it: the same windup and reach, more force behind it.</summary>
    private static AttackProfile Scaled(AttackProfile attack, int percent) => percent == 100 ? attack : attack with
    {
        DamageMin = Math.Max(1, (int)Math.Round(attack.DamageMin * percent / 100.0)),
        DamageMax = Math.Max(1, (int)Math.Round(attack.DamageMax * percent / 100.0)),
    };

    private sealed record TickSpace(Blocker[] Walls, Blocker[] Doors, Blocker[] People,
        Blocker[] CompanionBodies, NpcState[] Companions);
    private readonly Dictionary<SpaceId, TickSpace> _tickSpaces = new();
    private ImmutableSortedDictionary<string, CreatureState>? _viewSource;
    private ImmutableArray<CreatureView> _views;
    private long _viewTick;
    private SpaceId _viewSpace;
    // Derived only, per world: spawn-member keys stay the same across generations. Keep one channel per member, not per leg.
    private readonly Dictionary<string, ulong> _phaseHashes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (ulong Seed, CellKey Cell, long Leg, RngChannel Channel)> _wanderChannels = new(StringComparer.Ordinal);

    // Doors and people cannot move during this system's tick. Snapshot each space once, not once per creature/query.
    private TickSpace Obstacles(SpaceId space)
    {
        if (_tickSpaces.TryGetValue(space, out var cached)) return cached;
        var doors = _context.ClosedDoors(space).ToArray();
        var people = State.Npcs.Values.Where(n => n.Space == space).ToArray();
        var companions = people.Where(n => State.Companions.TryGetValue(n.Definition.Id, out var c)
            && c.Condition == CompanionCondition.Up).ToArray();
        Blocker BodyOf(NpcState n) => new CircleBlocker(n.Definition.Id, n.Body.XMm, n.Body.ZMm,
            _context.Setup.Movement.BodyRadiusMm, 0);
        return _tickSpaces[space] = new TickSpace(
            _context.Setup.SpaceFor(space).Blockers.Concat(doors).Where(b => space.IsExterior || b.ClearanceMm < 1800).ToArray(),
            doors, people.Select(BodyOf).ToArray(), companions.Select(BodyOf).ToArray(), companions);
    }
    private readonly Dictionary<string, int> _outsideAttemptTicks = new(StringComparer.Ordinal);

    public CreatureSystem(SystemContext context, SliceOwner owner, EntityId player, Func<MoveIntent> intent)
    {
        _context = context;
        _owner = owner;
        _player = player;
        _intent = intent;
    }

    private CombatSetup Setup => _context.Setup.Combat;
    private CombatConstants C => Setup.Constants;
    private RuntimeState State => _context.State;
    private int TickMs => _context.Setup.TickMilliseconds;

    /// <summary>A corpse's container key: a valid definition-ID-shaped name, unique per creature and generation.</summary>
    public static string CorpseKeyOf(string key, int generation)
    {
        int hash = key.LastIndexOf('#');
        string spawner = key[..hash].Replace("spawn.", string.Empty, StringComparison.Ordinal).Replace('.', '_');
        return $"corpse.{spawner}.m{key[(hash + 1)..]}_g{generation}";
    }

    /// <summary>The node a body worth skinning leaves: it lies there exactly as long as the corpse does.</summary>
    private static string CarcassKey(CreatureState c) => $"carcass.{c.Id}";

    /// <summary>A creature's identity from its name and generation: the same wolf is the same ID on every start.</summary>
    private static EntityId Identity(string key, int generation)
    {
        using var h = new CanonicalHasher();
        return EntityId.Create(EntityKind.Creature, 1, h.Add("unnamed.creature/v2").Add(key).Add(generation).FinishBytes().AsSpan(0, 10));
    }

    // ── placement ───────────────────────────────────────────────────────────

    /// <summary>World start: every spawner's creatures at their baseline places, then each record's divergence over it.</summary>
    public void Populate()
    {
        foreach (var site in Setup.Spawns)
        {
            // The spawner's own ground and space: a ring spawner's creatures stand on the ring's floor, and its cell
            // is a ring cell. Taking the layout's space gave every co-loaded ring creature an EXTERIOR key, so the
            // Custodian's defeat flag would have landed in a frontier cell (review 3, B8).
            var space = _context.Setup.SpaceFor(site.Space);
            var cell = _context.CellOfSite(site.Space, site.XMm, site.ZMm);
            for (int i = 0; i < site.Members.Length; i++)
            {
                var member = site.Members[i];
                var definition = Setup.Creatures[member.CreatureId];
                var role = Setup.Roles.GetValueOrDefault(member.RoleId) ?? new CreatureRole(member.RoleId, UnawareBehaviour.Hold);
                string key = $"{site.Key}#{i}";
                // Taken as a pet (spec 43): the wild animal does not stand here again, and the empty slot is the player's own doing.
                if (TamedAway?.Invoke(key) == true)
                    continue;
                var scale = ScaleOf();
                var channel = RngChannel.Open(State.World.WorldSeed, cell, "spawn", key);
                var others = State.Creatures.Values.Select(c => (Blocker)new CircleBlocker(c.Key, c.HomeXMm, c.HomeZMm, c.Definition.RadiusMm, 0)).ToList();
                (long x, long z) = (site.XMm, site.ZMm);
                for (uint sample = 0; sample < 32; sample += 2)
                {
                    double angle = channel.UInt64(sample) / SampleScale * 2 * Math.PI;
                    double distance = Math.Sqrt(channel.UInt64(sample + 1) / SampleScale) * site.RadiusMm;
                    long cx = site.XMm + (long)Math.Round(Math.Sin(angle) * distance), cz = site.ZMm + (long)Math.Round(Math.Cos(angle) * distance);
                    if (Kinematics.IsClear(cx, cz, definition.RadiusMm, space, others, site.Space.IsExterior ? 0 : 1800))
                    {
                        (x, z) = (cx, cz);
                        break;
                    }
                }
                int facing = (int)(channel.UInt64(40) % 360_000);
                var body = new Body(x, space.HeightAtMm(x, z), z, facing);
                var state = new CreatureState(Identity(key, 0), key, site, definition, role, x, z, facing, 0, CreatureCondition.Alive, body,
                    Math.Max(1, (int)Math.Round(definition.MaxHealth * scale.HealthFactor)), ActionState.Idle, 0)
                {
                    ScalePercent = scale.Percent,
                    LevelBonus = scale.Levels,
                };
                if (State.World.Creature(key) is { } record)
                {
                    state = state with
                    {
                        Id = record.InstanceId,
                        Generation = record.Generation,
                        Condition = record.Condition,
                        Body = new Body(record.XMm, space.HeightAtMm(record.XMm, record.ZMm), record.ZMm, record.FacingMdeg),
                        Health = record.Health,
                        ScalePercent = record.ScalePercent,
                        LevelBonus = record.LevelBonus,
                        DiedTick = record.DiedTick,
                        RespawnTick = record.RespawnTick,
                        Mind = record.Mind,
                        Awareness = record.Awareness,
                        Knows = record.Knows,
                        KnownXMm = record.KnownXMm,
                        KnownZMm = record.KnownZMm,
                        LastSeenTick = record.LastSeenTick,
                        SearchUntil = record.SearchUntil,
                        HasCalled = record.HasCalled,
                        NextChargeTick = record.NextChargeTick,
                        StaggerImmuneUntil = record.StaggerImmuneUntil,
                        Action = record.StaggeredTick is { } staggered
                            ? ActionState.Begin(ActionKind.Staggered, staggered) with { LastsTicks = record.StaggerLastsTicks }
                            : ActionState.Idle,
                    };
                }
                State.SetCreature(_owner, state);
                // A carcass is a node only the running game holds: a body loaded as a corpse lies where it fell again, still to be skinned.
                if (state.Condition == CreatureCondition.Corpse && definition.CarcassRef is { } carcass)
                    _context.Dispatch(new LeaveCarcassCommand(CarcassKey(state), carcass, state.Body.XMm, state.Body.ZMm));
                // Party composition changes later. Even an untouched authored-size
                // creature needs its spawn scale retained instead of being resized on load.
                if (Scale is not null && State.World.Creature(key) is null) Save(state);
            }
        }
    }

    // ── the absence and the login ───────────────────────────────────────────

    /// <summary>
    /// The world going on while the character was away. Closed-form: whatever fell due by <paramref name="toTick"/> has happened - a corpse
    /// is gone, a creature whose timer ran out is back at home (the character was not there to stand over the place) - and a living one is
    /// at home, unaware, as if it had long since given up and gone back. The sounds waiting to be heard are long gone. Every outcome depends
    /// only on the deadlines and <paramref name="toTick"/>, so the same absence split anywhere ends the same.
    /// </summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
        State.SetNoises(_owner, ImmutableArray<Noise>.Empty);
        foreach (string key in State.Creatures.Keys.ToList())
        {
            var creature = State.Creatures[key];
            if (creature.Site.Space.IsExterior)
                Save(creature.Alive ? AtHome(creature) : Afterlife(creature, toTick, away: true));
        }
    }

    /// <summary>A living creature after an absence: at home as it stood at the start, its fight and its search forgotten. Its wounds stay.</summary>
    private CreatureState AtHome(CreatureState c) => c with
    {
        Body = new Body(c.HomeXMm, _context.Setup.SpaceFor(c.Site.Space).HeightAtMm(c.HomeXMm, c.HomeZMm), c.HomeZMm, c.HomeFacingMdeg),
        Mind = CreatureMind.Unaware,
        Awareness = 0,
        Knows = false,
        KnownXMm = 0,
        KnownZMm = 0,
        LastSeenTick = 0,
        SearchUntil = 0,
        HasCalled = false,
        NextChargeTick = 0,
        StaggerImmuneUntil = 0,
        Action = ActionState.Idle,
    };

    // Load is an explicit attempt boundary. Saved player resources and generic effects are untouched.
    public bool RecoverLoadedAttempt()
    {
        bool recovered = false;
        foreach (var c in State.Creatures.Values.Where(c => c.Alive && c.Site.Space == State.Space
                     && c.Site.Recovery is not null && c.Mind == CreatureMind.Engaged).ToList())
        {
            var safe = c.Site.Recovery!;
            Save(AtHome(c) with { Health = c.ScaledMaxHealth });
            var arrival = new Body(safe.SafeXMm, _context.Setup.SpaceFor(State.Space).HeightAtMm(safe.SafeXMm, safe.SafeZMm), safe.SafeZMm, State.Body.FacingMdeg);
            _context.Dispatch(new Relocate(arrival));
            _context.Dispatch(new RestageEncounterParty(arrival, safe));
            _context.Events.Publish(new EncounterRestarted(c.Site.Key, true, State.WorldTick));
            recovered = true;
        }
        return recovered;
    }

    /// <summary>The login grace: until <paramref name="untilTick"/> no creature sees or hears the character (WP11).</summary>
    public void BeginGrace(long untilTick) => _graceUntil = untilTick;

    public long GraceUntil => _graceUntil;

    private bool InGrace(long tick) => tick <= _graceUntil;

    // ── the tick ────────────────────────────────────────────────────────────

    public void Tick(long tick)
    {
        _tickSpaces.Clear();
        var noises = (InGrace(tick) ? Enumerable.Empty<Noise>() : PlayerNoises()).Concat(State.World.Noises).ToList();
        State.SetNoises(_owner, ImmutableArray<Noise>.Empty);
        _stealthLevel = ProgressionEngine.SkillLevel(State.Progression, "skill.stealth");
        PracticeStealth(tick);
        foreach (string key in State.Creatures.Keys.ToList())
        {
            var creature = State.Creatures[key];
            // Leaving or dying resets an encounter to full health (owner ruling OQ-17); only a defeat is durable. A
            // wound counts as well as a fight still under way, so a guardian that lost track of the character keeps
            // nothing either. Out of its space - through a portal, or respawned outside - it resets at once, quietly: there
            // is nobody in the court to restage or to tell.
            bool attempted = creature.Alive && creature.Site.Recovery is not null
                             && (creature.Mind == CreatureMind.Engaged || creature.Health < creature.ScaledMaxHealth);
            if (attempted && creature.Site.Space != State.Space)
            {
                Save(AtHome(creature) with { Health = creature.ScaledMaxHealth });
                _outsideAttemptTicks.Remove(key);
                continue;
            }
            if (!creature.Site.Space.IsExterior && creature.Site.Space != State.Space)
                continue;
            if (attempted && creature.Site.Recovery is { } court)
            {
                var player = State.Body;
                bool outside = player.XMm < court.MinXMm || player.XMm > court.MaxXMm
                               || player.ZMm < court.MinZMm || player.ZMm > court.MaxZMm || State.PlayerCombat.Defeated;
                int count = outside ? _outsideAttemptTicks.GetValueOrDefault(key) + 1 : 0;
                _outsideAttemptTicks[key] = count;
                if (count >= 20)
                {
                    Save(AtHome(creature) with { Health = creature.ScaledMaxHealth });
                    var arrival = new Body(court.SafeXMm, _context.Setup.SpaceFor(State.Space).HeightAtMm(court.SafeXMm, court.SafeZMm), court.SafeZMm, 0);
                    _context.Dispatch(new RestageEncounterParty(arrival, court));
                    _context.Events.Publish(new EncounterRestarted(creature.Site.Key, false, tick));
                    _outsideAttemptTicks.Remove(key);
                    continue;
                }
            }
            Save(creature.Alive ? Live(creature, tick, creature.Site.Space == State.Space ? noises : new List<Noise>())
                : Afterlife(creature, tick, timer: _context.SpaceTick(creature.Site.Space, tick)));
        }
    }

    /// <summary>A sound made now - a blow, a howl - that creatures hear on the next tick. It waits in the world delta, so a save keeps it.</summary>
    private void Sound(Noise noise) => State.SetNoises(_owner, State.World.Noises.Add(noise));

    /// <summary>The sounds the player makes this tick: footfalls by gait, and a swing.</summary>
    private IEnumerable<Noise> PlayerNoises()
    {
        var combat = State.PlayerCombat;
        if (combat.Defeated)
            yield break;
        var body = State.Body;
        var intent = _intent();
        var sound = Setup.Noise;
        long radius = !intent.IsMoving ? 0 : combat.Blocking ? sound.WalkMm : intent.Gait switch
        {
            Gait.Walk => sound.WalkMm,
            Gait.Sprint when (State.Progression.Pools.Stamina ?? 1) > 0 => sound.SprintMm,
            _ => sound.RunMm,
        };
        var (phase, _) = combat.Action.PhaseAt(State.WorldTick + 1, C);
        if (combat.Action.Kind == ActionKind.Attack && phase is CombatPhase.Windup or CombatPhase.Active)
            radius = Math.Max(radius, sound.SwingMm);
        // A stealthy character makes less of every sound (finding 26: -15/30/45% of the effective radius by rank).
        if (radius > 0)
            radius = Math.Max(1, (long)Math.Round(radius * StealthNoiseFactor(), MidpointRounding.AwayFromZero));
        if (radius > 0)
            yield return new Noise(body.XMm, body.ZMm, radius);
    }

    /// <summary>
    /// The character's stealth level, read once a tick (finding 26: the skill levels with use) rather than once per creature per tick - the
    /// awareness factor is asked for by every body that can see the character, and offline catch-up ticks thousands of them.
    /// </summary>
    private int _stealthLevel;

    private double StealthNoiseFactor() => StealthRules.NoiseFactor(_stealthLevel);

    private double StealthAwarenessFactor() => StealthRules.AwarenessFactor(_stealthLevel);

    private const long StealthPracticeTicks = 60;
    private long _nextStealthPractice;

    /// <summary>
    /// The skill levels with use (finding 26, PROGRESSION.md §4.2): moving crouched while nothing has noticed the character is practice, one
    /// use every few seconds rather than one a tick. Its harder uses - a takedown, a body left unaware - are practiced where they happen.
    /// </summary>
    private void PracticeStealth(long tick)
    {
        if (tick < _nextStealthPractice || State.PlayerCombat.Defeated || State.Posture.Stance != Stance.Crouched || !_intent().IsMoving)
            return;
        if (State.Creatures.Values.Any(c => c.Alive && c.Mind == CreatureMind.Engaged && c.Site.Space == State.Space))
            return;
        _nextStealthPractice = tick + StealthPracticeTicks;
        _context.Dispatch(new PracticeSkill(new SkillPractice("skill.stealth", 1, PracticeOutcome.Success, tick)));
    }

    private CreatureState Live(CreatureState c, long tick, List<Noise> noises)
    {
        if (TierOf(c) != SimulationTier.A)
            return c;
        var (phase, _) = c.Action.PhaseAt(tick, C);
        if (c.Action.Kind != ActionKind.Idle && phase == CombatPhase.Idle)
            c = c with { Action = ActionState.Idle };
        c = Perceive(c, tick, noises);
        var control = _context.ControlOn(c.Id);
        if (control == ControlKind.Incapacitates) return c with { Action = ActionState.Idle };
        if (control == ControlKind.Roots && c.Action.Kind == ActionKind.Charge)
        {
            c = c with { Action = ActionState.Idle };
            phase = CombatPhase.Idle;
        }
        var next = phase switch
        {
            CombatPhase.Staggered or CombatPhase.Recovery => c,
            CombatPhase.Windup => Aim(c),
            CombatPhase.Dodge => c with { Body = Move(c, c.Body.XMm + c.Action.DirXPermille * 2,
                c.Body.ZMm + c.Action.DirZPermille * 2, Gait.Run,
                C.DodgeDistanceMm * 1000 / Math.Max(1, C.DodgeTicks * TickMs)) with { FacingMdeg = c.Body.FacingMdeg } },
            CombatPhase.Active when c.Action.Kind == ActionKind.Charge => Run(c, tick),
            CombatPhase.Active => Strike(c, tick),
            _ => Act(c, tick),
        };
        if (control == ControlKind.Roots)
            next = next with { Body = next.Body with { XMm=c.Body.XMm, YMm=c.Body.YMm, ZMm=c.Body.ZMm } };
        // Acting changes its mind too - it arrives, gives up, gets home; those are announced like what it perceives.
        if (next.Mind != c.Mind)
            _context.Events.Publish(new CreatureNoticed(next.Id, next.Key, next.Mind, tick));
        return next;
    }

    // ── perception and the mind ─────────────────────────────────────────────

    /// <summary>
    /// What it sees and hears this tick, and what that changes: awareness rises with sight and fades without it; a
    /// sound gives a place to look; a call from a packmate brings those who answer calls to the caller.
    /// </summary>
    private CreatureState Perceive(CreatureState c, long tick, List<Noise> noises)
    {
        var rules = Setup.Awareness;
        // A lit torch in hand is seen from further off (its stealth cost, WP3a).
        var senses = c.Definition.Senses is var own && _context.PlayerStealthPenalty() is > 0 and var penalty
            ? own with { SightMm = (long)Math.Round(own.SightMm * (1 + penalty)) }
            : c.Definition.Senses;
        var player = State.Body;
        bool target = c.Site.Space == State.Space && !State.PlayerCombat.Defeated && !InGrace(tick);
        bool asleep = Asleep(c, tick);
        // A lure has it: it is watching where the decoy went, and the character is not what it is looking at (spec 43's control skill).
        bool drawn = tick < c.DrawnUntil;
        double? seen = !asleep && target && !drawn ? Perception.Sees(c.Body, senses, player.XMm, player.ZMm, Walls(c)) : null;
        // The character's stealth rank slows how fast a sighting registers (finding 26) - it never stops it (§20: no hidden flag).
        int awareness = Perception.Accrue(c.Awareness, seen, senses.SightMm, rules, TickMs, StealthAwarenessFactor());
        var next = c with { Awareness = awareness };
        if (seen is not null)
            next = next with { Knows = true, KnownXMm = player.XMm, KnownZMm = player.ZMm, LastSeenTick = tick };

        long hearing = asleep ? senses.HearingMm * c.Role.SleepHearingPercent / 100 : senses.HearingMm;
        bool pounce = false;
        foreach (var noise in noises)
        {
            if (!Perception.Hears(c.Body, hearing, noise) || (noise.XMm == c.Body.XMm && noise.ZMm == c.Body.ZMm))
                continue;
            // A call is its own kind's: a howl brings wolves, not whatever else is in earshot.
            if (noise.Call && (!c.Role.AnswersCalls || PackKind(noise.CallerKind ?? "") != PackKind(c.Definition.Id)))
                continue;
            int level = noise.Call ? rules.HeardCall : rules.HeardNoise;
            if (next.Mind == CreatureMind.Engaged && next.Knows && seen is not null)
                continue;   // already watching the target; a sound adds nothing
            next = next with { Awareness = Math.Max(next.Awareness, level), Knows = true, KnownXMm = noise.XMm, KnownZMm = noise.ZMm };
            // An ambusher feels a footfall in its territory and goes for it at once.
            pounce |= !noise.Call && c.Role.PounceOnNoise
                      && (c.Role.TerritoryMm <= 0 || Distance(noise.XMm, noise.ZMm, c.HomeXMm, c.HomeZMm) <= c.Role.TerritoryMm);
            // A herd defends its own: a peaceful creature that answers its kind's call goes straight to the fight.
            pounce |= noise.Call && c.Role.Peaceful;
        }
        if (pounce && target)
            next = next with { Mind = CreatureMind.Engaged, LastSeenTick = tick };
        return Decide(next, c.Mind, seen, tick);
    }

    private CreatureState Decide(CreatureState c, CreatureMind was, double? seen, long tick)
    {
        var rules = Setup.Awareness;
        var player = State.Body;
        double fromHome = Distance(c.HomeXMm, c.HomeZMm, player.XMm, player.ZMm);
        var mind = c.Mind;
        // At rest or on its way home, it only takes notice of what is inside the ground it keeps.
        bool noticing = mind is not (CreatureMind.Unaware or CreatureMind.Returning) || Keeps(c, c.KnownXMm, c.KnownZMm);
        // A peaceful creature turns on someone only once roused - wounded, or called to its herd's fight - and goes on until it gives up.
        bool roused = !c.Role.Peaceful || mind is CreatureMind.Engaged or CreatureMind.Searching;
        double toPlayer = Distance(c.Body.XMm, c.Body.ZMm, player.XMm, player.ZMm);
        if (State.PlayerCombat.Defeated || c.Site.Space != State.Space)
            mind = mind is CreatureMind.Unaware ? mind : CreatureMind.Returning;
        else if (mind == CreatureMind.Fleeing)
        {
            // A bolting animal runs well clear of the distance that set it off before it drifts back.
            if (seen is null && toPlayer > Math.Max(C.LeashMm / 2, 2 * c.Role.FleeWithinMm))
                mind = CreatureMind.Returning;
        }
        else if (!roused && c.Role.FleeWithinMm > 0 && c.Awareness >= rules.Suspicious && c.Knows && toPlayer <= c.Role.FleeWithinMm)
            mind = CreatureMind.Fleeing;
        else if (seen is not null && c.Awareness >= Perception.Full && noticing && roused)
            mind = CreatureMind.Engaged;
        else if (mind == CreatureMind.Engaged && seen is null && tick - c.LastSeenTick > 20)
            mind = CreatureMind.Searching;
        else if (mind is CreatureMind.Unaware or CreatureMind.Returning && c.Awareness >= rules.Suspicious && noticing && !c.Role.Peaceful)
            mind = CreatureMind.Suspicious;
        else if (mind is CreatureMind.Suspicious && c.Awareness < rules.Suspicious / 2)
            mind = CreatureMind.Returning;

        // A hunt is bounded by the leash; a territory only stops the chase at its edge (Engage holds the line).
        if (mind == CreatureMind.Engaged && fromHome > C.LeashMm)
            mind = CreatureMind.Returning;
        if (mind is CreatureMind.Engaged && Frightened(c))
            mind = CreatureMind.Fleeing;

        var next = c with { Mind = mind };
        // Giving up is forgetting: a returning creature needs something new to notice before it turns again.
        if (mind == CreatureMind.Returning && was != CreatureMind.Returning)
            next = next with { Awareness = 0, Knows = false };
        if (mind == CreatureMind.Searching && was != CreatureMind.Searching)
            next = next with { SearchUntil = 0 };
        if (mind == CreatureMind.Engaged && c.Role.CallsForHelp && !c.HasCalled)
        {
            next = next with { HasCalled = true };
            Sound(new Noise(c.Body.XMm, c.Body.ZMm, Math.Max(Setup.Noise.CallMm, c.Role.CallMm), Call: true, CallerKind: c.Definition.Id));
            _context.Events.Publish(new CreatureCalled(c.Id, c.Key, c.Body.XMm, c.Body.ZMm, tick));
        }
        if (mind is CreatureMind.Unaware or CreatureMind.Returning)
            next = next with { HasCalled = false };
        if (mind != was)
            _context.Events.Publish(new CreatureNoticed(c.Id, c.Key, mind, tick));
        return next;
    }

    private static string PackKind(string id) => id is "creature.beast.wolf_grey" or "creature.beast.wolf_grey_alpha" ? "grey_wolf_pack" : id;

    private bool Frightened(CreatureState c) =>
        c.Role.FleeBelowPercent > 0 && c.Health * 100 < c.ScaledMaxHealth * c.Role.FleeBelowPercent;

    // ── acting ──────────────────────────────────────────────────────────────

    private static bool Enraged(CreatureState c) => c.Definition.Tags.Contains("boss") && c.Health * 2 < c.ScaledMaxHealth;
    private static AttackProfile PhaseMove(CreatureState c, AttackProfile attack) => Enraged(c)
        ? attack with { RecoveryTicks=Math.Max(4,attack.RecoveryTicks*3/4), DamageMin=attack.DamageMin*6/5,DamageMax=attack.DamageMax*6/5 }
        : attack;

    private CreatureState Act(CreatureState c, long tick)
    {
        var player = State.Body;
        switch (c.Mind)
        {
            case CreatureMind.Engaged:
                return Engage(c, tick);
            case CreatureMind.Fleeing:
            {
                double dx = c.Body.XMm - player.XMm, dz = c.Body.ZMm - player.ZMm;
                double length = Math.Max(1, Math.Sqrt(dx * dx + dz * dz));
                return c with { Body = Move(c, c.Body.XMm + (long)(dx / length * 5_000), c.Body.ZMm + (long)(dz / length * 5_000), Gait.Run) };
            }
            case CreatureMind.Suspicious:
                // Look first; go to look once sure enough something is there - and only inside the ground it keeps.
                var facing = Turn(c, CombatRules.FacingTowards(c.Body.XMm, c.Body.ZMm, c.KnownXMm, c.KnownZMm));
                if (c.Awareness < Setup.Awareness.HeardNoise || !Keeps(c, c.KnownXMm, c.KnownZMm))
                    return c with { Body = facing };
                if (Arrived(c, c.KnownXMm, c.KnownZMm))
                    return c with { Mind = CreatureMind.Searching, SearchUntil = 0 };
                // A call is answered at a run; a noise is looked into at a walk.
                return c with { Body = Move(c, c.KnownXMm, c.KnownZMm, c.Awareness >= Setup.Awareness.HeardCall ? Gait.Run : Gait.Walk) };
            case CreatureMind.Searching:
                // One budget for getting there and looking about: a place it cannot reach is given up like one it searched.
                if (c.SearchUntil == 0)
                    c = c with { SearchUntil = tick + 2L * Setup.Awareness.SearchTicks };
                if (tick >= c.SearchUntil || !Keeps(c, c.KnownXMm, c.KnownZMm))
                    return c with { Mind = CreatureMind.Returning, Knows = false, Awareness = 0 };
                if (!Arrived(c, c.KnownXMm, c.KnownZMm))
                    return c with { Body = Move(c, c.KnownXMm, c.KnownZMm, Gait.Run) };
                return Wander(c, c.KnownXMm, c.KnownZMm, 4_000, tick);
            case CreatureMind.Returning:
                if (Arrived(c, c.HomeXMm, c.HomeZMm))
                    return c with { Mind = CreatureMind.Unaware, Knows = false, LastSeenTick = 0, SearchUntil = 0 };
                return c with { Body = Move(c, c.HomeXMm, c.HomeZMm, Gait.Walk) };
            default:
                return Idle(c, tick);
        }
    }

    /// <summary>Asleep: a sleeper at rest, or a creature that keeps hours lying up in its resting time - blind, and hearing less.</summary>
    private bool Asleep(CreatureState c, long tick) =>
        _context.State.Effects.GetValueOrDefault(c.Id, ImmutableArray<ActiveEffect>.Empty).Any(e => e.EffectId == "effect.sleep")
        || c.Mind == CreatureMind.Unaware && (c.Role.Unaware == UnawareBehaviour.Sleep || Resting(c.Role, tick));

    private bool Resting(CreatureRole role, long tick) => role.Rests != RestTime.Never && Setup.Hours is { } hours
        && hours.IsNight(tick) == (role.Rests == RestTime.Night);

    /// <summary>Unaware: the role decides - hold the place, drift around it, walk the route, or sleep; in its resting hours it lies up at home.</summary>
    private CreatureState Idle(CreatureState c, long tick)
    {
        switch (Resting(c.Role, tick) ? UnawareBehaviour.Sleep : c.Role.Unaware)
        {
            case UnawareBehaviour.Wander:
                return Wander(c, c.HomeXMm, c.HomeZMm, c.Role.WanderMm, tick);
            case UnawareBehaviour.Patrol when !c.Site.Route.IsEmpty:
            {
                // The route is walked end to end and back, one leg per period: where it heads follows from the tick alone.
                int n = c.Site.Route.Length;
                int cycle = Math.Max(1, 2 * (n - 1));
                int leg = (int)(((tick + Offset(c.Key, PatrolLegTicks)) / PatrolLegTicks) % cycle);
                var (x, z) = c.Site.Route[n == 1 ? 0 : leg < n ? leg : cycle - leg];
                return Arrived(c, x, z) ? c : c with { Body = Move(c, x, z, Gait.Walk) };
            }
            default:
                if (!Arrived(c, c.HomeXMm, c.HomeZMm))
                    return c with { Body = Move(c, c.HomeXMm, c.HomeZMm, Gait.Walk) };
                return c;
        }
    }

    private const long WanderLegTicks = 120;
    private const long PatrolLegTicks = 400;

    /// <summary>
    /// Drift between points around a place, a leg per period: each period's point is rolled from the creature's name and
    /// the period, so the drift follows from the tick alone. Arrived early, it waits for the next.
    /// </summary>
    private CreatureState Wander(CreatureState c, long aroundX, long aroundZ, long radius, long tick)
    {
        if (radius <= 0)
            return c;
        long leg = (tick + Offset(c.Key, WanderLegTicks)) / WanderLegTicks;
        // The creature's own space, not the player's: keyed on the player's, every exterior creature's stream changed
        // the moment the character walked through a door (review 3, B8).
        var cell = CellKey.OfInterior(c.Site.Space, aroundX / 1000.0, aroundZ / 1000.0);
        ulong seed = State.World.WorldSeed;
        if (!_wanderChannels.TryGetValue(c.Key, out var cached) || cached.Seed != seed || cached.Cell != cell || cached.Leg != leg)
        {
            cached = (seed, cell, leg, RngChannel.Open(seed, cell, "wander", $"{c.Key}@{leg}"));
            _wanderChannels[c.Key] = cached;
        }
        var channel = cached.Channel;
        double angle = channel.UInt64(0) / SampleScale * 2 * Math.PI, distance = Math.Sqrt(channel.UInt64(1) / SampleScale) * radius;
        long x = aroundX + (long)Math.Round(Math.Sin(angle) * distance), z = aroundZ + (long)Math.Round(Math.Cos(angle) * distance);
        return Arrived(c, x, z) ? c : c with { Body = Move(c, x, z, Gait.Walk) };
    }

    /// <summary>A stable per-creature phase, so a spawner's creatures do not all set off on the same tick.</summary>
    private long Offset(string key, long period)
    {
        if (!_phaseHashes.TryGetValue(key, out ulong hash))
        {
            using var h = new CanonicalHasher();
            hash = BitConverter.ToUInt64(h.Add("unnamed.creature-phase/v1").Add(key).FinishBytes(), 0);
            _phaseHashes[key] = hash;
        }
        return (long)(hash % (ulong)period);
    }

    /// <summary>
    /// Fight: close and bite, flanking if a pack hunter, charging if it can; a wary role keeps its distance until it must
    /// strike. It goes where it last perceived its target - where it saw it this tick, or where it heard it - never to
    /// where it cannot know the target is. A blow lands only on what is really there.
    /// </summary>
    private CreatureState Engage(CreatureState c, long tick)
    {
        var foe = Foe(c);
        var player = foe.Body;
        if (PackKind(c.Definition.Id) == "grey_wolf_pack")
        {
            var pack = State.Creatures.Values.Where(o => o.Alive && o.Site.Space == c.Site.Space
                && PackKind(o.Definition.Id) == "grey_wolf_pack" && o.Mind == CreatureMind.Engaged
                && Distance(o.Body.XMm, o.Body.ZMm, player.XMm, player.ZMm) < 14_000)
                .OrderBy(o => o.Key, StringComparer.Ordinal).ToArray();
            bool otherStriking = pack.Any(o => o.Key != c.Key && o.Action.PhaseAt(tick, C).Phase is CombatPhase.Windup or CombatPhase.Active);
            bool turn = pack.Length < 2 || pack[(int)(tick / 20 % pack.Length)].Key == c.Key;
            if (c.Health * 100 < c.ScaledMaxHealth * 30 || otherStriking || !turn)
            {
                int index = Array.FindIndex(pack, o => o.Key == c.Key);
                double angle = player.FacingMdeg / 1000.0 * Math.PI / 180 + Math.PI / 2 + index * 2.4 + tick * .012;
                double radius = c.Health * 100 < c.ScaledMaxHealth * 30 ? 8_000 : 2_400;
                long x = player.XMm + (long)(Math.Sin(angle) * radius), z = player.ZMm + (long)(Math.Cos(angle) * radius);
                return c with { Body = Move(c, x, z, Gait.Run) };
            }
        }
        bool atCharacter = foe.Id == _player && foe.Decoy is null;
        var attack = Scaled(c.Definition.Attack, c.ScalePercent);
        // The character is gone after where they were last perceived; a companion or an animal that stands nearer is simply there (M6).
        long targetX = atCharacter && c.Knows ? c.KnownXMm : player.XMm, targetZ = atCharacter && c.Knows ? c.KnownZMm : player.ZMm;
        double distance = Distance(c.Body.XMm, c.Body.ZMm, player.XMm, player.ZMm);
        if (!c.Definition.Moves.IsDefaultOrEmpty)
        {
            var choices = c.Definition.Moves.Where(a => !a.IsCharge
                && distance <= a.ReachMm + a.LungeMm + _context.Setup.Movement.BodyRadiusMm - 100).ToArray();
            if (choices.Length > 0)
            {
                var rng = RngChannel.Open(State.World.WorldSeed, _context.CellOf(c.Body), "combat.move", $"{c.Key}@{tick}");
                attack = Scaled(choices[(int)(rng.UInt64(0) % (ulong)choices.Length)], c.ScalePercent);
            }
        }
        if (c.Definition.CloseAttack is { } close && distance <= close.ReachMm + _context.Setup.Movement.BodyRadiusMm - 100)
            attack = Scaled(close, c.ScalePercent);
        bool humanoidCombo = c.Definition.Tags.Contains("humanoid") && c.Definition.CloseAttack is not null;
        if (humanoidCombo && c.ComboStep >= 2 && c.ComboUntil >= tick)
            attack = Scaled(c.Definition.Attack, c.ScalePercent);
        double toKnown = Distance(c.Body.XMm, c.Body.ZMm, targetX, targetZ);
        int facing = CombatRules.FacingTowards(c.Body.XMm, c.Body.ZMm, targetX, targetZ);
        // A territory holds the line: past its edge the guardian goes back and faces the intruder from inside.
        if (c.Role.TerritoryMm > 0 && !Keeps(c, targetX, targetZ))
        {
            if (Distance(c.Body.XMm, c.Body.ZMm, c.HomeXMm, c.HomeZMm) > c.Role.TerritoryMm / 2)
                return c with { Body = Move(c, c.HomeXMm, c.HomeZMm, Gait.Run) };
            return c with { Body = Turn(c, facing) };
        }

        bool wary = c.Role.KeepDistanceMm > 0 && c.Health == c.Definition.MaxHealth && distance > c.Role.StrikeWithinMm;
        if (wary)
        {
            if (distance >= c.Role.KeepDistanceMm)
                return c with { Body = Turn(c, facing) };
            double dx = c.Body.XMm - player.XMm, dz = c.Body.ZMm - player.ZMm;
            double length = Math.Max(1, distance);
            var backed = Move(c, c.Body.XMm + (long)(dx / length * 3_000), c.Body.ZMm + (long)(dz / length * 3_000), Gait.Walk);
            return c with { Body = backed with { FacingMdeg = Turn(c, facing).FacingMdeg } };
        }

        bool clear = !Walled(c, c.Body.XMm, c.Body.ZMm, player.XMm, player.ZMm);
        bool seesTarget = tick == c.LastSeenTick;
        if (c.GuardUntil > tick)
            return c with { Body = Turn(c, facing), ComboStep = 0, ComboUntil = 0 };
        var incoming = State.PlayerCombat.Action;
        if (c.Definition.Tags.Contains("humanoid") && atCharacter && seesTarget && clear && distance < 4_000
            && incoming.Kind == ActionKind.Attack && incoming.PhaseAt(tick, C).Phase == CombatPhase.Windup
            && incoming.StartTick != c.ReactedToAttackTick)
        {
            int reaction = RngChannel.Open(State.World.WorldSeed,
                _context.CellOfSite(c.Site.Space, c.Site.XMm, c.Site.ZMm), "humanoid_defense",
                $"{c.Key}/{incoming.StartTick}").Int(0, 0, 3);
            c = c with { ReactedToAttackTick = incoming.StartTick };
            if (reaction == 0)
                return c with { Body = Turn(c, facing), ComboStep = 0, ComboUntil = 0, GuardUntil = incoming.StartTick
                    + incoming.Attack!.WindupTicks + incoming.Attack.ActiveTicks + 2 };
            if (reaction == 1 && _context.ControlOn(c.Id) != ControlKind.Roots)
            {
                var (dx, dz) = Direction(c.Body, player.XMm, player.ZMm);
                return c with { ComboStep = 0, ComboUntil = 0, Action = ActionState.Begin(ActionKind.Dodge, tick, dirX: -dx, dirZ: -dz) };
            }
        }

        // A charge opens from range, committed to its line: only at a target it can see, and not too often - and only at the character.
        if (_context.ControlOn(c.Id) != ControlKind.Roots && c.Definition.Charge is { } charge && atCharacter && seesTarget && clear && tick >= c.NextChargeTick
            && distance >= charge.ChargeMinRangeMm && distance <= charge.ReachMm)
        {
            charge = PhaseMove(c, Scaled(charge, c.ScalePercent));
            var (dx, dz) = Direction(c.Body, player.XMm, player.ZMm);
            _context.Events.Publish(new AttackStarted(c.Id, charge.Source, charge.WindupTicks, charge.ActiveTicks, charge.RecoveryTicks, tick));
            return c with
            {
                Body = Turn(c, facing),
                Action = ActionState.Begin(ActionKind.Charge, tick, charge, dx, dz),
                NextChargeTick = tick + charge.CooldownTicks,
                ComboStep = 0, ComboUntil = 0,
            };
        }

        // An attack that advances is begun closer: it runs on through the windup, and keeps half its leap for a target that flees.
        long reach = attack.ReachMm + (attack.Advances ? attack.LungeMm / 2 : attack.LungeMm) + _context.Setup.Movement.BodyRadiusMm;
        if (distance <= reach - 100 && clear)
        {
            // In reach: turn to face the target, and strike once facing it - a slow turner is slow to strike behind it.
            int toward = CombatRules.FacingTowards(c.Body.XMm, c.Body.ZMm, player.XMm, player.ZMm);
            var turned = Turn(c, toward);
            if (Math.Abs(Delta(turned.FacingMdeg, toward)) > 30_000)
                return c with { Body = turned };
            attack = PhaseMove(c, attack);
            _context.Events.Publish(new AttackStarted(c.Id, attack.Source, attack.WindupTicks, attack.ActiveTicks, attack.RecoveryTicks, tick));
            return c with { Body = turned, Action = ActionState.Begin(ActionKind.Attack, tick, attack),
                ComboStep = humanoidCombo && attack.Source == c.Definition.CloseAttack!.Source
                    ? (c.ComboUntil >= tick ? c.ComboStep : 0) + 1 : 0,
                ComboUntil = humanoidCombo ? tick + attack.TotalTicks + 12 : 0 };
        }

        // A pack hunter comes in from the side: aim off the straight line until close.
        long toX = targetX, toZ = targetZ;
        if (c.Role.FlankMm > 0 && toKnown > 3_000)
        {
            double dx = targetX - c.Body.XMm, dz = targetZ - c.Body.ZMm;
            int side = c.Key.EndsWith('1') || c.Key.EndsWith('3') ? 1 : -1;
            toX += (long)(-dz / toKnown * c.Role.FlankMm * side);
            toZ += (long)(dx / toKnown * c.Role.FlankMm * side);
        }
        return c with { Body = Move(c, toX, toZ, Gait.Run) };
    }

    /// <summary>
    /// A windup tracks the target as fast as the body turns - still running at it, for an attack that advances; a charge's
    /// windup also sets the line it will run.
    /// </summary>
    private CreatureState Aim(CreatureState c)
    {
        if (c.Action.Attack?.CommitFacing == true)
            return c;
        var player = c.Action.Kind == ActionKind.Charge ? State.Body : Foe(c).Body;
        var faced = c with
        {
            Body = c.Action.Attack is { Advances: true }
                ? Move(c, player.XMm, player.ZMm, Gait.Run)
                : Turn(c, CombatRules.FacingTowards(c.Body.XMm, c.Body.ZMm, player.XMm, player.ZMm)),
        };
        if (c.Action.Kind != ActionKind.Charge)
            return faced;
        var (dx, dz) = Direction(c.Body, player.XMm, player.ZMm);
        return faced with { Action = c.Action with { DirXPermille = dx, DirZPermille = dz } };
    }

    /// <summary>
    /// One tick of a charge: straight on at charge speed. It ends on the player - a blow that knocks them down unless
    /// guarded or dodged - after its distance, or against something solid, which stuns the charger.
    /// </summary>
    private CreatureState Run(CreatureState c, long tick)
    {
        var action = c.Action;
        var charge = action.Attack!;
        var player = State.Body;
        long elapsed = tick - action.StartTick - charge.WindupTicks;
        long step = charge.ChargeSpeedMmPerSecond * TickMs / 1000;
        long runTicks = Math.Max(1, charge.ReachMm / Math.Max(1, step));
        long playerRadius = _context.Setup.Movement.BodyRadiusMm;
        if (!action.Struck.Contains(_player) && Distance(c.Body.XMm, c.Body.ZMm, player.XMm, player.ZMm) <= c.Definition.RadiusMm + playerRadius + 300
            && _context.Dispatch(new CreatureStrike(c.Id, charge)) is null)
        {
            Sound(new Noise(c.Body.XMm, c.Body.ZMm, Setup.Noise.BlowMm));
            return c with { Action = action with { Struck = action.Struck.Add(_player), EndedTick = tick } };
        }
        if (elapsed > runTicks)
            return c with { Action = action with { EndedTick = tick } };

        var rules = new MovementRules(charge.ChargeSpeedMmPerSecond, 50, 100, c.Definition.RadiusMm, 0);
        var intent = new MoveIntent(action.DirXPermille, action.DirZPermille, Gait.Run, c.Body.FacingMdeg);
        var others = new List<Blocker>(Obstacles(c.Site.Space).Doors);
        others.AddRange(Herd(c));
        // People are solid to a charge as to anything else walking: a companion, or anyone standing in its line (L-24).
        others.AddRange(Obstacles(c.Site.Space).People);
        var moved = Kinematics.Step(c.Body, intent, rules, SpaceNear(c, c.Body), others, TickMs);
        if (Distance(c.Body.XMm, c.Body.ZMm, moved.XMm, moved.ZMm) < step / 2)
        {
            // It ran into something solid: rock, wall or tree takes the charge, and the charger reels.
            _context.Events.Publish(new CreatureStunned(c.Id, c.Key, tick));
            return c with { Body = moved, Action = ActionState.Begin(ActionKind.Staggered, tick) with { LastsTicks = charge.StunTicks } };
        }
        return c with { Body = moved };
    }

    private static (int X, int Z) Direction(Body from, long toXMm, long toZMm)
    {
        double dx = toXMm - from.XMm, dz = toZMm - from.ZMm;
        double length = Math.Max(1, Math.Sqrt(dx * dx + dz * dz));
        return ((int)Math.Round(dx / length * 1000), (int)Math.Round(dz / length * 1000));
    }

    private CreatureState Strike(CreatureState c, long tick)
    {
        var action = c.Action;
        var attack = action.Attack!;
        long elapsed = tick - action.StartTick;
        var foe = Foe(c);
        var player = foe.Body;
        // One swing lands on one body: the foe can change mid-swing (Tavar downed, the character nearest now), and the same blow must not
        // land again on the next (the Phase-1 technical audit, L-23).
        bool spent = !action.Struck.IsEmpty;
        // A lunge carries the body forward through the active window, stopping at whatever is in the way.
        if (attack.LungeMm > 0 && !spent && _context.ControlOn(c.Id) != ControlKind.Roots)
        {
            var rules = new MovementRules(attack.LungeMm * 1000 / Math.Max(1, attack.ActiveTicks * TickMs), 50, 100, c.Definition.RadiusMm, 0);
            var intent = new MoveIntent((int)Math.Round(Math.Sin(c.Body.FacingMdeg / 1000.0 * Math.PI / 180) * 1000),
                (int)Math.Round(Math.Cos(c.Body.FacingMdeg / 1000.0 * Math.PI / 180) * 1000), Gait.Run, c.Body.FacingMdeg);
            var obstacles = new List<Blocker>(Obstacles(c.Site.Space).Doors)
            {
                new CircleBlocker("player", State.Body.XMm, State.Body.ZMm, _context.Setup.Movement.BodyRadiusMm, 0),
            };
            obstacles.AddRange(People(c));
            c = c with { Body = Kinematics.Step(c.Body, intent, rules, SpaceNear(c, c.Body), obstacles, TickMs) };
        }
        bool reaches = !spent && !foe.Animal && (foe.Companion is not null || !State.PlayerCombat.Defeated)
            && CombatRules.InFront(c.Body.XMm, c.Body.ZMm, c.Body.FacingMdeg, player.XMm, player.ZMm,
                attack.ReachMm + _context.Setup.Movement.BodyRadiusMm, C.MeleeArcMdeg)
            && !Walled(c, c.Body.XMm, c.Body.ZMm, player.XMm, player.ZMm);
        // A swing at an animal lands in the animal's own fight, in the pet system, where the wound is the animal's to take: a creature whose
        // target is an animal must never wound the handler standing behind it. A companion and the character are struck through here.
        InternalCommand? blow = foe.Decoy is { } ghost ? new StrikeAfterimage(c.Id, c.Definition.Id, ghost.Of)
            : foe.Animal ? null
            : foe.Companion is { } companion ? new CompanionStruck(c.Id, companion, attack)
            : new CreatureStrike(c.Id, attack);
        if (blow is not null && reaches && _context.Dispatch(blow) is null)
        {
            Sound(new Noise(c.Body.XMm, c.Body.ZMm, Setup.Noise.BlowMm));
            return c with { Action = action with { Struck = action.Struck.Add(foe.Id) } };
        }
        if (action.Struck.IsEmpty && elapsed == attack.WindupTicks + attack.ActiveTicks)
            _context.Events.Publish(new AttackMissed(c.Id, attack.Source, tick));
        return c;
    }

    // ── wounds and death ────────────────────────────────────────────────────

    /// <summary>A blow lands. Whatever the creature was doing, it now knows where its attacker stands.</summary>
    public string? Handle(WoundCreature command, long tick)
    {
        if (Find(command.Target) is not { Alive: true } c)
            return "there is no such living creature";
        var hit = command.Hit;
        int health = Math.Max(0, c.Health - hit.Final);
        bool staggered = hit.Staggered && health > 0 && tick >= c.StaggerImmuneUntil;
        // A staggering blow starts a stagger, but never cuts one short: a charger stunned against a rock stays down its two seconds,
        // however hard it is hit meanwhile (the Phase-1 technical audit, M-08).
        var (phase, left) = c.Action.PhaseAt(tick, C);
        bool longer = phase == CombatPhase.Staggered && left >= C.StaggerTicks;
        // It knows where the blow came from: the character, or the companion who struck it (M6).
        var attacker = command.Attacker ?? _player;
        if (command.Attacker is null)
            (_focusKey, _focusTick) = (c.Key, tick);
        var (fromX, fromZ) = command.Attacker is null ? (State.Body.XMm, State.Body.ZMm) : (command.FromXMm, command.FromZMm);
        var wounded = c with
        {
            Health = health,
            Action = staggered && !longer ? ActionState.Begin(ActionKind.Staggered, tick) : c.Action,
            GuardUntil = staggered ? 0 : c.GuardUntil,
            ComboStep = staggered ? 0 : c.ComboStep,
            ComboUntil = staggered ? 0 : c.ComboUntil,
            StaggerImmuneUntil = staggered ? tick + C.StaggerImmunityTicks : c.StaggerImmuneUntil,
            Awareness = Perception.Full,
            Knows = true,
            KnownXMm = fromX,
            KnownZMm = fromZ,
            LastSeenTick = tick,
            // A blow from a pet is a grudge: what bit it is what it goes for (spec 43 ruling 5).
            ProvokedBy = command.Attacker is { Kind: EntityKind.Summon } bite ? bite : c.ProvokedBy,
            ProvokedUntil = command.Attacker is { Kind: EntityKind.Summon } ? tick + C.OutOfCombatTicks : c.ProvokedUntil,
        };
        // It felt the wound: it is on its attacker now, or running from it.
        var mind = Frightened(wounded) ? CreatureMind.Fleeing : CreatureMind.Engaged;
        if (mind != c.Mind)
            _context.Events.Publish(new CreatureNoticed(c.Id, c.Key, mind, tick));
        wounded = wounded with { Mind = mind };
        // A wound wakes a sleeper (effect.sleep's own rule): the body does not stay down through being stabbed.
        if (hit.Final > 0)
            _context.Dispatch(new RemoveEffect(c.Id, "effect.sleep"));
        Sound(new Noise(c.Body.XMm, c.Body.ZMm, Setup.Noise.BlowMm));
        _context.Events.Publish(new HitResolved(attacker, command.AttackerDefId ?? "player", c.Id, command.Source, hit.Region, hit.Final, hit.Critical,
            hit.Blocked, hit.Dodged, staggered, health, tick));
        Save(health == 0 ? Die(wounded, tick, attacker) : wounded);
        return null;
    }

    public string? Handle(HarmCreature command, long tick)
    {
        if (Find(command.Target) is not { Alive: true } c || command.Amount <= 0)
            return null;
        int health = Math.Max(0, c.Health - command.Amount);
        _context.Events.Publish(new HealthChanged(c.Id, command.Source, health - c.Health, health, tick));
        Save(health == 0 ? Die(c with { Health = 0 }, tick, _player) : c with { Health = health });
        return null;
    }

    public string? Handle(HealCreature command, long tick)
    {
        if (Find(command.Target) is not { Alive: true } c || command.Amount <= 0)
            return null;
        var healed = c with { Health = Math.Min(c.Definition.MaxHealth, c.Health + command.Amount) };
        if (healed.Health != c.Health)
            _context.Events.Publish(new HealthChanged(c.Id, command.Source, healed.Health - c.Health, healed.Health, tick));
        Save(healed);
        return null;
    }

    /// <summary>
    /// A death: the body stays as a corpse, a kill by the character earns combat XP (through AG-1..AG-3, the spawner as the cluster) and
    /// counts for their quests. Their pets/summons/party credit the owner (2026-10-24 ruling); a spawner schedules the return, twice as long
    /// while the cluster is saturated (AG-3).
    /// </summary>
    private CreatureState Die(CreatureState c, long tick, EntityId killer)
    {
        var directAttacker = killer;
        if (State.Pets.Values.Any(p => p.Id == killer)
            || State.Companions.Values.Any(p => State.Npcs.TryGetValue(p.NpcId, out var npc) && npc.InstanceId == killer))
            killer = _player;
        _context.Dispatch(new ClearEffects(c.Id));
        _context.Events.Publish(new CreatureKilled(c.Id, c.Definition.Id, killer, tick) { DirectAttacker = directAttacker });

        // D18 SYSTEMS (needed-2): a body worth skinning is left where it fell, by the system that owns the death, so a
        // test, a headless run and the game all see the same carcass. The kind says whether it leaves one at all.
        if (c.Definition.CarcassRef is { } carcass)
            _context.Dispatch(new LeaveCarcassCommand(CarcassKey(c), carcass, c.Body.XMm, c.Body.ZMm));
        if (killer == _player)
        {
            _context.Dispatch(new RecordDeed(new Deed(DeedKind.Killed, c.Definition.Id, 1, Domain.Crafting.Quality.Standard, null, tick)));
            _context.Dispatch(new CountUse());
            _context.Dispatch(new RecordAct(Domain.Factions.ActKinds.CreatureKilled, c.Definition.Id, _context.State.Body.XMm, _context.State.Body.ZMm));
        }
        var definition = c.Definition;
        if (definition.XpValue > 0 && killer == _player)
        {
            // The reward honours the scale the creature was placed at (spec 43 ruling 5): it was a party's fight, so it is a party's worth of
            // work and a party's worth of reward - its own value at the same percent, and the levels the scale bought, which is what the
            // level-band guard reads. A solo character's kill is the authored one to the coin.
            long worth = Math.Max(1, (long)Math.Round(definition.XpValue * c.ScalePercent / 100.0));
            _context.Dispatch(new AwardExperience(new XpAward(XpSource.Combat, worth, tick)
            {
                Kill = new KillContext(definition.Id, definition.Level + c.LevelBonus, c.Site.Key),
            }));
        }
        long respawn = 0;
        if (c.Site.RespawnTicks > 0)
        {
            bool saturated = Saturated(c.Site.Key, tick);
            respawn = _context.SpaceTick(c.Site.Space, tick) + c.Site.RespawnTicks * (saturated ? 2 : 1);
            if (saturated)
                _context.Events.Publish(new SpawnerSaturated(c.Site.Key, tick));
        }
        return c with
        {
            Condition = CreatureCondition.Corpse,
            Health = 0,
            Action = ActionState.Idle,
            Mind = CreatureMind.Unaware,
            Awareness = 0,
            Knows = false,
            DiedTick = _context.SpaceTick(c.Site.Space, tick),
            RespawnTick = respawn,
        };
    }

    /// <summary>AG-3: the player has killed at this cluster at least the threshold number of times inside the window.</summary>
    private bool Saturated(string cluster, long tick)
    {
        var guards = _context.Setup.Progression.Guards;
        var kills = State.Progression.Guards.ClusterKills.GetValueOrDefault(cluster, ImmutableArray<long>.Empty);
        return kills.Count(t => t > tick - guards.ClusterWindowTicks) >= guards.ClusterThreshold;
    }

    /// <summary>
    /// A corpse decays; a spawner with a timer brings its creature back once the player is not standing over the place - or, for an
    /// absence, <paramref name="away"/>, since the player was not there at all.
    /// </summary>
    private CreatureState Afterlife(CreatureState c, long tick, bool away = false, long? timer = null)
    {
        long elapsed = timer ?? tick;
        if (c.Condition == CreatureCondition.Corpse && elapsed >= c.DiedTick + Setup.CorpseDecayTicks)
        {
            _context.Dispatch(new DiscardContainer(c.CorpseKey));
            _context.Dispatch(new ClearCarcassCommand(CarcassKey(c)));
            _context.Events.Publish(new CorpseGone(c.CorpseKey, tick));
            c = c with { Condition = CreatureCondition.Gone };
        }
        var player = State.Body;
        if (c.RespawnTick > 0 && elapsed >= c.RespawnTick && (away || Distance(player.XMm, player.ZMm, c.HomeXMm, c.HomeZMm) > C.LeashMm))
        {
            if (c.Condition == CreatureCondition.Corpse)
            {
                _context.Dispatch(new DiscardContainer(c.CorpseKey));
                _context.Dispatch(new ClearCarcassCommand(CarcassKey(c)));
                _context.Events.Publish(new CorpseGone(c.CorpseKey, tick));
            }
            int generation = c.Generation + 1;
            var home = new Body(c.HomeXMm, _context.Setup.SpaceFor(c.Site.Space).HeightAtMm(c.HomeXMm, c.HomeZMm), c.HomeZMm, c.HomeFacingMdeg);
            var scale = ScaleOf();
            c = new CreatureState(Identity(c.Key, generation), c.Key, c.Site, c.Definition, c.Role, c.HomeXMm, c.HomeZMm, c.HomeFacingMdeg,
                generation, CreatureCondition.Alive, home, Math.Max(1, (int)Math.Round(c.Definition.MaxHealth * scale.HealthFactor)), ActionState.Idle, 0)
            {
                ScalePercent = scale.Percent,
                LevelBonus = scale.Levels,
            };
            _context.Events.Publish(new CreatureRespawned(c.Id, c.Key, generation, tick));
        }
        return c;
    }

    /// <summary>
    /// An adventurer's day took so many of a spawner's creatures (WP13; DN2: by the outcome table, never a fight). Only out of sight - a
    /// creature in a tier-A cell is never taken - and only from a spawner whose creatures come back: the culled are gone, no corpse, and
    /// their timer brings them back (§2.13 "kills reduce the spawn count, which respawn timers then restore"). Each is first brought to what
    /// had fallen due by the cull's tick, so the same culls on the same days leave the same creatures however an absence is split (T-22).
    /// Nobody's kill: no XP, no deed, no quest's defeat flag.
    /// </summary>
    public string? Handle(CullCreatures command)
    {
        long tick = command.Tick;
        int left = command.Count;
        foreach (string key in State.Creatures.Keys.Where(k => k.StartsWith(command.SpawnKey + "#", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList())
        {
            if (left <= 0)
                break;
            var c = State.Creatures[key];
            if (c.Site.RespawnTicks <= 0 || TierOf(c.HomeXMm, c.HomeZMm) == SimulationTier.A || TierOf(c.Body.XMm, c.Body.ZMm) == SimulationTier.A)
                continue;
            c = Afterlife(c, tick, away: true);
            if (!c.Alive)
            {
                Save(c);
                continue;
            }
            _context.Dispatch(new ClearEffects(c.Id));
            Save(AtHome(c) with { Condition = CreatureCondition.Gone, Health = 0, DiedTick = tick, RespawnTick = tick + c.Site.RespawnTicks });
            _context.Events.Publish(new CreatureCulled(c.Id, c.Key, tick));
            left--;
        }
        return null;
    }

    private SimulationTier TierOf(long xMm, long zMm) =>
        State.Tiers.GetValueOrDefault(CellKey.OfWorld(xMm / 1000.0, zMm / 1000.0).ToString(), SimulationTier.D);

    public string? Handle(ForgetPlayer command)
    {
        foreach (var c in State.Creatures.Values.Where(c => c.Alive && c.Mind != CreatureMind.Unaware).ToList())
            Save(c with { Mind = CreatureMind.Returning, Awareness = 0, Knows = false, Action = ActionState.Idle, HasCalled = false });
        return null;
    }

    public string? Handle(CorpseEmptied command, long tick)
    {
        if (State.Creatures.Values.FirstOrDefault(c => c.Condition == CreatureCondition.Corpse && c.CorpseKey == command.CorpseKey) is not { } c)
            return $"no corpse '{command.CorpseKey}'";
        Save(c with { Condition = CreatureCondition.Gone });
        _context.Dispatch(new ClearCarcassCommand(CarcassKey(c)));
        _context.Events.Publish(new CorpseGone(command.CorpseKey, tick));
        return null;
    }

    /// <summary>
    /// A pet drew this creature off (spec 43's control skill): it goes to look where the decoy went and loses the character for a while.
    /// Nothing is wounded by it - a lure moves information, not bodies - and it has now seen the trick once, so it is not drawn again until
    /// it is reset (owned ruling 5's "enemies that ... ignore lures they've seen").
    /// </summary>
    public string? Handle(DrawCreature command, long tick)
    {
        if (Find(command.Target) is not { Alive: true } c)
            return "there is no such living creature";
        Save(c with
        {
            Mind = c.Mind == CreatureMind.Engaged ? CreatureMind.Searching : c.Mind,
            KnownXMm = command.XMm,
            KnownZMm = command.ZMm,
            Knows = false,
            LastSeenTick = tick,
            SearchUntil = tick + command.Ticks,
            DrawnUntil = tick + command.Ticks,
            LureLearned = true,
            Action = ActionState.Idle,
        });
        return null;
    }

    /// <summary>The animal has left the wild as a pet (spec 43's taming): its slot stays empty, and its record goes with it.</summary>
    public string? Handle(TakeCreature command, long tick)
    {
        if (Find(command.Target) is not { } c)
            return "there is no such creature";
        State.RemoveCreatureRecord(_owner, c.Key);
        State.RemoveCreature(_owner, c.Key);
        return null;
    }

    /// <summary>Something was reached for and refused: it has seen the character, and it will not stand still for the next hand.</summary>
    public string? Handle(RouseCreature command, long tick)
    {
        if (Find(command.Target) is not { Alive: true } c)
            return "there is no such living creature";
        Save(c with
        {
            Awareness = Math.Max(c.Awareness, Setup.Awareness.Suspicious),
            Mind = c.Mind == CreatureMind.Unaware ? CreatureMind.Suspicious : c.Mind,
            Knows = true,
            KnownXMm = State.Body.XMm,
            KnownZMm = State.Body.ZMm,
            LastSeenTick = tick,
        });
        return null;
    }

    // ── state ───────────────────────────────────────────────────────────────

    /// <summary>Keep the working state, and write its persistent part through to the world delta whenever that changes.</summary>
    private void Save(CreatureState c)
    {
        State.SetCreature(_owner, c);
        // What its next ticks depend on is written only while it still matters (the Phase-1 technical audit, L-06): a load resumes no
        // earlier than the tick after the last one run, so a cooldown, an immunity or a stagger over by then is written as none - and a
        // creature over them has the record of one that never had them.
        long next = State.WorldTick + 1;
        bool alive = c.Condition == CreatureCondition.Alive;
        long charge = alive && c.NextChargeTick > next ? c.NextChargeTick : 0;
        long immune = alive && c.StaggerImmuneUntil > next ? c.StaggerImmuneUntil : 0;
        bool staggered = alive && c.Action.Kind == ActionKind.Staggered && c.Action.PhaseAt(next, C).Phase == CombatPhase.Staggered;
        bool baseline = Scale is null && c.Condition == CreatureCondition.Alive && c.Generation == 0 && c.Health == c.ScaledMaxHealth
                        && c.Body.XMm == c.HomeXMm && c.Body.ZMm == c.HomeZMm && c.Body.FacingMdeg == c.HomeFacingMdeg
                        && c.Mind == CreatureMind.Unaware && c.Awareness == 0 && !c.Knows && !c.HasCalled && charge == 0 && immune == 0 && !staggered;
        var existing = State.World.Creature(c.Key);
        if (baseline)
        {
            if (existing is not null)
                State.RemoveCreatureRecord(_owner, c.Key);
            return;
        }
        var record = new CreatureRecord(c.Key, c.Definition.Id, c.Id, _context.CellOfSite(c.Site.Space, c.Site.XMm, c.Site.ZMm).ToString(), c.Generation,
            c.Condition, c.Body.XMm, c.Body.ZMm, c.Body.FacingMdeg, c.Health, c.DiedTick, c.RespawnTick)
        {
            Mind = c.Mind,
            ScalePercent = c.ScalePercent,
            LevelBonus = c.LevelBonus,
            Awareness = c.Awareness,
            Knows = c.Knows,
            KnownXMm = c.Knows ? c.KnownXMm : 0,
            KnownZMm = c.Knows ? c.KnownZMm : 0,
            LastSeenTick = c.LastSeenTick,
            SearchUntil = c.SearchUntil,
            HasCalled = c.HasCalled,
            NextChargeTick = charge,
            StaggerImmuneUntil = immune,
            StaggeredTick = staggered ? c.Action.StartTick : null,
            StaggerLastsTicks = staggered ? c.Action.LastsTicks : 0,
        };
        if (existing is null || existing with { BaselineHash = null } != record)
            State.SetCreatureRecord(_owner, record);
    }

    private CreatureState? Find(EntityId id) => State.Creatures.Values.FirstOrDefault(c => c.Id == id);

    private Body Move(CreatureState c, long toXMm, long toZMm, Gait gait, long? speedOverride = null)
    {
        var from = c.Body;
        double dx = toXMm - from.XMm, dz = toZMm - from.ZMm;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length < 1)
            return from;
        var intent = new MoveIntent((int)Math.Round(dx / length * 1000), (int)Math.Round(dz / length * 1000), gait,
            Turn(c, CombatRules.FacingTowards(from.XMm, from.ZMm, toXMm, toZMm)).FacingMdeg);
        var rules = new MovementRules(speedOverride ?? c.Definition.MoveSpeedMmPerSecond, 50, 100, c.Definition.RadiusMm, 0);
        var others = new List<Blocker>(Obstacles(c.Site.Space).Doors)
        {
            new CircleBlocker("player", State.Body.XMm, State.Body.ZMm, _context.Setup.Movement.BodyRadiusMm, 0),
        };
        others.AddRange(People(c));
        others.AddRange(Herd(c));
        return Kinematics.Step(from, intent, rules, SpaceNear(c, from), others, TickMs);
    }

    /// <summary>The other living creatures near enough to be in this one's way this tick, as bodies it does not walk through.</summary>
    private IEnumerable<Blocker> Herd(CreatureState c) =>
        State.Creatures.Values.Where(o => o.Alive && o.Site.Space == c.Site.Space && o.Key != c.Key
                && Math.Abs(o.Body.XMm - c.Body.XMm) < NearMm && Math.Abs(o.Body.ZMm - c.Body.ZMm) < NearMm)
            .Select(o => (Blocker)new CircleBlocker(o.Key, o.Body.XMm, o.Body.ZMm, o.Definition.RadiusMm, 0));

    /// <summary>Further than this from a body, nothing touches it in one tick: the fastest run (a charge) covers half a metre.</summary>
    private const long NearMm = 8_000;

    private const long GridMm = 16_000;

    private Dictionary<(SpaceId, long, long), WalkSpace>? _near;

    /// <summary>
    /// The walk space round a body, holding only the region's structures within a grid square of it (16 m): what a body that moves at
    /// most half a metre a tick can touch. Gathered once per square - the structures never move - so a step weighs a few dozen
    /// structures rather than the whole region's (the world-life pass more than tripled the creatures that walk).
    /// </summary>
    private WalkSpace SpaceNear(CreatureState creature, Body body)
    {
        var space = _context.Setup.SpaceFor(creature.Site.Space);
        var square = (creature.Site.Space, Math.DivRem(body.XMm, GridMm).Quotient, Math.DivRem(body.ZMm, GridMm).Quotient);
        _near ??= new Dictionary<(SpaceId, long, long), WalkSpace>();
        if (_near.TryGetValue(square, out var near))
            return near;
        long x0 = (square.Item2 - 1) * GridMm, z0 = (square.Item3 - 1) * GridMm, x1 = (square.Item2 + 2) * GridMm, z1 = (square.Item3 + 2) * GridMm;
        var blockers = space.Blockers.Where(b => b switch
        {
            BoxBlocker box => box.MaxXMm >= x0 && box.MinXMm <= x1 && box.MaxZMm >= z0 && box.MinZMm <= z1,
            CircleBlocker round => round.CenterXMm + round.RadiusMm >= x0 && round.CenterXMm - round.RadiusMm <= x1
                && round.CenterZMm + round.RadiusMm >= z0 && round.CenterZMm - round.RadiusMm <= z1,
            _ => true,
        }).ToImmutableArray();
        return _near[square] = space with { Blockers = blockers };
    }

    /// <summary>
    /// Whom an engaged creature goes for (M6, spec 43 ruling 5): the character - or whoever of the party is on their feet and standing nearer,
    /// a companion or an animal. Then the role reads the fight:
    /// <list type="bullet">
    /// <item>Hunting the handler: an animal savaging it is the leash, not the target - it goes past the animal for the character behind it.</item>
    /// <item>The grudge: an ordinary predator a hound has bitten fights the hound, not the archer behind it.</item>
    /// <item>Focusing the weakest: the hurt of the party, wherever it stands in the group, is finished first.</item>
    /// </list>
    /// </summary>
    /// <summary>
    /// What the creature fights: <see cref="Quarry"/>, or that one's lingering afterimage when the creature takes the bait - one roll per
    /// creature and afterimage, the same on a replay.
    /// </summary>
    private (Body Body, EntityId Id, string? Companion, bool Animal, Afterimage? Decoy) Foe(CreatureState c)
    {
        var (body, id, companion, animal) = Quarry(c);
        foreach (var ghost in Afterimages?.Invoke() ?? Enumerable.Empty<Afterimage>())
            if (ghost.Of == id && RngChannel.Open(State.World.WorldSeed, _context.CellOf(ghost.At), "afterimage", $"{c.Key}>{ghost.Of}@{ghost.LeftTick}")
                    .UInt64(0) % 100 < (ulong)ghost.ChancePercent)
                return (ghost.At, id, companion, animal, ghost);
        return (body, id, companion, animal, null);
    }

    private (Body Body, EntityId Id, string? Companion, bool Animal) Quarry(CreatureState c)
    {
        var player = State.Body;
        double toPlayer = Distance(c.Body.XMm, c.Body.ZMm, player.XMm, player.ZMm);
        double toFoe = toPlayer;
        (Body, EntityId, string?, bool) foe = (player, _player, null, false);
        int playerMax = (int)_context.PlayerStats().HealthMax;
        int foeShare = Share(State.Progression.Pools.Health ?? playerMax, playerMax);
        var bitten = State.WorldTick < c.ProvokedUntil ? c.ProvokedBy : null;
        // The handler is the one the animal answers to: an animal that savages a role which hunts handlers is a reason to go for the character,
        // not a target of its own - so the animals below are read for the grudge only when the role does not hunt handlers.
        if (c.Role.HuntsHandler && bitten is not null && PetsOnFeet?.Invoke().Any(p => p.Id == bitten) == true)
            return (player, _player, null, false);
        foreach (var npc in Obstacles(c.Site.Space).Companions)
        {
            var companion = State.Companions[npc.Definition.Id];
            if (companion.Condition != CompanionCondition.Up) continue;
            double toCompanion = Distance(c.Body.XMm, c.Body.ZMm, npc.Body.XMm, npc.Body.ZMm);
            int share = Share(companion.Health, companion.Profile.MaxHealth);
            bool takes = c.Role.FocusWeakest && share < foeShare
                || toCompanion + FoeMarginMm < toPlayer && toCompanion + FoeMarginMm < toFoe;
            if (!takes) continue;
            foe = (npc.Body, npc.InstanceId, npc.Definition.Id, false);
            toFoe = toCompanion;
            foeShare = share;
        }
        foreach (var (body, id, health, maxHealth) in PetsOnFeet?.Invoke() ?? ImmutableArray<(Body Body, EntityId Id, int Health, int MaxHealth)>.Empty)
        {
            bool grudge = bitten == id;
            double toPet = Distance(c.Body.XMm, c.Body.ZMm, body.XMm, body.ZMm);
            int share = Share(health, maxHealth);
            bool takes = grudge || (c.Role.FocusWeakest ? share < foeShare : toPet + FoeMarginMm < toFoe);
            if (!takes) continue;
            foe = (body, id, null, true);
            toFoe = toPet;
            foeShare = share;
        }
        return foe;
    }

    /// <summary>A party member's health as thousandths of its own: what "the weakest" is read from.</summary>
    private static int Share(int health, int maxHealth) => health * 1000 / Math.Max(1, maxHealth);

    /// <summary>A companion stands nearer than the character by more than this before a creature turns on them.</summary>
    private const long FoeMarginMm = 1_000;

    /// <summary>Everyone standing near in the creature's space, as bodies creatures walk round: a creature never shoves a person out of its way (companions included).</summary>
    private IEnumerable<Blocker> People(CreatureState c) =>
        Obstacles(c.Site.Space).People.Where(b => b is CircleBlocker p && Math.Abs(p.CenterXMm - c.Body.XMm) < NearMm && Math.Abs(p.CenterZMm - c.Body.ZMm) < NearMm);

    private static bool Arrived(CreatureState c, long xMm, long zMm) => Distance(c.Body.XMm, c.Body.ZMm, xMm, zMm) <= 700;

    /// <summary>Whether a place lies in the ground it will go after something in: its territory if it keeps one, else its leash.</summary>
    private bool Keeps(CreatureState c, long xMm, long zMm) =>
        Distance(xMm, zMm, c.HomeXMm, c.HomeZMm) <= (c.Role.TerritoryMm > 0 ? c.Role.TerritoryMm : C.LeashMm);

    /// <summary>The body turned towards a facing by at most its turn rate for one tick.</summary>
    private Body Turn(CreatureState c, int towardMdeg)
    {
        long step = c.Definition.TurnMdegPerSecond * TickMs / 1000;
        int delta = Delta(c.Body.FacingMdeg, towardMdeg);
        if (Math.Abs(delta) <= step)
            return c.Body with { FacingMdeg = towardMdeg };
        long turned = c.Body.FacingMdeg + Math.Sign(delta) * step;
        return c.Body with { FacingMdeg = (int)(((turned % 360_000) + 360_000) % 360_000) };
    }

    /// <summary>The signed shortest turn from one facing to another, in millidegrees: (-180000, 180000].</summary>
    private static int Delta(int fromMdeg, int toMdeg)
    {
        int delta = ((toMdeg - fromMdeg) % 360_000 + 360_000) % 360_000;
        return delta > 180_000 ? delta - 360_000 : delta;
    }

    private IEnumerable<Blocker> Walls(CreatureState c) => Obstacles(c.Site.Space).Walls;

    private bool Walled(CreatureState c, double x0, double z0, double x1, double z1) =>
        Walls(c).Any(b => b.StandsBetween && b.Crosses(x0, z0, x1, z1));

    private SimulationTier TierOf(CreatureState c) =>
        State.Tiers.GetValueOrDefault(_context.CellOfSite(c.Site.Space, c.Body.XMm, c.Body.ZMm).ToString(), SimulationTier.D);

    private static double Distance(long x0, long z0, long x1, long z1)
    {
        double dx = x1 - x0, dz = z1 - z0;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    public ImmutableArray<CreatureView> Views()
    {
        // The source dictionary and its records are immutable. A command replacing a creature
        // invalidates this even between ticks; phase/sleep and space changes have their own keys.
        var source = State.Creatures;
        long tick = State.WorldTick;
        var space = State.Space;
        if (ReferenceEquals(source, _viewSource) && tick == _viewTick && space == _viewSpace)
            return _views;
        var views = source.Values.Where(c => c.Site.Space == space).Select(c =>
        {
            var (phase, left) = c.Action.PhaseAt(tick, C);
            return new CreatureView(c.Id, c.Key, c.Definition.Id, c.Role.Id, c.Body, c.Health, c.ScaledMaxHealth, phase, left, c.Condition,
                c.Mind, c.Awareness, Asleep(c, tick))
            {
                Generation = c.Generation, Peaceful = c.Role.Peaceful,
                Rooted = c.Alive && _context.ControlOn(c.Id) == ControlKind.Roots,
                Paralysed = c.Alive && State.Effects.GetValueOrDefault(c.Id, ImmutableArray<ActiveEffect>.Empty).Any(e => e.EffectId == "effect.paralyse"),
                Poisoned = c.Alive && State.Effects.GetValueOrDefault(c.Id, ImmutableArray<ActiveEffect>.Empty).Any(e => e.EffectId == "effect.venom"),
                Level = c.Definition.Level + c.LevelBonus,
                OnAnAnimal = c.ProvokedBy is { } provoked && tick < c.ProvokedUntil && PetsOnFeet?.Invoke().Any(p => p.Id == provoked) == true,
                Huntable = c.Definition.LootTableId is not null,
                Temperament = TemperamentRules.Of(c.Role.Peaceful, c.Role.AnswersCalls, c.Mind == CreatureMind.Engaged),
                Threat = ThreatRules.Of(c.Definition.Level + c.LevelBonus, State.Progression.Level),
                AttackSource = c.Action.Attack?.Source,
                AttackVariant = c.Definition.CloseAttack is not null && c.Action.Attack is not null
                    ? RngChannel.Open(State.World.WorldSeed, _context.CellOfSite(c.Site.Space, c.Site.XMm, c.Site.ZMm), "creature_motion",
                        $"{c.Key}@{c.Action.StartTick}/{c.Action.Attack.Source}").Int(0, 0, int.MaxValue) : 0,
                AttackTick = c.Action.StartTick,
                Guarding = c.GuardUntil > tick,
                Enraged = Enraged(c),
                PhaseDurationTicks = c.Action.Attack is { } attack ? phase switch
                {
                    CombatPhase.Windup => attack.WindupTicks,
                    CombatPhase.Active => c.Action.Kind == ActionKind.Charge
                        ? Math.Max(1, (int)(attack.ReachMm * 1000 / Math.Max(1, attack.ChargeSpeedMmPerSecond * TickMs))) : attack.ActiveTicks,
                    CombatPhase.Recovery => attack.RecoveryTicks,
                    _ => 0,
                } : 0,
            };
        }).ToImmutableArray();
        _viewSource = source;
        _viewTick = tick;
        _viewSpace = space;
        return _views = views;
    }
}
