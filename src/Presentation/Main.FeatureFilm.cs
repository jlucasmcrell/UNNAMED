using Godot;
using System.Text.Json;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using UNNAMED.Domain.Character;
using UNNAMED.Presentation.Ui;
using UNNAMED.Presentation.MHCharacters;
using UNNAMED.Presentation.Art;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Private filming only: cameras and ordinary player commands over the public content.
    // --stand/start-items/start-level are recorded starting fixtures, never hidden gameplay buffs.
    private async Task RunFeatureFilm(string directory,Camera3D camera,string planPath)
    {
        using var plan=JsonDocument.Parse(File.ReadAllText(planPath));
        var sim=_session.Simulation ?? throw new InvalidDataException("Film has no live world");
        string selectedPeople=sim.Player.Identity.PeopleId;
        if(_avatar is not SkinnedFigure native)throw new InvalidDataException("Film requires the public native player figure");
        if(selectedPeople!="people.veth"&&!native.ModelId.Contains("_race_"+selectedPeople[7..]+"_",StringComparison.Ordinal))
            throw new InvalidDataException("Film's actual model does not match chosen people: "+selectedPeople+" / "+native.ModelId);
        File.WriteAllText(Path.Combine(directory,"BODY_CONTRACT.json"),JsonSerializer.Serialize(new {
            selectedPeople,preset=sim.Player.Identity.BodyPresetId,actual_model=native.ModelId,
            stature_scale=native.StatureScale,library="Native exact-target public library; no donor figures",
            legacy_veth_comparison_gate="Not applicable to race models absent from the old Veth-only comparison table; actual model asserted here"}));
        _rcd3Driving=true;
        CloseScreens();CloseFrontEnd();
        _npcs.SuppressPresentationForBenchmark=false;
        bool ui=false;
        _hud.Visible=ui;if(_hintLine is not null)_hintLine.Visible=ui;
        camera.Fov=60;
        var offset=new Vector3(3,2.2f,4.5f);
        bool gameplayCamera=false;
        Vector3? fixedAim=null;
        Vector3 Pos(Body b)=>new(b.XMm/1000f,b.YMm/1000f,b.ZMm/1000f);
        Vector3 V(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
        string Text(JsonElement e,string key,string fallback="")=>e.TryGetProperty(key,out var v)?v.GetString()??fallback:fallback;
        double Number(JsonElement e,string key,double fallback)=>e.TryGetProperty(key,out var v)?v.GetDouble():fallback;
        void Mark(string phase,object? detail=null)=>File.AppendAllText(Path.Combine(directory,"phases.jsonl"),JsonSerializer.Serialize(new {
            phase,detail,utc=DateTimeOffset.UtcNow,uptime_ms=Time.GetTicksMsec(),tick=sim.WorldTick,
            player=sim.Player.Body,health=sim.Combat.Health,level=sim.Player.Progression.Level,
            camera=camera.GlobalPosition.ToString(),model=native.ModelId,tier=VisualOptions.Tier})+"\n");
        _session.Subscribe<CommandRejected>(e=>Mark("rejected",e));
        _session.Subscribe<HitResolved>(e=>Mark("hit",e));
        _session.Subscribe<WingsChanged>(e=>Mark("wings",e));
        _session.Subscribe<Blinked>(e=>Mark("blink",e));
        _session.Subscribe<WraithStepped>(e=>Mark("wraith_step",e));
        _session.Subscribe<AfterimageLeft>(e=>Mark("afterimage",e));
        _session.Subscribe<AfterimageStruck>(e=>Mark("decoy_struck",e));
        _session.Subscribe<StoneTouched>(e=>Mark("stone_touched",e));
        _session.Subscribe<PetAdopted>(e=>Mark("pet_adopted",e));
        void Frame()
        {
            if(gameplayCamera){_camera.Camera.MakeCurrent();return;}
            var aim=fixedAim??(_avatar.GlobalPosition+Vector3.Up);
            camera.GlobalPosition=aim+offset;camera.LookAt(aim);camera.MakeCurrent();
        }
        async Task Hold(double seconds,Action? input=null)
        {
            ulong until=Time.GetTicksMsec()+(ulong)(seconds*1000);
            do { input?.Invoke();Frame();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
            while(Time.GetTicksMsec()<until);
        }
        CreatureView Target(string contains)=>sim.Creatures.Where(c=>c.Alive&&(c.Hostile||contains.Length>0)&&
            (contains.Length==0||c.DefId.Contains(contains,StringComparison.Ordinal)||c.Key.Contains(contains,StringComparison.Ordinal)))
            .OrderBy(c=>Pos(c.Body).DistanceSquaredTo(_avatar.GlobalPosition)).FirstOrDefault()
            ??throw new InvalidDataException("No natural target: "+contains);
        async Task Fight(JsonElement step)
        {
            string selector=Text(step,"target");
            var initial=Target(selector);string key=initial.Key;
            Mark("fight_target",initial);
            bool ranged=Number(step,"range",0)>0;
            double began=Time.GetTicksMsec()/1000.0,seconds=Number(step,"seconds",45);
            double dodgeAt=began+4,guardUntil=began+2;
            while(Time.GetTicksMsec()/1000.0-began<seconds)
            {
                var target=sim.Creatures.Single(c=>c.Key==key);
                if(!target.Alive){Mark("kill",target);break;}
                if(sim.Combat.Downed||sim.Combat.Health<=0)throw new InvalidDataException("Player downed during real fight");
                var to=Pos(target.Body)-_avatar.GlobalPosition;to.Y=0;
                _camera.Yaw=Mathf.Atan2(-to.X,-to.Z);
                double now=Time.GetTicksMsec()/1000.0;
                float reach=ranged?(float)Number(step,"range",14):
                    (sim.Combat.Weapon.ReachMm+sim.Setup.Combat.Creatures[target.DefId].RadiusMm-100)/1000f;
                var goal=to;
                if(!ranged&&target.DefId is "creature.construct.gate_warden" or "creature.construct.animated_armour")
                {
                    float facing=target.Body.FacingMdeg/1000f*Mathf.Pi/180f;
                    var behind=to-new Vector3(Mathf.Sin(facing),0,Mathf.Cos(facing))*(reach-.5f);
                    if(behind.Length()>.7f)goal=behind;
                }
                _controller.SteerWorld(to.Length()>reach||goal!=to&&goal.Length()>.7f?goal.Normalized():Vector3.Zero,Gait.Run,_camera,true);
                bool guard=now<guardUntil||ranged;
                _controller.Guard(guard);
                if(now>=dodgeAt&&!ranged){_controller.Dodge(new Vector3(-to.Z,0,to.X).Normalized());dodgeAt=now+9;Mark("dodge");}
                if(now>=guardUntil&&to.Length()<=reach&&sim.Combat.Phase==CombatPhase.Idle)_controller.Attack();
                fixedAim=(_avatar.GlobalPosition+Pos(target.Body))*.5f+Vector3.Up*.9f;
                Frame();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            _controller.Guard(false);_controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);fixedAim=null;
        }
        Frame();await Hold(3);
        ulong last=Time.GetTicksMsec();
        for(int stable=0;stable<60;)
        {
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            ulong now=Time.GetTicksMsec();stable=now-last<250?stable+1:0;last=now;
        }
        Mark("world_ready",new {actors=sim.Npcs.Select(n=>new {n.Id,n.Body}),creatures=sim.Creatures.Select(c=>new {c.Key,c.DefId,c.Body,c.Alive})});
        File.WriteAllText(Path.Combine(directory,"record_ready.json"),"{}");
        ulong deadline=Time.GetTicksMsec()+60000;
        while(!File.Exists(Path.Combine(directory,"recording.started")))
        {if(Time.GetTicksMsec()>deadline)throw new TimeoutException("No owned recorder first frame");await Hold(.1);}
        int bus=AudioServer.GetBusIndex("Master"),slot=AudioServer.GetBusEffectCount(bus);
        var audio=new AudioEffectRecord();AudioServer.AddBusEffect(bus,audio);audio.SetRecordingActive(true);
        var frameTimes=new List<double>();
        void Sample()=>frameTimes.Add(GetProcessDeltaTime()*1000);
        GetTree().ProcessFrame+=Sample;
        Mark("audio_start");
        try
        {
            foreach(var step in plan.RootElement.GetProperty("steps").EnumerateArray())
            {
                string action=Text(step,"action");Mark("start_"+action,step);
                double seconds=Number(step,"seconds",5);
                if(step.TryGetProperty("camera",out var view))gameplayCamera=view.GetString()=="gameplay";
                if(step.TryGetProperty("offset",out var off))offset=V(off);
                if(step.TryGetProperty("aim",out var aim))fixedAim=V(aim);
                if(step.TryGetProperty("ground",out var ground))
                {
                    var point=V(ground);fixedAim=new Vector3(point.X,_ground.Height(point.X,point.Z)+point.Y,point.Z);
                }
                if(step.TryGetProperty("ui",out var visible)){ui=visible.GetBoolean();_hud.Visible=ui;if(_hintLine is not null)_hintLine.Visible=ui;}
                switch(action)
                {
                    case "equip":
                        var entry=sim.Player.Inventory.FirstOrDefault(i=>i.DefId==Text(step,"id"))??throw new InvalidDataException("No carried filming kit: "+Text(step,"id"));
                        _session.Submit(new EquipCommand(sim.PlayerId,entry.ItemId));await Hold(seconds);break;
                    case "fight":await Fight(step);break;
                    case "move":
                        var direction=V(step.GetProperty("direction"));
                        var gait=Enum.Parse<Gait>(Text(step,"gait","Run"));
                        await Hold(seconds,()=>_controller.SteerWorld(direction,gait,_camera));
                        _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);break;
                    case "guard":_controller.Guard(true);await Hold(seconds);_controller.Guard(false);break;
                    case "dodge":_controller.Dodge(V(step.GetProperty("direction")));await Hold(seconds);break;
                    case "attack":_controller.Attack();await Hold(seconds);break;
                    case "jump":_controller.Jump();await Hold(seconds);break;
                    case "glide":_controller.Glide(step.GetProperty("enabled").GetBoolean());await Hold(seconds);break;
                    case "crouch":_controller.Crouch(step.GetProperty("enabled").GetBoolean());await Hold(seconds);break;
                    case "ability":_controller.PeopleAbility(Text(step,"id"));await Hold(seconds);break;
                    case "takedown":_controller.Takedown();await Hold(seconds);break;
                    case "kal_route":
                        _glideFrom=(sim.Player.Body,step.GetProperty("dx").GetInt32(),step.GetProperty("dz").GetInt32(),0);
                        var down=new Vector3(_glideFrom.DirX/1000f,0,_glideFrom.DirZ/1000f);
                        var oldOffset=offset;
                        float side=(float)Number(step,"camera_side",1);
                        void FollowWings()
                        {
                            var aim=_avatar.GlobalPosition+Vector3.Up*.8f;
                            var eye=aim+new Vector3(down.Z,0,-down.X)*side*4.8f-down*1.2f+Vector3.Up*1.4f;
                            eye.Y=Math.Max(eye.Y,_ground.Height(eye.X,eye.Z)+.8f);
                            camera.Fov=55;camera.GlobalPosition=eye;camera.LookAt(aim);camera.MakeCurrent();
                        }
                        GetTree().ProcessFrame+=FollowWings;
                        try { await KalRoute(Mark); }
                        finally { GetTree().ProcessFrame-=FollowWings;offset=oldOffset; }
                        break;
                    case "decoy_route":
                        camera.GetNode<OmniLight3D>("CharacterFill").Reparent(_camera.Camera,false);
                        _camera.Camera.MakeCurrent();await DecoyRoute(Mark);
                        _camera.Camera.GetNode<OmniLight3D>("CharacterFill").Reparent(camera,false);break;
                    case "interact":_controller.Interact(Text(step,"id"));await Hold(seconds);break;
                    case "go":
                        var goal=V(step.GetProperty("position"));
                        await SteerTo(goal.X,goal.Z,Gait.Run,seconds);await Hold(1);break;
                    case "talk":_controller.Talk(Text(step,"id"));await Hold(seconds);break;
                    case "answer":
                        string reply=Text(step,"id");
                        if(reply.Length==0)reply=_dialogue.Replies.First(id=>id.Contains(Text(step,"contains"),StringComparison.Ordinal));
                        if(!_dialogue.Replies.Contains(reply))throw new InvalidDataException("Conversation does not offer "+reply);
                        _dialogue.Answer(reply);await Hold(seconds);break;
                    case "hire":ShowHire(Text(step,"id"));await Hold(seconds);break;
                    case "trade":OpenTrade(Text(step,"id"));await Hold(seconds);break;
                    case "clock":
                        AdvanceTo((int)Number(step,"minute",480));Mark("normal_offline_schedule_advance");await Hold(seconds);break;
                    case "away_hours":
                        // The real offline advance and its returned log, as at login; no synthetic digest or event rows.
                        var advance=sim.Advance((long)(Number(step,"hours",8)*sim.Setup.Clock.TicksPerDay/sim.Setup.Clock.HoursPerDay));
                        Mark("normal_offline_advance",new {advance.FromTick,advance.ToTick,lines=advance.Lines.Count});
                        ShowAway(AwayOf(advance.FromTick,advance.ToTick,advance.Lines,false));await Hold(seconds);break;
                    case "road_start":
                        OpenCreator();_creator.Use(_creator.Draft with {Name="Ada",PeopleId="people.veth",BodyPresetId="body.veth_f.average",Start=StartPoint.Road});
                        if(!_creator.PressBegin())throw new InvalidDataException("Normal road Begin refused: "+_creator.Draft.Problem);
                        sim=_session.Simulation!;native=(SkinnedFigure)_avatar;gameplayCamera=true;
                        Mark("actual_road_start",new {sim.Player.Identity,model=native.ModelId,lying=_lying});
                        await Hold(seconds);break;
                    case "begin_people":
                        string nextPeople=Text(step,"people");
                        string nextBody=$"body.{nextPeople[7..]}_f.average";
                        OpenCreator();
                        if(!_creator.DrawnBodyIds(nextPeople).Contains(nextBody))throw new InvalidDataException("Uninstalled real creator body: "+nextBody);
                        _creator.Use(_creator.Draft with {Name="Ada",PeopleId=nextPeople,BodyPresetId=nextBody,Start=StartPoint.Waystone});
                        if(!_creator.PressBegin())throw new InvalidDataException("Normal people Begin refused: "+_creator.Draft.Problem);
                        sim=_session.Simulation!;native=(SkinnedFigure)_avatar;gameplayCamera=false;fixedAim=null;
                        if(nextPeople!="people.veth"&&!native.ModelId.Contains("_race_"+nextPeople[7..]+"_",StringComparison.Ordinal))throw new InvalidDataException("New creator model mismatch");
                        Mark("actual_people_start",new {sim.Player.Identity,model=native.ModelId});await Hold(seconds);break;
                    case "wake":GetUp();await Hold(seconds);break;
                    case "touch_stone":_controller.TouchStone(Text(step,"id"));await Hold(seconds);break;
                    case "use_stone":
                        string stoneKey=Text(step,"id");
                        var stone=sim.Waystones.Single(w=>w.Site.Key==stoneKey);
                        var centre=Footprints.Center(stone.Site.Body);
                        if(new Vector2(centre.Item1/1000f-_avatar.GlobalPosition.X,centre.Item2/1000f-_avatar.GlobalPosition.Z).Length()>4)
                            throw new InvalidDataException("Filming hand is not in reach of the real Waystone");
                        // The same known-stone interaction as Main.Input, then close its real panel for the effect shot.
                        if(stone.Known){WaystoneResponse(stoneKey,true);_soundEvents.WaystoneUse(stoneKey);Show(_waystones,stoneKey);}
                        else _controller.TouchStone(stoneKey);
                        await Hold(.1);CloseScreens();await Hold(seconds);break;
                    case "adopt":
                        var lead=sim.Player.Inventory.First(i=>i.DefId=="item.misc.hound_pup");
                        _session.Submit(new AdoptPetCommand(sim.PlayerId,lead.ItemId));await Hold(seconds);break;
                    case "pet":
                        await Hold(seconds,()=>fixedAim=Pos(sim.Pets.First().Body)+Vector3.Up*.4f);break;
                    case "npc":
                        var npc=sim.Npcs.Single(n=>n.Id==Text(step,"id"));fixedAim=Pos(npc.Body)+Vector3.Up;
                        await Hold(seconds,()=>fixedAim=Pos(sim.Npcs.Single(n=>n.Id==npc.Id).Body)+Vector3.Up);break;
                    case "creator":
                        OpenCreator();
                        string people=Text(step,"people","people.veth");
                        _creator.Use(_creator.Draft with {PeopleId=people,BodyPresetId=$"body.{people[7..]}_f.average"});
                        _creator.Go(Enum.Parse<CreatorStep>(Text(step,"page","People")));_creator.Frame((float)Number(step,"zoom",0));
                        await Hold(seconds);CloseCreator();break;
                    case "lore":
                        CloseScreens();_journal.OpenLore(Text(step,"id"));_journal.Refresh(_session);_journal.Visible=true;await Hold(seconds);break;
                    case "button":
                        var button=GetTree().Root.FindChildren("*",nameof(Button),true,false).OfType<Button>().FirstOrDefault(b=>b.IsVisibleInTree()&&
                            (b.GetMeta("ui_id","").AsString()==Text(step,"id")||b.Text==Text(step,"text")))
                            ??throw new InvalidDataException("No visible real button: "+Text(step,"id"));
                        button.EmitSignal(Button.SignalName.Pressed);await Hold(seconds);break;
                    case "panel":
                        CloseScreens();
                        switch(Text(step,"id"))
                        {
                            case "inventory":_inventory.Open(null);break;
                            case "journal":Show(_journal);break;
                            case "map":Show(_map);break;
                            case "character":Show(_character);break;
                            case "skills":ShowSkills(null);break;
                            case "party":Show(_companionsPanel);break;
                            case "notice":Show(_noticeBoard,Text(step,"station"));break;
                            case "settings":_settings.Open(Text(step,"page","display"));break;
                            case "report":OpenReport();break;
                            case "close":break;
                            default:throw new InvalidDataException("Unknown real panel");
                        }
                        await Hold(seconds);break;
                    case "look":await Hold(seconds);break;
                    case "orbit":
                        var start=offset;float angle=(float)Number(step,"degrees",45)*Mathf.Pi/180f;
                        ulong t=Time.GetTicksMsec();await Hold(seconds,()=>offset=start.Rotated(Vector3.Up,angle*(Time.GetTicksMsec()-t)/(float)(seconds*1000)));break;
                    case "push":
                        var from=offset;var to=V(step.GetProperty("to"));ulong pushAt=Time.GetTicksMsec();
                        await Hold(seconds,()=>offset=from.Lerp(to,Mathf.Clamp((Time.GetTicksMsec()-pushAt)/(float)(seconds*1000),0,1)));break;
                    default:throw new InvalidDataException("Unknown film action: "+action);
                }
                Mark("end_"+action);
            }
        }
        finally
        {
            GetTree().ProcessFrame-=Sample;
            File.WriteAllText(Path.Combine(directory,"frame_ms.json"),JsonSerializer.Serialize(frameTimes));
            audio.SetRecordingActive(false);using(var wav=audio.GetRecording())wav?.SaveToWav(Path.Combine(directory,"game_audio.wav"));
            AudioServer.RemoveBusEffect(bus,slot);Mark("audio_end");
        }
        File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {status="CAPTURE_ONLY_PUBLIC_SOURCE_REQUIRES_REVIEW",plan=planPath,world_tick=sim.WorldTick,engine_max_fps=Engine.MaxFps}));
        File.WriteAllText(Path.Combine(directory,"recording.finished"),"done");
        deadline=Time.GetTicksMsec()+15000;
        while(!File.Exists(Path.Combine(directory,"recording.stopped"))&&Time.GetTicksMsec()<deadline)await Hold(.1);
    }
}
