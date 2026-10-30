"""Queue the six new carcass ids for the snapshot inserter (hunting and skinning hooks, all species)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\work\snapshot_fauna.py')
code = path.read_text(encoding='utf-8')
if "'node.carcass.elk'" not in code:
    code = code.replace("    'resource.carcass.moose'",
                        "    'node.carcass.elk', 'node.carcass.rabbit', 'node.carcass.red_fox',\n"
                        "    'resource.carcass.elk', 'resource.carcass.rabbit', 'resource.carcass.red_fox',\n"
                        "    'resource.carcass.moose'", 1)
    path.write_text(code, encoding='utf-8')
print('snapshot list ready')
