"""The key does one thing: read the ground (following is the movement that answers the read) (goal round 16)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.Input.cs')
code = path.read_text(encoding='utf-8')
old = '        _session.Submit(simulation.FollowingTrail ? new StopFollowingCommand(simulation.PlayerId) : new ReadTrailCommand(simulation.PlayerId));'
new = '        _session.Submit(new ReadTrailCommand(simulation.PlayerId));'
assert old in code
path.write_text(code.replace(old, new, 1), encoding='utf-8')
print('the key reads the ground')
