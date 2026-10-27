using Godot;
using System.Text.Json;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Art;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    private bool _sedgeReviewStarted;

    private async void RunSedgeBoatReview(string directory)
    {
        Directory.CreateDirectory(directory);_rcd3Driving=true;
        try
        {
            var boat=_hollow.FindChild("WorkingPunt0",true,false) as Node3D
                ?? throw new InvalidOperationException("Working punt missing");
            var source=_camera.Camera;
            var eye=new Camera3D { Fov=60,Near=source.Near,Far=source.Far,CullMask=source.CullMask,
                Environment=source.Environment,Attributes=source.Attributes };
            AddChild(eye);eye.MakeCurrent();
            for (int view=0;view<2;view++)
            {
                ulong began=Time.GetTicksMsec();
                while (Time.GetTicksMsec()-began<2000)
                {
                    var aim=boat.GlobalPosition+Vector3.Up*.5f;
                    eye.GlobalPosition=aim+(view==0?new Vector3(-4,2,4):new Vector3(5,1.3f,0));eye.LookAt(aim);
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
                SaveScreenshot(directory,view==0?"three_quarters":"side");
            }
            CaptureQuit.At(this,0);
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(directory,"FAILED.txt"),e.ToString());CaptureQuit.At(this,3); }
    }

    private async void RunSedgeLifeReview(string directory)
    {
        Directory.CreateDirectory(directory);
        _rcd3Driving = true;
        var sim = _session.Simulation!;
        var passages = new List<PassageTaken>();
        _session.Subscribe<PassageTaken>(e => passages.Add(e));
        async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        string? rejection=null;
        _session.Subscribe<CommandRejected>(e => rejection=e.Reason);
        void Submit(GameCommand command)
        {
            rejection=null;_session.Submit(command);
            if (rejection is not null) throw new InvalidOperationException(rejection);
        }
        async Task Shot(string name) { await Wait(2); SaveScreenshot(directory,name); }
        try
        {
            if (sim.ActiveLayout.Id != "region.sedge_levels") throw new InvalidOperationException("Sedge life review requires Sedge");
            await Wait(1);
            Submit(new TalkCommand(sim.PlayerId,"npc.sedge.sella_tally"));
            Submit(new ChooseCommand(sim.PlayerId,"survey"));
            await Shot("quest_offer");
            Submit(new ChooseCommand(sim.PlayerId,"go"));
            Show(_journal); await Shot("quest_journal"); CloseScreens();
            if (sim.Conversation is not null) Submit(new LeaveCommand(sim.PlayerId));
            ulong began = Time.GetTicksMsec();
            while (Math.Abs(sim.Player.Body.ZMm-1098500)>100 || Math.Abs(sim.Player.Body.XMm-4998500)>100)
            {
                if (Time.GetTicksMsec()-began>30_000) throw new InvalidOperationException("Could not walk to the ferry keeper");
                var b=sim.Player.Body;double dx=4998500-b.XMm,dz=1098500-b.ZMm,n=Math.Sqrt(dx*dx+dz*dz);
                Submit(new MoveCommand(sim.PlayerId,new MoveIntent((int)(dx/n*1000),(int)(dz/n*1000),Gait.Walk,
                    CombatRules.FacingTowards(b.XMm,b.ZMm,4998500,1098500))));
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            Submit(new MoveCommand(sim.PlayerId,MoveIntent.Idle(0)));
            if (_options.TryGetValue("--tide-tick",out var tideText) && long.TryParse(tideText,out var tideTick) && tideTick>sim.WorldTick)
                sim.Advance(tideTick-sim.WorldTick);
            Submit(new TalkCommand(sim.PlayerId,"npc.sedge.tess_ferry")); await Shot("ferry_offer");
            long before=sim.WorldTick;Submit(new ChooseCommand(sim.PlayerId,"passage"));
            long after=sim.WorldTick;await Shot("orr_arrival");
            if(passages.Count!=1 || Math.Abs(sim.Player.Body.XMm-4755500)>500)
                throw new InvalidOperationException("Outbound passage did not reach Orr: "+rejection);
            Submit(new TalkCommand(sim.PlayerId,"npc.sedge.iren_orr"));
            Submit(new ChooseCommand(sim.PlayerId,"passage"));await Shot("tollpiles_return");
            if(passages.Count!=2 || sim.Player.Currency!=16 || before!=after)
                throw new InvalidOperationException("Ferry roundtrip receipt did not match two paid passages without a time skip");
            File.WriteAllText(Path.Combine(directory,"evidence.json"),JsonSerializer.Serialize(new {
                before,after,passages,coin=sim.Player.Currency,quests=sim.Quests,body=sim.Player.Body },new JsonSerializerOptions {WriteIndented=true}));
            CaptureQuit.At(this,0);
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(directory,"FAILED.txt"),e.ToString());CaptureQuit.At(this,3);
        }
    }

    // Reuse the existing navigation/steering harness. Gear wear here comes from
    // the preceding shipped fight, never from a state edit or a fake UI row.
    private async Task SedgeReturnServices(string directory)
    {
        var sim=_session.Simulation!;
        async Task Wait(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
        async Task Approach(string id)
        {
            bool Reached()=>_controller.InTalkReach(sim.Npcs.Single(n=>n.Id==id));
            var at=sim.Npcs.Single(n=>n.Id==id).Body;
            long approachX=at.XMm+(id.EndsWith("oren_pin",StringComparison.Ordinal)?-1500:1500);
            await HeritageRoute(approachX,at.ZMm,Reached,Gait.Run);
            if(!Reached())await HeritageStep(approachX,at.ZMm,Gait.Run,Reached);
            if(!Reached())throw new InvalidOperationException("Could not reach "+id);
        }
        const string smith="npc.sedge.oren_pin", keeper="npc.sedge.nessa_reed";
        await Approach(smith);
        _session.Submit(new TalkCommand(sim.PlayerId,smith));
        _session.Submit(new ChooseCommand(sim.PlayerId,"repair"));await Wait(2);
        if(_inventory.OpenTrader!=smith)throw new InvalidOperationException("Repair did not open Oren's service");
        var sword=sim.Player.Inventory.Single(i=>i.DefId=="item.weapon.rusted_sword");
        var quote=sim.RepairQuote(smith,sword.ItemId)??throw new InvalidOperationException("Fight did not wear the sword");
        long before=sim.Player.Currency;
        SaveScreenshot(directory,"repair_quote");
        var repair=_inventory.FindChildren("*",nameof(Button),true,false).OfType<Button>()
            .FirstOrDefault(b=>b.Text.StartsWith("Repair (",StringComparison.Ordinal)&&!b.Disabled)
            ??throw new InvalidOperationException("No usable repair button");
        repair.EmitSignal(BaseButton.SignalName.Pressed);await Wait(2);
        var worn=sim.Player.Inventory.Single(i=>i.ItemId==sword.ItemId).Wear;
        if(worn is { Points: >0 } || sim.Player.Currency!=before-quote.Fee)
            throw new InvalidOperationException("Native repair did not restore condition and charge its quote");
        SaveScreenshot(directory,"repair_done");CloseScreens();
        await Approach(keeper);
        _session.Submit(new TalkCommand(sim.PlayerId,keeper));
        _session.Submit(new ChooseCommand(sim.PlayerId,"trade"));await Wait(2);SaveScreenshot(directory,"food_stock");
        var food=sim.Wares(keeper)!.Wares.First(w=>w.ItemId=="item.consumable.roast_meat");
        _session.Submit(new BuyCommand(sim.PlayerId,keeper,food.Ref,1));await Wait(.5);CloseScreens();
        _session.Submit(new TalkCommand(sim.PlayerId,keeper));
        _session.Submit(new ChooseCommand(sim.PlayerId,"rest"));await Wait(2);SaveScreenshot(directory,"paid_rest");
        if(sim.Combat.Health!=sim.Combat.MaxHealth || sim.Combat.Stamina!=sim.Combat.MaxStamina)
            throw new InvalidOperationException("Paid rest did not restore the character");
        File.WriteAllText(Path.Combine(directory,"services_evidence.json"),JsonSerializer.Serialize(new {
            quote,before,after_repair=before-quote.Fee,after_food_and_rest=sim.Player.Currency,
            condition=worn,combat=sim.Combat,body=sim.Player.Body },new JsonSerializerOptions {WriteIndented=true}));
    }

    // Explicit packaged review only. Uses the shipped encounter, normal commands
    // and wall-clock pacing; no state writes, invulnerability or animation forcing.
    private async void RunSedgeCombatReview(string directory)
    {
        Directory.CreateDirectory(directory);
        _rcd3Driving = true;
        var sim = _session.Simulation!;
        var rows = new List<object>();
        _session.Subscribe<AttackStarted>(e => rows.Add(e));
        _session.Subscribe<HitResolved>(e => rows.Add(e));
        var source = _camera.Camera;
        var camera = new Camera3D { Fov = 60, Near = source.Near, Far = source.Far,
            CullMask = source.CullMask, Environment = source.Environment, Attributes = source.Attributes };
        AddChild(camera); camera.MakeCurrent();
        source.GetNode<OmniLight3D>("CharacterFill").Reparent(camera, false);
        Vector3 At(Body body) => new(body.XMm / 1000f, body.YMm / 1000f, body.ZMm / 1000f);
        string targetKey = sim.Creatures.Where(c => c.Alive)
            .OrderBy(c => At(c.Body).DistanceSquaredTo(At(sim.Player.Body))).First().Key;
        CreatureView? Foe() => sim.Creatures.FirstOrDefault(c => c.Key == targetKey && c.Alive);
        void Frame(CreatureView? foe)
        {
            var centre = foe is null ? At(sim.Player.Body) : (At(sim.Player.Body) + At(foe.Body)) * .5f;
            var aim = centre + Vector3.Up;
            camera.GlobalPosition = aim + new Vector3(7, 4, 8); camera.LookAt(aim);
        }
        AudioEffectRecord? audio = null;
        int bus = AudioServer.GetBusIndex("Master"), slot = AudioServer.GetBusEffectCount(bus);
        try
        {
            if (sim.ActiveLayout.Id != "region.sedge_levels") throw new InvalidOperationException("Sedge review requires Sedge content");
            SetProcess(false);
            for (int i = 0; i < 8; i++)
            {
                Draw(1, 1.0 / 60);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Frame(Foe());
            audio = new AudioEffectRecord(); AudioServer.AddBusEffect(bus, audio); audio.SetRecordingActive(true);
            DateTimeOffset began = DateTimeOffset.UtcNow;
            GD.Print("SEDGE_COMBAT_BEGIN " + began.ToString("O"));
            ulong start = Time.GetTicksMsec(); int sample = -1; double nextAttack = 4, nextHeal = 0;
            SetProcess(true);
            while (!sim.Combat.Downed && sim.Combat.Health > 0 && Time.GetTicksMsec() - start < 90_000)
            {
                double seconds = (Time.GetTicksMsec() - start) / 1000.0;
                var foe = Foe(); Frame(foe);
                if (foe is null)
                {
                    _session.Submit(new MoveCommand(sim.PlayerId, MoveIntent.Idle(sim.Player.Body.FacingMdeg)));
                    await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
                    break;
                }
                var body = sim.Player.Body;
                int facing = CombatRules.FacingTowards(body.XMm, body.ZMm, foe.Body.XMm, foe.Body.ZMm);
                var direction = At(foe.Body) - At(body); direction.Y = 0;
                float range = direction.Length(); direction = direction.Normalized();
                float desired = seconds < 15 ? 1.25f : seconds < 30 ? 2.25f : seconds < 50 ? 6f : 1.25f;
                float move = range > desired + .2f ? 1 : range < desired - .2f ? -1 : 0;
                _session.Submit(new MoveCommand(sim.PlayerId, new MoveIntent((int)(direction.X * move * 1000),
                    (int)(direction.Z * move * 1000), Gait.Run, facing)));
                bool guard = seconds < 50 && seconds % 7 < 5;
                _session.Submit(new BlockCommand(sim.PlayerId, guard));
                if (!guard && seconds > nextAttack)
                {
                    _session.Submit(new AttackCommand(sim.PlayerId)); nextAttack = seconds + (seconds < 50 ? 5 : 1.2);
                }
                if (seconds > nextHeal && sim.Combat.Health < sim.Combat.MaxHealth * .65
                    && sim.Player.Inventory.FirstOrDefault(i => i.DefId == "item.consumable.salve_ember") is { } salve)
                {
                    _session.Submit(new UseItemCommand(sim.PlayerId, salve.ItemId)); nextHeal = seconds + 6;
                }
                if ((int)(seconds * 5) != sample)
                {
                    sample = (int)(seconds * 5);
                    rows.Add(new { seconds, world_tick = sim.WorldTick, player = body, combat = sim.Combat, creatures = sim.Creatures,
                        clips = _creatures.GetChildren().OfType<SkinnedCreature>().Select(c => new { id = c.Name.ToString(), clip = c.ClipState }).ToArray() });
                    if (sample % 25 == 0) SaveScreenshot(directory, "frame_" + sample.ToString("D3"));
                }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            audio.SetRecordingActive(false);
            using (var wav = audio.GetRecording()) wav.SaveToWav(Path.Combine(directory, "game_master.wav"));
            AudioServer.RemoveBusEffect(bus, slot); audio = null;
            File.WriteAllText(Path.Combine(directory, "evidence.json"), JsonSerializer.Serialize(new {
                began, ended = DateTimeOffset.UtcNow, player = sim.Player.Name, region = sim.ActiveLayout.Id,
                health = sim.Combat.Health, rows }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("SEDGE_COMBAT_END " + DateTimeOffset.UtcNow.ToString("O"));
            if(_options.ContainsKey("--sedge-return-services"))
            {
                if(Foe() is not null || sim.Combat.Downed)throw new InvalidOperationException("A living character must finish the encounter first");
                _session.Submit(new BlockCommand(sim.PlayerId,false));
                source.MakeCurrent();camera.GetNode<OmniLight3D>("CharacterFill").Reparent(source,false);camera.QueueFree();
                await SedgeReturnServices(directory);
            }
            CaptureQuit.At(this, 0);
        }
        catch (Exception e)
        {
            if (audio is not null) { audio.SetRecordingActive(false); AudioServer.RemoveBusEffect(bus, slot); }
            File.WriteAllText(Path.Combine(directory, "FAILED.txt"), e.ToString());
            CaptureQuit.At(this, 3);
        }
    }
}
