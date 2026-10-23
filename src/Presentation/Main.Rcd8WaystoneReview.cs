using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;
using UNNAMED.Persistence;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Actual world stones/states. Only the before shot hides the added presentation.
    // Burst shots audition the exact travel effect; they do not claim a real journey.
    private async Task RunRcd8WaystoneReview(string directory,Camera3D camera)
    {
        _rcd3Driving=true;
        if(OS.GetEnvironment("OTHERREACH_RCD8_WAYSTONE_VIDEO")=="1")
        {
            await RunRcd8WaystoneJourney(directory,camera);return;
        }
        _avatar.Visible=false; // Explicit object-only visual fixture, with the normal world still running.
        _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);
        var sim=_session.Simulation ?? throw new InvalidDataException("Waystone gate has no world");
        var evidence=new List<object>();
        foreach(var view in sim.Waystones)
        {
            var model=_hollow.PlacedModel(view.Site.Body.Id) ?? throw new InvalidDataException("Missing actual Waystone mesh: "+view.Site.Key);
            var glow=model.GetNode<WaystoneGlow>("WaystoneAmbient");
            var aim=model.ToGlobal(ArtGallery.Bounds(model).GetCenter());
            string id=view.Site.Key.Split('.').Last();
            foreach(float distance in new[]{5f,20f})
            {
                camera.GlobalPosition=aim+new Vector3(distance*.6f,1.2f,distance*.8f);
                camera.LookAt(aim);_shadowLods.Update(camera.GlobalPosition);
                if(view.Awake)
                {
                    glow.Visible=false;await Rcd3Wait(1);
                    await Rcd3Shot(directory,$"{id}_{distance:0}m_before");
                }
                glow.Visible=true;await Rcd3Wait(3);
                await Rcd3Shot(directory,$"{id}_{distance:0}m_{(view.Awake?"awake":"dormant")}");
                evidence.Add(new { stone=view.Site.Key,view.Awake,view.Known,distance,
                    camera=camera.GlobalPosition.ToString(),target=aim.ToString(),world_tick=sim.WorldTick,
                    before="Same camera/world; added glow hidden only",after="Actual authoritative awake state" });
                if(view.Awake && distance==5)
                {
                    TravelStonePulse(view.Site.Key,false);await Rcd3Wait(.12);
                    await Rcd3Shot(directory,id+"_departure_burst_fixture");await Rcd3Wait(1);
                    TravelStonePulse(view.Site.Key,true);await Rcd3Wait(.12);
                    await Rcd3Shot(directory,id+"_arrival_burst_fixture");await Rcd3Wait(1);
                }
            }
        }
        File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
            status="WAYSTONE_WORLD_VISUALS_REQUIRE_CLAUDE_REVIEW",stones=evidence,
            travel_burst="Presentation audition only; actual TravelCommand journey gate remains pending",
            people=VisualOptions.People,engine_max_fps=Engine.MaxFps
        },new JsonSerializerOptions{WriteIndented=true}));
    }

    private async Task RunRcd8WaystoneJourney(string directory,Camera3D reviewCamera)
    {
        var sim=_session.Simulation ?? throw new InvalidDataException("No journey world");
        string leg=OS.GetEnvironment("OTHERREACH_RCD8_WAYSTONE_LEG");
        if(string.IsNullOrEmpty(leg))leg=OS.GetEnvironment("OTHERREACH_RCD8_WAYSTONE_JOURNEY")=="1"?"prepare":"walk";
        _npcs.SuppressPresentationForBenchmark=false;
        reviewCamera.GetNode<OmniLight3D>("CharacterFill").Reparent(_camera.Camera,false);
        _camera.Camera.MakeCurrent();_camera.TargetDistance=4;_camera.Pitch=-.22f;
        var events=new List<object>();var journeys=new List<PlayerTravelled>();
        _session.Subscribe<PlayerTravelled>(e=>journeys.Add(e));
        void Mark(string phase)
        {
            var row=new {phase,utc=DateTimeOffset.UtcNow,uptime_ms=Time.GetTicksMsec(),world_tick=sim.WorldTick,body=_controller.Authoritative};
            events.Add(row);File.AppendAllText(Path.Combine(directory,"phases.jsonl"),JsonSerializer.Serialize(row)+"\n");
        }
        async Task Go(IEnumerable<(double X,double Z)> points,double budget,Gait gait=Gait.Run)
        {
            ulong deadline=Time.GetTicksMsec()+(ulong)(budget*1000);
            foreach(var point in points)
            {
                var goal=new Body((long)Math.Round(point.X*1000),0,(long)Math.Round(point.Z*1000),0);
                var corners=Playthrough.WayTo(sim,_controller.Authoritative,goal,0);
                foreach(var corner in corners)
                {
                    var last=_avatar.GlobalPosition;ulong moved=Time.GetTicksMsec();
                    while(true)
                    {
                        var body=_controller.Authoritative;
                        var toward=new Vector3((float)(corner.X-body.XMm/1000.0),0,(float)(corner.Z-body.ZMm/1000.0));
                        if(toward.Length()<.25f)break;
                        if(Time.GetTicksMsec()>deadline)throw new TimeoutException("Normal walking route timed out at "+body);
                        if(_avatar.GlobalPosition.DistanceTo(last)>.1f){last=_avatar.GlobalPosition;moved=Time.GetTicksMsec();}
                        if(Time.GetTicksMsec()-moved>8000)throw new InvalidDataException("Normal walking route blocked at "+body+" toward "+corner);
                        _camera.Yaw=Mathf.Atan2(-toward.X,-toward.Z);
                        _controller.SteerWorld(toward.Normalized(),gait,_camera);
                        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                    }
                }
            }
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);await Rcd3Wait(.2);
        }
        async Task Touch(string key)
        {
            _controller.TouchStone(key);await Rcd3Wait(.4);
            if(!sim.Waystones.Single(w=>w.Site.Key==key).Known)throw new InvalidDataException("Stone touch was refused: "+key);
            CloseScreens();
        }
        // Lazy NPC/model loads span several frames. Require a stable live renderer before timing movement.
        ulong previous=Time.GetTicksMsec();int stableFrames=0;
        while(stableFrames<60)
        {
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            ulong now=Time.GetTicksMsec();stableFrames=now-previous<250?stableFrames+1:0;previous=now;
        }
        Mark("world_warm");
        if(leg!="resume")
        {
            await Go(new[]{(428.9,758.0)},8);await Touch("waystone.ashen");
            await Go(new[]{(430.0,754.0),(407.0,754.0),(407.0,758.0)},30);
        }
        _camera.Yaw=-Mathf.Pi/2;await Rcd3Wait(1);
        DisplayServer.WindowSetTitle("Otherreach - "+Path.GetFileName(directory));
        File.WriteAllText(Path.Combine(directory,"record_ready.json"),"{}");
        ulong recordingDeadline=Time.GetTicksMsec()+30000;
        while(!File.Exists(Path.Combine(directory,"recording.started")))
        {
            if(Time.GetTicksMsec()>recordingDeadline)throw new TimeoutException("Waystone WGC recorder did not start");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
        if(leg!="resume")
        {
            Mark("approach_start");
            await Go(new[]{(425.1,758.0)},20,Gait.Walk);await Rcd3Wait(6);Mark("approach_end");
        }
        else Mark("continued_normal_checkpoint");
        if(OS.GetEnvironment("OTHERREACH_RCD8_WAYSTONE_JOURNEY")=="1")
        {
            var road=new[]{(425.0,754.0),(440.0,740.0),(447.0,739.0),(451.8,742.0),(451.8,736.0),(465.0,736.0),(488.0,745.0),
                (488.0,720.0),(522.0,690.0),(564.0,690.0),(600.0,656.0),(680.0,657.0),(760.0,658.0),(840.0,659.0),(920.0,660.0),
                (978.0,653.0),(978.5,651.0),(971.0,643.0),(961.0,644.0),(959.0,645.0)};
            await Go(leg=="prepare"?road.Take(11):leg=="resume"?road.Skip(11):road,260);
            if(leg=="prepare")
            {
                _session.Save(SaveSlots.Manual("waystone_gate"));await Rcd3Wait(2);Mark("normal_manual_checkpoint");
            }
            else
            {
            var berrin=sim.Npcs.Single(n=>n.Id=="npc.cairnwold.berrin_garth");
            if(!_controller.InTalkReach(berrin))await Go(Playthrough.WayTo(sim,_controller.Authoritative,berrin.Body),20);
            _controller.Talk(berrin.Id);await Rcd3Wait(.5);
            if(!sim.Conversation!.Replies.Any(r=>r.Id=="rim_stone"))throw new InvalidDataException("Berrin did not offer the Rim Stone rite");
            _dialogue.Answer("rim_stone");await Rcd3Wait(.5);
            if(!sim.DialogueVisited("dialogue.cairnwold.berrin_garth","rim_stone"))throw new InvalidDataException("Rim rite was not learned through dialogue");
            Mark("rite_heard");_dialogue.Leave();await Rcd3Wait(.3);CloseScreens();
            await Go(new[]{(955.5,648.5),(955.5,659.5),(954.0,661.0),(949.5,661.0),(944.0,662.0),(938.0,667.0),(936.0,667.0),
                (908.5,677.5),(906.0,677.5),(882.0,702.0),(880.6,702.5)},90);
            Mark("rim_touch");await Touch("waystone.rim");await Rcd3Wait(3);
            if(!sim.Waystones.Single(w=>w.Site.Key=="waystone.rim").Awake)throw new InvalidDataException("Rim Stone did not wake");
            float fov=_camera.Camera.Fov;Mark("departure");TravelWithEffects("waystone.rim","waystone.ashen");
            ulong deadline=Time.GetTicksMsec()+10000;
            while(_waystoneTravelBusy && Time.GetTicksMsec()<deadline)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(_waystoneTravelBusy || !_waystoneArrived || journeys.Count!=1)throw new InvalidDataException("Real Waystone journey failed");
            var site=sim.Setup.Layout.FindWaystone("waystone.ashen")!;
            var arrival=_controller.Authoritative;
            if(Math.Abs(arrival.XMm-site.ArrivalXMm)>2000 || Math.Abs(arrival.ZMm-site.ArrivalZMm)>2000 || Math.Abs(_camera.Camera.Fov-fov)>.01f)
                throw new InvalidDataException("Travel arrival or camera restoration failed");
            Mark("arrival");await Rcd3Wait(5);
            }
        }
        File.WriteAllText(Path.Combine(directory,"recording.finished"),"done");
        File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
            status="REAL_GAME_WAYSTONE_VIDEO_REQUIRES_CLAUDE_REVIEW",leg,events,journeys,
            route="Normal controller walking, TouchStoneCommand, Berrin dialogue, TravelCommand; no state injection or teleport fixtures",
            engine_max_fps=Engine.MaxFps,people=VisualOptions.People
        },new JsonSerializerOptions{WriteIndented=true}));
        ulong stoppedDeadline=Time.GetTicksMsec()+15000;
        while(!File.Exists(Path.Combine(directory,"recording.stopped")) && Time.GetTicksMsec()<stoppedDeadline)
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    }
}
