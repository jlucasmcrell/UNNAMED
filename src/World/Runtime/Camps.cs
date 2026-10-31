using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

public sealed record PlaceCampCommand(EntityId Actor,long XMm,long ZMm) : GameCommand(Actor);
public sealed record PackCampCommand(EntityId Actor) : GameCommand(Actor);
public sealed record RestAtCampCommand(EntityId Actor,bool Rest) : GameCommand(Actor);
public sealed record CampReturnCommand(EntityId Actor,bool Camp) : GameCommand(Actor);
public sealed record RefillFlaskCommand(EntityId Actor) : GameCommand(Actor);
public sealed record CampChanged(bool Placed,long Tick);
public sealed record CampRestChanged(bool Resting);
public sealed record CampPiece(EntityId Id,string DefId,long XMm,long ZMm);
public sealed record CampView(EntityId Owner,long XMm,long ZMm,ImmutableArray<CampPiece> Pieces)
{
    public const string StationKey="station.player.campfire";
    public StationSite Station => new(StationKey,"cookfire",XMm,ZMm);
}

/// <summary>Camp pieces use existing, baseline-proven created-instance saves. In this
/// single-player world its one placed camp belongs to the player whose world it is.</summary>
internal static class Camps
{
    public const string Kit="item.tool.camp_kit",Fire="item.tool.camp_fire",Bed="item.tool.camp_bedroll";
    public const string Flask="item.tool.water_flask",EmptyFlask="item.tool.water_flask_empty";
    public static bool IsPiece(string id)=>id is Fire or Bed;
    public static (long X,long Z) Position(CreatedEntityRecord record)
    {
        var cell=CellKey.Parse(record.HostCell);
        return (((long)cell.Region.Rx*WorldMath.RegionSizeMeters+(long)cell.Cx*WorldMath.CellSizeMeters)*1000+record.XCm*10L,
            ((long)cell.Region.Rz*WorldMath.RegionSizeMeters+(long)cell.Cz*WorldMath.CellSizeMeters)*1000+record.ZCm*10L);
    }
    public static CampView? View(SystemContext context,EntityId owner)
    {
        // ordered by the piece's id - a ULID, so the order is the order they were placed and it survives a save and a load
        var pieces=context.State.World.CreatedEntities.Where(c=>IsPiece(c.DefId))
            .OrderBy(c=>c.InstanceId.Value,StringComparer.Ordinal).Select(c=>
        {
            var (x,z)=Position(c); return new CampPiece(c.InstanceId,c.DefId,x,z);
        }).ToImmutableArray();
        if(pieces.FirstOrDefault(p=>p.DefId==Fire) is not { } fire) return null;
        return new CampView(owner,fire.XMm,fire.ZMm,pieces);
    }
    public static bool Near(SystemContext context,CampView? camp,long reach=3_000)=>
        context.State.Space.IsExterior && camp is not null &&
        Math.Pow(context.State.Body.XMm-camp.XMm,2)+Math.Pow(context.State.Body.ZMm-camp.ZMm,2)<=reach*(double)reach;
    public static bool WaterNear(SystemContext context)
    {
        if(!context.State.Space.IsExterior) return false;
        var space=context.Setup.Layout.Space; var body=context.State.Body;
        if(space.WaterAt(body.XMm,body.ZMm) is not null) return true;
        if(space.Blockers.Any(b=>b.Id.Contains("well",StringComparison.OrdinalIgnoreCase) && b.DistanceTo(body.XMm,body.ZMm)<=2_000)) return true;
        // The existing exterior stream surface is at 0.12 m; terrain bounds its banks.
        foreach(long dx in new long[] {-2_000,0,2_000})
            foreach(long dz in new long[] {-2_000,0,2_000})
            {
                long x=body.XMm+dx,z=body.ZMm+dz;
                if(x>=space.MinXMm && x<=space.MaxXMm && z>=space.MinZMm && z<=space.MaxZMm && space.Terrain.HeightAtMm(x,z)<120) return true;
            }
        return false;
    }
}

