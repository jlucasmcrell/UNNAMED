"""Bind/validate completed native preset bakes without changing source pixels."""
import json
import shutil
import struct
import sys
from pathlib import Path

work = Path(sys.argv[1])
catalogue = json.loads(Path(sys.argv[2]).read_text(encoding='utf-8'))
root = work / 'game/primary/rigged/humgen_creator_materials'
for face in catalogue['faces']:
    folder = root / face['id']
    receipt = folder / 'materials.json'
    data = json.loads(receipt.read_text())
    prefix = face['id'].replace('.', '_')
    for part, entry in data['parts'].items():
        entry['maps'] = {path.stem[len(prefix + '_' + part + '_'):]: path.name for path in folder.glob(prefix + '_' + part + '_*.png')}
        assert entry['maps'], (face['id'], part)
    for part, size in [('Skin', 4096), ('EyesInner', 2048)]:
        path = folder / data['parts'][part]['maps']['BaseColor']
        assert struct.unpack('>II', path.read_bytes()[16:24]) == (size, size), path
    receipt.write_text(json.dumps(data, indent=2))
    thumbnail = Path('D:/HumGen Content') / next(iter(face['paths'].values())).replace('\\', '/')
    if thumbnail.with_suffix('.jpg').is_file(): shutil.copy2(thumbnail.with_suffix('.jpg'), folder / 'thumbnail.jpg')
assert len(catalogue['faces']) == 83
(work / 'material_batch_complete.json').write_text(json.dumps(dict(status='COMPLETE', presets=83,
    gate_b='IN_PROGRESS', note='Native material batch only; not creator acceptance or shipping readiness.'), indent=2))
print('83_NATIVE_MATERIAL_SETS_BOUND_FULL_RESOLUTION', flush=True)
