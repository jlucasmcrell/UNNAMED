"""`Check` is a local helper inside region validation, so the fishing builder throws its own FormatExceptions (goal round 1)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Content\WorldContent.cs')
code = path.read_text(encoding='utf-8')

replacements = [
    ('            Check(items.ContainsKey(item), "CFH001", $"config.fishing: bait \'{item}\' is not an item");',
     '            if (!items.ContainsKey(item))\n                throw new FormatException($"config.fishing: bait \'{item}\' is not an item");'),
    ('                Check(loot.ContainsKey(table), "CFH002", $"config.fishing: {kind}/{band} names catch table \'{table}\', which is not defined");',
     '                if (!loot.ContainsKey(table))\n                    throw new FormatException($"config.fishing: {kind}/{band} names catch table \'{table}\', which is not defined");'),
    ('            Check(items.ContainsKey(rod), "CFH003", $"config.fishing: rod \'{rod}\' is not an item");',
     '            if (!items.ContainsKey(rod))\n                throw new FormatException($"config.fishing: rod \'{rod}\' is not an item");'),
]
for old, new in replacements:
    assert old in code, old[:60]
    code = code.replace(old, new, 1)
path.write_text(code, encoding='utf-8')
print('fishing builder reports with FormatException')
