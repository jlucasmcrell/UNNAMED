using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.World;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Social;
using UNNAMED.Persistence;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>WP13's adventurers across a save and a load (schema 29): their lives, their slots, and one in the character's hire.</summary>
public class AdventurerSaveTests
{
    private const long Day = 57_600;

    [Fact]
    public void TheirLives_SurviveASaveAndLoad_AndGoOnTheSame()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AdventurerWorldTests.AtTheInn(session, coin: 50);
        var simulation = arena.Simulation;
        simulation.Advance(4 * Day + 300);   // 07:35: they are at the inn's board, where the character stands
        // One of them is hired, with their sheet in the character's save.
        var hired = simulation.Adventurers.Where(a => a.InTown && a.At == "ashen_hollow")
            .FirstOrDefault(a => arena.Submit(new HireCommand(arena.Player, a.Key, HirelingRole.Guard, 5, AcceptsRisk: true)) is null);
        Assert.NotNull(hired);
        // Into the day, until they have gone out and their bodies have gone with them (spec 15: an empty seat says who is out).
        bool OutAndGone() => simulation.Adventurers.Any(a => !a.InTown && a.Party is null && !simulation.Npcs.Any(n => n.Id == a.Key));
        for (int hour = 0; hour < 48 && !OutAndGone(); hour++)
            simulation.Advance(Day / 24);
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("adventurers"), SaveDocuments.Capture(simulation.World, simulation.CaptureRecord(), session.Content, simulation.WorldTick, 0));
        var loaded = store.Load(SaveSlots.Manual("adventurers"), new LoadContext(session.Generator, session.Content, new Registry()));
        Assert.True(loaded.IsComplete);
        var resumed = Simulation.Start(simulation.Setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, new EventBus());
        // Every body stands again where it stood - one out has none to stand again - and the lives go on the same (the soak's exact save and
        // load).
        Assert.True(OutAndGone());
        Assert.Equal(AdventurerWorldTests.World(simulation), AdventurerWorldTests.World(resumed));
        simulation.Advance(3 * Day);
        resumed.Advance(3 * Day);
        Assert.Equal(AdventurerWorldTests.World(simulation), AdventurerWorldTests.World(resumed));
        Assert.Equal("hireling", resumed.Adventurers.Single(a => a.Key == hired!.Key).Party);
        Assert.NotNull(resumed.PartySheet(hired!.Key));
    }
    [Fact]
    public void DismissSaveReloadRehire_PreservesEveryCompletePersonalItem()
    {
        using var profile=new TempProfile();var session=Harness.Boot(profile);
        var vest=new InventoryEntry(EntityId.NewId(EntityKind.Item),"item.armor.hide_vest",1) {
            Quality=1,Wear=new ItemWear(7,1),Notable=new NotableRecord("Roadworn vest",ProvenanceKind.Found,"fixture.vest",0) {Familiarity=2,Uses=3} };
        var cap=new InventoryEntry(EntityId.NewId(EntityKind.Item),"item.armor.hide_cap",1) {Quality=1};
        var arena=Arena.OpenCreatures(session,session.Setup,(448.6,727.0),90,Array.Empty<(string,double,double,string)>(),
            r=>new PlayerRecord(r.Id,r.Name,r.XMm,r.YMm,r.ZMm,r.AppearanceSeed,r.Inventory.Concat(new[]{vest,cap}),r.Progression,r.FacingMdeg,r.Discoveries,r.Equipment,500),
            keepSpawns:true,origin:Harness.Region);
        const string key="npc.adventurer.ashen_hollow.slot_1_g0";
        Assert.Null(arena.Submit(new HireCommand(arena.Player,key,HirelingRole.Guard,3,AcceptsRisk:true)));
        Assert.Null(arena.Submit(new GiveGearCommand(arena.Player,key,vest.ItemId,Loan:true)));
        Assert.Null(arena.Submit(new TransferPartyItemCommand(arena.Player,key,cap.ItemId,true)));
        Assert.Null(arena.Submit(new DismissCommand(arena.Player,key)));
        Assert.DoesNotContain(arena.Simulation.Player.Inventory,e=>e.ItemId==vest.ItemId || e.ItemId==cap.ItemId);
        var kept=arena.Simulation.World.Adventurers!.Agents[key];
        Assert.Equal(vest.Notable,kept.Gear[EquipSlot.Chest].Item!.Notable);
        Assert.Equal(cap.ItemId,Assert.Single(kept.Belongings).ItemId);
        var store=new SaveStore(profile.Root);
        store.Save(SaveSlots.Quick,SaveDocuments.Capture(arena.Simulation.World,arena.Simulation.CaptureRecord(),session.Content,arena.Simulation.WorldTick,0));
        var loaded=store.Load(SaveSlots.Quick,new LoadContext(session.Generator,session.Content,new Registry()));
        Assert.True(loaded.IsComplete);
        var body=loaded.World.Adventurers!.Bodies[key];var r=loaded.Player;
        var near=new PlayerRecord(r.Id,r.Name,body.XMm+500,r.YMm,body.ZMm,r.AppearanceSeed,r.Inventory,r.Progression,r.FacingMdeg,r.Discoveries,r.Equipment,r.Currency);
        var bus=new EventBus();string? refusal=null;bus.Subscribe<CommandRejected>(e=>refusal=e.Reason);
        var resumed=Simulation.Start(arena.Simulation.Setup,near,loaded.World,loaded.Manifest.WorldTick,bus);
        resumed.Enqueue(new HireCommand(resumed.PlayerId,key,HirelingRole.Guard,3,AcceptsRisk:true));resumed.DrainCommands();
        Assert.Null(refusal);
        var sheet=resumed.CaptureRecord().PartySheets.Single(s=>s.NpcId==key);
        var worn=sheet.Gear[EquipSlot.Chest];
        Assert.True(worn.Loaned);Assert.Equal(vest,worn.Item);
        Assert.Equal(cap,Assert.Single(sheet.Belongings));
        Assert.Empty(resumed.World.Adventurers!.Agents[key].Belongings);
        Assert.Empty(resumed.World.Adventurers.Agents[key].Gear);
        Assert.DoesNotContain(resumed.Player.Inventory,e=>e.ItemId==vest.ItemId || e.ItemId==cap.ItemId);
    }

}
