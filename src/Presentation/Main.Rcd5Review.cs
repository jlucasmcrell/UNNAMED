using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Isolated content fixture, fresh profile, normal commands and live read-only state.
    // The fixture routes the existing coating to root because production has no root delivery item yet.
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
        try
        {
            var wolf=Wolf();float yaw=PlayerController.FacingRadians(wolf.Body.FacingMdeg);
            var forward=new Vector3(Mathf.Sin(yaw),0,Mathf.Cos(yaw));
            await Move(Pos(wolf.Body)-forward*4,Wolf);
            Frame(Wolf());await Rcd3Wait(.4);
            rows.Add(new {stage="crouched_sneak",state=Snapshot(Wolf())});
            await Rcd3Shot(directory,"crouched_sneak");
            await Move(Pos(Wolf().Body)-forward*2.7f,Wolf,shot:"crouched_moving");
            var before=Wolf();rows.Add(new {stage="before_takedown",state=Snapshot(before)});
            _controller.Takedown();await Rcd3Wait(.18);Frame(Wolf());
            await Rcd3Shot(directory,"takedown_strike");await Rcd3Wait(.6);
            rows.Add(new {stage="after_takedown",state=Snapshot(Wolf())});
            await Rcd3Shot(directory,"takedown_result");
            if(!hits.Any(h=>h.Attacker==sim.PlayerId&&h.Target==before.Id&&h.Damage>0))
                throw new InvalidOperationException("No real takedown wound");
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
            _controller.SteerWorld((Pos(sim.Player.Body)-Pos(rooted.Body)).Normalized(),Gait.Walk,_camera);
            await Rcd3Wait(2);_controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);Frame(Boar());
            var after=Boar();
            long drift=Math.Abs(after.Body.XMm-rooted.Body.XMm)+Math.Abs(after.Body.ZMm-rooted.Body.ZMm);
            rows.Add(new {stage="rooted_2s",startTick,endTick=sim.WorldTick,root.ExpiresTick,drift_mm=drift,before=rooted,after});
            await Rcd3Shot(directory,"rooted_2s");
            if(drift>1||sim.WorldTick>=root.ExpiresTick)throw new InvalidOperationException("Root hold assertion failed");
            while(sim.WorldTick<root.ExpiresTick+25)
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            rows.Add(new {stage="after_root_expired",state=Snapshot(Boar())});
            Frame(Boar());await Rcd3Shot(directory,"root_expired");
            File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
                status="LIVE_COMMAND_GATES_PASS_REQUIRES_CLAUDE_REVIEW",people=VisualOptions.People,
                fixture="Fresh review content only: dagger/flask boot kit, sleeper wolf, sentinel boar, coating delivers root. Owner content unchanged."}));
        }
        finally
        {
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);_rcd3Driving=false;
            File.WriteAllText(Path.Combine(directory,"stealth.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
