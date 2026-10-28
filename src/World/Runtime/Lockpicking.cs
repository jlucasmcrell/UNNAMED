// UNNAMED World - picking a lock at run time: the picks in the works, and the roll when they have had their ticks (a timed player activity)
// No Godot references - pure C#

using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>
/// Work the lock on the door or container a key names, with the picks in the pack. Refused where there is no lock, where the lock is
/// already open, without picks, out of reach, dead, and while anything else is running.
/// </summary>
public sealed record PickLockCommand(EntityId Actor,string TargetKey) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

/// <summary>The picks are in the works: the attempt holds for its ticks, and the roll is made when they run out.</summary>
public sealed record LockAttemptStarted(string TargetKey,int Tier,long Tick);

/// <summary>It gave: the door or container is open from now on, for good.</summary>
public sealed record LockPicked(string TargetKey,long Tick);

/// <summary>It held. <see cref="Broke"/> is the picks' own failure: they bound and snapped, and left the pack with it.</summary>
public sealed record LockPickFailed(string TargetKey,bool Broke,long Tick);

// ── the system ──────────────────────────────────────────────────────────────

/// <summary>
/// Owns: <see cref="StateSlice.Activity"/> with fishing - one body is in one timed thing at a time, and a lock is the second of them. One
/// loop (SYSTEMS.md, interaction): E on a tiered door or container starts the attempt, which holds for <see cref="AttemptTicks"/>; when it
/// ends the roll is made on the attempt's own channel - the character's hand against the lock's tier - and it practises the skill either
/// way. A lock that gave is recorded in a world flag in the site's own cell, so it never holds again; a lock that held may take the picks
/// with it. Moving off, dying, a blow or a working, or leaving the space ends the attempt and grants nothing.
/// </summary>
internal sealed class LockpickSystem
{
    public const string Skill="skill.lockpicking",Picks="item.tool.lockpicks";

    /// <summary>How long one attempt takes: 2 s, in ticks.</summary>
    public const int AttemptTicks=40;

    /// <summary>One failed attempt in this many binds and snaps the picks: 15%.</summary>
    public const double BreakChance=0.06;   // a beginner should lose the lock, not the only picks they own

    private const double SampleScale=18446744073709551616.0;

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;

    public LockpickSystem(SystemContext context,SliceOwner owner,EntityId player)
    {
        _context=context;
        _owner=owner;
        _player=player;
    }

    private RuntimeState State => _context.State;

    /// <summary>Whether the character carries the picks a lock needs.</summary>
    public bool HasPicks => State.Inventory.Any(e=>e.DefId==Picks);

    /// <summary>
    /// A lock is a tier on a door or a container, in the space the character is in: its tier, and how far the body stands from it. Null
    /// for no such site, or one that is in another place - local metres overlap between spaces, so distance alone would reach through a hill.
    /// </summary>
    private (int Tier,double Distance)? Site(string key)
    {
        if(_context.Setup.Sites(l=>l.Doors).FirstOrDefault(p=>p.Site.Key==key) is { Site: { } door, Layout: var home })
            return home.SpaceId!=State.Space ? null
                : (door.LockTier,door.ClosedFootprint.DistanceTo(State.Body.XMm,State.Body.ZMm));
        if(_context.Setup.Sites(l=>l.Containers).FirstOrDefault(p=>p.Site.Key==key) is { Site: { } container, Layout: var containerHome })
            return containerHome.SpaceId!=State.Space ? null
                : (container.LockTier,Distance(container.XMm,container.ZMm));
        return null;
    }

    public string? Handle(PickLockCommand command,long tick)
    {
        if(command.Actor!=_player) return $"unknown actor {command.Actor}";
        if(State.PlayerCombat.Defeated) return "dead";
        if(Site(command.TargetKey) is not { } site) return $"{command.TargetKey} is not in this place";
        if(site.Tier<=0) return "There is no lock on that";
        if(!_context.Locked(command.TargetKey)) return "The lock is already open";
        if(State.Activity is not null || Busy(tick)) return "You are already doing something";
        if(!HasPicks) return "You have no picks";
        long reach=_context.Setup.Movement.InteractReachMm;
        if(site.Distance>reach) return $"{command.TargetKey} is {site.Distance/1000:0.00} m away; reach is {reach/1000.0:0.00} m";
        State.SetActivity(_owner,new PlayerActivity(ActivityKind.PickLock,command.TargetKey,tick,tick+AttemptTicks,0));
        _context.Events.Publish(new LockAttemptStarted(command.TargetKey,site.Tier,tick));
        return null;
    }

