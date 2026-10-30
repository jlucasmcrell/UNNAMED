// UNNAMED World - NPCs walking their day: the never-teleporting mover and the tier B/C simulations (WP12; ported from M7 §3.12, §4.15)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>
/// The NPCs' tier simulations (D-06, WORLD_ARCHITECTURE.md §5): B moves bodies coarsely along their routes twice a second, C keeps only the
/// hour's cursor and puts bodies where their routine has them. Tier A's full walk is <see cref="NpcSystem.Tick"/>'s; tier D is stored state,
/// advanced an hour at a time with everyone else's life. Which tier an NPC is simulated at is their cell's, within the tier budgets.
/// </summary>
internal sealed class NpcTierSimulation : ITierSimulation
{
    private readonly NpcSystem _npcs;

    public NpcTierSimulation(SimulationTier tier, NpcSystem npcs)
    {
        Tier = tier;
        _npcs = npcs;
    }

    public SimulationTier Tier { get; }

    public void Tick(long tick, IReadOnlyList<CellKey> cells) => _npcs.TickTier(tick, Tier);
}

internal sealed partial class NpcSystem
{
    /// <summary>Where an alarm bell sends the guards, and when they stop answering it (STEALTH §8.2).</summary>
    private long _alarmXMm, _alarmZMm, _alarmUntilTick;

    /// <summary>A bell has rung: everyone within its earshot goes to the post. The responders answer before their rounds do.</summary>
    public void RespondToAlarm(long xMm, long zMm, long untilTick)
    {
        _alarmXMm = xMm;
        _alarmZMm = zMm;
        _alarmUntilTick = untilTick;
    }

    /// <summary>Headway: a step of at least 30% of a walk's travel, else the tick counts as stuck.</summary>
    private const long HeadwayMm = 24;

    /// <summary>Tier B moves once every this many ticks (2 Hz at 20 Hz, §2.13).</summary>
    public const int RegionalEveryTicks = 10;

    /// <summary>
    /// Adventurers walk to the place their day gives them (spec 15, the owner's inn): they have no life record, so the mover's own tier-A step is
    /// taken for them here. A body is never placed from one seat to the next; the walk is the same one a resident walks to their work.
    /// </summary>
    private void WalkAdventurers(long tick)
    {
        string? talking = State.Conversation?.NpcId;
        foreach (var npc in State.Npcs.Values.Where(n => n.Definition.Role == AdventurerSystem.Role && n.Definition.Id != talking
                     && !State.Companions.ContainsKey(n.Definition.Id)).ToList())
        {
            if ((npc.Body.XMm, npc.Body.ZMm) == (npc.Site.XMm, npc.Site.ZMm))
                continue;
            var goal = new NavPoint(npc.Site.XMm, npc.Site.ZMm);
            var (moved, _) = Walk(npc, goal, goal);
            if (moved != npc.Body)
                State.SetNpc(_owner, npc with { Body = moved });
        }
    }


    private int TickMs => _context.Setup.TickMilliseconds;

    /// <summary>A tier simulation's tick: B moves its bodies on every tenth tick; C keeps the hour and parks. Lives cross the hour first.</summary>
    public void TickTier(long tick, SimulationTier tier)
    {
        TickLives(tick);
        if (tier == SimulationTier.B && tick % RegionalEveryTicks == 0)
            TickBodies(tick, SimulationTier.B);
    }

    /// <summary>
    /// The bodies simulated at <paramref name="tier"/> this tick, in key order. Nobody talking moves (talking pauses the routine), nor a
    /// companion (the party moves them), nor anyone injured beyond their home. A: a full walk, a tick at a time, with collision and doors;
    /// B: a coarse glide along the route, doors opened as reached. Neither ever places a body: it is only walked (no visible teleport).
    /// </summary>
    private void TickBodies(long tick, SimulationTier tier)
    {
        if (State.World.NpcLife is not { } life)
            return;
        var tiers = EffectiveTiers(life);
        string? talking = State.Conversation?.NpcId;
        foreach (var record in life.Npcs.Values)
        {
            if (tiers.GetValueOrDefault(record.Key, SimulationTier.D) != tier || record.Key == talking || State.Companions.ContainsKey(record.Key)
                || record.Condition == NpcCondition.Hired || !State.Npcs.TryGetValue(record.Key, out var npc))
                continue;
            var current = LifeOf(record.Key)!;
            if (tier == SimulationTier.A)
            {
                if (!StepAside(npc, current))
                    WalkTick(npc, current, tick);
            }
            else
                GlideTick(npc, current, tick);
        }
    }

