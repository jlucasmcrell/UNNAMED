using Godot;
using System.Collections.Immutable;
using System.Text.Json;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;
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
        if(OS.GetEnvironment("OTHERREACH_HERITAGE_REVIEW")=="step5")
        {
            await HeritageRecorded(directory);
            return;
        }
        if(OS.GetEnvironment("OTHERREACH_HERITAGE_REVIEW")=="peoples")
        {
            await HeritageKal(directory);
            return;
        }
        if(OS.GetEnvironment("OTHERREACH_HERITAGE_REVIEW")=="kalkera")
        {
            await HeritageKera(directory);
            return;
        }
        if(OS.GetEnvironment("OTHERREACH_HERITAGE_REVIEW") is "kaljoint" or "kaljoint2" or "kaljoint3")
        {
            await HeritageJoint(directory);
            return;
        }

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
        await HeritageWalkTo(Kera);
        OpenTrade(Kera);await Rcd3Wait(2);
        if(_inventory.OpenTrader!=Kera)throw new InvalidDataException("Kera's trade did not open: "+string.Join("; ",refusals));
        Row().EmitSignal(Control.SignalName.MouseEntered);await Rcd3Wait(1);
        var sell=Row().FindChildren("*",nameof(Button),true,false).OfType<Button>().FirstOrDefault(b=>b.Text=="Sell")
                 ?? throw new InvalidDataException("No Sell on the roll at Kera's");
        if(!sell.Disabled)throw new InvalidDataException("Kera's Sell on the Veth roll is not disabled");
        string refusal=sell.TooltipText;await Rcd3Wait(5);
        if(!sim.Player.Inventory.Any(e=>e.ItemId==roll.ItemId))throw new InvalidDataException("The refused sale moved the roll");
        CloseScreens();await Rcd3Wait(1);

        // 4. Renn, the Veth keeper, reads the roll's open line.
        const string Renn="npc.ashen_hollow.renn_vale";
        await HeritageWalkTo(Renn);   // out of the forge shed and into the inn, where he keeps it from behind the counter
        _session.Submit(new TalkCommand(sim.PlayerId,Renn));await Rcd3Wait(3);
        if(sim.Conversation?.Replies.Any(r=>r.Id=="roll")!=true)throw new InvalidDataException("Renn offers no reading of the roll");
        _session.Submit(new ChooseCommand(sim.PlayerId,"roll"));await Rcd3Wait(10);
        string node=sim.Conversation?.NodeId ?? "";
        _session.Submit(new ChooseCommand(sim.PlayerId,"back"));await Rcd3Wait(1);
        _session.Submit(new LeaveCommand(sim.PlayerId));await Rcd3Wait(2);
        Show(_journal);await Rcd3Wait(6);CloseScreens();
        var after=sim.Quests.Single(q=>q.Id=="quest.heritage.what_was_kept");

        // 5. The road to Cairnwold, and Berrin's lodge book: the other half, released (keeping it wants four timber the new character lacks).
        const string Berrin="npc.cairnwold.berrin_garth";
        // The Old East Road, joined past the waystation's yards and left short of its end at the village green (the CPU route probe's legs).
        var road=sim.Setup.Layout.Roads.Single(r=>r.Id.EndsWith("old_east_road",StringComparison.Ordinal)).Points;
        var east=road.Skip(2).Take(road.Length-4).ToImmutableArray();
        await HeritageAlong(east);
        var approach=await HeritageWalkTo(Berrin);   // by day he works in front of the long lodge
        _session.Submit(new TalkCommand(sim.PlayerId,Berrin));await Rcd3Wait(3);
        if(sim.Conversation?.Replies.Any(r=>r.Id=="oath")!=true)throw new InvalidDataException("Berrin offers no reading of the oath");
        _session.Submit(new ChooseCommand(sim.PlayerId,"oath"));await Rcd3Wait(14);
        _session.Submit(new ChooseCommand(sim.PlayerId,"release"));await Rcd3Wait(10);
        string berrinNode=sim.Conversation?.NodeId ?? "";
        _session.Submit(new ChooseCommand(sim.PlayerId,"back"));await Rcd3Wait(1);
        _session.Submit(new LeaveCommand(sim.PlayerId));await Rcd3Wait(2);

        // 6. Back to Renn, who closes the line; the character sheet calls the character its keeper.
        foreach(var (x,z) in Enumerable.Reverse(approach))await HeritageStep(x,z);   // away from his door the way the character came
        await HeritageAlong(east.Reverse().ToImmutableArray());
        await HeritageWalkTo(Renn);
        _session.Submit(new TalkCommand(sim.PlayerId,Renn));await Rcd3Wait(3);
        if(sim.Conversation?.Replies.Any(r=>r.Id=="record")!=true)throw new InvalidDataException("Renn offers no closing of the line");
        _session.Submit(new ChooseCommand(sim.PlayerId,"record"));await Rcd3Wait(12);
        string closing=sim.Conversation?.NodeId ?? "";
        _session.Submit(new ChooseCommand(sim.PlayerId,"back"));await Rcd3Wait(1);
        _session.Submit(new LeaveCommand(sim.PlayerId));await Rcd3Wait(2);
        Show(_character);await Rcd3Wait(7);CloseScreens();
        Show(_journal);await Rcd3Wait(5);CloseScreens();
        var done=sim.Quests.Single(q=>q.Id=="quest.heritage.what_was_kept");

        File.WriteAllText(Path.Combine(directory,"recording.finished"),DateTime.Now.ToString("o"));
        File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
            status="HERITAGE_VETH_REALTIME_REQUIRES_CLAUDE_REVIEW",quest=quest.Title,roll=roll.ItemId.Value,drop_button=drop,refusal,
            renn_node=node,berrin_node=berrinNode,renn_closing=closing,quest_status=done.Status.ToString(),titles=sim.Titles,objective_after=after.Objectives.FirstOrDefault(o=>o.Status==UNNAMED.Domain.Quests.ObjectiveStatus.Active)?.Description,
            inventory=sim.Player.Inventory.Select(e=>new {e.DefId,e.Count}),world_tick=sim.WorldTick,engine_max_fps=Engine.MaxFps,
            profile="Fresh isolated profile; normal creator Begin; ordinary steering and commands; no desktop input"},new JsonSerializerOptions{WriteIndented=true}));
        deadline=Time.GetTicksMsec()+20000;
        while(!File.Exists(Path.Combine(directory,"recording.stopped")) && Time.GetTicksMsec()<deadline)
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>
    /// The last step alone, from a save left outside the inn with the oath released (OTHERREACH_HERITAGE_REVIEW=step5): Renn closes the line, and
    /// the character sheet calls the character its keeper. The road there and Berrin's book are the full route's (heritage_veth_gate16).
    /// </summary>
    private async Task HeritageRecorded(string directory)
    {
        var sim=_session.Simulation!;
        const string Renn="npc.ashen_hollow.renn_vale";
        await HeritageWalkTo(Renn);
        _session.Submit(new TalkCommand(sim.PlayerId,Renn));await Rcd3Wait(3);
        if(sim.Conversation?.Replies.Any(r=>r.Id=="record")!=true)throw new InvalidDataException("Renn offers no closing of the line");
        _session.Submit(new ChooseCommand(sim.PlayerId,"record"));await Rcd3Wait(12);
        string closing=sim.Conversation?.NodeId ?? "";
        _session.Submit(new ChooseCommand(sim.PlayerId,"back"));await Rcd3Wait(1);
        _session.Submit(new LeaveCommand(sim.PlayerId));await Rcd3Wait(2);
        Show(_character);await Rcd3Wait(8);CloseScreens();
        Show(_journal);await Rcd3Wait(5);CloseScreens();
        var done=sim.Quests.Single(q=>q.Id=="quest.heritage.what_was_kept");
        File.WriteAllText(Path.Combine(directory,"recording.finished"),DateTime.Now.ToString("o"));
        File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
            status="HERITAGE_VETH_STEP5_REALTIME_REQUIRES_CLAUDE_REVIEW",renn_closing=closing,quest_status=done.Status.ToString(),titles=sim.Titles,
            world_tick=sim.WorldTick,engine_max_fps=Engine.MaxFps,profile="Saved step-5 state outside the inn; ordinary steering and commands; no desktop input"},
            new JsonSerializerOptions{WriteIndented=true}));
        ulong deadline=Time.GetTicksMsec()+20000;
        while(!File.Exists(Path.Combine(directory,"recording.stopped")) && Time.GetTicksMsec()<deadline)
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>
    /// The playable peoples' heritage in the creator (OTHERREACH_HERITAGE_REVIEW=peoples), recorded from before Begin: Siann, Orenth and Kal in
    /// turn - the People card, a body, and the Confirm step whose Carries lists the people's heritage - then Begin as a Kal.
    /// </summary>
    private async void HeritageCreatorTour(string directory)
    {
        try
        {
            // This runs as the window first opens: give the engine its own title first, then name the window for the capture.
            await Rcd3Wait(2);
            DisplayServer.WindowSetTitle("Otherreach - "+Path.GetFileName(directory));
            await Rcd3Wait(1);
            File.WriteAllText(Path.Combine(directory,"record_ready.json"),JsonSerializer.Serialize(new {window_title="^Otherreach - "+Path.GetFileName(directory)+"$"}));
            ulong deadline=Time.GetTicksMsec()+90000;
            while(!File.Exists(Path.Combine(directory,"recording.started")))
            {
                if(Time.GetTicksMsec()>deadline)throw new TimeoutException("The window recording never started");
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            await Rcd3Wait(2);
            foreach(string people in new[]{"people.siann","people.orenth","people.kal"})
            {
                string body=_session.Characters.OfferedBodies(people).FirstOrDefault()?.Id ?? CharacterSpec.DefaultBodyPreset;
                _creator.Use(_creator.Draft with { PeopleId=people, BodyPresetId=body });
                _creator.Go(CreatorStep.People);await Rcd3Wait(5);
                _creator.Go(CreatorStep.Body);await Rcd3Wait(4);
                _creator.Go(CreatorStep.Confirm);await Rcd3Wait(2);
                // Carries closes the Confirm step, its Heritage last: scrolled into view as the player's wheel would.
                foreach(var scroll in _creator.FindChildren("*",nameof(ScrollContainer),true,false).OfType<ScrollContainer>())
                    scroll.ScrollVertical=(int)scroll.GetVScrollBar().MaxValue;
                await Rcd3Wait(6);
            }
            if(!_creator.PressBegin())throw new InvalidOperationException("The creator refused Begin as a Kal");
            _mhReviewStep=7;_mhReviewNext=Time.GetTicksMsec()+10000;
        }
        catch(Exception error)
        {
            File.WriteAllText(Path.Combine(directory,"failed.txt"),error.ToString());
            CaptureQuit.At(this,3);
        }
        finally { _mhReviewBusy=false; }
    }

    /// <summary>A Kal begun from the creator: the Finishing Tool in the pack, and a sale of it refused by a trader of no people at Cairnwold.</summary>
    private async Task HeritageKal(string directory)
    {
        var sim=_session.Simulation!;
        var refusals=new List<string>();_session.Subscribe<CommandRejected>(e=>refusals.Add(e.Reason));
        if(sim.Player.Identity.PeopleId!="people.kal")throw new InvalidDataException("The tour did not begin as a Kal");
        var tool=sim.Player.Inventory.SingleOrDefault(e=>e.DefId=="item.heritage.finishing_tool") ?? throw new InvalidDataException("A new Kal carries no Finishing Tool");
        Control Row()=>_inventory.FindChildren("*","",true,false).OfType<Control>()
            .First(c=>c.HasMeta("item_reference") && c.GetMeta("item_reference").AsString()==tool.ItemId.Value);
        _inventory.Open(null);await Rcd3Wait(1.5);
        Row().EmitSignal(Control.SignalName.MouseEntered);await Rcd3Wait(2);
        var details=(ScrollContainer)_inventory.FindChild("ItemDetailViewport",true,false);
        details.ScrollVertical=(int)details.GetVScrollBar().MaxValue;await Rcd3Wait(5);
        bool drop=Row().FindChildren("*",nameof(Button),true,false).OfType<Button>().Any(b=>b.Text=="Drop");
        CloseScreens();await Rcd3Wait(1);

        const string Durn="npc.cairnwold.durn_skell";
        var road=sim.Setup.Layout.Roads.Single(r=>r.Id.EndsWith("old_east_road",StringComparison.Ordinal)).Points;
        await HeritageAlong(road.Skip(2).Take(road.Length-4).ToImmutableArray());
        await HeritageWalkTo(Durn);
        OpenTrade(Durn);await Rcd3Wait(2);
        if(_inventory.OpenTrader!=Durn)throw new InvalidDataException("Durn's trade did not open: "+string.Join("; ",refusals));
        Row().EmitSignal(Control.SignalName.MouseEntered);await Rcd3Wait(1);
        var sell=Row().FindChildren("*",nameof(Button),true,false).OfType<Button>().FirstOrDefault(b=>b.Text=="Sell")
                 ?? throw new InvalidDataException("No Sell on the Finishing Tool at Durn's");
        if(!sell.Disabled)throw new InvalidDataException("Durn's Sell on the Kal tool is not disabled");
        string refusal=sell.TooltipText;
        bool worth=_inventory.FindChildren("*",nameof(Label),true,false).OfType<Label>().Any(l=>l.Text.Equals("Worth",StringComparison.OrdinalIgnoreCase));
        await Rcd3Wait(6);
        CloseScreens();await Rcd3Wait(1);
        File.WriteAllText(Path.Combine(directory,"recording.finished"),DateTime.Now.ToString("o"));
        File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
            status="HERITAGE_PEOPLES_REALTIME_REQUIRES_CLAUDE_REVIEW",people=sim.Player.Identity.PeopleId,tool=tool.DefId,drop_button=drop,sell_disabled=true,refusal,worth_shown=worth,
            inventory=sim.Player.Inventory.Select(e=>new {e.DefId,e.Count}),world_tick=sim.WorldTick,
            profile="Fresh isolated profile; the creator toured Siann, Orenth and Kal, then Begin as a Kal; ordinary steering and commands; no desktop input"},
            new JsonSerializerOptions{WriteIndented=true}));
        ulong deadline=Time.GetTicksMsec()+20000;
        while(!File.Exists(Path.Combine(directory,"recording.stopped")) && Time.GetTicksMsec()<deadline)
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>
    /// The Unfinished Joint for a new Kal, in leases of under ten minutes, each saved for the next. The first (OTHERREACH_HERITAGE_REVIEW=kaljoint):
    /// Kera reads the tool's grip, the road east to the Brannock hold in the south crags, and Berrin's book on the taken stone. The second
    /// (kaljoint2): the road back to Ashen Hollow. The third (kaljoint3): Kera, to trace the stone east (the new character has no iron for the
    /// cramp) and have the tool marked; the character sheet names the character in the hold.
    /// </summary>
    private async Task HeritageJoint(string directory)
    {
        var sim=_session.Simulation!;
        const string Quest="quest.heritage.unfinished_joint",Kera="npc.ashen_hollow.kera_voss",Berrin="npc.cairnwold.berrin_garth";
        if(sim.Player.Identity.PeopleId!="people.kal")throw new InvalidDataException("The run did not begin as a Kal");
        string Open()=>string.Join(",",sim.Quests.Single(q=>q.Id==Quest).Objectives.Where(o=>o.Status==UNNAMED.Domain.Quests.ObjectiveStatus.Active).Select(o=>o.Id));
        var nodes=new List<string>();
        async Task Say(string npc,params string[] replies)
        {
            _session.Submit(new TalkCommand(sim.PlayerId,npc));await Rcd3Wait(3);
            foreach(string reply in replies)
            {
                if(sim.Conversation?.Replies.Any(r=>r.Id==reply)!=true)throw new InvalidDataException($"{npc} offers no '{reply}' (quest at {Open()})");
                _session.Submit(new ChooseCommand(sim.PlayerId,reply));await Rcd3Wait(reply=="back"?1:14);
                if(reply!="back")nodes.Add(sim.Conversation?.NodeId ?? "");
            }
            _session.Submit(new LeaveCommand(sim.PlayerId));await Rcd3Wait(2);
        }
        Show(_journal);await Rcd3Wait(6);CloseScreens();await Rcd3Wait(1);
        var road=sim.Setup.Layout.Roads.Single(r=>r.Id.EndsWith("old_east_road",StringComparison.Ordinal)).Points;
        var east=road.Skip(2).Take(road.Length-4).ToImmutableArray();
        string branch,mode=OS.GetEnvironment("OTHERREACH_HERITAGE_REVIEW");
        // A lease ends saved, its journal shown, for the next lease to continue from.
        async Task Saved(string status)
        {
            _session.Save(SaveSlots.Manual("heritage_kaljoint"));await Rcd3Wait(2);
            Show(_journal);await Rcd3Wait(5);CloseScreens();
            File.WriteAllText(Path.Combine(directory,"recording.finished"),DateTime.Now.ToString("o"));
            File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
                status,people=sim.Player.Identity.PeopleId,nodes,quest_open=Open(),tool_carried=sim.Player.Inventory.Any(e=>e.DefId=="item.heritage.finishing_tool"),
                world_tick=sim.WorldTick,profile="Ordinary steering and commands; no desktop input; saved as heritage_kaljoint for the next lease"},
                new JsonSerializerOptions{WriteIndented=true}));
            ulong half=Time.GetTicksMsec()+20000;
            while(!File.Exists(Path.Combine(directory,"recording.stopped")) && Time.GetTicksMsec()<half)
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
        if(mode is "kaljoint2" or "kaljoint3")
        {
            branch=Open();
            if(branch!="o_finish,o_trace")throw new InvalidDataException("The save is not at the joint's choice: "+branch);
            if(mode=="kaljoint2")goto back;
            goto kera;
        }

        // 1. Kera reads Hesta's grip on the handle.
        var shed=await HeritageWalkTo(Kera);
        await Say(Kera,"grip","back");
        foreach(var (x,z) in Enumerable.Reverse(shed))await HeritageStep(x,z);   // out of the forge shed the way the character came in

        // 2. The road east, and the old Brannock hold in the south crags.
        await HeritageAlong(east);
        var crags=sim.Setup.Layout.Locations.Single(l=>l.Id=="location.cairnwold.the_crags");
        bool AtHold()=>Open()!="o_hold";
        // The crag line itself is rock: the walk goes to its foot, on the village side, until the hold is found.
        foreach(long back in new[]{0L,15000L,30000L})
        {
            if(AtHold())break;
            await HeritageRoute(crags.XMm,crags.ZMm+back,AtHold,Gait.Run);
        }
        if(!AtHold())throw new InvalidDataException("The walk did not find the Brannock hold");
        _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);await Rcd3Wait(6);   // the open joint over the hold's mouth

        // 3. Berrin's book: the dressed stone carted east and sold.
        await HeritageWalkTo(Berrin);
        await Say(Berrin,"crag_stone","back");
        branch=Open();
        await Saved("HERITAGE_KAL_JOINT_LEASE1_REALTIME_REQUIRES_CLAUDE_REVIEW");
        return;

        // 4. The road back to Ashen Hollow.
        back:
        await HeritageAlong(east.Reverse().ToImmutableArray());
        await Saved("HERITAGE_KAL_JOINT_LEASE2_REALTIME_REQUIRES_CLAUDE_REVIEW");
        return;

        // 5. Kera: trace the stone east, and the tool marked beside Hesta's.
        kera:
        await HeritageWalkTo(Kera);
        await Say(Kera,"joint","trace","back","mark","back");
        Show(_character);await Rcd3Wait(8);CloseScreens();
        Show(_journal);await Rcd3Wait(5);CloseScreens();
        var done=sim.Quests.Single(q=>q.Id==Quest);

        File.WriteAllText(Path.Combine(directory,"recording.finished"),DateTime.Now.ToString("o"));
        File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
            status="HERITAGE_KAL_JOINT_LEASE3_REALTIME_REQUIRES_CLAUDE_REVIEW",people=sim.Player.Identity.PeopleId,nodes,branch_after_berrin=branch,
            quest_status=done.Status.ToString(),titles=sim.Titles,tool_carried=sim.Player.Inventory.Any(e=>e.DefId=="item.heritage.finishing_tool"),
            world_tick=sim.WorldTick,profile="The second lease's save at Ashen Hollow (heritage_kaljoint); ordinary steering and commands; no desktop input"},
            new JsonSerializerOptions{WriteIndented=true}));
        ulong deadline=Time.GetTicksMsec()+20000;
        while(!File.Exists(Path.Combine(directory,"recording.stopped")) && Time.GetTicksMsec()<deadline)
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>
    /// A new Kal on the Kal body at Kera's counter (OTHERREACH_HERITAGE_REVIEW=kalkera), one short lease: the Finishing Tool in the pack, then
    /// Kera, a Kal, reading a Kal's tool - The Unfinished Joint's first step.
    /// </summary>
    private async Task HeritageKera(string directory)
    {
        var sim=_session.Simulation!;
        const string Kera="npc.ashen_hollow.kera_voss";
        if(sim.Player.Identity.PeopleId!="people.kal")throw new InvalidDataException("The run did not begin as a Kal");
        var tool=sim.Player.Inventory.SingleOrDefault(e=>e.DefId=="item.heritage.finishing_tool") ?? throw new InvalidDataException("A new Kal carries no Finishing Tool");
        _inventory.Open(null);await Rcd3Wait(1.5);
        _inventory.FindChildren("*","",true,false).OfType<Control>()
            .First(c=>c.HasMeta("item_reference") && c.GetMeta("item_reference").AsString()==tool.ItemId.Value).EmitSignal(Control.SignalName.MouseEntered);
        await Rcd3Wait(5);CloseScreens();await Rcd3Wait(1);
        await HeritageWalkTo(Kera);
        _session.Submit(new TalkCommand(sim.PlayerId,Kera));await Rcd3Wait(3);
        if(sim.Conversation?.Replies.Any(r=>r.Id=="grip")!=true)throw new InvalidDataException("Kera offers no reading of the grip");
        _session.Submit(new ChooseCommand(sim.PlayerId,"grip"));await Rcd3Wait(18);
        string node=sim.Conversation?.NodeId ?? "";
        _session.Submit(new ChooseCommand(sim.PlayerId,"back"));await Rcd3Wait(1);
        _session.Submit(new LeaveCommand(sim.PlayerId));await Rcd3Wait(4);
        File.WriteAllText(Path.Combine(directory,"recording.finished"),DateTime.Now.ToString("o"));
        File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
            status="HERITAGE_KAL_KERA_REALTIME_REQUIRES_CLAUDE_REVIEW",people=sim.Player.Identity.PeopleId,body=sim.Player.Identity.BodyPresetId,node,
            world_tick=sim.WorldTick,profile="Fresh isolated profile; creator Begin as a Kal on the Kal body; ordinary steering and commands; no desktop input"},
            new JsonSerializerOptions{WriteIndented=true}));
        ulong deadline=Time.GetTicksMsec()+20000;
        while(!File.Exists(Path.Combine(directory,"recording.stopped")) && Time.GetTicksMsec()<deadline)
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>Along a road from its point nearest the character, at a run; the way to that point is planned like any other.</summary>
    private async Task HeritageAlong(ImmutableArray<(long XMm,long ZMm)> points)
    {
        var body=_controller.Authoritative;
        int start=Enumerable.Range(0,points.Length).MinBy(i=>Math.Abs(points[i].XMm-body.XMm)+Math.Abs(points[i].ZMm-body.ZMm));
        await HeritageRoute(points[start].XMm,points[start].ZMm,()=>false);
        await HeritageStep(points[start].XMm,points[start].ZMm);
        // A road point a verge or a cart stands on is passed, not reached: the next one carries on from wherever the walk got to.
        foreach(var (x,z) in points.Skip(start+1))await HeritageStep(x,z,Gait.Run,required:false);
    }

    /// <summary>
    /// The way a person would go to a point, from the world's own navigation grid - doors count as passable, since the walk opens them - until
    /// <paramref name="arrived"/> says the walk is done.
    /// </summary>
    private async Task<List<(long X,long Z)>> HeritageRoute(long xMm,long zMm,Func<bool> arrived,Gait gait=Gait.Walk)
    {
        var walked=new List<(long X,long Z)>{(_controller.Authoritative.XMm,_controller.Authoritative.ZMm)};
        var sim=_session.Simulation!;
        var nav=sim.Navigation;
        var gates=nav.Gates.ToDictionary(g=>g.Key,g=>g.Open);
        var query=new NavQuery(nav.Grid,g=>g.GateKey is { } key && gates.GetValueOrDefault(key),sim.Setup.Navigation,new NavScratch(),null);
        var start=_controller.Authoritative;
        var plan=NavSearch.Plan(query,new NavAgent(sim.Setup.Navigation.PersonClass,true),new NavPoint(start.XMm,start.ZMm),new NavPoint(xMm,zMm));
        if(plan.Outcome!=NavOutcome.Found)return walked;
        foreach(var corner in plan.Corners.Skip(1))
        {
            if(arrived())break;
            await HeritageStep(corner.XMm,corner.ZMm,gait,arrived);
            walked.Add((corner.XMm,corner.ZMm));
        }
        return walked;
    }

    /// <summary>Walk the character straight to a point, with ordinary steering, opening a closed door in the way and stepping round the rest.</summary>
    private async Task HeritageStep(long xMm,long zMm,Gait gait=Gait.Walk,Func<bool>? arrived=null,bool required=true)
    {
        ulong through=Time.GetTicksMsec()+(required?45000UL:40000UL),stalledSince=Time.GetTicksMsec();
        long lastX=_controller.Authoritative.XMm,lastZ=_controller.Authoritative.ZMm;int sidestep=0;
        while(Math.Abs(_controller.Authoritative.XMm-xMm)+Math.Abs(_controller.Authoritative.ZMm-zMm)>700 && arrived?.Invoke()!=true)
        {
            if(Time.GetTicksMsec()>through)
            {
                if(required)throw new TimeoutException($"Could not walk to ({xMm}, {zMm})");
                break;
            }
            var body=_controller.Authoritative;
            _controller.SteerWorld(new Vector3(xMm-body.XMm,0,zMm-body.ZMm).Normalized(),gait,_camera);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(Math.Abs(body.XMm-lastX)+Math.Abs(body.ZMm-lastZ)>150){lastX=body.XMm;lastZ=body.ZMm;stalledSince=Time.GetTicksMsec();}
            else if(Time.GetTicksMsec()-stalledSince>1500)
            {
                // A closed door is opened as the interact key would; anything else is stepped along, alternating sides.
                var toward=new Vector3(xMm-body.XMm,0,zMm-body.ZMm).Normalized();
                if(_controller.FocusOn(_camera) is { Kind: FocusKind.Door } focus){_controller.Interact(focus.Key);await Rcd3Wait(1.5);}
                else
                {
                    var aside=new Vector3(-toward.Z,0,toward.X)*(sidestep++%2==0?1:-1);
                    ulong until=Time.GetTicksMsec()+800+400*(ulong)(sidestep/2);
                    while(Time.GetTicksMsec()<until){_controller.SteerWorld(aside,gait,_camera);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
                }
                stalledSince=Time.GetTicksMsec();
            }
        }
        _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);await Rcd3Wait(.5);
    }

    /// <summary>Walk the character to an NPC, by the planned way and then by straight steering, until they are within talking reach.</summary>
    private async Task<List<(long X,long Z)>> HeritageWalkTo(string npcId)
    {
        var sim=_session.Simulation!;
        bool InReach()=>_controller.InTalkReach(sim.Npcs.Single(n=>n.Id==npcId));
        var goal=sim.Npcs.Single(n=>n.Id==npcId).Body;
        var walked=await HeritageRoute(goal.XMm,goal.ZMm,InReach);
        ulong through=Time.GetTicksMsec()+90000;
        while(!InReach())
        {
            if(Time.GetTicksMsec()>through)throw new TimeoutException("Could not walk to "+npcId);
            var npc=sim.Npcs.Single(n=>n.Id==npcId).Body;
            await HeritageStep(npc.XMm,npc.ZMm,arrived:InReach);
        }
        _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);await Rcd3Wait(1);
        return walked;
    }
}
