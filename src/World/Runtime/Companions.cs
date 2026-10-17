// UNNAMED World - companions at run time: recruited, following, waiting, catching up, fighting, downed and back
// (SYSTEMS.md S-25; PROTOTYPE.md C16, §6.2; content bible §17; M6)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>Tell a companion to follow or to wait - the companion key, or a word in conversation.</summary>
public sealed record OrderCompanionCommand(EntityId Actor, string NpcId, CompanionOrder Order) : GameCommand(Actor);

/// <summary>Help a downed companion up, from within a hand's reach of them.</summary>
public sealed record ReviveCommand(EntityId Actor, string NpcId) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

public sealed record CompanionRecruited(string NpcId, long Tick);

public sealed record CompanionOrdered(string NpcId, CompanionOrder Order, long Tick);

/// <summary>A companion too far behind, or making no headway, was put down near the character (C16: no snag lasts).</summary>
public sealed record CompanionCaughtUp(string NpcId, string Reason, Body From, Body To, long Tick);

public sealed record CompanionDowned(string NpcId, string ByDefId, long Tick);

public sealed record CompanionRevived(string NpcId, int Health, long Tick);

/// <summary>
/// Downed too long with no hand: the party member fell. A named companion or an ally is gravely wounded and goes home to recover
/// (<see cref="CompanionGravelyWounded"/>); a hireling dies (<see cref="HirelingDied"/>). <see cref="At"/> is where they are now.
/// </summary>
public sealed record CompanionFell(string NpcId, Body At, long Tick);

// ── views ───────────────────────────────────────────────────────────────────

/// <summary>
/// A companion as the HUD shows them (content bible §19: the name, how they are, follow or wait): <see cref="Doing"/> is
/// <c>following</c>, <c>waiting</c>, <c>fighting</c> or <c>downed</c>.
/// </summary>
public sealed record CompanionView(string NpcId, EntityId InstanceId, string Name, CompanionOrder Order, CompanionCondition Condition, int Health,
    int MaxHealth, Body Body, string Doing)
{
    /// <summary>While downed: the tick they fall if no one helps them up.</summary>
    public long? FallsAtTick { get; init; }

    /// <summary>How they are in words (content bible §19): healthy, wounded, critical or downed.</summary>
    public string Standing => CompanionRules.Condition(Condition, Health, MaxHealth);

    /// <summary>A companion, a hireling or a temporary ally (WP15a).</summary>
    public PartyMembership Membership { get; init; } = PartyMembership.Companion;

    public CompanionStance Stance { get; init; } = CompanionStance.Aggressive;

    public PartyTactics Tactics { get; init; } = PartyTactics.Default;

    /// <summary>A hireling's contract: role, days left, wage, purse, work (WP15a).</summary>
    public HirelingContract? Contract { get; init; }

    /// <summary>The weapon they fight with: their own, or the melee weapon they were handed (WP15b).</summary>
    public string WeaponId { get; init; } = "";

    /// <summary>Their armour on each region: their own plus what they wear (WP15b).</summary>
    public ImmutableSortedDictionary<BodyRegion, int> Armor { get; init; } = ImmutableSortedDictionary<BodyRegion, int>.Empty;
}

// ── state and internal commands ─────────────────────────────────────────────

/// <summary>
/// A companion as the simulation holds them. Their body is an NPC body (<see cref="NpcSystem"/>). Everything but a blow in progress
/// is saved (<see cref="CompanionRecord"/>); the swing, the pause before the next, and what it is aimed at are not, as with creatures.
/// </summary>
internal sealed record CompanionState(string NpcId, CompanionProfile Profile, AttackProfile Attack, CompanionOrder Order, CompanionCondition Condition,
    int Health)
{
    public long DownedTick { get; init; }
    public int StuckTicks { get; init; }
    public long LastCombatTick { get; init; }
    public ImmutableArray<TrailMark> Trail { get; init; } = ImmutableArray<TrailMark>.Empty;
    public ActionState Action { get; init; } = ActionState.Idle;
    public long NextAttackTick { get; init; }
    public string? TargetKey { get; init; }
    public PartyMembership Membership { get; init; } = PartyMembership.Companion;
    public CompanionStance Stance { get; init; } = CompanionStance.Aggressive;
    public PartyTactics Tactics { get; init; } = PartyTactics.Default;
    public HirelingContract? Contract { get; init; }
}

/// <summary>To <see cref="CompanionSystem"/>: the speaker joins the character (a conversation's <c>recruit_companion</c>).</summary>
internal sealed record Recruit(string NpcId) : InternalCommand
{
    public PartyMembership As { get; init; } = PartyMembership.Companion;
}

/// <summary>To <see cref="CompanionSystem"/>: the speaker takes an order (a conversation's <c>order_companion</c>).</summary>
internal sealed record OrderCompanion(string NpcId, CompanionOrder Order) : InternalCommand;

/// <summary>To <see cref="CompanionSystem"/>: a creature's blow reached a companion (the creature system checked reach, arc and walls).</summary>
internal sealed record CompanionStruck(EntityId Attacker, string NpcId, AttackProfile Attack) : InternalCommand;

/// <summary>To <see cref="NpcSystem"/>: an NPC's body moves - a companion walking, turning, or put down somewhere.</summary>
internal sealed record PlaceNpc(string NpcId, Body Body) : InternalCommand;

// ── the system ──────────────────────────────────────────────────────────────

