"""CPU Blender: -- <candidate root> <owned library root> <identifiers ...>.

Use the installed HumGen clothing library and native optimizer, one authored
sex at a time. The content-root preference is changed only inside this owned
background process and restored; Blender user preferences are never saved.
"""
import bpy,json,sys,hashlib
from pathlib import Path
from HumGen3D.human.human import Human
from HumGen3D.backend.preferences.preference_func import get_prefs
from HumGen3D.backend.preview_collections import PREVIEW_COLLECTION_DATA
args=sys.argv[sys.argv.index('--')+1:];root,library_root=map(Path,args[:2])
assert library_root.is_absolute() and library_root.parent==Path(r'E:\Otherreach_Scratch\codex_systems_visuals_20261031\armour')
library_root.mkdir(parents=True,exist_ok=True)
for identifier in args[2:]:
 folder=root/'primary/rigged'/identifier;authoring=root/'sources'/identifier
 contract=json.loads((folder/'creator_part.json').read_text());sex='female' if contract['target_base'].endswith('female') else 'male'
 source=authoring/'native_outfit.blend';source_hash=hashlib.sha256(source.read_bytes()).hexdigest()
 bpy.ops.wm.open_mainfile(filepath=str(source));human=Human.from_existing(bpy.data.objects[contract['target_base']])
 assert human.gender==sex
 clothing=human.clothing.footwear if contract['slot']=='feet' else human.clothing.outfit
 assert len(clothing.objects)==1,'One native modular item/pair per preset'
 category='Otherreach_CMB04_20261031'
 target=library_root/PREVIEW_COLLECTION_DATA[clothing._pcoll_name][2]/sex/category/(identifier+'.blend')
 assert not target.exists(),'Refusing to replace native library artifact'
 children=[];owned=[];prefs=get_prefs();original=prefs.filepath
 try:
  prefs.filepath=str(library_root)
  children=clothing.save_to_library(identifier,for_male=sex=='male',for_female=sex=='female',open_when_finished=False,category=category,context=bpy.context)
  for child in children:
   if child is None:continue
   owned.append(child.pid);assert child.wait(timeout=240)==0,'Native optimizer failed'
 finally:
  prefs.filepath=original
  for child in children:
   if child is not None and child.poll() is None:child.terminate();child.wait(10)
 assert target.is_file() and target.stat().st_size>0
 assert hashlib.sha256(source.read_bytes()).hexdigest()==source_hash
 receipt=dict(method='HumGen clothing.save_to_library; exact authored sex; native optimizer completed',identifier=identifier,target_body_sha256=contract['target_body_sha256'],path=str(target),sha256=hashlib.sha256(target.read_bytes()).hexdigest(),owned_optimizer_pids=owned,source_blend_sha256=source_hash,source_blend_unchanged=True,blender_preferences_saved=False,status='NATIVE_AUTHORING_LIBRARY_NOT_GAME_APPROVAL')
 (authoring/'native_library.json').write_text(json.dumps(receipt,indent=2));print('NATIVE_GAP_LIBRARY_SAVED',identifier,flush=True)
