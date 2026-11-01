"""Stage a full native character export privately, retaining its exact accepted bone contract.

No source art or shared roots are modified. Motion is repackaged only after native
rest equivalence is proved; animation keys and timing remain byte-for-byte values.
"""
import argparse
import hashlib
import io
import json
import re
import shutil
import struct
import subprocess
import sys
from pathlib import Path
import numpy as np
from PIL import Image


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def glb(path):
    raw = path.read_bytes()
    length = struct.unpack_from('<I', raw, 12)[0]
    return json.loads(raw[20:20 + length]), memoryview(raw)[28 + length:]


def bones(doc):
    parents = {child: i for i, node in enumerate(doc['nodes']) for child in node.get('children', [])}
    def transform(index):
        node = doc['nodes'][index]
        if 'matrix' in node:
            matrix = np.asarray(node['matrix']).reshape(4, 4).T
        else:
            x, y, z, w = node.get('rotation', [0, 0, 0, 1])
            matrix = np.eye(4)
            matrix[:3, :3] = [[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
                              [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                              [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]]
            matrix[:3, :3] *= np.asarray(node.get('scale', [1, 1, 1]))[None, :]
            matrix[:3, 3] = node.get('translation', [0, 0, 0])
        return matrix
    joints = set(doc['skins'][0]['joints'])
    result = {}
    for joint in joints:
        matrix = transform(joint)
        parent = parents.get(joint)
        # Scene/armature container names are not bone parents. Preserve and
        # compare their transforms, folding them into the nearest bone rest.
        while parent is not None and parent not in joints:
            matrix = transform(parent) @ matrix
            parent = parents.get(parent)
        result[doc['nodes'][joint]['name']] = (doc['nodes'][parent]['name'] if parent is not None else None, matrix)
    return result


def texture_contract(doc, binary, folder):
    """Native map dimensions before runtime quality selection, including loose PBR/flow inputs."""
    manifest = folder / 'native_materials.json'
    metadata = json.loads(manifest.read_text()) if manifest.exists() else {}
    textures = []
    for node in doc['nodes']:
        if 'mesh' not in node: continue
        for index, primitive in enumerate(doc['meshes'][node['mesh']]['primitives']):
            material = doc['materials'][primitive['material']]
            pbr = material.get('pbrMetallicRoughness', {})
            cornea = '_Cornea_LOD0' in node['name']
            for role, texture in ([] if cornea else [('color', pbr.get('baseColorTexture')), ('roughness', pbr.get('metallicRoughnessTexture')), ('normal', material.get('normalTexture'))]):
                if not texture: continue
                image = doc['images'][doc['textures'][texture['index']]['source']]
                view = doc['bufferViews'][image['bufferView']]
                start = view.get('byteOffset', 0)
                with Image.open(io.BytesIO(binary[start:start + view['byteLength']])) as picture:
                    textures.append({'mesh': node['name'], 'surface': index, 'role': role, 'width': picture.width, 'height': picture.height})
            extras = []
            if material['name'].endswith('_Skin'): extras.append(('skin_pbr', 'native_specular'))
            if cornea: extras.append(('cornea_normal', 'normal'))
            if node['name'].endswith('_Eyes_LOD0'): extras.append(('eye_scatter', 'scatter'))
            if 'HairCards' in material['name']:
                level = re.search(r'_LOD([0-9]+)$', node['name'])
                key = ('face_hair_flow' if 'FaceHairCards' in material['name'] else 'hair_flow')
                if level and level[1] != '0': key += '_lod' + level[1]
                if key not in metadata and 'FaceHairCards' in material['name']:
                    key = 'hair_flow' + ('_lod' + level[1] if level and level[1] != '0' else '')
                extras.append((key, 'flow'))
            for extra, role in extras:
                if extra not in metadata: continue
                with Image.open(folder / metadata[extra]) as picture:
                    textures.append({'mesh': node['name'], 'surface': index, 'role': role, 'width': picture.width, 'height': picture.height})
    return textures


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('export', type=Path)
    parser.add_argument('old_body', type=Path)
    parser.add_argument('old_motion', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('id')
    parser.add_argument('--godot', type=Path, required=True)
    parser.add_argument('--keep-people-art', action='store_true', help='Retain accepted Kal/Siann side maps and native wings')
    args = parser.parse_args()
    if not re.fullmatch(r'humgen_[a-z0-9_]+', args.id):
        raise ValueError('Invalid runtime character ID')
    settings = json.loads((args.export / 'export_settings.json').read_text())
    assert all(settings['lods'][0][part] == 'original' for part in ('body', 'clothing', 'eyes', 'teeth'))
    assert settings['lods'][0]['haircards'] == 'ultra' and settings['shape_keys']['lod0_only'] is False
    audit = json.loads((args.export / 'source_audit.json').read_text())
    receipt = json.loads((args.export / 'native_export_receipt.json').read_text())
    assert receipt['ok'], receipt
    name = audit['recipe']['id']
    source = args.export / (name + '.glb')
    folder = args.output / 'primary/rigged' / args.id
    folder.mkdir(parents=True, exist_ok=True)
    body = folder / (args.id + '_rigged.glb')
    assert not body.exists(), 'Never overwrite a staged candidate'
    subprocess.run([sys.executable, str(Path(__file__).with_name('fix_humgen_lod_materials.py')), str(source), str(body)], check=True)
    doc, binary = glb(body)
    old = bones(glb(args.old_body)[0])
    new = bones(doc)
    assert old.keys() == new.keys(), 'Native bone vocabulary changed'
    assert all(old[n][0] == new[n][0] for n in old), 'Native bone parents changed'
    maximum = max(float(np.max(np.abs(old[n][1] - new[n][1]))) for n in old)
    assert maximum < 1e-5, ('Native local rests changed', maximum)
    equivalence = {'old_body': str(args.old_body), 'new_body': str(body), 'old_sha256': digest(args.old_body), 'new_sha256': digest(body),
                   'joints': len(new), 'maximum_rest_error': maximum, 'same_parents': True}
    (folder / 'rig_equivalence.json').write_text(json.dumps(equivalence, indent=2))
    weights = {}
    for node in doc['nodes']:
        if 'mesh' not in node: continue
        mesh = doc['meshes'][node['mesh']]
        names = mesh.get('extras', {}).get('targetNames', [])
        values = node.get('weights', mesh.get('weights', [0] * len(names)))
        if names: weights[node['name']] = [dict(name=n, value=v) for n, v in zip(names, values)]
    (folder / 'identity_weights.json').write_text(json.dumps({'target_body_sha256': digest(body), 'meshes': weights}, indent=2))
    metadata = {}
    for key, needle in [('skin_pbr', 'rough_spec_cavity'), ('cornea_normal', 'Eye_Outer_Normal'), ('eye_scatter', 'Eye_Scatter')]:
        image = next(i for i in audit['images'] if needle.lower() in i['name'].lower())
        original = Path(image['filepath'])
        target = folder / (key + original.suffix)
        shutil.copy2(original, target)
        assert digest(original) == digest(target)
        metadata[key] = target.name
    inner = next(v for k, v in audit['materials'].items() if 'Eyes_Inner' in k)
    bsdf = next(n['inputs'] for n in inner['nodes'] if n['type'] == 'ShaderNodeBsdfPrincipled')
    metadata.update(skin_sss_weight=1, eye_roughness=bsdf['Roughness'], eye_metallic=bsdf['Metallic'])
    for flow in args.export.glob('*HairCards_Flow*.png'):
        level = re.search(r'_LOD([0-9]+)', flow.stem)
        key = ('face_hair_flow' if 'FaceHairCards' in flow.name else 'hair_flow') + ('_lod' + level[1] if level else '')
        shutil.copy2(flow, folder / flow.name)
        metadata[key] = flow.name
    (folder / 'native_materials.json').write_text(json.dumps(metadata, indent=2))
    if args.keep_people_art:
        for old_file in args.old_body.parent.iterdir():
            if old_file.name in {'kal_stone_rgbh.png', 'kal_stone_rm.png', 'kal_stone.json', 'kal_wings.glb',
                                  'siann_skin.png', 'siann_skin.json', 'orenth_scar_mask.png'}:
                shutil.copy2(old_file, folder / old_file.name)
                assert digest(old_file) == digest(folder / old_file.name)
    textures = texture_contract(doc, binary, folder)
    proof = {'native_lod0_original': True, 'native_hair_ultra': True, 'native_eyes_separate': any('_Cornea_LOD0' in n.get('name', '') for n in doc['nodes']),
             'source_export_sha256': digest(source), 'source_audit_sha256': digest(args.export / 'source_audit.json'), 'rig': equivalence}
    assert proof['native_eyes_separate'], 'Native eye layers were discarded'
    (folder / 'native_quality.json').write_text(json.dumps(proof, indent=2))
    motion = args.output / 'humgen_characters/mocap_motion' / (args.id + '_motion.res')
    motion.parent.mkdir(parents=True, exist_ok=True)
    plan = {'old_motion': str(args.old_motion), 'new_motion': str(motion), 'proof': str(folder / 'rig_equivalence.json')}
    plan_path = folder / 'motion_repackage_plan.json'
    plan_path.write_text(json.dumps(plan, indent=2))
    subprocess.run([str(args.godot), '--headless', '--path', str(Path(__file__).parents[1] / 'humgen/minimal'),
                    '--script', str(Path(__file__).with_name('repackage_equivalent_character.gd')), '--', str(plan_path)], check=True, timeout=180)
    pinned = [p for p in folder.iterdir() if p.is_file()] + [motion]
    contract = {**proof, 'body': body.relative_to(args.output).as_posix(), 'motion': motion.relative_to(args.output).as_posix(),
                'native_materials': (folder / 'native_materials.json').relative_to(args.output).as_posix(), 'textures': textures,
                'required_near_meshes': [n['name'] for n in doc['nodes'] if 'mesh' in n and n['name'].endswith('_LOD0')
                                         and any('_' + part + '_LOD0' in n['name'] for part in ('Body', 'Eyes', 'Cornea', 'Eyelashes', 'TeethUpper', 'TeethLower'))],
                'files': {p.relative_to(args.output).as_posix(): digest(p) for p in pinned}}
    entries = args.output / 'quality_contract_entries'
    entries.mkdir(parents=True, exist_ok=True)
    (entries / (args.id + '.json')).write_text(json.dumps(contract, indent=2))
    print('NATIVE_CHARACTER_STAGED', args.id, 'joints', len(new), 'rest_error', maximum, 'NOT_VISUAL_ACCEPTANCE')


if __name__ == '__main__':
    main()
