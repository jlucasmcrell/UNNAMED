"""Keep the 'no spawn places a grazer' test pointed at its own subject (the full suite found it red).

The test rewrites the barrowback spawn to name a fellhorn, which unplaces the barrowback and is what the assertion wants. But the fellhorn's band is
[3, 3] and the spawn's is [5, 5], so the mutation also trips CMB001 - and the loader reports that instead of the quest error, so the test never sees
its own subject. The band goes with the species.
"""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\tests\Content.Tests\QuestContentTests.cs')
text = path.read_text(encoding='utf-8')

OLD = ('("spawns/wildlife/foldscar_barrowbacks.yaml", "creature_ref: creature.grazer.barrowback", '
       '"creature_ref: creature.grazer.fellhorn")),')
NEW = ('("spawns/wildlife/foldscar_barrowbacks.yaml", "creature_ref: creature.grazer.barrowback", '
       '"creature_ref: creature.grazer.fellhorn"),\n'
       '            ("spawns/wildlife/foldscar_barrowbacks.yaml", "level_band: [5, 5]", "level_band: [3, 3]")),')

if OLD in text:
    path.write_text(text.replace(OLD, NEW, 1), encoding='utf-8')
    print('the mutated spawn now carries the fellhorn\'s band')
else:
    print('anchor missing')
