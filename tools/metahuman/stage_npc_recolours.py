"""Stage DeepSeek's reviewed offline garment recolours; native skins and shell leather are unchanged."""
from pathlib import Path as _PipelinePath
import sys as _pipeline_sys
_pipeline_sys.path.insert(0, str(_PipelinePath(__file__).parent))
from pipeline_paths import pipeline_root as _pipeline_root
from pathlib import Path
import json, shutil, hashlib
root=Path((_pipeline_root().as_posix() + ''))
source=Path('C:/Otherreach_Build/npc_variants/assets/recolour')
runtime=root/'runtime'
catalog=json.loads((runtime/'catalog.json').read_text())
variants=['farmer_a','merchant_a','villager_b','farmer_b','elder_a','villager_a','guard_b','guard_c','elder_b','merchant_b']
parts={'Shirt_BaseColor.png':'shirt','Vest_BaseColor.png':'vest','Pants_Leather_baseColor.png':'pants','Hood_BaseColor.png':'hood'}
records=[]
dest=runtime/'wardrobe/recolour';dest.mkdir(parents=True,exist_ok=True)
assert len(catalog['npcs'])==10
for id,variant in zip(catalog['npcs'],variants):
 maps={}
 for original,part in parts.items():
  name=f'{variant}_{part}.png';src=source/name;assert src.is_file(),src
  shutil.copy2(src,dest/name)
  maps[original]=f'wardrobe/recolour/{name}'
  records.append(dict(character=id,variant=variant,part=part,source=str(src),sha256=hashlib.sha256(src.read_bytes()).hexdigest()))
 catalog['characters'][id]['cloth_albedos']=maps
 catalog['characters'][id]['wardrobe_variant']=variant
(runtime/'catalog.json').write_text(json.dumps(catalog,indent=2))
(root/'evidence/npc_recolours.json').write_text(json.dumps(records,indent=2))
print('STAGED_NPC_RECOLOURS',len(records))
