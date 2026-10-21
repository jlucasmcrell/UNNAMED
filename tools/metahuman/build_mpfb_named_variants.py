"""MPFB documented detail targets/MHCLO fitting/Export Copy; original 53-bone rigs retained."""
import bpy, addon_utils, json, os, sys, runpy, hashlib, time, traceback, shutil
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parent))
from pipeline_paths import pipeline_root
root=pipeline_root()/'mpfb_named'
root.mkdir(parents=True,exist_ok=True)
if not (root/'recipes.json').exists():(root/'recipes.json').write_bytes(Path(__file__).with_name('mpfb_named_recipes.json').read_bytes())
recipes=json.loads((root/'recipes.json').read_text(encoding='utf-8'))
selected=os.environ.get('OTHERREACH_MPFB_VARIANTS','').split(',')
if selected!=['']:recipes=[r for r in recipes if r['id'] in selected]
report=root/'build.json';state=json.loads(report.read_text(encoding='utf-8')) if report.exists() else {'characters':[]}
def save(**kw):
 state.update(kw);report.write_text(json.dumps(state,indent=2,default=str),encoding='utf-8')
addon_utils.enable('bl_ext.user_default.mpfb',default_set=True)
from bl_ext.user_default.mpfb.services import LocationService,ObjectService,HumanService,TargetService,ClothesService
mh=Path('D:/Otherreach_Scratch/mpfb_slice_20261019/mh_data')
prefs=bpy.context.preferences.addons['bl_ext.user_default.mpfb'].preferences;prefs.mh_user_data=str(mh);LocationService.update_mh_data()
targets=Path(LocationService.get_mpfb_data())/'targets'
def signature(rig):return [(b.name,b.parent.name if b.parent else None,[v for row in b.matrix_local for v in row]) for b in rig.data.bones]
try:
 for recipe in recipes:
  if any(r['id']==recipe['id'] and r.get('complete') for r in state['characters']):continue
  started=time.monotonic();source=Path('D:/Otherreach_Scratch/mpfb_slice_20261019/blends')/(recipe['base']+'.blend')
  bpy.ops.wm.open_mainfile(filepath=str(source));addon_utils.enable('bl_ext.user_default.mpfb',default_set=True)
  body=next(o for o in bpy.data.objects if o.type=='MESH' and ObjectService.object_is_basemesh(o))
  rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');before=signature(rig);assert len(before)==53
  for name,weight in recipe['targets'].items():
   found=list(targets.glob('*/'+name+'.target.gz')) or list(targets.glob('*/'+name+'.target'));assert len(found)==1,(name,found)
   key=TargetService.filename_to_shapekey_name(str(found[0]))
   existing=body.data.shape_keys.key_blocks.get(key) if body.data.shape_keys else None
   if existing:existing.value=weight
   else:TargetService.load_target(body,str(found[0]),weight=weight)
  # Refit proxy geometry only. Face detail does not require changing the original game-engine skeleton.
  for mesh in list(ObjectService.find_related_mesh_assets(rig,only_children=True)):
   if ObjectService.get_object_type(mesh)=='Hair':bpy.data.objects.remove(mesh,do_unlink=True)
   else:ClothesService.fit_clothes_to_human(mesh,body,set_parent=False)
  hair_dir=mh/'hair'/recipe['hair'];hair_path=next(hair_dir.glob('*.mhclo'))
  assert 'released as CC0' in hair_path.read_text(encoding='utf-8')
  hair=HumanService.add_mhclo_asset(str(hair_path),body,asset_type='Hair',subdiv_levels=0,material_type='MAKESKIN',import_subrig=False)
  assert before==signature(rig),'Variant creation changed skeleton rests'
  blend=root/'blends'/(recipe['id']+'.blend');blend.parent.mkdir(parents=True,exist_ok=True);bpy.ops.wm.save_as_mainfile(filepath=str(blend))
  output=root/'glb'/recipe['id']/(recipe['id']+'_rigged.glb');output.parent.mkdir(parents=True,exist_ok=True)
  export_report=root/(recipe['id']+'_export.json')
  sys.argv=['export_gltf.py','--','--blend',str(blend),'--outglb',str(output),'--report',str(export_report)]
  runpy.run_path(str(Path(__file__).with_name('export_mpfb_named_copy.py')),run_name='__main__')
  # Publish only our new variant directories, before the target-hash library is staged.
  target=root.parent/'shared_fallback'/'rigged'/recipe['id'];target.mkdir(parents=True,exist_ok=True)
  assert not target.is_junction() and not target.is_symlink() and target.resolve().is_relative_to(root.parent.resolve()),target
  shutil.copy2(output,target/output.name)
  assert hashlib.sha256(output.read_bytes()).digest()==hashlib.sha256((target/output.name).read_bytes()).digest()
  row=dict(recipe,complete=True,path=str(output),source=str(source),source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),sha256=hashlib.sha256(output.read_bytes()).hexdigest(),minutes=(time.monotonic()-started)/60,skeleton_before=before,original_skeleton_unchanged=True,hair_license='CC0 MakeHuman system assets, Data Collection AB/Joel Palmius/Jonas Hauquier')
  state['characters'].append(row);save(status='CHARACTER_DONE',current=recipe['id'])
 save(status='DONE')
except Exception:
 save(status='FAILED',error=traceback.format_exc());raise
