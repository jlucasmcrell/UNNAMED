"""List the presentation's command-line flags, so the motion proof can use an in-game recorder rather than a desktop grab."""
import re
from pathlib import Path

text = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.cs').read_text(encoding='utf-8')
flags = sorted(set(re.findall(r'"(--[a-z0-9-]+)"', text)))
print(' '.join(flags))
