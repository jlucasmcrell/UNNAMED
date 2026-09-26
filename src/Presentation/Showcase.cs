// UNNAMED Presentation - scripted gameplay scenes for review video (Phase B remediation)
// Godot presentation only: every action goes through the player's own command path; nothing here changes a rule

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>--showcase &lt;dir&gt; --showcase-scene locomotion|sword|spells</c> (with <c>--profile</c> for a scene that needs a later save):
/// a fixed script of the player's own commands in real time - idle, walk, run, stop, turn, sprint, jump; the sword's ready stance,
/// attacks in a row, block and back to running; each formula cast - with the camera held behind or beside the direction of travel.
/// Recorded by Godot's Movie Maker (<c>--write-movie out.avi --fixed-fps 60</c> before <c>--</c>) it is a review clip; a picture
/// is taken at each step's middle. Exit code 0 when the script ran to its end.
/// </summary>
public sealed class Showcase
{
    private sealed record Step(string Name, double Seconds, Action<Showcase, double> Act, float CameraTurn = 0, float Pitch = -0.25f, float Distance = 3.5f);

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly List<Step> _steps;
    private int _index = -1;
    private double _clock;
    private double _stepStart;
    private bool _shot;
    private Vector3 _heading;

    public string Directory { get; }

    public Showcase(GameSession session, PlayerController controller, CameraRig camera, string directory, string scene)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        _steps = scene switch
        {
            "locomotion" => Locomotion(),
            "sword" => Sword(),
            "spells" => Spells(),
            "dialogue" => Dialogue(),
            "equipment" => Equipment(),
            "companion" => Companion(),
            "closeup" => Closeup(),
            _ => throw new ArgumentException($"--showcase-scene: '{scene}' is not locomotion, sword, spells, dialogue, equipment, companion or closeup"),
        };
    }

    // Open ground at the fixed seed: the acceptance playthrough's own way out to the boar's wallow (UiShots.ToTheWallow), then on
    // down Blackvein's rim - no wall, stone or building in the way, with a right-angle turn at the first waypoint.
    private static readonly (double X, double Z)[] Route = { (60, 118), (40, 104), (36, 92), (25, 78), (22, 62) };

    private static List<Step> Locomotion() => new()
    {
        new("settle", 1.5, (s, _) => s.Hold()),
        new("idle", 3.0, (s, _) => s.Hold()),
        new("walk", 3.0, (s, _) => s.Go(Gait.Walk)),
        new("run", 4.0, (s, _) => s.Go(Gait.Run)),
        new("stop", 2.5, (s, _) => s.Hold()),
        new("run_turn", 4.0, (s, _) => s.Go(Gait.Run)),
        new("sprint", 3.0, (s, _) => s.Go(Gait.Sprint)),
        new("stop_from_sprint", 2.5, (s, _) => s.Hold()),
        new("run_side_view", 4.0, (s, _) => s.Go(Gait.Run), CameraTurn: 90, Pitch: -0.12f, Distance: 3.2f),
        new("stop_side_view", 2.0, (s, _) => s.Hold(), CameraTurn: 90, Pitch: -0.12f, Distance: 3.2f),
        new("run_jump", 3.0, (s, t) => { s.Go(Gait.Run); if (s.Once(t, 1.0)) s._controller.Jump(); }),
        new("stand_jump", 2.5, (s, t) => { s.Hold(); if (s.Once(t, 0.5)) s._controller.Jump(); }),
        new("crouch", 2.0, (s, t) => { s.Hold(); if (s.Once(t, 0.1)) s._controller.Crouch(true); }),
        new("crouch_walk", 3.5, (s, _) => s.Go(Gait.Walk)),
        new("stand", 2.0, (s, t) => { s.Hold(); if (s.Once(t, 0.1)) s._controller.Crouch(false); }),
        new("stop_end", 2.5, (s, _) => s.Hold()),
    };

    // The same route with the camera close to the body - a front quarter, the front, the side and from above - for the legs and feet in
    // each gait.
    private static List<Step> Closeup() => new()
    {
        new("settle", 1.5, (s, _) => s.Hold(), CameraTurn: 150, Pitch: -0.2f, Distance: 2.2f),
        new("walk_front_quarter", 4.0, (s, _) => s.Go(Gait.Walk), CameraTurn: 150, Pitch: -0.2f, Distance: 2.2f),
        new("run_front_quarter", 4.0, (s, _) => s.Go(Gait.Run), CameraTurn: 150, Pitch: -0.2f, Distance: 2.4f),
        new("run_front", 4.0, (s, _) => s.Go(Gait.Run), CameraTurn: 180, Pitch: -0.15f, Distance: 2.4f),
        new("run_side", 4.0, (s, _) => s.Go(Gait.Run), CameraTurn: 90, Pitch: -0.1f, Distance: 2.4f),
        new("run_above", 4.0, (s, _) => s.Go(Gait.Run), CameraTurn: 120, Pitch: -0.9f, Distance: 2.6f),
        new("sprint_front_quarter", 3.5, (s, _) => s.Go(Gait.Sprint), CameraTurn: 150, Pitch: -0.2f, Distance: 2.8f),
        new("sprint_side", 3.5, (s, _) => s.Go(Gait.Sprint), CameraTurn: 90, Pitch: -0.1f, Distance: 2.8f),
        new("walk_side", 4.0, (s, _) => s.Go(Gait.Walk), CameraTurn: 90, Pitch: -0.1f, Distance: 2.2f),
        new("stop", 2.5, (s, _) => s.Hold(), CameraTurn: 150, Pitch: -0.2f, Distance: 2.2f),
    };

    private static List<Step> Sword() => new()
    {
        new("settle", 1.5, (s, _) => s.Hold(), CameraTurn: 135, Pitch: -0.15f, Distance: 3.0f),
        new("ready", 2.5, (s, _) => s.Hold(), CameraTurn: 135, Pitch: -0.15f, Distance: 3.0f),
        new("attacks", 6.0, (s, t) => { s.Hold(); if (s.Every(t, 1.2)) s._controller.Attack(); }, CameraTurn: 135, Pitch: -0.15f, Distance: 3.0f),
        new("recover", 2.0, (s, _) => s.Hold(), CameraTurn: 135, Pitch: -0.15f, Distance: 3.0f),
        new("block", 2.5, (s, t) => { s.Hold(); s._controller.Guard(t < 2.0); }, CameraTurn: 135, Pitch: -0.15f, Distance: 3.0f),
        new("attacks_side", 4.0, (s, t) => { s.Hold(); if (s.Every(t, 1.0)) s._controller.Attack(); }, CameraTurn: 90, Pitch: -0.12f, Distance: 3.0f),
        new("to_run", 3.0, (s, _) => s.Go(Gait.Run), CameraTurn: 0),
        new("stop_end", 2.0, (s, _) => s.Hold()),
    };

    private static List<Step> Spells() => new()
    {
        new("settle", 2.0, (s, _) => s.Hold(), CameraTurn: 150, Pitch: -0.18f, Distance: 3.6f),
        new("impulse_bolt", 4.0, (s, t) => { s.Hold(); if (s.Once(t, 0.3)) s.Cast("spell.force.impulse_bolt"); }, CameraTurn: 120, Pitch: -0.15f, Distance: 3.6f),
        new("brace_ward", 6.0, (s, t) => { s.Hold(); if (s.Once(t, 0.3)) s.Cast("spell.warding.brace_ward"); }, CameraTurn: 150, Pitch: -0.18f, Distance: 3.6f),
        // The mending lasts 6 s from its active phase: its close, high and walking views all fall inside it (the owner's cut-off report
        // was at close range and at a steep pitch).
        new("mending_thread", 2.6, (s, t) => { s.Hold(); if (s.Once(t, 0.3)) s.Cast("spell.vital.mending_thread"); }, CameraTurn: 150, Pitch: -0.18f, Distance: 3.6f),
        new("mending_close", 1.8, (s, _) => s.Hold(), CameraTurn: 170, Pitch: -0.05f, Distance: 1.8f),
        new("mending_high", 1.8, (s, _) => s.Hold(), CameraTurn: 150, Pitch: -0.75f, Distance: 3.8f),
        new("walk_mending", 2.5, (s, _) => s.Go(Gait.Walk), CameraTurn: 30),
        new("end", 2.0, (s, _) => s.Hold()),
    };

    private const string Sel = "npc.ashen_hollow.sel_arien";
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Vest = "item.armor.hide_vest";

    // Phase B demo: a conversation from a new game - to Sel at her survey table, her greeting, two replies, goodbye - for the face
    // (blinks, the line spoken as visemes) and the conversation framing, with the player's own commands.
    private static List<Step> Dialogue() => new()
    {
        new("settle", 1.5, (s, _) => s.Hold()),
        new("to_sel", 24.0, (s, _) => s.Approach(Sel, Gait.Run)),
        new("greeting", 8.0, (s, t) => { s.Approach(Sel, Gait.Walk); if (s.Once(t, 0.2)) s._controller.Talk(Sel); }),
        new("reply_1", 8.0, (s, t) => { s.Hold(); if (s.Once(t, 0.2)) s.Choose(0); }),
        new("reply_2", 8.0, (s, t) => { s.Hold(); if (s.Once(t, 0.2)) s.Choose(0); }),
        new("goodbye", 3.0, (s, t) => { s.Hold(); if (s.Once(t, 0.2)) s.Leave(); }),
    };

    // Phase B demo (from a save that has earned the coin - the acceptance playthrough's own): into the smithy, the hide vest bought from
    // Kera's wares and put on, the character shown before and after, and the vest taken off again - the torso-item swap through the
    // game's own trade and equipment commands.
    private static List<Step> Equipment() => new()
    {
        new("settle", 1.5, (s, _) => s.Hold()),
        new("to_smithy", 14.0, (s, t) => { if (s.Walk(ToTheSmithy, Gait.Run)) s.OpenForgeDoor(); }),
        new("to_kera", 6.0, (s, _) => s.Approach(Kera, Gait.Walk)),
        new("trade", 7.0, (s, t) => { s.Hold(); if (s.Once(t, 0.2)) s._controller.Talk(Kera); if (s.Once(t, 1.5)) s.Choose("trade"); if (s.Every(t, 0.5) && t > 2.0 && t < 4.0) s.SellForVest(); if (s.Once(t, 4.2)) s.BuyVest(); if (s.Once(t, 5.5)) s.Leave(); }),
        new("out", 8.0, (s, _) => s.Walk(OutOfTheSmithy, Gait.Walk)),
        new("before", 4.0, (s, _) => s.Hold(), CameraTurn: 160, Pitch: -0.12f, Distance: 3.0f),
        new("put_on", 3.0, (s, t) => { s.Hold(); if (s.Once(t, 0.3)) s.EquipVest(); }, CameraTurn: 160, Pitch: -0.12f, Distance: 3.0f),
        new("after_front", 4.0, (s, _) => s.Hold(), CameraTurn: 180, Pitch: -0.12f, Distance: 2.6f),
        new("after_side", 4.0, (s, _) => s.Hold(), CameraTurn: 90, Pitch: -0.12f, Distance: 2.6f),
        new("walk_in_vest", 5.0, (s, _) => s.Walk(AwayFromTheSmithy, Gait.Walk), CameraTurn: 30),
        new("take_off", 3.0, (s, t) => { s.Hold(); if (s.Once(t, 0.3)) s._session.Submit(new UNNAMED.World.Runtime.UnequipCommand(s._session.Simulation!.PlayerId, UNNAMED.Domain.Items.EquipSlot.Chest)); }, CameraTurn: 160, Pitch: -0.12f, Distance: 3.0f),
        new("end", 2.0, (s, _) => s.Hold(), CameraTurn: 160, Pitch: -0.12f, Distance: 3.0f),
    };

    // Phase B demo (from the acceptance playthrough's own save, which ends home with Tavar following): back out along the way he came
    // home, walking, running, sprinting and stopping with the camera turned back on him - the companion's gaits on his own body.
    private static List<Step> Companion() => new()
    {
        new("settle", 1.5, (s, _) => s.Hold(), CameraTurn: 160, Pitch: -0.15f, Distance: 4.5f),
        new("walk", 6.0, (s, _) => s.Walk(OutWithTavar, Gait.Walk), CameraTurn: 160, Pitch: -0.15f, Distance: 4.5f),
        new("run", 6.0, (s, _) => s.Walk(OutWithTavar, Gait.Run), CameraTurn: 160, Pitch: -0.15f, Distance: 4.5f),
        new("sprint", 4.0, (s, _) => s.Walk(OutWithTavar, Gait.Sprint), CameraTurn: 160, Pitch: -0.15f, Distance: 5.0f),
        new("stop", 4.0, (s, _) => s.Hold(), CameraTurn: 160, Pitch: -0.15f, Distance: 4.5f),
        new("run_side", 5.0, (s, _) => s.Walk(OutWithTavar, Gait.Run), CameraTurn: 90, Pitch: -0.12f, Distance: 4.5f),
        new("stop_end", 3.0, (s, _) => s.Hold(), CameraTurn: 90, Pitch: -0.12f, Distance: 4.5f),
    };

    private static readonly (double X, double Z)[] OutWithTavar = { (62, 130), (80, 118), (100, 100), (135, 70), (150, 45) };
    private static readonly (double X, double Z)[] ToTheSmithy = { (51.8, 136), (51.8, 142) };
    private static readonly (double X, double Z)[] OutOfTheSmithy = { (54.4, 142), (51.8, 142), (49.5, 145.5), (46.5, 149.2) };
    private static readonly (double X, double Z)[] AwayFromTheSmithy = { (41.5, 153.4), (35, 156.2) };

    private bool _askedDoor;
    private (double X, double Z)[]? _walking;
    private int _walkPoint;

    /// <summary>Along a route at a gait, the camera behind the way; true once at its end (then in place).</summary>
    private bool Walk((double X, double Z)[] route, Gait gait)
    {
        if (!ReferenceEquals(route, _walking))
        {
            _walking = route;
            _walkPoint = 0;
        }
        var body = _controller.Authoritative;
        while (_walkPoint < route.Length)
        {
            var to = new Vector3((float)(route[_walkPoint].X - body.XMm / 1000.0), 0, (float)(route[_walkPoint].Z - body.ZMm / 1000.0));
            if (to.Length() > 0.5f)
            {
                _heading = to.Normalized();
                _controller.SteerWorld(_heading, gait, _camera);
                return false;
            }
            _walkPoint++;
        }
        Hold();
        return true;
    }

    /// <summary>To 1.3 m from an NPC, then facing them.</summary>
    private void Approach(string npcId, Gait gait)
    {
        var npc = _session.Simulation!.Npcs.Single(n => n.Id == npcId);
        var body = _controller.Authoritative;
        var at = new Vector3(npc.Body.XMm / 1000f, 0, npc.Body.ZMm / 1000f);
        var me = new Vector3(body.XMm / 1000f, 0, body.ZMm / 1000f);
        var stand = at + (me - at).Normalized() * 1.3f;
        var to = stand - me;
        if (to.Length() > 0.4f)
        {
            _heading = to.Normalized();
            _controller.SteerWorld(_heading, gait, _camera);
            return;
        }
        _heading = (at - me).Normalized();
        Hold();
    }

    private void Choose(int index)
    {
        if (_session.Simulation!.Conversation is { } talk && index < talk.Replies.Length)
            _session.Submit(new UNNAMED.World.Runtime.ChooseCommand(_session.Simulation.PlayerId, talk.Replies[index].Id));
    }

    private void Choose(string replyId)
    {
        if (_session.Simulation!.Conversation is { } talk && talk.Replies.Any(r => r.Id == replyId))
            _session.Submit(new UNNAMED.World.Runtime.ChooseCommand(_session.Simulation.PlayerId, replyId));
    }

    private void Leave() => _session.Submit(new UNNAMED.World.Runtime.LeaveCommand(_session.Simulation!.PlayerId));

    private void OpenForgeDoor()
    {
        if (!_controller.IsOpen("door.forge_shed") && !_askedDoor)
        {
            _controller.Interact("door.forge_shed");
            _askedDoor = true;
        }
    }

    /// <summary>Short of the vest's price: sell Kera one stack of what is carried and not worn (loot and materials), as a player would.</summary>
    private void SellForVest()
    {
        var simulation = _session.Simulation!;
        var vest = simulation.Wares(Kera)?.Wares.FirstOrDefault(w => w.ItemId == Vest);
        if (vest is null || simulation.Player.Currency >= vest.Price)
            return;
        var worn = simulation.Player.Equipment.Values.ToHashSet();
        if (simulation.Player.Inventory.FirstOrDefault(e => !worn.Contains(e.ItemId) && !e.DefId.StartsWith("item.weapon", StringComparison.Ordinal)
                && !e.DefId.StartsWith("item.ammo", StringComparison.Ordinal) && !e.DefId.StartsWith("item.book", StringComparison.Ordinal)) is { } goods)
            _session.Submit(new UNNAMED.World.Runtime.SellCommand(simulation.PlayerId, Kera, goods.ItemId, goods.Count));
    }

    private void BuyVest()
    {
        var simulation = _session.Simulation!;
        if (simulation.Wares(Kera)?.Wares.FirstOrDefault(w => w.ItemId == Vest) is { } vest && vest.Price <= simulation.Player.Currency)
            _session.Submit(new UNNAMED.World.Runtime.BuyCommand(simulation.PlayerId, Kera, vest.Ref, 1));
        else
            GD.PushWarning("UNNAMED showcase: the hide vest is not on sale or the coin does not reach its price");
    }

    private void EquipVest()
    {
        var simulation = _session.Simulation!;
        if (simulation.Player.Inventory.FirstOrDefault(e => e.DefId == Vest) is { } vest)
            _session.Submit(new UNNAMED.World.Runtime.EquipCommand(simulation.PlayerId, vest.ItemId));
        else
            GD.PushWarning("UNNAMED showcase: no hide vest to put on");
    }

    /// <summary>True on the first frame at or past <paramref name="at"/> seconds into the step.</summary>
    private bool Once(double t, double at) => _lastT < at && t >= at;

    /// <summary>True on the step's first frame and then every <paramref name="period"/> seconds.</summary>
    private bool Every(double t, double period) => Math.Floor(t / period) > Math.Floor(_lastT / period);

    private double _lastT = -1;

    private void Hold() => _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);

    private int _waypoint;

    /// <summary>Move along the route at a gait (the heading turns at each waypoint reached); in place at its end.</summary>
    private void Go(Gait gait)
    {
        var body = _controller.Authoritative;
        while (_waypoint < Route.Length)
        {
            var to = new Vector3((float)(Route[_waypoint].X - body.XMm / 1000.0), 0, (float)(Route[_waypoint].Z - body.ZMm / 1000.0));
            if (to.Length() > 1.5f)
            {
                _heading = to.Normalized();
                _controller.SteerWorld(_heading, gait, _camera);
                return;
            }
            _waypoint++;
        }
        Hold();
    }

    private void Cast(string formula)
    {
        if (!_controller.Formulas().Contains(formula))
            GD.PushWarning($"UNNAMED showcase: the character does not know {formula}");
        else
            _controller.Cast(formula);
    }

    /// <summary>This frame: null to keep going, a picture's name, "done", or "failed".</summary>
    public string? Update(double delta)
    {
        if (_index < 0)
        {
            // The camera starts behind the way to the first waypoint.
            var body = _controller.Authoritative;
            _heading = new Vector3((float)(Route[0].X - body.XMm / 1000.0), 0, (float)(Route[0].Z - body.ZMm / 1000.0)).Normalized();
            _index = 0;
            _stepStart = 0;
        }
        _clock += delta;
        var step = _steps[_index];
        double t = _clock - _stepStart;
        step.Act(this, t);
        _lastT = t;
        // The camera: behind the heading, turned by the step's angle (90 = beside, 180 = in front).
        float yaw = Mathf.Atan2(-_heading.X, -_heading.Z) + Mathf.DegToRad(step.CameraTurn);
        _camera.Yaw = Mathf.LerpAngle(_camera.Yaw, yaw, (float)Math.Min(1, delta * 3));
        _camera.Pitch = Mathf.Lerp(_camera.Pitch, step.Pitch, (float)Math.Min(1, delta * 3));
        _camera.TargetDistance = step.Distance;
        string? result = null;
        if (!_shot && t >= step.Seconds / 2)
        {
            _shot = true;
            result = $"{_index + 1:00}_{step.Name}";
        }
        if (t >= step.Seconds)
        {
            _index++;
            _stepStart = _clock;
            _shot = false;
            _lastT = -1;
            if (_index >= _steps.Count)
            {
                GD.Print($"UNNAMED showcase: {_steps.Count} steps in {_clock:0.0} s, written to {Directory}");
                return "done";
            }
        }
        return result;
    }
}
