"""Reuse prepared native DDS entries for byte-matching fallback sources."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil

parser = argparse.ArgumentParser()
parser.add_argument('runtime', type=Path)
args = parser.parse_args()
primary, fallback = args.runtime / 'primary', args.runtime / 'fallback'
source = json.loads((primary / 'texture_cache/manifest.json').read_text(encoding='utf-8-sig'))
manifest = fallback / 'texture_cache/manifest.json'
target = json.loads(manifest.read_text(encoding='utf-8-sig'))
signatures = {}
matched = 0
for key, entry in source['entries'].items():
    if not key.startswith('rigged/humgen_'):
        continue
    file = fallback / key.split('#')[0]
    if not file.is_file() or file.stat().st_size != entry['size']:
        continue
    if file not in signatures:
        with file.open('rb') as stream:
            length = file.stat().st_size
            raw = stream.read(min(65536, length))
            if length > 65536:
                stream.seek(-min(65536, length-len(raw)), 2)
                raw += stream.read()
        signatures[file] = hashlib.sha256(raw).hexdigest()[:24].upper()
    if signatures[file] != entry['sig']:
        continue
    dds = primary / 'texture_cache' / entry['dds']
    copied = fallback / 'texture_cache' / entry['dds']
    if not copied.is_file() or copied.stat().st_size != dds.stat().st_size:
        shutil.copy2(dds, copied)
    target['entries'][key] = entry
    matched += 1
temporary = manifest.with_suffix('.merge.tmp')
temporary.write_text(json.dumps(target, indent=2), encoding='utf-8')
temporary.replace(manifest)
print('Matching native fallback cache entries reused at original resolution:', matched)
