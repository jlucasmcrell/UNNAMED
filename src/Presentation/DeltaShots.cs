// UNNAMED Presentation - the owner's M6 playtest delta, shown: a scripted run through the real command path, with a picture of each change
// Godot presentation only: it submits commands and reads views, as a player's keys would (D-11)

using System.Globalization;
using System.Text;
using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Magic;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>--delta-shots dir</c>: a fresh game walked through each change of the owner's M6 playtest delta, a screenshot at each - the compass in
/// third and first person, the controls (F1), Take All at the waystation's chest and the panel closing as the body walks away, a running
/// jump over the fallen timber, the crouch under the Woundmoss beam and the stand refused beneath it, the bow's reticle, an arrow in flight
/// and stuck where it stopped, the Impulse Bolt's reticle, flight and burst, and the character sheet. Fights what hunts it on the way.
/// Writes <c>transcript.md</c> beside the pictures. Scripts may name content; the game may not.
/// </summary>
public sealed class DeltaShots
{
    private const float Near = 0.2f;
    private const string Chest = "container.waystation_chest";
    private const string Cart = "container.merchant_cart";
    private const string Sel = "npc.ashen_hollow.sel_arien";

    private static readonly (double X, double Z)[] ToTheChest = { (34, 150), (38, 138.4) };
    private static readonly (double X, double Z)[] AwayFromTheChest = { (38, 142.5) };
    // Up to the timber's west end (it runs x 163-171), so the camera, off to the west, sees its end and the body over it.
    private static readonly (double X, double Z)[] ToTheTimber = { (40, 142), (47, 139), (51.8, 139), (51.8, 136), (65, 136), (88, 145), (108, 140), (140, 140), (163.6, 141.5) };
    private static readonly (double X, double Z)[] ToTheBeam = { (172.5, 149), (172.5, 140), (155, 128.5), (149, 129.2) };
    private static readonly (double X, double Z)[] UnderTheBeam = { (149, 132.1) };
    private static readonly (double X, double Z)[] PastTheBeam = { (149, 134.4) };
    private static readonly (double X, double Z)[] ToTheCart = { (149, 140), (157, 158.8) };
    private static readonly (double X, double Z)[] ToSel = { (150, 145), (120, 141), (95, 130), (80, 124) };
    private static readonly (double X, double Z)[] ToTheLodgeWall = { (60, 126) };

    private sealed record Beat(string Name, string Says, int Budget, Func<bool> Run);

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly InventoryPanel _inventory;
    private readonly DialoguePanel _dialogue;
    private readonly CharacterPanel _character;
    private readonly HelpPanel _help;
    private readonly ProjectilesView _projectiles;
    private readonly List<Beat> _beats = new();
    private readonly StringBuilder _transcript = new();
    private readonly List<ShotLoosed> _shots = new();
    private readonly List<string> _refused = new();
    private int _beat;
    private long _beatTick;
    private int _waypoint;
    private int _phase;
    private int _frame;
    private string? _broken;
    private bool _wasAirborne;
    private readonly string _bolt;
    private int _fizzles;