    /// <summary>Cancel ends a pick cleanly, and nothing is granted: the same command that brings a line in.</summary>
    public string? Handle(CancelActivityCommand command)
    {
        if(command.Actor!=_player) return $"unknown actor {command.Actor}";
        if(State.Activity is { Kind: ActivityKind.PickLock })
            State.SetActivity(_owner,null);
        return null;
    }

    public void Tick(long tick)
    {
        if(State.Activity is not { Kind: ActivityKind.PickLock } activity) return;
        // The breath, the fight and the place: the same reasons a line comes in end the attempt with nothing granted.
        if(State.PlayerCombat.Defeated || Busy(tick) || Site(activity.TargetKey) is not { } site || site.Distance>_context.Setup.Movement.InteractReachMm)
        {
            State.SetActivity(_owner,null);
            return;
        }
        if(tick<activity.UntilTick) return;
        State.SetActivity(_owner,null);
        Resolve(activity.TargetKey,site.Tier,activity.StartedTick,tick);
    }

    /// <summary>
    /// The attempt is over: one roll on the attempt's own channel - the key and the tick it began, never call order - and the skill is
    /// practised either way, so the failure is worth what the success is. A lock that gave is opened for good; one that held may snap the
    /// picks, on the same channel under its own semantic key, and the line that says so names them gone.
    /// </summary>
    private void Resolve(string key,int tier,long startedTick,long tick)
    {
        bool picked=Turns(State.World.WorldSeed,_context.CellOf(State.Body),key,startedTick,
            ProgressionEngine.SkillLevel(State.Progression,Skill),tier);
        _context.Dispatch(new PracticeSkill(new SkillPractice(Skill,tier*10,picked ? PracticeOutcome.Success : PracticeOutcome.Failure,tick)
        {
            NoveltyKey = key,
        }));
        if(picked)
        {
            Open(key,tick);
            _context.Events.Publish(new LockPicked(key,tick));
            return;
        }
        bool broke=Snaps(State.World.WorldSeed,_context.CellOf(State.Body),key,startedTick);
        if(broke)
            _context.Dispatch(new ConsumeItem(Picks,1));
        _context.Events.Publish(new LockPickFailed(key,broke,tick));
    }

    /// <summary>The attempt's roll: the hand against the tier, on the key and the tick it began, never on call order.</summary>
    internal static bool Turns(ulong worldSeed,CellKey cell,string key,long startedTick,int skillLevel,int tier) =>
        RngChannel.Open(worldSeed,cell,"lockpicking",$"{key}@pick@{startedTick}").UInt64(0)/SampleScale<LockRules.Chance(skillLevel,tier);

    /// <summary>The picks' own roll, on the same channel under its own semantic key: a failed turn binds and snaps them now and then.</summary>
    internal static bool Snaps(ulong worldSeed,CellKey cell,string key,long startedTick) =>
        RngChannel.Open(worldSeed,cell,"lockpicking",$"{key}@break@{startedTick}").UInt64(0)/SampleScale<BreakChance;

    /// <summary>
    /// For good: the site's own cell records the pick in the world flag named from its key, so no load ever re-locks it. A door's open
    /// flag is written with it - that flag is what the door already reads as open - and the door is reported opened, as its handle does.
    /// </summary>
    private void Open(string key,long tick)
    {
        string picked=LockRules.FlagOf(key);
        if(_context.Setup.Sites(l=>l.Doors).FirstOrDefault(p=>p.Site.Key==key) is { Site: { } door, Layout: var home })
        {
            var cell=Simulation.CellOf(home.SpaceId,door);
            _context.Dispatch(new SetWorldFlag(cell,picked,1));
            _context.Dispatch(new SetWorldFlag(cell,door.FlagId,1));
            _context.Events.Publish(new DoorToggled(_player,door.Key,true,tick));
            return;
        }
        if(_context.Setup.Sites(l=>l.Containers).FirstOrDefault(p=>p.Site.Key==key) is { Site: { } container, Layout: var containerHome })
            _context.Dispatch(new SetWorldFlag(_context.CellOfSite(containerHome.SpaceId,container.XMm,container.ZMm),picked,1));
    }

    private double Distance(long xMm,long zMm) =>
        Math.Sqrt(Math.Pow(xMm-State.Body.XMm,2)+Math.Pow(zMm-State.Body.ZMm,2));

    /// <summary>Busy with a blow, a dodge, a stagger or a working: nothing time-shaped is started or kept through one.</summary>
    private bool Busy(long tick) =>
        State.PlayerCombat.Action.PhaseAt(tick,_context.Setup.Combat.Constants).Phase!=CombatPhase.Idle;
}
