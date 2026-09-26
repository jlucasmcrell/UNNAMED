// UNNAMED Presentation - scripted gameplay scenes for review video (Phase B remediation)
// Godot presentation only: every action goes through the player's own command path; nothing here changes a rule

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>--showcase &lt;dir&gt; --showcase-scene locomotion|sword|spells|formulas|rescue</c> (with <c>--profile</c> for a scene that needs a
/// later save): a fixed script of the player's own commands in real time - idle, walk, run, stop, turn, sprint, jump; the sword's ready
/// stance, attacks in a row, block and back to running; each formula cast; each formula through to its end and the Impulse Bolt thrown at
/// a creature (<c>formulas</c>); the Foldscar's three Quiet Stones turned, the heart steadied, Tavar spoken to and following (<c>rescue</c>,
/// from a new game) - with the camera held behind, beside or ahead of the character at gameplay distances. Recorded by Godot's Movie
/// Maker (<c>--write-movie out.avi --fixed-fps 60</c> before <c>--</c>) it is a review clip; a picture is taken at each timed step's
/// middle. Exit code 0 when the script ran to its end, 1 when a step that waits for the world did not get there in time.
/// </summary>
public sealed class Showcase
{
    /// <summary>
    /// A step: what the character does each frame, and where the camera sits (turned from behind the heading, pitch, distance). A timed
    /// step lasts <see cref="Seconds"/>; one with <see cref="Until"/> lasts until the world gets there, and fails past <see cref="Seconds"/>.
    /// </summary>
    private sealed record Step(string Name, double Seconds, Action<Showcase, double> Act, float CameraTurn = 0, float Pitch = -0.25f, float Distance = 3.5f,
        Func<Showcase, bool>? Until = null);

    private const string Sel = "npc.ashen_hollow.sel_arien";
    private const string Tavar = "npc.ashen_hollow.tavar_orr";

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly DialoguePanel _dialogue;
    private readonly List<Step> _steps;
    private readonly (double X, double Z)? _firstLook;
    private int _index = -1;
    private double _clock;
    private double _stepStart;
    private bool _shot;
    private Vector3 _heading;

    public string Directory { get; }

    public Showcase(GameSession session, PlayerController controller, CameraRig camera, DialoguePanel dialogue, string directory, string scene)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        _dialogue = dialogue;
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        (_steps, _firstLook) = scene switch
        {
            "locomotion" => (Locomotion(), Route[0]),
            "sword" => (Sword(), Route[0]),
            "spells" => (Spells(), Route[0]),
            "formulas" => (Formulas(), Route[0]),
            "rescue" => (Rescue(), ToSel[0]),
            _ => throw new ArgumentException($"--showcase-scene: '{scene}' is not locomotion, sword, spells, formulas or rescue"),
        };
        // What the world did, with the clip's time, for cutting the recording and for the evidence (what was hit, set, joined).
        string Name(string id) => session.DisplayName(id);
        session.Subscribe<ShotLoosed>(e => Log($"shot {e.Source} {(e.Target is null ? "struck nothing" : "struck a creature")}"));
        session.Subscribe<HitResolved>(e => Log($"hit {e.Source} for {e.Damage}{(e.Dodged ? ", dodged" : "")}{(e.Target == session.Simulation!.PlayerId ? " on the character" : "")}"));
        session.Subscribe<CastCompleted>(e => Log($"worked {e.FormulaId}"));
        session.Subscribe<CastFizzled>(e => Log($"fizzled {e.FormulaId}"));
        session.Subscribe<EffectApplied>(e => Log($"effect {e.EffectId} on"));
        session.Subscribe<EffectExpired>(e => Log($"effect {e.EffectId} off"));
        session.Subscribe<SwitchSet>(e => Log($"switch {e.SwitchKey} set"));
        session.Subscribe<WorldFlagChanged>(e => Log($"flag {e.FlagId} {e.From} -> {e.To}"));
        session.Subscribe<ObjectiveSatisfied>(e => Log($"objective {e.ObjectiveId} of {Name(e.QuestId)}"));
        session.Subscribe<QuestCompleted>(e => Log($"quest complete: {Name(e.QuestId)}"));
        session.Subscribe<CompanionRecruited>(e => Log($"{Name(e.NpcId)} joins"));
        session.Subscribe<CreatureKilled>(e => Log($"killed {Name(e.DefId)}"));
    }

    private void LogCreatures()
    {
        var body = _controller.Authoritative;
        foreach (var c in _session.Simulation!.Creatures.OrderBy(c => Metres(body, c.Body)).Take(4))
            Log($"creature {c.DefId} {(c.Alive ? "alive" : "dead")} {c.RoleId} at {Metres(body, c.Body):0.0} m");
    }

    private void Log(string what) => GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"UNNAMED showcase event: {_clock:0.00} s {what}"));

    // Open ground at the fixed seed: the acceptance playthrough's own way out to the boar's wallow (UiShots.ToTheWallow), then on
    // down Blackvein's rim - no wall, stone or building in the way, with a right-angle turn at the first waypoint.
    private static readonly (double X, double Z)[] Route = { (60, 118), (40, 104), (36, 92), (25, 78), (22, 62) };

    // The formulas scene: from home out towards the Bristleback Boar's wallow (18, 72), stopping within the Impulse Bolt's reach.
    private static readonly (double X, double Z)[] ToTheWallow = { (60, 118), (40, 104), (28, 84) };
    private const double WallowX = 18, WallowZ = 72;

    // The rescue: from the spawn to Sel, then the acceptance playthrough's own ways round the Foldscar (Playthrough.cs, content bible §8).
    private static readonly (double X, double Z)[] ToSel = { (34, 150), (40, 140), (51.8, 136), (62, 127) };
    private static readonly (double X, double Z)[] ToTheFoldscar = { (90, 112), (105, 108), (120, 93), (140, 80) };
    private static readonly (double X, double Z)[] ToTheNorthStone = { (148.5, 78) };
    private static readonly (double X, double Z)[] ToTheSouthWestStone = { (135, 60), (123.5, 38) };
    private static readonly (double X, double Z)[] ToTheSouthEastStone = { (140, 62), (160, 65), (187, 65), (194, 45), (182.5, 31) };
    private static readonly (double X, double Z)[] ToTheHeart = { (194, 45), (187, 65), (160, 65), (153, 50.2) };
    private static readonly (double X, double Z)[] AwayWithTavar = { (150, 47), (144, 58), (138, 68) };

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

    /// <summary>
    /// Each formula through to its end at gameplay distances, then the Impulse Bolt at a creature: the Brace Ward from its working to its
    /// expiry (10 s), the Mending Thread's 6 s, the walk out to the boar's wallow, a bolt seen from beside and one from behind, and the
    /// ward worked again as the boar comes on, so its blow lands on it. From the acceptance save (the three formulas known); the settle
    /// lets the save's Strain ebb first.
    /// </summary>
    private static List<Step> Formulas() => new()
    {
        new("settle", 15.0, (s, _) => s.Hold(), CameraTurn: 150, Pitch: -0.18f, Distance: 4.0f),
        new("ward_cast", 4.0, (s, t) => { s.Hold(); if (s.Once(t, 0.3)) s.Cast("spell.warding.brace_ward"); }, CameraTurn: 150, Pitch: -0.2f, Distance: 4.0f),
        new("ward_side", 3.0, (s, _) => s.Hold(), CameraTurn: 95, Pitch: -0.15f, Distance: 4.8f),
        new("ward_close", 2.5, (s, _) => s.Hold(), CameraTurn: 165, Pitch: -0.08f, Distance: 2.0f),
        new("ward_expiry", 3.5, (s, _) => s.Hold(), CameraTurn: 150, Pitch: -0.2f, Distance: 4.0f),
        new("mending_cast", 3.0, (s, t) => { s.Hold(); if (s.Once(t, 0.3)) s.Cast("spell.vital.mending_thread"); }, CameraTurn: 150, Pitch: -0.2f, Distance: 4.5f),
        new("mending_far", 2.5, (s, _) => s.Hold(), CameraTurn: 120, Pitch: -0.18f, Distance: 6.0f),
        new("mending_close", 1.8, (s, _) => s.Hold(), CameraTurn: 170, Pitch: -0.05f, Distance: 1.8f),
        new("mending_end", 2.5, (s, _) => s.Hold(), CameraTurn: 150, Pitch: -0.2f, Distance: 4.0f),
        new("to_the_wallow", 40, (s, _) => s.Walk(ToTheWallow), Until: s => s.Arrived),
        new("aim", 2.0, (s, t) => { s.AimAtNearest((WallowX, WallowZ)); if (s.Once(t, 0.1)) s.LogCreatures(); }),
        new("bolt_beside", 3.5, (s, t) => { s.Hold(); if (s.Once(t, 1.4)) s.Cast("spell.force.impulse_bolt"); }, CameraTurn: 70, Pitch: -0.12f, Distance: 5.0f),
        new("bolt_behind", 3.5, (s, t) => { s.AimAtNearest(); if (s.Once(t, 1.1)) s.Cast("spell.force.impulse_bolt"); }, Pitch: -0.18f, Distance: 3.8f),
        new("ward_the_charge", 7.0, (s, _) => s.WardTheCharge(), CameraTurn: 25, Pitch: -0.2f, Distance: 4.4f),
        new("fight", 7.0, (s, t) => s.Fight(t), CameraTurn: 35, Pitch: -0.2f, Distance: 4.2f),
        new("end", 2.5, (s, _) => s.Hold(), CameraTurn: 35, Pitch: -0.2f, Distance: 4.2f),
    };

    /// <summary>
    /// The Foldscar rescue from a new game (content bible §16): Sel tells of Tavar, the three Quiet Stones are turned one by one - each
    /// watched for a few seconds - Tavar is seen held in the fold, the heart is steadied and the fold lets him go, he is spoken to and
    /// asked along, and walks off following. Every step is the player's command: walking, interacting, talking, answering.
    /// </summary>
    private static List<Step> Rescue() => new()
    {
        new("settle", 1.5, (s, _) => s.Hold()),
        new("to_sel", 40, (s, _) => s.Travel(ToSel), Until: s => s.Arrived),
        new("approach_sel", 15, (s, _) => s.Approach(Sel), Until: s => s.Arrived),
        new("sel", 30, (s, t) => s.Converse(Sel, t, 1.2, "ruin", "tavar", "stones", "back"), CameraTurn: 60, Pitch: -0.15f, Distance: 3.2f,
            Until: s => s.Conversed),
        new("to_the_foldscar", 60, (s, _) => s.Travel(ToTheFoldscar), Until: s => s.Arrived),
        new("to_north_stone", 20, (s, _) => s.Travel(ToTheNorthStone), Until: s => s.Arrived),
        new("north_stone", 6.0, (s, t) => s.Work("switch.stone_north", t), CameraTurn: 35, Pitch: -0.18f, Distance: 4.4f),
        new("to_southwest_stone", 40, (s, _) => s.Travel(ToTheSouthWestStone), Until: s => s.Arrived),
        new("southwest_stone", 6.0, (s, t) => s.Work("switch.stone_southwest", t), CameraTurn: -35, Pitch: -0.18f, Distance: 4.4f),
        new("to_southeast_stone", 60, (s, _) => s.Travel(ToTheSouthEastStone), Until: s => s.Arrived),
        new("southeast_stone", 6.0, (s, t) => s.Work("switch.stone_southeast", t), CameraTurn: 35, Pitch: -0.18f, Distance: 4.4f),
        new("to_the_heart", 60, (s, _) => s.Travel(ToTheHeart), Until: s => s.Arrived),
        new("the_fold", 4.5, (s, _) => { s.Hold(); s.Look(145, 42); }, CameraTurn: 20, Pitch: -0.14f, Distance: 4.6f),
        new("steady_the_heart", 8.0, (s, t) => { s.Look(145, 42); if (s.Every(t, 0.5) && !s.IsSet("switch.foldscar_heart")) s._controller.Interact("switch.foldscar_heart"); s.Hold(); },
            CameraTurn: 20, Pitch: -0.14f, Distance: 4.6f),
        new("approach_tavar", 20, (s, _) => s.Approach(Tavar, (149.5, 49.5)), Until: s => s.Arrived),
        new("tavar", 30, (s, t) => s.Converse(Tavar, t, 2.8, "sent", "join"), CameraTurn: 70, Pitch: -0.12f, Distance: 3.4f, Until: s => s.Conversed),
        new("away_with_tavar", 30, (s, _) => s.Walk(AwayWithTavar, Gait.Walk), CameraTurn: 160, Pitch: -0.2f, Distance: 5.0f, Until: s => s.Arrived),
        new("look_back", 4.0, (s, _) => { s.Hold(); s.Look(145, 42); }, CameraTurn: 0, Pitch: -0.18f, Distance: 4.5f),
    };

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

    // The waiting steps' own progress: the route's next point, whether it was reached, and the conversation's replies said.
    private int _leg;
    private bool Arrived { get; set; }
    private bool Conversed { get; set; }
    private int _said;
    private bool _talking;

    /// <summary>Walk a route (the heading follows the way), and stand at its end.</summary>
    private void Walk((double X, double Z)[] route, Gait gait = Gait.Run, float arrive = 0.5f)
    {
        var body = _controller.Authoritative;
        while (_leg < route.Length)
        {
            var to = new Vector3((float)(route[_leg].X - body.XMm / 1000.0), 0, (float)(route[_leg].Z - body.ZMm / 1000.0));
            if (to.Length() >= arrive)
            {
                _heading = to.Normalized();
                _controller.SteerWorld(_heading, gait, _camera);
                return;
            }
            _leg++;
        }
        Hold();
        Arrived = true;
    }

    /// <summary>Walk a route, first fighting off anything that hunts the character (a sentinel or a stray is walked past).</summary>
    private void Travel((double X, double Z)[] route)
    {
        var simulation = _session.Simulation!;
        var body = _controller.Authoritative;
        var threat = simulation.Creatures
            .Where(c => c.Alive && c.Hostile && c.RoleId is not ("sentinel" or "stray") && Metres(body, c.Body) < 10)
            .OrderBy(c => Metres(body, c.Body)).FirstOrDefault();
        if (threat is null)
        {
            Walk(route);
            return;
        }
        Aim(threat.Body.XMm / 1000.0, threat.Body.ZMm / 1000.0);
        if (simulation.Combat.Phase == CombatPhase.Idle && Metres(body, threat.Body) < 2.4)
            _controller.Attack();
    }

    /// <summary>Walk to within a hand's reach of an NPC (by <paramref name="via"/> first, when given) and face them.</summary>
    private void Approach(string npcId, (double X, double Z)? via = null)
    {
        var npc = _session.Simulation!.Npcs.Single(n => n.Id == npcId);
        var body = _controller.Authoritative;
        var from = via is { } v ? new Vector3((float)v.X, 0, (float)v.Z) : new Vector3(body.XMm / 1000f, 0, body.ZMm / 1000f);
        var at = new Vector3(npc.Body.XMm / 1000f, 0, npc.Body.ZMm / 1000f);
        var spot = at + (from - at).Normalized() * 1.3f;
        var route = via is { } w ? new[] { w, (spot.X, spot.Z) } : new[] { ((double)spot.X, (double)spot.Z) };
        Walk(route, arrive: 0.25f);
        if (Arrived)
            Look(at.X, at.Z);
    }

    /// <summary>Talk to an NPC and say these replies in turn, a moment apart so each line can be read; then leave.</summary>
    private void Converse(string npcId, double t, double gap, params string[] replies)
    {
        Hold();
        var npc = _session.Simulation!.Npcs.Single(n => n.Id == npcId);
        Look(npc.Body.XMm / 1000.0, npc.Body.ZMm / 1000.0);
        var conversation = _session.Simulation!.Conversation;
        if (!_talking)
        {
            if (Once(t, 0.2))
                _controller.Talk(npcId);
            if (conversation is not null)
            {
                _talking = true;
                _nextLine = t + gap;
            }
            return;
        }
        if (t < _nextLine)
            return;
        if (_said < replies.Length)
        {
            if (conversation?.Replies.Any(r => r.Id == replies[_said]) == true)
                _dialogue.Answer(replies[_said]);
            else
                GD.PushWarning($"UNNAMED showcase: {npcId} did not offer '{replies[_said]}' at '{conversation?.NodeId ?? "(no conversation)"}'");
            _said++;
            _nextLine = t + gap;
            return;
        }
        if (conversation is not null)
            _dialogue.Leave();
        else
            Conversed = true;
    }

    private double _nextLine;

    /// <summary>Face a switch's structure and work it until it is set, then watch it.</summary>
    private void Work(string key, double t)
    {
        Hold();
        var view = _session.Simulation!.Switches.Single(s => s.Site.Key == key);
        var (x, z) = Footprints.Center(view.Site.Body);
        Look(x / 1000.0, z / 1000.0);
        if (!view.Set && t >= 1.0 && Every(t, 0.5))
            _controller.Interact(key);
    }

    private bool IsSet(string key) => _session.Simulation!.Switches.Single(s => s.Site.Key == key).Set;

    /// <summary>The heading (and so the camera behind it) towards a point.</summary>
    private void Look(double x, double z)
    {
        var body = _controller.Authoritative;
        var to = new Vector3((float)(x - body.XMm / 1000.0), 0, (float)(z - body.ZMm / 1000.0));
        if (to.LengthSquared() > 0.01f)
            _heading = to.Normalized();
    }

    /// <summary>
    /// Turn the body to face a point as the player aims: the camera swings behind it (<see cref="Update"/> eases it round) and the body
    /// faces the camera's forward.
    /// </summary>
    private void Aim(double x, double z)
    {
        Look(x, z);
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera, faceCamera: true);
    }

    /// <summary>Aim at the nearest living creature within 25 m (the boar); else at <paramref name="otherwise"/>, or hold.</summary>
    private void AimAtNearest((double X, double Z)? otherwise = null)
    {
        if (Nearest() is { } creature)
            Aim(creature.Body.XMm / 1000.0, creature.Body.ZMm / 1000.0);
        else if (otherwise is { } point)
            Aim(point.X, point.Z);
        else
            Hold();
    }

    private CreatureView? Nearest()
    {
        var body = _controller.Authoritative;
        return _session.Simulation!.Creatures.Where(c => c.Alive && Metres(body, c.Body) < 25).OrderBy(c => Metres(body, c.Body)).FirstOrDefault();
    }

    private bool _warded;

    /// <summary>Walk at the creature until it is within 6.5 m, then work the Brace Ward and stand facing it, so its blow lands on the ward.</summary>
    private void WardTheCharge()
    {
        if (Nearest() is not { } creature)
        {
            Hold();
            return;
        }
        var body = _controller.Authoritative;
        if (!_warded && Metres(body, creature.Body) > 6.5)
        {
            _heading = new Vector3((float)(creature.Body.XMm - body.XMm), 0, (float)(creature.Body.ZMm - body.ZMm)).Normalized();
            _controller.SteerWorld(_heading, Gait.Walk, _camera);
            return;
        }
        AimAtNearest();
        if (!_warded && _session.Simulation!.Combat.Phase == CombatPhase.Idle)
        {
            _warded = true;
            Cast("spell.warding.brace_ward");
        }
    }

    /// <summary>Close to the spear's reach of the creature and strike, a blow a second.</summary>
    private void Fight(double t)
    {
        if (Nearest() is not { } creature)
        {
            Hold();
            return;
        }
        var body = _controller.Authoritative;
        var combat = _session.Simulation!.Combat;
        long radius = _session.Setup.Combat.Creatures[creature.DefId].RadiusMm;
        if (Metres(body, creature.Body) * 1000 > combat.Weapon.ReachMm + radius - 150)
        {
            _heading = new Vector3((float)(creature.Body.XMm - body.XMm), 0, (float)(creature.Body.ZMm - body.ZMm)).Normalized();
            _controller.SteerWorld(_heading, Gait.Run, _camera);
            return;
        }
        AimAtNearest();
        if (combat.Phase == CombatPhase.Idle && Every(t, 0.25))
            _controller.Attack();
    }

    private static double Metres(Body a, Body b) => Math.Sqrt(Math.Pow(a.XMm - b.XMm, 2) + Math.Pow(a.ZMm - b.ZMm, 2)) / 1000.0;

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
            // The camera starts behind the way to the scene's first point.
            var body = _controller.Authoritative;
            if (_firstLook is { } first)
                _heading = new Vector3((float)(first.X - body.XMm / 1000.0), 0, (float)(first.Z - body.ZMm / 1000.0)).Normalized();
            _index = 0;
            _stepStart = 0;
            _shot = _steps[0].Until is not null;
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
        bool finished = step.Until is { } until ? until(this) : t >= step.Seconds;
        if (!finished && step.Until is not null && t >= step.Seconds)
        {
            GD.PushError($"UNNAMED showcase: step '{step.Name}' did not get there in {step.Seconds:0} s");
            return "failed";
        }
        if (finished)
        {
            GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"UNNAMED showcase: step {_index + 1:00} {step.Name} ended at {_clock:0.00} s"));
            _index++;
            _stepStart = _clock;
            _lastT = -1;
            _leg = 0;
            Arrived = false;
            Conversed = false;
            _said = 0;
            _talking = false;
            if (_index >= _steps.Count)
            {
                GD.Print($"UNNAMED showcase: {_steps.Count} steps in {_clock:0.0} s, written to {Directory}");
                return "done";
            }
            _shot = _steps[_index].Until is not null;
        }
        return result;
    }
}
