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
mobility = job.parent / 'mobility_sources/inventory.json'
if not os.environ.get('OTHERREACH_GATE_D_SOURCE_INVENTORY') and mobility.exists():
    rows.extend(json.loads(mobility.read_text()))
frames = job.parent / 'source_frames/inventory.json'
if not os.environ.get('OTHERREACH_GATE_D_SOURCE_INVENTORY'):
    assert frames.exists() and (frames.parent / 'verification.json').exists(), 'Source frame preparation is incomplete'
    upright = {row['id']: row for row in json.loads(frames.read_text())}
    rows = [upright.get(row['id'], row) for row in rows]
if os.environ.get('OTHERREACH_GATE_D_SOURCE_LIMIT'):
    rows = rows[:int(os.environ['OTHERREACH_GATE_D_SOURCE_LIMIT'])]
facial_path = job.parent / 'facial_sources/inventory.json'
facial = json.loads(facial_path.read_text()) if facial_path.exists() else []
report = dict(status='IMPORTING', sources=[], failures=[], facial_sources=facial)
previous = {}
if (job / 'extra_sources.json').exists():
    try:
        previous = {row['id']: row for row in json.loads((job / 'extra_sources.json').read_text())['sources']}
    except (ValueError, KeyError):
        pass
tools = unreal.AssetToolsHelpers.get_asset_tools()
selection = json.loads((job / 'selection.json').read_text())
semantic = {
    'blacksmithhammering_humanik_tf1': 'work_smith',
    'chefcooking_mixamo': 'work_cook', 'writing': 'work_write',
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
    'clap_slowclap_hik': 'emote_clap', 'laughs_hik': 'emote_laugh',
}
for row in rows:
    if row['status'] != 'SOURCE_EXPORTED_NOT_RETARGETED':
        if any(face['source'] == row['source'] and face['status'] == 'FACIAL_EXPORTED' for face in facial):
            continue
        report['failures'].append(row)
        continue
    try:
        assert Path(row['path']).is_absolute() and Path(row['path']).is_file(), 'Source transport path must be absolute and exist'
        version = row.get('source_import_version')
        folder = ('/Game/GateDAlignedSceneSourcesV3/' if version else '/Game/GateDSources/') + row['id']
        cached = previous.get(row['id'])
        reusable = cached and cached['path'] == row['path'] and cached.get('source_import_version') == version and all(
            (job / 'project/Content' / (item['asset'].split('.')[0].removeprefix('/Game/') + '.uasset')).is_file()
            for item in cached['assets'])
        if reusable:
            assets = cached['assets']
        else:
            if version:
                # Preserve the acquired sampling rate; automatic glTF import
                # reduced a 100 Hz Rokoko performance to 25 Hz.
                pipeline = unreal.InterchangeGenericAssetsPipeline()
                animation_pipeline = pipeline.get_editor_property('animation_pipeline')
                animation_pipeline.set_editor_property('use30_hz_to_bake_bone_animation', False)
                animation_pipeline.set_editor_property('custom_bone_animation_sample_rate', row['source_sample_rate'])
                # Some acquired FBX takes end between sample boundaries. Extend
                # only that final held sample; never trim their last motion.
                animation_pipeline.set_editor_property('frame_alignment', unreal.InterchangeFrameAlignment.SNAP_TO_CEILING)
                params = unreal.ImportAssetParameters()
                params.is_automated = True
                params.replace_existing = True
                params.override_pipelines = [unreal.SoftObjectPath(pipeline.get_path_name())]
                source_data = unreal.InterchangeManager.create_source_data(row['path'])
                unreal.InterchangeManager.get_interchange_manager_scripted().import_asset(folder, source_data, params)
                unreal.EditorAssetLibrary.save_directory(folder, only_if_is_dirty=True, recursive=True)
            else:
                # A partial report can precede CMU rows after interruption.
                # Its unchanged source transports already exist in this isolated
                # project; do not reimport 2,548 untouched performances.
                existing_cmu = row['id'].startswith('cmu_') and unreal.EditorAssetLibrary.list_assets(folder, True, False)
                if not existing_cmu:
                    task = unreal.AssetImportTask()
                    task.filename = row['path']
                    task.destination_path = folder
                    task.automated = True
                    task.save = True
                    task.replace_existing = True
                    tools.import_asset_tasks([task])
            assets = []
            for path in unreal.EditorAssetLibrary.list_assets(folder, recursive=True, include_folder=False):
                data = unreal.EditorAssetLibrary.find_asset_data(path)
                kind = str(data.asset_class_path.asset_name)
                if kind == 'SkeletalMesh':
                    assets.append(dict(kind='mesh', asset=path))
                elif kind == 'AnimSequence':
                    assets.append(dict(kind='animation', asset=path, name=str(data.asset_name)))
        for item in assets:
            if item['kind'] == 'animation':
                selection['owned_' + row['id'] + '_' + item['name'].lower()] = [row['id'], item['name']]
        assert any(a['kind'] == 'mesh' for a in assets), 'Source preview rig missing'
        assert any(a['kind'] == 'animation' for a in assets), 'Source takes missing'
        takes = [a for a in assets if a['kind'] == 'animation']
        source_name = re.sub('[^a-z0-9]+', '_', Path(row['source']).stem.lower()).strip('_')
        if source_name in semantic and len(takes) == 1:
            selection[semantic[source_name]] = [row['id'], takes[0]['name']]
        if row['id'] == 'cmu_13_23' and len(takes) == 1:
            # CMU's original subject/trial description: sweeping the floor.
            # https://mocap.cs.cmu.edu/search.php?subjectnumber=13&trinum=23
            selection['work_sweep'] = [row['id'], takes[0]['name']]
        report['sources'].append(dict(id=row['id'], path=row['path'], original=row['source'], assets=assets,
                                     source_import_version=version, source_sample_rate=row.get('source_sample_rate'),
                                     source_root_offset_cm=row.get('source_root_offset_cm')))
    except Exception:
        report['failures'].append(dict(source=row['source'], error=traceback.format_exc()))
    (job / 'extra_sources.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    (job / 'selection.json').write_text(json.dumps(selection, indent=2), encoding='utf-8')
catalog = job / 'catalog_sources.json'
if not os.environ.get('OTHERREACH_GATE_D_SOURCE_INVENTORY'):
    assert catalog.exists(), 'Native catalog migration has not finished'
    native = json.loads(catalog.read_text())
    for supplement in job.glob('catalog_sources_*.json'):
        part = json.loads(supplement.read_text())
        assert part['status'] == 'DONE', part.get('error', str(supplement))
        native['sources'].extend(part['sources'])
        native['excluded'].extend(part['excluded'])
    assert native['status'] == 'DONE', native.get('error', 'Native catalog migration incomplete')
    for source in native['sources']:
        source = dict(source, assets=[item for item in source['assets'] if '/Rigs/Poses/' not in item['asset']])
        if not any(item['kind'] == 'animation' for item in source['assets']):
            continue
        for item in source['assets']:
            assert unreal.load_asset(item['asset']) is not None, item['asset']
            if item['kind'] == 'animation':
                state = 'owned_' + re.sub('[^a-z0-9]+', '_', item['asset'].split('.')[0].lower()).strip('_')
                selection[state] = [source['id'], item['asset']]
        report['sources'].append(source)
    report['catalog_exclusions'] = native['excluded']
    report['catalog_exclusions'].extend(dict(asset=item['asset'], reason='Source mannequin corrective calibration, not a performance take')
        for source in native['sources'] for item in source['assets'] if '/Rigs/Poses/' in item['asset'])
report['status'] = 'DONE'
(job / 'extra_sources.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
(job / 'selection.json').write_text(json.dumps(selection, indent=2), encoding='utf-8')
unreal.SystemLibrary.quit_editor()
