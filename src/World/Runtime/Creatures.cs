// UNNAMED World - creatures at run time: spawners, perception, behaviour roles, corpses and respawn
// (SYSTEMS.md S-23, S-31; STEALTH_DETECTION_AND_THREAT.md §1-§7, §16; PROTOTYPE.md §4.1, §7.4; M3d)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Creatures;
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
    /// <summary>Ticks from a death to the return, or 0 when the dead stay dead.</summary>
    public long RespawnTicks { get; init; }

    public ImmutableArray<(long XMm, long ZMm)> Route { get; init; } = ImmutableArray<(long XMm, long ZMm)>.Empty;
}

public sealed record CreatureView(EntityId Id, string Key, string DefId, string RoleId, Body Body, int Health, int MaxHealth,
    CombatPhase Phase, int PhaseTicksLeft, CreatureCondition Condition, CreatureMind Mind, int Awareness, bool Asleep)
{
    public bool Alive => Condition == CreatureCondition.Alive;

    public bool Hostile => Mind == CreatureMind.Engaged;

    /// <summary>What a <see cref="MoveItemCommand"/> names the body by while it lies there.</summary>
    public string CorpseKey => CreatureSystem.CorpseKeyOf(Key, Generation);

    public int Generation { get; init; }
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
    public long DiedTick { get; init; }
    public long RespawnTick { get; init; }

    public bool Alive => Condition == CreatureCondition.Alive;
    public bool Asleep => Role.Unaware == UnawareBehaviour.Sleep && Mind == CreatureMind.Unaware;
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
/// Owns: <see cref="StateSlice.Creatures"/> and the creature records of the world delta (S-23 and S-31). It places each
/// spawner's creatures, lets them perceive - sight in a cone past no wall, and sound, each carrying only so far - and act
/// on what they perceive, infer or are told through a call, in the manner of their role. There is no shared awareness:
/// a packmate out of earshot of a howl knows nothing (STEALTH §1, §5, §6). Creatures act only in tier-A cells. A dead
/// creature leaves a corpse its loot table fills; a spawner with a timer brings it back. Everything about a creature's
/// future is in its record - body, health, life, and mind - and its idle wandering and patrolling are functions of the
/// world tick, so a loaded world goes on exactly as the saved one would have. Only a blow in progress is not kept.
/// </summary>
internal sealed class CreatureSystem
{
    private const double SampleScale = 18446744073709551616.0;

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;
    private readonly Func<MoveIntent> _intent;
    private readonly List<Noise> _pending = new();

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
        var space = _context.Setup.Layout.Space;
        foreach (var site in Setup.Spawns)
        {
            var cell = CellKey.OfWorld(site.XMm / 1000.0, site.ZMm / 1000.0);
            for (int i = 0; i < site.Members.Length; i++)
            {
                var member = site.Members[i];
                var definition = Setup.Creatures[member.CreatureId];
                var role = Setup.Roles.GetValueOrDefault(member.RoleId) ?? new CreatureRole(member.RoleId, UnawareBehaviour.Hold);
                string key = $"{site.Key}#{i}";
                var channel = RngChannel.Open(State.World.WorldSeed, cell, "spawn", key);
                var others = State.Creatures.Values.Select(c => (Blocker)new CircleBlocker(c.Key, c.HomeXMm, c.HomeZMm, c.Definition.RadiusMm, 0)).ToList();
                (long x, long z) = (site.XMm, site.ZMm);
                for (uint sample = 0; sample < 32; sample += 2)
                {
                    double angle = channel.UInt64(sample) / SampleScale * 2 * Math.PI;
                    double distance = Math.Sqrt(channel.UInt64(sample + 1) / SampleScale) * site.RadiusMm;
                    long cx = site.XMm + (long)Math.Round(Math.Sin(angle) * distance), cz = site.ZMm + (long)Math.Round(Math.Cos(angle) * distance);
                    if (Kinematics.IsClear(cx, cz, definition.RadiusMm, space, others))
                    {
                        (x, z) = (cx, cz);
                        break;
                    }
                }
                int facing = (int)(channel.UInt64(40) % 360_000);
                var body = new Body(x, space.Terrain.HeightAtMm(x, z), z, facing);
                var state = new CreatureState(Identity(key, 0), key, site, definition, role, x, z, facing, 0, CreatureCondition.Alive, body,
                    definition.MaxHealth, ActionState.Idle, 0);
                if (State.World.Creature(key) is { } record)
                {
                    state = state with
                    {
                        Id = record.InstanceId,
                        Generation = record.Generation,
                        Condition = record.Condition,
                        Body = new Body(record.XMm, space.Terrain.HeightAtMm(record.XMm, record.ZMm), record.ZMm, record.FacingMdeg),
                        Health = record.Health,
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
                    };
                }
                State.SetCreature(_owner, state);
            }
        }
    }

    // ── the tick ────────────────────────────────────────────────────────────

    public void Tick(long tick)
    {
        var noises = PlayerNoises().Concat(_pending).ToList();
        _pending.Clear();
        foreach (string key in State.Creatures.Keys.ToList())
        {
            var creature = State.Creatures[key];
            Save(creature.Alive ? Live(creature, tick, noises) : Afterlife(creature, tick));
        }
    }

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
        if (radius > 0)
            yield return new Noise(body.XMm, body.ZMm, radius);
    }

    private CreatureState Live(CreatureState c, long tick, List<Noise> noises)
    {
        if (TierOf(c.Body) != SimulationTier.A)
            return c;
        var (phase, _) = c.Action.PhaseAt(tick, C);
        if (c.Action.Kind != ActionKind.Idle && phase == CombatPhase.Idle)
            c = c with { Action = ActionState.Idle };
        c = Perceive(c, tick, noises);
        var next = phase switch
        {
            CombatPhase.Staggered or CombatPhase.Recovery => c,
            CombatPhase.Windup => Aim(c),
            CombatPhase.Active when c.Action.Kind == ActionKind.Charge => Run(c, tick),
            CombatPhase.Active => Strike(c, tick),
            _ => Act(c, tick),
        };
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
        var senses = c.Definition.Senses;
        var player = State.Body;
        bool target = !State.PlayerCombat.Defeated;
        double? seen = !c.Asleep && target ? Perception.Sees(c.Body, senses, player.XMm, player.ZMm, Walls()) : null;
        int awareness = Perception.Accrue(c.Awareness, seen, senses.SightMm, rules, TickMs);
        var next = c with { Awareness = awareness };
        if (seen is not null)
            next = next with { Knows = true, KnownXMm = player.XMm, KnownZMm = player.ZMm, LastSeenTick = tick };

        long hearing = c.Asleep ? senses.HearingMm * c.Role.SleepHearingPercent / 100 : senses.HearingMm;
        bool pounce = false;
        foreach (var noise in noises)
        {
            if (!Perception.Hears(c.Body, hearing, noise) || (noise.XMm == c.Body.XMm && noise.ZMm == c.Body.ZMm))
                continue;
            // A call is its own kind's: a howl brings wolves, not whatever else is in earshot.
            if (noise.Call && (!c.Role.AnswersCalls || noise.CallerKind != c.Definition.Id))
                continue;
            int level = noise.Call ? rules.HeardCall : rules.HeardNoise;
            if (next.Mind == CreatureMind.Engaged && next.Knows && seen is not null)
                continue;   // already watching the target; a sound adds nothing
            next = next with { Awareness = Math.Max(next.Awareness, level), Knows = true, KnownXMm = noise.XMm, KnownZMm = noise.ZMm };
            // An ambusher feels a footfall in its territory and goes for it at once.
            pounce |= !noise.Call && c.Role.PounceOnNoise
                      && (c.Role.TerritoryMm <= 0 || Distance(noise.XMm, noise.ZMm, c.HomeXMm, c.HomeZMm) <= c.Role.TerritoryMm);
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
        if (State.PlayerCombat.Defeated)
            mind = mind is CreatureMind.Unaware ? mind : CreatureMind.Returning;
        else if (mind == CreatureMind.Fleeing)
        {
            if (seen is null && Distance(c.Body.XMm, c.Body.ZMm, player.XMm, player.ZMm) > C.LeashMm / 2)
                mind = CreatureMind.Returning;
        }
        else if (seen is not null && c.Awareness >= Perception.Full && noticing)
            mind = CreatureMind.Engaged;
        else if (mind == CreatureMind.Engaged && seen is null && tick - c.LastSeenTick > 20)
            mind = CreatureMind.Searching;
        else if (mind is CreatureMind.Unaware or CreatureMind.Returning && c.Awareness >= rules.Suspicious && noticing)
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
            _pending.Add(new Noise(c.Body.XMm, c.Body.ZMm, Setup.Noise.CallMm, Call: true, CallerKind: c.Definition.Id));
            _context.Events.Publish(new CreatureCalled(c.Id, c.Key, c.Body.XMm, c.Body.ZMm, tick));
        }
        if (mind is CreatureMind.Unaware or CreatureMind.Returning)
            next = next with { HasCalled = false };
        if (mind != was)
            _context.Events.Publish(new CreatureNoticed(c.Id, c.Key, mind, tick));
        return next;
    }

    private bool Frightened(CreatureState c) =>
        c.Role.FleeBelowPercent > 0 && c.Health * 100 < c.Definition.MaxHealth * c.Role.FleeBelowPercent;

    // ── acting ──────────────────────────────────────────────────────────────

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

    /// <summary>Unaware: the role decides - hold the place, drift around it, walk the route, or sleep.</summary>
    private CreatureState Idle(CreatureState c, long tick)
    {
        switch (c.Role.Unaware)
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
        var channel = RngChannel.Open(State.World.WorldSeed, CellKey.OfWorld(aroundX / 1000.0, aroundZ / 1000.0), "wander", $"{c.Key}@{leg}");
        double angle = channel.UInt64(0) / SampleScale * 2 * Math.PI, distance = Math.Sqrt(channel.UInt64(1) / SampleScale) * radius;
        long x = aroundX + (long)Math.Round(Math.Sin(angle) * distance), z = aroundZ + (long)Math.Round(Math.Cos(angle) * distance);
        return Arrived(c, x, z) ? c : c with { Body = Move(c, x, z, Gait.Walk) };
    }

    /// <summary>A stable per-creature phase, so a spawner's creatures do not all set off on the same tick.</summary>
    private static long Offset(string key, long period)
    {
        using var h = new CanonicalHasher();
        return (long)(BitConverter.ToUInt64(h.Add("unnamed.creature-phase/v1").Add(key).FinishBytes(), 0) % (ulong)period);
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
        var attack = c.Definition.Attack;
        // The character is gone after where they were last perceived; a companion who stands nearer is simply there (M6).
        long targetX = foe.Companion is null && c.Knows ? c.KnownXMm : player.XMm, targetZ = foe.Companion is null && c.Knows ? c.KnownZMm : player.ZMm;
        double distance = Distance(c.Body.XMm, c.Body.ZMm, player.XMm, player.ZMm);
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

        bool clear = !Walled(c.Body.XMm, c.Body.ZMm, player.XMm, player.ZMm);
        bool seesTarget = tick == c.LastSeenTick;

        // A charge opens from range, committed to its line: only at a target it can see, and not too often - and only at the character.
        if (c.Definition.Charge is { } charge && foe.Companion is null && seesTarget && clear && tick >= c.NextChargeTick
            && distance >= charge.ChargeMinRangeMm && distance <= charge.ReachMm)
        {
            var (dx, dz) = Direction(c.Body, player.XMm, player.ZMm);
            _context.Events.Publish(new AttackStarted(c.Id, charge.Source, charge.WindupTicks, charge.ActiveTicks, charge.RecoveryTicks, tick));
            return c with
            {
                Body = Turn(c, facing),
                Action = ActionState.Begin(ActionKind.Charge, tick, charge, dx, dz),
                NextChargeTick = tick + charge.CooldownTicks,
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
            _context.Events.Publish(new AttackStarted(c.Id, attack.Source, attack.WindupTicks, attack.ActiveTicks, attack.RecoveryTicks, tick));
            return c with { Body = turned, Action = ActionState.Begin(ActionKind.Attack, tick, attack) };
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
            _pending.Add(new Noise(c.Body.XMm, c.Body.ZMm, Setup.Noise.BlowMm));
            return c with { Action = action with { Struck = action.Struck.Add(_player), EndedTick = tick } };
        }
        if (elapsed > runTicks)
            return c with { Action = action with { EndedTick = tick } };

        var rules = new MovementRules(charge.ChargeSpeedMmPerSecond, 50, 100, c.Definition.RadiusMm, 0);
        var intent = new MoveIntent(action.DirXPermille, action.DirZPermille, Gait.Run, c.Body.FacingMdeg);
        var others = new List<Blocker>(_context.ClosedDoors());
        others.AddRange(State.Creatures.Values.Where(o => o.Alive && o.Key != c.Key)
            .Select(o => (Blocker)new CircleBlocker(o.Key, o.Body.XMm, o.Body.ZMm, o.Definition.RadiusMm, 0)));
        // People are solid to a charge as to anything else walking: a companion, or anyone standing in its line (L-24).
        others.AddRange(State.Npcs.Values.Select(n => (Blocker)new CircleBlocker(n.Definition.Id, n.Body.XMm, n.Body.ZMm, _context.Setup.Movement.BodyRadiusMm, 0)));
        var moved = Kinematics.Step(c.Body, intent, rules, _context.Setup.Layout.Space, others, TickMs);
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
        if (attack.LungeMm > 0 && !spent)
        {
            var rules = new MovementRules(attack.LungeMm * 1000 / Math.Max(1, attack.ActiveTicks * TickMs), 50, 100, c.Definition.RadiusMm, 0);
            var intent = new MoveIntent((int)Math.Round(Math.Sin(c.Body.FacingMdeg / 1000.0 * Math.PI / 180) * 1000),
                (int)Math.Round(Math.Cos(c.Body.FacingMdeg / 1000.0 * Math.PI / 180) * 1000), Gait.Run, c.Body.FacingMdeg);
            var obstacles = new List<Blocker>(_context.ClosedDoors())
            {
                new CircleBlocker("player", State.Body.XMm, State.Body.ZMm, _context.Setup.Movement.BodyRadiusMm, 0),
            };
            obstacles.AddRange(Companions());
            c = c with { Body = Kinematics.Step(c.Body, intent, rules, _context.Setup.Layout.Space, obstacles, TickMs) };
        }
        bool reaches = !spent && (foe.Companion is not null || !State.PlayerCombat.Defeated)
            && CombatRules.InFront(c.Body.XMm, c.Body.ZMm, c.Body.FacingMdeg, player.XMm, player.ZMm,
                attack.ReachMm + _context.Setup.Movement.BodyRadiusMm, C.MeleeArcMdeg)
            && !Walled(c.Body.XMm, c.Body.ZMm, player.XMm, player.ZMm);
        InternalCommand blow = foe.Companion is { } companion ? new CompanionStruck(c.Id, companion, attack) : new CreatureStrike(c.Id, attack);
        if (reaches && _context.Dispatch(blow) is null)
        {
            _pending.Add(new Noise(c.Body.XMm, c.Body.ZMm, Setup.Noise.BlowMm));
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
        var (fromX, fromZ) = command.Attacker is null ? (State.Body.XMm, State.Body.ZMm) : (command.FromXMm, command.FromZMm);
        var wounded = c with
        {
            Health = health,
            Action = staggered && !longer ? ActionState.Begin(ActionKind.Staggered, tick) : c.Action,
            StaggerImmuneUntil = staggered ? tick + C.StaggerImmunityTicks : c.StaggerImmuneUntil,
            Awareness = Perception.Full,
            Knows = true,
            KnownXMm = fromX,
            KnownZMm = fromZ,
            LastSeenTick = tick,
        };
        // It felt the wound: it is on its attacker now, or running from it.
        var mind = Frightened(wounded) ? CreatureMind.Fleeing : CreatureMind.Engaged;
        if (mind != c.Mind)
            _context.Events.Publish(new CreatureNoticed(c.Id, c.Key, mind, tick));
        wounded = wounded with { Mind = mind };
        _pending.Add(new Noise(c.Body.XMm, c.Body.ZMm, Setup.Noise.BlowMm));
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
        _context.Events.Publish(new HealthChanged(c.Id, command.Source, -command.Amount, health, tick));
        Save(health == 0 ? Die(c with { Health = 0 }, tick, _player) : c with { Health = health });
        return null;
    }

    public string? Handle(HealCreature command, long tick)
    {
        if (Find(command.Target) is not { Alive: true } c || command.Amount <= 0)
            return null;
        var healed = c with { Health = Math.Min(c.Definition.MaxHealth, c.Health + command.Amount) };
        _context.Events.Publish(new HealthChanged(c.Id, command.Source, command.Amount, healed.Health, tick));
        Save(healed);
        return null;
    }

    /// <summary>
    /// A death: the body stays as a corpse, a kill by the character earns combat XP (through AG-1..AG-3, the spawner as the cluster) and
    /// counts for their quests - a companion's kill does neither (M6) - and a spawner with a timer schedules the return, twice as long
    /// while the cluster is saturated (AG-3).
    /// </summary>
    private CreatureState Die(CreatureState c, long tick, EntityId killer)
    {
        _context.Dispatch(new ClearEffects(c.Id));
        _context.Events.Publish(new CreatureKilled(c.Id, c.Definition.Id, killer, tick));
        if (killer == _player)
            _context.Dispatch(new RecordDeed(new Deed(DeedKind.Killed, c.Definition.Id, 1, Domain.Crafting.Quality.Standard, null, tick)));
        var definition = c.Definition;
        if (definition.XpValue > 0 && killer == _player)
        {
            _context.Dispatch(new AwardExperience(new XpAward(XpSource.Combat, definition.XpValue, tick)
            {
                Kill = new KillContext(definition.Id, definition.Level, c.Site.Key),
            }));
        }
        long respawn = 0;
        if (c.Site.RespawnTicks > 0)
        {
            bool saturated = Saturated(c.Site.Key, tick);
            respawn = tick + c.Site.RespawnTicks * (saturated ? 2 : 1);
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
            DiedTick = tick,
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

    /// <summary>A corpse decays; a spawner with a timer brings its creature back once the player is not standing over the place.</summary>
    private CreatureState Afterlife(CreatureState c, long tick)
    {
        if (c.Condition == CreatureCondition.Corpse && tick >= c.DiedTick + Setup.CorpseDecayTicks)
        {
            _context.Dispatch(new DiscardContainer(c.CorpseKey));
            _context.Events.Publish(new CorpseGone(c.CorpseKey, tick));
            c = c with { Condition = CreatureCondition.Gone };
        }
        var player = State.Body;
        if (c.RespawnTick > 0 && tick >= c.RespawnTick && Distance(player.XMm, player.ZMm, c.HomeXMm, c.HomeZMm) > C.LeashMm)
        {
            if (c.Condition == CreatureCondition.Corpse)
            {
                _context.Dispatch(new DiscardContainer(c.CorpseKey));
                _context.Events.Publish(new CorpseGone(c.CorpseKey, tick));
            }
            int generation = c.Generation + 1;
            var home = new Body(c.HomeXMm, _context.Setup.Layout.Space.Terrain.HeightAtMm(c.HomeXMm, c.HomeZMm), c.HomeZMm, c.HomeFacingMdeg);
            c = new CreatureState(Identity(c.Key, generation), c.Key, c.Site, c.Definition, c.Role, c.HomeXMm, c.HomeZMm, c.HomeFacingMdeg,
                generation, CreatureCondition.Alive, home, c.Definition.MaxHealth, ActionState.Idle, 0);
            _context.Events.Publish(new CreatureRespawned(c.Id, c.Key, generation, tick));
        }
        return c;
    }

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
        _context.Events.Publish(new CorpseGone(command.CorpseKey, tick));
        return null;
    }

    // ── state ───────────────────────────────────────────────────────────────

    /// <summary>Keep the working state, and write its persistent part through to the world delta whenever that changes.</summary>
    private void Save(CreatureState c)
    {
        State.SetCreature(_owner, c);
        bool baseline = c.Condition == CreatureCondition.Alive && c.Generation == 0 && c.Health == c.Definition.MaxHealth
                        && c.Body.XMm == c.HomeXMm && c.Body.ZMm == c.HomeZMm && c.Body.FacingMdeg == c.HomeFacingMdeg
                        && c.Mind == CreatureMind.Unaware && c.Awareness == 0 && !c.Knows && !c.HasCalled;
        var existing = State.World.Creature(c.Key);
        if (baseline)
        {
            if (existing is not null)
                State.RemoveCreatureRecord(_owner, c.Key);
            return;
        }
        var record = new CreatureRecord(c.Key, c.Definition.Id, c.Id, CellKey.OfWorld(c.Site.XMm / 1000.0, c.Site.ZMm / 1000.0).ToString(), c.Generation,
            c.Condition, c.Body.XMm, c.Body.ZMm, c.Body.FacingMdeg, c.Health, c.DiedTick, c.RespawnTick)
        {
            Mind = c.Mind,
            Awareness = c.Awareness,
            Knows = c.Knows,
            KnownXMm = c.Knows ? c.KnownXMm : 0,
            KnownZMm = c.Knows ? c.KnownZMm : 0,
            LastSeenTick = c.LastSeenTick,
            SearchUntil = c.SearchUntil,
            HasCalled = c.HasCalled,
        };
        if (existing is null || existing with { BaselineHash = null } != record)
            State.SetCreatureRecord(_owner, record);
    }

    private CreatureState? Find(EntityId id) => State.Creatures.Values.FirstOrDefault(c => c.Id == id);

    private Body Move(CreatureState c, long toXMm, long toZMm, Gait gait)
    {
        var from = c.Body;
        double dx = toXMm - from.XMm, dz = toZMm - from.ZMm;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length < 1)
            return from;
        var intent = new MoveIntent((int)Math.Round(dx / length * 1000), (int)Math.Round(dz / length * 1000), gait,
            Turn(c, CombatRules.FacingTowards(from.XMm, from.ZMm, toXMm, toZMm)).FacingMdeg);
        var rules = new MovementRules(c.Definition.MoveSpeedMmPerSecond, 50, 100, c.Definition.RadiusMm, 0);
        var others = new List<Blocker>(_context.ClosedDoors())
        {
            new CircleBlocker("player", State.Body.XMm, State.Body.ZMm, _context.Setup.Movement.BodyRadiusMm, 0),
        };
        others.AddRange(Companions());
        others.AddRange(State.Creatures.Values.Where(o => o.Alive && o.Key != c.Key)
            .Select(o => (Blocker)new CircleBlocker(o.Key, o.Body.XMm, o.Body.ZMm, o.Definition.RadiusMm, 0)));
        return Kinematics.Step(from, intent, rules, _context.Setup.Layout.Space, others, TickMs);
    }

    /// <summary>
    /// Whom an engaged creature goes for (M6): the character - or a companion on their feet standing nearer to it by more than a step,
    /// in reach of it, as the one in its way. It is the character it perceives and hunts; a companion is fought because they are there.
    /// </summary>
    private (Body Body, EntityId Id, string? Companion) Foe(CreatureState c)
    {
        var player = State.Body;
        double toPlayer = Distance(c.Body.XMm, c.Body.ZMm, player.XMm, player.ZMm);
        (Body, EntityId, string?) foe = (player, _player, null);
        foreach (var companion in State.Companions.Values.Where(x => x.Condition == CompanionCondition.Up))
        {
            if (!State.Npcs.TryGetValue(companion.NpcId, out var npc))
                continue;
            double toCompanion = Distance(c.Body.XMm, c.Body.ZMm, npc.Body.XMm, npc.Body.ZMm);
            if (toCompanion + FoeMarginMm < toPlayer && toCompanion + FoeMarginMm < Distance(c.Body.XMm, c.Body.ZMm, foe.Item1.XMm, foe.Item1.ZMm))
                foe = (npc.Body, npc.InstanceId, companion.NpcId);
        }
        return foe;
    }

    /// <summary>A companion stands nearer than the character by more than this before a creature turns on them.</summary>
    private const long FoeMarginMm = 1_000;

    /// <summary>Companions on their feet, as bodies creatures do not walk through.</summary>
    private IEnumerable<Blocker> Companions() =>
        State.Companions.Values.Where(x => x.Condition == CompanionCondition.Up && State.Npcs.ContainsKey(x.NpcId))
            .Select(x => State.Npcs[x.NpcId])
            .Select(n => (Blocker)new CircleBlocker(n.Definition.Id, n.Body.XMm, n.Body.ZMm, _context.Setup.Movement.BodyRadiusMm, 0));

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

    private IEnumerable<Blocker> Walls() => _context.Setup.Layout.Space.Blockers.Concat(_context.ClosedDoors());

    private bool Walled(double x0, double z0, double x1, double z1) => Walls().Any(b => b.Crosses(x0, z0, x1, z1));

    private SimulationTier TierOf(Body body) =>
        State.Tiers.GetValueOrDefault(CellKey.OfWorld(body.XMm / 1000.0, body.ZMm / 1000.0).ToString(), SimulationTier.D);

    private static double Distance(long x0, long z0, long x1, long z1)
    {
        double dx = x1 - x0, dz = z1 - z0;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    public ImmutableArray<CreatureView> Views() =>
        State.Creatures.Values.Select(c =>
        {
            var (phase, left) = c.Action.PhaseAt(State.WorldTick, C);
            return new CreatureView(c.Id, c.Key, c.Definition.Id, c.Role.Id, c.Body, c.Health, c.Definition.MaxHealth, phase, left, c.Condition,
                c.Mind, c.Awareness, c.Asleep) { Generation = c.Generation };
        }).ToImmutableArray();
}
