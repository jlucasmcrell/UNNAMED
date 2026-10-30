"""Fishing's rules belong in the domain, beside the weather's, so World can read them without depending on Content (goal round 2)."""
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')
source = (ROOT / 'src' / 'Content' / 'FishingContent.cs').read_text(encoding='utf-8')
body = source.split('namespace UNNAMED.Content;', 1)[1]
header = '''// UNNAMED Domain - how fishing is equipped and scheduled: the rod, the baits, the day's bands and the water's table (D20 follow-on)
// No Godot references - this is pure C# domain logic

using System;
using System.Collections.Immutable;

namespace UNNAMED.Domain;
'''
(ROOT / 'src' / 'Domain' / 'Fishing.cs').write_text(header + body, encoding='utf-8')
(ROOT / 'src' / 'Content' / 'FishingContent.cs').unlink()

world = ROOT / 'src' / 'Content' / 'WorldContent.cs'
code = world.read_text(encoding='utf-8')
if 'using UNNAMED.Domain;' not in code:
    code = code.replace('using UNNAMED.Content;', 'using UNNAMED.Content;', 1)
world.write_text(code, encoding='utf-8')

test = ROOT / 'tests' / 'Content.Tests' / 'FishingContentTests.cs'
code = test.read_text(encoding='utf-8')
if 'using UNNAMED.Domain;' not in code:
    code = code.replace('using UNNAMED.Content;', 'using UNNAMED.Content;\nusing UNNAMED.Domain;', 1)
test.write_text(code, encoding='utf-8')
print('fishing rules moved to the domain')
