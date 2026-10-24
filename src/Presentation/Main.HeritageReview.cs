using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;
namespace UNNAMED.Presentation;

/// <summary>
/// Opt-in real-time evidence for the Veth heritage (owner, 2026-10-24; HERITAGE_DESIGN.md), after the normal creator's Begin: the journal
/// with What Was Kept, the Kept Roll in the pack, a sale of it refused by a trader of another people, and Renn reading its open line. It
/// walks with ordinary steering and acts through the same commands and panels the player's input does, while an external window capture
/// records the run.
/// </summary>
public partial class Main
{
    private const string HeritageRoll = "item.heritage.kept_roll";

    private async Task RunHeritageReview(string directory)
    {
        _rcd3Driving=true;
        var sim=_session.Simulation!;
        var refusals=new List<string>();_session.Subscribe<CommandRejected>(e=>refusals.Add(e.Reason));
        DisplayServer.WindowSetTitle("Otherreach - "+Path.GetFileName(directory));
        File.WriteAllText(Path.Combine(directory,"record_ready.json"),JsonSerializer.Serialize(new {window_title="^Otherreach - "+Path.GetFileName(directory)+"$"}));
        ulong deadline=Time.GetTicksMsec()+90000;
        while(!File.Exists(Path.Combine(directory,"recording.started")))
        {
            if(Time.GetTicksMsec()>deadline)throw new TimeoutException("The window recording never started");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
        await Rcd3Wait(3);

        // 1. The journal: every new Veth has taken up What Was Kept.
        var quest=sim.Quests.SingleOrDefault(q=>q.Id=="quest.heritage.what_was_kept") ?? throw new InvalidDataException("A new Veth has no What Was Kept");
        Show(_journal);await Rcd3Wait(6);CloseScreens();await Rcd3Wait(1);

        // 2. The pack: the Kept Roll, its heritage line, and no Drop.
        var roll=sim.Player.Inventory.SingleOrDefault(e=>e.DefId==HeritageRoll) ?? throw new InvalidDataException("A new Veth carries no Kept Roll");
        _inventory.Open(null);await Rcd3Wait(1.5);
        Control Row()=>_inventory.FindChildren("*","",true,false).OfType<Control>()
            .First(c=>c.HasMeta("item_reference") && c.GetMeta("item_reference").AsString()==roll.ItemId.Value);
        Row().EmitSignal(Control.SignalName.MouseEntered);await Rcd3Wait(2);
        var details=(ScrollContainer)_inventory.FindChild("ItemDetailViewport",true,false);
        details.ScrollVertical=(int)details.GetVScrollBar().MaxValue;await Rcd3Wait(5);
        bool drop=Row().FindChildren("*",nameof(Button),true,false).OfType<Button>().Any(b=>b.Text=="Drop");
        CloseScreens();await Rcd3Wait(1);

        // 3. Kera, a Kal trader: the roll offered for sale, and refused.
        const string Kera="npc.ashen_hollow.kera_voss";
        var trail=new List<(long X,long Z)>();
        await HeritageWalkTo(Kera,trail);
        OpenTrade(Kera);await Rcd3Wait(2);
        if(_inventory.OpenTrader!=Kera)throw new InvalidDataException("Kera's trade did not open: "+string.Join("; ",refusals));
        Row().EmitSignal(Control.SignalName.MouseEntered);await Rcd3Wait(1);
        var sell=Row().FindChildren("*",nameof(Button),true,false).OfType<Button>().FirstOrDefault(b=>b.Text=="Sell")
                 ?? throw new InvalidDataException("No Sell on the roll at Kera's");
        int before=refusals.Count;
        sell.EmitSignal(Button.SignalName.Pressed);await Rcd3Wait(5);
        string refusal=refusals.Skip(before).FirstOrDefault() ?? throw new InvalidDataException("Kera took the Veth roll");
        if(!sim.Player.Inventory.Any(e=>e.ItemId==roll.ItemId))throw new InvalidDataException("The refused sale moved the roll");
        CloseScreens();await Rcd3Wait(1);

        // 4. Renn, the Veth keeper, reads the roll's open line.
        const string Renn="npc.ashen_hollow.renn_vale";
        trail.Reverse();
        foreach(var (x,z) in trail)await HeritageWalkBack(x,z);   // out of the forge shed the way the character came in
        await HeritageThroughDoor(Renn);   // Renn keeps the inn from behind its counter
        await HeritageWalkTo(Renn);
        _session.Submit(new TalkCommand(sim.PlayerId,Renn));await Rcd3Wait(3);
        if(sim.Conversation?.Replies.Any(r=>r.Id=="roll")!=true)throw new InvalidDataException("Renn offers no reading of the roll");
        _session.Submit(new ChooseCommand(sim.PlayerId,"roll"));await Rcd3Wait(10);
        string node=sim.Conversation?.NodeId ?? "";
        _session.Submit(new ChooseCommand(sim.PlayerId,"back"));await Rcd3Wait(1);
        _session.Submit(new LeaveCommand(sim.PlayerId));await Rcd3Wait(2);
        Show(_journal);await Rcd3Wait(6);CloseScreens();
        var after=sim.Quests.Single(q=>q.Id=="quest.heritage.what_was_kept");

        File.WriteAllText(Path.Combine(directory,"recording.finished"),DateTime.Now.ToString("o"));
        File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
            status="HERITAGE_VETH_REALTIME_REQUIRES_CLAUDE_REVIEW",quest=quest.Title,roll=roll.ItemId.Value,drop_button=drop,refusal,
            renn_node=node,objective_after=after.Objectives.FirstOrDefault(o=>o.Status==UNNAMED.Domain.Quests.ObjectiveStatus.Active)?.Description,
            inventory=sim.Player.Inventory.Select(e=>new {e.DefId,e.Count}),world_tick=sim.WorldTick,engine_max_fps=Engine.MaxFps,
            profile="Fresh isolated profile; normal creator Begin; ordinary steering and commands; no desktop input"},new JsonSerializerOptions{WriteIndented=true}));
        deadline=Time.GetTicksMsec()+20000;
        while(!File.Exists(Path.Combine(directory,"recording.stopped")) && Time.GetTicksMsec()<deadline)
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>
    /// Go in by the door nearest an NPC who stands indoors: to a point outside it, open it as the interact key would if it is shut, and step
    /// through. Nothing when no door stands near them.
    /// </summary>
    private async Task HeritageThroughDoor(string npcId)
    {
        var sim=_session.Simulation!;
        var npc=sim.Npcs.Single(n=>n.Id==npcId).Body;
        var door=sim.Doors.Where(d=>Math.Abs(d.Site.ClosedFootprint.CenterXMm-npc.XMm)+Math.Abs(d.Site.ClosedFootprint.CenterZMm-npc.ZMm)<15000)
            .OrderBy(d=>Math.Abs(d.Site.ClosedFootprint.CenterXMm-npc.XMm)+Math.Abs(d.Site.ClosedFootprint.CenterZMm-npc.ZMm)).FirstOrDefault();
        if(door is null)return;
        long cx=door.Site.ClosedFootprint.CenterXMm,cz=door.Site.ClosedFootprint.CenterZMm;
        var outward=new Vector2(cx-npc.XMm,cz-npc.ZMm).Normalized();
        await HeritageWalkBack(cx+(long)(outward.X*1800),cz+(long)(outward.Y*1800));
        if(!sim.Doors.Single(d=>d.Site.Key==door.Site.Key).Open){_controller.Interact(door.Site.Key);await Rcd3Wait(1.5);}
        await HeritageWalkBack(cx-(long)(outward.X*1200),cz-(long)(outward.Y*1200));
    }

    /// <summary>Walk the character back to where it stood, with ordinary steering, opening a closed door in the way.</summary>
    private async Task HeritageWalkBack(long xMm,long zMm)
    {
        ulong through=Time.GetTicksMsec()+45000,stalledSince=Time.GetTicksMsec();
        long lastX=_controller.Authoritative.XMm,lastZ=_controller.Authoritative.ZMm;
        while(Math.Abs(_controller.Authoritative.XMm-xMm)+Math.Abs(_controller.Authoritative.ZMm-zMm)>700)
        {
            if(Time.GetTicksMsec()>through)throw new TimeoutException("Could not walk back out");
            var body=_controller.Authoritative;
            _controller.SteerWorld(new Vector3(xMm-body.XMm,0,zMm-body.ZMm).Normalized(),Gait.Walk,_camera);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(Math.Abs(body.XMm-lastX)+Math.Abs(body.ZMm-lastZ)>150){lastX=body.XMm;lastZ=body.ZMm;stalledSince=Time.GetTicksMsec();}
            else if(Time.GetTicksMsec()-stalledSince>1500)
            {
                if(_controller.FocusOn(_camera) is { } focus)_controller.Interact(focus.Key);
                await Rcd3Wait(1.5);stalledSince=Time.GetTicksMsec();
            }
        }
        _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);await Rcd3Wait(.5);
    }

    /// <summary>Walk the character to an NPC with ordinary steering until they are within talking reach, leaving a trail to walk back by.</summary>
    private async Task HeritageWalkTo(string npcId,List<(long X,long Z)>? trail=null)
    {
        var sim=_session.Simulation!;
        ulong through=Time.GetTicksMsec()+90000,stalledSince=Time.GetTicksMsec();
        long lastX=_controller.Authoritative.XMm,lastZ=_controller.Authoritative.ZMm;int sidestep=0;
        while(!_controller.InTalkReach(sim.Npcs.Single(n=>n.Id==npcId)))
        {
            if(Time.GetTicksMsec()>through)throw new TimeoutException("Could not walk to "+npcId);
            var npc=sim.Npcs.Single(n=>n.Id==npcId);var body=_controller.Authoritative;
            var toward=new Vector3(npc.Body.XMm-body.XMm,0,npc.Body.ZMm-body.ZMm).Normalized();
            _controller.SteerWorld(toward,Gait.Walk,_camera);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(trail is not null && (trail.Count==0 || Math.Abs(body.XMm-trail[^1].X)+Math.Abs(body.ZMm-trail[^1].Z)>800))trail.Add((body.XMm,body.ZMm));
            if(Math.Abs(body.XMm-lastX)+Math.Abs(body.ZMm-lastZ)>150){lastX=body.XMm;lastZ=body.ZMm;stalledSince=Time.GetTicksMsec();}
            else if(Time.GetTicksMsec()-stalledSince>1500)
            {
                // A closed door in the way (Kera works inside the forge shed): what the interact key does to what the character faces.
                // Anything else (Renn's counter table): step along it, alternating sides, and come at the NPC again.
                if(_controller.FocusOn(_camera) is { Kind: FocusKind.Door } focus){_controller.Interact(focus.Key);await Rcd3Wait(1.5);}
                else
                {
                    var aside=new Vector3(-toward.Z,0,toward.X)*(sidestep++%2==0?1:-1);
                    ulong until=Time.GetTicksMsec()+800+400*(ulong)(sidestep/2);
                    while(Time.GetTicksMsec()<until){_controller.SteerWorld(aside,Gait.Walk,_camera);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
                }
                stalledSince=Time.GetTicksMsec();
            }
        }
        _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);await Rcd3Wait(1);
    }
}
