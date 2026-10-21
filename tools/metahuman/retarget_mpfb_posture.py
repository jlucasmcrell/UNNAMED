"""Offline Epic IK Retargeter -> native GLTFExporter; original MPFB game rig is the target."""
import json, traceback, struct
from pathlib import Path
import unreal

root=Path('D:/Otherreach_Scratch/rcd5_20261021')
report=root/'evidence/ue_mpfb_posture.json'
state=json.loads(report.read_text()) if report.exists() else {'characters':[]}
tools=unreal.AssetToolsHelpers.get_asset_tools()
package='/Game/MHPlaytest/MPFBPosture20261021'
# Measured first-pass sole offsets on the original five game rigs (UE centimetres).
GROUND_OFFSETS={'mpfb_fem_armour':-1.89843215048313,'mpfb_fem_lt':-1.89843215048313,
                'mpfb_male_armour':-2.85560935735703,'mpfb_male_lt':-2.85560935735703,
                'mpfb_var_armour':-2.20576338469982}

def save(**changes):
    if changes.get('status')!='FAILED':state.pop('error',None)
    state.update(changes)
    report.write_text(json.dumps(state,indent=2,default=str))

def asset(path,name,factory):
    return unreal.load_asset(path+'/'+name) or tools.create_asset(name,path,None,factory)

