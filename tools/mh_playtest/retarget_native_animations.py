"""Epic IK Rig/Retargeter batch retarget, then native GLTFExporter. No skeleton/rest edits."""
import unreal,json,traceback,os,struct
from pathlib import Path
root=Path('D:/Otherreach_Scratch/mh_playtest_20261019')
out=root/'evidence/ue_native_animations.json'
state=json.loads(out.read_text()) if out.exists() else {'characters':[]}
def save(**kw):
 if kw.get('status')!='FAILED':state.pop('error',None)
 state.update(kw);out.write_text(json.dumps(state,indent=2,default=str))
def chains(controller):
 return [dict(name=str(c.chain_name),start=str(controller.get_retarget_chain_start_bone(c.chain_name)),end=str(controller.get_retarget_chain_end_bone(c.chain_name))) for c in controller.get_retarget_chains()]
def asset(package,name,factory):
 path=package+'/'+name
 return unreal.load_asset(path) if unreal.EditorAssetLibrary.does_asset_exist(path) else unreal.AssetToolsHelpers.get_asset_tools().create_asset(name,package,None,factory)
try:
 sources=json.loads((root/'evidence/ue_animation_sources.json').read_text());assert sources['status']=='DONE'
 # Original in-place game clips; aliases are only names, never synthesized motion.
 clips={
 'idle':('ual1','Idle_Loop'),'walk':('ual1','Walk_Loop'),'run':('ual1','Jog_Fwd_Loop'),'sprint':('ual1','Sprint_Loop'),
 'crouch_idle':('ual1','Crouch_Idle_Loop'),'crouch_walk':('ual1','Crouch_Fwd_Loop'),
 'jump_start':('ual1','Jump_Start'),'fall':('ual1','Jump_Loop'),'land':('ual1','Jump_Land'),
 'sword_ready':('ual1','Sword_Idle'),'sword_attack_1':('ual1','Sword_Attack'),
 'sword_attack_2':('ual2','Sword_Regular_A'),'sword_attack_3':('ual2','Sword_Regular_B'),
 'sword_block':('ual2','Sword_Block'),'hit':('ual1','Hit_Chest'),'death':('ual1','Death01'),
 'interact':('ual1','Interact'),'talk':('ual1','Idle_Talking_Loop'),
 'cast':('ual1','Spell_Simple_Shoot'),
 'spear_ready':('kaykit_melee','Melee_2H_Idle'),'spear_thrust':('kaykit_melee','Melee_2H_Attack_Stab'),
 'bow_ready':('kaykit_ranged','Ranged_Bow_Aiming_Idle'),'bow_draw':('kaykit_ranged','Ranged_Bow_Draw'),
 'bow_release':('kaykit_ranged','Ranged_Bow_Release'),'pick_up':('kaykit_general','PickUp'),
 'dodge':('kaykit_move','Dodge_Backward')}
 if os.environ.get('OTHERREACH_MH_ANIM_PROBE')=='1':clips={k:clips[k] for k in ['idle','walk','sword_attack_1']}
 catalog=json.loads((root/'runtime/catalog.json').read_text())
 npc=json.loads((root/'evidence/npc_glb_export.json').read_text())
 selected=os.environ.get('OTHERREACH_MH_ANIM_CHARACTERS','ada').split(',')
 tools=unreal.AssetToolsHelpers.get_asset_tools();package='/Game/MHPlaytest/Retarget'
 rig_sources={};mesh_sources={};animations={};source_prefixes={}
 for source in sources['sources']:
  id=source['id']
  if id not in {x[0] for x in clips.values()}:continue
  source_meshes=[x['asset'] for x in source['assets'] if x['kind']=='mesh']
  mesh=unreal.load_asset(next((p for p in source_meshes if 'Mannequin_Body.' in p),source_meshes[0]))
  mesh_sources[id]=mesh
  rig=asset(package,'IK_Source_'+id,unreal.IKRigDefinitionFactory())
  ctrl=unreal.IKRigController.get_controller(rig);assert ctrl.set_skeletal_mesh(mesh)
  auto=ctrl.apply_auto_generated_retarget_definition()
  if id.startswith('kaykit_'):
   # Epic's documented custom IK Rig chain configuration for the original KayKit rig.
   # This defines retarget chains only; source and target skeletons/rests stay untouched.
   for chain in list(ctrl.get_retarget_chains()):ctrl.remove_retarget_chain(chain.chain_name)
   ctrl.set_retarget_root('hips')
   for name,start,end in [('Root','root','root'),('Spine','spine','chest'),('Head','head','head'),('LeftArm','upperarm_l','hand_l'),('RightArm','upperarm_r','hand_r'),('LeftLeg','upperleg_l','toes_l'),('RightLeg','upperleg_r','toes_r')]:
    ctrl.add_retarget_chain(name,start,end,'None')
  assert len(ctrl.get_retarget_chains())>=7,('Source template not recognized',id,str(auto),chains(ctrl))
  unreal.EditorAssetLibrary.save_asset(rig.get_path_name())
  rig_sources[id]=rig
  animations[id]={x['name']:unreal.load_asset(x['asset']) for x in source['assets'] if x['kind']=='animation'}
  source_prefixes[id]=Path(source['path']).stem
  save(status='SOURCE_RIG',source=id,source_chains=chains(ctrl))
 for id in selected:
  body=unreal.load_asset('/Game/MHSlice/Final/'+('Feminine' if id=='ada' else 'Masculine')+'/Source/MHC_Slice_'+('Feminine' if id=='ada' else 'Masculine')+'_Body') if id in ['ada','mateo'] else unreal.load_asset(next(x['asset'] for c in npc['characters'] if c['id']==id for x in c['exports'] if x['part']=='body'))
  assert body,id
  target_path=package+'/'+id;target_rig_path=target_path+'/IK_Target'
  target_rig=unreal.load_asset(target_rig_path) if unreal.EditorAssetLibrary.does_asset_exist(target_rig_path) else tools.duplicate_asset('IK_Target',target_path,unreal.load_asset('/MetaHumanCharacter/Animation/Retargeting/IK_MH_IKRig'))
  target_ctrl=unreal.IKRigController.get_controller(target_rig);assert target_ctrl.set_skeletal_mesh(body)
  record=next((c for c in state['characters'] if c['id']==id),None)
  if record is None:record=dict(id=id,body=body.get_path_name(),clips=[]);state['characters'].append(record)
  for source_id,source_rig in rig_sources.items():
   todo={k:name for k,(s,name) in clips.items() if s==source_id and not any(c['logical']==k for c in record['clips'])}
   if not todo:continue
   rtg=asset(target_path,'RTG_'+source_id,unreal.IKRetargetFactory())
   ctrl=unreal.IKRetargeterController.get_controller(rtg)
   ctrl.set_ik_rig(unreal.RetargetSourceOrTarget.SOURCE,source_rig)
   ctrl.set_ik_rig(unreal.RetargetSourceOrTarget.TARGET,target_rig)
   ctrl.set_preview_mesh(unreal.RetargetSourceOrTarget.SOURCE,mesh_sources[source_id])
   ctrl.set_preview_mesh(unreal.RetargetSourceOrTarget.TARGET,body)
   ctrl.add_default_ops();ctrl.auto_map_chains(unreal.AutoMapChainType.FUZZY,True)
   ctrl.auto_align_all_bones(unreal.RetargetSourceOrTarget.TARGET)
   unreal.EditorAssetLibrary.save_asset(rtg.get_path_name())
   selected_anims={}
   for logical,name in todo.items():
    matches=[a for n,a in animations[source_id].items() if n in [name,source_prefixes[source_id]+name,source_prefixes[source_id]+'_'+name]]
    assert len(matches)==1,('Source clip ambiguity',name,list(animations[source_id]))
    selected_anims[logical]=matches[0]
   inputs=unreal.IKRetargetBatchOperationInputs()
   inputs.assets_to_retarget=[unreal.EditorAssetLibrary.find_asset_data(a.get_path_name()) for a in dict.fromkeys(selected_anims.values())]
   inputs.source_mesh=mesh_sources[source_id];inputs.target_mesh=body;inputs.ik_retarget_asset=rtg
   inputs.target_path=target_path+'/Clips/'+source_id;inputs.prefix=id+'_';inputs.include_referenced_assets=False;inputs.overwrite_existing_files=True
   save(status='RETARGETING',current=id,current_source=source_id)
   results=unreal.IKRetargetBatchOperation.run_batch_retarget(inputs)
   assert len(results)==len(inputs.assets_to_retarget),('Epic batch incomplete',len(results),len(inputs.assets_to_retarget))
   result_assets=[r.get_asset() for r in results]
   for logical,source_anim in selected_anims.items():
    matches=[a for a in result_assets if a.get_name()==id+'_'+source_anim.get_name()]
    assert len(matches)==1,('Unexpected batch naming',logical,[a.get_name() for a in result_assets])
    anim=matches[0];anim.set_preview_skeletal_mesh(body)
    opts=unreal.GLTFExportOptions();opts.bake_material_inputs=unreal.GLTFMaterialBakeMode.DISABLED
    opts.export_vertex_skin_weights=True;opts.export_morph_targets=False;opts.export_animation_sequences=True;opts.export_preview_mesh=True
    opts.export_uniform_scale=.01;opts.default_level_of_detail=catalog['characters'][id].get('body_lod',0);opts.use_mesh_quantization=False
    folder=root/'animations/epic'/id;folder.mkdir(parents=True,exist_ok=True)
    path=folder/(logical+'.glb')
    task=unreal.AssetExportTask();task.object=anim;task.filename=str(path);task.automated=True;task.prompt=False;task.replace_identical=True;task.options=opts
    assert unreal.Exporter.run_asset_export_task(task),('Native animation export failed',logical)
    raw=path.read_bytes();g=json.loads(raw[20:20+struct.unpack_from('<I',raw,12)[0]])
    assert [len(s['joints']) for s in g['skins']]==[342] and g.get('animations'),('Native skeleton contract failed',logical)
    record['clips'].append(dict(logical=logical,source=source_anim.get_path_name(),retargeter=rtg.get_path_name(),asset=anim.get_path_name(),path=str(path),seconds=anim.sequence_length,channels=len(g['animations'][0]['channels'])))
    unreal.EditorAssetLibrary.save_asset(anim.get_path_name());save(status='EXPORTED',current=id,current_clip=logical)
  unreal.EditorAssetLibrary.save_directory(target_path,only_if_is_dirty=True,recursive=True)
  record['complete']=set(clips).issubset({c['logical'] for c in record['clips']});save(status='CHARACTER_DONE',current=id)
 state.pop('error',None);save(status='DONE')
except Exception:save(status='FAILED',error=traceback.format_exc())
unreal.SystemLibrary.quit_editor()
