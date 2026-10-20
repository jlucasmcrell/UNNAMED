"""Owner release wardrobe: DevonLux trousers, own-body boots/gloves and supplied NPC variants."""
from pathlib import Path
import json,hashlib,runpy
root=Path('D:/Otherreach_Scratch/mh_playtest_20261019');p=root/'runtime/catalog.json';catalog=json.loads(p.read_text())
source=Path('C:/Otherreach_Build/npc_variants/VARIANTS_FOR_CODEX.json');manifest=json.loads(source.read_text())
variants={name:row for group in manifest['archetypes'].values() for name,row in group.items()}
for id,row in catalog['characters'].items():
 row.pop('trousers_path',None);row.pop('trousers_albedo',None)
 row['hidden_mask']=row['hidden_mask'].replace('_native_trousers','')
 assert (root/'runtime'/row['hidden_mask']).is_file()
 if id in catalog['npcs']:
  parts=variants[row['wardrobe_variant']]['parts'];row['hood']='hood' in parts;row['vest']='vest' in parts
p.write_text(json.dumps(catalog,indent=2))
runpy.run_path(str(Path(__file__).with_name('stage_npc_recolours.py')),run_name='__main__')
(root/'evidence/devonlux_release_wardrobe.json').write_text(json.dumps(dict(owner_decision='2026-10-19 ship DevonLux trousers; body-shell trousers deferred',variant_manifest=str(source),variant_manifest_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),characters=list(catalog['characters']),masks_require_refresh=True),indent=2))
