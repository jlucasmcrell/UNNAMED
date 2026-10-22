using Godot;
using UNNAMED.Domain;
using UNNAMED.Domain.Pets;
using UNNAMED.World.Runtime;
namespace UNNAMED.Presentation.Ui;

public partial class CompanionsPanel
{
    private EntityId? _petId;
    public void OpenPet(EntityId pet)
    { _petId=pet;_drawn=null;Visible=true; }

    private void RefreshPet(Simulation simulation)
    {
        var pet=simulation.Pets.FirstOrDefault(p=>p.Id==_petId);
        string signature=$"pet:{pet}|"+string.Join("|",pet?.Skills.Select(s=>$"{s.Kind}:{s.Level}:{s.Uses}")??Array.Empty<string>())+
            string.Join("|",simulation.Player.Inventory.Select(e=>$"{e.ItemId}:{e.Count}"));
        if(signature==_drawn)return;
        _drawn=signature;
        var focus=UiKit.FocusedId(_sheet.Root);
        _live.Clear();UiKit.Clear(_panes.List);UiKit.Clear(_panes.Detail);
        _panes.ListScroll.CustomMinimumSize=Vector2.Zero;_panes.ListScroll.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        _sheet.SetHint("Pet · the world keeps moving");
        _panes.List.AddChild(UiKit.Action("Party",()=>{_petId=null;_inventoryMode=false;_drawn=null;},compact:true,id:"pet/party"));
        if(pet is null){_panes.Detail.AddChild(UiKit.Quiet("That animal is no longer with you."));return;}
        _panes.List.AddChild(UiKit.Words("Your pack · food",22));
        var species=simulation.Setup.Pets?.Of(pet.CreatureId);
        var food=simulation.Player.Inventory.Where(e=>species?.Eats(e.DefId)==true && !simulation.Player.Equipment.ContainsValue(e.ItemId)).ToArray();
        if(food.Length==0)_panes.List.AddChild(UiKit.Quiet("You have no food this animal eats."));
        foreach(var entry in food)
        {
            var row=UiKit.Row(null,ItemWords(entry));
            var feed=UiKit.Action("Feed",()=>Submit(new FeedPetCommand(simulation.PlayerId,pet.Id,entry.ItemId)),compact:true,id:$"pet/feed/{entry.ItemId}");
            feed.Disabled=pet.Condition!=PetCondition.Up;row.Root.AddChild(feed);_panes.List.AddChild(row.Root);
        }
        _panes.Detail.AddChild(UiKit.Words(pet.Name,24));
        _panes.Detail.AddChild(UiKit.Stat("Health",$"{pet.Health} / {pet.MaxHealth} · Level {pet.Level}"));
        _panes.Detail.AddChild(UiKit.Stat("Role",pet.Role.ToString()));
        _panes.Detail.AddChild(UiKit.Stat("Bond",$"{pet.Bond} / {PetBond.Max}"));
        _panes.Detail.AddChild(UiKit.Words($"{pet.Doing} · {pet.Standing}",17));
        if(pet.HomeUntilTick is { } home)_panes.Detail.AddChild(UiKit.Quiet($"Recovering at home · {Math.Max(0,home-simulation.WorldTick)*_session.TickSeconds/3600.0:0.#} hours remaining"));
        var training=UiKit.Section("Training");
        foreach(var skill in pet.Skills)training.AddChild(UiKit.Stat(Names.Words(skill.Kind.ToString()),$"Level {skill.Level} · {skill.Uses} uses"));
        _panes.Detail.AddChild(training);
        if(!UiKit.Refocus(_sheet.Root,focus))UiKit.FirstFocusable(_sheet.Root)?.GrabFocus();
    }
}
