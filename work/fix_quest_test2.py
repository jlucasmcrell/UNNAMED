"""The 'no spawn places a grazer' test only edited one of the two barrowback spawns.

`spawns/old_late/brute.yaml` also places a barrowback, so unplacing the herd left the species still placed and the quest error never fired - the test
saw an empty error list. Both placements are edited here, bands included so the mutation stays lint-clean.
"""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\tests\Content.Tests\QuestContentTests.cs')
text = path.read_text(encoding='utf-8')

OLD = ('            ("spawns/wildlife/foldscar_barrowbacks.yaml", "level_band: [5, 5]", "level_band: [3, 3]")),')
NEW = ('            ("spawns/wildlife/foldscar_barrowbacks.yaml", "level_band: [5, 5]", "level_band: [3, 3]"),\n'
       '            ("spawns/old_late/brute.yaml", "creature_ref: creature.grazer.barrowback", "creature_ref: creature.grazer.fellhorn"),\n'
       '            ("spawns/old_late/brute.yaml", "level_band:\\n- 5\\n- 5", "level_band:\\n- 3\\n- 3")),')

if OLD in text:
    path.write_text(text.replace(OLD, NEW, 1), encoding='utf-8')
    print('both barrowback placements are now unplaced by the test')
else:
    print('anchor missing')
