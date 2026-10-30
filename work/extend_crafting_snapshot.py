"""Add the seven carcass nodes this branch introduced to the crafting snapshot (the full suite caught the omission)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\tests\Content.Tests\CraftingContentTests.cs')
text = path.read_text(encoding='utf-8')

OLD = '"node.carcass.boar", "node.carcass.deer", "node.carcass.grey_wolf",'
NEW = ('"node.carcass.boar", "node.carcass.crow", "node.carcass.deer", "node.carcass.duck", "node.carcass.elk", '
       '"node.carcass.grey_wolf", "node.carcass.moose", "node.carcass.rabbit", "node.carcass.red_fox", "node.carcass.trout",')

if OLD in text:
    path.write_text(text.replace(OLD, NEW, 1), encoding='utf-8')
    print('node snapshot extended with the seven carcass nodes')
else:
    print('anchor missing; nothing changed')
