// UNNAMED Presentation - what the extended performance route does between its traversals (the Phase-1 technical audit, P-01, P-02, P-07)
// Godot presentation only

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Magic;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Perf;

/// <summary>
/// The play the gate's route avoids, done through the real command path so its cost is measured: a conversation with Sel and her
/// primer read, each formula worked, the Charwood hound fought to its death, and its body searched with the inventory open and Take
/// All. Each is a goal the route's segment runs until it is met, or its time runs out (then the capture says so).
/// </summary>
public sealed class PerfActivities
{
    private const string Sel = "npc.ashen_hollow.sel_arien";

    // From the Ashen Waystone past the lodge's south side to Sel's table; from there by the east road to Charwood's hound (the acceptance
    // run's way).
    private static readonly (double X, double Z)[] ToSel = { (44, 138), (54.5, 134), (62, 128) };
    private static readonly (double X, double Z)[] ToTheHound = { (68, 128), (65, 136), (88, 145), (108, 148), (128, 150) };

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly DialoguePanel _dialogue;
    private readonly InventoryPanel _inventory;
    private readonly BuildMode _build;
    private readonly List<string> _notes = new();
    private int _row;
    private int _cycles;
    private int _waypoint;
    private int _phase;
    private double _clock;
    private double _since;
    private int _killed;
    private string? _corpse;

