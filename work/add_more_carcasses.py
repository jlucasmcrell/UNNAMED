"""Every huntable species leaves a body worth skinning (hunting and skinning hooks, all of them).

The deer pair had a carcass; elk, rabbit and red fox had a loot table and nothing to skin. Each gets its own node with the yield its body carries,
on the same pattern as the deer's.
"""
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content')

SPECIES = {
    'elk': ('creature.beast.elk', 'Elk Carcass', 'A bull elk down on the moor - heavy hide, a deal of meat, and antlers worth the carrying.',
            'item.material.deer_hide', 4, 7),
    'rabbit': ('creature.beast.rabbit', 'Rabbit Carcass', 'A rabbit in the grass: small hide, a small meal, and quick work.',
               'item.material.small_hide', 1, 3),
    'red_fox': ('creature.beast.red_fox', 'Fox Carcass', 'A dog fox where it fell - a pelt worth more than the meat, in the old trade.',
                'item.material.alpha_pelt', 1, 6),
}

NODE = """id: node.carcass.{key}
kind: node
schema: 1
display_key: node.carcass.{key}.name
tags: [node]
name: {name}
notes: "{note}"
resource_ref: resource.carcass.{key}
charges: 2
respawn: none
harvest_skill: skill.skinning
difficulty: {difficulty}
tier: 1
"""

RESOURCE = """id: resource.carcass.{key}
kind: resource
schema: 1
display_key: resource.carcass.{key}.name
tags: [resource]
name: {name}
notes: "The body a {key} leaves behind; what it yields is what it carried."
yields:
  - {{ item_ref: {primary}, count_range: [1, {count}] }}
"""

for key, (creature, name, note, primary, count, difficulty) in SPECIES.items():
    node = ROOT / 'nodes' / f'carcass_{key}.yaml'
    resource = ROOT / 'resources' / f'carcass_{key}.yaml'
    if not node.exists():
        node.write_text(NODE.format(key=key, name=name, note=note, difficulty=difficulty), encoding='utf-8', newline='\n')
    if not resource.exists():
        resource.write_text(RESOURCE.format(key=key, name=name, primary=primary, count=count), encoding='utf-8', newline='\n')
    creature_path = next(ROOT.glob(f'creatures/**/{creature.rsplit(".", 1)[-1]}.yaml'), None)
    if creature_path is None:
        continue
    text = creature_path.read_text(encoding='utf-8')
    if 'carcass_ref:' in text:
        print('  already', creature)
        continue
    creature_path.write_text(text.replace('loot_table:', f'carcass_ref: node.carcass.{key}\nloot_table:', 1), encoding='utf-8')
    print('  bound', creature, 'to node.carcass.' + key)
