"""Give the four new species a body to skin, on the same model the deer already uses (hunting and skinning hooks).

Every creature the hunter kills leaves a carcass when its definition names one (`carcass_ref`), and the carcass node is what a skinner harvests
with skill.skinning. Moose, duck, crow and trout were added with a loot table but no carcass, so killing them gave loot and nothing to skin.
"""
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content')
NODES = ROOT / 'nodes'
RESOURCES = ROOT / 'resources'

# What each species gives up on the table, and the carcass node that carries it.
SPECIES = {
    'moose': ('creature.beast.moose', 'Moose Carcass', 'A bull moose down in the wet ground: hide, meat and a deal of bone, and more than one '
                                                'carrying trip out of it.', 'item.material.hide', 3, 8, 'loot.moose'),
    'duck': ('creature.beast.duck', 'Duck Carcass', 'A mallard in the reeds - a small body, a small meal, feathers worth keeping.',
             'item.material.feathers', 1, 2, 'loot.fowl'),
    'crow': ('creature.beast.crow', 'Crow Carcass', 'A crow where it fell: black feathers, little meat, and the old use for the feathers.',
             'item.material.feathers', 1, 2, 'loot.fowl'),
    'trout': ('creature.fish.trout', 'Trout Carcass', 'A trout landed or speared - the fish itself is the harvest.',
              'item.material.fish_trout', 1, 1, 'loot.trout'),
}

NODE_TEXT = """id: node.carcass.{key}
kind: node
schema: 1
display_key: node.carcass.{key}.name
tags: [node]
name: {name}
notes: "{note}"
resource_ref: resource.carcass.{key}
charges: {charges}
respawn: none
harvest_skill: skill.skinning
difficulty: {difficulty}
tier: 1
"""

RESOURCE_TEXT = """id: resource.carcass.{key}
kind: resource
schema: 1
display_key: resource.carcass.{key}.name
tags: [resource]
name: {name}
notes: "The body a {key} leaves behind; what it yields is what it carried."
yields:
  - {{ item_ref: {primary}, count_range: [1, {primary_count}] }}
  - {{ item_ref: item.material.meat_raw, count_range: [1, 2] }}
"""

for key, (creature, name, note, primary, primary_count, difficulty, _) in SPECIES.items():
    node = NODES / f'carcass_{key}.yaml'
    resource = RESOURCES / f'carcass_{key}.yaml'
    if not node.exists():
        node.write_text(NODE_TEXT.format(key=key, name=name, note=note, charges=2, difficulty=difficulty),
                        encoding='utf-8', newline='\n')
        print('wrote', node.name)
    if not resource.exists():
        resource.write_text(RESOURCE_TEXT.format(key=key, name=name, primary=primary, primary_count=primary_count),
                            encoding='utf-8', newline='\n')
        print('wrote', resource.name)

    creature_path = next(ROOT.glob(f'creatures/**/{creature.rsplit(".", 1)[-1]}.yaml'), None)
    if creature_path is None:
        print('  no creature file for', creature)
        continue
    text = creature_path.read_text(encoding='utf-8')
    if 'carcass_ref:' in text:
        continue
    text = text.replace('loot_table:', f'carcass_ref: node.carcass.{key}\nloot_table:', 1)
    creature_path.write_text(text, encoding='utf-8')
    print('  bound', creature, 'to node.carcass.' + key)
