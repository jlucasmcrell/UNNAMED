"""Stage the two creator bases through existing native LOD/material adapters.

Never marks creator_ready: native motion, grooms, people looks and all material
sets must be complete and proved before that production-availability marker.
"""
import hashlib
import json
import shutil
import struct
import subprocess
import sys
from pathlib import Path
from keep_native_eye_radius import keep_radius

ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / 'work/creator'
ART = WORK / 'game/primary/rigged'
# Native teeth/tongue atlases (HumGenMouth.Apply) ship beside the rigs.
mouth = ART / 'humgen_mouth'
mouth.mkdir(parents=True, exist_ok=True)
for map_name in ('TeethTongueSet_C_2K.png', 'TeethTongueSet_N_2K.png', 'TeethTongueSet_ROUGH_2K.png'):
    shutil.copy2(Path('D:/HumGen Content/textures/teeth') / map_name, mouth / map_name)
for sex in ('female', 'male'):
    source = WORK / sex
    record = json.loads((source / 'export_receipt.json').read_text(encoding='utf-8'))
    assert record['source_unchanged']
    identifier = 'humgen_creator_' + sex
    folder = ART / identifier
    folder.mkdir(parents=True, exist_ok=True)
    body = folder / (identifier + '_rigged.glb')
    subprocess.run([sys.executable, str(ROOT / 'tools/animation/fix_humgen_lod_materials.py'), str(source / (identifier + '.glb')), str(body)], check=True)
    if (source / 'eye_fit_source.json').is_file(): keep_radius(body, source / 'eye_fit_source.json')
    with body.open('rb') as stream:
        stream.seek(12)
        length, _ = struct.unpack('<II', stream.read(8))
        gltf = json.loads(stream.read(length))
    weights = {}
    for node in gltf['nodes']:
        if 'mesh' not in node: continue
        mesh = gltf['meshes'][node['mesh']]
        names = mesh.get('extras', {}).get('targetNames', [])
        values = node.get('weights', mesh.get('weights', [0] * len(names)))
        if names: weights[node['name']] = [dict(name=n, value=v) for n, v in zip(names, values)]
    (folder / 'identity_weights.json').write_text(json.dumps(dict(target_body_sha256=hashlib.sha256(body.read_bytes()).hexdigest(), meshes=weights), indent=2))
    audit = json.loads((source / 'source_audit.json').read_text())
    metadata = {}
    for key, needle in [('skin_pbr', 'rough_spec_cavity'), ('cornea_normal', 'Eye_Outer_Normal'), ('eye_scatter', 'Eye_Scatter')]:
        image = next(i for i in audit['images'] if needle.lower() in i['name'].lower())
        shutil.copy2(image['filepath'], folder / (key + '.png'))
        metadata[key] = key + '.png'
    inner = next(n for n in audit['materials']['.HG_Eyes_Inner']['nodes'] if n['name'] == 'Principled BSDF')['inputs']
    metadata.update(skin_sss_weight=1, eye_roughness=inner['Roughness'], eye_metallic=inner['Metallic'])
    (folder / 'native_materials.json').write_text(json.dumps(metadata, indent=2))
    print('STAGED_CREATOR_BASE', identifier, 'meshes with retained keys', len(weights), flush=True)

# Native filenames are cleaned by Namer. Repair completed receipts from the
# first bake iteration without touching any source pixels or repeating a bake.
catalogue = json.loads((ROOT / 'src/Presentation/Art/humgen_creator.json').read_text(encoding='utf-8'))
for face in catalogue['faces']:
    folder = ART / 'humgen_creator_materials' / face['id']
    receipt = folder / 'materials.json'
    if not receipt.is_file(): continue
    data = json.loads(receipt.read_text())
    prefix = face['id'].replace('.', '_')
    for part, entry in data['parts'].items():
        entry['maps'] = {p.stem[len(prefix + '_' + part + '_'):]: p.name for p in folder.glob(prefix + '_' + part + '_*.png')}
        assert entry['maps'], (face['id'], part)
    receipt.write_text(json.dumps(data, indent=2))
    thumbnail = Path('D:/HumGen Content') / next(iter(face['paths'].values())).replace('\\', '/')
    if thumbnail.with_suffix('.jpg').is_file(): shutil.copy2(thumbnail.with_suffix('.jpg'), folder / 'thumbnail.jpg')
