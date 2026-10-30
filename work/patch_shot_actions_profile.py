"""A capture runs its own fresh character, as every other shot harness does (goal round 11).

`--shot-actions` was missing from the list of harnesses that start from a scratch profile, so the fishing beat booted the default save - a
character carrying its own inventory and no rod, which the config now requires. A cast there could only ever be refused.
"""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.cs')
code = path.read_text(encoding='utf-8')
old = '              || _options.ContainsKey("--shot") || _options.ContainsKey("--d2-review")'
new = '              || _options.ContainsKey("--shot") || _options.ContainsKey("--shot-actions") || _options.ContainsKey("--d2-review")'
assert old in code
path.write_text(code.replace(old, new, 1), encoding='utf-8')
print('the action capture starts from a fresh character')