    /// <summary>
    /// The tier each NPC is simulated at: their cell's, then the budgets (§5, A=60 and B=300): beyond A's budget those farthest from every
    /// observer (WP12b: the nearest one counts) are simulated at B, beyond B's at C. Someone being spoken to is always A. Admission only -
    /// nobody is refused or despawned for the count.
    /// </summary>
    internal ImmutableSortedDictionary<string, SimulationTier> EffectiveTiers(NpcLifeState life)
    {
        var rules = _context.Setup.Tiers;
        string? talking = State.Conversation?.NpcId;
        var observers = _observers(State.WorldTick);
        var placed = life.Npcs.Values.Where(n => State.Npcs.ContainsKey(n.Key)).Select(n =>
        {
            var body = State.Npcs[n.Key].Body;
            double nearest = observers.Count == 0 ? double.MaxValue : observers.Min(o =>
            {
                double dx = body.XMm - o.XMm, dz = body.ZMm - o.ZMm;
                return dx * dx + dz * dz;
            });
            var tier = n.Key == talking ? SimulationTier.A : TierAt(body.XMm, body.ZMm);
            return (n.Key, Tier: tier, Distance: n.Key == talking ? -1 : nearest);
        }).ToList();
        var result = ImmutableSortedDictionary.CreateBuilder<string, SimulationTier>(StringComparer.Ordinal);
        foreach (var (tier, budget, overflow) in new[] { (SimulationTier.A, rules.FullBudget, SimulationTier.B), (SimulationTier.B, rules.RegionalBudget, SimulationTier.C) })
        {
            int admitted = 0;
            foreach (var n in placed.Where(p => p.Tier == tier).OrderBy(p => p.Distance).ThenBy(p => p.Key, StringComparer.Ordinal).ToList())
            {
                if (admitted++ < budget)
                    result[n.Key] = tier;
                else
                    placed[placed.FindIndex(p => p.Key == n.Key)] = n with { Tier = overflow };
            }
        }
        foreach (var n in placed.Where(p => !result.ContainsKey(p.Key)))
            result[n.Key] = n.Tier;
        return result.ToImmutable();
    }

    /// <summary>The tier of the cell holding a point; D outside the region's cells.</summary>
    private SimulationTier TierAt(long xMm, long zMm)
    {
        var cell = CellKey.OfWorld(xMm / 1000.0, zMm / 1000.0);
        return State.Tiers.GetValueOrDefault(cell.ToString(), SimulationTier.D);
    }

    /// <summary>
    /// The hour's turn for those far off (tier C and D, by their cell and where they are headed): each body is put where its routine has it
    /// now. Never a body in or bound for a cell of tier A or B - those walk.
    /// </summary>
    private void Park()
    {
        if (State.World.NpcLife is not { } life)
            return;
        var tiers = EffectiveTiers(life);
        foreach (var npc in life.Npcs.Values)
        {
            if (tiers.GetValueOrDefault(npc.Key, SimulationTier.D) < SimulationTier.C || State.Companions.ContainsKey(npc.Key) || npc.Condition == NpcCondition.Hired)
                continue;
            var (x, z, facing) = Goal(npc);
            if (TierAt(x, z) >= SimulationTier.C && (npc.XMm, npc.ZMm, npc.FacingMdeg) != (x, z, facing))
                Settle(npc, x, z, facing);
        }
    }

