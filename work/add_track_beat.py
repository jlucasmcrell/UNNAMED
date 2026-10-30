"""A capture beat for tracking: stand where deer walk, read the ground, follow the bearing, read again (goal round 17)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.ActionShots.cs')
code = path.read_text(encoding='utf-8')

old = '                case "fish": Fishing(); break;'
new = '''                case "fish": Fishing(); break;
                case "track": Tracking(); break;'''
assert old in code
code = code.replace(old, new, 1)

beat = '''        /// <summary>
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
                _moments.Add($"- standing at {simulation.Player.Position.X / 1000.0:0.#},{simulation.Player.Position.Z / 1000.0:0.#}, waiting for sign");
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
                    _bearing = read.BearingMdeg;
                    _step = 2;
                    _stepFrame = _frame;
                    return;
                }
                if (_frame - _stepFrame > 600)
                {
                    Fail($"no sign in ten seconds at {simulation.Player.Position.X / 1000.0:0.#},{simulation.Player.Position.Z / 1000.0:0.#}"
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
                    Done();
                }
                return;
            }
        }

'''
marker = '        /// <summary>\n        /// N casts and N reels'
assert marker in code
code = code.replace(marker, beat + marker, 1)

# Members and the subscription the beat reads.
members = '''
        /// <summary>The last read of the ground, so the beat can wait for sign rather than guess at it.</summary>
        private SpoorRead? _read;

        /// <summary>The bearing the first read gave, and the way the beat walks.</summary>
        private int _bearing;
'''
anchor = '        private void Shot(string name)'
assert anchor in code
code = code.replace(anchor, members + '\n' + anchor, 1)

sub = '''            session.Subscribe<SpoorRead>(e =>
            {
                _read = e;
                _moments.Add($"- tick {e.Tick}: the ground says {e.Count} went {e.BearingMdeg} mdeg, {e.AgeTicks} old"
                    + $"{(e.Uncertain ? ", uncertain" : "")}{(e.Readable ? "" : $" - {e.Reason}")}");
            });
'''
hook = '            session.Subscribe<FishCaught>(e =>'
assert hook in code
code = code.replace(hook, sub + hook, 1)
path.write_text(code, encoding='utf-8')
print('the tracking beat exists')
