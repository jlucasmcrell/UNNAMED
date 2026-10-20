"""Native Epic UE Optimized High build retains selectable native face LODs and groom cards.
NPC geometry is selected later with the exporter's native LOD selector, never decimation.
"""
from pathlib import Path as _PipelinePath
import sys as _pipeline_sys
_pipeline_sys.path.insert(0, str(_PipelinePath(__file__).parent))
from pipeline_paths import pipeline_root as _pipeline_root
import unreal,json,traceback,time,os
from pathlib import Path
root=Path((_pipeline_root().as_posix() + ''));path=root/'evidence/npc_assembly.json'
state=json.loads(path.read_text()) if path.exists() else {'characters':[]}
def save(phase,**kw):state.update(phase=phase,updated=time.time(),**kw);path.write_text(json.dumps(state,indent=2))
try:
 prep=json.loads((root/'evidence/npc_characters.json').read_text())
 sub=unreal.get_editor_subsystem(unreal.MetaHumanCharacterEditorSubsystem)
 start=int(os.environ.get('OTHERREACH_MH_NPC_START','0'));count=int(os.environ.get('OTHERREACH_MH_NPC_COUNT','2'))
 for row in prep['characters'][start:start+count]:
  id=row['id']
  if any(c['id']==id for c in state['characters']):continue
  character=unreal.load_asset(row['asset']);save('BUILDING',current=id)
  if not sub.try_add_object_to_edit(character):raise RuntimeError('Cannot edit '+id)
  try:
   if not character.has_high_resolution_textures or not sub.can_build_meta_human(character):raise RuntimeError('Character source not ready '+id)
   params=unreal.MetaHumanCharacterEditorBuildParameters()
   params.pipeline_type=unreal.MetaHumanDefaultPipelineType.OPTIMIZED;params.pipeline_quality=unreal.MetaHumanQualityLevel.HIGH
   params.name_override='MHNPC_'+id;params.absolute_build_path='/Game/MHPlaytest/Assembled'
   sub.build_meta_human(character,params)
   dest='/Game/MHPlaytest/Assembled/MHNPC_'+id
   unreal.EditorAssetLibrary.save_directory(dest,only_if_is_dirty=True,recursive=True)
   assets=[str(p) for p in unreal.EditorAssetLibrary.list_assets(dest,recursive=True,include_folder=False)]
   if not assets:raise RuntimeError('Native assembly produced no assets '+id)
   sk=unreal.get_editor_subsystem(unreal.SkeletalMeshEditorSubsystem)
   meshes=[]
   for p in assets:
    obj=unreal.load_asset(p)
    if isinstance(obj,unreal.SkeletalMesh):meshes.append(dict(path=p,lod_count=sk.get_lod_count(obj)))
   state['characters'].append(dict(id=id,path=dest,assets=assets,meshes=meshes,pipeline='OPTIMIZED HIGH, native LOD selection pending'))
   save('ASSEMBLED',current=id)
  finally:sub.remove_object_to_edit(character)
 save('BATCH_DONE')
 if os.environ.get('OTHERREACH_MH_NPC_ASSEMBLE')=='1':
  __import__('runpy').run_path(str(Path(__file__).with_name('export_npc_glb.py')))
  __import__('runpy').run_path(str(Path(__file__).with_name('export_npc_materials_grooms.py')))
except Exception:save('FAILED',error=traceback.format_exc())
finally:
 if os.environ.get('OTHERREACH_MH_NPC_ASSEMBLE')!='1':unreal.SystemLibrary.quit_editor()
