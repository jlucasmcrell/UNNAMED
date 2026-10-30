"""Bring the species this branch is missing over from the fauna commit, without disturbing its bindings (region fauna at full scale).

The build-2 branch carries deer, elk, rabbit and the foldscar grazers, but not moose, duck, crow or trout - those were built and committed on
the Gate D branch. This copies only the files that are absent here; existing content is left exactly as it is.
"""
import subprocess
from pathlib import Path

SOURCE = Path(r'D:\Otherreach_Scratch\ds_gate_d_wt')
TARGET = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')
COMMIT = '0c98aa2d'

WANTED = [
    'content/creatures/beast/crow.yaml',
    'content/creatures/beast/duck.yaml',
    'content/creatures/beast/moose.yaml',
    'content/creatures/fish/trout.yaml',
    'content/loot/fowl.yaml',
    'content/loot/moose.yaml',
    'content/loot/trout.yaml',
    'content/abilities/creature/tail_slap.yaml',
    'content/spawns/wildlife/charwood_crows.yaml',
    'content/spawns/wildlife/foldscar_moose.yaml',
    'content/spawns/wildlife/outfall_trout.yaml',
    'content/spawns/wildlife/outfall_ducks.yaml',
]

copied, kept = [], []
for relative in WANTED:
    destination = TARGET / relative
    if destination.exists():
        kept.append(relative)
        continue
    text = subprocess.run(['git', '-C', str(SOURCE), 'show', f'{COMMIT}:{relative}'],
                          capture_output=True, text=True, check=True).stdout
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(text, encoding='utf-8', newline='\n')
    copied.append(relative)

print('copied:', len(copied))
for item in copied:
    print('  +', item)
print('already here:', len(kept))
