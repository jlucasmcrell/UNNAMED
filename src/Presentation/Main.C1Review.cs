using Godot;
using System.Text.Json;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;
namespace UNNAMED.Presentation;
public partial class Main
{
    // Explicit review only: isolated content/profile, normal creator and commands, real gameplay pacing.
    private async void RunC1Review(string directory)
    {
        Directory.CreateDirectory(directory);
        _rcd3Driving=true;
        var sim=_session.Simulation!;
        var rows=new List<object>();
        bool dead=false;_session.Subscribe<PlayerDied>(_=>dead=true);
        _session.Subscribe<AttackStarted>(e=>rows.Add(e));
        _session.Subscribe<HitResolved>(e=>rows.Add(e));
        _session.Subscribe<CreatureCalled>(e=>rows.Add(e));
        _session.Subscribe<CommandRejected>(e=>rows.Add(e));
        var source=_camera.Camera;
        var camera=new Camera3D { Fov=55,Near=source.Near,Far=source.Far,CullMask=source.CullMask,
            Environment=source.Environment,Attributes=source.Attributes };
        AddChild(camera);camera.MakeCurrent();
        source.GetNode<OmniLight3D>("CharacterFill").Reparent(camera,false);
        Vector3 Pos(Body b)=>new(b.XMm/1000f,b.YMm/1000f,b.ZMm/1000f);
        void Frame()
        {
            var enemy=sim.Creatures.Where(c=>c.Alive).OrderBy(c=>Pos(c.Body).DistanceSquaredTo(Pos(sim.Player.Body))).FirstOrDefault();
            var center=enemy is null?Pos(sim.Player.Body):(Pos(enemy.Body)+Pos(sim.Player.Body))*.5f;
            var aim=center+Vector3.Up;
            camera.GlobalPosition=aim+new Vector3(4.7f,3.3f,5.8f);camera.LookAt(aim);
        }
        try
        {
            SetProcess(false);
            // Creator Begin queues native NPC/mesh readiness. Let those deferred imports and draws
            // complete with the simulation frozen before arming the real-time recorder.
            for (int frame=0;frame<8;frame++)
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Frame();
            for (int frame=0;frame<2;frame++)
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            File.WriteAllText(Path.Combine(directory,"record_ready.json"),"{}");
            ulong deadline=Time.GetTicksMsec()+60_000;
            while(!File.Exists(Path.Combine(directory,"recording.started")))
            {
                if(Time.GetTicksMsec()>deadline)throw new TimeoutException("C1 owned recorder not ready");
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            SetProcess(true);
            int bus=AudioServer.GetBusIndex("Master"),slot=AudioServer.GetBusEffectCount(bus);
            var audio=new AudioEffectRecord();AudioServer.AddBusEffect(bus,audio);audio.SetRecordingActive(true);
            var audioStart=DateTimeOffset.UtcNow;
            ulong start=Time.GetTicksMsec();int dodge=0,sample=-1;double nextAttack=1.5;
            while(!dead && Time.GetTicksMsec()-start<24_000)
            {
                if(File.Exists(@"C:\Otherreach_Recovery\OWNER_PLAYING.flag")) throw new OperationCanceledException("Owner playing; stop C1 renderer");
                double elapsed=(Time.GetTicksMsec()-start)/1000.0;
                Frame();
                var foe=sim.Creatures.Where(c=>c.Alive).OrderBy(c=>Pos(c.Body).DistanceSquaredTo(Pos(sim.Player.Body))).FirstOrDefault();
                if(foe is not null)
                {
                    int facing=CombatRules.FacingTowards(sim.Player.Body.XMm,sim.Player.Body.ZMm,foe.Body.XMm,foe.Body.ZMm);
                    _session.Submit(new MoveCommand(sim.PlayerId,MoveIntent.Idle(facing)));
                    bool guard=elapsed%8<5;
                    _session.Submit(new BlockCommand(sim.PlayerId,guard));
                    if(elapsed>nextAttack && !guard) { _session.Submit(new AttackCommand(sim.PlayerId));nextAttack=elapsed+4; }
                    if((elapsed>7 && dodge==0)||(elapsed>15 && dodge==1))
                    { _session.Submit(new BlockCommand(sim.PlayerId,false));_session.Submit(new DodgeCommand(sim.PlayerId,0,-1000));dodge++; }
                    if(sim.Combat.Health<sim.Combat.MaxHealth/2 && (int)elapsed%7==0 && sim.Player.Inventory.FirstOrDefault(i=>i.DefId=="item.consumable.salve_ember") is { } salve)
                        _session.Submit(new UseItemCommand(sim.PlayerId,salve.ItemId));
                }
                if((int)(elapsed*5)!=sample)
                {
                    sample=(int)(elapsed*5);
                    rows.Add(new { seconds=elapsed,player=sim.Player.Body,combat=sim.Combat,creatures=sim.Creatures,
                        clips=_creatures.GetChildren().OfType<SkinnedCreature>().Select(c=>new { id=c.Name.ToString(),clip=c.ClipState }).ToArray() });
                }
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            audio.SetRecordingActive(false);using(var wav=audio.GetRecording())wav.SaveToWav(Path.Combine(directory,"game_master.wav"));AudioServer.RemoveBusEffect(bus,slot);
            File.WriteAllText(Path.Combine(directory,"evidence.json"),JsonSerializer.Serialize(new {player=sim.Player.Name,audioStart,seconds=(Time.GetTicksMsec()-start)/1000.0,dead,rows},new JsonSerializerOptions{WriteIndented=true}));
            File.WriteAllText(Path.Combine(directory,"recording.finished"),"done");
            await Rcd3Wait(1);
            CaptureQuit.At(this,0);
        }
        catch(Exception e)
        {
            File.WriteAllText(Path.Combine(directory,"FAILED.txt"),e.ToString());
            File.WriteAllText(Path.Combine(directory,"recording.finished"),"failed");
            CaptureQuit.At(this,3);
        }
    }
}