internal sealed partial class InventorySystem
{
    public string? Handle(PlaceCampCommand command,long tick)
    {
        if(command.Actor!=_player) return "Unknown camper";
        if(State.PlayerCombat.Defeated) return "dead";
        if(tick-State.PlayerCombat.LastCombat<_context.Setup.Combat.Constants.OutOfCombatTicks) return "Leave the fight before making camp";
        if(Camps.View(_context,_player) is not null) return "Pack your existing camp before placing another";
        if(Distance(command.XMm,command.ZMm)>3_000) return "Place the camp within reach";
        var towns=_context.Setup.Adventurers?.Settlements.Values.Select(t=>(t.LodgeXMm,t.LodgeZMm)) ?? Enumerable.Empty<(long,long)>();
        if(CampRules.Placement(_context.Setup.LayoutFor(State.Space),command.XMm,command.ZMm,towns) is { } why) return why;
        if(State.Npcs.Values.Any(n=>n.Space==State.Space && Math.Pow(n.Body.XMm-command.XMm,2)+Math.Pow(n.Body.ZMm-command.ZMm,2)<9_000_000))
            return "Give the people here some room";
        if(Handle(new ConsumeItem(Camps.Kit,1),tick) is { } refused) return refused;
        void Piece(string def,long x,long z)
        {
            var cell=_context.CellOfSite(SpaceId.Exterior,x,z); var origin=CellOrigin(cell);
            State.PlaceItem(_owner,cell,NewItem(def),def,1,(int)((x-origin.X)/10),(int)((z-origin.Z)/10),Quality.Standard);
        }
        Piece(Camps.Fire,command.XMm,command.ZMm);
        Piece(Camps.Bed,command.XMm+1_800,command.ZMm);
        _context.Events.Publish(new CampChanged(true,tick));
        return null;
    }
    public string? Handle(PackCampCommand command,long tick)
    {
        if(command.Actor!=_player || State.PlayerCombat.Defeated) return "Cannot pack the camp now";
        var camp=Camps.View(_context,_player);
        if(!Camps.Near(_context,camp)) return "Stand beside your camp to pack it";
        if(Handle(new ExchangeItems(ImmutableArray<StackTake>.Empty,Camps.Kit,1,Quality.Standard),tick) is { } refused) return refused;
        foreach(var piece in camp!.Pieces) { State.TakeItem(_owner,piece.Id); Retire(piece.Id); }
        _context.Events.Publish(new CampChanged(false,tick));
        return null;
    }
    public string? Handle(RefillFlaskCommand command,long tick)
    {
        if(command.Actor!=_player || State.PlayerCombat.Defeated) return "Cannot refill now";
        if(!Camps.WaterNear(_context)) return "Refill at a well or the stream's edge";
        if(State.Inventory.FirstOrDefault(i=>i.DefId==Camps.EmptyFlask) is not { } flask) return "Your flask is already full";
        return Handle(new ExchangeItems(ImmutableArray.Create(new StackTake(flask.ItemId,1)),Camps.Flask,1,Quality.Standard),tick);
    }
}

/// <summary>Rest and death-screen selection are transient; the camp pieces themselves persist.</summary>
internal sealed class CampSystem(SystemContext context,SliceOwner owner,EntityId player)
{
    public string? Handle(RestAtCampCommand command)
    {
        if(command.Actor!=player || context.State.PlayerCombat.Defeated) return "Cannot rest now";
        if(command.Rest && context.State.WorldTick-context.State.PlayerCombat.LastCombat<context.Setup.Combat.Constants.OutOfCombatTicks) return "Leave the fight before resting";
        if(command.Rest && !Camps.Near(context,Camps.View(context,player))) return "Rest beside your campfire";
        context.State.SetCampSession(owner,command.Rest?context.State.Body:null,context.State.ReturnToCamp);
        context.Events.Publish(new CampRestChanged(command.Rest));
        return null;
    }
    public string? Handle(CampReturnCommand command)
    {
        if(command.Actor!=player || !context.State.PlayerCombat.Defeated) return "Choose where to return after death";
        if(command.Camp && Camps.View(context,player) is null) return "You have no camp";
        context.State.SetCampSession(owner,null,command.Camp);
        return null;
    }
    public void Tick()
    {
        var state=context.State;
        if(state.CampRestAt is { } at && (at.XMm!=state.Body.XMm || at.ZMm!=state.Body.ZMm || state.PlayerCombat.Defeated
            || state.WorldTick-state.PlayerCombat.LastCombat<context.Setup.Combat.Constants.OutOfCombatTicks
            || !Camps.Near(context,Camps.View(context,player))))
        {
            state.SetCampSession(owner,null,state.ReturnToCamp);
            context.Events.Publish(new CampRestChanged(false));
        }
        if(!state.PlayerCombat.Defeated && state.ReturnToCamp) state.SetCampSession(owner,state.CampRestAt,false);
    }
}
