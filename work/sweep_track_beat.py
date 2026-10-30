"""Sweep the herd's ground while waiting for sign, so the beat is not stood in one spot (goal round 19).

Sign is laid where the animals walked, and a read only reaches 6 m, so standing still waits on luck. The beat now walks in short stretches and
reads as it goes - the way a tracker covers ground - and still fails honestly if ten seconds pass with nothing.
"""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.ActionShots.cs')
code = path.read_text(encoding='utf-8')
old = '''                if ((_frame - _stepFrame) % 10 == 0)
                {
                    _read = null;
                    _session.Submit(new ReadTrailCommand(simulation.PlayerId));
                }'''
new = '''                if ((_frame - _stepFrame) % 10 == 0)
                {
                    _read = null;
                    _session.Submit(new ReadTrailCommand(simulation.PlayerId));
                }
                // Walk in short stretches and read as we go: sign lies where the animals went, and a read reaches six metres.
                if ((_frame - _stepFrame) % 40 < 20)
                    Press("move_forward");'''
assert old in code
path.write_text(code.replace(old, new, 1), encoding='utf-8')
print('the beat sweeps the herd ground')
