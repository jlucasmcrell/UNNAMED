using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Progression;

namespace UNNAMED.World.Runtime;

public sealed record VisitStationCommand(EntityId Actor,string StationKey) : GameCommand(Actor);
public sealed record StationVisited(string Key,string Kind,long Tick);
public sealed record OpeningTorchGiven(long Tick);

/// <summary>First-hour grants and teaching use the same inventory and learning commands as dialogue.</summary>
internal sealed class FirstHourSystem(SystemContext context,EntityId player)
{
    public void Listen(SimulationEvents events) => events.Listen<ConversationLine>(e=>
    {
        if(!e.FirstVisit || e.NpcId!="npc.ashen_hollow.renn_vale" || e.NodeId!="greet") return;
        if(context.Setup.Items.Catalog.Find("item.tool.torch") is null) return;
        context.Dispatch(new GrantItem("item.tool.torch",1,Quality.Standard) { SourceRef=e.NpcId });
        context.Events.Publish(new OpeningTorchGiven(e.Tick));
    });
    public string? Handle(VisitStationCommand command,long tick)
    {
        if(command.Actor!=player || context.State.PlayerCombat.Defeated) return "Cannot use that station now";
        var station=context.Stations(player).FirstOrDefault(p=>p.Layout.SpaceId==context.State.Space && p.Site.Key==command.StationKey).Site;
        if(station is null) return "That station is not here";
        var body=context.State.Body;
        if(Math.Pow(body.XMm-station.XMm,2)+Math.Pow(body.ZMm-station.ZMm,2)>Math.Pow(context.Setup.Items.Inventory.ReachMm,2)) return "That station is out of reach";
        string[] lessons=station.Kind switch
        {
            "forge"=>["recipe.smithing.torch","recipe.cooking.torch"],
            "cookfire"=>["recipe.cooking.roast_meat","recipe.cooking.torch","recipe.cooking.boil_water","recipe.cooking.herb_tea","recipe.herbal.salve_minor","recipe.herbal.salve_minor_boiled"],
            _=>[],
        };
        foreach(string id in lessons.Where(id=>context.Setup.Crafting.Recipes.ContainsKey(id) && !ProgressionEngine.Knows(context.State.Progression,id)))
            context.Dispatch(new LearnTechnique(new TechniqueLearning(id,station.Kind=="forge"?LearningSource.Teacher:LearningSource.Discovery,tick)
                { SourceRef=station.Kind=="forge"?"npc.ashen_hollow.kera_voss":station.Key }));
        context.Events.Publish(new StationVisited(station.Key,station.Kind,tick));
        return null;
    }
}
