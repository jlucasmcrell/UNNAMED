using Godot;
using System.Text.Json;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;
namespace UNNAMED.Presentation;

public partial class Main
{
    private async Task RequestReviewWgcStill(string directory,string name)
    {
        string label=Path.GetFileName(directory);
        DisplayServer.WindowSetTitle("Otherreach - "+label);
        var pixels=DisplayServer.WindowGetSize();
        File.WriteAllText(Path.Combine(directory,name+".wgc_request.json"),JsonSerializer.Serialize(new {
            name,window_title="^Otherreach - "+System.Text.RegularExpressions.Regex.Escape(label)+"$",width=pixels.X,height=pixels.Y }));
        ulong deadline=Time.GetTicksMsec()+45000;
        while(!File.Exists(Path.Combine(directory,name+".wgc_done.json")))
        {
            if(File.Exists(Path.Combine(directory,name+".wgc_failed.txt")))throw new InvalidDataException(File.ReadAllText(Path.Combine(directory,name+".wgc_failed.txt")));
            if(Time.GetTicksMsec()>deadline)throw new TimeoutException("Window screenshot timeout: "+name);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task RunInventoryReview(string directory,Camera3D camera)
    {
        _rcd3Driving=true;
        var sim=_session.Simulation!;
        var evidence=new List<object>();
        long began=sim.WorldTick;
        var refusals=new List<string>();_session.Subscribe<CommandRejected>(e=>refusals.Add(e.Reason));
        void Frame()
        {var aim=_avatar.GlobalPosition+Vector3.Up;camera.GlobalPosition=aim+new Vector3(2,1.5f,3);camera.LookAt(aim);camera.MakeCurrent();}
        void PixelSize(Vector2I expected)
        {if(DisplayServer.WindowGetSize()!=expected)throw new InvalidDataException("Requested UI pixel size was not applied");}
        async Task Press(string id)
        {
            var root=UiKit.SheetOf(_companionsPanel)!.Root;
            if(!UiKit.Refocus(root,id) || root.GetViewport().GuiGetFocusOwner() is not Button button)throw new InvalidDataException("Missing real inspector control "+id);
            int before=refusals.Count;button.EmitSignal(Button.SignalName.Pressed);await Rcd3Wait(.4);
            if(refusals.Count!=before)throw new InvalidDataException(refusals[^1]);
        }
        foreach(var size in new[]{new Vector2I(1920,1080),new Vector2I(1280,720)})
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);DisplayServer.WindowSetSize(size);
            CloseScreens();_inventory.Open(null);await Rcd3Wait(2);PixelSize(size);Frame();
            var list=(ScrollContainer)_inventory.FindChild("PackListViewport",true,false);
            var details=(ScrollContainer)_inventory.FindChild("ItemDetailViewport",true,false);
            var sheetFrame=UiKit.SheetOf(_inventory)!;
            var panel=sheetFrame.Panel.GetGlobalRect();
            if(panel.Size.Y>sheetFrame.Room+1)throw new InvalidDataException("Inventory extends outside its reserved HUD band");
            var bounds=list.GetGlobalRect();int scroll=list.ScrollVertical;
            var rows=_inventory.FindChildren("*",nameof(Control),true,false).OfType<Control>().Where(c=>c.HasMeta("item_definition")).Take(2).ToArray();
            if(rows.Length<2)throw new InvalidDataException("Inventory gate requires two real carried items");
            for(int i=0;i<rows.Length;i++)
            {
                rows[i].EmitSignal(Control.SignalName.MouseEntered);await Rcd3Wait(.5);
                if(!list.GetGlobalRect().IsEqualApprox(bounds) || list.ScrollVertical!=scroll || bounds.Intersects(details.GetGlobalRect()))
                    throw new InvalidDataException("Item hover resized/scrolled/overlapped the pack");
                await Rcd3Shot(directory,$"inventory_{size.Y}_item_{i}");
            }
            var worn=sim.Player.Equipment.Values.ToHashSet();
            if(sim.Pack.Containers.SelectMany(c=>c.Stacks).Any(e=>worn.Contains(e.ItemId)))throw new InvalidDataException("Worn gear still appears in pack");
            evidence.Add(new {stage="inventory",width=size.X,height=size.Y,panel_rect=panel.ToString(),reserved_height=sheetFrame.Room,pack_rect=bounds.ToString(),details_rect=details.GetGlobalRect().ToString(),scroll,
                pack_used=sim.Pack.Used,worn=worn.Count,weight=sim.Player.CarriedGrams,body=_avatarModel,tick=sim.WorldTick});
        }
        if(OS.GetEnvironment("OTHERREACH_RCD6_EXISTING_PARTY_UI")=="1")
        {
            const string id="npc.ashen_hollow.tavar_orr";
            if(!sim.Companions.Any(c=>c.NpcId==id))throw new InvalidDataException("Copied owner save has no Tavar");
            CloseScreens();ulong through=Time.GetTicksMsec()+6000;
            while(!_controller.InTalkReach(sim.Npcs.Single(n=>n.Id==id)) && Time.GetTicksMsec()<through)
            {
                var npc=sim.Npcs.Single(n=>n.Id==id);var body=_controller.Authoritative;
                var toward=new Vector3(npc.Body.XMm-body.XMm,0,npc.Body.ZMm-body.ZMm).Normalized();
                _controller.SteerWorld(toward,Gait.Walk,_camera);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);await Rcd3Wait(.4);
            var sheet=sim.CaptureRecord().PartySheets.Single(s=>s.NpcId==id);
            bool Equippable(UNNAMED.World.InventoryEntry e) => sim.Setup.Items.Catalog.Find(e.DefId) is {Slot:not null} definition
                && definition.Weapon is not {Ranged:true} && EquipmentRules.Refusal(definition,sheet.Progression,sim.Setup.Progression) is null;
            // Use an actual wearable from this copied save, even when all its gear is worn.
            // Unequip and restore through the same commands as the inventory UI.
            var candidates=sim.Player.Inventory.Where(e=>!sim.Player.Bags.Contains(e.ItemId)
                && sim.Setup.Items.Catalog.Find(e.DefId) is {NoDrop:false}).ToArray();
            var entry=candidates.OrderByDescending(Equippable)
                .ThenBy(e=>sim.Player.Equipment.ContainsValue(e.ItemId))
                .ThenBy(e=>sim.Setup.Items.Catalog.Find(e.DefId)!.WeightGrams*e.Count).First();
            EquipSlot? restoreSlot=sim.Player.Equipment.FirstOrDefault(e=>e.Value==entry.ItemId) is var previous
                && previous.Value==entry.ItemId ? previous.Key : null;
            if(restoreSlot is { } originalSlot)
            {
                int before=refusals.Count;_session.Submit(new UnequipCommand(sim.PlayerId,originalSlot));await Rcd3Wait(.4);
                if(refusals.Count!=before || sim.Player.Equipment.ContainsValue(entry.ItemId))throw new InvalidDataException("Cannot make room for the real wearable: "+string.Join("; ",refusals.Skip(before)));
            }
            ShowParty(id);await Rcd3Wait(.5);
            await Press($"{id}/givepack/{entry.ItemId}");
            if(!sim.PartySheet(id)!.Belongings.Any(e=>e.ItemId==entry.ItemId))throw new InvalidDataException("Offload failed");
            bool canEquip=Equippable(entry);
            foreach(var size in new[]{new Vector2I(1920,1080),new Vector2I(1280,720)})
            {
                DisplayServer.WindowSetSize(size);await Rcd3Wait(.7);PixelSize(size);Frame();
                await Rcd3Shot(directory,$"companion_{size.Y}_offload");
                if(canEquip)
                {
                    await Press($"{id}/equip/{entry.ItemId}");await Rcd3Shot(directory,$"companion_{size.Y}_equipped");
                    var slot=sim.Setup.Items.Catalog.Find(entry.DefId)!.Slot!.Value;
                    await Press($"{id}/unequip/{slot}");
                }
                await Press($"{id}/dismiss");await Rcd3Shot(directory,$"companion_{size.Y}_dismiss_warning");
                await Press($"{id}/reviewkept");
            }
            await Press($"{id}/takepack/{entry.ItemId}");
            var returned=sim.Player.Inventory.Single(e=>e.ItemId==entry.ItemId);
            if(returned.Count!=entry.Count || returned.Quality!=entry.Quality || returned.Notable!=entry.Notable || returned.Wear!=entry.Wear)
                throw new InvalidDataException("Transfer changed the item instance");
            if(restoreSlot is { } original)
            {
                _session.Submit(new EquipCommand(sim.PlayerId,entry.ItemId));await Rcd3Wait(.4);
                if(!sim.Player.Equipment.TryGetValue(original,out var restored) || restored!=entry.ItemId)throw new InvalidDataException("Player loadout was not restored");
            }
            evidence.Add(new {stage="companion",npc=id,item=entry.ItemId.Value,entry.DefId,equipped=canEquip,player_loadout_restored=restoreSlot is not null,normal_commands=true,
                distance_mm=sim.Companions.Single(c=>c.NpcId==id).Body.XMm-sim.Player.Body.XMm,tick=sim.WorldTick});
        }
        if(sim.WorldTick<=began)throw new InvalidDataException("UI paused the world");
        File.WriteAllText(Path.Combine(directory,"inventory_review.json"),JsonSerializer.Serialize(new {from_tick=began,to_tick=sim.WorldTick,evidence},new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(directory,"complete.json"),"{\"status\":\"UI_REQUIRES_VISUAL_REVIEW\"}");
    }
}
