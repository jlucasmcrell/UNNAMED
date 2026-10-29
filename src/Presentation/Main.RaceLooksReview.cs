using Godot;
using System.Collections.Immutable;
using System.Text.Json;
using UNNAMED.Domain.Character;
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
        _hud.Visible = false;
        _avatar.Visible = false;
        _npcs.Visible = false;
        var stage = _avatar.GlobalPosition;
        var ground = _npcs.Ground ?? ((x, z) => stage.Y);
        var figures = new Dictionary<string, (SkinnedFigure Figure, Vector3 Feet, float Yaw, float Speed)>();
        var log = new List<object>();
        ulong? recordingStarted = null;
        OrenthDecoy? decoy = null;
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
                case "spawn":
                {
                    string? people = s.TryGetProperty("people", out var wanted) ? wanted.GetString() : null;
                    // A binding by its own id, or the first bound to a donor model (and people, when given).
                    string person = s.TryGetProperty("person", out var named) ? named.GetString()!
                        : _bindings.People.Where(p => p.Value.Model == s.GetProperty("donor").GetString() && (people is null || p.Value.PeopleId == people))
                            .Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).First();
                    GD.Print($"RACE_LOOKS person {person}: model {_bindings.People[person].Model}, people {_bindings.People[person].PeopleId}");
                    // The player binding takes the people given, as a player of that people is drawn.
                    var figure = SkinnedFigure.Create(_art, _bindings, person, s.TryGetProperty("body", out var body) ? body.GetString() : null,
                                     people: person == "player" ? people : null)
                        ?? throw new InvalidDataException("Race look body did not load: " + s);
                    AddChild(figure);
                    figure.SetStance(CombatStance.AtRest);
                    float yaw = Mathf.DegToRad(Num(s, "yaw", 0));
                    figure.SetMovementDirection(new Basis(Vector3.Up, yaw) * Vector3.Back);
                    figures[s.GetProperty("id").GetString()!] = (figure, Feet(Num(s, "x", 0), Num(s, "z", 0)), yaw, 0);
                    log.Add(new { step, person, model = body.ValueKind == JsonValueKind.String ? body.GetString() : _bindings.People[person].Model, people = _bindings.People[person].PeopleId });
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
                case "decoy":
                {
                    var f = figures[s.GetProperty("id").GetString()!].Figure;
                    decoy = f.FindChildren("OrenthPhase", "", true, false).OfType<OrenthPhase>().Single().Linger(Num(s, "seconds", 1), f.GlobalTransform);
                    break;
                }
                case "phase":
                    figures[s.GetProperty("id").GetString()!].Figure.FindChildren("OrenthPhase", "", true, false).OfType<OrenthPhase>().Single().Phase(Num(s, "seconds", 1.5f));
                    break;
                case "probe":
                {
                    var f = figures[s.GetProperty("id").GetString()!].Figure;
                    log.Add(new { step, meshes = f.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().Select(m => new
                    {
                        name = m.Name.ToString(), visible = m.IsVisibleInTree(), skinned = m.Skin is not null,
                        surfaces = Enumerable.Range(0, m.Mesh?.GetSurfaceCount() ?? 0).Select(i =>
                        {
                            var material = m.GetSurfaceOverrideMaterial(i) ?? m.MaterialOverride ?? m.Mesh!.SurfaceGetMaterial(i);
                            return new
                            {
                                type = material?.GetType().Name, resource = material?.ResourceName,
                                base_map = material is ShaderMaterial sm ? sm.GetShaderParameter("base_map").AsGodotObject()?.GetType().Name : null,
                                texture_albedo = material is ShaderMaterial tm ? tm.GetShaderParameter("texture_albedo").AsGodotObject()?.GetType().Name : null,
                                albedo_texture = (material as BaseMaterial3D)?.AlbedoTexture?.GetType().Name,
                            };
                        }).ToList(),
                    }).ToList() });
                    File.WriteAllText(Path.Combine(directory, "race_looks.json"), JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true }));
                    break;
                }
                case "shatter":
                    decoy?.Shatter();
                    break;
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
                case "look":
                {
                    // The figure wears a creator look: the same parts, chains and tints the creator draws, through the same call.
                    string id = s.GetProperty("id").GetString()!;
                    var model = s.GetProperty("model").GetString()!;
                    var sliders = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
                    if (s.TryGetProperty("sliders", out var values))
                        foreach (var slider in values.EnumerateObject()) sliders[slider.Name] = slider.Value.GetInt32();
                    var look = new CharacterAppearance(
                        s.TryGetProperty("stature", out var stature) ? stature.GetInt32() : 1750,
                        s.TryGetProperty("face", out var face) ? face.GetString() : null,
                        s.TryGetProperty("hair", out var hair) ? hair.GetString() : null,
                        s.TryGetProperty("facial_hair", out var beard) ? beard.GetString() : null,
                        s.TryGetProperty("outfit", out var outfit) ? outfit.GetString() : null,
                        s.TryGetProperty("footwear", out var shoes) ? shoes.GetString() : null) { Sliders = sliders.ToImmutable() };
                    var figure = figures[id].Figure;
                    PeopleAppearance.ApplyLook(figure, s.TryGetProperty("people", out var people) ? people.GetString() : null,
                        look, _art, model, dyeClothes: false);
                    GD.Print($"RACE_LOOKS look {id}: hair {look.Hair}, outfit {look.Outfit}, footwear {look.Footwear}");
                    break;
                }
                case "pose":
                {
                    // Hold a named clip on the figure, which is how a jump is shown without a locomotion speed.
                    var figure = figures[s.GetProperty("id").GetString()!].Figure;
                    figure.SetRestPose(s.GetProperty("clip").GetString());
                    break;
                }
                case "film":
                {
                    // A numbered still per frame, with the camera kept on the figure so a walk cannot carry it out of shot.
                    string name = s.GetProperty("name").GetString()!;
                    float fps = Num(s, "fps", 24);
                    double during = Num(s, "seconds", 2);
                    int total = Math.Max(1, (int)Math.Round(during * fps));
                    string? watched = s.TryGetProperty("id", out var who) ? who.GetString() : null;
                    for (int frame = 0; frame < total; frame++)
                    {
                        double dt = GetProcessDeltaTime();
                        foreach (var (_, f) in figures.ToList()) f.Figure.Pose(f.Feet, f.Yaw, f.Speed, dt);
                        if (watched is not null && figures.TryGetValue(watched, out var subject))
                        {
                            var aim = subject.Feet + Vector3.Up * Num(s, "height", 1.05f);
                            camera.Fov = Num(s, "fov", 38);
                            camera.GlobalPosition = aim + new Basis(Vector3.Up, subject.Yaw + Mathf.DegToRad(Num(s, "angle", 200)))
                                * new Vector3(0f, 0f, Num(s, "dist", 4.2f));
                            camera.LookAt(aim);
                        }
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        GetViewport().GetTexture().GetImage()
                            .SavePng(Path.Combine(directory, $"{name}_{frame:000}.png"));
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    }
                    break;
                }
                case "walk":
                {
                    string id = s.GetProperty("id").GetString()!;
                    float speed = Num(s, "speed", 1.4f);
                    var dir = new Basis(Vector3.Up, Mathf.DegToRad(Num(s, "dir", 0))) * Vector3.Back;
                    var f = figures[id];
                    f.Figure.SetMovementDirection(dir);
                    figures[id] = f with { Speed = speed, Yaw = Mathf.Atan2(dir.X, dir.Z) };
                    await Frames(Num(s, "seconds", 4), dt =>
                    {
                        var g = figures[id];
                        var next = g.Feet + dir * speed * (float)dt;
                        figures[id] = g with { Feet = new Vector3(next.X, ground(next.X, next.Z), next.Z) };
                        if (s.TryGetProperty("cam", out var cam))
                            Aim(cam);
                    });
                    figures[id] = figures[id] with { Speed = 0 };
                    log.Add(new { step, id, speed, fps = Engine.GetFramesPerSecond() });
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
