"""Checkpointed native Creator source export. Run small batches under the GPU guard."""
from pathlib import Path as _PipelinePath
import sys as _pipeline_sys
_pipeline_sys.path.insert(0, str(_PipelinePath(__file__).parent))
from pipeline_paths import pipeline_root as _pipeline_root
import unreal,json,traceback,time,os
from pathlib import Path
root=Path((_pipeline_root().as_posix() + ''))
path=root/'evidence/npc_cloud_export.json'
state=json.loads(path.read_text()) if path.exists() else {'characters':[]}
# A saved phase is written only after the previous native stage and asset save succeeded.
# Recover interrupted batches from that evidence, then checkpoint every native stage.
steps=state.setdefault('steps',{})
previous=state.get('phase','');current=state.get('current')
if current:
 done=steps.setdefault(current,{})
 if previous in ['FULL_RIG_SAVED','REQUEST_4K_TEXTURES','TEXTURES_SAVED','EXPORT_GEOMETRY','GEOMETRY_SAVED','EXPORT_DCC','DCC_SAVED','EXPORT_MATERIALS']:done['rig']=True
 if previous in ['TEXTURES_SAVED','EXPORT_GEOMETRY','GEOMETRY_SAVED','EXPORT_DCC','DCC_SAVED','EXPORT_MATERIALS']:done['textures']=True
 if previous in ['GEOMETRY_SAVED','EXPORT_DCC','DCC_SAVED','EXPORT_MATERIALS']:done['geometry']=True
 if previous in ['DCC_SAVED','EXPORT_MATERIALS']:done['dcc']=True

def save(phase,**kw):
 state.update(phase=phase,updated=time.time(),**kw);path.write_text(json.dumps(state,indent=2));unreal.log('[mh-playtest] '+phase+' '+str(kw))
try:
 prep=json.loads((root/'evidence/npc_characters.json').read_text())
 if prep['status']!='PREPARED':raise RuntimeError('NPC preparation incomplete')
 sub=unreal.get_editor_subsystem(unreal.MetaHumanCharacterEditorSubsystem)
 lib=unreal.MetaHumanCharacterExportBlueprintLibrary
 start=int(os.environ.get('OTHERREACH_MH_NPC_START','0'));count=int(os.environ.get('OTHERREACH_MH_NPC_COUNT','1'))
 for row in prep['characters'][start:start+count]:
  id=row['id']
  if any(c['id']==id for c in state['characters']):continue
  completed=steps.setdefault(id,{})
  character=unreal.load_asset(row['asset'])
  save('EDITING',current=id)
  if not sub.try_add_object_to_edit(character):raise RuntimeError('Cannot edit '+id)
  try:
   if not completed.get('rig'):
    save('REQUEST_FULL_RIG',current=id)
    rp=unreal.MetaHumanCharacterAutoRiggingRequestParams();rp.blocking=True;rp.report_progress=False;rp.rig_type=unreal.MetaHumanRigType.JOINTS_AND_BLEND_SHAPES
    sub.request_auto_rigging(character,rp)
    if not unreal.EditorAssetLibrary.save_loaded_asset(character):raise RuntimeError('Rig asset save failed '+id)
    completed['rig']=True;save('FULL_RIG_SAVED',current=id)
   if not completed.get('textures'):
    save('REQUEST_4K_TEXTURES',current=id)
    tp=unreal.MetaHumanCharacterTextureRequestParams();tp.blocking=True;tp.report_progress=False
    sub.request_texture_sources(character,tp)
    if not character.has_high_resolution_textures:raise RuntimeError('Cloud texture sources incomplete; inspect authentication in UE log')
    if not unreal.EditorAssetLibrary.save_loaded_asset(character):raise RuntimeError('Texture asset save failed '+id)
    completed['textures']=True;save('TEXTURES_SAVED',current=id)
   save('EXPORT_GEOMETRY',current=id)
   base='/Game/MHPlaytest/NPC/'+id
   gp=unreal.MetaHumanGeometryExportParams();gp.project_path=base+'/Source'
   gp.head_skeletal_mesh=True;gp.body_skeletal_mesh=True;gp.full_body_skeletal_mesh=True;gp.overwrite_existing_assets=True
   lib.export_geometry(character,gp)
   unreal.EditorAssetLibrary.save_directory(gp.project_path,only_if_is_dirty=True,recursive=True)
   meshes=[str(p) for p in unreal.EditorAssetLibrary.list_assets(gp.project_path,recursive=True,include_folder=False) if isinstance(unreal.load_asset(p),unreal.SkeletalMesh)]
   if len(meshes)!=3:raise RuntimeError(('Expected native head/body/fullbody',meshes))
   completed['geometry']=True;save('GEOMETRY_SAVED',current=id)
   out=root/'exports'/id;out.mkdir(parents=True,exist_ok=True)
   if not completed.get('dcc'):
    save('EXPORT_DCC',current=id)
    dp=unreal.MetaHumanDCCExportParams();dp.external_path=str(out/'dcc');dp.compress_in_zip_file=False
    lib.export_dcc(character,dp)
    dna=[str(p) for p in (out/'dcc').rglob('*.dna')]
    if len(dna)<2:raise RuntimeError('Native DCC export missing DNA: '+id)
    completed['dcc']=True;save('DCC_SAVED',current=id)
   dna=[str(p) for p in (out/'dcc').rglob('*.dna')]
   save('EXPORT_MATERIALS',current=id)
   mp=unreal.MetaHumanMaterialsExportParams();mp.project_path=base+'/Materials';mp.apply_as_overrides=False
   lib.export_materials(character,mp)
   unreal.EditorAssetLibrary.save_directory(base,only_if_is_dirty=True,recursive=True)
   state['characters'].append(dict(id=id,asset=row['asset'],preset=row['preset'],geometry=meshes,dna=dna,materials=mp.project_path,high_resolution_textures=True))
   save('CHARACTER_SOURCE_EXPORTED',current=id)
  finally:
   if sub.is_object_added_for_editing(character):sub.remove_object_to_edit(character)
 save('BATCH_DONE')
 if os.environ.get('OTHERREACH_MH_NPC_ASSEMBLE')=='1':
  __import__('runpy').run_path(str(Path(__file__).with_name('assemble_npcs.py')))
except Exception:save('FAILED',error=traceback.format_exc())
finally:unreal.SystemLibrary.quit_editor()
