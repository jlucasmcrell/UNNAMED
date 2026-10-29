"""Import original-rig owned motion transports into this lane's isolated Epic project."""
import json
import os
from pathlib import Path
import traceback
import re
import unreal

job = Path(os.environ['OTHERREACH_HUMGEN_ANIMATION_JOB'])
rows = json.loads(Path(os.environ.get('OTHERREACH_GATE_D_SOURCE_INVENTORY', str(job.parent / 'sources/inventory.json'))).read_text())
cmu = job.parent / 'cmu_sources/inventory.json'
if not os.environ.get('OTHERREACH_GATE_D_SOURCE_INVENTORY') and cmu.exists():
    rows.extend(json.loads(cmu.read_text()))
if os.environ.get('OTHERREACH_GATE_D_SOURCE_LIMIT'):
    rows = rows[:int(os.environ['OTHERREACH_GATE_D_SOURCE_LIMIT'])]
report = dict(status='IMPORTING', sources=[], failures=[])
tools = unreal.AssetToolsHelpers.get_asset_tools()
selection = json.loads((job / 'selection.json').read_text())
semantic = {
    'blacksmithhammering_humanik_tf1': 'work_smith',
    'chefcooking_mixamo': 'work_cook', 'writing': 'work_read',
    'digging': 'work_mine', 'working_on_device': 'work_craft',
    'drinking': 'drink', 'carrying': 'work_carry',
    'talking_1': 'talk_1', 'talking_2': 'talk_2', 'talking_3': 'talk_3',
    'nervously_look_around': 'idle_nervous', 'breathing_idle': 'idle_breathe',
    'waving_gesture': 'emote_wave', 'beckoning': 'emote_beckon',
    'pointing_gesture': 'emote_point', 'praying': 'emote_pray',
    'sitting_drinking': 'sit_drink', 'laying_sleeping': 'sleep',
    'great_sword_slash_1': 'greatsword_attack_1', 'great_sword_slash_2': 'greatsword_attack_2',
    'great_sword_blocking': 'greatsword_block',
    'sword_and_shield_slash_1': 'shield_sword_attack_1', 'sword_and_shield_slash_2': 'shield_sword_attack_2',
    'sword_and_shield_block': 'shield_sword_block', 'sword_and_shield_block_idle': 'shield_sword_ready',
}
for row in rows:
    if row['status'] != 'SOURCE_EXPORTED_NOT_RETARGETED':
        report['failures'].append(row)
        continue
    try:
        assert Path(row['path']).is_absolute() and Path(row['path']).is_file(), 'Source transport path must be absolute and exist'
        folder = '/Game/GateDSources/' + row['id']
        if not unreal.EditorAssetLibrary.list_assets(folder, recursive=True, include_folder=False):
            task = unreal.AssetImportTask()
            task.filename = row['path']
            task.destination_path = folder
            task.automated = True
            task.save = True
            tools.import_asset_tasks([task])
        assets = []
        for path in unreal.EditorAssetLibrary.list_assets(folder, recursive=True, include_folder=False):
            asset = unreal.load_asset(path)
            if isinstance(asset, unreal.SkeletalMesh):
                assets.append(dict(kind='mesh', asset=asset.get_path_name()))
            elif isinstance(asset, unreal.AnimSequence):
                assets.append(dict(kind='animation', asset=asset.get_path_name(), name=asset.get_name()))
                selection['owned_' + row['id'] + '_' + asset.get_name().lower()] = [row['id'], asset.get_name()]
        assert any(a['kind'] == 'mesh' for a in assets), 'Source preview rig missing'
        assert any(a['kind'] == 'animation' for a in assets), 'Source takes missing'
        takes = [a for a in assets if a['kind'] == 'animation']
        source_name = re.sub('[^a-z0-9]+', '_', Path(row['source']).stem.lower()).strip('_')
        if source_name in semantic and len(takes) == 1:
            selection[semantic[source_name]] = [row['id'], takes[0]['name']]
        report['sources'].append(dict(id=row['id'], path=row['path'], original=row['source'], assets=assets,
                                     source_root_offset_cm=row.get('source_root_offset_cm')))
    except Exception:
        report['failures'].append(dict(source=row['source'], error=traceback.format_exc()))
    (job / 'extra_sources.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    (job / 'selection.json').write_text(json.dumps(selection, indent=2), encoding='utf-8')
report['status'] = 'DONE'
(job / 'extra_sources.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
unreal.SystemLibrary.quit_editor()
