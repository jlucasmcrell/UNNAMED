// UNNAMED Presentation - the action captures: a scripted run that presses the game's own keys at the game's own places, and keeps the
// frame of each moment of a timed activity (a line in the creek, a pick in the granary's lock, three swings at the seam).
// Godot presentation only: it presses keys and writes pictures, and never writes world state (D-11).

using System.Text;
using Godot;
using UNNAMED.Application;
using UNNAMED.Domain;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    private ActionShots? _actions;

    /// <summary>
    /// <c>--shot-actions &lt;dir&gt; --shot-beat fish|lock|mine</c>: the beats <c>--shot</c> cannot reach, because <c>--shot</c> only
    /// steers a body to a place and takes a still. Fishing, picking a lock and swinging at a node are timed activities: they begin when a
    /// key is pressed, they resolve on the simulation's own roll, and the moment worth a picture is the middle of one, not the place it
    /// happens at. So this driver presses the game's own actions - <see cref="Input.ActionPress"/>, the route
    /// <see cref="InputCheck"/> already uses - and then calls <see cref="ReadInput"/>, which is the code a player's press reaches, so
    /// nothing here can show a state a player could not reach.
    /// <para>
    /// It keeps a frame at each moment that matters rather than on a fixed interval: the cast, the line out, the bite, the catch; the
    /// pick going in, the lock giving, the door swinging; each of the three swings and the ore coming out. Every file is named for what
    /// it shows and every moment it waited on is written to <c>report.md</c> beside the frames, so a frame can be traced to a tick.
    /// </para>
    /// <para>
    /// The place to stand is <c>--stand x,z</c> (the same option every other scripted pass begins from) and the camera is the beat's own,
    /// because where to look is part of the beat: along the water, at the lock, at the seam.
    /// </para>
    /// </summary>
    private sealed class ActionShots
    {
        /// <summary>The shipped frontier's three sites (<c>content/regions/starting_frontier.yaml</c>).</summary>
        private const string CreekDef = "water.ah_creek", GranaryDef = "door.granary", SeamDef = "node.ore.iron_seam";

        private readonly GameSession _session;
        private readonly PlayerController _controller;
        private readonly CameraRig _camera;
        private readonly string _beat;
        private readonly float _yaw;
        private readonly float _pitch;
        private readonly StringBuilder _report = new();

        /// <summary>The longest a beat may wait for the moment it is named for: two minutes of ticks, after which it says so and ends.</summary>
        private const int MaxFrames = 2400;

        private int _frame;
        private int _step;
        private string? _press;
        private int _releaseFrame;
        private string? _shot;
        private bool _done;
        private string _stepNote = "";

        // What the events said, each with the tick it landed on: the evidence that the picture is of the moment it is named for.
        private long _castTick = -1, _lineTick = -1, _biteTick = -1, _reelTick = -1;
        private bool _caught, _picked, _gathered;
        private int _struck, _attempts, _stepFrame;
        private readonly List<string> _moments = new();

        public ActionShots(Main main, GameSession session, PlayerController controller, CameraRig camera, string directory, string beat)
        {
            _main = main;
            _session = session;
            _controller = controller;
            _camera = camera;
            _beat = beat;
            Directory = directory;
            System.IO.Directory.CreateDirectory(directory);
            (_yaw, _pitch) = Aim(beat);
            _report.AppendLine($"# {beat} - the frames of one timed activity, and the tick each was taken on");
            _report.AppendLine();
            _report.AppendLine($"Region {session.Setup.Layout.Id}, {session.TickSeconds * 1000:0} ms ticks, world seed "
                               + $"{session.Simulation!.World.WorldSeed}. The keys are the game's own: each is pressed as `Input.ActionPress` and read "
                               + "by `Main.ReadInput`, so what happens here happens to a player who presses the same key at the same place.");
            _report.AppendLine();
            _report.AppendLine($"Camera yaw {_yaw:0.000} rad ({Mathf.RadToDeg(_yaw):0} deg), pitch {_pitch:0.00}.");
            _report.AppendLine();
            // The events the three systems publish, subscribed here so the transcript is the simulation's own account of what happened
            // rather than this driver's guess at it.
            session.Subscribe<FishCastStarted>(e => Moment("cast began", e.Tick, ref _castTick));
            session.Subscribe<FishingLineOut>(e => Moment("the line is in the water", e.Tick, ref _lineTick));
            session.Subscribe<SpoorRead>(e =>
            {
                _read = e;
                _moments.Add($"- tick {e.Tick}: the ground says {e.Count} went {e.BearingMdeg} mdeg, {e.AgeTicks} old"
                    + $"{(e.Uncertain ? ", uncertain" : "")}{(e.Readable ? "" : $" - {e.Reason}")}");
            });
            session.Subscribe<FishCaught>(e => { _caught = true; _moments.Add($"- tick {e.Tick}: **caught** {e.Count} x {e.ItemId} from {e.WaterKey}"); });
            session.Subscribe<FishLost>(e => _moments.Add($"- tick {e.Tick}: the line came back empty - {e.Reason}"));
            session.Subscribe<LockAttemptStarted>(e => _moments.Add($"- tick {e.Tick}: a pick started on {e.TargetKey} (tier {e.Tier})"));
            session.Subscribe<LockPicked>(e => { _picked = true; _moments.Add($"- tick {e.Tick}: **the lock gave** - {e.TargetKey}"); });
            session.Subscribe<LockPickFailed>(e => _moments.Add($"- tick {e.Tick}: the lock held on {e.TargetKey}{(e.Broke ? ", and the picks snapped" : "")}"));
            session.Subscribe<SwingStarted>(e => _moments.Add($"- tick {e.Tick}: a swing started at {e.NodeKey}"));
            session.Subscribe<NodeStruck>(e => { _struck++; _moments.Add($"- tick {e.Tick}: strike {e.NodeKey}, {e.StrikesLeft} to go"); });
            session.Subscribe<NodeGathered>(e => { _gathered = true; _moments.Add($"- tick {e.Tick}: **the ore came out** - {e.Count} x {e.ItemId}{(e.Spent ? ", the seam worked out" : "")}"); });
            session.Subscribe<CommandRejected>(e => _moments.Add($"- tick {e.Tick}: refused - {e.Command.GetType().Name}: {e.Reason}"));
        }

        public string Directory { get; }

        private readonly Main _main;

        /// <summary>
        /// A quest card opens over the middle of the picture when a new objective lands - on a fresh character the first one comes up
        /// within a few seconds of the first cast, and it covers the character and the water alike, which is exactly what the frame is
        /// for. So it is dismissed with the key the card itself offers (Escape, Main.Input's own branch) - and ONLY while a card is up,
        /// because Escape with none up opens the System Menu instead.
        /// </summary>
        private void CardsDown()
        {
            if (_main._questCards.Cards.Current is not null && _press is null)
                Press("release_mouse");
        }

        private void Moment(string what, long tick, ref long slot)
        {
            slot = tick;
            _moments.Add($"- tick {tick}: {what}");
        }

        /// <summary>Where each beat looks: along the water, at the lock, at the seam. The yaw is the camera's own, the convention
        /// <see cref="SurveyShots"/> uses - the view direction is <c>(-sin yaw, -cos yaw)</c>.</summary>
        private static (float Yaw, float Pitch) Aim(string beat) => beat switch
        {
            // Down the creek from its north bank at (470.6, 759.7), looking along the run toward (452, 760). The water reaches the body
            // (WaterSite.ReachMm, 2 m past the half-width) and the pitch is steep enough to hold the surface in frame: the creek is cut
            // 0.9 m below its own banks, so a level look shows only the far bank's grass.
            "fish" => (Mathf.DegToRad(90f), -0.28f),
            // The granary's south door from (941.0, 674.7): due north, at the hasp.
            "lock" => (Mathf.Pi, -0.04f),
            // The seam rock's south face from (428, 644.2): due north, at the vein.
            "mine" => (Mathf.Pi, -0.06f),
            // The deer ground at the Charwood spawn (612, 718): looking east along the trees, tilted down so the ground a person reads is in
            // frame rather than the treeline.
            "track" => (Mathf.DegToRad(270f), -0.42f),
            _ => throw new ArgumentException($"--shot-beat wants fish, lock, mine or track; got '{beat}'"),
        };

        /// <summary>Advance one frame: press this frame's key, let the game read it, and name the picture this frame is. Null when it is
        /// not one, "done" at the end.</summary>
        public string? Update(Action readInput)
        {
            _frame++;
            if (_press is not null && _frame >= _releaseFrame)
            {
                Input.ActionRelease(_press);
                _press = null;
            }
            // The camera is the beat's own and stays there: a press that had to be re-aimed would be a press a player could not make.
            _camera.Yaw = _yaw;
            _camera.Pitch = _pitch;
            _shot = null;
            // A run that waited on a moment that never came still has to end: a window left open holds the GPU lock.
            if (!_done && _frame > MaxFrames)
                Fail($"nothing more happened within {MaxFrames} frames");
            if (!_done)
                Advance();
            CardsDown();
            readInput();
            // The body faces where the camera looks, so the character is turned to the water, the lock or the seam and not standing
            // with their back to it. The camera is set before this, so the turn is the read one.
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera, faceCamera: true);
            if (_shot is not null)
            {
                _moments.Add($"- frame {_frame}: picture **{_shot}.png**");
                return _shot;
            }
            return _done ? "done" : null;
        }

        private void Advance()
        {
            switch (_beat)
            {
                case "fish": Fishing(); break;
                case "track": Tracking(); break;
                case "lock": Locking(); break;
                case "mine": Mining(); break;
            }
        }

        // ── fishing ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Read the ground, follow what it says, and read again. Deer wander a spawn on the frontier and leave prints, broken stems, blood,
        /// droppings and scent as they go, so the beat waits at the edge of a herd's ground for sign to be laid, reads it, then walks the
        /// bearing the read gave and reads a second time - the loop canon calls Read the Ground and Walk Their Road.
        /// </summary>
        private void Tracking()
        {
            var simulation = _session.Simulation!;
            if (_step == 0)
            {
                if (_frame < 20)
                    return;
                _moments.Add($"- standing at {simulation.Player.Body.XMm / 1000.0:0.#},{simulation.Player.Body.ZMm / 1000.0:0.#}, waiting for sign");
                _step = 1;
                _stepFrame = _frame;
                return;
            }
            if (_step == 1)
            {
                // Sign arrives as the animals move, so the read is tried every half second until the ground has something to say.
                if ((_frame - _stepFrame) % 10 == 0)
                {
                    _read = null;
                    _session.Submit(new ReadTrailCommand(simulation.PlayerId));
                }
                if (_read is { Readable: true } read)
                {
                    _moments.Add($"- tick {read.Tick}: read **{read.Count} went {read.BearingMdeg} mdeg, {read.AgeTicks} ticks old**");
                    Shot("01_read");
                    _step = 2;
                    _stepFrame = _frame;
                    return;
                }
                if (_frame - _stepFrame > 600)
                {
                    Fail($"no sign in ten seconds at {simulation.Player.Body.XMm / 1000.0:0.#},{simulation.Player.Body.ZMm / 1000.0:0.#}"
                        + $" (last read: {_read?.Reason ?? "nothing"})");
                    _step = 3;
                }
                return;
            }
            if (_step == 2)
            {
                // Walk the bearing the read gave: the camera is already turned that way, so forward is the trail.
                if (_frame - _stepFrame == 60)
                {
                    Shot("02_following");
                    _step = 3;
                    _stepFrame = _frame;
                    return;
                }
                Press("move_forward");
                return;
            }
            if (_step == 3)
            {
                if (_frame - _stepFrame == 30)
                {
                    _session.Submit(new ReadTrailCommand(simulation.PlayerId));
                    Shot("03_read_again");
                    _moments.Add($"- second read: {(_read is { } again ? (again.Readable ? $"{again.Count} went {again.BearingMdeg} mdeg" : again.Reason) : "no answer")}");
                    Done($"read {_moments.Count} sign(s) and walked the trail");
                }
                return;
            }
        }

        /// <summary>
        /// N casts and N reels (Main.Input binds <c>fish</c> to N). The cast plays for its ticks, the line then waits, and the reel has to
        /// land inside the bite's window - so this waits on the simulation's own <see cref="PlayerActivity.BiteTick"/> rather than on a
        /// guess, which is also how it can keep one frame at the bite itself.
        /// </summary>
        private void Fishing()
        {
            var simulation = _session.Simulation!;
            long tick = simulation.WorldTick;
            if (_step == 0)
            {
                if (_frame < 24)
                    return;
                if (!simulation.CanFish)
                {
                    Fail($"not at a fishable bank: water '{simulation.WaterKey ?? "none"}'");
                    return;
                }
                // What the cast needs is the config's: say what the pack had when it failed, so one run explains itself.
                string rod = simulation.Setup.Fishing.RodItemId ?? "";
                bool hasRod = rod.Length == 0 || simulation.Player.Inventory.Any(e => e.DefId == rod);
                string baits = string.Join(",", simulation.Setup.Fishing.Baits.Keys
                    .Where(id => simulation.Player.Inventory.Any(e => e.DefId == id)));
                _moments.Add($"- before the cast: bank ok, rod {(hasRod ? "carried" : "MISSING")}, baits [{baits}], "
                    + $"activity {simulation.Activity?.Kind.ToString() ?? "none"}");
                // The key casts only when the camera's focus is water; a capture camera is fixed, so the beat submits what the key submits.
                _session.Submit(new StartFishingCommand(simulation.PlayerId, simulation.WaterKey!));
                _step = 1;
                _stepFrame = _frame;
                _stepNote = $"cast (N) at tick {tick}";
                return;
            }
            var activity = simulation.Activity;
            if (_step == 1)
            {
                if (activity is null && _frame - _stepFrame > 20)
                {
                    Fail($"the cast did not start: water '{simulation.WaterKey ?? "none"}', can fish {simulation.CanFish}");
                    return;
                }
                if (activity is { Kind: ActivityKind.FishCast } && _frame - _stepFrame == 14)
                    Shot("01_cast");
                if (activity is { Kind: ActivityKind.FishWaiting } waiting)
                {
                    if (!_lineOut)
                    {
                        _lineOut = true;
                        _lineFrame = _frame;
                        Shot("02_line_out");
                    }
                    _biteTick = waiting.BiteTick;
                    // The reel has to land inside the bite's window, and the bite is the simulation's own hidden number; reading it is
                    // what makes the catch a beat this can keep a frame of rather than something to stand and hope for.
                    if (waiting.BiteTick > 0 && tick >= waiting.BiteTick)
                    {
                        Shot("03_bite");
                        Press("fish");
                        _reelFrame = _frame;
                        _step = 2;
                    }
                }
                // A line that came back empty, or a cast the bank refused, is cast again: a refusal is the system saying why, not a
                // reason to end the beat with no picture of it.
                else if (activity is null && _frame - _stepFrame >= 6)
                {
                    if (++_casts > 3)
                    {
                        Fail(_lineOut ? "three casts and nothing took the line" : "the cast was refused");
                        return;
                    }
                    _lineOut = false;
                    Press("fish");
                    _stepFrame = _frame;
                    _stepNote = $"cast {_casts + 1} (N) at tick {tick}";
                }
                return;
            }
            if (_step == 2)
            {
                if (activity is { Kind: ActivityKind.FishReel })
                {
                    if (_frame - _reelFrame == 10)
                        Shot("04_reel");
                    if (_caught && _frame - _reelFrame == 28)
                        Shot("05_catch");
                }
                else if (activity is null && _frame - _reelFrame > 12)
                {
                    Shot("06_after");
                    Done(_caught ? "the fish is in the pack" : "the line came back empty");
                }
            }
        }

        private bool _lineOut;
        private int _casts;
        private long _lineFrame, _reelFrame;

        // ── lockpicking ─────────────────────────────────────────────────────────

        /// <summary>
        /// E on a tiered door starts the pick instead of opening it (Main.Input's own branch). The attempt holds for its ticks and then
        /// rolls, and a beginner's hand is 8% on a tier-1 lock (<see cref="LockRules.Chance"/>), so this keeps attempting - a refusal is
        /// the system working, not a failure - and keeps one frame of the picks in the works from each of the first three attempts, so
        /// the folder carries a pick in progress without carrying fifty of them. Once the lock gives, E opens the door it just unlocked,
        /// which is the one frame that shows the difference.
        /// </summary>
        private void Locking()
        {
            var simulation = _session.Simulation!;
            long tick = simulation.WorldTick;
            if (_step == 0)
            {
                if (_frame < 24)
                    return;
                Focus(simulation);
                Press("interact");
                _step = 1;
                _attempts = 1;
                _stepFrame = _frame;
                return;
            }
            if (_step == 1)
            {
                if (simulation.Activity is { Kind: ActivityKind.PickLock } && _frame - _stepFrame == 12 && _attempts <= 3)
                    Shot($"01_pick_{_attempts:00}");
                if (_picked)
                {
                    _step = 2;
                    _stepFrame = _frame;
                    return;
                }
                // A refusal has already put the activity out; press again once the toast has had its few frames. One attempt is 40
                // ticks and the run takes a tick a frame, so the roll has been made by the 40th frame after the press.
                if (simulation.Activity is null && _frame - _stepFrame >= 42)
                {
                    if (++_attempts > 80)
                    {
                        Fail("80 attempts and the lock never turned");
                        return;
                    }
                    Press("interact");
                    _stepFrame = _frame;
                }
                return;
            }
            if (_step == 2)
            {
                // E again, now that the hasp is free: an unlocked door opens instead of taking a pick. The first frame is `<= 1`
                // because the step is entered on this same frame (the tick that set _picked), one frame before this runs.
                if (_frame - _stepFrame <= 1)
                {
                    Shot("10_lock_gives");
                    Press("interact");
                }
                else if (_frame - _stepFrame == 26)
                    Shot("11_door_opens");
                else if (_frame - _stepFrame == 48)
                {
                    Shot("12_after");
                    Done($"the lock gave after {_attempts} attempt{(_attempts == 1 ? "" : "s")}");
                }
            }
        }

        // ── mining ──────────────────────────────────────────────────────────────

        /// <summary>
        /// E at a node swings the pick (Main.Input sends it to <see cref="PlayerController.Gather"/>). The seam is three swings a working
        /// and the system renews the swing itself, so one press carries all three: one frame in each swing, one on each strike, and one
        /// as the ore comes out.
        /// </summary>
        private void Mining()
        {
            var simulation = _session.Simulation!;
            if (_step == 0)
            {
                if (_frame < 24)
                    return;
                Focus(simulation);
                Press("interact");
                _step = 1;
                _stepFrame = _frame;
                return;
            }
            if (_step == 1)
            {
                // One swing is SwingTicks (30) long at one tick a frame; the first is 12 frames in when the pick is deepest.
                int into = _frame - _stepFrame;
                if (into == 12)
                    Shot("01_swing_1");
                if (into == 34)
                    Shot("02_swing_2");
                if (into == 64)
                    Shot("03_swing_3");
                if (_gathered)
                {
                    _step = 2;
                    _stepFrame = _frame;
                }
                else if (simulation.Activity is null && into > 30)
                {
                    Fail("the swing ended with nothing granted");
                }
                return;
            }
            if (_step == 2 && _frame - _stepFrame == 12)
            {
                Shot("04_ore_taken");
                Done($"the seam gave {(_struck > 0 ? $"after {_struck} strikes" : "on the last swing")}");
            }
        }

        // ── plumbing ────────────────────────────────────────────────────────────

        /// <summary>What the interact key means where the character stands, printed so the log says which site the press was for.</summary>
        private void Focus(Simulation simulation)
        {
            string? focus = _controller.FocusOn(_camera) is { } found
                ? $"{found.Kind} '{found.Key}'"
                : "nothing in reach and in front";
            _moments.Add($"- frame {_frame} (tick {simulation.WorldTick}): E means {focus}");
        }

        private void Press(string action)
        {
            if (_press is not null)
                return;
            Input.ActionPress(action);
            _press = action;
            _releaseFrame = _frame + 2;
        }


        /// <summary>The last read of the ground, so the beat can wait for sign rather than guess at it.</summary>
        private SpoorRead? _read;


        private void Shot(string name)
        {
            if (_shot is null)
                _shot = name;
        }

        private void Fail(string why)
        {
            _moments.Add($"- **failed**: {why}");
            Done(why, failed: true);
        }

        private void Done(string why, bool failed = false)
        {
            if (_done)
                return;
            _done = true;
            _report.AppendLine($"## What happened");
            _report.AppendLine();
            foreach (string moment in _moments)
                _report.AppendLine(moment);
            _report.AppendLine();
            _report.AppendLine(failed ? $"**FAILED**: {why}" : $"**{why}**. {_stepNote}");
            System.IO.File.WriteAllText(System.IO.Path.Combine(Directory, "report.md"), _report.ToString());
            GD.Print($"UNNAMED action shots: {_beat} {(failed ? "FAILED" : "done")} - {why} ({_frame} frames)");
        }
    }
}
