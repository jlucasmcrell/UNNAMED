"""Plan or install the accepted ART3 pieces in a separate Barrowworks workspace.

The default is a cheap metadata-only plan. --install checks the owner flag, verifies
every selected source checksum, then copies without editing the production sources.
Run installation through run_owner_safe.py, which also watches for the owner returning.
This does not rebuild rooms, build native caches, enable the ring, or touch RC d.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct

from run_owner_safe import OWNER_FLAG

FROZEN_RC = Path(r'D:\Otherreach_Scratch\release\20261019d')


def plan(config):
    root = Path(config['art_root'])
    catalogue = {item['key']: item for item in json.loads((root / 'manifest.json').read_text(encoding='utf-8'))}
    approved = json.loads((root / config['dressing_acceptance']).read_text(encoding='utf-8'))
    selected = dict(config['core'])
    for key in config['architecture'] + [item['key'] for item in approved['assets']]:
        # ReleaseModels("bw_codex_") must also evict these pieces on space exit.
        selected.setdefault(key, 'bw_codex_art3_' + key.removeprefix('production_'))
    assets = []
    for key, target in selected.items():
        if key.startswith('production_dressing_r'):
            raise ValueError('Unaccepted room composites must not enter this installer')
        item = catalogue[key]
        lods = sorted(item['lods'], key=lambda lod: lod['level'])
        if [lod['level'] for lod in lods] != [0, 1, 2]:
            raise ValueError(f'{key}: all three supplied LODs are required')
        assets.append({'source_key': key, 'asset_id': target, 'category': item['group'],
                       'lods': lods, 'sources': item.get('sources', []),
                       'source_review_status': item.get('owner_review_ART3', item.get('status'))})
    return {'base_commit': config['base_commit'], 'review': config['review'], 'assets': assets,
            'room_composites_installed': False, 'enable_gates_passed': False}


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def gltf_header(path):
    with path.open('rb') as stream:
        if stream.read(4) != b'glTF':
            raise ValueError(f'Not a GLB: {path}')
        stream.read(8)
        length, kind = struct.unpack('<II', stream.read(8))
        if kind != 0x4E4F534A:
            raise ValueError(f'Missing GLB JSON: {path}')
        return json.loads(stream.read(length))


def owner_check():
    if OWNER_FLAG.exists():
        raise RuntimeError('OWNER_PLAYING: installation not started/continued')


def install(document, destination):
    owner_check()
    destination = destination.resolve()
    frozen = FROZEN_RC.resolve()
    if destination == frozen or frozen in destination.parents:
        raise ValueError('RC d is frozen')
    plan_id = hashlib.sha256(json.dumps(document, sort_keys=True).encode()).hexdigest()
    marker = destination / 'art3_install_owner.json'
    if destination.exists() and (not marker.exists() or json.loads(marker.read_text())['plan_id'] != plan_id):
        raise ValueError('Destination is not owned by this exact ART3 plan')
    # Validate every source before creating the destination. No texture rescaling or
    # geometry/animation conversion: the supplied LOD bytes are copied unchanged.
    for asset in document['assets']:
        for lod in asset['lods']:
            owner_check()
            source = Path(lod['file'])
            if digest(source) != lod['sha256']:
                raise ValueError(f'Source changed since the plan: {source}')
            if asset['source_key'] == 'production_original_walker_detailed':
                header = gltf_header(source)
                clips = {clip['name'].rsplit('.', 1)[-1] for clip in header.get('animations', [])}
                if clips != {'idle', 'walk', 'run', 'hit', 'death', 'sweep', 'fall', 'grasp'}:
                    raise ValueError(f'Custodian clips changed: {clips}')
                if not header.get('skins') or len(header['skins'][0]['joints']) != 11:
                    raise ValueError('Custodian must retain its eleven-joint rig')
    owner_check()
    destination.mkdir(parents=True, exist_ok=True)
    marker.write_text(json.dumps({'plan_id': plan_id}) + '\n', encoding='utf-8')
    for asset in document['assets']:
        asset_id = asset['asset_id']
        if not asset_id.isidentifier() or not asset_id.startswith('bw_'):
            raise ValueError(f'Invalid asset ID: {asset_id}')
        rigged = asset['source_key'] == 'production_original_walker_detailed'
        folder = destination / ('rigged' if rigged else 'ready') / asset_id
        if not folder.resolve().is_relative_to(destination):
            raise ValueError('Asset folder escapes the owned destination')
        folder.mkdir(parents=True, exist_ok=True)
        for lod in asset['lods']:
            owner_check()
            suffix = '_rigged' if rigged and lod['level'] == 0 else '' if lod['level'] == 0 else f"_lod{lod['level']}"
            output = folder / (asset_id + suffix + '.glb')
            if not output.exists() or digest(output) != lod['sha256']:
                temporary = output.with_suffix('.glb.copying')
                shutil.copyfile(lod['file'], temporary)
                if digest(temporary) != lod['sha256']:
                    raise ValueError(f'Copy checksum mismatch: {temporary}')
                os.replace(temporary, output)
        (folder / (asset_id + '_meta.json')).write_text(json.dumps(asset, indent=2) + '\n', encoding='utf-8')
    (destination / 'art3_install_manifest.json').write_text(json.dumps(document, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config', type=Path, default=Path(__file__).with_name('barrowworks_production_assets.json'))
    parser.add_argument('--plan', type=Path, help='Write the selected sources/checksums for review; no source hashing')
    parser.add_argument('--from-plan', type=Path, help='Use an already reviewed, pinned selection')
    parser.add_argument('--install', action='store_true')
    args = parser.parse_args()
    config = json.loads(args.config.read_text(encoding='utf-8'))
    if args.install and not args.from_plan:
        parser.error('--install requires --from-plan; do not silently pick up changed production exports')
    document = json.loads(args.from_plan.read_text(encoding='utf-8')) if args.from_plan else plan(config)
    if args.plan:
        args.plan.write_text(json.dumps(document, indent=2) + '\n', encoding='utf-8')
    print(f"ART3 selected {len(document['assets'])} standalone assets; room composites excluded")
    if args.install:
        install(document, Path(config['destination_workspace']))
        print('ART3 source-verified pieces installed; room assembly/cache/enable gates remain pending')
