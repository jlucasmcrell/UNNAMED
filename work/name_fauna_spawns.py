"""Name the two new spawns the way the snapshot already expects them, and give the roster its four species (region fauna)."""
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')

RENAMES = {
    'content/spawns/wildlife/charwood_crows.yaml': ('spawn.cairnwold.charwood_crows', 'spawn.hollow.charwood_crows'),
    'content/spawns/wildlife/foldscar_moose.yaml': ('spawn.foldscar.moose', 'spawn.cairnwold.foldscar_moose'),
}
for relative, (old, new) in RENAMES.items():
    path = ROOT / relative
    text = path.read_text(encoding='utf-8')
    if old in text:
        path.write_text(text.replace(old, new), encoding='utf-8')
        print('renamed', old, '->', new)

combat = ROOT / 'tests' / 'Content.Tests' / 'CombatContentTests.cs'
code = combat.read_text(encoding='utf-8')
code = code.replace('Assert.Equal(50, spawns.Count);', 'Assert.Equal(54, spawns.Count);', 1)
for species, before in (('creature.beast.crow', '"creature.beast.chicken"'),
                        ('creature.beast.duck', '"creature.beast.doe"'),
                        ('creature.beast.moose', '"creature.beast.hound_pup"'),
                        ('creature.fish.trout', '"creature.grazer.barrowback"')):
    if f'"{species}"' not in code:
        anchor = f'{before},'
        code = code.replace(anchor, f'{before}, "{species}",', 1)
combat.write_text(code, encoding='utf-8')
print('roster and spawn count updated')
