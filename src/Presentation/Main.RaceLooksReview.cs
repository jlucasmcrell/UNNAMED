using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

public partial class Main
{
    /// <summary>
    /// Opt-in race-look review (OTHERREACH_RACE_LOOKS_PLAN): HumGen race bodies drawn by the normal figure path on an existing
    /// person's binding, in the live frontier, wearing their people's look exactly as in play. Steps come from the plan file.
    /// </summary>
    private async Task RunRaceLooksReview(string directory, Camera3D camera)
    {
        using var plan = JsonDocument.Parse(File.ReadAllText(OS.GetEnvironment("OTHERREACH_RACE_LOOKS_PLAN")));
        DisplayServer.WindowSetTitle("Otherreach - " + Path.GetFileName(directory));
        _innFreeCamera = true;
        GetViewport().CanvasCullMask = 0; // Diagnostic anatomy/motion frames contain no tutorial or lesson overlays.
        camera.MakeCurrent();
        _hud.Visible = false;
        _avatar.Visible = false;
        _npcs.Visible = false;
        var stage = _avatar.GlobalPosition;
        var ground = _npcs.Ground ?? ((x, z) => stage.Y);
        var figures = new Dictionary<string, (SkinnedFigure Figure, Vector3 Feet, float Yaw, float Speed)>();
        var log = new List<object>();
        ulong? recordingStarted = null;
        string? clip = null;
        ulong clipStart = 0;

        Vector3 Feet(float x, float z) => new(stage.X + x, ground(stage.X + x, stage.Z + z), stage.Z + z);
        async Task Frames(double seconds, Action<double>? each = null)
        {
            ulong until = Time.GetTicksMsec() + (ulong)(seconds * 1000);
            do
            {
                double dt = GetProcessDeltaTime();
                each?.Invoke(dt);
                foreach (var (id, f) in figures.ToList())
                    f.Figure.Pose(f.Feet, f.Yaw, f.Speed, dt);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (GetViewport().GetCamera3D() != camera) throw new InvalidDataException("Race/FACS review camera is not rendering");
            } while (Time.GetTicksMsec() < until);
        }
        void Aim(JsonElement s)
        {
            var f = figures[s.GetProperty("id").GetString()!];
            float angle = Mathf.DegToRad(Num(s, "angle", 0)), dist = Num(s, "dist", 5);
            Vector3 aim = s.TryGetProperty("bone", out var bone) && f.Figure.Skeleton.FindBone(bone.GetString()!) is int b and >= 0
                ? f.Figure.Skeleton.GlobalTransform * f.Figure.Skeleton.GetBoneGlobalPose(b).Origin
                : f.Feet + Vector3.Up * Num(s, "height", .9f);
            aim += Vector3.Up * Num(s, "aim_up", 0);
            if (aim.DistanceTo(f.Feet) > 5) throw new InvalidDataException("Review bone aim is not at the posed world-space body");
            camera.Fov = Num(s, "fov", 40);
            camera.GlobalPosition = aim + new Basis(Vector3.Up, f.Yaw + angle) * new Vector3(Num(s, "side", 0), Num(s, "lift", 0), dist);
            camera.LookAt(aim);
        }
        static float Num(JsonElement s, string key, float fallback) => s.TryGetProperty(key, out var v) ? v.GetSingle() : fallback;
        foreach (var s in plan.RootElement.GetProperty("steps").EnumerateArray())
        {
            string step = s.GetProperty("do").GetString()!;
            GD.Print("RACE_LOOKS step " + step + " " + s);
            switch (step)
            {
                case "arena":
                {
                    _scatter.Visible=false;
                    stage=_avatar.GlobalPosition+Vector3.Up*8;
                    ground=(_,_)=>stage.Y;
                    AddChild(new MeshInstance3D {
                        Name="MotionInspectionPlatform",Position=stage-Vector3.Up*.05f,
                        Mesh=new BoxMesh {Size=new Vector3(28,.1f,28)},
                        MaterialOverride=new StandardMaterial3D {AlbedoColor=new Color(.18f,.2f,.2f),Roughness=1}
                    });
                    log.Add(new {step,stage=stage.ToString(),scatter_visible=_scatter.Visible,note="Diagnostic flat raised inspection platform; production figures/materials/motion"});
                    break;
                }
                case "reset":
                {
                    string id=s.GetProperty("id").GetString()!;var f=figures[id];
                    figures[id]=f with {Feet=stage+new Vector3(0,0,-4),Speed=0};
                    f.Figure.Pose(figures[id].Feet,f.Yaw,0,0);await Frames(.15);break;
                }
                case "eye_audit":
                {
                    string id=s.GetProperty("id").GetString()!;var figure=figures[id].Figure;
                    var materials=new List<object>();var saved=new HashSet<ulong>();
                    foreach(var mesh in figure.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>()
                        .Where(m=>m.Name.ToString().Contains("Eye",StringComparison.OrdinalIgnoreCase) && !m.Name.ToString().Contains("lash",StringComparison.OrdinalIgnoreCase)))
                    for(int surface=0;surface<(mesh.Mesh?.GetSurfaceCount()??0);surface++)
                    {
                        if(mesh.GetActiveMaterial(surface) is not { } material)continue;
                        foreach(var property in material.GetPropertyList())
                        {
                            string name=property["name"].AsString();
                            var value=material.Get(name);
                            if(value.VariantType!=Variant.Type.Object || value.AsGodotObject() is not Texture2D texture)continue;
                            string file=id+"_eye_"+texture.GetInstanceId()+".png";
                            if(saved.Add(texture.GetInstanceId()))
                            {
                                using var image=texture.GetImage();
                                if(image is not null){if(image.IsCompressed())image.Decompress();image.SavePng(Path.Combine(directory,file));}
                            }
                            materials.Add(new {mesh=mesh.Name.ToString(),surface,material=material.ResourceName,property=name,width=texture.GetWidth(),height=texture.GetHeight(),file});
                        }
                    }
                    var rig=figure.Skeleton;var bones=new List<object>();
                    foreach(string name in new[]{"Head","LeftEye","RightEye"})
                        if(rig.FindBone(name) is int bone && bone>=0)bones.Add(new {name,world=(rig.GlobalTransform*rig.GetBoneGlobalPose(bone).Origin).ToString(),rest=rig.GetBoneRest(bone).ToString(),pose=rig.GetBonePoseRotation(bone).ToString()});
                    var eyeTarget=figure.FindChildren("FacialEyeTarget","",true,false).OfType<Node3D>().FirstOrDefault();
                    File.WriteAllText(Path.Combine(directory,id+"_eyes.json"),JsonSerializer.Serialize(new {materials,bones,target=eyeTarget?.GlobalPosition.ToString(),native_eye_bindings=figure.FindChildren("*","",true,false).OfType<FaceDriver>().Single().NativeEyeBindings},new JsonSerializerOptions {WriteIndented=true}));
                    break;
                }
                case "spawn":
                {
                    string donor = s.GetProperty("donor").GetString()!;
                    string? people = s.TryGetProperty("people", out var wanted) ? wanted.GetString() : null;
                    var appearance = s.TryGetProperty("people_override", out var overridePeople)
                        ? _bindings.People["player"] with { PeopleId = overridePeople.GetString(), UniformScale = 1 } : null;
                    var person = appearance is not null ? "player" : _bindings.People.Where(p => p.Value.Model == donor && (people is null || p.Value.PeopleId == people))
                        .Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).First();
                    var figure = SkinnedFigure.Create(_art, _bindings, person, s.TryGetProperty("body", out var body) ? body.GetString() : null, appearance)
                        ?? throw new InvalidDataException("Race look body did not load: " + s);
                    AddChild(figure);
                    figure.SetStance(CombatStance.AtRest);
                    float yaw = Mathf.DegToRad(Num(s, "yaw", 0));
                    figure.SetMovementDirection(new Basis(Vector3.Up, yaw) * Vector3.Back);
                    float x = Num(s,"x",0), z = Num(s,"z",0);
                    Vector3 feet = s.TryGetProperty("world",out var absolute) && absolute.GetBoolean()
                        ? new Vector3(x,ground(x,z),z) : Feet(x,z);
                    figures[s.GetProperty("id").GetString()!] = (figure, feet, yaw, 0);
                    figure.Pose(feet, yaw, 0, 0);
                    await Frames(.05); // Bone-based framing must read the positioned skeleton, not its initial origin.
                    log.Add(new { step, person, model = body.ValueKind == JsonValueKind.String ? body.GetString() : _bindings.People[person].Model, people = (appearance ?? _bindings.People[person]).PeopleId });
                    break;
                }
                case "wings":
                {
                    var f = figures[s.GetProperty("id").GetString()!].Figure;
                    var wings = f.Skeleton.GetNodeOrNull<KalWings>("KalWings")
                        ?? KalWings.Attach(f.Skeleton, s.GetProperty("glb").GetString()!, Num(s, "back", .1f))
                        ?? throw new InvalidDataException("Wings did not attach");
                    switch (s.GetProperty("play").GetString())
                    {
                        case "folded": wings.Openness = 0; break;
                        case "deployed": wings.Openness = 1; break;
                        case "deploy": wings.Openness = 0; wings.SetState(WingState.Glide); break;
                        case "flare": wings.SetState(WingState.Flare); break;
                        case "fold": wings.SetState(WingState.Folded); break;
                    }
                    break;
                }
                case "remove":
                {
                    string id = s.GetProperty("id").GetString()!;
                    figures[id].Figure.QueueFree();
                    figures.Remove(id);
                    break;
                }
                case "hour":
                    VisualOptions.Pin("hour", s.GetProperty("value").GetString()!);
                    await Frames(Num(s, "settle", 2));
                    break;
                case "camera":
                    Aim(s);
                    await Frames(Num(s, "settle", .4f));
                    break;
                case "wait":
                    await Frames(Num(s, "s", 1));
                    break;
                case "shot":
                    await Rcd3Shot(directory, s.GetProperty("name").GetString()!);
                    break;
                case "walk":
                {
                    string id = s.GetProperty("id").GetString()!;
                    float speed = Num(s, "speed", 1.4f);
                    var dir = new Basis(Vector3.Up, Mathf.DegToRad(Num(s, "dir", 0))) * Vector3.Back;
                    var f = figures[id];
                    f.Figure.SetMovementDirection(dir);
                    figures[id] = f with { Speed = speed, Yaw = s.TryGetProperty("facing", out var facing) ? Mathf.DegToRad(facing.GetSingle()) : Mathf.Atan2(dir.X, dir.Z) };
                    await Frames(Num(s, "seconds", 4), dt =>
                    {
                        var g = figures[id];
                        var next = g.Feet + dir * speed * (float)dt;
                        figures[id] = g with { Feet = new Vector3(next.X, ground(next.X, next.Z), next.Z) };
                        if (s.TryGetProperty("cam", out var cam))
                            Aim(cam);
                    });
                    figures[id] = figures[id] with { Speed = 0 };
                    log.Add(new { step, id, speed, actual_clip = f.Figure.ClipState, fps = Engine.GetFramesPerSecond() });
                    break;
                }
                case "jaw":
                {
                    string id = s.GetProperty("id").GetString()!;
                    var figure = figures[id].Figure;
                    var driver = figure.FindChildren("*", "", true, false).OfType<FaceDriver>().Single();
                    float requested = Num(s, "weight", 0);
                    if (requested is < 0 or > 1) throw new InvalidDataException("FACS jaw weight outside 0..1");
                    figure.Say(null);figure.SetTalking(true);
                    driver.ReviewJawOpen = requested;
                    await Frames(.6);
                    var hinge = figure.FindChildren("*", "", true, false).OfType<HumGenJaw>().Single();
                    if (!driver.NativeJawBound || !hinge.Active || Math.Abs(driver.JawWeight-requested) > .025f
                        || Math.Abs(hinge.AppliedWeight-requested) > .025f)
                        throw new InvalidDataException("Native FACS jaw modifier did not follow requested weight");
                    log.Add(new { step, id, requested, actual = driver.JawWeight, native_jaw_bound = driver.NativeJawBound,
                        modifier_weight = hinge.AppliedWeight, modifier_angle_rad = hinge.AppliedAngle,
                        native_pose_before = hinge.PoseBefore.ToString(), native_pose_after = hinge.PoseAfter.ToString() });
                    break;
                }
                case "jaw_release":
                    figures[s.GetProperty("id").GetString()!].Figure.FindChildren("*", "", true, false)
                        .OfType<FaceDriver>().Single().ReviewJawOpen = null;
                    break;
                case "talk":
                {
                    string id = s.GetProperty("id").GetString()!;
                    var figure = figures[id].Figure;
                    var driver = figure.FindChildren("*", "", true, false).OfType<FaceDriver>().Single();
                    figure.SetTalking(true);
                    figure.Say("Ada, we should go to the inn. Who are you? We can walk there together.");
                    float jawPeak = 0;
                    await Frames(Num(s, "seconds", 3), _ => jawPeak = Math.Max(jawPeak, driver.JawWeight));
                    if (jawPeak < .045f) throw new InvalidDataException("Race talking gate produced no speaking jaw: " + id);
                    log.Add(new { step, id, actual_clip = figure.ClipState, jaw_peak = jawPeak });
                    break;
                }
                case "silence":
                {
                    var figure = figures[s.GetProperty("id").GetString()!].Figure;
                    figure.SetTalking(false); figure.Say(null);
                    break;
                }
                case "record_ready":
                    File.WriteAllText(Path.Combine(directory, "record_ready.json"), "{}");
                    while (!File.Exists(Path.Combine(directory, "recording.started")))
                        await Frames(.1);
                    recordingStarted = Time.GetTicksMsec();
                    break;
                case "clip_start":
                    clip = s.GetProperty("name").GetString();
                    clipStart = Time.GetTicksMsec() - (recordingStarted ?? 0);
                    break;
                case "clip_end":
                    log.Add(new { step = "clip", name = clip, start_ms = clipStart, end_ms = Time.GetTicksMsec() - (recordingStarted ?? 0) });
                    File.WriteAllText(Path.Combine(directory,"race_looks_partial.json"),JsonSerializer.Serialize(log));
                    break;
                case "record_end":
                    File.WriteAllText(Path.Combine(directory, "recording.finished"), "{}");
                    await Frames(1.5);
                    break;
                default:
                    throw new InvalidDataException("Unknown race look step: " + step);
            }
        }
        File.WriteAllText(Path.Combine(directory, "race_looks.json"), JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(directory, "complete.json"), "{}");
    }
}
