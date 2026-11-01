"""Verify accepted native character delivery; retain the earlier MetaHuman sealing mode.

HumGen packages carry an accepted_native_character_delivery manifest whose relative
file hashes come from the accepted body/motion receipts, not from the RC candidate.
--verify checks every native body and exact-target motion file in that manifest.

Seal only a new RC assetbase, never a shared/source depot:
  python tools/release/approved_player_bodies.py ASSETBASE --approved-base APPROVED_ASSETBASE
Packaging calls the same command with --verify. Body-region index partitions may be
appended, but the approved vertex, skin, morph and image bytes and node poses stay exact.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct

APPROVED = ('mh_veth_f', 'mh_veth_f_lean', 'mh_veth_m', 'mh_veth_m_lean')
RETIRED = re.compile(r'^(mh_veth_[mf](?:_|$)|pc_veth_|player_veth_|race2_veth)')


def glb(path):
    data = path.read_bytes()
    assert data[:4] == b'glTF', f'Not a GLB: {path}'
    size, kind = struct.unpack_from('<II', data, 12)
    assert kind == 0x4E4F534A
    document = json.loads(data[20:20 + size])
    offset = 20 + size
    length, kind = struct.unpack_from('<II', data, offset)
    assert kind == 0x004E4942
    return document, data[offset + 8:offset + 8 + length]


def poses(document):
    return [{k: n[k] for k in ('name', 'translation', 'rotation', 'scale', 'matrix') if k in n}
            for n in document['nodes']]


def check(base, records):
    for aid in APPROVED:
        record = records[aid]
        path = base / 'primary' / 'rigged' / aid / (aid + '_rigged.glb')
        document, binary = glb(path)
        assert hashlib.sha256(binary[:record['binary_bytes']]).hexdigest() == record['binary_sha256'], f'Unapproved body bytes: {path}'
        assert poses(document)[:len(record['node_poses'])] == record['node_poses'], f'Unapproved body pose: {path}'


def check_native(base, records):
    """Verify the accepted HumGen bodies and exact-target motion, never seal the candidate itself."""
    assert records, 'No accepted native character files'
    for relative, record in records.items():
        path = (base / relative).resolve()
        assert path.is_relative_to(base.resolve()), f'External character file refused: {relative}'
        assert path.is_file(), f'Missing accepted character file: {relative}'
        assert path.stat().st_size == record['bytes'], f'Changed character file size: {relative}'
        with path.open('rb') as stream:
            actual = hashlib.file_digest(stream, 'sha256').hexdigest()
        assert actual == record['sha256'], f'Unapproved character bytes: {relative}'


def check_quality_restoration(base, delivery):
    """Verify source lineage for native-detail restoration; visual acceptance belongs to the played package gate."""
    def inside(relative):
        path = (base / relative).resolve()
        assert path.is_relative_to(base.resolve()) and path.is_file(), f'Missing/external native proof: {relative}'
        return path
    source = inside(delivery['baseline_manifest'])
    assert hashlib.sha256(source.read_bytes()).hexdigest() == delivery['baseline_sha256'], 'Changed source approval manifest'
    baseline = json.loads(source.read_text(encoding='utf-8-sig'))
    assert baseline['kind'] == 'accepted_native_character_delivery', 'Restoration needs the accepted native source lineage'
    files = dict(baseline['files'])
    for model, replacement in delivery['restorations'].items():
        body_source, motion_source = replacement['source_body'], replacement['source_motion']
        proof = json.loads(inside(replacement['rig_proof']).read_text(encoding='utf-8-sig'))
        motion = json.loads(inside(replacement['motion_proof']).read_text(encoding='utf-8-sig'))
        assert proof['old_sha256'] == files[body_source]['sha256'], f'Wrong accepted body source: {model}'
        assert proof['new_sha256'] == delivery['files'][replacement['body']]['sha256'], f'Wrong restored body: {model}'
        assert proof['same_parents'] and proof['joints'] >= 59 and proof['maximum_rest_error'] < 1e-5, f'Native rig changed: {model}'
        assert motion['status'] == 'PASS' and motion['unchanged_keys'] > 0 and motion['states'] >= 49, f'Unproven native motion: {model}'
        assert motion['original_motion_sha256'] == files[motion_source]['sha256'], f'Wrong accepted motion source: {model}'
        final_motion_sha = motion['new_motion_sha256']
        if replacement.get('motion_additions_proof'):
            additions = json.loads(inside(replacement['motion_additions_proof']).read_text(encoding='utf-8-sig'))
            assert additions['status'] == 'PASS' and additions['body_sha256'] == proof['new_sha256'], f'Wrong supplement target: {model}'
            assert additions['original_sha256'] == final_motion_sha, f'Wrong supplement baseline: {model}'
            assert additions['original_states'] == motion['states'] and additions['unchanged_keys'] == motion['unchanged_keys'], f'Existing motion changed by supplement: {model}'
            assert set(additions['added_states']) == {'swim', 'tread'} and additions['states'] == motion['states'] + 2, f'Unexpected motion supplement: {model}'
            final_motion_sha = additions['output_sha256']
        assert final_motion_sha == delivery['files'][replacement['motion']]['sha256'], f'Wrong restored motion: {model}'
        del files[body_source]
        del files[motion_source]
    # Every untouched accepted body/motion remains pinned. Only the proved restoration pairs may change.
    for relative, record in files.items():
        assert delivery['files'].get(relative) == record, f'Unproved character delivery change: {relative}'
    expected = set(files) | {r[key] for r in delivery['restorations'].values() for key in ('body', 'motion')}
    assert set(delivery['files']) == expected, 'Unexpected character files in restoration delivery'
    check_native(base, delivery['files'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('asset_base', type=Path)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--approved-base', type=Path)
    mode.add_argument('--verify', action='store_true')
    args = parser.parse_args()
    base = args.asset_base.resolve(strict=True)
    manifest = base / 'primary' / 'manifests' / 'approved_player_bodies.json'
    if args.approved_base:
        approved = args.approved_base.resolve(strict=True)
        assert base != approved and approved not in base.parents and base not in approved.parents, 'RC and approved source must be separate roots'
        records = {}
        for aid in APPROVED:
            path = approved / 'primary' / 'rigged' / aid / (aid + '_rigged.glb')
            document, binary = glb(path)
            records[aid] = dict(binary_bytes=len(binary), binary_sha256=hashlib.sha256(binary).hexdigest(),
                                source_sha256=hashlib.sha256(path.read_bytes()).hexdigest(), node_poses=poses(document))
    else:
        delivery = json.loads(manifest.read_text())
        if delivery.get('kind') == 'native_quality_restoration_delivery':
            check_quality_restoration(base, delivery)
            print(f"NATIVE_CHARACTER_SOURCE_LINEAGE_VERIFIED {len(delivery['files'])}; packaged visual quality verdict still required")
            return
        if delivery.get('kind') == 'accepted_native_character_delivery':
            check_native(base, delivery['files'])
            print(f"APPROVED_NATIVE_CHARACTER_FILES {len(delivery['files'])}; verified")
            return
        records = delivery['bodies']
    check(base, records)  # Validate all four before removing any retired files.
    retired = []
    for workspace in ('primary', 'fallback'):
        for kind in ('rigged', 'ready'):
            root = base / workspace / kind
            for path in root.glob('*'):
                if RETIRED.match(path.name) and (path.name not in APPROVED or workspace == 'fallback' or kind == 'ready'):
                    assert path.resolve().is_relative_to(base), f'External link refused: {path}'
                    retired.append(path)
    if args.verify:
        assert not retired, f'Retired player files in default paths: {retired}'
    else:
        for path in retired:
            if path.is_dir():
                shutil.rmtree(path)
            else:
                path.unlink()
        manifest.write_text(json.dumps(dict(bodies=records, removed=[str(p.relative_to(base)) for p in retired]), indent=2))
    print(f'APPROVED_PLAYER_BODIES 4; retired default paths {len(retired)}; {"verified" if args.verify else "removed"}')


if __name__ == '__main__':
    main()
