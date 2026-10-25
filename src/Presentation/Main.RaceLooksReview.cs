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
                    string donor = s.GetProperty("donor").GetString()!;
                    string? people = s.TryGetProperty("people", out var wanted) ? wanted.GetString() : null;
                    var person = _bindings.People.Where(p => p.Value.Model == donor && (people is null || p.Value.PeopleId == people))
                        .Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).First();
                    var figure = SkinnedFigure.Create(_art, _bindings, person, s.TryGetProperty("body", out var body) ? body.GetString() : null)
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
