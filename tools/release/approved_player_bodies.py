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
