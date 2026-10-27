using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World.Runtime;
using Xunit.Abstractions;

namespace UNNAMED.Application.Tests;

public class CampTests(ITestOutputHelper output)
{
    private static string? Send(GameSession session,GameCommand command)
    {
        string? reason=null;
        void Refused(CommandRejected e) { if(ReferenceEquals(e.Command,command)) reason=e.Reason; }
        session.Subscribe<CommandRejected>(Refused);
        try { session.Simulation!.Enqueue(command);session.Simulation.DrainCommands(); }
        finally { session.Unsubscribe<CommandRejected>(Refused); }
        return reason;
    }
    [Fact]
    public void PlacementRequiresDryLevelClearExteriorGroundOutsideTownsAndRoads()
    {
        var terrain=new TerrainGrid(-200_000,-200_000,400_000,2,2,new long[] {1000,1000,1000,1000});
        var space=new WalkSpace(-200_000,-200_000,200_000,200_000,terrain,[]);
        var layout=new RegionLayout("region.test",[],space,[],[],new Body(0,1000,0,0),new(1000,0,2));
        Assert.Null(CampRules.Placement(layout,0,0,[]));
        Assert.NotNull(CampRules.Placement(layout,0,0,[(0,0)]));
        Assert.NotNull(CampRules.Placement(layout with {SpaceId=new SpaceId("space.test")},0,0,[]));
        Assert.NotNull(CampRules.Placement(layout with {Roads=[new RoadSite("road","Road",[(-20_000,0),(20_000,0)])]},0,0,[]));
        Assert.NotNull(CampRules.Placement(layout with {Space=space with {Blockers=[new BoxBlocker("hut",-1000,-1000,1000,1000,2000)]}},0,0,[]));
        Assert.NotNull(CampRules.Placement(layout with {Space=space with {Terrain=new TerrainGrid(-200_000,-200_000,400_000,2,2,new long[] {0,0,0,0})}},0,0,[]));
        Assert.NotNull(CampRules.Placement(layout with {Space=space with {Terrain=new TerrainGrid(-200_000,-200_000,400_000,2,2,new long[] {0,100_000,0,100_000})}},0,0,[]));
        Assert.NotNull(CampRules.Placement(layout,199_000,0,[]));
    }

    [Fact]
    public void CampPersistsAcrossSaveAndAbsence_OffersBothReturns_AndWorksAsARealCookfire()
    {
        using var profile=new TempProfile();
        using var session=Harness.Boot(profile);
        session.NewGame(Harness.Named("Camper"),42);
        var sim=session.Simulation!;
        var (x,z)=FindGround(sim);
        output.WriteLine($"Real early-frontier campsite: {x/1000.0},{z/1000.0}");
        Move(sim,x,z);
        Assert.Null(sim.Submit(new GrantItem(Camps.Kit,1,Quality.Standard)));
        Assert.Null(Send(session,new PlaceCampCommand(sim.PlayerId,x,z)));
        var camp=Assert.IsType<CampView>(sim.Camp);
        Assert.Equal(sim.PlayerId,camp.Owner);
        Assert.Equal(2,camp.Pieces.Length);
        Assert.Contains(sim.Stations,s=>s.Key==CampView.StationKey && s.Kind=="cookfire");
        Assert.NotNull(Send(session,new PlaceCampCommand(sim.PlayerId,x,z)));
        Assert.Null(Send(session,new VisitStationCommand(sim.PlayerId,CampView.StationKey)));
        Assert.Null(sim.Submit(new GrantItem("item.material.raw_meat",1,Quality.Standard)));
        Assert.Null(Send(session,new CraftCommand(sim.PlayerId,"recipe.cooking.roast_meat")));
        Assert.Contains(sim.Player.Inventory,i=>i.DefId=="item.consumable.roast_meat");
        var sword=sim.Player.Inventory.First(i=>i.DefId=="item.weapon.rusted_sword");
        Assert.Null(sim.Submit(new WearEquipped([EquipSlot.MainHand],20)));
        Assert.Null(sim.Submit(new GrantItem("item.material.iron_ingot",1,Quality.Standard)));
        Assert.Null(Send(session,new RepairCommand(sim.PlayerId,sword.ItemId)));
        Assert.False(Durability.NeedsRepair(sim.Player.Inventory.First(i=>i.ItemId==sword.ItemId).Quality,
            sim.Player.Inventory.First(i=>i.ItemId==sword.ItemId).Wear));
        Assert.Null(sim.Submit(new ChangePools(-60,0)));
        int health=sim.Combat.Health;
        for(int i=0;i<100;i++) sim.Step();
        int ordinary=sim.Combat.Health-health;
        Assert.Null(Send(session,new RestAtCampCommand(sim.PlayerId,true)));
        Assert.True(sim.RestingAtCamp);
        health=sim.Combat.Health;
        for(int i=0;i<100;i++) sim.Step();
        Assert.True(sim.Combat.Health-health>ordinary);
        Move(sim,x+10_000,z);
        sim.Step();
        Assert.False(sim.RestingAtCamp);
        session.Save(SaveSlots.Quick);
        session.Load(SaveSlots.Quick);
        sim=session.Simulation!;
        Assert.Equal(camp.Pieces.ToArray(),sim.Camp!.Pieces.ToArray());
        long before=sim.WorldTick;
        sim.Advance(1200);
        Assert.Equal(before+1200,sim.WorldTick);
        Assert.Equal(camp.Pieces.ToArray(),sim.Camp!.Pieces.ToArray());

        var stone=sim.Waystones.First();
        Move(sim,Footprints.Center(stone.Site.Body).XMm,Footprints.Center(stone.Site.Body).ZMm);
        Assert.Null(Send(session,new TouchStoneCommand(sim.PlayerId,stone.Site.Key)));
        Assert.Null(Send(session,new BindStoneCommand(sim.PlayerId,stone.Site.Key)));
        void Die(bool toCamp)
        {
            Assert.Null(sim.Submit(new Harm(sim.PlayerId,"fall",sim.Combat.MaxHealth+1000)));
            sim.Step();
            Assert.True(sim.Combat.Defeated);
            Assert.Null(Send(session,new CampReturnCommand(sim.PlayerId,toCamp)));
            for(int i=0;i<1000 && sim.Combat.Defeated;i++) sim.Step();
            Assert.False(sim.Combat.Defeated);
        }
        Die(true);
        Assert.InRange(Math.Abs(sim.Player.Body.XMm-camp.XMm),0,3000);
        Assert.InRange(Math.Abs(sim.Player.Body.ZMm-camp.ZMm),0,3000);
        Die(false);
        Assert.Equal((stone.Site.ArrivalXMm,stone.Site.ArrivalZMm),(sim.Player.Body.XMm,sim.Player.Body.ZMm));
        Move(sim,x,z);
        Assert.Null(Send(session,new PackCampCommand(sim.PlayerId)));
        Assert.Null(sim.Camp);
        Assert.Contains(sim.Player.Inventory,i=>i.DefId==Camps.Kit);
    }