    /// <summary>
    /// Where an NPC is headed now, and how they will face there: a trip's meeting place or destination, else the anchor their schedule's
    /// activity puts them at this hour. Derived, never stored.
    /// </summary>
    private (long X, long Z, int Facing) Goal(NpcLifeRecord npc)
    {
        var routine = Life.Routines[npc.RoutineId];
        // Captives keep their original site until the existing quest barrier is lifted.
        if (routine.ReleaseBarrier is { } key
            && _context.Setup.Layout.Barriers.FirstOrDefault(b => b.Key == key) is { } barrier
            && State.World.GetFlag(Simulation.CellOf(_context.Setup.Layout.SpaceId, barrier), barrier.FlagId) == 0
            && PlaceOf(npc.Key) is { } held)
            return held;
        // A trip (WP12b): waiting at its meeting place, then gone to its destination.
        if (routine.Trip is { } trip && npc.Activity is NpcActivities.SetOut or NpcActivities.Away)
        {
            var at = npc.Activity == NpcActivities.SetOut ? trip.Meet : trip.Destination;
            return (at.XMm, at.ZMm, at.FacingMdeg);
        }
        int hourOfDay = (int)(((State.World.NpcLife!.Hour % Clock.HoursPerDay) + Clock.HoursPerDay) % Clock.HoursPerDay);
        // An alarm outranks both the round and the day: a body that hears the bell goes to it (STEALTH §8.2).
        if (_alarmUntilTick > State.WorldTick)
        {
            long dx = _alarmXMm - npc.XMm, dz = _alarmZMm - npc.ZMm;
            double away = Math.Sqrt((double)dx * dx + (double)dz * dz);
            if (away <= AlarmSystem.EarshotMm)
            {
                // A bell hangs on a post: she answers the alarm beside it, not inside it. Stand off by a body's reach and face the post.
                double scale = away > AlarmSystem.PostStandOffMm ? (away - AlarmSystem.PostStandOffMm) / away : 0;
                return (npc.XMm + (long)Math.Round(dx * scale), npc.ZMm + (long)Math.Round(dz * scale), (int)PatrolRules.FacingToward(dx, dz));
            }
        }
        // A patrol (STEALTH §8.2): while she is on duty the round is her goal, so she walks stop to stop and stands at each one. Off duty she
        // goes back to her routine - the round is the post, not the day.
        if (PatrolLegFor(npc, routine, hourOfDay) is { } round)
            return (round.XMm, round.ZMm, (int)round.FacingMdeg);
        var own = routine.AnchorFor(npc.Activity, hourOfDay);
        return (own.XMm, own.ZMm, own.FacingMdeg);
    }

    /// <summary>
    /// Where her patrol has her at this tick, or null when she is not on it: the authored round (STEALTH §8.2) is walked at a walk while her
    /// routine's work hours are open, and she stands at each stop for the rest of its share of the period. A body with no patrol - everyone in
    /// the game so far - never enters this.
    /// </summary>
    private PatrolLeg? PatrolLegFor(NpcLifeRecord life, RoutineDefinition routine, int hourOfDay)
    {
        var layout = _context.Layout;
        if (layout.Patrols.IsDefaultOrEmpty)
            return null;
        bool onDuty = routine.WorkFromHour <= routine.WorkToHour
            ? hourOfDay >= routine.WorkFromHour && hourOfDay < routine.WorkToHour
            : hourOfDay >= routine.WorkFromHour || hourOfDay < routine.WorkToHour;
        if (!onDuty)
            return null;
        foreach (var patrol in layout.Patrols)
        {
            if (patrol.NpcId != routine.Key)
                continue;
            int tickMs = TickMs;
            long walkMmPerSecond = Math.Max(1, _context.Setup.Movement.BaseSpeedMmPerSecond * _context.Setup.Movement.WalkPercent / 100);
            long step = Math.Max(1, walkMmPerSecond * tickMs / 1000);
            return PatrolRules.At(patrol, State.WorldTick, step);
        }
        return null;
    }

    /// <summary>
    /// One tick of a full walk (tier A). The NPC lands exactly on the goal and turns exactly to its facing; on the way, the route is
    /// followed, replanned when the world under it changes, and a closed door in the way is opened from within a hand's reach.
    /// </summary>
    private void WalkTick(NpcState npc, NpcLifeRecord life, long tick)
    {
        if (life.Condition == NpcCondition.Injured && life.Route.Status == NavRouteStatus.None && life.Activity == NpcActivities.Sleep
            && (npc.Body.XMm, npc.Body.ZMm) == Home(life))
            return;
        var (goalX, goalZ, goalFacing) = Goal(life);
        var body = npc.Body;
        if (body.XMm == goalX && body.ZMm == goalZ)
        {
            Write(npc, life, Turn(body, goalFacing), NavRoute.None, 0);
            return;
        }
        var goal = new NavPoint(goalX, goalZ);
        var step = _navigation.Follow(_navigation.Person, life.Route, new NavPoint(body.XMm, body.ZMm), goal, life.StuckTicks, tick);
        if (step.ReplanReason is { } why)
            _context.Events.Publish(new RoutePlanned(life.Key, NavSearch.OutcomeKey(step.Outcome!.Value), why, step.Route.Corners.Length, step.Expansions, tick));
        switch (step.Kind)
        {
            case NavStepKind.Unreachable:
                Write(npc, life, Turn(body, CombatRules.FacingTowards(body.XMm, body.ZMm, goalX, goalZ)), step.Route, life.StuckTicks + 1);
                break;
            case NavStepKind.OpenGate:
                // A door opened costs no headway; one that cannot be counts as stuck, so the stuck replans still come.
                bool opened = OpenDoor(step.GateKey!, npc, tick);
                Write(npc, life, Turn(body, CombatRules.FacingTowards(body.XMm, body.ZMm, step.Target.XMm, step.Target.ZMm)), step.Route,
                    opened ? life.StuckTicks : life.StuckTicks + 1);
                break;
            default:
                var (moved, landing) = Walk(npc, step.Target, goal);
                long dx = moved.XMm - body.XMm, dz = moved.ZMm - body.ZMm;
                bool headway = landing || dx * dx + dz * dz >= HeadwayMm * HeadwayMm;
                Write(npc, life, moved, step.Route, headway ? 0 : life.StuckTicks + 1);
                break;
        }
    }