    public DeltaShots(GameSession session, PlayerController controller, CameraRig camera, InventoryPanel inventory, DialoguePanel dialogue,
        CharacterPanel character, HelpPanel help, ProjectilesView projectiles, string directory, string? assetRoot)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        _inventory = inventory;
        _dialogue = dialogue;
        _character = character;
        _help = help;
        _projectiles = projectiles;
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        session.Subscribe<ShotLoosed>(_shots.Add);
        session.Subscribe<CommandRejected>(e => _refused.Add($"{e.Command.GetType().Name}: {e.Reason}"));
        session.Subscribe<CastFizzled>(_ => _fizzles++);
        var magic = session.Setup.Magic;
        string bolt = _bolt = magic.Formulas.Values.First(f => f.Targeting == Targeting.Projectile).Id;
        _beats.AddRange(new[]
        {
            new Beat("d01_compass_third", "The compass at the top: heading only, in third person (east from the Waystone)", 120, () => Settle(90, third: true)),
            new Beat("d02_compass_first", "The compass in first person, looking north", 120, () => Settle(0, third: false)),
            new Beat("d03_help", "F1: the controls, read from the input map (the developer's keys apart)", 60, ShowHelp),
            new Beat("d04_take_all_offered", "The waystation's chest opened: Take all [R] above its stacks", 1_500, OpenTheChest),
            new Beat("d05_took_all", "Take all: every stack into the pack", 200, TakeAllFromTheChest),
            new Beat("d06_walked_away", "Walked out of reach: the panel closed by itself", 300, WalkAway),
            new Beat("d07_jump_over_the_timber", "A running jump over the fallen timber (0.8 m), caught over it", 4_000, JumpTheTimber),
            new Beat("d08_crouched_under_the_beam", "Crouched under the Woundmoss beam (1.3 m clear)", 3_000, CrouchUnderTheBeam),
            new Beat("d09_no_room_to_stand", "Standing up under the beam is refused: \"No room to stand\"", 200, StandUnderTheBeam),
            new Beat("d10_bow_taken", "Out the far side, stood, on to the cart: Take all gives the bow and its arrows; the bow in hand", 2_500, TakeTheBow),
            new Beat("d11_bow_drawn", "The bow drawn at a tree 16 m off: the reticle sits where the arrow will go", 200, () => Draw(172_000, 152_000)),
            new Beat("d12_arrow_in_flight", "The arrow in flight, its streak behind it, along the path the shot resolved", 200, () => InFlight(workingOnly: false, metres: 2.5f)),
            new Beat("d13_arrow_stuck", "The arrow stuck in the tree it hit, where it stays a while", 200, ArrowStuck),
            new Beat("d14_primer_read", "Back to Sel: the primer given and read - three formulas known", 4_000, LearnFromSel),
            new Beat("d15_bolt_tell", $"{session.DisplayName(bolt)}'s tell, at the lodge's east wall: the reticle on the wall", 600, () => Tell(bolt)),
            new Beat("d16_bolt_in_flight", $"{session.DisplayName(bolt)} in flight", 200, () => InFlight(workingOnly: true, metres: 3f)),
            new Beat("d17_bolt_burst", $"{session.DisplayName(bolt)} bursting on the wall", 200, Burst),
            new Beat("d18_character", "K: the character sheet - only what the game has now", 60, ShowCharacter),
        });
        _beatTick = session.Simulation!.WorldTick;
        _transcript.AppendLine("# The owner's M6 playtest delta, shown").AppendLine()
            .AppendLine($"Content {session.Content.Version} ({session.Content.Hash}), a fresh game, the real command path. " +
                        $"Asset art: {assetRoot ?? "none - greybox"}.").AppendLine()
            .AppendLine("| Tick | What the picture shows |").AppendLine("|---|---|");
    }

    public string Directory { get; }

    /// <summary>Advance one frame: the name of a screenshot to take now, "done", "failed", or null.</summary>
    public string? Update()
    {
        _frame++;
        if (_beat >= _beats.Count)
        {
            Write();
            return "done";
        }
        var beat = _beats[_beat];
        var simulation = _session.Simulation!;
        if (_broken is null && simulation.WorldTick - _beatTick > beat.Budget)
            _broken = $"it did not finish in {beat.Budget} ticks (refusals: {string.Join("; ", _refused.TakeLast(3))})";
        bool done = false;
        if (_broken is null)
        {
            try
            {
                done = beat.Run();
            }
            catch (Exception e)
            {
                _broken = $"{e.GetType().Name}: {e.Message}";
            }
        }
        if (_broken is { } why)
        {
            _transcript.AppendLine($"| {simulation.WorldTick} | FAILED at '{beat.Name}': {why}, at ({simulation.Player.Body.XMm / 1000.0:0.0}, {simulation.Player.Body.ZMm / 1000.0:0.0}) |");
            Write();
            GD.PushError($"UNNAMED delta shots: '{beat.Name}' failed: {why}");
            return "failed";
        }
        if (!done)
            return null;
        _transcript.AppendLine($"| {simulation.WorldTick} | **{beat.Says}** ![{beat.Name}]({beat.Name}.png) |");
        _beat++;
        _beatTick = simulation.WorldTick;
        _waypoint = 0;
        _phase = 0;
        return beat.Name;
    }

    // ── the beats ───────────────────────────────────────────────────────────

    /// <summary>Stand, turn the view to a bearing, and give the frame time to draw.</summary>
    private bool Settle(float bearing, bool third)
    {
        if (_phase == 0)
        {
            _camera.TargetDistance = third ? 3.5f : 0;
            _camera.Pitch = third ? -0.25f : -0.05f;
        }
        _camera.Yaw = Mathf.DegToRad(bearing + 180);
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
        return ++_phase > 40;
    }

    private bool ShowHelp()
    {
        _camera.TargetDistance = 3.5f;
        if (_phase++ == 0)
            _help.Toggle();
        if (_phase < 20)
            return false;
        _pendingClose = () => _help.Toggle();
        return true;
    }

    private Action? _pendingClose;

    private bool OpenTheChest()
    {
        Close();
        if (!Walk(ToTheChest, Near))
            return false;
        Look(38_000, 137_000);
        if (_phase++ == 0)
            _inventory.Open(Chest);
        return _phase > 20 && _inventory.OpenContainer == Chest;
    }

    private bool TakeAllFromTheChest()
    {
        if (_phase++ == 0)
            _inventory.TakeAll();
        bool empty = _session.Simulation!.Containers.Single(c => c.Site.Key == Chest).Items.IsEmpty;
        if (_phase > 60 && !empty)
            _broken = "the chest still holds something after Take all";
        return empty && _phase > 20;
    }

    private bool WalkAway()
    {
        if (!Walk(AwayFromTheChest, Near))
            return false;
        if (_inventory.Visible)
            _broken = "the chest's panel stayed open out of reach";
        return ++_phase > 10;
    }

    /// <summary>Run north at the timber, take off short of it, and take the picture while the body is over it.</summary>
    private bool JumpTheTimber()
    {
        var body = _controller.Authoritative;
        if (_phase == 0)
        {
            if (!Travel(ToTheTimber, Near))
                return false;
            _phase = 1;
        }
        // Watched from the west, looking east and a little up from low down, side on to the jump: the timber's end face in front,
        // the body crossing the picture over it.
        _camera.TargetDistance = 6f;
        _camera.Yaw = Mathf.DegToRad(90 + 180);
        _camera.Pitch = 0.08f;
        var posture = _session.Simulation!.Posture;
        if (_phase == 1)
        {
            _controller.SteerWorld(new Vector3(0, 0, 1), Gait.Run, _camera);
            if (body.ZMm >= 145_700)
            {
                _controller.Jump();
                _phase = 2;
            }
            return false;
        }
        _controller.SteerWorld(new Vector3(0, 0, 1), Gait.Run, _camera);
        _wasAirborne |= posture.Airborne;
        if (posture.Airborne && body.ZMm is > 147_300 and < 147_900 && _phase == 2)
        {
            _phase = 3;
            return true;   // the picture, over the timber
        }
        if (_wasAirborne && !posture.Airborne && body.ZMm > 148_200 && _phase == 2)
            _broken = "the arc landed past the timber before a frame caught it over it";
        if (_phase == 2 && _wasAirborne && !posture.Airborne && body.ZMm < 147_000)
            _broken = "the jump did not carry the body over the timber";
        return false;
    }

    private bool CrouchUnderTheBeam()
    {
        _camera.TargetDistance = 3.5f;
        if (_phase == 0)
        {
            if (_controller.Authoritative.ZMm < 148_300)
            {
                _controller.SteerWorld(new Vector3(0, 0, 1), Gait.Run, _camera);   // off the arc and clear of the timber
                return false;
            }
            _phase = 1;
            _waypoint = 0;
        }
        if (_phase == 1)
        {
            if (!Travel(ToTheBeam, Near))
                return false;
            _controller.Crouch(true);
            _phase = 2;
            _waypoint = 0;
            return false;
        }
        // From the south-east, looking north-west, level with the crouched eye so the camera's arm stays under the beam: the beam runs
        // across the picture with the crouched body under it.
        _camera.Yaw = Mathf.DegToRad(330 + 180);
        _camera.Pitch = 0f;
        if (!Walk(UnderTheBeam, Near))
            return false;
        return ++_phase > 20 && _session.Simulation!.Posture.Stance == Stance.Crouched;
    }

    private bool StandUnderTheBeam()
    {
        if (_phase++ == 0)
        {
            _refused.Clear();
            _controller.Crouch(false);
        }
        if (_phase < 12)
            return false;
        if (_session.Simulation!.Posture.Stance != Stance.Crouched || !_refused.Any(r => r.Contains("no room to stand", StringComparison.Ordinal)))
            _broken = "standing under the beam was not refused";
        return true;
    }

    private bool TakeTheBow()
    {
        var simulation = _session.Simulation!;
        if (_phase == 0)
        {
            if (!Walk(PastTheBeam, Near))
                return false;
            _controller.Crouch(false);
            _phase = 1;
            _waypoint = 0;
            return false;
        }
        if (_phase == 1)
        {
            if (!Travel(ToTheCart, Near))
                return false;
            Look(157_000, 160_200);
            _inventory.Open(Cart);
            _inventory.TakeAll();
            _phase = 2;
            return false;
        }
        var bow = simulation.Player.Inventory.FirstOrDefault(e => _session.Setup.Items.Catalog.Find(e.DefId)?.Weapon?.Ranged == true);
        if (bow is null)
        {
            if (++_phase > 80)
                _broken = "no bow came out of the cart";
            return false;
        }
        Close();
        _inventory.Close();
        if (!simulation.Player.Equipment.ContainsValue(bow.ItemId))
            _session.Submit(new EquipCommand(simulation.PlayerId, bow.ItemId));
        return simulation.Combat.Weapon.Ranged && ++_phase > 30;
    }

    /// <summary>Face a point and start drawing; the picture is taken at the end of the draw, just before the release.</summary>
    private bool Draw(long xMm, long zMm)
    {
        _camera.TargetDistance = 3.5f;
        _camera.Pitch = -0.1f;
        Look(xMm, zMm);
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera, faceCamera: true);
        var combat = _session.Simulation!.Combat;
        if (_phase == 0 && combat.Phase == CombatPhase.Idle && ++_wait > 15)
        {
            _shots.Clear();
            _controller.Attack();
            _phase = 1;
        }
        return _phase == 1 && combat.Phase == CombatPhase.Windup && combat.PhaseTicksLeft <= 2;
    }

    private int _wait;

    /// <summary>The shot on its way, once it is some metres out. A working that fizzled at its release is worked again.</summary>
    private bool InFlight(bool workingOnly, float metres)
    {
        _wait = 0;
        if (_shots.Count == 0)
        {
            if (workingOnly && _fizzles > 0 && _session.Simulation!.Combat.Phase == CombatPhase.Idle)
            {
                _fizzles = 0;
                _controller.Cast(_bolt);
            }
            return false;
        }
        if (workingOnly && !_session.Setup.Magic.Formulas.ContainsKey(_shots[^1].Source))
            _broken = "the shot was not the working's";
        if (_projectiles.Travelled(workingOnly) is not { } travelled)
        {
            if (++_phase > 10)
                _broken = "the shot was published but nothing was drawn in flight";
            return false;
        }
        return travelled >= metres;
    }

    /// <summary>Walk up to where the arrow stopped and look at it from the side: seen end on, from where it was loosed, it is a speck.</summary>
    private bool ArrowStuck()
    {
        var shot = _shots.LastOrDefault() ?? throw new InvalidOperationException("no shot was loosed");
        if (shot.Target is not null)
            _broken = "the arrow struck a creature, not the tree";
        double dx = shot.ToXMm - shot.FromXMm, dz = shot.ToZMm - shot.FromZMm, length = Math.Sqrt(dx * dx + dz * dz);
        var stand = ((shot.ToXMm - dx / length * 1_600 + dz / length * 1_200) / 1000.0, (shot.ToZMm - dz / length * 1_600 - dx / length * 1_200) / 1000.0);
        _camera.TargetDistance = 2.2f;
        _camera.Pitch = -0.15f;
        if (!Walk(new[] { stand }, Near))
            return false;
        Look(shot.ToXMm, shot.ToZMm);
        _camera.Yaw += 0.3f;   // the arrow off to the right of the body, not behind it
        return ++_phase > 30;
    }

    private bool LearnFromSel()
    {
        _camera.TargetDistance = 3.5f;
        var simulation = _session.Simulation!;
        if (_phase == 0)
        {
            if (!Travel(ToSel))
                return false;
            _phase = 1;
            _waypoint = 0;
            return false;
        }
        if (_phase < 1000)
        {
            if (!Converse(Sel, "books", "take", "ruin", "tavar", "stones", "back"))
                return false;
            _phase = 1000;
            return false;
        }
        if (simulation.Player.Inventory.FirstOrDefault(e => _session.Setup.Magic.Teaches.ContainsKey(e.DefId)) is { } book)
        {
            _session.Submit(new UseItemCommand(simulation.PlayerId, book.ItemId));
            return false;
        }
        return _controller.Formulas().Length == 3;
    }

    private bool Tell(string formula)
    {
        _camera.TargetDistance = 3.5f;
        _camera.Pitch = -0.1f;
        if (_phase == 0)
        {
            if (!Walk(ToTheLodgeWall, Near))
                return false;
            _phase = 1;
        }
        Look(40_000, 126_000);
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera, faceCamera: true);
        var combat = _session.Simulation!.Combat;
        if (_phase == 1 && combat.Phase == CombatPhase.Idle && ++_wait > 15)
        {
            _shots.Clear();
            _fizzles = 0;
            _controller.Cast(formula);
            _phase = 2;
        }
        return _phase == 2 && combat.Casting is not null && combat.Phase == CombatPhase.Windup && combat.PhaseTicksLeft <= 2;
    }

    private bool Burst()
    {
        var shot = _shots.LastOrDefault() ?? throw new InvalidOperationException("no working was thrown");
        if (shot.Target is not null)
            _broken = "the working struck a creature, not the wall";
        return _projectiles.SinceBurst >= 0.15;
    }

    private bool ShowCharacter()
    {
        if (_phase++ == 0)
        {
            _character.Visible = true;
            _character.Refresh();
        }
        return _phase > 20;
    }

    // ── moving and looking ──────────────────────────────────────────────────

    /// <summary>Close whatever the last beat left open.</summary>
    private void Close()
    {
        _pendingClose?.Invoke();
        _pendingClose = null;
    }

    private bool Travel((double X, double Z)[] route, float arrive = 0.5f) => !Defend() && Walk(route, arrive);

    private bool Walk((double X, double Z)[] route, float arrive = 0.5f)
    {
        if (_waypoint >= route.Length)
        {
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
            return true;
        }
        var body = _controller.Authoritative;
        var (x, z) = route[_waypoint];
        var to = new Vector3((float)(x - body.XMm / 1000.0), 0, (float)(z - body.ZMm / 1000.0));
        if (to.Length() < arrive)
            _waypoint++;
        else
            _controller.SteerWorld(to.Normalized(), Gait.Run, _camera);
        return false;
    }

    /// <summary>Fight the nearest creature hunting the character, as the acceptance playthrough does; strays and sentinels are walked past.</summary>
    private bool Defend()
    {
        var simulation = _session.Simulation!;
        var body = _controller.Authoritative;
        var threat = simulation.Creatures
            .Where(c => c.Alive && c.Hostile && c.RoleId is not ("sentinel" or "stray") && Distance(body, c.Body) < 25_000)
            .OrderBy(c => Distance(body, c.Body))
            .FirstOrDefault();
        if (threat is null)
            return false;
        var to = new Vector3((float)((threat.Body.XMm - body.XMm) / 1000.0), 0, (float)((threat.Body.ZMm - body.ZMm) / 1000.0));
        _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
        var combat = simulation.Combat;
        long radius = _session.Setup.Combat.Creatures[threat.DefId].RadiusMm;
        bool inReach = combat.Weapon.Ranged || to.Length() <= (combat.Weapon.ReachMm + radius - 150) / 1000f;
        _controller.SteerWorld(inReach ? Vector3.Zero : to.Normalized(), Gait.Run, _camera, faceCamera: true);
        if (inReach && combat.Phase == CombatPhase.Idle)
            _controller.Attack();
        return true;
    }

    private bool Approach(string npcId)
    {
        var npc = _session.Simulation!.Npcs.Single(n => n.Id == npcId);
        var body = _controller.Authoritative;
        var away = new Vector3(body.XMm - npc.Body.XMm, 0, body.ZMm - npc.Body.ZMm).Normalized() * 1.3f;
        if (!Walk(new[] { (npc.Body.XMm / 1000.0 + away.X, npc.Body.ZMm / 1000.0 + away.Z) }, Near))
            return false;
        Look(npc.Body.XMm, npc.Body.ZMm);
        return true;
    }

    /// <summary>Walk up to an NPC and say these replies in turn, one a moment; true once the conversation has ended after the last.</summary>
    private bool Converse(string npcId, params string[] replies)
    {
        var simulation = _session.Simulation!;
        if (_phase == 1 || (_phase == 2 && simulation.Conversation is null))
        {
            if (!Approach(npcId))
                return false;
            _controller.Talk(npcId);
            _phase = 3;
            return false;
        }
        int said = (_phase - 3) / 8;
        if ((_phase - 3) % 8 == 0 && said < replies.Length)
        {
            if (simulation.Conversation?.Replies.Any(r => r.Id == replies[said]) == true)
                _dialogue.Answer(replies[said]);
            else
                _broken = $"{npcId} did not offer '{replies[said]}' at the line '{simulation.Conversation?.NodeId ?? "(no conversation)"}'";
        }
        _phase++;
        if (said < replies.Length)
            return false;
        if (simulation.Conversation is not null)
            _dialogue.Leave();
        return simulation.Conversation is null;
    }

    private bool Look(long xMm, long zMm)
    {
        var body = _controller.Authoritative;
        _camera.Yaw = Mathf.Atan2(-(xMm - body.XMm) / 1000f, -(zMm - body.ZMm) / 1000f);
        return true;
    }

    private static double Distance(Body a, Body b) => Math.Sqrt(Math.Pow(a.XMm - b.XMm, 2) + Math.Pow(a.ZMm - b.ZMm, 2));

    private void Write() =>
        File.WriteAllText(Path.Combine(Directory, "transcript.md"), _transcript.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
}
