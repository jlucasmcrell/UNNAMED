// UNNAMED World - Waystones: knowing them, waking them, choosing one to return to, and travelling between them (SYSTEMS.md S-38;
// OWNER OD-03, OD-C16; NEXT_AREA_TRAVEL_CITY_PLAN.md D.3)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Spatial;
using UNNAMED.Domain.Travel;
using UNNAMED.Domain.Social;

namespace UNNAMED.World.Runtime;

/// <summary>Touch the stone: know it, or - for a dormant stone whose rite the character has heard - wake it. Within reach of it.</summary>
public sealed record TouchStoneCommand(EntityId Actor, string StoneKey) : GameCommand(Actor);

/// <summary>Make a stone the character knows, and stands at, their return point. Never a side effect of anything else.</summary>
public sealed record BindStoneCommand(EntityId Actor, string StoneKey) : GameCommand(Actor);

/// <summary>From the stone the character stands at to another they know.</summary>
public sealed record TravelCommand(EntityId Actor, string FromKey, string ToKey) : GameCommand(Actor);

/// <summary>A hand on a stone: the stone is known from now on - touched, or woken by this hand.</summary>
public sealed record StoneTouched(string StoneKey, WaystoneMethod Method, string Text, long Tick);

/// <summary>A stone was made the character's return point.</summary>
public sealed record ReturnPointBound(string StoneKey, long Tick);

/// <summary>The party went by the stones: the road it did not walk. No world time passes for it (DN8).</summary>
public sealed record PlayerTravelled(string FromKey, string ToKey, TravelCost Cost, Body Arrival, ImmutableArray<string> Party, long Tick);

/// <summary>A Waystone as the character sees it: awake or dormant, known to them or not, and whether it is their return point.</summary>
public sealed record WaystoneView(WaystoneSite Site, bool Awake, bool Known, bool ReturnPoint);

/// <summary>
/// A journey the stones offer from one stone to another the character knows - one entry of the destination list: the stone and its region,
/// what it costs, and why it cannot be taken now, if so.
/// </summary>
public sealed record TravelOption(string ToKey, string RegionId, TravelCost Cost, string? Blocked);

/// <summary>To <see cref="CompanionSystem"/>: the party arrives with the character, on a ring round where they stand.</summary>
internal sealed record ArriveWithParty(Body Arrival) : InternalCommand;
internal sealed record RestageEncounterParty(Body Arrival, EncounterRecovery Court) : InternalCommand;

internal sealed record TakePassage(string NpcId, TakePassageConsequence Passage) : InternalCommand;
public sealed record PassageTaken(string NpcId, string LocationId, long Coin, Body Arrival, long Tick);

