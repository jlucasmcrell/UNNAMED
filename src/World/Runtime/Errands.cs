// UNNAMED World - errands: a named NPC walking to a player's bench, working there, and walking home (M7 design §3.12, §4.15)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// ── commands, events and views ──────────────────────────────────────────────

/// <summary>To <see cref="NpcSystem"/>, from <see cref="BuildingSystem"/> once an assignment passes: the NPC sets off to work at a station.</summary>
internal sealed record BeginWork(string NpcId, EntityId PieceId, EntityId Owner, long AnchorXMm, long AnchorZMm, int FacingMdeg) : InternalCommand;

/// <summary>
/// To <see cref="NpcSystem"/>, from <see cref="BuildingSystem"/>: the NPC's work is over and they walk home. <see cref="Reason"/> is
/// <c>released</c>, <c>dismantled</c> or <c>destroyed</c>.
/// </summary>
internal sealed record EndWork(string NpcId, string Reason) : InternalCommand;

/// <summary>An NPC was asked to work at a station, and sets off for its work anchor.</summary>
public sealed record WorkerAssigned(string NpcId, EntityId PieceId, long AnchorXMm, long AnchorZMm, int FacingMdeg, long Tick);

/// <summary>An NPC's work at a station is over (<c>released</c>, <c>dismantled</c> or <c>destroyed</c>); they walk home.</summary>
public sealed record WorkerReleased(string NpcId, EntityId PieceId, string Reason, long Tick);

/// <summary>An NPC stands exactly at the work anchor, facing the work.</summary>
public sealed record NpcArrivedAtWork(string NpcId, EntityId PieceId, long Tick);

/// <summary>An NPC stands exactly at their place again, facing its way: the errand is over and they are baseline again.</summary>
public sealed record NpcReturnedHome(string NpcId, long Tick);

/// <summary>An errand as the game shows it: where the NPC is headed and how they will face there; walking home, their place and no piece.</summary>
public sealed record WorkAssignmentView(string NpcId, EntityId? PieceId, long AnchorXMm, long AnchorZMm, int FacingMdeg, NpcErrandPhase Phase);

/// <summary>Where a station's worker stands and which way they face: the piece's anchor plus its turned work anchor, and the turned facing.</summary>
internal static class WorkAnchors
{
    public static (long XMm, long ZMm, int FacingMdeg) Of(PieceStation station, PieceRecord row)
    {
        var (dx, dz) = QuarterTurn.Apply(station.AnchorXMm, station.AnchorZMm, row.Rotation);
        return (row.XMm + dx, row.ZMm + dz, (int)(((long)station.FacingMdeg + row.Rotation * 90_000L) % 360_000));
    }
}

// ── the mover ───────────────────────────────────────────────────────────────

internal sealed partial class NpcSystem
{
    private static readonly NavAgent Person = new(0, true);

    /// <summary>Headway: a step of at least 30% of a walk's travel (24 of 80 mm), else the tick counts as stuck.</summary>
    private const long HeadwayMm = 24;

    private int TickMs => _context.Setup.TickMilliseconds;

    public string? Handle(BeginWork command, long tick)
    {
        if (!State.Npcs.TryGetValue(command.NpcId, out var npc))
            return $"there is no one called {command.NpcId} here";
        var body = npc.Body;
        State.SetNpcErrand(_owner, new NpcErrandRecord(command.NpcId, HostOf(npc.Site), NpcErrandPhase.ToWork, command.PieceId, command.Owner,
            body.XMm, body.ZMm, body.FacingMdeg));
        _context.Events.Publish(new WorkerAssigned(command.NpcId, command.PieceId, command.AnchorXMm, command.AnchorZMm, command.FacingMdeg, tick));
        return null;
    }

    public string? Handle(EndWork command, long tick)
    {
        if (State.World.NpcErrand(command.NpcId) is not { PieceId: { } piece } errand)
            return $"{command.NpcId} works nowhere";
        State.SetNpcErrand(_owner, errand with { Phase = NpcErrandPhase.ToHome, PieceId = null, Route = NavRoute.None, StuckTicks = 0 });
        _context.Events.Publish(new WorkerReleased(command.NpcId, piece, command.Reason, tick));
        return null;
    }