try:
    sources=json.loads(Path('D:/Otherreach_Scratch/rcd3_20261020/evidence/ue_animation_sources.json').read_text())
    source=next(s for s in sources['sources'] if s['id']=='ual1')
    meshes=[s['asset'] for s in source['assets'] if s['kind']=='mesh']
    source_mesh=unreal.load_asset(next((p for p in meshes if 'Mannequin_Body.' in p),meshes[0]))
    source_rig=asset(package,'IK_Source',unreal.IKRigDefinitionFactory())
    source_ctrl=unreal.IKRigController.get_controller(source_rig)
    assert source_ctrl.set_skeletal_mesh(source_mesh)
    source_ctrl.apply_auto_generated_retarget_definition()
    assert source_ctrl.set_root_motion_bone('root')
    clips={}
    for logical,name in [('crouch_idle','Crouch_Idle_Loop'),('crouch_walk','Crouch_Fwd_Loop')]:
        matches=[a for a in source['assets'] if a['kind']=='animation' and a['name'].endswith(name)]
        assert len(matches)==1,(logical,matches)
        clips[logical]=unreal.load_asset(matches[0]['asset'])
    for id in ['mpfb_fem_armour','mpfb_fem_lt','mpfb_male_armour','mpfb_male_lt','mpfb_var_armour']:
        if any(r['id']==id and r.get('complete') and r.get('grounded') for r in state['characters']): continue
        target_path=package+'/'+id
        existing=unreal.EditorAssetLibrary.list_assets(target_path+'/Body',recursive=True,include_folder=False)
        if not existing:
            task=unreal.AssetImportTask()
            task.filename='D:/Otherreach_Scratch/rcd3_20261020/shared_fallback/rigged/'+id+'/'+id+'_rigged.glb'
            task.destination_path=target_path+'/Body';task.automated=True;task.save=True;task.replace_existing=False
            save(status='IMPORTING',current=id)
            tools.import_asset_tasks([task])
        bodies=[unreal.load_asset(p) for p in unreal.EditorAssetLibrary.list_assets(target_path+'/Body',recursive=True,include_folder=False)]
        bodies=[b for b in bodies if isinstance(b,unreal.SkeletalMesh) and b.get_name()=='Human_export_copy']
        assert len(bodies)==1,(id,[str(b) for b in bodies])
        body=bodies[0]
        component=unreal.new_object(unreal.SkeletalMeshComponent);component.set_skeletal_mesh_asset(body)
        bones=[str(component.get_bone_name(i)) for i in range(component.get_num_bones())]
        assert len(bones)==53,(id,bones)
        target_rig=asset(target_path,'IK_Target',unreal.IKRigDefinitionFactory())
        target_ctrl=unreal.IKRigController.get_controller(target_rig)
        assert target_ctrl.set_skeletal_mesh(body)
        target_ctrl.apply_auto_generated_retarget_definition()
        assert target_ctrl.set_retarget_root('pelvis')
        assert target_ctrl.set_root_motion_bone(bones[0])
        excluded=[]
        for chain in list(target_ctrl.get_retarget_chains()):
            start=str(target_ctrl.get_retarget_chain_start_bone(chain.chain_name)).lower()
            if any(f in start for f in ['thumb','index','middle','ring','pinky']):
                excluded.append(str(chain.chain_name));target_ctrl.remove_retarget_chain(chain.chain_name)
        assert len(target_ctrl.get_retarget_chains())>=7,(id,'target not characterized')
        rtg=asset(target_path,'RTG_Posture',unreal.IKRetargetFactory())
        ctrl=unreal.IKRetargeterController.get_controller(rtg)
        ctrl.set_ik_rig(unreal.RetargetSourceOrTarget.SOURCE,source_rig)
        ctrl.set_ik_rig(unreal.RetargetSourceOrTarget.TARGET,target_rig)
        ctrl.set_preview_mesh(unreal.RetargetSourceOrTarget.SOURCE,source_mesh)
        ctrl.set_preview_mesh(unreal.RetargetSourceOrTarget.TARGET,body)
        while ctrl.get_num_retarget_ops()>0:ctrl.remove_retarget_op(ctrl.get_num_retarget_ops()-1)
        ctrl.add_default_ops()
        for i in range(ctrl.get_num_retarget_ops()):ctrl.run_op_initial_setup(i)
        ctrl.auto_map_chains(unreal.AutoMapChainType.FUZZY,True)
        ctrl.auto_align_all_bones(unreal.RetargetSourceOrTarget.TARGET)
        digits=[b for b in bones if any(f in b.lower() for f in ['thumb','index','middle','ring','pinky'])]
        ctrl.reset_retarget_pose(ctrl.get_current_retarget_pose_name(unreal.RetargetSourceOrTarget.TARGET),digits+['foot_l','ball_l','foot_r','ball_r'],unreal.RetargetSourceOrTarget.TARGET)
        ground_offset=GROUND_OFFSETS[id]
        adjusted=False
        for i in range(ctrl.get_num_retarget_ops()):
            operation=ctrl.get_op_controller(i)
            if isinstance(operation,unreal.IKRetargetPelvisMotionController):
                settings=operation.get_settings()
                settings.translation_offset_global=unreal.Vector(0,0,ground_offset)
                settings.affect_ik_vertical=1.0
                operation.set_settings(settings)
                adjusted=True
        assert adjusted,'Pelvis Motion operation missing'
        inputs=unreal.IKRetargetBatchOperationInputs()
        inputs.assets_to_retarget=[unreal.EditorAssetLibrary.find_asset_data(a.get_path_name()) for a in clips.values()]
        inputs.source_mesh=source_mesh;inputs.target_mesh=body;inputs.ik_retarget_asset=rtg
        inputs.target_path=target_path+'/Clips';inputs.prefix=id+'_';inputs.include_referenced_assets=False;inputs.overwrite_existing_files=True
        save(status='RETARGETING',current=id)
        results=[r.get_asset() for r in unreal.IKRetargetBatchOperation.run_batch_retarget(inputs)]
        assert len(results)==2,(id,results)
        rows=[]
        for logical,original in clips.items():
            animation=next(a for a in results if a.get_name()==id+'_'+original.get_name())
            animation.set_preview_skeletal_mesh(body)
            options=unreal.GLTFExportOptions();options.bake_material_inputs=unreal.GLTFMaterialBakeMode.DISABLED
            options.export_vertex_skin_weights=True;options.export_morph_targets=False
            options.export_animation_sequences=True;options.export_preview_mesh=True
            options.export_uniform_scale=.01;options.default_level_of_detail=0;options.use_mesh_quantization=False
            folder=root/'ue_mpfb_posture'/id;folder.mkdir(parents=True,exist_ok=True)
            path=folder/(logical+'.glb')
            task=unreal.AssetExportTask();task.object=animation;task.filename=str(path)
            task.automated=True;task.prompt=False;task.replace_identical=True;task.options=options
            assert unreal.Exporter.run_asset_export_task(task),(id,logical)
            raw=path.read_bytes();g=json.loads(raw[20:20+struct.unpack_from('<I',raw,12)[0]])
            assert g.get('animations') and len(g['skins'][0]['joints'])==53
            rows.append(dict(clip=logical,path=str(path),source=original.get_path_name(),asset=animation.get_path_name(),retargeter=rtg.get_path_name()))
        unreal.EditorAssetLibrary.save_directory(target_path,only_if_is_dirty=True,recursive=True)
        state['characters']=[r for r in state['characters'] if r['id']!=id]
        state['characters'].append(dict(id=id,clips=rows,bones=bones,excluded_finger_chains=excluded,ground_offset_cm=ground_offset,grounded=True,complete=True))
        save(status='CHARACTER_DONE')
    save(status='DONE')
except Exception:
    save(status='FAILED',error=traceback.format_exc())
unreal.SystemLibrary.quit_editor()