/// <summary>
/// Owns: <see cref="StateSlice.Companions"/> - the party (S-25; WP15a): companions, hirelings and allies, their order, stance and
/// standing rules, whether they are up, their health, the trail they walk and a hireling's contract - and the party's ledger of who is
/// recovering from a grave wound and who has fallen. Their bodies are NPC bodies, moved through <see cref="NpcSystem"/>. The rules are
/// plain (ROADMAP.md M6: "boringly reliable, not clever"): following, walk the character's trail and stand close; too far behind, or
/// making no headway, catch up at once; waiting, stand. Fight what is engaged with the character - and, waiting or defensive, only what
/// comes close; passive, nothing. Downed, wait for a hand; with none in time, fall (DN9): a named companion is gravely wounded and
/// recovers at home, a hireling dies. A companion's kills are theirs: they earn the character no XP and count for no quest. How many
/// may travel with the character is <c>config.party</c>'s; nothing else here assumes it.
/// </summary>
internal sealed partial class CompanionSystem : IAbstractAdvance
{
    private const double SampleScale = 18446744073709551616.0;

    /// <summary>A trail mark this close is reached.</summary>
    private const long MarkReachedMm = 800;

    /// <summary>Catching up, a companion is put on the trail at least this far from the character and at most the next.</summary>
    private const long CatchUpNearMm = 2_000, CatchUpFarMm = 6_000;

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;
    private readonly ImmutableArray<CompanionRecord> _saved;
    private readonly IPartyLife _life;

    public CompanionSystem(SystemContext context, SliceOwner owner, EntityId player, ImmutableArray<CompanionRecord> saved, IPartyLife life)
    {
        _context = context;
        _owner = owner;
        _player = player;
        _saved = saved;
        _life = life;
    }

    private RuntimeState State => _context.State;
    private CompanionTuning? Tuning => _context.Setup.Social.Companions;
    private CombatConstants C => _context.Setup.Combat.Constants;
    private int TickMs => _context.Setup.TickMilliseconds;

    /// <summary>
    /// World start: each saved companion stands where they stood. One whose NPC is no longer in the region, or can no longer join
    /// (or, a hireling, be hired), is left out - the load reports definitions that vanished.
    /// </summary>
    public void Populate()
    {
        foreach (var record in _saved)
        {
            if (!State.Npcs.TryGetValue(record.NpcId, out var npc) || BodyOf(npc.Definition, record.Membership) is not { } body || Tuning is null
                || Fitted(record.NpcId, body) is not { } profile || AttackOf(profile) is not { } attack)
                continue;
            var terrain = _context.Setup.Layout.Space.Terrain;
            _context.Dispatch(new PlaceNpc(record.NpcId, new Body(record.XMm, terrain.HeightAtMm(record.XMm, record.ZMm), record.ZMm, record.FacingMdeg)));
            State.SetCompanion(_owner, new CompanionState(record.NpcId, profile, attack, record.Order, record.Condition, Math.Min(record.Health, profile.MaxHealth))
            {
                DownedTick = record.DownedTick,
                StuckTicks = record.StuckTicks,
                LastCombatTick = record.LastCombatTick,
                Trail = record.Trail,
                Membership = record.Membership,
                Stance = record.Stance,
                Tactics = record.Tactics,
                Contract = record.Contract,
            });
        }
    }

    /// <summary>The body someone travels with: a companion's or an ally's from their companion profile, a hireling's from their hire profile.</summary>
    private static CompanionProfile? BodyOf(NpcDefinition definition, PartyMembership membership) =>
        membership == PartyMembership.Hireling ? definition.Hireling?.Body : definition.Companion;

    /// <summary>The companions as a save records them.</summary>
    public ImmutableArray<CompanionRecord> Records() =>
        State.Companions.Values.Where(c => State.Npcs.ContainsKey(c.NpcId)).Select(c =>
        {
            var body = State.Npcs[c.NpcId].Body;
            return new CompanionRecord(c.NpcId, c.Order, c.Condition, body.XMm, body.ZMm, body.FacingMdeg, c.Health)
            {
                DownedTick = c.DownedTick,
                StuckTicks = c.StuckTicks,
                LastCombatTick = c.LastCombatTick,
                Trail = c.Trail,
                Membership = c.Membership,
                Stance = c.Stance,
                Tactics = c.Tactics,
                Contract = c.Contract,
            };
        }).ToImmutableArray();

    /// <summary>Who is recovering and who has fallen, as a save records them.</summary>
    public PartyLedger Ledger() =>
        new(State.Recovering.Select(r => new RecoveryRecord(r.Key, r.Value)).ToImmutableArray(), State.Fallen.ToImmutableArray());

    // ── recruiting and orders ───────────────────────────────────────────────

    public string? Handle(Recruit command, long tick)
    {
        if (State.Companions.ContainsKey(command.NpcId))
            return null;   // already with the character: nothing to do, and no failure of the reply
        if (!State.Npcs.TryGetValue(command.NpcId, out var npc))
            return $"there is no one called {command.NpcId} here";
        if (command.As == PartyMembership.Hireling || npc.Definition.Companion is not { } body || Tuning is null || AttackOf(body) is null)
            return $"{npc.Definition.Name} cannot join you";
        if (Joinable(npc.Definition) is { } refused)
            return refused;
        if (Willing(command.NpcId, "join", tick) is { } unwilling)
            return unwilling;
        var profile = Fitted(command.NpcId, body)!;
        State.SetCompanion(_owner, new CompanionState(command.NpcId, profile, AttackOf(profile) ?? AttackOf(body)!, CompanionOrder.Follow, CompanionCondition.Up,
            profile.MaxHealth)
        {
            Membership = command.As,
        });
        _life.Release(command.NpcId, tick);
        _context.Events.Publish(new CompanionRecruited(command.NpcId, tick));
        return null;
    }

