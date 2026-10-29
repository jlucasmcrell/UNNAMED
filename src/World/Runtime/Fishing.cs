// UNNAMED World - fishing at run time: a cast, a wait, and a reel (a timed player activity)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>Put a line into a declared water within reach. Refused away from the bank, and while anything else is running.</summary>
public sealed record StartFishingCommand(EntityId Actor,string WaterKey) : GameCommand(Actor);

/// <summary>Reel in: an empty line before the bite, a catch in the window, and nothing at all once the fish has gone.</summary>
public sealed record ReelInCommand(EntityId Actor) : GameCommand(Actor);

/// <summary>Stop whatever timed activity the character is in the middle of, cleanly. Nothing happens when there is none.</summary>
public sealed record CancelActivityCommand(EntityId Actor) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

/// <summary>A line went out: the cast plays, and the HUD tells it.</summary>
public sealed record FishCastStarted(string WaterKey,long Tick);

/// <summary>The cast landed and the line is in the water, waiting. Nothing is caught by waiting.</summary>
public sealed record FishingLineOut(string WaterKey,long Tick);

/// <summary>Something was taken: which item, how many, and from which water - the toast and any sound hang on this.</summary>
public sealed record FishCaught(string WaterKey,string ItemId,int Count,long Tick);

/// <summary>The line came back with nothing, <see cref="Reason"/> in the words the HUD shows ("Nothing has taken it", "It has gone").</summary>
public sealed record FishLost(string WaterKey,string Reason,long Tick);

// ── the system ──────────────────────────────────────────────────────────────

/// <summary>
/// Owns: <see cref="StateSlice.Activity"/> - the one timed thing the character is doing, which is fishing here and a lockpicking or a
/// swing at a node in their own systems. Transient: a cast interrupted by a save is dropped, not saved, because a line in the water
/// is a moment and not a possession.
///
/// One loop (SYSTEMS.md, fishing): F casts, and the cast plays for <see cref="CastTicks"/>; when it lands the bite is set three to nine
/// seconds out from the cast's own random channel, and the line waits as long as the player leaves it. A reel in the window takes the
/// water's catch table, awards the skill and plays the reel; a reel early or late comes back empty. Moving off the bank, dying, starting
/// a blow or a working, or leaving the exterior ends it.
/// </summary>
internal sealed class FishingSystem
{
    public const string Skill = "skill.fishing";
    public const int CastTicks = 50, ReelTicks = 40;

    /// <summary>How long the fish is on: 5 s (100 ticks) from the bite, with 0.3 s (6 ticks) of grace before it while a hand is quick.</summary>
    public const int BiteWindowTicks = 100, BiteGraceTicks = 6;

    /// <summary>How long after the cast lands a bite may come: 3 s to 9 s, in ticks.</summary>
    public const int BiteMinTicks = 60, BiteMaxTicks = 180;

    private const double SampleScale = 18446744073709551616.0;

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;

    public FishingSystem(SystemContext context,SliceOwner owner,EntityId player)
    {
        _context=context;
        _owner=owner;
        _player=player;
    }

    private RuntimeState State => _context.State;
    private FishingRules Rules => _context.Setup.Fishing;
    private WorldClock Clock => _context.Setup.Clock;

    /// <summary>The bait scale of the cast in the water now.</summary>
    private double? _baitScale;

    /// <summary>True when the pack holds <paramref name="definitionId"/> in any stack, placed or not.</summary>
    private bool Carries(string definitionId) =>
        State.Inventory.Any(entry => entry.DefId == definitionId && entry.Count > 0);

    /// <summary>Spend one bait and return what its kind is worth to the wait; null when none is carried.</summary>
    private double? TakeBait()
    {
        foreach (var entry in State.Inventory)
            if (entry.Count > 0 && Rules.Baits.TryGetValue(entry.DefId, out var bait))
                {
                    _context.Dispatch(new ExchangeItems(ImmutableArray.Create(new StackTake(entry.ItemId, 1)), null, 0, Quality.Standard));
                    return bait.BiteScale;
                }
        return null;
    }
    private WalkSpace Space => _context.Setup.Layout.Space;

    /// <summary>The difficulty a water's practice is worth: a stream teaches least, a river or a lake most.</summary>
    public static int Difficulty(string kind) => kind switch
    {
        "stream" => 6,
        "creek" => 8,
        "pond" => 10,
        "river" or "lake" => 12,
        _ => 8,
    };

    public string? Handle(StartFishingCommand command,long tick)
    {
        if(command.Actor!=_player) return $"unknown actor {command.Actor}";
        if(State.PlayerCombat.Defeated) return "dead";
        if(!State.Space.IsExterior) return "Stand at the water's edge";
        if(State.Activity is not null || Busy(tick)) return "You are already doing something";
        if(Space.Waters.FirstOrDefault(w=>w.Key==command.WaterKey) is not { CatchTableId: not null } water) return "There is nothing to fish for here";
        if(Rules.RodItemId is { } rod && !Carries(rod)) return "You need a rod";
        var body=State.Body;
        if(Space.WaterAt(body.XMm,body.ZMm) is not { } at || at.Key!=water.Key) return "Stand at the water's edge";
        _baitScale=TakeBait();
        State.SetActivity(_owner,new PlayerActivity(ActivityKind.FishCast,water.Key,tick,tick+CastTicks,0));
        _context.Events.Publish(new FishCastStarted(water.Key,tick));
        return null;
    }

