using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Isolated content fixture, fresh profile, normal commands and live read-only state.
    // Legacy RC d5 fixture routed venom to root; production now has its own Snarebind Resin delivery.
    private async Task RunRcd5StealthReview(string directory, Camera3D camera)
    {
        var sim=_session.Simulation!;
        var rows=new List<object>();
        var hits=new List<HitResolved>();
        var effects=new List<EffectApplied>();
        _session.Subscribe<HitResolved>(e => { hits.Add(e); rows.Add(e); });
        _session.Subscribe<EffectApplied>(e => { effects.Add(e); rows.Add(e); });
        _session.Subscribe<EffectExpired>(e => rows.Add(e));
        _session.Subscribe<CommandRejected>(e => rows.Add(e));
        _camera.TargetDistance=3.5f;
        _rcd3Driving=true; SetProcess(true);
        var dagger=sim.Player.Inventory.Single(i=>i.DefId=="item.weapon.iron_dagger");
        _session.Submit(new EquipCommand(sim.PlayerId,dagger.ItemId));
        _controller.Crouch(true);
        await Rcd3Wait(.4);
        CreatureView Wolf()=>sim.Creatures.Single(c=>c.Key.Contains("valley_strays",StringComparison.Ordinal));
        CreatureView Boar()=>sim.Creatures.Single(c=>c.Key.Contains("boar_wallow",StringComparison.Ordinal));
        Vector3 Pos(Body b)=>new(b.XMm/1000f,b.YMm/1000f,b.ZMm/1000f);
        object Snapshot(CreatureView c)=>new { tick=sim.WorldTick,player=sim.Player.Body,creature=c,clip=_avatar.ClipState,
            native=_avatar.GetNodeOrNull<Node>("Native")?.Call("native_metrics").ToString() };
        void Frame(CreatureView c)
        {
            var direction=Pos(c.Body)-Pos(sim.Player.Body);
            _camera.Yaw=Mathf.Atan2(-direction.X,-direction.Z);
            var aim=(Pos(c.Body)+_avatar.GlobalPosition)*.5f+Vector3.Up*.8f;
            camera.Fov=55;camera.GlobalPosition=aim+new Vector3(2.8f,2.1f,4.5f);camera.LookAt(aim);
        }
        async Task Move(Vector3 goal,Func<CreatureView> target,double seconds=30,string? shot=null)
        {
            var from=sim.Player.Body;
            var to=new Body((long)(goal.X*1000),from.YMm,(long)(goal.Z*1000),from.FacingMdeg);
            var path=Playthrough.WayTo(sim,from,to,standOff:0);
            ulong until=Time.GetTicksMsec()+(ulong)(seconds*1000);
            foreach(var point in path)
            {
                while(Time.GetTicksMsec()<until)
                {
                    var b=sim.Player.Body;
                    var d=new Vector3((float)(point.X-b.XMm/1000.0),0,(float)(point.Z-b.ZMm/1000.0));
                    if(d.Length()<.06f)break;
                    _controller.SteerWorld(d.Normalized(),Gait.Walk,_camera);Frame(target());
                    if(shot is not null && d.Length()<2.3f)
                    {
                        rows.Add(new {stage=shot,state=Snapshot(target())});
                        await Rcd3Shot(directory,shot);shot=null;
                    }
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
            }
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);
            if((Pos(sim.Player.Body)-goal).LengthSquared()>2)throw new InvalidOperationException("Review route failed: "+sim.Player.Body);
        }
        async Task<Vector3> Rear(Func<CreatureView> target,double seconds=30)
        {
            var c=target();float yaw=PlayerController.FacingRadians(c.Body.FacingMdeg);
            var forward=new Vector3(Mathf.Sin(yaw),0,Mathf.Cos(yaw));
            var near=Pos(sim.Player.Body)-Pos(c.Body);near.Y=0;near=near.Normalized();
            float angle=Mathf.Atan2(near.X,near.Z),goalAngle=Mathf.Atan2(-forward.X,-forward.Z);
            await Move(Pos(c.Body)+near*5,target,seconds);
            float turn=Mathf.AngleDifference(angle,goalAngle);
            for(int arc=1;arc<=4;arc++)
            {
                float a=angle+turn*arc/4;
                await Move(Pos(c.Body)+new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*5,target);
            }
            await Move(Pos(c.Body)-forward*4,target);
            return forward;
        }
        try
        {
            var forward=await Rear(Wolf);
            Frame(Wolf());await Rcd3Wait(.4);
            rows.Add(new {stage="crouched_sneak",state=Snapshot(Wolf())});
            await Rcd3Shot(directory,"crouched_sneak");
            var head=_avatar.Head??_avatar.GlobalPosition+Vector3.Up;
            camera.Fov=40;camera.GlobalPosition=head+_avatar.GlobalBasis.Z*1.9f+Vector3.Up*.35f;camera.LookAt(head);
            await Rcd3Wait(.15);await Rcd3Shot(directory,"crouch_head");
            await Move(Pos(Wolf().Body)-forward*2.7f,Wolf,shot:"crouched_moving");
            var before=Wolf();rows.Add(new {stage="before_takedown",state=Snapshot(before)});
            _controller.Takedown();await Rcd3Wait(.18);Frame(Wolf());
            await Rcd3Shot(directory,"takedown_strike");await Rcd3Wait(.6);
            rows.Add(new {stage="after_takedown",state=Snapshot(Wolf())});
            await Rcd3Shot(directory,"takedown_result");
            if(!hits.Any(h=>h.Attacker==sim.PlayerId&&h.Target==before.Id&&h.Damage>0))
                throw new InvalidOperationException("No real takedown wound");
            if(Wolf().Alive)throw new InvalidOperationException("Qualifying even-target ambush did not execute");
            // Circle outside hearing; the higher-level sleeper then honestly resists the same F command.
            var high=Boar();var highForward=await Rear(Boar,65);
            await Move(Pos(high.Body)-highForward*2.7f,Boar);
            rows.Add(new {stage="before_higher_resist",state=Snapshot(Boar())});
            _controller.Takedown();await Rcd3Wait(.18);Frame(Boar());
            await Rcd3Shot(directory,"higher_resist_strike");await Rcd3Wait(.4);
            rows.Add(new {stage="higher_resist",state=Snapshot(Boar())});
            await Rcd3Shot(directory,"higher_resist");
            if(!Boar().Alive||Boar().Health>=high.Health)throw new InvalidOperationException("Higher target did not honestly resist");
            _controller.Crouch(false);
            var flask=sim.Player.Inventory.Single(i=>i.DefId=="item.consumable.poison_flask");
            _session.Submit(new UseItemCommand(sim.PlayerId,flask.ItemId));await Rcd3Wait(.3);
            ulong closeUntil=Time.GetTicksMsec()+12000;
            while(Time.GetTicksMsec()<closeUntil)
            {
                var d=Pos(Boar().Body)-Pos(sim.Player.Body);d.Y=0;
                if(d.Length()<1.1f)break;
                _controller.SteerWorld(d.Normalized(),Gait.Walk,_camera);Frame(Boar());
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            var boar=Boar();
            var facing=(Pos(boar.Body)-Pos(sim.Player.Body));facing.Y=0;
            _controller.SteerWorld(facing.Normalized(),Gait.Walk,_camera);
            await Rcd3Wait(.08);_controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);
            for(int attempt=0;attempt<3&&!effects.Any(e=>e.Target==boar.Id&&e.EffectId=="effect.root");attempt++)
            { _controller.Attack();await Rcd3Wait(.7); }
            var root=effects.LastOrDefault(e=>e.Target==boar.Id&&e.EffectId=="effect.root")
                ?? throw new InvalidOperationException("Normal coated strike did not apply root");
            _hud.Toast("REVIEW FIXTURE: Root applied — feet held; turning and strikes remain possible.",6);
            var rooted=Boar();Frame(rooted);await Rcd3Shot(directory,"rooted_start");
            long startTick=sim.WorldTick;
            var playerBefore=sim.Player.Body;
            _controller.SteerWorld((Pos(sim.Player.Body)-Pos(rooted.Body)).Normalized(),Gait.Walk,_camera);
            await Rcd3Wait(2);_controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);Frame(Boar());
            var after=Boar();
            long drift=Math.Abs(after.Body.XMm-rooted.Body.XMm)+Math.Abs(after.Body.ZMm-rooted.Body.ZMm);
            rows.Add(new {stage="rooted_2s",startTick,endTick=sim.WorldTick,root.ExpiresTick,drift_mm=drift,before=rooted,after,
                playerBefore,playerAfter=sim.Player.Body});
            await Rcd3Shot(directory,"rooted_2s");
            if(drift>1||sim.WorldTick>=root.ExpiresTick)throw new InvalidOperationException("Root hold assertion failed");
            long separatedAt=sim.WorldTick;
            await Rcd3Wait(.8);
            float separation=(Pos(sim.Player.Body)-Pos(Boar().Body)).Length();
            int outsideHits=hits.Count(h=>h.Attacker==boar.Id&&h.Target==sim.PlayerId&&h.Tick>separatedAt);
            _hud.Toast($"REVIEW: Root holds — 0 mm movement; Ada {separation:0.0} m away, {outsideHits} hits after withdrawal.",3);
            await Rcd3Wait(.1);Frame(Boar());await Rcd3Shot(directory,"rooted_away");
            rows.Add(new {stage="rooted_away",separatedAt,tick=sim.WorldTick,separation_m=separation,outsideHits,state=Snapshot(Boar())});
            if(outsideHits!=0)throw new InvalidOperationException("Rooted creature hit Ada after withdrawal");
            while(sim.WorldTick<root.ExpiresTick+25)
            {
                var held=Boar();
                if(held.Rooted && (held.Body.XMm!=rooted.Body.XMm||held.Body.ZMm!=rooted.Body.ZMm))
                    throw new InvalidOperationException("Root moved during an action phase");
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            rows.Add(new {stage="after_root_expired",state=Snapshot(Boar())});
            Frame(Boar());await Rcd3Shot(directory,"root_expired");
            File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
                status="LIVE_COMMAND_GATES_PASS_REQUIRES_CLAUDE_REVIEW",people=VisualOptions.People,
                fixture="Fresh review content only: dagger/flask boot kit, sleeper wolf, higher-level sleeper boar, coating delivers root. Owner content unchanged."}));
        }
        finally
        {
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);_rcd3Driving=false;
            File.WriteAllText(Path.Combine(directory,"stealth.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        }
    }
    // Production content, fresh normal creator start, ordinary movement/looting/use/attack commands only.
    private async Task RunEffectDeliveryReview(string directory, Camera3D camera)
    {
        var sim = _session.Simulation!;
        var rows = new List<object>();
        var effects = new List<EffectApplied>();
        _session.Subscribe<EffectApplied>(e => { effects.Add(e); rows.Add(e); });
        _session.Subscribe<ItemMoved>(e => rows.Add(e));
        _session.Subscribe<ItemUsed>(e => rows.Add(e));
        _session.Subscribe<CommandRejected>(e => rows.Add(e));
        _session.Subscribe<HitResolved>(e => rows.Add(e));
        _rcd3Driving = true; SetProcess(true); _camera.TargetDistance = 3.5f;
        Vector3 Pos(Body b) => new(b.XMm/1000f,b.YMm/1000f,b.ZMm/1000f);
        void Frame(Vector3 goal)
        {
            var aim = (_avatar.GlobalPosition + goal) * .5f + Vector3.Up * .8f;
            camera.Fov = 55; camera.GlobalPosition = aim + new Vector3(3,2.8f,5); camera.LookAt(aim);
        }
        async Task Move(Vector3 goal, double seconds = 100)
        {
            var from = sim.Player.Body;
            var to = new Body((long)(goal.X*1000),from.YMm,(long)(goal.Z*1000),from.FacingMdeg);
            var path = Playthrough.WayTo(sim,from,to,standOff:0);
            ulong until = Time.GetTicksMsec()+(ulong)(seconds*1000);
            foreach(var point in path)
            {
                while(Time.GetTicksMsec()<until)
                {
                    var b=sim.Player.Body;
                    var d=new Vector3((float)(point.X-b.XMm/1000.0),0,(float)(point.Z-b.ZMm/1000.0));
                    if(d.Length()<.15f) break;
                    _controller.SteerWorld(d.Normalized(),Gait.Run,_camera); Frame(Pos(b));
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
            }
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);
            var gap=goal-Pos(sim.Player.Body);gap.Y=0;
            if(gap.Length()>2)throw new InvalidOperationException("Production route failed: "+sim.Player.Body);
        }
        try
        {
            rows.Add(new {stage="normal_start",player=sim.Player.Body,inventory=sim.Player.Inventory});
            var cart=sim.Containers.Single(c=>c.Site.Key=="container.merchant_cart");
            await Move(new Vector3(cart.Site.XMm/1000f,0,cart.Site.ZMm/1000f)+Vector3.Left);
            Frame(new Vector3(cart.Site.XMm/1000f,_avatar.GlobalPosition.Y,cart.Site.ZMm/1000f));
            OpenInventory(cart.Site.Key); await Rcd3Wait(.3); await Rcd3Shot(directory,"01_cart_loot"); CloseInventory();
            _session.Submit(new TakeAllCommand(sim.PlayerId,cart.Site.Key)); await Rcd3Wait(.3);
            foreach(var item in new[]{"poison_flask","sleep_coating","paralyse_coating","snarebind_resin"})
                if(!sim.Player.Inventory.Any(i=>i.DefId=="item.consumable."+item))throw new InvalidOperationException("Not looted: "+item);
            rows.Add(new {stage="looted_production_cart",inventory=sim.Player.Inventory,player=sim.Player.Body});
            OpenInventory(null);await Rcd3Wait(.3);await Rcd3Shot(directory,"02_acquired_inventory");CloseInventory();
            _session.Submit(new EquipCommand(sim.PlayerId,sim.Player.Inventory.Single(i=>i.DefId=="item.weapon.iron_dagger").ItemId));
            if(sim.Player.Inventory.FirstOrDefault(i=>i.DefId=="item.consumable.salve_minor") is { } salve)
                _session.Submit(new UseItemCommand(sim.PlayerId,salve.ItemId));
            CreatureView Boar()=>sim.Creatures.Single(c=>c.Key.Contains("hollow.boar_wallow",StringComparison.Ordinal));
            await Move(Pos(Boar().Body)+new Vector3(0,0,6));
            foreach(var pair in new[]{("snarebind_resin","root"),("paralyse_coating","paralyse"),("sleep_coating","sleep"),("poison_flask","venom")})
            {
                var item=sim.Player.Inventory.Single(i=>i.DefId=="item.consumable."+pair.Item1);
                _session.Submit(new UseItemCommand(sim.PlayerId,item.ItemId));await Rcd3Wait(.1);
                ulong until=Time.GetTicksMsec()+12000, nextAttack=0;
                while(!effects.Any(e=>e.Target==Boar().Id&&e.EffectId=="effect."+pair.Item2)&&Time.GetTicksMsec()<until)
                {
                    var d=Pos(Boar().Body)-Pos(sim.Player.Body);d.Y=0;
                    if(d.Length()>1.05f)_controller.SteerWorld(d.Normalized(),Gait.Walk,_camera);
                    else { _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);if(Time.GetTicksMsec()>=nextAttack){_controller.Attack();nextAttack=Time.GetTicksMsec()+750;} }
                    Frame(Pos(Boar().Body));await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
                _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);
                var applied=effects.LastOrDefault(e=>e.Target==Boar().Id&&e.EffectId=="effect."+pair.Item2)
                    ??throw new InvalidOperationException("No delivery: "+pair.Item2);
                _hud.Toast("NORMAL LOOT: "+pair.Item1+" consumed; dagger delivered "+pair.Item2+".",5);
                rows.Add(new {stage="delivered_"+pair.Item2,applied,creature=Boar(),inventory=sim.Player.Inventory,player=sim.Player.Body});
                Frame(Pos(Boar().Body));await Rcd3Shot(directory,"03_"+pair.Item2);
                if(pair.Item2=="venom")
                {
                    var before=Boar().Health;
                    await Rcd3Wait(2.2);Frame(Pos(Boar().Body));
                    rows.Add(new {stage="venom_tick",before,after=Boar().Health});
                    await Rcd3Shot(directory,"04_venom_tick");
                    if(Boar().Health>=before)throw new InvalidOperationException("Venom did not tick");
                }
                if(pair.Item2 is "root" or "paralyse" or "sleep")
                {
                    var start=Boar().Body;
                    _controller.SteerWorld((Pos(sim.Player.Body)-Pos(start)).Normalized(),Gait.Walk,_camera);
                    await Rcd3Wait(pair.Item2=="paralyse"?1:2);
                    _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);
                    var after=Boar();Frame(Pos(after.Body));
                    rows.Add(new {stage="held_"+pair.Item2,before=start,after,player=sim.Player.Body});
                    if(start.XMm!=after.Body.XMm||start.ZMm!=after.Body.ZMm)throw new InvalidOperationException("Control moved: "+pair.Item2);
                    await Rcd3Shot(directory,"04_"+pair.Item2+"_held");
                    while(sim.WorldTick<=applied.ExpiresTick+5)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
            }
            File.WriteAllText(Path.Combine(directory,"complete.json"),"{\"status\":\"PRODUCTION_LOOT_USE_STRIKE_ALL_FOUR_PASS\"}");
        }
        finally
        {
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);_rcd3Driving=false;
            File.WriteAllText(Path.Combine(directory,"delivery.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        }
    }

}
