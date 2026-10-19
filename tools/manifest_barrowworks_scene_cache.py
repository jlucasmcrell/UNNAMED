"""Publish source-verified native-cache receipts for the owned enable-work depot.

This hashes large sources: run it through run_owner_safe.py when the owner is away.
Unlike the older asset publisher, this uses the runtime's current cache format.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re

from install_barrowworks_production import FROZEN_RC, owner_check


def digest(path):
    with path.open('rb') as stream:
        result = hashlib.sha256()
        while chunk := stream.read(4 * 1024 * 1024):
            owner_check()
            result.update(chunk)
        return result.hexdigest()


def receipt(depot, version):
    outputs, textures = [], []
    for path in sorted((depot / 'barrowworks_native').glob(f'*/{version}/scenes/**/*.scn')):
        owner_check()
        metadata = json.loads(Path(str(path) + '.source.json').read_text())
        source = Path(metadata['source'])
        stamp = Path(str(path) + '.sha256').read_text()
        if stamp != metadata['stamp'] or not stamp.startswith(version + ' ') or not stamp.endswith(digest(source).upper()):
            raise ValueError(f'Native cache source/stamp mismatch: {source}')
        outputs.append({'file': str(path), 'bytes': path.stat().st_size, 'sha256': digest(path),
                        'source': str(source), 'stamp': stamp})
    for path in sorted((depot / 'barrowworks_native').glob(f'*/{version}/textures/*.dds.res')):
        textures.append({'file': str(path), 'bytes': path.stat().st_size, 'sha256': digest(path)})
    if not outputs or not textures:
        raise ValueError(f'No complete {version} scene/texture cache; build it before publishing receipts')
    return {'cache_format': version, 'raw_sources_committed': False, 'outputs': outputs, 'shared_textures': textures,
            'relocation': 'External texture paths are absolute; rebuild after moving this depot.'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--depot', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    owner_check()
    output = args.output.resolve()
    frozen = FROZEN_RC.resolve()
    if output == frozen or frozen in output.parents:
        parser.error('RC d is frozen')
    runtime = Path(__file__).parents[1] / 'src/Presentation/Art/ArtLibrary.cs'
    version = re.search(r'SceneCacheFormat = "(v\d+)"', runtime.read_text()).group(1)
    document = receipt(args.depot.resolve(), version)
    temporary = output.with_suffix(output.suffix + '.writing')
    temporary.write_text(json.dumps(document, indent=2) + '\n', encoding='utf-8')
    os.replace(temporary, output)
    print(f"NATIVE_CACHE_RECEIPT {version}: {len(document['outputs'])} scenes, {len(document['shared_textures'])} textures")