    public string? Handle(ReelInCommand command,long tick)
    {
        if(command.Actor!=_player) return $"unknown actor {command.Actor}";
        if(State.Activity is not { Kind: ActivityKind.FishWaiting } activity) return null;
        if(tick<activity.BiteTick-BiteGraceTicks)
        {
            End(activity.TargetKey,"Nothing has taken it",tick);
            return null;
        }
        if(tick>activity.BiteTick+BiteWindowTicks)
        {
            End(activity.TargetKey,"It has gone",tick);
            return null;
        }
        Catch(activity,tick);
        return null;
    }

    public string? Handle(CancelActivityCommand command)
    {
        if(command.Actor!=_player) return $"unknown actor {command.Actor}";
        State.SetActivity(_owner,null);
        return null;
    }

    public void Tick(long tick)
    {
        if(State.Activity is not { } activity) return;
        // The record is shared with every timed thing the body can be in (a pick working a lock): a line's ends are a line's own, so
        // another system's activity is not measured against this bank or broken off by this fight.
        if(activity.Kind is not (ActivityKind.FishCast or ActivityKind.FishWaiting or ActivityKind.FishReel)) return;
        // The bank, the breath and the fight: either is reason enough for the line to come in.
        if(State.PlayerCombat.Defeated || !State.Space.IsExterior || Busy(tick)
            || Space.WaterAt(State.Body.XMm,State.Body.ZMm)?.Key!=activity.TargetKey)
        {
            State.SetActivity(_owner,null);
            return;
        }
        switch(activity.Kind)
        {
            case ActivityKind.FishCast when tick>=activity.UntilTick:
            {
                // The cast lands, and the bite is set now - three to nine seconds out - from the cast's own channel, so the same cast
                // of the same water in the same world is always the same fish.
                var channel=RngChannel.Open(State.World.WorldSeed,_context.CellOf(State.Body),"fishing",$"{activity.TargetKey}@cast@{activity.StartedTick}");
                int wait=channel.Int(0,BiteMinTicks,BiteMaxTicks+1);
                if(_baitScale is { } scale) wait=Math.Max(BiteMinTicks/2,(int)Math.Round(wait*scale));
                State.SetActivity(_owner,new PlayerActivity(ActivityKind.FishWaiting,activity.TargetKey,tick,0,tick+wait));
                _context.Events.Publish(new FishingLineOut(activity.TargetKey,tick));
                break;
            }
            case ActivityKind.FishReel when tick>=activity.UntilTick:
                State.SetActivity(_owner,null);
                break;
        }
    }

    /// <summary>
    /// A fish is on: what is granted is granted at the start of the reel, so the toast, the item and the animation land together.
    /// The catch is the water's table, rolled on the water and the cast - never on call order - and it trains the skill.
    /// </summary>
    private void Catch(PlayerActivity activity,long tick)
    {
        State.SetActivity(_owner,new PlayerActivity(ActivityKind.FishReel,activity.TargetKey,tick,tick+ReelTicks,0));
        if(Space.Waters.FirstOrDefault(w=>w.Key==activity.TargetKey) is not { } water)
            return;
        double hour=Clock.Hour(activity.StartedTick)%Clock.HoursPerDay;
        string tableId=Rules.TableFor(water.Kind,hour) ?? water.CatchTableId ?? string.Empty;
        if(!_context.Setup.Items.LootTables.TryGetValue(tableId,out var table))
            return;
        var channel=RngChannel.Open(State.World.WorldSeed,_context.CellOf(State.Body),"fishing",$"{water.Key}@{activity.StartedTick}");
        var tables=_context.Setup.Items.LootTables;
        var drops=LootRoller.Roll(table,tables,sample=>channel.UInt64(sample)/SampleScale);
        var landed=new List<LootDrop>();
        string? refused=null;
        foreach(var drop in drops)
        {
            if(refused is not null)
                break;
            if(_context.Dispatch(new ExchangeItems(ImmutableArray<StackTake>.Empty,drop.ItemId,drop.Count,Quality.Standard)) is { } why)
                refused=why;
            else
                landed.Add(drop);
        }
        if(landed.Count>0)
            _context.Dispatch(new PracticeSkill(new SkillPractice(Skill,Difficulty(water.Kind),PracticeOutcome.Success,tick)
            {
                NoveltyKey = water.Key,
            }));
        foreach(var drop in landed)
            _context.Events.Publish(new FishCaught(water.Key,drop.ItemId,drop.Count,tick));
        // What the pack had no room for is let go, in the refusal's own words.
        if(refused is not null)
            _context.Events.Publish(new FishLost(water.Key,refused,tick));
    }

    private void End(string waterKey,string reason,long tick)
    {
        State.SetActivity(_owner,null);
        _baitScale=null;
        _context.Events.Publish(new FishLost(waterKey,reason,tick));
    }

    /// <summary>Busy with a blow, a dodge, a stagger or a working: nothing time-shaped is started or kept through one.</summary>
    private bool Busy(long tick) =>
        State.PlayerCombat.Action.PhaseAt(tick,_context.Setup.Combat.Constants).Phase!=CombatPhase.Idle;
}
