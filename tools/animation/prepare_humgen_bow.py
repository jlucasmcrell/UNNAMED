"""Import the unedited CMU bow performance with Epic and inspect its native source rig."""
import json
import os
import traceback
from pathlib import Path
import unreal

root = Path(os.environ['OTHERREACH_HUMGEN_ANIMATION_JOB'])
source = json.loads((root / 'sources/cmu/bow_blender_transport.json').read_text())
state = {'source': source, 'sources': []}
try:
    assert source['status'] == 'DONE'
    tools = unreal.AssetToolsHelpers.get_asset_tools()
    folder = '/Game/HumGenSources/cmu_bow'
    for entry in [source] + source.get('clips', []):
        destination = folder if entry is source else folder + '/' + Path(entry['path']).stem
        if not unreal.EditorAssetLibrary.list_assets(destination, recursive=True, include_folder=False):
            task = unreal.AssetImportTask()
            task.filename = entry['path']
            task.destination_path = destination
            task.automated = True
            task.save = True
            tools.import_asset_tasks([task])
    assets = []
    for path in unreal.EditorAssetLibrary.list_assets(folder, recursive=True, include_folder=False):
        item = unreal.load_asset(path)
        if isinstance(item, unreal.AnimSequence):
            assets.append({'kind': 'animation', 'asset': item.get_path_name(), 'name': item.get_name(), 'seconds': item.sequence_length})
        elif isinstance(item, unreal.SkeletalMesh):
            assets.append({'kind': 'mesh', 'asset': item.get_path_name()})
            rig = unreal.load_asset(folder + '/IK_CMU_Bow') or tools.create_asset('IK_CMU_Bow', folder, None, unreal.IKRigDefinitionFactory())
            controller = unreal.IKRigController.get_controller(rig)
            controller.set_skeletal_mesh(item)
            controller.apply_auto_generated_retarget_definition()
            state['chains'] = [{'name': str(c.chain_name), 'start': str(controller.get_retarget_chain_start_bone(c.chain_name)), 'end': str(controller.get_retarget_chain_end_bone(c.chain_name))} for c in controller.get_retarget_chains()]
            state['retarget_root'] = str(controller.get_retarget_root())
            pose = unreal.AnimPoseExtensions.get_reference_pose(item.get_editor_property('skeleton'))
            state['hips_reference'] = str(unreal.AnimPoseExtensions.get_ref_bone_pose(pose, 'Hips', unreal.AnimPoseSpaces.WORLD))
    assert any(a['kind'] == 'animation' for a in assets)
    state['sources'].append({'id': 'cmu_bow', 'path': source['path'], 'sha256': source['sha256'], 'assets': assets})
    state['root_offset_api'] = unreal.IKRetargeterController.set_root_offset_in_retarget_pose.__doc__
    state['status'] = 'DONE'
    unreal.EditorAssetLibrary.save_directory(folder, only_if_is_dirty=True, recursive=True)
except Exception:
    state.update(status='FAILED', error=traceback.format_exc())
(root / 'bow_epic_source.json').write_text(json.dumps(state, indent=2))
unreal.SystemLibrary.quit_editor()