    /// <summary>
    /// One coarse step (tier B, every <see cref="RegionalEveryTicks"/> ticks): the route's own corners, as far as half a second of walking
    /// takes the body, with no collision - the route is already clear of the world. A door in the way is opened as the body reaches it.
    /// </summary>
    private void GlideTick(NpcState npc, NpcLifeRecord life, long tick)
    {
        var (goalX, goalZ, goalFacing) = Goal(life);
        var body = npc.Body;
        if (body.XMm == goalX && body.ZMm == goalZ)
        {
            Write(npc, life, body with { FacingMdeg = goalFacing }, NavRoute.None, 0);
            return;
        }
        var goal = new NavPoint(goalX, goalZ);
        var step = _navigation.Follow(_navigation.Person, life.Route, new NavPoint(body.XMm, body.ZMm), goal, life.StuckTicks, tick);
        if (step.Kind == NavStepKind.Unreachable)
        {
            // A body pressed into something cannot plan from where it stands: it steps out to the nearest place it can stand first.
            if (StandingClear(body) is { } clear)
                Write(npc, life, BodyAt(npc.Space, clear.XMm, clear.ZMm, body.FacingMdeg), NavRoute.None, 0);
            else
                Write(npc, life, body, step.Route, life.StuckTicks + 1);
            return;
        }
        if (step.Kind == NavStepKind.OpenGate)
        {
            Write(npc, life, body, step.Route, OpenDoor(step.GateKey!, npc, tick) ? life.StuckTicks : life.StuckTicks + 1);
            return;
        }
        double budget = _context.Setup.Movement.SpeedMmPerSecond(Gait.Walk) * TickMs * RegionalEveryTicks / 1000.0;
        var route = step.Route;
        double x = body.XMm, z = body.ZMm;
        int facing = body.FacingMdeg;
        while (budget > 0)
        {
            var target = route.Corners[0];
            double dx = target.XMm - x, dz = target.ZMm - z, length = Math.Sqrt(dx * dx + dz * dz);
            if (length > 0)
                facing = CombatRules.FacingTowards((long)x, (long)z, target.XMm, target.ZMm);
            if (length > budget)
            {
                x += dx / length * budget;
                z += dz / length * budget;
                break;
            }
            (x, z) = (target.XMm, target.ZMm);
            budget -= length;
            if (route.Corners.Length == 1)
                break;
            route = route.Advance(1);
        }
        long nx = (long)Math.Round(x), nz = (long)Math.Round(z);
        bool landed = nx == goalX && nz == goalZ;
        // The glide is checked as the walk is: a step that would sweep through the world is not taken, and counts as stuck.
        long r = _navigation.Config.Classes[_navigation.Person.ClassIndex].RadiusMm - _navigation.Config.Limits.OffLineToleranceMm;
        if (!NavSearch.Clear(_navigation.Grid, _navigation.Person, _navigation.IsGateOpen, new NavPoint(body.XMm, body.ZMm), new NavPoint(nx, nz), r))
        {
            Write(npc, life, body, route, life.StuckTicks + 1);
            return;
        }
        Write(npc, life, BodyAt(npc.Space, nx, nz, landed ? goalFacing : facing), landed ? NavRoute.None : route, 0);
    }