    public PerfActivities(GameSession session, PlayerController controller, CameraRig camera, DialoguePanel dialogue, InventoryPanel inventory,
        BuildMode build, Action<string> mark)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        _dialogue = dialogue;
        _inventory = inventory;
        _build = build;
        // The building segment's moments, each set against the segment's median frame (M7 design §14, R17).
        session.Subscribe<PiecePlaced>(e => mark($"piece placed ({e.DefId})"));
        session.Subscribe<NavigationRebuilt>(e => mark($"navigation rebuilt ({e.NodesRestamped} nodes)"));
        session.Subscribe<RoutePlanned>(e => mark($"route planned ({e.MoverKey})"));
        session.Subscribe<CreatureKilled>(e =>
        {
            if (e.Killer == session.Simulation!.PlayerId)
            {
                _killed++;
                _corpse = session.Simulation!.Creatures.FirstOrDefault(c => c.Id == e.Creature)?.CorpseKey;
            }
        });
    }

    /// <summary>What each goal did, for the capture's notes.</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>A goal starts afresh: its own waypoints and steps.</summary>
    public void Begin()
    {
        _waypoint = 0;
        _phase = 0;
        _since = _clock;
    }

    /// <summary>To Sel, her conversation on screen - the primer asked for and taken - and read: three formulas known.</summary>
    public bool GetThePrimer(double delta)
    {
        _clock += delta;
        var simulation = _session.Simulation!;
        switch (_phase)
        {
            case 0:
                if (Walk(ToSel) && Approach(Sel))
                {
                    _controller.Talk(Sel);
                    Next();
                }
                return false;
            case 1 or 2 or 3:
                // A reply a second, so the panel is on screen long enough to be measured.
                if (_clock - _since < 1)
                    return false;
                string reply = new[] { "books", "take", "back" }[_phase - 1];
                if (simulation.Conversation?.Replies.Any(r => r.Id == reply) == true)
                    _dialogue.Answer(reply);
                Next();
                return false;
            case 4:
                if (_clock - _since < 1)
                    return false;
                if (simulation.Conversation is not null)
                    _dialogue.Leave();
                if (simulation.Player.Inventory.FirstOrDefault(e => _session.Setup.Magic.Teaches.ContainsKey(e.DefId)) is { } book)
                    _session.Submit(new UseItemCommand(simulation.PlayerId, book.ItemId));
                Next();
                return false;
            default:
                if (_controller.Formulas().Length == 0)
                    return false;
                _notes.Add($"conversation: the primer taken and read, {_controller.Formulas().Length} formulas known");
                return true;
        }
    }

    /// <summary>Each formula known worked once, one after another as the body comes free: the tell, the release and its effect.</summary>
    public bool WorkFormulas(double delta)
    {
        _clock += delta;
        var simulation = _session.Simulation!;
        var formulas = _controller.Formulas();
        if (_phase >= formulas.Length)
        {
            _notes.Add($"magic: {formulas.Length} formulas worked");
            return true;
        }
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
        if (simulation.Combat.Phase == CombatPhase.Idle && _clock - _since > 1.5)
        {
            _camera.Yaw += 1.2f;   // a bolt thrown along a new line each time
            _controller.Cast(formulas[_phase]);
            Next();
        }
        return false;
    }

    /// <summary>East to Charwood and the hound, fought until it dies - mended between blows when hurt.</summary>
    public bool FightTheHound(double delta)
    {
        _clock += delta;
        if (_killed > 0 && _corpse is not null)
        {
            _notes.Add($"combat: {_killed} killed; its body is {_corpse}");
            return true;
        }
        if (!Defend() && !Mend())
            Walk(ToTheHound);
        return false;
    }

    // The ruined merchant cart, a little north-east of the hound's ground: a container that always holds something (the bow, arrows).
    private const string Cart = "container.merchant_cart";
    private static readonly (double X, double Z)[] ToTheCart = { (145, 156), (157, 159.3) };

    /// <summary>
    /// The body searched, then the merchant cart: the inventory open on each, everything taken with Take All, the panel closed again. The
    /// hound's loot is rolled and may be nothing; the cart's is not.
    /// </summary>
    public bool SearchTheCorpse(double delta)
    {
        _clock += delta;
        var simulation = _session.Simulation!;
        switch (_phase)
        {
            case 0:
                if (_corpse is null || simulation.Creatures.FirstOrDefault(c => c.CorpseKey == _corpse) is not { } body)
                {
                    _notes.Add("loot: no body to search");
                    _phase = 3;
                    _waypoint = 0;
                    return false;
                }
                if (Walk(new[] { (body.Body.XMm / 1000.0, body.Body.ZMm / 1000.0 - 1.2) }))
                    Open(_corpse);
                return false;
            case 1:
                return TakeAll(_corpse!, "the body");
            case 2:
                if (_clock - _since < 2)
                    return false;
                _inventory.Close();
                _waypoint = 0;
                Next();
                return false;
            case 3:
                if (Walk(ToTheCart))
                    Open(Cart);
                return false;
            case 4:
                return TakeAll(Cart, "the merchant cart");
            default:
                if (_clock - _since < 2)
                    return false;
                _inventory.Close();
                return true;
        }
    }

    private void Open(string container)
    {
        _inventory.Open(container);
        Next();
    }

    private bool TakeAll(string container, string what)
    {
        if (_clock - _since < 1.5)
            return false;
        int stacks = _session.Simulation!.Containers.FirstOrDefault(c => c.Site.Key == container)?.Items.Length ?? 0;
        _inventory.TakeAll();
        _notes.Add($"loot: {what} searched with the inventory open, Take All on {stacks} stacks");
        Next();
        return false;
    }

    // From the end of the first-person traversal, by the lodge's east side, to the timber stack by the crossing; then into the square of the
    // Crossing Workshop, where its first step is built (M7 design §4.22, rows R01-R05).
    private static readonly (double X, double Z)[] ToTheTimber = { (60, 156), (65, 136), (80, 120), (84, 116.6) };
    private static readonly (double X, double Z)[] IntoTheWorkshop = { (95, 110), (102, 102) };
    private const string Stack = "container.timber_stack";

    private static readonly (string Def, long X, long Z, int R)[] StepOne =
    {
        ("piece.pad.timber", 100_500, 100_500, 0), ("piece.pad.timber", 103_500, 100_500, 0), ("piece.pad.timber", 100_500, 103_500, 0),
        ("piece.pad.timber", 103_500, 103_500, 0), ("piece.doorway.timber", 100_500, 99_000, 0), ("piece.wall.timber", 103_500, 99_000, 0),
        ("piece.wall.timber", 100_500, 105_000, 0), ("piece.wall.timber", 103_500, 105_000, 0), ("piece.wall.timber", 99_000, 100_500, 1),
        ("piece.wall.timber", 99_000, 103_500, 1), ("piece.wall.timber", 105_000, 100_500, 1), ("piece.wall.timber", 105_000, 103_500, 1),
        ("piece.roof.timber", 100_500, 100_500, 0), ("piece.roof.timber", 103_500, 100_500, 0), ("piece.roof.timber", 100_500, 103_500, 0),
        ("piece.roof.timber", 103_500, 103_500, 0),
    };

    /// <summary>
    /// The building segment (M7 design §14, R17): timber taken at the stack; build mode entered in the workshop's square and its first step
    /// placed, a row every half second, the ghost following each; the ghost swept over the area for 10 s; one wall taken down and put back
    /// twice; and one synchronous save with the pieces standing. The session's events mark each placement and rebuild.
    /// </summary>
    public bool BuildAtTheCrossing(double delta)
    {
        _clock += delta;
        var simulation = _session.Simulation!;
        switch (_phase)
        {
            case 0:
                if (Walk(ToTheTimber))
                    Open(Stack);
                return false;
            case 1:
                return TakeAll(Stack, "the timber stack");
            case 2:
                if (_clock - _since < 1.5)
                    return false;
                _inventory.Close();
                _waypoint = 0;
                Next();
                return false;
            case 3:
                if (!Walk(IntoTheWorkshop))
                    return false;
                if (!_build.Enter(_camera))
                {
                    _notes.Add("building: nothing to build with");
                    return true;
                }
                _row = 0;
                Next();
                return false;
            case 4:
            {
                if (_clock - _since < 0.5)
                    return false;
                if (_row >= StepOne.Length)
                {
                    _notes.Add($"building: {simulation.Pieces.Length} pieces standing after the first step");
                    Next();
                    return false;
                }
                var (def, x, z, r) = StepOne[_row++];
                _build.Select(_build.Pieces.ToList().FindIndex(p => p.Id == def));
                _build.ScriptedAim = (x, z);
                _controller.Place(def, x, z, r);
                _since = _clock;
                return false;
            }
            case 5:
            {
                double t = _clock - _since;
                if (t >= 10)
                {
                    _build.ScriptedAim = null;
                    _cycles = 0;
                    Next();
                    return false;
                }
                _build.ScriptedAim = (94_000 + (long)(12_000 * Math.Abs(Math.Sin(t * 0.7))), 94_000 + (long)(12_000 * Math.Abs(Math.Cos(t * 0.5))));
                _camera.Yaw += (float)delta * 0.4f;
                return false;
            }
            case 6:
            {
                // The north-east wall down and up again, twice.
                if (_clock - _since < 0.5)
                    return false;
                var wall = simulation.Pieces.FirstOrDefault(p => p.DefId == "piece.wall.timber" && p.XMm == 103_500 && p.ZMm == 105_000);
                if (wall is not null)
                    _controller.Dismantle(wall.Id);
                else
                    _controller.Place("piece.wall.timber", 103_500, 105_000, 0);
                _since = _clock;
                if (++_cycles >= 4)
                    Next();
                return false;
            }
            default:
            {
                if (_clock - _since < 0.5)
                    return false;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                _session.Save(SaveSlots.Manual("perf_building"));
                _notes.Add($"building: {simulation.Pieces.Length} pieces standing, saved in {clock.Elapsed.TotalMilliseconds:0.0} ms");
                _build.Exit(_camera);
                return true;
            }
        }
    }

    private void Next()
    {
        _phase++;
        _since = _clock;
    }

    /// <summary>Along a route; true at its end.</summary>
    private bool Walk((double X, double Z)[] route)
    {
        if (_waypoint >= route.Length)
        {
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
            return true;
        }
        var body = _controller.Authoritative;
        var (x, z) = route[_waypoint];
        var to = new Vector3((float)(x - body.XMm / 1000.0), 0, (float)(z - body.ZMm / 1000.0));
        if (to.Length() < 0.5f)
            _waypoint++;
        else
            _controller.SteerWorld(to.Normalized(), Gait.Run, _camera);
        _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
        return false;
    }

    /// <summary>To within a hand's reach of an NPC, facing them; true once there.</summary>
    private bool Approach(string npcId)
    {
        var npc = _session.Simulation!.Npcs.Single(n => n.Id == npcId).Body;
        var body = _controller.Authoritative;
        var away = new Vector3(body.XMm - npc.XMm, 0, body.ZMm - npc.ZMm).Normalized() * 1.3f;
        var spot = new Vector3((float)(npc.XMm / 1000.0 + away.X - body.XMm / 1000.0), 0, (float)(npc.ZMm / 1000.0 + away.Z - body.ZMm / 1000.0));
        if (spot.Length() < 0.3f)
        {
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
            _camera.Yaw = Mathf.Atan2(-(npc.XMm - body.XMm) / 1000f, -(npc.ZMm - body.ZMm) / 1000f);
            return true;
        }
        _controller.SteerWorld(spot.Normalized(), Gait.Run, _camera);
        return false;
    }

    /// <summary>Fight the nearest creature hunting the character (a sentinel or a stray is walked past); true while fighting.</summary>
    private bool Defend()
    {
        var simulation = _session.Simulation!;
        var body = _controller.Authoritative;
        double Distance(Body b) => Math.Sqrt(Math.Pow(b.XMm - body.XMm, 2) + Math.Pow(b.ZMm - body.ZMm, 2));
        var threat = simulation.Creatures.Where(c => c.Alive && c.Hostile && c.RoleId is not ("sentinel" or "stray") && Distance(c.Body) < 25_000)
            .OrderBy(c => Distance(c.Body)).FirstOrDefault();
        if (threat is null)
            return false;
        var to = new Vector3((float)((threat.Body.XMm - body.XMm) / 1000.0), 0, (float)((threat.Body.ZMm - body.ZMm) / 1000.0));
        _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
        var combat = simulation.Combat;
        bool inReach = to.Length() <= (combat.Weapon.ReachMm + _session.Setup.Combat.Creatures[threat.DefId].RadiusMm - 150) / 1000f;
        _controller.SteerWorld(inReach ? Vector3.Zero : to.Normalized(), Gait.Run, _camera, faceCamera: true);
        if (inReach && combat.Phase == CombatPhase.Idle)
            _controller.Attack();
        return true;
    }

    private double _mended = -100;

    /// <summary>Below half health and out of a fight, mend: the last self formula known, or a salve. True while it is worked.</summary>
    private bool Mend()
    {
        var simulation = _session.Simulation!;
        var magic = _session.Setup.Magic;
        var combat = simulation.Combat;
        if (_clock - _mended < 2)
            return true;
        if (combat.Health * 2 >= combat.MaxHealth || combat.Phase != CombatPhase.Idle || _clock - _mended < 15)
            return false;
        string? mend = _controller.Formulas().LastOrDefault(f => magic.Formulas[f].Targeting == Targeting.Self);
        if (mend is not null && combat.Focus >= magic.Formulas[mend].FocusCost)
            _controller.Cast(mend);
        else if (!_controller.UseConsumable())
            return false;
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
        _mended = _clock;
        return true;
    }
}
