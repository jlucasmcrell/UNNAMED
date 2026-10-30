"""Sign that is found and refuses to be read is still the ground having something to say (tracking footage).

The walking beat reached a rooster and read "Sign, but not what it means" - the canon behaviour of a tracker whose skill.tracking is still below
Read the Ground's needs: the sign is there, the character cannot interpret it yet. That is worth a frame and worth reporting, so the beat takes
it as sign found rather than failing and losing the run.
"""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.ActionShots.cs')
code = path.read_text(encoding='utf-8')
old = '                if (_read is { Readable: true, Count: > 0 } read)'
new = ('                bool signFound = _read is { Readable: true, Count: > 0 }\n'
       '                    || _read is { Readable: false, Reason: SignNotUnderstood };\n'
       '                if (signFound && _read is { } read)')
assert old in code
code = code.replace(old, new, 1)

anchor = '    private sealed class ActionShots'
constant = '''    /// <summary>What a read says when sign is there and the character has not the technique to read it yet.</summary>
    private const string SignNotUnderstood = "Sign, but not what it means";
'''
if anchor in code and 'SignNotUnderstood =' not in code:
    code = code.replace(anchor, constant + '\n' + anchor, 1)
path.write_text(code, encoding='utf-8')
print('sign found counts')