    public string? Handle(OrderCompanionCommand command, long tick) =>
        command.Actor != _player ? $"unknown actor {command.Actor}" : Order(command.NpcId, command.Order, tick);

    public string? Handle(OrderCompanion command, long tick) => Order(command.NpcId, command.Order, tick);

    private string? Order(string npcId, CompanionOrder order, long tick)
    {
        if (!State.Companions.TryGetValue(npcId, out var c))
            return $"{Name(npcId)} is not with you";
        if (c.Condition == CompanionCondition.Downed)
            return $"{Name(npcId)} is down";
        if (c.Order == order)
            return null;
        if (Willing(npcId, CompanionKeys.Key(order), tick) is { } unwilling)
            return unwilling;
        // Told to follow, they set off from where they stand: the trail walked while they waited is not theirs to retrace. A hireling told
        // to follow leaves the work they were set to.
        State.SetCompanion(_owner, c with
        {
            Order = order,
            StuckTicks = 0,
            Trail = ImmutableArray<TrailMark>.Empty,
            TargetKey = null,
            Contract = order == CompanionOrder.Follow && c.Contract is { } k ? k with { WorkSite = null, RecipeId = null } : c.Contract,
        });
        _context.Events.Publish(new CompanionOrdered(npcId, order, tick));
        return null;
    }

    public string? Handle(ReviveCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        if (!State.Companions.TryGetValue(command.NpcId, out var c))
            return $"{Name(command.NpcId)} is not with you";
        if (c.Condition != CompanionCondition.Downed)
            return $"{Name(command.NpcId)} is not down";
        if (!_context.InTalkReach(State.Npcs[c.NpcId].Body))
            return $"{Name(command.NpcId)} is out of reach";
        int health = Math.Max(1, c.Profile.MaxHealth * Tuning!.RevivePercent / 100);
        State.SetCompanion(_owner, Up(c, health, tick));
        _context.Events.Publish(new CompanionRevived(c.NpcId, health, tick));
        return null;
    }

    /// <summary>
    /// The party went by the stones (S-38): every companion following and on their feet is put down on a ring round where the character
    /// arrived, behind first, facing them, with no trail to walk back along. Those told to wait stay where they wait.
    /// </summary>
    public string? Handle(ArriveWithParty command)
    {
        var terrain = _context.Setup.Layout.Space.Terrain;
        int place = 0;
        foreach (var c in State.Companions.Values.Where(c => c.Order == CompanionOrder.Follow && c.Condition == CompanionCondition.Up).ToList())
        {
            var npc = State.Npcs[c.NpcId];
            var arrival = command.Arrival;
            var at = Ring(arrival, arrival.FacingMdeg + 180_000 + place++ * 45_000, 2_000, Obstacles(c.NpcId)) ?? (arrival.XMm, arrival.ZMm);
            Place(npc, new Body(at.X, terrain.HeightAtMm(at.X, at.Z), at.Z, CombatRules.FacingTowards(at.X, at.Z, arrival.XMm, arrival.ZMm)));
            State.SetCompanion(_owner, c with { StuckTicks = 0, Trail = ImmutableArray<TrailMark>.Empty, TargetKey = null, Action = ActionState.Idle });
        }
        return null;
    }

    // ── blows on a companion ────────────────────────────────────────────────

    /// <summary>A creature's blow on a companion: resolved like one on the character - their armor, no guard, no dodge - and applied here.</summary>
    public string? Handle(CompanionStruck command, long tick)
    {
        if (!State.Companions.TryGetValue(command.NpcId, out var c) || c.Condition != CompanionCondition.Up)
            return "no such companion on their feet";
        if (State.Creatures.Values.FirstOrDefault(x => x.Id == command.Attacker) is not { Alive: true } creature)
            return "no such attacker";
        var npc = State.Npcs[c.NpcId];
        var defense = new DefenseProfile(c.Profile.MaxHealth, c.Profile.Armor, ImmutableSortedDictionary.Create<string, double>(StringComparer.Ordinal),
            Blocking: false, Dodging: false);
        var strike = new Strike(command.Attack, EffectRules.DamageDealtMultiplier(EffectsOf(creature.Id), _context.Setup.Combat.Effects), C.BaseCritPercent, 1.0);
        var result = CombatRules.Resolve(strike, defense, C, Random(creature.Key, c.NpcId, npc.Body, tick));
        int health = Math.Max(0, c.Health - result.Final);
        _context.Events.Publish(new HitResolved(creature.Id, creature.Definition.Id, npc.InstanceId, command.Attack.Source, result.Region, result.Final,
            result.Critical, result.Blocked, result.Dodged, false, health, tick));
        if (health > 0)
        {
            State.SetCompanion(_owner, c with { Health = health, LastCombatTick = tick });
            return null;
        }
        State.SetCompanion(_owner, c with
        {
            Condition = CompanionCondition.Downed,
            Health = 0,
            DownedTick = tick,
            LastCombatTick = tick,
            Action = ActionState.Idle,
            TargetKey = null,
            StuckTicks = 0,
            Trail = ImmutableArray<TrailMark>.Empty,
        });
        _context.Events.Publish(new CompanionDowned(c.NpcId, creature.Definition.Id, tick));
        return null;
    }

    // ── the tick ────────────────────────────────────────────────────────────

    public void Tick(long tick)
    {
        foreach (string id in State.Companions.Keys.ToList())
        {
            if (State.Npcs.TryGetValue(id, out var npc) && Live(State.Companions[id], npc, tick) is { } lived)
                State.SetCompanion(_owner, lived);
        }
        // The party's day (WP15a): at midnight hirelings deliver their work and draw their wages; the wounded mend at home.
        if (_context.Setup.Clock.DaysCrossed(tick - 1, tick) > 0)
            Midnight(tick, null);
        Recover(tick, null);
    }

