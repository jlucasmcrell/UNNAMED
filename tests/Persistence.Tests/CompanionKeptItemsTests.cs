using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Social;
using UNNAMED.M2Probe;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using Registry=UNNAMED.EntityRegistry.EntityRegistry;
namespace UNNAMED.Persistence.Tests;

public class CompanionKeptItemsTests
{
    [Fact]
    public void DismissedAdventurer_ItemsSurviveTheRealSaveLoad_WithHistoryWearFlagsAndLoanOwnership()
    {
        using var profile=new TempProfile();
        var world=M2Fixtures.NewWorld(new Registry());
        var notable=new NotableRecord("The old blade",ProvenanceKind.Found,"container.fixture",8) {Familiarity=2,Uses=3};
        var sword=new AdventurerItem(EntityId.NewId(EntityKind.Item),"item.weapon.iron_sword",1,1,notable,new ItemWear(7,1),true,false);
        var supplies=new AdventurerItem(EntityId.NewId(EntityKind.Item),"item.potion.minor_healing",3,0,null,null,false,true);
        var agent=new AdventurerRecord("npc.adventurer.fixture.slot_1_g0","fixture.slot_1",0,"Brenna Holt","fixture","fixture",CharacterProgression.Empty,0,0) {
            Gear=ImmutableSortedDictionary<EquipSlot,AdventurerGear>.Empty.Add(EquipSlot.MainHand,new AdventurerGear(sword.ItemId,sword.DefId,sword.Quality) {Item=sword,Loaned=true}),
            Belongings=ImmutableArray.Create(supplies) };
        var state=new AdventurerState(0,ImmutableSortedDictionary.Create<string,AdventurerRecord>(StringComparer.Ordinal).Add(agent.Key,agent),ImmutableSortedDictionary.Create<string,AdventurerVacancy>(StringComparer.Ordinal));
        var doc=M2Fixtures.Document(world);
        doc=doc with {Delta=doc.Delta with {Adventurers=state}};
        var store=new SaveStore(profile.Root);
        store.Save(SaveSlots.Quick,doc);
        var loaded=store.Load(SaveSlots.Quick,M2Fixtures.Context(new Registry()));
        Assert.True(loaded.IsComplete);
        var restored=Assert.Single(loaded.World.Adventurers!.Agents).Value;
        Assert.Equal(agent,restored);
        Assert.Equal(sword,restored.Gear[EquipSlot.MainHand].Item);
        Assert.True(restored.Gear[EquipSlot.MainHand].Loaned);
        Assert.Equal(supplies,Assert.Single(restored.Belongings));
        using var before=new CanonicalHasher();using var after=new CanonicalHasher();
        state.AddTo(before);loaded.World.Adventurers.AddTo(after);
        Assert.Equal(before.Finish(),after.Finish());
        // A malformed duplicate instance must be refused, never silently instantiated twice.
        var bad=doc.Delta with {Adventurers=state with {Agents=state.Agents.SetItem(agent.Key,agent with {Belongings=ImmutableArray.Create(sword)})}};
        Assert.Throws<FormatException>(()=>SectionCodec.DecodeAdventurers(SectionCodec.EncodeEntities(bad)));
    }
}
