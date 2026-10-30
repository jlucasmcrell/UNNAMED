"""Point the fishing capture at a fishable bank with a rod in hand (goal round 9).

The first capture ran and failed honestly: 'not at a fishable bank: water none'. The beat presses the game's own keys from wherever the
harness stands, so it needs `--stand` at a water, and a rod in the pack now that the config names one, which `--start-items` gives.
"""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\work\capture_fishing.ps1')
code = path.read_text(encoding='utf-8')
old = "        '--shot-actions', $Out,"
new = ("        '--shot-actions', $Out,\n"
       "        '--stand', '492.6,748.4',\n"
       "        '--start-items', 'item.tool.fishing_rod:1,item.material.bait_worm:3',")
assert old in code
path.write_text(code.replace(old, new, 1), encoding='utf-8')
print('the capture stands at the creek with a rod and three worms')