    /// <summary>One tick of a party member's life; null when they fell and are no longer in the party.</summary>
    private CompanionState? Live(CompanionState c, NpcState npc, long tick)
    {
        var tuning = Tuning!;
        if (c.Condition == CompanionCondition.Downed)
        {
            if (tick - c.DownedTick < tuning.ReviveWindowTicks)
                return c;
            Fall(c, npc, tick);
            return null;
        }
        if (TierOf(npc.Body) != SimulationTier.A)
        {
            // Left outside the simulated ground - the character went far at once, by a fall, a jump or a return - a follower would stand
            // frozen there for good (NA:275): they catch up instead, as when too far behind.
            return c.Order == CompanionOrder.Follow && !State.PlayerCombat.Defeated ? CatchUp(c, npc, "distance", tick) : c;
        }
        c = Mend(c, tick);
        if (c.Order == CompanionOrder.Follow)
            c = Mark(c);

        var (phase, _) = c.Action.PhaseAt(tick, C);
        if (c.Action.Kind != ActionKind.Idle && phase == CombatPhase.Idle)
            c = c with { Action = ActionState.Idle, NextAttackTick = tick + tuning.AttackPauseTicks };
        switch (phase)
        {
            case CombatPhase.Windup:
                // The swing tracks its mark as fast as a body turns.
                if (c.TargetKey is { } key && State.Creatures.TryGetValue(key, out var aimed) && aimed.Alive)
                    Place(npc, Turn(npc.Body, CombatRules.FacingTowards(npc.Body.XMm, npc.Body.ZMm, aimed.Body.XMm, aimed.Body.ZMm)));
                return c;
            case CombatPhase.Active:
                return Swing(c, npc, tick);
            case CombatPhase.Recovery:
                return c;
        }

        // In conversation: stand, facing whoever is speaking.
        if (State.Conversation?.NpcId == c.NpcId)
        {
            Place(npc, Turn(npc.Body, CombatRules.FacingTowards(npc.Body.XMm, npc.Body.ZMm, State.Body.XMm, State.Body.ZMm)));
            return c with { StuckTicks = 0 };
        }
        if (Target(c, npc) is { } target)
            return Fight(c, npc, target, tick);
        c = c with { TargetKey = null };
        return c.Order == CompanionOrder.Follow ? Follow(c, npc, tick) : c with { StuckTicks = 0 };
    }

    /// <summary>Out of a fight long enough, a companion mends a little every second.</summary>
    private CompanionState Mend(CompanionState c, long tick)
    {
        var tuning = Tuning!;
        int perSecond = Math.Max(1, 1000 / TickMs);
        if (c.Health >= c.Profile.MaxHealth || tuning.RegenPerSecond == 0 || tick - c.LastCombatTick < tuning.RegenDelayTicks || tick % perSecond != 0)
            return c;
        return c with { Health = Math.Min(c.Profile.MaxHealth, c.Health + tuning.RegenPerSecond) };
    }

    /// <summary>A mark on the character's trail every step they go, while a companion follows.</summary>
    private CompanionState Mark(CompanionState c)
    {
        var tuning = Tuning!;
        var player = State.Body;
        if (State.PlayerCombat.Defeated || (!c.Trail.IsEmpty && Distance(c.Trail[^1].XMm, c.Trail[^1].ZMm, player.XMm, player.ZMm) < tuning.TrailStepMm))
            return c;
        var trail = c.Trail.Add(new TrailMark(player.XMm, player.ZMm));
        return c with { Trail = trail.Length > tuning.TrailLength ? trail.RemoveRange(0, trail.Length - tuning.TrailLength) : trail };
    }

    // ── following ───────────────────────────────────────────────────────────

    /// <summary>
    /// Keep close to the character: walk, run or sprint by how far behind, along the trail they walked - past the marks already
    /// reached, straight for the farthest mark in clear view, or for the character when they are in clear view. Too far behind, or
    /// making no headway for long enough, catch up at once.
    /// </summary>
    private CompanionState Follow(CompanionState c, NpcState npc, long tick)
    {
        var tuning = Tuning!;
        var body = npc.Body;
        var player = State.Body;
        double distance = Distance(body.XMm, body.ZMm, player.XMm, player.ZMm);
        if (CompanionRules.CatchUp(distance, c.StuckTicks, tuning) is { } reason)
            return CatchUp(c, npc, reason, tick);
        if (CompanionRules.FollowGait(distance, tuning) is not { } gait)
        {
            Place(npc, Turn(body, CombatRules.FacingTowards(body.XMm, body.ZMm, player.XMm, player.ZMm)));
            return c with { StuckTicks = 0 };
        }

        var trail = c.Trail;
        while (!trail.IsEmpty && Distance(body.XMm, body.ZMm, trail[0].XMm, trail[0].ZMm) <= MarkReachedMm)
            trail = trail.RemoveAt(0);
        (long X, long Z) goal = (player.XMm, player.ZMm);
        if (!InClearView(body, player.XMm, player.ZMm) && !trail.IsEmpty)
        {
            int seen = -1;
            for (int i = trail.Length - 1; i >= 0 && seen < 0; i--)
            {
                if (InClearView(body, trail[i].XMm, trail[i].ZMm))
                    seen = i;
            }
            // With no mark in clear view, the oldest is the way back onto the trail.
            int next = Math.Max(0, seen);
            goal = (trail[next].XMm, trail[next].ZMm);
            trail = trail.RemoveRange(0, next);
        }
        var moved = Step(npc, goal.X, goal.Z, gait);
        double expected = _context.Setup.Movement.SpeedMmPerSecond(gait) * TickMs / 1000.0;
        bool headway = Distance(body.XMm, body.ZMm, moved.XMm, moved.ZMm) >= expected * 0.3;
        Place(npc, moved);
        return c with { Trail = trail, StuckTicks = headway ? 0 : c.StuckTicks + 1 };
    }

