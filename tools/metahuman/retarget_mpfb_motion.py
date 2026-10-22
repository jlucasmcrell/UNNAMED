"""Epic IK Retargeter directly from CC0 sources onto each original MPFB rig. No runtime retarget."""
import json, os, traceback, struct, time
from pathlib import Path
import unreal
root=Path(os.environ['OTHERREACH_PIPELINE_ROOT'])
assert root.is_absolute() and root.name=='rcd6_20261021'
inn_mode=os.environ.get('OTHERREACH_INN_CLIPS')=='1'
calm_mode=os.environ.get('OTHERREACH_CALM_ATTACK_CLIPS')=='1'
assert not (inn_mode and calm_mode),'Animation batches must be separate'
report=root/'evidence'/('ue_mpfb_calm.json' if calm_mode else 'ue_mpfb_inn.json' if inn_mode else 'ue_mpfb_motion.json')
state=json.loads(report.read_text(encoding='utf-8')) if report.exists() else {'characters':[]}
tools=unreal.AssetToolsHelpers.get_asset_tools()
package='/Game/MHPlaytest/'+('MPFBCalm20261021' if calm_mode else 'MPFBInn20261021' if inn_mode else 'MPFBMotion20261021')
GROUND_OFFSETS={'mpfb_fem_armour':-1.89843215048313,'mpfb_fem_lt':-1.89843215048313,'mpfb_male_armour':-2.85560935735703,'mpfb_male_lt':-2.85560935735703,'mpfb_var_armour':-2.20576338469982}
def save(**kw):
 if kw.get('status')!='FAILED':state.pop('error',None)
 state.update(kw);report.write_text(json.dumps(state,indent=2,default=str),encoding='utf-8')
def asset(folder,name,factory):
 return unreal.load_asset(folder+'/'+name) or tools.create_asset(name,folder,None,factory)

clips={'idle': ('ual1', 'Idle_Loop'), 'walk': ('ual1', 'Walk_Loop'), 'run': ('ual1', 'Jog_Fwd_Loop'), 'sprint': ('ual1', 'Sprint_Loop'), 'crouch_idle': ('ual1', 'Crouch_Idle_Loop'), 'crouch_walk': ('ual1', 'Crouch_Fwd_Loop'), 'jump_start': ('ual1', 'Jump_Start'), 'fall': ('ual1', 'Jump_Loop'), 'land': ('ual1', 'Jump_Land'), 'sword_ready': ('ual1', 'Sword_Idle'), 'sword_attack_1': ('ual1', 'Sword_Attack'), 'sword_attack_2': ('ual2', 'Sword_Regular_A'), 'sword_attack_3': ('ual2', 'Sword_Regular_B'), 'sword_block': ('ual2', 'Sword_Block'), 'hit': ('ual1', 'Hit_Chest'), 'death': ('ual1', 'Death01'), 'interact': ('ual1', 'Interact'), 'talk': ('ual1', 'Idle_Talking_Loop'), 'cast': ('ual1', 'Spell_Simple_Shoot'), 'spear_ready': ('kaykit_melee', 'Melee_2H_Idle'), 'spear_thrust': ('kaykit_melee', 'Melee_2H_Attack_Stab'), 'bow_ready': ('kaykit_ranged', 'Ranged_Bow_Aiming_Idle'), 'bow_draw': ('kaykit_ranged', 'Ranged_Bow_Draw'), 'bow_release': ('kaykit_ranged', 'Ranged_Bow_Release'), 'pick_up': ('kaykit_general', 'PickUp'), 'dodge': ('kaykit_move', 'Dodge_Backward'), 'strafe_left': ('kaykit_move', 'Running_Strafe_Left'), 'strafe_right': ('kaykit_move', 'Running_Strafe_Right'), 'walk_back': ('kaykit_move', 'Walking_Backwards'), 'idle_foldarms': ('ual2', 'Idle_FoldArms_Loop'), 'idle_shift': ('ual2', 'Idle_No_Loop'), 'idle_look': ('kaykit_general', 'Idle_B')}