    /// <summary>
    /// When a body cannot stand where it is, the nearest point within a metre where it can, by (distance, z, x) on a 125 mm lattice; null when
    /// it stands clear already, or nothing near does.
    /// </summary>
    private NavPoint? StandingClear(Body body)
    {
        var at = new NavPoint(body.XMm, body.ZMm);
        if (_navigation.Standable(at))
            return null;
        const long Step = 125;
        var best = ((long D2, long Z, long X)?)null;
        for (long dz = -8; dz <= 8; dz++)
        {
            for (long dx = -8; dx <= 8; dx++)
            {
                var p = new NavPoint(body.XMm + dx * Step, body.ZMm + dz * Step);
                var key = (dx * dx + dz * dz, p.ZMm, p.XMm);
                if ((best is null || key.CompareTo(best.Value) < 0) && _navigation.Standable(p))
                    best = key;
            }
        }
        return best is { } b ? new NavPoint(b.X, b.Z) : null;
    }

    /// <summary>An NPC opens a door in their way: NPCs open doors and never close them. False when it is no door, already open, or locked.</summary>
    private bool OpenDoor(string key, NpcState npc, long tick)
    {
        if (_navigation.Door(key) is not { } door || _context.IsOpen(door) || _context.Locked(door))
            return false;
        if (door.ClosedFootprint.DistanceTo(npc.Body.XMm, npc.Body.ZMm) > _context.Setup.Movement.InteractReachMm)
            return false;
        if (_context.Dispatch(new SetWorldFlag(Simulation.CellOf(SpaceId.Exterior, door), door.FlagId, 1)) is not null)
            return false;
        _context.Events.Publish(new DoorToggled(npc.InstanceId, door.Key, true, tick));
        return true;
    }

    /// <summary>
    /// One tick's walk towards a target. Within one tick's travel of the goal, the direction is the remaining offset in thousandths of the
    /// travel, rounded half away from zero, so the step ends exactly on the goal's millimetre; otherwise a full step along the way.
    /// </summary>
    private (Body Moved, bool Landing) Walk(NpcState npc, NavPoint target, NavPoint goal)
    {
        var body = npc.Body;
        long travel = (long)_context.Setup.Movement.SpeedMmPerSecond(Gait.Walk) * TickMs / 1000;
        long dx = target.XMm - body.XMm, dz = target.ZMm - body.ZMm;
        if ((dx == 0 && dz == 0) || travel <= 0)
            return (body, false);
        bool landing = target == goal && dx * dx + dz * dz <= travel * travel;
        (int X, int Z) direction;
        if (landing)
        {
            direction = (DivRound(MoveIntent.FullDeflection * dx, travel), DivRound(MoveIntent.FullDeflection * dz, travel));
        }
        else
        {
            double length = Math.Sqrt((double)dx * dx + (double)dz * dz);
            direction = ((int)Math.Round(dx / length * MoveIntent.FullDeflection), (int)Math.Round(dz / length * MoveIntent.FullDeflection));
        }
        var intent = new MoveIntent(direction.X, direction.Z, Gait.Walk, CombatRules.FacingTowards(body.XMm, body.ZMm, target.XMm, target.ZMm));
        // She walks on the ground of the space she stands in, not the character's: an exterior villager is not lifted onto an interior's floor.
        return (Kinematics.Step(body, intent, _context.Setup.Movement, _context.Setup.LayoutFor(npc.Space).Space, Obstacles(npc), TickMs), landing);
    }

    private static int DivRound(long numerator, long denominator) =>
        (int)(numerator >= 0 ? (2 * numerator + denominator) / (2 * denominator) : -((-2 * numerator + denominator) / (2 * denominator)));

    /// <summary>
    /// What a walking NPC's body cannot pass: closed doors, standing barriers, creatures alive, the character, and other people - all in the
    /// space she stands in. Interiors have lives of their own now (the Barrowworks' watch), so the exterior is not the only floor walked.
    /// </summary>
    private List<Blocker> Obstacles(NpcState self)
    {
        long r = _context.Setup.Movement.BodyRadiusMm;
        var obstacles = new List<Blocker>(_context.ClosedDoors(self.Space));
        obstacles.AddRange(State.Creatures.Values.Where(c => c.Alive && c.Site.Space == self.Space)
            .Select(c => (Blocker)new CircleBlocker(c.Key, c.Body.XMm, c.Body.ZMm, c.Definition.RadiusMm, 0)));
        if (State.Space == self.Space)
            obstacles.Add(new CircleBlocker("player", State.Body.XMm, State.Body.ZMm, r, 0));
        obstacles.AddRange(State.Npcs.Values.Where(n => n.Definition.Id != self.Definition.Id && n.Space == self.Space)
            .Select(n => (Blocker)new CircleBlocker(n.Definition.Id, n.Body.XMm, n.Body.ZMm, r, 0)));
        return obstacles;
    }