    [Fact]
    public void FlaskSipsRefillsAndBoilsWithoutDestroyingTheBottle()
    {
        using var profile=new TempProfile();
        using var session=Harness.Boot(profile);
        session.NewGame(Harness.Named("Water"),43);
        var sim=session.Simulation!;
        Assert.Null(sim.Submit(new ChangePools(-30,0)));
        int before=sim.Combat.Health;
        var flask=sim.Player.Inventory.First(i=>i.DefId==Camps.Flask);
        Assert.Null(Send(session,new UseItemCommand(sim.PlayerId,flask.ItemId)));
        Assert.Contains(sim.Player.Inventory,i=>i.DefId==Camps.EmptyFlask);
        Assert.DoesNotContain(sim.Player.Inventory,i=>i.DefId==Camps.Flask);
        for(int i=0;i<65;i++) sim.Step();
        Assert.True(sim.Combat.Health>before);
        Move(sim,450_500,751_000);
        Assert.True(sim.CanRefillFlask);
        Assert.Null(Send(session,new RefillFlaskCommand(sim.PlayerId)));
        Assert.Contains(sim.Player.Inventory,i=>i.DefId==Camps.Flask);
        Move(sim,447_000,738_000);
        Assert.Null(Send(session,new VisitStationCommand(sim.PlayerId,"station.hollow.cookfire")));
        Assert.Null(Send(session,new CraftCommand(sim.PlayerId,"recipe.cooking.boil_water")));
        Assert.Contains(sim.Player.Inventory,i=>i.DefId=="item.material.boiled_water");
        Assert.Contains(sim.Player.Inventory,i=>i.DefId==Camps.EmptyFlask);
        Move(sim,800_000,800_000);
        Assert.NotNull(Send(session,new RefillFlaskCommand(sim.PlayerId)));
    }

    private static void Move(Simulation sim,long x,long z) =>
        Assert.Null(sim.Submit(new Relocate(new Body(x,sim.Setup.Layout.Space.HeightAtMm(x,z),z,0))));

    [Fact]
    public void RennGivesOneTorchAcrossReload_AndTheFirstForgeVisitTeachesIt()
    {
        using var profile=new TempProfile();
        using var session=Harness.Boot(profile);
        session.NewGame(Harness.Named("Light"),44);
        var sim=session.Simulation!;
        var renn=sim.Npcs.First(n=>n.Id=="npc.ashen_hollow.renn_vale");
        Move(sim,renn.Body.XMm+800,renn.Body.ZMm);
        Assert.Null(Send(session,new TalkCommand(sim.PlayerId,renn.Id)));
        Assert.Equal(1,sim.Player.Inventory.Where(i=>i.DefId=="item.tool.torch").Sum(i=>i.Count));
        Assert.Null(Send(session,new LeaveCommand(sim.PlayerId)));
        session.Save(SaveSlots.Quick);
        session.Load(SaveSlots.Quick);
        sim=session.Simulation!;
        Assert.Null(Send(session,new TalkCommand(sim.PlayerId,renn.Id)));
        Assert.Equal(1,sim.Player.Inventory.Where(i=>i.DefId=="item.tool.torch").Sum(i=>i.Count));
        Assert.Null(Send(session,new LeaveCommand(sim.PlayerId)));
        Move(sim,460_500,745_000);
        Assert.Null(Send(session,new VisitStationCommand(sim.PlayerId,"station.forge_hearth")));
        Assert.Contains("recipe.smithing.torch",sim.Player.Progression.Known.Keys);
        Assert.Contains("recipe.cooking.torch",sim.Player.Progression.Known.Keys);
    }

    internal static (long X,long Z) FindGround(Simulation sim)
    {
        var towns=sim.Setup.Adventurers!.Settlements.Values.Select(t=>(t.LodgeXMm,t.LodgeZMm)).ToArray();
        for(long z=750_000;z<1_100_000;z+=10_000)
            for(long x=600_000;x<900_000;x+=10_000)
                if(CampRules.Placement(sim.Setup.Layout,x,z,towns) is null) return (x,z);
        throw new InvalidOperationException("No clear campsite in the early frontier");
    }
}