/// <summary>
/// Owns: <see cref="StateSlice.Waystones"/> - the stones the character knows and the one they chose to return to. A stone becomes known by
/// touching it; a dormant stone wakes for someone who has heard its rite, and its flag is set in its cell. Travel goes only from a stone the
/// character stands at to another they know (19 §9.3: never from the field), takes the following party and nothing else, is refused while
/// anything is fighting the character or a companion, or while a companion is down. It lets no world time pass (DN8, accepted 2026-10-17:
/// with a living world, a time cost would let one player fast-forward everyone else). Binding the return point is its own command: travel
/// never changes it.
/// </summary>
internal sealed class TravelSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;

    public TravelSystem(SystemContext context, SliceOwner owner, EntityId player)
    {
        _context = context;
        _owner = owner;
        _player = player;
    }

    private RuntimeState State => _context.State;
    private RegionLayout Layout => _context.Setup.Layout;

    public bool IsAwake(WaystoneSite stone) =>
        stone.Dormant is not { } dormant || State.World.GetFlag(CellOf(stone), dormant.FlagId) != 0;

    // A local boat service uses the same movement/party owners as other travel,
    // but neither discovers a Waystone nor changes the return point or world time.
    public string? Handle(TakePassage command, long tick)
    {
        if (!State.Space.IsExterior || !State.Npcs.TryGetValue(command.NpcId, out var npc)
            || _context.DistanceToPlayer(npc.Body) > _context.TalkReachMm)
            return "Speak to the ferry keeper at the landing";
        if (Blocked("ferry crew") is { } blocked) return blocked;
        var p = command.Passage;
        if (p.MinTideMm is { } minimum && (Layout.Tide is not { } tide || tide.LevelAt(tick) < minimum))
            return "Not enough water over the flats for the ferry. Use the raised walk until high water";
        if (Layout.Locations.FirstOrDefault(l => l.Id == p.LocationId) is not { } location
            || Math.Sqrt(Math.Pow(p.XMm - location.XMm,2) + Math.Pow(p.ZMm - location.ZMm,2)) > location.DiscoveryRadiusMm)
            return "That landing is not part of this local passage";
        if (!Kinematics.IsClear(p.XMm, p.ZMm, _context.Setup.Movement.BodyRadiusMm, Layout.Space, _context.ClosedDoors())
            || State.Npcs.Values.Any(n => Math.Sqrt(Math.Pow(n.Body.XMm-p.XMm,2)+Math.Pow(n.Body.ZMm-p.ZMm,2))<1000))
            return "The landing is obstructed";
        if (_context.Dispatch(new SpendCurrency(p.Coin)) is { } unpaid) return unpaid;
        var arrival = new Body(p.XMm, Layout.Space.HeightAtMm(p.XMm,p.ZMm), p.ZMm, State.Body.FacingMdeg);
        _context.Dispatch(new Relocate(arrival));
        _context.Dispatch(new ArriveWithParty(arrival));
        _context.Events.Publish(new PassageTaken(command.NpcId, p.LocationId, p.Coin, arrival, tick));
        return null;
    }

    public string? Handle(TouchStoneCommand command, long tick)
    {
        if (Reach(command.Actor, command.StoneKey, out var stone) is { } refused)
            return refused;
        if (IsAwake(stone))
        {
            if (State.KnownStones.ContainsKey(stone.Key))
                return null;   // a hand on a stone already known: nothing new, and no failure
            Know(stone, WaystoneMethod.Touched, _context.Setup.Travel.TouchText, tick);
            return null;
        }
        var dormant = stone.Dormant!;
        if (!State.Conversations.TryGetValue(dormant.RiteDialogueId, out var heard) || !heard.Contains(dormant.RiteNodeId))
            return dormant.DormantText;
        if (_context.Dispatch(new SetWorldFlag(CellOf(stone), dormant.FlagId, 1)) is { } failed)
            return failed;
        Know(stone, WaystoneMethod.Woken, dormant.WakeText, tick);
        return null;
    }

    public string? Handle(BindStoneCommand command, long tick)
    {
        if (Reach(command.Actor, command.StoneKey, out var stone) is { } refused)
            return refused;
        if (!Knows(stone))
            return $"Touch the stone first: the {stone.Name} does not know you yet";
        if (State.ReturnPoint == stone.Key)
            return null;
        State.SetReturnPoint(_owner, stone.Key);
        _context.Events.Publish(new ReturnPointBound(stone.Key, tick));
        return null;
    }

    public string? Handle(TravelCommand command, long tick)
    {
        // No road runs under the hill (canon). A stone's arrival point is a place in the REGION's own space, so
        // travelling from an interior would put the body at exterior metres while the simulation still called
        // it inside - the collision the space identity exists to prevent. Refused in the simulation rather than
        // in the panel, so no UI path can reach it.
        if (!State.Space.IsExterior)
            return "No road runs under the hill";
        if (Reach(command.Actor, command.FromKey, out var from) is { } refused)
            return refused;
        if (!Knows(from))
            return $"Touch the stone first: the {from.Name} does not know you yet";
        if (Layout.FindWaystone(command.ToKey) is not { } to || !Knows(to))
            return "That is not a stone you know";
        if (to.Key == from.Key)
            return $"You are already at the {from.Name}";
        if (Blocked() is { } blocked)
            return blocked;
        var cost = CostTo(from, to);
        var terrain = Layout.Space.Terrain;
        var arrival = new Body(to.ArrivalXMm, terrain.HeightAtMm(to.ArrivalXMm, to.ArrivalZMm), to.ArrivalZMm, AwayFrom(to));
        var party = State.Companions.Values.Where(c => c.Order == CompanionOrder.Follow && c.Condition == CompanionCondition.Up)
            .Select(c => c.NpcId).ToImmutableArray();
        _context.Dispatch(new Relocate(arrival));
        _context.Dispatch(new ArriveWithParty(arrival));
        _context.Events.Publish(new PlayerTravelled(from.Key, to.Key, cost, arrival, party, State.WorldTick));
        return null;
    }

    /// <summary>Where a character who died comes back: beside the stone they chose, or the region's spawn when they chose none here.</summary>
    /// <remarks>
    /// The return point is a place in the REGION's own space, so a character who dies in an interior has to come
    /// back out of it - the caller resets the space before relocating to this (see <see cref="DeathSystem"/>).
    /// Without that the body would stand at the stone's exterior metres while the simulation still called it
    /// inside, and every cell it touched would be the wrong cell.
    /// </remarks>
    public Body ReturnBody()
    {
        if(State.ReturnToCamp && Camps.View(_context,_player) is { } camp)
        {
            long x=camp.XMm+1_800,z=camp.ZMm+1_200;
            return new Body(x,Layout.Space.Terrain.HeightAtMm(x,z),z,0);
        }
        if (State.ReturnPoint is { } key && Layout.FindWaystone(key) is { } stone)
            return new Body(stone.ArrivalXMm, Layout.Space.Terrain.HeightAtMm(stone.ArrivalXMm, stone.ArrivalZMm), stone.ArrivalZMm, AwayFrom(stone));
        var spawn = Layout.Spawn;
        return new Body(spawn.XMm, spawn.YMm, spawn.ZMm, spawn.FacingMdeg);
    }

    public ImmutableArray<WaystoneView> Views() =>
        Layout.Waystones.Select(w => new WaystoneView(w, IsAwake(w), Knows(w), State.ReturnPoint == w.Key))
            .ToImmutableArray();

    /// <summary>
    /// The destination list: every other stone the character knows, from this one, with the cost of each and why none can be taken now,
    /// if so. Only stones in the region played are offered - one region is loaded at a time - so a known stone of another region (none
    /// exist yet) waits in the record for cross-region travel.
    /// </summary>
    public ImmutableArray<TravelOption> Options(string fromKey)
    {
        if (Layout.FindWaystone(fromKey) is not { } from)
            return ImmutableArray<TravelOption>.Empty;
        string? blocked = Blocked();
        return Layout.Waystones.Where(w => w.Key != fromKey && Knows(w))
            .Select(w => new TravelOption(w.Key, Layout.Id, CostTo(from, w), blocked)).ToImmutableArray();
    }

    /// <summary>The character knows this stone of the region played.</summary>
    private bool Knows(WaystoneSite stone) => State.KnownStones.TryGetValue(stone.Key, out var record) && record.RegionId == Layout.Id;

    public ImmutableArray<WaystoneRecord> Records() => State.KnownStones.Values.ToImmutableArray();

    /// <summary>
    /// Why the stones will not carry the party now (19 §9.3, the plan's T3): anything fighting the character or a companion, or a companion
    /// down; null when nothing stops them.
    /// </summary>
    private string? Blocked(string carrier = "stones")
    {
        if (State.PlayerCombat.Defeated)
            return "dead";
        bool hunted = State.Creatures.Values.Any(c => c.Alive && c.Mind == CreatureMind.Engaged
            && State.Tiers.GetValueOrDefault(CellKey.OfWorld(c.Body.XMm / 1000.0, c.Body.ZMm / 1000.0).ToString(), SimulationTier.D) == SimulationTier.A);
        if (hunted)
            return $"You are being hunted. The {carrier} will not take you while something is fighting you";
        if (State.Companions.Values.FirstOrDefault(c => c.Condition == CompanionCondition.Downed) is { } down)
            return $"{(State.Npcs.TryGetValue(down.NpcId, out var npc) ? npc.Definition.Name : down.NpcId)} is down. Help them up first: the {carrier} carry only those who can stand";
        return null;
    }

    private TravelCost CostTo(WaystoneSite from, WaystoneSite to) =>
        Journeys.Cost(Layout.Roads, from, to, _context.Setup.Movement.SpeedMmPerSecond(Gait.Walk), _context.Setup.TickMilliseconds);

    private void Know(WaystoneSite stone, WaystoneMethod method, string text, long tick)
    {
        State.KnowStone(_owner, new WaystoneRecord(stone.Key, Layout.Id, method, tick));
        _context.Events.Publish(new StoneTouched(stone.Key, method, text, tick));
    }

    /// <summary>The actor is the character, the stone is this region's, and it is within a hand's reach of the body.</summary>
    private string? Reach(EntityId actor, string key, out WaystoneSite stone)
    {
        stone = null!;
        if (actor != _player)
            return $"unknown actor {actor}";
        if (Layout.FindWaystone(key) is not { } found)
            return $"there is no stone called '{key}' here";
        stone = found;
        var body = State.Body;
        long reach = _context.Setup.Movement.InteractReachMm;
        double distance = found.Body.DistanceTo(body.XMm, body.ZMm);
        return distance > reach ? $"the {found.Name} is {distance / 1000:0.00} m away; reach is {reach / 1000.0:0.00} m" : null;
    }

    /// <summary>Arriving at a stone, the body faces away from it, out to the place.</summary>
    private static int AwayFrom(WaystoneSite stone)
    {
        var (x, z) = Footprints.Center(stone.Body);
        return CombatRules.FacingTowards(x, z, stone.ArrivalXMm, stone.ArrivalZMm);
    }

    private static CellKey CellOf(WaystoneSite stone)
    {
        var (x, z) = Footprints.Center(stone.Body);
        return CellKey.OfWorld(x / 1000.0, z / 1000.0);
    }
}