    /// <summary>How close the character must press, beyond touching, for someone to make way: a hand's width.</summary>
    private const long PressMm = 120;

    /// <summary>
    /// Making way (WP12b): NPCs stay solid, but someone in the character's way - seated at a meal, idling, or on their way - steps aside
    /// rather than hold them. In the way: touching, and the character's next step would carry them farther were this NPC not there (a push
    /// straight on, or a slide along a wall that they block). They take one tick's walk across the way the character is going, to the side
    /// they already stand on (by key, dead ahead), else the other side, else straight on. Derived from where the bodies stand and the
    /// character's intent this tick - nothing to save - and they walk back to their place once the character has passed. False when nobody
    /// pressed.
    /// </summary>
    private bool StepAside(NpcState npc, NpcLifeRecord life)
    {
        var intent = _playerIntent();
        if (!intent.IsMoving || npc.Space != State.Space)
            return false;
        var player = State.Body;
        long r = _context.Setup.Movement.BodyRadiusMm;
        long dx = npc.Body.XMm - player.XMm, dz = npc.Body.ZMm - player.ZMm;
        long reach = 2 * r + PressMm;
        if (dx * dx + dz * dz > reach * reach || !InTheWay(npc.Definition.Id, intent))
            return false;
        double ix = intent.DirXPermille, iz = intent.DirZPermille, il = Math.Sqrt(ix * ix + iz * iz);
        double cross = ix * dz - iz * dx;
        int side = cross > 0 ? 1 : cross < 0 ? -1 : (KeyHash(life.Key) & 1) == 0 ? 1 : -1;
        var away = new (double X, double Z)[] { (-iz / il * side, ix / il * side), (iz / il * side, -ix / il * side), (ix / il, iz / il) };
        long travel = (long)_context.Setup.Movement.SpeedMmPerSecond(Gait.Walk) * TickMs / 1000;
        foreach (var (wx, wz) in away)
        {
            long tx = npc.Body.XMm + (long)Math.Round(wx * travel * 4), tz = npc.Body.ZMm + (long)Math.Round(wz * travel * 4);
            var (moved, _) = Walk(npc, new NavPoint(tx, tz), new NavPoint(long.MinValue, long.MinValue));
            if (moved.XMm == npc.Body.XMm && moved.ZMm == npc.Body.ZMm)
                continue;
            Write(npc, life, moved, NavRoute.None, 0);
            return true;
        }
        return false;
    }

    /// <summary>Whether the character's next step would carry them farther without this NPC: the step taken with and without their body.</summary>
    private bool InTheWay(string npcId, MoveIntent intent)
    {
        var from = State.Body;
        var space = _context.Layout.Space;
        var all = _context.Obstacles();
        var (with, _) = Kinematics.Step(from, State.Posture, intent, _context.Setup.Movement, space, all, TickMs);
        var (without, _) = Kinematics.Step(from, State.Posture, intent, _context.Setup.Movement, space, all.Where(b => b.Id != npcId).ToImmutableArray(), TickMs);
        long Gone(Body b) => (b.XMm - from.XMm) * (b.XMm - from.XMm) + (b.ZMm - from.ZMm) * (b.ZMm - from.ZMm);
        return Gone(without) > Gone(with) + 1;
    }

    private (long X, long Z) Home(NpcLifeRecord life)
    {
        var home = Life.Routines[life.RoutineId].Anchors[NpcAnchorKinds.Home];
        return (home.XMm, home.ZMm);
    }

    /// <summary>The body and the life, both written in the same tick when either changed: the life's pose is the body's.</summary>
    private void Write(NpcState npc, NpcLifeRecord life, Body body, NavRoute route, int stuckTicks)
    {
        Place(npc, body);
        var next = life with { XMm = body.XMm, ZMm = body.ZMm, FacingMdeg = body.FacingMdeg, Route = route, StuckTicks = stuckTicks };
        if (next.XMm != life.XMm || next.ZMm != life.ZMm)
            next = WithCell(next);
        SetLife(next);
    }
}
