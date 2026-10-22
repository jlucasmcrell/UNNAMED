using Godot;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Presentation.Art;
using UNNAMED.World;
using UNNAMED.World.Runtime;
namespace UNNAMED.Presentation.Ui;

public partial class InventoryPanel
{
    private readonly Control _doll=new() {CustomMinimumSize=new Vector2(340,120),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ExpandFill};
    private readonly CreatorPreview _dollPreview=new() {Name="PaperDoll",Centered=true};
    private readonly Dictionary<EquipSlot,Button> _dollSlots=new();
    private Func<(string? Model,float ArmSplay)> _bodyChoice=()=> (null,0);

    public void BindArt(ArtLibrary art,ArtBindings bindings,Func<(string? Model,float ArmSplay)> body)
    { _dollPreview.Bind(art,bindings);_bodyChoice=body; }

    private void BuildPaperDoll(BoxContainer columns)
    {
        _doll.AddChild(_dollPreview);columns.AddChild(_doll);
        EquipSlot[][] sides={new[]{EquipSlot.Head,EquipSlot.Chest,EquipSlot.Hands,EquipSlot.Legs,EquipSlot.Feet},
            new[]{EquipSlot.Amulet,EquipSlot.Cloak,EquipSlot.MainHand,EquipSlot.OffHand,EquipSlot.Ring}};
        for(int side=0;side<2;side++)for(int i=0;i<sides[side].Length;i++)
        {
            var slot=sides[side][i];
            var button=UiKit.Action(Names.Slot(slot),()=>{
                if(_session.Simulation is { } sim && sim.Player.Equipment.ContainsKey(slot))Submit(new UnequipCommand(sim.PlayerId,slot));
            },compact:true,id:$"doll/{slot}");
            button.SetAnchorsAndOffsetsPreset(side==0?Control.LayoutPreset.TopLeft:Control.LayoutPreset.TopRight);
            button.AnchorTop=button.AnchorBottom=(i+.5f)/5;
            button.OffsetTop=-21;button.OffsetBottom=21;
            button.OffsetLeft=side==0?0:-150;button.OffsetRight=side==0?150:0;
            button.ClipText=true;button.IconAlignment=HorizontalAlignment.Left;button.ExpandIcon=true;button.AddThemeConstantOverride("icon_max_width",20);
            button.AddThemeFontSizeOverride("font_size",HudStyle.Sized(14));
            button.SetDragForwarding(default,Callable.From<Vector2,Variant,bool>((_,data)=>EquipmentDrop(data,slot) is not null),
                Callable.From<Vector2,Variant>((_,data)=>{if(EquipmentDrop(data,slot) is { } item && _session.Simulation is { } sim)Submit(new EquipCommand(sim.PlayerId,item));}));
            void Inspect()
            {
                if(_session.Simulation is { } sim && sim.Player.Equipment.TryGetValue(slot,out var item))Choose(item.Value);
            }
            button.MouseEntered+=Inspect;button.FocusEntered+=Inspect;
            _doll.AddChild(button);_dollSlots[slot]=button;
        }
    }

    private EntityId? EquipmentDrop(Variant data,EquipSlot slot)
    {
        if(data.VariantType!=Variant.Type.Dictionary || _session.Simulation is not { } sim)return null;
        var payload=data.AsGodotDictionary();
        if(!payload.TryGetValue("kind",out var kind) || kind.AsString()!="equipment_item" || !payload.TryGetValue("item",out var item) || !EntityId.TryParse(item.AsString(),out var id))return null;
        var entry=sim.Player.Inventory.FirstOrDefault(e=>e.ItemId==id);
        return entry is not null && sim.Setup.Items.Catalog.Find(entry.DefId)?.Slot==slot ? id : null;
    }

    private void EquipmentDragSource(Control row,InventoryEntry entry)
    {
        row.SetDragForwarding(Callable.From<Vector2,Variant>(_=>{
            row.SetDragPreview(UiKit.Words(Names.Item(_session,entry.DefId,entry.Quality),18));
            return new Godot.Collections.Dictionary { ["kind"]="equipment_item",["item"]=entry.ItemId.Value };
        }),default,default);
    }

    private void RefreshPaperDoll(Simulation simulation)
    {
        string? Worn(EquipSlot slot)=>simulation.Player.Equipment.TryGetValue(slot,out var item)?simulation.Player.Inventory.FirstOrDefault(e=>e.ItemId==item)?.DefId:null;
        var body=_bodyChoice();
        _dollPreview.Show(body.Model,body.ArmSplay,new WornOutfit(Worn(EquipSlot.Chest),Worn(EquipSlot.Head),Worn(EquipSlot.Feet),Worn(EquipSlot.Hands),Worn(EquipSlot.Legs),Worn(EquipSlot.Cloak)),Worn(EquipSlot.MainHand));
        foreach(var (slot,button) in _dollSlots)
        {
            var entry=simulation.Player.Equipment.TryGetValue(slot,out var id)?simulation.Player.Inventory.FirstOrDefault(e=>e.ItemId==id):null;
            string word=Names.Slot(slot);word=char.ToUpperInvariant(word[0])+word[1..];
            button.Text=entry is null?word:$"{word}:\n{Names.Item(_session,entry.DefId,entry.Quality)}";
            button.Icon=entry is null?null:_icons.For(entry.DefId);
            button.TooltipText=entry is null?$"{Names.Slot(slot)} · Empty. Drag an item here.":$"{Names.Item(_session,entry.DefId,entry.Quality)} · Click to unequip";
            UiKit.Select(button,entry is not null);

        }
    }
}
