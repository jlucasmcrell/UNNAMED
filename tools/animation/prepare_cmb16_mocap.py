"""Import three acquired, recorded CMU performances for source review. Source geometry is never shipped."""
import hashlib
import json
import os
from pathlib import Path
import traceback
import unreal

root = Path(os.environ['OTHERREACH_HUMGEN_ANIMATION_JOB'])
prepared = Path(os.environ.get('OTHERREACH_CMU_CONTROL_PREPARED', str(root.parent/'sources/cmu_control/CMU_SOURCES_PREPARED.json')))
inputs = json.loads(prepared.read_text())
assert inputs['status'] == 'DONE'
state = {'status': 'RUNNING', 'sources': [], 'provenance': inputs.get('scope', '')}

try:
    tools = unreal.AssetToolsHelpers.get_asset_tools()
    for source in inputs['sources']:
        path = Path(source['path'])
        source_hash = hashlib.sha256(path.read_bytes()).hexdigest()
        assert source_hash == source['sha256']
        assert 50 < source['rest_root_height_cm'] < 130
        provider = source['id']
        assert provider.startswith('cmu_control_')
        folder = '/Game/HumGenSources/'+provider
        if not unreal.EditorAssetLibrary.list_assets(folder, recursive=True, include_folder=False):
            task = unreal.AssetImportTask()
            task.filename = str(path);task.destination_path = folder
            task.automated = True;task.save = True
            tools.import_asset_tasks([task])
        assets = []
        for name in unreal.EditorAssetLibrary.list_assets(folder, recursive=True, include_folder=False):
            item = unreal.load_asset(name)
            if isinstance(item, unreal.AnimSequence):
                assets.append({'kind': 'animation', 'asset': item.get_path_name(), 'name': item.get_name(), 'seconds': item.sequence_length})
            elif isinstance(item, unreal.SkeletalMesh):
                assets.append({'kind': 'mesh', 'asset': item.get_path_name()})
        assert any(a['kind'] == 'animation' for a in assets)
        assert hashlib.sha256(path.read_bytes()).hexdigest() == source_hash
        state['sources'].append(dict(source, assets=assets))
    unreal.EditorAssetLibrary.save_directory('/Game/HumGenSources', only_if_is_dirty=True, recursive=True)
    state['status'] = 'DONE'
except Exception:
    state.update(status='FAILED', error=traceback.format_exc())
finally:
    (root/'cmu_sources.json').write_text(json.dumps(state, indent=2))
    unreal.SystemLibrary.quit_editor()
