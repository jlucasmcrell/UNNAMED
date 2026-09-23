// UNNAMED World - the Phase-1 runtime systems (SYSTEMS.md S-04, S-08, S-20, S-21, S-22, S-30)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// Each system declares the state it owns in its header, writes only that through its SliceOwner, and reaches
// another system's state only by dispatching an internal command (ARCHITECTURE.md §4.2, §5).

/// <summary>A command one system sends another. Presentation cannot construct these.</summary>
internal abstract record InternalCommand;

/// <summary>To <see cref="WorldFlagSystem"/>: set a cell's world flag.</summary>
internal sealed record SetWorldFlag(CellKey Cell, string FlagId, long Value) : InternalCommand;

/// <summary>To <see cref="ProgressionSystem"/>: award level XP.</summary>
internal sealed record AwardExperience(XpAward Award) : InternalCommand;

/// <summary>What systems share: the state, the rules, the event bus, and the internal command route.</summary>
internal sealed class SystemContext
{
    public SystemContext(RuntimeState state, SimulationSetup setup, IEventBus events, Func<InternalCommand, string?> dispatch)
    {
        State = state;
        Setup = setup;
        Events = events;
        Dispatch = dispatch;
    }

    public RuntimeState State { get; }
    public SimulationSetup Setup { get; }
    public IEventBus Events { get; }

    /// <summary>Route an internal command to its owner; null when accepted, otherwise the reason.</summary>
    public Func<InternalCommand, string?> Dispatch { get; }

    public bool IsOpen(DoorSite door) => State.World.GetFlag(Simulation.CellOf(door), door.FlagId) != 0;

    public ImmutableArray<Blocker> ClosedDoors() => Setup.Layout.ClosedDoors(IsOpen);

    /// <summary>What the player's body cannot pass besides the static blockers: closed doors and living creatures.</summary>
    public ImmutableArray<Blocker> Obstacles() =>
        ClosedDoors().AddRange(State.Creatures.Values.Where(c => c.Alive)
            .Select(c => (Blocker)new CircleBlocker(c.Key, c.Body.XMm, c.Body.ZMm, c.Definition.RadiusMm, 0)));
}

/// <summary>Owns: <see cref="StateSlice.Clock"/>. Advances <c>world_tick</c>, the only clock (S-04).</summary>
internal sealed class ClockSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;

    public ClockSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }

    public void Tick() => _context.State.AdvanceClock(_owner);
}

/// <summary>
/// Owns: <see cref="StateSlice.PlayerBody"/>. Integrates the player's movement intent every tick with
/// <see cref="Kinematics.Step"/>. The intent itself is transient input state and is never saved.
/// </summary>
internal sealed class MovementSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;

    public MovementSystem(SystemContext context, SliceOwner owner, EntityId player)
    {
        _context = context;
        _owner = owner;
        _player = player;
        Intent = MoveIntent.Idle(context.State.Body.FacingMdeg);
    }

    public MoveIntent Intent { get; private set; }

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
        var combat = _context.State.PlayerCombat;
        var constants = _context.Setup.Combat.Constants;
        var rules = _context.Setup.Movement;
        var intent = Intent;
        var (phase, _) = combat.Action.PhaseAt(tick, constants);
        if (combat.Defeated || phase == CombatPhase.Staggered || (phase == CombatPhase.Recovery && combat.Action.Kind == ActionKind.Dodge))
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

        var to = Kinematics.Step(from, intent, rules, _context.Setup.Layout.Space, _context.Obstacles(), _context.Setup.TickMilliseconds);
        if (to == from)
            return;
        _context.State.SetBody(_owner, to);
        _context.Events.Publish(new BodyMoved(_player, from, to, tick));
    }

    public string? Handle(Relocate command, long tick)
    {
        var from = _context.State.Body;
        _context.State.SetBody(_owner, command.Body);
        Intent = MoveIntent.Idle(command.Body.FacingMdeg);
        _context.Events.Publish(new BodyMoved(_player, from, command.Body, tick));
        return null;
    }
}

