"""Import original game clip GLBs with Epic's native importer; inspect public IK APIs. Run after NPC exports."""
import unreal,json,traceback,hashlib
from pathlib import Path
root=Path('D:/Otherreach_Scratch/mh_playtest_20261019');out=root/'evidence/ue_animation_sources.json'
state=json.loads(out.read_text()) if out.exists() else {'sources':[]}
def save():out.write_text(json.dumps(state,indent=2,default=str))
try:
 exported=json.loads((root/'evidence/npc_material_groom_export.json').read_text())['characters']
 assert len(exported)==10,'NPC exports must finish before animation work'
 source_root=Path('C:/Otherreach_External_Assets/Armory_Pilot_20261019/anim_src')
 kay=source_root/'kaykit/KayKit_Character_Animations_1.1/Animations/gltf/Rig_Medium'
 sources=[('ual1',source_root/'quaternius_ual1/Universal Animation Library[Standard]/Unreal-Godot/UAL1_Standard.glb'),('ual2',source_root/'quaternius_ual2/Universal Animation Library 2[Standard]/Unreal-Godot/UAL2_Standard.glb'),('kaykit_general',kay/'Rig_Medium_General.glb'),('kaykit_move',kay/'Rig_Medium_MovementAdvanced.glb'),('kaykit_melee',kay/'Rig_Medium_CombatMelee.glb'),('kaykit_ranged',kay/'Rig_Medium_CombatRanged.glb')]
 state['api']={}
 for name in ['IKRigController','IKRetargeterController','IKRetargetBatchOperation','IKRetargetBatchOperationInputs','IKRigDefinitionFactory','IKRetargetFactory','GLTFExportOptions']:
  cls=getattr(unreal,name);state['api'][name]=dict(doc=cls.__doc__,members={m:getattr(cls,m).__doc__ for m in dir(cls) if not m.startswith('_') and any(k in m for k in ['retarget','chain','auto','preview','ik_rig','default_op','batch','export'])})
 rig=unreal.load_asset('/MetaHumanCharacter/Animation/Retargeting/IK_MH_IKRig');assert rig
 ctrl=unreal.IKRigController.get_controller(rig)
 state['native_mh_rig']=dict(asset=rig.get_path_name(),pelvis=str(ctrl.get_retarget_root()),chains=[dict(name=str(c.chain_name),start=str(ctrl.get_retarget_chain_start_bone(c.chain_name)),end=str(ctrl.get_retarget_chain_end_bone(c.chain_name)),goal=str(ctrl.get_retarget_chain_goal(c.chain_name))) for c in ctrl.get_retarget_chains()])
 save()
 tools=unreal.AssetToolsHelpers.get_asset_tools()
 for id,source in sources:
  if any(x['id']==id for x in state['sources']):continue
  assert source.exists(),str(source)
  package='/Game/MHPlaytest/AnimationSources/'+id
  state.update(status='IMPORTING',current=id);save()
  existing=unreal.EditorAssetLibrary.list_assets(package,recursive=True,include_folder=False)
  if not existing:
   task=unreal.AssetImportTask();task.filename=str(source);task.destination_path=package;task.automated=True;task.save=True;task.replace_existing=False
   tools.import_asset_tasks([task])
  unreal.EditorAssetLibrary.save_directory(package,only_if_is_dirty=True,recursive=True)
  records=[]
  for path in unreal.EditorAssetLibrary.list_assets(package,recursive=True,include_folder=False):
   asset=unreal.load_asset(path)
   if isinstance(asset,unreal.AnimSequence):records.append(dict(kind='animation',asset=str(path),name=asset.get_name(),seconds=asset.sequence_length,skeleton=str(asset.get_editor_property('skeleton').get_path_name())))
   elif isinstance(asset,unreal.SkeletalMesh):
    component=unreal.new_object(unreal.SkeletalMeshComponent);component.set_skeletal_mesh_asset(asset)
    records.append(dict(kind='mesh',asset=str(path),skeleton=asset.get_editor_property('skeleton').get_path_name(),bones=[dict(name=str(component.get_bone_name(i)),parent=str(component.get_parent_bone(component.get_bone_name(i)))) for i in range(component.get_num_bones())]))
  assert any(x['kind']=='animation' for x in records) and any(x['kind']=='mesh' for x in records),('Native clip import incomplete',id,records)
  state['sources'].append(dict(id=id,path=str(source),sha256=hashlib.sha256(source.read_bytes()).hexdigest(),assets=records));save()
 state['status']='DONE';state.pop('error',None)
except Exception:state.update(status='FAILED',error=traceback.format_exc())
save()
if state.get('status')=='DONE':
 import runpy,os
 os.environ['OTHERREACH_MH_ANIM_PROBE']='1'
 os.environ['OTHERREACH_MH_ANIM_CHARACTERS']='ada'
 runpy.run_path(str(root/'scripts/retarget_native_animations.py'),run_name='__main__')
unreal.SystemLibrary.quit_editor()