    /// <summary>
    /// Put the companion down near the character: on the trail a little way behind them, in sight of them, or else on a ring round
    /// them - behind first - wherever a body fits with nothing between. Nowhere fits: they try again next time.
    /// </summary>
    private CompanionState CatchUp(CompanionState c, NpcState npc, string reason, long tick)
    {
        var player = State.Body;
        var obstacles = Obstacles(c.NpcId);
        long radius = _context.Setup.Movement.BodyRadiusMm;
        var space = _context.Setup.Layout.Space;
        (long X, long Z)? spot = null;
        int from = c.Trail.Length;
        for (int i = c.Trail.Length - 1; i >= 0 && spot is null; i--)
        {
            var mark = c.Trail[i];
            double d = Distance(mark.XMm, mark.ZMm, player.XMm, player.ZMm);
            if (d >= CatchUpNearMm && d <= CatchUpFarMm && Kinematics.IsClear(mark.XMm, mark.ZMm, radius, space, obstacles)
                && !Walled(mark.XMm, mark.ZMm, player.XMm, player.ZMm))
            {
                spot = (mark.XMm, mark.ZMm);
                from = i + 1;
            }
        }
        spot ??= Ring(player, player.FacingMdeg + 180_000, 2_500, obstacles);
        if (spot is not { } at)
            return c with { StuckTicks = 0 };
        var to = new Body(at.X, space.Terrain.HeightAtMm(at.X, at.Z), at.Z, CombatRules.FacingTowards(at.X, at.Z, player.XMm, player.ZMm));
        Place(npc, to);
        _context.Events.Publish(new CompanionCaughtUp(c.NpcId, reason, npc.Body, to, tick));
        return c with { StuckTicks = 0, Trail = c.Trail.RemoveRange(0, Math.Min(from, c.Trail.Length)) };
    }

    /// <summary>The first point on a ring round a place, starting from a facing and going round in eighths, where a body fits in sight of it.</summary>
    private (long X, long Z)? Ring(Body around, long startMdeg, long radiusMm, IReadOnlyList<Blocker> obstacles)
    {
        var space = _context.Setup.Layout.Space;
        long body = _context.Setup.Movement.BodyRadiusMm;
        for (int i = 0; i < 8; i++)
        {
            double angle = (startMdeg + i * 45_000L) / 1000.0 * Math.PI / 180;
            long x = around.XMm + (long)Math.Round(Math.Sin(angle) * radiusMm), z = around.ZMm + (long)Math.Round(Math.Cos(angle) * radiusMm);
            if (Kinematics.IsClear(x, z, body, space, obstacles) && !Walled(x, z, around.XMm, around.ZMm))
                return (x, z);
        }
        return null;
    }

    // ── fighting ────────────────────────────────────────────────────────────

