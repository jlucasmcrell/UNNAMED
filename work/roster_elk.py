"""Add the elk to the creature roster assertion, on the line that holds the doe (fauna goal)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\tests\Content.Tests\CombatContentTests.cs')
lines = path.read_text(encoding='utf-8').split('\n')
needle = '"creature.beast.doe"'
for index, line in enumerate(lines):
    if needle in line and 'hound_pup' in line and '"creature.beast.elk"' not in line:
        lines[index] = line.replace(needle + ',', needle + ', "creature.beast.elk",', 1)
        print('patched line', index + 1)
        break
else:
    print('no line needed the elk')
path.write_text('\n'.join(lines), encoding='utf-8')
