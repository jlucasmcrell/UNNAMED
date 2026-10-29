"""Mark the two native creator bases available to the creator.

CreatorScreen draws the native creator only beside a `<base>/creator_ready.json`, so this is the one step that
makes it visible. It refuses to write that marker unless the payload the FACE and BODY steps need is whole: both
bases with their native inputs, the eye fitter, and all 83 preset texture sets at full resolution. The modular
grooms and garments are offered one by one as their own exports land (CreatorScreen.Installed), so the marker
records which of them this build holds and names the rest rather than pretending they are there.
"""
import json
import re
import struct
import sys
from pathlib import Path

work = Path(sys.argv[1])
catalogue = json.loads(Path(sys.argv[2]).read_text(encoding='utf-8'))
rigged = work / 'game/primary/rigged'
materials = rigged / 'humgen_creator_materials'

missing = []
ready = {}
for sex, base in catalogue['runtime']['bases'].items():
    folder = rigged / base
    for name in (base + '_rigged.glb', 'identity_weights.json', 'eye_fit.json', 'native_materials.json',
                 'cornea_normal.png', 'eye_scatter.png', 'skin_pbr.png'):
        if not (folder / name).is_file():
            missing.append(base + '/' + name)
    motion = work / 'motion' / ('library_' + base + '.json')
    ready[base] = {'sex': sex, 'glb_bytes': (folder / (base + '_rigged.glb')).stat().st_size if (folder / (base + '_rigged.glb')).is_file() else 0,
                   'motion_library': motion.is_file()}

faces = 0
for face in catalogue['faces']:
    folder = materials / face['id']
    receipt = folder / 'materials.json'
    if not receipt.is_file():
        missing.append(face['id'])
        continue
    data = json.loads(receipt.read_text())
    for part, entry in data['parts'].items():
        path = folder / entry['maps'].get('BaseColor', '')
        if not path.is_file():
            missing.append(face['id'] + '/' + part)
        elif part in ('Skin', 'EyesInner'):
            want = 4096 if part == 'Skin' else 2048
            if struct.unpack('>II', path.read_bytes()[16:24]) != (want, want):
                missing.append(f'{face["id"]}/{part} is not {want}px')
    faces += 1
if faces != 83:
    missing.append(f'{faces} of 83 preset material sets')

held, unheld = [], []
for kind, field in (('hair', 'hair'), ('beard', 'facial_hair'), ('outfit', 'outfits'), ('footwear', 'footwear')):
    for option in catalogue[field]:
        for sex in option['paths']:
            identifier = 'humgen_creator_' + sex + '_' + kind + '_' + re.sub(r'[^a-z0-9]+', '_', option['id'].lower()).strip('_')
            (held if (rigged / identifier / 'creator_part.json').is_file() else unheld).append(identifier)
if missing:
    print('NOT_READY:\n  ' + '\n  '.join(missing), flush=True)
    raise SystemExit(1)

record = {'status': 'Facenet materials complete', 'faces': faces, 'bases': ready,
          'modular_parts_held': len(held), 'modular_parts_pending': unheld,
          'note': 'The FACE and BODY steps draw every native preset at full resolution. Modular grooms and garments are '
                  'offered as their own exports land; this marker lists what this build holds.'}
for sex, base in catalogue['runtime']['bases'].items():
    (rigged / base / 'creator_ready.json').write_text(json.dumps(record | {'base': base}, indent=2))
print(f'CREATOR_READY {faces} faces, {len(held)} modular parts held, {len(unheld)} pending', flush=True)