    /// <summary>
    /// One tick of every errand, in NPC ID order (M7 design §3.12). No tier gate - tiers are unsaved - and no read of the conversation:
    /// what an errand does next depends only on what is saved, the grid, the gates, the other bodies and the tick. The NPC lands exactly
    /// on the goal and turns exactly to its facing before arriving or retiring, and is never placed: only walked, a tick at a time.
    /// </summary>
    private void TickErrands(long tick)
    {
        foreach (var errand in State.World.NpcErrands)
        {
            if (!State.Npcs.TryGetValue(errand.NpcId, out var npc))
                continue;
            var (goalX, goalZ, goalFacing) = Goal(errand, npc);
            var body = npc.Body;
            if (errand.Phase == NpcErrandPhase.AtWork)
            {
                Write(npc, errand, Turn(body, goalFacing), errand.Route, errand.StuckTicks);
                continue;
            }
            if (body.XMm == goalX && body.ZMm == goalZ)
            {
                var turned = Turn(body, goalFacing);
                if (turned.FacingMdeg != goalFacing)
                {
                    Write(npc, errand, turned, errand.Route, errand.StuckTicks);
                }
                else if (errand.Phase == NpcErrandPhase.ToWork)
                {
                    Write(npc, errand with { Phase = NpcErrandPhase.AtWork }, turned, NavRoute.None, 0);
                    _context.Events.Publish(new NpcArrivedAtWork(errand.NpcId, errand.PieceId!, tick));
                }
                else
                {
                    Place(npc, turned);
                    State.RemoveNpcErrand(_owner, errand.NpcId);
                    _context.Events.Publish(new NpcReturnedHome(errand.NpcId, tick));
                }
                continue;
            }

            var goal = new NavPoint(goalX, goalZ);
            var step = _navigation.Follow(Person, errand.Route, new NavPoint(body.XMm, body.ZMm), goal, errand.StuckTicks, tick);
            if (step.ReplanReason is { } why)
                _context.Events.Publish(new RoutePlanned(errand.NpcId, NavSearch.OutcomeKey(step.Outcome!.Value), why, step.Route.Corners.Length, step.Expansions, tick));
            switch (step.Kind)
            {
                case NavStepKind.Unreachable:
                    Write(npc, errand, Turn(body, CombatRules.FacingTowards(body.XMm, body.ZMm, goalX, goalZ)), step.Route, errand.StuckTicks + 1);
                    break;
                case NavStepKind.OpenGate:
                    // A door opened costs no headway; one refused counts as stuck, so the stuck replans and "blocked" still come (G26).
                    bool refused = _context.Dispatch(new OpenDoor(step.GateKey!, errand.NpcId)) is not null;
                    npc = State.Npcs[errand.NpcId];
                    Write(npc, errand, Turn(npc.Body, CombatRules.FacingTowards(body.XMm, body.ZMm, step.Target.XMm, step.Target.ZMm)), step.Route,
                        refused ? errand.StuckTicks + 1 : errand.StuckTicks);
                    break;
                default:
                    var (moved, landing) = Walk(npc, step.Target, goal);
                    long dx = moved.XMm - body.XMm, dz = moved.ZMm - body.ZMm;
                    bool headway = landing || dx * dx + dz * dz >= HeadwayMm * HeadwayMm;
                    Write(npc, errand, moved, step.Route, headway ? 0 : errand.StuckTicks + 1);
                    break;
            }
        }
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
        if (dx == 0 && dz == 0)
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
        return (Kinematics.Step(body, intent, _context.Setup.Movement, _context.Space, _context.PersonObstacles(npc.Definition.Id), TickMs), landing);
    }

    private static int DivRound(long numerator, long denominator) =>
        (int)(numerator >= 0 ? (2 * numerator + denominator) / (2 * denominator) : -((-2 * numerator + denominator) / (2 * denominator)));

    /// <summary>
    /// Where an errand is headed: to work or at work, its station's work anchor and facing (from the row and its definition); walking home,
    /// the NPC's place and its facing. Derived, never stored.
    /// </summary>
    private (long X, long Z, int Facing) Goal(NpcErrandRecord errand, NpcState npc)
    {
        if (errand.PieceId is { } id && State.World.Piece(id) is { } row && _context.Setup.Building.Catalog.Find(row.DefId)?.Station is { } station)
            return WorkAnchors.Of(station, row);
        return (npc.Site.XMm, npc.Site.ZMm, npc.Site.FacingMdeg);
    }

