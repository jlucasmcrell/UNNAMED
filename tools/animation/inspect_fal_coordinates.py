import json, os, traceback
from pathlib import Path
import unreal

root = Path(os.environ['OTHERREACH_HUMGEN_ANIMATION_JOB'])
result = {}
try:
    p = '/Game/FreeAnimationLibrary/Animations/Walk/anim_Walk_Left_Loop.anim_Walk_Left_Loop'
    a = unreal.load_asset(p)
    skeleton = a.get_editor_property('skeleton')
    result['skeleton'] = skeleton.get_path_name()
    result['previews'] = {}
    for obj in [a, skeleton]:
        for prop in ['preview_skeletal_mesh', 'preview_mesh', 'retarget_source', 'retarget_source_asset']:
            try:
                result['previews'][obj.get_class().get_name() + ':' + prop] = str(obj.get_editor_property(prop))
            except Exception as e:
                result['previews'][obj.get_class().get_name() + ':' + prop] = str(e)
    mesh = unreal.load_asset('/Game/FreeAnimationLibrary/Demo/Characters/Mannequins/Meshes/SKM_Manny_Simple')
    result['mesh_skeleton'] = mesh.get_editor_property('skeleton').get_path_name()
    result['additive'] = str(a.get_editor_property('additive_anim_type'))
    result['pose_api'] = unreal.AnimPoseExtensions.get_bone_pose.__doc__
    options = unreal.AnimPoseEvaluationOptions()
    pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(a, .4, options)
    result['native_pose'] = {b: str(unreal.AnimPoseExtensions.get_bone_pose(pose, b, unreal.AnimPoseSpaces.WORLD)) for b in ['root', 'pelvis', 'clavicle_l', 'clavicle_r', 'head']}
    controller = unreal.IKRetargeterController.get_controller(unreal.load_asset('/Game/HumGenMotion/ProductionAdaMotionProbe/RTG_fal'))
    result['pose'] = {b: str(controller.get_rotation_offset_for_retarget_pose_bone(b, unreal.RetargetSourceOrTarget.TARGET))
                      for b in ['Root', 'Hips', 'Spine', 'UpperChest', 'LeftShoulder', 'LeftUpperArm']}
    result['ops'] = [str(controller.get_op_controller(i).get_class().get_name()) for i in range(controller.get_num_retarget_ops())]
except Exception:
    result['error'] = traceback.format_exc()
(root / 'fal_coordinates.json').write_text(json.dumps(result, indent=2))
unreal.SystemLibrary.quit_editor()
