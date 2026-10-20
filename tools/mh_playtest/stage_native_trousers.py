"""Swap only accepted shell garments/materials in the runtime catalog, then require fresh posed masks."""
from pathlib import Path
import json,hashlib,shutil
import numpy as np
from PIL import Image
root=Path('D:/Otherreach_Scratch/mh_playtest_20261019');runtime=root/'runtime'
catalog=json.loads((runtime/'catalog.json').read_text())
assert len(catalog['characters'])==12
records=[]
for id,c in catalog['characters'].items():
 assert c.get('animation_library') and (runtime/c['animation_library']).exists(),'Finish animation staging before catalog swap'
 evidence=json.loads((root/'evidence'/('trousers_'+id+'.json')).read_text())
 assert evidence['status']=='DONE' and evidence['native_rest_and_body_preserved']
 assert evidence['source_body_sha256']==hashlib.sha256((runtime/c['body']).read_bytes()).hexdigest()
 assert (runtime/'wardrobe/shell'/(id+'_trousers.glb')).is_file()
 c['trousers_path']='shell/'+id+'_trousers.glb'
 old=runtime/c['hidden_mask']
 if not old.stem.endswith('_native_trousers'):
  new=old.with_name(old.stem+'_native_trousers.png')
  shutil.copy2(old,new) # seed only so actor configuration can run; final real-renderer bake is mandatory
  c['hidden_mask']=str(new.relative_to(runtime)).replace('\\','/')
 records.append(dict(id=id,garment=c['trousers_path'],native_body_sha256=evidence['source_body_sha256'],final_mask_required=c['hidden_mask']))
# Period wool colour choices from DeepSeek's reviewed multi-garment variant table.
colors={'brown':[.30,.23,.16],'grey':[.32,.33,.34],'green':[.25,.30,.20],'charcoal':[.16,.17,.18]}
wool={'villager_a':'brown','villager_b':'grey','farmer_a':'brown','farmer_b':'green','elder_a':'charcoal','elder_b':'brown','guard_a':'charcoal','guard_b':'charcoal','guard_c':'brown','merchant_a':'grey','merchant_b':'grey'}
source=Path('C:/Otherreach_External_Assets/BodyShell_Footwear_20261019/tex/trouser_wool_albedo_1k.jpg')
pixels=np.asarray(Image.open(source).convert('RGB'),dtype=np.float32)/255
luminance=pixels@np.array([.2126,.7152,.0722],dtype=np.float32)
shade=np.clip(luminance/max(float(luminance.mean()),.001),.25,1.8)
folder=runtime/'wardrobe/recolour';folder.mkdir(parents=True,exist_ok=True)
for family,color in colors.items():
 rgb=np.clip(shade[...,None]*np.array(color)*255,0,255).astype(np.uint8)
 Image.fromarray(rgb).save(folder/('shell_wool_'+family+'.png'))
for id in catalog['npcs']:
 c=catalog['characters'][id];c['trousers_albedo']='wardrobe/recolour/shell_wool_'+wool[c['wardrobe_variant']]+'.png'
(runtime/'catalog.json').write_text(json.dumps(catalog,indent=2))
(root/'evidence/native_trouser_stage.json').write_text(json.dumps(dict(status='STAGED_REQUIRES_POSED_MASK_BAKE_AND_VISUAL_GATE',characters=records,cloth_source=str(source),cloth_source_sha256=hashlib.sha256(source.read_bytes()).hexdigest()),indent=2))
print('NATIVE_TROUSERS_STAGED_REQUIRES_POSED_MASK_BAKE',len(records))