    /// <summary>The body and the errand, both written in the same tick when either changed (G18: the errand's pose is the body's).</summary>
    private void Write(NpcState npc, NpcErrandRecord errand, Body body, NavRoute route, int stuckTicks)
    {
        Place(npc, body);
        var next = errand with { XMm = body.XMm, ZMm = body.ZMm, FacingMdeg = body.FacingMdeg, Route = route, StuckTicks = stuckTicks };
        if (next != errand)
            State.SetNpcErrand(_owner, next);
    }

    private void Place(NpcState npc, Body body)
    {
        if (body != npc.Body)
            State.SetNpc(_owner, npc with { Body = body });
    }

    /// <summary>The body turned towards a facing by at most 18 degrees a tick, as NPCs turn.</summary>
    private static Body Turn(Body body, int towardMdeg)
    {
        int delta = (int)(((long)towardMdeg - body.FacingMdeg + 540_000) % 360_000) - 180_000;
        if (delta == 0)
            return body;
        return body with { FacingMdeg = (int)(((long)body.FacingMdeg + Math.Clamp(delta, -TurnMdegPerTick, TurnMdegPerTick) + 360_000) % 360_000) };
    }

    private static string HostOf(NpcSite site) => CellKey.OfWorld(site.XMm / 1000.0, site.ZMm / 1000.0).ToString();

    /// <summary>
    /// World start (M7 design §4.15): each saved errand is checked and repaired before its NPC is placed at the errand's pose, each repair
    /// with an audit line. An errand of an NPC saved as a companion is dropped (never both, G23), as is one of an NPC with no place here; one
    /// whose NPC's place moved to another cell is re-hosted; one whose station is no longer an intact station walks home; one at work but
    /// not at the anchor walks to it. Publishes nothing.
    /// </summary>
    private void RepairErrands(ImmutableArray<CompanionRecord> companions)
    {
        var lines = new List<StructureConflict>();
        var sites = _context.Setup.Layout.Npcs.ToDictionary(n => n.NpcId, n => n, StringComparer.Ordinal);
        foreach (var errand in State.World.NpcErrands)
        {
            string subject = $"npc errand {errand.NpcId}";
            if (companions.Any(c => c.NpcId == errand.NpcId))
            {
                State.RemoveNpcErrand(_owner, errand.NpcId);
                lines.Add(new StructureConflict(subject, "they travel with you: the errand is dropped"));
                continue;
            }
            if (!sites.TryGetValue(errand.NpcId, out var site) || !_context.Setup.Social.Npcs.ContainsKey(errand.NpcId))
            {
                State.RemoveNpcErrand(_owner, errand.NpcId);
                lines.Add(new StructureConflict(subject, "they have no place in this region: the errand is dropped"));
                continue;
            }
            var repaired = errand;
            if (repaired.HostCell != HostOf(site))
            {
                repaired = repaired with { HostCell = HostOf(site), BaselineHash = null };
                lines.Add(new StructureConflict(subject, $"their place is now in {HostOf(site)}: the errand moves there"));
            }
            if (repaired.PieceId is { } id
                && (State.World.Piece(id) is not { } row || _context.Setup.Building.Catalog.Find(row.DefId) is not { Family: PieceFamily.Station, Station: not null }))
            {
                repaired = repaired with { Phase = NpcErrandPhase.ToHome, PieceId = null, Route = NavRoute.None, StuckTicks = 0 };
                lines.Add(new StructureConflict(subject, $"their work place {id.Value} is no station now: they walk home"));
            }
            if (repaired.Phase == NpcErrandPhase.AtWork && State.World.Piece(repaired.PieceId!) is { } at
                && _context.Setup.Building.Catalog.Find(at.DefId)?.Station is { } station
                && WorkAnchors.Of(station, at) != (repaired.XMm, repaired.ZMm, repaired.FacingMdeg))
            {
                repaired = repaired with { Phase = NpcErrandPhase.ToWork };
                lines.Add(new StructureConflict(subject, "they are not at their work place: they walk to it"));
            }
            if (repaired != errand)
                State.SetNpcErrand(_owner, repaired);
        }
        State.SetErrandAudit(_owner, lines.ToImmutableArray());
    }

    /// <summary>Every errand, by NPC ID: where the NPC is headed and how they will face there.</summary>
    public ImmutableArray<WorkAssignmentView> WorkAssignments() =>
        State.World.NpcErrands.Where(e => State.Npcs.ContainsKey(e.NpcId)).Select(e =>
        {
            var (x, z, facing) = Goal(e, State.Npcs[e.NpcId]);
            return new WorkAssignmentView(e.NpcId, e.PieceId, x, z, facing, e.Phase);
        }).ToImmutableArray();
}