try:
 if calm_mode:clips={'sword_attack_1':('kaykit_melee','Melee_1H_Attack_Chop'),'sword_attack_2':('kaykit_melee','Melee_1H_Attack_Slice_Diagonal'),'sword_attack_3':('kaykit_melee','Melee_1H_Attack_Stab')}
 elif inn_mode:clips={'sit_enter':('ual1','Sitting_Enter'),'sit':('ual1','Sitting_Idle_Loop'),'sit_talk':('ual1','Sitting_Talking_Loop'),'sit_exit':('ual1','Sitting_Exit'),'lie_to_idle':('ual2','LayToIdle')}
 sources=json.loads(Path('D:/Otherreach_Scratch/rcd3_20261020/evidence/ue_animation_sources.json').read_text(encoding='utf-8'))
 if os.environ.get('OTHERREACH_MPFB_PROBE')=='1':clips={k:clips[k] for k in ['idle','walk','run','sword_attack_1']}
 selected=os.environ.get('OTHERREACH_MPFB_BODIES',','.join(GROUND_OFFSETS)).split(',')
 rigs={};meshes={};animations={};prefixes={}
 for source in sources['sources']:
  sid=source['id']
  if sid not in {x[0] for x in clips.values()}:continue
  source_meshes=[x['asset'] for x in source['assets'] if x['kind']=='mesh']
  mesh=unreal.load_asset(next((p for p in source_meshes if 'Mannequin_Body.' in p),source_meshes[0]));meshes[sid]=mesh
  rig=asset(package,'IK_Source_'+sid,unreal.IKRigDefinitionFactory());ctrl=unreal.IKRigController.get_controller(rig)
  assert ctrl.set_skeletal_mesh(mesh);ctrl.apply_auto_generated_retarget_definition()
  if sid.startswith('kaykit_'):
   for c in list(ctrl.get_retarget_chains()):ctrl.remove_retarget_chain(c.chain_name)
   ctrl.set_retarget_root('hips')
   for name,start,end in [('Root','root','root'),('Spine','spine','chest'),('Head','head','head'),('LeftArm','upperarm_l','hand_l'),('RightArm','upperarm_r','hand_r'),('LeftLeg','upperleg_l','toes_l'),('RightLeg','upperleg_r','toes_r')]:ctrl.add_retarget_chain(name,start,end,'None')
  assert len(ctrl.get_retarget_chains())>=7
  assert ctrl.set_root_motion_bone('root');rigs[sid]=rig
  prefixes[sid]=Path(source['path']).stem
  animations[sid]={x['name']:unreal.load_asset(x['asset']) for x in source['assets'] if x['kind']=='animation'}
 for id in selected:
  started=time.monotonic();target_path=package+'/'+id
  body=unreal.load_asset('/Game/MHPlaytest/MPFBPosture20261021/'+id+'/Body/'+id+'_rigged/SkeletalMeshes/Human_export_copy.Human_export_copy')
  if body is None:
   existing=unreal.EditorAssetLibrary.list_assets('/Game/MHPlaytest/MPFBPosture20261021/'+id+'/Body',recursive=True,include_folder=False)
   bodies=[unreal.load_asset(p) for p in existing];bodies=[b for b in bodies if isinstance(b,unreal.SkeletalMesh) and b.get_name()=='Human_export_copy'];assert len(bodies)==1,(id,existing);body=bodies[0]
  component=unreal.new_object(unreal.SkeletalMeshComponent);component.set_skeletal_mesh_asset(body)
  bones=[str(component.get_bone_name(i)) for i in range(component.get_num_bones())];assert len(bones)==53
  target=asset(target_path,'IK_Target',unreal.IKRigDefinitionFactory());tc=unreal.IKRigController.get_controller(target)
  assert tc.set_skeletal_mesh(body);tc.apply_auto_generated_retarget_definition();assert tc.set_retarget_root('pelvis');assert tc.set_root_motion_bone(bones[0])
  for c in list(tc.get_retarget_chains()):
   if any(f in str(tc.get_retarget_chain_start_bone(c.chain_name)).lower() for f in ['thumb','index','middle','ring','pinky']):tc.remove_retarget_chain(c.chain_name)
  assert len(tc.get_retarget_chains())>=7
  record=next((c for c in state['characters'] if c['id']==id),None)
  if record is None:record={'id':id,'body':body.get_path_name(),'clips':[]};state['characters'].append(record)
  for sid,source_rig in rigs.items():
   todo={k:name for k,(s,name) in clips.items() if s==sid and not any(c['logical']==k for c in record['clips'])}
   if not todo:continue
   rtg=asset(target_path,'RTG_'+sid,unreal.IKRetargetFactory());ctrl=unreal.IKRetargeterController.get_controller(rtg)
   ctrl.set_ik_rig(unreal.RetargetSourceOrTarget.SOURCE,source_rig);ctrl.set_ik_rig(unreal.RetargetSourceOrTarget.TARGET,target)
   ctrl.set_preview_mesh(unreal.RetargetSourceOrTarget.SOURCE,meshes[sid]);ctrl.set_preview_mesh(unreal.RetargetSourceOrTarget.TARGET,body)
   while ctrl.get_num_retarget_ops()>0:ctrl.remove_retarget_op(ctrl.get_num_retarget_ops()-1)
   ctrl.add_default_ops()
   for i in range(ctrl.get_num_retarget_ops()):ctrl.run_op_initial_setup(i)
   ctrl.auto_map_chains(unreal.AutoMapChainType.FUZZY,True);ctrl.auto_align_all_bones(unreal.RetargetSourceOrTarget.TARGET)
   digits=[b for b in bones if any(f in b.lower() for f in ['thumb','index','middle','ring','pinky'])]
   ctrl.reset_retarget_pose(ctrl.get_current_retarget_pose_name(unreal.RetargetSourceOrTarget.TARGET),digits+['foot_l','ball_l','foot_r','ball_r'],unreal.RetargetSourceOrTarget.TARGET)
   for i in range(ctrl.get_num_retarget_ops()):
    op=ctrl.get_op_controller(i)
    if isinstance(op,unreal.IKRetargetPelvisMotionController):
     settings=op.get_settings();settings.translation_offset_global=unreal.Vector(0,0,GROUND_OFFSETS[id]);settings.affect_ik_vertical=1.0;op.set_settings(settings)
   chosen={}
   for logical,name in todo.items():
    found=[a for n,a in animations[sid].items() if n in [name,prefixes[sid]+name,prefixes[sid]+'_'+name]]
    assert len(found)==1,(sid,logical,[n for n in animations[sid] if name in n]);chosen[logical]=found[0]
   inputs=unreal.IKRetargetBatchOperationInputs();inputs.assets_to_retarget=[unreal.EditorAssetLibrary.find_asset_data(a.get_path_name()) for a in dict.fromkeys(chosen.values())]
   inputs.source_mesh=meshes[sid];inputs.target_mesh=body;inputs.ik_retarget_asset=rtg;inputs.target_path=target_path+'/Clips/'+sid;inputs.prefix=id+'_';inputs.include_referenced_assets=False;inputs.overwrite_existing_files=True
   save(status='RETARGETING',current=id,current_source=sid)
   results=[r.get_asset() for r in unreal.IKRetargetBatchOperation.run_batch_retarget(inputs)];assert len(results)==len(inputs.assets_to_retarget)
   for logical,original in chosen.items():
    animation=next(a for a in results if a.get_name()==id+'_'+original.get_name());animation.set_preview_skeletal_mesh(body)
    options=unreal.GLTFExportOptions();options.bake_material_inputs=unreal.GLTFMaterialBakeMode.DISABLED;options.export_vertex_skin_weights=True;options.export_morph_targets=False;options.export_animation_sequences=True;options.export_preview_mesh=True;options.export_uniform_scale=.01;options.default_level_of_detail=0;options.use_mesh_quantization=False
    folder=root/('mpfb_calm' if calm_mode else 'mpfb_inn' if inn_mode else 'mpfb_motion')/id;folder.mkdir(parents=True,exist_ok=True);path=folder/(logical+'.glb')
    task=unreal.AssetExportTask();task.object=animation;task.filename=str(path);task.automated=True;task.prompt=False;task.replace_identical=True;task.options=options
    assert unreal.Exporter.run_asset_export_task(task)
    raw=path.read_bytes();g=json.loads(raw[20:20+struct.unpack_from('<I',raw,12)[0]]);assert g.get('animations') and len(g['skins'][0]['joints'])==53
    record['clips'].append(dict(logical=logical,path=str(path),source=original.get_path_name(),retargeter=rtg.get_path_name(),seconds=animation.sequence_length));save(status='EXPORTED',current=id,current_clip=logical)
   unreal.EditorAssetLibrary.save_directory(target_path,only_if_is_dirty=True,recursive=True)
  record['complete']=set(clips).issubset({c['logical'] for c in record['clips']});record['minutes']=(time.monotonic()-started)/60;save(status='CHARACTER_DONE')
 save(status='DONE')
except Exception:save(status='FAILED',error=traceback.format_exc())
unreal.SystemLibrary.quit_editor()
