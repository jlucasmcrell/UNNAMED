"""Epic Sequencer Bake To Control Rig -> native animation/glTF export.
Only NEW sequence/animation assets in an owned job package are written.
Reference: dev.epicgames.com/documentation/metahuman/animating-metahumans-with-control-rig-in-unreal-engine
"""
import json
import os
from pathlib import Path
import traceback
import unreal

root=Path(os.environ['OTHERREACH_PIPELINE_ROOT'])
job=os.environ.get('OTHERREACH_HAND_CONTROL_JOB','handgrip_controlrig_01')
package='/Game/MHPlaytest/'+job
report=root/'evidence'/(job+'.json')
state=json.loads(report.read_text(encoding='utf-8')) if report.exists() else {'characters':[]}
def save(**values):
    state.update(values);report.write_text(json.dumps(state,indent=2,default=str),encoding='utf-8')
try:
    source_path=Path(os.environ.get('OTHERREACH_HAND_SOURCE_MANIFEST','D:/Otherreach_Scratch/mh_rcd1_owner_20261020/evidence/ue_native_animations_extremities.json'))
    source=json.loads(source_path.read_text())
    blueprint=unreal.load_asset('/MetaHumanCharacter/Common/MetaHuman_ControlRig')
    tools=unreal.AssetToolsHelpers.get_asset_tools()
    world=unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    actors=unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    for id in os.environ.get('OTHERREACH_MH_ANIM_CHARACTERS','ada').split(','):
        if any(c['id']==id and c.get('complete') for c in state['characters']):continue
        original=next(c for c in source['characters'] if c['id']==id)
        body=unreal.load_asset(original['body'])
        source_clip=next(c for c in original['clips'] if c['logical']=='idle')
        animation=unreal.load_asset(source_clip['asset'])
        assert body and animation
        folder=package+'/'+id
        assert not unreal.EditorAssetLibrary.does_asset_exist(folder+'/GripSequence'),'Use a fresh job name'
        sequence=tools.create_asset('GripSequence',folder,unreal.LevelSequence,unreal.LevelSequenceFactoryNew())
        sequence.set_display_rate(unreal.FrameRate(30,1));sequence.set_playback_start(0);sequence.set_playback_end(90)
        actor=actors.spawn_actor_from_class(unreal.SkeletalMeshActor,unreal.Vector(0,0,0))
        component=actor.get_component_by_class(unreal.SkeletalMeshComponent);component.set_skeletal_mesh_asset(body)
        component.set_skinned_asset_and_update(body)
        if os.environ.get('OTHERREACH_HAND_BODY_CORRECTIVES')=='1':
            component.set_forced_lod(1)
            component.set_editor_property('disable_post_process_blueprint',False)
            component.set_editor_property('post_process_anim_bplod_threshold',-1)
            component.set_override_post_process_anim_bp(body.get_editor_property('post_process_anim_blueprint'),True)
        assert component.get_num_bones()==342
        binding=sequence.add_possessable(actor)
        track=binding.add_track(unreal.MovieSceneSkeletalAnimationTrack)
        section=track.add_section();section.set_range(0,90)
        params=unreal.MovieSceneSkeletalAnimationParams();params.animation=animation;section.params=params
        options=unreal.AnimSeqExportOption()
        options.export_morph_targets=False;options.export_attribute_curves=False;options.export_material_curves=False
        options.evaluate_all_skeletal_mesh_components=True;options.record_in_world_space=False
        assert unreal.LevelSequenceEditorBlueprintLibrary.open_level_sequence(sequence)
        if os.environ.get('OTHERREACH_HAND_BODY_CORRECTIVES')=='1':
            unreal.LevelSequenceEditorBlueprintLibrary.set_current_time(0)
            unreal.LevelSequenceEditorBlueprintLibrary.force_update()
            post=component.get_post_process_instance();assert post,'Body post-process not initialized'
            post.set_editor_property('Enable Body Correctives',True)
            assert post.get_editor_property('Enable Body Correctives')
            options.warm_up_frames=unreal.FrameNumber(10)
            options.delay_before_start=unreal.FrameNumber(10)
            save(body_post_process=post.get_class().get_path_name(),body_correctives_enabled=True)
        save(status='BAKING_CONTROL_RIG',current=id)
        assert unreal.ControlRigSequencerLibrary.bake_to_control_rig(world,sequence,blueprint.get_control_rig_class(),options,False,.001,binding,True)
        proxies=unreal.ControlRigSequencerLibrary.get_control_rigs(sequence);assert len(proxies)==1
        rig=proxies[0].control_rig
        # Per-finger values account for differing phalange lengths, rather than
        # imposing the same tight fist on every finger. Only right FK controls.
        curls={'index':[35,70,35],'middle':[35,60,35],'ring':[35,45,30],'pinky':[35,30,25]}
        preset_file=os.environ.get('OTHERREACH_HAND_CONTROL_PRESETS')
        presets=json.loads(Path(preset_file).read_text(encoding='utf-8')) if preset_file else [
            {finger:[angle*strength for angle in angles] for finger,angles in curls.items()}
            for strength in [.8,1.0,1.2]]
        assert len(presets)<=4
        record={'id':id,'body':body.get_path_name(),'sequence':sequence.get_path_name(),'source_idle':source_clip['asset'],'source_manifest':str(source_path),'clips':[]}
        state['characters'].append(record)
        thumb_base=unreal.ControlRigSequencerLibrary.get_local_control_rig_euler_transform(sequence,rig,'thumb_01_r_ctrl',unreal.FrameNumber(0),unreal.MovieSceneTimeUnit.DISPLAY_RATE)
        for index,preset in enumerate(presets):
            thumb_delta=preset.pop('_thumb_delta_yaw',None)
            start=10+index*20
            # Export evaluates/restores Sequencer and may replace its bound rig.
            rig=unreal.ControlRigSequencerLibrary.get_control_rigs(sequence)[0].control_rig
            for finger,angles in preset.items():
                assert finger in ['thumb','index','middle','ring','pinky'] and len(angles)==3
                for joint,angle in enumerate(angles,1):
                    assert 0<=angle<=100
                    name=f'{finger}_0{joint}_r_ctrl'
                    value=unreal.EulerTransform();value.rotation=unreal.Rotator(yaw=-angle);value.scale=unreal.Vector(1,1,1)
                    for frame in [start,start+1,start+2]:
                        unreal.LevelSequenceEditorBlueprintLibrary.set_current_time(frame)
                        unreal.LevelSequenceEditorBlueprintLibrary.force_update()
                        unreal.ControlRigSequencerLibrary.set_local_control_rig_euler_transform(sequence,rig,name,unreal.FrameNumber(frame),value,unreal.MovieSceneTimeUnit.DISPLAY_RATE,True)
                    actual=unreal.ControlRigSequencerLibrary.get_local_control_rig_euler_transform(sequence,rig,name,unreal.FrameNumber(start),unreal.MovieSceneTimeUnit.DISPLAY_RATE)
                    assert abs(actual.rotation.yaw+angle)<.01,(name,str(actual),str(value))
            if thumb_delta is not None:
                value=unreal.EulerTransform();value.location=thumb_base.location;value.scale=thumb_base.scale
                value.rotation=unreal.Rotator(pitch=thumb_base.rotation.pitch,yaw=thumb_base.rotation.yaw+thumb_delta,roll=thumb_base.rotation.roll)
                for frame in [start,start+1,start+2]:
                    unreal.LevelSequenceEditorBlueprintLibrary.set_current_time(frame)
                    unreal.LevelSequenceEditorBlueprintLibrary.force_update()
                    unreal.ControlRigSequencerLibrary.set_local_control_rig_euler_transform(sequence,rig,'thumb_01_r_ctrl',unreal.FrameNumber(frame),value,unreal.MovieSceneTimeUnit.DISPLAY_RATE,True)
                preset['_thumb_delta_yaw']=thumb_delta
            unreal.LevelSequenceEditorBlueprintLibrary.force_update()
            factory=unreal.AnimSequenceFactory();factory.target_skeleton=body.get_editor_property('skeleton');factory.preview_skeletal_mesh=body
            name=f'{id}_Grip_{index}'
            pose=tools.create_asset(name,folder,unreal.AnimSequence,factory)
            options.use_custom_time_range=True;options.custom_start_frame=unreal.FrameNumber(start);options.custom_end_frame=unreal.FrameNumber(start+2)
            options.custom_display_rate=unreal.FrameRate(30,1)
            save(status='EXPORTING_POSE',current=id,current_pose=index)
            assert unreal.SequencerTools.export_anim_sequence(world,sequence,pose,options,binding,False)
            pose.set_preview_skeletal_mesh(body)
            gltf=unreal.GLTFExportOptions();gltf.bake_material_inputs=unreal.GLTFMaterialBakeMode.DISABLED
            gltf.export_vertex_skin_weights=True;gltf.export_morph_targets=False;gltf.export_animation_sequences=True
            gltf.export_preview_mesh=True;gltf.export_uniform_scale=.01;gltf.default_level_of_detail=0;gltf.use_mesh_quantization=False
            output=root/job/id;output.mkdir(parents=True,exist_ok=True);path=output/(name+'.glb')
            task=unreal.AssetExportTask();task.object=pose;task.filename=str(path);task.automated=True;task.prompt=False;task.replace_identical=False;task.options=gltf
            assert unreal.Exporter.run_asset_export_task(task)
            record['clips'].append(dict(logical=f'weapon_grip_controlrig_{index}',path=str(path),asset=pose.get_path_name(),controls=preset,sample_seconds=0.0,route='Epic Sequencer Bake To Control Rig -> per-finger FK controls -> native GLTFExporter'))
            save(status='EXPORTED')
        unreal.EditorAssetLibrary.save_directory(folder,only_if_is_dirty=True,recursive=True)
        actors.destroy_actor(actor);record['complete']=True
    save(status='DONE')
except Exception:
    save(status='FAILED',error=traceback.format_exc())
unreal.SystemLibrary.quit_editor()
