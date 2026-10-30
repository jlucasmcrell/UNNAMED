"""Give tracking a key and a line of HUD, so a player - and a capture - can read a trail (goal round 16).

The spoor work merged with commands and events but no way to reach them from the chair: nothing pressed ReadTrailCommand and nothing said what
a read found. G reads the ground and G again follows what it found; the read's own words go to the log.
"""
from pathlib import Path

root = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')

inputs = root / 'src' / 'Presentation' / 'Main.Input.cs'
code = inputs.read_text(encoding='utf-8')
old = '        Bind("wash", Key.B);'
new = ('        Bind("wash", Key.B);'
       '              // the water\'s second verb: a wash at the bank, beside N\'s line and E\'s flask\n'
       '        Bind("track", Key.G);             // read the ground; press again to follow what it found (canon: skill.tracking)')
assert old in code
code = code.replace(old, new, 1)

anchor = '''    /// <summary>
    /// F: a line into the water, or the line brought in.'''
tracking = '''    /// <summary>
    /// G: read the ground, or take up the trail that was read. Sign is the creatures' own - prints, broken stems, blood, droppings and
    /// scent on the wind - and what a read says depends on the tracking techniques the character has (canon's seven).
    /// </summary>
    private void Track()
    {
        var simulation = _session.Simulation!;
        _session.Submit(simulation.FollowingTrail ? new StopFollowingCommand(simulation.PlayerId) : new ReadTrailCommand(simulation.PlayerId));
    }

''' + anchor
assert anchor in code
code = code.replace(anchor, tracking, 1)
inputs.write_text(code, encoding='utf-8')
print('the tracking key exists')