    /// <summary>
    /// What to fight: the creature being fought while it is still worth it; else the nearest creature engaged with the character that a
    /// following companion can reach within the fight radius - and leash - or that has come within a waiting one's guard.
    /// </summary>
    private CreatureState? Target(CompanionState c, NpcState npc)
    {
        // The stance (WP15a): passive never fights; defensive fights only what comes close, as a waiting companion does.
        if (c.Stance == CompanionStance.Passive)
            return null;
        var tuning = Tuning!;
        var body = npc.Body;
        var player = State.Body;
        long radius = c.Order == CompanionOrder.Follow && c.Stance == CompanionStance.Aggressive ? tuning.FightRadiusMm : tuning.GuardRadiusMm;
        bool leashed = c.Order == CompanionOrder.Follow && c.Tactics.KeepToLeash;
        bool InPlay(CreatureState x, long within) =>
            x.Alive && TierOf(x.Body) == SimulationTier.A && Distance(body.XMm, body.ZMm, x.Body.XMm, x.Body.ZMm) <= within
            && (!leashed || Distance(player.XMm, player.ZMm, x.Body.XMm, x.Body.ZMm) <= tuning.LeashMm);
        if (c.TargetKey is { } key && State.Creatures.TryGetValue(key, out var current) && InPlay(current, radius * 3 / 2))
            return current;
        if (State.PlayerCombat.Defeated)
            return null;
        // Protecting the injured, what is nearest an injured ally goes first; otherwise what is nearest.
        var ward = c.Tactics.ProtectInjured ? InjuredAlly(c.NpcId) : null;
        return State.Creatures.Values
            .Where(x => x.Mind == CreatureMind.Engaged && InPlay(x, radius) && !Walled(body.XMm, body.ZMm, x.Body.XMm, x.Body.ZMm))
            .OrderBy(x => ward is { } w ? Distance(w.XMm, w.ZMm, x.Body.XMm, x.Body.ZMm) : Distance(body.XMm, body.ZMm, x.Body.XMm, x.Body.ZMm))
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>The most hurt ally below the injured share - the character, or another party member on their feet - or null.</summary>
    private Body? InjuredAlly(string self)
    {
        var stats = ProgressionEngine.Derive(State.Progression, _context.Setup.Progression);
        int max = (int)stats.HealthMax, health = State.Progression.Pools.Health ?? max;
        var allies = State.Companions.Values.Where(m => m.NpcId != self && m.Condition == CompanionCondition.Up && State.Npcs.ContainsKey(m.NpcId))
            .Select(m => (Share: m.Health * 1000L / Math.Max(1, m.Profile.MaxHealth), Injured: PartyRulesOf.Injured(m.Health, m.Profile.MaxHealth), Body: State.Npcs[m.NpcId].Body))
            .Append((Share: health * 1000L / Math.Max(1, max), Injured: !State.PlayerCombat.Defeated && PartyRulesOf.Injured(health, max), Body: State.Body));
        return allies.Where(a => a.Injured).OrderBy(a => a.Share).Select(a => (Body?)a.Body).FirstOrDefault();
    }

    /// <summary>Close to reach - a waiting companion holds their place and only turns - face the creature, and swing once a breath has passed.</summary>
    private CompanionState Fight(CompanionState c, NpcState npc, CreatureState target, long tick)
    {
        var body = npc.Body;
        c = c with { TargetKey = target.Key, LastCombatTick = tick, StuckTicks = 0 };
        double distance = Distance(body.XMm, body.ZMm, target.Body.XMm, target.Body.ZMm);
        int toward = CombatRules.FacingTowards(body.XMm, body.ZMm, target.Body.XMm, target.Body.ZMm);
        if (distance <= c.Attack.ReachMm + target.Definition.RadiusMm - 100 && !Walled(body.XMm, body.ZMm, target.Body.XMm, target.Body.ZMm))
        {
            var turned = Turn(body, toward);
            Place(npc, turned);
            if (Math.Abs(Delta(turned.FacingMdeg, toward)) > 30_000 || tick < c.NextAttackTick)
                return c;
            _context.Events.Publish(new AttackStarted(npc.InstanceId, c.Attack.Source, c.Attack.WindupTicks, c.Attack.ActiveTicks, c.Attack.RecoveryTicks, tick));
            return c with { Action = ActionState.Begin(ActionKind.Attack, tick, c.Attack) };
        }
        Place(npc, c.Order == CompanionOrder.Wait ? Turn(body, toward) : Step(npc, target.Body.XMm, target.Body.ZMm, Gait.Run));
        return c;
    }

    /// <summary>The active window of a swing: every living creature in front within reach, with no wall between, is struck once.</summary>
    private CompanionState Swing(CompanionState c, NpcState npc, long tick)
    {
        var action = c.Action;
        var attack = action.Attack!;
        var body = npc.Body;
        var struck = action.Struck;
        foreach (var creature in State.Creatures.Values.Where(x => x.Alive && !struck.Contains(x.Id)).ToList())
        {
            if (!CombatRules.InFront(body.XMm, body.ZMm, body.FacingMdeg, creature.Body.XMm, creature.Body.ZMm,
                    attack.ReachMm + creature.Definition.RadiusMm, C.MeleeArcMdeg)
                || Walled(body.XMm, body.ZMm, creature.Body.XMm, creature.Body.ZMm))
                continue;
            struck = struck.Add(creature.Id);
            Hit(c, npc, creature, attack, tick);
        }
        if (struck.IsEmpty && tick - action.StartTick == attack.WindupTicks + attack.ActiveTicks)
            _context.Events.Publish(new AttackMissed(npc.InstanceId, attack.Source, tick));
        return c with { Action = action with { Struck = struck }, LastCombatTick = tick };
    }

    /// <summary>A companion's blow: resolved as the character's are, with the weapon's own numbers and nothing of the character's.</summary>
    private void Hit(CompanionState c, NpcState npc, CreatureState creature, AttackProfile attack, long tick)
    {
        var definition = creature.Definition;
        var effects = _context.Setup.Combat.Effects;
        var defense = new DefenseProfile(definition.MaxHealth, CombatSystem.WithBonus(definition.Armor, EffectRules.ArmorBonus(EffectsOf(creature.Id), effects)),
            definition.Resistances, Blocking: false, Dodging: false);
        var strike = new Strike(attack, 1.0, C.BaseCritPercent, 1.0);
        if (definition.WeakPoint is { FromBehind: true } weak && CombatSystem.Behind(creature.Body, npc.Body))
            strike = strike with { ForcedRegion = weak.Region };
        var result = CombatRules.Resolve(strike, defense, C, Random(c.NpcId, creature.Key, creature.Body, tick));
        _context.Dispatch(new WoundCreature(creature.Id, result, attack.Source)
        {
            Attacker = npc.InstanceId,
            AttackerDefId = c.NpcId,
            FromXMm = npc.Body.XMm,
            FromZMm = npc.Body.ZMm,
        });
        // Their weapon skill grows as the character's does, from a blow that wounded something that fights back (WP15b).
        if (attack.SkillId is { } skill && result.Final > 0)
            _context.Dispatch(new PracticePartySkill(c.NpcId, new SkillPractice(skill, definition.Level, PracticeOutcome.Success, tick)));
    }

    // ── downed, helped up, fallen ───────────────────────────────────────────

    private static CompanionState Up(CompanionState c, int health, long tick) => c with
    {
        Condition = CompanionCondition.Up,
        Health = health,
        DownedTick = 0,
        LastCombatTick = tick,
        Action = ActionState.Idle,
        TargetKey = null,
        StuckTicks = 0,
        Trail = ImmutableArray<TrailMark>.Empty,
    };

    /// <summary>
    /// Downed too long with no hand, the party member falls (DN9). A named companion or an ally is gravely wounded: they leave the party
    /// and go home, where they recover for some days (canon's guardrail: no companion lost to jank). A hireling dies, as they were told
    /// when hired. Either way they are out of the party; the caller does not keep them.
    /// </summary>
    private void Fall(CompanionState c, NpcState npc, long tick)
    {
        State.RemoveCompanion(_owner, c.NpcId);
        if (c.Membership == PartyMembership.Hireling)
        {
            State.AddFallen(_owner, c.NpcId);
            _context.Events.Publish(new CompanionFell(c.NpcId, npc.Body, tick));
            // What was loaned comes back to the character; what was theirs lies where they fell (§2.15, DN9).
            _context.Dispatch(new SettleFallen(c.NpcId, npc.Body.XMm, npc.Body.ZMm));
            _context.Dispatch(new RemoveNpc(c.NpcId, tick));
            _context.Events.Publish(new HirelingDied(c.NpcId, tick));
            return;
        }
        var home = Home(npc);
        Place(npc, home);
        _context.Events.Publish(new CompanionFell(c.NpcId, home, tick));
        long until = tick + WoundDays(c.NpcId, tick) * _context.Setup.Clock.TicksPerDay;
        State.SetRecovering(_owner, c.NpcId, until);
        _context.Dispatch(new ReturnLoans(c.NpcId));
        _life.TakeUp(new PartyLifeEntry(c.NpcId, c.Membership, "wounded", State.PartySheets.GetValueOrDefault(c.NpcId), tick));
        _context.Events.Publish(new CompanionGravelyWounded(c.NpcId, until, tick));
    }

    // ── views ───────────────────────────────────────────────────────────────

    public ImmutableArray<CompanionView> Views() =>
        State.Companions.Values.Where(c => State.Npcs.ContainsKey(c.NpcId)).Select(c =>
        {
            var npc = State.Npcs[c.NpcId];
            string doing = c.Condition == CompanionCondition.Downed ? "downed"
                : c.TargetKey is not null || c.Action.Kind != ActionKind.Idle ? "fighting"
                : c.Order == CompanionOrder.Wait ? "waiting"
                : "following";
            return new CompanionView(c.NpcId, npc.InstanceId, npc.Definition.Name, c.Order, c.Condition, c.Health, c.Profile.MaxHealth, npc.Body, doing)
            {
                FallsAtTick = c.Condition == CompanionCondition.Downed ? c.DownedTick + Tuning!.ReviveWindowTicks : null,
                Membership = c.Membership,
                Stance = c.Stance,
                Tactics = c.Tactics,
                Contract = c.Contract,
                WeaponId = c.Profile.WeaponId,
                Armor = c.Profile.Armor,
            };
        }).ToImmutableArray();

    // ── fitting out and going along ─────────────────────────────────────────

    /// <summary>
    /// The body a member fights with (WP15b): their own, with the health their attributes add, the melee weapon in their main hand if they
    /// wear one, and the armour they wear on each region - read as the character's is (COMBAT §11, a piece's quality moving its armour).
    /// </summary>
    private CompanionProfile? Fitted(string npcId, CompanionProfile body)
    {
        if (!State.PartySheets.TryGetValue(npcId, out var sheet))
            return body;
        var catalog = _context.Setup.Items.Catalog;
        string weapon = sheet.Gear.TryGetValue(EquipSlot.MainHand, out var held) && catalog.Find(held.Item.DefId)?.Weapon is { Ranged: false }
            ? held.Item.DefId
            : body.WeaponId;
        int Worn(params EquipSlot[] slots) => slots.Sum(slot =>
            sheet.Gear.TryGetValue(slot, out var gear) && catalog.Find(gear.Item.DefId) is { } item
                ? EquipmentRules.ArmorOf(item, gear.Item.Quality, _context.Setup.Crafting.Constants.ArmorPerStep)
                : 0);
        var worn = new Dictionary<BodyRegion, int>
        {
            [BodyRegion.Head] = Worn(EquipSlot.Head),
            [BodyRegion.Torso] = Worn(EquipSlot.Chest, EquipSlot.Cloak, EquipSlot.OffHand),
            [BodyRegion.Limbs] = Worn(EquipSlot.Hands, EquipSlot.Legs, EquipSlot.Feet),
        };
        var armor = Enum.GetValues<BodyRegion>().Where(r => body.Armor.ContainsKey(r) || worn.GetValueOrDefault(r) > 0)
            .ToImmutableSortedDictionary(r => r, r => body.Armor.GetValueOrDefault(r) + worn.GetValueOrDefault(r));
        return new CompanionProfile(Approval.MaxHealth(body.MaxHealth, sheet.Progression, _context.Setup.Progression), weapon, armor);
    }

    /// <summary>A member's gear or attributes changed: their body is fitted again, their health kept within it.</summary>
    public string? Handle(RefitMember command)
    {
        if (!State.Companions.TryGetValue(command.NpcId, out var c) || !State.Npcs.TryGetValue(command.NpcId, out var npc)
            || BodyOf(npc.Definition, c.Membership) is not { } body || Fitted(command.NpcId, body) is not { } profile || AttackOf(profile) is not { } attack)
            return null;
        int health = c.Condition == CompanionCondition.Downed ? 0
            : c.Health >= c.Profile.MaxHealth ? profile.MaxHealth
            : Math.Clamp(c.Health, 1, profile.MaxHealth);
        State.SetCompanion(_owner, c with { Profile = profile, Attack = attack, Health = health });
        return null;
    }

    /// <summary>
    /// Whether a member goes along with what they are asked (WP15b): past a refuse or leave bound they refuse; past a hesitate bound they go
    /// along after a moment's doubt, which is said; otherwise they simply do.
    /// </summary>
    private string? Willing(string npcId, string asked, long tick)
    {
        switch (PartyDisposition.Of(State, Rules, npcId))
        {
            case Disposition.Refuse or Disposition.Leave:
                return $"{Name(npcId)} refuses";
            case Disposition.Hesitate:
                _context.Events.Publish(new PartyMemberHesitated(npcId, asked, tick));
                return null;
            default:
                return null;
        }
    }

    /// <summary>A member leaves of their own accord (WP15b): past the leave bound. Someone down stays where they lie.</summary>
    public string? Handle(LeaveParty command, long tick)
    {
        if (!State.Companions.TryGetValue(command.NpcId, out var c) || c.Condition == CompanionCondition.Downed)
            return null;
        Leave(c, command.Reason, tick, null);
        return null;
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>A companion's attack: their weapon's, timed as the character's would be; null when it is not a melee weapon.</summary>
    private AttackProfile? AttackOf(CompanionProfile profile) =>
        _context.Setup.Items.Catalog.Find(profile.WeaponId)?.Weapon is { Ranged: false } weapon
            ? CombatSystem.AttackOf(profile.WeaponId, weapon, C, TickMs)
            : null;

    private string Name(string npcId) => State.Npcs.TryGetValue(npcId, out var npc) ? npc.Definition.Name : npcId;

    private void Place(NpcState npc, Body body)
    {
        if (body != npc.Body)
            _context.Dispatch(new PlaceNpc(npc.Definition.Id, body));
    }

    /// <summary>One tick of walking towards a point, at a gait, facing the way it goes; everything solid pushes back.</summary>
    private Body Step(NpcState npc, long toXMm, long toZMm, Gait gait)
    {
        var from = npc.Body;
        double dx = toXMm - from.XMm, dz = toZMm - from.ZMm;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length < 1)
            return from;
        var intent = new MoveIntent((int)Math.Round(dx / length * MoveIntent.FullDeflection), (int)Math.Round(dz / length * MoveIntent.FullDeflection), gait,
            CombatRules.FacingTowards(from.XMm, from.ZMm, toXMm, toZMm));
        return Kinematics.Step(from, intent, _context.Setup.Movement, _context.Setup.Layout.Space, Obstacles(npc.Definition.Id), TickMs);
    }

    /// <summary>What a companion's body cannot pass: closed doors, standing barriers, creatures alive, the character, and other people.</summary>
    private List<Blocker> Obstacles(string npcId)
    {
        long radius = _context.Setup.Movement.BodyRadiusMm;
        var obstacles = new List<Blocker>(_context.ClosedDoors());
        obstacles.AddRange(State.Creatures.Values.Where(x => x.Alive)
            .Select(x => (Blocker)new CircleBlocker(x.Key, x.Body.XMm, x.Body.ZMm, x.Definition.RadiusMm, 0)));
        obstacles.Add(new CircleBlocker("player", State.Body.XMm, State.Body.ZMm, radius, 0));
        obstacles.AddRange(State.Npcs.Values.Where(n => n.Definition.Id != npcId)
            .Select(n => (Blocker)new CircleBlocker(n.Definition.Id, n.Body.XMm, n.Body.ZMm, radius, 0)));
        return obstacles;
    }

    /// <summary>A straight line with room for a body along it: nothing solid across the middle or either edge.</summary>
    private bool InClearView(Body from, long toXMm, long toZMm)
    {
        double dx = toXMm - from.XMm, dz = toZMm - from.ZMm;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length < 1)
            return true;
        double radius = _context.Setup.Movement.BodyRadiusMm;
        double ox = -dz / length * radius, oz = dx / length * radius;
        return !Walled(from.XMm, from.ZMm, toXMm, toZMm)
               && !Walled(from.XMm + ox, from.ZMm + oz, toXMm + ox, toZMm + oz)
               && !Walled(from.XMm - ox, from.ZMm - oz, toXMm - ox, toZMm - oz);
    }

