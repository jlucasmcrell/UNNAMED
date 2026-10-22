using Godot;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.World;
using UNNAMED.World.Runtime;
namespace UNNAMED.Presentation.Ui;

public partial class CompanionsPanel
{
    private void BuildInventory(Simulation simulation,string id)
    {
        _panes.ListScroll.CustomMinimumSize=Vector2.Zero;
        _panes.ListScroll.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        var sheet=simulation.PartySheet(id)!;
        var member=simulation.Companions.Single(c=>c.NpcId==id);
        var catalog=simulation.Setup.Items.Catalog;
        _sheet.SetHint($"{sheet.Name} — inventory and equipment · the world keeps moving");
        _panes.List.AddChild(UiKit.Action("Party and orders",()=>{_inventoryMode=false;_drawn=null;},compact:true,id:"party/overview"));
        _panes.List.AddChild(UiKit.Words("Your pack",22));
        _panes.List.AddChild(UiKit.Quiet($"{simulation.Player.CarriedGrams/1000.0:0.#} / {simulation.Player.CarryLimitGrams/1000.0:0.#} kg"));
        var playerRecord=simulation.CaptureRecord();
        foreach(var entry in simulation.Player.Inventory.Where(e=>!simulation.Player.Equipment.ContainsValue(e.ItemId) && !playerRecord.Bags.Contains(e.ItemId)))
        {
            var row=UiKit.Row(null,ItemWords(entry),WearWords.Line(entry.Wear,catalog.Find(entry.DefId)?.DurabilityMax??0)??"");
            row.Root.AddChild(UiKit.Action("Give →",()=>Submit(new TransferPartyItemCommand(simulation.PlayerId,id,entry.ItemId,true)),compact:true,id:$"{id}/givepack/{entry.ItemId}"));
            _panes.List.AddChild(row.Root);
        }
        _panes.Detail.AddChild(UiKit.Words(sheet.Name,22));
        _panes.Detail.AddChild(UiKit.Stat("Health",$"{member.Health} / {sheet.MaxHealth} · Level {sheet.Level}"));
        _panes.Detail.AddChild(UiKit.Quiet(string.Join(" · ",sheet.Attributes.Select(a=>$"{ProgressionKeys.Key(a.Key)} {a.Value}"))));
        _panes.Detail.AddChild(UiKit.Stat("Armour",string.Join(" · ",member.Armor.Select(a=>$"{a.Key} {a.Value}"))));
        if(member.Attack is { } attack)_panes.Detail.AddChild(UiKit.Stat("Damage",$"{attack.DamageMin}–{attack.DamageMax}"));
        _panes.Detail.AddChild(UiKit.Stat("Orders",$"{member.Order} · {member.Stance} · {member.Doing}"));
        _panes.Detail.AddChild(UiKit.Quiet($"Carrying {sheet.CarriedGrams/1000.0:0.#} / {sheet.CarryLimitGrams/1000.0:0.#} kg"));
        var gear=UiKit.Section("Equipment");
        foreach(var slot in Enum.GetValues<EquipSlot>())
        {
            var worn=sheet.Gear.FirstOrDefault(g=>g.Slot==slot);
            var row=UiKit.Row(UiKit.Chip(PartyModel.SlotWord(slot),HudStyle.Iron),worn is null?"Empty":Names.Item(_session,worn.DefId,worn.Quality),worn?.Loaned==true?"Loaned":"");
            if(worn is not null)row.Root.AddChild(UiKit.Action("To pack",()=>Submit(new UnequipPartyGearCommand(simulation.PlayerId,id,slot)),compact:true,id:$"{id}/unequip/{slot}"));
            gear.AddChild(row.Root);
        }
        _panes.Detail.AddChild(gear);
        var pack=UiKit.Section("Their pack");
        if(sheet.Belongings.IsEmpty)pack.AddChild(UiKit.Quiet("Empty"));
        foreach(var entry in sheet.Belongings)
        {
            var row=UiKit.Row(null,ItemWords(entry),WearWords.Line(entry.Wear,catalog.Find(entry.DefId)?.DurabilityMax??0)??"");
            if(catalog.Find(entry.DefId)?.Slot is not null)
                row.Root.AddChild(UiKit.Action("Equip",()=>Submit(new EquipPartyPackCommand(simulation.PlayerId,id,entry.ItemId)),compact:true,id:$"{id}/equip/{entry.ItemId}"));
            row.Root.AddChild(UiKit.Action("← Take",()=>Submit(new TransferPartyItemCommand(simulation.PlayerId,id,entry.ItemId,false)),compact:true,id:$"{id}/takepack/{entry.ItemId}"));
            pack.AddChild(row.Root);
        }
        _panes.Detail.AddChild(pack);
        var skills=UiKit.Section("Skills");
        foreach(var skill in sheet.Skills)skills.AddChild(UiKit.Stat(Names.Of(_session,skill.Key),$"{skill.Value.Level}"));
        _panes.Detail.AddChild(skills);
        Dismiss(simulation,PartyModel.Card(member,sheet,simulation.Party,(def,q)=>Names.Item(_session,def,q)),_panes.Detail);
    }

    private string ItemWords(InventoryEntry entry)=>(entry.Notable?.Name??Names.Item(_session,entry.DefId,entry.Quality))+(entry.Count>1?$" ×{entry.Count}":"");

    private void Dismiss(Simulation simulation,PartyCard card,VBoxContainer parent)
    {
        string id=card.NpcId;
        var leave=UiKit.Section("Dismiss");
        bool asked=_confirm==id;
        leave.AddChild(UiKit.Words(card.Dismiss??"They keep every worn and packed item when dismissed. Take items back first if you want to keep them.",16,HudStyle.Muted));
        if(asked)
        {
            var kept=simulation.CaptureRecord().PartySheets.FirstOrDefault(s=>s.NpcId==id)?.Items.ToArray()??Array.Empty<InventoryEntry>();
            leave.AddChild(UiKit.Words(kept.Length==0?"They will leave with no items you handed them.":"They will keep: "+string.Join(", ",kept.Select(ItemWords))+".",17,HudStyle.Brass));
            leave.AddChild(UiKit.Action("Cancel",()=>{_confirm=null;_drawn=null;},compact:true,id:$"{id}/canceldismiss"));
            leave.AddChild(UiKit.Action("Take items back",()=>{_confirm=null;_inventoryMode=true;_drawn=null;},compact:true,id:$"{id}/reviewkept"));
        }
        var dismiss=UiKit.Action(asked?"Confirm dismissal":card.Hired?"Dismiss: end the contract":"Dismiss",()=>
        {
            if(_confirm==id){_confirm=null;Submit(new DismissCommand(simulation.PlayerId,id));}
            else{_confirm=id;}
            _drawn=null;
        },UiKit.Kind.Destructive,id:$"{id}/dismiss");
        dismiss.Disabled=!card.CanDismiss;leave.AddChild(dismiss);parent.AddChild(leave);
    }
}