/// <summary>
/// Owns no state. Validates an interaction against the actor's authoritative body - never the camera - and asks
/// the owner of what changes to change it. In Phase 1 the interactables are doors.
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
        var door = _context.Setup.Layout.FindDoor(command.TargetKey);
        if (door is null)
            return $"nothing to interact with called '{command.TargetKey}'";
        var body = _context.State.Body;
        var rules = _context.Setup.Movement;
        double distance = door.ClosedFootprint.DistanceTo(body.XMm, body.ZMm);
        if (distance > rules.InteractReachMm)
            return $"{door.Key} is {distance / 1000:0.00} m away; reach is {rules.InteractReachMm / 1000.0:0.00} m";

        bool open = _context.IsOpen(door);
        if (open && door.ClosedFootprint.Separation(body.XMm, body.ZMm, rules.BodyRadiusMm) is not null)
            return $"{door.Key} cannot close: something is in the doorway";
        if (_context.Dispatch(new SetWorldFlag(Simulation.CellOf(door), door.FlagId, open ? 0 : 1)) is { } refused)
            return refused;
        _context.Events.Publish(new DoorToggled(_player, door.Key, !open, tick));
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

    public string? Handle(SetWorldFlag command, long tick)
    {
        long from = _context.State.World.GetFlag(command.Cell, command.FlagId);
        if (from == command.Value)
            return null;
        _context.State.SetFlag(_owner, command.Cell, command.FlagId, command.Value);
        _context.Events.Publish(new WorldFlagChanged(command.Cell.ToString(), command.FlagId, from, command.Value, tick));
        return null;
    }
}

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
        _context.Events.Publish(new ExperienceGained(command.Award.Source, result.Awarded, result.Repaid, result.LevelsGained,
            result.Progression.Level, command.Award.Tick));
        return null;
    }

    /// <summary>The current pools move, clamped to their derived maxima; a full pool is stored as full (null), never as a number.</summary>
    public string? Handle(ChangePools command)
    {
        var progression = _context.State.Progression;
        var stats = ProgressionEngine.Derive(progression, _context.Setup.Progression);
        int maxHealth = (int)stats.HealthMax, maxStamina = (int)stats.StaminaMax;
        int health = Math.Clamp((progression.Pools.Health ?? maxHealth) + command.Health, 0, maxHealth);
        int stamina = Math.Clamp((progression.Pools.Stamina ?? maxStamina) + command.Stamina, 0, maxStamina);
        var pools = progression.Pools with { Health = health >= maxHealth ? null : health, Stamina = stamina >= maxStamina ? null : stamina };
        if (pools != progression.Pools)
            _context.State.SetProgression(_owner, progression with { Pools = pools });
        return null;
    }

    public string? Handle(PracticeSkill command)
    {
        var result = ProgressionEngine.Practice(_context.State.Progression, command.Practice, _context.Setup.Progression);
        _context.State.SetProgression(_owner, result.Progression);
        _context.Events.Publish(new SkillPracticed(command.Practice.SkillId, result.XpGained,
            ProgressionEngine.SkillLevel(result.Progression, command.Practice.SkillId), command.Practice.Tick));
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
/// discovers it, once, and earns its discovery XP (PROGRESSION.md §3.3 <c>discovery</c>). Only places in tier-A
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

    public void Tick(long tick)
    {
        var state = _context.State;
        foreach (var place in _context.Setup.Layout.Locations)
        {
            if (state.Discoveries.ContainsKey(place.Id))
                continue;
            string cell = CellKey.OfWorld(place.XMm / 1000.0, place.ZMm / 1000.0).ToString();
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
    }
}

/// <summary>
/// Owns: <see cref="StateSlice.CellTiers"/>. Assigns every cell of the region a simulation tier from its distance
/// to the player (S-21, D-06), one step per tick with hysteresis. Tier A cells are fully simulated and tier D
/// cells are stored state only; tiers B and C have no simulation yet (<see cref="ITierSimulation"/>).
/// </summary>
internal sealed class TierSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly ImmutableArray<CellKey> _cells;

    public TierSystem(SystemContext context, SliceOwner owner, ImmutableArray<CellKey> cells)
    {
        _context = context;
        _owner = owner;
        _cells = cells;
    }

    /// <summary>A hard boundary (a load, a new game): each cell settles directly, with no events (WORLD_ARCHITECTURE.md §5.1).</summary>
    public void Settle()
    {
        foreach (var cell in _cells)
        {
            var tier = SimulationTier.D;
            for (int step = 0; step < 3; step++)
                tier = _context.Setup.Tiers.Next(tier, DistanceTo(cell));
            _context.State.SetTier(_owner, cell.ToString(), tier);
        }
    }

    public void Tick(long tick)
    {
        foreach (var cell in _cells)
        {
            string key = cell.ToString();
            var from = _context.State.Tiers.GetValueOrDefault(key, SimulationTier.D);
            var to = _context.Setup.Tiers.Next(from, DistanceTo(cell));
            if (to == from)
                continue;
            _context.State.SetTier(_owner, key, to);
            _context.Events.Publish(new CellTierChanged(key, from, to, tick));
        }
    }

    private long DistanceTo(CellKey cell)
    {
        long minX = ((long)cell.Region.Rx * WorldMath.RegionSizeMeters + (long)cell.Cx * WorldMath.CellSizeMeters) * 1000;
        long minZ = ((long)cell.Region.Rz * WorldMath.RegionSizeMeters + (long)cell.Cz * WorldMath.CellSizeMeters) * 1000;
        long size = WorldMath.CellSizeMeters * 1000L;
        var body = _context.State.Body;
        long dx = body.XMm < minX ? minX - body.XMm : body.XMm > minX + size ? body.XMm - (minX + size) : 0;
        long dz = body.ZMm < minZ ? minZ - body.ZMm : body.ZMm > minZ + size ? body.ZMm - (minZ + size) : 0;
        return (long)Math.Sqrt((double)dx * dx + (double)dz * dz);
    }
}

/// <summary>
/// What a tier runs for the entities in its cells. Phase 1 has no simulated entities yet (creatures arrive in M3d,
/// NPCs in M4), so tier A's work is the systems above and D runs nothing. B and C are declared, not built: the
/// interface is where M4's regional and abstract simulation plug in (ROADMAP M3: "tier B/C stubbed behind the
/// interface").
/// </summary>
internal interface ITierSimulation
{
    SimulationTier Tier { get; }

    void Tick(long tick, IReadOnlyList<CellKey> cells);
}

internal sealed class StubTierSimulation : ITierSimulation
{
    public StubTierSimulation(SimulationTier tier) => Tier = tier;

    public SimulationTier Tier { get; }

    public void Tick(long tick, IReadOnlyList<CellKey> cells)
    {
        // Intentionally empty until M4 (B: coarse movement on routes; C: schedule bands and economy).
    }
}