    private bool Walled(double x0, double z0, double x1, double z1) =>
        _context.Setup.Layout.Space.Blockers.Concat(_context.ClosedDoors()).Any(b => b.Crosses(x0, z0, x1, z1));

    /// <summary>The body turned towards a facing by at most 360 degrees a second, as NPCs turn.</summary>
    private Body Turn(Body body, int towardMdeg)
    {
        long step = 360_000L * TickMs / 1000;
        int delta = Delta(body.FacingMdeg, towardMdeg);
        if (Math.Abs(delta) <= step)
            return body with { FacingMdeg = towardMdeg };
        long turned = body.FacingMdeg + Math.Sign(delta) * step;
        return body with { FacingMdeg = (int)(((turned % 360_000) + 360_000) % 360_000) };
    }

    private static int Delta(int fromMdeg, int toMdeg)
    {
        int delta = ((toMdeg - fromMdeg) % 360_000 + 360_000) % 360_000;
        return delta > 180_000 ? delta - 360_000 : delta;
    }

    private ImmutableArray<ActiveEffect> EffectsOf(EntityId body) => State.Effects.GetValueOrDefault(body, ImmutableArray<ActiveEffect>.Empty);

    /// <summary>The rolls of one blow, keyed by who struck whom on which tick: the same on a replay and after a load.</summary>
    private Func<uint, double> Random(string attacker, string target, Body at, long tick)
    {
        var channel = RngChannel.Open(State.World.WorldSeed, CellKey.OfWorld(at.XMm / 1000.0, at.ZMm / 1000.0), "combat", $"{attacker}>{target}@{tick}");
        return sample => channel.UInt64(sample) / SampleScale;
    }

    private SimulationTier TierOf(Body body) =>
        State.Tiers.GetValueOrDefault(CellKey.OfWorld(body.XMm / 1000.0, body.ZMm / 1000.0).ToString(), SimulationTier.D);

    private static double Distance(long x0, long z0, long x1, long z1)
    {
        double dx = x1 - x0, dz = z1 - z0;
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
