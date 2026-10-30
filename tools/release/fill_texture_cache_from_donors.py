"""Fill a workspace's texture-cache gaps from other caches on disk that already hold a signed BC7/BC6H copy of the same source.

usage: python fill_texture_cache_from_donors.py <root> <gate receipt for root> <out report> <donor texture_cache dir>...
Matches on the source's size + signature (+ image index for a model file), checks the donor DDS header and size against the
gate's rules, COPIES the DDS (never links it) into <root>/texture_cache and writes the manifest entry. Backs the manifest up first.
"""
import hashlib, json, shutil, struct, sys
from pathlib import Path


def signature(file):
    with file.open('rb') as f:
        n = file.stat().st_size
        data = f.read(min(65536, n))
        if n > 65536:
            f.seek(-min(65536, n - 65536), 2)
            data += f.read()
    return hashlib.sha256(data).hexdigest()[:24].upper()


def dds_ok(path, record):
    if not path.is_file():
        return False
    with path.open('rb') as f:
        h = f.read(148)
    if len(h) < 148 or h[:4] != b'DDS ' or h[84:88] != b'DX10':
        return False
    if struct.unpack_from('<I', h, 128)[0] not in ((95, 96) if record['role'] == 'hdr' else (98, 99)):
        return False
    height, width = struct.unpack_from('<II', h, 12)
    return record['width'] <= width <= (record['width'] + 3) // 4 * 4 and record['height'] <= height <= (record['height'] + 3) // 4 * 4


root, receipt, report = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
donors = [Path(d) for d in sys.argv[4:]]
gate = json.loads(receipt.read_text(encoding='utf-8'))
records = {r['key']: r for r in gate['images']}
failing = [f['key'] for f in gate['failures']]
cache = root / 'texture_cache'
manifest_path = cache / 'manifest.json'
doc = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
backup = manifest_path.with_suffix('.json.before_donors')
if not backup.exists():
    shutil.copy2(manifest_path, backup)

index = {}  # (size, sig, image-index suffix) -> [(donor cache dir, entry)]
for d in donors:
    m = d / 'manifest.json'
    if not m.is_file() or d.resolve() == cache.resolve():
        continue
    try:
        entries = json.loads(m.read_text(encoding='utf-8-sig'))['entries']
    except (ValueError, KeyError):
        continue
    for key, e in entries.items():
        if 'sig' in e and 'dds' in e:
            index.setdefault((e['size'], e['sig'], key.partition('#')[2]), []).append((d, e))

restored, unmatched = [], []
for key in failing:
    record = records[key]
    source = root / key.partition('#')[0]
    size, sig = source.stat().st_size, signature(source)
    found = None
    for d, e in index.get((size, sig, key.partition('#')[2]), []):
        if (e['role'] == 'hdr') == (record['role'] == 'hdr') and dds_ok(d / e['dds'], record):
            found = (d, e)
            break
    if not found:
        unmatched.append(key)
        continue
    d, e = found
    src = d / e['dds']
    name = e['dds']
    target = cache / name
    if target.exists() and target.stat().st_size != src.stat().st_size:
        name = 'donor_' + hashlib.sha256(src.read_bytes()).hexdigest()[:24] + '.' + name.split('.', 1)[1]
        target = cache / name
    if not target.exists():
        shutil.copyfile(src, target)
    doc['entries'][key] = {'dds': name, 'size': size, 'sig': sig, 'role': e['role']}
    restored.append({'key': key, 'from': str(src), 'dds': name})

manifest_path.write_text(json.dumps({'comment': doc.get('comment', ''), 'entries': doc['entries']}, indent=2), encoding='utf-8')
report.write_text(json.dumps({'restored': restored, 'unmatched': unmatched}, indent=2), encoding='utf-8')
print(json.dumps({'failing': len(failing), 'restored': len(restored), 'unmatched': len(unmatched), 'examples': unmatched[:10]}))
