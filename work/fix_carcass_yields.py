"""Carcasses that yield things the pack actually defines, one primary yield each (hunting and skinning hooks).

Phase 1 builds a resource with exactly one yield, and every item_ref has to exist: a moose gives `item.material.small_hide`… no - it gives
deer_hide-sized hide, which the pack calls `item.material.deer_hide`, and the birds give feathers.
"""
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content')

YIELDS = {
    'moose': ('item.material.deer_hide', 3),
    'duck': ('item.material.feathers', 2),
    'crow': ('item.material.feathers', 2),
    'trout': ('item.material.fish_trout', 1),
}

for key, (item, count) in YIELDS.items():
    path = ROOT / 'resources' / f'carcass_{key}.yaml'
    text = path.read_text(encoding='utf-8')
    head = text.split('yields:', 1)[0]
    path.write_text(head + f'yields:\n  - {{ item_ref: {item}, count_range: [1, {count}] }}\n', encoding='utf-8', newline='\n')
    print('rewrote', path.name, '->', item)
