"""Move the tackle helper out of the subscription method and onto the class (goal round 3)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.Events.cs')
code = path.read_text(encoding='utf-8')

helper = '''
    /// <summary>The rod, line and float the character fishes with, made once and kept on the player's body.</summary>
    private Art.FishingTackle? _tackle;

    private Art.FishingTackle Tackle()
    {
        if (_tackle is not null && GodotObject.IsInstanceValid(_tackle))
            return _tackle;
        _tackle = new Art.FishingTackle { Name = "Tackle" };
        (_avatar as Node3D ?? this).AddChild(_tackle);
        return _tackle;
    }
'''
if helper in code:
    code = code.replace(helper, '', 1)

lines = code.rstrip('\n').split('\n')
# The file's last closing brace closes the class: put the helper just before it.
for index in range(len(lines) - 1, -1, -1):
    if lines[index].strip() == '}':
        lines.insert(index, helper.rstrip('\n'))
        break
path.write_text('\n'.join(lines) + '\n', encoding='utf-8')
print('tackle helper moved onto the class')
